using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KnobFlyout.Audio;
using KnobFlyout.Config;
using KnobFlyout.Input;
using KnobFlyout.Osd;
using KnobFlyout.Profiles;
using static KnobFlyout.Loc;
using static KnobFlyout.Settings.UiHelpers;

namespace KnobFlyout.Settings
{
    /// <summary>
    /// Ersetzt settings_app.py (CustomTkinter). Aufbau wie die ModernFlyouts-/Windows-11-Einstellungen:
    /// Navigation links (Allgemein, Lautstärke, Profile &amp; Apps, OSD, Media-Flyout, Position, Tastenkürzel,
    /// Spotify | Info), rechts je eine Seite aus kompakten Einstellungs-Karten (UiHelpers.SettingCard usw.).
    /// Sortierung: eine Seite pro sichtbarem Ding (OSD, Media-Flyout) mit Anzeige → Inhalt → Aussehen,
    /// alles zur Position auf „Position“, alle Tasten auf „Tastenkürzel“ (zusätzlich auf ihren Fachseiten).
    /// Alle Texte zweisprachig über Loc.T(de, en); Farbschema- und Sprachwechsel bauen das Fenster neu auf.
    /// Apps und Profile stehen zusammen auf „Profile &amp; Apps“, weil cfg.profiles (nicht cfg.apps) die Zuordnung
    /// steuert (siehe SessionVolumeController.GetTargets).
    ///
    /// Änderungen liegen bis zum Klick auf "Speichern" nur im Arbeitsspeicher (_cfg). Ausnahmen, die
    /// sofort gespeichert werden: Farbschema, Sprache, "Position zurücksetzen".
    /// </summary>
    public sealed partial class SettingsWindow : Window
    {
        private readonly AppConfig _cfg;
        private readonly SessionVolumeController _liveAudio = new();
        private readonly SpotifyController _spotify;
        private readonly YouTubeBridge _youtube;

        private StackPanel? _activeListHost;
        private StackPanel? _appListHost;
        private StackPanel? _profileNestedHost;
        private TextBox? _searchBox;

        // ── Navigationsleiste + Seiten im Stil der Windows-11-Einstellungen ──
        private const double NavWidthExpanded = 220;
        private const double NavWidthCollapsed = 52;

        private sealed record NavEntry(string Key, string Glyph, string Label);

        private static NavEntry[] MainNav() => new NavEntry[]
        {
            new("general", "\uE713", T("Allgemein", "General")),
            new("volume", "\uE767", T("Lautstärke", "Volume")),
            new("profiles", "\uE716", T("Profile & Apps", "Profiles & apps")),
            new("osd", "\uE7F4", "OSD"),
            new("media", "\uE8D6", "Media-Flyout"),
            new("position", "\uE81D", "Position"),
            new("design", "\uE790", "Design"),
            new("hotkeys", "\uE765", T("Hotkeys", "Shortcuts")),
            new("spotify", "\uE753", "Spotify"),
            new("youtube", "\uE714", "Browser-Addon"),
        };

        /// <summary>Seiten, die es im Lite-Modus nicht gibt (dort regelt der Knopf nur die Windows-Lautstärke).</summary>
        private static readonly string[] LiteHiddenPages = { "profiles", "spotify", "youtube" };

        private bool IsPageShown(string key) => !_cfg.LiteMode || !LiteHiddenPages.Contains(key);

        private static NavEntry InfoNav() => new("info", "\uE946", "Info");

        private readonly Dictionary<string, UIElement> _pages = new();
        private readonly Dictionary<string, Border> _navItems = new();
        private readonly List<TextBlock> _navLabels = new();
        private DockPanel? _nav;
        private ColumnDefinition? _navColumn;
        private ScrollViewer? _pageScroll;
        private Border? _pinnedTop; // über der Seite, scrollt nicht mit (Unterseite „Eigenes Design“: Kopf + Live-Vorschau)
        private bool _navExpanded = true;

        /// <summary>Wird ausgelöst, wenn sich das Fenster (Farbschema, Sprache, Neu laden) durch ein neues ersetzt.</summary>
        public event Action<SettingsWindow>? Replaced;

        /// <summary>„Einführung anzeigen“: App zeigt sie als Dialog; Ergebnis = gewünschte Einstellungs-Seite (oder null).</summary>
        public event Func<Window, string?>? TutorialRequested;

        /// <summary>Gespeichert, Positionen zurückgesetzt, Sprache gewechselt oder neu geladen: App übernimmt die Config.</summary>
        public event Action? ConfigApplied;

        private TextBlock? _status;
        private string _savedKey = "";
        private System.Windows.Threading.DispatcherTimer? _saveTimer;
        private HashSet<string> _assignedAtOpen = new();
        private List<string> _keepAppsAtOpen = new();
        private bool _bridgeAtOpen; // Browser-Erweiterung beim Öffnen (das „…“-Menü kann sie nebenher schalten)
        private bool _skipPlayingAtOpen; // „Skip nur bei Wiedergabe“ beim Öffnen (ebenso im „…“-Menü schaltbar)
        private Action _saveAll = () => { };
        private bool _skipLeavePrompt; // Fenster wird nur neu aufgebaut, die Änderungen ziehen mit um
        private DesignDraft? _designDraft; // Entwurf der Unterseite „Design anpassen“

        // Ändern sich außerhalb dieses Fensters oder werden sofort gespeichert: zählen nicht als „ungespeichert“.
        private static readonly string[] NotForSaveKeys =
        {
            "osd_x", "osd_y", "media_osd_x", "media_osd_y", "media_keep_x", "media_keep_y", "active_profile", "seen_apps", "screen_layout",
            "tutorial_hidden", "tutorial_started", "snap_independent", "color_profiles", "media_preview_live", "osd_preview_live", "design_preview_screen",
        };

        /// <summary>Vergleichsstand für den Speichern-Knopf.</summary>
        private static string SaveKey(AppConfig c)
        {
            var node = System.Text.Json.JsonSerializer.SerializeToNode(c)!.AsObject();
            foreach (var key in NotForSaveKeys) node.Remove(key);
            return node.ToJsonString();
        }

        private static HashSet<string> AssignedApps(AppConfig c) =>
            c.Profiles.Values.SelectMany(v => v).Select(a => a.ToLowerInvariant()).ToHashSet();
        private TextBox? _anchorXBox;
        private TextBox? _anchorYBox;
        private string _snapTab = "tile";          // Gespeicherte Positionen: neue per Kachel oder eigene Position
        private DualTilePicker? _dualPicker;       // Position: Platz-Kacheln (getrennt, OSD und Flyout in einer Auswahl)
        private ScreenTilePicker? _combinedPicker; // Position: Platz-Kacheln (zusammen)
        private bool _layoutTouched; // Anordnung/Ecke hier geändert → Snap-Unabhängigkeit endet
        private bool _keepPlaceTouched; // Platz der Daueranzeige hier gewählt (sonst gilt der gezogene Stand von der Platte)
        private readonly Media.MediaController? _media;

        private void LayoutTouched()
        {
            _layoutTouched = true;
            _cfg.SnapIndependent = false;
        }

        private string _currentPage = "general";

        /// <param name="carry">Bei einem Farbschema-/Sprachwechsel: die bisherige (evtl. ungespeicherte) Konfiguration übernehmen.</param>
        public SettingsWindow(SpotifyController spotify, YouTubeBridge youtube, AppConfig? carry = null, string startPage = "general", Media.MediaController? media = null)
        {
            _spotify = spotify;
            _youtube = youtube;
            _media = media;
            _cfg = carry ?? ConfigManager.Load();
            _assignedAtOpen = AssignedApps(_cfg);
            _keepAppsAtOpen = new List<string>(_cfg.MediaKeepApps);
            _bridgeAtOpen = _cfg.YoutubeBridge;
            _skipPlayingAtOpen = _cfg.MediaSkipOnlyPlaying;

            Title = T("KnobFlyout – Einstellungen", "KnobFlyout – Settings");
            Icon = Tray.AppIcon.Render(48); // Titelleiste + Taskleiste
            Width = 900;
            Height = 680;
            MinWidth = 820;
            MinHeight = 440;
            Background = ContentBg;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SourceInitialized += (_, _) => ApplyDarkTitleBar();
            ApplyControlTheme();
            Motion.EnablePress(this); // Klick drückt kurz ein

            var root = new Grid();
            _navColumn = new ColumnDefinition { Width = new GridLength(NavWidthExpanded) };
            root.ColumnDefinitions.Add(_navColumn);
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Content = root;

            var nav = _nav = BuildNav();
            Grid.SetColumn(nav, 0);
            root.Children.Add(nav);

            var right = new DockPanel();
            Grid.SetColumn(right, 1);
            root.Children.Add(right);

            var bottom = BuildBottomBar();
            DockPanel.SetDock(bottom, Dock.Bottom);
            right.Children.Add(bottom);

            _pinnedTop = new Border
            {
                Visibility = Visibility.Collapsed, Background = ContentBg,
                BorderBrush = CardBorder, BorderThickness = new Thickness(0, 0, 0, 1),
            };
            DockPanel.SetDock(_pinnedTop, Dock.Top);
            right.Children.Add(_pinnedTop);

            _pageScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            right.Children.Add(_pageScroll);

            SelectPage(_navItems.ContainsKey(startPage) ? startPage : "general");

            Closing += (_, e) =>
            {
                if (_skipLeavePrompt || Application.Current is App { IsExiting: true }) return;
                if (!ConfirmLeaveDesign())
                {
                    e.Cancel = true;
                    return;
                }
                if (SaveKey(_cfg) == _savedKey) return;
                var choice = DesignSaveDialog.AskSettings(this);
                if (choice == LeaveChoice.Save) _saveAll();
                else if (choice == LeaveChoice.Stay)
                {
                    e.Cancel = true;
                    if (IsDesignEditor(_currentPage)) OpenDesignEditor(_currentPage, _subParent);
                }
            };
            Closed += (_, _) => { CloseEyePopup(); _customPreviewTimer?.Stop(); _customPreviewDispose?.Invoke(); _screen.Close(); _youtubeTimer?.Stop(); _saveTimer?.Stop(); _liveAudio.Dispose(); };
            // Live-Vorschau steht neben dem Fenster: beim Verschieben/Minimieren schließen statt zu verwaisen.
            LocationChanged += (_, _) => CloseEyePopup();
            StateChanged += (_, _) => CloseEyePopup();
        }

        /// <summary>
        /// Lädt Settings/Theme.xaml (eigene Stile für Scrollbar, Dropdown, Aufklapp-Pfeile, Tabs,
        /// Slider, Buttons, Listen, Kontextmenü) und stellt die Theme-Farben als Ressourcen bereit.
        /// Schlägt das Laden fehl, bleibt es still beim Windows-Standardlook.
        /// </summary>
        private void ApplyControlTheme() => UiHelpers.ApplyControlTheme(this);

        /// <summary>
        /// Farbschema gewechselt: nur den Theme-Wert sofort speichern (übrige, noch ungespeicherte
        /// Änderungen bleiben unberührt im Speicher), Farben umstellen und das Fenster mit
        /// derselben Konfiguration, Position und Seite neu aufbauen.
        /// </summary>
        private void ReopenWithTheme(string theme)
        {
            _cfg.Theme = theme;

            var onDisk = ConfigManager.Load();
            onDisk.Theme = theme;
            ConfigManager.Save(onDisk);

            SetTheme(ThemeHelper.IsLight(_cfg));
            ReopenWith(_cfg);
        }

        /// <summary>Wie beim Farbschema: Sprache sofort speichern, übernehmen und das Fenster neu aufbauen.</summary>
        private void ReopenWithLite(bool lite)
        {
            _cfg.LiteMode = lite;
            var onDisk = ConfigManager.Load();
            onDisk.LiteMode = lite;
            ConfigManager.Save(onDisk);
            _savedKey = SaveKey(ConfigManager.Load());
            ConfigApplied?.Invoke(); // Knopf regelt ab sofort die Windows-Lautstärke bzw. wieder die Profile
            if (!IsPageShown(_currentPage)) _currentPage = "general";
            // An Ort und Stelle umbauen statt das Fenster neu zu öffnen (kein Aufploppen)
            if (Content is Grid root && _nav != null)
            {
                root.Children.Remove(_nav);
                _navItems.Clear();
                _navLabels.Clear();
                CloseSearchPopup();
                _searchIndex = null;
                if (_searchPopup != null) _searchPopup.PlacementTarget = null;
                _nav = BuildNav();
                if (_searchPopup != null) _searchPopup.PlacementTarget = _settingsSearch;
                Grid.SetColumn(_nav, 0);
                root.Children.Insert(0, _nav);
                if (!_navExpanded) foreach (var l in _navLabels) l.Visibility = Visibility.Collapsed;
            }
            _pages.Clear();
            _pageSnapshots.Clear();
            RebuildCurrentPage();
        }

        /// <summary>Animationen in Einstellungen und Tutorial an/aus: sofort gespeichert, Stile getauscht, Seiten neu (Schalter-Vorlage).</summary>
        private void SetAnimations(bool on)
        {
            _cfg.SettingsAnimations = on;
            var onDisk = ConfigManager.Load();
            onDisk.SettingsAnimations = on;
            ConfigManager.Save(onDisk);
            _savedKey = SaveKey(ConfigManager.Load());
            Motion.Enabled = on;
            UiHelpers.SetMotionStyles(this, on);
            _pages.Clear();
            _pageSnapshots.Clear();
            RebuildCurrentPage();
        }

