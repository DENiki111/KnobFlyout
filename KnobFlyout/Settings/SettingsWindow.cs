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
    /// Navigation links (Allgemein, Lautstärke &amp; Apps, OSD, Media-Flyout, Position, Tastenkürzel,
    /// Spotify | Info), rechts je eine Seite aus kompakten Einstellungs-Karten (UiHelpers.SettingCard usw.).
    /// Sortierung: eine Seite pro sichtbarem Ding (OSD, Media-Flyout) mit Anzeige → Inhalt → Aussehen,
    /// alles zur Position auf „Position“, alle Tasten auf „Tastenkürzel“ (zusätzlich auf ihren Fachseiten).
    /// Alle Texte zweisprachig über Loc.T(de, en); Farbschema- und Sprachwechsel bauen das Fenster neu auf.
    /// Apps und Profile stehen auf „Lautstärke &amp; Apps“, weil cfg.profiles (nicht cfg.apps) die Zuordnung
    /// steuert (siehe SessionVolumeController.GetTargets).
    ///
    /// Änderungen liegen bis zum Klick auf "Speichern" nur im Arbeitsspeicher (_cfg). Ausnahmen, die
    /// sofort gespeichert werden: Farbschema, Sprache, "Position zurücksetzen".
    /// </summary>
    public sealed class SettingsWindow : Window
    {
        private readonly AppConfig _cfg;
        private readonly SessionVolumeController _liveAudio = new();
        private readonly SpotifyController _spotify;

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
            new("volume", "\uE767", T("Lautstärke & Apps", "Volume & apps")),
            new("profiles", "\uE716", T("Profile", "Profiles")),
            new("osd", "\uE7F4", "OSD"),
            new("media", "\uE8D6", "Media-Flyout"),
            new("position", "\uE81D", "Position"),
            new("design", "\uE790", "Design"),
            new("hotkeys", "\uE765", T("Tastenkürzel", "Shortcuts")),
            new("spotify", "\uE753", "Spotify"),
        };

        private static NavEntry InfoNav() => new("info", "\uE946", "Info");

        private readonly Dictionary<string, UIElement> _pages = new();
        private readonly Dictionary<string, Border> _navItems = new();
        private readonly List<TextBlock> _navLabels = new();
        private ColumnDefinition? _navColumn;
        private ScrollViewer? _pageScroll;
        private Border? _pinnedTop; // über der Seite, scrollt nicht mit (Unterseite „Eigenes Design“: Kopf + Live-Vorschau)
        private bool _navExpanded = true;

        /// <summary>Wird ausgelöst, wenn sich das Fenster (Farbschema, Sprache, Neu laden) durch ein neues ersetzt.</summary>
        public event Action<SettingsWindow>? Replaced;

        /// <summary>Gespeichert, Positionen zurückgesetzt, Sprache gewechselt oder neu geladen: App übernimmt die Config.</summary>
        public event Action? ConfigApplied;

        private TextBlock? _status;
        private TextBox? _anchorXBox;
        private TextBox? _anchorYBox;
        private ScreenTilePicker? _osdPicker;      // Position: Platz-Kacheln (getrennt)
        private ScreenTilePicker? _mediaPicker;
        private ScreenTilePicker? _combinedPicker; // Position: Platz-Kacheln (zusammen)
        private bool _layoutTouched; // Anordnung/Ecke hier geändert → Snap-Unabhängigkeit endet

        private void LayoutTouched()
        {
            _layoutTouched = true;
            _cfg.SnapIndependent = false;
        }

        private string _currentPage = "general";

        /// <param name="carry">Bei einem Farbschema-/Sprachwechsel: die bisherige (evtl. ungespeicherte) Konfiguration übernehmen.</param>
        public SettingsWindow(SpotifyController spotify, AppConfig? carry = null, string startPage = "general")
        {
            _spotify = spotify;
            _cfg = carry ?? ConfigManager.Load();

            Title = T("KnobFlyout – Einstellungen", "KnobFlyout – Settings");
            Icon = Tray.AppIcon.Render(48); // Titelleiste + Taskleiste
            Width = 900;
            Height = 680;
            MinWidth = 660;
            MinHeight = 440;
            Background = ContentBg;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SourceInitialized += (_, _) => ApplyDarkTitleBar();
            ApplyControlTheme();

            var root = new Grid();
            _navColumn = new ColumnDefinition { Width = new GridLength(NavWidthExpanded) };
            root.ColumnDefinitions.Add(_navColumn);
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Content = root;

            var nav = BuildNav();
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

            Closed += (_, _) => { CloseEyePopup(); _customPreviewTimer?.Stop(); _liveAudio.Dispose(); };
            // Live-Vorschau steht neben dem Fenster: beim Verschieben/Minimieren schließen statt zu verwaisen.
            LocationChanged += (_, _) => CloseEyePopup();
            StateChanged += (_, _) => CloseEyePopup();
        }

        /// <summary>
        /// Lädt Settings/Theme.xaml (eigene Stile für Scrollbar, Dropdown, Aufklapp-Pfeile, Tabs,
        /// Slider, Buttons, Listen, Kontextmenü) und stellt die Theme-Farben als Ressourcen bereit.
        /// Schlägt das Laden fehl, bleibt es still beim Windows-Standardlook.
        /// </summary>
        private void ApplyControlTheme()
        {
            Resources["ThemeFg"] = Fg;
            Resources["ThemeField"] = FieldBg;
            Resources["ThemeBorder"] = FieldBorder;
            Resources["ThemeHover"] = HoverBg;
            Resources["ThemePressed"] = PressedBg;
            Resources["ThemeAccent"] = Accent;
            Resources["ThemeThumb"] = ThumbBg;
            Resources["ThemeThumbHover"] = ThumbHoverBg;
            Resources["ThemeTrack"] = TrackBg;

            try
            {
                Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/KnobFlyout;component/Settings/Theme.xaml", UriKind.Relative),
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Theme.xaml: {ex.Message}");
            }
        }

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

        /// <summary>Fenster mit derselben Position, Größe und Seite neu aufbauen (carry null = frisch von der Platte).</summary>
        private SettingsWindow ReopenWith(AppConfig? carry)
        {
            var next = new SettingsWindow(_spotify, carry, _currentPage)
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
            Close();
            return next;
        }

        private void ApplyDarkTitleBar()
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int on = Light ? 0 : 1;
                DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
            }
            catch
            {
                // Rein kosmetisch - ohne dunkle Titelleiste funktioniert alles genauso.
            }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // ── Navigation ──────────────────────────────────────────────────────
        private DockPanel BuildNav()
        {
            var host = new DockPanel { Background = NavBg };

            // Oben: Hamburger zum Ein-/Ausklappen der Beschriftungen
            var burger = new Border { Height = 44, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
            burger.Child = new TextBlock
            {
                Text = "\uE700", FontFamily = IconFont, FontSize = 16, Foreground = Fg,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0),
            };
            burger.MouseLeftButtonUp += (_, _) => ToggleNav();
            DockPanel.SetDock(burger, Dock.Top);
            host.Children.Add(burger);

            // Unten: Info
            var info = MakeNavItem(InfoNav());
            info.Margin = new Thickness(4, 0, 4, 8);
            DockPanel.SetDock(info, Dock.Bottom);
            host.Children.Add(info);

            var list = new StackPanel();
            foreach (var e in MainNav()) list.Children.Add(MakeNavItem(e));
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
            item.MouseEnter += (_, _) => { if (!IsSelected(item)) item.Background = NavHoverBg; };
            item.MouseLeave += (_, _) => { if (!IsSelected(item)) item.Background = Brushes.Transparent; };
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
        }

        private void SelectPage(string key)
        {
            CloseEyePopup();
            _currentPage = key;
            if (key == "media_custom") _pages.Remove(key); // Unterseite immer frisch (Werte können sich auf der Fachseite geändert haben)
            else HideMediaCustomHeader();
            string navKey = key == "media_custom" ? _subParent : key; // Unterseite: Eintrag der Fachseite bleibt markiert
            foreach (var kv in _navItems)
            {
                bool sel = kv.Key == navKey;
                kv.Value.Background = sel ? NavSelectedBg : Brushes.Transparent;
                var bar = (kv.Value.Child as Grid)?.Children.OfType<Border>().FirstOrDefault(b => (b.Tag as string) == "bar");
                if (bar != null) bar.Visibility = sel ? Visibility.Visible : Visibility.Hidden;
            }

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
            if (_pageScroll != null) _pageScroll.Content = page;
        }

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
                case "position": BuildPositionPage(page); break;
                case "design": BuildDesignPage(page); break;
                case "hotkeys": BuildHotkeysPage(page); break;
                case "spotify": BuildSpotifyPage(page); break;
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

            var save = AccentBtn(T("Speichern", "Save"), 110);
            save.Click += (_, _) =>
            {
                // Felder, die sich außerhalb dieses Fensters ändern können (Drag am OSD, Profil-
                // Hotkey, neu erkannte Apps), werden vor dem Speichern vom Datenträger übernommen,
                // damit "Speichern" im Settings-Fenster sie nicht mit einem alten Stand überschreibt.
                var onDisk = ConfigManager.Load();
                _cfg.OsdX = onDisk.OsdX;
                _cfg.OsdY = onDisk.OsdY;
                _cfg.MediaOsdX = onDisk.MediaOsdX;
                _cfg.MediaOsdY = onDisk.MediaOsdY;
                _cfg.ActiveProfile = onDisk.ActiveProfile;
                var profileKeys = ProfileDefinitions.Keys(_cfg); // aktives Profil gelöscht → erstes Profil
                if (profileKeys.Length > 0 && !profileKeys.Contains(_cfg.ActiveProfile)) _cfg.ActiveProfile = profileKeys[0];
                _cfg.SeenApps = onDisk.SeenApps;
                _cfg.ScreenLayout = onDisk.ScreenLayout; // vom Bildschirmwechsel-Abgleich der App gepflegt
                // Vom Snap-Zyklus gesetzt; wurden hier Anordnung/Ecke geändert, endet es.
                _cfg.SnapIndependent = !_layoutTouched && onDisk.SnapIndependent;

                ConfigManager.Save(_cfg);
                ConfigApplied?.Invoke();
                status.Text = T("Gespeichert ✓", "Saved ✓");
            };

            var close = Btn(T("Schließen", "Close"), 100);
            close.Margin = new Thickness(0);
            close.Click += (_, _) => Close();

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

            page.Children.Add(SectionTitle(T("Start", "Startup")));
            page.Children.Add(SettingCard("\uE7E8", T("Mit Windows starten", "Start with Windows"),
                T("Gilt sofort · auch im Task-Manager unter „Autostart-Apps“ umschaltbar", "Applies immediately · can also be switched in Task Manager under “Startup apps”"),
                Toggle(Autostart.IsEnabled(), v =>
                {
                    try { Autostart.SetEnabled(v); }
                    catch (Exception ex) { Logger.Error($"Autostart: {ex.Message}"); }
                })));

            page.Children.Add(SectionTitle(T("Problembehebung", "Troubleshooting")));
            page.Children.Add(BuildReloadCard());
            page.Children.Add(Hint(T("OSD oder Media-Flyout nicht sichtbar? Seite „Position“ → „Position zurücksetzen“.",
                "OSD or media flyout not visible? “Position” page → “Reset position”.")));
        }

        // ══════════════════════════════════════════════════════════════════
        //  Design – alle Aussehen-Einstellungen an einem Ort (zusätzlich zu ihren Fachseiten, wie „Tastenkürzel“)
        // ══════════════════════════════════════════════════════════════════
        private void BuildDesignPage(StackPanel page)
        {
            page.Children.Add(PageTitle("Design"));
            page.Children.Add(Hint(T("Alle Einstellungen zum Aussehen an einem Ort. Sie stehen zusätzlich auf ihren Seiten „Allgemein“, „OSD“ und „Media-Flyout“ – " +
                "eine Änderung gilt überall.",
                "All appearance settings in one place. They also stay on their pages “General”, “OSD” and “Media flyout” – a change applies everywhere.")));

            page.Children.Add(SectionTitle(T("Farbschema", "Color scheme")));
            page.Children.Add(ThemeCard());

            AddOsdAppearance(page, T("OSD – Aussehen", "OSD – appearance"), T("OSD – Farben", "OSD – colors"), designPage: true);
            AddMediaAppearance(page, T("Media-Flyout – Aussehen", "Media flyout – appearance"), designPage: true);
            page.Children.Add(Hint(T("Profilfarben und Logos: Seite „Profile“ → „Profile bearbeiten“.",
                "Profile colors and logos: “Profiles” page → “Edit profiles”.")));
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
            return SettingCard("\uE790", T("Hell oder dunkel", "Light or dark"),
                T("Für Einstellungen, OSD und Media-Flyout", "For settings, OSD and media flyout"),
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
            _osdPicker?.Set("custom");
            _mediaPicker?.Set("custom");
            _combinedPicker?.Set("custom");

            ConfigApplied?.Invoke();
            if (_status != null) _status.Text = T("Position zurückgesetzt ✓", "Position reset ✓");
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
        //  Lautstärke & Apps (Drehknopf, App-Zuweisung, Profile)
        // ══════════════════════════════════════════════════════════════════
        private void BuildVolumePage(StackPanel page)
        {
            page.Children.Add(PageTitle(T("Lautstärke & Apps", "Volume & apps")));

            page.Children.Add(SectionTitle(T("Drehknopf", "Knob")));
            page.Children.Add(SettingCard("\uE767", T("Schrittweite", "Step size"), T("Pro Raste", "Per notch"),
                SliderControl(1, 15, _cfg.Step, "0' %'", v => _cfg.Step = (int)v, tick: 1)));
            page.Children.Add(SettingCard("\uE8D6", T("Schrittweite Spotify", "Spotify step size"),
                T("Nur bei Steuerung über die Spotify-API", "Only when controlled via the Spotify API"),
                SliderControl(1, 15, _cfg.StepSpotify, "0' %'", v => _cfg.StepSpotify = (int)v, tick: 1)));

            page.Children.Add(SectionTitle("Apps"));
            BuildAppsSection(page);
            page.Children.Add(Hint(T("Profile bearbeiten, wechseln und per Taste zuweisen: Seite „Profile“.",
                "Edit, switch and assign profiles by key: “Profiles” page.")));
        }

        // ══════════════════════════════════════════════════════════════════
        //  Profile (eigene Seite, vorher Teil von „Lautstärke & Apps“)
        // ══════════════════════════════════════════════════════════════════
        private void BuildProfilesPage(StackPanel page)
        {
            page.Children.Add(PageTitle(T("Profile", "Profiles")));
            page.Children.Add(Hint(T("Welche App zu welchem Profil gehört, stellst du in der App-Liste auf „Lautstärke & Apps“ ein – oder im Neue-App-Popup.",
                "Which app belongs to which profile is set in the app list on “Volume & apps” – or in the new-app popup.")));

            page.Children.Add(SectionTitle(T("Wechseln", "Switching")));
            page.Children.Add(ProfileHotkeyCard());

            var modeCombo = Combo(220, _cfg.ProfileMode == "assigned" ? 1 : 0,
                T("Nur Apps mit Ton", "Only apps playing sound"),
                T("Alle Apps im Profil", "All apps in the profile"));
            modeCombo.SelectionChanged += (_, _) => _cfg.ProfileMode = modeCombo.SelectedIndex == 1 ? "assigned" : "active";
            page.Children.Add(SettingCard("\uE8AB", T("Gesteuerte Apps", "Controlled apps"), null, modeCombo));

            page.Children.Add(SettingCard("\uE893", T("Leere Profile überspringen", "Skip empty profiles"),
                T("Nur Profile mit laufenden Apps", "Only profiles with running apps"),
                Toggle(_cfg.SkipEmptyProfiles, v => _cfg.SkipEmptyProfiles = v)));

            page.Children.Add(SectionTitle(T("Profile", "Profiles")));
            _profileEditorHost = new StackPanel();
            RenderProfileEditor();
            page.Children.Add(ExpanderCard("\uE790", T("Profile bearbeiten", "Edit profiles"),
                T($"Name, Logo und Farbe · bis zu {ProfileDefinitions.MaxProfiles} Profile", $"Name, logo and color · up to {ProfileDefinitions.MaxProfiles} profiles"),
                _profileEditorHost));

            _profileNestedHost = new StackPanel();
            RenderProfileOverview();
            page.Children.Add(ExpanderCard("\uE8A9", T("Profile im Überblick", "Profiles overview"), null, _profileNestedHost));

            page.Children.Add(SectionTitle(T("Neue Apps zuweisen", "Assign new apps")));
            page.Children.Add(ProfileHotkeysCard(out var profileHotkeyRows));
            _profileHotkeyHost = profileHotkeyRows; // nach Änderungen an den Profilen neu aufbauen
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
            host.Children.Add(Hint(T("Logo anklicken: Windows-Symbol wählen oder eigenes Bild laden. Gelöschte Profile geben ihre Apps frei. Übernommen wird mit „Speichern“.",
                "Click a logo: pick a Windows symbol or load your own image. Deleted profiles release their apps. Applied with “Save”.")));
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
            if (_profileHotkeyHost != null) BuildProfileHotkeyRows(_profileHotkeyHost);
            if (_currentPage != "hotkeys") _pages.Remove("hotkeys"); // zeigt die Profile ebenfalls
        }

        private StackPanel? _profileHotkeyHost; // „Zuweisen per Taste“ auf der Seite Lautstärke & Apps

        /// <summary>
        /// „Zuweisen per Taste“: eine Taste je Profil, die im Neue-App-Popup die angezeigte App diesem Profil
        /// zuweist (Seiten Lautstärke &amp; Apps und Tastenkürzel). Außerhalb des Popups wirkt die Taste nicht.
        /// </summary>
        private Border ProfileHotkeysCard(out StackPanel rows)
        {
            rows = new StackPanel();
            BuildProfileHotkeyRows(rows);
            return ExpanderCard("\uE765", T("Zuweisen per Taste", "Assign by key"),
                T("Im Neue-App-Popup: Taste drücken = App diesem Profil zuweisen", "In the new-app popup: press the key = assign the app to this profile"), rows);
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
            logoBtn.MouseEnter += (_, _) => logoBtn.Background = HoverBg;
            logoBtn.MouseLeave += (_, _) => logoBtn.Background = FieldBg;
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
            return row;
        }

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
                    cell.MouseEnter += (_, _) => cell.Background = HoverBg;
                    cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;
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
            page.Children.Add(ContentCard("\uE71D", T("Apps zuweisen", "Assign apps"), T("Profil per Klick zuweisen · ⊘ = ignorieren", "Click to assign a profile · ⊘ = ignore"), body));

            page.Children.Add(ExpanderCard("\uE733", T("Ignorierte Apps", "Ignored apps"), T("Werden nie gesteuert", "Never controlled"),
                ListEditor(_cfg.Exclude, T("z. B. systemsounds", "e.g. systemsounds"), LiveAudioApps)));
        }

        private void RerenderApps()
        {
            RenderActiveList();
            RenderAppList(_searchBox?.Text ?? "");
            RenderProfileOverview();
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
            page.Children.Add(SettingCard("\uE7F4", T("OSD anzeigen", "Show OSD"), T("Lautstärke, Profil und Feststelltasten", "Volume, profile and lock keys"),
                Toggle(_cfg.Osd, v => _cfg.Osd = v)));
            page.Children.Add(SettingCard("\uE916", T("Anzeigedauer", "Duration"), null,
                SliderControl(500, 10000, _cfg.OsdDuration, "0' ms'", v => _cfg.OsdDuration = (int)v, tick: 100)));
            page.Children.Add(SettingCard("\uE8BB", T("Per Klick ausblenden", "Click to dismiss"),
                T("Klick auf OSD oder Media-Flyout (nicht auf Knöpfe) blendet es sofort aus", "Clicking the OSD or media flyout (not a button) hides it right away"),
                Toggle(_cfg.ClickToDismiss, v => _cfg.ClickToDismiss = v)));
            page.Children.Add(Hint(T("Wo das OSD erscheint: Seite „Position“.", "Where the OSD appears: “Position” page.")));

            AddOsdAppearance(page, T("Aussehen", "Appearance"), T("Farben", "Colors"), designPage: false);

            page.Children.Add(SectionTitle(T("Tasten", "Keys")));
            page.Children.Add(ProfileHotkeyCard());
        }

        /// <summary>
        /// Aussehen des OSD (Design mit Augen-Knopf, eigene Farben, Ecken, Deckkraft, Snap-Farben). Steht auf der Seite „OSD“
        /// und zusätzlich auf „Design“ (wie die Tasten auf „Tastenkürzel“); SelectPage baut die andere Seite bei Änderungen neu.
        /// </summary>
        private void AddOsdAppearance(StackPanel page, string appearanceTitle, string colorsTitle, bool designPage)
        {
            // ── Aussehen (Augen-Knopf = Live-Vorschau) ──
            page.Children.Add(SectionTitle(appearanceTitle));
            // Ältere Designs, dahinter alle aus dem Design-System (Designs.All, z. B. „Dynamic Island“).
            string[] osdDesigns = new[] { "dark", "modern", "win11" }.Concat(Designs.All.Select(d => d.Key)).ToArray();
            string[] osdDesignNames = new[] { T("Eigenes", "Custom"), "ModernFlyouts", "Windows 11" }.Concat(Designs.All.Select(d => d.Name)).ToArray();
            int OsdDesignIndex(string design) => Math.Max(0, Array.IndexOf(osdDesigns, design == "windows" ? "modern" : design));
            var designCombo = Combo(200, OsdDesignIndex(_cfg.OsdDesign), osdDesignNames);
            var osdCustomHost = new StackPanel(); // Einstellungen des eigenen Designs, nur wenn „Eigenes“ gewählt ist
            void ShowOsdCustom() => osdCustomHost.Visibility = _cfg.OsdDesign == "dark" ? Visibility.Visible : Visibility.Collapsed;
            designCombo.SelectionChanged += (_, _) =>
            {
                _cfg.OsdDesign = osdDesigns[Math.Max(0, designCombo.SelectedIndex)];
                ShowOsdCustom();
            };
            int defaultDesign = OsdDesignIndex(Defaults.OsdDesign);
            page.Children.Add(SettingCard("\uE7F4", "Design", T("Auch fürs Neue-App-Popup · „Eigenes“ mit frei wählbaren Farben", "Also for the new-app popup · “Custom” with your own colors"),
                WithEye(WithReset(designCombo, () => designCombo.SelectedIndex = defaultDesign), () => new FrameworkElement[]
                {
                    OsdCard.CreatePreview(_cfg, "volume"),
                    OsdCard.CreatePreview(_cfg, "profile"),
                    OsdCard.CreatePreview(_cfg, "snap"),
                    OsdCard.CreatePreview(_cfg, "lockkey"),
                    NewAppOsd.CreatePreview(_cfg),
                }, T("OSD-Design", "OSD design"))));
            page.Children.Add(osdCustomHost); // Inhalt (Farben des eigenen Designs) siehe unten
            ShowOsdCustom();
            page.Children.Add(SettingCard("\uE7F4", T("Ecken", "Corners"), T("Auch das Neue-App-Popup", "Also the new-app popup"),
                CornerControl(_cfg.OsdCornerRadius, Defaults.OsdCornerRadius, r => _cfg.OsdCornerRadius = r)));
            // Designs aus dem Design-System bringen ihre Form selbst mit: „Ecken“ nur bei den älteren Designs zeigen.
            var osdCorners = page.Children[^1];
            void ShowOsdCorners() => osdCorners.Visibility = Designs.Get(_cfg.OsdDesign) == null ? Visibility.Visible : Visibility.Collapsed;
            ShowOsdCorners();
            designCombo.SelectionChanged += (_, _) => ShowOsdCorners();
            page.Children.Add(SettingCard("\uE706", T("Deckkraft Hintergrund", "Background opacity"),
                T("Fl\u00E4che der Karten und des Neue-App-Popups \u00B7 0 % = nur Text und Symbole", "Surface of the cards and the new-app popup \u00B7 0 % = text and icons only"),
                ResetSlider(SliderControl(0, 100, Math.Round(_cfg.WinAlpha * 100), "0' %'", v => _cfg.WinAlpha = Math.Clamp(v / 100.0, 0.0, 1.0), tick: 1),
                    Math.Round(Defaults.WinAlpha * 100))));
            page.Children.Add(SettingCard("\uE8D2", T("Deckkraft Text und Symbole", "Text and icon opacity"),
                T("Schrift, Symbole, Leisten und Kn\u00F6pfe", "Text, icons, bars and buttons"),
                ResetSlider(SliderControl(20, 100, Math.Clamp(_cfg.OsdContentOpacity, 20, 100), "0' %'", v => _cfg.OsdContentOpacity = (int)v, tick: 1),
                    Defaults.OsdContentOpacity)));

            // ── Farben ──
            page.Children.Add(SectionTitle(colorsTitle));
            var colorsHeading = page.Children[^1]; // auf „Design“ ohne Inhalt (keine Hinweiszeile) → mit ausblenden
            var snapBoxes = new Dictionary<string, TextBox>();
            var snapColors = new StackPanel();
            AddColorField(snapColors, snapBoxes, "color_snap_border", T("Balken", "Bar"), _cfg.ColorSnapBorder, Defaults.ColorSnapBorder, v => _cfg.ColorSnapBorder = v);
            AddColorField(snapColors, snapBoxes, "color_snap_text", T("Text und Symbol", "Text and icon"), _cfg.ColorSnapText, Defaults.ColorSnapText, v => _cfg.ColorSnapText = v);
            snapColors.Children.Insert(0, BuildColorPresets(snapBoxes));
            page.Children.Add(ExpanderCard("\uE81D", T("Farben der Positions-Karte (Snap)", "Position card colors (snap)"),
                null, snapColors));
            // Wirkt nur beim Design „Eigenes“ (die übrigen färben die Positions-Karte selbst) → nur dort zeigen.
            var snapCard = page.Children[^1];
            void ShowSnapColors()
            {
                snapCard.Visibility = _cfg.OsdDesign == "dark" ? Visibility.Visible : Visibility.Collapsed;
                if (designPage) colorsHeading.Visibility = snapCard.Visibility;
            }
            ShowSnapColors();
            designCombo.SelectionChanged += (_, _) => ShowSnapColors();

            var customBoxes = new Dictionary<string, TextBox>();
            var customColors = new StackPanel();
            void Custom(string key, string label, string value, string def, Action<string> set) =>
                AddColorField(customColors, customBoxes, key, label, value, def, set);
            Custom("color_bg", T("Hintergrund", "Background"), _cfg.ColorBg, Defaults.ColorBg, v => _cfg.ColorBg = v);
            Custom("color_vol_border", T("Rahmen", "Border"), _cfg.ColorVolBorder, Defaults.ColorVolBorder, v => _cfg.ColorVolBorder = v);
            Custom("color_vol_text", T("App-Name", "App name"), _cfg.ColorVolText, Defaults.ColorVolText, v => _cfg.ColorVolText = v);
            Custom("color_vol_pct", T("Prozent", "Percent"), _cfg.ColorVolPct, Defaults.ColorVolPct, v => _cfg.ColorVolPct = v);
            Custom("color_vol_icon", "Icon", _cfg.ColorVolIcon, Defaults.ColorVolIcon, v => _cfg.ColorVolIcon = v);
            Custom("color_bar_bg", T("Balken-Hintergrund", "Bar background"), _cfg.ColorBarBg, Defaults.ColorBarBg, v => _cfg.ColorBarBg = v);
            Custom("color_bar_hi", T("Balken hoch", "Bar high"), _cfg.ColorBarHi, Defaults.ColorBarHi, v => _cfg.ColorBarHi = v);
            Custom("color_bar_mid", T("Balken mittel", "Bar medium"), _cfg.ColorBarMid, Defaults.ColorBarMid, v => _cfg.ColorBarMid = v);
            Custom("color_bar_lo", T("Balken niedrig", "Bar low"), _cfg.ColorBarLo, Defaults.ColorBarLo, v => _cfg.ColorBarLo = v);
            Custom("color_prof_border", T("Profil-Rahmen", "Profile border"), _cfg.ColorProfBorder, Defaults.ColorProfBorder, v => _cfg.ColorProfBorder = v);
            Custom("color_prof_text", T("Profil-Text", "Profile text"), _cfg.ColorProfText, Defaults.ColorProfText, v => _cfg.ColorProfText = v);
            customColors.Children.Insert(0, BuildColorPresets(customBoxes));
            // Erscheint direkt unter der Design-Auswahl, nur bei „Eigenes“.
            osdCustomHost.Children.Add(ExpanderCard("\uE771", T("Farben des eigenen Designs", "Custom design colors"), null, customColors));
            if (!designPage)
                page.Children.Add(Hint(T("Profilfarben: Seite „Profile“ · Hell oder dunkel: Seite „Allgemein“.",
                    "Profile colors: “Profiles” page · Light or dark: “General” page.")));
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

            // Nur die Kopfzeile ist eine Karte; die Vorschau darunter hat keinen Hintergrund (Jasper-Wunsch):
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
        private static UIElement CornerControl(int current, int def, Action<int> set)
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
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };

            var combo = new ComboBox { Width = 170 };
            void Refill(string? select = null)
            {
                combo.Items.Clear();
                foreach (var name in _cfg.ColorProfiles.Where(kv => kv.Value.Keys.Any(boxes.ContainsKey))
                                                       .Select(kv => kv.Key)
                                                       .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                    combo.Items.Add(name);
                if (select != null && combo.Items.Contains(select)) combo.SelectedItem = select;
                else if (combo.Items.Count > 0) combo.SelectedIndex = 0;
            }
            Refill();

            var apply = Btn(T("Anwenden", "Apply"));
            apply.Margin = new Thickness(6, 0, 6, 0);
            apply.Click += (_, _) =>
            {
                if (combo.SelectedItem is not string name || !_cfg.ColorProfiles.TryGetValue(name, out var preset)) return;
                foreach (var kv in preset)
                    if (boxes.TryGetValue(kv.Key, out var box)) box.Text = kv.Value;
                if (_status != null) _status.Text = T($"Farbprofil „{name}“ angewendet – mit „Speichern“ übernehmen", $"Color profile “{name}” applied – click “Save” to keep it");
            };

            var delete = Btn("\uE74D"); // Delete
            delete.FontFamily = IconFont;
            delete.ToolTip = T("Farbprofil löschen", "Delete color profile");
            delete.Margin = new Thickness(0);
            delete.Click += (_, _) =>
            {
                if (combo.SelectedItem is not string name || !_cfg.ColorProfiles.TryGetValue(name, out var preset)) return;
                foreach (var key in boxes.Keys) preset.Remove(key);
                if (preset.Count == 0) _cfg.ColorProfiles.Remove(name);
                PersistColorProfiles();
                Refill();
            };

            var pickRow = new StackPanel { Orientation = Orientation.Horizontal };
            pickRow.Children.Add(combo);
            pickRow.Children.Add(apply);
            pickRow.Children.Add(delete);
            panel.Children.Add(OptionRow(T("Farbprofil", "Color profile"), pickRow));

            var nameBox = TextField("", 170);
            nameBox.Margin = new Thickness(0);
            nameBox.ToolTip = T("Name des Farbprofils", "Color profile name");
            var save = AccentBtn(T("Speichern", "Save"));
            save.Margin = new Thickness(6, 0, 0, 0);
            save.Click += (_, _) =>
            {
                var name = nameBox.Text.Trim();
                if (name.Length == 0) { nameBox.Focus(); return; }
                if (!_cfg.ColorProfiles.TryGetValue(name, out var preset))
                    _cfg.ColorProfiles[name] = preset = new Dictionary<string, string>();
                foreach (var kv in boxes) preset[kv.Key] = kv.Value.Text.Trim();
                PersistColorProfiles();
                nameBox.Text = "";
                Refill(name);
                if (_status != null) _status.Text = T($"Farbprofil „{name}“ gespeichert ✓", $"Color profile “{name}” saved ✓");
            };

            var saveRow = new StackPanel { Orientation = Orientation.Horizontal };
            saveRow.Children.Add(nameBox);
            saveRow.Children.Add(save);
            panel.Children.Add(OptionRow(T("Aktuelle Farben speichern als", "Save current colors as"), saveRow));

            panel.Children.Add(new Border { Height = 1, Background = CardBorder, Margin = new Thickness(0, 6, 0, 6) });
            return panel;
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
            page.Children.Add(Hint(T("Wo es erscheint und Anordnung mit dem OSD: Seite „Position“ · Per Klick ausblenden: Seite „OSD“.",
                "Where it appears and arrangement with the OSD: “Position” page · Click to dismiss: “OSD” page.")));

            page.Children.Add(SectionTitle(T("Inhalt", "Content")));
            page.Children.Add(SettingCard("\uE768", T("Fortschrittsleiste", "Progress bar"), null,
                Toggle(_cfg.MediaShowProgress, v => _cfg.MediaShowProgress = v)));
            page.Children.Add(SettingCard("\uE893", T("±10-s-Knöpfe", "±10 s buttons"), null,
                Toggle(_cfg.MediaSkipButtons, v => _cfg.MediaSkipButtons = v)));
            page.Children.Add(ExpanderCard("\uE712", T("„…“-Menü", "“…” menu"), T("Einträge auswählen", "Choose the items"), BuildMoreMenuOptions(),
                headerControl: Toggle(_cfg.MediaShowMore, v => _cfg.MediaShowMore = v)));

            AddMediaAppearance(page, T("Aussehen", "Appearance"), designPage: false);

            page.Children.Add(SectionTitle(T("Tasten", "Keys")));
            AddSkipHotkeyCards(page);
            AddGestureCards(page);
            page.Children.Add(Hint(T("Zeiten für „Lange halten“ und „Zweimal schnell drücken“: Seite „Tastenkürzel“.",
                "Times for “Hold” and “Press twice quickly”: “Shortcuts” page.")));
        }

        /// <summary>
        /// Aussehen des Media-Flyouts (Design mit Augen-Knopf, Einstellungen des eigenen Designs, Ecken, Deckkraft, Cover als
        /// Hintergrund). Steht auf der Seite „Media-Flyout“ und zusätzlich auf „Design“.
        /// </summary>
        private void AddMediaAppearance(StackPanel page, string appearanceTitle, bool designPage)
        {
            // ── Aussehen (Augen-Knopf = Live-Vorschau) ──
            page.Children.Add(SectionTitle(appearanceTitle));
            // Ältere Designs, dahinter alle aus dem Design-System (Designs.All, z. B. „Dynamic Island“).
            string[] mediaDesigns = new[] { "modernflyouts", "win11", "custom" }.Concat(Designs.All.Select(d => d.Key)).ToArray();
            string[] mediaDesignNames = new[] { "ModernFlyouts", "Windows 11", T("Eigenes", "Custom") }.Concat(Designs.All.Select(d => d.Name)).ToArray();
            var mediaDesign = Combo(200, Math.Max(0, Array.IndexOf(mediaDesigns, _cfg.MediaDesign)), mediaDesignNames);
            var mediaCustomHost = new StackPanel(); // Einstellungen des eigenen Designs, nur wenn „Eigenes“ gewählt ist
            void ShowMediaCustom() => mediaCustomHost.Visibility = _cfg.MediaDesign == "custom" ? Visibility.Visible : Visibility.Collapsed;
            mediaDesign.SelectionChanged += (_, _) =>
            {
                _cfg.MediaDesign = mediaDesigns[Math.Max(0, mediaDesign.SelectedIndex)];
                ShowMediaCustom();
            };
            page.Children.Add(SettingCard("\uE8D6", "Design", T("„Eigenes“: Aufbau, Größen und Farben frei wählbar", "“Custom”: layout, sizes and colors of your choice"),
                WithEye(WithReset(mediaDesign, () => mediaDesign.SelectedIndex = Math.Max(0, Array.IndexOf(mediaDesigns, Defaults.MediaDesign))),
                    () => new[] { MediaOsd.CreatePreview(_cfg) }, T("Media-Flyout", "Media flyout"))));
            mediaCustomHost.Children.Add(NavCard("\uE70F", T("Eigenes Design gestalten", "Design your own"), // Edit
                T("Elemente ein- und ausschalten, Reihenfolge, Cover, Knöpfe und Farben – mit Live-Vorschau",
                    "Turn elements on and off, order, album art, buttons and colors – with live preview"),
                () => OpenMediaCustomEditor(designPage ? "design" : "media")));
            ShowMediaCustom();
            page.Children.Add(mediaCustomHost);
            page.Children.Add(SettingCard("\uE8D6", T("Ecken", "Corners"), null,
                CornerControl(_cfg.MediaCornerRadius, Defaults.MediaCornerRadius, r => _cfg.MediaCornerRadius = r)));
            var mediaCorners = page.Children[^1]; // wie beim OSD: nur bei den älteren Designs
            void ShowMediaCorners() => mediaCorners.Visibility = Designs.Get(_cfg.MediaDesign) == null ? Visibility.Visible : Visibility.Collapsed;
            ShowMediaCorners();
            mediaDesign.SelectionChanged += (_, _) => ShowMediaCorners();
            page.Children.Add(SettingCard("\uE706", T("Deckkraft Hintergrund", "Background opacity"),
                T("Fl\u00E4che, Rand und Cover-Hintergrund \u00B7 0 % = nur Text und Symbole", "Surface, border and album art background \u00B7 0 % = text and icons only"),
                ResetSlider(SliderControl(0, 100, Math.Clamp(_cfg.MediaOpacity, 0, 100), "0' %'", v => _cfg.MediaOpacity = (int)v, tick: 1),
                    Defaults.MediaOpacity)));
            page.Children.Add(SettingCard("\uE8D2", T("Deckkraft Text und Symbole", "Text and icon opacity"),
                T("Schrift, Symbole, Cover, Leiste und Kn\u00F6pfe", "Text, icons, album art, bar and buttons"),
                ResetSlider(SliderControl(20, 100, Math.Clamp(_cfg.MediaContentOpacity, 20, 100), "0' %'", v => _cfg.MediaContentOpacity = (int)v, tick: 1),
                    Defaults.MediaContentOpacity)));

            var art = new StackPanel();
            art.Children.Add(OptionRow(T("Unschärfe", "Blur"),
                ResetSlider(SliderControl(0, 60, Math.Clamp(_cfg.MediaArtBlur, 0, 60), "0", v => _cfg.MediaArtBlur = (int)v, tick: 1),
                    Defaults.MediaArtBlur)));
            art.Children.Add(OptionRow(T("Abdunkelung", "Dimming"),
                ResetSlider(SliderControl(0, 100, Math.Clamp(_cfg.MediaArtDim, 0, 100), "0' %'", v => _cfg.MediaArtDim = (int)v, tick: 1),
                    Defaults.MediaArtDim)));
            page.Children.Add(ExpanderCard("\uE91B", T("Cover als Hintergrund", "Album art background"), null, art,
                headerControl: ResetToggle(Toggle(_cfg.MediaArtBackground, v => _cfg.MediaArtBackground = v), Defaults.MediaArtBackground)));
            if (!designPage)
                page.Children.Add(Hint(T("Hell oder dunkel: Seite „Allgemein“.", "Light or dark: “General” page.")));
        }

        // ══ Unterseite „Eigenes Design“ des Media-Flyouts (Key media_custom) ═══════════
        // Aufbau nach gängigen Vorbildern (Windows-Schnelleinstellungen, iOS-Kontrollzentrum, Shopify-Abschnitte,
        // Overlay-Baukästen): Vorlagen als Startpunkt, alle Elemente in der Reihenfolge des Flyouts mit Ein/Aus und
        // ↑/↓, Feineinstellungen erst aufgeklappt, und eine Live-Vorschau, die beim Scrollen oben stehen bleibt.

        private string _subParent = "media";                      // Seite, von der der Editor geöffnet wurde (Zurück, Navigation)
        private readonly HashSet<string> _customExpanded = new(); // aufgeklappte Bereiche (bleiben beim Umsortieren offen)
        private System.Windows.Threading.DispatcherTimer? _customPreviewTimer;

        /// <summary>Karte, die wie in den Windows-11-Einstellungen auf eine Unterseite führt (ganze Karte klickbar, › rechts).</summary>
        private static Border NavCard(string glyph, string title, string description, Action open)
        {
            var card = SettingCard(glyph, title, description, new TextBlock
            {
                Text = "\uE76C", FontFamily = IconFont, FontSize = 12, Foreground = Fg, // ChevronRight
            });
            card.Cursor = System.Windows.Input.Cursors.Hand;
            card.MouseEnter += (_, _) => card.Background = HoverBg;
            card.MouseLeave += (_, _) => card.Background = CardBg;
            card.MouseLeftButtonUp += (_, _) => open();
            return card;
        }

        private void OpenMediaCustomEditor(string parent)
        {
            _subParent = parent;
            _customExpanded.Clear();
            SelectPage("media_custom");
            _pageScroll?.ScrollToTop();
        }

        /// <summary>Vorlage gewählt: Seite neu aufbauen (Schalter zeigen die neuen Werte), Scroll-Position bleibt.</summary>
        private void RebuildMediaCustomPage()
        {
            double offset = _pageScroll?.VerticalOffset ?? 0;
            SelectPage("media_custom");
            if (_pageScroll == null) return;
            _pageScroll.UpdateLayout();
            _pageScroll.ScrollToVerticalOffset(offset);
        }

        /// <summary>Kopf (Zurück + Pfad) und Live-Vorschau über der scrollenden Seite; die Vorschau baut sich bei jeder Änderung neu.</summary>
        private void ShowMediaCustomHeader()
        {
            if (_pinnedTop == null) return;
            var back = FlatIconButton("\uE72B", T("Zurück", "Back"), () => SelectPage(_subParent)); // Back
            back.Width = back.Height = 32;
            back.FontSize = 14;
            back.Foreground = Fg;
            var parentName = new TextBlock
            {
                Text = _subParent == "design" ? "Design" : "Media-Flyout", FontSize = 20, FontWeight = FontWeights.SemiBold,
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
                Text = T("Eigenes Design", "Custom design"), FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Fg,
                VerticalAlignment = VerticalAlignment.Center,
            });

            // Echte Größe, bei hohen Aufbauten (Cover oben) bzw. niedrigem Fenster verkleinert, damit unten genug Platz für die Einstellungen bleibt.
            var previewHost = new Viewbox { StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Center };
            void Fit() => previewHost.MaxHeight = Math.Clamp(ActualHeight - 450, 110, 230);
            string snapshot = "";
            void Refresh()
            {
                Fit();
                string now = ConfigSnapshot();
                if (now == snapshot) return;
                snapshot = now;
                try
                {
                    var preview = MediaOsd.CreatePreview(_cfg);
                    preview.IsHitTestVisible = false; // reine Ansicht
                    previewHost.Child = preview;
                }
                catch (Exception ex) { Logger.Error($"Design-Vorschau: {ex.Message}"); }
            }
            Refresh();

            var panel = new StackPanel { Margin = new Thickness(24, 14, 32, 12), MaxWidth = 968 };
            panel.Children.Add(crumbs);
            panel.Children.Add(new Border { Margin = new Thickness(0, 12, 0, 0), Child = previewHost });
            panel.Children.Add(new TextBlock
            {
                Text = T("Live-Vorschau · gilt nach „Speichern“", "Live preview · applies after “Save”"), FontSize = 11, Foreground = SubFg,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0),
            });
            _pinnedTop.Child = panel;
            _pinnedTop.Visibility = Visibility.Visible;

            _customPreviewTimer?.Stop();
            _customPreviewTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _customPreviewTimer.Tick += (_, _) => Refresh();
            _customPreviewTimer.Start();
        }

        private void HideMediaCustomHeader()
        {
            _customPreviewTimer?.Stop();
            _customPreviewTimer = null;
            if (_pinnedTop == null) return;
            _pinnedTop.Child = null;
            _pinnedTop.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Unterseite „Eigenes Design“: Vorlagen als Startpunkt, alle Bereiche in der Reihenfolge des Flyouts (Ein/Aus,
        /// ↑/↓, aufgeklappt die einzelnen Elemente als Kacheln und die Feineinstellungen), darunter die Farben.
        /// </summary>
        private void BuildMediaCustomPage(StackPanel page)
        {
            ShowMediaCustomHeader();

            page.Children.Add(SectionTitle(T("Vorlage", "Template")));
            page.Children.Add(Hint(T("Startpunkt wählen und danach beliebig anpassen. Die Farben bleiben erhalten.",
                "Pick a starting point and adjust it as you like. Your colors are kept.")));
            page.Children.Add(BuildMediaTemplates());

            page.Children.Add(SectionTitle(T("Was soll zu sehen sein?", "What should be shown?")));
            page.Children.Add(Hint(T("Schalter und Kacheln blenden ein oder aus · ↑ ↓ ändert die Reihenfolge von oben nach unten · Klick auf eine Zeile zeigt mehr.",
                "Switches and tiles show or hide · ↑ ↓ changes the order from top to bottom · click a row for more.")));
            var elements = new StackPanel();
            page.Children.Add(elements);
            void RenderElements()
            {
                elements.Children.Clear();
                var order = _cfg.MediaCustomOrder = MediaOsd.NormalizeOrder(_cfg.MediaCustomOrder);
                for (int i = 0; i < order.Count; i++)
                {
                    elements.Children.Add(MediaBlockCard(order[i], i, order.Count, (from, delta) =>
                    {
                        int to = from + delta;
                        if (to < 0 || to >= order.Count) return;
                        (order[from], order[to]) = (order[to], order[from]);
                        RenderElements();
                    }));
                }
            }
            RenderElements();
            page.Children.Add(Hint(T("Zeitleiste, ±10 s und „…“ gelten auch für die anderen Designs.",
                "Timeline, ±10 s and “…” also apply to the other designs.")));

            // ── Farben (eigene Farbprofile wie beim OSD) ──
            page.Children.Add(SectionTitle(T("Aussehen", "Appearance")));
            var boxes = new Dictionary<string, TextBox>();
            var colors = new StackPanel();
            AddColorField(colors, boxes, "media_custom_bg", T("Hintergrund", "Background"), _cfg.MediaCustomBg, Defaults.MediaCustomBg, v => _cfg.MediaCustomBg = v);
            AddColorField(colors, boxes, "media_custom_fg", T("Text und Symbole", "Text and icons"), _cfg.MediaCustomFg, Defaults.MediaCustomFg, v => _cfg.MediaCustomFg = v);
            AddColorField(colors, boxes, "media_custom_sub", T("Nebentext", "Secondary text"), _cfg.MediaCustomSub, Defaults.MediaCustomSub, v => _cfg.MediaCustomSub = v);
            AddColorField(colors, boxes, "media_custom_accent", T("Akzent (Leiste, Quelle)", "Accent (bar, source)"), _cfg.MediaCustomAccent, Defaults.MediaCustomAccent, v => _cfg.MediaCustomAccent = v);
            AddColorField(colors, boxes, "media_custom_button_bg", T("Knopf-Fläche", "Button fill"), _cfg.MediaCustomButtonBg, Defaults.MediaCustomButtonBg, v => _cfg.MediaCustomButtonBg = v);
            AddColorField(colors, boxes, "media_custom_track", T("Leiste (Hintergrund)", "Bar (background)"), _cfg.MediaCustomTrack, Defaults.MediaCustomTrack, v => _cfg.MediaCustomTrack = v);
            colors.Children.Insert(0, BuildColorPresets(boxes));
            page.Children.Add(ExpanderCard("\uE790", T("Farben", "Colors"),
                T("Hintergrund, Text, Akzent, Knöpfe und Leiste", "Background, text, accent, buttons and bar"), colors));
            page.Children.Add(Hint(T("Deckkraft, Ecken und Cover als Hintergrund: Seite „Media-Flyout“ → Aussehen.",
                "Opacity, corners and album art background: “Media flyout” page → Appearance.")));
        }

        /// <summary>Ein Bereich des Flyouts als Karte: ↑/↓ und ggf. Ein/Aus im Kopf, aufgeklappt seine Elemente und Einstellungen.</summary>
        private Border MediaBlockCard(string key, int index, int count, Action<int, int> move)
        {
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var up = FlatIconButton("\uE70E", T("Nach oben", "Move up"), () => move(index, -1));     // ChevronUp
            var down = FlatIconButton("\uE70D", T("Nach unten", "Move down"), () => move(index, +1)); // ChevronDown
            up.IsEnabled = index > 0;
            down.IsEnabled = index < count - 1;
            up.Margin = down.Margin = new Thickness(0, 0, 2, 0);
            head.Children.Add(up);
            head.Children.Add(down);
            void Switch(bool value, Action<bool> set)
            {
                var toggle = Toggle(value, set);
                toggle.Margin = new Thickness(10, 0, 0, 0);
                toggle.VerticalAlignment = VerticalAlignment.Center;
                head.Children.Add(toggle);
            }

            string glyph, title, desc;
            StackPanel? body = new StackPanel();
            switch (key)
            {
                case "app":
                    glyph = "\uE71D"; // AllApps
                    title = T("App-Zeile", "App row");
                    desc = T("Name und Symbol der App, die gerade spielt", "Name and icon of the app that is playing");
                    Switch(_cfg.MediaCustomShowApp, v => _cfg.MediaCustomShowApp = v);
                    body.Children.Add(OptionRow(T("„…“-Knopf rechts in dieser Zeile (statt bei den Knöpfen)", "“…” button on the right of this row (instead of with the buttons)"),
                        Toggle(_cfg.MediaCustomMorePos == "top", v => _cfg.MediaCustomMorePos = v ? "top" : "controls")));
                    break;
                case "info":
                    glyph = "\uE8D6"; // Audio
                    title = T("Titel und Cover", "Title and album art");
                    desc = T("Titel, Interpret und Cover", "Title, artist and album art");
                    AddInfoOptions(body);
                    break;
                case "progress":
                    glyph = "\uE823"; // Recent
                    title = T("Zeitleiste", "Timeline");
                    desc = T("Zeigt, wie weit der Titel ist · zum Springen anklicken oder ziehen", "Shows how far the track is · click or drag to jump");
                    Switch(_cfg.MediaShowProgress, v => _cfg.MediaShowProgress = v);
                    body.Children.Add(OptionRow(T("Zeiten darunter", "Times below"),
                        Toggle(_cfg.MediaCustomShowTimes, v => _cfg.MediaCustomShowTimes = v)));
                    break;
                case "sources":
                    glyph = "\uE8AB"; // Switch
                    title = T("Quellen-Leiste", "Sources bar");
                    desc = T("Nur sichtbar, wenn mehrere Apps gleichzeitig Medien melden", "Only visible when several apps report media at once");
                    Switch(_cfg.MediaCustomShowSources, v => _cfg.MediaCustomShowSources = v);
                    body = null;
                    break;
                default: // "controls"
                    glyph = "\uE768"; // Play
                    title = T("Knöpfe", "Buttons");
                    desc = T("Zurück, ±10 s, Play/Pause, Weiter und „…“", "Previous, ±10 s, play/pause, next and “…”");
                    AddButtonOptions(body);
                    break;
            }
            if (body == null) return SettingCard(glyph, title, desc, head);
            return ExpanderCard(glyph, title, desc, body, _customExpanded.Contains(key), head,
                open => { if (open) _customExpanded.Add(key); else _customExpanded.Remove(key); });
        }

        /// <summary>Titel/Interpret/Cover als Kacheln, dazu Platz und Größe des Covers und die Titelgröße.</summary>
        private void AddInfoOptions(StackPanel body)
        {
            string[] covers = { "right", "left", "top" };
            string coverPos = Array.IndexOf(covers, _cfg.MediaCustomCover) >= 0 ? _cfg.MediaCustomCover : "right"; // bei „aus“: Platz fürs Wiedereinschalten
            var tiles = new WrapPanel();
            tiles.Children.Add(ElementTile("\uE8D2", T("Titel", "Title"), _cfg.MediaCustomShowTitle, v => _cfg.MediaCustomShowTitle = v));    // Font
            tiles.Children.Add(ElementTile("\uE77B", T("Interpret", "Artist"), _cfg.MediaCustomShowArtist, v => _cfg.MediaCustomShowArtist = v)); // Contact
            tiles.Children.Add(ElementTile("\uE91B", "Cover", _cfg.MediaCustomCover != "none", v => _cfg.MediaCustomCover = v ? coverPos : "none")); // Photo
            body.Children.Add(tiles);

            var coverCombo = Combo(160, Array.IndexOf(covers, coverPos), T("Rechts", "Right"), T("Links", "Left"), T("Oben", "Top"));
            coverCombo.SelectionChanged += (_, _) =>
            {
                coverPos = covers[Math.Max(0, coverCombo.SelectedIndex)];
                if (_cfg.MediaCustomCover != "none") _cfg.MediaCustomCover = coverPos;
            };
            body.Children.Add(OptionRow(T("Platz des Covers", "Album art position"),
                WithReset(coverCombo, () => coverCombo.SelectedIndex = Math.Max(0, Array.IndexOf(covers, Defaults.MediaCustomCover)))));
            body.Children.Add(OptionRow(T("Größe des Covers", "Album art size"),
                ResetSlider(SliderControl(32, 140, Math.Clamp(_cfg.MediaCustomCoverSize, 32, 140), "0' px'", v => _cfg.MediaCustomCoverSize = (int)v, tick: 2),
                    Defaults.MediaCustomCoverSize)));
            body.Children.Add(OptionRow(T("Titelgröße", "Title size"),
                ResetSlider(SliderControl(11, 22, Math.Clamp(_cfg.MediaCustomTitleSize, 11, 22), "0' pt'", v => _cfg.MediaCustomTitleSize = (int)v, tick: 1),
                    Defaults.MediaCustomTitleSize)));
        }

        /// <summary>Jeder Knopf als Kachel (in der Reihenfolge des Flyouts), dazu Stil, Ausrichtung und Größe.</summary>
        private void AddButtonOptions(StackPanel body)
        {
            var tiles = new WrapPanel();
            tiles.Children.Add(ElementTile("\uE892", T("Zurück", "Previous"), _cfg.MediaCustomShowPrev, v => _cfg.MediaCustomShowPrev = v)); // Previous
            tiles.Children.Add(ElementTile("\uED3C", "±10 s", _cfg.MediaSkipButtons, v => _cfg.MediaSkipButtons = v));                       // SkipBack10
            tiles.Children.Add(ElementTile("\uE768", "Play/Pause", _cfg.MediaCustomShowPlay, v => _cfg.MediaCustomShowPlay = v));            // Play
            tiles.Children.Add(ElementTile("\uE893", T("Weiter", "Next"), _cfg.MediaCustomShowNext, v => _cfg.MediaCustomShowNext = v));     // Next
            tiles.Children.Add(ElementTile("\uE712", T("„…“-Menü", "“…” menu"), _cfg.MediaShowMore, v => _cfg.MediaShowMore = v));          // More
            body.Children.Add(tiles);

            string[] styles = { "round", "flat" };
            var styleCombo = Combo(160, Math.Max(0, Array.IndexOf(styles, _cfg.MediaCustomButtons)), T("Rund", "Round"), T("Flach", "Flat"));
            styleCombo.SelectionChanged += (_, _) => _cfg.MediaCustomButtons = styles[Math.Max(0, styleCombo.SelectedIndex)];
            string[] aligns = { "center", "left", "right" };
            var alignCombo = Combo(160, Math.Max(0, Array.IndexOf(aligns, _cfg.MediaCustomButtonsAlign)), T("Mittig", "Center"), T("Links", "Left"), T("Rechts", "Right"));
            alignCombo.SelectionChanged += (_, _) => _cfg.MediaCustomButtonsAlign = aligns[Math.Max(0, alignCombo.SelectedIndex)];
            body.Children.Add(OptionRow(T("Stil", "Style"),
                WithReset(styleCombo, () => styleCombo.SelectedIndex = Math.Max(0, Array.IndexOf(styles, Defaults.MediaCustomButtons)))));
            body.Children.Add(OptionRow(T("Ausrichtung", "Alignment"),
                WithReset(alignCombo, () => alignCombo.SelectedIndex = Math.Max(0, Array.IndexOf(aligns, Defaults.MediaCustomButtonsAlign)))));
            body.Children.Add(OptionRow(T("Größe", "Size"),
                ResetSlider(SliderControl(70, 140, Math.Clamp(_cfg.MediaCustomButtonSize, 70, 140), "0' %'", v => _cfg.MediaCustomButtonSize = (int)v, tick: 5),
                    Defaults.MediaCustomButtonSize)));
            body.Children.Add(Hint(T("Einträge im „…“-Menü: Seite „Media-Flyout“ → Inhalt.", "Items in the “…” menu: “Media flyout” page → Content.")));
        }

        /// <summary>Kachel für ein einzelnes Element (Symbol + Name): an = Akzentfarbe mit Häkchen, aus = grau; Klick schaltet um.</summary>
        private static Border ElementTile(string glyph, string label, bool on, Action<bool> set)
        {
            var icon = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center };
            var text = new TextBlock { Text = label, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
            var check = new TextBlock
            {
                Text = "\uE73E", FontFamily = IconFont, FontSize = 10, // CheckMark
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 6, 0),
            };
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(icon);
            stack.Children.Add(text);
            var grid = new Grid();
            grid.Children.Add(stack);
            grid.Children.Add(check);
            var tile = new Border
            {
                Width = 84, Height = 62, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 8), Cursor = System.Windows.Input.Cursors.Hand, Child = grid,
                ToolTip = T("Klicken zum Ein- oder Ausblenden", "Click to show or hide"),
            };
            void Apply()
            {
                tile.Background = on ? Accent : FieldBg;
                tile.BorderBrush = on ? Accent : FieldBorder;
                icon.Foreground = text.Foreground = check.Foreground = on ? Brushes.White : SubFg;
                check.Visibility = on ? Visibility.Visible : Visibility.Hidden;
            }
            Apply();
            tile.MouseLeftButtonDown += (_, e) => e.Handled = true;
            tile.MouseLeftButtonUp += (_, e) =>
            {
                on = !on;
                Apply();
                set(on);
                e.Handled = true;
            };
            return tile;
        }

        private static readonly (string Key, string De, string En)[] MediaTemplates =
        {
            ("standard", "Standard", "Standard"),
            ("compact", "Kompakt", "Compact"),
            ("minimal", "Minimal", "Minimal"),
            ("cover", "Großes Cover", "Large album art"),
        };

        /// <summary>Vorlage auf c anwenden: Aufbau, Elemente und Größen (die Farben bleiben). „standard“ = Ausgangszustand.</summary>
        private static void ApplyMediaTemplate(AppConfig c, string key)
        {
            var d = Defaults;
            c.MediaCustomOrder = new List<string>(d.MediaCustomOrder);
            c.MediaCustomShowApp = d.MediaCustomShowApp;
            c.MediaCustomShowTimes = d.MediaCustomShowTimes;
            c.MediaCustomCover = d.MediaCustomCover;
            c.MediaCustomCoverSize = d.MediaCustomCoverSize;
            c.MediaCustomButtons = d.MediaCustomButtons;
            c.MediaCustomButtonsAlign = d.MediaCustomButtonsAlign;
            c.MediaCustomButtonSize = d.MediaCustomButtonSize;
            c.MediaCustomMorePos = d.MediaCustomMorePos;
            c.MediaCustomTitleSize = d.MediaCustomTitleSize;
            c.MediaCustomShowTitle = d.MediaCustomShowTitle;
            c.MediaCustomShowArtist = d.MediaCustomShowArtist;
            c.MediaCustomShowSources = d.MediaCustomShowSources;
            c.MediaCustomShowPrev = d.MediaCustomShowPrev;
            c.MediaCustomShowPlay = d.MediaCustomShowPlay;
            c.MediaCustomShowNext = d.MediaCustomShowNext;
            c.MediaShowProgress = d.MediaShowProgress;
            c.MediaSkipButtons = d.MediaSkipButtons;
            c.MediaShowMore = d.MediaShowMore;
            switch (key)
            {
                case "compact": // eine Zeile Titel mit kleinem Cover links, flache Knöpfe ohne ±10 s
                    c.MediaCustomShowApp = false;
                    c.MediaCustomCover = "left";
                    c.MediaCustomCoverSize = 48;
                    c.MediaCustomTitleSize = 13;
                    c.MediaCustomShowTimes = false;
                    c.MediaCustomButtons = "flat";
                    c.MediaCustomButtonSize = 90;
                    c.MediaSkipButtons = false;
                    break;
                case "minimal": // nur Titel, Interpret und Zurück/Play/Weiter
                    c.MediaCustomShowApp = false;
                    c.MediaCustomCover = "none";
                    c.MediaCustomButtons = "flat";
                    c.MediaCustomShowSources = false;
                    c.MediaShowProgress = false;
                    c.MediaSkipButtons = false;
                    c.MediaShowMore = false;
                    break;
                case "cover": // großes Cover oben, „…“ in der App-Zeile
                    c.MediaCustomCover = "top";
                    c.MediaCustomCoverSize = 140;
                    c.MediaCustomTitleSize = 16;
                    c.MediaCustomButtonSize = 110;
                    c.MediaCustomMorePos = "top";
                    break;
            }
        }

        /// <summary>Vorlagen als Kacheln mit echter Mini-Vorschau (Flyout mit angewendeter Vorlage und den eigenen Farben).</summary>
        private UIElement BuildMediaTemplates()
        {
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = MediaTemplates.Length };
            for (int i = 0; i < MediaTemplates.Length; i++)
            {
                var (key, de, en) = MediaTemplates[i];
                var sample = CloneConfig(_cfg);
                ApplyMediaTemplate(sample, key);
                FrameworkElement thumb;
                try { thumb = MediaOsd.CreatePreview(sample); }
                catch (Exception ex)
                {
                    Logger.Error($"Vorlagen-Vorschau: {ex.Message}");
                    thumb = new Border();
                }
                thumb.IsHitTestVisible = false;
                var content = new StackPanel();
                content.Children.Add(new Viewbox { Height = 96, Stretch = Stretch.Uniform, Child = thumb });
                content.Children.Add(new TextBlock { Text = T(de, en), Foreground = Fg, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
                var tile = new Border
                {
                    Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10), Margin = new Thickness(0, 0, i < MediaTemplates.Length - 1 ? 8 : 0, 0),
                    Cursor = System.Windows.Input.Cursors.Hand, Child = content,
                    ToolTip = T("Als Startpunkt übernehmen", "Use as starting point"),
                };
                tile.MouseEnter += (_, _) => tile.Background = HoverBg;
                tile.MouseLeave += (_, _) => tile.Background = CardBg;
                tile.MouseLeftButtonUp += (_, _) =>
                {
                    ApplyMediaTemplate(_cfg, key);
                    RebuildMediaCustomPage();
                };
                grid.Children.Add(tile);
            }
            return grid;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Tastenkürzel (alle Tasten an einem Ort; stehen zusätzlich auf ihren Fachseiten)
        // ══════════════════════════════════════════════════════════════════
        private void BuildHotkeysPage(StackPanel page)
        {
            page.Children.Add(PageTitle(T("Tastenkürzel", "Shortcuts")));
            page.Children.Add(Hint(T("Die Tasten stehen zusätzlich auf ihren Seiten (Profile, OSD, Media-Flyout) – Änderungen gelten überall.",
                "The keys are also on their own pages (Profiles, OSD, Media flyout) – changes apply everywhere.")));

            page.Children.Add(SectionTitle(T("Profil", "Profile")));
            page.Children.Add(ProfileHotkeyCard());
            page.Children.Add(SettingCard("\uE916", T("Haltedauer für Positionswechsel", "Hold time for position switch"),
                T("So lange „Profil wechseln“ halten = nächste gespeicherte Position", "Hold “Switch profile” this long = next saved position"),
                SliderControl(200, 2000, _cfg.SnapHoldMs, "0' ms'", v => _cfg.SnapHoldMs = (int)v, tick: 50)));
            page.Children.Add(SettingCard("\uE916", T("Weiter halten = schneller", "Keep holding = faster"),
                T("Jeder weitere Wechsel nach halber Haltedauer", "Each further switch after half the hold time"),
                Toggle(_cfg.SnapRepeatFast, v => _cfg.SnapRepeatFast = v)));
            page.Children.Add(ProfileHotkeysCard(out _));

            page.Children.Add(SectionTitle(T("Vor- und Zurückspulen", "Skip forward and back")));
            AddSkipHotkeyCards(page);

            page.Children.Add(SectionTitle(T("Mediengesten", "Media gestures")));
            AddGestureCards(page);
            page.Children.Add(SettingCard("\uE916", T("Wie lange halten", "How long to hold"), T("Für „Lange halten“", "For “Hold”"),
                SliderControl(200, 2000, _cfg.MediaSwitchHoldMs, "0' ms'", v => _cfg.MediaSwitchHoldMs = (int)v, tick: 50)));
            page.Children.Add(SettingCard("\uE916", T("Zeit für zweimal drücken", "Time to press twice"), T("Für „Zweimal schnell drücken“", "For “Press twice quickly”"),
                SliderControl(150, 800, Math.Clamp(_cfg.MediaDoubleMs, 150, 800), "0' ms'", v => _cfg.MediaDoubleMs = (int)v, tick: 25)));
        }

        /// <summary>Seiten, auf denen dieselben Hotkey-Einstellungen stehen.</summary>
        private static readonly string[] HotkeyPages = { "profiles", "osd", "media", "hotkeys" };

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

        /// <summary>Profil-Taste (Seiten Lautstärke &amp; Apps, OSD und Tastenkürzel).</summary>
        private Border ProfileHotkeyCard() => SettingCard("\uE765", T("Profil wechseln", "Switch profile"),
            T("Kurz drücken: nächstes Profil · Halten: nächste gespeicherte Position", "Tap: next profile · Hold: next saved position"),
            BuildHotkeyCapture(_cfg.ProfileHotkey, v => _cfg.ProfileHotkey = v, allowClear: false));

        /// <summary>±10-s-Tasten mit Ein/Aus (Seiten Media-Flyout und Tastenkürzel).</summary>
        private void AddSkipHotkeyCards(StackPanel page)
        {
            page.Children.Add(SettingCard("\uE765", T("10 s vorspulen", "Skip forward 10 s"), null,
                BuildHotkeyCapture(_cfg.MediaSkipForwardHotkey, v => _cfg.MediaSkipForwardHotkey = v)));
            page.Children.Add(SettingCard("\uE765", T("10 s zurückspulen", "Skip back 10 s"), null,
                BuildHotkeyCapture(_cfg.MediaSkipBackHotkey, v => _cfg.MediaSkipBackHotkey = v)));
            page.Children.Add(SettingCard("\uE765", T("Spul-Tasten aktiv", "Skip keys enabled"),
                T("Aus = beide Tasten ruhen, bleiben aber gespeichert", "Off = both keys pause but stay saved"),
                Toggle(_cfg.MediaSkipHotkeysEnabled, Shared<bool>(v => _cfg.MediaSkipHotkeysEnabled = v))));
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

        /// <summary>Zeile im aufgeklappten Teil einer Karte: Beschriftung links, Bedienelement rechts.</summary>
        private static Grid OptionRow(string label, UIElement control)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = label, Foreground = Fg, VerticalAlignment = VerticalAlignment.Center });
            if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            row.Children.Add(control);
            return row;
        }

        /// <summary>Ein Schalter je möglichem Eintrag des "…"-Menüs (cfg.media_more_items).</summary>
        private UIElement BuildMoreMenuOptions()
        {
            var panel = new StackPanel();
            (string Key, string Label)[] options =
            {
                ("shuffle", T("Zufallswiedergabe", "Shuffle")),
                ("repeat", T("Wiederholen", "Repeat")),
                ("switch_source", T("Nächste Quelle", "Next source")),
                ("skip_hotkeys", T("Hotkeys ±10 s an/aus", "±10 s hotkeys on/off")),
                ("pin", T("Angeheftet lassen (nicht ausblenden)", "Keep pinned (don't hide)")),
                ("save_position", T("Position speichern", "Save position")),
                ("next_position", T("Nächste Position", "Next position")),
                ("reset_position", T("Position zurücksetzen", "Reset position")),
                ("settings", T("Einstellungen öffnen", "Open settings")),
                ("close", T("Ausblenden", "Hide")),
            };
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
        private static readonly (string Layout, string Side)[] Arrangements =
        {
            ("separate", ""),
            ("combined", "above"), // Media-Flyout über dem OSD
            ("combined", "below"), // Media-Flyout unter dem OSD
        };

        private static int ArrangementIndex(AppConfig c) =>
            c.OsdLayout == "separate" ? 0 : c.MediaCombinedSide == "above" ? 1 : 2;

        /// <summary>Platz und Bildschirm des zusammen angezeigten Blocks = die des Ankers (combined_anchor).</summary>
        private static (string Preset, string Device) CombinedPosition(AppConfig c) =>
            c.CombinedAnchor == "media" ? (c.MediaCornerPreset, c.MediaMonitor) : (c.OsdCornerPreset, c.OsdMonitor);

        /// <summary>
        /// Platz für den zusammen angezeigten Block: Anker passend zum Platz (oben/Mitte das obere, unten das
        /// untere Element, Placement.CombinedAnchorFor), Platz und Bildschirm für beide – so stehen sie auch im
        /// Design „Eigenes“ (dort kein Anhängen) am selben Platz. „Eigene Position“ behält den Anker.
        /// </summary>
        private void SetCombinedPosition(string preset, string device)
        {
            if (Placement.IsPreset(preset)) _cfg.CombinedAnchor = Placement.CombinedAnchorFor(_cfg.MediaCombinedSide, preset);
            _cfg.OsdCornerPreset = _cfg.MediaCornerPreset = preset;
            _cfg.OsdMonitor = _cfg.MediaMonitor = device;
            LayoutTouched();
        }

        /// <summary>Platz-Auswahl je nach Anordnung: zusammen eine für beide, getrennt je eine für OSD und Media-Flyout.</summary>
        private void BuildPositionCards(StackPanel host, List<MonitorArea> monitors)
        {
            host.Children.Clear();
            _osdPicker = _mediaPicker = _combinedPicker = null;
            string desc = T("Kachel anklicken · „Eigene Position“ = verschoben/Snap-Punkt", "Click a tile · “Own position” = dragged/snap point");

            if (_cfg.OsdLayout == "combined")
            {
                var (preset, device) = CombinedPosition(_cfg);
                var both = new ScreenTilePicker(monitors, device, preset);
                both.Changed += () => SetCombinedPosition(both.Preset, both.Device);
                _combinedPicker = both;
                host.Children.Add(SettingCard("\uE8A9", T("OSD und Media-Flyout", "OSD and media flyout"), desc,
                    WithReset(both, () =>
                    {
                        both.Set(Defaults.OsdCornerPreset, "");
                        SetCombinedPosition(Defaults.OsdCornerPreset, "");
                    })));
                return;
            }

            var osd = new ScreenTilePicker(monitors, _cfg.OsdMonitor, _cfg.OsdCornerPreset);
            osd.Changed += () => { _cfg.OsdCornerPreset = osd.Preset; _cfg.OsdMonitor = osd.Device; LayoutTouched(); };
            _osdPicker = osd;
            host.Children.Add(SettingCard("\uE7F4", "OSD", desc, WithReset(osd, () =>
            {
                osd.Set(Defaults.OsdCornerPreset, "");
                _cfg.OsdCornerPreset = Defaults.OsdCornerPreset;
                _cfg.OsdMonitor = "";
                LayoutTouched();
            })));

            var media = new ScreenTilePicker(monitors, _cfg.MediaMonitor, _cfg.MediaCornerPreset);
            media.Changed += () => { _cfg.MediaCornerPreset = media.Preset; _cfg.MediaMonitor = media.Device; LayoutTouched(); };
            _mediaPicker = media;
            host.Children.Add(SettingCard("\uE8D6", "Media-Flyout", desc, WithReset(media, () =>
            {
                media.Set(Defaults.MediaCornerPreset, "");
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
            ScreenTilePicker? snapOsd = null, snapMedia = null, snapBoth = null;

            // Neue Snap-Position per Kacheln statt Ziehen: getrennt je OSD und Media-Flyout, zusammen eine für den Block.
            void BuildSnapChoice()
            {
                snapChoiceHost.Children.Clear();
                snapOsd = snapMedia = snapBoth = null;
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                if (_cfg.OsdLayout == "combined")
                {
                    snapBoth = new ScreenTilePicker(monitors, "", "top_right", allowCustom: false);
                    row.Children.Add(Labeled(T("OSD und Media-Flyout", "OSD and media flyout"), snapBoth));
                }
                else
                {
                    snapOsd = new ScreenTilePicker(monitors, "", "top_right", allowCustom: false);
                    snapMedia = new ScreenTilePicker(monitors, "", "top_center", allowCustom: false);
                    row.Children.Add(Labeled("OSD", snapOsd));
                    row.Children.Add(Labeled("Media-Flyout", snapMedia));
                }
                snapChoiceHost.Children.Add(row);
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
            page.Children.Add(SettingCard("\uE8A9", T("OSD und Media-Flyout anordnen", "Arrange OSD and media flyout"),
                T("Zusammen: eine Platz-Auswahl für beide · beim OSD-Design „Eigenes“ nur gleicher Platz, ohne Aneinanderhängen",
                    "Together: one place for both · with the “Custom” OSD design same place only, not attached"),
                WithReset(arrangeCombo, () => arrangeCombo.SelectedIndex = ArrangementIndex(Defaults))));

            // ── Platz-Voreinstellungen (Kacheln, passen sich automatisch an Auflösung/Taskleiste an) ──
            page.Children.Add(SectionTitle(T("Platz auf dem Bildschirm", "Place on screen")));
            page.Children.Add(Hint(T("Erkannt: ", "Detected: ") + string.Join("  ·  ", monitors.Select((m, i) => Placement.MonitorLabel(m, i)))));
            page.Children.Add(positionHost);
            BuildPositionCards(positionHost, monitors);

            var test = AccentBtn(T("Testen", "Test"), 110);
            test.Margin = new Thickness(0);
            test.Click += (_, _) => RunPositionTest(null, null);
            page.Children.Add(SettingCard("\uE768", T("Position testen", "Test position"),
                T("Zeigt OSD und Media-Flyout kurz genau dort, wo sie erscheinen werden", "Briefly shows the OSD and media flyout exactly where they will appear"), test));
            page.Children.Add(Hint(T("OSD und Flyout bleiben immer im Bildschirm, unten direkt über der Taskleiste, und überlappen sich nicht. " +
                "Bei einer anderen Auflösung werden eigene Positionen automatisch umgerechnet.",
                "OSD and flyout always stay on screen, at the bottom right above the taskbar, and never overlap. " +
                "On a resolution change, own positions are converted automatically.")));

            page.Children.Add(SectionTitle(T("Verschieben", "Moving")));
            page.Children.Add(SettingCard("\uE7C2", T("Mit der Maus verschieben", "Drag with the mouse"),
                T("Rastet an gespeicherten Positionen ein", "Snaps to saved positions"),
                Toggle(_cfg.SnapEnabled, v => _cfg.SnapEnabled = v)));

            // Gespeicherte Positionen (Snap-Punkte)
            var snapBody = new StackPanel();
            var listBox = new ListBox { Height = 96, Background = FieldBg, Foreground = Fg, BorderBrush = FieldBorder, Margin = new Thickness(0, 0, 0, 6) };
            foreach (var p in _cfg.SnapPositions) listBox.Items.Add(FormatSnapPoint(p));

            var addBtn = Btn(T("Aktuelle Position hinzufügen", "Add current position"));
            addBtn.Click += (_, _) =>
            {
                // Liest IMMER frisch von der Festplatte (nicht die Felder unten, die seit dem
                // Öffnen des Fensters veraltet sein können) - inkl. korrekter Standardecke, falls
                // noch nie gezogen wurde. Ein Klick übernimmt beide Positionen auf einmal.
                var point = OsdManager.CurrentSnapPoint(ConfigManager.Load());
                _cfg.SnapPositions.Add(point);
                listBox.Items.Add(FormatSnapPoint(point));
            };

            var removeBtn = Btn(T("Entfernen", "Remove"));
            removeBtn.Click += (_, _) =>
            {
                if (listBox.SelectedIndex < 0) return;
                _cfg.SnapPositions.RemoveAt(listBox.SelectedIndex);
                listBox.Items.RemoveAt(listBox.SelectedIndex);
            };

            var testSnap = Btn(T("Testen", "Test"));
            testSnap.ToolTip = T("Ausgewählte Position kurz anzeigen", "Briefly show the selected position");
            testSnap.Click += (_, _) =>
            {
                int i = listBox.SelectedIndex;
                if (i < 0 || i >= _cfg.SnapPositions.Count) return;
                var sp = _cfg.SnapPositions[i];
                bool hasMedia = sp.MediaX.HasValue && sp.MediaY.HasValue;
                // Wie im Snap-Zyklus: mit eigener Flyout-Position stehen beide unabhängig.
                var tmp = CloneConfig(_cfg);
                tmp.SnapIndependent = hasMedia && tmp.OsdLayout == "combined";
                RunPositionTest(new Point(sp.X, sp.Y), hasMedia ? new Point(sp.MediaX!.Value, sp.MediaY!.Value) : null, tmp);
            };

            snapBody.Children.Add(listBox);
            var snapButtons = Row(addBtn, removeBtn, testSnap);
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
                else if (snapOsd != null && snapMedia != null)
                {
                    tmp.OsdCornerPreset = snapOsd.Preset;
                    tmp.OsdMonitor = snapOsd.Device;
                    tmp.MediaCornerPreset = snapMedia.Preset;
                    tmp.MediaMonitor = snapMedia.Device;
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
                var (osd, media, _) = ChosenLayout();
                // Media-Position immer mit speichern: wirkt bei "Getrennt" und "OSD unter/über dem Media";
                // hängt das Flyout am OSD, wird sie ignoriert.
                var point = new SnapPosition
                {
                    X = (int)Math.Round(osd.X), Y = (int)Math.Round(osd.Y),
                    MediaX = (int)Math.Round(media.X), MediaY = (int)Math.Round(media.Y),
                };
                _cfg.SnapPositions.Add(point);
                listBox.Items.Add(FormatSnapPoint(point));
                listBox.SelectedIndex = listBox.Items.Count - 1;
            };
            var testChosen = Btn(T("Testen", "Test"));
            testChosen.ToolTip = T("Auswahl vor dem Hinzufügen kurz anzeigen", "Briefly show the choice before adding it");
            testChosen.Click += (_, _) =>
            {
                var (osd, media, tmp) = ChosenLayout();
                RunPositionTest(osd.TopLeft, media.TopLeft, tmp);
            };

            snapBody.Children.Add(new TextBlock
            {
                Text = T("Neue Position per Auswahl", "New position by choice"), Foreground = SubFg, FontSize = 12, Margin = new Thickness(0, 4, 0, 2),
            });
            snapBody.Children.Add(snapChoiceHost);
            var chosenButtons = Inline(testChosen, addChosen);
            chosenButtons.Margin = new Thickness(0, 8, 0, 4);
            snapBody.Children.Add(chosenButtons);
            snapBody.Children.Add(Hint(T("Gespeicherte Positionen setzen OSD und Flyout genau an ihre Stelle – auch bei „Zusammen“. " +
                "Das Anhängen gilt wieder, sobald du Anordnung oder Platz änderst oder die Position zurücksetzt.",
                "Saved positions put the OSD and flyout exactly in place – even when “combined”. " +
                "Attaching applies again once you change the arrangement or place, or reset the position.")));
            page.Children.Add(ExpanderCard("\uE81D", T("Gespeicherte Positionen", "Saved positions"),
                T("Snap-Punkte · Wechseln: „Profil wechseln“ halten (Seite „Tastenkürzel“)", "Snap points · Switch: hold “Switch profile” (“Shortcuts” page)"), snapBody));

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
                _osdPicker?.Set("custom");
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
            page.Children.Add(SettingCard("\uE707", T("Position zurücksetzen", "Reset position"),
                T("Wenn OSD oder Media-Flyout nicht sichtbar sind.", "If the OSD or media flyout is off-screen."), resetBtn));
        }

        // ══════════════════════════════════════════════════════════════════
        //  Spotify
        // ══════════════════════════════════════════════════════════════════
        private void BuildSpotifyPage(StackPanel page)
        {
            page.Children.Add(PageTitle("Spotify"));

            page.Children.Add(SectionTitle(T("Steuerung", "Control")));
            page.Children.Add(SettingCard("\uE8D6", T("Über die Spotify-API steuern", "Control via the Spotify API"),
                T("Statt über den Windows-Mixer · braucht Spotify Premium", "Instead of the Windows mixer · needs Spotify Premium"),
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
            page.Children.Add(ContentCard("\uE946", T("So geht's", "How it works"), null, steps));

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
            page.Children.Add(Hint(T("Lautstärke über die API geht nur mit Spotify Premium und wenn Spotify gerade auf einem Gerät abspielt. " +
                "Ohne API steuert KnobFlyout Spotify wie jede andere App über den Windows-Mixer.",
                "Volume via the API only works with Spotify Premium and while Spotify is playing on a device. " +
                "Without the API, KnobFlyout controls Spotify like any other app via the Windows mixer.")));
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
        private static void BuildInfoPage(StackPanel page)
        {
            page.Children.Add(PageTitle("Info"));
            page.Children.Add(SectionTitle("KnobFlyout"));
            page.Children.Add(SettingCard("\uE946", "KnobFlyout",
                T("Lautstärke pro App mit OSD und Media-Flyout", "Per-app volume with OSD and media flyout"), null));
            page.Children.Add(SectionTitle(T("Dateien", "Files")));
            page.Children.Add(SettingCard("\uE713", T("Konfiguration", "Configuration"), System.IO.Path.Combine(AppContext.BaseDirectory, "config.json"), null));
            page.Children.Add(SettingCard("\uE81C", T("Protokoll", "Log"), System.IO.Path.Combine(AppContext.BaseDirectory, "knob_service.log"), null));
        }

        // ── Hotkey-Feld (Aufnahme per Tastendruck, optional ✕ zum Entfernen) ──

        /// <summary>
        /// Klick ins Feld, dann Tastenkombination drücken (Esc bricht ab). Mit allowClear entfernt
        /// "✕" den Hotkey (leer = deaktiviert). onChanged bekommt z.B. "ctrl+alt+right" bzw. "".
        /// Ein Statustext erscheint nur während der Aufnahme oder bei einem Problem.
        /// </summary>
        /// <param name="emptyText">Anzeige, wenn kein Hotkey gesetzt ist (z.B. "Play/Pause-Taste" als Vorgabe).</param>
        private UIElement BuildHotkeyCapture(string current, Action<string> onChanged, bool allowClear = true, string? emptyText = null)
        {
            onChanged = Shared(onChanged); // jeder Hotkey steht auch auf der Seite „Hotkeys“
            string saved = current ?? "";
            string Shown(string v) => string.IsNullOrWhiteSpace(v) ? emptyText ?? T("Nicht festgelegt", "Not set") : v;

            var box = TextField(Shown(saved), 180);
            box.IsReadOnly = true;
            box.Cursor = System.Windows.Input.Cursors.Hand;
            box.Margin = new Thickness(0);

            var status = new TextBlock { Foreground = SubFg, FontSize = 11, Margin = new Thickness(0, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            void SetStatus(string text)
            {
                status.Text = text;
                status.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            void ShowValidity()
            {
                // Nur ein ungültiger gespeicherter Wert wird angezeigt (z.B. von Hand in der Config eingetragen).
                bool invalid = saved.Length > 0 && HotkeyParser.Parse(saved).Item2 == 0;
                SetStatus(invalid ? T("Ungültig – wird ignoriert", "Invalid – ignored") : "");
            }
            ShowValidity();

            bool capturing = false;

            box.GotFocus += (_, _) =>
            {
                capturing = true;
                box.Text = T("Taste(n) drücken …", "Press key(s) …");
                SetStatus(T("Esc = abbrechen", "Esc = cancel"));
            };

            box.LostFocus += (_, _) =>
            {
                if (!capturing) return;
                capturing = false;
                box.Text = Shown(saved);
                ShowValidity();
            };

            box.PreviewKeyDown += (_, e) =>
            {
                if (!capturing) return;
                e.Handled = true;

                var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;

                if (key == System.Windows.Input.Key.Escape)
                {
                    capturing = false;
                    box.Text = Shown(saved);
                    ShowValidity();
                    System.Windows.Input.Keyboard.ClearFocus();
                    return;
                }

                bool pureModifier = key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl
                    or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift
                    or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt
                    or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin;

                var mods = new List<string>();
                var held = System.Windows.Input.Keyboard.Modifiers;
                if ((held & System.Windows.Input.ModifierKeys.Control) != 0) mods.Add("ctrl");
                if ((held & System.Windows.Input.ModifierKeys.Alt) != 0) mods.Add("alt");
                if ((held & System.Windows.Input.ModifierKeys.Shift) != 0) mods.Add("shift");
                if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.LWin) || System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.RWin)) mods.Add("win");

                if (pureModifier)
                {
                    // Nur Vorschau anzeigen, noch nicht übernehmen – wartet auf die Haupttaste.
                    box.Text = mods.Count > 0 ? string.Join("+", mods) + "+…" : T("Taste(n) drücken …", "Press key(s) …");
                    return;
                }

                int vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
                string? name = HotkeyParser.NameForVk(vk);
                if (name == null)
                {
                    SetStatus(T("Taste nicht unterstützt", "Key not supported"));
                    return;
                }

                mods.Remove(name); // falls Haupttaste selbst ein Modifier-Name wäre (sollte nicht vorkommen)
                string combo = mods.Count > 0 ? string.Join("+", mods) + "+" + name : name;

                saved = combo;
                capturing = false;
                box.Text = combo;
                SetStatus("");
                onChanged(combo);
                System.Windows.Input.Keyboard.ClearFocus();
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            row.Children.Add(box);

            if (allowClear)
            {
                var clear = Btn("✕");
                clear.ToolTip = T("Hotkey entfernen", "Remove hotkey");
                clear.Margin = new Thickness(6, 0, 0, 0);
                clear.Click += (_, _) =>
                {
                    saved = "";
                    capturing = false;
                    box.Text = Shown(saved);
                    SetStatus("");
                    onChanged("");
                };
                row.Children.Add(clear);
            }

            var panel = new StackPanel();
            panel.Children.Add(row);
            panel.Children.Add(status);
            return panel;
        }

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
        private void RunPositionTest(Point? osdAt, Point? mediaAt, AppConfig? cfg = null)
        {
            try
            {
                cfg ??= _cfg;
                var media = MediaOsd.CreateTest(cfg);
                var (osd, flyout) = Placement.Layout(cfg, media.MeasureHeight(), osdAt, mediaAt);
                OsdCard.ShowTest(cfg, osd.TopLeft);
                media.ShowTestAt(flyout.TopLeft);
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
