using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using KnobFlyout.Audio;
using KnobFlyout.Config;
using KnobFlyout.Osd;
using KnobFlyout.Profiles;
using static KnobFlyout.Loc;
using static KnobFlyout.Settings.UiHelpers;

namespace KnobFlyout.Settings
{
    /// <summary>
    /// Die Seiten der Einführung. Reihenfolge = was man braucht, damit der Knopf etwas tut (Knopf → Profile →
    /// Apps), danach, was man sich einrichten kann (eigene Profile, Flyout, Look, Platz), zuletzt der Spickzettel.
    /// </summary>
    public sealed partial class TutorialWindow
    {
        private readonly SessionVolumeController _audio = new();

        // Vorführung auf der Seite „Knopf“
        private int _demoPct = 64;
        private bool _demoMuted;
        private KnobView? _demoKnob;
        private Action<int, bool>? _demoCard;
        private StatusPill? _knobPill;

        // Seite „Profile“
        private StackPanel? _profilesHost;
        private StatusPill? _profilePill;

        // Seite „Eigene Profile“
        private string _draftName = "";
        private string _draftGlyph = ProfileDefinitions.Gallery[4];
        private string _draftColor = ProfileDefinitions.Palette[4];
        private string _draftDone = "";
        private bool _draftTouched;
        private Border? _draftPreview;

        private void ClearLiveFields()
        {
            _demoKnob = null;
            _demoCard = null;
            _knobPill = null;
            _profilesHost = null;
            _profilePill = null;
            _draftPreview = null;
            _draftDone = "";
        }

        // ── Von der App: echte Tasten ───────────────────────────────────────

        public void OnVolumeKey(int dir)
        {
            if (_demoKnob == null) return; // nur auf der Seite „Knopf“
            _demoPct = Math.Clamp(_demoPct + dir * Math.Max(1, Cfg().Step), 0, 100);
            _demoMuted = false;
            _demoKnob.Set(_demoPct, false, true);
            _demoCard?.Invoke(_demoPct, false);
            _knobPill?.Done(T("Knopf erkannt: Genau so regelst du gleich deine Apps", "Knob detected: this is how you'll control your apps"));
        }

        public void OnMuteKey()
        {
            if (_demoKnob == null) return;
            _demoMuted = !_demoMuted;
            _demoKnob.Set(_demoPct, _demoMuted, true);
            _demoCard?.Invoke(_demoPct, _demoMuted);
            _knobPill?.Done(_demoMuted ? T("Stumm erkannt: Noch einmal drücken schaltet wieder laut", "Mute detected: press again to unmute") : T("Wieder laut", "Unmuted"));
        }

        public void OnProfileHotkey()
        {
            if (_profilesHost == null) return; // nur auf der Seite „Profile“
            var cfg = Cfg();
            RenderProfiles(_profilesHost, cfg);
            string name = ProfileDefinitions.For(cfg).FirstOrDefault(p => p.Key == cfg.ActiveProfile)?.Name ?? "";
            _profilePill?.Done(T($"Klappt: Jetzt ist „{name}“ aktiv", $"Works: “{name}” is now active"));
        }

        // ── Seiten ──────────────────────────────────────────────────────────

        private List<Page> BuildPages() => new()
        {
            new("#3b82f6", () => T("Willkommen bei KnobFlyout", "Welcome to KnobFlyout"),
                () => T("Dein Lautstärke-Knopf regelt ab jetzt genau die Apps, die du willst, statt immer das ganze System.",
                    "From now on, your volume knob controls exactly the apps you want instead of the whole system."),
                StageWelcome, BodyWelcome),
            new("#0ea5e9", () => T("Drehen und drücken", "Turn and press"),
                () => T("Drehen ändert die Lautstärke, Drücken schaltet stumm. Die Lautstärke-Tasten einer Tastatur funktionieren genauso.",
                    "Turning changes the volume, pressing mutes. The volume keys on a keyboard work just the same."),
                StageKnob, BodyKnob),
            new("#a855f7", () => T("Profile: Gruppen von Apps", "Profiles: groups of apps"),
                () => T("Der Knopf regelt nur die Apps des aktiven Profils. So drehst du zum Beispiel das Spiel leiser, während Discord gleich laut bleibt.",
                    "The knob only controls the apps in the active profile. For example, you can turn the game down while Discord stays as loud as it is."),
                StageProfiles, BodyProfiles),
            new("#22c55e", () => T("Apps zuweisen", "Assign apps"),
                () => T("Spielt eine neue App Ton ab, fragt KnobFlyout von selbst, zu welchem Profil sie gehört. Ein Klick genügt.",
                    "When a new app plays sound, KnobFlyout asks which profile it belongs to. One click is all it takes."),
                StageApps, BodyApps),
            new("#ec4899", () => T("Eigene Profile anlegen", "Create your own profiles"),
                () => T("Bis zu 6 Profile, jedes mit eigenem Namen, Logo und eigener Farbe. Auch die Standard-Profile lassen sich ändern.",
                    "Up to 6 profiles, each with its own name, logo and color. The default profiles can be changed too."),
                StageCustom, BodyCustom),
            new("#14b8a6", () => T("Media-Flyout", "Media flyout"),
                () => T("Zeigt bei jedem Titelwechsel, was gerade läuft: für Spotify, Browser und jede App, die Medien an Windows meldet.",
                    "Shows what's playing whenever the track changes: for Spotify, browsers and any app that reports media to Windows."),
                StageMedia, BodyMedia),
            new("#f97316", () => T("Such dir einen Look aus", "Pick a look"),
                () => T("Ein Klick ändert OSD und Media-Flyout gemeinsam. Später lässt sich beides einzeln einstellen, bis hin zum eigenen Design.",
                    "One click changes the OSD and media flyout together. Later you can set each one separately, all the way to your own design."),
                StageLook, BodyLook),
            new("#6366f1", () => T("Wo soll es erscheinen?", "Where should it appear?"),
                () => T("Klick links auf einen der neun Plätze. „Testen“ zeigt OSD und Media-Flyout sofort genau dort.",
                    "Click one of the nine places on the left. “Test” shows the OSD and media flyout right there."),
                StagePosition, BodyPosition),
            new("#3b82f6", () => T("Alles bereit", "You're all set"),
                () => T("KnobFlyout läuft jetzt im Infobereich der Taskleiste. Ein Rechtsklick auf das Lautsprecher-Symbol öffnet die Einstellungen.",
                    "KnobFlyout now runs in the notification area of the taskbar. Right-click the speaker icon to open the settings."),
                StageDone, BodyDone),
        };

