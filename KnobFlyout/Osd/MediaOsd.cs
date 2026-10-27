using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using KnobFlyout.Config;
using KnobFlyout.Media;

namespace KnobFlyout.Osd
{
    /// <summary>
    /// Media-Flyout im Look des echten ModernFlyouts: App-Name (+ Logo) oben links, Cover oben
    /// rechts, Titel/Interpret, Fortschrittsleiste mit Zeiten (zum Springen anklickbar/ziehbar),
    /// darunter "…"-Menü (Zufall/Wiederholen), Zurück, Play/Pause im Kreis und Weiter.
    /// Optional (cfg.media_art_background) liegt das Cover weichgezeichnet als Kartenhintergrund.
    /// Immer klickbar (nicht klick-durchlässig).
    ///
    /// Drei Designs (cfg.media_design, unabhängig vom OSD): "modernflyouts" (Cover rechts, runde
    /// Knöpfe), "win11" (Cover links, flache Knöpfe, Windows-Akzent, 1-px-Rand) und "custom" (eigenes:
    /// Reihenfolge der Bereiche, Cover, Knöpfe, Titelgröße und Farben aus cfg.media_custom_*, ArrangeCustom).
    ///
    /// Positionierung (cfg.OsdLayout, "combined" nur bei osd_design "modern"/"windows"/"win11"):
    /// - "combined": hängt direkt unter bzw. über (cfg.MediaCombinedSide) dem Lautstärke-/Profil-Stapel
    ///   (per OsdManager.StackChanged neu berechnet), nicht eigenständig verschiebbar.
    /// - "separate": eigene Position (cfg.MediaOsdX/Y), per Maus verschiebbar inkl. Einrasten.
    ///
    /// Eine einzelne Instanz wird von App.xaml.cs wiederverwendet und bei jeder Änderung
    /// aktualisiert; die Höhe passt sich dem Inhalt an (Quellen-Leiste nur bei mehreren Quellen).
    /// </summary>
    internal sealed partial class MediaOsd : Window
    {
        private const double W = 354;

        /// <summary>Breite des Flyouts und grobe Höhe, solange es noch nie angezeigt wurde (für Platzierung).</summary>
        public const double FlyoutWidth = W;
        public const double EstimatedHeight = 190;

        /// <summary>Wird gerade per Maus gezogen (dann nicht automatisch verschieben).</summary>
        public bool IsDragging => _dragging;

        /// <summary>Ziehen beendet (neue Position gespeichert) – App prüft danach auf Überlappung.</summary>
        public event Action? DragFinished;
        private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

        private readonly MediaController _media;
        private readonly bool _light;
        private readonly bool _win11; // cfg.media_design "win11": Windows-11-Look statt ModernFlyouts
        private readonly bool _custom; // cfg.media_design "custom": eigener Aufbau, eigene Farben und Größen
        private readonly bool _flatButtons; // flache Knöpfe (Windows 11 bzw. eigenes Design „flach“)
        private readonly double _buttonScale = 1.0;
        private readonly SolidColorBrush? _customButtonBg;
        private readonly string _styleKey; // Design + Farbschema bzw. alle eigenen Einstellungen (StyleChanged)
        private FrameworkElement? _progressBlock; // eigenes Design: Fortschritt-Bereich (mit Leiste ein-/ausgeblendet)
        private readonly bool _hideSources;       // eigenes Design: Quellen-Leiste abgeschaltet

        // Design-System (DesignSystem.cs): gesetzt, wenn media_design eines der Designs aus Designs.All ist
        private readonly DesignSpec? _spec;
        private DesignPalette? _pal; // Material You: wechselt mit der Cover-Farbe (ApplyMaterial)
        private readonly SolidColorBrush _waveBrush = new(Colors.White); // Wellenform in Cover-Farbe
        private readonly List<ScaleTransform> _waveBars = new();
        private bool _waveRunning;
        private readonly SolidColorBrush _accent;
        private readonly SolidColorBrush _fgBrush;
        private readonly SolidColorBrush _subBrush;
        private string _cardHex;
        private bool _pinned; // "Anheften" im "…"-Menü: nicht automatisch ausblenden
        private Popup? _menuPopup;
        private readonly Border _card;
        private readonly Grid _host;
        private readonly Grid _content; // alles außer dem Hintergrund (Deckkraft: media_content_opacity)
        private readonly AppConfig? _previewCfg; // gesetzt = Vorschau in den Einstellungen
        private double _radius;
        private bool _pressed;        // Maus auf freier Fläche gedrückt (für "Klick blendet aus")
        private bool _clickDismiss;
        private double _pressLeft, _pressTop;

        /// <summary>Der Nutzer hat das Flyout per Klick ausgeblendet (cfg.ClickToDismiss).</summary>
        public event Action? DismissRequested;

        /// <summary>Aktion aus dem "…"-Menü, die die App ausführt: "save_position", "next_position", "reset_position".</summary>
        public event Action<string>? MenuActionRequested;

        /// <summary>"Einstellungen" im "…"-Menü gewählt.</summary>
        public event Action? SettingsRequested;

        /// <summary>Das "…"-Menü hat eine Einstellung direkt gespeichert (z.B. ±10-s-Hotkeys an/aus).</summary>
        public event Action? ConfigChanged;
        private readonly DispatcherTimer _hideTimer;
        private readonly DispatcherTimer _tickTimer;

        private readonly Image _bgImage;
        private readonly Border _bgOverlay;
        private readonly Image _appIcon;
        private readonly TextBlock _appGlyph;
        private readonly TextBlock _appName;
        private readonly TextBlock _title;
        private readonly TextBlock _artist;
        private readonly Border _art;
        private readonly TextBlock _playPauseGlyph;
        private readonly StackPanel _sourcesRow;
        private readonly Grid _seekHost;
        private readonly Border _seekFill;
        private readonly Ellipse _seekThumb;
        private readonly Grid _timesRow;
        private readonly TextBlock _elapsed;
        private readonly TextBlock _total;
        private readonly Border _moreBtn;
        private readonly Border _skipBackBtn;
        private readonly Border _skipFwdBtn;
        private readonly Border _prevBtn;
        private readonly Border _playBtn;
        private readonly Border _nextBtn;

        private MediaInfo? _lastInfo;
        private byte[]? _logoData;
        private byte[]? _coverData;
        private BitmapImage? _cover;
        private bool _hasTimeline;
        private bool _canSeek;
        private bool _playing;
        private bool _seeking;
        private bool _menuOpen;
        private bool _showProgress = true;
        private TimeSpan _duration;
        private TimeSpan _basePos;
        private DateTimeOffset _baseStamp = DateTimeOffset.Now;
        private int _tickCount;
        private Point _dragStart;
        private bool _dragging;
        private bool _keepBottom; // "combined" + "above": Unterkante bleibt am Stapel

