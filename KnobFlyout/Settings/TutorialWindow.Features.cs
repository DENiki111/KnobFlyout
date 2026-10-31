using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using KnobFlyout.Config;
using KnobFlyout.Osd;
using KnobFlyout.Profiles;
using static KnobFlyout.Loc;
using static KnobFlyout.Settings.UiHelpers;

namespace KnobFlyout.Settings
{
    /// <summary>
    /// „Alle Funktionen“: Übersicht über alles, was KnobFlyout kann, nach Themen. Jede Funktion hat eine eigene Unterseite
    /// mit lebender Vorschau, kurzer Erklärung und den passenden Einstellungen. „Weiter“ führt zur nächsten Funktion.
    /// </summary>
    public sealed partial class TutorialWindow
    {
        private sealed record FeatureItem(Func<string> Group, string Glyph, Func<string> Short, Page Page);

        private List<FeatureItem>? _features;
        private string _catalogGroup = "";

        // Live-Rückmeldungen auf den Funktions-Seiten
        private StatusPill? _lockPill, _skipPill, _gesturePill;
        private Border? _lockHost;

        public void OnLockKey(string name, bool on)
        {
            if (_lockHost != null) _lockHost.Child = OsdCard.CreateLockPreview(Cfg(), name, on);
            _lockPill?.Done(T($"Erkannt: {LockName(name)} ist {(on ? "an" : "aus")}", $"Detected: {LockName(name)} is {(on ? "on" : "off")}"));
        }

        private static string LockName(string name) => name switch
        {
            "CapsLock" => T("Feststelltaste", "Caps Lock"),
            "NumLock" => "Num Lock",
            "ScrollLock" => T("Rollen", "Scroll Lock"),
            _ => name,
        };

        public void OnSkipKey(int dir) =>
            _skipPill?.Done(dir > 0 ? T("Klappt: 10 Sekunden vor", "Works: 10 seconds forward") : T("Klappt: 10 Sekunden zurück", "Works: 10 seconds back"));

        public void OnMediaGesture(string what) =>
            _gesturePill?.Done(what == "next" ? T("Klappt: nächster Titel", "Works: next track") : T("Klappt: Quelle gewechselt", "Works: source switched"));

        // ── Übersicht ───────────────────────────────────────────────────────

        private Page CatalogPage() => new("#3b82f6", () => T("Alle Funktionen", "All features"),
            () => T("Alles, was KnobFlyout kann. Klick auf eine Funktion erklärt sie, und du kannst sie gleich ausprobieren und einstellen.",
                "Everything KnobFlyout can do. Click a feature to see it explained and to try and set it up right away."),
            () => StageLive(), BodyCatalog)
        {
            Crumb = () => T("Alle Funktionen", "All features"),
            NextText = () => T("Zum Programm", "Go to the program"),
            Next = Close,
        };

        /// <summary>Themen als Pillen, darunter nur die Funktionen des gewählten Themas: passt ohne Scrollen ins Fenster.</summary>
        private void BodyCatalog(StackPanel body)
        {
            var features = Features();
            string settings = T("Einstellungen", "Settings");
            var groups = features.Select(f => f.Group()).Distinct().Append(settings).ToList();
            if (!groups.Contains(_catalogGroup)) _catalogGroup = groups[0];

            var chips = new WrapPanel { Margin = new Thickness(0, 0, -6, 6) };
            foreach (var group in groups)
            {
                string chosen = group;
                var chip = Chip(group, group == _catalogGroup, () =>
                {
                    _catalogGroup = chosen;
                    RefreshPage();
                });
                chip.Margin = new Thickness(0, 0, 6, 6);
                chips.Children.Add(chip);
            }
            body.Children.Add(chips);

            var rows = new StackPanel();
            if (_catalogGroup == settings)
            {
                rows.Children.Add(LinkRow("\uE765", T("Alle Tastenkürzel", "All shortcuts"), T("Alle Tasten und Zeiten an einem Ort", "All keys and times in one place"), "hotkeys"));
                rows.Children.Add(LinkRow("\uE713", T("Alle Einstellungen", "All settings"), T("Einführung schließen und dort weitermachen", "Close the introduction and continue there"), "general"));
            }
            else
            {
                foreach (var item in features.Where(f => f.Group() == _catalogGroup))
                {
                    int index = features.IndexOf(item);
                    rows.Children.Add(ActionRow(item.Glyph, item.Page.Title(), item.Short(), () => OpenSub(FeatureDetail(index))));
                }
            }
            body.Children.Add(new Border
            {
                Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4), Child = rows,
            });
        }

        /// <summary>Funktion als Unterseite: „Weiter“ blättert zur nächsten, „Übersicht“ führt zurück.</summary>
        private Page FeatureDetail(int index)
        {
            var features = Features();
            bool last = index >= features.Count - 1;
            _catalogGroup = features[index].Group(); // zurück in der Übersicht: dieses Thema
            return features[index].Page with
            {
                Crumb = () => T($"Alle Funktionen  ›  {index + 1} von {features.Count}", $"All features  ›  {index + 1} of {features.Count}"),
                BackText = () => T("Übersicht", "Overview"),
                NextText = () => last ? T("Zum Programm", "Go to the program") : T("Nächste Funktion", "Next feature"),
                Next = () =>
                {
                    if (last) Close();
                    else ReplaceSub(FeatureDetail(index + 1));
                },
            };
        }

