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

// Oberfläche direkt im Browser öffnen (macOS: open, Windows: start, Linux: xdg-open)
const opener = { darwin: 'open', win32: 'start' }[process.platform] || 'xdg-open';
exec(`${opener} http://localhost:${PORT}`);

const client = new Client({
  authStrategy: new LocalAuth(), // Login bleibt in .wwebjs_auth gespeichert
});

client.on('qr', async (qr) => {
  await ui.showQr(qr);
  console.log('Scanne den QR-Code im Browser (oder hier im Terminal):');
  qrcode.generate(qr, { small: true });
});

client.on('authenticated', () => ui.setState('authenticated'));
client.on('ready', () => {
  const who = client.info?.pushname || client.info?.wid?.user || 'Bot';
  ui.setState('ready', who);
  console.log('✅ Bot läuft. Schreib !hilfe in einen Chat.');
});
client.on('auth_failure', (msg) => console.error('Login fehlgeschlagen:', msg));
client.on('disconnected', (reason) => {
  ui.setState('disconnected');
  console.log('Verbindung getrennt:', reason);
});

// message_create fängt auch eigene Nachrichten ab, damit man vom Bot-Handy aus testen kann.
client.on('message_create', async (msg) => {
  if (!msg.body.startsWith('!')) return;
  // Chat-ID direkt aus der Nachricht lesen; msg.getChat() bricht bei WhatsApp-Web-Updates gern.
  const chatId = msg.fromMe ? msg.to : msg.from;
  console.log(`📩 ${chatId}: ${msg.body}`);
  try {
    const reply = handle(msg.body, chatId, store);
    if (!reply) return;
    try {
      await msg.reply(reply);
    } catch {
      await client.sendMessage(chatId, reply); // Fallback ohne Zitat
    }
  } catch (err) {
    console.error('Fehler bei Nachricht:', err);
  }
});

client.initialize();