        private static SolidColorBrush Hex(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

        /// <param name="previewCfg">Nur für die Vorschau in den Einstellungen: diese (ungespeicherte) Config, nie anzeigen.</param>
        public MediaOsd(MediaController media, AppConfig? previewCfg = null)
        {
            _media = media;
            _previewCfg = previewCfg;
            var cfg = previewCfg ?? ConfigManager.Load();
            _radius = Math.Clamp(cfg.MediaCornerRadius, 0, 30);
            _win11 = cfg.MediaDesign == "win11";
            _custom = cfg.MediaDesign == "custom";
            _spec = Designs.Get(cfg.MediaDesign); // Design-System (z. B. Dynamic Island)
            _styleKey = StyleKey(cfg);
            // Eigenes Design: Farben aus der Config; hell/dunkel (Hover, Quellen-Pills, Menü) nach dem Hintergrund.
            string customBg = NormHex(cfg.MediaCustomBg, "#222222");
            _light = _custom ? IsLightHex(customBg) : ThemeHelper.IsLight(cfg);
            _pal = _spec?.Palette(_light);
            _flatButtons = _win11 || _spec != null || (_custom && cfg.MediaCustomButtons == "flat");
            if (_custom)
            {
                _buttonScale = Math.Clamp(cfg.MediaCustomButtonSize, 70, 140) / 100.0;
                _customButtonBg = Hex(NormHex(cfg.MediaCustomButtonBg, "#3a3a3a"));
                _hideSources = !cfg.MediaCustomShowSources;
            }
            if (_spec != null) _radius = _spec.MediaRadius; // Form des Designs statt Ecken-Einstellung

            // Windows 11: schwarz/weiß (Füllungen in Textfarbe), gedämpfte Flächen, 1-px-Rand.
            var fg = _pal != null ? Hex(_pal.Fg) : _custom ? Hex(NormHex(cfg.MediaCustomFg, "#ffffff")) : Hex(_light ? "#1b1b1b" : "#ffffff");
            var sub = _pal != null ? Hex(_pal.Sub) : _custom ? Hex(NormHex(cfg.MediaCustomSub, "#a0a0a6")) : Hex(_light ? "#5c5c5c" : _win11 ? "#c5c5c5" : "#a0a0a6");
            var accent = _accent = _pal != null ? Hex(_pal.Fill) : _custom ? Hex(NormHex(cfg.MediaCustomAccent, "#3b82f6")) : _win11 ? fg : Hex("#3b82f6");
            string cardHex = _cardHex = _pal != null ? NormHex(_pal.Bg, "#000000") : _custom ? customBg : _win11 ? (_light ? "#E9E9EB" : "#2C2C2C") : (_light ? "#EFEFEF" : "#222222");
            _fgBrush = fg;
            _subBrush = sub;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Width = W;
            SizeToContent = SizeToContent.Height;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            // Deckkraft (cfg.media_opacity) wirkt nur auf den Hintergrund, siehe ApplyBackground().
            WindowStartupLocation = WindowStartupLocation.Manual;
            var start = Placement.Corner("top_center", new Size(W, EstimatedHeight));
            Left = start.X;
            Top = start.Y;
            // Höhe ändert sich mit dem Inhalt (Quellen-Leiste, Fortschritt): oberhalb des Stapels nach oben wachsen.
            SizeChanged += (_, e) =>
            {
                if (_keepBottom && e.HeightChanged) Top -= e.NewSize.Height - e.PreviousSize.Height;
                KeepOnScreen(); // z.B. Quellen-Leiste erscheint und das Flyout würde unten herausragen
            };

            // ── Karte: Hintergrund (Farbe, optional Cover) + Inhalt, rund beschnitten ──
            var host = _host = new Grid();
            host.SizeChanged += (_, e) =>
            {
                host.Clip = CardClip(e.NewSize.Width, e.NewSize.Height);
                // Hintergrundbild immer exakt so groß wie die Karte (+ Überstand für den weichen Rand).
                _bgImage.Width = e.NewSize.Width + 80;
                _bgImage.Height = e.NewSize.Height + 80;
            };
            Content = _card = new Border
            {
                CornerRadius = new CornerRadius(_radius), // Standard 8 = exakt ModernFlyouts' FlyoutCornerRadius
                Background = Hex(cardHex),          // ModernFlyouts: exakt dessen FlyoutBackground
                BorderBrush = _pal != null ? Hex(_pal.Edge) : _win11 ? Hex(_light ? "#D0D0D4" : "#404040") : null,
                BorderThickness = new Thickness(_win11 || _pal != null ? 1 : 0),
                Child = host,
            };
            if (_spec != null)
            {
                // Design-System: beim Erscheinen aufziehen (siehe AppearSpec)
                _card.RenderTransformOrigin = new Point(0.5, 0.5);
                _card.RenderTransform = new ScaleTransform(1, 1);
            }

            _bgImage = new Image
            {
                Stretch = Stretch.UniformToFill,
                Visibility = Visibility.Collapsed,
                Effect = new BlurEffect { Radius = 28, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
            };
            // Ein Canvas meldet nie eine Wunschgröße: Das Bild darf die Kartenhöhe NICHT mitbestimmen
            // (vorher hat die Pixelgröße des Covers das Fenster über den ganzen Bildschirm gezogen,
            // weil sich die Fensterhöhe dem Inhalt anpasst). Größe/Position setzt host.SizeChanged.
            Canvas.SetLeft(_bgImage, -40); // Überstand, damit der weiche Rand der Unschärfe beschnitten wird
            Canvas.SetTop(_bgImage, -40);
            var bgLayer = new Canvas();
            bgLayer.Children.Add(_bgImage);
            _bgOverlay = new Border
            {
                Background = Hex((_light ? "#C8" : "#B8") + cardHex[1..]), // Abdunklung/Aufhellung für Lesbarkeit
                Visibility = Visibility.Collapsed,
            };
            host.Children.Add(bgLayer);
            host.Children.Add(_bgOverlay);

            var root = _content = new Grid { Margin = _spec != null ? new Thickness(18, 16, 18, 14) : _win11 ? new Thickness(16, 12, 16, 10) : new Thickness(14) };
            for (int i = 0; i < 6; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.Children.Add(root);

            // App-Symbol + Name (ModernFlyouts: über dem Titel, Windows 11: eigene Kopfzeile)
            var appRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _appIcon = new Image { Width = 16, Height = 16, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
            _appGlyph = new TextBlock { Text = "\uE8D6", FontFamily = IconFont, FontSize = 14, Foreground = sub, VerticalAlignment = VerticalAlignment.Center };
            _appName = new TextBlock { FontSize = 12, Foreground = _win11 ? fg : sub, Margin = new Thickness(_win11 ? 8 : 6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            appRow.Children.Add(_appIcon);
            appRow.Children.Add(_appGlyph);
            appRow.Children.Add(_appName);

            // "…"-Knopf: ModernFlyouts unten links in der Knopfreihe, Windows 11 oben rechts als kleines
            // Quadrat (wie der ">"-Knopf der Windows-Schnelleinstellungen).
            Brush moreBg = _customButtonBg ?? (_win11 ? Hex(_light ? "#0F000000" : "#14FFFFFF") : Hex(_light ? "#DADADA" : "#3a3a3a"));
            _moreBtn = new Border
            {
                Width = _win11 ? 30 : 34, Height = _win11 ? 30 : 28, CornerRadius = new CornerRadius(4),
                HorizontalAlignment = _win11 ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
                Background = moreBg,
                Child = new TextBlock
                {
                    Text = "\uE712", FontFamily = IconFont, FontSize = _win11 ? 12 : 14, Foreground = fg,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            if (_win11 || _customButtonBg != null)
            {
                var moreHover = _customButtonBg != null ? Shade(_customButtonBg, 0.12) : Hex(_light ? "#1F000000" : "#26FFFFFF");
                _moreBtn.MouseEnter += (_, _) => _moreBtn.Background = moreHover;
                _moreBtn.MouseLeave += (_, _) => _moreBtn.Background = moreBg;
            }
            _moreBtn.MouseLeftButtonDown += (_, e) => e.Handled = true;
            _moreBtn.MouseLeftButtonUp += (_, e) => { ShowMoreMenu(); e.Handled = true; };

            // ── Zeile 0 (nur Windows 11): App links, "…" rechts ──
            if (_win11)
            {
                var topBar = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                topBar.Children.Add(appRow);
                Grid.SetColumn(_moreBtn, 1);
                topBar.Children.Add(_moreBtn);
                Grid.SetRow(topBar, 0);
                root.Children.Add(topBar);
            }

            // ── Zeile 1: Titel/Interpret links (ModernFlyouts: mit App darüber), Cover rechts ──
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(header, 1);
            root.Children.Add(header);

            var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = _win11 ? VerticalAlignment.Center : VerticalAlignment.Top };
            header.Children.Add(left);
            if (!_win11) left.Children.Add(appRow);

            _title = new TextBlock { Foreground = fg, FontSize = _custom ? Math.Clamp(cfg.MediaCustomTitleSize, 11, 22) : 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, _win11 ? 0 : 10, 0, 0) };
            _artist = new TextBlock { Foreground = sub, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
            left.Children.Add(_title);
            left.Children.Add(_artist);

            // Windows 11: flaches Vorschaubild wie in den Schnelleinstellungen, ModernFlyouts: quadratisches Cover.
            double artW = _win11 ? 72 : 64, artH = _win11 ? 42 : 64;
            if (_custom) artW = artH = Math.Clamp(cfg.MediaCustomCoverSize, 32, 140);
            _art = new Border
            {
                Width = artW, Height = artH, CornerRadius = new CornerRadius(4),
                VerticalAlignment = _win11 ? VerticalAlignment.Center : VerticalAlignment.Top,
                Background = Hex(_light ? "#D6D6D6" : "#404048"),
                Child = MusicGlyph(),
                Clip = new RectangleGeometry(new Rect(0, 0, artW, artH), 4, 4),
            };
            Grid.SetColumn(_art, 1);
            header.Children.Add(_art);

            // ── Zeile 2: Fortschrittsleiste (anklickbar/ziehbar zum Springen) ──
            _seekHost = new Grid { Height = 22, Background = Brushes.Transparent, Margin = new Thickness(0, 10, 0, 0) };
            double trackH = ReferenceEquals(_spec, Designs.Island) ? 6 : 4; // Dynamic Island: dickere Kapsel-Leiste
            _seekHost.Children.Add(new Border
            {
                Height = trackH, CornerRadius = new CornerRadius(trackH / 2), VerticalAlignment = VerticalAlignment.Center,
                Background = _pal != null ? Hex(_pal.Track) : _custom ? Hex(NormHex(cfg.MediaCustomTrack, "#5a5a5a")) : Hex(_light ? "#BDBDBD" : "#5a5a5a"),
            });
            _seekFill = new Border
            {
                Height = trackH, Width = 0, CornerRadius = new CornerRadius(trackH / 2), VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left, Background = accent,
            };
            _seekHost.Children.Add(_seekFill);
            _seekThumb = new Ellipse
            {
                Width = 14, Height = 14, Fill = accent, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            if (_win11)
            {
                // Windows-11-Slider-Griff: Akzentpunkt mit hellem/dunklem Ring.
                _seekThumb.Width = _seekThumb.Height = 16;
                _seekThumb.Stroke = Hex(_light ? "#FFFFFF" : "#454545");
                _seekThumb.StrokeThickness = 4;
            }
            if (_spec != null)
            {
                // Design-System: Leiste ohne Griff; der Griff erscheint nur beim Überfahren bzw. Ziehen.
                _seekThumb.Opacity = 0;
                _seekHost.MouseEnter += (_, _) => _seekThumb.Opacity = 1;
                _seekHost.MouseLeave += (_, _) => { if (!_seeking) _seekThumb.Opacity = 0; };
            }
            _seekHost.Children.Add(_seekThumb);
            _seekHost.MouseLeftButtonDown += OnSeekDown;
            _seekHost.MouseMove += OnSeekMove;
            _seekHost.MouseLeftButtonUp += OnSeekUp;
            _seekHost.SizeChanged += (_, _) => RefreshSeek();
            Grid.SetRow(_seekHost, 2);
            root.Children.Add(_seekHost);

            // ── Zeile 3: vergangene Zeit links, Gesamtdauer rechts ──
            _timesRow = new Grid();
            _elapsed = new TextBlock { FontSize = 11, Foreground = sub, HorizontalAlignment = HorizontalAlignment.Left };
            _total = new TextBlock { FontSize = 11, Foreground = sub, HorizontalAlignment = HorizontalAlignment.Right };
            _timesRow.Children.Add(_elapsed);
            _timesRow.Children.Add(_total);
            Grid.SetRow(_timesRow, 3);
            root.Children.Add(_timesRow);

            // ── Zeile 4: Quellen-Leiste, nur sichtbar, wenn mehrere Quellen eine Mediensession haben ──
            _sourcesRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            Grid.SetRow(_sourcesRow, 4);
            root.Children.Add(_sourcesRow);

            // ── Zeile 5: Zurück / Play-Pause / Weiter zentriert (ModernFlyouts: "…" links daneben) ──
            var controls = new Grid { Margin = new Thickness(0, _win11 ? 10 : 6, 0, 0) };
            Grid.SetRow(controls, 5);
            root.Children.Add(controls);

            var center = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            // Reihenfolge: Zurück | -10 s | Play/Pause | +10 s | Weiter - die ±10-s-Knöpfe sitzen direkt
            // neben Play/Pause und sind per Einstellung ausblendbar (siehe UpdateFrom).
            // Design-System: gefüllte Symbole (PreviousSolid/NextSolid, Play/PauseSolid)
            _prevBtn = MakeButton(_spec != null ? "\uF8AC" : "\uE892", 38, false, () => { _media.Previous(); ResetHideTimer(); }, out _);
            center.Children.Add(_prevBtn);
            _skipBackBtn = MakeButton("\uED3C", 34, false, () => SkipLocal(-1), out _);
            center.Children.Add(_skipBackBtn);
            _playBtn = MakeButton("\uE768", 46, true, () => { _media.TogglePlayPause(); ResetHideTimer(); }, out _playPauseGlyph);
            center.Children.Add(_playBtn);
            _skipFwdBtn = MakeButton("\uED3D", 34, false, () => SkipLocal(+1), out _);
            center.Children.Add(_skipFwdBtn);
            _nextBtn = MakeButton(_spec != null ? "\uF8AD" : "\uE893", 38, false, () => { _media.Next(); ResetHideTimer(); }, out _);
            center.Children.Add(_nextBtn);
            controls.Children.Add(center);
            if (!_win11) controls.Children.Add(_moreBtn);
            if (_custom) ArrangeCustom(cfg, root, header, left, appRow, controls, center);
            if (IsMac) ArrangeMac(root, header, left, appRow, controls, center);
            else if (IsMaterial) ArrangeMaterial(root, header, left, appRow, controls, center);
            else if (IsHud) ArrangeHud(root, header, left, appRow, controls, center);
            else if (IsHardware) ArrangeHardware(root, header, left, appRow, controls, center);
            else if (_spec != null) ArrangeSpec(root, header, left, appRow, controls, center);

            // ── Timer ──
            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            _hideTimer.Tick += (_, _) =>
            {
                if (_seeking || _menuOpen || _pinned) return; // nicht mitten im Springen/Menü bzw. angeheftet ausblenden
                _hideTimer.Stop();
                if (_spec != null) FadeOutThenHide(); // Design-System: weich ausblenden (HideMs; macOS langsam)
                else Hide();
            };

            _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _tickTimer.Tick += OnTick;
            IsVisibleChanged += (_, _) =>
            {
                if (IsVisible) { _tickCount = 0; _tickTimer.Start(); }
                else _tickTimer.Stop();
                UpdateWave(_playing); // Wellenform nur animieren, solange das Flyout sichtbar ist
            };

            // Ziehen nur im "separate"-Modus; Buttons verarbeiten ihr MouseDown selbst
            // (e.Handled), lösen dadurch dieses Drag nicht aus.
            MouseLeftButtonDown += OnMouseDown;
            MouseLeftButtonUp += OnMouseUp;
            MouseMove += OnMouseMove;

            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            };
        }

        private static TextBlock MusicGlyph() => new()
        {
            Text = "\uE8D6", FontFamily = IconFont, FontSize = 22,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8a, 0x8a, 0x92)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };

        /// <summary>
        /// Knopf im Original-Stil: Zurück/Weiter nur als Symbol (beim Überfahren leicht hinterlegt),
        /// Play/Pause als Kreis mit Rand. Fängt auch MouseLeftButtonDown ab, sonst greift das
        /// Fenster-weite Drag (separate-Modus) die Maus zuerst und der Klick läuft ins Leere.
        /// </summary>
        private Border MakeButton(string glyph, double size, bool filled, Action onClick, out TextBlock glyphBlock)
        {
            // Windows 11 (bzw. eigenes Design „flach“): alle Knöpfe flach (nur Hover-Fläche), eckig gerundet statt Kreis.
            bool isPlay = filled;
            if (_flatButtons) filled = false;
            size *= _buttonScale;
            // Design-System: reine Symbole mit runder Hover-Fläche, Play größer (48) als die übrigen (40).
            if (_spec != null) size = isPlay ? 48 : 40;
            Brush normalBg = filled ? _customButtonBg ?? Hex(_light ? "#D4D4D4" : "#3a3a3a") : Brushes.Transparent;
            Brush hoverBg = filled
                ? (_customButtonBg != null ? Shade(_customButtonBg, 0.12) : Hex(_light ? "#C6C6C6" : "#4a4a4a"))
                : _pal != null ? Hex(_pal.Button) : Hex(_light ? "#14000000" : "#1AFFFFFF");

            var border = new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(_flatButtons && _spec == null ? 4 : size / 2),
                Background = normalBg,
                BorderBrush = filled ? (_customButtonBg != null ? Shade(_customButtonBg, 0.2) : Hex(_light ? "#B4B4B4" : "#5c5c5c")) : Brushes.Transparent,
                BorderThickness = new Thickness(filled ? 1 : 0),
                // Win11: weiter auseinander wie in den Schnelleinstellungen
                Margin = _spec != null ? new Thickness(3, 0, 3, 0)
                    : _win11 ? new Thickness(10, 0, 10, 0) : _flatButtons ? new Thickness(6, 0, 6, 0) : new Thickness(4, 0, 4, 0),
                Cursor = Cursors.Hand,
            };
            glyphBlock = new TextBlock
            {
                Text = glyph, FontFamily = IconFont, FontSize = size * (_spec != null ? (isPlay ? 0.54 : 0.45) : 0.36),
                Foreground = _custom || _spec != null ? _fgBrush : Hex(_light ? "#1b1b1b" : "#ffffff"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            border.Child = glyphBlock;
            border.MouseEnter += (_, _) => border.Background = hoverBg;
            border.MouseLeave += (_, _) => border.Background = normalBg;
            border.MouseLeftButtonDown += (_, e) => e.Handled = true;
            border.MouseLeftButtonUp += (_, e) => { onClick(); e.Handled = true; };
            return border;
        }

        /// <summary>True, wenn sich Farbschema, Media-Design oder eine Einstellung des eigenen Designs seit dem Aufbau dieses Fensters geändert hat (dann neu aufbauen).</summary>
        public bool StyleChanged => StyleKey(ConfigManager.Load()) != _styleKey;

        private static string StyleKey(AppConfig c) => c.MediaDesign == "custom"
            ? string.Join("|", "custom", string.Join(",", c.MediaCustomOrder), c.MediaCustomShowApp, c.MediaCustomShowTimes,
                c.MediaCustomCover, c.MediaCustomCoverSize, c.MediaCustomButtons, c.MediaCustomButtonsAlign, c.MediaCustomButtonSize,
                c.MediaCustomMorePos, c.MediaCustomTitleSize, c.MediaCustomBg, c.MediaCustomFg, c.MediaCustomSub,
                c.MediaCustomAccent, c.MediaCustomButtonBg, c.MediaCustomTrack,
                c.MediaCustomShowTitle, c.MediaCustomShowArtist, c.MediaCustomShowSources,
                c.MediaCustomShowPrev, c.MediaCustomShowPlay, c.MediaCustomShowNext,
                // entscheiden mit, ob die Knopfreihe ganz wegfällt (ArrangeCustom)
                c.MediaShowMore, c.MediaSkipButtons)
            : c.MediaDesign + "|" + ThemeHelper.IsLight(c);

        /// <summary>Bereiche des eigenen Designs in gültiger Reihenfolge: jeder genau einmal, fehlende hinten angehängt.</summary>
        public static List<string> NormalizeOrder(IEnumerable<string>? order)
        {
            string[] all = { "app", "info", "progress", "sources", "controls" };
            var result = (order ?? Array.Empty<string>()).Where(all.Contains).Distinct().ToList();
            result.AddRange(all.Where(k => !result.Contains(k)));
            return result;
        }

        /// <summary>
        /// Eigenes Design: die fertig gebauten Bereiche aus dem Standardaufbau lösen und in der eingestellten
        /// Reihenfolge untereinander setzen – Cover links/rechts/oben/aus, Knöpfe links/mittig/rechts,
        /// „…“ rechts in der App-Zeile oder bei den Knöpfen, App-Zeile und Zeiten optional.
        /// </summary>
        private void ArrangeCustom(AppConfig cfg, Grid root, Grid header, StackPanel left, StackPanel appRow, Grid controls, StackPanel center)
        {
            root.Children.Clear();
            root.RowDefinitions.Clear();
            header.Children.Clear();
            left.Children.Clear();
            controls.Children.Clear();
            var stack = new StackPanel();
            root.Children.Add(stack);

            bool moreTop = cfg.MediaCustomMorePos == "top" && cfg.MediaCustomShowApp;

            // App-Zeile (optional mit „…“ rechts)
            var appBlock = new Grid();
            appBlock.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            appBlock.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            appBlock.Children.Add(appRow);
            if (moreTop)
            {
                _moreBtn.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(_moreBtn, 1);
                appBlock.Children.Add(_moreBtn);
            }

            // Titel/Interpret mit Cover (jedes Element einzeln abschaltbar)
            _title.Margin = new Thickness(0);
            _title.Visibility = cfg.MediaCustomShowTitle ? Visibility.Visible : Visibility.Collapsed;
            _artist.Visibility = cfg.MediaCustomShowArtist ? Visibility.Visible : Visibility.Collapsed;
            if (!cfg.MediaCustomShowTitle) _artist.Margin = new Thickness(0);
            bool hasText = cfg.MediaCustomShowTitle || cfg.MediaCustomShowArtist;
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(_title);
            text.Children.Add(_artist);
            _art.VerticalAlignment = VerticalAlignment.Center;
            FrameworkElement? info;
            if (!hasText)
            {
                // Nur das Cover (oder gar nichts): ohne leere Textspalte, auf der gewählten Seite
                _art.HorizontalAlignment = cfg.MediaCustomCover switch
                {
                    "left" => HorizontalAlignment.Left,
                    "right" => HorizontalAlignment.Right,
                    _ => HorizontalAlignment.Center,
                };
                info = cfg.MediaCustomCover == "none" ? null : _art;
            }
            else switch (cfg.MediaCustomCover)
            {
                case "none":
                    info = text;
                    break;
                case "top":
                {
                    var panel = new StackPanel();
                    _art.HorizontalAlignment = HorizontalAlignment.Center;
                    panel.Children.Add(_art);
                    text.Margin = new Thickness(0, 10, 0, 0);
                    panel.Children.Add(text);
                    info = panel;
                    break;
                }
                case "left":
                {
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    Grid.SetColumn(_art, 0); // stand im Standardaufbau in Spalte 1
                    grid.Children.Add(_art);
                    text.Margin = new Thickness(12, 0, 0, 0);
                    Grid.SetColumn(text, 1);
                    grid.Children.Add(text);
                    info = grid;
                    break;
                }
                default: // "right"
                {
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    text.Margin = new Thickness(0, 0, 12, 0);
                    grid.Children.Add(text);
                    Grid.SetColumn(_art, 1);
                    grid.Children.Add(_art);
                    info = grid;
                    break;
                }
            }

            // Fortschritt (Leiste + optional Zeiten); wird mit der Leiste ein-/ausgeblendet (RefreshSeek)
            var progress = new StackPanel();
            _seekHost.Margin = new Thickness(0);
            progress.Children.Add(_seekHost);
            if (cfg.MediaCustomShowTimes) progress.Children.Add(_timesRow);
            _progressBlock = progress;

            // Quellen-Leiste: ohne mehrere Quellen ganz weg (RenderSources), damit kein leerer Abstand bleibt
            _sourcesRow.Margin = new Thickness(0);

            // Knöpfe (einzeln abschaltbar; ±10 s und „…“ blendet UpdateFrom nach den allgemeinen Schaltern aus); „…“ auf der freien Seite
            _prevBtn.Visibility = cfg.MediaCustomShowPrev ? Visibility.Visible : Visibility.Collapsed;
            _playBtn.Visibility = cfg.MediaCustomShowPlay ? Visibility.Visible : Visibility.Collapsed;
            _nextBtn.Visibility = cfg.MediaCustomShowNext ? Visibility.Visible : Visibility.Collapsed;
            bool hasButtons = cfg.MediaCustomShowPrev || cfg.MediaCustomShowPlay || cfg.MediaCustomShowNext
                || cfg.MediaSkipButtons || (cfg.MediaShowMore && !moreTop);
            controls.Margin = new Thickness(0);
            center.HorizontalAlignment = cfg.MediaCustomButtonsAlign switch
            {
                "left" => HorizontalAlignment.Left,
                "right" => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Center,
            };
            controls.Children.Add(center);
            if (!moreTop)
            {
                _moreBtn.HorizontalAlignment = cfg.MediaCustomButtonsAlign == "left" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                Grid.SetColumn(_moreBtn, 0);
                controls.Children.Add(_moreBtn);
            }

            var blocks = new Dictionary<string, FrameworkElement?>
            {
                ["app"] = appBlock, ["info"] = info, ["progress"] = progress, ["sources"] = _sourcesRow,
                ["controls"] = hasButtons ? controls : null,
            };
            foreach (var key in NormalizeOrder(cfg.MediaCustomOrder))
            {
                if (key == "app" && !cfg.MediaCustomShowApp) continue;
                if (blocks[key] is not { } block) continue; // Bereich ohne sichtbare Elemente: samt Abstand weglassen
                block.Margin = new Thickness(0, stack.Children.Count == 0 ? 0 : 10, 0, 0);
                stack.Children.Add(block);
            }
        }

        /// <summary>
        /// Design-System (Dynamic Island): aufgeklappte Insel – oben Cover mit App-Logo als Abzeichen · Titel und
        /// „Interpret · App“ · Wellenform in Cover-Farbe; darunter Zeit · Leiste · Restzeit; dann −10 · Zurück ·
        /// Play/Pause · Weiter · +10 mit „…“ rechts; zuletzt die Quellen-Pills (nur bei mehreren Quellen).
        /// Wie ArrangeCustom: die fertigen Elemente aus dem Standardaufbau lösen und neu anordnen.
        /// </summary>
        private void ArrangeSpec(Grid root, Grid header, StackPanel left, StackPanel appRow, Grid controls, StackPanel center)
        {
            var pal = _pal!;
            var font = _spec!.Font;
            root.Children.Clear();
            root.RowDefinitions.Clear();
            header.Children.Clear();
            left.Children.Clear();
            appRow.Children.Clear();
            controls.Children.Clear();
            _timesRow.Children.Clear();
            var stack = new StackPanel();
            root.Children.Add(stack);

            // ── Kopf: Cover (+ Abzeichen) | Titel / Interpret · App | Wellenform ──
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            const double art = 52;
            _art.Width = _art.Height = art;
            _art.CornerRadius = new CornerRadius(12);
            _art.Clip = new RectangleGeometry(new Rect(0, 0, art, art), 12, 12);
            _art.Background = Hex(pal.ArtPlaceholder);
            _art.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_art, 0);
            var artHost = new Grid { Width = art, Height = art, VerticalAlignment = VerticalAlignment.Center };
            artHost.Children.Add(_art);
            _appIcon.Width = _appIcon.Height = 13;
            _appIcon.HorizontalAlignment = HorizontalAlignment.Center;
            _appGlyph.FontSize = 11;
            _appGlyph.Foreground = _fgBrush;
            _appGlyph.HorizontalAlignment = HorizontalAlignment.Center;
            // Logo rund beschnitten (App-Logos sind meist quadratisch); Rand in Kartenfarbe trennt es vom Cover.
            var badgeContent = new Grid { Width = 18, Height = 18, Clip = new EllipseGeometry(new Point(9, 9), 9, 9) };
            badgeContent.Children.Add(_appIcon);
            badgeContent.Children.Add(_appGlyph);
            artHost.Children.Add(new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Hex(pal.ArtPlaceholder),
                BorderBrush = Hex(_cardHex), BorderThickness = new Thickness(2), Child = badgeContent,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -6, -6),
            });
            top.Children.Add(artHost);

            _title.FontFamily = font;
            _title.FontSize = 15;
            _title.Margin = new Thickness(0);
            _artist.FontFamily = font;
            _artist.FontSize = 13;
            _artist.Margin = new Thickness(0, 1, 0, 0);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
            text.Children.Add(_title);
            text.Children.Add(_artist);
            Grid.SetColumn(text, 1);
            top.Children.Add(text);

            _waveBrush.Color = _fgBrush.Color; // ohne Cover in Textfarbe (mit Cover setzt UpdateFrom die Cover-Farbe)
            var wave = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Height = 20 };
            for (int i = 0; i < 5; i++)
            {
                var scale = new ScaleTransform(1, 0.3);
                wave.Children.Add(new Rectangle
                {
                    Width = 3, Height = 20, RadiusX = 1.5, RadiusY = 1.5, Fill = _waveBrush, Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0),
                    RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale,
                });
                _waveBars.Add(scale);
            }
            Grid.SetColumn(wave, 2);
            top.Children.Add(wave);
            stack.Children.Add(top);

            // ── Fortschritt: Zeit · Leiste · Restzeit in einer Reihe ──
            var progress = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            progress.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            progress.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            progress.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foreach (var t in new[] { _elapsed, _total })
            {
                t.FontFamily = font;
                t.FontSize = 12;
                t.VerticalAlignment = VerticalAlignment.Center;
                System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            }
            _elapsed.Margin = new Thickness(0, 0, 10, 0);
            _total.Margin = new Thickness(10, 0, 0, 0);
            _seekHost.Margin = new Thickness(0);
            Grid.SetRow(_seekHost, 0);
            Grid.SetColumn(_seekHost, 1);
            Grid.SetColumn(_total, 2);
            progress.Children.Add(_elapsed);
            progress.Children.Add(_seekHost);
            progress.Children.Add(_total);
            _progressBlock = progress;
            stack.Children.Add(progress);

            // ── Knöpfe: −10 · Zurück · Play/Pause · Weiter · +10, „…“ rechts ──
            var buttons = center.Children.Cast<UIElement>().ToArray(); // Standard: Zurück, −10, Play, +10, Weiter
            center.Children.Clear();
            foreach (int i in new[] { 1, 0, 2, 4, 3 }) center.Children.Add(buttons[i]);
            SpecSkipLabel(_skipBackBtn, "−10");
            SpecSkipLabel(_skipFwdBtn, "+10");
            controls.Margin = new Thickness(0, 6, 0, 0);
            Grid.SetRow(controls, 0);
            controls.Children.Add(center);
            _moreBtn.Width = _moreBtn.Height = 30;
            _moreBtn.CornerRadius = new CornerRadius(15);
            _moreBtn.Background = Hex(pal.Button);
            _moreBtn.HorizontalAlignment = HorizontalAlignment.Right;
            if (_moreBtn.Child is TextBlock dots) dots.FontSize = 13;
            _moreBtn.MouseEnter += (_, _) => _moreBtn.Opacity = 0.8;
            _moreBtn.MouseLeave += (_, _) => _moreBtn.Opacity = 1.0;
            Grid.SetColumn(_moreBtn, 0);
            controls.Children.Add(_moreBtn);
            stack.Children.Add(controls);

            // ── Quellen-Pills (RenderSources blendet die Reihe ohne mehrere Quellen ganz aus) ──
            _sourcesRow.Margin = new Thickness(0, 10, 0, 0);
            Grid.SetRow(_sourcesRow, 0);
            stack.Children.Add(_sourcesRow);
        }

        /// <summary>±10-s-Knopf als Text „−10“/„+10“ (es gibt kein Fluent-Symbol für +10; ED3D zeigt „30“).</summary>
        private void SpecSkipLabel(Border button, string text)
        {
            if (button.Child is not TextBlock t) return;
            t.Text = text;
            t.FontFamily = _spec!.Font;
            t.FontSize = 13;
            t.FontWeight = FontWeights.Bold;
            t.Foreground = _subBrush;
        }

        /// <summary>Design-System: Aufziehen und Einblenden beim Erscheinen (ohne Überschwingen, das Fenster würde abschneiden).</summary>
        private void AppearSpec()
        {
            if (_card.RenderTransform is not ScaleTransform st) return;
            var dur = TimeSpan.FromMilliseconds(_spec!.AppearMs);
            var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 };
            double from = _spec.MediaAppearFromScale;
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(from, 1, dur) { EasingFunction = ease });
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(_spec.MediaAppearFromScaleY ?? from, 1, dur) { EasingFunction = ease });
            _card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        }

