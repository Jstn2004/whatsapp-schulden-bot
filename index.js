const path = require('path');
const { exec } = require('child_process');
const { Client, LocalAuth } = require('whatsapp-web.js');
const qrcode = require('qrcode-terminal');
const { createStore } = require('./src/store');
const { handle } = require('./src/commands');
const { startWebUi } = require('./src/web');

const PORT = 3000;
const store = createStore(path.join(__dirname, 'data', 'groups.json'));
const ui = startWebUi(PORT);

// Open the UI right away in the browser (macOS: open, Windows: start, Linux: xdg-open)
const opener = { darwin: 'open', win32: 'start' }[process.platform] || 'xdg-open';
exec(`${opener} http://localhost:${PORT}`);

const client = new Client({
  authStrategy: new LocalAuth(), // Login is persisted in .wwebjs_auth
});

client.on('qr', async (qr) => {
  await ui.showQr(qr);
  console.log('Scan the QR code in the browser (or here in the terminal):');
  qrcode.generate(qr, { small: true });
});

client.on('authenticated', () => ui.setState('authenticated'));
client.on('ready', () => {
  const who = client.info?.pushname || client.info?.wid?.user || 'Bot';
  ui.setState('ready', who);
  console.log('✅ Bot is running. Send !help in a chat.');
});
client.on('auth_failure', (msg) => console.error('Login failed:', msg));
client.on('disconnected', (reason) => {
  ui.setState('disconnected');
  console.log('Disconnected:', reason);
});

// message_create also catches our own messages, so you can test from the bot phone.
client.on('message_create', async (msg) => {
  if (!msg.body.startsWith('!')) return;
  // Read the chat ID straight from the message; msg.getChat() tends to break on WhatsApp Web updates.
  const chatId = msg.fromMe ? msg.to : msg.from;
  console.log(`📩 ${chatId}: ${msg.body}`);
  try {
    const reply = handle(msg.body, chatId, store);
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

client.initialize();
