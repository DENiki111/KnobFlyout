// KnobFlyout Browser-Bridge – Hintergrund: hält die Verbindung zu KnobFlyout (ws://127.0.0.1:8975/ytb)
// und reicht Befehle an den Player weiter, den KnobFlyout ausgewählt hat. Jeder Frame mit Player (Crunchyroll
// und VOE spielen in iframes) bekommt eine eigene Nummer; KnobFlyout kennt sie als "tabId".
const URL = "ws://127.0.0.1:8975/ytb";
const ports = new Map();  // Nummer -> Port des Content-Scripts
const states = new Map(); // Nummer -> letzter gemeldeter Zustand (null = kein Player)
let nextId = 1;
let ws = null;
let delay = 2000;

function send(obj) {
  if (ws && ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify(obj));
}

function sendState(tabId, s) {
  if (s) send({ type: "state", tabId, ...s });
  else send({ type: "gone", tabId });
}

function connect() {
  ws = new WebSocket(URL);
  ws.onopen = () => {
    delay = 2000;
    send({ type: "hello", version: 2 });
    for (const [tabId, s] of states) sendState(tabId, s);
  };
  // KnobFlyout läuft nicht (oder die Bridge ist aus): immer wieder versuchen, höchstens alle 10 s
  ws.onclose = () => {
    ws = null;
    setTimeout(connect, delay);
    delay = Math.min(delay * 2, 10000);
  };
  ws.onmessage = (e) => {
    let m;
    try { m = JSON.parse(e.data); } catch (err) { return; }
    if (m.type !== "cmd") return;
    const port = ports.get(m.tabId);
    if (port) port.postMessage(m);
    else send({ type: "res", rid: m.rid, tabId: m.tabId, ok: false });
  };
}

// Leerer Titel (Player im iframe) → Titel des Tabs
function withTitle(port, s) {
  if (s && !s.title && port.sender.tab && port.sender.tab.title) return { ...s, title: port.sender.tab.title };
  return s;
}

browser.runtime.onConnect.addListener((port) => {
  if (port.name !== "ytb" || !port.sender || !port.sender.tab) return;
  const tabId = nextId++;
  ports.set(tabId, port);
  port.onMessage.addListener((m) => {
    if (m.type === "state") {
      const s = withTitle(port, m.state);
      states.set(tabId, s);
      sendState(tabId, s);
    } else if (m.type === "res") {
      const s = withTitle(port, m.state);
      send({ type: "res", rid: m.rid, tabId, ok: !!s, ...(s || {}) });
    } else if (m.type === "error") {
      send({ type: "error", tabId, message: m.message });
    }
  });
  port.onDisconnect.addListener(() => {
    ports.delete(tabId);
    states.delete(tabId);
    send({ type: "gone", tabId });
  });
});

// Nur für den Test: Video auf einer nicht erkannten Seite → KnobFlyout-Protokoll
browser.runtime.onMessage.addListener((m) => {
  if (m && m.type === "unknown") send({ type: "unknown", host: String(m.host), frame: !!m.frame });
});

connect();
