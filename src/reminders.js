// Tracks how long people have owed money and builds reminder messages for overdue debts.
const { balances, settle } = require('./ledger');
const { formatEuro } = require('./money');

const HOUR = 60 * 60 * 1000;

// Parses "3d", "12h" or a plain number of days into hours.
function parseDuration(input) {
  const match = (input || '').trim().toLowerCase().match(/^(\d+)\s*([dh]?)$/);
  if (!match) return null;
  const hours = Number(match[1]) * (match[2] === 'h' ? 1 : 24);
  return hours >= 1 ? hours : null;
}

// Dative form, for use after "seit" and "nach".
function formatDuration(hours) {
  if (hours >= 24 && hours % 24 === 0) return hours === 24 ? '1 Tag' : `${hours / 24} Tagen`;
  return hours === 1 ? '1 Stunde' : `${hours} Stunden`;
}

// Remembers since when each person has had a negative balance. Call after every change.
function trackDebts(group, now = new Date()) {
  const owing = new Set();
  for (const [name, cents] of balances(group.expenses)) {
    if (cents < 0) owing.add(name);
  }
  for (const name of Object.keys(group.debtSince)) {
    if (!owing.has(name)) {
      delete group.debtSince[name];
      delete group.reminders.lastSent[name];
    }
  }
  for (const name of owing) {
    if (!group.debtSince[name]) group.debtSince[name] = now.toISOString();
  }
}

// The most recently linked WhatsApp ID for a name, used to @mention the person.
function idForName(group, name) {
  const ids = Object.keys(group.links).filter((id) => group.links[id] === name);
  return ids[ids.length - 1] || null;
}

// Returns { text, mentions } for everyone whose debt is overdue, or null if nobody is.
// A person is reminded again only after another full interval has passed.
function buildReminder(group, now = new Date()) {
  const { enabled, afterHours, lastSent } = group.reminders;
  if (!enabled) return null;

  const isDue = (name) => {
    if (!group.debtSince[name]) return false;
    const since = Math.max(Date.parse(group.debtSince[name]), Date.parse(lastSent[name] || 0));
    return now - since >= afterHours * HOUR;
  };
  const transfers = settle(balances(group.expenses)).filter((t) => isDue(t.from));
  if (transfers.length === 0) return null;

  const mentions = [];
  const lines = transfers.map((t) => {
    const id = idForName(group, t.from);
    if (id && !mentions.includes(id)) mentions.push(id);
    const who = id ? `@${id.split('@')[0]}` : t.from;
    // Whole days once it is a day or more, so a check at 73h still reads "3 Tagen".
    const openHours = Math.max(1, Math.floor((now - Date.parse(group.debtSince[t.from])) / HOUR));
    const open = openHours >= 24 ? openHours - (openHours % 24) : openHours;
    return `• ${who} → ${t.to}: *${formatEuro(t.amount)}* (offen seit ${formatDuration(open)})`;
  });
  for (const t of transfers) lastSent[t.from] = now.toISOString();

  return {
    text: `⏰ *Erinnerung: noch nicht bezahlt*\n${lines.join('\n')}\n\nSchon bezahlt? Dann schreib: !repay --to <Name>`,
    mentions,
  };
}

module.exports = { parseDuration, formatDuration, trackDebts, buildReminder };