        // ── 1 · Willkommen ──────────────────────────────────────────────────

        private FrameworkElement StageWelcome()
        {
            var knob = new KnobView(HardwareParts.Colors(Light), UiColor.Brush("#3b82f6"), UiColor.Brush("#ef4444"), 64);
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(new Viewbox { Width = 176, Height = 176, Child = knob, Margin = new Thickness(0, 0, 0, 20) });

            var rows = new StackPanel();
            var (musicFill, musicPct) = DemoRow(rows, "\uE8D6", "Spotify", 50, true);
            DemoRow(rows, "\uE7FC", T("Spiel", "Game"), 72, false);
            DemoRow(rows, "\uE8BD", "Discord", 86, false);
            panel.Children.Add(new Border
            {
                Width = 340, Background = Surface, BorderBrush = SurfaceEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16, 12, 16, 8), Child = rows, Effect = Soft(),
            });

            // Der Knopf dreht, nur eine App folgt: das ist die ganze Idee.
            var started = DateTime.UtcNow;
            void Tick()
            {
                double t = (DateTime.UtcNow - started).TotalSeconds;
                int pct = (int)Math.Round(52 + 30 * Math.Sin(t * 0.9));
                knob.Set(pct, false, false);
                musicFill.ScaleX = pct / 100.0;
                musicPct.Text = pct + " %";
            }
            Tick();
            StartPageTimer(TimeSpan.FromMilliseconds(40), Tick);
            return OnStage(panel, T("Nur die Musik ändert sich, Spiel und Discord bleiben gleich.", "Only the music changes, the game and Discord stay the same."));
        }

