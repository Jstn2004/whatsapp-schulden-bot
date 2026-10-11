// Beträge werden intern immer in Cent (Integer) gespeichert, um Rundungsfehler zu vermeiden.

function parseAmount(input) {
  if (!input) return null;
  const cleaned = input.replace(/€|eur/gi, '').trim().replace(',', '.');
  if (!/^\d+(\.\d{1,2})?$/.test(cleaned)) return null;
  const cents = Math.round(parseFloat(cleaned) * 100);
  return cents > 0 ? cents : null;
}

function formatEuro(cents) {
  return (cents / 100).toLocaleString('de-DE', { style: 'currency', currency: 'EUR' });
}

module.exports = { parseAmount, formatEuro };
