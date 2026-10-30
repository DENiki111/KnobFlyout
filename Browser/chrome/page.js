// KnobFlyout YT-Bridge (Chrome) – läuft in der Seite (world: MAIN) und hat so direkten Zugriff auf die
// Player-API von YouTube (setVolume/mute). Mit der Erweiterung spricht es über content.js per DOM-Ereignis.
(() => {
  const TO_EXT = "knobflyout-ytb-out";
  const FROM_EXT = "knobflyout-ytb-in";
  let last = "";
  let lastError = 0;

  function send(msg) {
    document.dispatchEvent(new CustomEvent(TO_EXT, { detail: JSON.stringify(msg) }));
  }

  // Fehler landen in knob_service.log (höchstens einer pro Minute)
  function report(e) {
    const now = Date.now();
    if (now - lastError < 60000) return;
    lastError = now;
    send({ type: "error", message: String((e && e.message) || e) });
  }

  // Das spielende Video samt Player (Video-Seite, Shorts, Miniplayer); ohne spielendes Video das erste
  // mit Player. Vorschauen beim Überfahren von Vorschaubildern zählen nicht.
  function current() {
    let first = null;
    for (const v of document.querySelectorAll("video")) {
      if (v.closest("ytd-video-preview, #inline-preview-player")) continue;
      const p = v.closest(".html5-video-player");
      if (!p || typeof p.setVolume !== "function") continue;
      if (!v.paused && !v.ended) return { v, p };
      if (!first) first = { v, p };
    }
    return first;
  }

  function state() {
    const c = current();
    if (!c) return null;
    const md = navigator.mediaSession && navigator.mediaSession.metadata;
    return {
      volume: c.p.getVolume(),
      muted: c.p.isMuted(),
      // getPlayerState() liefert während Werbung -1, deshalb das video-Element fragen
      playing: !c.v.paused && !c.v.ended,
      title: (md && md.title) || document.title,
    };
  }

  function push() {
    let s = null;
    try { s = state(); } catch (e) { report(e); return; }
    const key = JSON.stringify(s);
    if (key === last) return;
    last = key;
    send({ type: "state", state: s });
  }

  document.addEventListener(FROM_EXT, (e) => {
    let m;
    try { m = JSON.parse(e.detail); } catch (err) { return; }
    if (m.type === "resync") { last = ""; push(); return; } // Hintergrund neu gestartet
    if (m.type !== "cmd") return;
    let s = null;
    try {
      const c = current();
      if (c) {
        if (m.op === "set") c.p.setVolume(Math.max(0, Math.min(100, Math.round(m.value))));
        else if (m.op === "mute") m.value ? c.p.mute() : c.p.unMute();
        s = state();
      }
    } catch (err) {
      s = null;
      report(err);
    }
    if (s) last = JSON.stringify(s);
    send({ type: "res", rid: m.rid, state: s });
  });

  // Player nur bei Ereignissen abfragen statt dauernd (stört YouTube und andere Erweiterungen am wenigsten).
  // Medien-Ereignisse steigen nicht auf → in der Capture-Phase am Dokument mithören.
  for (const ev of ["play", "playing", "pause", "ended", "emptied", "volumechange", "loadedmetadata"])
    document.addEventListener(ev, () => setTimeout(push, 50), true);
  setInterval(push, 2000); // Titelwechsel und alles ohne Ereignis
  push();
})();
