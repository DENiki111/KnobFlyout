using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using KnobFlyout.Config;
using KnobFlyout.Profiles;

namespace KnobFlyout.Osd
{
    internal static class UiColor
    {
        // Eingefrorene Brushes sind unveränderlich und teilbar - pro Hex-Wert nur einmal parsen/erzeugen
        // (UpdateVolume im Dark-Design setzte sonst bei jeder Rast neue Brushes).
        private static readonly ConcurrentDictionary<string, SolidColorBrush> Cache = new();

        public static SolidColorBrush Brush(string? hex) => Cache.GetOrAdd(hex ?? "#ffffff", Create);

        /// <summary>Wie Brush(), aber mit Deckkraft (0–1) – für "Deckkraft nur auf den Hintergrund".</summary>
        public static SolidColorBrush Brush(string? hex, double opacity)
        {
            var c = Brush(hex).Color;
            byte a = (byte)Math.Round(c.A * Math.Clamp(opacity, 0.0, 1.0));
            return Cache.GetOrAdd($"#{a:X2}{c.R:X2}{c.G:X2}{c.B:X2}", Create);
        }

        private static SolidColorBrush Create(string hex)
        {
            SolidColorBrush b;
            try { b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { b = new SolidColorBrush(Colors.White); }
            b.Freeze();
            return b;
        }
    }

    /// <summary>
    /// Eine OSD-Karte = eigenes Fenster: topmost, rahmenlos, nicht aktivierbar. Standardmäßig
    /// klick-durchlässig; ist cfg.SnapEnabled aktiv, kann die Karte per Maus verschoben werden
    /// (siehe Dragged-Event, ausgewertet in OsdManager für Anker-Position + Einrasten).
    /// Drei Designs: "dark" (Standard, wie knob_service.py), "modern"/"windows" (ModernFlyouts-Look:
    /// 354 px breit, Radius 8, Segoe-Fluent-Lautsprecher-Glyph, Slider mit festem Blau, Zahl rechts)
    /// und "win11" (Windows-11-Flyout: flacher, 1-px-Rand, Windows-Akzentfarbe, Slider-Griff mit
    /// Ring). modern und win11 folgen dem Farbschema hell/dunkel und blenden weich ein/aus.
    /// </summary>
    internal sealed partial class OsdCard : Window
    {
        public const double Gap = 8;
        private const double DarkW = 340;
        private const double ModernW = 354;
        private const double CardH = 72;
        private const double TrackLeft = 56;                 // 48 Glyph + 8 Rand
        private const double ModernTrackW = ModernW - 112;   // minus Glyph, Zahl und Ränder

        private const double Win11W = 320;
        private const double Win11H = 56;
        private const double Win11TrackLeft = 48;
        private const double Win11TrackW = Win11W - 48 - 56; // minus Glyph links und Zahl rechts

        private readonly AppConfig _cfg;
        private readonly bool _modern; // modern ODER win11 (helles/dunkles Thema, Einblenden)
        private readonly bool _win11;
        private readonly double _w;
        private readonly double _h;
        private readonly double _darkBarW;
        private readonly double _targetOpacity;
        private readonly Canvas _canvas = new();
        private readonly SolidColorBrush _fg;
        private readonly SolidColorBrush _sub;
        private readonly SolidColorBrush _trackBrush;
        private readonly SolidColorBrush _accent;
        private readonly SolidColorBrush _thumbRing;

        // Design-System (DesignSystem.cs): gesetzt, wenn osd_design eines der Designs aus Designs.All ist
        private readonly DesignSpec? _spec;
        private readonly DesignPalette? _pal;
        private readonly Border _shell;      // Kartenfläche (Design-System: wird beim Erscheinen aufgezogen)
        private ScaleTransform? _fillScale;  // Design-System: Lautstärke-Füllung per Skalierung (kein Neuaufbau je Raste)
        private Border? _islandFill;         // Dynamic Island: Füllung (bei „Stumm“ gedimmt)

        private TextBlock? _lapp;
        private TextBlock? _lpct;
        private TextBlock? _glyph;
        private Rectangle? _barBg;
        private Rectangle? _barFill;
        private Ellipse? _thumb;

        private static readonly FontFamily UiFont = new("Segoe UI, Segoe UI Emoji");
        private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

        private static double Pt(double points) => points * 96.0 / 72.0;

        private static bool IsWin11(AppConfig cfg) => cfg.OsdDesign == "win11";
        private static bool IsModern(AppConfig cfg) => cfg.OsdDesign == "modern" || cfg.OsdDesign == "windows" || IsWin11(cfg);

        /// <summary>Entspricht OSD._dims(): (Breite, Höhe, Abstand) je nach Design.</summary>
        public static (double W, double H, double Gap) Dims(AppConfig cfg) =>
            Designs.Get(cfg.OsdDesign) is { } spec ? (spec.OsdWidth, spec.OsdHeight, Gap)
            : IsWin11(cfg) ? (Win11W, Win11H, Gap) : IsModern(cfg) ? (ModernW, CardH, Gap) : (DarkW, CardH, Gap);

