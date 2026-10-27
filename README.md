# KnobEarTrumpet (C#/.NET/WPF) — Milestone 2/3 (Core + OSD Dark)

Projektgerüst: Config, Profile, WASAPI-Session-Handling (NAudio), Low-Level-
Keyboard-Hook, Tray-Icon. Stack identisch zu ModernFlyouts (net8.0-windows,
WPF), damit dessen Code später direkt übernommen werden kann.

## Build
```
dotnet restore
dotnet build -c Debug
```
Visual Studio 2022 (Workload ".NET Desktop Development") oder `dotnet` CLI
mit .NET 8 SDK erforderlich. **Wichtig:** Ich konnte hier nicht kompilieren
(keine .NET-SDK-/Windows-Umgebung verfügbar) — bitte beim ersten Build
auftretende Compiler-Fehler exakt mitschicken, dann fixe ich sie direkt.

## Stand
- Core 1:1 nach knob_service.py: get_targets (active/assigned), Volume/Mute je App,
  Audio-Worker mit Akkumulation, Hotkey-Parser (profile_hotkey), Profilwechsel inkl. skip_empty.
- OSD (Dark-Design): Volume-Karten, STUMM, Profil-Karte, Stapelung um osd_x/osd_y, Auto-Hide.
  Zweites Design "modern" (auch für osd_design "windows"): ModernFlyouts-Look mit Systemthema,
  Akzentfarbe, Fluent-Icons und Fade. Karten sind klick-durchlässig (noch keine Interaktion). osd_x/osd_y werden als
  Bildschirmeinheiten (DIP) verwendet.

## OSD verschieben & Snap-Positionen
In den Einstellungen unter "OSD-Positionen" per Haken aktivierbar: Ist er gesetzt,
ist jede OSD-Karte (Volume/Profil, klick-durchlässig entfällt dann) per Maus
verschiebbar (OsdCard.Dragged-Event). Beim Loslassen berechnet OsdManager daraus
die neue Stapel-Anker-Position (osd_x/osd_y), rastet innerhalb 40px in eine
gespeicherte Snap-Position ein und speichert. Das Neue-App-Popup verwendet jetzt
denselben Anker statt einer festen Position oben rechts.

## Quellen-Leiste im Media-Flyout (mehrere Apps gleichzeitig)
Haben mehrere Apps gerade eine aktive Mediensession (z.B. Spotify + YouTube im
Browser), zeigt das Media-Flyout jetzt eine kleine Leiste mit einem Pill pro Quelle
(Media/MediaController.GetSources() nutzt GSMTC-Manager.GetSessions() statt nur
GetCurrentSession()). Klick auf einen Pill schaltet um, welche Quelle angezeigt/
gesteuert wird (SwitchTo). Der kleine ⏸-Punkt neben einer spielenden Quelle pausiert
sie direkt, ohne umzuschalten (PauseSource) - z.B. Spotify stummschalten, während
YouTube weiterläuft. Bei nur einer aktiven Quelle bleibt die Leiste leer/unsichtbar.
App-Namen werden aus der AppUserModelId grob lesbar gemacht (FriendlyAppName).

## Settings-Fenster im ModernFlyouts-Stil
Nach Vorlage der ModernFlyouts-Einstellungen: Navigationsleiste links (Hamburger klappt
sie ein), rechts je eine Seite. Seiten: Allgemein (Optionen), Personalisierung (Farben/
Design), Layout (Anordnung, Snap-Punkte) | Flyout Module: Audio (Apps/Profile +
Ausschließen), Medien (Quellenwechsel-Taste), Spotify | Info. Theme folgt dem Windows-
System (hell/dunkel, UiHelpers.Light), Checkboxen sind Toggle-Schalter (XAML-Vorlage zur
Laufzeit; bei Fehler Rückfall auf normale CheckBox), dunkle Titelleiste im Dark-Theme.
Inhalte/Logik der alten Abschnitte unverändert (Body() entnimmt sie den Expander-Hüllen).
Speichern weiterhin explizit per Button unten rechts.

## Fix: Firefox erschien in der Quellen-Leiste als Zahlenfolge
Firefox meldet als AppUserModelId einen 16-stelligen Hex-Hash (z.B. 308046B0AF4A39CB), den
Windows nicht auflösen kann. MediaController.FriendlyAppName() erkennt dieses Muster jetzt
und zeigt "Firefox". Außerdem nutzt der Pause-Punkt an den Quellen-Pills jetzt das
Fluent-Icon-Glyph statt U+23F8, das in der Standardschrift als Kästchen dargestellt wurde.

