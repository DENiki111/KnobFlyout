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
    /// Die Schritte der Einführung. Reihenfolge = was man braucht, damit der Knopf etwas tut (Knopf → Profile → Taste →
    /// eigene Profile → Apps), danach, was man sich einrichten kann (Flyout, Look, Platz, Snap-Punkte). Am Schluss die
    /// Wahl: zum Programm oder alle Funktionen ansehen (TutorialWindow.Features.cs).
    /// </summary>
    public sealed partial class TutorialWindow
    {
        private readonly SessionVolumeController _audio = new();

        // Live-Vorführung: Knopf, OSD-Karte und Flyout auf jeder Seite, die sie zeigt
        private string _liveName = "Spotify";
        private int _livePct = 64;
        private bool _liveMuted;
        private bool _liveReal;   // die Werte stammen von einer echten App
        private bool _fromKnob;   // letzte Eingabe kam vom echten Knopf (nicht von den Knöpfen unter der Vorschau)
        private bool _mediaReal;  // das Flyout zeigt echte Wiedergabe
        private bool _stageHasOsd, _stageHasMedia;
        private KnobView? _demoKnob;
        private Action<string, int, bool>? _demoCard;
        private Action<int>? _welcomeFollow;
        private TextBlock? _liveCaption;
        private Action? _liveMediaDispose;
        private StatusPill? _knobPill;

        // Seiten „Profile“ und „Profil-Taste“
        private StackPanel? _profilesHost;
        private StatusPill? _profilePill;
        private Border? _hotkeyCard;
        private StackPanel? _hotkeyDots;
        private StatusPill? _hotkeyPill;

        // Seite „Eigene Profile“
        private string? _editKey; // null = neues Profil
        private string _draftName = "";
        private string _draftGlyph = ProfileDefinitions.Gallery[4];
        private string _draftColor = ProfileDefinitions.Palette[4];
        private string _draftImage = "";
        private string _draftSystem = ""; // was das Profil regelt (ProfileEntry.SystemVolume)
        private string _draftDone = "";
        private bool _draftTouched;
        // Einmal ins Zuweisen-Popup geklickt: erst dann geht es nach dem Schritt „Von Hand einsortieren“ weiter.
        private bool _popupTried;

        private Border? _draftPreview;
        private UniformGrid? _profileTiles;

        // Seiten „Automatisch einsortieren“ und „Von Hand einsortieren“
        private ContentControl? _appsPopupHost;
        private string? _appsPopupApp;
        private StatusPill? _appsPill;
        private Action<bool>? _appsRender;
        private int _appsIndex;

        // Seite „Snap-Punkte“
        private StatusPill? _snapPill;

        // Unterseite „Eigenes Design“
        private DesignDraft? _designDraft; // Entwurf der Unterseite „Design anpassen“: gespeichert wird nur als eigenes Design
        private string _designTab = "template";
        private string _designTarget = "media"; // Flyout oder OSD
        private string _osdKind = OsdCardLayout.Volume; // OSD-Aufbau: gewählte Karte
        private readonly HashSet<string> _designExpanded = new();

        private void ClearStageFields()
        {
            DisposeLiveMedia();
            _demoKnob = null;
            _demoCard = null;
            _welcomeFollow = null;
            _liveCaption = null;
            _stageHasOsd = _stageHasMedia = false;
            _profilesHost = null;
            _hotkeyCard = null;
            _hotkeyDots = null;
            _draftPreview = null;
            _appsPopupHost = null;
            _lockHost = null;
        }

        private void ClearLiveFields()
        {
            ClearStageFields();
            _knobPill = null;
            _profilePill = null;
            _hotkeyPill = null;
            _profileTiles = null;
            _appsPill = null;
            _appsRender = null;
            _snapPill = null;
            _lockPill = null;
            _skipPill = null;
            _gesturePill = null;
            _draftDone = "";
        }

        private void DisposeLiveMedia()
        {
            _liveMediaDispose?.Invoke();
            _liveMediaDispose = null;
        }

        // ── Von der App: echte Tasten und ihre Wirkung ──────────────────────

        /// <summary>Der echte Knopf wurde gedreht oder gedrückt (die Wirkung kommt danach über OnVolumeResult/OnMuteResult).</summary>
        public void OnKnobKey() => _fromKnob = true;

        /// <summary>Lautstärke wurde geändert (Knopf oder Knöpfe unter der Vorschau); ohne passende App bewegt sich nur die Vorschau.</summary>
        public void OnVolumeResult(IReadOnlyList<(string Name, int Value)> results, int steps)
        {
            _liveReal = results.Count > 0;
            if (_liveReal)
            {
                _liveName = LiveName(results.Select(r => r.Name).ToList());
                _livePct = results[0].Value;
            }
            else _livePct = Math.Clamp(_livePct + steps * Math.Max(1, Cfg().Step), 0, 100);
            _liveMuted = false;
            PushLive();

            if (_liveReal) _knobPill?.Done(T($"Live: {_liveName} ist jetzt bei {_livePct} %", $"Live: {_liveName} is now at {_livePct} %"));
            else if (_fromKnob) _knobPill?.Done(T("Knopf erkannt! Nur die Vorschau bewegt sich: Im aktiven Profil spielt gerade nichts.",
                "Knob detected! Only the preview moves: nothing in the active profile is playing."));
            else _knobPill?.Done(T("Die Vorschau reagiert. Spielt eine zugewiesene App, ändert sich auch ihre Lautstärke.",
                "The preview reacts. When an assigned app plays, its volume changes too."));
            _fromKnob = false;
        }

        public void OnMuteResult(IReadOnlyList<(string Name, bool Muted)> results)
        {
            _liveReal = results.Count > 0;
            if (_liveReal)
            {
                _liveName = LiveName(results.Select(r => r.Name).ToList());
                _liveMuted = results[0].Muted;
            }
            else _liveMuted = !_liveMuted;
            PushLive();
            _knobPill?.Done(_liveMuted
                ? T("Stumm. Noch einmal drücken schaltet wieder laut.", "Muted. Press again to unmute.")
                : T("Wieder laut.", "Unmuted."));
            _fromKnob = false;
        }

        private static string LiveName(List<string> processes) =>
            AppRow.Label(processes[0]) + (processes.Count > 1 ? $" +{processes.Count - 1}" : "");

        /// <summary>Aktuellen Stand auf alles übertragen, was gerade auf der Bühne steht.</summary>
        private void PushLive()
        {
            _demoKnob?.Set(_livePct, _liveMuted, true);
            _demoCard?.Invoke(_liveName, _livePct, _liveMuted);
            _welcomeFollow?.Invoke(_livePct);
            if (_liveCaption != null) _liveCaption.Text = LiveCaption();
        }

        private string LiveCaption()
        {
            var parts = new List<string>();
            if (_stageHasOsd)
                parts.Add(_liveReal
                    ? T($"Live: {_liveName}", $"Live: {_liveName}")
                    : T("Dreh am Knopf, die Vorschau folgt.", "Turn the knob, the preview follows."));
            if (_stageHasMedia)
                parts.Add(_mediaReal
                    ? T("Live: deine Wiedergabe, die Knöpfe wirken.", "Live: what's playing, the buttons work.")
                    : T("Starte Musik, dann erscheint sie hier.", "Start some music and it shows up here."));
            return string.Join("\n", parts);
        }

        public void OnProfileHotkey()
        {
            var cfg = Cfg();
            if (_profilesHost != null) RenderProfiles(_profilesHost, cfg, PickProfile);
            if (_hotkeyCard != null) _hotkeyCard.Child = OsdCard.CreatePreview(cfg, "profile");
            if (_hotkeyDots != null) RenderProfileDots(_hotkeyDots, cfg);
            string name = ProfileDefinitions.For(cfg).FirstOrDefault(p => p.Key == cfg.ActiveProfile)?.Name ?? "";
            _profilePill?.Done(T($"Klappt: Jetzt ist „{name}“ aktiv", $"Works: “{name}” is now active"));
            _hotkeyPill?.Done(T($"Klappt: Jetzt ist „{name}“ aktiv", $"Works: “{name}” is now active"));
        }

        public void OnSnapCycle()
        {
            if (_snapPill == null) return;
            _snapPill.Done(T("Klappt: OSD und Flyout stehen am nächsten gespeicherten Platz.", "Works: the OSD and flyout are at the next saved place."));
            RefreshStage();
        }

        // ── Schritte ────────────────────────────────────────────────────────

        /// <summary>Lite-Modus: nur Willkommen, Media-Flyout, Look, Position und Alles bereit (keine Profile).</summary>
        private List<Page> ShownPages() => Cfg().LiteMode
            ? new[] { 0, 7, 8, 9, 10 }.Where(i => i < _allPages.Count).Select(i => _allPages[i]).ToList()
            : new List<Page>(_allPages);

        private List<Page> BuildPages()
        {
            var pages = new List<Page>
            {
                new("#3b82f6", () => T("Willkommen bei KnobFlyout", "Welcome to KnobFlyout"),
                    () => Cfg().LiteMode
                        ? T("Dein Knopf regelt die Windows-Lautstärke, dazu zeigt das Flyout, was läuft.", "Your knob controls the Windows volume, and the flyout shows what's playing.")
                        : T("Dein Knopf regelt nur noch die Apps, die du willst.", "Your knob now controls only the apps you want."),
                    StageWelcome, BodyWelcome),
                new("#0ea5e9", () => T("Drehen und drücken", "Turn and press"),
                    () => T("Drehen = Lautstärke, Drücken = stumm. Die Lautstärke-Tasten der Tastatur auch.", "Turn = volume, press = mute. Keyboard volume keys too."),
                    StageKnob, BodyKnob),
                new("#8b5cf6", () => T("Der Profil-Hotkey", "The profile key"),
                    () => T("Der Knopf regelt immer ein Profil, eine Gruppe von Apps. Dieser Hotkey wechselt es.", "The knob always controls one profile, a group of apps. This key switches it."),
                    StageHotkey, BodyHotkey),
                new("#a855f7", () => T("Profile: Gruppen von Apps", "Profiles: groups of apps"),
                    () => T("Nur das aktive Profil wird lauter oder leiser, z. B. das Spiel, Discord bleibt.", "Only the active profile gets louder or quieter, e.g. the game, Discord stays."),
                    StageProfiles, BodyProfiles),
                new("#ec4899", () => T("Eigene Profile", "Your own profiles"),
                    () => T("Bis zu 6 Profile mit Name, Logo und Farbe, alle änderbar.", "Up to 6 profiles with name, logo and color, all editable."),
                    StageCustom, BodyCustom),
                new("#22c55e", () => T("Automatisch einsortieren", "Sort automatically"),
                    () => T("Spiele, Browser, Musik und Chat landen von selbst im passenden Profil.", "Games, browsers, music and chat go to the right profile by themselves."),
                    StageAuto, BodyAuto),
                new("#10b981", () => T("Von Hand einsortieren", "Sort by hand"),
                    () => T("Unbekannte Apps fragen kurz, in welches Profil sie gehören.", "Unknown apps briefly ask which profile they belong to."),
                    StageApps, BodyApps),
                new("#14b8a6", () => T("Media-Flyout", "Media flyout"),
                    () => T("Zeigt bei jedem Titelwechsel, was läuft.", "Shows what's playing whenever the track changes."),
                    StageMedia, BodyMedia),
                new("#f97316", () => T("Such dir einen Look aus", "Pick a look"),
                    () => T("Ein Klick ändert OSD und Flyout. „Eigenes“: alles selbst gestalten.", "One click changes the OSD and flyout. “Custom”: design it all yourself."),
                    StageLook, BodyLook),
                new("#6366f1", () => T("Wo soll es erscheinen?", "Where should it appear?"),
                    () => T("Klick links auf einen Platz, beide erscheinen sofort dort.", "Click a place on the left, both appear there right away."),
                    StagePosition, BodyPosition),
                new("#3b82f6", () => T("Alles bereit", "You're all set"),
                    () => T("KnobFlyout läuft im Infobereich. Klick aufs Knopf-Symbol öffnet die Einstellungen.", "KnobFlyout runs in the notification area. Click the knob icon to open the settings."),
                    StageDone, BodyDone),
            };
            _appsIndex = 6;
            return pages;
        }

        // ── Live-Bausteine der Bühne ────────────────────────────────────────

        /// <summary>Stand der Apps lesen, die der Knopf gerade regeln würde; ohne solche App bleibt die Vorführung.</summary>
        private void ReadLive(AppConfig cfg)
        {
            try
            {
                _audio.InvalidateCache();
                var levels = _audio.ReadVolumes(cfg);
                _liveReal = levels.Count > 0;
                if (!_liveReal) return;
                _liveName = LiveName(levels.Select(l => l.Name).ToList());
                _livePct = levels[0].Value;
            }
            catch (Exception ex)
            {
                _liveReal = false;
                Logger.Error($"Einführung, Lautstärke lesen: {ex.Message}");
            }
        }

        /// <summary>OSD-Karte, die dem Knopf folgt.</summary>
        private FrameworkElement LiveOsd(AppConfig cfg)
        {
            ReadLive(cfg);
            _stageHasOsd = true;
            var card = OsdCard.CreateLiveVolumePreview(cfg, _liveName, _livePct, _liveMuted, out _demoCard);
            card.Effect = Soft();
            card.HorizontalAlignment = HorizontalAlignment.Center;
            return card;
        }

        /// <summary>Echtes Flyout mit der laufenden Wiedergabe.</summary>
        private FrameworkElement LiveFlyout(AppConfig cfg)
        {
            DisposeLiveMedia();
            _stageHasMedia = true;
            _mediaReal = _media.HasSession;
            var flyout = MediaOsd.CreateLive(cfg, _media, real =>
            {
                _mediaReal = real;
                if (_liveCaption != null) _liveCaption.Text = LiveCaption();
            }, out var dispose);
            _liveMediaDispose = dispose;
            flyout.Effect = Soft();
            flyout.HorizontalAlignment = HorizontalAlignment.Center;
            return flyout;
        }

        /// <summary>Leiser, Stumm, Lauter: wirkt wie der echte Knopf, auch ohne einen zur Hand zu haben.</summary>
        private FrameworkElement LiveControls()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
            void Key(string glyph, string tip, Action click)
            {
                var b = Btn(glyph, 56);
                b.FontFamily = IconFont;
                b.FontSize = 15;
                b.Margin = new Thickness(3, 0, 3, 0);
                b.Padding = new Thickness(0, 6, 0, 6);
                b.ToolTip = tip;
                b.Focusable = false; // Enter bleibt bei „Weiter“
                b.Click += (_, _) => click();
                row.Children.Add(b);
            }
            void Turn(int dir)
            {
                if (VolumeRequested != null) VolumeRequested(dir);
                else OnVolumeResult(Array.Empty<(string, int)>(), dir);
            }
            Key("\uE738", T("Leiser", "Quieter"), () => Turn(-1));   // Remove
            Key("\uE74F", T("Stumm", "Mute"), () =>                   // Mute
            {
                if (MuteRequested != null) MuteRequested();
                else OnMuteResult(Array.Empty<(string, bool)>());
            });
            Key("\uE710", T("Lauter", "Louder"), () => Turn(+1));     // Add
            return row;
        }

        /// <summary>Bühne mit lebendem Flyout und/oder lebender OSD-Karte samt Knöpfen und Beschriftung.</summary>
        private FrameworkElement StageLive(bool osd = true, bool media = true, AppConfig? shown = null)
        {
            var cfg = shown ?? Cfg();
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            if (media) panel.Children.Add(LiveFlyout(cfg));
            if (osd)
            {
                var card = LiveOsd(cfg);
                if (media) card.Margin = new Thickness(0, 14, 0, 0);
                panel.Children.Add(card);
                panel.Children.Add(LiveControls());
            }
            var fit = new Viewbox { MaxWidth = 420, MaxHeight = 440, StretchDirection = StretchDirection.DownOnly, Child = panel };
            var stage = OnStage(fit, LiveCaption(), out _liveCaption);
            return stage;
        }

        // ── 1 · Willkommen ──────────────────────────────────────────────────

        private FrameworkElement StageWelcome()
        {
            var knob = new KnobView(HardwareParts.Colors(Light), UiColor.Brush("#3b82f6"), UiColor.Brush("#ef4444"), 64);
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(new Viewbox { Width = 176, Height = 176, Child = knob, Margin = new Thickness(0, 0, 0, 20) });

            var rows = new StackPanel();
            var (musicFill, musicPct) = DemoRow(rows, "\uE8D6", "Spotify", 50, true);
            DemoRow(rows, "\uE7FC", T("Spiel", "Game"), 72, false);
            DemoRow(rows, "\uE720", "Discord", 86, false);
            bool lite = Cfg().LiteMode;
            if (!lite) // die einzelnen Apps gibt es nur mit Profilen
                panel.Children.Add(new Border
                {
                    Width = 340, Background = Surface, BorderBrush = SurfaceEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(16, 12, 16, 8), Child = rows, Effect = Soft(),
                });

            // Der Knopf dreht, nur eine App folgt: das ist die ganze Idee. Dreht man selbst, übernimmt der echte Knopf.
            var started = DateTime.UtcNow;
            bool manual = false;
            void Show(int pct, bool animate)
            {
                knob.Set(pct, false, animate);
                musicFill.ScaleX = pct / 100.0;
                musicPct.Text = pct + " %";
            }
            void Tick()
            {
                if (manual) return;
                double t = (DateTime.UtcNow - started).TotalSeconds;
                Show((int)Math.Round(52 + 30 * Math.Sin(t * 0.9)), false);
            }
            _welcomeFollow = pct =>
            {
                manual = true;
                Show(pct, true);
            };
            Tick();
            StartPageTimer(TimeSpan.FromMilliseconds(40), Tick);
            return OnStage(panel, lite ? T("Dreh ruhig selbst.", "Go ahead and turn it.") : T("Nur die Musik ändert sich. Dreh ruhig selbst.", "Only the music changes. Go ahead and turn it."));
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
            if (Cfg().LiteMode)
                body.Children.Add(Feature("\uE767", T("Windows-Lautstärke", "Windows volume"), T("Der Knopf regelt Windows, mit OSD oder ohne", "The knob controls Windows, with or without OSD")));
            else
                body.Children.Add(Feature("\uE716", T("Profile", "Profiles"), T("Gruppen von Apps, per Hotkey wechseln", "Groups of apps, switched by key")));
            body.Children.Add(Feature("\uE7F4", T("OSD und Media-Flyout", "OSD and media flyout"), T("Lautstärke und Titel, wie und wo du willst", "Volume and track, how and where you like")));
            body.Children.Add(Feature("\uE768", T("Live ausprobieren", "Try it live"), T("Die Vorschauen folgen deinem Knopf", "The previews follow your knob")));

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

            // Voll oder Lite (nur Windows-Lautstärke + Flyout); ändert die Schritte der Einführung
            var mode = Segments(new[] { ("full", T("Voll", "Full")), ("lite", "Lite") }, Cfg().LiteMode ? "lite" : "full", key =>
            {
                Update(c => c.LiteMode = key == "lite");
                Dispatcher.BeginInvoke(new Action(Rebuild));
            });
            body.Children.Add(SettingCard("\uE945", T("Modus", "Mode"), // LightningBolt
                T("Lite: Knopf regelt nur die Windows-Lautstärke, keine Profile", "Lite: the knob only controls the Windows volume, no profiles"), mode));
            if (Cfg().LiteMode)
                body.Children.Add(SettingCard("\uE7F4", T("OSD anzeigen", "Show OSD"), T("Aus: nur das Media-Flyout", "Off: only the media flyout"),
                    Toggle(Cfg().Osd, v => Update(c => c.Osd = v))));
        }

        // ── 2 · Knopf ───────────────────────────────────────────────────────

        private FrameworkElement StageKnob()
        {
            var cfg = Cfg();
            _demoKnob = new KnobView(HardwareParts.Colors(Light), UiColor.Brush("#3b82f6"), UiColor.Brush("#ef4444"), 64);
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(new Viewbox { Width = 150, Height = 150, Child = _demoKnob, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 20) });
            panel.Children.Add(LiveOsd(cfg));
            panel.Children.Add(LiveControls());
            _demoKnob.Set(_livePct, _liveMuted, false);
            return OnStage(panel, LiveCaption(), out _liveCaption);
        }

        private void BodyKnob(StackPanel body)
        {
            _knobPill = new StatusPill(T("Probier es: Knopf drehen oder − / + links", "Try it: turn the knob or use − / + on the left"));
            body.Children.Add(_knobPill);

            body.Children.Add(SettingCard("\uE767", T("Schrittweite", "Step size"), null,
                SliderControl(1, 15, Cfg().Step, "0' %'", v => Update(c => c.Step = (int)v, notify: false), tick: 1, width: 120)));

            body.Children.Add(new Border { Height = 8 });
            body.Children.Add(Note(T("Die Tasten regeln nicht mehr die Gesamtlautstärke, nur noch das aktive Profil.",
                "The keys no longer control the overall volume, only the active profile."), warn: true));
            body.Children.Add(Note(T("Reagiert nichts? Vor Admin-Fenstern gibt Windows die Tasten nicht weiter.",
                "Nothing happens? Windows doesn't pass on keys while an admin window is in front.")));
        }

        // ── 4 · Profile ─────────────────────────────────────────────────────

        private FrameworkElement StageProfiles()
        {
            _profilesHost = new StackPanel { Width = 384 };
            RenderProfiles(_profilesHost, Cfg(), PickProfile);
            return OnStage(_profilesHost, T("Deine Profile. Klick macht eins aktiv.", "Your profiles. Click one to make it active."));
        }

        private void PickProfile(string key)
        {
            Update(c => c.ActiveProfile = key);
            ProfileOsdRequested?.Invoke();
            OnProfileHotkey();
        }

        /// <summary>Die echten Profile mit ihren Apps; das aktive ist hervorgehoben, Klick macht ein Profil aktiv.</summary>
        private static void RenderProfiles(StackPanel host, AppConfig cfg, Action<string> pick)
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
                string key = p.Key;
                var card = new Border
                {
                    Background = Surface, BorderBrush = active ? UiColor.Brush(p.Color) : SurfaceEdge, BorderThickness = new Thickness(active ? 2 : 1),
                    CornerRadius = new CornerRadius(12), Padding = active ? new Thickness(11, 7, 11, 7) : new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 8), Child = row, Effect = active ? Soft() : null, Cursor = System.Windows.Input.Cursors.Hand,
                };
                card.MouseLeftButtonUp += (_, _) => pick(key);
                host.Children.Add(card);
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
            _profilePill = new StatusPill(T("Probier es: Klick links auf ein Profil", "Try it: click a profile on the left"));
            body.Children.Add(_profilePill);
            AddProfileModeCards(body);
        }

        /// <summary>Gesteuerte Apps und Leere überspringen (Seite „Profile“ und „Alle Funktionen“).</summary>
        private void AddProfileModeCards(StackPanel body)
        {
            var cfg = Cfg();
            var mode = Combo(170, cfg.ProfileMode == "assigned" ? 1 : 0, T("Nur Apps mit Ton", "Only apps playing sound"), T("Alle Apps im Profil", "All apps in the profile"));
            mode.SelectionChanged += (_, _) => Update(c => c.ProfileMode = mode.SelectedIndex == 1 ? "assigned" : "active");
            body.Children.Add(SettingCard("\uE8AB", T("Gesteuerte Apps", "Controlled apps"), null, mode));
            body.Children.Add(SettingCard("\uE893", T("Leere Profile überspringen", "Skip empty profiles"), null,
                Toggle(cfg.SkipEmptyProfiles, v => Update(c => c.SkipEmptyProfiles = v))));
        }

        // ── 3 · Profil-Taste ────────────────────────────────────────────────

        private FrameworkElement StageHotkey()
        {
            var cfg = Cfg();
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var caps = KeyCaps(cfg.ProfileHotkey);
            caps.HorizontalAlignment = HorizontalAlignment.Center;
            caps.LayoutTransform = new ScaleTransform(1.35, 1.35);
            caps.Margin = new Thickness(0, 0, 0, 24);
            panel.Children.Add(caps);
            _hotkeyCard = new Border { Child = OsdCard.CreatePreview(cfg, "profile"), Effect = Soft(), HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(_hotkeyCard);
            _hotkeyDots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0) };
            RenderProfileDots(_hotkeyDots, cfg);
            panel.Children.Add(_hotkeyDots);
            return OnStage(panel, T("Reagiert live", "Reacts live"));
        }

        /// <summary>Alle Profile als kleine Kreise in Wechsel-Reihenfolge; das aktive leuchtet.</summary>
        private static void RenderProfileDots(StackPanel host, AppConfig cfg)
        {
            host.Children.Clear();
            foreach (var p in ProfileDefinitions.For(cfg))
            {
                bool active = p.Key == cfg.ActiveProfile;
                host.Children.Add(new Border
                {
                    Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Margin = new Thickness(4, 0, 4, 0), Background = UiColor.Brush(p.Color),
                    Opacity = active ? 1 : 0.35, BorderThickness = new Thickness(active ? 2 : 0), BorderBrush = Fg, Child = p.CreateLogo(16, Brushes.White), ToolTip = p.Name,
                });
            }
        }

        private void BodyHotkey(StackPanel body)
        {
            string hotkey = Cfg().ProfileHotkey;
            string Wait(string combo) => T($"Probier es: Drück {FormatHotkey(combo)}", $"Try it: press {FormatHotkey(combo)}");
            _hotkeyPill = new StatusPill(Wait(hotkey));
            body.Children.Add(_hotkeyPill);
            body.Children.Add(SettingCard("\uE765", T("Hotkey zum Wechseln", "Key to switch"), T("Klicken, dann Taste drücken", "Click, then press a key"),
                HotkeyCapture(hotkey, v =>
                {
                    Update(c => c.ProfileHotkey = v);
                    _hotkeyPill?.Waiting(Wait(v));
                    RefreshStage();
                }, allowClear: false, format: FormatHotkey, width: 176)));
            body.Children.Add(new Border { Height = 10 });
            body.Children.Add(Feature("\uE893", T("Kurz drücken", "Tap"), T("Nächstes Profil", "Next profile")));
            body.Children.Add(Feature("\uE81D", T("Lange halten", "Hold"), T("Nächste gespeicherte Position", "Next saved position")));
        }

        // ── 5 · Eigene Profile ──────────────────────────────────────────────

        private FrameworkElement StageCustom()
        {
            var cfg = Cfg();
            if (_editKey != null && cfg.ProfileList.All(p => p.Key != _editKey)) _editKey = null;
            if (_editKey == null && !_draftTouched)
            {
                _draftGlyph = ProfileDefinitions.Gallery.FirstOrDefault(g => cfg.ProfileList.All(p => p.Glyph != g)) ?? _draftGlyph;
                _draftColor = ProfileDefinitions.Palette.FirstOrDefault(c => cfg.ProfileList.All(p => !string.Equals(p.Color, c, StringComparison.OrdinalIgnoreCase))) ?? _draftColor;
                _draftImage = "";
            }
            _draftPreview = new Border { HorizontalAlignment = HorizontalAlignment.Center, Effect = Soft() };
            RenderDraft();
            return OnStage(_draftPreview, T("Vorschau: So erscheint das Profil beim Wechseln", "Preview: this is how the profile appears when switching"));
        }

        private void RenderDraft()
        {
            if (_draftPreview == null) return;
            var cfg = Clone(Cfg());
            string name = _draftName.Trim().Length > 0 ? _draftName.Trim() : T("Mein Profil", "My profile");
            var entry = cfg.ProfileList.FirstOrDefault(p => p.Key == _editKey);
            if (entry == null)
            {
                entry = new ProfileEntry { Key = "tutorial_draft" };
                cfg.ProfileList = cfg.ProfileList.Take(ProfileDefinitions.MaxProfiles - 1).Append(entry).ToList();
            }
            entry.Name = name;
            entry.Glyph = _draftGlyph;
            entry.Color = _draftColor;
            entry.Image = _draftImage;
            entry.Emoji = "";
            cfg.ActiveProfile = entry.Key;
            _draftPreview.Child = OsdCard.CreatePreview(cfg, "profile");
        }

        /// <summary>Änderung am gerade bearbeiteten Profil sofort speichern.</summary>
        private void EditProfile(Action<ProfileEntry> change, bool notify = true)
        {
            string? key = _editKey;
            if (key == null) return;
            Update(c =>
            {
                var e = c.ProfileList.FirstOrDefault(p => p.Key == key);
                if (e != null) change(e);
            }, notify);
        }

        private void DeleteProfile(string key)
        {
            string name = ProfileDefinitions.For(Cfg()).FirstOrDefault(p => p.Key == key)?.Name ?? "";
            Update(c =>
            {
                if (c.ProfileList.Count <= 1) return;
                c.ProfileList.RemoveAll(p => p.Key == key);
                c.Profiles.Remove(key); // zugewiesene Apps werden frei
                if (c.ActiveProfile == key) c.ActiveProfile = c.ProfileList[0].Key;
            });
            if (_editKey == key)
            {
                _editKey = null;
                _draftName = "";
                _draftTouched = false;
            }
            _draftDone = T($"„{name}“ ist gelöscht. Seine Apps sind wieder frei.", $"“{name}” has been deleted. Its apps are free again.");
            RefreshPage();
        }

        private void RenderProfileTiles()
        {
            if (_profileTiles == null) return;
            _profileTiles.Children.Clear();
            var cfg = Cfg();
            var defs = ProfileDefinitions.For(cfg);
            foreach (var def in defs)
            {
                string key = def.Key;
                bool selected = key == _editKey;
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new Border
                {
                    Width = 26, Height = 26, CornerRadius = new CornerRadius(7), Background = UiColor.Brush(def.Color), Child = def.CreateLogo(13, Brushes.White),
                    VerticalAlignment = VerticalAlignment.Center,
                });
                var label = new TextBlock { Text = def.Name, FontSize = 12, Foreground = Fg, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 2, 0) };
                Grid.SetColumn(label, 1);
                row.Children.Add(label);
                var delete = FlatIconButton("\uE711", T("Profil löschen (seine Apps werden frei)", "Delete profile (its apps become free)"), () => DeleteProfile(key)); // Cancel
                delete.Width = delete.Height = 24;
                delete.FontSize = 10;
                delete.Margin = new Thickness(0);
                delete.IsEnabled = defs.Count > 1;
                delete.Focusable = false;
                Grid.SetColumn(delete, 2);
                row.Children.Add(delete);

                var tile = new Border
                {
                    Background = CardBg, BorderBrush = selected ? Accent : CardBorder, BorderThickness = new Thickness(selected ? 2 : 1), CornerRadius = new CornerRadius(8),
                    Padding = selected ? new Thickness(6, 5, 3, 5) : new Thickness(7, 6, 4, 6), Margin = new Thickness(0, 0, 5, 5), Cursor = System.Windows.Input.Cursors.Hand, Child = row,
                    ToolTip = T("Klicken zum Bearbeiten", "Click to edit"),
                };
                if (!selected)
                {
                    tile.MouseEnter += (_, _) => Motion.Fade(tile, HoverBg);
                    tile.MouseLeave += (_, _) => Motion.Fade(tile, CardBg);
                }
                tile.MouseLeftButtonUp += (_, _) =>
                {
                    var entry = Cfg().ProfileList.FirstOrDefault(p => p.Key == key);
                    if (entry == null) return;
                    _editKey = key;
                    _draftName = ProfileDefinitions.DisplayName(entry);
                    _draftGlyph = string.IsNullOrEmpty(entry.Glyph) ? ProfileDefinitions.Gallery[0] : entry.Glyph;
                    _draftColor = entry.Color;
                    _draftImage = entry.Image;
                    _draftSystem = entry.SystemVolume;
                    _draftDone = "";
                    RefreshPage();
                };
                _profileTiles.Children.Add(tile);
            }
            if (defs.Count >= ProfileDefinitions.MaxProfiles) return;

            var addRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            addRow.Children.Add(new TextBlock { Text = "\uE710", FontFamily = IconFont, FontSize = 12, Foreground = Fg, VerticalAlignment = VerticalAlignment.Center });
            addRow.Children.Add(new TextBlock { Text = T("Neues Profil", "New profile"), FontSize = 12, Foreground = Fg, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            bool newSelected = _editKey == null;
            var add = new Border
            {
                Background = Brushes.Transparent, BorderBrush = newSelected ? Accent : FieldBorder, BorderThickness = new Thickness(newSelected ? 2 : 1), CornerRadius = new CornerRadius(8),
                MinHeight = 38, Margin = new Thickness(0, 0, 5, 5), Cursor = System.Windows.Input.Cursors.Hand, Child = addRow,
            };
            add.MouseLeftButtonUp += (_, _) =>
            {
                _editKey = null;
                _draftName = "";
                _draftSystem = "";
                _draftTouched = false;
                _draftDone = "";
                RefreshPage();
            };
            _profileTiles.Children.Add(add);
        }

        private void BodyCustom(StackPanel body)
        {
            _profileTiles = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -5, 7) };
            RenderProfileTiles();
            body.Children.Add(_profileTiles);

            var cfg = Cfg();
            bool editing = _editKey != null && cfg.ProfileList.Any(p => p.Key == _editKey);
            bool full = cfg.ProfileList.Count >= ProfileDefinitions.MaxProfiles;
            var form = new StackPanel();

            form.Children.Add(new TextBlock { Text = T("Name", "Name"), FontSize = 12, Foreground = SubFg, Margin = new Thickness(0, 0, 0, 4) });
            var name = TextField(_draftName);
            name.Margin = new Thickness(0, 0, 0, 6);
            name.MaxLength = 24;
            name.TextChanged += (_, _) =>
            {
                _draftName = name.Text;
                RenderDraft();
                if (!editing) return;
                EditProfile(e => e.Name = _draftName.Trim(), notify: false);
                RenderProfileTiles();
            };
            name.LostFocus += (_, _) => { if (editing) ConfigChanged?.Invoke(); };
            form.Children.Add(name);

            form.Children.Add(new TextBlock { Text = "Logo", FontSize = 12, Foreground = SubFg, Margin = new Thickness(0, 0, 0, 4) });
            var logos = new UniformGrid { Columns = 11, Margin = new Thickness(-2, 0, -2, 4) };
            var logoTiles = new List<(Border Tile, string Glyph, TextBlock Text)>();
            void MarkLogos()
            {
                foreach (var (tile, glyph, text) in logoTiles)
                {
                    bool on = _draftImage.Length == 0 && glyph == _draftGlyph;
                    tile.Background = on ? FromHex(_draftColor) : Brushes.Transparent;
                    text.Foreground = on ? Brushes.White : Fg;
                }
            }
            var gallery = ProfileDefinitions.Gallery.Take(22).ToList();
            if (!gallery.Contains(_draftGlyph) && ProfileDefinitions.Gallery.Contains(_draftGlyph)) gallery[^1] = _draftGlyph;
            foreach (var glyph in gallery)
            {
                string g = glyph;
                var text = new TextBlock { Text = g, FontFamily = IconFont, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var tile = new Border { Height = 30, Margin = new Thickness(1), CornerRadius = new CornerRadius(6), Cursor = System.Windows.Input.Cursors.Hand, Child = text };
                tile.MouseEnter += (_, _) => { if (g != _draftGlyph) Motion.Fade(tile, HoverBg); };
                tile.MouseLeave += (_, _) => { if (g != _draftGlyph) Motion.Fade(tile, Brushes.Transparent); };
                tile.MouseLeftButtonUp += (_, _) =>
                {
                    _draftGlyph = g;
                    _draftImage = "";
                    _draftTouched = true;
                    EditProfile(e =>
                    {
                        e.Glyph = g;
                        e.Image = "";
                        e.Emoji = ""; // geändertes Logo gilt auch im Design „Eigenes“
                    });
                    MarkLogos();
                    RenderDraft();
                    RenderProfileTiles();
                };
                logoTiles.Add((tile, g, text));
                logos.Children.Add(tile);
            }
            form.Children.Add(logos);

            form.Children.Add(new TextBlock { Text = T("Farbe", "Color"), FontSize = 12, Foreground = SubFg, Margin = new Thickness(0, 0, 0, 5) });
            var colors = new StackPanel { Orientation = Orientation.Horizontal };
            var swatches = new List<(Border Swatch, string Hex)>();
            void MarkColors()
            {
                foreach (var (swatch, hex) in swatches)
                    swatch.BorderBrush = string.Equals(hex, _draftColor, StringComparison.OrdinalIgnoreCase) ? Fg : Brushes.Transparent;
            }
            foreach (var hex in ProfileDefinitions.Palette)
            {
                string h = hex;
                var swatch = new Border
                {
                    Width = 28, Height = 28, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(2), Margin = new Thickness(0, 0, 6, 0),
                    Cursor = System.Windows.Input.Cursors.Hand, Padding = new Thickness(2),
                    Child = new Border { CornerRadius = new CornerRadius(10), Background = FromHex(h) },
                };
                swatch.MouseLeftButtonUp += (_, _) =>
                {
                    _draftColor = h;
                    _draftTouched = true;
                    EditProfile(e => e.Color = h);
                    MarkColors();
                    MarkLogos();
                    RenderDraft();
                    RenderProfileTiles();
                };
                swatches.Add((swatch, h));
                colors.Children.Add(swatch);
            }
            form.Children.Add(colors);

            // Was der Knopf bei diesem Profil regelt: Apps oder die Windows-Lautstärke
            var routes = SettingsWindow.ProfileRoutes();
            var route = Combo(330, Math.Max(0, Array.FindIndex(routes, r => r.Key == _draftSystem)), routes.Select(r => r.Label).ToArray());
            route.Margin = new Thickness(0, 4, 0, 0);
            route.HorizontalAlignment = HorizontalAlignment.Left;
            route.SelectionChanged += (_, _) =>
            {
                _draftSystem = routes[Math.Max(0, route.SelectedIndex)].Key;
                EditProfile(e => e.SystemVolume = _draftSystem);
            };
            form.Children.Add(route);

            if (!editing && !full)
            {
                var create = AccentBtn(T("Profil anlegen", "Create profile"));
                create.Margin = new Thickness(0, 8, 0, 0);
                create.HorizontalAlignment = HorizontalAlignment.Left;
                create.Padding = new Thickness(16, 6, 16, 6);
                create.Click += (_, _) =>
                {
                    string finalName = _draftName.Trim().Length > 0 ? _draftName.Trim() : T("Mein Profil", "My profile");
                    var entry = new ProfileEntry { Key = ProfileDefinitions.NewKey(), Name = finalName, Glyph = _draftGlyph, Color = _draftColor, SystemVolume = _draftSystem };
                    Update(c =>
                    {
                        if (c.ProfileList.Count >= ProfileDefinitions.MaxProfiles) return;
                        c.ProfileList.Add(entry);
                        c.Profiles[entry.Key] = new List<string>();
                    });
                    _draftName = "";
                    _draftTouched = false;
                    _draftDone = T($"„{finalName}“ ist angelegt.", $"“{finalName}” has been created.");
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
            }

            MarkLogos();
            MarkColors();
            body.Children.Add(new Border
            {
                Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 12, 14, 14), Margin = new Thickness(0, 0, 0, 6), Child = form,
            });

            if (_draftDone.Length > 0)
            {
                var pill = new StatusPill("");
                pill.Done(_draftDone);
                body.Children.Add(pill);
            }
            else if (!editing && full)
                body.Children.Add(Note(T("Du hast schon 6 Profile, mehr gehen nicht. Lösch eins mit ✕, wenn du Platz brauchst.",
                    "You already have 6 profiles, the maximum. Delete one with ✕ if you need room.")));
            else
                body.Children.Add(Hint(editing
                    ? T("Gilt sofort. Eigene Bilder und die Reihenfolge: Einstellungen → Profile & Apps.", "Applies right away. Your own images and the order: Settings → Profiles & apps.")
                    : T("Klick bearbeitet ein Profil, ✕ löscht es. Eins muss bleiben.", "Click to edit a profile, ✕ deletes it. One has to stay.")));
        }

        // ── 6 · Automatisch einsortieren ────────────────────────────────────

        /// <summary>Welche Art von App wohin kommt, als Überblick; folgt der Auswahl rechts.</summary>
        private FrameworkElement StageAuto()
        {
            var cfg = Cfg();
            var defs = ProfileDefinitions.For(cfg);
            var panel = new StackPanel { Width = 350, Opacity = cfg.AutoAssign ? 1 : 0.45 };
            foreach (var (category, glyph, label) in new[]
            {
                (AppClassifier.Games, "\uE7FC", T("Spiele", "Games")),
                (AppClassifier.Browsers, "\uE774", T("Browser und Video", "Browsers and video")),
                (AppClassifier.Music, "\uE8D6", T("Musik", "Music")),
                (AppClassifier.Comms, "\uE720", T("Kommunikation", "Communication")),
            })
            {
                string key = cfg.AutoAssignMap.TryGetValue(category, out var mapped) ? mapped : AppClassifier.DefaultProfile(category);
                var profile = defs.FirstOrDefault(p => p.Key == key);

                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var kind = new StackPanel { Orientation = Orientation.Horizontal };
                kind.Children.Add(new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 16, Foreground = Fg, Width = 26, VerticalAlignment = VerticalAlignment.Center });
                kind.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = Fg, TextWrapping = TextWrapping.Wrap, MaxWidth = 112, VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(kind);

                var arrow = new TextBlock { Text = "\uE72A", FontFamily = IconFont, FontSize = 12, Foreground = SubFg, VerticalAlignment = VerticalAlignment.Center }; // Forward
                Grid.SetColumn(arrow, 1);
                row.Children.Add(arrow);

                var target = new StackPanel { Orientation = Orientation.Horizontal };
                if (profile != null)
                    target.Children.Add(new Border
                    {
                        Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Background = UiColor.Brush(profile.Color), Margin = new Thickness(0, 0, 9, 0),
                        Child = profile.CreateLogo(15, Brushes.White),
                    });
                target.Children.Add(new TextBlock
                {
                    Text = profile?.Name ?? T("Nachfragen", "Ask"), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Fg,
                    TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 92, VerticalAlignment = VerticalAlignment.Center,
                });
                Grid.SetColumn(target, 2);
                row.Children.Add(target);

                panel.Children.Add(new Border
                {
                    Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14, 9, 12, 9), Margin = new Thickness(0, 0, 0, 7), MinHeight = 50, Child = row, Effect = Soft(),
                });
            }
            return OnStage(panel, cfg.AutoAssign
                ? T("So landen neue Apps von selbst im richtigen Profil.", "This is how new apps end up in the right profile by themselves.")
                : T("Aus: Neue Apps sortierst du selbst ein.", "Off: you sort new apps yourself."));
        }

        private void BodyAuto(StackPanel body)
        {
            _appsPill = new StatusPill(T("Probier es: Starte eine App ohne Profil", "Try it: start an app without a profile"));
            body.Children.Add(_appsPill);
            AddAutoAssignCards(body);
            body.Children.Add(new Border { Height = 8 });
            body.Children.Add(Note(T("Gilt nur für neue Apps. Bei „Nachfragen“ und unbekannten Apps fragt ein Popup, das kommt im nächsten Schritt.",
                "Only for new apps. With “Ask” and unknown apps a popup asks, that's the next step.")));
        }

        // ── 7 · Von Hand einsortieren ────────────────────────────────────

        private FrameworkElement StageApps()
        {
            _appsPopupHost = new ContentControl { HorizontalAlignment = HorizontalAlignment.Center, Focusable = false };
            RenderAppsPopup();
            var color = _popupTried ? Color.FromRgb(0x22, 0xC5, 0x5E) : ((SolidColorBrush)Accent).Color;
            var ringBrush = new SolidColorBrush(color);
            if (!_popupTried) // pulsiert, bis einmal geklickt wurde
                ringBrush.BeginAnimation(Brush.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(new TextBlock
            {
                Text = T("So erscheint es, wenn eine App zum ersten Mal Ton abspielt:", "This is how it appears when an app plays sound for the first time:"),
                FontSize = 13, Foreground = SubFg, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 360, Margin = new Thickness(0, 0, 0, 14),
            });
            panel.Children.Add(new Border
            {
                BorderBrush = ringBrush, BorderThickness = new Thickness(2.5), CornerRadius = new CornerRadius(20), Padding = new Thickness(8),
                HorizontalAlignment = HorizontalAlignment.Center, Child = _appsPopupHost,
            });
            panel.Children.Add(new TextBlock
            {
                Text = _popupTried ? T("✓ Erledigt", "✓ Done") : T("↑ Hier auf ein Logo klicken", "↑ Click a logo here"),
                FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(color),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0),
            });
            return OnStage(panel, T("Echt, wenn gerade eine App ohne Profil spielt, sonst ein Beispiel.",
                "Real if an app without a profile is playing, otherwise an example."));
        }

        private List<string> RunningApps()
        {
            try
            {
                return _audio.GetSessions()
                    .Where(s => s.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive)
                    .Select(s => s.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception ex)
            {
                Logger.Error($"Einführung, Apps: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>Echtes Popup: für die erste spielende App ohne Profil, sonst als Beispiel mit Discord.</summary>
        private void RenderAppsPopup()
        {
            if (_appsPopupHost == null) return;
            var cfg = Cfg();
            string? app = RunningApps().FirstOrDefault(p => AppRow.CurrentProfile(cfg, p) == null
                && !cfg.Exclude.Contains(p, StringComparer.OrdinalIgnoreCase));
            if (_appsPopupHost.Content != null && app == _appsPopupApp) return;
            _appsPopupApp = app;
            var popup = NewAppOsd.CreateLivePreview(cfg, app ?? "Discord.exe", key => OnPopupChoice(app, key));
            popup.Effect = Soft();
            _appsPopupHost.Content = popup;
        }

        private void OnPopupChoice(string? app, string? profileKey)
        {
            _popupTried = true;
            if (_subPages.Count == 0) _next.IsEnabled = true;
            var fresh = Cfg();
            string target = profileKey == null ? "" : ProfileDefinitions.For(fresh).FirstOrDefault(p => p.Key == profileKey)?.Name ?? "";
            string label = AppRow.Label(app ?? "Discord.exe");
            if (app != null)
            {
                if (profileKey == null) AppRow.ToggleExclude(fresh, app);
                else AppRow.Assign(fresh, app, profileKey);
                if (!fresh.SeenApps.Contains(app.ToLowerInvariant())) fresh.SeenApps.Add(app.ToLowerInvariant());
                ConfigManager.Save(fresh);
                ConfigChanged?.Invoke();
            }
            string example = app == null ? T(" (nur ein Beispiel)", " (just an example)") : "";
            _appsPill?.Done(profileKey == null
                ? T($"Geschafft! „{label}“ wird ignoriert{example}: Der Knopf regelt es nie.", $"Done! “{label}” is ignored{example}: the knob never controls it.")
                : T($"Geschafft! „{label}“ ist in „{target}“{example}. Ist „{target}“ aktiv, regelt der Knopf {label}.", $"Done! “{label}” is in “{target}”{example}. While “{target}” is active, the knob controls {label}."));
            RefreshStage();
            if (_appsRender != null) _appsRender(true);
            else if (app != null) RefreshPage(); // „Alle Funktionen“: Seite mit der Liste der ignorierten Apps
        }

        /// <summary>Die Logos im Popup sind die Profile: als Legende in derselben Reihenfolge, nebeneinander wie im Popup.</summary>
        private UIElement PopupLegend()
        {
            var grid = new UniformGrid { Rows = 1 };
            void Entry(UIElement icon, string name)
            {
                var cell = new StackPanel { Margin = new Thickness(2, 4, 2, 4) };
                cell.Children.Add(new Border
                {
                    Width = 38, Height = 32, CornerRadius = new CornerRadius(8), Background = HoverBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Center, Child = icon,
                });
                cell.Children.Add(new TextBlock
                {
                    Text = name, FontSize = 12, Foreground = Fg, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 5, 0, 0),
                });
                grid.Children.Add(cell);
            }
            Entry(new TextBlock { Text = "⊘", FontSize = 15, Foreground = Fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                T("Nie regeln", "Never"));
            foreach (var p in ProfileDefinitions.For(Cfg()))
            {
                var logo = p.CreateLogo(15, UiColor.Brush(p.Color));
                logo.HorizontalAlignment = HorizontalAlignment.Center;
                logo.VerticalAlignment = VerticalAlignment.Center;
                Entry(logo, p.Name);
            }
            return new Border
            {
                Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 7, 12, 7), Child = grid,
            };
        }

        private void BodyApps(StackPanel body)
        {
            string Task() => T($"Wohin gehört „{AppRow.Label(_appsPopupApp ?? "Discord.exe")}“? Klick links auf ein Logo.",
                $"Where does “{AppRow.Label(_appsPopupApp ?? "Discord.exe")}” belong? Click a logo on the left.");
            _appsPill = new StatusPill(Task());
            if (_popupTried) _appsPill.Done(T("Geschafft", "Done"));
            body.Children.Add(_appsPill);
            body.Children.Add(Heading(T("Die Logos sind deine Profile", "The logos are your profiles")));
            body.Children.Add(PopupLegend());
            body.Children.Add(new Border { Height = 10 });
            body.Children.Add(Hint(T("Ohne Klick fragt es später wieder. Ändern: Einstellungen → Profile & Apps.",
                "Without a click it asks again later. To change: Settings → Profiles & apps.")));

            void Render(bool force)
            {
                RenderAppsPopup();
                if (!_popupTried) _appsPill?.Waiting(Task()); // zeigt das Popup jetzt eine echte App, steht hier ihr Name
            }
            _appsRender = Render;
            StartPageTimer(TimeSpan.FromSeconds(1.5), () => Render(false));
        }

        /// <summary>Ein/Aus der Automatik und je Art von App das Profil („Nachfragen“ = Popup wie bisher).</summary>
        private void AddAutoAssignCards(StackPanel body)
        {
            var cfg = Cfg();
            var rows = SettingsWindow.AutoAssignRows(cfg, (category, key) =>
            {
                Update(c => c.AutoAssignMap[category] = key);
                AutoAssignRequested?.Invoke();
                RefreshStage();
            });
            var rowsCard = new Border
            {
                Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 0, 3), Child = rows,
                Visibility = cfg.AutoAssign ? Visibility.Visible : Visibility.Collapsed,
            };
            body.Children.Add(SettingCard("\uE8CB", T("Automatisch einsortieren", "Sort automatically"), null,
                Toggle(cfg.AutoAssign, on =>
                {
                    Update(c => c.AutoAssign = on);
                    rowsCard.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                    if (on) AutoAssignRequested?.Invoke();
                    RefreshStage();
                })));
            body.Children.Add(rowsCard);
        }

        /// <summary>Die App hat laufende Apps ohne Rückfrage einsortiert.</summary>
        public void OnAppsAutoAssigned(IReadOnlyList<(string Process, string Profile)> apps)
        {
            var defs = ProfileDefinitions.For(Cfg());
            string list = string.Join(", ", apps.Select(a => $"{AppRow.Label(a.Process)} → {defs.FirstOrDefault(p => p.Key == a.Profile)?.Name ?? a.Profile}"));
            _appsPill?.Done(T($"Automatisch einsortiert: {list}", $"Sorted automatically: {list}"));
            _appsRender?.Invoke(true);
            RenderAppsPopup();
        }

        // ── 8 · Media-Flyout ────────────────────────────────────────────────

        private FrameworkElement StageMedia() => StageLive(osd: false);

        private void BodyMedia(StackPanel body)
        {
            body.Children.Add(Feature("\uE916", T("Zeitleiste", "Timeline"), T("Klicken oder ziehen zum Springen, ± zum Skippen", "Click or drag to jump, ± to skip")));
            body.Children.Add(Feature("\uE8AB", T("Mehrere Quellen", "Several sources"), T("Unten im Flyout umschalten", "Switch at the bottom of the flyout")));
            body.Children.Add(Feature("\uE712", T("„…“-Menü", "“…” menu"), T("Zufall, Wiederholen und mehr", "Shuffle, repeat and more")));
            body.Children.Add(new Border { Height = 4 });
            body.Children.Add(SettingCard("\uE8D6", T("Media-Flyout anzeigen", "Show media flyout"), null,
                Toggle(Cfg().MediaEnabled, v => Update(c => c.MediaEnabled = v))));
        }

        // ── 9 · Look ────────────────────────────────────────────────────────

        /// <summary>Zusammenpassende Designs für OSD und Flyout; „Eigenes“ öffnet die Unterseite.</summary>
        private static IEnumerable<(string Osd, string Media, string Name)> Looks()
        {
            yield return ("modern", "modernflyouts", "ModernFlyouts");
            yield return ("win11", "win11", "Windows 11");
            foreach (var d in Designs.All) yield return (d.Key, d.Key, d.Name);
        }

        private FrameworkElement StageLook() => StageLive();

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
                    tile.MouseEnter += (_, _) => Motion.Fade(tile, HoverBg);
                    tile.MouseLeave += (_, _) => Motion.Fade(tile, CardBg);
                }
                var chosen = look;
                tile.MouseLeftButtonUp += (_, _) =>
                {
                    Update(c => { c.OsdDesign = chosen.Osd; c.SelectMediaPreset(chosen.Media); c.OsdSavedActive = ""; });
                    RefreshPage();
                };
                grid.Children.Add(tile);
            }
            bool ownOn = cfg.OsdDesign == "dark" || cfg.MediaDesign == "custom";
            var own = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            own.Children.Add(new TextBlock { Text = "\uE70F", FontFamily = IconFont, FontSize = 17, Foreground = Fg, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 8) });
            own.Children.Add(new TextBlock { Text = T("Eigenes", "Custom"), FontSize = 12, FontWeight = ownOn ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Fg, TextAlignment = TextAlignment.Center });
            var ownTile = new Border
            {
                Background = CardBg, BorderBrush = ownOn ? Accent : CardBorder, BorderThickness = new Thickness(ownOn ? 2 : 1), CornerRadius = new CornerRadius(8),
                Padding = ownOn ? new Thickness(7) : new Thickness(8), Margin = new Thickness(3),
                Cursor = System.Windows.Input.Cursors.Hand, Child = own, ToolTip = T("Selbst gestalten, hier im Tutorial", "Design it yourself, right here in the introduction"),
            };
            ownTile.MouseEnter += (_, _) => Motion.Fade(ownTile, HoverBg);
            ownTile.MouseLeave += (_, _) => Motion.Fade(ownTile, CardBg);
            ownTile.MouseLeftButtonUp += (_, _) => OpenDesignEditor();
            grid.Children.Add(ownTile);
            body.Children.Add(grid);
            body.Children.Add(new Border
            {
                Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 6),
                Child = ActionRow("\uE70F", T("Dieses Flyout anpassen", "Customize this flyout"), T("Aufbau, Farben, Deckkraft", "Layout, colors, opacity"), () => OpenSub(DesignPage())),
            });

            body.Children.Add(SettingCard("\uE790", T("Theme", "Color scheme"), null, ThemeChoice(cfg.Theme)));
        }

        private FrameworkElement ThemeChoice(string current) =>
            Segments(new[] { ("system", T("System", "System")), ("light", T("Hell", "Light")), ("dark", T("Dunkel", "Dark")) }, current, key =>
            {
                Update(c => c.Theme = key);
                Rebuild();
            });

        /// <summary>Umschalter mit mehreren Feldern; das gewählte ist hervorgehoben.</summary>
        // ── 8a · Unterseite „Eigenes Design“ ────────────────────────────────

        private static string MediaDesignName(string key) => key switch
        {
            "modernflyouts" => "ModernFlyouts",
            "win11" => "Windows 11",
            "custom" => T("Eigenes", "Custom"),
            _ => Designs.Get(key)?.Name ?? key,
        };

        private static string OsdDesignName(string key) => key switch
        {
            "dark" => T("Eigenes", "Custom"),
            "modern" or "windows" => "ModernFlyouts",
            "win11" => "Windows 11",
            _ => Designs.Get(key)?.Name ?? key,
        };

        private Page DesignPage() => new("#f97316", () => T("Design anpassen", "Customize the design"),
            () => _designTarget == "osd"
                ? T($"Du passt das OSD „{OsdDesignName(Cfg().OsdDesign)}“ an. Gespeichert wird als eigenes Design.", $"You're customizing the OSD “{OsdDesignName(Cfg().OsdDesign)}”. It's saved as your own design.")
                : T($"Du passt „{MediaDesignName(Cfg().MediaDesign)}“ an. Gespeichert wird als eigenes Design.", $"You're customizing “{MediaDesignName(Cfg().MediaDesign)}”. It's saved as your own design."),
            () => StageLive(shown: Draft().Cfg), BodyDesign)
        {
            Crumb = () => T("Such dir einen Look aus  ›  Design anpassen", "Pick a look  ›  Customize the design"),
            NextText = () => T("Fertig", "Done"),
        };

        /// <summary>Kachel „Eigenes“: leeres Blatt mit freien Farben.</summary>
        private void OpenDesignEditor()
        {
            Update(c =>
            {
                c.OsdDesign = "dark";
                c.OsdSavedActive = "";
                c.SelectMediaPreset("custom");
            });
            OpenSub(DesignPage());
        }

        private DesignDraft Draft() => _designDraft ??= new DesignDraft(Cfg());

        /// <summary>Entwurf als eigenes Design speichern und gleich verwenden.</summary>
        private void SaveDesignDraft(string name)
        {
            if (_designDraft == null) return;
            var fresh = Cfg();
            _designDraft.SaveTo(name, fresh, _designDraft.Cfg);
            ConfigManager.Save(fresh);
            _designDraft.Rebase();
            ConfigChanged?.Invoke();
        }

        /// <summary>Unterseite „Design anpassen“ verlassen: bei ungespeichertem Entwurf nachfragen. false = bleiben.</summary>
        private bool ConfirmLeaveDesign()
        {
            if (_designDraft == null) return true;
            if (_designDraft.Dirty)
            {
                var (choice, name) = DesignSaveDialog.AskDesign(this, _designDraft.SuggestedName(), leaving: true);
                if (choice == LeaveChoice.Stay) return false;
                if (choice == LeaveChoice.Save) SaveDesignDraft(name);
            }
            _designDraft = null;
            return true;
        }

        /// <summary>Eigene Designs in der Einführung: Klick wählt (nach Nachfrage), ✕ löscht, Speichern übernimmt den Entwurf.</summary>
        private UIElement DesignSavedPanel()
        {
            if (_designTarget == "osd") return OsdSavedPanel();
            var draft = Draft();
            var saved = draft.Cfg.MediaSavedDesigns;
            return MediaDesignEditor.SavedPanel(saved.Select(s => s.Name).ToList(), draft.Cfg.MediaSavedActive,
                name =>
                {
                    if (!ConfirmLeaveDesign()) return;
                    Update(c => c.MediaSavedDesigns.First(s => s.Name == name).ApplyTo(c));
                    RefreshPage();
                },
                name =>
                {
                    Update(c =>
                    {
                        c.MediaSavedDesigns.RemoveAll(s => s.Name == name);
                        if (c.MediaSavedActive == name) c.SelectMediaPreset(c.MediaDesign);
                    });
                    saved.RemoveAll(s => s.Name == name);
                    if (draft.Cfg.MediaSavedActive == name)
                    {
                        draft.Cfg.SelectMediaPreset(draft.Cfg.MediaDesign);
                        draft.Rebase();
                    }
                    RefreshPage();
                },
                name =>
                {
                    SaveDesignDraft(name);
                    RefreshPage();
                },
                true, T("Name, z. B. Mein Island", "Name, e.g. My Island"),
                T("Gleicher Name überschreibt.", "The same name overwrites."));
        }

        /// <summary>Eigene OSD-Designs: wie DesignSavedPanel fürs Flyout.</summary>
        private UIElement OsdSavedPanel()
        {
            var draft = Draft();
            var saved = draft.Cfg.OsdSavedDesigns;
            return MediaDesignEditor.SavedPanel(saved.Select(s => s.Name).ToList(), draft.Cfg.OsdSavedActive,
                name =>
                {
                    if (!ConfirmLeaveDesign()) return;
                    Update(c => c.OsdSavedDesigns.First(s => s.Name == name).ApplyTo(c));
                    RefreshPage();
                },
                name =>
                {
                    Update(c =>
                    {
                        c.OsdSavedDesigns.RemoveAll(s => s.Name == name);
                        if (c.OsdSavedActive == name) c.SelectOsdPreset();
                    });
                    saved.RemoveAll(s => s.Name == name);
                    if (draft.Cfg.OsdSavedActive == name)
                    {
                        draft.Cfg.SelectOsdPreset();
                        draft.Rebase();
                    }
                    RefreshPage();
                },
                name =>
                {
                    SaveDesignDraft(name);
                    RefreshPage();
                },
                true, T("Name, z. B. Mein HUD", "Name, e.g. My HUD"),
                T("Gleicher Name überschreibt.", "The same name overwrites."));
        }

        private void BodyDesign(StackPanel body)
        {
            var cfg = Draft().Cfg;
            void Changed() => _stageDirty = true;

            var tabItems = new[] { ("template", T("Vorlage", "Template")), ("layout", T("Aufbau", "Layout")), ("colors", T("Farben", "Colors")), ("opacity", T("Deckkraft", "Opacity")) };
            var tabs = Segments(tabItems, _designTab, key =>
            {
                _designTab = key;
                RefreshPage();
            });
            ((Border)tabs).Margin = new Thickness(0, 0, 0, 10);
            body.Children.Add(tabs);

            var target = Segments(new[] { ("media", T("Media-Flyout", "Media flyout")), ("osd", "OSD") }, _designTarget, key =>
            {
                _designTarget = key;
                RefreshPage();
            });
            ((Border)target).Margin = new Thickness(0, 2, 0, 10);
            body.Children.Add(target);
            bool osd = _designTarget == "osd";
            if (_designTab == "colors")
            {
                AddDesignColors(body, cfg, Changed, osd);
                return;
            }
            if (_designTab == "opacity")
            {
                AddDesignOpacity(body, cfg, Changed);
                return;
            }
            if (osd)
            {
                AddOsdDesign(body, cfg, Changed);
                return;
            }

            var editor = new MediaDesignEditor(cfg, _designExpanded, Changed, RefreshPage, compact: true);
            if (_designTab == "template")
            {
                body.Children.Add(editor.Templates());
                body.Children.Add(new Border { Height = 10 });
                body.Children.Add(Heading(T("Eigene Designs", "Your own designs")));
                body.Children.Add(DesignSavedPanel());
                return;
            }
            body.Children.Add(editor.Elements());
            body.Children.Add(Hint(T("Schalter: ein/aus · Pfeile: Reihenfolge · Klick auf eine Zeile: mehr", "Switch: on/off · Arrows: order · Click a row: more")));
        }

        /// <summary>OSD im Reiter Vorlage (Vorlagen, eigene OSD-Designs) bzw. Aufbau (Karte wählen, Vorschau, Teile).</summary>
        private void AddOsdDesign(StackPanel body, AppConfig cfg, Action changed)
        {
            if (_designTab == "template")
            {
                body.Children.Add(new OsdDesignEditor(cfg, changed, RefreshPage, compact: true).Templates());
                body.Children.Add(new Border { Height = 4 });
                body.Children.Add(Heading(T("Eigene Designs", "Your own designs")));
                body.Children.Add(OsdSavedPanel());
                return;
            }
            var align = new OsdDesignEditor(cfg, changed, RefreshPage, compact: true).AlignTabs(RefreshPage); // gilt für alle Karten
            align.Margin = new Thickness(0, 0, 0, 6);
            body.Children.Add(align);
            var kinds = OsdDesignEditor.KindTabs(_osdKind, kind =>
            {
                _osdKind = kind;
                RefreshPage();
            });
            kinds.Margin = new Thickness(0, 0, 0, 8);
            body.Children.Add(kinds);
            // Die Bühne zeigt die Lautstärke-Karte; die übrigen Karten stehen hier.
            var preview = new Viewbox { Height = 44, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
            void ShowPreview() => preview.Child = OsdDesignEditor.Preview(cfg, _osdKind);
            if (_osdKind != OsdCardLayout.Volume)
            {
                ShowPreview();
                body.Children.Add(preview);
            }
            body.Children.Add(new OsdDesignEditor(cfg, () =>
            {
                changed();
                if (preview.Parent != null) ShowPreview();
            }, RefreshPage, compact: true).Elements(_osdKind));
            body.Children.Add(Hint(T("Oben = links auf der Karte · Schalter: ein/aus", "Top = left on the card · Switch: on/off")));
        }

        private void AddDesignColors(StackPanel body, AppConfig cfg, Action changed, bool osd)
        {
            if (!osd)
            {
                body.Children.Add(new MediaDesignEditor(cfg, _designExpanded, changed, () => { }, compact: true).DesignColors());
                return;
            }
            body.Children.Add(new OsdDesignEditor(cfg, changed, RefreshPage, compact: true).Colors());
            body.Children.Add(Hint(T("Klick auf eine Farbe öffnet den Farbkreis, ↺ setzt sie zurück.", "Click a color to open the color wheel, ↺ resets it.")));
        }

        private void AddDesignOpacity(StackPanel body, AppConfig cfg, Action changed)
        {
            var d = new AppConfig();
            Border Slide(string glyph, string title, double min, double value, double def, Action<int> set) =>
                SettingCard(glyph, title, null, ResetSlider(SliderControl(min, 100, value, "0' %'", v => { set((int)v); changed(); }, tick: 1, width: 110), def));

            if (_designTarget == "osd")
            {
                body.Children.Add(Slide("\uE706", T("Hintergrund", "Background"), 0, Math.Round(cfg.WinAlpha * 100), Math.Round(d.WinAlpha * 100), v => cfg.WinAlpha = v / 100.0));
                body.Children.Add(Slide("\uE8D2", T("Text und Symbole", "Text and icons"), 20, Math.Clamp(cfg.OsdContentOpacity, 20, 100), d.OsdContentOpacity, v => cfg.OsdContentOpacity = v));
                body.Children.Add(ContentCard("\uE7F4", T("Ecken", "Corners"), null, SettingsWindow.CornerControl(cfg.OsdCornerRadius, d.OsdCornerRadius, r => { cfg.OsdCornerRadius = r; changed(); })));
                return;
            }
            body.Children.Add(Slide("\uE706", T("Hintergrund", "Background"), 0, Math.Clamp(cfg.MediaOpacity, 0, 100), d.MediaOpacity, v => cfg.MediaOpacity = v));
            body.Children.Add(Slide("\uE8D2", T("Text und Symbole", "Text and icons"), 20, Math.Clamp(cfg.MediaContentOpacity, 20, 100), d.MediaContentOpacity, v => cfg.MediaContentOpacity = v));
            body.Children.Add(ContentCard("\uE8D6", T("Ecken", "Corners"), null, SettingsWindow.CornerControl(cfg.MediaCornerRadius, d.MediaCornerRadius, r => { cfg.MediaCornerRadius = r; changed(); })));
            var coverToggle = Toggle(cfg.MediaArtBackground, v =>
            {
                cfg.MediaArtBackground = v;
                changed();
                RefreshPage(); // Unschärfe und Abdunkelung nur bei eingeschaltetem Cover-Hintergrund
            });
            if (!cfg.MediaArtBackground)
            {
                body.Children.Add(SettingCard("\uE91B", T("Cover als Hintergrund", "Album art background"), null, coverToggle));
                return;
            }
            var art = new StackPanel();
            art.Children.Add(OptionRow(T("Unschärfe", "Blur"),
                ResetSlider(SliderControl(0, 60, Math.Clamp(cfg.MediaArtBlur, 0, 60), "0", v => { cfg.MediaArtBlur = (int)v; changed(); }, tick: 1, width: 110), d.MediaArtBlur)));
            art.Children.Add(OptionRow(T("Abdunkelung", "Dimming"),
                ResetSlider(SliderControl(0, 100, Math.Clamp(cfg.MediaArtDim, 0, 100), "0' %'", v => { cfg.MediaArtDim = (int)v; changed(); }, tick: 1, width: 110), d.MediaArtDim)));
            body.Children.Add(ContentCard("\uE91B", T("Cover als Hintergrund", "Album art background"), null, art, coverToggle));
        }

        // ── 10 · Position ────────────────────────────────────────────────────

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
                both.Changed += () =>
                {
                    Update(c =>
                    {
                        SettingsWindow.ApplyCombinedPosition(c, both.Preset, both.Device);
                        c.SnapIndependent = false;
                    });
                    RunPositionTest(Cfg());
                };
                row.Children.Add(Labeled(T("OSD und Media-Flyout", "OSD and media flyout"), both));
            }
            else
            {
                // Linksklick = Flyout, Rechtsklick = OSD (wie in den Einstellungen)
                var dual = new DualTilePicker(monitors, cfg.OsdMonitor, cfg.OsdCornerPreset == "custom" && cfg.OsdX == null ? "top_right" : cfg.OsdCornerPreset,
                    cfg.MediaMonitor, cfg.MediaCornerPreset, width: 372, maxScreenHeight: 226, allowCustom: false);
                dual.Changed += osd =>
                {
                    Update(c =>
                    {
                        if (osd) { c.OsdCornerPreset = dual.OsdPreset; c.OsdMonitor = dual.OsdDevice; }
                        else { c.MediaCornerPreset = dual.MediaPreset; c.MediaMonitor = dual.MediaDevice; }
                        c.SnapIndependent = false;
                    });
                    RunPositionTest(Cfg());
                };
                row.Children.Add(Labeled(T("Linksklick: Flyout · Rechtsklick: OSD", "Left click: flyout · Right click: OSD"), dual));
            }
            return OnStage(row, T("Jeder Klick zeigt es sofort dort.", "Every click shows it there right away."));
        }

        /// <summary>OSD und Flyout kurz an ihrem Platz zeigen (wie „Position testen“ in den Einstellungen).</summary>
        private static void RunPositionTest(AppConfig cfg, Point? osdAt = null, Point? mediaAt = null)
        {
            try
            {
                var media = MediaOsd.CreateTest(cfg);
                var (osd, flyout) = Placement.Layout(cfg, media.MeasureHeight(), osdAt, mediaAt);
                OsdCard.ShowTest(cfg, osd.TopLeft);
                media.ShowTestAt(flyout.TopLeft);
            }
            catch (Exception ex)
            {
                Logger.Error($"Einführung, Position testen: {ex.Message}");
            }
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
                RefreshStage();
                RunPositionTest(Cfg());
            };
            body.Children.Add(SettingCard("\uE8A9", T("Anordnung", "Arrangement"), null, arrange));

            var test = AccentBtn(T("Testen", "Test"), 92);
            test.Margin = new Thickness(0);
            test.Click += (_, _) => RunPositionTest(Cfg());
            body.Children.Add(SettingCard("\uE768", T("Noch einmal zeigen", "Show again"), null, test));
        }

        // ── 11 · Snap-Punkte ────────────────────────────────────────────────

        /// <summary>Alle Bildschirme maßstäblich: wo OSD und Flyout gerade stehen und wo die gespeicherten Plätze liegen.</summary>
        private FrameworkElement StageSnap()
        {
            var cfg = Cfg();
            var monitors = Placement.Monitors();
            var all = monitors.Count > 0 ? monitors[0].Work : new Rect(0, 0, 1920, 1080);
            foreach (var m in monitors) all.Union(m.Work);
            double scale = Math.Min(410 / all.Width, 300 / all.Height);
            double X(double x) => (x - all.X) * scale;
            double Y(double y) => (y - all.Y) * scale;

            var map = new Canvas { Width = all.Width * scale, Height = all.Height * scale };
            void Put(FrameworkElement el, double x, double y)
            {
                Canvas.SetLeft(el, x);
                Canvas.SetTop(el, y);
                map.Children.Add(el);
            }
            foreach (var m in monitors)
                Put(new Border
                {
                    Width = m.Work.Width * scale, Height = m.Work.Height * scale, Background = Surface, BorderBrush = SurfaceEdge, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6), Effect = Soft(),
                }, X(m.Work.X), Y(m.Work.Y));

            var boxes = new List<Rect>();
            // Stelle für ein Element der Größe w×h: in seinem Bildschirm, auch wenn es in der kleinen Karte größer gezeichnet wird als maßstäblich.
            Point Inside(Rect r, double w, double h)
            {
                var screen = Placement.WorkAreaFor(r, monitors);
                double left = X(screen.Left) + 1, top = Y(screen.Top) + 1, right = X(screen.Right) - 1, bottom = Y(screen.Bottom) - 1;
                return new Point(Math.Clamp(X(r.X), left, Math.Max(left, right - w)), Math.Clamp(Y(r.Y), top, Math.Max(top, bottom - h)));
            }
            void Box(Rect r, string text, string hex)
            {
                var brush = UiColor.Brush(hex);
                var label = new TextBlock { Text = text, FontSize = 9, Foreground = Fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double w = Math.Max(r.Width * scale, label.DesiredSize.Width + 6), h = Math.Max(r.Height * scale, label.DesiredSize.Height + 2);
                var at = Inside(r, w, h);
                boxes.Add(new Rect(at, new Size(w, h)));
                Put(new Border
                {
                    Width = w, Height = h, CornerRadius = new CornerRadius(3), Background = UiColor.Brush(hex, 0.25),
                    BorderBrush = brush, BorderThickness = new Thickness(1), Child = label,
                }, at.X, at.Y);
            }
            var (osd, media) = Placement.Layout(cfg, 220);
            Box(media, "Flyout", "#14b8a6");
            Box(osd, "OSD", "#3b82f6");

            for (int i = 0; i < cfg.SnapPositions.Count; i++)
            {
                var point = cfg.SnapPositions[i];
                var marker = new Border
                {
                    Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Accent, BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5),
                    Cursor = System.Windows.Input.Cursors.Hand, ToolTip = T($"Position {i + 1} zeigen", $"Show position {i + 1}"),
                    Child = new TextBlock { Text = (i + 1).ToString(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
                marker.MouseLeftButtonUp += (_, _) => TestSnapPoint(point);
                var spot = Inside(new Rect(point.X, point.Y, 1, 1), 22, 22);
                // Liegt die Nummer auf OSD oder Flyout (der Platz ist gerade aktiv): daneben, sonst wäre deren Text verdeckt.
                foreach (var box in boxes)
                    if (box.IntersectsWith(new Rect(spot, new Size(22, 22)))) spot.X = Math.Max(X(Placement.WorkAreaFor(new Rect(point.X, point.Y, 1, 1), monitors).Left) + 1, box.Left - 24);
                Put(marker, spot.X, spot.Y);
            }
            return OnStage(map, cfg.SnapPositions.Count == 0
                ? T("Hier erscheinen deine Plätze.", "Your places show up here.")
                : T("Deine Plätze. Klick auf eine Nummer zeigt sie.", "Your places. Click a number to show it."));
        }

        /// <summary>Wie im Snap-Zyklus: mit eigener Flyout-Position stehen beide unabhängig.</summary>
        private static void TestSnapPoint(SnapPosition point)
        {
            var tmp = Clone(Cfg());
            bool hasMedia = point.MediaX.HasValue && point.MediaY.HasValue;
            tmp.SnapIndependent = hasMedia && tmp.OsdLayout == "combined";
            RunPositionTest(tmp, new Point(point.X, point.Y), hasMedia ? new Point(point.MediaX!.Value, point.MediaY!.Value) : null);
        }

        /// <summary>Punkt für einen gewählten Platz, berechnet wie die Platz-Voreinstellungen (inkl. Rand und Überlappung).</summary>
        private static SnapPosition SnapPointFor(string preset, string device)
        {
            var tmp = Clone(Cfg());
            tmp.OsdCornerPreset = tmp.MediaCornerPreset = preset;
            tmp.OsdMonitor = tmp.MediaMonitor = device;
            if (tmp.OsdLayout == "combined") tmp.CombinedAnchor = Placement.CombinedAnchorFor(tmp.MediaCombinedSide, preset);
            tmp.SnapIndependent = false;
            var probe = MediaOsd.CreateTest(tmp);
            double mediaHeight = probe.MeasureHeight() + 32;
            probe.Close();
            var (osd, media) = Placement.Layout(tmp, mediaHeight, osdYields: true);
            return new SnapPosition
            {
                X = (int)Math.Round(osd.X), Y = (int)Math.Round(osd.Y),
                MediaX = (int)Math.Round(media.X), MediaY = (int)Math.Round(media.Y),
                OsdTile = preset, OsdDevice = device, MediaTile = preset, MediaDevice = device,
            };
        }

        private void AddSnapPoint(SnapPosition point)
        {
            Update(c =>
            {
                c.SnapPositions.Add(point);
                c.SnapEnabled = true; // ohne das wirkt weder das Einrasten noch das Halten der Taste
            });
            RefreshPage();
            TestSnapPoint(point);
        }

        private void BodySnap(StackPanel body)
        {
            var cfg = Cfg();
            string hotkey = FormatHotkey(cfg.ProfileHotkey);
            _snapPill = new StatusPill(cfg.SnapPositions.Count == 0
                ? T("Speichere zuerst einen Platz", "First save a place")
                : cfg.SnapPositions.Count == 1
                    ? T($"Probier es: Halte {hotkey} (mit zwei Plätzen schöner)", $"Try it: hold {hotkey} (nicer with two places)")
                    : T($"Probier es: Halte {hotkey}", $"Try it: hold {hotkey}"));
            body.Children.Add(_snapPill);

            body.Children.Add(Feature("\uE7C2", T("Ziehen und einrasten", "Drag and snap"), T("Nahe an einem Platz rastet es ein", "It snaps in near a place")));
            body.Children.Add(Feature("\uE765", T("Per Hotkey springen", "Jump with a key"), T($"{hotkey} halten: nächster Platz", $"Hold {hotkey}: next place")));

            body.Children.Add(SettingCard("\uE7C2", T("Mit der Maus verschieben", "Drag with the mouse"), T("Nötig für Snap-Punkte", "Needed for snap points"),
                Toggle(cfg.SnapEnabled, v => { Update(c => c.SnapEnabled = v); })));

            // Neuer Platz: aktuelle Lage oder per Kachel
            var monitors = Placement.Monitors();
            var picker = new ScreenTilePicker(monitors, cfg.OsdMonitor, "top_right", allowCustom: false, width: 176, maxScreenHeight: 90);
            var buttons = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
            var addChosen = AccentBtn(T("Diesen Platz speichern", "Save this place"));
            addChosen.Margin = new Thickness(0, 0, 0, 6);
            addChosen.Click += (_, _) => AddSnapPoint(SnapPointFor(picker.Preset, picker.Device));
            buttons.Children.Add(addChosen);
            var testChosen = Btn(T("Vorher zeigen", "Show first"));
            testChosen.Margin = new Thickness(0, 0, 0, 6);
            testChosen.Click += (_, _) => TestSnapPoint(SnapPointFor(picker.Preset, picker.Device));
            buttons.Children.Add(testChosen);
            var addCurrent = Btn(T("Aktuelle Position speichern", "Save current position"));
            addCurrent.Margin = new Thickness(0);
            addCurrent.Click += (_, _) => AddSnapPoint(OsdManager.CurrentSnapPoint(Cfg()));
            buttons.Children.Add(addCurrent);
            var add = new Grid();
            add.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            add.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            add.Children.Add(picker);
            Grid.SetColumn(buttons, 1);
            add.Children.Add(buttons);
            body.Children.Add(new Border { Height = 6 });
            body.Children.Add(Heading(T("Neuen Platz speichern", "Save a new place")));
            body.Children.Add(new Border
            {
                Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10), Child = add,
            });

            if (cfg.SnapPositions.Count == 0) return;
            body.Children.Add(Heading(T("Gespeicherte Plätze", "Saved places")));
            var rows = new StackPanel();
            for (int i = 0; i < cfg.SnapPositions.Count; i++)
            {
                int index = i;
                var point = cfg.SnapPositions[i];
                var row = new Grid { Margin = new Thickness(6, 2, 4, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = T($"Position {i + 1}", $"Position {i + 1}"), FontSize = 13, Foreground = Fg, VerticalAlignment = VerticalAlignment.Center });
                var show = FlatIconButton("\uE768", T("Zeigen", "Show"), () => TestSnapPoint(point));
                show.Margin = new Thickness(0, 0, 2, 0);
                Grid.SetColumn(show, 1);
                row.Children.Add(show);
                var remove = FlatIconButton("\uE74D", T("Löschen", "Delete"), () =>
                {
                    Update(c => { if (index < c.SnapPositions.Count) c.SnapPositions.RemoveAt(index); });
                    RefreshPage();
                });
                remove.Margin = new Thickness(0);
                Grid.SetColumn(remove, 2);
                row.Children.Add(remove);
                rows.Children.Add(row);
            }
            body.Children.Add(new Border
            {
                Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(4), Child = rows,
            });
        }

        // ── 12 · Fertig ─────────────────────────────────────────────────────

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
            if (!cfg.LiteMode) Add(KeyCaps(cfg.ProfileHotkey), T("Nächstes Profil", "Next profile"));
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

        /// <summary>Große Auswahl auf der letzten Seite.</summary>
        private static Border ChoiceCard(string glyph, string title, string text, bool primary, Action click)
        {
            var fg = primary ? Brushes.White : Fg;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 20, Foreground = fg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = fg });
            words.Children.Add(new TextBlock { Text = text, FontSize = 12, Foreground = primary ? Brushes.White : SubFg, Opacity = primary ? 0.9 : 1, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) });
            Grid.SetColumn(words, 1);
            row.Children.Add(words);
            var arrow = new TextBlock { Text = "\uE76C", FontFamily = IconFont, FontSize = 12, Foreground = fg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) };
            Grid.SetColumn(arrow, 2);
            row.Children.Add(arrow);

            Brush normal = primary ? Accent : CardBg;
            Brush hover = primary ? FromHex("#1A86DC") : HoverBg;
            var card = new Border
            {
                Background = normal, BorderBrush = primary ? Accent : CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 12, 12, 12), Margin = new Thickness(0, 0, 0, 8), Cursor = System.Windows.Input.Cursors.Hand, Child = row,
            };
            card.MouseEnter += (_, _) => Motion.Fade(card, hover);
            card.MouseLeave += (_, _) => Motion.Fade(card, normal);
            card.MouseLeftButtonUp += (_, _) => click();
            return card;
        }

        private void BodyDone(StackPanel body)
        {
            var cfg = Cfg();
            if (!cfg.LiteMode && !cfg.Profiles.Values.Any(a => a.Count > 0))
            {
                var warn = Note(T("Noch keine App in einem Profil, der Knopf tut noch nichts. Hier zuweisen.",
                    "No app in a profile yet, so the knob does nothing. Assign one here."), warn: true);
                warn.Cursor = System.Windows.Input.Cursors.Hand;
                warn.MouseLeftButtonUp += (_, _) => ShowPage(_appsIndex, -1);
                body.Children.Add(warn);
            }

            body.Children.Add(ChoiceCard("\uE768", T("Zum Programm", "Go to the program"),
                T("Tutorial schließen und loslegen", "Close the introduction and get started"), true, Close));
            body.Children.Add(ChoiceCard("\uE7C1", T("Tour durch die Einstellungen", "Tour of the settings"),
                T("Zeigt die wichtigsten Einstellungen direkt im Fenster", "Shows the key settings right in the window"), false, () =>
                {
                    RequestedTour = true;
                    Close();
                }));
            body.Children.Add(ChoiceCard("\uE8A9", T("Alle Funktionen anzeigen", "Show all features"),
                T("Erklärt und zum Ausprobieren", "Explained and ready to try"), false, () => OpenSub(CatalogPage())));

            body.Children.Add(new Border { Height = 8 });
            body.Children.Add(SettingCard("\uE7E8", T("Mit Windows starten", "Start with Windows"), null,
                Toggle(Autostart.IsEnabled(), v =>
                {
                    try { Autostart.SetEnabled(v); }
                    catch (Exception ex) { Logger.Error($"Autostart: {ex.Message}"); }
                })));
            body.Children.Add(Hint(T("Ohne Häkchen unten kommt das Tutorial mit den Einstellungen wieder.",
                "Without the tick below, the introduction comes back with the settings.")));
        }

        // ── Zeilen ──────────────────────────────────────────────────────────

        /// <summary>Anklickbare Zeile mit Symbol, Titel, Kurztext und Pfeil.</summary>
        private static Border ActionRow(string glyph, string title, string text, Action click)
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
            item.MouseEnter += (_, _) => Motion.Fade(item, HoverBg);
            item.MouseLeave += (_, _) => Motion.Fade(item, Brushes.Transparent);
            item.MouseLeftButtonUp += (_, _) => click();
            return item;
        }

        /// <summary>Zeile, die die Einführung schließt und die Einstellungen auf dieser Seite öffnet.</summary>
        private Border LinkRow(string glyph, string title, string text, string page) => ActionRow(glyph, title, text, () =>
        {
            RequestedSettingsPage = page;
            Close();
        });
    }
}