        private List<FeatureItem> Features() => _features ??= BuildFeatures();

        private List<FeatureItem> BuildFeatures()
        {
            string Volume() => T("Lautstärke", "Volume");
            string Profiles() => T("Profile und Apps", "Profiles and apps");
            string Keys() => T("Tasten", "Keys");
            string Osd() => "OSD";
            string Flyout() => T("Media-Flyout", "Media flyout");
            string Looks() => T("Aussehen", "Appearance");
            string Place() => T("Platz auf dem Bildschirm", "Place on screen");
            string Program() => T("Programm", "Program");

            FeatureItem Item(Func<string> group, string glyph, string hue, Func<string> title, Func<string> lead, Func<string> brief, Func<FrameworkElement> stage, Action<StackPanel> body) =>
                new(group, glyph, brief, new Page(hue, title, lead, stage, body));

            // Schritt der Einführung noch einmal als Funktion
            FeatureItem Step(Func<string> group, string glyph, int page, Func<string> brief) => new(group, glyph, brief, _pages[page]);

            return new List<FeatureItem>
            {
                // ── Lautstärke ──
                Item(Volume, "\uE767", "#0ea5e9", () => T("Drehen, drücken, Schrittweite", "Turn, press, step size"),
                    () => T("Eine Raste am Knopf ändert die Lautstärke um die Schrittweite, Drücken schaltet stumm. Spotify über die API hat eine eigene Schrittweite.",
                        "One notch changes the volume by the step size, pressing mutes. Spotify via the API has its own step size."),
                    () => T("Wie fein der Knopf regelt", "How finely the knob controls"),
                    () => StageLive(media: false), BodyKnobSteps),
                Item(Volume, "\uE8AB", "#a855f7", () => T("Welche Apps der Knopf regelt", "Which apps the knob controls"),
                    () => T("Nur die Apps des aktiven Profils, die gerade Ton abspielen, oder alle Apps des Profils, auch wenn sie still sind.",
                        "Only the apps in the active profile that are playing sound, or all apps in the profile, even when they're silent."),
                    () => T("Nur Apps mit Ton oder alle, leere Profile überspringen", "Only apps playing sound or all, skip empty profiles"),
                    StageProfiles, body =>
                    {
                        _profilePill = new StatusPill(T("Klick links auf ein Profil, dann dreh am Knopf …", "Click a profile on the left, then turn your knob …"));
                        body.Children.Add(_profilePill);
                        AddProfileModeCards(body);
                    }),
                Item(Volume, "\uE8D6", "#22c55e", () => T("Spotify direkt regeln", "Control Spotify directly"),
                    () => T("Ohne API regelt KnobFlyout Spotify wie jede App im Windows-Mixer. Mit der Spotify-API bewegt sich stattdessen Spotifys eigener Regler, auch in der App.",
                        "Without the API, KnobFlyout controls Spotify like any app in the Windows mixer. With the Spotify API, Spotify's own slider moves instead, in the app too."),
                    () => T("Spotifys eigener Regler über die Web-API", "Spotify's own slider via the Web API"),
                    () => StageLive(media: false), body =>
                    {
                        body.Children.Add(Feature("\uE8D6", T("Was du brauchst", "What you need"), T("Spotify Premium und eine kostenlose App im Spotify-Developer-Dashboard. Die Einstellungen führen dich Schritt für Schritt durch.", "Spotify Premium and a free app in the Spotify Developer Dashboard. The settings walk you through it step by step.")));
                        AddLink(body, "\uE8D6", T("Spotify einrichten", "Set up Spotify"), T("Öffnet die Einstellungen auf der Seite „Spotify“", "Opens the settings on the “Spotify” page"), "spotify");
                    }),
                Item(Volume, "\uE714", "#ef4444", () => T("YouTube im Browser regeln", "Control YouTube in the browser"),
                    () => T("Mit der Browser-Erweiterung regelt der Knopf den Player von YouTube statt des ganzen Browsers. Andere Tabs bleiben gleich laut.",
                        "With the browser extension, the knob controls the YouTube player instead of the whole browser. Other tabs stay as loud as they are."),
                    () => T("Nur das Video statt den ganzen Browser", "Just the video instead of the whole browser"),
                    () => StageLive(media: false), body =>
                    {
                        body.Children.Add(SettingCard("\uE714", T("Lautstärke im YouTube-Player regeln", "Control volume in the YouTube player"), T("Firefox, Chrome und Opera", "Firefox, Chrome and Opera"),
                            Toggle(Cfg().YoutubeBridge, v => Update(c => c.YoutubeBridge = v))));
                        body.Children.Add(new Border { Height = 6 });
                        body.Children.Add(Note(T("Spielt kein YouTube-Video, regelt der Knopf wie gewohnt den Browser. Die Erweiterung installierst du einmalig.",
                            "If no YouTube video is playing, the knob controls the browser as usual. You install the extension once.")));
                        AddLink(body, "\uE714", T("Erweiterung einrichten", "Set up the extension"), T("Öffnet die Einstellungen auf der Seite „YouTube“", "Opens the settings on the “YouTube” page"), "youtube");
                    }),

                // ── Profile und Apps ──
                Step(Profiles, "\uE716", 4, () => T("Name, Logo, Farbe, anlegen und löschen", "Name, logo, color, create and delete")),
                Item(Profiles, "\uE765", "#22c55e", () => T("Zuweisen per Taste", "Assign by key"),
                    () => T("Gib jedem Profil eine Taste. Erscheint das Neue-App-Popup, weist ein Druck auf diese Taste die App zu, ohne die Maus anzufassen, auch im Spiel. Außerhalb des Popups bleibt die Taste frei.",
                        "Give each profile a key. When the new-app popup shows up, pressing that key assigns the app without touching the mouse, even in a game. Outside the popup the key stays free."),
                    () => T("Neue Apps zuweisen, ohne die Maus anzufassen", "Assign new apps without touching the mouse"),
                    StageApps, BodyAssignKeys),
                Item(Profiles, "\uE8CB", "#22c55e", () => T("Apps automatisch einsortieren", "Sort apps automatically"),
                    () => T("KnobFlyout erkennt Spiele (Steam, Epic, GOG, Xbox, die Spieleliste von Windows), Browser und Video-Player, Musik-Apps und Chat-Apps und legt sie ohne Nachfrage ins passende Profil. Was es nicht kennt, fragt wie gewohnt das Popup.",
                        "KnobFlyout recognizes games (Steam, Epic, GOG, Xbox, the Windows game list), browsers and video players, music apps and chat apps and puts them in the right profile without asking. Anything it doesn't know is still asked about in the popup."),
                    () => T("Spiele, Browser, Musik und Chat-Apps ohne Nachfrage", "Games, browsers, music and chat apps without asking"),
                    StageApps, body =>
                    {
                        _appsPill = new StatusPill(T("Schalte es ein und starte eine App …", "Turn it on and start an app …"));
                        body.Children.Add(_appsPill);
                        AddAutoAssignCards(body);
                        body.Children.Add(new Border { Height = 6 });
                        body.Children.Add(Hint(T("Gilt nur für neue Apps. Bereits zugewiesene bleiben, wo sie sind; ändern kannst du alles unter Einstellungen → Profile & Apps.",
                            "Only applies to new apps. Assigned ones stay where they are; you can change everything under Settings → Profiles & apps.")));
                    }),
                Item(Profiles, "\uE733", "#64748b", () => T("Neue und ignorierte Apps", "New and ignored apps"),
                    () => T("Spielt eine unbekannte App Ton ab, fragt das Popup nach. Ohne Antwort verschwindet es nach 12 Sekunden und fragt später wieder. ⊘ ignoriert eine App dauerhaft.",
                        "When an unknown app plays sound, the popup asks. Without an answer it disappears after 12 seconds and asks again later. ⊘ ignores an app for good."),
                    () => T("Popup bei neuen Apps, Apps dauerhaft ignorieren", "Popup for new apps, ignore apps for good"),
                    StageApps, BodyExcluded),

                // ── Tasten ──
                Item(Keys, "\uE72E", "#eab308", () => T("Feststelltasten", "Lock keys"),
                    () => T("Feststelltaste, Num Lock und Rollen zeigen im OSD, ob sie gerade an oder aus sind.",
                        "Caps Lock, Num Lock and Scroll Lock show in the OSD whether they are on or off."),
                    () => T("Caps, Num und Scroll Lock im OSD", "Caps, Num and Scroll Lock in the OSD"),
                    StageLock, body =>
                    {
                        _lockPill = new StatusPill(T("Probier es aus: Drück die Feststelltaste …", "Try it: press Caps Lock …"));
                        body.Children.Add(_lockPill);
                        body.Children.Add(SwitchCard("\uE72E", T("Feststelltasten anzeigen", "Show lock keys"), T("Auch unter Einstellungen → Allgemein", "Also under Settings → General"), Cfg().LockKeysOsd, (c, v) => c.LockKeysOsd = v));
                    }),
                Item(Keys, "\uE893", "#14b8a6", () => T("Vor- und Zurückspulen", "Skip forward and back"),
                    () => T("Zwei eigene Tasten springen im laufenden Titel 10 Sekunden vor oder zurück, in jeder App, die das zulässt.",
                        "Two keys of your own jump 10 seconds forward or back in the current track, in any app that allows it."),
                    () => T("Zwei Tasten für ±10 Sekunden", "Two keys for ±10 seconds"),
                    () => StageLive(osd: false), BodySkipKeys),
                Item(Keys, "\uE8AB", "#14b8a6", () => T("Mediengesten", "Media gestures"),
                    () => T("Zweimal schnell drücken oder lange halten: Damit springst du zum nächsten Titel oder wechselst die Quelle. Das geht mit der Play/Pause-Taste oder einer eigenen.",
                        "Press twice quickly or hold: that jumps to the next track or switches the source. It works with the Play/Pause key or one of your own."),
                    () => T("Nächster Titel und Quelle wechseln per Geste", "Next track and switch source by gesture"),
                    () => StageLive(osd: false), BodyGestures),

                // ── OSD ──
                Item(Osd, "\uE7F4", "#3b82f6", () => T("OSD: anzeigen und wegklicken", "OSD: show and dismiss"),
                    () => T("Das OSD erscheint beim Drehen und Profilwechsel und verschwindet von selbst. Wie lange, stellst du hier ein. Dreh ruhig am Knopf, um es zu sehen.",
                        "The OSD appears when you turn the knob or switch profiles and disappears on its own. You set how long here. Turn your knob to see it."),
                    () => T("Ein/Aus, Anzeigedauer, per Klick ausblenden", "On/off, duration, click to dismiss"),
                    () => StageLive(media: false), body =>
                    {
                        var cfg = Cfg();
                        body.Children.Add(SwitchCard("\uE7F4", T("OSD anzeigen", "Show OSD"), null, cfg.Osd, (c, v) => c.Osd = v));
                        body.Children.Add(SliderCard("\uE916", T("Anzeigedauer", "Duration"), null, 500, 10000, cfg.OsdDuration, "0' ms'", (c, v) => c.OsdDuration = v, 100));
                        body.Children.Add(SwitchCard("\uE8BB", T("Per Klick ausblenden", "Click to dismiss"), T("Klick auf OSD oder Flyout blendet es sofort aus", "Clicking the OSD or flyout hides it right away"), cfg.ClickToDismiss, (c, v) => c.ClickToDismiss = v));
                    }),
                Item(Osd, "\uE706", "#6366f1", () => T("Deckkraft", "Opacity"),
                    () => T("Hintergrund sowie Text und Symbole lassen sich getrennt einstellen, bei allen Designs. Ganz ohne Hintergrund bleiben nur Text und Symbole sichtbar.",
                        "The background and the text and icons can be set separately, in every design. With no background at all, only text and icons remain."),
                    () => T("Hintergrund und Text getrennt, für OSD und Flyout", "Background and text separately, for OSD and flyout"),
                    () => StageLive(), body =>
                    {
                        var cfg = Cfg();
                        var d = new AppConfig();
                        body.Children.Add(Heading("OSD"));
                        body.Children.Add(SliderCard("\uE706", T("Hintergrund", "Background"), null, 0, 100, Math.Round(cfg.WinAlpha * 100), "0' %'", (c, v) => c.WinAlpha = v / 100.0, 1, Math.Round(d.WinAlpha * 100)));
                        body.Children.Add(SliderCard("\uE8D2", T("Text und Symbole", "Text and icons"), null, 20, 100, Math.Clamp(cfg.OsdContentOpacity, 20, 100), "0' %'", (c, v) => c.OsdContentOpacity = v, 1, d.OsdContentOpacity));
                        body.Children.Add(new Border { Height = 10 });
                        body.Children.Add(Heading(T("Media-Flyout", "Media flyout")));
                        body.Children.Add(SliderCard("\uE706", T("Hintergrund", "Background"), null, 0, 100, Math.Clamp(cfg.MediaOpacity, 0, 100), "0' %'", (c, v) => c.MediaOpacity = v, 1, d.MediaOpacity));
                        body.Children.Add(SliderCard("\uE8D2", T("Text und Symbole", "Text and icons"), null, 20, 100, Math.Clamp(cfg.MediaContentOpacity, 20, 100), "0' %'", (c, v) => c.MediaContentOpacity = v, 1, d.MediaContentOpacity));
                    }),

                // ── Media-Flyout ──
                Item(Flyout, "\uE8D6", "#14b8a6", () => T("Flyout: Inhalt und Dauer", "Flyout: content and duration"),
                    () => T("Lege fest, ob und wie lange das Flyout erscheint und was es zeigt. Links siehst du jede Änderung sofort.",
                        "Decide whether and how long the flyout appears and what it shows. You see every change on the left right away."),
                    () => T("Ein/Aus, Dauer, Zeitleiste, ±10-s-Knöpfe", "On/off, duration, timeline, ±10 s buttons"),
                    () => StageLive(osd: false), body =>
                    {
                        var cfg = Cfg();
                        body.Children.Add(SwitchCard("\uE8D6", T("Media-Flyout anzeigen", "Show media flyout"), null, cfg.MediaEnabled, (c, v) => c.MediaEnabled = v));
                        body.Children.Add(SliderCard("\uE916", T("Anzeigedauer", "Duration"), null, 2, 30, Math.Clamp(cfg.MediaDurationMs / 1000.0, 2, 30), "0' s'", (c, v) => c.MediaDurationMs = v * 1000));
                        body.Children.Add(SwitchCard("\uE823", T("Zeitleiste", "Timeline"), null, cfg.MediaShowProgress, (c, v) => c.MediaShowProgress = v));
                        body.Children.Add(SwitchCard("\uE893", T("±10-s-Knöpfe", "±10 s buttons"), null, cfg.MediaSkipButtons, (c, v) => c.MediaSkipButtons = v));
                    }),
                Item(Flyout, "\uE718", "#14b8a6", () => T("Flyout dauerhaft anzeigen", "Keep the flyout on screen"),
                    () => T("Bei Programmen deiner Wahl bleibt das Flyout stehen, zum Beispiel solange Spotify spielt. Spielt ein anderes Programm los, zeigt es das kurz an und verschwindet.",
                        "For programs you choose, the flyout stays on screen, for example while Spotify is playing. If another program starts playing, it shows that briefly and disappears."),
                    () => T("Zum Beispiel, solange Spotify spielt", "For example while Spotify is playing"),
                    () => StageLive(osd: false), BodyKeep),
                Item(Flyout, "\uE712", "#14b8a6", () => T("Das „…“-Menü", "The “…” menu"),
                    () => T("Der „…“-Knopf öffnet ein Menü direkt im Flyout. Welche Einträge es zeigt, wählst du selbst. Klick links darauf, um es auszuprobieren.",
                        "The “…” button opens a menu right in the flyout. You choose which items it shows. Click it on the left to try it."),
                    () => T("Zufall, Wiederholen, Position speichern und mehr", "Shuffle, repeat, save position and more"),
                    () => StageLive(osd: false), BodyMoreMenu),
                Item(Flyout, "\uE91B", "#14b8a6", () => T("Cover als Hintergrund", "Album art background"),
                    () => T("Das Cover füllt das ganze Flyout als unscharfer Hintergrund. Unschärfe und Abdunkelung stellst du selbst ein. Am besten siehst du es, wenn gerade Musik läuft.",
                        "The album art fills the whole flyout as a blurred background. You set the blur and dimming yourself. It's easiest to see while music is playing."),
                    () => T("Unscharfes Cover hinter dem Flyout", "Blurred album art behind the flyout"),
                    () => StageLive(osd: false), body =>
                    {
                        var cfg = Cfg();
                        body.Children.Add(SwitchCard("\uE91B", T("Cover als Hintergrund", "Album art background"), null, cfg.MediaArtBackground, (c, v) => c.MediaArtBackground = v));
                        body.Children.Add(SliderCard("\uE706", T("Unschärfe", "Blur"), null, 0, 60, Math.Clamp(cfg.MediaArtBlur, 0, 60), "0", (c, v) => c.MediaArtBlur = v));
                        body.Children.Add(SliderCard("\uE706", T("Abdunkelung", "Dimming"), null, 0, 100, Math.Clamp(cfg.MediaArtDim, 0, 100), "0' %'", (c, v) => c.MediaArtDim = v));
                    }),
                Item(Flyout, "\uE8AB", "#14b8a6", () => T("Mehrere Quellen", "Several sources"),
                    () => T("Spielen mehrere Apps gleichzeitig, zeigt das Flyout für jede einen Eintrag. Ein Klick wechselt dorthin und pausiert die vorherige, der kleine Punkt pausiert eine Quelle, ohne zu wechseln.",
                        "When several apps play at once, the flyout shows an entry for each. A click switches there and pauses the previous one, the small dot pauses a source without switching."),
                    () => T("Zwischen Apps umschalten, die gerade spielen", "Switch between apps that are playing"),
                    () => StageLive(osd: false), body =>
                    {
                        var cfg = Cfg();
                        body.Children.Add(SwitchCard("\uE8AB", T("Quellen-Leiste anzeigen", "Show sources bar"), null, cfg.MediaShowSources, (c, v) => c.MediaShowSources = v));
                        body.Children.Add(SwitchCard("\uE769", T("Pause-Zeichen neben spielenden Quellen", "Pause symbol next to playing sources"), null, cfg.MediaShowSourcePause, (c, v) => c.MediaShowSourcePause = v));
                        body.Children.Add(new Border { Height = 6 });
                        body.Children.Add(Note(T("Zum Ausprobieren: Starte in zwei Apps gleichzeitig etwas, zum Beispiel Spotify und ein Video im Browser. Links stehen dann beide in der Quellen-Leiste. Im Betrieb erscheint sie nur, wenn mehrere Apps spielen.",
                            "To try it: start something in two apps at once, for example Spotify and a video in the browser. Both then appear in the sources bar on the left. In use it only appears when several apps are playing.")));
                    }),

                // ── Aussehen ──
                Step(Looks, "\uE790", 7, () => T("8 Designs für OSD und Flyout, hell oder dunkel", "8 designs for OSD and flyout, light or dark")),
                new FeatureItem(Looks, "\uE70F", () => T("Jedes Design anpassen, Vorlagen, eigene Designs speichern", "Customize any design, templates, save your own designs"), DesignPage()),

                // ── Platz ──
                Step(Place, "\uE8A9", 8, () => T("9 Plätze je Bildschirm, getrennt oder zusammen", "9 places per monitor, separate or together")),
                Step(Place, "\uE81D", 9, () => T("Gespeicherte Plätze, Einrasten, Wechseln per Taste", "Saved places, snapping, switching by key")),
                Item(Place, "\uE7F4", "#6366f1", () => T("Mehrere Bildschirme", "Several monitors"),
                    () => T("OSD und Flyout können auf verschiedenen Bildschirmen stehen. Ändert sich die Auflösung oder ein Bildschirm kommt dazu, rechnet KnobFlyout eigene Positionen von selbst um.",
                        "The OSD and flyout can be on different monitors. If the resolution changes or a monitor is added, KnobFlyout converts your own positions by itself."),
                    () => T("Eigener Bildschirm je Anzeige, Positionen passen sich an", "A monitor of its own for each, positions adapt"),
                    StageSnap, body =>
                    {
                        body.Children.Add(Feature("\uE7F4", T("Bildschirm wählen", "Choose a monitor"), T("Im Schritt „Wo soll es erscheinen?“ hat jede Auswahl ein eigenes Dropdown.", "In the step “Where should it appear?” each choice has its own dropdown.")));
                        var reset = Btn(T("Position zurücksetzen", "Reset position"));
                        reset.Margin = new Thickness(0);
                        reset.Click += (_, _) =>
                        {
                            Update(ResetPosition);
                            RefreshStage();
                            RunPositionTest(Cfg());
                        };
                        body.Children.Add(SettingCard("\uE72C", T("Etwas liegt außerhalb?", "Something off screen?"), T("Setzt OSD und Flyout auf den Standardplatz", "Puts the OSD and flyout back in the default place"), reset));
                    }),

                // ── Programm ──
                Item(Program, "\uE7E8", "#3b82f6", () => T("Start und Infobereich", "Startup and notification area"),
                    () => T("KnobFlyout läuft unsichtbar im Infobereich der Taskleiste. Mit Windows starten kannst du hier oder im Task-Manager unter „Autostart-Apps“ ein- und ausschalten.",
                        "KnobFlyout runs invisibly in the notification area of the taskbar. You can turn starting with Windows on and off here or in Task Manager under “Startup apps”."),
                    () => T("Autostart, Tray-Symbol, Beenden", "Autostart, tray icon, exit"),
                    StageDone, body =>
                    {
                        body.Children.Add(SettingCard("\uE7E8", T("Mit Windows starten", "Start with Windows"), null,
                            Toggle(Autostart.IsEnabled(), v =>
                            {
                                try { Autostart.SetEnabled(v); }
                                catch (Exception ex) { Logger.Error($"Autostart: {ex.Message}"); }
                            })));
                        body.Children.Add(new Border { Height = 8 });
                        body.Children.Add(Feature("\uE713", T("Rechtsklick aufs Symbol", "Right-click the icon"), T("Öffnet die Einstellungen oder beendet KnobFlyout.", "Opens the settings or exits KnobFlyout.")));
                    }),
                Item(Program, "\uE774", "#3b82f6", () => T("Sprache und Farbschema", "Language and color scheme"),
                    () => T("Deutsch oder Englisch, hell, dunkel oder wie Windows. OSD und Flyout folgen dem Farbschema.",
                        "German or English, light, dark or like Windows. The OSD and flyout follow the color scheme."),
                    () => T("Deutsch oder Englisch, hell oder dunkel", "German or English, light or dark"),
                    () => StageLive(), body =>
                    {
                        var cfg = Cfg();
                        var lang = Combo(150, cfg.Language == "en" ? 1 : 0, "Deutsch", "English");
                        lang.SelectionChanged += (_, _) =>
                        {
                            string chosen = lang.SelectedIndex == 1 ? "en" : "de";
                            if (chosen == Cfg().Language) return;
                            Update(c => c.Language = chosen);
                            Dispatcher.BeginInvoke(new Action(Rebuild));
                        };
                        body.Children.Add(SettingCard("\uE774", T("Sprache", "Language"), null, lang));
                        body.Children.Add(SettingCard("\uE790", T("Farbschema", "Color scheme"), null, ThemeChoice(cfg.Theme)));
                    }),
                Item(Program, "\uE897", "#64748b", () => T("Wenn etwas nicht klappt", "If something doesn't work"),
                    () => T("Die häufigsten Gründe und was hilft.", "The most common reasons and what helps."),
                    () => T("Häufige Gründe und was hilft", "Common reasons and what helps"),
                    () => StageLive(media: false), body =>
                    {
                        body.Children.Add(Feature("\uE767", T("Der Knopf tut nichts", "The knob does nothing"), T("Die App muss im aktiven Profil sein und gerade Ton abspielen.", "The app has to be in the active profile and playing sound.")));
                        body.Children.Add(Feature("\uE7EF", T("Reagiert in einem Fenster nicht", "Doesn't respond in one window"), T("Windows gibt die Tasten von als Administrator gestarteten Fenstern nicht weiter.", "Windows doesn't pass on keys from windows started as administrator.")));
                        body.Children.Add(Feature("\uE7F4", T("OSD oder Flyout nicht zu sehen", "OSD or flyout not visible"), T("Setze die Position zurück.", "Reset the position.")));
                        var reset = Btn(T("Position zurücksetzen", "Reset position"));
                        reset.Margin = new Thickness(0);
                        reset.Click += (_, _) =>
                        {
                            Update(ResetPosition);
                            RunPositionTest(Cfg());
                        };
                        body.Children.Add(SettingCard("\uE72C", T("Position", "Position"), null, reset));
                        body.Children.Add(new Border { Height = 6 });
                        body.Children.Add(Hint(T("Diese Einführung findest du jederzeit unter Einstellungen → Allgemein → Hilfe.", "You can find this introduction any time under Settings → General → Help.")));
                    }),
            };
        }

