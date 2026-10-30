using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using KnobFlyout.Media;

namespace KnobFlyout.Osd
{
    /// <summary>Media-Flyout im Design „Gaming-HUD“ (Designs.Hud): abgeschrägte Platte, Segment-Fortschritt, Reiter für Quellen.</summary>
    internal sealed partial class MediaOsd
    {
        private bool IsHud => ReferenceEquals(_spec, Designs.Hud);

        private System.Windows.Shapes.Path? _hudBg;       // abgeschrägte Fläche + Rand (ersetzt den Kartenhintergrund)
        private System.Windows.Shapes.Path? _hudBrackets;
        private Rectangle[]? _hudProg;                    // leuchtende Fortschritts-Segmente
        private StackPanel? _hudTabs;
        private Rectangle? _hudStatusDot;
        private TextBlock? _hudStatusPause;
        private TextBlock? _hudStatusText;
        private double _hudGlowOpacity;

        /// <summary>Kartenumriss: Gaming-HUD abgeschrägt, sonst gerundet (Ecken-Einstellung bzw. Design).</summary>
        private Geometry CardClip(double w, double h) =>
            _spec != null && _spec.MediaChamfer > 0 ? HudParts.Chamfer(w, h, _spec.MediaChamfer)
                : new RectangleGeometry(new Rect(0, 0, w, h), _radius, _radius);

