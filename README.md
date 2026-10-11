# 💸 WhatsApp Schulden-Bot

Ein WhatsApp-Bot für Gruppen, der Ausgaben erfasst und am Ende ausrechnet, wer wem wie viel schuldet, mit möglichst wenigen Überweisungen.

## Starten

```bash
npm install
npm start
```

Im Browser öffnet sich automatisch http://localhost:3000 mit einem QR-Code. Scanne ihn mit dem **Bot-Handy** unter WhatsApp → Einstellungen → Verknüpfte Geräte. Die Seite zeigt an, sobald der Bot verbunden ist. Der Login wird gespeichert, beim nächsten Start ist kein Scan mehr nötig.

> ⚠️ Der Bot nutzt WhatsApp Web inoffiziell (whatsapp-web.js). Das verstößt gegen die WhatsApp-Nutzungsbedingungen, die Nummer kann gesperrt werden. **Nimm eine Zweitnummer (Prepaid-SIM), nicht deine Hauptnummer.**

## Befehle

| Befehl | Was passiert |
|---|---|
| `!leute Max Anna Tom` | Leute zur Gruppe hinzufügen |
| `!zahlt Max 45,50 Pizza` | Max hat bezahlt, wird auf alle aufgeteilt |
| `!zahlt Max 30 Kino für Anna Tom` | nur auf Anna und Tom aufteilen |
| `!liste` | alle Ausgaben anzeigen |
| `!löschen 3` | Ausgabe Nr. 3 löschen |
| `!abrechnung` | wer schuldet wem wie viel |
| `!raus Max` | Max bei neuen Ausgaben nicht mehr automatisch einrechnen |
| `!reset ja` | alles zurücksetzen |
| `!hilfe` | Befehlsübersicht |

Jede Gruppe hat ihre eigene Abrechnung. Die Daten liegen in `data/groups.json`.

## Aufbau

- `index.js` – WhatsApp-Verbindung
- `src/commands.js` – Befehle parsen und beantworten
- `src/ledger.js` – Salden und minimale Überweisungen berechnen
- `src/money.js` – Beträge parsen/formatieren (intern in Cent)
- `src/store.js` – Speichern als JSON
- `src/web.js` – lokale Weboberfläche mit QR-Code und Verbindungsstatus

Tests: `npm test`
