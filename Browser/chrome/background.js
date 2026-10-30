// KnobFlyout YT-Bridge (Chrome) – Service Worker: hält die Verbindung zu KnobFlyout (ws://127.0.0.1:8975/ytb)
// und reicht Befehle an den YouTube-Tab weiter, den KnobFlyout ausgewählt hat. Chrome beendet Service Worker
// nach 30 s ohne Aktivität: der Herzschlag der YouTube-Tabs und das Ping an KnobFlyout halten ihn wach.
const URL = "ws://127.0.0.1:8975/ytb";
const ports = new Map();  // tabId -> Port des Content-Scripts
const states = new Map(); // tabId -> letzter gemeldeter Zustand (null = kein Player auf der Seite)
let ws = null;
let delay = 2000;

// Alle Chromium-Browser melden sich mit chrome-extension:// – Opera (auch GX) hier unterscheiden,
// damit KnobFlyout den Mixer-Eintrag opera.exe dem richtigen Browser zuordnet
function browserName() {
  const brands = (navigator.userAgentData && navigator.userAgentData.brands) || [];
  if (brands.some((b) => /opera/i.test(b.brand)) || / OPR\//.test(navigator.userAgent)) return "opera";
  return "chrome";
}

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
    send({ type: "hello", version: 1, browser: browserName() });
    for (const [tabId, s] of states) sendState(tabId, s);
  };
  // KnobFlyout läuft nicht (oder YT-Bridge ist aus): immer wieder versuchen, höchstens alle 10 s
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

chrome.runtime.onConnect.addListener((port) => {
  if (port.name !== "ytb" || !port.sender || !port.sender.tab) return;
  const tabId = port.sender.tab.id;
  ports.set(tabId, port);
  port.onMessage.addListener((m) => {
    if (m.type === "state") {
      states.set(tabId, m.state);
      sendState(tabId, m.state);
    } else if (m.type === "res") {
      send({ type: "res", rid: m.rid, tabId, ok: !!m.state, ...(m.state || {}) });
    } else if (m.type === "error") {
      send({ type: "error", tabId, message: m.message });
    }
  });
  port.onDisconnect.addListener(() => {
    if (ports.get(tabId) !== port) return; // Tab hat schon eine neue Seite geladen
    ports.delete(tabId);
    states.delete(tabId);
    send({ type: "gone", tabId });
  });
});

// Senden verlängert die Lebenszeit des Service Workers (Chrome 116+); KnobFlyout ignoriert "ping"
setInterval(() => send({ type: "ping" }), 20000);
connect();
