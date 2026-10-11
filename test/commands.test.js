const test = require('node:test');
const assert = require('node:assert');
const { handle } = require('../src/commands');
const { createStore } = require('../src/store');
const { splitEvenly, settle } = require('../src/ledger');
const { parseAmount } = require('../src/money');
const { tokenize, parseArgs } = require('../src/args');

const MAX = { id: 'max@c.us', name: 'Max Mustermann' };
const ANNA = { id: 'anna@c.us', name: 'Anna' };
const STRANGER = { id: 'x@c.us', name: '🐱' };

// Messages are sent by Max unless another sender is given.
function setup() {
  const store = createStore(null);
  return (text, sender = MAX) => handle(text, 'group', store, sender);
}

test('parseAmount understands German notation', () => {
  assert.strictEqual(parseAmount('45'), 4500);
  assert.strictEqual(parseAmount('45,50'), 4550);
  assert.strictEqual(parseAmount('45.5€'), 4550);
  assert.strictEqual(parseAmount('0'), null);
  assert.strictEqual(parseAmount('abc'), null);
});

test('splitEvenly distributes leftover cents', () => {
  assert.deepStrictEqual(splitEvenly(1000, 3), [334, 333, 333]);
});

test('tokenize keeps quoted text together, including typographic quotes', () => {
  assert.deepStrictEqual(tokenize('!pay Max 30 "Kino & Popcorn" -f Anna'), ['!pay', 'Max', '30', 'Kino & Popcorn', '-f', 'Anna']);
  assert.deepStrictEqual(tokenize('!pay Max 30 „Pizza Margherita“'), ['!pay', 'Max', '30', 'Pizza Margherita']);
});

test('parseArgs collects flag values and resolves aliases', () => {
  const { positional, flags } = parseArgs(['Max', '30', 'Pizza', '-f', 'Anna', 'Tom', '—x'], { f: 'for' });
  assert.deepStrictEqual(positional, ['Max', '30', 'Pizza']);
  assert.deepStrictEqual(flags, { for: ['Anna', 'Tom'], x: [] });
});

test('settle needs at most n-1 transfers', () => {
  const b = new Map([['A', 3000], ['B', -1000], ['C', -1000], ['D', -1000]]);
  const t = settle(b);
  assert.strictEqual(t.length, 3);
  assert.ok(t.every((x) => x.to === 'A' && x.amount === 1000));
});

test('full flow: split pizza and settle up', () => {
  const send = setup();
  send('!people Max Anna Tom');
  send('!pay 30 Pizza');
  send('!pay 12 Drinks --for Max Tom', ANNA);
  const reply = send('!settle');
  // Max: +30 -10 -6 = 14, Anna: -10 +12 = 2, Tom: -10 -6 = -16
  assert.match(reply, /Tom → Max: \*14,00\s€\*/);
  assert.match(reply, /Tom → Anna: \*2,00\s€\*/);
});

test('deleting expenses and error messages', () => {
  const send = setup();
  assert.match(send('!pay 10 Eis --by Max'), /Leg erst die anderen an/);
  send('!people Max Anna');
  send('!pay 10 Eis');
  assert.match(send('!delete 1'), /gelöscht/);
  assert.match(send('!settle'), /Noch keine Ausgaben/);
  assert.match(send('!pay ten'), /kein gültiger Betrag/);
  assert.strictEqual(send('hello'), null);
});

test('!pay records the exact amount and description', () => {
  const send = setup();
  send('!people Max Anna Tom');
  assert.match(send('!pay 45,50 "Kino & Popcorn" --for Anna Tom'), /Max hat 45,50\s€ für „Kino & Popcorn“[\s\S]*Anna, Tom/);
  assert.match(send('!pay 9.99 Eis —for Anna'), /9,99\s€/); // iOS em dash
  assert.match(send('!list'), /#1 Max: 45,50\s€ – Kino & Popcorn \(Anna, Tom\)/);
});

test('!pay rejects bad input with usage', () => {
  const send = setup();
  send('!people Max Anna');
  assert.match(send('!pay 10 Eis --with Anna'), /Unbekannte Option: --with/);
  assert.match(send('!pay 10 Eis --for'), /--for braucht mindestens einen Namen/);
  assert.match(send('!pay 10 Eis --by Anna Max'), /genau einen Namen/);
  assert.match(send('!pay 10,555'), /kein gültiger Betrag/);
  assert.match(send('!pay Max 30 Pizza'), /Meintest du: !pay 30 Pizza --by Max/);
  assert.match(send('!pay --help'), /^\*!pay\* <betrag>/);
  assert.match(send('!settle -h'), /Schulden-Bot/);
});

test('sender is matched by WhatsApp name or linked with !me', () => {
  const send = setup();
  send('!people Max Anna Tom');
  // "Max Mustermann" matches member Max by first name.
  assert.match(send('!pay 30 Pizza'), /Max hat 30,00/);
  assert.match(send('!me'), /Du bist als Max eingetragen/);
  // An unknown sender has to introduce themselves first.
  assert.match(send('!pay 30 Pizza', STRANGER), /Ich weiß noch nicht, wer du bist/);
  assert.match(send('!pay 30 Pizza --by Tom', STRANGER), /Tom hat 30,00/);
  assert.match(send('!me tom', STRANGER), /du bist Tom/);
  assert.match(send('!pay 9 Kebab', STRANGER), /Tom hat 9,00/);
});

test('!request makes each debtor owe the full amount', () => {
  const send = setup();
  send('!people Max Anna Tom');
  assert.match(send('!request 20 Tickets --from Anna Tom'), /Max fordert 20,00\s€ für „Tickets“[\s\S]*Anna, Tom/);
  assert.match(send('!list'), /#1 📨 Max fordert 20,00\s€ von Anna, Tom – Tickets/);
  assert.match(send('!list'), /Gesamtausgaben: 0,00/);
  const reply = send('!settle');
  assert.match(reply, /Anna → Max: \*20,00\s€\*/);
  assert.match(reply, /Tom → Max: \*20,00\s€\*/);
});

test('!request validates its input', () => {
  const send = setup();
  send('!people Max Anna');
  assert.match(send('!request 20 Tickets'), /Gib --from an/);
  assert.match(send('!request 20 Tickets --from'), /--from braucht mindestens einen Namen/);
  assert.match(send('!request 20 Tickets --from Max'), /nicht von sich selbst/);
  assert.match(send('!request 20 --from Anna', STRANGER), /wer du bist/);
  assert.match(send('!request 20 Miete --from Anna --by Tom', STRANGER), /Tom fordert 20,00/);
  assert.match(send('!request --help'), /^\*!request\*/);
});

test('groups saved by older versions get missing fields', () => {
  const store = createStore(null);
  store.get('old');
  const group = store.get('old');
  delete group.links;
  assert.deepStrictEqual(store.get('old').links, {});
});

test('reset requires confirmation', () => {
  const send = setup();
  send('!people Max Anna');
  assert.match(send('!reset'), /!reset yes/);
  assert.match(send('!reset yes'), /zurückgesetzt/);
  assert.match(send('!people'), /Noch niemand/);
});

test('replies never start with "!"', () => {
  const send = setup();
  for (const cmd of ['!help', '!me', '!people', '!pay', '!request', '!list', '!delete', '!settle', '!reset', '!remove']) {
    assert.ok(!send(cmd).startsWith('!'), cmd);
  }
});
