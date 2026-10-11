// Übersetzt Chat-Befehle in Aktionen und liefert den Antworttext zurück.
// Wichtig: Antworten dürfen nie mit "!" beginnen, sonst reagiert der Bot auf sich selbst.
const { parseAmount, formatEuro } = require('./money');
const { balances, settle } = require('./ledger');

const HELP = `💸 *Schulden-Bot*

*!leute* Max Anna Tom – Leute zur Gruppe hinzufügen
*!leute* – zeigt alle Leute
*!raus* Max – entfernt jemanden aus der Standard-Aufteilung
*!zahlt* Max 45,50 Pizza – Max hat bezahlt, wird auf alle aufgeteilt
*!zahlt* Max 30 Kino für Anna Tom – nur auf Anna und Tom aufteilen
*!liste* – alle Ausgaben
*!löschen* 3 – Ausgabe Nr. 3 löschen
*!abrechnung* – wer schuldet wem wie viel
*!reset* – alles zurücksetzen (nach dem Begleichen)`;

const NAME_PATTERN = /^[\p{L}][\p{L}\d'-]*$/u;

function capitalize(name) {
  return name.charAt(0).toUpperCase() + name.slice(1);
}

// Findet einen bekannten Namen unabhängig von Groß-/Kleinschreibung oder legt ihn neu an.
function resolveName(group, raw) {
  const existing = group.members.find((m) => m.toLowerCase() === raw.toLowerCase());
  if (existing) return existing;
  const name = capitalize(raw);
  group.members.push(name);
  return name;
}

function splitNames(text) {
  return text
    .split(/[\s,]+/)
    .filter((t) => t && !['und', '&'].includes(t.toLowerCase()));
}

function invalidNames(names) {
  return names.filter((n) => !NAME_PATTERN.test(n));
}

function cmdLeute(group, args) {
  const names = splitNames(args.join(' '));
  if (names.length === 0) {
    return group.members.length
      ? `👥 Leute: ${group.members.join(', ')}`
      : 'Noch niemand eingetragen. Beispiel: !leute Max Anna Tom';
  }
  const bad = invalidNames(names);
  if (bad.length) return `Ungültige Namen: ${bad.join(', ')}`;
  names.forEach((n) => resolveName(group, n));
  return `👥 Leute: ${group.members.join(', ')}`;
}

function cmdRaus(group, args) {
  if (args.length !== 1) return 'So geht\'s: !raus Max';
  const idx = group.members.findIndex((m) => m.toLowerCase() === args[0].toLowerCase());
  if (idx === -1) return `${args[0]} kenne ich nicht.`;
  const [removed] = group.members.splice(idx, 1);
  return `${removed} wird bei neuen Ausgaben nicht mehr automatisch berücksichtigt.`;
}

function cmdZahlt(group, args) {
  const usage = 'So geht\'s: !zahlt Max 45,50 Pizza (optional: für Anna Tom)';
  if (args.length < 2) return usage;

  const [payerRaw, amountRaw, ...rest] = args;
  if (!NAME_PATTERN.test(payerRaw)) return usage;
  const amount = parseAmount(amountRaw);
  if (amount === null) return `"${amountRaw}" ist kein gültiger Betrag. ${usage}`;

  const forIdx = rest.findIndex((t) => ['für', 'fuer'].includes(t.toLowerCase()));
  const descriptionParts = forIdx === -1 ? rest : rest.slice(0, forIdx);
  const forNames = forIdx === -1 ? [] : splitNames(rest.slice(forIdx + 1).join(' '));

  const bad = invalidNames(forNames);
  if (bad.length) return `Ungültige Namen: ${bad.join(', ')}`;
  if (forIdx !== -1 && forNames.length === 0) return usage;

  const payer = resolveName(group, payerRaw);
  const participants = forNames.length
    ? [...new Set(forNames.map((n) => resolveName(group, n)))]
    : [...group.members];

  if (participants.length === 1 && participants[0] === payer) {
    return `Aktuell kenne ich nur ${payer}. Leg erst die anderen an: !leute Anna Tom`;
  }

  const expense = {
    id: group.nextId++,
    payer,
    amount,
    description: descriptionParts.join(' ') || 'Ausgabe',
    participants,
    date: new Date().toISOString(),
  };
  group.expenses.push(expense);

  const share = formatEuro(Math.round(amount / participants.length));
  return `✅ #${expense.id}: ${payer} hat ${formatEuro(amount)} für „${expense.description}“ bezahlt.\n`
    + `Aufgeteilt auf ${participants.join(', ')} (je ca. ${share}).`;
}

function cmdListe(group) {
  if (group.expenses.length === 0) return 'Noch keine Ausgaben erfasst.';
  const lines = group.expenses.map(
    (e) => `#${e.id} ${e.payer}: ${formatEuro(e.amount)} – ${e.description} (${e.participants.join(', ')})`,
  );
  const total = group.expenses.reduce((sum, e) => sum + e.amount, 0);
  return `🧾 *Ausgaben*\n${lines.join('\n')}\n\nGesamt: ${formatEuro(total)}`;
}

function cmdLoeschen(group, args) {
  const id = parseInt((args[0] || '').replace('#', ''), 10);
  const idx = group.expenses.findIndex((e) => e.id === id);
  if (idx === -1) return 'So geht\'s: !löschen 3 (Nummer aus !liste)';
  const [removed] = group.expenses.splice(idx, 1);
  return `🗑️ #${removed.id} gelöscht (${removed.payer}, ${formatEuro(removed.amount)}, ${removed.description}).`;
}

function cmdAbrechnung(group) {
  if (group.expenses.length === 0) return 'Noch keine Ausgaben erfasst.';
  const transfers = settle(balances(group.expenses));
  if (transfers.length === 0) return '🎉 Alles ausgeglichen, niemand schuldet jemandem etwas.';
  const lines = transfers.map((t) => `• ${t.from} → ${t.to}: *${formatEuro(t.amount)}*`);
  return `📊 *Abrechnung*\n${lines.join('\n')}\n\nDanach mit !reset neu starten.`;
}

function cmdReset(group, args, store, groupId) {
  if ((args[0] || '').toLowerCase() !== 'ja') {
    return 'Wirklich alle Ausgaben und Leute löschen? Dann schreib: !reset ja';
  }
  store.reset(groupId);
  return '🧹 Alles zurückgesetzt.';
}

const COMMANDS = {
  '!hilfe': () => HELP,
  '!help': () => HELP,
  '!leute': cmdLeute,
  '!raus': cmdRaus,
  '!zahlt': cmdZahlt,
  '!liste': cmdListe,
  '!löschen': cmdLoeschen,
  '!loeschen': cmdLoeschen,
  '!abrechnung': cmdAbrechnung,
  '!reset': cmdReset,
};

// Gibt die Antwort zurück oder null, wenn die Nachricht kein Befehl ist.
function handle(text, groupId, store) {
  const [cmd, ...args] = text.trim().split(/\s+/);
  const fn = COMMANDS[cmd.toLowerCase()];
  if (!fn) return null;

  const group = store.get(groupId);
  const reply = fn(group, args, store, groupId);
  store.save();
  return reply;
}

module.exports = { handle };
