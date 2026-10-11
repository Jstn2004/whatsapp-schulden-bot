// Translates chat commands into actions and returns the reply text.
// Important: replies must never start with "!", otherwise the bot reacts to itself.
const { parseAmount, formatEuro } = require('./money');
const { balances, settle } = require('./ledger');
const { tokenize, parseArgs } = require('./args');

const HELP = `💸 *Schulden-Bot*

*!me* Max – sagt dem Bot, wer du bist
*!people* Max Anna Tom – Leute zur Gruppe hinzufügen
*!people* – zeigt alle Leute
*!remove* Max – entfernt jemanden aus der Standard-Aufteilung
*!pay* 45,50 Pizza – du hast bezahlt, wird auf alle aufgeteilt
*!pay* 30 "Kino & Popcorn" --for Anna Tom – nur auf Anna und Tom aufteilen
*!request* 20 Tickets --from Anna Tom – Anna und Tom schulden dir je 20 €
*!list* – alle Ausgaben und Forderungen
*!delete* 3 – Eintrag Nr. 3 löschen
*!settle* – wer schuldet wem wie viel
*!reset* – alles zurücksetzen (nach dem Begleichen)

Details zu einem Befehl: z. B. !pay --help`;

const PAY_USAGE = `*!pay* <betrag> [beschreibung] [--for namen…] [--by wer]

*Beispiele*
!pay 45,50 Pizza
!pay 30 "Kino & Popcorn" --for Anna Tom
!pay 12 Getränke --by Max

*Optionen*
--for, -f  nur auf diese Leute aufteilen (Standard: alle)
--by, -b   jemand anderes hat bezahlt (Standard: du)`;

const REQUEST_USAGE = `*!request* <betrag> [beschreibung] --from namen… [--by wer]

Jede genannte Person schuldet den vollen Betrag.

*Beispiele*
!request 20 Tickets --from Anna
!request 15 "Festival Shuttle" --from Anna Tom
!request 50 Miete --from Tom --by Max

*Optionen*
--from     wer zahlen muss (Pflicht)
--by, -b   wer das Geld bekommt (Standard: du)`;

const ME_USAGE = `*!me* <dein name>

Verknüpft deine WhatsApp-Nummer mit einem Namen, damit !pay und !request wissen, wer du bist.
*!me* ohne Namen zeigt, als wer du eingetragen bist.`;

