using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using KnobEarTrumpet.Media;

namespace KnobEarTrumpet.Osd
{
    /// <summary>
    /// Media-Flyout im Design „Material You“ (Designs.Material): Farben aus dem Cover (ohne Cover: Windows-Akzentfarbe),
    /// wellige Fortschrittsleiste, verbundene Knopfgruppe −10 · Zurück · Play (breit) · Weiter · +10, Filter-Chips.
    /// </summary>
    internal sealed partial class MediaOsd
    {
        private const double M3WaveLen = 20;   // Wellenlänge der Fortschrittsleiste
        private const double M3WaveGap = 6;    // Lücke zwischen Leiste und Griff

        private bool IsMaterial => ReferenceEquals(_spec, Designs.Material);

        private readonly List<Border> _m3Group = new();      // Hüllen der Knopfgruppe (−10, Zurück, Play, Weiter, +10)
        private readonly List<Border> _m3GroupInner = new(); // die Knöpfe darin (Hover-Fläche)
        private Grid? _m3GroupGrid;
        private WrapPanel? _m3Chips;
        private Canvas? _m3WaveLayer;                         // abgespielter Teil, auf die Position beschnitten
        private TranslateTransform? _m3WaveShift;
        private System.Windows.Shapes.Path? _m3WavePath;
        private System.Windows.Shapes.Path? _m3FlatPath;
        private Rectangle? _m3Rest;
        private Rectangle? _m3Grip;
        private bool _m3WaveRunning;
        private BitmapSource? _m3LastCover;
        private Color? _m3Seed;

        // Veränderliche Pinsel: ein Cover-Wechsel färbt nur um, ohne Neuaufbau
        private readonly SolidColorBrush _m3Seg = new();
        private readonly SolidColorBrush _m3Play = new();
        private readonly SolidColorBrush _m3OnPlay = new();
        private readonly SolidColorBrush _m3Track = new();
        private readonly SolidColorBrush _m3Prim = new();

