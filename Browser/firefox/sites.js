// KnobFlyout Browser-Bridge – Seiten-Erkennung und Video-Steuerung für alle Seiten außer YouTube.
// Läuft im Content-Script (isolierte Welt); video.volume/muted lassen sich von dort direkt setzen.
// Datei ist in Browser/firefox und Browser/chrome identisch.
var KnobSites = (() => {
  const host = location.hostname.toLowerCase();
  const on = (domain) => host === domain || host.endsWith("." + domain);

  // Seiten mit fester Adresse
  function byHost() {
    if (on("youtube.com") || on("youtube-nocookie.com")) return "YouTube";
    if (on("crunchyroll.com")) return "Crunchyroll";
    if (on("twitch.tv")) return "Twitch";
    if (on("tiktok.com")) return "TikTok";
    if (on("joyn.de") || on("joyn.at") || on("joyn.ch")) return "Joyn";
    if (on("netflix.com")) return "Netflix";
    if (on("primevideo.com")) return "Prime Video";
    if (on("disneyplus.com")) return "Disney+";
    if (on("instagram.com")) return "Instagram";
    if (on("voe.sx") || /(^|\.)voe[a-z0-9-]*\./.test(host)) return "VOE";
    return hoster(host) || anime(host);
  }

  // Video-Hoster (meist als iframe auf Anime-/Serien-Seiten); wechseln oft die Adresse → Namensmuster + Endung egal
  function hoster(h) {
    if (/(^|\.)(dood|doodstream|dooood|d0+d|d000d|do0od|ds2play|ds2video|doodcdn)\.[a-z.]+$/.test(h)) return "Doodstream";
    if (/(^|\.)(streamtape|strtape|stape|strcloud|tapecontent|shavetape|tapeblocker|tapewithadblock|adblockstrtape|streamadblocker|watchadsontape)\.[a-z.]+$/.test(h) || h === "streamta.pe") return "Streamtape";
    if (/(^|\.)(vidoza|videzz)\.[a-z.]+$/.test(h)) return "Vidoza";
    if (/(^|\.)(filemoon|kerapoxy|moonplayer)\.[a-z.]+$/.test(h)) return "Filemoon";
    // Player von HiAnime/9anime
    if (/(^|\.)(megacloud|rapid-cloud|rabbitstream|vidwish|megaplay)\.[a-z.]+$/.test(h)) return "MegaCloud";
    return null;
  }

  function anime(h) {
    if (/(^|\.)(hianime|aniwatch|aniwatchtv)\.[a-z.]+$/.test(h)) return "HiAnime";
    if (/(^|\.)9anime(tv)?\.[a-z.]+$/.test(h)) return "9anime";
    return null;
  }

  // Seite, in die ein Player-iframe eingebettet ist (Chrome: ancestorOrigins, Firefox: Referrer)
  function topHost() {
    if (window === window.top) return host;
    try {
      const a = location.ancestorOrigins;
      if (a && a.length) return new URL(a[a.length - 1]).hostname.toLowerCase();
    } catch (e) { /* egal */ }
    try { return document.referrer ? new URL(document.referrer).hostname.toLowerCase() : ""; } catch (e) { return ""; }
  }

  // Name fürs OSD: Hoster-Player auf HiAnime/9anime heißen wie die Anime-Seite, sonst wie der Hoster
  function displayName() {
    if (site && hoster(host)) return anime(topHost()) || site;
    return site;
  }

  // Jellyfin läuft auf dem eigenen Server (beliebige Adresse): an der Web-Oberfläche erkennen
  function isJellyfin() {
    const app = document.querySelector('meta[name="application-name"]');
    if (app && /jellyfin/i.test(app.content)) return true;
    return !!document.querySelector("video.htmlvideoplayer");
  }

  // VOE wechselt ständig die Adresse (Spiegel-Domains, meist als iframe auf anderen Seiten):
  // an Verweisen auf voe im Seitenquelltext erkennen
  function isVoe() {
    if (/^voe\b/i.test(document.title)) return true;
    for (const el of document.querySelectorAll("script[src], link[href], meta[content]")) {
      const v = el.getAttribute("src") || el.getAttribute("href") || el.getAttribute("content") || "";
      if (/(^|[\/.])voe\.sx\b|voe-?player/i.test(v)) return true;
    }
    for (const s of document.querySelectorAll("script:not([src])")) {
      const t = s.textContent;
      if (t.length < 200000 && /voe\.sx|voeplayer|voe_player|base_domain\s*=\s*'voe/i.test(t)) return true;
    }
    return false;
  }

  // Hoster auf einer noch unbekannten Spiegel-Adresse: am Seitentitel bzw. an Verweisen im Quelltext erkennen
  function hosterByPage() {
    const names = { doodstream: "Doodstream", streamtape: "Streamtape", vidoza: "Vidoza", filemoon: "Filemoon" };
    const find = (t) => { const m = /doodstream|streamtape|vidoza|filemoon/i.exec(t || ""); return m ? names[m[0].toLowerCase()] : null; };
    let r = find(document.title);
    if (r) return r;
    for (const el of document.querySelectorAll("script[src], link[href], meta[content]")) {
      r = find(el.getAttribute("src") || el.getAttribute("href") || el.getAttribute("content"));
      if (r) return r;
    }
    return null;
  }

  // Prime Video läuft auch auf amazon.de/.com usw. als Overlay – nur dort, wo der Prime-Player auf der Seite ist
  // (Produktvideos im Shop zählen nicht)
  const amazon = /(^|\.)amazon\.(com|de|at|co\.uk|fr|it|es|nl|ca|co\.jp|com\.au)$/.test(host);
  const isPrime = () => !!document.querySelector('[class*="atvwebplayersdk"], .webPlayerSDKContainer, #dv-web-player');

  let site = byHost();
  let lastCheck = 0;

  // Seite erkannt? Jellyfin/VOE erst nachsehen, wenn ein Video da ist, und höchstens alle 5 s
  function detect() {
    if (site) return site;
    const now = Date.now();
    if (now - lastCheck < 5000 || !document.querySelector("video")) return null;
    lastCheck = now;
    if (amazon) { if (isPrime()) site = "Prime Video"; return site; }
    if (isJellyfin()) site = "Jellyfin";
    else if (isVoe()) site = "VOE";
    else site = hosterByPage();
    return site;
  }

  // Jellyfin zeigt die Lautstärke kubisch an (Element = (Wert/100)^3), die anderen linear
  const toElement = (pct) => (site === "Jellyfin" ? Math.pow(pct / 100, 3) : pct / 100);
  const fromElement = (vol) => (site === "Jellyfin" ? Math.cbrt(vol) * 100 : vol * 100);

  // Twitch merkt sich die Lautstärke selbst und stellt sie beim Stumm/Laut wieder her (setzt beim Stummschalten
  // video.volume auf 0) → dort über den eigenen Regler und Stumm-Knopf des Players gehen
  const twitchSlider = () => (site === "Twitch" ? document.querySelector('input[data-a-target="player-volume-slider"]') : null);
  const twitchMute = () => document.querySelector('button[data-a-target="player-mute-unmute-button"]');

  function setSlider(input, value) {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value").set.call(input, String(value));
    input.dispatchEvent(new Event("input", { bubbles: true }));
    input.dispatchEvent(new Event("change", { bubbles: true }));
  }

  const playing = (v) => !v.paused && !v.ended && v.readyState > 2;

  // Alle Videos, auch in offenen Shadow-Roots (falls ein Player sein Video dort versteckt)
  function videos() {
    const found = [...document.querySelectorAll("video")];
    if (found.length || !site) return found;
    const walk = (root) => {
      for (const el of root.querySelectorAll("*")) {
        if (!el.shadowRoot) continue;
        found.push(...el.shadowRoot.querySelectorAll("video"));
        walk(el.shadowRoot);
      }
    };
    walk(document);
    return found;
  }

  // Diese Seiten starten jedes neue Video (nächstes Video im Feed, nächste Folge, Werbung) wieder mit dem eigenen
  // Wert → zuletzt per Knopf gesetzten Wert auf jedes neu startende Video übertragen
  const STICKY = ["TikTok", "Joyn", "Netflix", "Prime Video", "Disney+", "Instagram"];
  const sticky = () => STICKY.includes(site);
  let kept = null;      // { volume (0–1 am Element), muted }
  let keptVideo = null; // Video, auf dem der Wert zuletzt gesetzt wurde

  function keep(v) {
    if (!sticky() || !kept || v === keptVideo || !playing(v)) return;
    keptVideo = v;
    if (kept.volume != null) v.volume = kept.volume;
    if (kept.muted != null) v.muted = kept.muted;
  }

  // Das spielende Video; sonst das erste geladene. Kleine Vorschauen (Twitch-Startseite) zählen nicht.
  function current() {
    let first = null;
    for (const v of videos()) {
      if (v.offsetWidth < 160 && v.offsetHeight < 90) continue;
      if (playing(v)) return v;
      if (!first && (v.currentSrc || v.src)) first = v;
    }
    return first;
  }

  function state() {
    const v = current();
    if (!v) return null;
    keep(v);
    const md = navigator.mediaSession && navigator.mediaSession.metadata;
    const tw = twitchSlider();
    return {
      site: displayName(),
      volume: Math.round(tw ? parseFloat(tw.value) * 100 : fromElement(v.volume)),
      muted: v.muted,
      playing: playing(v),
      // In iframes (Crunchyroll, VOE) ist der Titel oft leer – dann nimmt der Hintergrund den Tab-Titel
      title: (md && md.title) || (window === window.top ? document.title : ""),
    };
  }

  // Sprung um m.value Sekunden. Netflix stürzt bei direktem currentTime ab, Livestreams haben kein Ende →
  // seeked: false, KnobFlyout springt dann über Windows
  function seek(v, by) {
    const dur = v.duration;
    if (site === "Netflix" || !isFinite(dur) || dur <= 0 || !isFinite(by)) return { ...state(), seeked: false };
    v.currentTime = Math.max(0, Math.min(dur - 0.25, v.currentTime + by));
    return { ...state(), seeked: true, pos: v.currentTime, dur };
  }

  function command(m) {
    const v = current();
    if (!v) return null;
    if (m.op === "seek") return seek(v, Number(m.value));
    const pct = Math.max(0, Math.min(100, m.value));
    const tw = twitchSlider();
    if (m.op === "set") {
      if (tw) setSlider(tw, Math.round(pct) / 100);
      else v.volume = Math.max(0, Math.min(1, toElement(pct)));
      if (sticky()) { kept = { ...kept, volume: v.volume }; keptVideo = v; }
    } else if (m.op === "mute") {
      const btn = tw && twitchMute();
      if (btn) { if (v.muted !== !!m.value) btn.click(); }
      else v.muted = !!m.value;
      if (sticky()) { kept = { ...kept, muted: !!m.value }; keptVideo = v; }
    }
    return state();
  }

  // Spielt ein Video auf einer nicht erkannten Seite? Nur für den Test: KnobFlyout schreibt den Host ins Protokoll,
  // damit sich weitere VOE-Adressen nachtragen lassen
  function unknownPlaying() {
    if (site) return false;
    for (const v of document.querySelectorAll("video")) if (playing(v)) return true;
    return false;
  }

  return { byHost, detect, state, command, unknownPlaying, host };
})();
