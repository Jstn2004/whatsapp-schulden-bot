const test = require('node:test');
const assert = require('node:assert');
const { handle } = require('../src/commands');
const { createStore } = require('../src/store');
const { splitEvenly, balances, settle } = require('../src/ledger');
const { parseAmount } = require('../src/money');

function setup() {
  const store = createStore(null);
  return (text) => handle(text, 'gruppe', store);
}

test('parseAmount versteht deutsche Schreibweisen', () => {
  assert.strictEqual(parseAmount('45'), 4500);
  assert.strictEqual(parseAmount('45,50'), 4550);
  assert.strictEqual(parseAmount('45.5€'), 4550);
  assert.strictEqual(parseAmount('0'), null);
  assert.strictEqual(parseAmount('abc'), null);
});

test('splitEvenly verteilt Rest-Cents', () => {
  assert.deepStrictEqual(splitEvenly(1000, 3), [334, 333, 333]);
});

test('settle braucht höchstens n-1 Überweisungen', () => {
  const b = new Map([['A', 3000], ['B', -1000], ['C', -1000], ['D', -1000]]);
  const t = settle(b);
  assert.strictEqual(t.length, 3);
  assert.ok(t.every((x) => x.to === 'A' && x.amount === 1000));
});

test('kompletter Ablauf: Pizza teilen und abrechnen', () => {
  const send = setup();
  send('!leute Max Anna Tom');
  send('!zahlt max 30 Pizza');
  send('!zahlt Anna 12 Getränke für Max Tom');
  const reply = send('!abrechnung');
  // Max: +30 -10 -6 = 14, Anna: -10 +12 = 2, Tom: -10 -6 = -16
  assert.match(reply, /Tom → Max: \*14,00\s€\*/);
  assert.match(reply, /Tom → Anna: \*2,00\s€\*/);
});

test('Ausgaben löschen und Fehlermeldungen', () => {
  const send = setup();
  assert.match(send('!zahlt Max 10 Eis'), /Leg erst die anderen an/);
  send('!leute Max Anna');
  send('!zahlt Max 10 Eis');
  assert.match(send('!löschen 1'), /gelöscht/);
  assert.match(send('!abrechnung'), /Noch keine Ausgaben/);
  assert.match(send('!zahlt Max zehn'), /kein gültiger Betrag/);
  assert.strictEqual(send('hallo'), null);
});

test('Antworten beginnen nie mit "!"', () => {
  const send = setup();
  for (const cmd of ['!hilfe', '!leute', '!zahlt', '!liste', '!löschen', '!abrechnung', '!reset', '!raus']) {
    assert.ok(!send(cmd).startsWith('!'), cmd);
  }
});