        private OsdCard(AppConfig cfg)
        {
            _cfg = cfg;
            _spec = Designs.Get(cfg.OsdDesign);
            _modern = IsModern(cfg);
            _win11 = IsWin11(cfg);
            (_w, _h, _) = Dims(cfg);
            _darkBarW = DarkW - 24;
            _targetOpacity = Math.Clamp(cfg.WinAlpha, 0.0, 1.0);

            bool light = (_modern || _spec != null) && ThemeHelper.IsLight(cfg);
            _pal = _spec?.Palette(light); // Design-System: alle Farben aus der Palette
            _fg = UiColor.Brush(_pal?.Fg ?? (light ? (_win11 ? "#1b1b1b" : "#000000") : "#ffffff"));
            _sub = UiColor.Brush(_pal?.Sub ?? (light ? "#5c5c5c" : _win11 ? "#c5c5c5" : "#a0a0a0"));
            _trackBrush = UiColor.Brush(_pal?.Track ?? (_win11 ? (light ? "#B4B4B8" : "#5C5C5C") : light ? "#d0d0d0" : "#5a5a63"));
            // ModernFlyouts: festes Blau wie im Original; Windows 11: bewusst schwarz/weiß (Füllung = Textfarbe).
            _accent = _pal != null ? UiColor.Brush(_pal.Fill) : _win11 ? _fg : UiColor.Brush("#3b82f6");
            _thumbRing = UiColor.Brush(light ? "#E9E9EB" : "#454545");

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            Width = _w;
            Height = _h;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            // Deckkraft (cfg.WinAlpha) wirkt nur auf den Hintergrund, Text/Symbole bleiben voll deckend.
            Opacity = _modern ? 0.0 : 1.0; // modern/win11 blenden per FadeIn() ein
            Content = _shell = new Border
            {
                CornerRadius = new CornerRadius(Math.Clamp(cfg.OsdCornerRadius, 0, 30)), // Standard 8 = ModernFlyouts' FlyoutCornerRadius
                Background = UiColor.Brush(_win11 ? (light ? "#E9E9EB" : "#2C2C2C")
                    : _modern ? (light ? "#EFEFEF" : "#222222") : cfg.ColorBg, _targetOpacity), // modern: exakt ModernFlyouts' FlyoutBackground
                BorderBrush = _win11 ? UiColor.Brush(light ? "#D0D0D4" : "#404040", _targetOpacity) : null,
                BorderThickness = new Thickness(_win11 ? 1 : 0),
                Child = _canvas,
            };
            if (_spec != null && _pal != null)
            {
                // Form des Designs (Kapsel: Radius = halbe Höhe) statt Ecken-Einstellung; 1-px-Rand trennt vom Hintergrund.
                _shell.CornerRadius = new CornerRadius(_spec.OsdRadius);
                _shell.Background = _pal.Background(_targetOpacity);
                _shell.BorderBrush = UiColor.Brush(_pal.Edge);
                _shell.BorderThickness = new Thickness(1);
                _shell.Child = null; // Zeichenfläche lösen, damit sie unter die Lichtkante gehängt werden kann
                _shell.Child = _pal.WithHighlight(_canvas, _spec.OsdRadius - 1); // macOS: Lichtkante innen
                if (IsHud) SetupHudShell(); // Gaming-HUD: abgeschrägte Fläche statt Rechteck
                if (IsHardware) Place(HardwareParts.Screws(_w, _h, HardwareParts.Colors(light)), -1, -1); // Hardware: Schrauben
                _shell.RenderTransformOrigin = new Point(0.5, 0.5);
                _shell.RenderTransform = new ScaleTransform(1, 1);
                _shell.Opacity = 0; // blendet per FadeIn() ein (sonst blitzt die Karte vor der Animation kurz auf)
            }
            SourceInitialized += OnSourceInitialized;

            if (cfg.SnapEnabled || cfg.ClickToDismiss)
            {
                MouseLeftButtonDown += (_, _) =>
                {
                    if (cfg.SnapEnabled)
                    {
                        double left = Left, top = Top;
                        try
                        {
                            DragMove();
                        }
                        catch
                        {
                            // DragMove() wirft, wenn die Maustaste zwischenzeitlich losgelassen wurde - ignorieren.
                            return;
                        }
                        // Kaum bewegt = einfacher Klick → ggf. ausblenden statt verschieben.
                        bool moved = Math.Abs(Left - left) > 3 || Math.Abs(Top - top) > 3;
                        if (moved || !cfg.ClickToDismiss)
                        {
                            Dragged?.Invoke(this);
                            return;
                        }
                    }
                    DismissRequested?.Invoke();
                };
            }
        }

        /// <summary>Wird nach einem abgeschlossenen Drag ausgelöst (nur wenn cfg.SnapEnabled true ist).</summary>
        public event Action<OsdCard>? Dragged;

        /// <summary>Einfacher Klick auf die Karte bei cfg.ClickToDismiss: der Stapel soll sofort verschwinden.</summary>
        public event Action? DismissRequested;

        // ── Fabriken ──────────────────────────────────────────────────────────
        public static OsdCard CreateVolume(AppConfig cfg, string name)
        {
            var card = new OsdCard(cfg);
            if (card.IsMac) card.BuildVolumeMac();
            else if (card.IsMaterial) card.BuildVolumeMaterial();
            else if (card.IsHud) card.BuildVolumeHud();
            else if (card.IsHardware) card.BuildVolumeHardware();
            else if (card._spec != null) card.BuildVolumeIsland();
            else if (card._win11) card.BuildVolumeWin11();
            else if (card._modern) card.BuildVolumeModern();
            else card.BuildVolume(name);
            return card;
        }

