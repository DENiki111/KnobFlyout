# KnobFlyout YT-Bridge (Firefox, Chrome, Opera)

Small browser extensions that let KnobFlyout change the volume of the **YouTube player itself** (`setVolume`) instead of the whole browser session in the Windows mixer. They talk to KnobFlyout only locally over `ws://127.0.0.1:8975/ytb`, run only on `https://www.youtube.com/*`, need no permissions and collect no data.

| Folder | Browser | Format |
|---|---|---|
| `firefox/` | Firefox 140+ | Manifest V2, persistent background page |
| `chrome/` (`Opera/` = copy for Opera) | Chrome, Opera, Opera GX | Manifest V3, service worker (`page.js` runs in the page, `content.js` connects it to the extension); tells KnobFlyout whether it runs in Opera, so `opera.exe` and `chrome.exe` are kept apart |

Since 1.2.0 KnobFlyout's skip keys and ± buttons jump right in the video through the extension (command `seek`); KnobFlyout falls back to Windows when a page cannot seek (live streams, Netflix) or the extension is older.

**Build the upload packages:** run `pack.cmd` → `dist\knobflyout-yt-bridge-firefox-<version>.zip` and `dist\knobflyout-yt-bridge-chrome-<version>.zip`. For every new upload, raise `"version"` in that browser's `manifest.json` first.

*Deutsch weiter unten.*

## Firefox

Release Firefox only keeps **signed** extensions after a restart.