## Media-Flyout nach dem echten ModernFlyouts (Screenshot als Vorlage) + Browser-Name
- Osd/MediaOsd.cs neu: App-Name (+ Logo, falls Windows eins kennt) oben links, Cover oben rechts,
  Titel/Interpret, Fortschrittsleiste mit Zeiten (vergangen links, Gesamt rechts), darunter "…"-
  Menü (Zufallswiedergabe, Wiederholen aus/Titel/Liste), Zurück, Play/Pause im Kreis, Weiter.
  Die Leiste ist anklickbar/ziehbar zum Springen (MediaController.SeekTo, nur wenn die Quelle das
  erlaubt). Die Position läuft lokal weiter (500ms-Takt) und wird alle 2s nachgelesen, statt bei
  jedem Timeline-Event das ganze Flyout neu zu zeigen. Höhe passt sich dem Inhalt an.
- Neue Option (Personalisierung): "Album-Cover als Hintergrund im Media-Flyout" (cfg.media_art_background,
  Standard AUS) - Cover weichgezeichnet + abgedunkelt/aufgehellt hinter der Karte.
- Quellen-Name: FriendlyAppName() erkennt Firefox (feste Hash-ID 308046B0AF4A39CB); andere reine
  Hash-IDs → laufender Browser (nur wenn genau einer läuft), sonst "Browser" statt rohe ID.
  Der Pause-Punkt in den Quellen-Pills nutzt jetzt ein Icon-Font-Symbol (das ⏸-Textzeichen wurde
  auf manchen Systemen als leeres Kästchen dargestellt).

## Fix: Skip funktionierte nur einmal + jede Taste als Hotkey
- Ursache "nur einmal": Quellen wie Firefox melden nach einem Sprung keine neue Position. Die Zeitleiste
  blieb auf dem alten Stand, der nächste Sprung wurde davon aus gerechnet und hob den ersten wieder auf
  (auch die Leiste im Flyout sprang beim 2-s-Nachlesen zurück). MediaController.SeekTo merkt sich jetzt die
  eigene Position (optimistisch, bei abgelehntem Sprung verworfen); ReadTimeline bevorzugt sie, solange die
  Quelle keine spätere LastUpdatedTime meldet.
- Hotkeys: HotkeyParser.NameForVk/Parse unterstützen jetzt JEDE Taste (Pos1/Ende/…, F13–F24, Oem-Tasten,
  Medien-/Browser-Tasten, Apps-Taste): bekannte Namen aus der festen Liste, sonst WPF-Tastenname in
  Kleinbuchstaben (z.B. "oemplus", "f13"), beim Einlesen zurückübersetzt. Gilt für alle Hotkey-Felder,
  auch den Profil-Hotkey.

## 10 Sekunden vor-/zurückspringen (Flyout-Knöpfe + zwei Hotkeys)
- MediaController.SkipBy(±10 s): rechnet die aktuelle Position aus der Zeitleiste hoch (inkl. seit dem
  letzten Update vergangener Zeit beim Spielen), begrenzt auf 0..Dauer, springt per SeekTo und lässt
  das Flyout kurz danach aktualisieren. Wirkt auf die im Flyout gewählte Quelle.
- Flyout: ±10-s-Knöpfe (Symbole SkipBack10/SkipForward10) sitzen direkt links/rechts von Play/Pause
  (Reihenfolge: Zurück | −10 | Play/Pause | +10 | Weiter), die Anzeige springt sofort mit; ausgegraut,
  wenn die Quelle kein Springen erlaubt. Abschaltbar: cfg.media_skip_buttons (Standard an).
- Hotkeys: cfg.media_skip_forward_hotkey / cfg.media_skip_back_hotkey (leer = aus). Im Hook
  (LowLevelKeyboardHook, Event MediaSkipRequested ±1) wie der Profil-Hotkey geparst, geblockt, einmal
  pro Tastendruck ausgelöst (kein Dauerfeuer beim Halten). Funktionieren auch bei ausgeblendetem Flyout.