        /// <summary>Text im Feststelltasten-OSD je Taste (leer = Standardname, steht grau im Feld).</summary>
        private UIElement LockKeyNameRows()
        {
            var rows = new StackPanel();
            rows.Children.Add(new TextBlock { Text = T("Text im OSD", "Text in the OSD"), Foreground = SubFg, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
            foreach (var key in new[] { "CapsLock", "NumLock", "ScrollLock" })
            {
                string standard = Osd.OsdCard.DefaultLockKeyName(key);
                _cfg.LockKeyNames ??= new();
                var box = TextField(_cfg.LockKeyNames.TryGetValue(key, out var own) ? own : "", 220);
                box.Margin = new Thickness(0);
                System.Windows.Automation.AutomationProperties.SetName(box, standard);
                var hint = new TextBlock
                {
                    Text = standard, Foreground = SubFg, IsHitTestVisible = false, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed,
                };
                box.TextChanged += (_, _) =>
                {
                    hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                    if (string.IsNullOrWhiteSpace(box.Text)) _cfg.LockKeyNames.Remove(key);
                    else _cfg.LockKeyNames[key] = box.Text;
                };
                var field = new Grid();
                field.Children.Add(box);
                field.Children.Add(hint);
                rows.Children.Add(OptionRow(standard, field));
            }
            return rows;
        }

        private void ReopenWithLanguage(string language)
        {
            _cfg.Language = language;

            var onDisk = ConfigManager.Load();
            onDisk.Language = language;
            ConfigManager.Save(onDisk);

            Loc.Apply(_cfg);
            ConfigApplied?.Invoke(); // Tray-Menü und Flyouts übernehmen die Sprache
            ReopenWith(_cfg);
        }

        /// <summary>Einführung als Dialog; was sie direkt gespeichert hat, danach übernehmen (ungespeicherte Änderungen hier bleiben).</summary>
        private void OpenTutorial()
        {
            var before = ConfigManager.Load();
            string? page = TutorialRequested?.Invoke(this);
            TutorialWindow.CopyChangedFields(before, ConfigManager.Load(), _cfg);
            Loc.Apply(_cfg);
            SetTheme(ThemeHelper.IsLight(_cfg));
            bool tour = page == TourPage;
            if (page != null && !tour) _currentPage = page;
            var next = ReopenWith(_cfg);
            // „Tour durch die Einstellungen“ gewählt: im neuen Fenster starten, sobald es steht
            if (tour) next.Dispatcher.BeginInvoke(new Action(next.StartTour), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        /// <summary>Fenster mit derselben Position, Größe und Seite neu aufbauen (carry null = frisch von der Platte).</summary>
        private SettingsWindow ReopenWith(AppConfig? carry)
        {
            var next = new SettingsWindow(_spotify, _youtube, carry, _currentPage, _media)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = Left,
                Top = Top,
                Width = ActualWidth,
                Height = ActualHeight,
            };
            Replaced?.Invoke(next);
            next.Show();
            next.Activate();
            _skipLeavePrompt = true;
            Close();
            return next;
        }

        private void ApplyDarkTitleBar() => UiHelpers.ApplyDarkTitleBar(this);

        // ── Navigation ──────────────────────────────────────────────────────
        private DockPanel BuildNav()
        {
            var host = new DockPanel { Background = NavBg };

            // Oben: Hamburger zum Ein-/Ausklappen der Beschriftungen
            var burger = new Border { Height = 44, Width = NavWidthCollapsed, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
            burger.Child = new TextBlock
            {
                Text = "\uE700", FontFamily = IconFont, FontSize = 16, Foreground = Fg,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0),
            };
            burger.MouseLeftButtonUp += (_, _) => ToggleNav();
            // Rechts daneben die Suche (nur bei aufgeklappter Navigation)
            var top = new DockPanel { Height = 44 };
            DockPanel.SetDock(burger, Dock.Left);
            top.Children.Add(burger);
            var search = BuildSearchBox();
            search.Visibility = _navExpanded ? Visibility.Visible : Visibility.Collapsed;
            top.Children.Add(search);
            DockPanel.SetDock(top, Dock.Top);
            host.Children.Add(top);

            // Unten: Info
            var info = MakeNavItem(InfoNav());
            info.Margin = new Thickness(4, 0, 4, 8);
            DockPanel.SetDock(info, Dock.Bottom);
            host.Children.Add(info);

            var list = new StackPanel();
            foreach (var e in MainNav().Where(e => IsPageShown(e.Key))) list.Children.Add(MakeNavItem(e));
            host.Children.Add(list);
            return host;
        }

        private Border MakeNavItem(NavEntry entry)
        {
            var grid = new Grid { Height = 36 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var bar = new Border
            {
                Width = 3, Height = 16, CornerRadius = new CornerRadius(2), Background = Accent,
                VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Hidden, Tag = "bar",
            };
            Grid.SetColumn(bar, 0);
            grid.Children.Add(bar);

            var icon = new TextBlock
            {
                Text = entry.Glyph, FontFamily = IconFont, FontSize = 15, Foreground = Fg,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(icon, 1);
            grid.Children.Add(icon);

            var label = new TextBlock
            {
                Text = entry.Label, FontSize = 13, Foreground = Fg, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(label, 2);
            grid.Children.Add(label);
            _navLabels.Add(label);

            var item = new Border
            {
                Child = grid, Background = Brushes.Transparent, CornerRadius = new CornerRadius(4),
                Margin = new Thickness(4, 1, 4, 1), Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = entry.Label,
            };
            item.MouseEnter += (_, _) => { if (!IsSelected(item)) Motion.Fade(item, NavHoverBg); };
            item.MouseLeave += (_, _) => { if (!IsSelected(item)) Motion.Fade(item, Brushes.Transparent); };
            item.MouseLeftButtonUp += (_, _) => SelectPage(entry.Key);

            _navItems[entry.Key] = item;
            return item;
        }

        private static bool IsSelected(Border item) =>
            ((item.Child as Grid)?.Children.OfType<Border>().FirstOrDefault(b => (b.Tag as string) == "bar")?.Visibility) == Visibility.Visible;

        private void ToggleNav()
        {
            _navExpanded = !_navExpanded;
            if (_navColumn != null) _navColumn.Width = new GridLength(_navExpanded ? NavWidthExpanded : NavWidthCollapsed);
            var vis = _navExpanded ? Visibility.Visible : Visibility.Collapsed;
            foreach (var l in _navLabels) l.Visibility = vis;
            if (_searchHost != null) _searchHost.Visibility = vis;
            if (!_navExpanded) CloseSearchPopup();
        }

        private void SelectPage(string key)
        {
            if (IsDesignEditor(_currentPage) && key != _currentPage && !ConfirmLeaveDesign()) return;
            CloseEyePopup();
            _currentPage = key;
            if (IsSubPage(key)) _pages.Remove(key); // Unterseite immer frisch
            else HideMediaCustomHeader();
            string navKey = IsSubPage(key) ? _subParent : key; // Unterseite: Eintrag der Fachseite bleibt markiert
            static Border? BarOf(Border item) => (item.Child as Grid)?.Children.OfType<Border>().FirstOrDefault(b => (b.Tag as string) == "bar");
            var oldBar = _navItems.Values.Select(BarOf).FirstOrDefault(b => b is { Visibility: Visibility.Visible });
            Border? newBar = null;
            foreach (var kv in _navItems)
            {
                bool sel = kv.Key == navKey;
                bool was = BarOf(kv.Value) is { Visibility: Visibility.Visible };
                if (sel != was) Motion.Fade(kv.Value, sel ? NavSelectedBg : Brushes.Transparent, 150);
                else kv.Value.Background = sel ? NavSelectedBg : Brushes.Transparent;
                var bar = BarOf(kv.Value);
                if (bar != null) bar.Visibility = sel ? Visibility.Visible : Visibility.Hidden;
                if (sel) newBar = bar;
            }
            if (newBar != null && _nav != null) Motion.SlideFrom(newBar, oldBar, _nav); // Balken gleitet zum neuen Eintrag

            // Aussehen-Einstellungen stehen doppelt (Fachseite + „Design“): wurde seit dem Aufbau dieser Seite etwas
            // geändert, die zwischengespeicherte Seite verwerfen, damit sie den aktuellen Stand zeigt.
            string now = DesignPages.Contains(key) ? ConfigSnapshot() : "";
            if (DesignPages.Contains(key) && _pages.ContainsKey(key)
                && _pageSnapshots.TryGetValue(key, out var built) && built != now)
                _pages.Remove(key);
            if (!_pages.TryGetValue(key, out var page))
            {
                page = BuildPage(key);
                _pages[key] = page;
                if (DesignPages.Contains(key)) _pageSnapshots[key] = ConfigSnapshot();
            }
            if (_pageScroll != null)
            {
                bool changed = !ReferenceEquals(_pageScroll.Content, page) && key != _shownPage;
                _pageScroll.Content = page;
                if (changed && IsLoaded) Motion.PageIn(page); // neue Seite gleitet herein (nicht beim Neuaufbau derselben Seite)
                _shownPage = key;
            }
        }

        private string? _shownPage;

        /// <summary>Seiten mit denselben Aussehen-Einstellungen (Farbschema, OSD- und Media-Aussehen).</summary>
        private static readonly string[] DesignPages = { "general", "osd", "media", "design" };
        private readonly Dictionary<string, string> _pageSnapshots = new();

        private string ConfigSnapshot()
        {
            try { return System.Text.Json.JsonSerializer.Serialize(_cfg); }
            catch { return Guid.NewGuid().ToString(); } // im Zweifel neu aufbauen
        }

        private UIElement BuildPage(string key)
        {
            var page = new StackPanel { Margin = new Thickness(32, 22, 32, 16), MaxWidth = 960 };
            switch (key)
            {
                case "general": BuildGeneralPage(page); break;
                case "volume": BuildVolumePage(page); break;
                case "profiles": BuildProfilesPage(page); break;
                case "osd": BuildOsdPage(page); break;
                case "media": BuildMediaPage(page); break;
                case "media_custom": BuildMediaCustomPage(page); break;
                case "osd_custom": BuildOsdCustomPage(page); break;
                case "position": BuildPositionPage(page); break;
                case "design": BuildDesignPage(page); break;
                case "colors_both": BuildBothColorsPage(page); break;
                case "hotkeys": BuildHotkeysPage(page); break;
                case "spotify": BuildSpotifyPage(page); break;
                case "youtube": BuildYoutubePage(page); break;
                case "info": BuildInfoPage(page); break;
            }
            return page;
        }

        // ── Unten: Speichern / Schließen ────────────────────────────────────
        private Border BuildBottomBar()
        {
            var bar = new DockPanel { Margin = new Thickness(32, 10, 32, 12) };

            var status = new TextBlock { Foreground = SubFg, VerticalAlignment = VerticalAlignment.Center };
            _status = status;
            DockPanel.SetDock(status, Dock.Left);
            bar.Children.Add(status);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(btnRow, Dock.Right);

            var save = _saveButton = AccentBtn(T("Speichern", "Save"), 110);
            // Nimmt alle noch nicht gespeicherten Einstellungen zurück (ein Entwurf in „Design anpassen“ bleibt)
            var revert = Btn("\uE7A7", 44); // Undo
            revert.FontFamily = IconFont;
            revert.ToolTip = T("Änderungen zurücknehmen", "Discard changes");
            System.Windows.Automation.AutomationProperties.SetName(revert, T("Änderungen zurücknehmen", "Discard changes"));
            revert.Click += (_, _) => RevertSettings();
            string savedText = T("Gespeichert ✓", "Saved ✓");
            // Ausgegraut, solange es nichts zu speichern gibt
            bool dirty = true;
            void RefreshSave()
            {
                bool now = SaveKey(_cfg) != _savedKey;
                if (now == dirty) return;
                dirty = now;
                save.IsEnabled = now;
                save.Background = now ? Accent : ButtonBg;
                save.BorderBrush = now ? Accent : ButtonBorder;
                save.Foreground = now ? Brushes.White : SubFg;
                revert.IsEnabled = now;
                revert.Foreground = now ? Fg : SubFg;
                if (now && status.Text == savedText) status.Text = "";
            }
            _savedKey = SaveKey(ConfigManager.Load());
            RefreshSave();
            _saveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _saveTimer.Tick += (_, _) => RefreshSave();
            _saveTimer.Start();

            _saveAll = () =>
            {
                // Felder, die sich außerhalb dieses Fensters ändern können (Drag am OSD, Profil-
                // Hotkey, neu erkannte Apps), werden vor dem Speichern vom Datenträger übernommen,
                // damit "Speichern" im Settings-Fenster sie nicht mit einem alten Stand überschreibt.
                var onDisk = ConfigManager.Load();
                _cfg.OsdX = onDisk.OsdX;
                _cfg.OsdY = onDisk.OsdY;
                _cfg.MediaOsdX = onDisk.MediaOsdX;
                _cfg.MediaOsdY = onDisk.MediaOsdY;
                _cfg.MediaKeepX = onDisk.MediaKeepX;
                _cfg.MediaKeepY = onDisk.MediaKeepY;
                if (_cfg.MediaKeepApps.SequenceEqual(_keepAppsAtOpen)) _cfg.MediaKeepApps = onDisk.MediaKeepApps; // hier nicht geändert: was das „…“-Menü gesetzt hat, bleibt
                _keepAppsAtOpen = new List<string>(_cfg.MediaKeepApps);
                if (_cfg.YoutubeBridge == _bridgeAtOpen) _cfg.YoutubeBridge = onDisk.YoutubeBridge; // hier nicht geändert: Stand vom „…“-Menü bleibt
                _bridgeAtOpen = _cfg.YoutubeBridge;
                if (_cfg.MediaSkipOnlyPlaying == _skipPlayingAtOpen) _cfg.MediaSkipOnlyPlaying = onDisk.MediaSkipOnlyPlaying; // ebenso
                _skipPlayingAtOpen = _cfg.MediaSkipOnlyPlaying;
                if (!_keepPlaceTouched)
                {
                    _cfg.MediaKeepPreset = onDisk.MediaKeepPreset;
                    _cfg.MediaKeepMonitor = onDisk.MediaKeepMonitor;
                }
                _keepPlaceTouched = false;
                _cfg.ActiveProfile = onDisk.ActiveProfile;
                var profileKeys = ProfileDefinitions.Keys(_cfg); // aktives Profil gelöscht → erstes Profil
                if (profileKeys.Length > 0 && !profileKeys.Contains(_cfg.ActiveProfile)) _cfg.ActiveProfile = profileKeys[0];
                _cfg.SeenApps = onDisk.SeenApps;
                _cfg.ScreenLayout = onDisk.ScreenLayout; // vom Bildschirmwechsel-Abgleich der App gepflegt
                _cfg.TutorialHidden = onDisk.TutorialHidden; // Häkchen in der Einführung
                _cfg.TutorialStarted = onDisk.TutorialStarted;
                // Vom Snap-Zyklus gesetzt; wurden hier Anordnung/Ecke geändert, endet es.
                _cfg.SnapIndependent = !_layoutTouched && onDisk.SnapIndependent;
                // Während das Fenster offen war, neu zugewiesene Apps (Popup, Automatik) nicht verwerfen.
                foreach (var (profile, apps) in onDisk.Profiles)
                    foreach (var app in apps)
                        if (!_assignedAtOpen.Contains(app.ToLowerInvariant()) && AppRow.CurrentProfile(_cfg, app) == null && _cfg.Profiles.TryGetValue(profile, out var list))
                            list.Add(app);
                _assignedAtOpen = AssignedApps(_cfg);
                // Deckkraft usw. der Fachseiten gehören zum gewählten eigenen Design
                int ownMedia = _cfg.MediaSavedDesigns.FindIndex(s => s.Name == _cfg.MediaSavedActive && s.Design == _cfg.MediaDesign);
                if (ownMedia >= 0) _cfg.MediaSavedDesigns[ownMedia] = SavedMediaDesign.From(_cfg, _cfg.MediaSavedActive);
                int ownOsd = _cfg.OsdSavedDesigns.FindIndex(s => s.Name == _cfg.OsdSavedActive && s.Design == _cfg.OsdDesign);
                if (ownOsd >= 0) _cfg.OsdSavedDesigns[ownOsd] = SavedOsdDesign.From(_cfg, _cfg.OsdSavedActive);

                ConfigManager.Save(_cfg);
                _savedKey = SaveKey(_cfg);
                RefreshSave();
                ConfigApplied?.Invoke();
                status.Text = savedText;
            };
            save.Click += (_, _) => _saveAll();

            var close = Btn(T("Schließen", "Close"), 100);
            close.Margin = new Thickness(0);
            close.Click += (_, _) => Close();

            btnRow.Children.Add(revert);
            btnRow.Children.Add(save);
            btnRow.Children.Add(close);
            bar.Children.Add(btnRow);
            return new Border { Child = bar, Background = ContentBg, BorderBrush = CardBorder, BorderThickness = new Thickness(0, 1, 0, 0) };
        }

        // ══════════════════════════════════════════════════════════════════
        //  Allgemein
        // ══════════════════════════════════════════════════════════════════
        private void BuildGeneralPage(StackPanel page)
        {
            page.Children.Add(PageTitle(T("Allgemein", "General")));

            page.Children.Add(SectionTitle(T("Darstellung", "Appearance")));
            var langCombo = Combo(160, _cfg.Language == "en" ? 1 : 0, "Deutsch", "English");
            langCombo.SelectionChanged += (_, _) =>
            {
                string chosen = langCombo.SelectedIndex == 1 ? "en" : "de";
                if (chosen == _cfg.Language) return;
                // Erst nach dem aktuellen Ereignis neu aufbauen, nicht mitten im SelectionChanged.
                Dispatcher.BeginInvoke(new Action(() => ReopenWithLanguage(chosen)));
            };
            page.Children.Add(SettingCard("\uE774", T("Sprache", "Language"), null, langCombo));

            page.Children.Add(ThemeCard());
            page.Children.Add(SettingCard("\uE7AD", T("Animationen", "Animations"), null,
                Toggle(_cfg.SettingsAnimations, v => Dispatcher.BeginInvoke(new Action(() => SetAnimations(v))))));

            page.Children.Add(SectionTitle(T("Modus", "Mode")));
            var lite = Toggle(_cfg.LiteMode, v => Dispatcher.BeginInvoke(new Action(() => ReopenWithLite(v))));
            page.Children.Add(SettingCard("\uE945", T("Lite-Modus", "Lite mode"), // LightningBolt
                T("Der Knopf regelt nur die Windows-Lautstärke, dazu das Media-Flyout. Keine Profile.", "The knob only controls the Windows volume, plus the media flyout. No profiles."), lite));
            if (_cfg.LiteMode)
                page.Children.Add(SettingCard("\uE7F4", T("OSD anzeigen", "Show OSD"), T("Aus: nur das Media-Flyout", "Off: only the media flyout"),
                    Toggle(_cfg.Osd, v => _cfg.Osd = v)));

            page.Children.Add(SectionTitle(T("Anzeige", "Display")));
            page.Children.Add(ExpanderCard("\uE72E", T("Feststelltasten anzeigen", "Show lock keys"), null, LockKeyNameRows(),
                headerControl: Toggle(_cfg.LockKeysOsd, v => _cfg.LockKeysOsd = v)));
            if (!_cfg.LiteMode) page.Children.Add(NewAppPopupCard());
            if (Osd.NativeFlyout.Supported)
                page.Children.Add(SettingCard("\uE7E7", T("Windows-Flyout ausblenden", "Hide Windows flyout"),
                    T("Die Lautstärke- und Media-Anzeige von Windows 10", "The volume and media indicator of Windows 10"),
                    Toggle(_cfg.HideWindowsFlyout, v => _cfg.HideWindowsFlyout = v)));

            page.Children.Add(SectionTitle(T("Start", "Startup")));
            page.Children.Add(SettingCard("\uE7E8", T("Mit Windows starten", "Start with Windows"), T("Gilt sofort", "Applies immediately"),
                Toggle(Autostart.IsEnabled(), v =>
                {
                    try { Autostart.SetEnabled(v); }
                    catch (Exception ex) { Logger.Error($"Autostart: {ex.Message}"); }
                })));

            page.Children.Add(SectionTitle(T("Hilfe", "Help")));
            var tutorial = Btn(T("Anzeigen", "Show"), 110);
            tutorial.Margin = new Thickness(0);
            tutorial.Click += (_, _) => OpenTutorial();
            var tour = Btn(T("Starten", "Start"), 110);
            tour.Margin = new Thickness(0);
            tour.Click += (_, _) => StartTour();
            page.Children.Add(SettingCard("\uE897", T("Tutorial", "Introduction"), null, tutorial));
            page.Children.Add(SettingCard("\uE7C1", T("Tour durch die Einstellungen", "Tour of the settings"), null, tour)); // Flag

            page.Children.Add(SectionTitle(T("Problembehebung", "Troubleshooting")));
            page.Children.Add(BuildReloadCard());
        }

        // ══════════════════════════════════════════════════════════════════
        //  Design – alle Aussehen-Einstellungen an einem Ort (zusätzlich zu ihren Fachseiten, wie „Tastenkürzel“)
        // ══════════════════════════════════════════════════════════════════
        private void BuildDesignPage(StackPanel page)
        {
            page.Children.Add(PageTitle("Design"));

            page.Children.Add(SectionTitle(T("Theme", "Color scheme")));
            page.Children.Add(ThemeCard());

            AddOsdAppearance(page, T("OSD – Aussehen", "OSD – appearance"), designPage: true);
            AddMediaAppearance(page, T("Media-Flyout – Aussehen", "Media flyout – appearance"), designPage: true);

            page.Children.Add(SectionTitle(T("OSD und Media-Flyout", "OSD and media flyout")));
            page.Children.Add(ProfileBothCard());
            page.Children.Add(NavCard("\uE790", T("Beide Farben anpassen", "Customize the colors of both"), null, () => // Color
            {
                _subParent = "design";
                SelectPage("colors_both");
                _pageScroll?.ScrollToTop();
            }));
        }

        /// <summary>Farbschema System/Hell/Dunkel (Seiten „Allgemein“ und „Design“); ein Wechsel baut das Fenster neu auf.</summary>
        private Border ThemeCard()
        {
            var themeCombo = Combo(160, _cfg.Theme == "light" ? 1 : _cfg.Theme == "dark" ? 2 : 0,
                T("System", "System"), T("Hell", "Light"), T("Dunkel", "Dark"));
            themeCombo.SelectionChanged += (_, _) =>
            {
                string chosen = themeCombo.SelectedIndex == 1 ? "light" : themeCombo.SelectedIndex == 2 ? "dark" : "system";
                if (chosen == _cfg.Theme) return;
                Dispatcher.BeginInvoke(new Action(() => ReopenWithTheme(chosen)));
            };
            return SettingCard("\uE790", T("Hell oder dunkel", "Light or dark"), null,
                WithReset(themeCombo, () => themeCombo.SelectedIndex = 0));
        }

        private Border BuildReloadCard()
        {
            var reloadBtn = Btn(T("Neu laden", "Reload"), 120);
            reloadBtn.Margin = new Thickness(0);
            reloadBtn.Click += (_, _) => ReloadConfig();
            return SettingCard("\uE72C", T("Einstellungen neu laden", "Reload settings"),
                T("Verwirft ungespeicherte Änderungen.", "Discards unsaved changes."), reloadBtn);
        }

        /// <summary>
        /// OSD und Media-Flyout auf die Standardposition zurücksetzen (z.B. wenn sie nach einem
        /// Monitorwechsel außerhalb des Bildschirms liegen). Wird sofort gespeichert, weil
        /// "Speichern" die Positionen ohnehin frisch von der Platte übernimmt.
        /// </summary>
        private void ResetPositions()
        {
            var onDisk = ConfigManager.Load();
            onDisk.OsdX = null;
            onDisk.OsdY = null;
            onDisk.MediaOsdX = null;
            onDisk.MediaOsdY = null;
            onDisk.OsdCornerPreset = onDisk.MediaCornerPreset = "custom";
            onDisk.SnapIndependent = false;
            ConfigManager.Save(onDisk);

            _cfg.OsdX = null;
            _cfg.OsdY = null;
            _cfg.MediaOsdX = null;
            _cfg.MediaOsdY = null;
            _cfg.OsdCornerPreset = _cfg.MediaCornerPreset = "custom";
            _cfg.SnapIndependent = false;
            if (_anchorXBox != null) _anchorXBox.Text = "";
            if (_anchorYBox != null) _anchorYBox.Text = "";
            _dualPicker?.SetOsd("custom");
            _dualPicker?.SetMedia("custom");
            _combinedPicker?.Set("custom");

            _savedKey = SaveKey(ConfigManager.Load());
            ConfigApplied?.Invoke();
            if (_status != null) _status.Text = T("Position zurückgesetzt ✓", "Position reset ✓");
        }

        /// <summary>
        /// Knopf „Zurück“: alle Einstellungen auf den gespeicherten Stand, Seiten neu aufbauen. Ein Entwurf in „Design anpassen“ bleibt
        /// (er hat seine eigene Rückfrage).
        /// </summary>
        private void RevertSettings()
        {
            var fresh = ConfigManager.Load();
            foreach (var prop in typeof(AppConfig).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (!prop.CanRead || !prop.CanWrite || prop.GetIndexParameters().Length > 0) continue;
                if (prop.IsDefined(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), true)) continue; // nur Zugriffe auf andere Felder
                prop.SetValue(_cfg, prop.GetValue(fresh));
            }
            _assignedAtOpen = AssignedApps(_cfg);
            _keepAppsAtOpen = new List<string>(_cfg.MediaKeepApps);
            _bridgeAtOpen = _cfg.YoutubeBridge;
            _skipPlayingAtOpen = _cfg.MediaSkipOnlyPlaying;
            _layoutTouched = false;
            _keepPlaceTouched = false;
            _pages.Clear();
            _pageSnapshots.Clear();
            RebuildCurrentPage();
            if (_status != null) _status.Text = T("Änderungen zurückgenommen", "Changes discarded");
        }

        /// <summary>config.json neu einlesen (verwirft ungespeicherte Änderungen) und das Fenster neu aufbauen.</summary>
        private void ReloadConfig()
        {
            var fresh = ConfigManager.Load();
            Loc.Apply(fresh);
            SetTheme(ThemeHelper.IsLight(fresh));
            ConfigApplied?.Invoke();
            var next = ReopenWith(null);
            if (next._status != null) next._status.Text = T("Config neu geladen ✓", "Config reloaded ✓");
        }

        // ══════════════════════════════════════════════════════════════════
        //  Lautstärke (Drehknopf)
        // ══════════════════════════════════════════════════════════════════
        private void BuildVolumePage(StackPanel page)
        {
            page.Children.Add(PageTitle(T("Lautstärke", "Volume")));

            page.Children.Add(SectionTitle(T("Drehknopf", "Knob")));
            page.Children.Add(SettingCard("\uE767", T("Schrittweite", "Step size"), null,
                SliderControl(1, 15, _cfg.Step, "0' %'", v => _cfg.Step = (int)v, tick: 1)));
            if (!_cfg.LiteMode)
            page.Children.Add(SettingCard("\uE8D6", T("Schrittweite Spotify", "Spotify step size"),
                T("Nur bei Steuerung über die Spotify-API", "Only when controlled via the Spotify API"),
                SliderControl(1, 15, _cfg.StepSpotify, "0' %'", v => _cfg.StepSpotify = (int)v, tick: 1)));

            page.Children.Add(SectionTitle(T("Eigene Hotkeys", "Own hotkeys")));
            AddVolumeHotkeyCards(page);
        }

        /// <summary>Lauter/Leiser/Stumm auf beliebige Hotkeys (Seiten Lautstärke und Hotkeys); wirken wie die Tasten des Drehknopfs.</summary>
        private void AddVolumeHotkeyCards(StackPanel page)
        {
            page.Children.Add(SettingCard("\uE995", T("Lauter", "Volume up"), T("Wirkt wie der Drehknopf, Halten wiederholt", "Works like the knob, holding repeats"),
                BuildHotkeyCapture(_cfg.VolumeUpHotkey, v => _cfg.VolumeUpHotkey = v)));
            page.Children.Add(SettingCard("\uE993", T("Leiser", "Volume down"), null,
                BuildHotkeyCapture(_cfg.VolumeDownHotkey, v => _cfg.VolumeDownHotkey = v)));
            page.Children.Add(SettingCard("\uE74F", T("Stumm", "Mute"), null,
                BuildHotkeyCapture(_cfg.VolumeMuteHotkey, v => _cfg.VolumeMuteHotkey = v)));
        }

        // ══════════════════════════════════════════════════════════════════
        //  Profile & Apps (Profile, App-Zuweisung, ignorierte Apps)
        // ══════════════════════════════════════════════════════════════════
        private void BuildProfilesPage(StackPanel page)
        {
            page.Children.Add(PageTitle(T("Profile & Apps", "Profiles & apps")));

            page.Children.Add(SectionTitle(T("Wechseln", "Switching")));
            page.Children.Add(ProfileHotkeyCard());

            var modeCombo = Combo(220, _cfg.ProfileMode == "assigned" ? 1 : 0,
                T("Nur Apps mit Ton", "Only apps playing sound"),
                T("Alle Apps im Profil", "All apps in the profile"));
            modeCombo.SelectionChanged += (_, _) => _cfg.ProfileMode = modeCombo.SelectedIndex == 1 ? "assigned" : "active";
            page.Children.Add(SettingCard("\uE8AB", T("Gesteuerte Apps", "Controlled apps"), null, modeCombo));

            page.Children.Add(SettingCard("\uE893", T("Leere Profile überspringen", "Skip empty profiles"), null,
                Toggle(_cfg.SkipEmptyProfiles, v => _cfg.SkipEmptyProfiles = v)));

            page.Children.Add(SectionTitle(T("Profile", "Profiles")));
            _profileEditorHost = new StackPanel();
            RenderProfileEditor();
            page.Children.Add(ExpanderCard("\uE790", T("Profile bearbeiten", "Edit profiles"), null,
                _profileEditorHost));

            _profileNestedHost = new StackPanel();
            RenderProfileOverview();
            page.Children.Add(ExpanderCard("\uE8A9", T("Profile im Überblick", "Profiles overview"), null, _profileNestedHost));

            page.Children.Add(SectionTitle(T("Neue Apps zuweisen", "Assign new apps")));
            page.Children.Add(NewAppPopupCard());
            _autoAssignHost = new ContentControl { Focusable = false };
            RenderAutoAssign();
            page.Children.Add(ExpanderCard("\uE8CB", T("Automatisch einsortieren", "Sort automatically"), null,
                _autoAssignHost, expanded: _cfg.AutoAssign, headerControl: Toggle(_cfg.AutoAssign, v => _cfg.AutoAssign = v)));
            page.Children.Add(ProfileHotkeysCard(out var profileHotkeyRows));
            _profileHotkeyHost = profileHotkeyRows; // nach Änderungen an den Profilen neu aufbauen

            page.Children.Add(SectionTitle("Apps"));
            BuildAppsSection(page);
        }

        // ── Profile bearbeiten (cfg.profile_list, max. 6, alle änderbar und löschbar) ──

        private StackPanel? _profileEditorHost;

        /// <summary>Je Profil eine Zeile (Logo, Name, Farbe, Löschen), darüber Farbprofile, darunter „Profil hinzufügen“.</summary>
        private void RenderProfileEditor()
        {
            if (_profileEditorHost == null) return;
            var host = _profileEditorHost;
            host.Children.Clear();
            if (_cfg.ProfileList.Count == 0) _cfg.ProfileList = ProfileDefinitions.DefaultEntries(_cfg);

            var boxes = new Dictionary<string, TextBox>(); // Farbfelder je Profil (für eigene Farbprofile)
            var rows = new StackPanel();
            foreach (var entry in _cfg.ProfileList.ToList())
                rows.Children.Add(ProfileEditorRow(entry, boxes));
            host.Children.Add(BuildColorPresets(boxes));
            host.Children.Add(rows);

            var add = Btn(T("+ Profil hinzufügen", "+ Add profile"));
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(0, 4, 0, 0);
            add.IsEnabled = _cfg.ProfileList.Count < ProfileDefinitions.MaxProfiles;
            add.Click += (_, _) =>
            {
                if (_cfg.ProfileList.Count >= ProfileDefinitions.MaxProfiles) return;
                var usedColors = _cfg.ProfileList.Select(p => p.Color.ToLowerInvariant()).ToHashSet();
                var entry = new ProfileEntry
                {
                    Key = ProfileDefinitions.NewKey(),
                    Name = T("Profil", "Profile") + " " + (_cfg.ProfileList.Count + 1),
                    Glyph = ProfileDefinitions.Gallery.FirstOrDefault(g => _cfg.ProfileList.All(p => p.Glyph != g)) ?? ProfileDefinitions.Gallery[0],
                    Color = ProfileDefinitions.Palette.FirstOrDefault(c => !usedColors.Contains(c)) ?? ProfileDefinitions.Palette[0],
                };
                _cfg.ProfileList.Add(entry);
                _cfg.Profiles[entry.Key] = new List<string>();
                ProfilesChanged();
            };
            host.Children.Add(add);
            host.Children.Add(Hint(T("Logo anklicken: Windows-Symbol oder eigenes Bild.", "Click a logo: Windows symbol or your own image.")));
        }

        /// <summary>Profile geändert: Editor, App-Listen (Segment-Leisten), Überblick und Tasten je Profil neu zeichnen.</summary>
        private void ProfilesChanged()
        {
            RenderProfileEditor();
            ProfileLabelsChanged();
        }

        /// <summary>Name/Farbe/Logo geändert: alles neu zeichnen, was Profile anzeigt (außer dem Editor selbst).</summary>
        private void ProfileLabelsChanged()
        {
            RerenderApps();
            RenderAutoAssign();
            if (_profileHotkeyHost != null) BuildProfileHotkeyRows(_profileHotkeyHost);
            if (_currentPage != "hotkeys") _pages.Remove("hotkeys"); // zeigt die Profile ebenfalls
        }

        private StackPanel? _profileHotkeyHost; // „Zuweisen per Taste“ auf der Seite Profile & Apps
        private ContentControl? _autoAssignHost;

        private void RenderAutoAssign()
        {
            if (_autoAssignHost != null) _autoAssignHost.Content = AutoAssignRows(_cfg, (category, key) => _cfg.AutoAssignMap[category] = key);
        }

        /// <summary>Je Art von App das Zielprofil (Einstellungen und Einführung); „Nachfragen“ = Popup wie bisher.</summary>
        internal static StackPanel AutoAssignRows(AppConfig cfg, Action<string, string> set)
        {
            var rows = new StackPanel();
            var defs = ProfileDefinitions.For(cfg);
            var names = new[] { T("Nachfragen", "Ask") }.Concat(defs.Select(d => d.Name)).ToArray();
            foreach (var (category, label) in new[]
            {
                (AppClassifier.Games, T("Spiele (Steam, Epic, GOG, Xbox …)", "Games (Steam, Epic, GOG, Xbox …)")),
                (AppClassifier.Browsers, T("Browser und Video (Firefox, Chrome, VLC …)", "Browsers and video (Firefox, Chrome, VLC …)")),
                (AppClassifier.Music, T("Musik (Spotify, Apple Music, Tidal …)", "Music (Spotify, Apple Music, Tidal …)")),
                (AppClassifier.Comms, T("Kommunikation (Discord, Teams, Zoom …)", "Communication (Discord, Teams, Zoom …)")),
            })
            {
                string current = cfg.AutoAssignMap.TryGetValue(category, out var key) ? key : AppClassifier.DefaultProfile(category);
                var combo = Combo(140, defs.ToList().FindIndex(d => d.Key == current) + 1, names); // 0 = Nachfragen
                string chosen = category;
                combo.SelectionChanged += (_, _) => set(chosen, combo.SelectedIndex <= 0 ? "" : defs[combo.SelectedIndex - 1].Key);
                rows.Children.Add(OptionRow(label, combo));
            }
            return rows;
        }

        /// <summary>
        /// „Zuweisen per Taste“: eine Taste je Profil, die im Neue-App-Popup die angezeigte App diesem Profil
        /// zuweist (Seiten Profile &amp; Apps und Tastenkürzel). Außerhalb des Popups wirkt die Taste nicht.
        /// </summary>
        private Border ProfileHotkeysCard(out StackPanel rows)
        {
            rows = new StackPanel();
            BuildProfileHotkeyRows(rows);
            return ExpanderCard("\uE765", T("Zuweisen per Hotkey", "Assign by key"), T("Wirkt nur im Zuweisungs-OSD", "Only works in the assignment OSD"), rows);
        }

        private void BuildProfileHotkeyRows(StackPanel rows)
        {
            rows.Children.Clear();
            if (_cfg.ProfileList.Count == 0) _cfg.ProfileList = ProfileDefinitions.DefaultEntries(_cfg);
            var defs = ProfileDefinitions.For(_cfg);
            foreach (var entry in _cfg.ProfileList.ToList())
            {
                var def = defs.FirstOrDefault(p => p.Key == entry.Key);
                if (def == null) continue;

                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var logo = def.CreateLogo(14, FromHex(def.Color));
                if (logo is Image) logo.Width = logo.Height = 18;
                row.Children.Add(logo);

                var name = new TextBlock { Text = def.Name, Foreground = Fg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 12, 0) };
                Grid.SetColumn(name, 1);
                row.Children.Add(name);

                var capture = BuildHotkeyCapture(entry.AssignHotkey, v => entry.AssignHotkey = v);
                if (capture is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(capture, 2);
                row.Children.Add(capture);
                rows.Children.Add(row);
            }
        }

        private Grid ProfileEditorRow(ProfileEntry entry, Dictionary<string, TextBox> boxes)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var def = ProfileDefinitions.For(_cfg).First(p => p.Key == entry.Key);

            // Logo: Klick öffnet Auswahl (Windows-Symbole) bzw. eigenes Bild
            var logo = def.CreateLogo(18, FromHex(entry.Color));
            if (logo is Image) logo.Width = logo.Height = 24;
            var logoBtn = new Border
            {
                Width = 38, Height = 38, CornerRadius = new CornerRadius(6), Background = FieldBg,
                BorderBrush = FieldBorder, BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = T("Logo ändern", "Change logo"), Child = logo, VerticalAlignment = VerticalAlignment.Center,
            };
            logoBtn.MouseEnter += (_, _) => Motion.Fade(logoBtn, HoverBg);
            logoBtn.MouseLeave += (_, _) => Motion.Fade(logoBtn, FieldBg);
            logoBtn.MouseLeftButtonUp += (_, _) => OpenLogoPicker(logoBtn, row, entry);
            row.Children.Add(logoBtn);

            // Name
            var name = TextField(ProfileDefinitions.DisplayName(entry));
            name.Margin = new Thickness(10, 0, 10, 0);
            name.VerticalAlignment = VerticalAlignment.Center;
            name.ToolTip = T("Name des Profils", "Profile name");
            name.TextChanged += (_, _) => entry.Name = name.Text.Trim();
            name.LostFocus += (_, _) => ProfileLabelsChanged(); // Überblick, Listen und Tasten zeigen den neuen Namen
            Grid.SetColumn(name, 1);
            row.Children.Add(name);

            // Farbe: Hex-Feld + Vorschau (Klick = Farbkreis)
            var box = TextField(entry.Color, 90);
            box.Margin = new Thickness(0);
            boxes["profile_color_" + entry.Key] = box;
            var swatch = new Border
            {
                Width = 24, Height = 24, Margin = new Thickness(8, 0, 0, 0), CornerRadius = new CornerRadius(6),
                BorderBrush = FieldBorder, BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand, ToolTip = T("Farbe auswählen", "Pick a color"),
            };
            void UpdateColor()
            {
                try { swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(box.Text)); }
                catch { swatch.Background = Brushes.Transparent; }
                if (logo is TextBlock glyph) glyph.Foreground = FromHex(box.Text.Trim(), entry.Color);
            }
            UpdateColor();
            box.TextChanged += (_, _) => { entry.Color = box.Text.Trim(); UpdateColor(); };
            box.LostFocus += (_, _) => ProfileLabelsChanged();
            var colorRow = Row(box, swatch);
            colorRow.Margin = new Thickness(0);
            colorRow.VerticalAlignment = VerticalAlignment.Center;
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                var picker = new ColorWheelPopup(swatch, box.Text, hex => box.Text = hex);
                row.Children.Add(picker);
                picker.Closed += (_, _) => { row.Children.Remove(picker); ProfileLabelsChanged(); };
                picker.IsOpen = true;
            };
            Grid.SetColumn(colorRow, 2);
            row.Children.Add(colorRow);

            // Löschen (mindestens ein Profil bleibt)
            var delete = FlatIconButton("\uE74D", T("Profil löschen", "Delete profile"), () => // Delete
            {
                if (_cfg.ProfileList.Count <= 1) return;
                _cfg.ProfileList.Remove(entry);
                _cfg.Profiles.Remove(entry.Key); // zugewiesene Apps werden frei
                if (_cfg.ActiveProfile == entry.Key) _cfg.ActiveProfile = _cfg.ProfileList[0].Key;
                ProfilesChanged();
            });
            delete.Margin = new Thickness(8, 0, 0, 0);
            delete.IsEnabled = _cfg.ProfileList.Count > 1;
            Grid.SetColumn(delete, 3);
            row.Children.Add(delete);

            // Zweite Zeile: was der Knopf bei diesem Profil regelt
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var routes = ProfileRoutes();
            var route = Combo(300, Math.Max(0, Array.FindIndex(routes, r => r.Key == entry.SystemVolume)), routes.Select(r => r.Label).ToArray());
            route.Margin = new Thickness(10, 6, 0, 0);
            route.HorizontalAlignment = HorizontalAlignment.Left;
            route.ToolTip = T("Was der Knopf bei diesem Profil regelt", "What the knob controls in this profile");
            route.SelectionChanged += (_, _) => entry.SystemVolume = routes[Math.Max(0, route.SelectedIndex)].Key;
            Grid.SetRow(route, 1);
            Grid.SetColumn(route, 1);
            Grid.SetColumnSpan(route, 3);
            row.Children.Add(route);
            return row;
        }

        /// <summary>Was ein Profil regeln kann (ProfileEntry.SystemVolume).</summary>
        internal static (string Key, string Label)[] ProfileRoutes() => new[]
        {
            ("", T("Regelt die Apps im Profil", "Controls the apps in the profile")),
            ("knobflyout", T("Regelt die Windows-Lautstärke (KnobFlyout-OSD)", "Controls the Windows volume (KnobFlyout OSD)")),
            ("windows", T("Regelt die Windows-Lautstärke (Windows-OSD)", "Controls the Windows volume (Windows OSD)")),
        };

        /// <summary>Logo-Auswahl: Raster aus Windows-Symbolen, dazu „Eigenes Bild laden …“ bzw. „Bild entfernen“.</summary>
        private void OpenLogoPicker(FrameworkElement anchor, Panel owner, ProfileEntry entry)
        {
            var popup = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                StaysOpen = false, AllowsTransparency = true, VerticalOffset = 4,
            };

            var grid = new WrapPanel { Width = 11 * 36 };
            foreach (var g in ProfileDefinitions.Gallery)
            {
                string glyph = g;
                bool selected = entry.Image.Length == 0 && entry.Glyph == glyph;
                var cell = new Border
                {
                    Width = 32, Height = 32, Margin = new Thickness(2), CornerRadius = new CornerRadius(4),
                    Background = selected ? Accent : Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = glyph, FontFamily = IconFont, FontSize = 16, Foreground = selected ? Brushes.White : Fg,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                if (!selected)
                {
                    cell.MouseEnter += (_, _) => Motion.Fade(cell, HoverBg);
                    cell.MouseLeave += (_, _) => Motion.Fade(cell, Brushes.Transparent);
                }
                cell.MouseLeftButtonUp += (_, _) =>
                {
                    entry.Glyph = glyph;
                    entry.Image = "";
                    entry.Emoji = ""; // geändertes Logo gilt auch im Design „Eigenes“
                    popup.IsOpen = false;
                    ProfilesChanged();
                };
                grid.Children.Add(cell);
            }

            var load = Btn(T("Eigenes Bild laden …", "Load own image …"));
            load.Click += (_, _) =>
            {
                popup.IsOpen = false;
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = T("Logo auswählen", "Choose logo"),
                    Filter = T("Bilder", "Images") + " (*.png, *.jpg, *.ico, *.bmp, *.gif)|*.png;*.jpg;*.jpeg;*.ico;*.bmp;*.gif",
                };
                if (dialog.ShowDialog(this) != true) return;
                try
                {
                    entry.Image = ProfileDefinitions.ImportImage(dialog.FileName);
                    entry.Emoji = "";
                }
                catch (Exception ex)
                {
                    Logger.Error($"Profil-Logo laden: {ex.Message}");
                    if (_status != null) _status.Text = T("Bild konnte nicht geladen werden.", "Could not load the image.");
                    return;
                }
                ProfilesChanged();
            };
            var remove = Btn(T("Bild entfernen", "Remove image"));
            remove.IsEnabled = entry.Image.Length > 0;
            remove.Click += (_, _) =>
            {
                entry.Image = "";
                popup.IsOpen = false;
                ProfilesChanged();
            };

            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = T("Windows-Symbole", "Windows symbols"), Foreground = SubFg, FontSize = 12, Margin = new Thickness(2, 0, 0, 6) });
            content.Children.Add(grid);
            var buttons = Row(load, remove);
            buttons.Margin = new Thickness(2, 8, 0, 0);
            content.Children.Add(buttons);

