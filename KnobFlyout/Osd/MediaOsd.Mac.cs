using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using KnobFlyout.Media;

namespace KnobFlyout.Osd
{
    /// <summary>Media-Flyout im Design „macOS“ (Designs.Mac): „Jetzt läuft“ wie im Control Center.</summary>
    internal sealed partial class MediaOsd
    {
        private bool IsMac => ReferenceEquals(_spec, Designs.Mac);

        private Border? _segHost;      // Segment-Steuerung für die Quellen (statt Pills)
        private UniformGrid? _segGrid;
        private bool _fadingOut;       // Design-System: verblasst gerade (Ausblenden nach Ablauf der Anzeigedauer)

        /// <summary>
        /// Oben Cover 64 · Titel / Interpret / App-Zeile (Logo + Name) · „…“ rund; darunter dünne Leiste mit Zeiten
        /// darunter (Restzeit mit Minus); dann −10 · Zurück · Play/Pause · Weiter · +10 (±10 als Kreispfeil mit „10“);
        /// zuletzt die Quellen als Segment-Steuerung (nur bei mehreren Quellen). Wie ArrangeSpec: die fertigen
        /// Elemente aus dem Standardaufbau lösen und neu anordnen.
        /// </summary>
        private void ArrangeMac(Grid root, Grid header, StackPanel left, StackPanel appRow, Grid controls, StackPanel center)
        {
            var pal = _pal!;
            var font = _spec!.Font;
            root.Children.Clear();
            root.RowDefinitions.Clear();
            header.Children.Clear();
            left.Children.Clear();
            appRow.Children.Clear();
            controls.Children.Clear();
            root.Margin = new Thickness(13, 13, 13, 12); // + 1 px Rand = 14 px
            var stack = new StackPanel();
            root.Children.Add(stack);

            // ── Kopf: Cover | Titel / Interpret / App | „…“ ──
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            const double art = 64;
            _art.Width = _art.Height = art;
            _art.CornerRadius = new CornerRadius(8);
            _art.Clip = new RectangleGeometry(new Rect(0, 0, art, art), 8, 8);
            _art.Background = Hex(pal.ArtPlaceholder);
            _art.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_art, 0);
            top.Children.Add(_art);

            _title.FontFamily = font;
            _title.FontSize = 14;
            _title.Margin = new Thickness(0);
            _artist.FontFamily = font;
            _artist.FontSize = 13;
            _artist.Margin = new Thickness(0, 1, 0, 0);
            _appIcon.Width = _appIcon.Height = 14;
            _appIcon.Clip = new RectangleGeometry(new Rect(0, 0, 14, 14), 3.5, 3.5);
            _appGlyph.FontSize = 12;
            _appGlyph.Foreground = _subBrush;
            _appName.FontFamily = font;
            _appName.FontSize = 11;
            _appName.Foreground = _subBrush;
            _appName.Margin = new Thickness(5, 0, 0, 0);
            appRow.Margin = new Thickness(0, 5, 0, 0);
            appRow.HorizontalAlignment = HorizontalAlignment.Left;
            appRow.Children.Add(_appIcon);
            appRow.Children.Add(_appGlyph);
            appRow.Children.Add(_appName);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
            text.Children.Add(_title);
            text.Children.Add(_artist);
            text.Children.Add(appRow);
            Grid.SetColumn(text, 1);
            top.Children.Add(text);

            _moreBtn.Width = _moreBtn.Height = 24;
            _moreBtn.CornerRadius = new CornerRadius(12);
            _moreBtn.Background = Hex(pal.Button);
            _moreBtn.HorizontalAlignment = HorizontalAlignment.Right;
            _moreBtn.VerticalAlignment = VerticalAlignment.Top;
            if (_moreBtn.Child is TextBlock dots) dots.FontSize = 12;
            _moreBtn.MouseEnter += (_, _) => _moreBtn.Opacity = 0.8;
            _moreBtn.MouseLeave += (_, _) => _moreBtn.Opacity = 1.0;
            Grid.SetColumn(_moreBtn, 2);
            top.Children.Add(_moreBtn);
            stack.Children.Add(top);

            // ── Fortschritt: dünne Leiste, Zeiten darunter (Griff nur beim Überfahren, siehe Konstruktor) ──
            _seekHost.Height = 16;
            _seekHost.Margin = new Thickness(0, 8, 0, 0);
            _seekFill.Background = Hex(pal.Scrub ?? pal.Fill);
            _seekThumb.Width = _seekThumb.Height = 12;
            _seekThumb.Fill = Brushes.White;
            _seekThumb.Stroke = Hex("#33000000");
            _seekThumb.StrokeThickness = 1;
            _seekThumb.Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Direction = 270, Opacity = 0.35 };
            Grid.SetRow(_seekHost, 0);
            foreach (var t in new[] { _elapsed, _total })
            {
                t.FontFamily = font;
                t.FontSize = 11;
                System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            }
            _timesRow.Margin = new Thickness(0);
            Grid.SetRow(_timesRow, 0);
            var progress = new StackPanel();
            progress.Children.Add(_seekHost);
            progress.Children.Add(_timesRow);
            _progressBlock = progress;
            stack.Children.Add(progress);

