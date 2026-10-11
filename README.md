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
| `!people Max Anna Tom` | add people to the group |
| `!people` | show all people |
| `!pay 45,50 Pizza` | you paid, split across everyone |
| `!pay 30 "Cinema & Popcorn" --for Anna Tom` | split only across Anna and Tom |
| `!pay 12 Drinks --by Max` | someone else paid |
| `!request 20 Tickets --from Anna Tom` | Anna and Tom each owe you 20 € |
| `!request 50 Rent --from Tom --by Max` | request on behalf of someone else |
| `!list` | show all expenses and requests |
| `!delete 3` | delete entry #3 |
| `!settle` | who owes whom how much |
| `!remove Max` | stop including Max in new expenses by default |
| `!reset yes` | reset everything |
| `!help` | command overview |
| `!pay --help` | usage of a single command (`-h` works too) |

Commands work like a CLI: `!command <positional args> [--flag values…]`. Quote arguments that contain spaces (`"…"`, `„…“` and `“…”` all work). Flags can go anywhere; everything after a flag belongs to it until the next flag. `—for` (iOS turns `--` into a dash) is accepted as well.

### Who paid?

`!pay` and `!request` use the sender of the message by default. The bot recognizes you automatically if the first word of your WhatsApp name matches someone in `!people`; otherwise send `!me <your name>` once. `--by` overrides the sender.

### Requests

A request (`!request`) means each person listed in `--from` owes the full amount to the requester. Requests count in `!settle` like expenses but not in the total spending shown by `!list`.

Each group has its own ledger. Data is stored in `data/groups.json`.

## Structure

- `index.js` – WhatsApp connection
- `src/commands.js` – parse and answer commands
- `src/args.js` – CLI-style tokenizer and flag parser
- `src/ledger.js` – compute balances and minimal transfers
- `src/money.js` – parse/format amounts (stored internally as cents)
- `src/store.js` – JSON persistence
- `src/web.js` – local web UI with QR code and connection status

Tests: `npm test`
