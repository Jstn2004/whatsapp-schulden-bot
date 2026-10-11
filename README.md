# 💸 WhatsApp Debt Bot

A WhatsApp bot that splits group expenses. Someone pays in advance and creates a request, the bot tells everyone involved what they owe and keeps track of who has paid.

Commands are in English, the bot's replies are in German.

> The previous version with `!` commands is tagged `v1`.

## Architecture

```
WhatsApp ⇄ gateway/ (Node.js, whatsapp-web.js) ⇄ HTTP ⇄ src/DebtBot.Host (.NET 9) → SQLite
```

| Part | Responsibility |
|---|---|
| `gateway/` | Connects to WhatsApp Web, forwards every message starting with `/` to the core (`POST /api/messages`), sends messages for the core (`POST /api/send`). Shows the login QR code. No business logic. |
| `src/DebtBot.Core` | Domain model, command parser, splitting and rounding, all command handlers. No WhatsApp dependency. |
| `src/DebtBot.Infrastructure` | EF Core with SQLite, HTTP client for the gateway (`IMessagingGateway`). |
| `src/DebtBot.Host` | ASP.NET Core host: receives messages, processes them one at a time, sends the replies. |
| `tests/DebtBot.Tests` | Unit and flow tests running the real engine against in-memory SQLite. |

Incoming messages are queued and handled strictly one after another, so concurrent commands can never produce inconsistent data. Each command is saved in one transaction; a failing command saves nothing. Message IDs are stored, so a message delivered twice is only booked once.

## Commands

| Command | Description |
|---|---|
| `/help [command]` | all commands, or syntax and example of one |
| `/group create <name> @…` | create a group (you become admin; the first group is the default ⭐) |
| `/group add <group> @…` | add members (admins) |
| `/group remove <group> @Person` | remove a member (admins; asks first if they have open items) |
| `/group rename <group> <newName>` | rename (admins) |
| `/group default <group>` | make it the default group (admins) |
| `/group admin <group> @Person` | grant or revoke admin (admins) |
| `/group settings <group> [remind=] [repeat=] [maxremind=]` | show or change settings |
| `/group delete <group>` | delete, open requests are cancelled and debtors notified (admins) |
| `/group list` · `/group info [group]` | overview |
| `/request create <amount> "<reason>" [group=] [includeme=yes\|no] [@…]` | request money, split equally; shows a preview first |
| `/pay [group]` | numbered list of your open shares (sent privately) |
| `/pay 1 2` · `/pay all` · `/pay #12` | mark shares as paid |
| `/yes` · `/no` · `/edit` | answer the bot's questions (previews expire after 5 minutes) |

Arguments with spaces go in quotes (`"Pizza Freitag"`, typographic quotes work too). People are always given as WhatsApp @mentions. Personal answers (`/pay`) are sent as private messages.

**Splitting:** with `includeme=yes` (default) the creator counts as one of the participants but owes nothing: 40 € in a group of 4 means 3 shares of 10 €. Leftover cents go to the first debtors (10 € / 3 = 3,34 + 3,33 + 3,33).

### Roadmap

Implemented so far are steps 1–5 of the specification. Still to come: custom amounts (`split=custom`), deleting requests and pinning, verification mode (`verify=on`, `/confirm`, `/reject`), reminders, `/open`, `/me`, `/stats`.

## Running locally

Requirements: Node.js 18+, .NET SDK 9.

```bash
# Terminal 1: core on http://localhost:5080
dotnet run --project src/DebtBot.Host

# Terminal 2: gateway, opens http://localhost:3000 with the QR code
cd gateway && npm install && npm start
```

Scan the QR code with the **bot phone** under WhatsApp → Settings → Linked Devices. The login is kept in `gateway/.wwebjs_auth`. The database is created automatically (`src/DebtBot.Host/data/debtbot.db`).

> ⚠️ whatsapp-web.js uses WhatsApp Web unofficially. This violates the WhatsApp Terms of Service and the number may get banned. **Use a secondary number (prepaid SIM).**

### Configuration

| Setting | Where | Default |
|---|---|---|
| Core URL / port | `Urls` in `appsettings.json` | `http://localhost:5080` |
| Database file | `Database:Path` (relative to the host folder or absolute) | `data/debtbot.db` |
| Gateway URL | `Gateway:Url` | `http://localhost:3000` |
| Shared secret | `Gateway:Token` (core) and `GATEWAY_TOKEN` (gateway) | empty = off |
| Commands per person and minute | `RateLimit:CommandsPerMinute` | `10` |
| Gateway port | `GATEWAY_PORT` | `3000` |
| Core URL for the gateway | `CORE_URL` | `http://localhost:5080` |
| Pause between sent messages | `SEND_INTERVAL_MS` | `800` |
| Open the QR page on start | `OPEN_BROWSER` | `true` except on Linux |

.NET settings can also be set as environment variables, e.g. `Gateway__Token`.

## Development

```bash
dotnet test                       # all tests
dotnet tool restore               # once, installs dotnet-ef
dotnet ef migrations add <Name> --project src/DebtBot.Infrastructure --startup-project src/DebtBot.Infrastructure --output-dir Persistence/Migrations
```

Migrations are applied automatically when the host starts.

## Running on a server

A small Linux VPS or a Raspberry Pi is enough. Steps for Ubuntu/Debian, everything runs as the user `bot`:

```bash
# Node.js 22, git and a user for the bot
curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash -
sudo apt install -y nodejs git
sudo useradd --create-home bot

# .NET 9 SDK for the bot user
sudo -iu bot bash -c 'curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 9.0'

# Code, build and gateway dependencies
sudo -iu bot git clone https://github.com/Jstn2004/whatsapp-schulden-bot.git
sudo -iu bot bash -c 'cd whatsapp-schulden-bot && ~/.dotnet/dotnet publish src/DebtBot.Host -c Release -o publish'
sudo -iu bot bash -c 'cd whatsapp-schulden-bot/gateway && npm install'
cd /home/bot/whatsapp-schulden-bot/gateway && sudo npx puppeteer browsers install chrome --install-deps

# Shared secret and data folder
sudo -iu bot bash -c 'mkdir -p ~/debt-bot-data && cp whatsapp-schulden-bot/deploy/debt-bot.env.example ~/debt-bot.env'
sudo -iu bot nano /home/bot/debt-bot.env   # set the same random token twice (openssl rand -hex 32)
```

**First login:** start the core as a service, then run the gateway once by hand and scan the QR code shown in the terminal:

```bash
sudo cp /home/bot/whatsapp-schulden-bot/deploy/*.service /etc/systemd/system/
sudo systemctl enable --now debt-bot-core
sudo -iu bot bash -c 'set -a && . ~/debt-bot.env && cd whatsapp-schulden-bot/gateway && OPEN_BROWSER=false npm start'
```

If the terminal QR code is hard to scan, open an SSH tunnel (`ssh -L 3000:localhost:3000 you@server`) and open http://localhost:3000. Both services only listen on localhost on purpose: whoever can scan the QR code controls the WhatsApp account. Stop with `Ctrl+C` once it says it is connected, then:

```bash
sudo systemctl enable --now debt-bot-gateway
journalctl -u debt-bot-core -u debt-bot-gateway -f   # logs
```

> Run only one gateway at a time. If it also runs on your laptop, every reply comes twice. Remove the old linked device in WhatsApp.

**Updating:**

```bash
sudo -iu bot bash -c 'cd whatsapp-schulden-bot && git pull && ~/.dotnet/dotnet publish src/DebtBot.Host -c Release -o publish && cd gateway && npm install'
sudo systemctl restart debt-bot-core debt-bot-gateway
```