- Settings → Medien: Schalter für die Knöpfe + zwei Hotkey-Felder mit Aufnahme per Tastendruck und ✕
  zum Entfernen (SettingsWindow.BuildHotkeyCapture, wiederverwendbar).

## Media-Flyout: an/aus und Anzeigedauer einstellbar
Settings → Medien → Media-Flyout: Schalter "Media-Flyout anzeigen" (cfg.media_enabled, Standard an)
und Slider "Anzeigedauer" 2–30 s (cfg.media_duration_ms, Standard 6000). Aus = das Flyout erscheint
nie (App.xaml.cs prüft das im Changed-Handler); der Quellenwechsel per Taste läuft trotzdem. Die
Dauer gilt ab der letzten Aktualisierung bzw. Bedienung im Flyout (MediaOsd.ResetHideTimer), das
Ausblenden wartet weiterhin, solange gesprungen wird oder das "…"-Menü offen ist.

## Fix: Cover-Hintergrund zog das Media-Flyout über den ganzen Bildschirm
Das unscharfe Hintergrundbild lag direkt im Grid; seine Pixelgröße wurde als Wunschgröße gemeldet,
und weil MediaOsd.SizeToContent=Height nutzt, wuchs das Fenster auf Bildhöhe (+Rand). Jetzt liegt
das Bild auf einem Canvas (meldet nie eine Wunschgröße); Breite/Höhe werden in host.SizeChanged
auf Kartengröße + 80px Überstand gesetzt (Überstand wird vom runden Clip beschnitten).

## Fix: Logo oben links im Media-Flyout
Das Logo kam bisher nur über Windows' AppInfo (nur Store-Apps) - Browser wie Firefox bekamen das
Standard-Notensymbol, Spotify ein Kachel-Logo mit Hintergrundfläche. MediaController.
TryGetAppLogoAsync nimmt jetzt zuerst das Icon der laufenden Programmdatei (Prozess über .exe-Name,
bekannten Namen zum Anzeigenamen oder Teile der AppId, Icon per ExtractAssociatedIcon → PNG), erst
danach das Store-Logo. Treffer werden pro AppId zwischengespeichert. Ohne Treffer bleibt das
Standardsymbol.

## Media-Flyout: Fortschrittsleiste und "…"-Knopf einzeln abschaltbar
Settings → Medien → Media-Flyout: zwei Schalter (cfg.media_show_progress / cfg.media_show_more,
beide Standard AN). MediaOsd liest sie bei jedem Aktualisieren: aus = Leiste samt Zeiten bzw.
"…"-Knopf wird ausgeblendet, die Karte schrumpft entsprechend (Höhe passt sich dem Inhalt an).

## Farbschema umschaltbar: Systemstandard / Hell / Dunkel
Neu: cfg.theme ("system" Standard | "light" | "dark"), Auswahl unter Personalisierung →
Farbschema. Config/ThemeHelper.IsLight(cfg) wird von Settings-Fenster (UiHelpers.SetTheme),
modernen OSD-Karten (OsdCard) und Media-Flyout (MediaOsd, ThemeChanged → wird bei Wechsel
neu aufgebaut) genutzt. Beim Wechsel wird nur der Theme-Wert sofort gespeichert; das Settings-
Fenster ersetzt sich durch ein neues mit derselben (auch ungespeicherten) Konfiguration, Position
und Seite (SettingsWindow.ReopenWithTheme / Replaced → App.AttachSettings). Das "dark"-OSD-Design
nutzt weiter die frei einstellbaren Farben und ist vom Farbschema nicht betroffen.

## Settings: eigene Stile statt weißer Windows-Standard-Controls
Settings/Theme.xaml (ResourceDictionary, wird in SettingsWindow.ApplyControlTheme() geladen)
restylt Scrollbar (dünner, abgerundeter Griff), Dropdown (Pfeil + Liste), Expander-Pfeile,
TabControl (Unterstrich), Slider (Akzent-Füllung, runder Griff), Buttons (Hover/Klick per
Deckkraft, damit eigene Farben bleiben), ListBox-Auswahl und das Kontextmenü. Farben kommen
als DynamicResource (ThemeFg/-Field/-Border/-Hover/-Pressed/-Accent/-Thumb/-Track) aus
UiHelpers, also hell/dunkel mit denselben Stilen. Schlägt das Laden fehl, bleibt der
Standardlook (Eintrag in knob_service.log). Betrifft nur das Settings-Fenster.