        /// <summary>
        /// Kopf: App-Logo + APP · Status (● LÄUFT / ❚❚ PAUSE) · „…“ (abgeschrägt); Cover 60 mit Eck-Klammern · TITEL / Interpret;
        /// Zeit · 36 Segmente · Gesamtzeit; abgeschrägte Knöpfe −10 · Zurück · Play (gefüllt) · Weiter · +10; Quellen als Reiter.
        /// Wie ArrangeSpec: die fertigen Elemente aus dem Standardaufbau lösen und neu anordnen.
        /// </summary>
        private void ArrangeHud(Grid root, Grid header, StackPanel left, StackPanel appRow, Grid controls, StackPanel center)
        {
            var pal = _pal!;
            var font = _spec!.Font;
            _hudGlowOpacity = _light ? 0.3 : 0.6;
            root.Children.Clear();
            root.RowDefinitions.Clear();
            header.Children.Clear();
            left.Children.Clear();
            appRow.Children.Clear();
            controls.Children.Clear();
            root.Margin = new Thickness(16, 12, 16, 14);
            var stack = new StackPanel();
            root.Children.Add(stack);

            // ── Platte: abgeschrägte Fläche ganz hinten (fängt die Maus für Ziehen/Wegklicken), Scanlines + Klammern ganz vorn ──
            _card.BorderThickness = new Thickness(0);
            _card.CornerRadius = new CornerRadius(0);
            _card.Background = Brushes.Transparent; // Fläche zeichnet _hudBg (siehe ApplyHudBackground)
            _hudBg = new System.Windows.Shapes.Path { Stroke = Hex(pal.Edge), StrokeThickness = 1 };
            _host.Children.Insert(0, _hudBg);
            if (pal.Scan != null) _host.Children.Add(new Rectangle { Fill = HudParts.Scanlines(pal.Scan), IsHitTestVisible = false });
            _hudBrackets = new System.Windows.Shapes.Path { Stroke = _accent, StrokeThickness = 2, IsHitTestVisible = false };
            _host.Children.Add(_hudBrackets);
            _host.SizeChanged += (_, e) =>
            {
                _hudBg.Data = HudParts.Chamfer(e.NewSize.Width, e.NewSize.Height, _spec.MediaChamfer, 0.5);
                _hudBrackets.Data = HudParts.BracketGeometry(e.NewSize.Width, e.NewSize.Height);
            };

            // ── Kopf ──
            var top = new Grid { Height = 26 };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _appIcon.Width = _appIcon.Height = 16;
            _appGlyph.FontSize = 13;
            _appGlyph.Foreground = _subBrush;
            _appName.FontFamily = font;
            _appName.FontSize = 12;
            _appName.FontWeight = FontWeights.Bold;
            _appName.Foreground = _subBrush;
            _appName.Margin = new Thickness(7, 0, 0, 0);
            _appName.TextTrimming = TextTrimming.CharacterEllipsis;
            appRow.Children.Add(_appIcon);
            appRow.Children.Add(_appGlyph);
            appRow.Children.Add(_appName);
            top.Children.Add(appRow);

            var status = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            _hudStatusDot = new Rectangle
            {
                Width = 6, Height = 6, Fill = Hex(pal.On), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center,
                Effect = HudParts.Glow(Hex(pal.On).Color, _hudGlowOpacity, 6),
            };
            _hudStatusPause = new TextBlock
            {
                Text = "\uF8AE", FontFamily = IconFont, FontSize = 9, Foreground = _subBrush, Margin = new Thickness(0, 0, 5, 0), // PauseSolid
                VerticalAlignment = VerticalAlignment.Center,
            };
            _hudStatusText = new TextBlock { FontFamily = font, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = _subBrush, VerticalAlignment = VerticalAlignment.Center };
            status.Children.Add(_hudStatusDot);
            status.Children.Add(_hudStatusPause);
            status.Children.Add(_hudStatusText);
            Grid.SetColumn(status, 1);
            top.Children.Add(status);
            _hudStatus = status;

            var more = new Grid { Width = 30, Height = 26, Margin = new Thickness(8, 0, 0, 0) };
            more.Children.Add(HudParts.ChamferPath(30, 26, 5, null, Hex(pal.Edge)));
            _moreBtn.Width = 30;
            _moreBtn.Height = 26;
            _moreBtn.CornerRadius = new CornerRadius(0);
            _moreBtn.Background = Brushes.Transparent;
            _moreBtn.Clip = HudParts.Chamfer(30, 26, 5);
            _moreBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (_moreBtn.Child is TextBlock dots) dots.FontSize = 13;
            var hover = Hex(pal.Button);
            _moreBtn.MouseEnter += (_, _) => _moreBtn.Background = hover;
            _moreBtn.MouseLeave += (_, _) => _moreBtn.Background = Brushes.Transparent;
            more.Children.Add(_moreBtn);
            more.SetBinding(VisibilityProperty, new Binding(nameof(Visibility)) { Source = _moreBtn });
            Grid.SetColumn(more, 2);
            top.Children.Add(more);
            stack.Children.Add(top);
            _moreHost = more;

            // ── Cover mit Eck-Klammern | TITEL / Interpret ──
            var mid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            mid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            const double art = 60;
            var artHost = new Grid { Width = art, Height = art, Margin = new Thickness(4, 4, 18, 4), VerticalAlignment = VerticalAlignment.Center };
            _art.Width = _art.Height = art;
            _art.CornerRadius = new CornerRadius(0);
            _art.Clip = null;
            _art.Background = Hex(pal.ArtPlaceholder);
            _art.Margin = new Thickness(0);
            _art.VerticalAlignment = VerticalAlignment.Center;
            artHost.Children.Add(_art);
            _hudArtCorners = new System.Windows.Shapes.Path
            {
                Data = HudParts.CornerGeometry(art, art, 9, 4), Stroke = _accent, StrokeThickness = 2, IsHitTestVisible = false,
            };
            artHost.Children.Add(_hudArtCorners);
            Grid.SetColumn(artHost, 0);
            mid.Children.Add(artHost);
            _title.FontFamily = font;
            _title.FontSize = 18;
            _title.FontWeight = FontWeights.Bold;
            _title.Margin = new Thickness(0);
            _artist.FontFamily = font;
            _artist.FontSize = 13;
            _artist.Margin = new Thickness(0, 2, 0, 0);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(_title);
            text.Children.Add(_artist);
            Grid.SetColumn(text, 1);
            mid.Children.Add(text);
            stack.Children.Add(mid);
            _artBlock = artHost;
            _textBlock = text;
            _infoRow = mid;
            _buttonsPanel = center;

            // ── Fortschritt: Zeit · Segmente (Klicken/Ziehen über _seekHost wie gehabt) · Gesamtzeit ──
            var progress = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            progress.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            progress.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            progress.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _timesRow.Children.Clear();
            foreach (var t in new[] { _elapsed, _total })
            {
                t.FontFamily = font;
                t.FontSize = 13;
                t.FontWeight = FontWeights.SemiBold;
                t.VerticalAlignment = VerticalAlignment.Center;
                t.HorizontalAlignment = HorizontalAlignment.Left;
                System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            }
            _elapsed.Margin = new Thickness(0, 0, 10, 0);
            _total.Margin = new Thickness(10, 0, 0, 0);
            foreach (UIElement child in _seekHost.Children) child.Visibility = Visibility.Collapsed; // Standard-Spur, Füllung, Griff
            var segs = HudParts.Segments(36, 8, 2, Hex(pal.Track), out _hudProg, out var front);
            segs.VerticalAlignment = VerticalAlignment.Center;
            segs.IsHitTestVisible = false;
            front.Effect = HudParts.Glow(_accent.Color, _hudGlowOpacity, 6);
            _seekHost.Children.Add(segs);
            _seekHost.Margin = new Thickness(0);
            Grid.SetRow(_seekHost, 0);
            Grid.SetColumn(_seekHost, 1);
            Grid.SetColumn(_total, 2);
            progress.Children.Add(_elapsed);
            progress.Children.Add(_seekHost);
            progress.Children.Add(_total);
            _progressBlock = progress;
            stack.Children.Add(progress);

            // ── Knöpfe: abgeschrägt; Play gefüllt in Akzentfarbe ──
            var buttons = center.Children.Cast<UIElement>().ToArray(); // Standard: Zurück, −10, Play, +10, Weiter
            center.Children.Clear();
            foreach (int i in new[] { 1, 0, 2, 4, 3 })
            {
                var inner = (Border)buttons[i];
                bool play = i == 2;
                double w = play ? 64 : 44, h = 34;
                var shell = new Grid { Width = w, Height = h, Margin = new Thickness(4, 0, 4, 0) };
                shell.Children.Add(HudParts.ChamferPath(w, h, 6, play ? _accent : null, play ? _accent : Hex(pal.Edge)));
                inner.Width = w;
                inner.Height = h;
                inner.Margin = new Thickness(0);
                inner.CornerRadius = new CornerRadius(0);
                inner.Clip = HudParts.Chamfer(w, h, 6);
                shell.Children.Add(inner);
                shell.SetBinding(VisibilityProperty, new Binding(nameof(Visibility)) { Source = inner }); // ±10 per Einstellung ausblendbar
                center.Children.Add(shell);
            }
            if (buttons[0] is Border { Child: TextBlock prev }) prev.FontSize = 16;
            if (buttons[4] is Border { Child: TextBlock next }) next.FontSize = 16;
            _playPauseGlyph.FontSize = 18;
            _playPauseGlyph.Foreground = Hex(pal.OnAccent ?? pal.Bg);
            HudSkipLabel(_skipBackBtn, "−10");
            HudSkipLabel(_skipFwdBtn, "+10");
            controls.Margin = new Thickness(0, 12, 0, 0);
            Grid.SetRow(controls, 0);
            controls.Children.Add(center);
            stack.Children.Add(controls);

            // ── Quellen als Reiter (RenderTabs) ──
            _hudTabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
            stack.Children.Add(_hudTabs);
        }

