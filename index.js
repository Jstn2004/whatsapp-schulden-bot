const path = require('path');
const { exec } = require('child_process');
const { Client, LocalAuth } = require('whatsapp-web.js');
const qrcode = require('qrcode-terminal');
const { createStore } = require('./src/store');
const { handle } = require('./src/commands');
const { buildReminder } = require('./src/reminders');
const { startWebUi } = require('./src/web');

const PORT = Number(process.env.PORT) || 3000;
// On a headless Linux server there is no browser to open.
const OPEN_BROWSER = process.env.OPEN_BROWSER
  ? process.env.OPEN_BROWSER === 'true'
  : process.platform !== 'linux';
const REMINDER_CHECK_MINUTES = Number(process.env.REMINDER_CHECK_MINUTES) || 15;

const store = createStore(path.join(__dirname, 'data', 'groups.json'));
const ui = startWebUi(PORT);

if (OPEN_BROWSER) {
  // Open the UI right away in the browser (macOS: open, Windows: start, Linux: xdg-open)
  const opener = { darwin: 'open', win32: 'start' }[process.platform] || 'xdg-open';
  exec(`${opener} http://localhost:${PORT}`);
}

const client = new Client({
  authStrategy: new LocalAuth(), // Login is persisted in .wwebjs_auth
  puppeteer: {
    // Chrome's sandbox does not work when running as root, which is common on servers.
    args: process.platform === 'linux' ? ['--no-sandbox', '--disable-setuid-sandbox'] : [],
  },
});

let ready = false;

client.on('qr', async (qr) => {
  await ui.showQr(qr);
  console.log('Scan the QR code in the browser (or here in the terminal):');
  qrcode.generate(qr, { small: true });
});

client.on('authenticated', () => ui.setState('authenticated'));
client.on('ready', () => {
  ready = true;
  const who = client.info?.pushname || client.info?.wid?.user || 'Bot';
  ui.setState('ready', who);
  console.log('✅ Bot is running. Send !help in a chat.');
});
client.on('auth_failure', (msg) => console.error('Login failed:', msg));
client.on('disconnected', (reason) => {
  ready = false;
  ui.setState('disconnected');
  console.log('Disconnected:', reason);
});

// Looks up the display names of everyone @mentioned in a message.
async function mentionedContacts(msg) {
  const ids = (msg.mentionedIds || []).map((id) => (typeof id === 'string' ? id : id._serialized));
  return Promise.all(ids.map(async (id) => {
    try {
      const contact = await client.getContactById(id);
      return { id, name: contact.pushname || contact.name || null };
    } catch {
      return { id, name: null };
    }
  }));
}

// message_create also catches our own messages, so you can test from the bot phone.
client.on('message_create', async (msg) => {
  if (!msg.body.startsWith('!')) return;
  // Read the chat ID straight from the message; msg.getChat() tends to break on WhatsApp Web updates.
  const chatId = msg.fromMe ? msg.to : msg.from;
  // In groups msg.author is the sender; in direct chats it is empty and msg.from is the sender.
  const sender = msg.fromMe
    ? { id: client.info?.wid?._serialized, name: client.info?.pushname }
    : { id: msg.author || msg.from, name: msg._data?.notifyName };
  console.log(`📩 ${chatId} (${sender.name || sender.id}): ${msg.body}`);
  try {
    const mentions = await mentionedContacts(msg);
    const reply = handle(msg.body, chatId, store, { sender, mentions });
    if (!reply) return;
    try {
      await msg.reply(reply);
    } catch {
      await client.sendMessage(chatId, reply); // Fallback without quoting
    }
  } catch (err) {
    console.error('Error handling message:', err);
  }
});

// Periodically posts reminders for overdue debts into groups that enabled them.
async function sendReminders() {
  if (!ready) return;
  for (const groupId of store.groupIds()) {
    const reminder = buildReminder(store.get(groupId));
    if (!reminder) continue;
    try {
      await client.sendMessage(groupId, reminder.text, { mentions: reminder.mentions });
      console.log(`⏰ Reminder sent to ${groupId}`);
    } catch (err) {
      console.error(`Failed to send reminder to ${groupId}:`, err);
    }
  }
  store.save();
}

setInterval(sendReminders, REMINDER_CHECK_MINUTES * 60 * 1000);

client.initialize();