## Fix: Umschalten spielte die neue Quelle immer noch nicht zuverlässig ab
Reines Pausieren der alten Quelle hat nicht gereicht, um Windows' globale Medientaste
zuverlässig umzulenken - die ist ein nicht an eine App gebundener Tastendruck, Windows
entscheidet selbst, wen er trifft. SwitchTo() ruft jetzt zusätzlich TryPlayAsync() direkt
auf der neu gewählten Session auf - derselbe gezielte Weg, den auch der Play/Pause-
Button im Flyout selbst nutzt, quasi ein virtueller Druck auf dessen Play-Button für die
neue Quelle, statt sich auf die mehrdeutige globale Taste zu verlassen.

## Fix: Nach Quellenwechsel spielte die physische Medientaste die falsche App
Ursache: Unser Umschalten (Pill-Klick oder Langdruck) änderte nur, welche Session
UNSERE UI anzeigt/steuert - welche App die PHYSISCHE Medientaste anspricht, entscheidet
Windows komplett selbst anhand seiner eigenen "zuletzt aktiv"-Einschätzung, unabhängig
von unserer Auswahl. SwitchTo() pausiert jetzt die zuvor aktive Quelle automatisch,
damit die neu gewählte eindeutig die "zuletzt aktive" wird und die physische Taste sie
korrekt trifft. Deckt sich auch mit dem ursprünglichen Wunsch: Umschalten pausiert die
andere Quelle. Der Play/Pause-Button im Flyout selbst war von diesem Bug nicht
betroffen (ruft direkt auf der ausgewählten Session auf), bleibt aber konsistent dazu.

## Fix: Flyout verschwand beim Pausieren + neue Einstellung "Taste lange halten = Quelle wechseln"
Windows meldet oft gar keine "aktuelle" GSMTC-Session mehr, sobald wirklich ALLES
pausiert ist (z.B. nur Spotify läuft und wird pausiert) - MediaController._session
fiel dadurch auf null, das Flyout verschwand. Rehook() nimmt jetzt ersatzweise die
erste verfügbare Session aus GetSessions(), statt sessionless zu bleiben.

Neue Einstellung (Settings → Optionen): "Medien-Play/Pause-Taste lange halten wechselt
die Medienquelle". Kurz drücken bleibt normales Play/Pause (Taste wird nicht geblockt,
Windows verarbeitet sie normal weiter); ab der eingestellten Haltedauer (Standard 600ms)
feuert LowLevelKeyboardHook.MediaSourceSwitchRequested, App.xaml.cs ruft dann
MediaController.CycleSource() auf (reiht einmal zur nächsten Quelle weiter, wie ein
Klick auf den nächsten Pill in der Quellen-Leiste). Standardmäßig deaktiviert. Da die
Taste nicht geblockt wird, kann bei einem langen Druck zusätzlich einmal normales
Play/Pause über Windows selbst auftreten - das ist eine bewusste Abwägung, um die
Taste nicht künstlich über SendInput nachzubauen.

## Fix: Media-Buttons reagierten gar nicht (im "Getrennt"-Modus)
Ursache: Alle Buttons (Zurück/Play-Pause/Weiter, Quellen-Pills, Pause-Punkt) reagierten
nur auf MouseLeftButtonUp, nicht auf Down. Im "separate"-Layout fängt das Fenster bei
JEDEM Mausklick zuerst per CaptureMouse() die Maus für den Zieh-Vorgang ab (sobald
MouseLeftButtonDown das Fenster erreicht) - dadurch landete das Loslassen beim Fenster
statt beim Button, und der Klick lief ins Leere. Jetzt setzen alle Buttons e.Handled
bereits bei MouseLeftButtonDown, sodass das Fenster-Drag sie gar nicht erst sieht.

## Fix: Quellen-Leiste – Umschalten griff nicht, Name war eine rohe ID
Zwei Ursachen: CurrentSessionChanged hat die manuelle Auswahl sofort wieder
überschrieben, sobald Windows kurz eine andere Session als "aktuell" meldete (z.B.
weil ein Video kurz anspielte) - SwitchTo() wirkte dadurch wie "tut nichts". Jetzt
greift die Automatik nur noch ein, wenn die gewählte Quelle wirklich weg ist
(IsStillValid-Prüfung gegen GetSessions()), eine manuelle Auswahl bleibt sonst stehen.