const NAME_PATTERN = /^[\p{L}][\p{L}\d'-]*$/u;

function capitalize(name) {
  return name.charAt(0).toUpperCase() + name.slice(1);
}

function findMember(group, raw) {
  return group.members.find((m) => m.toLowerCase() === raw.toLowerCase());
}

// Finds a known name regardless of case, or adds it as a new member.
function resolveName(group, raw) {
  const existing = findMember(group, raw);
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

// Parses args and reports the first flag that the command does not support.
function parseOptions(args, allowed, aliases) {
  const parsed = parseArgs(args, aliases);
  const unknown = Object.keys(parsed.flags).find((f) => !allowed.includes(f));
  return { ...parsed, unknown };
}

// Reads the names given to a flag; undefined means the flag was not used.
function readNames(values, flag) {
  if (values === undefined) return { names: [] };
  const names = splitNames(values.join(' '));
  if (names.length === 0) return { error: `${flag} braucht mindestens einen Namen.` };
  const bad = invalidNames(names);
  if (bad.length) return { error: `Ungültige Namen: ${bad.join(', ')}` };
  return { names };
}

// Looks up who sent the message. A sender whose WhatsApp name matches a member is linked automatically.
function senderName(group, sender) {
  if (!sender?.id) return null;
  if (group.links[sender.id]) return group.links[sender.id];
  const firstName = (sender.name || '').trim().split(/\s+/)[0];
  const match = firstName && findMember(group, firstName);
  if (match) group.links[sender.id] = match;
  return match || null;
}

// The --by flag wins over the sender. Returns the raw name, the name is resolved later.
function whoPays(group, byValues, sender) {
  if (byValues !== undefined) {
    const { names, error } = readNames(byValues, '--by');
    if (error) return { error };
    if (names.length !== 1) return { error: '--by braucht genau einen Namen.' };
    return { name: names[0] };
  }
  const name = senderName(group, sender);
  if (name) return { name };
  return { error: 'Ich weiß noch nicht, wer du bist. Schreib einmal !me <dein Name> oder gib --by Max an.' };
}

function cmdMe(group, args, { sender }) {
  if (!sender?.id) return 'Ich kann nicht erkennen, wer die Nachricht geschickt hat.';
  if (args.length === 0) {
    const name = senderName(group, sender);
    return name ? `Du bist als ${name} eingetragen.` : `Du bist noch nicht eingetragen.\n\n${ME_USAGE}`;
  }
  if (args.length !== 1 || !NAME_PATTERN.test(args[0])) return ME_USAGE;
  const name = resolveName(group, args[0]);
  group.links[sender.id] = name;
  return `👋 Alles klar, du bist ${name}.`;
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

function cmdPay(group, args, { sender }) {
  const usage = `So geht's:\n${PAY_USAGE}`;
  const { positional, flags, unknown } = parseOptions(args, ['for', 'by'], { f: 'for', b: 'by' });
  if (unknown) return `Unbekannte Option: --${unknown}\n\n${usage}`;
  if (positional.length === 0) return usage;

  const [amountRaw, ...descriptionParts] = positional;
  const amount = parseAmount(amountRaw);
  if (amount === null) {
    // Hint for the old syntax "!pay Max 30 Pizza".
    if (NAME_PATTERN.test(amountRaw) && parseAmount(descriptionParts[0]) !== null) {
      const rest = descriptionParts.join(' ');
      return `Der Betrag kommt zuerst. Meintest du: !pay ${rest} --by ${amountRaw}`;
    }
    return `"${amountRaw}" ist kein gültiger Betrag.\n\n${usage}`;
  }

  const forResult = readNames(flags.for, '--for');
  if (forResult.error) return `${forResult.error}\n\n${usage}`;
  const payerResult = whoPays(group, flags.by, sender);
  if (payerResult.error) return payerResult.error;

  const payer = resolveName(group, payerResult.name);
  const participants = forResult.names.length
    ? [...new Set(forResult.names.map((n) => resolveName(group, n)))]
    : [...group.members];

  if (participants.length === 1 && participants[0] === payer) {
    return `Aktuell kenne ich nur ${payer}. Leg erst die anderen an: !people Anna Tom`;
  }

  const expense = {
    id: group.nextId++,
    type: 'expense',
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

// A request is stored like an expense that the payer does not share in:
// each debtor's share is exactly the requested amount.
function cmdRequest(group, args, { sender }) {
  const usage = `So geht's:\n${REQUEST_USAGE}`;
  const { positional, flags, unknown } = parseOptions(args, ['from', 'by'], { b: 'by' });
  if (unknown) return `Unbekannte Option: --${unknown}\n\n${usage}`;
  if (positional.length === 0) return usage;

  const [amountRaw, ...descriptionParts] = positional;
  const perPerson = parseAmount(amountRaw);
  if (perPerson === null) return `"${amountRaw}" ist kein gültiger Betrag.\n\n${usage}`;

  if (flags.from === undefined) return `Von wem willst du das Geld? Gib --from an.\n\n${usage}`;
  const fromResult = readNames(flags.from, '--from');
  if (fromResult.error) return `${fromResult.error}\n\n${usage}`;
  const payeeResult = whoPays(group, flags.by, sender);
  if (payeeResult.error) return payeeResult.error;

  const payee = resolveName(group, payeeResult.name);
  const debtors = [...new Set(fromResult.names.map((n) => resolveName(group, n)))];
  if (debtors.includes(payee)) return `${payee} kann nicht von sich selbst Geld fordern.`;

  const request = {
    id: group.nextId++,
    type: 'request',
    payer: payee,
    amount: perPerson * debtors.length,
    perPerson,
    description: descriptionParts.join(' ') || 'Forderung',
    participants: debtors,
    date: new Date().toISOString(),
  };
  group.expenses.push(request);

  return `📨 #${request.id}: ${payee} fordert ${formatEuro(perPerson)} für „${request.description}“.\n`
    + `Zahlen muss: ${debtors.join(', ')}.`;
}

function describeEntry(e) {
  if (e.type === 'request') {
    return `#${e.id} 📨 ${e.payer} fordert ${formatEuro(e.perPerson)} von ${e.participants.join(', ')} – ${e.description}`;
  }
  return `#${e.id} ${e.payer}: ${formatEuro(e.amount)} – ${e.description} (${e.participants.join(', ')})`;
}

function cmdList(group) {
  if (group.expenses.length === 0) return 'Noch keine Ausgaben erfasst.';
  const lines = group.expenses.map(describeEntry);
  const total = group.expenses
    .filter((e) => e.type !== 'request')
    .reduce((sum, e) => sum + e.amount, 0);
  return `🧾 *Ausgaben*\n${lines.join('\n')}\n\nGesamtausgaben: ${formatEuro(total)}`;
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

function cmdReset(group, args, { store, groupId }) {
  if ((args[0] || '').toLowerCase() !== 'yes') {
    return 'Wirklich alle Ausgaben und Leute löschen? Dann schreib: !reset yes';
  }
  store.reset(groupId);
  return '🧹 Alles zurückgesetzt.';
}

const COMMANDS = {
  '!help': () => HELP,
  '!me': cmdMe,
  '!people': cmdPeople,
  '!remove': cmdRemove,
  '!pay': cmdPay,
  '!request': cmdRequest,
  '!list': cmdList,
  '!delete': cmdDelete,
  '!settle': cmdSettle,
  '!reset': cmdReset,
};

const USAGE = {
  '!pay': PAY_USAGE,
  '!request': REQUEST_USAGE,
  '!me': ME_USAGE,
};

const isHelpFlag = (t) => ['--help', '—help', '–help', '-h'].includes(t.toLowerCase());

// Returns the reply, or null if the message is not a command.
// sender is { id, name } of whoever wrote the message (WhatsApp ID and display name).
function handle(text, groupId, store, sender = null) {
  const [cmd = '', ...args] = tokenize(text);
  const name = cmd.toLowerCase();
  const fn = COMMANDS[name];
  if (!fn) return null;
  if (args.some(isHelpFlag)) return USAGE[name] || HELP;

  const group = store.get(groupId);
  const reply = fn(group, args, { store, groupId, sender });
  store.save();
  return reply;
}

module.exports = { handle };
