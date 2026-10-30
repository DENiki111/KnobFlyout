// KnobFlyout YT-Bridge (Chrome) – isolierte Welt: verbindet page.js (Seite) mit dem Hintergrund.
(() => {
  const TO_EXT = "knobflyout-ytb-out";
  const FROM_EXT = "knobflyout-ytb-in";
  let port = null;

  function toPage(msg) {
    document.dispatchEvent(new CustomEvent(FROM_EXT, { detail: JSON.stringify(msg) }));
  }

  function connect() {
    if (!chrome.runtime || !chrome.runtime.id) return; // Erweiterung neu geladen oder entfernt
    try { port = chrome.runtime.connect({ name: "ytb" }); } catch (e) { port = null; return; }
    port.onMessage.addListener((m) => { if (m.type === "cmd") toPage(m); });
    // Chrome beendet den Service Worker immer wieder → neu verbinden (weckt ihn) und Zustand neu melden
    port.onDisconnect.addListener(() => { port = null; setTimeout(connect, 1000); });
    toPage({ type: "resync" });
  }

  document.addEventListener(TO_EXT, (e) => {
    if (!port) return;
    try { port.postMessage(JSON.parse(e.detail)); } catch (err) { /* Port gerade weg */ }
  });

  connect();
  // Herzschlag hält den Service Worker wach, solange YouTube offen ist – sonst ist er beim Drehen nicht verbunden
  setInterval(() => { try { if (port) port.postMessage({ type: "ping" }); } catch (e) { /* Port gerade weg */ } }, 20000);
})();
