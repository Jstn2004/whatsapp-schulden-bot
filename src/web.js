// Small local web UI: shows the QR code for connecting and the bot's status.
// The page itself stays in German since it is user-facing.
const http = require('http');
const QRCode = require('qrcode');

const PAGE = `<!doctype html>
<html lang="de">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Schulden-Bot</title>
<style>
  :root {
    --bg: #f0f2f5; --card: #ffffff; --text: #111b21; --muted: #667781;
    --accent: #25d366; --accent-dark: #128c7e; --border: #e4e7ea; --code: #f5f6f7;
  }
  @media (prefers-color-scheme: dark) {
    :root { --bg: #0b141a; --card: #1f2c33; --text: #e9edef; --muted: #8696a0;
            --border: #2a3942; --code: #111b21; }
  }
  * { box-sizing: border-box; }
  body { margin: 0; min-height: 100vh; display: grid; place-items: center; padding: 24px 16px;
         background: var(--bg); color: var(--text);
         font: 16px/1.5 -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; }
  .card { width: 100%; max-width: 440px; background: var(--card); border-radius: 20px;
          padding: 32px; box-shadow: 0 8px 30px rgba(0,0,0,.08); text-align: center; }
  h1 { margin: 0 0 4px; font-size: 24px; }
  .sub { color: var(--muted); margin: 0 0 24px; }
  .status { display: inline-flex; align-items: center; gap: 8px; padding: 6px 14px; border-radius: 999px;
            background: var(--code); font-size: 14px; font-weight: 600; margin-bottom: 24px; }
  .dot { width: 10px; height: 10px; border-radius: 50%; background: #f0b429; }
  .dot.pulse { animation: pulse 1.4s ease-in-out infinite; }
  .dot.ok { background: var(--accent); }
  .dot.err { background: #e5484d; }
  @keyframes pulse { 50% { opacity: .3; } }
  .qr-wrap { position: relative; width: 280px; height: 280px; margin: 0 auto 24px; border-radius: 16px;
             background: #fff; padding: 12px; border: 1px solid var(--border); }
  .qr-wrap img { width: 100%; height: 100%; display: block; image-rendering: pixelated; }
  .spinner { width: 48px; height: 48px; border-radius: 50%; border: 4px solid #e4e7ea;
             border-top-color: var(--accent); animation: spin 1s linear infinite;
             position: absolute; inset: 0; margin: auto; }
  @keyframes spin { to { transform: rotate(360deg); } }
  ol { text-align: left; padding-left: 22px; margin: 0; color: var(--muted); }
  ol b { color: var(--text); }
  .ready-icon { font-size: 64px; margin: 8px 0 12px; }
  .commands { text-align: left; background: var(--code); border-radius: 12px; padding: 16px;
              margin-top: 20px; font-size: 14px; }
  .commands code { font-family: ui-monospace, Menlo, monospace; color: var(--accent-dark); font-weight: 600; }
  @media (prefers-color-scheme: dark) { .commands code { color: var(--accent); } }
  .commands div { padding: 3px 0; }
  .hidden { display: none; }
</style>
</head>
<body>
<main class="card">
  <h1>💸 Schulden-Bot</h1>
  <p class="sub">WhatsApp mit dem Bot verbinden</p>
  <div class="status"><span id="dot" class="dot pulse"></span><span id="status">Starte…</span></div>

  <section id="connect">
    <div class="qr-wrap"><div id="spinner" class="spinner"></div><img id="qr" class="hidden" alt="QR-Code"></div>
    <ol>
      <li>Öffne <b>WhatsApp</b> auf dem Bot-Handy</li>
      <li>Tippe auf <b>Einstellungen → Verknüpfte Geräte</b></li>
      <li>Tippe auf <b>Gerät hinzufügen</b> und scanne den Code</li>
    </ol>
  </section>

  <section id="ready" class="hidden">
    <div class="ready-icon">✅</div>
    <p id="who" class="sub"></p>
    <div class="commands">
      <div><code>!me Max</code> sagen, wer du bist</div>
      <div><code>!people Max Anna Tom</code> Leute anlegen</div>
      <div><code>!pay 30 Pizza</code> Ausgabe erfassen</div>
      <div><code>!request 20 Tickets --from Anna</code> Geld fordern</div>
      <div><code>!settle</code> wer schuldet wem</div>
      <div><code>!help</code> alle Befehle</div>
    </div>
  </section>
</main>
<script>
  const labels = {
    starting: ['Starte WhatsApp…', 'pulse'],
    qr: ['Warte auf Scan', 'pulse'],
    authenticated: ['Verbinde…', 'pulse'],
    ready: ['Verbunden', 'ok'],
    disconnected: ['Getrennt – Bot neu starten', 'err'],
  };
  const $ = (id) => document.getElementById(id);
  async function poll() {
    try {
      const s = await (await fetch('/status')).json();
      const [text, cls] = labels[s.state] || labels.starting;
      $('status').textContent = text;
      $('dot').className = 'dot ' + cls;
      $('connect').classList.toggle('hidden', s.state === 'ready');
      $('ready').classList.toggle('hidden', s.state !== 'ready');
      if (s.qr) { $('qr').src = s.qr; }
      $('qr').classList.toggle('hidden', !s.qr || s.state !== 'qr');
      $('spinner').classList.toggle('hidden', s.state === 'qr' && !!s.qr);
      if (s.who) $('who').textContent = 'Der Bot läuft als ' + s.who + '. Schreib einen Befehl in eine Gruppe:';
    } catch {
      $('status').textContent = 'Bot nicht erreichbar';
      $('dot').className = 'dot err';
    }
    setTimeout(poll, 1500);
  }
  poll();
</script>
</body>
</html>`;

function startWebUi(port) {
  const state = { state: 'starting', qr: null, who: null };

  http.createServer((req, res) => {
    if (req.url === '/status') {
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify(state));
      return;
    }
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
    res.end(PAGE);
  }).listen(port, '127.0.0.1', () => {
    console.log(`🌐 Web UI: http://localhost:${port}`);
  });

  return {
    async showQr(qr) {
      state.qr = await QRCode.toDataURL(qr, { width: 512, margin: 1 });
      state.state = 'qr';
    },
    setState(newState, who = null) {
      state.state = newState;
      if (who) state.who = who;
      if (newState !== 'qr') state.qr = null;
    },
  };
}

module.exports = { startWebUi };