        /// <summary>
        /// Oben App-Logo (rund) + Name · „…“ rund; Cover 64 (Radius 16) · Titel / Interpret; wellige Leiste mit Griff,
        /// Zeiten darunter; verbundene Knopfgruppe; Quellen als Filter-Chips (nur bei mehreren Quellen). Wie ArrangeSpec:
        /// die fertigen Elemente aus dem Standardaufbau lösen und neu anordnen.
        /// </summary>
        private void ArrangeMaterial(Grid root, Grid header, StackPanel left, StackPanel appRow, Grid controls, StackPanel center)
        {
            var font = _spec!.Font;
            root.Children.Clear();
            root.RowDefinitions.Clear();
            header.Children.Clear();
            left.Children.Clear();
            appRow.Children.Clear();
            controls.Children.Clear();
            root.Margin = new Thickness(15, 9, 15, 15); // + 1 px Rand; die Kopfzeile ist 36 hoch (Knopf), Text sitzt bei 16
            var stack = new StackPanel();
            root.Children.Add(stack);

            // ── Kopf: App-Logo + Name | „…“ ──
            var top = new Grid { Height = 36 };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _appIcon.Width = _appIcon.Height = 20;
            _appIcon.Clip = new EllipseGeometry(new Point(10, 10), 10, 10);
            _appGlyph.FontSize = 14;
            _appGlyph.Foreground = _subBrush;
            _appName.FontFamily = font;
            _appName.FontSize = 13;
            _appName.FontWeight = FontWeights.SemiBold;
            _appName.Foreground = _subBrush;
            _appName.Margin = new Thickness(8, 0, 0, 0);
            appRow.Margin = new Thickness(0);
            appRow.Children.Add(_appIcon);
            appRow.Children.Add(_appGlyph);
            appRow.Children.Add(_appName);
            top.Children.Add(appRow);
            _moreBtn.Width = _moreBtn.Height = 36;
            _moreBtn.CornerRadius = new CornerRadius(18);
            _moreBtn.Background = _m3Seg;
            _moreBtn.HorizontalAlignment = HorizontalAlignment.Right;
            _moreBtn.VerticalAlignment = VerticalAlignment.Center;
            if (_moreBtn.Child is TextBlock dots) dots.FontSize = 14;
            _moreBtn.MouseEnter += (_, _) => _moreBtn.Opacity = 0.85;
            _moreBtn.MouseLeave += (_, _) => _moreBtn.Opacity = 1.0;
            Grid.SetColumn(_moreBtn, 1);
            top.Children.Add(_moreBtn);
            stack.Children.Add(top);

            // ── Cover | Titel / Interpret (mit Cover-Hintergrund ohne eigenes Cover, siehe ApplyMaterial) ──
            var mid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            mid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            const double art = 64;
            _art.Width = _art.Height = art;
            _art.CornerRadius = new CornerRadius(16);
            _art.Clip = new RectangleGeometry(new Rect(0, 0, art, art), 16, 16);
            _art.Background = _m3Seg;
            _art.VerticalAlignment = VerticalAlignment.Center;
            _art.Margin = new Thickness(0, 0, 14, 0);
            Grid.SetColumn(_art, 0);
            mid.Children.Add(_art);
            _title.FontFamily = font;
            _title.FontSize = 16;
            _title.Margin = new Thickness(0);
            _artist.FontFamily = font;
            _artist.FontSize = 14;
            _artist.Margin = new Thickness(0, 2, 0, 0);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(_title);
            text.Children.Add(_artist);
            Grid.SetColumn(text, 1);
            mid.Children.Add(text);
            stack.Children.Add(mid);

            // ── Wellige Leiste: Welle bis kurz vor den Griff, danach flache Spur (die Maus bedient weiter _seekHost) ──
            _seekHost.Height = 24;
            _seekHost.Margin = new Thickness(0, 14, 0, 0);
            foreach (UIElement child in _seekHost.Children) child.Visibility = Visibility.Collapsed; // Standard-Spur, Füllung, Griff
            _m3WaveShift = new TranslateTransform();
            _m3WavePath = new System.Windows.Shapes.Path
            {
                Data = WaveGeometry(-M3WaveLen, 360, 3, 12), Stroke = _m3Prim, StrokeThickness = 4,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransform = _m3WaveShift,
            };
            _m3FlatPath = new System.Windows.Shapes.Path
            {
                Data = new LineGeometry(new Point(2, 12), new Point(360, 12)), Stroke = _m3Prim, StrokeThickness = 4,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Visibility = Visibility.Collapsed,
            };
            _m3WaveLayer = new Canvas();
            _m3WaveLayer.Children.Add(_m3WavePath);
            _m3WaveLayer.Children.Add(_m3FlatPath);
            _m3Rest = new Rectangle { Height = 4, RadiusX = 2, RadiusY = 2, Fill = _m3Track };
            _m3Grip = new Rectangle { Width = 4, Height = 20, RadiusX = 2, RadiusY = 2, Fill = _m3Prim };
            Canvas.SetTop(_m3Rest, 10);
            Canvas.SetTop(_m3Grip, 2);
            var layer = new Canvas { IsHitTestVisible = false };
            layer.Children.Add(_m3WaveLayer);
            layer.Children.Add(_m3Rest);
            layer.Children.Add(_m3Grip);
            _seekHost.Children.Add(layer);
            Grid.SetRow(_seekHost, 0);
            foreach (var t in new[] { _elapsed, _total })
            {
                t.FontFamily = font;
                t.FontSize = 12;
                System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            }
            _timesRow.Margin = new Thickness(0);
            Grid.SetRow(_timesRow, 0);
            var progress = new StackPanel();
            progress.Children.Add(_seekHost);
            progress.Children.Add(_timesRow);
            _progressBlock = progress;
            stack.Children.Add(progress);

            // ── Verbundene Knopfgruppe: −10 · Zurück · Play (breit, Primärfarbe) · Weiter · +10 ──
            var buttons = center.Children.Cast<UIElement>().ToArray(); // Standard: Zurück, −10, Play, +10, Weiter
            center.Children.Clear();
            _m3GroupGrid = new Grid { Height = 48, Margin = new Thickness(0, 10, 0, 0) };
            int k = 0;
            foreach (int i in new[] { 1, 0, 2, 4, 3 })
            {
                var inner = (Border)buttons[i];
                bool play = i == 2;
                inner.Width = inner.Height = double.NaN;
                inner.Margin = new Thickness(0);
                inner.HorizontalAlignment = HorizontalAlignment.Stretch;
                inner.VerticalAlignment = VerticalAlignment.Stretch;
                var shell = new Border { Background = play ? _m3Play : _m3Seg, Child = inner };
                shell.SetBinding(VisibilityProperty, new Binding(nameof(Visibility)) { Source = inner }); // ±10 per Einstellung ausblendbar
                _m3GroupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(play ? 1.9 : 1, GridUnitType.Star) });
                Grid.SetColumn(shell, k++);
                _m3GroupGrid.Children.Add(shell);
                _m3Group.Add(shell);
                _m3GroupInner.Add(inner);
            }
            if (buttons[0] is Border { Child: TextBlock prev }) prev.FontSize = 20;
            if (buttons[4] is Border { Child: TextBlock next }) next.FontSize = 20;
            _playPauseGlyph.FontSize = 24;
            _playPauseGlyph.Foreground = _m3OnPlay;
            MacSkipIcon(_skipBackBtn, "\uE777"); // Kreispfeil + „10“ (wie replay_10/forward_10)
            MacSkipIcon(_skipFwdBtn, "\uE72C");
            stack.Children.Add(_m3GroupGrid);
            UpdateMaterialGroup();

