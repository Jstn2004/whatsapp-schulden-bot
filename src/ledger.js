// Reine Rechenlogik: Salden berechnen und minimale Überweisungen ermitteln.

// Teilt einen Betrag fair auf n Personen auf; übrige Cents gehen an die ersten.
function splitEvenly(amount, n) {
  const base = Math.floor(amount / n);
  const rest = amount - base * n;
  return Array.from({ length: n }, (_, i) => base + (i < rest ? 1 : 0));
}

// Positiver Saldo = bekommt Geld, negativer Saldo = schuldet Geld.
function balances(expenses) {
  const result = new Map();
  const add = (name, cents) => result.set(name, (result.get(name) || 0) + cents);

  for (const e of expenses) {
    add(e.payer, e.amount);
    const shares = splitEvenly(e.amount, e.participants.length);
    e.participants.forEach((p, i) => add(p, -shares[i]));
  }
  return result;
}

// Greedy: der größte Schuldner zahlt an den größten Gläubiger. Ergibt höchstens n-1 Überweisungen.
function settle(balanceMap) {
  const creditors = [];
  const debtors = [];
  for (const [name, cents] of balanceMap) {
    if (cents > 0) creditors.push({ name, cents });
    if (cents < 0) debtors.push({ name, cents: -cents });
  }
  creditors.sort((a, b) => b.cents - a.cents);
  debtors.sort((a, b) => b.cents - a.cents);

  const transfers = [];
  let c = 0;
  let d = 0;
  while (c < creditors.length && d < debtors.length) {
    const amount = Math.min(creditors[c].cents, debtors[d].cents);
    transfers.push({ from: debtors[d].name, to: creditors[c].name, amount });
    creditors[c].cents -= amount;
    debtors[d].cents -= amount;
    if (creditors[c].cents === 0) c++;
    if (debtors[d].cents === 0) d++;
  }
  return transfers;
}

module.exports = { splitEvenly, balances, settle };