        // ── Bausteine ───────────────────────────────────────────────────────

        private static void ResetPosition(AppConfig c)
        {
            c.OsdX = c.OsdY = null;
            c.MediaOsdX = c.MediaOsdY = null;
            c.OsdCornerPreset = c.MediaCornerPreset = "custom";
            c.SnapIndependent = false;
        }

        private void AddLink(StackPanel body, string glyph, string title, string text, string page)
        {
            body.Children.Add(new Border
            {
                Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4), Margin = new Thickness(0, 4, 0, 6), Child = LinkRow(glyph, title, text, page),
            });
        }

        private Border SwitchCard(string glyph, string title, string? text, bool value, Action<AppConfig, bool> set) =>
            SettingCard(glyph, title, text, Toggle(value, v => Live(c => set(c, v))));

        /// <summary>Slider-Karte, deren Änderung live auf der Bühne erscheint; mit def zusätzlich ↺.</summary>
        private Border SliderCard(string glyph, string title, string? text, double min, double max, double value, string format, Action<AppConfig, int> set, double tick = 1, double? def = null)
        {
            var slider = SliderControl(min, max, value, format, v => Live(c => set(c, (int)Math.Round(v))), tick: tick, width: 120);
            return SettingCard(glyph, title, text, def.HasValue ? ResetSlider(slider, def.Value) : slider);
        }

        // ── Einzelne Seiten ─────────────────────────────────────────────────

        private void BodyKeep(StackPanel body)
        {
            var cfg = Cfg();
            body.Children.Add(new KeepEditor(cfg, () => _media.GetSources().Select(s => s.Label).Distinct().OrderBy(n => n).ToList(), () => Update(c =>
            {
                c.MediaKeepApps = new List<string>(cfg.MediaKeepApps);
                c.MediaKeepMode = cfg.MediaKeepMode;
                c.MediaKeepOwnPosition = cfg.MediaKeepOwnPosition;
                c.MediaKeepPreset = cfg.MediaKeepPreset;
                c.MediaKeepMonitor = cfg.MediaKeepMonitor;
            })));
        }

        private void BodyKnobSteps(StackPanel body)
        {
            var cfg = Cfg();
            _knobPill = new StatusPill(T("Probier es aus: Dreh am Knopf oder nimm − und + …", "Try it: turn your knob or use − and + …"));
            body.Children.Add(_knobPill);
            body.Children.Add(SettingCard("\uE767", T("Schrittweite", "Step size"), T("Pro Raste", "Per notch"),
                SliderControl(1, 15, cfg.Step, "0' %'", v => Update(c => c.Step = (int)v, notify: false), tick: 1, width: 120)));
            body.Children.Add(SettingCard("\uE8D6", T("Schrittweite Spotify", "Spotify step size"), T("Nur über die Spotify-API", "Only via the Spotify API"),
                SliderControl(1, 15, cfg.StepSpotify, "0' %'", v => Update(c => c.StepSpotify = (int)v, notify: false), tick: 1, width: 120)));
        }

        private void BodyAssignKeys(StackPanel body)
        {
            _appsPill = new StatusPill(T("Klick links im Popup auf ein Logo, um es auszuprobieren …", "Click a logo in the popup on the left to try it …"));
            body.Children.Add(_appsPill);
            var cfg = Cfg();
            var defs = ProfileDefinitions.For(cfg);
            foreach (var entry in cfg.ProfileList)
            {
                var def = defs.FirstOrDefault(p => p.Key == entry.Key);
                if (def == null) continue;
                string key = entry.Key;
                body.Children.Add(SettingCard("\uE765", def.Name, null, HotkeyCapture(entry.AssignHotkey, v => Update(c =>
                {
                    var e = c.ProfileList.FirstOrDefault(p => p.Key == key);
                    if (e != null) e.AssignHotkey = v;
                }), format: FormatHotkey, width: 176)));
            }
        }

        private void BodyExcluded(StackPanel body)
        {
            _appsPill = new StatusPill(T("Klick links im Popup auf ⊘ oder ein Logo …", "Click ⊘ or a logo in the popup on the left …"));
            body.Children.Add(_appsPill);
            body.Children.Add(SwitchCard("\uE7E7", T("Zuweisungs-OSD anzeigen", "Show assignment OSD"), T("Auch unter Einstellungen → Allgemein und Profile & Apps", "Also under Settings → General and Profiles & apps"), Cfg().NewAppPopup, (c, v) => c.NewAppPopup = v));
            var list = new StackPanel();
            void Render()
            {
                list.Children.Clear();
                var excluded = Cfg().Exclude;
                if (excluded.Count == 0)
                {
                    list.Children.Add(new TextBlock { Text = T("Noch keine App ignoriert.", "No app ignored yet."), FontSize = 13, Foreground = SubFg, Margin = new Thickness(8) });
                    return;
                }
                foreach (var proc in excluded.ToList())
                {
                    var row = new Grid { Margin = new Thickness(6, 2, 4, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Children.Add(new TextBlock { Text = AppRow.Label(proc), FontSize = 13, Foreground = Fg, VerticalAlignment = VerticalAlignment.Center });
                    var remove = FlatIconButton("\uE711", T("Nicht mehr ignorieren", "Stop ignoring"), () =>
                    {
                        Update(c => c.Exclude.RemoveAll(p => string.Equals(p, proc, StringComparison.OrdinalIgnoreCase)));
                        Render();
                    });
                    remove.Margin = new Thickness(0);
                    Grid.SetColumn(remove, 1);
                    row.Children.Add(remove);
                    list.Children.Add(row);
                }
            }
            Render();
            body.Children.Add(Heading(T("Ignorierte Apps", "Ignored apps")));
            body.Children.Add(new Border
            {
                Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(4),
                Child = new ScrollViewer { MaxHeight = 190, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list, Focusable = false }, // lange Liste scrollt in sich, die Seite nicht
            });
        }

        private FrameworkElement StageLock()
        {
            _lockHost = new Border { Child = OsdCard.CreateLockPreview(Cfg(), "CapsLock", System.Windows.Input.Keyboard.IsKeyToggled(System.Windows.Input.Key.CapsLock)), Effect = Soft(), HorizontalAlignment = HorizontalAlignment.Center };
            return OnStage(_lockHost, T("Reagiert live auf deine Tasten", "Reacts live to your keys"));
        }

        private void BodySkipKeys(StackPanel body)
        {
            var cfg = Cfg();
            _skipPill = new StatusPill(T("Leg zwei Tasten fest und probier sie aus …", "Set two keys and try them …"));
            body.Children.Add(_skipPill);
            body.Children.Add(SettingCard("\uE765", T("10 s vorspulen", "Skip forward 10 s"), null,
                HotkeyCapture(cfg.MediaSkipForwardHotkey, v => Update(c => c.MediaSkipForwardHotkey = v), format: FormatHotkey, width: 176)));
            body.Children.Add(SettingCard("\uE765", T("10 s zurückspulen", "Skip back 10 s"), null,
                HotkeyCapture(cfg.MediaSkipBackHotkey, v => Update(c => c.MediaSkipBackHotkey = v), format: FormatHotkey, width: 176)));
            body.Children.Add(SettingCard("\uE765", T("Spul-Tasten aktiv", "Skip keys enabled"), T("Aus = beide ruhen, bleiben aber gespeichert", "Off = both pause but stay saved"),
                Toggle(cfg.MediaSkipHotkeysEnabled, v => Update(c => c.MediaSkipHotkeysEnabled = v))));
        }

        private void BodyGestures(StackPanel body)
        {
            var cfg = Cfg();
            _gesturePill = new StatusPill(T("Schalte eine Geste ein und probier sie aus …", "Turn on a gesture and try it …"));
            body.Children.Add(_gesturePill);
            void Gesture(string glyph, string title, bool enabled, string key, string gesture, Action<AppConfig, bool> setEnabled, Action<AppConfig, string> setKey, Action<AppConfig, string> setGesture)
            {
                var card = new StackPanel();
                var trigger = Combo(190, gesture == "double" ? 0 : 1, T("Zweimal schnell drücken", "Press twice quickly"), T("Lange halten", "Hold"));
                trigger.SelectionChanged += (_, _) => Update(c => setGesture(c, trigger.SelectedIndex == 0 ? "double" : "hold"));
                card.Children.Add(OptionRow(T("Auslöser", "Trigger"), trigger));
                card.Children.Add(OptionRow(T("Taste", "Key"), HotkeyCapture(key, v => Update(c => setKey(c, v)), emptyText: T("Play/Pause-Taste", "Play/Pause key"), format: FormatHotkey, width: 150)));
                body.Children.Add(ExpanderCard(glyph, title, null, card, headerControl: Toggle(enabled, v => Update(c => setEnabled(c, v)))));
            }
            Gesture("\uE893", T("Nächster Titel", "Next track"), cfg.MediaNextEnabled, cfg.MediaNextKey, cfg.MediaNextGesture,
                (c, v) => c.MediaNextEnabled = v, (c, v) => c.MediaNextKey = v, (c, v) => c.MediaNextGesture = v);
            Gesture("\uE8AB", T("Quelle wechseln", "Switch source"), cfg.MediaSwitchEnabled, cfg.MediaSwitchKey, cfg.MediaSwitchGesture,
                (c, v) => c.MediaSwitchEnabled = v, (c, v) => c.MediaSwitchKey = v, (c, v) => c.MediaSwitchGesture = v);
            body.Children.Add(SettingCard("\uE916", T("Wie lange halten", "How long to hold"), null,
                SliderControl(200, 2000, cfg.MediaSwitchHoldMs, "0' ms'", v => Update(c => c.MediaSwitchHoldMs = (int)v), tick: 50, width: 110)));
            body.Children.Add(SettingCard("\uE916", T("Zeit für zweimal drücken", "Time to press twice"), null,
                SliderControl(150, 800, Math.Clamp(cfg.MediaDoubleMs, 150, 800), "0' ms'", v => Update(c => c.MediaDoubleMs = (int)v), tick: 25, width: 110)));
        }

        private void BodyMoreMenu(StackPanel body)
        {
            var cfg = Cfg();
            body.Children.Add(SwitchCard("\uE712", T("„…“-Knopf anzeigen", "Show “…” button"), null, cfg.MediaShowMore, (c, v) => c.MediaShowMore = v));
            body.Children.Add(new Border { Height = 6 });
            body.Children.Add(Heading(T("Einträge im Menü", "Items in the menu")));
            var options = SettingsWindow.MoreMenuOptions();
            var rows = new StackPanel();
            foreach (var (key, label) in options)
            {
                string item = key;
                rows.Children.Add(OptionRow(label, Toggle(cfg.MediaMoreItems.Contains(item), on => Live(c =>
                {
                    c.MediaMoreItems.Remove(item);
                    if (on) c.MediaMoreItems.Add(item);
                    // Feste Reihenfolge wie im Menü, unabhängig von der Klick-Reihenfolge.
                    c.MediaMoreItems = options.Select(o => o.Key).Where(c.MediaMoreItems.Contains).ToList();
                }))));
            }
            body.Children.Add(new Border
            {
                Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 8, 14, 8), Child = rows,
            });
        }
    }
}