1. Run `pack.cmd`.
2. Sign in at [addons.mozilla.org → Submit a New Add-on](https://addons.mozilla.org/developers/addon/submit/distribution) and choose **“On your own”** (self-distributed, not listed publicly).
3. Upload `dist\knobflyout-yt-bridge-firefox-<version>.zip`. Source code is not minified, so no source upload is needed. Signing usually finishes automatically within minutes.
4. Download the signed `.xpi` and drag it onto Firefox (or `about:addons` → ⚙ → “Install Add-on From File…”). If you use private windows, allow the add-on there (`about:addons` → KnobFlyout YT-Bridge → “Run in Private Windows”).

To try it without signing: `about:debugging#/runtime/this-firefox` → “Load Temporary Add-on…” → `firefox\manifest.json` (gone after a Firefox restart).

## Chrome, Opera, Opera GX

- **Just for you (no store):** `chrome://extensions` → turn on **Developer mode** → **Load unpacked** → folder `chrome`. Stays installed after restarts. Reload open YouTube tabs afterwards (Chrome does not inject into tabs that were already open).
- **Chrome Web Store:** needs a developer account (one-time fee) at the [Chrome Web Store Developer Dashboard](https://chrome.google.com/webstore/devconsole). Upload `dist\knobflyout-yt-bridge-chrome-<version>.zip`, fill in the store listing (description, the 128 px icon, at least one screenshot), the privacy tab (single purpose: control the YouTube volume from KnobFlyout; host access only to www.youtube.com, needed to set the player volume; no data collected; no remote code) and choose visibility **Unlisted** if only you should find it. Review can take a few days.
- **Opera / Opera GX:** Opera removes a plainly loaded unpacked extension after a restart. Use the folder `Opera` (same files as `chrome`):
  1. `opera://extensions` → turn on **Developer mode** → **Pack extension**.
  2. Root directory = folder `Opera`. Private key file: leave empty the first time; Opera then creates `Opera.pem` next to the folder – keep it.
  3. Then **Load unpacked** → the same folder `Opera`. It stays after restarts and on autostart.
  4. **Update:** replace the folder with the new version and pack it again the same way, this time with the `.pem` from before as private key file, then load it again.

  Alternatively from the Chrome Web Store or [addons.opera.com](https://addons.opera.com/developer/).

Chrome stops the extension's service worker when it is idle. While a YouTube tab is open, the tab keeps it awake, so the bridge connects as soon as YouTube is open.

Then in KnobFlyout: **Settings → YouTube → “Control volume in the YouTube player”** → Save. The status shows each browser separately.

---

## Deutsch

Kleine Browser-Erweiterungen: KnobFlyout ändert damit die Lautstärke **im YouTube-Player selbst** statt der ganzen Browser-Session im Windows-Mixer. Verbindung nur lokal über `ws://127.0.0.1:8975/ytb`, laufen nur auf `https://www.youtube.com/*`, brauchen keine Berechtigungen und sammeln keine Daten.

Seit 1.2.0 springen die Skip-Hotkeys und ±-Knöpfe von KnobFlyout über die Erweiterung direkt im Video (Befehl `seek`); kann eine Seite nicht springen (Livestreams, Netflix) oder ist die Erweiterung älter, springt KnobFlyout über Windows.

**Pakete zum Hochladen bauen:** `pack.cmd` ausführen → `dist\knobflyout-yt-bridge-firefox-<Version>.zip` und `dist\knobflyout-yt-bridge-chrome-<Version>.zip`. Vor jedem neuen Hochladen `"version"` im `manifest.json` des Browsers erhöhen.

**Firefox** (Release-Firefox behält nur signierte Erweiterungen):
1. `pack.cmd` ausführen.
2. Auf [addons.mozilla.org → Neues Add-on einreichen](https://addons.mozilla.org/developers/addon/submit/distribution) anmelden und **„Selbst verteilen“ / „On your own“** wählen (nicht öffentlich gelistet).
3. `dist\knobflyout-yt-bridge-firefox-<Version>.zip` hochladen. Der Code ist nicht minifiziert, Quellcode muss nicht extra hochgeladen werden. Das Signieren läuft meist in wenigen Minuten automatisch.
4. Die signierte `.xpi` herunterladen und auf Firefox ziehen (oder `about:addons` → ⚙ → „Add-on aus Datei installieren…“). Für private Fenster dort „In privaten Fenstern ausführen“ erlauben.

Ausprobieren ohne Signatur: `about:debugging#/runtime/this-firefox` → „Temporäres Add-on laden…“ → `firefox\manifest.json` (weg nach Firefox-Neustart).

**Chrome, Opera, Opera GX:**
- **Nur für dich (ohne Store):** `chrome://extensions` → **Entwicklermodus** an → **Entpackte Erweiterung laden** → Ordner `chrome`. Bleibt nach Neustarts installiert. Danach offene YouTube-Tabs neu laden.
- **Chrome Web Store:** braucht ein Entwicklerkonto (einmalige Gebühr) im [Chrome Web Store Developer Dashboard](https://chrome.google.com/webstore/devconsole). `dist\knobflyout-yt-bridge-chrome-<Version>.zip` hochladen, Store-Eintrag (Beschreibung, 128-px-Symbol, mindestens ein Screenshot) und Datenschutz-Angaben ausfüllen (einziger Zweck: YouTube-Lautstärke aus KnobFlyout regeln; Host-Zugriff nur auf www.youtube.com, nötig um den Player zu regeln; keine Daten; kein Remote-Code), Sichtbarkeit **„Nicht gelistet“**, wenn nur du sie finden sollst. Die Prüfung kann ein paar Tage dauern.
- **Opera / Opera GX:** Eine einfach entpackt geladene Erweiterung entfernt Opera beim Neustart. Deshalb Ordner `Opera` (gleiche Dateien wie `chrome`):
  1. `opera://extensions` → **Entwicklermodus** an → **Erweiterung packen**.
  2. Stammverzeichnis = Ordner `Opera`. Private Schlüsseldatei beim ersten Mal leer lassen; Opera legt dann neben dem Ordner `Opera.pem` an – aufheben.
  3. Danach **Entpackte Erweiterung laden** → denselben Ordner `Opera`. Bleibt nach Neustarts und beim Autostart.
  4. **Update:** Ordner durch die neue Version ersetzen und genauso packen, diesmal mit der `.pem` von vorher als private Schlüsseldatei, dann wieder laden.

  Alternativ aus dem Chrome Web Store oder über [addons.opera.com](https://addons.opera.com/developer/).

Danach in KnobFlyout: **Einstellungen → YouTube → „Lautstärke im YouTube-Player regeln“** → Speichern. Der Status zeigt jeden Browser einzeln.