FriendlyAppName() nutzt jetzt zuerst Windows' eigene AppInfo-Namensauflösung (echter
Anzeigename), dann den .exe-Dateinamen für klassische Win32-Apps, erst danach die
alte Text-Heuristik, und zeigt nie mehr eine lange Zahlenfolge unbeschnitten an.

## Kein UAC-Dialog mehr beim Start
app.manifest: requestedExecutionLevel von "requireAdministrator" auf "asInvoker"
geändert. Admin war aus dem Python-Original übernommen, wird aber für nichts hier
gebraucht (WASAPI, Keyboard-Hook, GSMTC, Spotify-API, Tray laufen alle ohne). Einzige
Einschränkung: Der Low-Level-Keyboard-Hook bekommt wegen UIPI keine Tastendrücke,
solange ein ALS ADMINISTRATOR laufendes Fenster im Vordergrund ist (z.B. ein Installer) -
der Knob reagiert dann kurz nicht, bis der Vordergrund wechselt. Alles andere bleibt
unberührt. Falls Admin doch wieder gebraucht wird: level wieder auf
"requireAdministrator" zurückstellen.

## Fix: Position sprang beim Design-Wechsel (dark ↔ modern)
Ursache: "modern" war oben verankert, "dark" unten - derselbe gespeicherte osd_y-Wert
bedeutete je nach Design etwas anderes. Jetzt sind BEIDE Designs oben verankert
(OsdManager.Restack, OnCardDragged, SettingsWindow.CurrentVolumeAnchor vereinheitlicht,
IsTopAnchored-Verzweigung entfernt). Nebeneffekt: Auch "dark" hat jetzt Standard oben
rechts statt unten rechts und wächst nach unten statt nach oben - war vorher nicht
Teil der Anfrage, aber nötig, damit osd_y designübergreifend dieselbe Stelle meint.

## Snap-Punkte: ein Button speichert jetzt beide Positionen
SnapPosition hat jetzt optionale media_x/media_y (Config/AppConfig.cs). EIN Button
"+ Aktuelle Position(en) übernehmen" speichert bei "Getrennt" Lautstärke- UND Media-
Position in einem Eintrag; bei "Zusammen" nur die Lautstärke-Position (Media folgt ihr ja
automatisch). Hotkey-Zyklus (CycleSnapPosition) bewegt bei "Getrennt" jetzt beide
Flyouts zusammen, wenn der Punkt eine Media-Position hat. Ziehen des Media-Fensters
rastet jetzt vorrangig in dessen gespeicherte Media-Position ein (Fallback: Lautstärke-
Position desselben Punkts).

## Fix: Snap-Punkte erfassten nur den veralteten Lautstärke-Anker
Zwei Lücken: Die X/Y-Felder zeigten nur den Stand von beim Öffnen des Settings-
Fensters (nicht live), und es gab überhaupt keinen Weg, die Media-Position zu
erfassen - nur den Lautstärke-Anker. Jetzt zwei Buttons, beide lesen IMMER frisch
von der Festplatte (ConfigManager.Load(), nicht die Textfelder): "+ Lautstärke-
Position übernehmen" und "+ Media-Position übernehmen". Beide berechnen bei noch nie
gezogenen OSDs auch korrekt die tatsächliche Standardecke (CurrentVolumeAnchor/
CurrentMediaAnchor in SettingsWindow.cs, exakt dieselben Formeln wie OsdManager/
MediaOsd), statt bei leeren Feldern einfach nichts zu tun.

## Fix: Media-Flyout-Positionierung (3 zusammenhängende Bugs)
Ursache für alle drei: ApplyPosition() wurde bei JEDER GSMTC-Metadaten-Änderung
(Titelwechsel, Play/Pause, ...) neu aufgerufen, nicht nur bei echten Stapel-Änderungen.
- "Getrennt" ließ sich nicht ziehen: OnMouseDown verlangte zusätzlich den "verschiebbar"-
  Haken (SnapEnabled), der eigentlich nur die Lautstärke-Karten betrifft. Jetzt ist Ziehen
  in "separate" immer möglich, unabhängig vom Haken.
