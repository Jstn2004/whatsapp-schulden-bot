// Translates chat commands into actions and returns the reply text.
// Important: replies must never start with "!", otherwise the bot reacts to itself.
const { parseAmount, formatEuro } = require('./money');
const { balances, settle } = require('./ledger');
const { tokenize, parseArgs } = require('./args');

const HELP = `💸 *Schulden-Bot*

*!people* Max Anna Tom – Leute zur Gruppe hinzufügen
*!people* – zeigt alle Leute
*!remove* Max – entfernt jemanden aus der Standard-Aufteilung
*!pay* Max 45,50 Pizza – Max hat bezahlt, wird auf alle aufgeteilt
*!pay* Max 30 "Kino & Popcorn" --for Anna Tom – nur auf Anna und Tom aufteilen
*!list* – alle Ausgaben
*!delete* 3 – Ausgabe Nr. 3 löschen
*!settle* – wer schuldet wem wie viel
*!reset* – alles zurücksetzen (nach dem Begleichen)

Details zu einem Befehl: z. B. !pay --help`;

const PAY_USAGE = `*!pay* <wer> <betrag> [beschreibung] [--for namen…]

*Beispiele*
!pay Max 45,50 Pizza
!pay Max 30 "Kino & Popcorn" --for Anna Tom

*Optionen*
--for, -f  nur auf diese Leute aufteilen (Standard: alle)`;

const NAME_PATTERN = /^[\p{L}][\p{L}\d'-]*$/u;

function capitalize(name) {
  return name.charAt(0).toUpperCase() + name.slice(1);
}

// Finds a known name regardless of case, or adds it as a new member.
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
    .filter((t) => t && !['and', '&'].includes(t.toLowerCase()));
}

function invalidNames(names) {
  return names.filter((n) => !NAME_PATTERN.test(n));
}

function cmdPeople(group, args) {
  const names = splitNames(args.join(' '));
  if (names.length === 0) {
    return group.members.length
      ? `👥 Leute: ${group.members.join(', ')}`
      : 'Noch niemand eingetragen. Beispiel: !people Max Anna Tom';
  }
  const bad = invalidNames(names);
  if (bad.length) return `Ungültige Namen: ${bad.join(', ')}`;
  names.forEach((n) => resolveName(group, n));
  return `👥 Leute: ${group.members.join(', ')}`;
}

function cmdRemove(group, args) {
  if (args.length !== 1) return 'So geht\'s: !remove Max';
  const idx = group.members.findIndex((m) => m.toLowerCase() === args[0].toLowerCase());
  if (idx === -1) return `${args[0]} kenne ich nicht.`;
  const [removed] = group.members.splice(idx, 1);
  return `${removed} wird bei neuen Ausgaben nicht mehr automatisch berücksichtigt.`;
}

function cmdPay(group, args) {
  const usage = `So geht's:\n${PAY_USAGE}`;
  const { positional, flags } = parseArgs(args, { f: 'for' });

  const unknown = Object.keys(flags).filter((f) => f !== 'for');
  if (unknown.length) return `Unbekannte Option: --${unknown[0]}\n\n${usage}`;
  if (positional.length < 2) return usage;

  const [payerRaw, amountRaw, ...descriptionParts] = positional;
  if (!NAME_PATTERN.test(payerRaw)) return `"${payerRaw}" ist kein gültiger Name.\n\n${usage}`;
  const amount = parseAmount(amountRaw);
  if (amount === null) return `"${amountRaw}" ist kein gültiger Betrag.\n\n${usage}`;

  const forNames = splitNames((flags.for || []).join(' '));
  if (flags.for && forNames.length === 0) return `--for braucht mindestens einen Namen.\n\n${usage}`;
  const bad = invalidNames(forNames);
  if (bad.length) return `Ungültige Namen: ${bad.join(', ')}`;

  const payer = resolveName(group, payerRaw);
  const participants = forNames.length
    ? [...new Set(forNames.map((n) => resolveName(group, n)))]
    : [...group.members];

  if (participants.length === 1 && participants[0] === payer) {
    return `Aktuell kenne ich nur ${payer}. Leg erst die anderen an: !people Anna Tom`;
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

function cmdList(group) {
  if (group.expenses.length === 0) return 'Noch keine Ausgaben erfasst.';
  const lines = group.expenses.map(
    (e) => `#${e.id} ${e.payer}: ${formatEuro(e.amount)} – ${e.description} (${e.participants.join(', ')})`,
  );
  const total = group.expenses.reduce((sum, e) => sum + e.amount, 0);
  return `🧾 *Ausgaben*\n${lines.join('\n')}\n\nGesamt: ${formatEuro(total)}`;
}

function cmdDelete(group, args) {
  const id = parseInt((args[0] || '').replace('#', ''), 10);
  const idx = group.expenses.findIndex((e) => e.id === id);
  if (idx === -1) return 'So geht\'s: !delete 3 (Nummer aus !list)';
  const [removed] = group.expenses.splice(idx, 1);
  return `🗑️ #${removed.id} gelöscht (${removed.payer}, ${formatEuro(removed.amount)}, ${removed.description}).`;
}

function cmdSettle(group) {
  if (group.expenses.length === 0) return 'Noch keine Ausgaben erfasst.';
  const transfers = settle(balances(group.expenses));
  if (transfers.length === 0) return '🎉 Alles ausgeglichen, niemand schuldet jemandem etwas.';
  const lines = transfers.map((t) => `• ${t.from} → ${t.to}: *${formatEuro(t.amount)}*`);
  return `📊 *Abrechnung*\n${lines.join('\n')}\n\nDanach mit !reset neu starten.`;
}

function cmdReset(group, args, store, groupId) {
  if ((args[0] || '').toLowerCase() !== 'yes') {
    return 'Wirklich alle Ausgaben und Leute löschen? Dann schreib: !reset yes';
  }
  store.reset(groupId);
  return '🧹 Alles zurückgesetzt.';
}

const COMMANDS = {
  '!help': () => HELP,
  '!people': cmdPeople,
  '!remove': cmdRemove,
  '!pay': cmdPay,
  '!list': cmdList,
  '!delete': cmdDelete,
  '!settle': cmdSettle,
  '!reset': cmdReset,
};

const USAGE = {
  '!pay': PAY_USAGE,
};

const isHelpFlag = (t) => ['--help', '—help', '–help', '-h'].includes(t.toLowerCase());

// Returns the reply, or null if the message is not a command.
function handle(text, groupId, store) {
  const [cmd = '', ...args] = tokenize(text);
  const name = cmd.toLowerCase();
  const fn = COMMANDS[name];
  if (!fn) return null;
  if (args.some(isHelpFlag)) return USAGE[name] || HELP;

  const group = store.get(groupId);
  const reply = fn(group, args, store, groupId);
  store.save();
  return reply;
}

module.exports = { handle };
