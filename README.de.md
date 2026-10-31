# KnobFlyout

**Lautstärke pro App für Windows mit einem echten Lautstärke-Drehknopf** – mit Profilen, Bildschirmanzeige (OSD) und Media-Flyout.

Am Knopf drehen, und KnobFlyout ändert die Lautstärke genau der Apps, die du willst (zum Beispiel nur das Spiel, nur Discord oder nur Spotify), statt die des ganzen Systems. Ein kleines OSD zeigt, was passiert ist, ein Media-Flyout zeigt, was gerade läuft.

🇬🇧 [English version](README.md)

---

## Inhalt

- [Funktionen](#funktionen)
- [Voraussetzungen](#voraussetzungen)
- [Installation](#installation)
- [Einführung](#einführung)
- [Bedienung](#bedienung)
- [Profile](#profile)
- [OSD (Lautstärke-Anzeige)](#osd-lautstärke-anzeige)
- [Media-Flyout](#media-flyout)
- [Position](#position)
- [Designs](#designs)
- [Spotify (optional)](#spotify-optional)
- [YouTube in Firefox und Chrome (optional)](#youtube-in-firefox-und-chrome-optional)
- [Einstellungen im Überblick](#einstellungen-im-überblick)
- [Dateien](#dateien)
- [Aus dem Quellcode bauen](#aus-dem-quellcode-bauen)
- [Danksagung](#danksagung)

---

## Funktionen

- **Lautstärke pro App statt Systemlautstärke** – der Drehknopf (oder die Tasten Lauter / Leiser / Stumm) steuert nur die Apps des aktiven Profils.
- **Profile** – bis zu 6 (Standard: Gaming, Komm., Musik, Video), jeweils mit Name, Logo und Farbe. Wechsel mit einer Taste.
- **Neue-App-Erkennung** – spielt eine unbekannte App Ton ab, fragt ein Popup, zu welchem Profil sie gehört. Lässt sich abschalten.
- **Automatisch einsortieren** (standardmäßig an) – Spiele, Browser, Musik- und Chat-Apps landen von selbst im passenden Profil; Unbekanntes fragt weiter das Popup.
- **OSD** – zeigt Lautstärke und Stumm je App, Profilwechsel, Feststell-/Num-/Rollen-Taste und Positionswechsel.
- **Media-Flyout** – Titel, Interpret, Cover, Fortschrittsleiste (klicken oder ziehen zum Springen), Zurück / Play-Pause / Weiter, ±10 Sekunden, „…“-Menü und Quellen-Leiste, wenn mehrere Apps gleichzeitig spielen. Bleibt auf Wunsch stehen bei Programmen deiner Wahl, z. B. solange Spotify spielt.
- **8 OSD-Designs und 8 Media-Flyout-Designs** – vom ModernFlyouts- und Windows-11-Look über Dynamic Island, macOS, Material You, Gaming-HUD und Hardware-Regler bis zu frei einstellbaren Designs. Jedes Flyout-Design lässt sich anpassen und unter eigenem Namen speichern.
- **Freie Platzierung** – 9 Plätze je Bildschirm, mehrere Bildschirme, Verschieben mit der Maus, gespeicherte Positionen, per Taste durchschaltbar.
- **Spotify Web API** (optional) – steuert Spotifys eigene Lautstärke.
- **YouTube in Firefox und Chrome** (optional) – mit einer kleinen Browser-Erweiterung ändert der Knopf die Lautstärke im YouTube-Player selbst statt die des ganzen Browsers.
- **Deutsch und Englisch**, Farbschema hell, dunkel oder wie das System.
- **Autostart** – mit Windows starten, umschaltbar in den Einstellungen und im Task-Manager („Autostart-Apps“).
- Läuft im Infobereich der Taskleiste, **keine Administratorrechte** nötig.

## Voraussetzungen

- Windows 10 (Version 2004 / Build 19041 oder neuer) oder Windows 11, 64 Bit
- Ein Drehknopf oder eine Tastatur, die die normalen Medientasten **Lauter / Leiser / Stumm** sendet (das tun die meisten Multimedia-Tastaturen, Lautstärke-Knöpfe und Makro-Pads)

Das Setup bringt alles mit, eine eigene .NET-Installation ist nicht nötig.

## Installation

1. **`KnobFlyout-Setup.exe`** unter [Releases](../../releases) herunterladen und starten.
2. Sprache wählen (Deutsch / English).
3. Zielordner wählen – Standard `%LocalAppData%\Programs`. Der Ordner **`KnobFlyout`** wird dort automatisch angelegt.
4. Zusätzliche Aufgaben wählen: Desktop-Verknüpfung, Startmenü-Eintrag (damit die Windows-Suche KnobFlyout findet), mit Windows starten.
5. **Installieren**, dann **Fertigstellen** – KnobFlyout startet und erscheint im Infobereich der Taskleiste (neben der Uhr).

Hinweise:

- **Windows SmartScreen** warnt eventuell, weil das Setup nicht signiert ist: „Weitere Informationen“ → „Trotzdem ausführen“.
- Für geschützte Ordner wie `C:\Program Files` bietet das Setup an, sich als Administrator neu zu starten.
- **Autostart** lässt sich später in den Einstellungen (Seite **Allgemein**) oder im Task-Manager unter „Autostart-Apps“ ein- und ausschalten – KnobFlyout steht dort immer in der Liste.
- **Update:** einfach das neuere Setup ausführen – es erkennt die installierte Version, ein Klick auf **Aktualisieren** genügt. Ordner, Einstellungen, Spotify-Anmeldung, Autostart und Verknüpfungen bleiben, wie sie sind; KnobFlyout wird dafür kurz beendet und lässt sich am Ende wieder starten. „Ordner oder Optionen ändern …“ gibt es weiterhin, falls du etwas anderes willst.
- **Umstieg von ModernFlyout** (dem alten Namen): Das Setup übernimmt Einstellungen und Spotify-Verbindung und entfernt die alte Version.
- **Deinstallieren:** Windows-Einstellungen → Apps → Installierte Apps → KnobFlyout → Deinstallieren. Dabei wird der Programmordner samt Einstellungen gelöscht.

## Einführung

Beim ersten Start öffnet sich eine kurze Einführung (das Neue-App-Popup wartet, bis sie zu ist). Sie erklärt Knopf, Profile und Funktionen und richtet sie direkt mit dir ein. Die Vorschauen sind live: Sie reagieren auf deinen Knopf (oder die Knöpfe − und + darunter) und zeigen, was wirklich läuft, und ihre Knöpfe wirken wirklich. Was du dort änderst, gilt sofort.

Die Schritte: Drehen und drücken · Profile · Profil-Taste · eigene Profile (auch die vorgefertigten lassen sich ändern und löschen) · Apps zuweisen (oder automatisch einsortieren) · Media-Flyout · Look (direkt in der Einführung anpassbar: Vorlage, Aufbau, Farben, Deckkraft) · Platz auf dem Bildschirm · Snap-Punkte.

Am Schluss wählst du: **Zum Programm** oder **Alle Funktionen anzeigen**, eine Übersicht über alles, was KnobFlyout kann. Jede Funktion ist dort erklärt und lässt sich gleich ausprobieren und einstellen.

Du kannst sie jederzeit überspringen. Sie kommt wieder, wenn du die Einstellungen öffnest, bis du **Nicht mehr anzeigen** anhakst. Später findest du sie unter **Einstellungen → Allgemein → Einführung**.

## Bedienung

### Tasten

| Aktion | Standard-Taste | Was passiert |
|---|---|---|
| Knopf nach rechts / links drehen | Lauter / Leiser | Ändert die Lautstärke der Apps im aktiven Profil (2 % pro Raste) |
| Stumm-Taste drücken | Stumm | Schaltet diese Apps stumm / wieder laut |
| Profil wechseln | **Strg + Umschalt + P** (kurz) | Nächstes Profil, Anzeige im OSD |
| Nächste gespeicherte Position | Strg + Umschalt + P **halten** (600 ms) | Setzt OSD und Flyout auf die nächste gespeicherte Position (braucht gespeicherte Positionen und „Mit der Maus verschieben“); weiter halten = weiter durchschalten |
| 10 s vor- / zurückspulen | nicht festgelegt | Springt im laufenden Titel |
| Nächster Titel (Geste) | aus | z. B. Play/Pause zweimal schnell drücken |
| Quelle wechseln (Geste) | aus | z. B. Play/Pause lange halten |
| Zuweisen-Taste je Profil | nicht festgelegt | Nur solange das Neue-App-Popup offen ist: weist die App diesem Profil zu |
| Feststell- / Num- / Rollen-Taste | – | Zeigt An / Aus im OSD |

Alle Tasten lassen sich in den Einstellungen ändern (Seite **Tastenkürzel**): ins Feld klicken, neue Tastenkombination drücken, Esc bricht ab. Gesten können eine eigene Taste oder die Play/Pause-Taste nutzen; mit der Play/Pause-Taste funktioniert normales Play/Pause weiterhin.

Gut zu wissen:

- Solange KnobFlyout läuft, ändern die Lautstärke-Tasten **nicht mehr die Windows-Gesamtlautstärke** – sie steuern nur die Apps des aktiven Profils.
- Bei „Nur Apps mit Ton“ (Standard) wirkt der Knopf nur auf Apps des Profils, die gerade Ton abspielen. Spielt keine, ändert sich nichts.
- Windows gibt keine Tasten an normale Programme weiter, solange ein als Administrator laufendes Fenster im Vordergrund ist. Der Knopf reagiert dann so lange nicht.

### Maus

| Wo | Aktion |
|---|---|
| Symbol in der Taskleiste | Linksklick → **Einstellungen**, Rechtsklick → **Einstellungen** oder **Beenden** |
| KnobFlyout.exe starten | Öffnet die Einstellungen, auch wenn KnobFlyout schon läuft (mit Windows gestartet bleibt es im Hintergrund) |
| OSD / Media-Flyout | Ziehen zum Verschieben (Option „Mit der Maus verschieben“); rastet an gespeicherten Positionen ein |
| OSD / Media-Flyout | Klicken zum Ausblenden (Option „Per Klick ausblenden“) |
| Media-Flyout | Zurück, Play/Pause, Weiter, −10 s / +10 s |
| Fortschrittsleiste | Klicken oder ziehen zum Springen |
| „…“-Knopf | Menü mit Zufallswiedergabe, Wiederholen und mehr (siehe unten) |
| Quellen-Leiste | Klick auf eine Quelle schaltet um (die bisherige pausiert); der kleine Pause-Punkt pausiert eine Quelle, ohne umzuschalten (Leiste und Punkt lassen sich abschalten) |
| Neue-App-Popup | Profil-Knopf = zuweisen, ⊘ = App ausschließen |

## Profile

Ein Profil ist eine Gruppe von Apps, die der Knopf gemeinsam steuert – zum Beispiel *Gaming* (Spiel), *Komm.* (Discord, Teams), *Musik* (Spotify), *Video* (Browser, VLC).

- **Apps zuweisen:** Seite **Profile & Apps** → App-Liste: In jeder Zeile gibt es einen Knopf je Profil und ⊘ zum Ausschließen. Oder direkt im Neue-App-Popup.
- **Automatisch einsortieren** (Seite **Profile & Apps** oder in der Einführung): Neue Spiele (Steam, Epic, GOG, Xbox, die Spieleliste von Windows), Browser und Video-Player, Musik- und Chat-Apps landen ohne Nachfrage in dem Profil, das du für diese Art von App gewählt hast. Was KnobFlyout nicht erkennt oder auf „Nachfragen“ steht, geht wie gewohnt ins Popup. Bereits zugewiesene Apps bleiben, wo sie sind.
- **Neue-App-Popup:** erscheint, wenn eine noch nicht zugewiesene App Ton abspielt. Profil-Knopf klicken oder dessen Zuweisen-Taste drücken; ⊘ schließt die App aus. Ohne Klick verschwindet es nach 12 Sekunden, die App wird später erneut vorgeschlagen. Abschalten: Seite **Allgemein** oder **Profile & Apps** → **Zuweisungs-OSD anzeigen**. Automatisch einsortieren läuft dann weiter, alle anderen neuen Apps bleiben ohne Profil, bis du sie in der App-Liste zuweist.
- **Profile bearbeiten** (Seite **Profile & Apps**): bis zu 6 Profile; Name, Logo (44 Windows-Symbole oder ein eigenes Bild), Farbe, löschen, hinzufügen. Die Reihenfolge ist die Wechsel-Reihenfolge. Mindestens ein Profil bleibt.
- **Gesteuerte Apps:** „Nur Apps mit Ton“ (Standard) oder „Alle Apps im Profil“.
- **Leere Profile überspringen:** Wechsel nur zu Profilen mit laufenden Apps.

## OSD (Lautstärke-Anzeige)

Das OSD erscheint beim Drehen, beim Profilwechsel, bei Feststelltasten und beim Positionswechsel. Werden mehrere Apps geändert, steht die erste mit „+N“ da.

- An / aus, Anzeigedauer 0,5–10 s
- **Design:** darunter **Eigene Designs** (Design, Farben, Deckkraft und Ecken unter einem Namen speichern) und **Farbprofil**
- **Farben** je Design: Hintergrund, Text, Nebentext, Akzent, Knopf-Fläche, Leiste und Rand einzeln (Design „Eigenes“ hat seine ausführlichen Farben); Ecken bei den älteren Designs
- **Farbprofile** sind für OSD und Media-Flyout dieselben und passen in jedes Design; auf Wunsch gilt ein gewähltes Profil für beide zugleich
- Deckkraft getrennt für Hintergrund (0–100 %) und Text/Symbole (20–100 %) – bei allen Designs, auch fürs Neue-App-Popup
- 👁-Knopf neben dem Design: Live-Vorschau aller Ansichten neben dem Einstellungsfenster

## Media-Flyout

Das Media-Flyout erscheint bei einem Titelwechsel und wenn die Wiedergabe startet oder pausiert – für jede App, die ihre Medien an Windows meldet (Spotify, Browser, Mediaplayer, …).

- **Inhalt:** App-Name und Logo, Cover, Titel und Interpret, Fortschrittsleiste mit Zeiten, Zurück / Play-Pause / Weiter, ±10-Sekunden-Knöpfe
- **Quellen-Leiste:** spielen mehrere Apps gleichzeitig, gibt es einen Eintrag je App – Klick schaltet um. Sie lässt sich abschalten, ebenso das Pause-Zeichen neben spielenden Quellen (Seite **Media-Flyout** → **Inhalt**)
- **„…“-Menü** (Einträge wählbar): Zufallswiedergabe, Wiederholen, Nächste Quelle, Dauerhaft anzeigen an/aus (für die App, die gerade spielt), Hotkeys ±10 s an/aus, Angeheftet lassen, Position speichern, Nächste Position, Position zurücksetzen, Einstellungen öffnen, Ausblenden
- **Dauerhaft anzeigen** (Seite **Media-Flyout** → **Dauerhaft anzeigen**): Klick die Programme an, bei denen es stehen bleiben soll, zum Beispiel Spotify. Du wählst, ob es **beim Abspielen** steht (bei Pause blendet es aus) oder **solange geöffnet** (auch pausiert). Spielt ein anderes Programm los, zeigt das Flyout das kurz an und verschwindet; es kommt zurück, wenn dieses Programm aufhört. Mit Position **Eigene** hat es eine eigene Stelle, gewählt per Kachel oder durch Ziehen des Flyouts; es hängt dann nicht am OSD, und das OSD weicht ihm aus. Das gilt für alle Designs.
- **Optionen:** an / aus, Anzeigedauer 2–30 s, Fortschrittsleiste, ±10-s-Knöpfe, Cover als unscharfer Hintergrund (Unschärfe und Abdunkelung einstellbar), Deckkraft getrennt für Hintergrund und Text/Symbole
- **Jedes Design anpassen:** Aussehen → **Design anpassen** öffnet einen Editor mit Vorschau oben (die Quellen-Leiste steht dort immer; **Live-Flyout** schaltet auf das, was wirklich läuft, mit wirkenden Knöpfen), für jedes Design, zum Beispiel Dynamic Island oder Gaming-HUD:
  - **Alles hier ist ein Entwurf:** Weder die Voreinstellung noch eine Vorlage wird verändert. Gespeichert wird mit **Als eigenes Design speichern**; verlässt du die Seite mit ungespeicherten Änderungen, fragt KnobFlyout, ob du speichern oder ohne Speichern verlassen willst
  - **Vorlagen** als Startpunkt: Standard, Kompakt, Minimal, Großes Cover (die Farben bleiben). ↺ neben einem Wert setzt ihn auf die gewählte Vorlage zurück
  - **Eigene Designs** enthalten Aufbau, Deckkraft, Ecken und Farben. Wählen und löschen: Seite **Media-Flyout** → **Eigene Designs**. Ein gewähltes Design bearbeitest du hier weiter, gleicher Name überschreibt
  - **Was soll zu sehen sein?** – alle Bereiche in der Reihenfolge des Flyouts: App-Zeile, Titel und Cover, Zeitleiste, Quellen-Leiste, Knöpfe. Schalter blendet einen Bereich ein oder aus, ↑ ↓ ändert die Reihenfolge, Klick auf die Zeile zeigt mehr
  - einzelne Elemente als Kacheln an/aus: Titel, Interpret, Cover · Zurück, ±10 s, Play/Pause, Weiter, „…“
  - dazu Cover-Platz und -Größe, Titelgröße, Zeiten, „…“ oben rechts, Knopfgröße und Ausrichtung; runde oder flache Knöpfe bei ModernFlyouts, Windows 11 und „Eigenes“
  - **Farben** bei jedem Design: Hintergrund, Text, Nebentext, Akzent, Knöpfe, Leiste und Rand einzeln, jede fest oder aus dem Titelbild (Bild-Knopf); ↺ setzt eine Farbe auf das Design zurück. Der Hardware-Regler hat dazu seine Display-Farben. Die Wellen von Dynamic Island und die Grundfarbe von Material You folgen dem Titelbild, bis du eine feste Farbe wählst. Farbprofile gelten für alle Flyout-Designs
  - Manches gehört fest zum Design: Es lässt sich ausschalten, aber nicht verschieben, etwa die Zeitleiste im Display des Hardware-Reglers

## Position

Seite **Position**:

- **Anordnung:** Getrennt · Media-Flyout über dem OSD · Media-Flyout unter dem OSD. Zusammen bewegen sich beide als ein Block.
- **Platz auf dem Bildschirm:** Vorschau jedes Bildschirms mit 9 Kacheln (oben / Mitte / unten × links / Mitte / rechts) oder „Eigene Position“. Bildschirm getrennt für OSD und Flyout wählbar.
- **Position testen:** zeigt beide kurz genau dort, wo sie erscheinen werden.
- **Gespeicherte Positionen:** aktuelle Position übernehmen oder per Kachel wählen, jede einzeln testen, durch Halten der Profil-Taste durchschalten.
- **Position zurücksetzen** (Erweitert), falls etwas außerhalb des Bildschirms liegt.

OSD und Flyout bleiben immer im Bildschirm, halten die Taskleiste frei (auch eine automatisch ausgeblendete) und überlappen sich nicht. Nach einem Auflösungs- oder Bildschirmwechsel werden eigene Positionen automatisch umgerechnet.

## Designs

| OSD | Media-Flyout |
|---|---|
| ModernFlyouts (Standard) | ModernFlyouts (Standard) |
| Windows 11 | Windows 11 |
| Eigenes (eigene Farben) | Eigenes (eigener Aufbau und Farben) |
| Dynamic Island | Dynamic Island |
| macOS | macOS |
| Material You (Windows-Akzentfarbe) | Material You (Farbe aus dem Cover) |
| Gaming-HUD | Gaming-HUD |
| Hardware-Regler | Hardware-Regler |

OSD- und Media-Flyout-Design sind unabhängig wählbar. Alle Einstellungen zum Aussehen stehen zusätzlich gesammelt auf der Seite **Design**. ↺ neben einer Einstellung setzt sie auf den Standard zurück.

## Spotify (optional)

Ohne API steuert KnobFlyout Spotify wie jede andere App über den Windows-Mixer. Mit der Spotify Web API ändert es stattdessen Spotifys eigenen Lautstärkeregler. Dafür ist **Spotify Premium** nötig.

Einrichten (Seite **Spotify**, dort Schritt für Schritt erklärt):

1. Das [Spotify-Developer-Dashboard](https://developer.spotify.com/dashboard) öffnen und „Create app“ klicken.
2. Als Redirect-URI genau **`http://127.0.0.1:5000/callback/`** eintragen (in den Einstellungen gibt es dafür einen Kopieren-Knopf).
3. „Web API“ ankreuzen und speichern.
4. Client ID und Client Secret in KnobFlyout eintragen.
5. **Verbinden** klicken, im Browser zustimmen – danach prüft **Testen** die Verbindung.

Der Token liegt in `.spotify_cache` im Programmordner. Für Spotify gibt es eine eigene Schrittweite (Standard 4 %).

## YouTube in Firefox und Chrome (optional)

Normalerweise ändert der Knopf die ganze Browser-Session im Windows-Mixer. Mit der YT-Bridge ändert er stattdessen den Lautstärkeregler des **YouTube-Players** – nur das Video, nicht den restlichen Browser. Das geht ohne Fokus, mit YouTube im Hintergrund-Tab und bei minimiertem Fenster, auch während Werbung, bei Shorts und im Miniplayer.

- Gilt, solange in dem Browser ein YouTube-Video **spielt** und der Browser im aktiven Profil ist. Sonst regelt der Knopf den Browser wie bisher über den Mixer.
- Firefox, Chrome und Opera / Opera GX (nutzen die Chrome-Erweiterung) können gleichzeitig verbunden sein; jede Browser-Session im Mixer (`firefox.exe`, `chrome.exe`, `opera.exe`) regelt YouTube im jeweiligen Browser.
- Mehrere YouTube-Tabs spielen: der zuletzt gestartete gewinnt. Stumm schaltet YouTubes eigenes Stumm. Schrittweite = normale Schrittweite.
- Das OSD zeigt dann **YouTube** statt des Browsers.

Einrichten (Seite **YouTube**, Details in der [README in `Browser`](Browser/README.md)):

1. **Firefox:** auf der Seite [Releases](../../releases) die signierte `.xpi` anklicken und die Installation bestätigen.
2. **Chrome, Opera, Opera GX:** den Ordner `Browser/chrome` aus diesem Repo holen, dann `chrome://extensions` bzw. `opera://extensions` → Entwicklermodus → **Entpackte Erweiterung laden** → diesen Ordner wählen. Danach offene YouTube-Tabs neu laden.
3. **Lautstärke im YouTube-Player regeln** einschalten und **Speichern**. Der Status zeigt je Browser, wann seine Erweiterung verbunden ist.

Die Erweiterungen sprechen nur lokal mit KnobFlyout (`ws://127.0.0.1:8975`, angenommen werden nur Verbindungen von Browser-Erweiterungen).

## Einstellungen im Überblick

Öffnen per Linksklick auf das Symbol in der Taskleiste, über Rechtsklick → **Einstellungen** oder indem du KnobFlyout noch einmal startest. Änderungen gelten nach **Speichern**; der Knopf ist ausgegraut, bis sich etwas geändert hat (Sprache und Farbschema gelten sofort).

| Seite | Inhalt |
|---|---|
| Allgemein | Sprache, hell oder dunkel, Zuweisungs-OSD an / aus, mit Windows starten, Einführung, Einstellungen neu laden |
| Lautstärke | Schrittweite, Schrittweite Spotify |
| Profile & Apps | Profil-Taste, gesteuerte Apps, leere Profile überspringen, Profile bearbeiten, Zuweisungs-OSD an / aus, automatisch einsortieren, Zuweisen-Tasten, App-Liste mit Profil-Zuweisung, ignorierte Apps |
| OSD | An / aus, Anzeigedauer, per Klick ausblenden, Design, Deckkraft, Farben |
| Media-Flyout | An / aus, Anzeigedauer, dauerhaft anzeigen, Inhalt, Quellen-Leiste, „…“-Menü, Design (anpassbar, mit Farben, Vorlagen und eigenen gespeicherten Designs), Deckkraft, Cover-Hintergrund, Spul-Tasten, Gesten |
| Position | Anordnung, Platz auf dem Bildschirm, testen, verschieben, gespeicherte Positionen, zurücksetzen |
| Design | Alle Einstellungen zum Aussehen an einem Ort |
| Tastenkürzel | Alle Tasten und Halte-/Doppeldruck-Zeiten an einem Ort |
| Spotify | Anleitung, verbinden, testen, trennen |
| YouTube | YouTube-Player in Firefox und Chrome an / aus, Verbindungsstatus je Browser, Anleitung |
| Info | Pfade von Konfiguration und Protokoll, Klick öffnet den Ordner |

## Dateien

Alle Dateien liegen im Programmordner (Standard `%LocalAppData%\Programs\KnobFlyout`):

| Datei | Inhalt |
|---|---|
| `KnobFlyout.exe` | Das Programm |
| `config.json` | Alle Einstellungen |
| `.spotify_cache` | Spotify-Token (nur mit Spotify-API) |
| `knob_service.log` | Protokoll für die Fehlersuche |
| `uninstall.cmd` | Deinstallation (wird auch von „Installierte Apps“ benutzt) |

Für einen Neuanfang mit Standard-Einstellungen KnobFlyout beenden und `config.json` löschen.

## Aus dem Quellcode bauen

Voraussetzungen: Windows, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
# Starten (Debug)
dotnet run --project KnobFlyout

# Portable einzelne exe → publish\KnobFlyout.exe
publish.bat

# Setup → dist\KnobFlyout-Setup.exe (veröffentlicht die App automatisch mit)
dotnet build Installer\KnobFlyoutSetup.csproj -c Release
```

Aufbau:

| Ordner | Inhalt |
|---|---|
| `KnobFlyout/` | Die App (WPF, .NET 8) – `Audio/` (Sessions, Spotify), `Input/` (Tastatur-Hook), `Osd/` (OSD, Media-Flyout, Designs), `Media/` (Windows-Mediensitzungen), `Settings/`, `Tray/`, `Config/` |
| `Installer/` | Setup-Assistent (WinForms, .NET Framework 4.8) mit eingebetteter App |

Gebaut mit [NAudio](https://github.com/naudio/NAudio) (Audio-Sessions), [Hardcodet NotifyIcon](https://github.com/hardcodet/wpf-notifyicon) (Tray-Symbol) und der Windows-Mediensteuerung (GSMTC).

## Danksagung

- Aussehen und Media-Flyout angelehnt an [ModernFlyouts](https://github.com/ModernFlyouts-Community/ModernFlyouts).
- Entstanden als Python-Werkzeug (*KnobEarTrumpet*), danach nach C#/WPF portiert.
