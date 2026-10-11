# 💸 WhatsApp Debt Bot

A WhatsApp bot for groups that tracks expenses and works out who owes whom how much, using as few transfers as possible.

The bot's chat replies and the local web UI are in German; code, commands and docs are in English.

## Getting started

```bash
npm install
npm start
```

Your browser opens http://localhost:3000 with a QR code. Scan it with the **bot phone** under WhatsApp → Settings → Linked Devices. The page shows when the bot is connected. The login is persisted, so no scan is needed on the next start.

> ⚠️ The bot uses WhatsApp Web unofficially (whatsapp-web.js). This violates the WhatsApp Terms of Service and the number may get banned. **Use a secondary number (prepaid SIM), not your main number.**

## Commands

| Command | What it does |
|---|---|
| `!me Max` | tell the bot who you are (links your WhatsApp number to a name) |
| `!people Max @Anna Tom` | add people to the group |
| `!people` | show all people |
| `!pay 45,50 Pizza` | you paid, split across everyone |
| `!pay 30 "Cinema & Popcorn" --for @Anna @Tom` | split only across Anna and Tom |
| `!pay 12 Drinks --by Max` | someone else paid |
| `!request 20 Tickets --from Anna Tom` | Anna and Tom each owe you 20 € |
| `!request 50 Rent --from Tom --by Max` | request on behalf of someone else |
| `!repay --to @Max` | you paid back what you owe Max (amount taken from `!settle`) |
| `!repay 10 --to Max` | you paid back a specific amount |
| `!list` | show all entries |
| `!delete 3` | delete entry #3 |
| `!settle` | who owes whom how much |
| `!remove Max` | stop including Max in new expenses by default |
| `!remind on --after 3d` | remind people of debts open for 3 days (`12h` works too) |
| `!remind off` / `!remind` | turn reminders off / show the setting |
| `!reset yes` | reset everything |
| `!help` | command overview |
| `!pay --help` | usage of a single command (`-h` works too) |

Commands work like a CLI: `!command <positional args> [--flag values…]`. Quote arguments that contain spaces (`"…"`, `„…“` and `“…”` all work). Flags can go anywhere; everything after a flag belongs to it until the next flag. `—for` (iOS turns `--` into a dash) is accepted as well.

### Who paid?

`!pay` and `!request` use the sender of the message by default. The bot recognizes you automatically if the first word of your WhatsApp name matches someone in `!people`; otherwise send `!me <your name>` once. `--by` overrides the sender.

### @mentions

Wherever a command takes names you can @mention people instead. A mentioned person who is not known yet is added under the first word of their WhatsApp name and linked to their number, so they don't need `!me`. Typed names keep working, e.g. for people who are not in the WhatsApp group.

### Requests

A request (`!request`) means each person listed in `--from` owes the full amount to the requester. Requests count in `!settle` like expenses but not in the total spending shown by `!list`.

### Repayments and reminders

`!repay` records that someone paid back money, which brings their balance towards zero. Without an amount it uses what `!settle` says the sender owes the `--to` person.

With `!remind on`, the bot posts a message into the group that @mentions everyone who has owed money for longer than the configured time (default 3 days), and again after each further interval until they `!repay`. People who never linked their number (via `!me` or a mention) are listed by name without a mention. Reminders are off by default and only go out while the bot is running, so it should run on a server. The bot checks every 15 minutes (`REMINDER_CHECK_MINUTES`).

Each group has its own ledger. Data is stored in `data/groups.json`.

## Running on a server

Reminders only work while the bot runs around the clock. A small Linux VPS (1 GB RAM is enough) or a Raspberry Pi works. Steps for Ubuntu/Debian:

```bash
# Node.js 22 and git
curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash -
sudo apt install -y nodejs git

# A dedicated user for the bot
sudo useradd --create-home bot
sudo -iu bot git clone https://github.com/Jstn2004/whatsapp-schulden-bot.git
sudo -iu bot bash -c 'cd whatsapp-schulden-bot && npm install'

# System libraries that Chrome needs
cd /home/bot/whatsapp-schulden-bot && sudo npx puppeteer browsers install chrome --install-deps
```

**First login:** start the bot once by hand and scan the QR code printed in the terminal:

```bash
sudo -iu bot bash -c 'cd whatsapp-schulden-bot && npm start'
```

If the terminal QR code is hard to scan, open an SSH tunnel from your computer (`ssh -L 3000:localhost:3000 you@server`) and open http://localhost:3000. The web UI only listens on localhost, on purpose: anyone who can scan the QR code gets access to the WhatsApp account. Stop the bot with `Ctrl+C` once it says it is running.

**Run as a service** (starts on boot, restarts on crashes):

```bash
sudo cp /home/bot/whatsapp-schulden-bot/deploy/whatsapp-debt-bot.service /etc/systemd/system/
sudo systemctl enable --now whatsapp-debt-bot
journalctl -u whatsapp-debt-bot -f   # logs
```

**Moving existing data:** copy `data/groups.json` to `/home/bot/whatsapp-schulden-bot/data/` before starting the service.

> Only run one instance at a time. If the bot also runs on your laptop, both answer every command and send every reminder twice. Stop it on the laptop and remove the old linked device in WhatsApp.

**Updating:** `sudo -iu bot bash -c 'cd whatsapp-schulden-bot && git pull && npm install' && sudo systemctl restart whatsapp-debt-bot`

### Environment variables

| Variable | Default | Meaning |
|---|---|---|
| `PORT` | `3000` | port of the local web UI |
| `OPEN_BROWSER` | `true` except on Linux | open the web UI in a browser on start |
| `REMINDER_CHECK_MINUTES` | `15` | how often to check for overdue debts |

## Structure

- `index.js` – WhatsApp connection
- `src/commands.js` – parse and answer commands
- `src/args.js` – CLI-style tokenizer and flag parser
- `src/ledger.js` – compute balances and minimal transfers
- `src/money.js` – parse/format amounts (stored internally as cents)
- `src/reminders.js` – track how long debts are open and build reminder messages
- `src/store.js` – JSON persistence
- `src/web.js` – local web UI with QR code and connection status
- `deploy/whatsapp-debt-bot.service` – systemd unit for servers

Tests: `npm test`
