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
| `!people Max Anna Tom` | add people to the group |
| `!people` | show all people |
| `!pay Max 45,50 Pizza` | Max paid, split across everyone |
| `!pay Max 30 "Cinema & Popcorn" --for Anna Tom` | split only across Anna and Tom |
| `!list` | show all expenses |
| `!delete 3` | delete expense #3 |
| `!settle` | who owes whom how much |
| `!remove Max` | stop including Max in new expenses by default |
| `!reset yes` | reset everything |
| `!help` | command overview |
| `!pay --help` | usage of a single command (`-h` works too) |

Commands work like a CLI: `!command <positional args> [--flag values…]`. Quote arguments that contain spaces (`"…"`, `„…“` and `“…”` all work). Flags can go anywhere; everything after a flag belongs to it until the next flag. `—for` (iOS turns `--` into a dash) is accepted as well.

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