- "Zusammen" rückte nach, wenn die Lautstärke-Karte verschwand: GetCombinedMediaPosition()
  liefert jetzt null, wenn der Stapel leer ist, ApplyPosition lässt die Karte dann an Ort
  und Stelle statt sie an den bloßen Anker zu ziehen.
- Ziehen sprang zurück: Die Position wurde bei jeder Metadaten-Änderung neu gesetzt - lief
  so ein GSMTC-Event mitten im Ziehen auf, riss das die Karte auf die zuletzt gespeicherte
  Position zurück. ApplyPosition läuft jetzt nur noch beim ersten Erscheinen und bei
  echten Stapel-Änderungen (StackChanged), nicht mehr bei jedem Titel-/Play-Pause-Update.
  Zusätzlich reagiert StackChanged im "separate"-Modus jetzt gar nicht mehr auf
  Lautstärke-Stapel-Events. Speichern beim Loslassen war schon vorher sofort (OnMouseUp).

## Fix: Snap-Hotkey (Langdruck) funktionierte nie
Der eigentliche Langdruck-Watcher aus _wait_long_press() fehlte komplett im Hook –
es gab nur den Kurzdruck-Profilwechsel. Jetzt per DispatcherTimer ergänzt
(LowLevelKeyboardHook.StartLongPressWatch): hält man den Profil-Hotkey >= snap_hold_ms,
feuert SnapCycleRequested (erst einmal, danach alle max(300, snap_hold_ms/2)ms weiter,
solange gehalten). App.xaml.cs.CycleSnapPosition() zyklt durch cfg.snap_positions,
setzt osd_x/osd_y darauf und zeigt eine "📍 Position X/Y"-Karte (OsdCard.CreateSnapInfo,
nutzt die bislang ungenutzten color_snap_border/color_snap_text). Ist Snap aus oder
keine Positionen gespeichert, passiert bei Langdruck nichts (wie im Original) – aber es
wird auch kein Profilwechsel mehr ausgelöst.

## Hotkey per Tastendruck aufnehmen
Settings → Optionen → Profil-Hotkey: Feld ist jetzt read-only, Klick rein startet die
Aufnahme ("Taste(n) drücken …"). Nächste gedrückte Tastenkombination (inkl. Ctrl/Alt/
Shift/Win) wird automatisch als Hotkey übernommen (HotkeyParser.NameForVk – Rückwärts-
Zuordnung VK-Code → Name). Esc bricht ab, Fokus verlieren ohne gültige Taste stellt den
alten Wert wieder her.