            // ── Knöpfe: −10 · Zurück · Play/Pause · Weiter · +10 ──
            var buttons = center.Children.Cast<UIElement>().ToArray(); // Standard: Zurück, −10, Play, +10, Weiter
            center.Children.Clear();
            foreach (int i in new[] { 1, 0, 2, 4, 3 }) center.Children.Add(buttons[i]);
            foreach (var b in buttons.OfType<Border>()) b.Margin = new Thickness(4, 0, 4, 0);
            if (buttons[0] is Border { Child: TextBlock prev }) prev.FontSize = 22;
            if (buttons[4] is Border { Child: TextBlock next }) next.FontSize = 22;
            _playPauseGlyph.FontSize = 30;
            MacSkipIcon(_skipBackBtn, "\uE777"); // Kreispfeil gegen den Uhrzeigersinn
            MacSkipIcon(_skipFwdBtn, "\uE72C");  // Kreispfeil im Uhrzeigersinn
            controls.Margin = new Thickness(0, 2, 0, 0);
            Grid.SetRow(controls, 0);
            controls.Children.Add(center);
            stack.Children.Add(controls);

            // ── Quellen: Segment-Steuerung (RenderSegments blendet sie ohne mehrere Quellen aus) ──
            _segGrid = new UniformGrid { Rows = 1 };
            _segHost = new Border
            {
                Background = Hex(pal.Seg ?? pal.Button), CornerRadius = new CornerRadius(7), Padding = new Thickness(2),
                Margin = new Thickness(0, 10, 0, 0), Child = _segGrid, Visibility = Visibility.Collapsed,
            };
            stack.Children.Add(_segHost);

            // Lichtkante innen über allem (fängt keine Maus ab)
            if (pal.HighlightOverlay(_radius - 1) is { } hi) _host.Children.Add(hi);
        }

        /// <summary>±10-s-Knopf wie die SF-Symbole „gobackward.10“/„goforward.10“: Kreispfeil mit „10“ in der Mitte.</summary>
        private void MacSkipIcon(Border button, string arrow)
        {
            var g = new Grid();
            g.Children.Add(new TextBlock
            {
                Text = arrow, FontFamily = IconFont, FontSize = 24, Foreground = _subBrush,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
            g.Children.Add(new TextBlock
            {
                Text = "10", FontFamily = _spec!.Font, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = _subBrush,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0),
            });
            button.Child = g;
        }

        /// <summary>
        /// Quellen als macOS-Segment-Steuerung: gleich breite Segmente, die gesteuerte Quelle erhaben hinterlegt.
        /// Klick schaltet um; der kleine Pause-Punkt pausiert eine spielende Quelle direkt (wie bei den Pills).
        /// </summary>
        private void RenderSegments(List<MediaSource> sources)
        {
            if (_segGrid == null || _segHost == null) return;
            _segGrid.Children.Clear();
            _segHost.Visibility = sources.Count < 2 ? Visibility.Collapsed : Visibility.Visible;
            if (sources.Count < 2) return;

            var pal = _pal!;
            string? activeAppId = _media.CurrentAppId;
            foreach (var src in sources)
            {
                bool active = src.AppId == activeAppId;
                var fg = active ? Hex(pal.SegOnText ?? pal.Fg) : _fgBrush;
                var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                row.Children.Add(new TextBlock
                {
                    Text = src.Label, FontFamily = _spec!.Font, FontSize = 12, FontWeight = FontWeights.Medium, Foreground = fg,
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                });
                if (src.IsPlaying)
                {
                    var pauseDot = new TextBlock
                    {
                        Text = "\uE769", FontFamily = IconFont, FontSize = 10, Margin = new Thickness(5, 0, 0, 0), Foreground = fg,
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

                // Schatten nur auf der Fläche des gewählten Segments, nicht auf dem Text.
                var seg = new Grid { Cursor = Cursors.Hand, Background = Brushes.Transparent };
                if (active)
                {
                    seg.Children.Add(new Border
                    {
                        CornerRadius = new CornerRadius(5), Background = Hex(pal.SegOn ?? pal.Fill),
                        Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Direction = 270, Opacity = 0.25 },
                    });
                }
                seg.Children.Add(new Border { Padding = new Thickness(6, 3, 6, 4), Child = row });
                seg.MouseLeftButtonDown += (_, e) => e.Handled = true;
                seg.MouseLeftButtonUp += (_, e) =>
                {
                    if (!active) _media.SwitchTo(src.Session);
                    e.Handled = true;
                    ResetHideTimer();
                };
                _segGrid.Children.Add(seg);
            }
        }

        /// <summary>Design-System: nach Ablauf der Anzeigedauer in HideMs verblassen (macOS langsam), dann ausblenden.</summary>
        private void FadeOutThenHide()
        {
            _fadingOut = true;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(_spec!.HideMs))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            };
            fade.Completed += (_, _) =>
            {
                if (!_fadingOut) return; // inzwischen neue Meldung bzw. sofort ausgeblendet
                _fadingOut = false;
                Hide();
                _card.BeginAnimation(OpacityProperty, null);
                _card.Opacity = 1;
            };
            _card.BeginAnimation(OpacityProperty, fade);
        }

        /// <summary>Laufendes Verblassen abbrechen (neue Meldung oder sofortiges Ausblenden): Karte wieder deckend.</summary>
        private void CancelFadeOut()
        {
            if (!_fadingOut) return;
            _fadingOut = false;
            _card.BeginAnimation(OpacityProperty, null);
            _card.Opacity = 1;
        }
    }
}
