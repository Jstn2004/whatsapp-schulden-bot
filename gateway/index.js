// WhatsApp gateway: connects to WhatsApp Web, forwards "/" commands to the .NET core
// and sends messages on its behalf. Contains no business logic.
const path = require('path');
const { exec } = require('child_process');
const { Client, LocalAuth } = require('whatsapp-web.js');
const qrcode = require('qrcode-terminal');
const { startWebUi } = require('./src/web');

const PORT = Number(process.env.GATEWAY_PORT) || 3000;
const CORE_URL = process.env.CORE_URL || 'http://localhost:5080';
const TOKEN = process.env.GATEWAY_TOKEN || '';
// Minimum pause between outgoing messages, so bursts don't look like spam to WhatsApp.
const SEND_INTERVAL_MS = Number(process.env.SEND_INTERVAL_MS) || 800;
// On a headless Linux server there is no browser to open.
const OPEN_BROWSER = process.env.OPEN_BROWSER
  ? process.env.OPEN_BROWSER === 'true'
  : process.platform !== 'linux';

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

const client = new Client({
  authStrategy: new LocalAuth({ dataPath: path.join(__dirname, '.wwebjs_auth') }),
  puppeteer: {
    // Chrome's sandbox does not work when running as root, which is common on servers.
    args: process.platform === 'linux' ? ['--no-sandbox', '--disable-setuid-sandbox'] : [],
  },
});

let ready = false;

// IDs of messages the gateway sent itself, so they are never treated as commands.
const sentIds = new Set();
function rememberSent(id) {
  sentIds.add(id);
  if (sentIds.size > 1000) sentIds.delete(sentIds.values().next().value);
}

// WhatsApp increasingly uses anonymous "@lid" IDs. The core identifies people by phone
// number, so every ID is translated to its "@c.us" form when WhatsApp tells us the number.
const phoneIds = new Map();
async function toPhoneId(id) {
  if (!id || !id.endsWith('@lid')) return id;
  if (phoneIds.has(id)) return phoneIds.get(id);
  try {
    const [result] = await client.getContactLidAndPhone([id]);
    const phoneId = result?.pn || id;
    phoneIds.set(id, phoneId);
    return phoneId;
  } catch {
    return id;
  }
}

// token is how the mention appears in the text ("@<token>"), id is the normalized person ID.
async function mentionsOf(msg) {
  const rawIds = (msg.mentionedIds || []).map((id) => (typeof id === 'string' ? id : id._serialized));
  return Promise.all(rawIds.map(async (rawId) => {
    let name = null;
    try {
      const contact = await client.getContactById(rawId);
      name = contact.pushname || contact.name || null;
    } catch {
      // Name stays unknown; the core falls back to the number.
    }
    return { token: rawId.split('@')[0], id: await toPhoneId(rawId), name };
  }));
}

async function forwardToCore(payload) {
  for (let attempt = 1; attempt <= 3; attempt++) {
    try {
      const res = await fetch(`${CORE_URL}/api/messages`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-Gateway-Token': TOKEN },
        body: JSON.stringify(payload),
      });
      if (!res.ok) console.error(`Core rejected message ${payload.messageId}: HTTP ${res.status}`);
      return;
    } catch {
      console.error(`Core not reachable at ${CORE_URL} (attempt ${attempt}/3)`);
      await sleep(2000 * attempt);
    }
  }
}

// Outgoing messages go through one queue with a pause between them.
let sendQueue = Promise.resolve();
let lastSentAt = 0;
function enqueueSend(send) {
  const run = sendQueue.then(async () => {
    const wait = lastSentAt + SEND_INTERVAL_MS - Date.now();
    if (wait > 0) await sleep(wait);
    try {
      return await send();
    } finally {
      lastSentAt = Date.now();
    }
  });
  sendQueue = run.catch(() => {});
  return run;
}

function readJson(req) {
  return new Promise((resolve, reject) => {
    let body = '';
    req.on('data', (chunk) => { body += chunk; });
    req.on('end', () => {
      try {
        resolve(JSON.parse(body || '{}'));
      } catch (err) {
        reject(err);
      }
    });
  });
}

function sendJson(res, status, data) {
  res.writeHead(status, { 'Content-Type': 'application/json' });
  res.end(JSON.stringify(data));
}

// POST /api/send { chatId, text, mentions: [id] } -> { messageId }
async function handleApi(req, res) {
  if (!req.url.startsWith('/api/')) return false;
  if (TOKEN && req.headers['x-gateway-token'] !== TOKEN) {
    sendJson(res, 401, { error: 'invalid token' });
    return true;
  }
  if (req.method === 'POST' && req.url === '/api/send') {
    if (!ready) {
      sendJson(res, 503, { error: 'WhatsApp not connected' });
      return true;
    }
    try {
      const { chatId, text, mentions = [] } = await readJson(req);
      const sent = await enqueueSend(() => client.sendMessage(chatId, text, { mentions }));
      // WhatsApp returns nothing for chats it cannot resolve, e.g. numbers without WhatsApp.
      if (!sent?.id) throw new Error(`Could not send to ${chatId}`);
      rememberSent(sent.id._serialized);
      sendJson(res, 200, { messageId: sent.id._serialized });
    } catch (err) {
      console.error('Send failed:', err.message || err);
      sendJson(res, 502, { error: String(err.message || err) });
    }
    return true;
  }
  sendJson(res, 404, { error: 'not found' });
  return true;
}

const ui = startWebUi(PORT, handleApi);

if (OPEN_BROWSER) {
  // Open the UI right away in the browser (macOS: open, Windows: start, Linux: xdg-open)
  const opener = { darwin: 'open', win32: 'start' }[process.platform] || 'xdg-open';
  exec(`${opener} http://localhost:${PORT}`);
}

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
  console.log(`✅ Gateway connected as ${who}, forwarding commands to ${CORE_URL}`);
});
client.on('auth_failure', (msg) => console.error('Login failed:', msg));

// Reconnect automatically; after a logout this shows a new QR code.
client.on('disconnected', (reason) => {
  ready = false;
  ui.setState('disconnected');
  console.log('Disconnected:', reason, '- reconnecting in 5s');
  setTimeout(async () => {
    try {
      await client.destroy();
    } catch {
      // Already gone.
    }
    client.initialize();
  }, 5000);
});

// message_create also catches messages typed on the bot phone itself, which is handy for testing.
client.on('message_create', async (msg) => {
  if (!msg.body.startsWith('/') || sentIds.has(msg.id._serialized)) return;
  try {
    // Read the chat ID straight from the message; msg.getChat() tends to break on WhatsApp Web updates.
    const chatId = msg.fromMe ? msg.to : msg.from;
    // In groups msg.author is the sender; in direct chats it is empty and msg.from is the sender.
    const rawSender = msg.fromMe ? client.info.wid._serialized : (msg.author || msg.from);
    const payload = {
      messageId: msg.id._serialized,
      chatId,
      isGroup: chatId.endsWith('@g.us'),
      senderId: await toPhoneId(rawSender),
      senderName: (msg.fromMe ? client.info.pushname : msg._data?.notifyName) || null,
      body: msg.body,
      mentions: await mentionsOf(msg),
      botId: client.info.wid._serialized,
      timestamp: new Date(msg.timestamp * 1000).toISOString(),
    };
    console.log(`📩 ${chatId} (${payload.senderName || payload.senderId}): ${msg.body}`);
    await forwardToCore(payload);
  } catch (err) {
    console.error('Error forwarding message:', err);
  }
});

client.initialize();