        /// <summary>
        /// Wellenform: 5 Balken schwingen versetzt, solange abgespielt wird UND das Flyout sichtbar ist (sonst
        /// keine Animation im Hintergrund); pausiert flach, in der Vorschau als feste Momentaufnahme.
        /// </summary>
        private void UpdateWave(bool playing)
        {
            UpdateMaterialWave(playing); // Material You: wellige Fortschrittsleiste
            UpdateHudStatus(playing);    // Gaming-HUD: „LÄUFT“/„PAUSE“
            UpdateHwStatus(playing);     // Hardware: Symbol im Display, LED auf der Play-Taste
            if (_waveBars.Count == 0) return;
            bool run = playing && IsVisible;
            double[] still = { 0.5, 0.9, 0.65, 1.0, 0.45 };
            for (int i = 0; i < _waveBars.Count; i++)
            {
                var st = _waveBars[i];
                if (run && !_waveRunning)
                {
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.25, 1.0, TimeSpan.FromMilliseconds(380 + 90 * (i * 3 % 5)))
                    {
                        AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(70 * i),
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    });
                }
                else if (!run)
                {
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    st.ScaleY = playing ? still[i] : 0.25;
                }
            }
            _waveRunning = run;
        }

        /// <summary>Kräftigste Farbe des Covers (für die Wellenform), für Kontrast zum Hintergrund aufgehellt/abgedunkelt.</summary>
        private Color CoverAccent(BitmapSource? cover)
        {
            var fallback = _fgBrush.Color;
            if (cover == null || cover.PixelWidth == 0 || cover.PixelHeight == 0) return fallback;
            try
            {
                var small = new TransformedBitmap(cover, new ScaleTransform(12.0 / cover.PixelWidth, 12.0 / cover.PixelHeight));
                var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
                int w = bgra.PixelWidth, h = bgra.PixelHeight;
                var px = new byte[w * h * 4];
                bgra.CopyPixels(px, w * 4, 0);

                double best = -1;
                var pick = fallback;
                for (int i = 0; i + 3 < px.Length; i += 4)
                {
                    byte b = px[i], g = px[i + 1], r = px[i + 2];
                    double max = Math.Max(r, Math.Max(g, b)) / 255.0, min = Math.Min(r, Math.Min(g, b)) / 255.0;
                    double score = (max <= 0 ? 0 : (max - min) / max) * 0.7 + max * 0.3; // satt und nicht zu dunkel
                    if (score > best) { best = score; pick = Color.FromRgb(r, g, b); }
                }
                double lum = (0.299 * pick.R + 0.587 * pick.G + 0.114 * pick.B) / 255.0;
                if (!_light && lum < 0.5) pick = Mix(pick, Colors.White, 0.65 - lum);
                if (_light && lum > 0.55) pick = Mix(pick, Colors.Black, lum - 0.4);
                return pick;
            }
            catch
            {
                return fallback;
            }
        }

        private static Color Mix(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            byte M(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
            return Color.FromRgb(M(a.R, b.R), M(a.G, b.G), M(a.B, b.B));
        }

        /// <summary>Farbe als "#RRGGBB" (ohne Alpha; Deckkraft regelt media_opacity), bei ungültigem Wert fallback.</summary>
        private static string NormHex(string? hex, string fallback)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex ?? fallback);
                return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }
            catch
            {
                return fallback;
            }
        }

        private static bool IsLightHex(string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0 > 0.55;
        }

        /// <summary>Etwas heller (dunkle Farbe) bzw. dunkler (helle Farbe) – für Hover und Rand eigener Knöpfe.</summary>
        private static SolidColorBrush Shade(SolidColorBrush b, double amount)
        {
            var c = b.Color;
            bool light = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0 > 0.55;
            byte Mix(byte v) => (byte)Math.Round(light ? v * (1 - amount) : v + (255 - v) * amount);
            return new SolidColorBrush(Color.FromRgb(Mix(c.R), Mix(c.G), Mix(c.B)));
        }

        public void UpdateFrom(MediaInfo info)
        {
            _lastInfo = info;
            _title.Text = info.Title.Length > 0 ? info.Title : Loc.T("(Unbekannter Titel)", "(Unknown title)");
            // Design-System: App-Name hinter dem Interpreten (das Logo sitzt als Abzeichen am Cover).
            _artist.Text = ReferenceEquals(_spec, Designs.Island) && info.AppName.Length > 0 // andere Designs: App in eigener Zeile
                ? (info.Artist.Length > 0 ? $"{info.Artist} · {info.AppName}" : info.AppName)
                : info.Artist;
            _appName.Text = info.AppName;
            if (IsHud) HudTexts(info); // Gaming-HUD: Großbuchstaben mit Sperrung
            if (IsHardware) HwTexts(info); // Hardware: Display in Großbuchstaben
            _playPauseGlyph.Text = _spec != null
                ? (info.IsPlaying ? "\uF8AE" : "\uF5B0")  // PauseSolid : PlaySolid
                : (info.IsPlaying ? "\uE769" : "\uE768"); // Pause : Play

            // Logo kommt aus dem Cache des MediaControllers (dasselbe Array) - nur bei Wechsel neu dekodieren.
            if (!ReferenceEquals(info.AppLogo, _logoData))
            {
                _logoData = info.AppLogo;
                var logo = DecodeImage(info.AppLogo);
                _appIcon.Source = logo;
                _appIcon.Visibility = logo != null ? Visibility.Visible : Visibility.Collapsed;
                _appGlyph.Visibility = logo != null ? Visibility.Collapsed : Visibility.Visible;
            }

            // Cover wird bei jeder Änderung neu gelesen (z.B. Play/Pause) - nur bei anderem Inhalt neu dekodieren/einsetzen.
            if (!SameBytes(info.Thumbnail, _coverData))
            {
                _coverData = info.Thumbnail;
                _cover = DecodeImage(info.Thumbnail);
                _art.Child = _cover != null
                    ? new Image { Source = _cover, Stretch = Stretch.UniformToFill }
                    : MusicGlyph();
                if (_waveBars.Count > 0) _waveBrush.Color = CoverAccent(_cover); // Wellenform in Cover-Farbe
            }
            var cover = _cover;

            var cfg = _previewCfg ?? ConfigManager.Load();
            _showProgress = cfg.MediaShowProgress;

            // Ecken (eckig/rund) laut Einstellung - auch ohne Neuaufbau des Fensters.
            _radius = _spec?.MediaRadius ?? Math.Clamp(cfg.MediaCornerRadius, 0, 30); // Design-System: feste Form
            _card.CornerRadius = new CornerRadius(_radius);
            if (_host.ActualWidth > 0)
                _host.Clip = CardClip(_host.ActualWidth, _host.ActualHeight);
            _hideTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(cfg.MediaDurationMs, 1000, 60000));
            _moreBtn.Visibility = cfg.MediaShowMore ? Visibility.Visible : Visibility.Collapsed;

            bool useBackground = cover != null && cfg.MediaArtBackground;
            if (IsMaterial) ApplyMaterial(cover, useBackground); // Farben aus dem Cover (vor dem Kartenhintergrund)
            _bgImage.Source = useBackground ? cover : null;
            _bgImage.Visibility = _bgOverlay.Visibility = useBackground ? Visibility.Visible : Visibility.Collapsed;
            // Deckkraft: media_opacity für den Hintergrund (Fläche, Rand, Cover-Hintergrund), media_content_opacity für den Inhalt.
            double opacity = Math.Clamp(cfg.MediaOpacity, 0, 100) / 100.0;
            _content.Opacity = Math.Clamp(cfg.MediaContentOpacity, 20, 100) / 100.0;
            _card.Background = _pal != null ? _pal.Background(opacity) : Hex($"#{(byte)Math.Round(255 * opacity):X2}{_cardHex[1..]}");
            if (IsHud) ApplyHudBackground(opacity); // abgeschrägte Fläche statt Rechteck
            if (_win11)
                _card.BorderBrush = Hex($"#{(byte)Math.Round(255 * opacity):X2}{(_light ? "D0D0D4" : "404040")}");
            _bgImage.Opacity = opacity;
            if (useBackground)
            {
                // Unschärfe und Abdunkelung (bzw. Aufhellung im hellen Schema) laut Einstellungen.
                if (_bgImage.Effect is BlurEffect blur) blur.Radius = Math.Clamp(cfg.MediaArtBlur, 0, 60);
                byte a = (byte)Math.Round(Math.Clamp(cfg.MediaArtDim, 0, 100) * 2.55 * opacity);
                _bgOverlay.Background = Hex($"#{a:X2}{_cardHex[1..]}");
            }

            if (!_seeking)
            {
                _hasTimeline = info.HasTimeline && info.Duration > TimeSpan.Zero;
                _canSeek = info.CanSeek;
                _duration = info.Duration;
                _basePos = info.Position;
                _baseStamp = info.PositionStamp;
            }
            _playing = info.IsPlaying;
            _seekHost.Cursor = _canSeek ? Cursors.Hand : Cursors.Arrow;
            _skipBackBtn.Visibility = _skipFwdBtn.Visibility = cfg.MediaSkipButtons ? Visibility.Visible : Visibility.Collapsed;
            if (IsMaterial) UpdateMaterialGroup(); // Knopfgruppe: Rundungen/Breiten an sichtbare Knöpfe anpassen
            _skipBackBtn.Opacity = _skipFwdBtn.Opacity = _canSeek ? 1.0 : 0.4; // Quelle kann nicht springen → abgedunkelt
            RefreshSeek();

            RenderSources();

            if (_previewCfg != null)
            {
                UpdateWave(_playing);
                return; // Vorschau: nie als Fenster anzeigen
            }
            CancelFadeOut(); // neue Meldung während des Verblassens: wieder voll sichtbar
            bool appearing = !IsVisible;
            if (appearing) Show();
            if (appearing && _spec != null) AppearSpec();
            UpdateWave(_playing);
            ResetHideTimer();
        }

        private static MediaInfo SampleInfo() => new()
        {
            Title = Loc.T("Beispiel-Titel", "Sample title"),
            Artist = Loc.T("Interpret", "Artist"),
            AppName = "Spotify",
            IsPlaying = true,
            HasTimeline = true,
            CanSeek = true,
            Duration = TimeSpan.FromSeconds(210),
            Position = TimeSpan.FromSeconds(72),
            PositionStamp = DateTimeOffset.Now,
        };

        /// <summary>Flyout-Inhalt mit Beispieldaten für die Vorschau in den Einstellungen (Fenster wird verworfen).</summary>
        public static FrameworkElement CreatePreview(AppConfig cfg)
        {
            using var media = new MediaController(); // nicht initialisiert: keine Quellen, Knöpfe ohne Wirkung
            var w = new MediaOsd(media, cfg);
            w.UpdateFrom(SampleInfo());
            var content = (FrameworkElement)w.Content;
            w.Content = null;
            content.Width = W;
            w.Close();
            return content;
        }

        /// <summary>"Testen" in den Einstellungen: Flyout mit Beispieldaten (noch nicht angezeigt, siehe ShowTestAt).</summary>
        public static MediaOsd CreateTest(AppConfig cfg)
        {
            var media = new MediaController(); // nicht initialisiert: keine Quellen, Knöpfe ohne Wirkung
            var w = new MediaOsd(media, cfg);
            w.UpdateFrom(SampleInfo());
            w.Closed += (_, _) => media.Dispose();
            return w;
        }

        /// <summary>Höhe des Inhalts vor dem ersten Anzeigen (für die genaue Platzierung beim Testen).</summary>
        public double MeasureHeight()
        {
            if (Content is not FrameworkElement c) return EstimatedHeight;
            c.Measure(new Size(W, double.PositiveInfinity));
            return c.DesiredSize.Height > 0 ? c.DesiredSize.Height : EstimatedHeight;
        }

        /// <summary>Test-Flyout an der Stelle p kurz anzeigen und danach schließen.</summary>
        public void ShowTestAt(Point p, int ms = 3000)
        {
            Left = p.X;
            Top = p.Y;
            Show();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try { Close(); } catch { }
            };
            timer.Start();
        }

        private static bool SameBytes(byte[]? a, byte[]? b) =>
            ReferenceEquals(a, b) || (a != null && b != null && a.AsSpan().SequenceEqual(b));

        private static BitmapImage? DecodeImage(byte[]? data)
        {
            if (data == null || data.Length == 0) return null;
            try
            {
                var bmp = new BitmapImage();
                using var ms = new System.IO.MemoryStream(data);
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        // ── Fortschrittsleiste ──────────────────────────────────────────────────
        private static string Fmt(TimeSpan t) =>
            t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

        private TimeSpan Estimate()
        {
            var pos = _basePos;
            if (_playing) pos += DateTimeOffset.Now - _baseStamp;
            if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
            if (pos > _duration) pos = _duration;
            return pos;
        }

        private void SetSeekFraction(double fraction)
        {
            double w = _seekHost.ActualWidth > 0 ? _seekHost.ActualWidth : W - 28;
            fraction = Math.Clamp(fraction, 0, 1);
            double x = w * fraction;
            _seekFill.Width = x;
            double thumb = _seekThumb.Width;
            _seekThumb.Margin = new Thickness(Math.Clamp(x - thumb / 2, 0, Math.Max(0, w - thumb)), 0, 0, 0);
            LayoutMaterialWave(x, w);
            LayoutHudProgress(fraction);
            LayoutHwProgress(fraction);
        }

        private void RefreshSeek()
        {
            bool show = _hasTimeline && _showProgress; // per Einstellung ausblendbar
            var vis = show ? Visibility.Visible : Visibility.Collapsed;
            _seekHost.Visibility = vis;
            _timesRow.Visibility = vis;
            if (_progressBlock != null) _progressBlock.Visibility = vis; // eigenes Design: samt Abstand
            if (!show) return;

            var pos = Estimate();
            SetSeekFraction(pos.TotalSeconds / _duration.TotalSeconds);
            _elapsed.Text = TimeText(pos);
            _total.Text = TotalText(pos);
        }

        /// <summary>Rechte Zeit: Gesamtdauer bzw. im Design-System die Restzeit mit Minus (wie iOS).</summary>
        private string TotalText(TimeSpan pos) =>
            ReferenceEquals(_spec, Designs.Island) || IsMac ? "−" + Fmt(_duration - pos < TimeSpan.Zero ? TimeSpan.Zero : _duration - pos) : TimeText(_duration);

        /// <summary>Zeitangabe; Gaming-HUD zweistellig („01:12“), sonst wie bisher.</summary>
        private string TimeText(TimeSpan t) => IsHud && t.TotalHours < 1 ? $"{t.Minutes:00}:{t.Seconds:00}" : Fmt(t);

        private void OnTick(object? sender, EventArgs e)
        {
            if (!_hasTimeline || !_showProgress || _seeking) return;

            // Alle 2 Sekunden die echte Position nachlesen (falls in der App selbst gesprungen wurde).
            if (++_tickCount % 4 == 0)
            {
                var t = _media.GetTimeline();
                if (t != null)
                {
                    _duration = t.Value.Duration;
                    _basePos = t.Value.Position;
                    _baseStamp = t.Value.Stamp;
                }
            }
            RefreshSeek();
        }

        private TimeSpan PositionFromMouse(MouseEventArgs e)
        {
            double w = _seekHost.ActualWidth > 0 ? _seekHost.ActualWidth : W - 28;
            double f = Math.Clamp(e.GetPosition(_seekHost).X / w, 0, 1);
            return TimeSpan.FromSeconds(_duration.TotalSeconds * f);
        }

        private void OnSeekDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true; // das Fenster-Drag darf hier nie anspringen
            if (!_hasTimeline || !_canSeek) return;
            _seeking = true;
            _seekHost.CaptureMouse();
            PreviewSeek(e);
        }

        private void OnSeekMove(object sender, MouseEventArgs e)
        {
            if (_seeking) PreviewSeek(e);
        }

        private void PreviewSeek(MouseEventArgs e)
        {
            var pos = PositionFromMouse(e);
            SetSeekFraction(pos.TotalSeconds / _duration.TotalSeconds);
            _elapsed.Text = TimeText(pos);
            if (_spec != null) _total.Text = TotalText(pos);
        }

        private void OnSeekUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (!_seeking) return;

            _seeking = false;
            _seekHost.ReleaseMouseCapture();
            if (_spec != null && !_seekHost.IsMouseOver) _seekThumb.Opacity = 0;

            var pos = PositionFromMouse(e);
            _media.SeekTo(pos);
            _basePos = pos;
            _baseStamp = DateTimeOffset.Now;
            RefreshSeek();
            ResetHideTimer();
        }

        /// <summary>±10 s: Anzeige sofort mitziehen (ohne auf die Quelle zu warten) und den Sprung auslösen.</summary>
        private void SkipLocal(int direction)
        {
            var delta = TimeSpan.FromSeconds(MediaController.SkipSeconds * direction);
            if (_hasTimeline)
            {
                var target = Estimate() + delta;
                if (target < TimeSpan.Zero) target = TimeSpan.Zero;
                if (target > _duration) target = _duration;
                _basePos = target;
                _baseStamp = DateTimeOffset.Now;
                RefreshSeek();
            }
            _media.SkipBy(delta);
            ResetHideTimer();
        }

        // ── "…"-Menü: Inhalt laut cfg.media_more_items (Zufall, Wiederholen, ±10-s-Hotkeys, Einstellungen) ──
        private void ShowMoreMenu()
        {
            if (_menuPopup != null) _menuPopup.IsOpen = false;

            var info = _lastInfo;
            var cfg = ConfigManager.Load();
            var items = cfg.MediaMoreItems;
            var list = new StackPanel { Margin = new Thickness(4) };
            var hover = Hex(_light ? "#14000000" : "#1AFFFFFF");
            var sepBrush = Hex(_light ? "#1F000000" : "#26FFFFFF");

            void CloseMenu() { if (_menuPopup != null) _menuPopup.IsOpen = false; }

            // Trennlinie nur zwischen Gruppen, nie am Anfang.
            void Group()
            {
                if (list.Children.Count > 0)
                    list.Children.Add(new Border { Height = 1, Background = sepBrush, Margin = new Thickness(8, 4, 8, 4) });
            }

            // Eine Menüzeile im Flyout-Stil: links Häkchen (aktiv) bzw. Symbol, daneben der Text.
            void Row(string label, string? glyph, bool isChecked, bool enabled, Action onClick)
            {
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                string? icon = isChecked ? "\uE73E" : glyph; // CheckMark
                if (icon != null)
                {
                    grid.Children.Add(new TextBlock
                    {
                        Text = icon, FontFamily = IconFont, FontSize = 12, Foreground = _fgBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                }
                var text = new TextBlock { Text = label, FontSize = 13, Foreground = _fgBrush, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(text, 1);
                grid.Children.Add(text);

                var row = new Border
                {
                    Child = grid, Padding = new Thickness(10, 7, 16, 7), CornerRadius = new CornerRadius(4),
                    Background = Brushes.Transparent, Opacity = enabled ? 1.0 : 0.4,
                    Cursor = enabled ? Cursors.Hand : Cursors.Arrow,
                };
                if (enabled)
                {
                    row.MouseEnter += (_, _) => row.Background = hover;
                    row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
                    row.MouseLeftButtonUp += (_, e) =>
                    {
                        e.Handled = true;
                        CloseMenu();
                        onClick();
                        ResetHideTimer();
                    };
                }
                list.Children.Add(row);
            }

            if (items.Contains("shuffle"))
            {
                Row(Loc.T("Zufallswiedergabe", "Shuffle"), null, info?.ShuffleActive == true, info?.ShuffleEnabled == true,
                    () => _media.SetShuffle(info?.ShuffleActive != true));
            }

            if (items.Contains("repeat"))
            {
                Group();
                (string Label, int Mode)[] repeat =
                {
                    (Loc.T("Wiederholen aus", "Repeat off"), 0),
                    (Loc.T("Titel wiederholen", "Repeat track"), 1),
                    (Loc.T("Liste wiederholen", "Repeat list"), 2),
                };
                foreach (var (label, mode) in repeat)
                    Row(label, null, info?.RepeatMode == mode, info?.RepeatEnabled == true, () => _media.SetRepeat(mode));
            }

            if (items.Contains("switch_source"))
            {
                Group();
                Row(Loc.T("Nächste Quelle", "Next source"), "\uE8AB", false, _media.GetSources().Count > 1, () => _media.CycleSource());
            }

            if (items.Contains("skip_hotkeys") || items.Contains("pin"))
            {
                Group();
                if (items.Contains("skip_hotkeys"))
                {
                    Row(Loc.T("Hotkeys ±10 s", "±10 s hotkeys"), null, cfg.MediaSkipHotkeysEnabled, true, () =>
                    {
                        var c = ConfigManager.Load();
                        c.MediaSkipHotkeysEnabled = !c.MediaSkipHotkeysEnabled;
                        ConfigManager.Save(c);
                        ConfigChanged?.Invoke();
                    });
                }
                if (items.Contains("pin"))
                    Row(Loc.T("Angeheftet lassen", "Keep pinned"), null, _pinned, true, () => _pinned = !_pinned);
            }

            if (items.Contains("save_position") || items.Contains("next_position") || items.Contains("reset_position"))
            {
                Group();
                if (items.Contains("save_position"))
                    Row(Loc.T("Position speichern", "Save position"), "\uE74E", false, true, () => MenuActionRequested?.Invoke("save_position"));
                if (items.Contains("next_position"))
                    Row(Loc.T("Nächste Position", "Next position"), "\uE81D", false, cfg.SnapPositions.Count > 0, () => MenuActionRequested?.Invoke("next_position"));
                if (items.Contains("reset_position"))
                    Row(Loc.T("Position zurücksetzen", "Reset position"), "\uE7A7", false, true, () => MenuActionRequested?.Invoke("reset_position"));
            }

            if (items.Contains("settings") || items.Contains("close"))
            {
                Group();
                if (items.Contains("settings"))
                    Row(Loc.T("Einstellungen …", "Settings …"), "\uE713", false, true, () => SettingsRequested?.Invoke());
                if (items.Contains("close"))
                {
                    Row(Loc.T("Ausblenden", "Hide"), "\uE711", false, true, () =>
                    {
                        _pinned = false;
                        _hideTimer.Stop();
                        Hide();
                    });
                }
            }

            if (list.Children.Count == 0)
                Row(Loc.T("(leer – in den Einstellungen anpassen)", "(empty – customize in settings)"), null, false, false, () => { });

            // Eigenes Popup statt ContextMenu: gleiche Farben, Rundung und Rand wie das Flyout.
            _menuPopup = new Popup
            {
                PlacementTarget = _moreBtn, Placement = PlacementMode.Top,
                AllowsTransparency = true, StaysOpen = false, PopupAnimation = PopupAnimation.Fade,
                Child = new Border
                {
                    MinWidth = 210, CornerRadius = new CornerRadius(8), Margin = new Thickness(8), // Rand = Platz für den Schatten
                    Background = Hex(_cardHex),
                    BorderBrush = Hex(_light ? "#D0D0D4" : "#404040"), BorderThickness = new Thickness(1),
                    Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Direction = 270, Opacity = 0.35 },
                    Child = list,
                },
            };
            _menuPopup.Opened += (_, _) => _menuOpen = true;
            _menuPopup.Closed += (_, _) => { _menuOpen = false; ResetHideTimer(); };
            _menuPopup.IsOpen = true;
        }

        // ── Quellen-Leiste ─────────────────────────────────────────────────────
        /// <summary>
        /// Ein kleiner Pill pro aktiver Mediensession (z.B. Spotify + Firefox gleichzeitig). Klick
        /// schaltet die gesteuerte Quelle um; der kleine Pause-Punkt pausiert eine Quelle direkt,
        /// ohne zu ihr zu wechseln. Bei nur einer Quelle bleibt die Leiste leer.
        /// </summary>
        private void RenderSources()
        {
            _sourcesRow.Children.Clear();

            var sources = _media.GetSources();
            if (IsMac)
            {
                RenderSegments(sources); // macOS: Segment-Steuerung statt Pills
                return;
            }
            if (IsMaterial)
            {
                RenderChips(sources); // Material You: Filter-Chips
                return;
            }
            if (IsHud)
            {
                RenderTabs(sources); // Gaming-HUD: Reiter mit Unterstrich
                return;
            }
            if (IsHardware)
            {
                RenderInputs(sources); // Hardware: Eingangswahl mit LED-Punkten
                return;
            }
            if (_hideSources)
            {
                _sourcesRow.Visibility = Visibility.Collapsed; // eigenes Design: Quellen-Leiste abgeschaltet
                return;
            }
            if (_custom || _spec != null) _sourcesRow.Visibility = sources.Count < 2 ? Visibility.Collapsed : Visibility.Visible;
            if (sources.Count < 2) return;

            string? activeAppId = _media.CurrentAppId;

            foreach (var src in sources)
            {
                bool active = src.AppId == activeAppId;

                var pill = new Border
                {
                    CornerRadius = new CornerRadius(_spec != null ? 12 : 11),
                    Background = _pal != null ? Hex(active ? _pal.Fill : _pal.Button) // Design-System: aktive Quelle in Textfarbe
                        : active ? _accent
                        : _light ? Hex(_win11 ? "#D6D6DA" : "#D9D9D9") : Hex(_win11 ? "#3A3A3A" : "#33333C"),
                    Padding = _spec != null ? new Thickness(11, 4, 9, 4) : new Thickness(8, 3, 6, 3),
                    Margin = new Thickness(0, 0, 6, 0),
                    Cursor = Cursors.Hand,
                };

                // Windows-11-Akzent im Dunkelmodus ist hellblau → dort dunkle Schrift auf dem aktiven Pill.
                bool lightText = _spec != null ? (active ? _light : !_light) : active ? !(_win11 && !_light) : !_light;
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock
                {
                    Text = src.Label, FontSize = _spec != null ? 12 : 10, FontWeight = FontWeights.SemiBold,
                    FontFamily = _spec?.Font ?? FontFamily,
                    Foreground = lightText ? Brushes.White : Hex("#1b1b1b"), VerticalAlignment = VerticalAlignment.Center,
                });

                if (src.IsPlaying)
                {
                    var pauseDot = new TextBlock
                    {
                        Text = "\uE769", FontFamily = IconFont, FontSize = 10, Margin = new Thickness(5, 0, 0, 0),
                        Foreground = lightText ? Hex("#E6FFFFFF") : Hex("#CC1b1b1b"),
                        VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
                    };
                    pauseDot.MouseLeftButtonDown += (_, e) => e.Handled = true;
                    pauseDot.MouseLeftButtonUp += (_, e) =>
                    {
                        _media.PauseSource(src.Session);
                        e.Handled = true;
                        ResetHideTimer();
                    };
                    row.Children.Add(pauseDot);
                }

                pill.Child = row;
                pill.MouseLeftButtonDown += (_, e) => e.Handled = true;
                pill.MouseLeftButtonUp += (_, e) =>
                {
                    if (!active) _media.SwitchTo(src.Session);
                    e.Handled = true;
                    ResetHideTimer();
                };

                _sourcesRow.Children.Add(pill);
            }
        }

        /// <summary>
        /// Positioniert das Fenster neu – Media hängt am Stapel (cfg.MediaFollowsOsd): unter/über dem
        /// Lautstärke-Stapel, aber NUR wenn der Stapel gerade sichtbar ist (sonst bleibt die Karte, wo
        /// sie ist); sonst ("separate" oder Stapel hängt am Media-Flyout): eigener, gespeicherter Anker.
        /// Wird bewusst nur beim Erstanzeigen und bei echten Stapel-Änderungen aufgerufen, NICHT bei
        /// jeder Metadaten-Aktualisierung – sonst würde ein laufendes Ziehen überschrieben.
        /// </summary>
        public void ApplyPosition(AppConfig cfg, OsdManager osd)
        {
            if (cfg.MediaFollowsOsd())
            {
                var p = osd.GetCombinedMediaPosition(cfg, ActualHeight);
                if (p == null) return; // Stapel gerade leer -> Position unverändert lassen
                // "Oberhalb": die Unterkante hängt am Stapel, Höhenänderungen wachsen nach oben (siehe SizeChanged).
                _keepBottom = p.Value.Above;
                Left = p.Value.Pos.X;
                Top = p.Value.Pos.Y;
            }
            else
            {
                // Unten verankerte Ecken wachsen ebenfalls nach oben, damit die Unterkante am Rand bleibt.
                _keepBottom = Placement.IsPreset(cfg.MediaCornerPreset) && cfg.MediaCornerPreset.StartsWith("bottom");
                double h = _keepBottom ? ActualHeight : (ActualHeight > 0 ? ActualHeight : EstimatedHeight);
                var anchor = Placement.MediaAnchor(cfg, W, h);
                var c = Placement.Clamp(new Rect(anchor, new Size(W, Math.Max(1, h))));
                Left = c.X;
                Top = c.Y;
            }
        }

        /// <summary>Liegt das Flyout (teilweise) außerhalb des Bildschirms, hineinschieben.</summary>
        private void KeepOnScreen()
        {
            if (_dragging || ActualHeight <= 0) return;
            var c = Placement.Clamp(new Rect(Left, Top, ActualWidth, ActualHeight));
            if (Math.Abs(c.X - Left) > 0.5) Left = c.X;
            if (Math.Abs(c.Y - Top) > 0.5) Top = c.Y;
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            var cfg = ConfigManager.Load();
            _clickDismiss = cfg.ClickToDismiss;
            _pressed = true;
            _pressLeft = Left;
            _pressTop = Top;
            // Hängt das Flyout am Lautstärke-Stapel, folgt es automatisch dessen Anker und ist hier
            // nicht separat ziehbar. Sonst ("separate" oder Stapel hängt am Flyout) ist Ziehen IMMER
            // erlaubt, unabhängig vom "OSD verschiebbar"-Haken (der regelt nur die Klick-
            // Durchlässigkeit der Lautstärke-Karten, nicht dieses ohnehin immer klickbare Fenster).
            if (cfg.MediaFollowsOsd()) return;
            _dragging = true;
            _dragStart = e.GetPosition(this);
            CaptureMouse();
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var p = e.GetPosition(this);
            Left += p.X - _dragStart.X;
            Top += p.Y - _dragStart.Y;
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            bool wasPressed = _pressed;
            bool wasDragging = _dragging;
            _pressed = false;
            bool moved = Math.Abs(Left - _pressLeft) > 3 || Math.Abs(Top - _pressTop) > 3;

            if (wasDragging)
            {
                _dragging = false;
                ReleaseMouseCapture();
            }
            else if (!wasPressed) return;

            // Einfacher Klick (kaum bewegt) auf eine freie Stelle → bei cfg.ClickToDismiss ausblenden.
            if (wasPressed && !moved && _clickDismiss)
            {
                Left = _pressLeft;
                Top = _pressTop;
                Dismiss();
                return;
            }
            if (!wasDragging || !moved) return; // Klick ohne Ausblenden bzw. kein eigenes Ziehen: nichts zu speichern

            var cfg = ConfigManager.Load();
            double x = Left, y = Top;
            const double snapThreshold = 40.0;
            foreach (var sp in cfg.SnapPositions)
            {
                // Erst die zum Punkt gehörende Media-Position prüfen, sonst dessen Lautstärke-Position als Fallback.
                double px = sp.MediaX ?? sp.X;
                double py = sp.MediaY ?? sp.Y;
                if (Math.Abs(px - x) <= snapThreshold && Math.Abs(py - y) <= snapThreshold)
                {
                    x = px; y = py;
                    break;
                }
            }
            // Nicht außerhalb des Bildschirms ablegen.
            var c = Placement.Clamp(new Rect(x, y, ActualWidth, ActualHeight));
            Left = c.X;
            Top = c.Y;
            cfg.MediaOsdX = (int)Math.Round(c.X);
            cfg.MediaOsdY = (int)Math.Round(c.Y);
            cfg.MediaCornerPreset = "custom"; // selbst verschoben → keine Ecken-Voreinstellung mehr
            ConfigManager.Save(cfg);
            DragFinished?.Invoke();
        }

        /// <summary>Per Klick ausgeblendet (cfg.ClickToDismiss): sofort weg und App informieren.</summary>
        private void Dismiss()
        {
            HideNow();
            DismissRequested?.Invoke();
        }

        /// <summary>Sofort ausblenden (auch "angeheftet" endet), ohne DismissRequested auszulösen.</summary>
        public void HideNow()
        {
            _pinned = false;
            _hideTimer.Stop();
            if (_menuPopup != null) _menuPopup.IsOpen = false;
            CancelFadeOut();
            Hide();
        }

        private void ResetHideTimer()
        {
            _hideTimer.Stop();
            _hideTimer.Start();
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