            popup.Child = new Border
            {
                Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10), Margin = new Thickness(8), Child = content,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.35 },
            };
            owner.Children.Add(popup); // im Fensterbaum, damit die Theme-Stile greifen
            popup.Closed += (_, _) => owner.Children.Remove(popup);
            popup.IsOpen = true;
        }

        /// <summary>App-Liste (Aktiv / Alle mit Suche, Profil zuweisen, ⊘) und darunter die ignorierten Apps.</summary>
        private void BuildAppsSection(StackPanel page)
        {
            var body = new StackPanel();
            var tabs = new TabControl { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };

            // Tab 1: nur gerade aktive (Ton abspielende) Apps
            var activeTab = new TabItem { Header = T("Aktiv", "Active") };
            _activeListHost = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            activeTab.Content = _activeListHost;
            tabs.Items.Add(activeTab);

            // Tab 2: alle bekannten Apps (laufend, gesehen, zugewiesen oder ausgeschlossen) mit Suche
            var allTab = new TabItem { Header = T("Alle", "All") };
            var allPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            _searchBox = TextField("", 240);
            _searchBox.Margin = new Thickness(0);
            _searchBox.ToolTip = T("Suchen", "Search");
            var refresh = Btn("\uE72C");
            refresh.FontFamily = IconFont;
            refresh.ToolTip = T("Aktualisieren", "Refresh");
            refresh.Margin = new Thickness(6, 0, 0, 0);
            searchRow.Children.Add(_searchBox);
            searchRow.Children.Add(refresh);
            allPanel.Children.Add(searchRow);
            _appListHost = new StackPanel();
            allPanel.Children.Add(_appListHost);
            allTab.Content = allPanel;
            tabs.Items.Add(allTab);

            _searchBox.TextChanged += (_, _) => RenderAppList(_searchBox.Text);
            refresh.Click += (_, _) => { RenderActiveList(); RenderAppList(_searchBox.Text); };

            body.Children.Add(tabs);
            RenderActiveList();
            RenderAppList("");
            page.Children.Add(ContentCard("\uE71D", T("Apps zuweisen", "Assign apps"), T("⊘ = ignorieren", "⊘ = ignore"), body));

            page.Children.Add(ExpanderCard("\uE733", T("Ignorierte Apps", "Ignored apps"), null, BuildIgnoredApps()));
        }

        private void RerenderApps()
        {
            RenderActiveList();
            RenderAppList(_searchBox?.Text ?? "");
            RenderProfileOverview();
            RenderIgnoredApps();
        }

        private StackPanel? _ignoredList;
        private WrapPanel? _ignoredRunning;
        private TextBlock? _ignoredRunningTitle;
        private TextBox? _ignoredInput;
        private Button? _ignoredAdd;

        /// <summary>Ignorierte Apps als Zeilen mit ✕, darunter eintragen und gerade laufende Apps als Pillen.</summary>
        private StackPanel BuildIgnoredApps()
        {
            var panel = new StackPanel();
            panel.Children.Add(_ignoredList = new StackPanel { Margin = new Thickness(0, 0, 0, 10) });

            _ignoredInput = TextField();
            _ignoredInput.Margin = new Thickness(0);
            InputHint(_ignoredInput, T("Programm eintragen, z. B. systemsounds", "Enter a program, e.g. systemsounds"));
            _ignoredAdd = Btn(T("Hinzufügen", "Add"));
            _ignoredAdd.Margin = new Thickness(8, 0, 0, 0);
            void Add()
            {
                string name = _ignoredInput.Text.Trim();
                if (name.Length == 0) return;
                IgnoreApp(name);
                _ignoredInput.Text = "";
                _ignoredInput.Focus();
            }
            _ignoredAdd.Click += (_, _) => Add();
            _ignoredInput.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Enter) return;
                e.Handled = true;
                Add();
            };
            var addRow = new Grid();
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            addRow.Children.Add(_ignoredInput);
            Grid.SetColumn(_ignoredAdd, 1);
            addRow.Children.Add(_ignoredAdd);
            panel.Children.Add(addRow);

            panel.Children.Add(_ignoredRunningTitle = new TextBlock { Text = T("Läuft gerade, Klick ignoriert:", "Running now, click to ignore:"), FontSize = 12, Foreground = SubFg, Margin = new Thickness(0, 12, 0, 6) });
            panel.Children.Add(_ignoredRunning = new WrapPanel());
            RenderIgnoredApps();
            return panel;
        }

        private void IgnoreApp(string name)
        {
            if (!_cfg.Exclude.Any(e => e.Equals(name, StringComparison.OrdinalIgnoreCase))) AppRow.ToggleExclude(_cfg, name);
            RerenderApps();
        }

        private void RenderIgnoredApps()
        {
            if (_ignoredList == null || _ignoredRunning == null || _ignoredRunningTitle == null) return;
            _ignoredList.Children.Clear();
            if (_cfg.Exclude.Count == 0)
                _ignoredList.Children.Add(new TextBlock { Text = T("Keine App wird ignoriert.", "No apps are ignored."), Foreground = SubFg });
            foreach (var proc in _cfg.Exclude.OrderBy(AppRow.Label, StringComparer.OrdinalIgnoreCase))
                _ignoredList.Children.Add(IgnoredRow(proc));

            _ignoredRunning.Children.Clear();
            var running = LiveAudioApps().Where(a => !_cfg.Exclude.Any(e => e.Equals(a, StringComparison.OrdinalIgnoreCase))).OrderBy(AppRow.Label, StringComparer.OrdinalIgnoreCase).ToList();
            _ignoredRunningTitle.Visibility = running.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var proc in running)
            {
                string name = proc;
                var chip = Chip(AppRow.Label(proc), false, () => IgnoreApp(name));
                chip.Margin = new Thickness(0, 0, 6, 6);
                chip.ToolTip = proc.ToLowerInvariant();
                _ignoredRunning.Children.Add(chip);
            }
        }

        /// <summary>Eine ignorierte App: Name und Prozess wie in der App-Liste, ✕ nimmt sie wieder auf.</summary>
        private Border IgnoredRow(string proc)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = AppRow.Label(proc), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Fg, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = proc.ToLowerInvariant(), FontSize = 11, Foreground = SubFg, TextTrimming = TextTrimming.CharacterEllipsis });
            grid.Children.Add(text);
            var remove = FlatIconButton("\uE711", T($"„{AppRow.Label(proc)}“ nicht mehr ignorieren", $"Stop ignoring “{AppRow.Label(proc)}”"), () =>
            {
                AppRow.ToggleExclude(_cfg, proc);
                RerenderApps();
            });
            remove.Width = remove.Height = 28;
            remove.FontSize = 11;
            remove.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(remove, 1);
            grid.Children.Add(remove);
            var row = new Border { Background = Brushes.Transparent, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 5, 4, 5), Margin = new Thickness(0, 0, 0, 1), Child = grid };
            row.MouseEnter += (_, _) => Motion.Fade(row, HoverBg);
            row.MouseLeave += (_, _) => Motion.Fade(row, Brushes.Transparent);
            return row;
        }

        /// <summary>Entspricht _render_inc(): nur Apps, die gerade aktiv Ton abspielen.</summary>
        private void RenderActiveList()
        {
            if (_activeListHost == null) return;
            _activeListHost.Children.Clear();

            var running = LiveAudioApps();
            if (running.Count == 0)
            {
                _activeListHost.Children.Add(new TextBlock { Text = T("Gerade spielt keine App Ton ab.", "No app is playing sound right now."), Foreground = SubFg, Margin = new Thickness(4) });
                return;
            }

            foreach (var proc in running.OrderBy(a => a.ToLowerInvariant()))
                _activeListHost.Children.Add(AppRow.Build(_cfg, proc, true, RerenderApps));
        }

        /// <summary>Entspricht _all_apps: laufende Audio-Apps + seen_apps + bereits zugewiesene/ausgeschlossene Apps.</summary>
        private void RenderAppList(string query)
        {
            if (_appListHost == null) return;
            _appListHost.Children.Clear();

            var running = LiveAudioApps();
            var runningSet = running.Select(a => a.ToLowerInvariant()).ToHashSet();

            var all = new List<string>(running);
            foreach (var a in _cfg.SeenApps) if (!all.Any(x => x.Equals(a, StringComparison.OrdinalIgnoreCase))) all.Add(a);
            foreach (var kv in _cfg.Profiles)
                foreach (var a in kv.Value) if (!all.Any(x => x.Equals(a, StringComparison.OrdinalIgnoreCase))) all.Add(a);
            foreach (var a in _cfg.Exclude) if (!all.Any(x => x.Equals(a, StringComparison.OrdinalIgnoreCase))) all.Add(a);

            var q = query.Trim().ToLowerInvariant();
            var filtered = all.Where(a => q.Length == 0 || a.ToLowerInvariant().Contains(q))
                              .OrderByDescending(a => runningSet.Contains(a.ToLowerInvariant()))
                              .ThenBy(a => a.ToLowerInvariant())
                              .ToList(); // einmal sortieren statt bei Any() und foreach je erneut

            if (filtered.Count == 0)
            {
                _appListHost.Children.Add(new TextBlock { Text = T("Keine Apps gefunden.", "No apps found."), Foreground = SubFg, Margin = new Thickness(4) });
                return;
            }

            foreach (var proc in filtered)
                _appListHost.Children.Add(AppRow.Build(_cfg, proc, runningSet.Contains(proc.ToLowerInvariant()), RerenderApps));
        }

        /// <summary>Entspricht _render_profiles(): je Profil ein Expander mit dessen zugewiesenen Apps.</summary>
        private void RenderProfileOverview()
        {
            if (_profileNestedHost == null) return;
            _profileNestedHost.Children.Clear();

            var running = LiveAudioApps().Select(a => a.ToLowerInvariant()).ToHashSet();

            foreach (var p in ProfileDefinitions.For(_cfg))
            {
                var apps = _cfg.Profiles.TryGetValue(p.Key, out var list) ? list : new List<string>();
                var color = p.Color;

                var inner = new StackPanel();
                if (apps.Count == 0)
                {
                    inner.Children.Add(new TextBlock { Text = T("Noch keine Apps", "No apps yet"), Foreground = SubFg, FontSize = 11, Margin = new Thickness(8, 4, 0, 4) });
                }
                else
                {
                    foreach (var proc in apps)
                        inner.Children.Add(AppRow.Build(_cfg, proc, running.Contains(proc.ToLowerInvariant()), RerenderApps));
                }

                var logo = p.CreateLogo(13, FromHex(color));
                if (logo is Image) logo.Width = logo.Height = 16;
                logo.Margin = new Thickness(0, 0, 6, 0);
                var header = new StackPanel { Orientation = Orientation.Horizontal };
                header.Children.Add(logo);
                header.Children.Add(new TextBlock { Text = $"{p.Name} ({apps.Count})", Foreground = FromHex(color), FontWeight = FontWeights.SemiBold });
                _profileNestedHost.Children.Add(new Expander
                {
                    Header = header,
                    Content = inner,
                    Margin = new Thickness(0, 0, 0, 2),
                });
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  OSD
        // ══════════════════════════════════════════════════════════════════
        private void BuildOsdPage(StackPanel page)
        {
            page.Children.Add(PageTitle(T("OSD – Lautstärke-Anzeige", "OSD – volume display")));

            page.Children.Add(SectionTitle(T("Anzeige", "Display")));
            page.Children.Add(SettingCard("\uE7F4", T("OSD anzeigen", "Show OSD"), null,
                Toggle(_cfg.Osd, v => _cfg.Osd = v)));
            page.Children.Add(SettingCard("\uE916", T("Anzeigedauer", "Duration"), null,
                SliderControl(500, 10000, _cfg.OsdDuration, "0' ms'", v => _cfg.OsdDuration = (int)v, tick: 100)));
            page.Children.Add(SettingCard("\uE8BB", T("Per Klick ausblenden", "Click to dismiss"), null,
                Toggle(_cfg.ClickToDismiss, v => _cfg.ClickToDismiss = v)));

            AddOsdAppearance(page, T("Aussehen", "Appearance"), designPage: false);
        }

        /// <summary>
        /// Aussehen des OSD (Design mit Augen-Knopf, eigene Designs, Farbprofil, „Design anpassen“, Ecken). Steht auf der Seite „OSD“
        /// und zusätzlich auf „Design“ (wie die Tasten auf „Tastenkürzel“); SelectPage baut die andere Seite bei Änderungen neu.
        /// </summary>
        private void AddOsdAppearance(StackPanel page, string appearanceTitle, bool designPage)
        {
            // ── Aussehen (Augen-Knopf = Live-Vorschau) ──
            page.Children.Add(SectionTitle(appearanceTitle));
            // Ältere Designs, dahinter alle aus dem Design-System (Designs.All, z. B. „Dynamic Island“).
            string[] osdDesigns = new[] { "dark", "modern", "win11" }.Concat(Designs.All.Select(d => d.Key)).ToArray();
            string[] osdDesignNames = new[] { T("Eigenes", "Custom"), "ModernFlyouts", "Windows 11" }.Concat(Designs.All.Select(d => d.Name)).ToArray();
            int OsdDesignIndex(string design) => Math.Max(0, Array.IndexOf(osdDesigns, design == "windows" ? "modern" : design));
            var designCombo = Combo(200, OsdDesignIndex(_cfg.OsdDesign), osdDesignNames);
            ComboBox? ownCombo = null;
            Action refreshProfile = () => { };
            designCombo.SelectionChanged += (_, _) =>
            {
                _cfg.OsdDesign = osdDesigns[Math.Max(0, designCombo.SelectedIndex)];
                _cfg.OsdSavedActive = "";
                if (ownCombo != null) ownCombo.SelectedIndex = 0;
                refreshProfile();
            };
            int defaultDesign = OsdDesignIndex(Defaults.OsdDesign);
            page.Children.Add(SettingCard("\uE7F4", "Design", null,
                WithEye(WithReset(designCombo, () => designCombo.SelectedIndex = defaultDesign), () => new FrameworkElement[]
                {
                    OsdCard.CreatePreview(_cfg, "volume"),
                    OsdCard.CreatePreview(_cfg, "profile"),
                    OsdCard.CreatePreview(_cfg, "snap"),
                    OsdCard.CreatePreview(_cfg, "lockkey"),
                    NewAppOsd.CreatePreview(_cfg),
                }, T("OSD-Design", "OSD design"))));
            page.Children.Add(SavedOsdCard(out ownCombo));
            page.Children.Add(ColorProfileCard(true, out refreshProfile));
            if (!designPage) page.Children.Add(ProfileBothCard());
            page.Children.Add(NavCard("\uE70F", T("Design anpassen", "Customize the design"), null, // Edit
                () => OpenDesignEditor("osd_custom", designPage ? "design" : "osd")));
            page.Children.Add(SettingCard("\uE7F4", T("Ecken", "Corners"), null,
                CornerControl(_cfg.OsdCornerRadius, Defaults.OsdCornerRadius, r => _cfg.OsdCornerRadius = r)));
            // Designs aus dem Design-System bringen ihre Form selbst mit: „Ecken“ nur bei den älteren Designs zeigen.
            var osdCorners = page.Children[^1];
            void ShowOsdCorners() => osdCorners.Visibility = Designs.Get(_cfg.OsdDesign) == null ? Visibility.Visible : Visibility.Collapsed;
            ShowOsdCorners();
            designCombo.SelectionChanged += (_, _) => ShowOsdCorners();
        }

        // ── Aussehen-Bausteine (Augen-Knopf, Ecken, Farbfelder, Farbprofile) ──

        /// <summary>
        /// Bedienelement mit vorangestelltem Augen-Knopf: Klick öffnet rechts neben dem Fenster eine Live-
        /// Vorschau aller Ansichten des gewählten Designs. Sie bleibt offen, während weiter eingestellt
        /// wird, und baut sich bei jeder Änderung (auch ungespeichert) neu auf; ✕ oder erneuter Klick schließt.
        /// </summary>
        private StackPanel WithEye(FrameworkElement control, Func<IEnumerable<FrameworkElement>> build, string title)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var eye = FlatIconButton("\uE890", T("Live-Vorschau dieses Designs", "Live preview of this design"), () => // View
            {
                bool wasOpen = _eyePopup != null;
                CloseEyePopup();
                if (!wasOpen) OpenEyePopup(panel, build, title);
            });
            panel.Children.Add(eye);
            control.VerticalAlignment = VerticalAlignment.Center;
            panel.Children.Add(control);
            return panel;
        }

        private System.Windows.Controls.Primitives.Popup? _eyePopup;
        private System.Windows.Threading.DispatcherTimer? _eyeTimer;

        private void OpenEyePopup(Panel owner, Func<IEnumerable<FrameworkElement>> build, string title)
        {
            var host = new WrapPanel { IsHitTestVisible = false, MaxWidth = 760 }; // reine Ansicht
            string snapshot = "";
            void Refresh()
            {
                string now;
                try { now = System.Text.Json.JsonSerializer.Serialize(_cfg); } catch { return; }
                if (now == snapshot) return;
                snapshot = now;
                host.Children.Clear();
                try
                {
                    foreach (var el in build())
                    {
                        el.Margin = new Thickness(6);
                        el.VerticalAlignment = VerticalAlignment.Top;
                        host.Children.Add(el);
                    }
                }
                catch (Exception ex) { Logger.Error($"Design-Vorschau: {ex.Message}"); }
            }
            Refresh();

            var header = new DockPanel();
            var close = FlatIconButton("\uE711", T("Schließen", "Close"), CloseEyePopup); // Cancel
            close.Margin = new Thickness(0);
            DockPanel.SetDock(close, Dock.Right);
            header.Children.Add(close);
            header.Children.Add(new TextBlock
            {
                Text = title + " · " + T("Live-Vorschau", "Live preview"), Foreground = Fg, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            });

            // Nur die Kopfzeile ist eine Karte; die Vorschau darunter hat keinen Hintergrund:
            // der Desktop scheint durch, so sieht man die Deckkraft wie im Betrieb.
            var content = new StackPanel { Margin = new Thickness(8) };
            content.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 6, 6, 6), Margin = new Thickness(6, 0, 6, 0),
                Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.35 },
                Child = header,
            });
            content.Children.Add(host);

            _eyePopup = new System.Windows.Controls.Primitives.Popup
            {
                // Neben dem Fenster, damit die Einstellungen frei bleiben (kein Platz → automatisch links).
                PlacementTarget = this, Placement = System.Windows.Controls.Primitives.PlacementMode.Right,
                HorizontalOffset = 6, StaysOpen = true, AllowsTransparency = true,
                Child = content,
            };
            owner.Children.Add(_eyePopup); // im Fensterbaum, damit Theme-Ressourcen greifen
            var popup = _eyePopup;
            popup.Closed += (_, _) => owner.Children.Remove(popup);
            popup.IsOpen = true;

            _eyeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _eyeTimer.Tick += (_, _) => Refresh();
            _eyeTimer.Start();
        }

        private void CloseEyePopup()
        {
            _eyeTimer?.Stop();
            _eyeTimer = null;
            if (_eyePopup != null) _eyePopup.IsOpen = false;
            _eyePopup = null;
        }

        /// <summary>Ecken-Auswahl: Eckig / Abgerundet + Radius-Slider (nur bei abgerundet) + ↺.</summary>
        internal static UIElement CornerControl(int current, int def, Action<int> set)
        {
            int lastRound = current > 0 ? current : (def > 0 ? def : 8);
            var combo = Combo(130, current > 0 ? 1 : 0, T("Eckig", "Square"), T("Abgerundet", "Rounded"));
            var slider = SliderControl(2, 24, Math.Clamp(lastRound, 2, 24), "0' px'", v =>
            {
                lastRound = (int)v;
                if (combo.SelectedIndex == 1) set(lastRound);
            }, tick: 1, width: 120);
            slider.IsEnabled = current > 0;
            slider.Margin = new Thickness(8, 0, 0, 0);
            combo.SelectionChanged += (_, _) =>
            {
                bool round = combo.SelectedIndex == 1;
                slider.IsEnabled = round;
                set(round ? lastRound : 0);
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(combo);
            row.Children.Add(slider);
            return WithReset(row, () =>
            {
                foreach (var child in slider.Children)
                    if (child is Slider s && def > 0) s.Value = def;
                combo.SelectedIndex = def > 0 ? 1 : 0;
                set(def > 0 ? lastRound : 0);
            });
        }

        /// <summary>
        /// Hex-Farbfeld: Text, Vorschau (Klick öffnet den Farbkreis) und ↺-Knopf (setzt auf defaultValue
        /// zurück). Das Textfeld wird unter key (JSON-Name, z.B. "color_bg") für Farbprofile registriert.
        /// </summary>
        private static void AddColorField(StackPanel host, Dictionary<string, TextBox> boxes, string key, string label,
            string value, string defaultValue, Action<string> onChange)
        {
            var box = TextField(value, 100);
            box.Margin = new Thickness(0);
            boxes[key] = box;

            var swatch = new Border
            {
                Width = 24, Height = 24, Margin = new Thickness(8, 0, 0, 0), CornerRadius = new CornerRadius(6),
                BorderBrush = FieldBorder, BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand, ToolTip = T("Farbe auswählen", "Pick a color"),
            };
            void Update()
            {
                try { swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(box.Text)); }
                catch { swatch.Background = Brushes.Transparent; }
            }
            Update();
            box.TextChanged += (_, _) => { onChange(box.Text.Trim()); Update(); };

            var reset = ResetButton(() => box.Text = defaultValue); // TextChanged übernimmt Wert + Vorschau
            reset.Margin = new Thickness(8, 0, 0, 0);

            var row = Row(new TextBlock { Text = label, Width = 160, Foreground = Fg, VerticalAlignment = VerticalAlignment.Center }, box, swatch, reset);
            row.Margin = new Thickness(0, 0, 0, 4);

            swatch.MouseLeftButtonUp += (_, _) =>
            {
                // Im Fensterbaum einhängen, damit Slider/Textfeld die Theme-Stile bekommen.
                var picker = new ColorWheelPopup(swatch, box.Text, hex => box.Text = hex);
                row.Children.Add(picker);
                picker.Closed += (_, _) => row.Children.Remove(picker);
                picker.IsOpen = true;
            };
            host.Children.Add(row);
        }

        /// <summary>
        /// Eigene Farbprofile (cfg.color_profiles, Name → {JSON-Key → Hex}) für eine Farbgruppe:
        /// gespeichertes Profil anwenden oder löschen, aktuelle Farben unter einem Namen speichern.
        /// Ein Profil kann Profil- und Dark-Farben enthalten; jede Gruppe liest/schreibt nur ihre Keys.
        /// Profile werden sofort gespeichert, die angewendeten Farben erst mit "Speichern".
        /// </summary>
        private UIElement BuildColorPresets(Dictionary<string, TextBox> boxes)
        {
            var combo = new ComboBox { Width = 200 };
            bool filling = false;
            void Refill(string? select = null)
            {
                filling = true;
                combo.Items.Clear();
                combo.Items.Add("—");
                foreach (var name in _cfg.ColorProfiles.Where(kv => kv.Value.Keys.Any(k => boxes.ContainsKey(PresetKey(k))))
                                                       .Select(kv => kv.Key)
                                                       .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                    combo.Items.Add(name);
                combo.SelectedIndex = select != null && combo.Items.Contains(select) ? combo.Items.IndexOf(select) : 0;
                filling = false;
            }
            Refill();

            var delete = FlatIconButton("\uE74D", T("Gewähltes Farbprofil löschen", "Delete the selected color profile"), () => // Delete
            {
                if (combo.SelectedIndex <= 0 || combo.SelectedItem is not string name || !_cfg.ColorProfiles.TryGetValue(name, out var preset)) return;
                if (!DesignSaveDialog.ConfirmDelete(this, name, design: false)) return;
                foreach (var key in preset.Keys.Where(k => boxes.ContainsKey(PresetKey(k))).ToList()) preset.Remove(key);
                if (preset.Count == 0) _cfg.ColorProfiles.Remove(name);
                PersistColorProfiles();
                Refill();
            });
            delete.Margin = new Thickness(4, 0, 0, 0);
            delete.IsEnabled = false;
            // Auswahl wendet das Profil gleich an
            combo.SelectionChanged += (_, _) =>
            {
                delete.IsEnabled = combo.SelectedIndex > 0;
                if (filling || combo.SelectedItem is not string name || !_cfg.ColorProfiles.TryGetValue(name, out var preset)) return;
                foreach (var kv in preset)
                    if (boxes.TryGetValue(PresetKey(kv.Key), out var box) && (kv.Value != MediaLayout.CoverColor || box.Tag as string == MediaLayout.CoverColor)) box.Text = kv.Value;
                if (_status != null) _status.Text = IsDesignEditor(_currentPage)
                    ? T($"Farbprofil „{name}“ angewendet – als eigenes Design speichern, um es zu behalten", $"Color profile “{name}” applied – save as your own design to keep it")
                    : T($"Farbprofil „{name}“ angewendet – mit „Speichern“ übernehmen", $"Color profile “{name}” applied – click “Save” to keep it");
            };

            var save = Btn(T("Als Farbprofil speichern …", "Save as color profile …"));
            save.Margin = new Thickness(12, 0, 0, 0);
            save.Click += (_, _) =>
            {
                if (DesignSaveDialog.AskProfileName(this) is not { } name) return;
                if (!_cfg.ColorProfiles.TryGetValue(name, out var preset))
                    _cfg.ColorProfiles[name] = preset = new Dictionary<string, string>();
                foreach (var kv in boxes) preset[kv.Key] = kv.Value.Text.Trim();
                PersistColorProfiles();
                Refill(name);
                if (_status != null) _status.Text = T($"Farbprofil „{name}“ gespeichert ✓", $"Color profile “{name}” saved ✓");
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(combo);
            row.Children.Add(delete);
            row.Children.Add(save);
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            panel.Children.Add(OptionRow(T("Farbprofil", "Color profile"), row));
            panel.Children.Add(new Border { Height = 1, Background = CardBorder, Margin = new Thickness(0, 6, 0, 6) });
            return panel;
        }

        private static string PresetKey(string key) => ColorProfiles.Normalize(key);

        /// <summary>
        /// Zeile „Farbprofil“ für OSD (osd) oder Flyout: Auswahl wendet das Profil gleich an, ↺ zurück auf das Design; aufgeklappt
        /// speichern, löschen und ob ein Profil für beide gilt. Die Liste ist für beide dieselbe. refresh zeigt nach einem Designwechsel den neuen Stand.
        /// </summary>
        private Border ColorProfileCard(bool osd, out Action refresh, Action? changed = null)
        {
            var combo = new ComboBox { Width = 200 };
            bool filling = false;
            void Fill()
            {
                filling = true;
                var names = _cfg.ColorProfiles.Where(kv => ColorProfiles.Usable(kv.Value)).Select(kv => kv.Key).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                combo.Items.Clear();
                combo.Items.Add("—");
                foreach (var n in names) combo.Items.Add(n);
                string? active = names.FirstOrDefault(n => ColorProfiles.Matches(_cfg, _cfg.ColorProfiles[n], osd));
                combo.SelectedIndex = active != null ? names.IndexOf(active) + 1 : 0;
                filling = false;
            }
            Fill();
            combo.MouseLeftButtonUp += (_, e) => e.Handled = true; // klappt die Karte nicht mit auf
            void ToDesignColors()
            {
                ColorProfiles.Clear(_cfg, osd);
                if (_cfg.ColorProfileBoth) ColorProfiles.Clear(_cfg, !osd);
                changed?.Invoke();
                if (_status != null) _status.Text = T("Farben des Designs – mit „Speichern“ übernehmen", "Colors of the design – click “Save” to keep them");
            }
            // „—“ steht schon da, wenn die Farben zu keinem Profil passen: Auch dann gilt der Klick darauf.
            bool wasNone = false;
            combo.DropDownOpened += (_, _) => wasNone = combo.SelectedIndex == 0;
            combo.AddHandler(System.Windows.Input.Mouse.MouseUpEvent, new System.Windows.Input.MouseButtonEventHandler((_, e) =>
            {
                var hit = e.OriginalSource as DependencyObject;
                while (hit != null && hit is not ComboBoxItem) hit = VisualTreeHelper.GetParent(hit);
                if (wasNone && hit is ComboBoxItem { Content: "—" }) ToDesignColors();
            }), true);
            combo.SelectionChanged += (_, _) =>
            {
                if (filling) return;
                if (combo.SelectedIndex == 0)
                {
                    ToDesignColors();
                    return;
                }
                if (combo.SelectedIndex < 0 || combo.SelectedItem is not string name) return;
                var preset = _cfg.ColorProfiles[name];
                ColorProfiles.Apply(_cfg, preset, osd);
                if (_cfg.ColorProfileBoth) ColorProfiles.Apply(_cfg, preset, !osd);
                changed?.Invoke();
                if (_status != null) _status.Text = T($"Farbprofil „{name}“ angewendet – mit „Speichern“ übernehmen", $"Color profile “{name}” applied – click “Save” to keep it");
            };

            void DeleteSelected()
            {
                if (combo.SelectedIndex <= 0 || combo.SelectedItem is not string name) return;
                if (!DesignSaveDialog.ConfirmDelete(this, name, design: false)) return;
                _cfg.ColorProfiles.Remove(name);
                PersistColorProfiles();
                Fill();
            }
            var delete = FlatIconButton("\uE74D", T("Gewähltes Farbprofil löschen", "Delete the selected color profile"), DeleteSelected); // Delete
            void DeleteState() => delete.IsEnabled = combo.SelectedIndex > 0;
            DeleteState();
            combo.SelectionChanged += (_, _) => DeleteState();
            refresh = () =>
            {
                Fill();
                DeleteState();
            };
            var reset = WithReset(combo, () =>
            {
                ColorProfiles.Clear(_cfg, osd);
                changed?.Invoke();
                Fill();
            });
            // Eine Zeile; der Schalter „gemeinsam“ steht als eigene Zeile auf der Seite (ProfileBothCard)
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(reset);
            delete.Margin = new Thickness(4, 0, 0, 0);
            header.Children.Add(delete);
            return SettingCard("\uE790", T("Farbprofil", "Color profile"), null, header);
        }

        private readonly List<CheckBox> _bothToggles = new();

        /// <summary>Schalter „Farbprofil gilt für OSD und Flyout“: Alle Exemplare zeigen denselben Stand (color_profile_both).</summary>
        private CheckBox ProfileBothToggle()
        {
            CheckBox? self = null;
            self = Toggle(_cfg.ColorProfileBoth, v =>
            {
                _cfg.ColorProfileBoth = v;
                foreach (var other in _bothToggles.ToList())
                    if (!ReferenceEquals(other, self) && other.IsChecked != v) other.IsChecked = v;
            });
            _bothToggles.RemoveAll(t => PresentationSource.FromVisual(t) == null && t.IsLoaded);
            _bothToggles.Add(self);
            return self;
        }

        private Border ProfileBothCard() => SettingCard("\uE71B", T("Farbprofile für OSD und Flyout gemeinsam", "Color profiles for OSD and flyout together"), null, ProfileBothToggle()); // Link

        /// <summary>Zeile „Eigene Designs“ des Flyouts: Auswahl wendet an, aufgeklappt speichern und löschen.</summary>
        private Border SavedMediaCard(out ComboBox combo)
        {
            var saved = _cfg.MediaSavedDesigns;
            var names = saved.Select(s => s.Name).ToList();
            var own = combo = Combo(200, names.IndexOf(_cfg.MediaSavedActive) + 1, new[] { "—" }.Concat(names).ToArray());
            own.MouseLeftButtonUp += (_, e) => e.Handled = true; // klappt die Karte nicht mit auf
            own.SelectionChanged += (_, _) =>
            {
                if (own.SelectedIndex <= 0)
                {
                    if (_cfg.MediaSavedActive.Length == 0) return;
                    _cfg.SelectMediaPreset(_cfg.MediaDesign);
                    Dispatcher.BeginInvoke(new Action(RebuildCurrentPage)); // Inhalt und Farben zeigen wieder die der Voreinstellung
                    return;
                }
                saved[own.SelectedIndex - 1].ApplyTo(_cfg);
                Dispatcher.BeginInvoke(new Action(RebuildCurrentPage)); // Design, Deckkraft, Ecken usw. zeigen die neuen Werte
            };
            var editor = new MediaDesignEditor(_cfg, _customExpanded, () => { }, RebuildCurrentPage, savedChanged: PersistSavedDesigns);
            return ExpanderCard("\uE74E", T("Eigene Designs", "Your own designs"), null,
                editor.SavedDesigns(), headerControl: own);
        }

        /// <summary>Zeile „Eigene Designs“ des OSD: Auswahl wendet an, aufgeklappt speichern und löschen.</summary>
        private Border SavedOsdCard(out ComboBox combo)
        {
            var saved = _cfg.OsdSavedDesigns;
            var names = saved.Select(s => s.Name).ToList();
            var own = combo = Combo(200, names.IndexOf(_cfg.OsdSavedActive) + 1, new[] { "—" }.Concat(names).ToArray());
            own.MouseLeftButtonUp += (_, e) => e.Handled = true; // klappt die Karte nicht mit auf
            own.SelectionChanged += (_, _) =>
            {
                if (own.SelectedIndex <= 0)
                {
                    if (_cfg.OsdSavedActive.Length == 0) return;
                    _cfg.SelectOsdPreset();
                    Dispatcher.BeginInvoke(new Action(RebuildCurrentPage)); // Farben zeigen wieder die des Designs
                    return;
                }
                saved[own.SelectedIndex - 1].ApplyTo(_cfg);
                Dispatcher.BeginInvoke(new Action(RebuildCurrentPage));
            };
            void Done()
            {
                PersistOsdSavedDesigns();
                RebuildCurrentPage();
            }
            var body = MediaDesignEditor.SavedPanel(names, _cfg.OsdSavedActive,
                name =>
                {
                    saved.First(s => s.Name == name).ApplyTo(_cfg);
                    RebuildCurrentPage();
                },
                name =>
                {
                    saved.RemoveAll(s => s.Name == name);
                    if (_cfg.OsdSavedActive == name) _cfg.SelectOsdPreset();
                    Done();
                },
                text =>
                {
                    var entry = SavedOsdDesign.From(_cfg, text);
                    int index = saved.FindIndex(s => string.Equals(s.Name, text, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0) saved[index] = entry;
                    else saved.Add(entry);
                    _cfg.OsdSavedActive = text;
                    Done();
                },
                false, T("Name, z. B. Mein OSD", "Name, e.g. My OSD"),
                T("Gleicher Name überschreibt.", "The same name overwrites."));
            return ExpanderCard("\uE74E", T("Eigene Designs", "Your own designs"), null, body, headerControl: own);
        }

        private void PersistOsdSavedDesigns()
        {
            var onDisk = ConfigManager.Load();
            onDisk.OsdSavedDesigns = _cfg.OsdSavedDesigns;
            ConfigManager.Save(onDisk);
            _savedKey = SaveKey(ConfigManager.Load());
        }

        /// <summary>Farbprofile sofort auf die Platte schreiben (übrige ungespeicherte Änderungen bleiben im Fenster).</summary>
        private void PersistColorProfiles()
        {
            var onDisk = ConfigManager.Load();
            onDisk.ColorProfiles = _cfg.ColorProfiles.ToDictionary(kv => kv.Key, kv => new Dictionary<string, string>(kv.Value));
            ConfigManager.Save(onDisk);
        }

        /// <summary>Standardwerte (frische AppConfig) für die ↺-Knöpfe der Design-Einstellungen.</summary>
        private static readonly AppConfig Defaults = new();

        private static StackPanel ResetToggle(CheckBox toggle, bool def) => WithReset(toggle, () => toggle.IsChecked = def);

        // ══════════════════════════════════════════════════════════════════
        //  Media-Flyout
        // ══════════════════════════════════════════════════════════════════
        private void BuildMediaPage(StackPanel page)
        {
            page.Children.Add(PageTitle("Media-Flyout"));

            page.Children.Add(SectionTitle(T("Anzeige", "Display")));
            page.Children.Add(SettingCard("\uE8D6", T("Media-Flyout anzeigen", "Show media flyout"), null,
                Toggle(_cfg.MediaEnabled, v => _cfg.MediaEnabled = v)));
            page.Children.Add(SettingCard("\uE916", T("Anzeigedauer", "Duration"), null,
                SliderControl(2, 30, Math.Clamp(_cfg.MediaDurationMs / 1000.0, 2, 30), "0' s'", v => _cfg.MediaDurationMs = (int)Math.Round(v) * 1000, tick: 1)));
            page.Children.Add(ExpanderCard("\uE718", T("Dauerhaft anzeigen", "Keep on screen"), null,
                new KeepEditor(_cfg, LiveMediaApps, () => { }, () => _keepPlaceTouched = true), expanded: _cfg.MediaKeepApps.Count > 0));

            // Fortschrittsleiste, ±10 s und Quellen-Leiste stehen in „Design anpassen“ → Aufbau
            var showMore = Toggle(_cfg.MediaShowMore, v => _cfg.MediaShowMore = v);
            _mediaContentToggles = new List<(CheckBox, Func<bool>)> { (showMore, () => _cfg.MediaShowMore) };
            page.Children.Add(ExpanderCard("\uE712", T("„…“-Menü", "“…” menu"), null, BuildMoreMenuOptions(),
                headerControl: showMore));

            AddMediaAppearance(page, T("Aussehen", "Appearance"), designPage: false);

            page.Children.Add(SectionTitle(T("Tasten", "Keys")));
            AddSkipHotkeyCards(page);
            AddGestureCards(page);
        }

        private List<string> LiveMediaApps() =>
            _media?.GetSources().Select(s => s.Label).Distinct().OrderBy(n => n).ToList() ?? new List<string>();

        /// <summary>
        /// Aussehen des Media-Flyouts (Design mit Augen-Knopf, eigene Designs, Farbprofil, „Design anpassen“, Ecken).
        /// Steht auf der Seite „Media-Flyout“ und zusätzlich auf „Design“.
        /// </summary>
        private void AddMediaAppearance(StackPanel page, string appearanceTitle, bool designPage)
        {
            // ── Aussehen (Augen-Knopf = Live-Vorschau) ──
            page.Children.Add(SectionTitle(appearanceTitle));
            // Ältere Designs, dahinter alle aus dem Design-System (Designs.All, z. B. „Dynamic Island“).
            string[] mediaDesigns = new[] { "modernflyouts", "win11", "custom" }.Concat(Designs.All.Select(d => d.Key)).ToArray();
            string[] mediaDesignNames = new[] { "ModernFlyouts", "Windows 11", T("Eigenes", "Custom") }.Concat(Designs.All.Select(d => d.Name)).ToArray();
            var mediaDesign = Combo(200, Math.Max(0, Array.IndexOf(mediaDesigns, _cfg.MediaDesign)), mediaDesignNames);
            ComboBox? ownCombo = null;
            Action refreshProfile = () => { };
            mediaDesign.SelectionChanged += (_, _) =>
            {
                _cfg.SelectMediaPreset(mediaDesigns[Math.Max(0, mediaDesign.SelectedIndex)]);
                if (ownCombo != null) ownCombo.SelectedIndex = 0;
                refreshProfile();
                foreach (var (toggle, value) in _mediaContentToggles) toggle.IsChecked = value();
            };
            page.Children.Add(SettingCard("\uE8D6", "Design", null,
                WithEye(WithReset(mediaDesign, () => mediaDesign.SelectedIndex = Math.Max(0, Array.IndexOf(mediaDesigns, Defaults.MediaDesign))),
                    () => new[] { MediaOsd.CreatePreview(_cfg) }, T("Media-Flyout", "Media flyout"))));
            page.Children.Add(SavedMediaCard(out ownCombo));
            page.Children.Add(ColorProfileCard(false, out refreshProfile));
            if (!designPage) page.Children.Add(ProfileBothCard());
            page.Children.Add(NavCard("\uE70F", T("Design anpassen", "Customize the design"), null, // Edit
                () => OpenDesignEditor("media_custom", designPage ? "design" : "media")));
            page.Children.Add(SettingCard("\uE8D6", T("Ecken", "Corners"), null,
                CornerControl(_cfg.MediaCornerRadius, Defaults.MediaCornerRadius, r => _cfg.MediaCornerRadius = r)));
            var mediaCorners = page.Children[^1]; // wie beim OSD: nur bei den älteren Designs
            void ShowMediaCorners() => mediaCorners.Visibility = Designs.Get(_cfg.MediaDesign) == null ? Visibility.Visible : Visibility.Collapsed;
            ShowMediaCorners();
            mediaDesign.SelectionChanged += (_, _) => ShowMediaCorners();
        }

        // ══ Unterseiten „Design anpassen“ (Keys media_custom und osd_custom) ═══════════
        // Aufbau nach gängigen Vorbildern (Windows-Schnelleinstellungen, iOS-Kontrollzentrum, Shopify-Abschnitte,
        // Overlay-Baukästen): Reiter Vorlage · Aufbau · Farben (beim Flyout dazu Hintergrund), alle Teile in ihrer
        // Reihenfolge mit Ein/Aus und Pfeilen, und eine Live-Vorschau, die beim Scrollen oben stehen bleibt.
        // Alles wirkt auf einen Entwurf (DesignDraft), gespeichert wird als eigenes Design.

        private static bool IsDesignEditor(string key) => key is "media_custom" or "osd_custom";

        /// <summary>Unterseiten mit festem Kopf (nicht in der Navigation; der Eintrag der Herkunftsseite bleibt markiert).</summary>
        private static bool IsSubPage(string key) => IsDesignEditor(key) || key == "colors_both";

        private List<(CheckBox Toggle, Func<bool> Value)> _mediaContentToggles = new();
        private string _subParent = "media";                      // Seite, von der der Editor geöffnet wurde (Zurück, Navigation)
        private readonly HashSet<string> _customExpanded = new(); // aufgeklappte Bereiche (bleiben beim Umsortieren offen)
        private string _designTab = "template";                   // gewählter Reiter
        private string _osdKind = OsdCardLayout.Volume;           // OSD-Aufbau: gewählte Karte
        private System.Windows.Threading.DispatcherTimer? _customPreviewTimer;
        private Action? _customPreviewDispose; // beendet das Live-Flyout der Vorschau

        /// <summary>Karte, die wie in den Windows-11-Einstellungen auf eine Unterseite führt (ganze Karte klickbar, › rechts).</summary>
        private static Border NavCard(string glyph, string title, string? description, Action open)
        {
            var card = SettingCard(glyph, title, description, new TextBlock
            {
                Text = "\uE76C", FontFamily = IconFont, FontSize = 12, Foreground = Fg, // ChevronRight
            });
            card.Cursor = System.Windows.Input.Cursors.Hand;
            card.MouseEnter += (_, _) => Motion.Fade(card, HoverBg);
            card.MouseLeave += (_, _) => Motion.Fade(card, CardBg);
            card.MouseLeftButtonUp += (_, _) => open();
            return card;
        }

        private void OpenDesignEditor(string key, string parent)
        {
            _subParent = parent;
            _customExpanded.Clear();
            _designTab = "template";
            _osdKind = OsdCardLayout.Volume;
            _designDraft = new DesignDraft(_cfg);
            SelectPage(key);
            _pageScroll?.ScrollToTop();
        }

        /// <summary>Reiter, Vorlage oder Karte gewechselt: Seite neu aufbauen (Schalter zeigen die neuen Werte), Scroll-Position bleibt.</summary>
        private void RebuildDesignPage()
        {
            double offset = _pageScroll?.VerticalOffset ?? 0;
            SelectPage(_currentPage);
            if (_pageScroll == null) return;
            _pageScroll.UpdateLayout();
            _pageScroll.ScrollToVerticalOffset(offset);
        }

        // Schalter im Kopf: Live-OSD (Lautstärke-Karte folgt dem echten Knopf), Live-Flyout (echte Wiedergabe),
        // Dauerhaft auf dem Bildschirm (Entwurf echt an seiner Stelle). Ansichtssache: sofort gespeichert, nicht über „Speichern“.
        private readonly ScreenPreview _screen = new();
        private Action<string, int, bool>? _liveOsdCard; // Lautstärke-Karte der Vorschau bei Live-OSD
        private string _liveOsdName = "Spotify";
        private int _liveOsdPct = 64;
        private bool _liveOsdMuted;

        /// <summary>Von der App: Der Knopf hat die Lautstärke geändert (Live-OSD).</summary>
        public void OnVolumeResult(IReadOnlyList<(string Name, int Value)> results)
        {
            if (results.Count == 0) return;
            _liveOsdName = LiveOsdName(results.Select(r => r.Name).ToList());
            _liveOsdPct = results[0].Value;
            _liveOsdMuted = false;
            PushLiveOsd();
        }

        /// <summary>Von der App: Der Knopf hat stumm geschaltet (Live-OSD).</summary>
        public void OnMuteResult(IReadOnlyList<(string Name, bool Muted)> results)
        {
            if (results.Count == 0) return;
            _liveOsdName = LiveOsdName(results.Select(r => r.Name).ToList());
            _liveOsdMuted = results[0].Muted;
            PushLiveOsd();
        }

        private static string LiveOsdName(List<string> processes) =>
            AppRow.Label(processes[0]) + (processes.Count > 1 ? $" +{processes.Count - 1}" : "");

        private void PushLiveOsd()
        {
            if (!_cfg.OsdPreviewLive) return;
            _liveOsdCard?.Invoke(_liveOsdName, _liveOsdPct, _liveOsdMuted);
            _screen.SetVolume(_liveOsdName, _liveOsdPct, _liveOsdMuted);
        }

        /// <summary>Stand der Apps, die der Knopf gerade regeln würde (Startwert für Live-OSD); ohne solche App bleibt der letzte Stand.</summary>
        private void ReadLiveOsd()
        {
            try
            {
                _liveAudio.InvalidateCache();
                var levels = _liveAudio.ReadVolumes(_cfg);
                if (levels.Count == 0) return;
                _liveOsdName = LiveOsdName(levels.Select(l => l.Name).ToList());
                _liveOsdPct = levels[0].Value;
            }
            catch (Exception ex)
            {
                Logger.Error($"Live-OSD, Lautstärke lesen: {ex.Message}");
            }
        }

        /// <summary>OSD-Karte der Vorschau; die Lautstärke-Karte folgt bei Live-OSD dem echten Knopf.</summary>
        private FrameworkElement OsdPreviewCard(AppConfig cfg, string kind)
        {
            if (kind != OsdCardLayout.Volume || !_cfg.OsdPreviewLive) return OsdDesignEditor.Preview(cfg, kind);
            try
            {
                var card = OsdCard.CreateLiveVolumePreview(cfg, _liveOsdName, _liveOsdPct, _liveOsdMuted, out var update);
                _liveOsdCard = update;
                card.IsHitTestVisible = false;
                return card;
            }
            catch (Exception ex)
            {
                Logger.Error($"Live-OSD: {ex.Message}");
                return OsdDesignEditor.Preview(cfg, kind);
            }
        }

        /// <summary>Flyout der Vorschau: bei Live-Flyout das echte mit der laufenden Wiedergabe, sonst das Beispiel.</summary>
        private FrameworkElement FlyoutPreview(AppConfig cfg)
        {
            if (_cfg.MediaPreviewLive && _media != null)
            {
                var live = MediaOsd.CreateLive(cfg, _media, null, out var dispose);
                _customPreviewDispose = dispose;
                return live;
            }
            var preview = MediaOsd.CreatePreview(cfg);
            preview.IsHitTestVisible = false; // reine Ansicht
            return preview;
        }

        /// <summary>Kopf (Zurück + Pfad bzw. Titel) und Live-Vorschau über der scrollenden Seite; die Vorschau baut sich bei jeder Änderung neu.</summary>
        private void ShowDesignHeader()
        {
            bool both = _currentPage == "colors_both"; // OSD und Flyout, wirkt direkt auf die Einstellungen (kein Entwurf)
            if (_pinnedTop == null || (_designDraft == null && !both)) return;
            var draft = both ? null : _designDraft;
            var src = draft?.Cfg ?? _cfg;
            bool osd = _currentPage == "osd_custom";
            var panel = new StackPanel { Margin = new Thickness(24, 14, 32, 12), MaxWidth = 968 };
            {
                var back = FlatIconButton("\uE72B", T("Zurück", "Back"), () => SelectPage(_subParent)); // Back
                back.Width = back.Height = 32;
                back.FontSize = 14;
                back.Foreground = Fg;
                var parentName = new TextBlock
                {
                    Text = _subParent == "design" || both ? "Design" : osd ? "OSD" : "Media-Flyout", FontSize = 20, FontWeight = FontWeights.SemiBold,
                    Foreground = SubFg, Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
                };
                parentName.MouseLeftButtonUp += (_, _) => SelectPage(_subParent);
                var crumbs = new StackPanel { Orientation = Orientation.Horizontal };
                crumbs.Children.Add(back);
                crumbs.Children.Add(parentName);
                crumbs.Children.Add(new TextBlock
                {
                    Text = "\uE76C", FontFamily = IconFont, FontSize = 12, Foreground = SubFg, // ChevronRight
                    Margin = new Thickness(10, 3, 10, 0), VerticalAlignment = VerticalAlignment.Center,
                });
                crumbs.Children.Add(new TextBlock
                {
                    Text = both ? T("Beide Farben anpassen", "Customize the colors of both") : T("Design anpassen", "Customize the design"),
                    FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Fg, VerticalAlignment = VerticalAlignment.Center,
                });
                panel.Children.Add(crumbs);
            }

            // Echte Größe, bei hohen Aufbauten (Cover oben) bzw. niedrigem Fenster verkleinert, damit unten genug Platz für die Einstellungen bleibt.
            var previewHost = new Viewbox { StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Center };
            void Fit() => previewHost.MaxHeight = both ? Math.Clamp(ActualHeight - 470, 130, 260) : Math.Clamp(ActualHeight - 470, 110, 230);
            string snapshot = "";
            var saveDraft = AccentBtn(T("Als eigenes Design speichern", "Save as your own design"));
            saveDraft.Click += (_, _) =>
            {
                if (draft == null) return;
                var (choice, name) = DesignSaveDialog.AskDesign(this, draft.SuggestedName(), leaving: false);
                if (choice == LeaveChoice.Save) SaveDesignDraft(name);
            };
            if (_cfg.OsdPreviewLive) ReadLiveOsd();
            void Refresh()
            {
                Fit();
                bool dirty = draft?.Dirty == true;
                saveDraft.IsEnabled = dirty;
                saveDraft.Background = dirty ? Accent : ButtonBg;
                saveDraft.BorderBrush = dirty ? Accent : ButtonBorder;
                saveDraft.Foreground = dirty ? Brushes.White : SubFg;
                string now;
                try { now = System.Text.Json.JsonSerializer.Serialize(src) + _cfg.MediaPreviewLive + _cfg.OsdPreviewLive + _cfg.DesignPreviewScreen; }
                catch { return; }
                if (now == snapshot) return;
                snapshot = now;
                try
                {
                    _customPreviewDispose?.Invoke();
                    _customPreviewDispose = null;
                    _liveOsdCard = null;
                    if (osd)
                    {
                        // alle vier Karten des OSD
                        var cards = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
                        foreach (var kind in OsdCardLayout.Kinds)
                        {
                            var card = OsdPreviewCard(src, kind);
                            card.Margin = new Thickness(6);
                            cards.Children.Add(card);
                        }
                        previewHost.Child = cards;
                    }
                    else if (both)
                    {
                        // alle vier OSD-Karten neben dem Flyout
                        var cards = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, VerticalAlignment = VerticalAlignment.Center };
                        foreach (var kind in OsdCardLayout.Kinds)
                        {
                            var card = OsdPreviewCard(src, kind);
                            card.Margin = new Thickness(6);
                            cards.Children.Add(card);
                        }
                        var flyout = FlyoutPreview(src);
                        flyout.Margin = new Thickness(18, 0, 0, 0);
                        flyout.VerticalAlignment = VerticalAlignment.Center;
                        var row = new StackPanel { Orientation = Orientation.Horizontal };
                        row.Children.Add(cards);
                        row.Children.Add(flyout);
                        previewHost.Child = row;
                    }
                    else previewHost.Child = FlyoutPreview(src);
                }
                catch (Exception ex) { Logger.Error($"Design-Vorschau: {ex.Message}"); }

                if (_cfg.DesignPreviewScreen)
                    _screen.Show(src, _cfg.MediaPreviewLive ? _media : null, _cfg.OsdPreviewLive ? _liveOsdName : "Spotify",
                        _cfg.OsdPreviewLive ? _liveOsdPct : 64, _cfg.OsdPreviewLive && _liveOsdMuted, now);
                else _screen.Close();
            }
            Refresh();

            // bricht bei schmalem Fenster in eine zweite Zeile um
            var liveRow = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            void AddSwitch(string label, bool value, Action<AppConfig, bool> set)
            {
                var group = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 4, 10, 0) };
                liveRow.Children.Add(group);
                group.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Fg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
                group.Children.Add(Toggle(value, v =>
                {
                    set(_cfg, v);
                    // Ansichtssache: sofort gespeichert, der Speichern-Knopf bleibt davon unberührt
                    var onDisk = ConfigManager.Load();
                    set(onDisk, v);
                    ConfigManager.Save(onDisk);
                    Refresh();
                }));
            }
            if (osd || both)
                AddSwitch(T("Live-OSD", "Live OSD"), _cfg.OsdPreviewLive, (c, v) =>
                {
                    c.OsdPreviewLive = v;
                    if (v && ReferenceEquals(c, _cfg)) ReadLiveOsd();
                });
            if (!osd) AddSwitch(T("Live-Flyout", "Live flyout"), _cfg.MediaPreviewLive, (c, v) => c.MediaPreviewLive = v);
            AddSwitch(T("Dauerhaft auf dem Bildschirm", "Keep on screen"), _cfg.DesignPreviewScreen, (c, v) => c.DesignPreviewScreen = v);
            saveDraft.Margin = new Thickness(10, 4, 10, 0);
            if (draft != null) liveRow.Children.Add(saveDraft);

            panel.Children.Add(new Border { Margin = new Thickness(0, 12, 0, 0), Child = previewHost });
            panel.Children.Add(liveRow);
            _pinnedTop.Child = panel;
            _pinnedTop.Visibility = Visibility.Visible;

            _customPreviewTimer?.Stop();
            _customPreviewTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _customPreviewTimer.Tick += (_, _) => Refresh();
            _customPreviewTimer.Start();
        }

        /// <summary>Entwurf als eigenes Design speichern und gleich verwenden; andere noch nicht gespeicherte Einstellungen bleiben offen.</summary>
        private void SaveDesignDraft(string name)
        {
            if (_designDraft == null) return;
            var onDisk = ConfigManager.Load();
            _designDraft.SaveTo(name, _cfg, onDisk);
            ConfigManager.Save(onDisk);
            _savedKey = SaveKey(ConfigManager.Load());
            _designDraft = new DesignDraft(_cfg);
            ConfigApplied?.Invoke();
            if (_status != null) _status.Text = T($"Design „{name}“ gespeichert ✓", $"Design “{name}” saved ✓");
            if (IsDesignEditor(_currentPage)) RebuildDesignPage();
        }

        /// <summary>Unterseite „Design anpassen“ verlassen: bei ungespeichertem Entwurf nachfragen. false = bleiben.</summary>
        private bool ConfirmLeaveDesign()
        {
            if (!ConfirmDraftReplace()) return false;
            _designDraft = null;
            return true;
        }

        /// <summary>Entwurf wird verworfen (Seite verlassen oder anderes Design gewählt): bei ungespeicherten Änderungen nachfragen. false = bleiben.</summary>
        private bool ConfirmDraftReplace()
        {
            if (_designDraft is not { Dirty: true }) return true;
            var (choice, name) = DesignSaveDialog.AskDesign(this, _designDraft.SuggestedName(), leaving: true);
            if (choice == LeaveChoice.Stay) return false;
            if (choice == LeaveChoice.Save) SaveDesignDraft(name);
            return true;
        }

        private void HideMediaCustomHeader()
        {
            _customPreviewTimer?.Stop();
            _customPreviewTimer = null;
            _customPreviewDispose?.Invoke();
            _customPreviewDispose = null;
            _liveOsdCard = null;
            _screen.Close();
            if (_pinnedTop == null) return;
            _pinnedTop.Child = null;
            _pinnedTop.Visibility = Visibility.Collapsed;
        }

        /// <summary>Reiter über der Seite; ein Wechsel baut die Seite neu.</summary>
        private void AddDesignTabs(StackPanel page, (string Key, string Label)[] tabs)
        {
            if (!tabs.Any(tab => tab.Key == _designTab)) _designTab = tabs[0].Key;
            var bar = Segments(tabs, _designTab, key =>
            {
                _designTab = key;
                RebuildDesignPage();
            });
            bar.Margin = new Thickness(0, 4, 0, 14);
            page.Children.Add(bar);
        }

        private (string Key, string Label)[] MediaDraftTabs() => new[]
        {
            ("template", T("Vorlage", "Template")), ("layout", T("Aufbau", "Layout")), ("colors", T("Farben", "Colors")),
            ("opacity", T("Deckkraft", "Opacity")), ("cover", T("Hintergrund", "Background")),
        };

        private (string Key, string Label)[] OsdDraftTabs() => new[]
        {
            ("template", T("Vorlage", "Template")), ("layout", T("Aufbau", "Layout")), ("colors", T("Farben", "Colors")), ("opacity", T("Deckkraft", "Opacity")),
        };

        /// <summary>
        /// „Design anpassen“ des Media-Flyouts: Vorlagen als Startpunkt, alle Bereiche in der Reihenfolge des Flyouts
        /// (Ein/Aus, ↑/↓, aufgeklappt die einzelnen Elemente als Kacheln und die Feineinstellungen), Farben, Cover als Hintergrund.
        /// </summary>
        private void BuildMediaCustomPage(StackPanel page)
        {
            _designDraft ??= new DesignDraft(_cfg);
            ShowDesignHeader();
            AddDesignTabs(page, MediaDraftTabs());
            AddMediaDraftTab(page, _designDraft.Cfg, _designTab);
        }

        /// <summary>Inhalt eines Reiters von „Design anpassen“ des Flyouts.</summary>
        private void AddMediaDraftTab(StackPanel page, AppConfig cfg, string tab)
        {
            var editor = new MediaDesignEditor(cfg, _customExpanded, () => { }, RebuildDesignPage);
            switch (tab)
            {
                case "layout":
                    page.Children.Add(editor.Elements());
                    break;
                case "colors":
                    page.Children.Add(ContentCard("\uE790", T("Farben", "Colors"), null, MediaColorsWithPresets(cfg)));
                    break;
                case "opacity":
                    AddDraftOpacity(page, cfg, osd: false);
                    break;
                case "cover":
                    var art = new StackPanel();
                    art.Children.Add(OptionRow(T("Unschärfe", "Blur"),
                        ResetSlider(SliderControl(0, 60, Math.Clamp(cfg.MediaArtBlur, 0, 60), "0", v => cfg.MediaArtBlur = (int)v, tick: 1),
                            Defaults.MediaArtBlur)));
                    art.Children.Add(OptionRow(T("Abdunkelung", "Dimming"),
                        ResetSlider(SliderControl(0, 100, Math.Clamp(cfg.MediaArtDim, 0, 100), "0' %'", v => cfg.MediaArtDim = (int)v, tick: 1),
                            Defaults.MediaArtDim)));
                    page.Children.Add(ExpanderCard("\uE91B", T("Cover als Hintergrund", "Album art background"), null, art, expanded: cfg.MediaArtBackground,
                        headerControl: ResetToggle(Toggle(cfg.MediaArtBackground, v => cfg.MediaArtBackground = v), Defaults.MediaArtBackground)));
                    break;
                default:
                    page.Children.Add(editor.Templates());
                    break;
            }
        }

        /// <summary>Farben des Flyouts mit den Farbprofilen darüber.</summary>
        private UIElement MediaColorsWithPresets(AppConfig cfg)
        {
            var editor = new MediaDesignEditor(cfg, _customExpanded, () => { }, RebuildDesignPage);
            var boxes = new Dictionary<string, TextBox>();
            var colors = new StackPanel();
            colors.Children.Add(editor.DesignColors(boxes));
            colors.Children.Insert(0, BuildColorPresets(boxes));
            return colors;
        }

        /// <summary>Farben des OSD mit den Farbprofilen darüber (wie beim Flyout).</summary>
        private UIElement OsdColorsWithPresets(OsdDesignEditor editor)
        {
            var boxes = new Dictionary<string, TextBox>();
            var colors = new StackPanel();
            colors.Children.Add(editor.Colors(boxes));
            colors.Children.Insert(0, BuildColorPresets(boxes));
            return colors;
        }

        /// <summary>„Design anpassen“ des OSD: Vorlagen, je Karte die Teile in ihrer Reihenfolge (Ein/Aus, Pfeile), Farben.</summary>
        private void BuildOsdCustomPage(StackPanel page)
        {
            _designDraft ??= new DesignDraft(_cfg);
            ShowDesignHeader();
            AddDesignTabs(page, OsdDraftTabs());
            AddOsdDraftTab(page, _designDraft.Cfg, _designTab);
        }

        /// <summary>Inhalt eines Reiters von „Design anpassen“ des OSD.</summary>
        private void AddOsdDraftTab(StackPanel page, AppConfig cfg, string tab)
        {
            var editor = new OsdDesignEditor(cfg, () => { }, RebuildDesignPage);
            switch (tab)
            {
                case "opacity":
                    AddDraftOpacity(page, cfg, osd: true);
                    break;
                case "layout":
                    page.Children.Add(SettingCard("\uE8E3", T("Ausrichtung aller Karten", "Alignment of all cards"), null, editor.AlignTabs(RebuildDesignPage))); // AlignCenter
                    var kinds = OsdDesignEditor.KindTabs(_osdKind, kind =>
                    {
                        _osdKind = kind;
                        RebuildDesignPage();
                    });
                    kinds.Margin = new Thickness(0, 0, 0, 10);
                    page.Children.Add(kinds);
                    page.Children.Add(editor.Elements(_osdKind));
                    page.Children.Add(Hint(T("Oben in der Liste steht links auf der Karte. Der Schalter blendet ein Teil aus.", "The top of the list is on the left of the card. The switch hides a part.")));
                    break;
                case "colors":
                    page.Children.Add(ContentCard("\uE790", T("Farben", "Colors"), null, OsdColorsWithPresets(editor)));
                    break;
                default:
                    page.Children.Add(editor.Templates());
                    break;
            }
        }

        /// <summary>Deckkraft (und bei den älteren Designs die Ecken) im Entwurf von „Design anpassen“; gespeichert wird mit dem eigenen Design.</summary>
        private void AddDraftOpacity(StackPanel page, AppConfig cfg, bool osd)
        {
            if (osd)
            {
                page.Children.Add(SettingCard("\uE706", T("Deckkraft Hintergrund", "Background opacity"), null,
                    ResetSlider(SliderControl(0, 100, Math.Round(cfg.WinAlpha * 100), "0' %'", v => cfg.WinAlpha = Math.Clamp(v / 100.0, 0.0, 1.0), tick: 1),
                        Math.Round(Defaults.WinAlpha * 100))));
                page.Children.Add(SettingCard("\uE8D2", T("Deckkraft Text und Symbole", "Text and icon opacity"), null,
                    ResetSlider(SliderControl(20, 100, Math.Clamp(cfg.OsdContentOpacity, 20, 100), "0' %'", v => cfg.OsdContentOpacity = (int)v, tick: 1),
                        Defaults.OsdContentOpacity)));
                if (Designs.Get(cfg.OsdDesign) == null)
                    page.Children.Add(SettingCard("\uE7F4", T("Ecken", "Corners"), null,
                        CornerControl(cfg.OsdCornerRadius, Defaults.OsdCornerRadius, r => cfg.OsdCornerRadius = r)));
                return;
            }
            page.Children.Add(SettingCard("\uE706", T("Deckkraft Hintergrund", "Background opacity"), null,
                ResetSlider(SliderControl(0, 100, Math.Clamp(cfg.MediaOpacity, 0, 100), "0' %'", v => cfg.MediaOpacity = (int)v, tick: 1),
                    Defaults.MediaOpacity)));
            page.Children.Add(SettingCard("\uE8D2", T("Deckkraft Text und Symbole", "Text and icon opacity"), null,
                ResetSlider(SliderControl(20, 100, Math.Clamp(cfg.MediaContentOpacity, 20, 100), "0' %'", v => cfg.MediaContentOpacity = (int)v, tick: 1),
                    Defaults.MediaContentOpacity)));
            if (Designs.Get(cfg.MediaDesign) == null)
                page.Children.Add(SettingCard("\uE8D6", T("Ecken", "Corners"), null,
                    CornerControl(cfg.MediaCornerRadius, Defaults.MediaCornerRadius, r => cfg.MediaCornerRadius = r)));
        }

        // ══ Unterseite „Beide Farben anpassen“ (Key colors_both, von der Seite Design) ═══════════
        // Oben fest alle OSD-Karten und das Flyout, darunter die Farben beider: gemeinsam mit einer Farbreihe oder getrennt.
        // Wirkt wie die anderen Seiten auf die Einstellungen (Speichern-Knopf); behalten lassen sich die Farben als Farbprofil.

        private bool _colorsShared; // eine Farbreihe für beide

        private void BuildBothColorsPage(StackPanel page)
        {
            ShowDesignHeader();
            AddSharedColors(page, _cfg);
        }

        /// <summary>
        /// Farben beider: getrennt die Farben von OSD und Flyout untereinander, gemeinsam eine Farbreihe, die beide gleichzeitig setzt
        /// (was nur eines der beiden hat, ist so beschriftet). Einschalten übernimmt die Farben des Flyouts aufs OSD.
        /// </summary>
        private void AddSharedColors(StackPanel page, AppConfig cfg)
        {
            page.Children.Add(SettingCard("\uE790", T("Gleiche Farben für OSD und Flyout", "Same colors for OSD and flyout"),
                _colorsShared ? null : T("Beim Einschalten übernimmt das OSD die Farben des Flyouts.", "When switched on, the OSD takes the flyout's colors."),
                Toggle(_colorsShared, v =>
                {
                    _colorsShared = v;
                    if (v) ColorProfiles.Apply(cfg, ColorProfiles.Current(cfg, false), true);
                    RebuildDesignPage();
                })));
            if (!_colorsShared)
            {
                var osdBoxes = new Dictionary<string, TextBox>();
                var osdColors = new StackPanel();
                osdColors.Children.Add(new OsdDesignEditor(cfg, () => { }, RebuildDesignPage).Colors(osdBoxes));
                osdColors.Children.Insert(0, BuildColorPresets(osdBoxes));
                page.Children.Add(ContentCard("\uE7F4", "OSD", null, osdColors));
                page.Children.Add(ContentCard("\uE8D6", "Media-Flyout", null, MediaColorsWithPresets(cfg)));
                return;
            }

            string P = ColorProfiles.Prefix, Cover = MediaLayout.CoverColor;
            var flyCurrent = ColorProfiles.Current(cfg, false);
            var osdCurrent = ColorProfiles.Current(cfg, true);
            var rows = new List<(string Key, string Label, string Default, bool Fly, bool Osd)>();
            void Add(string key, string label, string def, bool fly, bool osdHas)
            {
                int i = rows.FindIndex(r => r.Key == key);
                if (i < 0) rows.Add((key, label, def, fly, osdHas));
                else rows[i] = (key, rows[i].Label, rows[i].Default, rows[i].Fly || fly, rows[i].Osd || osdHas);
            }
            if (MediaDesignColors.CoverColor(cfg) is { } coverRow) Add(coverRow.Key, coverRow.Label, coverRow.Default, true, false);
            foreach (var (key, label, def) in MediaDesignColors.For(cfg)) Add(key, label, def, true, false);
            if (cfg.OsdDesign == "dark") // OSD-Design „Eigenes“: feste Felder, die gemeinsamen Farben stehen unter media_color_*
            {
                foreach (var name in osdCurrent.Keys.Where(k => k.StartsWith(P)))
                    Add(name[P.Length..], SharedColorLabel(name[P.Length..]), osdCurrent[name], false, true);
            }
            else foreach (var (key, label, def) in OsdDesignColors.For(cfg)) Add(key, label, def, false, true);

            var boxes = new Dictionary<string, TextBox>();
            var list = new StackPanel();
            foreach (var row in rows)
            {
                string label = row.Label + (row.Fly && !row.Osd ? T(" (nur Flyout)", " (flyout only)") : !row.Fly ? T(" (nur OSD)", " (OSD only)") : "");
                string value = (row.Fly && flyCurrent.TryGetValue(P + row.Key, out var f) ? f : null)
                    ?? (osdCurrent.TryGetValue(P + row.Key, out var o) ? o : row.Default);
                var line = MediaDesignEditor.ColorRow(label, value, row.Default, hex =>
                {
                    var one = new Dictionary<string, string> { [P + row.Key] = hex };
                    if (row.Fly) ColorProfiles.Apply(cfg, one, false);
                    if (row.Osd && hex != Cover) ColorProfiles.Apply(cfg, one, true); // „aus dem Titelbild“ gibt es beim OSD nicht
                }, hexField: true, cover: row.Fly, firstColor: row.Default == Cover ? null : row.Default);
                if (line.Tag is TextBox box) boxes[P + row.Key] = box;
                list.Children.Add(line);
            }
            var panel = new StackPanel();
            panel.Children.Add(BuildColorPresets(boxes));
            panel.Children.Add(list);
            panel.Children.Add(Hint(T("Bild-Knopf: Farbe aus dem Cover (nur Flyout)", "Picture button: color from the album art (flyout only)")));
            page.Children.Add(ContentCard("\uE790", T("Farben für beide", "Colors for both"), null, panel));
        }

        private static string SharedColorLabel(string key) => key switch
        {
            MediaLayout.ColorBg => T("Hintergrund", "Background"),
            MediaLayout.ColorFg => T("Text und Symbole", "Text and icons"),
            MediaLayout.ColorSub => T("Nebentext", "Secondary text"),
            MediaLayout.ColorAccent => T("Akzent", "Accent"),
            MediaLayout.ColorButton => T("Knopf-Fläche", "Button fill"),
            MediaLayout.ColorTrack => T("Leiste (Hintergrund)", "Bar (background)"),
            MediaLayout.ColorEdge => T("Rand", "Border"),
            _ => key,
        };

        /// <summary>Liste der gespeicherten Designs sofort sichern (wie die Farbprofile); ein gewähltes gilt erst mit „Speichern“.</summary>
        private void PersistSavedDesigns()
        {
            var onDisk = ConfigManager.Load();
            onDisk.MediaSavedDesigns = _cfg.MediaSavedDesigns;
            ConfigManager.Save(onDisk);
            _savedKey = SaveKey(ConfigManager.Load());
        }

        /// <summary>Aktuelle Seite neu aufbauen (wurde die Config geändert), Scroll-Position bleibt.</summary>
        private void RebuildCurrentPage()
        {
            double offset = _pageScroll?.VerticalOffset ?? 0;
            SelectPage(_currentPage);
            if (_pageScroll == null) return;
            _pageScroll.UpdateLayout();
            _pageScroll.ScrollToVerticalOffset(offset);
        }

        // ══════════════════════════════════════════════════════════════════
        //  Tastenkürzel (alle Tasten an einem Ort; stehen zusätzlich auf ihren Fachseiten)
        // ══════════════════════════════════════════════════════════════════
        private void BuildHotkeysPage(StackPanel page)
        {
            page.Children.Add(PageTitle(T("Hotkeys", "Shortcuts")));

            page.Children.Add(SectionTitle(T("Lautstärke", "Volume")));
            AddVolumeHotkeyCards(page);

            page.Children.Add(SectionTitle(T("Profil", "Profile")));
            page.Children.Add(ProfileHotkeyCard());
            page.Children.Add(SettingCard("\uE916", T("Haltedauer für Positionswechsel", "Hold time for position switch"), null,
                SliderControl(200, 2000, _cfg.SnapHoldMs, "0' ms'", v => _cfg.SnapHoldMs = (int)v, tick: 50)));
            page.Children.Add(SettingCard("\uE916", T("Weiter halten = schneller", "Keep holding = faster"), null,
                Toggle(_cfg.SnapRepeatFast, v => _cfg.SnapRepeatFast = v)));
            if (!_cfg.LiteMode) page.Children.Add(ProfileHotkeysCard(out _));

            page.Children.Add(SectionTitle(T("Skip vor und zurück", "Skip forward and back")));
            AddSkipHotkeyCards(page);

            page.Children.Add(SectionTitle(T("Mediengesten", "Media gestures")));
            AddGestureCards(page);
            page.Children.Add(SettingCard("\uE916", T("Wie lange halten", "How long to hold"), null,
                SliderControl(200, 2000, _cfg.MediaSwitchHoldMs, "0' ms'", v => _cfg.MediaSwitchHoldMs = (int)v, tick: 50)));
            page.Children.Add(SettingCard("\uE916", T("Zeit für zweimal drücken", "Time to press twice"), null,
                SliderControl(150, 800, Math.Clamp(_cfg.MediaDoubleMs, 150, 800), "0' ms'", v => _cfg.MediaDoubleMs = (int)v, tick: 25)));
        }

        /// <summary>Seiten, auf denen dieselben Hotkey-Einstellungen stehen.</summary>
        private static readonly string[] HotkeyPages = { "profiles", "media", "hotkeys", "volume" };

        /// <summary>
        /// Setter für eine Einstellung, die auf mehreren Seiten steht: verwirft danach die anderen
        /// zwischengespeicherten Seiten, damit sie beim nächsten Öffnen den neuen Wert zeigen.
        /// </summary>
        private Action<TValue> Shared<TValue>(Action<TValue> set) => v =>
        {
            set(v);
            foreach (var k in HotkeyPages)
                if (k != _currentPage) _pages.Remove(k);
        };

        /// <summary>Zuweisungs-OSD an/aus (Seiten Allgemein und Profile &amp; Apps).</summary>
        private Border NewAppPopupCard() => SettingCard("\uE7E7", T("Zuweisungs-OSD anzeigen", "Show assignment OSD"), T("Popup bei neuen Apps", "Popup for new apps"),
            Toggle(_cfg.NewAppPopup, v =>
            {
                _cfg.NewAppPopup = v;
                _pages.Remove(_currentPage == "general" ? "profiles" : "general"); // die andere Seite zeigt beim Öffnen den neuen Wert
            }));

        /// <summary>Profil-Taste (Seiten Profile &amp; Apps, OSD und Tastenkürzel).</summary>
        private Border ProfileHotkeyCard() => SettingCard("\uE765", T("Profil wechseln", "Switch profile"), T("Halten: nächste gespeicherte Position", "Hold: next saved position"),
            BuildHotkeyCapture(_cfg.ProfileHotkey, v => _cfg.ProfileHotkey = v, allowClear: false));

        /// <summary>Skip-Hotkeys mit Weite und Ein/Aus (Seiten Media-Flyout und Tastenkürzel).</summary>
        private void AddSkipHotkeyCards(StackPanel page)
        {
            page.Children.Add(SettingCard("\uE765", T("Skip vor", "Skip forward"), null,
                BuildHotkeyCapture(_cfg.MediaSkipForwardHotkey, v => _cfg.MediaSkipForwardHotkey = v)));
            page.Children.Add(SettingCard("\uE765", T("Skip zurück", "Skip back"), null,
                BuildHotkeyCapture(_cfg.MediaSkipBackHotkey, v => _cfg.MediaSkipBackHotkey = v)));
            page.Children.Add(SettingCard("\uE916", T("Skip-Weite", "Skip distance"), T("Gilt auch für die ±-Knöpfe im Flyout", "Also for the ± buttons in the flyout"),
                SliderControl(5, 60, Math.Clamp(_cfg.MediaSkipSeconds, 5, 60), "0' s'", Shared<double>(v => _cfg.MediaSkipSeconds = (int)v), tick: 5)));
            page.Children.Add(SettingCard("\uE765", T("Skip-Hotkeys aktiv", "Skip keys enabled"), null,
                Toggle(_cfg.MediaSkipHotkeysEnabled, Shared<bool>(v => _cfg.MediaSkipHotkeysEnabled = v))));
            page.Children.Add(SettingCard("\uE768", T("Skip nur bei Wiedergabe", "Skip only while playing"),
                T("Spielt nichts (auch 10 s nach einer Pause), geht die Taste an die App im Vordergrund, z. B. ein Spiel. Aus: Skip wirkt immer.",
                  "When nothing plays (also 10 s after a pause), the key goes to the app in front, e.g. a game. Off: skip always works."),
                Toggle(_cfg.MediaSkipOnlyPlaying, Shared<bool>(v => _cfg.MediaSkipOnlyPlaying = v))));
        }

        /// <summary>Medien-Gesten Nächster Titel / Quelle wechseln (Seiten Media-Flyout und Tastenkürzel).</summary>
        private void AddGestureCards(StackPanel page)
        {
            page.Children.Add(BuildGestureCard("\uE893", T("Nächster Titel", "Next track"),
                _cfg.MediaNextEnabled, _cfg.MediaNextKey, _cfg.MediaNextGesture,
                v => _cfg.MediaNextEnabled = v, v => _cfg.MediaNextKey = v, v => _cfg.MediaNextGesture = v));
            page.Children.Add(BuildGestureCard("\uE8AB", T("Quelle wechseln", "Switch source"),
                _cfg.MediaSwitchEnabled, _cfg.MediaSwitchKey, _cfg.MediaSwitchGesture,
                v => _cfg.MediaSwitchEnabled = v, v => _cfg.MediaSwitchKey = v, v => _cfg.MediaSwitchGesture = v));
        }

        /// <summary>
        /// Medien-Geste (Nächster Titel / Quelle wechseln): Ein/Aus im Kopf, aufgeklappt Auslöser
        /// (zweimal schnell drücken / lange halten) und Taste (leer = Play/Pause-Medientaste).
        /// </summary>
        private Border BuildGestureCard(string glyph, string title, bool enabled, string key, string gesture,
            Action<bool> setEnabled, Action<string> setKey, Action<string> setGesture)
        {
            // Steht auf Medien und Hotkeys (Taste läuft über BuildHotkeyCapture, das selbst teilt).
            setEnabled = Shared(setEnabled);
            setGesture = Shared(setGesture);
            var body = new StackPanel();
            var gestureCombo = Combo(200, gesture == "double" ? 0 : 1, T("Zweimal schnell drücken", "Press twice quickly"), T("Lange halten", "Hold"));
            gestureCombo.SelectionChanged += (_, _) => setGesture(gestureCombo.SelectedIndex == 0 ? "double" : "hold");
            body.Children.Add(OptionRow(T("Auslöser", "Trigger"), gestureCombo));
            body.Children.Add(OptionRow(T("Taste", "Key"),
                BuildHotkeyCapture(key, setKey, emptyText: T("Play/Pause-Taste", "Play/Pause key"))));
            return ExpanderCard(glyph, title, null, body, headerControl: Toggle(enabled, setEnabled));
        }

        /// <summary>Ein Schalter je möglichem Eintrag des "…"-Menüs (cfg.media_more_items).</summary>
        internal static (string Key, string Label)[] MoreMenuOptions() => new[]
        {
            ("shuffle", T("Zufallswiedergabe", "Shuffle")),
            ("repeat", T("Wiederholen", "Repeat")),
            ("switch_source", T("Nächste Quelle", "Next source")),
            ("keep", T("Dauerhaft anzeigen an/aus", "Keep on screen on/off")),
            ("bridge", T("Browser-Erweiterung an/aus", "Browser extension on/off")),
            ("skip_hotkeys", T("Skip-Hotkeys an/aus", "Skip keys on/off")),
            ("skip_playing", T("Skip nur bei Wiedergabe an/aus", "Skip only while playing on/off")),
            ("pin", T("Angeheftet lassen (nicht ausblenden)", "Keep pinned (don't hide)")),
            ("save_position", T("Position speichern", "Save position")),
            ("next_position", T("Nächste Position", "Next position")),
            ("reset_position", T("Position zurücksetzen", "Reset position")),
            ("settings", T("Einstellungen öffnen", "Open settings")),
            ("close", T("Ausblenden", "Hide")),
        };

        private UIElement BuildMoreMenuOptions()
        {
            var panel = new StackPanel();
            panel.Children.Add(OptionRow(T("Rechtsklick aufs Flyout öffnet das Menü", "Right-clicking the flyout opens the menu"),
                Toggle(_cfg.MediaRightClickMenu, v => _cfg.MediaRightClickMenu = v)));
            panel.Children.Add(new Border { Height = 1, Background = CardBorder, Margin = new Thickness(0, 6, 0, 6) });
            var options = MoreMenuOptions();
            foreach (var (key, label) in options)
            {
                panel.Children.Add(OptionRow(label, Toggle(_cfg.MediaMoreItems.Contains(key), on =>
                {
                    _cfg.MediaMoreItems.Remove(key);
                    if (on) _cfg.MediaMoreItems.Add(key);
                    // Feste Reihenfolge wie im Menü, unabhängig von der Klick-Reihenfolge.
                    _cfg.MediaMoreItems = options.Select(o => o.Key).Where(_cfg.MediaMoreItems.Contains).ToList();
                })));
            }
            return panel;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Position
        // ══════════════════════════════════════════════════════════════════

        // Anordnung: getrennt oder zusammen (media_combined_side = Lage des Media-Flyouts relativ zum OSD).
        // Welches Element bei „zusammen“ am gewählten Platz sitzt (combined_anchor), ergibt sich aus dem Platz.
        internal static readonly (string Layout, string Side)[] Arrangements =
        {
            ("separate", ""),
            ("combined", "above"), // Media-Flyout über dem OSD
            ("combined", "below"), // Media-Flyout unter dem OSD
        };

        internal static int ArrangementIndex(AppConfig c) =>
            c.OsdLayout == "separate" ? 0 : c.MediaCombinedSide == "above" ? 1 : 2;

        /// <summary>Platz und Bildschirm des zusammen angezeigten Blocks = die des Ankers (combined_anchor).</summary>
        internal static (string Preset, string Device) CombinedPosition(AppConfig c) =>
            c.CombinedAnchor == "media" ? (c.MediaCornerPreset, c.MediaMonitor) : (c.OsdCornerPreset, c.OsdMonitor);

        /// <summary>
        /// Platz für den zusammen angezeigten Block: Anker passend zum Platz (oben/Mitte das obere, unten das
        /// untere Element, Placement.CombinedAnchorFor), Platz und Bildschirm für beide – so stehen sie auch im
        /// Design „Eigenes“ (dort kein Anhängen) am selben Platz. „Eigene Position“ behält den Anker.
        /// </summary>
        private void SetCombinedPosition(string preset, string device)
        {
            ApplyCombinedPosition(_cfg, preset, device);
            LayoutTouched();
        }

        internal static void ApplyCombinedPosition(AppConfig c, string preset, string device)
        {
            if (Placement.IsPreset(preset)) c.CombinedAnchor = Placement.CombinedAnchorFor(c.MediaCombinedSide, preset);
            c.OsdCornerPreset = c.MediaCornerPreset = preset;
            c.OsdMonitor = c.MediaMonitor = device;
        }

        /// <summary>Platz-Auswahl je nach Anordnung: zusammen eine für beide, getrennt je eine für OSD und Media-Flyout.</summary>
        private void BuildPositionCards(StackPanel host, List<MonitorArea> monitors)
        {
            host.Children.Clear();
            _dualPicker = null;
            _combinedPicker = null;

            if (_cfg.OsdLayout == "combined")
            {
                var (preset, device) = CombinedPosition(_cfg);
                var both = new ScreenTilePicker(monitors, device, preset);
                both.Changed += () =>
                {
                    SetCombinedPosition(both.Preset, both.Device);
                    RunPositionTest(null, null); // kurz zeigen, wo es ist
                };
                _combinedPicker = both;
                host.Children.Add(SettingCard("\uE8A9", T("OSD und Media-Flyout", "OSD and media flyout"), null,
                    WithReset(both, () =>
                    {
                        both.Set(Defaults.OsdCornerPreset, "");
                        SetCombinedPosition(Defaults.OsdCornerPreset, "");
                    })));
                return;
            }

            // Rechtsklick = OSD, Linksklick = Flyout
            var dual = new DualTilePicker(monitors, _cfg.OsdMonitor, _cfg.OsdCornerPreset, _cfg.MediaMonitor, _cfg.MediaCornerPreset);
            dual.Changed += osd =>
            {
                if (osd)
                {
                    _cfg.OsdCornerPreset = dual.OsdPreset;
                    _cfg.OsdMonitor = dual.OsdDevice;
                }
                else
                {
                    _cfg.MediaCornerPreset = dual.MediaPreset;
                    _cfg.MediaMonitor = dual.MediaDevice;
                }
                LayoutTouched();
                RunPositionTest(null, null); // kurz zeigen, wo es ist
            };
            _dualPicker = dual;
            host.Children.Add(SettingCard("\uE8A9", T("OSD und Media-Flyout", "OSD and media flyout"), T("Linksklick: Flyout · Rechtsklick: OSD", "Left click: flyout · Right click: OSD"),
                WithReset(dual, () =>
                {
                    dual.SetOsd(Defaults.OsdCornerPreset, "");
                    dual.SetMedia(Defaults.MediaCornerPreset, "");
                    _cfg.OsdCornerPreset = Defaults.OsdCornerPreset;
                    _cfg.OsdMonitor = "";
                    _cfg.MediaCornerPreset = Defaults.MediaCornerPreset;
                    _cfg.MediaMonitor = "";
                    LayoutTouched();
                })));
        }

        /// <summary>Bedienelement mit kleiner Beschriftung darüber (für Auswahlen nebeneinander).</summary>
        private static StackPanel Labeled(string label, FrameworkElement control)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            panel.Children.Add(new TextBlock { Text = label, Foreground = SubFg, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(control);
            return panel;
        }

        private void BuildPositionPage(StackPanel page)
        {
            page.Children.Add(PageTitle("Position"));
            var monitors = Placement.Monitors();
            var positionHost = new StackPanel();   // Platz auf dem Bildschirm: je nach Anordnung eine oder zwei Auswahlen
            var snapChoiceHost = new StackPanel(); // Gespeicherte Positionen → Neue Position per Auswahl
            ScreenTilePicker? snapBoth = null;
            DualTilePicker? snapDual = null;
            Action refreshSnapMarks = () => { };
            Action showChosen = () => { }; // Klick auf eine Kachel: kurz anzeigen

            // Neue Snap-Position per Kacheln statt Ziehen: getrennt je OSD und Media-Flyout, zusammen eine für den Block.
            void BuildSnapChoice()
            {
                snapChoiceHost.Children.Clear();
                snapBoth = null;
                snapDual = null;
                if (_cfg.OsdLayout == "combined")
                {
                    snapBoth = new ScreenTilePicker(monitors, "", "top_right", allowCustom: false, width: 300, maxScreenHeight: 190);
                    snapBoth.Changed += () => showChosen();
                    snapChoiceHost.Children.Add(Labeled(T("OSD und Media-Flyout", "OSD and media flyout"), snapBoth));
                }
                else
                {
                    snapDual = new DualTilePicker(monitors, "", "top_right", "", "top_center", allowCustom: false);
                    snapDual.Changed += _ => showChosen();
                    snapChoiceHost.Children.Add(Labeled(T("Linksklick: Flyout · Rechtsklick: OSD", "Left click: flyout · Right click: OSD"), snapDual));
                }
                refreshSnapMarks();
            }

            page.Children.Add(SectionTitle(T("Anordnung", "Arrangement")));
            var arrangeCombo = Combo(240, ArrangementIndex(_cfg),
                T("Getrennt", "Separate"),
                T("Media-Flyout über dem OSD", "Media flyout above OSD"),
                T("Media-Flyout unter dem OSD", "Media flyout below OSD"));
            arrangeCombo.SelectionChanged += (_, _) =>
            {
                var a = Arrangements[Math.Max(0, arrangeCombo.SelectedIndex)];
                _cfg.OsdLayout = a.Layout;
                if (a.Layout == "combined")
                {
                    _cfg.MediaCombinedSide = a.Side;
                    var (preset, device) = CombinedPosition(_cfg);
                    SetCombinedPosition(preset, device); // Anker passend zur (neuen) Reihenfolge
                }
                LayoutTouched();
                BuildPositionCards(positionHost, monitors);
                BuildSnapChoice();
            };
            page.Children.Add(SettingCard("\uE8A9", T("OSD und Media-Flyout anordnen", "Arrange OSD and media flyout"), null,
                WithReset(arrangeCombo, () => arrangeCombo.SelectedIndex = ArrangementIndex(Defaults))));

            // ── Platz-Voreinstellungen (Kacheln, passen sich automatisch an Auflösung/Taskleiste an) ──
            page.Children.Add(SectionTitle(T("Platz auf dem Bildschirm", "Place on screen")));
            page.Children.Add(positionHost);
            BuildPositionCards(positionHost, monitors);

            var test = AccentBtn(T("Testen", "Test"), 110);
            test.Margin = new Thickness(0);
            test.Click += (_, _) => RunPositionTest(null, null);
            page.Children.Add(SettingCard("\uE768", T("Position testen", "Test position"), null, test));

            page.Children.Add(SectionTitle(T("Verschieben", "Moving")));
            page.Children.Add(SettingCard("\uE7C2", T("Mit der Maus verschieben", "Drag with the mouse"), null,
                Toggle(_cfg.SnapEnabled, v => _cfg.SnapEnabled = v)));

            // Gespeicherte Positionen (Snap-Punkte)
            var snapBody = new StackPanel();
            int selectedSnap = _cfg.SnapPositions.Count > 0 ? 0 : -1;
            var numbers = new WrapPanel { Margin = new Thickness(0, 0, -6, 4) };
            var removeBtn = Btn(T("Entfernen", "Remove"));
            var testSnap = Btn(T("Testen", "Test"));
            testSnap.ToolTip = T("Ausgewählte Position kurz anzeigen", "Briefly show the selected position");
            var noSnaps = new TextBlock { Text = T("Noch keine gespeicherten Positionen", "No saved positions yet"), Foreground = SubFg, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) };

            // Nummern der gespeicherten Positionen (Auswahl für Testen/Entfernen) und ihre Beschriftungen in den Kacheln
            void RenderSnaps()
            {
                numbers.Children.Clear();
                var points = _cfg.SnapPositions;
                if (selectedSnap >= points.Count) selectedSnap = points.Count - 1;
                for (int i = 0; i < points.Count; i++)
                {
                    int index = i;
                    var sp = points[i];
                    bool known = sp.OsdTile != null || sp.MediaTile != null;
                    var chip = Chip(known ? $"{i + 1}" : T($"{i + 1} (eigene Position)", $"{i + 1} (own position)"), i == selectedSnap, () =>
                    {
                        selectedSnap = index;
                        RenderSnaps();
                    }, check: true);
                    chip.ToolTip = FormatSnapPoint(sp);
                    chip.Margin = new Thickness(0, 0, 6, 6);
                    numbers.Children.Add(chip);
                }
                noSnaps.Visibility = points.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                removeBtn.IsEnabled = testSnap.IsEnabled = selectedSnap >= 0;
                refreshSnapMarks();
            }
            refreshSnapMarks = () =>
            {
                var points = _cfg.SnapPositions;
                if (snapDual != null)
                {
                    var marks = new List<(string, string, string)>();
                    for (int i = 0; i < points.Count; i++)
                    {
                        if (points[i].OsdTile is { } ot) marks.Add((ot, points[i].OsdDevice ?? "", $"{i + 1} OSD"));
                        if (points[i].MediaTile is { } mt) marks.Add((mt, points[i].MediaDevice ?? "", $"{i + 1} Flyout"));
                    }
                    snapDual.SetMarks(marks);
                }
                snapBoth?.SetMarks(points.Select((sp, i) => (sp.MediaTile ?? sp.OsdTile, sp.MediaDevice ?? sp.OsdDevice ?? "", $"{i + 1}"))
                    .Where(m => m.Item1 != null).Select(m => (m.Item1!, m.Item2, m.Item3)));
            };

            removeBtn.Click += (_, _) =>
            {
                if (selectedSnap < 0 || selectedSnap >= _cfg.SnapPositions.Count) return;
                _cfg.SnapPositions.RemoveAt(selectedSnap);
                RenderSnaps();
            };
            testSnap.Click += (_, _) =>
            {
                int i = selectedSnap;
                if (i < 0 || i >= _cfg.SnapPositions.Count) return;
                var sp = _cfg.SnapPositions[i];
                bool hasMedia = sp.MediaX.HasValue && sp.MediaY.HasValue;
                // Wie im Snap-Zyklus: mit eigener Flyout-Position stehen beide unabhängig.
                var tmp = CloneConfig(_cfg);
                tmp.SnapIndependent = hasMedia && tmp.OsdLayout == "combined";
                RunPositionTest(new Point(sp.X, sp.Y), hasMedia ? new Point(sp.MediaX!.Value, sp.MediaY!.Value) : null, tmp);
            };

            snapBody.Children.Add(noSnaps);
            snapBody.Children.Add(numbers);
            var snapButtons = Row(testSnap, removeBtn);
            snapButtons.Margin = new Thickness(0, 0, 0, 8);
            snapBody.Children.Add(snapButtons);

            BuildSnapChoice();

            // Wie bei den Platz-Voreinstellungen berechnen (inkl. Bildschirmrand und Überlappung). Getrennt am
            // gleichen Platz: Flyout bleibt dort, OSD bündig (gleiche X) darunter bzw. darüber. Zusammen: angehängt
            // wie im Betrieb. Als Höhe die echte Flyout-Höhe plus Platz für die Quellen-Leiste, damit sich beide
            // später nicht überdecken. Der Snap-Punkt setzt beide später genau dorthin (snap_independent).
            (Rect Osd, Rect Media, AppConfig Cfg) ChosenLayout()
            {
                var tmp = CloneConfig(_cfg);
                if (snapBoth != null)
                {
                    tmp.OsdCornerPreset = tmp.MediaCornerPreset = snapBoth.Preset;
                    tmp.OsdMonitor = tmp.MediaMonitor = snapBoth.Device;
                    tmp.CombinedAnchor = Placement.CombinedAnchorFor(tmp.MediaCombinedSide, snapBoth.Preset);
                }
                else if (snapDual != null)
                {
                    tmp.OsdCornerPreset = snapDual.OsdPreset;
                    tmp.OsdMonitor = snapDual.OsdDevice;
                    tmp.MediaCornerPreset = snapDual.MediaPreset;
                    tmp.MediaMonitor = snapDual.MediaDevice;
                }
                tmp.SnapIndependent = false;
                var probe = MediaOsd.CreateTest(tmp);
                double mediaHeight = probe.MeasureHeight() + 32;
                probe.Close();
                var (o, m) = Placement.Layout(tmp, mediaHeight, osdYields: true);
                tmp.SnapIndependent = tmp.OsdLayout == "combined"; // „Testen“: beide genau an den berechneten Punkten
                return (o, m, tmp);
            }

            var addChosen = Btn(T("Hinzufügen", "Add"));
            addChosen.Click += (_, _) =>
            {
                var (osd, media, chosen) = ChosenLayout();
                // Media-Position immer mit speichern: wirkt bei "Getrennt" und "OSD unter/über dem Media";
                // hängt das Flyout am OSD, wird sie ignoriert.
                var point = new SnapPosition
                {
                    X = (int)Math.Round(osd.X), Y = (int)Math.Round(osd.Y),
                    MediaX = (int)Math.Round(media.X), MediaY = (int)Math.Round(media.Y),
                    OsdTile = chosen.OsdCornerPreset, OsdDevice = chosen.OsdMonitor,
                    MediaTile = chosen.MediaCornerPreset, MediaDevice = chosen.MediaMonitor,
                };
                _cfg.SnapPositions.Add(point);
                selectedSnap = _cfg.SnapPositions.Count - 1;
                RenderSnaps();
            };
            var testChosen = Btn(T("Testen", "Test"));
            testChosen.ToolTip = T("Auswahl vor dem Hinzufügen kurz anzeigen", "Briefly show the choice before adding it");
            showChosen = () =>
            {
                var (osd, media, tmp) = ChosenLayout();
                RunPositionTest(osd.TopLeft, media.TopLeft, tmp);
            };
            testChosen.Click += (_, _) => showChosen();

            RenderSnaps();
            var chosenButtons = Inline(testChosen, addChosen);
            chosenButtons.Margin = new Thickness(0, 8, 0, 4);
            var byTile = new StackPanel();
            byTile.Children.Add(snapChoiceHost);
            byTile.Children.Add(chosenButtons);

            // Eigene Position: die Stelle, an die OSD und Flyout mit der Maus gezogen wurden
            var addCurrent = Btn(T("Aktuelle Position speichern", "Save current position"));
            addCurrent.HorizontalAlignment = HorizontalAlignment.Left;
            addCurrent.Click += (_, _) =>
            {
                // frisch von der Platte: dort steht, wohin zuletzt gezogen wurde
                _cfg.SnapPositions.Add(OsdManager.CurrentSnapPoint(ConfigManager.Load()));
                selectedSnap = _cfg.SnapPositions.Count - 1;
                RenderSnaps();
            };
            var byHand = new StackPanel();
            byHand.Children.Add(Hint(T("Vorher OSD und Flyout mit der Maus an die Stelle ziehen („Mit der Maus verschieben“ an).",
                "First drag the OSD and flyout to the spot with the mouse (“Drag with the mouse” on).")));
            byHand.Children.Add(addCurrent);

            var newHost = new ContentControl { Focusable = false, Margin = new Thickness(0, 6, 0, 0) };
            void ShowSnapTab()
            {
                newHost.Content = _snapTab == "own" ? byHand : byTile;
            }
            // Segments markiert die Auswahl nicht selbst um: bei jedem Wechsel neu bauen
            var tabsHost = new ContentControl { Focusable = false, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            void BuildSnapTabs()
            {
                tabsHost.Content = Segments(new[] { ("tile", T("Per Kachel", "By tile")), ("own", T("Eigene Position", "Own position")) }, _snapTab, key =>
                {
                    _snapTab = key;
                    BuildSnapTabs();
                    ShowSnapTab();
                });
            }
            BuildSnapTabs();
            ShowSnapTab();
            snapBody.Children.Add(tabsHost);
            snapBody.Children.Add(newHost);
            page.Children.Add(ExpanderCard("\uE81D", T("Gespeicherte Positionen", "Saved positions"), T("Wechseln: „Profil wechseln“ halten", "Switch: hold “Switch profile”"), snapBody));

            page.Children.Add(SectionTitle(T("Erweitert", "Advanced")));

            // Manuelle Anker-Position
            var x = TextField(_cfg.OsdX?.ToString(CultureInfo.InvariantCulture) ?? "", 80);
            var y = TextField(_cfg.OsdY?.ToString(CultureInfo.InvariantCulture) ?? "", 80);
            x.Margin = y.Margin = new Thickness(0);
            // Von Hand eingetragen = eigene Position (Platz-Voreinstellung aus).
            void ManualEdited()
            {
                if (!(x.IsKeyboardFocusWithin || y.IsKeyboardFocusWithin)) return;
                _cfg.OsdCornerPreset = "custom";
                _dualPicker?.SetOsd("custom");
                if (_cfg.CombinedAnchor != "media") _combinedPicker?.Set("custom");
                LayoutTouched();
            }
            x.TextChanged += (_, _) => { _cfg.OsdX = int.TryParse(x.Text, out var vx) ? vx : null; ManualEdited(); };
            y.TextChanged += (_, _) => { _cfg.OsdY = int.TryParse(y.Text, out var vy) ? vy : null; ManualEdited(); };
            _anchorXBox = x;
            _anchorYBox = y;
            var xyRow = new StackPanel { Orientation = Orientation.Horizontal };
            xyRow.Children.Add(new TextBlock { Text = "X", Foreground = SubFg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            xyRow.Children.Add(x);
            xyRow.Children.Add(new TextBlock { Text = "Y", Foreground = SubFg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) });
            xyRow.Children.Add(y);
            page.Children.Add(SettingCard("\uE707", T("OSD-Position in Pixeln", "OSD position in pixels"), T("Leer = oben rechts", "Empty = top right"), xyRow));

            var resetBtn = Btn(T("Zurücksetzen", "Reset"), 120);
            resetBtn.Margin = new Thickness(0);
            resetBtn.Click += (_, _) => ResetPositions();
            page.Children.Add(SettingCard("\uE707", T("Position zurücksetzen", "Reset position"), null, resetBtn));
        }

        // ══════════════════════════════════════════════════════════════════
        //  Spotify
        // ══════════════════════════════════════════════════════════════════
        private void BuildSpotifyPage(StackPanel page)
        {
            page.Children.Add(PageTitle("Spotify"));

            page.Children.Add(SectionTitle(T("Steuerung", "Control")));
            page.Children.Add(SettingCard("\uE8D6", T("Über die Spotify-API steuern", "Control via the Spotify API"), T("Braucht Spotify Premium", "Needs Spotify Premium"),
                Toggle(_cfg.SpotifyUseApi, v => _cfg.SpotifyUseApi = v)));

            // ── Einrichten: Schritt für Schritt, Dashboard-Link klickbar, Redirect-URI zum Kopieren ──
            page.Children.Add(SectionTitle(T("Einrichten (einmalig)", "Setup (once)")));
            var steps = new StackPanel();
            void Step(int n, params System.Windows.Documents.Inline[] parts)
            {
                var line = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Fg, FontSize = 13, Margin = new Thickness(0, 0, 0, 8) };
                line.Inlines.Add(new System.Windows.Documents.Run($"{n}.  ") { FontWeight = FontWeights.SemiBold });
                foreach (var p in parts) line.Inlines.Add(p);
                steps.Children.Add(line);
            }
            System.Windows.Documents.Run Txt(string de, string en) => new(T(de, en));
            Step(1, Txt("Öffne das ", "Open the "), Link("Spotify-Developer-Dashboard", "https://developer.spotify.com/dashboard"),
                Txt(" und melde dich mit deinem Spotify-Konto an.", " and sign in with your Spotify account."));
            Step(2, Txt("Klicke „Create app“. Name und Beschreibung sind egal (z. B. „KnobFlyout“).",
                "Click “Create app”. Name and description don't matter (e.g. “KnobFlyout”)."));
            Step(3, Txt("Bei „Redirect URIs“ genau diese Adresse eintragen und „Add“ klicken:",
                "Under “Redirect URIs” enter exactly this address and click “Add”:"));
            var uriBox = TextField(SpotifyController.RedirectUri, 300);
            uriBox.IsReadOnly = true;
            var copy = Btn(T("Kopieren", "Copy"), 100);
            copy.Click += (_, _) =>
            {
                try { Clipboard.SetText(SpotifyController.RedirectUri); copy.Content = T("Kopiert ✓", "Copied ✓"); }
                catch (Exception ex) { Logger.Error($"Zwischenablage: {ex.Message}"); }
            };
            var uriRow = Row(uriBox, copy);
            uriRow.Margin = new Thickness(22, 0, 0, 10);
            steps.Children.Add(uriRow);
            Step(4, Txt("Bei „Which API/SDKs are you planning to use?“ „Web API“ ankreuzen, den Bedingungen zustimmen und „Save“ klicken.",
                "Under “Which API/SDKs are you planning to use?” tick “Web API”, accept the terms and click “Save”."));
            Step(5, Txt("In der neuen App „Settings“ öffnen: „Client ID“ kopieren, „View client secret“ klicken und das Secret kopieren – beides unten eintragen.",
                "Open “Settings” in the new app: copy the “Client ID”, click “View client secret” and copy the secret – enter both below."));
            Step(6, Txt("„Verbinden“ klicken: Der Browser öffnet Spotify, dort mit „Agree“ zustimmen – danach steht hier „Verbunden“. „Testen“ prüft, ob der Zugang funktioniert.",
                "Click “Connect”: the browser opens Spotify, approve with “Agree” – then it says “Connected” here. “Test” checks that access works."));
            // Aufklappbar; schon verbunden = zugeklappt, sonst offen
            page.Children.Add(ExpanderCard("\uE946", T("So geht's", "How it works"), null, steps, expanded: !_spotify.HasToken));

            page.Children.Add(SectionTitle(T("Verbindung", "Connection")));
            var body = new StackPanel();

            body.Children.Add(Label("Client ID"));
            var id = TextField(_cfg.SpotifyClientId);
            id.MaxWidth = 420;
            id.HorizontalAlignment = HorizontalAlignment.Left;
            id.Width = 420;
            id.TextChanged += (_, _) => _cfg.SpotifyClientId = id.Text.Trim();
            body.Children.Add(id);

            body.Children.Add(Label("Client Secret"));
            var secret = TextField(_cfg.SpotifyClientSecret);
            secret.HorizontalAlignment = HorizontalAlignment.Left;
            secret.Width = 420;
            secret.TextChanged += (_, _) => _cfg.SpotifyClientSecret = secret.Text.Trim();
            body.Children.Add(secret);

            var status = new TextBlock
            {
                Text = _spotify.HasToken ? T("✅ Verbunden", "✅ Connected") : T("❌ Nicht verbunden", "❌ Not connected"),
                Foreground = _spotify.HasToken ? FromHex("#4ade80") : FromHex("#f87171"),
                Margin = new Thickness(0, 4, 0, 8),
            };
            body.Children.Add(status);

            var connectBtn = AccentBtn(T("Verbinden", "Connect"), 130);
            var testBtn = Btn(T("Testen", "Test"), 130);
            var disconnectBtn = Btn(T("Trennen", "Disconnect"), 130);
            var buttons = Row(connectBtn, testBtn, disconnectBtn);
            buttons.Margin = new Thickness(0);
            body.Children.Add(buttons);

            connectBtn.Click += async (_, _) =>
            {
                var cid = id.Text.Trim();
                var csec = secret.Text.Trim();
                if (cid.Length == 0 || csec.Length == 0)
                {
                    status.Text = "⚠️ " + T("Zuerst Client ID & Secret eingeben.", "Enter Client ID & Secret first.");
                    status.Foreground = FromHex("#f87171");
                    return;
                }

                _cfg.SpotifyClientId = cid;
                _cfg.SpotifyClientSecret = csec;
                ConfigManager.Save(_cfg);

                status.Text = "⏳ " + T("Verbinde – Browser öffnet sich …", "Connecting – browser opens …");
                status.Foreground = FromHex("#facc15");
                connectBtn.IsEnabled = false;
                try
                {
                    var (ok, message) = await _spotify.ConnectAsync(cid, csec);
                    status.Text = (ok ? "✅ " : "❌ ") + message;
                    status.Foreground = FromHex(ok ? "#4ade80" : "#f87171");
                }
                finally
                {
                    connectBtn.IsEnabled = true;
                }
            };

            // Testen: Token holen (bei Bedarf erneuern) und das Konto abfragen – zeigt, ob Verbinden/Erneuern wirklich klappt.
            testBtn.Click += async (_, _) =>
            {
                status.Text = "⏳ " + T("Teste …", "Testing …");
                status.Foreground = FromHex("#facc15");
                testBtn.IsEnabled = false;
                try
                {
                    var (ok, message) = await _spotify.TestAsync(_cfg);
                    status.Text = (ok ? "✅ " : "❌ ") + message;
                    status.Foreground = FromHex(ok ? "#4ade80" : "#f87171");
                }
                finally
                {
                    testBtn.IsEnabled = true;
                }
            };

            disconnectBtn.Click += (_, _) =>
            {
                _spotify.Disconnect();
                status.Text = T("❌ Nicht verbunden", "❌ Not connected");
                status.Foreground = FromHex("#f87171");
            };

            page.Children.Add(ContentCard("\uE71B", T("Spotify-Konto", "Spotify account"), null, body));
            page.Children.Add(Hint(T("Geht nur, wenn Spotify gerade auf einem Gerät abspielt.", "Only works while Spotify is playing on a device.")));
        }

        // ══════════════════════════════════════════════════════════════════
        //  Browser-Addon (Erweiterung für YouTube, Twitch, Crunchyroll, VOE, Jellyfin)
        // ══════════════════════════════════════════════════════════════════
        private System.Windows.Threading.DispatcherTimer? _youtubeTimer;

        private void BuildYoutubePage(StackPanel page)
        {
            page.Children.Add(PageTitle("Browser-Addon"));

            page.Children.Add(SectionTitle(T("Steuerung", "Control")));
            page.Children.Add(SettingCard("\uE714", T("Lautstärke im Video-Player regeln", "Control volume in the video player"), T("Firefox, Chrome und Opera", "Firefox, Chrome and Opera"),
                Toggle(_cfg.YoutubeBridge, v => _cfg.YoutubeBridge = v)));

            // Je Webseite abschaltbar: dort regelt der Knopf dann wie ohne Addon den ganzen Browser
            page.Children.Add(SectionTitle(T("Webseiten", "Websites")));
            foreach (var site in YouTubeBridge.Sites)
            {
                string key = site;
                page.Children.Add(SettingCard("\uE774", key, null,
                    Toggle(!_cfg.BridgeSitesOff.Contains(key, StringComparer.OrdinalIgnoreCase), on =>
                    {
                        var list = _cfg.BridgeSitesOff.Where(s => !s.Equals(key, StringComparison.OrdinalIgnoreCase)).ToList();
                        if (!on) list.Add(key);
                        _cfg.BridgeSitesOff = list;
                    })));
            }

            // Status live (1 s), damit man beim Installieren der Erweiterung sieht, wann sie sich verbindet
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 2, 0, 2) };
            void Refresh()
            {
                status.Inlines.Clear();
                if (!_youtube.IsListening)
                {
                    status.Inlines.Add(new System.Windows.Documents.Run(T("Aus – nach dem Einschalten „Speichern“ klicken", "Off – click “Save” after switching on")) { Foreground = Fg });
                    return;
                }
                foreach (var browser in YouTubeBridge.Browsers)
                {
                    var s = _youtube.Status(browser);
                    string name = YouTubeBridge.DisplayName(browser);
                    string text = !s.Connected
                        ? T($"❌ {name}: nicht verbunden", $"❌ {name}: not connected")
                          + (browser != YouTubeBridge.Firefox ? T($" ({name} verbindet sich erst, wenn ein Video-Tab offen ist)", $" ({name} only connects while a video tab is open)") : "")
                        : T($"✅ {name}: verbunden · {s.Tabs} Player", $"✅ {name}: connected · {s.Tabs} player{(s.Tabs == 1 ? "" : "s")}")
                          + (s.PlayingTitle != null ? T($" · Knopf regelt: {s.PlayingTitle}", $" · knob controls: {s.PlayingTitle}")
                                                    : T($" · gerade spielt kein Video (Knopf regelt {name})", $" · no video playing (knob controls {name})"));
                    if (status.Inlines.Count > 0) status.Inlines.Add(new System.Windows.Documents.LineBreak());
                    status.Inlines.Add(new System.Windows.Documents.Run(text) { Foreground = s.Connected ? FromHex("#4ade80") : Fg });
                }
            }
            Refresh();
            _youtubeTimer?.Stop();
            _youtubeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _youtubeTimer.Tick += (_, _) => Refresh();
            _youtubeTimer.Start();
            page.Children.Add(ContentCard("\uE71B", T("Browser-Erweiterungen", "Browser extensions"), null, status));

            page.Children.Add(SectionTitle(T("Einrichten (einmalig)", "Setup (once)")));
            var steps = new StackPanel();
            void Step(int n, params System.Windows.Documents.Inline[] parts)
            {
                var line = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Fg, FontSize = 13, Margin = new Thickness(0, 0, 0, 8) };
                line.Inlines.Add(new System.Windows.Documents.Run($"{n}.  ") { FontWeight = FontWeights.SemiBold });
                foreach (var p in parts) line.Inlines.Add(p);
                steps.Children.Add(line);
            }
            System.Windows.Documents.Run Txt(string de, string en) => new(T(de, en));
            Step(1, Txt("Firefox: auf der ", "Firefox: on the "), Link(T("Release-Seite", "releases page"), "https://github.com/DENiki111/KnobFlyout/releases/latest"),
                Txt(" die .xpi anklicken und die Installation bestätigen.", " click the .xpi and confirm the installation."));
            Step(2, Txt("Chrome: den Ordner ", "Chrome: get the folder "), Link("Browser/chrome", "https://github.com/DENiki111/KnobFlyout/tree/main/Browser/chrome"),
                Txt(" aus dem GitHub-Repo herunterladen, chrome://extensions öffnen → „Entwicklermodus“ an → „Entpackte Erweiterung laden“ → diesen Ordner wählen. Danach offene Video-Tabs neu laden.",
                    " from the GitHub repo, open chrome://extensions → turn on “Developer mode” → “Load unpacked” → pick that folder. Then reload open video tabs."));
            Step(3, Txt("Opera und Opera GX: den Ordner ", "Opera and Opera GX: get the folder "), Link("Browser/Opera", "https://github.com/DENiki111/KnobFlyout/tree/main/Browser/Opera"),
                Txt(" herunterladen, opera://extensions öffnen → „Entwicklermodus“ an → „Erweiterung packen“ → Stammverzeichnis = dieser Ordner, Schlüsseldatei beim ersten Mal leer lassen (Opera legt daneben eine .pem an, die aufheben) → danach „Entpackte Erweiterung laden“ → denselben Ordner wählen. Nur so bleibt sie nach einem Opera-Neustart. Update: neuen Ordner genauso packen, als Schlüsseldatei die .pem von vorher.",
                    ", open opera://extensions → turn on “Developer mode” → “Pack extension” → root directory = that folder, leave the key file empty the first time (Opera creates a .pem next to it, keep it) → then “Load unpacked” → pick the same folder. Only this way it survives an Opera restart. Update: pack the new folder the same way, with the .pem from before as key file."));
            Step(4, Txt("Oben einschalten und „Speichern“ – die Erweiterung verbindet sich nach ein paar Sekunden.",
                "Switch it on above and click “Save” – the extension connects within a few seconds."));
            page.Children.Add(ContentCard("\uE946", T("So geht's", "How to"), null, steps));

            page.Children.Add(Hint(T("Der Browser muss im aktiven Profil sein.", "The browser has to be in the active profile.")));
        }

        /// <summary>Klickbarer Link im Text (öffnet den Standard-Browser).</summary>
        private static System.Windows.Documents.Hyperlink Link(string text, string url)
        {
            var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(text)) { NavigateUri = new Uri(url), Foreground = Accent };
            link.RequestNavigate += (_, e) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
                catch (Exception ex) { Logger.Error($"Link öffnen: {ex.Message}"); }
                e.Handled = true;
            };
            return link;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Info
        // ══════════════════════════════════════════════════════════════════
        /// <summary>Ordner im Explorer öffnen und die Datei markieren (gibt es sie noch nicht, nur den Ordner).</summary>
        private static void ShowInExplorer(string path)
        {
            try
            {
                string args = System.IO.File.Exists(path) ? $"/select,\"{path}\"" : $"\"{System.IO.Path.GetDirectoryName(path)}\"";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Error($"Ordner öffnen: {ex.Message}");
            }
        }

        private static void BuildInfoPage(StackPanel page)
        {
            page.Children.Add(PageTitle("Info"));
            page.Children.Add(SectionTitle("KnobFlyout"));
            page.Children.Add(SettingCard("\uE946", "KnobFlyout",
                T("Lautstärke pro App mit OSD und Media-Flyout", "Per-app volume with OSD and media flyout"), null));
            page.Children.Add(SectionTitle(T("Dateien", "Files")));
            string config = System.IO.Path.Combine(AppContext.BaseDirectory, "config.json");
            string log = System.IO.Path.Combine(AppContext.BaseDirectory, "knob_service.log");
            page.Children.Add(NavCard("\uE713", T("Konfiguration", "Configuration"), config, () => ShowInExplorer(config)));
            page.Children.Add(NavCard("\uE81C", T("Protokoll", "Log"), log, () => ShowInExplorer(log)));
        }

        // ── Hotkey-Feld (Aufnahme per Tastendruck, optional ✕ zum Entfernen) ──

        /// <summary>
        /// Klick ins Feld, dann Tastenkombination drücken (Esc bricht ab). Mit allowClear entfernt
        /// "✕" den Hotkey (leer = deaktiviert). onChanged bekommt z.B. "ctrl+alt+right" bzw. "".
        /// Ein Statustext erscheint nur während der Aufnahme oder bei einem Problem.
        /// </summary>
        /// <param name="emptyText">Anzeige, wenn kein Hotkey gesetzt ist (z.B. "Play/Pause-Taste" als Vorgabe).</param>
        private UIElement BuildHotkeyCapture(string current, Action<string> onChanged, bool allowClear = true, string? emptyText = null) =>
            HotkeyCapture(current, Shared(onChanged), allowClear, emptyText); // jeder Hotkey steht auch auf der Seite „Hotkeys“

        // ── Hilfen ────────────────────────────────────────────────────────────

        /// <summary>Mehrere Bedienelemente nebeneinander (für die rechte Kartenseite).</summary>
        private static StackPanel Inline(params FrameworkElement[] elements)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < elements.Length; i++)
            {
                elements[i].Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0);
                elements[i].VerticalAlignment = VerticalAlignment.Center;
                panel.Children.Add(elements[i]);
            }
            return panel;
        }

        private static AppConfig CloneConfig(AppConfig c) =>
            System.Text.Json.JsonSerializer.Deserialize<AppConfig>(System.Text.Json.JsonSerializer.Serialize(c)) ?? new AppConfig();

        /// <summary>
        /// "Testen": OSD-Karte und Media-Flyout kurz genau dort zeigen, wo sie mit den aktuellen (auch
        /// ungespeicherten) Einstellungen erscheinen würden; osdAt/mediaAt = z.B. ein Snap-Punkt.
        /// </summary>
        private readonly List<Window> _testWindows = new(); // zuletzt gezeigter Test (OSD-Karte, Flyout)

        private void RunPositionTest(Point? osdAt, Point? mediaAt, AppConfig? cfg = null)
        {
            try
            {
                cfg ??= _cfg;
                foreach (var old in _testWindows)
                    try { old.Close(); } catch { }
                _testWindows.Clear();
                var media = MediaOsd.CreateTest(cfg);
                var (osd, flyout) = Placement.Layout(cfg, media.MeasureHeight(), osdAt, mediaAt);
                _testWindows.Add(OsdCard.ShowTest(cfg, osd.TopLeft));
                media.ShowTestAt(flyout.TopLeft);
                _testWindows.Add(media);
            }
            catch (Exception ex)
            {
                Logger.Error($"Position testen: {ex.Message}");
            }
        }

        private static string FormatSnapPoint(SnapPosition p) =>
            p.MediaX.HasValue && p.MediaY.HasValue
                ? $"{p.X}, {p.Y}  (+ Media {p.MediaX}, {p.MediaY})"
                : $"{p.X}, {p.Y}";

        private List<string> LiveAudioApps()
        {
            try { return _liveAudio.GetSessions().Select(s => s.ProcessName).Distinct().OrderBy(n => n).ToList(); }
            catch { return new List<string>(); }
        }
    }
}