        public static OsdCard CreateProfile(AppConfig cfg)
        {
            var card = new OsdCard(cfg);
            if (card.IsMac) card.BuildProfileMac();
            else if (card.IsMaterial) card.BuildProfileMaterial();
            else if (card.IsHud) card.BuildProfileHud();
            else if (card.IsHardware) card.BuildProfileHardware();
            else if (card._spec != null) card.BuildProfileIsland();
            else if (card._win11) card.BuildProfileWin11();
            else if (card._modern) card.BuildProfileModern();
            else card.BuildProfile();
            return card;
        }

        /// <summary>Entspricht dem Lock-Keys-Flyout von ModernFlyouts (Caps/Num/Scroll Lock).</summary>
        public static OsdCard CreateLockKey(AppConfig cfg, string name, bool on)
        {
            var card = new OsdCard(cfg);
            if (card.IsMac) card.BuildLockKeyMac(name, on);
            else if (card.IsMaterial) card.BuildLockKeyMaterial(name, on);
            else if (card.IsHud) card.BuildLockKeyHud(name, on);
            else if (card.IsHardware) card.BuildLockKeyHardware(name, on);
            else if (card._spec != null) card.BuildLockKeyIsland(name, on);
            else if (card._modern) card.BuildLockKeyClean(name, on); // modern + Win11: schlicht wie Windows
            else card.BuildLockKey(name, on);
            return card;
        }

        /// <summary>Entspricht show_snap_move(): zeigt "📍 Position X/Y" beim Langdruck-Zyklen durch snap_positions.</summary>
        public static OsdCard CreateSnapInfo(AppConfig cfg, int index, int total)
        {
            var card = new OsdCard(cfg);
            if (card.IsMac) card.BuildSnapInfoMac(index, total);
            else if (card.IsMaterial) card.BuildSnapInfoMaterial(index, total);
            else if (card.IsHud) card.BuildSnapInfoHud(index, total);
            else if (card.IsHardware) card.BuildSnapInfoHardware(index, total);
            else if (card._spec != null) card.BuildSnapInfoIsland(index, total);
            else if (card._win11) card.BuildSnapInfoWin11(index, total);
            else if (card._modern) card.BuildSnapInfoModern(index, total);
            else card.BuildSnapInfo(index, total);
            return card;
        }

        /// <summary>"Testen" in den Einstellungen: Beispiel-Lautstärkekarte kurz an der Stelle at zeigen.</summary>
        public static void ShowTest(AppConfig cfg, Point at, int ms = 3000)
        {
            var card = CreateVolume(cfg, "spotify.exe");
            card.UpdateVolume("spotify.exe", 64, false);
            card.Left = at.X;
            card.Top = at.Y;
            card.Show();
            card.FadeIn();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try { card.CloseAnimated(); } catch { }
            };
            timer.Start();
        }

        /// <summary>
        /// Karteninhalt für die Live-Vorschau in den Einstellungen ("volume", "profile", "lockkey",
        /// "snap"): Karte mit cfg (auch ungespeichert) bauen, Inhalt herauslösen, Fenster verwerfen.
        /// </summary>
        public static FrameworkElement CreatePreview(AppConfig cfg, string kind)
        {
            OsdCard card = kind switch
            {
                "volume" => CreateVolume(cfg, "spotify.exe"),
                "profile" => CreateProfile(cfg),
                "lockkey" => CreateLockKey(cfg, "CapsLock", true),
                _ => CreateSnapInfo(cfg, 0, 3),
            };
            if (kind == "volume") card.UpdateVolume("spotify.exe", 64, false);
            var content = (FrameworkElement)card.Content;
            card.Content = null;
            content.Width = card.Width;
            content.Height = card.Height;
            content.Opacity = 1; // Design-System: sonst noch unsichtbar (blendet erst beim Anzeigen ein)
            card.Close(); // nie angezeigt - nur aus der Fensterliste der App entfernen
            return content;
        }