        private void HudSkipLabel(Border button, string text) => button.Child = new TextBlock
        {
            Text = text, FontFamily = _spec!.Font, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = _subBrush,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };

        /// <summary>Kartenhintergrund auf die abgeschrägte Fläche legen (die rechteckige Karte bleibt durchsichtig).</summary>
        private void ApplyHudBackground(double opacity)
        {
            _card.Background = Brushes.Transparent;
            if (_hudBg != null) _hudBg.Fill = _pal!.Background(opacity);
        }

        /// <summary>Titel und App in Großbuchstaben mit Sperrung.</summary>
        private void HudTexts(MediaInfo info)
        {
            _title.Text = HudParts.Caps(_title.Text);
            _appName.Text = HudParts.Caps(info.AppName);
        }

        /// <summary>Status oben rechts: grüner Punkt + „LÄUFT“ bzw. Pause-Symbol + „PAUSE“.</summary>
        private void UpdateHudStatus(bool playing)
        {
            if (_hudStatusText == null || _hudStatusDot == null || _hudStatusPause == null) return;
            _hudStatusText.Text = HudParts.Caps(playing ? Loc.T("Läuft", "Playing") : Loc.T("Pause", "Paused"));
            _hudStatusDot.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
            _hudStatusPause.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Fortschritts-Segmente bis zum Anteil fraction leuchten lassen.</summary>
        private void LayoutHudProgress(double fraction)
        {
            if (_hudProg == null) return;
            int lit = (int)Math.Round(Math.Clamp(fraction, 0, 1) * _hudProg.Length);
            for (int i = 0; i < _hudProg.Length; i++) _hudProg[i].Fill = i < lit ? _accent : Brushes.Transparent;
        }

        /// <summary>
        /// Quellen als Reiter: GROSSBUCHSTABEN, die gesteuerte Quelle in Akzentfarbe mit Unterstrich. Klick schaltet um; das kleine
        /// Pause-Symbol pausiert eine spielende Quelle direkt (wie bei den Pills).
        /// </summary>
        private void RenderTabs(List<MediaSource> sources)
        {
            if (_hudTabs == null) return;
            _hudTabs.Children.Clear();
            _hudTabs.Visibility = sources.Count < MinSources ? Visibility.Collapsed : Visibility.Visible;
            if (sources.Count < MinSources) return;

            string? activeAppId = ActiveSourceId;
            foreach (var src in sources)
            {
                bool active = src.AppId == activeAppId;
                Brush fg = active ? _accent : _subBrush;
                var label = new StackPanel { Orientation = Orientation.Horizontal };
                label.Children.Add(new TextBlock
                {
                    Text = HudParts.Caps(src.Label), FontFamily = _spec!.Font, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = fg,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                if (src.IsPlaying && _showSourcePause)
                {
                    var pauseDot = new TextBlock
                    {
                        Text = "\uF8AE", FontFamily = IconFont, FontSize = 9, Margin = new Thickness(6, 0, 0, 0), Foreground = fg, // PauseSolid
                        VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
                    };
                    pauseDot.MouseLeftButtonDown += (_, e) => e.Handled = true;
                    pauseDot.MouseLeftButtonUp += (_, e) =>
                    {
                        _media.PauseSource(src.Session);
                        e.Handled = true;
                        ResetHideTimer();
                    };
                    label.Children.Add(pauseDot);
                }
                var tab = new StackPanel { Margin = new Thickness(0, 0, 18, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent };
                tab.Children.Add(label);
                tab.Children.Add(new Border { Height = 2, Margin = new Thickness(0, 4, 0, 0), Background = active ? _accent : Brushes.Transparent });
                tab.MouseLeftButtonDown += (_, e) => e.Handled = true;
                tab.MouseLeftButtonUp += (_, e) =>
                {
                    if (!active) _media.SwitchTo(src.Session);
                    e.Handled = true;
                    ResetHideTimer();
                };
                _hudTabs.Children.Add(tab);
            }
        }
    }
}
