// KnobFlyout Browser-Bridge (Chrome) – isolierte Welt, läuft in jedem Frame jeder Seite, wird aber nur auf
// erkannten Seiten aktiv (Erkennung in sites.js).
// YouTube: verbindet page.js (Seite, Player-API) per DOM-Ereignis mit dem Hintergrund.
// Andere Seiten: video.volume/muted direkt hier am Element.
(() => {
  const TO_EXT = "knobflyout-ytb-out";
  const FROM_EXT = "knobflyout-ytb-in";
  const youtube = KnobSites.byHost() === "YouTube";
  let port = null;
  let last = "";
  let lastError = 0;
  let unknownSent = false;

  function toPage(msg) {
    document.dispatchEvent(new CustomEvent(FROM_EXT, { detail: JSON.stringify(msg) }));
  }

  function post(msg) {
    try { if (port) port.postMessage(msg); } catch (e) { /* Port gerade weg */ }
  }

  function report(e) {
    const now = Date.now();
    if (now - lastError < 60000) return;
    lastError = now;
    post({ type: "error", message: String((e && e.message) || e) });
  }

  function onCommand(m) {
    if (m.type !== "cmd") return;
    if (youtube) { toPage(m); return; }
    let s = null;
    try { s = KnobSites.command(m); } catch (e) { s = null; report(e); }
    if (s) last = JSON.stringify(s);
    post({ type: "res", rid: m.rid, state: s });
  }

  function connect() {
    if (!chrome.runtime || !chrome.runtime.id) return; // Erweiterung neu geladen oder entfernt
    try { port = chrome.runtime.connect({ name: "ytb" }); } catch (e) { port = null; return; }
    port.onMessage.addListener(onCommand);
    // Chrome beendet den Service Worker immer wieder → neu verbinden (weckt ihn) und Zustand neu melden
    port.onDisconnect.addListener(() => { port = null; setTimeout(connect, 1000); });
    if (youtube) toPage({ type: "resync" });
    else { last = ""; push(); }
  }

  function push() {
    if (!KnobSites.detect()) {
      // Nur für den Test: Video auf einer nicht erkannten Seite einmal melden
      if (!unknownSent && KnobSites.unknownPlaying()) {
        unknownSent = true;
        try { chrome.runtime.sendMessage({ type: "unknown", host: KnobSites.host, frame: window !== window.top }); } catch (e) { /* egal */ }
      }
      return;
    }
    if (!port) { connect(); return; } // connect() meldet den Zustand selbst
    let s = null;
    try { s = KnobSites.state(); } catch (e) { report(e); return; }
    const key = JSON.stringify(s);
    if (key === last) return;
    last = key;
    post({ type: "state", state: s });
  }

  if (youtube) {
    document.addEventListener(TO_EXT, (e) => {
      try { post(JSON.parse(e.detail)); } catch (err) { /* kaputte Meldung */ }
    });
    connect();
  } else {
    for (const ev of ["play", "playing", "pause", "ended", "emptied", "volumechange", "loadedmetadata"])
      document.addEventListener(ev, () => setTimeout(push, 50), true);
    setInterval(() => { if (port) push(); }, 2000); // Titelwechsel und alles ohne Ereignis
    push();
  }
  // Herzschlag hält den Service Worker wach, solange ein Player offen ist – sonst ist er beim Drehen nicht verbunden
  setInterval(() => post({ type: "ping" }), 20000);
})();