        // ── Animation (ModernFlyouts: 83 ms halten, dann 166 ms linear) ───────
        public void FadeIn()
        {
            if (_spec != null)
            {
                AppearSpec();
                return;
            }
            if (!_modern) return;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1.0, TimeSpan.FromMilliseconds(166))
            {
                BeginTime = TimeSpan.FromMilliseconds(83),
            });
        }

        public void CloseAnimated()
        {
            if (_spec != null)
            {
                HideSpec();
                return;
            }
            if (!_modern)
            {
                Close();
                return;
            }
            var anim = new DoubleAnimation(1.0, 0, TimeSpan.FromMilliseconds(166));
            anim.Completed += (_, _) =>
            {
                try { Close(); } catch { }
            };
            BeginAnimation(OpacityProperty, anim);
        }

        // ══ Design-System: Dynamic Island (Kapsel, Farben/Maße aus DesignSpec) ══════

        /// <summary>Aufziehen aus der schmalen Kapsel; Inhalt blendet kurz danach ein.</summary>
        private void AppearSpec()
        {
            if (_spec == null) return;
            var dur = TimeSpan.FromMilliseconds(_spec.AppearMs);
            // Ohne Überschwingen: über die Fenstergröße hinaus würde das Fenster die Kanten abschneiden.
            var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 };
            if (_shell.RenderTransform is ScaleTransform st)
            {
                st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(_spec.AppearFromScale, 1, dur) { EasingFunction = ease });
                st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(_spec.AppearFromScaleY, 1, dur) { EasingFunction = ease });
            }
            _shell.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(_spec.AppearFadeMs)));
            if (_spec.ContentDelayMs <= 0) return; // Inhalt erscheint gleich mit der Fläche
            _canvas.Opacity = 0;
            _canvas.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
            {
                BeginTime = TimeSpan.FromMilliseconds(_spec.ContentDelayMs),
            });
        }

        /// <summary>Zurück zur schmalen Kapsel und ausblenden, danach schließen.</summary>
        private void HideSpec()
        {
            var dur = TimeSpan.FromMilliseconds(_spec!.HideMs);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            if (!_spec.SlowFade) // macOS: nur langsam verblassen, nicht schrumpfen
            {
                if (_shell.RenderTransform is ScaleTransform st)
                {
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(_spec.AppearFromScale, dur) { EasingFunction = ease });
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(_spec.AppearFromScaleY, dur) { EasingFunction = ease });
                }
                _canvas.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(Math.Min(100, _spec.HideMs))));
            }
            var fade = new DoubleAnimation(0, dur);
            fade.Completed += (_, _) =>
            {
                try { Close(); } catch { }
            };
            _shell.BeginAnimation(OpacityProperty, fade);
        }

        /// <summary>Eine Zeile über die ganze Kapsel (Innenabstand links 20, rechts 22), senkrecht mittig.</summary>
        private Grid IslandRow(params GridLength[] columns)
        {
            const double padLeft = 20, padRight = 22;
            var row = new Grid { Width = _w - 2 - padLeft - padRight, Height = _h - 2 };
            foreach (var c in columns) row.ColumnDefinitions.Add(new ColumnDefinition { Width = c });
            Place(row, padLeft, 0);
            return row;
        }

        private static void Cell(Grid row, UIElement element, int column)
        {
            Grid.SetColumn(element, column);
            row.Children.Add(element);
        }

        private TextBlock IslandText(string text, double size, Brush fg, bool bold = true) => new()
        {
            Text = text, FontFamily = _spec!.Font, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = fg, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };

        private static TextBlock IslandGlyph(string glyph, double size, Brush fg) => new()
        {
            Text = glyph, FontFamily = IconFont, FontSize = size, Foreground = fg,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };

        /// <summary>Kleines Schild rechts (z. B. „AN“/„AUS“).</summary>
        private Border IslandChip(string text, string bgHex, string fgHex) => new()
        {
            CornerRadius = new CornerRadius(12), Padding = new Thickness(11, 3, 11, 4), Background = UiColor.Brush(bgHex),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontFamily = _spec!.Font, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = UiColor.Brush(fgHex) },
        };

        /// <summary>Lautstärke: Stufen-Symbol · App-Name · Leiste · Zahl (bzw. rotes Stumm-Symbol und „Stumm“).</summary>
        private void BuildVolumeIsland()
        {
            var row = IslandRow(new GridLength(26), GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto);
            _glyph = IslandGlyph("\uE995", 21, _fg);
            Cell(row, _glyph, 0);

            _lapp = IslandText("", 14, _fg);
            _lapp.MaxWidth = 130;
            _lapp.Opacity = 0.92;
            _lapp.Margin = new Thickness(12, 0, 14, 0);
            Cell(row, _lapp, 1);

            // Füllung als Skalierung einer vollen Leiste: jede Raste ändert nur ScaleX (animiert, kein Neuaufbau).
            _fillScale = new ScaleTransform(0, 1);
            var bar = new Grid { VerticalAlignment = VerticalAlignment.Center };
            bar.Children.Add(new Border { Height = 7, CornerRadius = new CornerRadius(3.5), Background = _trackBrush });
            bar.Children.Add(_islandFill = new Border { Height = 7, CornerRadius = new CornerRadius(3.5), Background = _accent, RenderTransform = _fillScale });
            Cell(row, bar, 2);

            _lpct = IslandText("", 17, _fg);
            _lpct.MinWidth = 38;
            _lpct.TextAlignment = TextAlignment.Right;
            _lpct.Margin = new Thickness(14, 0, 0, 0);
            System.Windows.Documents.Typography.SetNumeralAlignment(_lpct, FontNumeralAlignment.Tabular);
            Cell(row, _lpct, 3);
        }

        private void UpdateIsland(string clean, int pct, bool muted)
        {
            if (_lapp == null || _lpct == null || _glyph == null || _fillScale == null || _pal == null) return;
            _lapp.Text = clean;
            _glyph.Text = muted ? "\uE74F" : pct == 0 ? "\uE992" : pct < 34 ? "\uE993" : pct < 67 ? "\uE994" : "\uE995";
            _glyph.Foreground = muted ? UiColor.Brush(_pal.Mute) : _fg;
            _lpct.Text = muted ? Loc.T("Stumm", "Muted") : pct.ToString();
            _lpct.FontSize = muted ? 13 : 17;
            _lpct.Foreground = muted ? _sub : _fg;

            // Stumm: Pegel bleibt sichtbar, aber gedimmt (wie in den übrigen Designs)
            if (_islandFill != null) _islandFill.Opacity = muted ? 0.35 : 1.0;
            double target = pct / 100.0;
            if (IsVisible)
                _fillScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(90)));
            else
            {
                _fillScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                _fillScale.ScaleX = target;
            }
        }

        /// <summary>Profilwechsel: nur Logo (in Profilfarbe bzw. eigenes Bild) und Profilname, mittig.</summary>
        private void BuildProfileIsland()
        {
            var profiles = ProfileDefinitions.For(_cfg);
            var def = profiles.FirstOrDefault(p => p.Key == _cfg.ActiveProfile) ?? profiles.FirstOrDefault();
            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            FrameworkElement logo = def != null ? def.CreateLogo(22, UiColor.Brush(def.Color)) : IslandGlyph("\uE734", 22, _fg);
            if (logo is Image) logo.Width = logo.Height = 26;
            logo.VerticalAlignment = VerticalAlignment.Center;
            panel.Children.Add(logo);
            var name = IslandText(def?.Name ?? "Profile", 17, _fg);
            name.Margin = new Thickness(12, 0, 0, 0);
            name.MaxWidth = _w - 90;
            panel.Children.Add(name);

            var host = new Grid { Width = _w - 2, Height = _h - 2 };
            host.Children.Add(panel);
            Place(host, 0, 0);
        }

        /// <summary>Feststelltasten: Schloss · Name · „AN“ (grün) / „AUS“.</summary>
        private void BuildLockKeyIsland(string name, bool on)
        {
            string label = name switch
            {
                "CapsLock" => Loc.T("Feststelltaste", "Caps Lock"),
                "NumLock" => Loc.T("Nummernblock", "Num Lock"),
                _ => Loc.T("Rollen", "Scroll Lock"),
            };
            var row = IslandRow(new GridLength(26), new GridLength(1, GridUnitType.Star), GridLength.Auto);
            Cell(row, IslandGlyph(on ? LockGlyph : UnlockGlyph, 20, _fg), 0);
            var title = IslandText(label, 16, _fg);
            title.Margin = new Thickness(12, 0, 8, 0);
            Cell(row, title, 1);
            Cell(row, on ? IslandChip(Loc.T("AN", "ON"), _pal!.On, _pal.OnText) : IslandChip(Loc.T("AUS", "OFF"), _pal!.Off, _pal.OffText), 2);
        }

        /// <summary>Positionswechsel: Stecknadel · „Position n“ · Punkte (aktueller Punkt gefüllt), ab 9 Punkten als Zahl.</summary>
        private void BuildSnapInfoIsland(int index, int total)
        {
            var row = IslandRow(new GridLength(26), new GridLength(1, GridUnitType.Star), GridLength.Auto);
            Cell(row, IslandGlyph("\uE707", 20, _fg), 0); // MapPin
            var title = IslandText($"Position {index + 1}", 16, _fg);
            title.Margin = new Thickness(12, 0, 8, 0);
            Cell(row, title, 1);

            FrameworkElement right;
            if (total <= 8)
            {
                var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                for (int i = 0; i < total; i++)
                    dots.Children.Add(new Ellipse
                    {
                        Width = 8, Height = 8, Margin = new Thickness(i == 0 ? 0 : 7, 0, 0, 0),
                        Fill = i == index ? _accent : _trackBrush,
                    });
                right = dots;
            }
            else right = IslandText($"{index + 1} / {total}", 14, _sub, bold: false);
            Cell(row, right, 2);
        }

        // ── Volume-Karte Dark (entspricht _make_vol_card_dark) ────────────────
        private void BuildVolume(string name)
        {
            _lapp = new TextBlock
            {
                Width = _w - 120, Height = 20, FontFamily = UiFont, FontSize = Pt(10),
                Foreground = UiColor.Brush(_cfg.ColorVolText),
            };
            Place(_lapp, 12, 10);

            _lpct = new TextBlock
            {
                Width = 55, Height = 20, FontFamily = UiFont, FontSize = Pt(10), FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Right, Foreground = UiColor.Brush(_cfg.ColorVolPct),
            };
            Place(_lpct, _w - 95, 10);

            _barBg = new Rectangle { Width = _darkBarW, Height = 7, Fill = UiColor.Brush(_cfg.ColorBarBg) };
            Place(_barBg, 12, 32);
            _barFill = new Rectangle { Width = 0, Height = 7, Fill = Brushes.Transparent };
            Place(_barFill, 12, 32);

            string current = GetAppProfile(name);
            double px = 12;
            foreach (var p in ProfileDefinitions.For(_cfg))
            {
                var brush = UiColor.Brush(current == p.Key ? p.Color : "#252540");
                // Unverändertes Standard-Profil: Emoji wie bisher, sonst das gewählte Logo (Symbol/Bild).
                FrameworkElement icon = p.Icon.Length > 0
                    ? new TextBlock { Text = p.Icon, FontFamily = UiFont, FontSize = Pt(11), Foreground = brush }
                    : p.CreateLogo(Pt(11), brush);
                if (p.Image != null && current != p.Key) icon.Opacity = 0.35; // Bild nicht einfärbbar → abgeblendet
                Place(icon, px, CardH - 25);
                px += 30;
            }
        }

        // ── Volume-Karte Modern (ModernFlyouts-Layout: Icon – Slider – Zahl in einer Reihe) ──
        private void BuildVolumeModern()
        {
            _lapp = new TextBlock
            {
                Width = _w - 32, Height = 14, FontFamily = UiFont, FontSize = 9, Foreground = _sub,
            };
            Place(_lapp, 16, 6);

            var glyphHost = new Grid { Width = 40, Height = 40 };
            _glyph = new TextBlock
            {
                FontFamily = IconFont, FontSize = 16, Foreground = _fg,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            glyphHost.Children.Add(_glyph);
            Place(glyphHost, 4, 22);

            _barBg = new Rectangle { Width = ModernTrackW, Height = 4, RadiusX = 2, RadiusY = 2, Fill = _trackBrush };
            Place(_barBg, TrackLeft, 40);
            _barFill = new Rectangle { Width = 0, Height = 4, RadiusX = 2, RadiusY = 2, Fill = _accent };
            Place(_barFill, TrackLeft, 40);
            _thumb = new Ellipse { Width = 14, Height = 14, Fill = _accent };
            Place(_thumb, TrackLeft - 7, 35);

            var valueHost = new Grid { Width = 44, Height = 40 };
            _lpct = new TextBlock
            {
                FontFamily = UiFont, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = _fg,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            };
            valueHost.Children.Add(_lpct);
            Place(valueHost, _w - 48, 22);
        }

        public void UpdateVolume(string name, int pct, bool muted)
        {
            string clean = name.Replace(".exe", "");
            if (clean.Length > 20) clean = clean[..20] + "…";
            pct = Math.Clamp(pct, 0, 100);

            if (_spec != null)
            {
                if (IsMac) UpdateMac(clean, pct, muted);
                else if (IsMaterial) UpdateMaterial(clean, pct, muted);
                else if (IsHud) UpdateHud(clean, pct, muted);
                else if (IsHardware) UpdateHardware(clean, pct, muted);
                else UpdateIsland(clean, pct, muted);
                return;
            }
            if (_modern)
            {
                UpdateModern(clean, pct, muted);
                return;
            }

            if (_lapp == null || _lpct == null || _barBg == null || _barFill == null) return;

            _lapp.Text = (muted ? "🔇" : "🔊") + "  " + clean;

            if (muted)
            {
                _lpct.Text = Loc.T("STUMM", "MUTED");
                _barBg.Fill = UiColor.Brush("#333333");
                _barFill.Width = 0;
                return;
            }

            _lpct.Text = $"{pct}%";
            _barBg.Fill = UiColor.Brush(_cfg.ColorBarBg);
            _barFill.Fill = UiColor.Brush(pct > 55 ? _cfg.ColorBarHi : pct > 20 ? _cfg.ColorBarMid : _cfg.ColorBarLo);
            _barFill.Width = _darkBarW * pct / 100.0;
        }

        private void UpdateModern(string clean, int pct, bool muted)
        {
            if (_lapp == null || _lpct == null || _glyph == null || _barFill == null || _thumb == null) return;

            double trackLeft = _win11 ? Win11TrackLeft : TrackLeft;
            double trackW = _win11 ? Win11TrackW : ModernTrackW;

            _lapp.Text = clean;
            double fillWidth;
            if (muted)
            {
                _glyph.Text = "\uE74F";
                _lpct.Text = "–";
                fillWidth = 0;
            }
            else
            {
                _glyph.Text = pct == 0 ? "\uE992" : pct < 34 ? "\uE993" : pct < 67 ? "\uE994" : "\uE995";
                _lpct.Text = pct.ToString();
                fillWidth = trackW * pct / 100.0;
            }

            _barFill.Width = fillWidth;
            Canvas.SetLeft(_thumb, trackLeft + fillWidth - (_win11 ? _thumb.Width / 2 : 9));
            _thumb.Fill = muted ? _trackBrush : _accent;
        }

        // ── Windows-11-Design: Symbol – Name/Slider – Zahl, flach mit 1-px-Rand ─────
        private void BuildVolumeWin11()
        {
            _lapp = new TextBlock
            {
                Width = Win11TrackW, FontFamily = UiFont, FontSize = 12, Foreground = _sub,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Place(_lapp, Win11TrackLeft, 9);

            var glyphHost = new Grid { Width = Win11TrackLeft, Height = _h };
            _glyph = new TextBlock
            {
                FontFamily = IconFont, FontSize = 16, Foreground = _fg,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            glyphHost.Children.Add(_glyph);
            Place(glyphHost, 0, 0);

            const double trackY = 35;
            _barBg = new Rectangle { Width = Win11TrackW, Height = 4, RadiusX = 2, RadiusY = 2, Fill = _trackBrush };
            Place(_barBg, Win11TrackLeft, trackY);
            _barFill = new Rectangle { Width = 0, Height = 4, RadiusX = 2, RadiusY = 2, Fill = _accent };
            Place(_barFill, Win11TrackLeft, trackY);
            // Windows-11-Slider-Griff: Akzentpunkt mit Ring in Kartenfarbe.
            _thumb = new Ellipse { Width = 18, Height = 18, Fill = _accent, Stroke = _thumbRing, StrokeThickness = 4 };
            Place(_thumb, Win11TrackLeft - 9, trackY + 2 - 9);

            var valueHost = new Grid { Width = 40, Height = _h };
            _lpct = new TextBlock
            {
                FontFamily = UiFont, FontSize = 15, Foreground = _fg,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            valueHost.Children.Add(_lpct);
            Place(valueHost, _w - 48, 0);
        }

        /// <summary>Fluent-Symbol in Textfarbe (Info-Karten).</summary>
        private TextBlock GlyphIcon(string glyph) => new()
        {
            Text = glyph, FontFamily = IconFont, FontSize = 16, Foreground = _fg,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };

        /// <summary>Logo des aktiven Profils (Symbol in Textfarbe bzw. eigenes Bild, 20 px).</summary>
        private FrameworkElement ActiveProfileLogo()
        {
            var profiles = ProfileDefinitions.For(_cfg);
            var def = profiles.FirstOrDefault(p => p.Key == _cfg.ActiveProfile) ?? profiles.FirstOrDefault();
            if (def == null) return GlyphIcon("\uE734");
            return def.Image != null ? def.CreateLogo(20, _fg) : GlyphIcon(def.Glyph);
        }

        /// <summary>Gemeinsamer Aufbau der Win11-Infokarten (schwarz/weiß): Symbol, Titel + Untertitel.</summary>
        private void BuildInfoWin11(FrameworkElement icon, string title, string subtitle)
        {
            var iconHost = new Grid { Width = Win11TrackLeft, Height = _h };
            iconHost.Children.Add(icon);
            Place(iconHost, 0, 0);

            Place(new TextBlock
            {
                Text = title, FontFamily = UiFont, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = _fg,
                Width = _w - Win11TrackLeft - 16, TextTrimming = TextTrimming.CharacterEllipsis,
            }, Win11TrackLeft, 9);
            Place(new TextBlock { Text = subtitle, FontFamily = UiFont, FontSize = 12, Foreground = _sub }, Win11TrackLeft, 29);
        }

        private void BuildProfileWin11()
        {
            var (_, label, _) = ProfileInfo();
            BuildInfoWin11(ActiveProfileLogo(), label, Loc.T("Aktives Profil", "Active profile"));
        }

        private void BuildSnapInfoWin11(int index, int total)
        {
            BuildInfoWin11(GlyphIcon(SnapGlyph), "Position", $"{index + 1} / {total}");
        }

        private const string SnapGlyph = "\uE81D"; // Location

        // ── Profil-Karte Dark (entspricht show_profile_switch, Dark-Design) ───
        private void BuildProfile()
        {
            var (icon, label, pcolor) = ProfileInfo();

            // Unverändertes Standard-Profil: Emoji wie bisher, sonst das gewählte Logo in Profilfarbe.
            var profiles = ProfileDefinitions.For(_cfg);
            var def = profiles.FirstOrDefault(p => p.Key == _cfg.ActiveProfile) ?? profiles.FirstOrDefault();
            Place(icon.Length > 0 || def == null
                ? new TextBlock { Text = icon, FontFamily = UiFont, FontSize = Pt(18), Foreground = UiColor.Brush(pcolor) }
                : def.CreateLogo(Pt(18), UiColor.Brush(pcolor)), 14, 10);

            Place(new TextBlock
            {
                Text = label, FontFamily = UiFont, FontSize = Pt(13), FontWeight = FontWeights.Bold,
                Foreground = UiColor.Brush(_cfg.ColorProfText),
            }, 46, 14);

            Place(new Rectangle { Width = _w - 28, Height = 5, Fill = UiColor.Brush(_cfg.ColorBarBg) }, 14, CardH - 14);
            Place(new Rectangle { Width = _w - 28, Height = 5, Fill = UiColor.Brush(pcolor) }, 14, CardH - 14);
        }

        // ── Info-Karten Modern (Profil, Position): ein gemeinsamer Aufbau ──────
        /// <summary>
        /// Einheitliche ModernFlyouts-Infokarte: Fluent-Symbol links (gleich groß und an gleicher Stelle
        /// wie der Lautsprecher der Lautstärkekarte), Titel + Untertitel, darunter eine dünne Leiste
        /// (fraction = gefüllter Anteil, z.B. 2/3 bei "Position 2 / 3").
        /// </summary>
        private void BuildInfoModern(FrameworkElement icon, string title, string subtitle, Brush barFill, double fraction)
        {
            var glyphHost = new Grid { Width = 40, Height = 40 };
            glyphHost.Children.Add(icon);
            Place(glyphHost, 4, 10);

            Place(new TextBlock
            {
                Text = title, FontFamily = UiFont, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = _fg,
                Width = _w - TrackLeft - 16, TextTrimming = TextTrimming.CharacterEllipsis,
            }, TrackLeft, 10);
            Place(new TextBlock { Text = subtitle, FontFamily = UiFont, FontSize = 12, Foreground = _sub }, TrackLeft, 31);

            double barW = _w - TrackLeft - 16;
            Place(new Rectangle { Width = barW, Height = 4, RadiusX = 2, RadiusY = 2, Fill = _trackBrush }, TrackLeft, CardH - 16);
            Place(new Rectangle { Width = barW * Math.Clamp(fraction, 0, 1), Height = 4, RadiusX = 2, RadiusY = 2, Fill = barFill }, TrackLeft, CardH - 16);
        }

        private void BuildProfileModern()
        {
            var (_, label, pcolor) = ProfileInfo();
            BuildInfoModern(ActiveProfileLogo(), label, Loc.T("Aktives Profil", "Active profile"),
                UiColor.Brush(pcolor), 1.0);
        }

        private void BuildSnapInfoModern(int index, int total)
        {
            BuildInfoModern(GlyphIcon(SnapGlyph), "Position", $"{index + 1} / {total}", _accent, total > 0 ? (index + 1.0) / total : 1.0);
        }

        // ── Feststelltasten: schlicht wie die Windows-Anzeige (Schloss + "… ist an"), modern und Win11 ──
        private void BuildLockKeyClean(string name, bool on)
        {
            string label = name switch
            {
                "CapsLock" => on ? Loc.T("Feststelltaste ist an", "Caps Lock is on") : Loc.T("Feststelltaste ist aus", "Caps Lock is off"),
                "NumLock" => on ? Loc.T("Nummernblock ist an", "Num Lock is on") : Loc.T("Nummernblock ist aus", "Num Lock is off"),
                _ => on ? Loc.T("Rollen ist an", "Scroll Lock is on") : Loc.T("Rollen ist aus", "Scroll Lock is off"),
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new TextBlock
            {
                Text = on ? LockGlyph : UnlockGlyph, FontFamily = IconFont, FontSize = 15, Foreground = _fg,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
            });
            row.Children.Add(new TextBlock { Text = label, FontFamily = UiFont, FontSize = 14, Foreground = _fg, VerticalAlignment = VerticalAlignment.Center });

            var host = new Grid { Width = _w, Height = _h };
            host.Children.Add(row);
            Place(host, 0, 0);
        }

        private const string LockGlyph = "\uE72E";   // Lock
        private const string UnlockGlyph = "\uE785"; // Unlock

        private (string Icon, string Label, string Color) ProfileInfo()
        {
            var profiles = ProfileDefinitions.For(_cfg);
            var def = profiles.FirstOrDefault(p => p.Key == _cfg.ActiveProfile) ?? profiles.FirstOrDefault();
            string icon = def?.Icon ?? "";
            string label = def?.Name ?? "Profile";
            string pcolor = def?.Color ?? "#a855f7";
            return (icon, label, pcolor);
        }

        // ── Lock-Keys-Karte (Caps/Num/Scroll Lock, ein einfaches Icon + Status) ─
        private void BuildLockKey(string name, bool on)
        {
            string icon = name switch { "CapsLock" => "⇪", "NumLock" => "🔢", _ => "⤓" };
            string label = name switch
            {
                "CapsLock" => Loc.T("Feststelltaste", "Caps Lock"),
                "NumLock" => Loc.T("Nummernblock", "Num Lock"),
                _ => Loc.T("Rollen", "Scroll Lock"),
            };
            string stateColor = on ? "#4ade80" : "#f87171";

            Place(new TextBlock
            {
                Text = icon, FontFamily = UiFont, FontSize = Pt(18),
                Foreground = _modern ? _fg : UiColor.Brush("#aaaadd"),
            }, 14, 10);

            Place(new TextBlock
            {
                Text = $"{label} {(on ? Loc.T("AN", "ON") : Loc.T("AUS", "OFF"))}", FontFamily = UiFont, FontSize = Pt(12), FontWeight = FontWeights.Bold,
                Foreground = UiColor.Brush(stateColor),
            }, 46, 14);

            Place(new Rectangle { Width = _w - 28, Height = 5, Fill = _modern ? _trackBrush : UiColor.Brush(_cfg.ColorBarBg) }, 14, CardH - 14);
            Place(new Rectangle { Width = on ? _w - 28 : 0, Height = 5, Fill = UiColor.Brush(stateColor) }, 14, CardH - 14);
        }

        // ── Snap-Position-Karte (entspricht show_snap_move) ────────────────────
        private void BuildSnapInfo(int index, int total)
        {
            string borderColor = _cfg.ColorSnapBorder;
            string textColor = _cfg.ColorSnapText;

            Place(new TextBlock
            {
                Text = "📍", FontFamily = UiFont, FontSize = Pt(18),
                Foreground = UiColor.Brush(textColor),
            }, 14, 10);

            Place(new TextBlock
            {
                Text = $"Position {index + 1}/{total}", FontFamily = UiFont, FontSize = Pt(13), FontWeight = FontWeights.Bold,
                Foreground = UiColor.Brush(textColor),
            }, 46, 14);

            Place(new Rectangle { Width = _w - 28, Height = 5, Fill = _modern ? _trackBrush : UiColor.Brush(_cfg.ColorBarBg) }, 14, CardH - 14);
            Place(new Rectangle { Width = _w - 28, Height = 5, Fill = UiColor.Brush(borderColor) }, 14, CardH - 14);
        }

        // ── Hilfen ────────────────────────────────────────────────────────────
        private void Place(UIElement element, double left, double top)
        {
            Canvas.SetLeft(element, left);
            Canvas.SetTop(element, top);
            _canvas.Children.Add(element);
        }

        /// <summary>Entspricht OSD._get_app_profile().</summary>
        private string GetAppProfile(string app)
        {
            string lower = app.ToLowerInvariant();
            foreach (var kv in _cfg.Profiles)
            {
                if (kv.Value.Any(a => lower.Contains(a.ToLowerInvariant()) || a.ToLowerInvariant().Contains(lower)))
                    return kv.Key;
            }
            return "";
        }

        // ── Fenster-Stile (nicht aktivierbar; klick-durchlässig, außer wenn verschiebbar) ──
        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            ex |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            if (!_cfg.SnapEnabled && !_cfg.ClickToDismiss) ex |= WS_EX_TRANSPARENT; // verschiebbar/wegklickbar → muss Maus-Events empfangen
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