            // ── Quellen als Filter-Chips (RenderChips) ──
            _m3Chips = new WrapPanel { Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
            stack.Children.Add(_m3Chips);

            ApplyMaterialColors(_pal!, false);
        }

        /// <summary>Rundungen und Breiten der Knopfgruppe an die sichtbaren Knöpfe anpassen (außen 24, innen 8, 2 px Lücke).</summary>
        private void UpdateMaterialGroup()
        {
            if (_m3GroupGrid == null) return;
            var visible = _m3Group.Where(s => s.Visibility == Visibility.Visible).ToList();
            for (int k = 0; k < _m3Group.Count; k++)
            {
                var shell = _m3Group[k];
                int pos = visible.IndexOf(shell);
                _m3GroupGrid.ColumnDefinitions[k].Width = pos < 0 ? new GridLength(0) : new GridLength(k == 2 ? 1.9 : 1, GridUnitType.Star);
                if (pos < 0) continue;
                bool first = pos == 0, last = pos == visible.Count - 1;
                double l = first ? 24 : 8, r = last ? 24 : 8;
                shell.CornerRadius = _m3GroupInner[k].CornerRadius = new CornerRadius(l, r, r, l);
                shell.Margin = new Thickness(first ? 0 : 1, 0, last ? 0 : 1, 0);
            }
        }

        /// <summary>Farben aus dem Cover (bzw. der Windows-Akzentfarbe) errechnen und anwenden; mit Cover-Hintergrund kein eigenes Cover.</summary>
        private void ApplyMaterial(BitmapSource? cover, bool useBackground)
        {
            // Ohne Cover jedes Mal die aktuelle Akzentfarbe (ändert sich sonst erst beim nächsten Cover)
            if (cover == null || !ReferenceEquals(cover, _m3LastCover) || _m3Seed == null)
            {
                _m3LastCover = cover;
                _m3Seed = cover != null ? CoverAccent(cover) : Config.ThemeHelper.AccentColor();
            }
            ApplyMaterialColors(MaterialColors.Scheme(_m3Seed.Value, _light), useBackground);
            _art.Visibility = useBackground ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ApplyMaterialColors(DesignPalette pal, bool onArt)
        {
            static Color C(string hex) => UiColor.Brush(hex).Color;
            _pal = pal;
            _cardHex = NormHex(pal.Bg, "#000000");
            _fgBrush.Color = C(pal.Fg);
            _subBrush.Color = C(pal.Sub);
            _accent.Color = C(pal.Fill);
            _m3Prim.Color = C(pal.Fill);
            _m3Play.Color = C(pal.Fill);
            _m3OnPlay.Color = C(pal.OnAccent ?? pal.Bg);
            // Auf dem Cover-Hintergrund: durchscheinende Flächen statt der Kartenfarbe
            _m3Seg.Color = onArt ? (_light ? Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x47, 0, 0, 0)) : C(pal.Tile ?? pal.Button);
            _m3Track.Color = onArt ? _m3Seg.Color : C(pal.Track);
            _card.BorderBrush = UiColor.Brush(pal.Edge);
        }