        /// <summary>Zeile der Vorführung: Symbol, App, Pegel, Zahl.</summary>
        private static (ScaleTransform Fill, TextBlock Pct) DemoRow(StackPanel host, string glyph, string name, int pct, bool live)
        {
            var row = new Grid { Height = 30, Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            var fg = live ? Fg : SubFg;
            row.Children.Add(new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 14, Foreground = fg, VerticalAlignment = VerticalAlignment.Center });
            var label = new TextBlock { Text = name, FontSize = 13, FontWeight = live ? FontWeights.SemiBold : FontWeights.Normal, Foreground = fg, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            var fill = new ScaleTransform(pct / 100.0, 1);
            var bar = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center };
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Light ? UiColor.Brush("#1F000000") : UiColor.Brush("#2EFFFFFF") });
            bar.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(3), Background = live ? UiColor.Brush("#3b82f6") : (Light ? UiColor.Brush("#66000000") : UiColor.Brush("#73FFFFFF")),
                RenderTransform = fill, RenderTransformOrigin = new Point(0, 0.5),
            });
            Grid.SetColumn(bar, 2);
            row.Children.Add(bar);
            var value = new TextBlock { Text = pct + " %", FontSize = 12, Foreground = fg, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(value, 3);
            row.Children.Add(value);
            host.Children.Add(row);
            return (fill, value);
        }

        private void BodyWelcome(StackPanel body)
        {
            body.Children.Add(Feature("\uE716", T("Profile", "Profiles"), T("Gruppen von Apps, zwischen denen du per Taste wechselst.", "Groups of apps you switch between with a key.")));
            body.Children.Add(Feature("\uE7F4", T("OSD und Media-Flyout", "OSD and media flyout"), T("Zeigen Lautstärke und laufenden Titel, in dem Look und an dem Platz, die dir gefallen.", "Show the volume and current track, in the look and place you like.")));
            body.Children.Add(Feature("\uE713", T("In etwa 2 Minuten eingerichtet", "Set up in about 2 minutes"), T("Du richtest alles direkt hier ein und kannst jederzeit überspringen. Ändern lässt sich später alles.", "You set everything up right here and can skip at any time. Everything can be changed later.")));

            var lang = Combo(150, Cfg().Language == "en" ? 1 : 0, "Deutsch", "English");
            lang.SelectionChanged += (_, _) =>
            {
                string chosen = lang.SelectedIndex == 1 ? "en" : "de";
                if (chosen == Cfg().Language) return;
                Update(c => c.Language = chosen);
                Dispatcher.BeginInvoke(new Action(Rebuild)); // nicht mitten im SelectionChanged
            };
            body.Children.Add(new Border { Height = 6 });
            body.Children.Add(SettingCard("\uE774", T("Sprache", "Language"), null, lang));
        }

        // ── 2 · Knopf ───────────────────────────────────────────────────────

        private FrameworkElement StageKnob()
        {
            var cfg = Cfg();
            _demoKnob = new KnobView(HardwareParts.Colors(Light), UiColor.Brush("#3b82f6"), UiColor.Brush("#ef4444"), 64);
            _demoKnob.Set(_demoPct, _demoMuted, false);
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(new Viewbox { Width = 176, Height = 176, Child = _demoKnob, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 24) });
            var card = OsdCard.CreateLiveVolumePreview(cfg, "Spotify", _demoPct, _demoMuted, out _demoCard);
            card.Effect = Soft();
            card.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(card);
            return OnStage(panel, T("Reagiert live auf deinen Knopf", "Reacts live to your knob"));
        }

        private void BodyKnob(StackPanel body)
        {
            _knobPill = new StatusPill(T("Probier es aus: Dreh einmal am Knopf …", "Try it: turn your knob …"));
            body.Children.Add(_knobPill);

            body.Children.Add(SettingCard("\uE767", T("Schrittweite", "Step size"), T("Pro Raste, gilt sofort", "Per notch, applies immediately"),
                SliderControl(1, 15, Cfg().Step, "0' %'", v => Update(c => c.Step = (int)v, notify: false), tick: 1, width: 120)));

            body.Children.Add(new Border { Height = 8 });
            body.Children.Add(Note(T("Solange KnobFlyout läuft, ändern diese Tasten nicht mehr die Windows-Gesamtlautstärke, sondern nur die Apps des aktiven Profils. Ohne zugewiesene App tut sich deshalb noch nichts. Das kommt gleich.",
                "While KnobFlyout is running, these keys no longer change the Windows master volume, only the apps in the active profile. So nothing happens until an app is assigned. We'll get to that next."), warn: true));
            body.Children.Add(Note(T("Reagiert der Knopf mal nicht? Solange ein als Administrator gestartetes Fenster im Vordergrund ist, gibt Windows seine Tasten nicht an KnobFlyout weiter.",
                "Knob not responding? While a window started as administrator is in the foreground, Windows doesn't pass its keys on to KnobFlyout.")));
        }

        // ── 3 · Profile und Wechsel-Taste ───────────────────────────────────

        private FrameworkElement StageProfiles()
        {
            _profilesHost = new StackPanel { Width = 384 };
            RenderProfiles(_profilesHost, Cfg());
            return OnStage(_profilesHost);
        }

        /// <summary>Die echten Profile mit ihren Apps; das aktive ist hervorgehoben.</summary>
        private static void RenderProfiles(StackPanel host, AppConfig cfg)
        {
            host.Children.Clear();
            foreach (var p in ProfileDefinitions.For(cfg))
            {
                bool active = p.Key == cfg.ActiveProfile;
                var apps = cfg.Profiles.TryGetValue(p.Key, out var list) ? list : new List<string>();

                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new Border
                {
                    Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Background = UiColor.Brush(p.Color),
                    HorizontalAlignment = HorizontalAlignment.Left, Child = p.CreateLogo(20, Brushes.White),
                });
                var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                words.Children.Add(new TextBlock { Text = p.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Fg, TextTrimming = TextTrimming.CharacterEllipsis });
                words.Children.Add(new TextBlock
                {
                    Text = apps.Count == 0 ? T("Noch keine Apps", "No apps yet") : string.Join(", ", apps.Select(AppRow.Label)),
                    FontSize = 12, Foreground = SubFg, TextTrimming = TextTrimming.CharacterEllipsis,
                });
                Grid.SetColumn(words, 1);
                row.Children.Add(words);
                if (active)
                {
                    var tag = new Border
                    {
                        Background = UiColor.Brush(p.Color), CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 2, 9, 3),
                        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
                        Child = new TextBlock { Text = T("Aktiv", "Active"), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = TextOn(p.Color) },
                    };
                    Grid.SetColumn(tag, 2);
                    row.Children.Add(tag);
                }
                host.Children.Add(new Border
                {
                    Background = Surface, BorderBrush = active ? UiColor.Brush(p.Color) : SurfaceEdge, BorderThickness = new Thickness(active ? 2 : 1),
                    CornerRadius = new CornerRadius(12), Padding = active ? new Thickness(11, 7, 11, 7) : new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 8), Child = row, Effect = active ? Soft() : null,
                });
            }
        }

        /// <summary>Schwarz oder weiß, je nachdem, was auf dieser Farbe lesbar ist.</summary>
        private static Brush TextOn(string hex)
        {
            var c = UiColor.Brush(hex).Color;
            return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 150 ? UiColor.Brush("#111111") : Brushes.White;
        }

        private void BodyProfiles(StackPanel body)
        {
            string hotkey = Cfg().ProfileHotkey;
            _profilePill = new StatusPill(T($"Probier es aus: Drück {FormatHotkey(hotkey)} …", $"Try it: press {FormatHotkey(hotkey)} …"));
            body.Children.Add(SettingCard("\uE765", T("Taste zum Wechseln", "Key to switch"), T("Zum Ändern ins Feld klicken und Taste drücken", "To change, click the field and press a key"),
                HotkeyCapture(hotkey, v =>
                {
                    Update(c => c.ProfileHotkey = v);
                    _profilePill?.Waiting(T($"Probier es aus: Drück {FormatHotkey(v)} …", $"Try it: press {FormatHotkey(v)} …"));
                }, allowClear: false, format: FormatHotkey, width: 176)));
            body.Children.Add(new Border { Height = 6 });
            body.Children.Add(_profilePill);
            body.Children.Add(Note(T("Der Knopf wirkt auf die Apps des Profils, die gerade Ton abspielen. Spielt keine davon, ändert sich nichts.",
                "The knob acts on the apps in the profile that are currently playing sound. If none of them is, nothing changes.")));
        }

        // ── 4 · Apps zuweisen ───────────────────────────────────────────────

        private FrameworkElement StageApps()
        {
            var popup = NewAppOsd.CreatePreview(Cfg());
            popup.Effect = Soft();
            return OnStage(popup, T("So fragt KnobFlyout nach, sobald eine neue App Ton abspielt", "This is how KnobFlyout asks as soon as a new app plays sound"));
        }

        private void BodyApps(StackPanel body)
        {
            body.Children.Add(Heading(T("Gerade spielen Ton ab", "Playing sound right now")));
            var list = new StackPanel();
            var card = new Border
            {
                Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6), MinHeight = 74, Child = new ScrollViewer { MaxHeight = 204, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list, Focusable = false },
            };
            body.Children.Add(card);
            body.Children.Add(new Border { Height = 10 });
            body.Children.Add(Note(T("⊘ ignoriert eine App dauerhaft. Alle Apps findest du später unter Einstellungen → Lautstärke & Apps.",
                "⊘ ignores an app for good. You'll find all apps later under Settings → Volume & apps.")));

            string shown = "\0";
            void Render(bool force)
            {
                List<string> running;
                try
                {
                    running = _audio.GetSessions()
                        .Where(s => s.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive)
                        .Select(s => s.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Einführung, Apps: {ex.Message}");
                    running = new List<string>();
                }
                string key = string.Join("|", running);
                if (!force && key == shown) return; // sonst flackert die Zeile unter der Maus
                shown = key;

                list.Children.Clear();
                if (running.Count == 0)
                {
                    list.Children.Add(new TextBlock
                    {
                        Text = T("Gerade spielt keine App Ton ab. Starte Musik oder ein Video, dann erscheint die App hier sofort.",
                            "No app is playing sound right now. Start some music or a video and the app shows up here right away."),
                        FontSize = 13, LineHeight = 19, Foreground = SubFg, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 8, 8, 8),
                    });
                    return;
                }
                var cfg = Cfg();
                foreach (var proc in running)
                    list.Children.Add(AppRow.Build(cfg, proc, true, () =>
                    {
                        ConfigManager.Save(cfg);
                        ConfigChanged?.Invoke();
                        Render(true);
                    }));
            }
            Render(true);
            StartPageTimer(TimeSpan.FromSeconds(1.5), () => Render(false));
        }

        // ── 5 · Eigene Profile ──────────────────────────────────────────────

        private FrameworkElement StageCustom()
        {
            if (!_draftTouched)
            {
                var cfg = Cfg();
                _draftGlyph = ProfileDefinitions.Gallery.FirstOrDefault(g => cfg.ProfileList.All(p => p.Glyph != g)) ?? _draftGlyph;
                _draftColor = ProfileDefinitions.Palette.FirstOrDefault(c => cfg.ProfileList.All(p => !string.Equals(p.Color, c, StringComparison.OrdinalIgnoreCase))) ?? _draftColor;
            }
            _draftPreview = new Border { HorizontalAlignment = HorizontalAlignment.Center, Effect = Soft() };
            RenderDraft();
            return OnStage(_draftPreview, T("Vorschau: So erscheint dein Profil beim Wechseln", "Preview: this is how your profile appears when switching"));
        }

        private void RenderDraft()
        {
            if (_draftPreview == null) return;
            var cfg = Clone(Cfg());
            var entry = new ProfileEntry
            {
                Key = "tutorial_draft", Glyph = _draftGlyph, Color = _draftColor,
                Name = _draftName.Trim().Length > 0 ? _draftName.Trim() : T("Mein Profil", "My profile"),
            };
            cfg.ProfileList = cfg.ProfileList.Take(ProfileDefinitions.MaxProfiles - 1).Append(entry).ToList();
            cfg.ActiveProfile = entry.Key;
            _draftPreview.Child = OsdCard.CreatePreview(cfg, "profile");
        }

        private void BodyCustom(StackPanel body)
        {
            var cfg = Cfg();
            int count = cfg.ProfileList.Count;
            bool full = count >= ProfileDefinitions.MaxProfiles;
            var form = new StackPanel();

            form.Children.Add(new TextBlock { Text = T("Name", "Name"), FontSize = 12, Foreground = SubFg, Margin = new Thickness(0, 0, 0, 4) });
            var name = TextField(_draftName);
            name.Margin = new Thickness(0, 0, 0, 10);
            name.MaxLength = 24;
            name.TextChanged += (_, _) => { _draftName = name.Text; RenderDraft(); };
            form.Children.Add(name);

            form.Children.Add(new TextBlock { Text = "Logo", FontSize = 12, Foreground = SubFg, Margin = new Thickness(0, 0, 0, 4) });
            var logos = new UniformGrid { Columns = 11, Margin = new Thickness(-2, 0, -2, 8) };
            var logoTiles = new List<(Border Tile, string Glyph, TextBlock Text)>();
            void MarkLogos()
            {
                foreach (var (tile, glyph, text) in logoTiles)
                {
                    bool on = glyph == _draftGlyph;
                    tile.Background = on ? FromHex(_draftColor) : Brushes.Transparent;
                    text.Foreground = on ? Brushes.White : Fg;
                }
            }
            foreach (var glyph in ProfileDefinitions.Gallery.Take(22))
            {
                string g = glyph;
                var text = new TextBlock { Text = g, FontFamily = IconFont, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var tile = new Border { Height = 32, Margin = new Thickness(1), CornerRadius = new CornerRadius(6), Cursor = System.Windows.Input.Cursors.Hand, Child = text };
                tile.MouseEnter += (_, _) => { if (g != _draftGlyph) tile.Background = HoverBg; };
                tile.MouseLeave += (_, _) => { if (g != _draftGlyph) tile.Background = Brushes.Transparent; };
                tile.MouseLeftButtonUp += (_, _) => { _draftGlyph = g; _draftTouched = true; MarkLogos(); RenderDraft(); };
                logoTiles.Add((tile, g, text));
                logos.Children.Add(tile);
            }
            form.Children.Add(logos);

            form.Children.Add(new TextBlock { Text = T("Farbe", "Color"), FontSize = 12, Foreground = SubFg, Margin = new Thickness(0, 0, 0, 5) });
            var colors = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            var swatches = new List<(Border Swatch, string Hex)>();
            void MarkColors()
            {
                foreach (var (swatch, hex) in swatches)
                    swatch.BorderBrush = hex == _draftColor ? Fg : Brushes.Transparent;
            }
            foreach (var hex in ProfileDefinitions.Palette)
            {
                string h = hex;
                var swatch = new Border
                {
                    Width = 30, Height = 30, CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(2), Margin = new Thickness(0, 0, 6, 0),
                    Cursor = System.Windows.Input.Cursors.Hand, Padding = new Thickness(2),
                    Child = new Border { CornerRadius = new CornerRadius(11), Background = FromHex(h) },
                };
                swatch.MouseLeftButtonUp += (_, _) => { _draftColor = h; _draftTouched = true; MarkColors(); MarkLogos(); RenderDraft(); };
                swatches.Add((swatch, h));
                colors.Children.Add(swatch);
            }
            form.Children.Add(colors);

            var create = AccentBtn(T("Profil anlegen", "Create profile"));
            create.Margin = new Thickness(0);
            create.HorizontalAlignment = HorizontalAlignment.Left;
            create.Padding = new Thickness(16, 6, 16, 6);
            create.IsEnabled = !full;
            create.Click += (_, _) =>
            {
                string finalName = _draftName.Trim().Length > 0 ? _draftName.Trim() : T("Mein Profil", "My profile");
                var entry = new ProfileEntry { Key = ProfileDefinitions.NewKey(), Name = finalName, Glyph = _draftGlyph, Color = _draftColor };
                Update(c =>
                {
                    if (c.ProfileList.Count >= ProfileDefinitions.MaxProfiles) return;
                    c.ProfileList.Add(entry);
                    c.Profiles[entry.Key] = new List<string>();
                });
                string done = T($"„{finalName}“ ist angelegt und steht jetzt beim Wechseln und Zuweisen zur Wahl.", $"“{finalName}” has been created and is now available for switching and assigning.");
                _draftName = "";
                _draftTouched = false;
                _draftDone = done;
                RefreshPage();
            };
            form.Children.Add(create);
            // Enter im Namensfeld legt das Profil an, statt zur nächsten Seite zu springen.
            name.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Enter || !create.IsEnabled) return;
                e.Handled = true;
                create.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            };

            MarkLogos();
            MarkColors();
            body.Children.Add(new Border
            {
                Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 12, 14, 14), Margin = new Thickness(0, 0, 0, 10), Child = form,
            });

            if (_draftDone.Length > 0)
            {
                var pill = new StatusPill("");
                pill.Done(_draftDone);
                body.Children.Add(pill);
            }
            else if (full)
                body.Children.Add(Note(T("Du hast schon 6 Profile, mehr gehen nicht. Löschen oder ändern kannst du sie unter Einstellungen → Profile.",
                    "You already have 6 profiles, the maximum. You can delete or change them under Settings → Profiles.")));
            else
                body.Children.Add(Hint(T("Eigene Bilder als Logo, Reihenfolge und Zuweisen-Tasten: Einstellungen → Profile.",
                    "Your own images as logos, the order and assign keys: Settings → Profiles.")));
        }

        // ── 6 · Media-Flyout ────────────────────────────────────────────────

        private FrameworkElement StageMedia()
        {
            var flyout = MediaOsd.CreatePreview(Cfg());
            flyout.Effect = Soft();
            return OnStage(flyout, T("Beispiel im aktuellen Design", "Example in the current design"));
        }

        private void BodyMedia(StackPanel body)
        {
            body.Children.Add(Feature("\uE916", T("Zeitleiste", "Timeline"), T("Klicken oder ziehen springt im Titel, die Knöpfe spulen 10 Sekunden.", "Click or drag to jump within a track, the buttons skip 10 seconds.")));
            body.Children.Add(Feature("\uE8AB", T("Mehrere Quellen", "Several sources"), T("Spielen mehrere Apps, schaltest du unten im Flyout zwischen ihnen um.", "When several apps are playing, you switch between them at the bottom of the flyout.")));
            body.Children.Add(Feature("\uE712", T("„…“-Menü", "“…” menu"), T("Zufall, Wiederholen und mehr. Eigene Spul-Tasten und Gesten: Einstellungen → Tastenkürzel.", "Shuffle, repeat and more. Your own skip keys and gestures: Settings → Shortcuts.")));
            body.Children.Add(new Border { Height = 4 });
            body.Children.Add(SettingCard("\uE8D6", T("Media-Flyout anzeigen", "Show media flyout"), null,
                Toggle(Cfg().MediaEnabled, v => Update(c => c.MediaEnabled = v))));
        }

        // ── 7 · Look ────────────────────────────────────────────────────────

        /// <summary>Zusammenpassende Designs für OSD und Flyout; „Eigenes“ gibt es in den Einstellungen.</summary>
        private static IEnumerable<(string Osd, string Media, string Name)> Looks()
        {
            yield return ("modern", "modernflyouts", "ModernFlyouts");
            yield return ("win11", "win11", "Windows 11");
            foreach (var d in Designs.All) yield return (d.Key, d.Key, d.Name);
        }

        private FrameworkElement StageLook()
        {
            var cfg = Cfg();
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var flyout = MediaOsd.CreatePreview(cfg);
            flyout.Effect = Soft();
            flyout.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(flyout);
            var osd = OsdCard.CreatePreview(cfg, "volume");
            osd.Effect = Soft();
            osd.HorizontalAlignment = HorizontalAlignment.Center;
            osd.Margin = new Thickness(0, 14, 0, 0);
            panel.Children.Add(osd);
            return OnStage(new Viewbox { MaxWidth = 410, StretchDirection = StretchDirection.DownOnly, Child = panel });
        }

        private void BodyLook(StackPanel body)
        {
            var cfg = Cfg();
            var grid = new UniformGrid { Columns = 3, Margin = new Thickness(-3, 0, -3, 10) };
            foreach (var look in Looks())
            {
                bool on = look.Osd == cfg.OsdDesign || (look.Osd == "modern" && cfg.OsdDesign == "windows");
                var sample = Clone(cfg);
                sample.OsdDesign = look.Osd;
                var thumb = OsdCard.CreatePreview(sample, "volume");
                var content = new StackPanel();
                content.Children.Add(new Viewbox { Height = 27, Child = thumb, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 6) });
                content.Children.Add(new TextBlock
                {
                    Text = look.Name, FontSize = 12, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Fg,
                    TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                });
                var tile = new Border
                {
                    Background = CardBg, BorderBrush = on ? Accent : CardBorder, BorderThickness = new Thickness(on ? 2 : 1), CornerRadius = new CornerRadius(8),
                    Padding = on ? new Thickness(7) : new Thickness(8), Margin = new Thickness(3), Cursor = System.Windows.Input.Cursors.Hand, Child = content,
                };
                if (!on)
                {
                    tile.MouseEnter += (_, _) => tile.Background = HoverBg;
                    tile.MouseLeave += (_, _) => tile.Background = CardBg;
                }
                var chosen = look;
                tile.MouseLeftButtonUp += (_, _) =>
                {
                    Update(c => { c.OsdDesign = chosen.Osd; c.MediaDesign = chosen.Media; });
                    RefreshPage();
                };
                grid.Children.Add(tile);
            }
            var own = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            own.Children.Add(new TextBlock { Text = "\uE70F", FontFamily = IconFont, FontSize = 17, Foreground = Fg, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 8) });
            own.Children.Add(new TextBlock { Text = T("Eigenes", "Custom"), FontSize = 12, Foreground = Fg, TextAlignment = TextAlignment.Center });
            var ownTile = new Border
            {
                Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(8), Margin = new Thickness(3),
                Cursor = System.Windows.Input.Cursors.Hand, Child = own, ToolTip = T("Öffnet die Einstellungen auf der Seite „Design“", "Opens the settings on the “Design” page"),
            };
            ownTile.MouseEnter += (_, _) => ownTile.Background = HoverBg;
            ownTile.MouseLeave += (_, _) => ownTile.Background = CardBg;
            ownTile.MouseLeftButtonUp += (_, _) =>
            {
                RequestedSettingsPage = "design";
                Close();
            };
            grid.Children.Add(ownTile);
            body.Children.Add(grid);

            body.Children.Add(SettingCard("\uE790", T("Farbschema", "Color scheme"), null, ThemeChoice(cfg.Theme)));
        }

        private FrameworkElement ThemeChoice(string current)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (key, label) in new[] { ("system", T("System", "System")), ("light", T("Hell", "Light")), ("dark", T("Dunkel", "Dark")) })
            {
                bool on = key == current;
                string chosen = key;
                var seg = new Border
                {
                    Background = on ? Accent : Brushes.Transparent, CornerRadius = new CornerRadius(5), Padding = new Thickness(12, 4, 12, 5),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Child = new TextBlock { Text = label, FontSize = 12, Foreground = on ? Brushes.White : Fg },
                };
                if (!on)
                {
                    seg.MouseEnter += (_, _) => seg.Background = HoverBg;
                    seg.MouseLeave += (_, _) => seg.Background = Brushes.Transparent;
                    seg.MouseLeftButtonUp += (_, _) =>
                    {
                        Update(c => c.Theme = chosen);
                        Rebuild();
                    };
                }
                row.Children.Add(seg);
            }
            return new Border
            {
                Background = FieldBg, BorderBrush = FieldBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(2), Child = row,
            };
        }

        // ── 8 · Position ────────────────────────────────────────────────────

        private FrameworkElement StagePosition()
        {
            var cfg = Cfg();
            var monitors = Placement.Monitors();
            StackPanel Labeled(string label, FrameworkElement picker)
            {
                var p = new StackPanel { Margin = new Thickness(6, 0, 6, 0) };
                p.Children.Add(new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Fg, Margin = new Thickness(0, 0, 0, 8) });
                p.Children.Add(picker);
                return p;
            }

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            if (cfg.OsdLayout == "combined")
            {
                var (preset, device) = SettingsWindow.CombinedPosition(cfg);
                // Noch nie verschoben: Das OSD steht oben rechts, also auch so anzeigen.
                if (preset == "custom" && cfg.OsdX == null && cfg.MediaOsdX == null) preset = "top_right";
                var both = new ScreenTilePicker(monitors, device, preset, allowCustom: false, width: 372, maxScreenHeight: 226);
                both.Changed += () => Update(c =>
                {
                    SettingsWindow.ApplyCombinedPosition(c, both.Preset, both.Device);
                    c.SnapIndependent = false;
                });
                row.Children.Add(Labeled(T("OSD und Media-Flyout", "OSD and media flyout"), both));
            }
            else
            {
                var osd = new ScreenTilePicker(monitors, cfg.OsdMonitor, cfg.OsdCornerPreset == "custom" && cfg.OsdX == null ? "top_right" : cfg.OsdCornerPreset, allowCustom: false, width: 196);
                osd.Changed += () => Update(c => { c.OsdCornerPreset = osd.Preset; c.OsdMonitor = osd.Device; c.SnapIndependent = false; });
                row.Children.Add(Labeled("OSD", osd));
                var media = new ScreenTilePicker(monitors, cfg.MediaMonitor, cfg.MediaCornerPreset, allowCustom: false, width: 196);
                media.Changed += () => Update(c => { c.MediaCornerPreset = media.Preset; c.MediaMonitor = media.Device; c.SnapIndependent = false; });
                row.Children.Add(Labeled("Media-Flyout", media));
            }
            return OnStage(row);
        }

        private void BodyPosition(StackPanel body)
        {
            var arrange = Combo(206, SettingsWindow.ArrangementIndex(Cfg()),
                T("Getrennt", "Separate"),
                T("Flyout über dem OSD", "Flyout above OSD"),
                T("Flyout unter dem OSD", "Flyout below OSD"));
            arrange.SelectionChanged += (_, _) =>
            {
                var a = SettingsWindow.Arrangements[Math.Max(0, arrange.SelectedIndex)];
                Update(c =>
                {
                    c.OsdLayout = a.Layout;
                    if (a.Layout == "combined")
                    {
                        c.MediaCombinedSide = a.Side;
                        var (preset, device) = SettingsWindow.CombinedPosition(c);
                        SettingsWindow.ApplyCombinedPosition(c, preset, device);
                    }
                    c.SnapIndependent = false;
                });
                _stageHost.Content = StagePosition();
            };
            body.Children.Add(SettingCard("\uE8A9", T("Anordnung", "Arrangement"), null, arrange));

            var test = AccentBtn(T("Testen", "Test"), 92);
            test.Margin = new Thickness(0);
            test.Click += (_, _) =>
            {
                try
                {
                    var cfg = Cfg();
                    var media = MediaOsd.CreateTest(cfg);
                    var (osd, flyout) = Placement.Layout(cfg, media.MeasureHeight());
                    OsdCard.ShowTest(cfg, osd.TopLeft);
                    media.ShowTestAt(flyout.TopLeft);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Einführung, Position testen: {ex.Message}");
                }
            };
            body.Children.Add(SettingCard("\uE768", T("Ausprobieren", "Try it out"), T("Zeigt beides kurz am gewählten Platz", "Briefly shows both in the chosen place"), test));

            body.Children.Add(SettingCard("\uE7C2", T("Mit der Maus verschieben", "Drag with the mouse"), T("OSD und Flyout frei platzieren", "Place the OSD and flyout freely"),
                Toggle(Cfg().SnapEnabled, v => Update(c => c.SnapEnabled = v))));
            body.Children.Add(new Border { Height = 8 });
            body.Children.Add(Note(T("Gespeicherte Positionen schaltest du durch, indem du die Profil-Taste gedrückt hältst. Einrichten: Einstellungen → Position.",
                "Hold the profile key to cycle through saved positions. To set them up: Settings → Position.")));
        }

        // ── 9 · Fertig ──────────────────────────────────────────────────────

        private FrameworkElement StageDone()
        {
            var cfg = Cfg();
            var rows = new StackPanel();
            rows.Children.Add(new TextBlock
            {
                Text = T("Spickzettel", "Cheat sheet"), FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
                FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Fg, Margin = new Thickness(0, 0, 0, 12),
            });
            void Add(FrameworkElement left, string right)
            {
                var row = new Grid { MinHeight = 34 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                left.VerticalAlignment = VerticalAlignment.Center;
                left.HorizontalAlignment = HorizontalAlignment.Left;
                row.Children.Add(left);
                var text = new TextBlock { Text = right, FontSize = 13, Foreground = SubFg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                rows.Children.Add(row);
            }
            Add(KeyCap(T("Knopf drehen", "Turn knob")), T("Lauter und leiser", "Louder and quieter"));
            Add(KeyCap(T("Knopf drücken", "Press knob")), T("Stumm", "Mute"));
            Add(KeyCaps(cfg.ProfileHotkey), T("Nächstes Profil", "Next profile"));
            var hold = KeyCaps(cfg.ProfileHotkey);
            hold.Children.Add(new TextBlock { Text = T("halten", "hold"), FontSize = 12, Foreground = SubFg, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            Add(hold, T("Nächste Position", "Next position"));

            var tray = new StackPanel { Orientation = Orientation.Horizontal };
            tray.Children.Add(new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(5), Background = UiColor.Brush("#2B2B2F"),
                Child = new Image { Source = Tray.AppIcon.RenderTray(32, false), Width = 16, Height = 16 },
            });
            tray.Children.Add(new TextBlock { Text = T("Rechtsklick", "Right-click"), FontSize = 12, Foreground = SubFg, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            Add(tray, T("Einstellungen, Beenden", "Settings, exit"));

            return OnStage(new Border
            {
                Width = 392, Background = Surface, BorderBrush = SurfaceEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
                Padding = new Thickness(20, 16, 20, 14), Child = rows, Effect = Soft(),
            });
        }

        private void BodyDone(StackPanel body)
        {
            var cfg = Cfg();
            if (!cfg.Profiles.Values.Any(a => a.Count > 0))
            {
                var warn = Note(T("Noch keine App zugewiesen: Der Knopf tut erst etwas, wenn eine App in einem Profil ist. Klick hier, um das nachzuholen.",
                    "No app assigned yet: the knob only does something once an app is in a profile. Click here to do that now."), warn: true);
                warn.Cursor = System.Windows.Input.Cursors.Hand;
                warn.MouseLeftButtonUp += (_, _) => ShowPage(3, -1);
                body.Children.Add(warn);
            }

            body.Children.Add(Heading(T("Noch mehr entdecken", "Discover more")));
            var links = new StackPanel();
            links.Children.Add(LinkRow("\uE8D6", T("Spotify direkt regeln", "Control Spotify directly"), T("Spotifys eigener Regler über die Web-API", "Spotify's own slider via the Web API"), "spotify"));
            links.Children.Add(LinkRow("\uE714", T("YouTube im Browser", "YouTube in the browser"), T("Nur das Video regeln statt den ganzen Browser", "Control just the video instead of the whole browser"), "youtube"));
            links.Children.Add(LinkRow("\uE765", T("Alle Tastenkürzel", "All shortcuts"), T("Spul-Tasten, Gesten, Zuweisen-Tasten", "Skip keys, gestures, assign keys"), "hotkeys"));
            links.Children.Add(LinkRow("\uE713", T("Alle Einstellungen", "All settings"), T("Hier jetzt weitermachen", "Continue here now"), "general"));
            body.Children.Add(new Border
            {
                Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 6),
                Child = links,
            });

            body.Children.Add(SettingCard("\uE7E8", T("Mit Windows starten", "Start with Windows"), null,
                Toggle(Autostart.IsEnabled(), v =>
                {
                    try { Autostart.SetEnabled(v); }
                    catch (Exception ex) { Logger.Error($"Autostart: {ex.Message}"); }
                })));
            body.Children.Add(Hint(T("Ohne Häkchen bei „Nicht mehr anzeigen“ erscheint die Einführung wieder, wenn du die Einstellungen öffnest. Später findest du sie unter Einstellungen → Allgemein.",
                "Unless you tick “Don't show again”, the introduction comes back when you open the settings. You'll find it later under Settings → General.")));
        }

        /// <summary>Zeile, die die Einführung schließt und die Einstellungen auf dieser Seite öffnet.</summary>
        private Border LinkRow(string glyph, string title, string text, string page)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 15, Foreground = Fg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Fg });
            words.Children.Add(new TextBlock { Text = text, FontSize = 12, Foreground = SubFg, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(words, 1);
            row.Children.Add(words);
            var arrow = new TextBlock { Text = "\uE76C", FontFamily = IconFont, FontSize = 11, Foreground = SubFg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) };
            Grid.SetColumn(arrow, 2);
            row.Children.Add(arrow);

            var item = new Border { Background = Brushes.Transparent, CornerRadius = new CornerRadius(6), Padding = new Thickness(4, 5, 4, 5), Cursor = System.Windows.Input.Cursors.Hand, Child = row };
            item.MouseEnter += (_, _) => item.Background = HoverBg;
            item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
            item.MouseLeftButtonUp += (_, _) =>
            {
                RequestedSettingsPage = page;
                Close();
            };
            return item;
        }
    }
}