## ModernFlyouts-Look übernommen (Screenshot des Originals als Vorlage)
- Lautstärke-Karte (modern): Icon – Slider – Zahl in einer Reihe, festes Blau (#3b82f6)
  statt Systemfarbe, dunkleres rundes Karten-Design (Radius 12, #2b2b30).
- Media-Flyout (Osd/MediaOsd.cs): Cover, Titel/Interpret, runde Zurück/Play-Pause/
  Weiter-Buttons – direkt an den ModernFlyouts-Screenshot angelehnt.
- Standard-Position für das moderne Design ist jetzt oben rechts (statt unten rechts),
  wächst nach unten. Das Dark-Design bleibt unverändert unten rechts, nach oben wachsend.
- Neue Einstellung "Media-Flyout-Anordnung" (OSD-Positionen): "Zusammen" (Standard) –
  Media hängt direkt unter dem Lautstärke-Stapel und folgt ihm automatisch; "Getrennt" –
  Media hat eine eigene Position (Standard oben mittig), bei aktiviertem "verschiebbar"
  separat per Maus ziehbar inkl. Einrasten in Snap-Positionen.

## ModernFlyouts-Features: Media-Flyout & Lock-Keys
- Media/MediaController.cs: nutzt die Windows-GSMTC-API (Windows.Media.Control),
  dieselbe Schnittstelle, über die auch ModernFlyouts sein Media-Flyout speist.
  Liefert Titel/Interpret/Cover der aktiven Wiedergabe-App und erlaubt Play/Pause/
  Weiter/Zurück.
- Osd/MediaOsd.cs: eigenes, klickbares Fenster oben mittig auf dem Bildschirm (wie
  ModernFlyouts' Standardposition), erscheint automatisch bei Titel-/Wiedergabewechsel,
  blendet nach 5s ohne Interaktion aus.
- Lock-Keys-OSD (Caps/Num/Scroll Lock): LowLevelKeyboardHook meldet jetzt auch diese
  Tasten (nicht blockierend – Windows schaltet sie selbst um, wir lesen den neuen
  Zustand danach aus), OsdCard.CreateLockKey zeigt eine kleine AN/AUS-Karte im
  gewohnten Kartenstapel.
- Noch nicht portiert (aus ModernFlyouts): Bildschirmhelligkeit, Flugmodus.

## Spotify API
Echte Web-API-Anbindung statt Windows-Mixer für Spotify (Audio/SpotifyController.cs):
OAuth-Authorization-Code-Flow (Browser öffnet sich, lokaler HttpListener auf
http://127.0.0.1:5000/callback/ fängt den Code ab), Token-Cache als .spotify_cache
neben der exe, automatische Refresh-Token-Erneuerung. Lautstärke läuft wie im
Original: sofort gecachter Wert fürs OSD, echter API-Call 80ms debounced. In
SessionVolumeController.ChangeVolume/ToggleMute läuft jede Session mit "spotify" im
Namen bei aktivierter API über den eigenen Regler (step_spotify) statt WASAPI.
Verbinden/Trennen im Settings-Fenster unter "Spotify API". Redirect-URI muss exakt
so im Spotify-Dashboard hinterlegt sein wie in der App angezeigt (Trailing-Slash!).

## Fix: OSD-Position ging beim Speichern in den Einstellungen verloren
Wahrscheinliche Ursache: War das Settings-Fenster offen, während am OSD gezogen
wurde, überschrieb ein späterer Klick auf "Speichern" die per Drag gesetzte Position
wieder mit dem alten Stand aus dem Moment, als das Fenster geöffnet wurde. "Speichern"
übernimmt jetzt osd_x/osd_y (und active_profile, seen_apps) frisch von der Festplatte,
bevor es schreibt. Zusätzlich loggt die App jetzt bei jedem Drag und bei jeder OSD-
Anzeige den verwendeten Anker (knob_service.log), falls das Problem doch woanders liegt.

## Neue-App-Erkennung
Alle 3s wird geprüft, ob eine Audio-Session mit aktivem Sound auftaucht, die noch
keinem Profil zugewiesen, nicht ausgeschlossen und noch nicht "gesehen" ist
(App.xaml.cs: StartNewAppDetection/CheckForNewApps). Dafür öffnet sich ein
klickbares OSD-Popup (Osd/NewAppOsd.cs, anders als die sonstigen Karten NICHT
klick-durchlässig): ⊘ schließt aus, ein Profil-Icon weist direkt zu. Ohne Klick
blendet es sich nach 12s aus, die App wird dabei als "gesehen" markiert und danach
nicht erneut vorgeschlagen. Mehrere neue Apps werden nacheinander gezeigt.

## Settings-Fenster
Tray → ⚙️ Einstellungen öffnet SettingsWindow.cs. "Apps" zeigt jede Zeile wie im
Original: Name, "● läuft", rechts ⊘ (Ausschließen) + ein Button je Profil, farbig
markiert, wenn die App diesem Profil zugewiesen ist (AppRow.cs, Zuordnung exklusiv
wie im Original). Darunter "Profile im Überblick" mit je einem aufklappbaren Bereich
pro Profil und dessen zugewiesenen Apps. Schritte, Snap-Haltedauer und Deckkraft sind
jetzt Slider statt Textfelder. Icon-Spalten stehen über ein festes Grid (AppRow.cs)
in jeder Zeile exakt untereinander. "Apps" hat zwei Tabs: "Aktive Apps" (nur gerade
Ton abspielende) und "Alle Apps" (mit Suche, wie zuvor). Speichert erst beim Klick auf "Speichern". Alle Bereiche starten eingeklappt. Noch nicht
portiert: Spotify-OAuth-Dialog, echte Farbwähler (aktuell Hex-Textfelder), Drag zum
Setzen von Snap-Punkten (nur manuelle X/Y-Eingabe).

## Noch nicht portiert
- OSD-Interaktion (Drag, ⊘ Ausschließen, Profil-Icons, Assign-Karten), Windows-Design, Snap-Positionen
- Spotify-API-Lautstärke
- Settings-Fenster (7 Collapsible-Sections)
- ModernFlyouts-Design/-Code