        /// <summary>Griff an die Position x setzen, Welle bis kurz davor zeigen, danach die flache Spur.</summary>
        private void LayoutMaterialWave(double x, double w)
        {
            if (_m3WaveLayer == null || _m3Rest == null || _m3Grip == null) return;
            _m3WaveLayer.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, x - M3WaveGap), 24));
            Canvas.SetLeft(_m3Grip, Math.Clamp(x - 2, 0, Math.Max(0, w - 4)));
            double restLeft = x + 2 + M3WaveGap;
            Canvas.SetLeft(_m3Rest, restLeft);
            _m3Rest.Width = Math.Max(0, w - restLeft);
            _m3Rest.Visibility = w - restLeft > 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Welle beim Abspielen (bewegt sich nur, solange das Flyout sichtbar ist), pausiert flache Linie.</summary>
        private void UpdateMaterialWave(bool playing)
        {
            if (_m3WaveShift == null || _m3WavePath == null || _m3FlatPath == null) return;
            _m3WavePath.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
            _m3FlatPath.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
            bool run = playing && IsVisible;
            if (run && !_m3WaveRunning)
                _m3WaveShift.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(0, M3WaveLen, TimeSpan.FromMilliseconds(1000)) { RepeatBehavior = RepeatBehavior.Forever });
            else if (!run && _m3WaveRunning)
                _m3WaveShift.BeginAnimation(TranslateTransform.XProperty, null);
            _m3WaveRunning = run;
        }

        private static Geometry WaveGeometry(double from, double to, double amp, double y)
        {
            double Y(double x) => y + amp * Math.Sin(x / M3WaveLen * 2 * Math.PI);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(from, Y(from)), false, false);
                for (double x = from + 1; x <= to; x += 1) ctx.LineTo(new Point(x, Y(x)), true, true);
            }
            g.Freeze();
            return g;
        }

        /// <summary>
        /// Quellen als M3-Filter-Chips: gewählt = gefüllt mit Häkchen, sonst nur Umriss. Klick schaltet um; der kleine
        /// Pause-Punkt pausiert eine spielende Quelle direkt (wie bei den Pills).
        /// </summary>
        private void RenderChips(List<MediaSource> sources)
        {
            if (_m3Chips == null) return;
            _m3Chips.Children.Clear();
            _m3Chips.Visibility = sources.Count < 2 ? Visibility.Collapsed : Visibility.Visible;
            if (sources.Count < 2) return;

            var pal = _pal!;
            string? activeAppId = _media.CurrentAppId;
            foreach (var src in sources)
            {
                bool active = src.AppId == activeAppId;
                var fg = UiColor.Brush(active ? pal.OnTrack ?? pal.Fg : pal.Sub);
                var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                if (active)
                {
                    row.Children.Add(new TextBlock
                    {
                        Text = "\uE73E", FontFamily = IconFont, FontSize = 12, Foreground = fg, // CheckMark
                        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
                    });
                }
                row.Children.Add(new TextBlock
                {
                    Text = src.Label, FontFamily = _spec!.Font, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = fg,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                if (src.IsPlaying)
                {
                    var pauseDot = new TextBlock
                    {
                        Text = "\uE769", FontFamily = IconFont, FontSize = 10, Margin = new Thickness(6, 0, 0, 0), Foreground = fg,
                        Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
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

                var chip = new Border
                {
                    Height = 32, CornerRadius = new CornerRadius(8), Padding = new Thickness(active ? 8 : 12, 0, 12, 0),
                    Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand, Child = row,
                    Background = active ? _m3Track : Brushes.Transparent,
                    BorderBrush = active ? null : UiColor.Brush(pal.OutlineVariant ?? pal.Track),
                    BorderThickness = new Thickness(active ? 0 : 1),
                };
                chip.MouseLeftButtonDown += (_, e) => e.Handled = true;
                chip.MouseLeftButtonUp += (_, e) =>
                {
                    if (!active) _media.SwitchTo(src.Session);
                    e.Handled = true;
                    ResetHideTimer();
                };
                _m3Chips.Children.Add(chip);
            }
        }
    }
}
