// KnobFlyout Browser-Bridge – läuft in jedem Frame jeder Seite, wird aber nur auf erkannten Seiten aktiv
// (YouTube, Twitch, TikTok, Instagram, Netflix, Prime Video, Disney+, Crunchyroll, Joyn, VOE, Jellyfin; Erkennung in sites.js).
// YouTube: wrappedJSObject gibt direkten Zugriff auf die Player-API der Seite (setVolume/mute), ohne ein Skript
// in die Seite einzuschleusen. Andere Seiten: video.volume/muted direkt am Element.
(() => {
  const youtube = KnobSites.byHost() === "YouTube";
  let port = null;
  let last = "";
  let lastError = 0;
  let unknownSent = false;

  // Fehler landen in knob_service.log (höchstens einer pro Minute)
  function report(e) {
    const now = Date.now();
    if (!port || now - lastError < 60000) return;
    lastError = now;
    port.postMessage({ type: "error", message: String((e && e.message) || e) });
  }

  // YouTube: das spielende Video samt Player (Video-Seite, Shorts, Miniplayer); ohne spielendes Video das erste
  // mit Player. Vorschauen beim Überfahren von Vorschaubildern zählen nicht.
  function ytCurrent() {
    let first = null;
    for (const v of document.querySelectorAll("video")) {
      if (v.closest("ytd-video-preview, #inline-preview-player")) continue;
      const box = v.closest(".html5-video-player");
      const p = box && box.wrappedJSObject;
      if (!p || typeof p.setVolume !== "function") continue;
      if (!v.paused && !v.ended) return { v, p };
      if (!first) first = { v, p };
    }
    return first;
  }

  function ytState() {
    const c = ytCurrent();
    if (!c) return null;
    const md = navigator.mediaSession && navigator.mediaSession.metadata;
    return {
      site: "YouTube",
      volume: c.p.getVolume(),
      muted: c.p.isMuted(),
      // getPlayerState() liefert während Werbung -1, deshalb das video-Element fragen
      playing: !c.v.paused && !c.v.ended,
      title: (md && md.title) || document.title,
    };
  }

  // Sprung um by Sekunden über den Player (seekTo puffert wie die Pfeiltasten); Livestreams → seeked: false
  function ytSeek(c, by) {
    const dur = c.p.getDuration();
    const live = typeof c.p.getVideoData === "function" && c.p.getVideoData().isLive;
    if (live || !isFinite(dur) || dur <= 0 || !isFinite(by)) return { ...ytState(), seeked: false };
    const pos = Math.max(0, Math.min(dur - 0.25, c.p.getCurrentTime() + by));
    c.p.seekTo(pos, true);
    return { ...ytState(), seeked: true, pos, dur };
  }

  function ytCommand(m) {
    const c = ytCurrent();
    if (!c) return null;
    if (m.op === "seek") return ytSeek(c, Number(m.value));
    if (m.op === "set") c.p.setVolume(Math.max(0, Math.min(100, Math.round(m.value))));
    else if (m.op === "mute") m.value ? c.p.mute() : c.p.unMute();
    return ytState();
  }

  const state = () => (youtube ? ytState() : KnobSites.state());
  const command = (m) => (youtube ? ytCommand(m) : KnobSites.command(m));

  function connect() {
    port = browser.runtime.connect({ name: "ytb" });
    port.onMessage.addListener((m) => {
      if (m.type !== "cmd") return;
      let s = null;
      try { s = command(m); } catch (e) { s = null; report(e); }
      if (s) last = JSON.stringify(s);
      port.postMessage({ type: "res", rid: m.rid, state: s });
    });
  }

  function push() {
    if (!youtube && !KnobSites.detect()) {
      // Nur für den Test: Video auf einer nicht erkannten Seite einmal melden
      if (!unknownSent && KnobSites.unknownPlaying()) {
        unknownSent = true;
        browser.runtime.sendMessage({ type: "unknown", host: KnobSites.host, frame: window !== window.top });
      }
      return;
    }
    if (!port) connect();
    let s = null;
    try { s = state(); } catch (e) { report(e); return; }
    const key = JSON.stringify(s);
    if (key === last) return;
    last = key;
    port.postMessage({ type: "state", state: s });
  }

  // Nur bei Ereignissen abfragen statt dauernd (stört die Seiten und andere Add-ons am wenigsten).
  // Medien-Ereignisse steigen nicht auf → in der Capture-Phase am Dokument mithören.
  for (const ev of ["play", "playing", "pause", "ended", "emptied", "volumechange", "loadedmetadata"])
    document.addEventListener(ev, () => setTimeout(push, 50), true);
  // Titelwechsel und alles ohne Ereignis – nur auf erkannten Seiten
  setInterval(() => { if (port) push(); }, 2000);
  push();
})();
