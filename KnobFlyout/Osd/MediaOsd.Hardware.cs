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
    /// <summary>Media-Flyout im Design „Hardware-Regler“ (Designs.Hardware): Display oben, Gerätetasten, Eingangswahl.</summary>
    internal sealed partial class MediaOsd
    {
        private bool IsHardware => ReferenceEquals(_spec, Designs.Hardware);

        private Border? _hwFill;          // Fortschritt im Display
        private TextBlock? _hwStatus;     // Play/Pause-Symbol oben rechts im Display
        private Rectangle? _hwPlayLed;    // LED auf der Play-Taste
        private StackPanel? _hwInputs;
        private HwColors? _hw;

        /// <summary>
        /// Display (bernstein, Mono): Cover 56 · TITEL / INTERPRET / APP + Zeit, Status oben rechts, Leiste darunter (klick-/ziehbar);
        /// darunter Gerätetasten −10 · Zurück · Play (LED) · Weiter · +10 und „…“ rechts; zuletzt die Eingangswahl (mehrere Quellen).
        /// Wie ArrangeSpec: die fertigen Elemente aus dem Standardaufbau lösen und neu anordnen.
        /// </summary>
        private void ArrangeHardware(Grid root, Grid header, StackPanel left, StackPanel appRow, Grid controls, StackPanel center)
        {
            var pal = _pal!;
            var hw = _hw = OwnHwColors(HardwareParts.Colors(_light));
            var lcdFont = HardwareParts.LcdFont;
            var amber = Hex(hw.LcdText);
            root.Children.Clear();
            root.RowDefinitions.Clear();
            header.Children.Clear();
            left.Children.Clear();
            appRow.Children.Clear();
            controls.Children.Clear();
            root.Margin = new Thickness(13, 13, 13, 12);
            var stack = new StackPanel();
            root.Children.Add(stack);

            // ── Display ──
            var screen = new StackPanel();
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            const double art = 56;
            _art.Width = _art.Height = art;
            _art.CornerRadius = new CornerRadius(3);
            _art.Clip = new RectangleGeometry(new Rect(0, 0, art, art), 3, 3);
            _art.Background = Hex(pal.ArtPlaceholder);
            _art.Margin = new Thickness(0, 0, 10, 0);
            _art.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_art, 0);
            top.Children.Add(_art);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Effect = HardwareParts.AmberGlow(hw) };
            foreach (var t in new[] { _title, _artist, _appName, _elapsed, _total })
            {
                t.FontFamily = lcdFont;
                t.Foreground = amber;
            }
            _title.FontSize = 14;
            _title.FontWeight = FontWeights.Bold;
            _title.Margin = new Thickness(0, 0, 16, 0); // Platz für das Status-Symbol
            _artist.FontSize = 13;
            _artist.Opacity = 0.75;
            _artist.Margin = new Thickness(0, 1, 0, 0);
            _appName.FontSize = 12;
            _appName.Margin = new Thickness(0);
            _appName.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(_title);
            text.Children.Add(_artist);
            var info = new Grid { Opacity = 0.75, Margin = new Thickness(0, 1, 0, 0) };
            info.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            info.Children.Add(_appName);
            _timesRow.Children.Clear();
            _timesRow.Margin = new Thickness(8, 0, 0, 0);
            var times = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var t in new[] { _elapsed, _total })
            {
                t.FontSize = 12;
                t.Margin = new Thickness(0);
                t.HorizontalAlignment = HorizontalAlignment.Left;
            }
            times.Children.Add(_elapsed);
            times.Children.Add(new TextBlock { Text = " / ", FontFamily = lcdFont, FontSize = 12, Foreground = amber });
            times.Children.Add(_total);
            _timesRow.Children.Add(times);
            Grid.SetRow(_timesRow, 0);
            Grid.SetColumn(_timesRow, 1);
            info.Children.Add(_timesRow);
            text.Children.Add(info);
            Grid.SetColumn(text, 1);
            top.Children.Add(text);
            _artBlock = _art;
            _textBlock = text;
            _infoRow = top;

            var screenTop = new Grid();
            screenTop.Children.Add(top);
            _hwStatus = new TextBlock
            {
                FontFamily = IconFont, FontSize = 11, Foreground = amber, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0), Effect = HardwareParts.AmberGlow(hw),
            };
            screenTop.Children.Add(_hwStatus);
            screen.Children.Add(screenTop);

            // Leiste im Display: Spur + Füllung (Klicken/Ziehen über _seekHost wie gehabt)
            _seekHost.Height = 12;
            _seekHost.Margin = new Thickness(0, 5, 0, 0);
            foreach (UIElement child in _seekHost.Children) child.Visibility = Visibility.Collapsed; // Standard-Spur, Füllung, Griff
            _seekHost.Children.Add(new Border
            {
                Height = 4, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
                Background = Hex(Own(Config.MediaLayout.ColorTrack) ?? "#1F" + NormHex(hw.LcdText, "#FFB04A")[1..]),
            });
            _hwFill = new Border
            {
                Height = 4, Width = 0, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
                Background = amber, Effect = HardwareParts.AmberGlow(hw, 0.7), IsHitTestVisible = false,
            };
            _seekHost.Children.Add(_hwFill);
            Grid.SetRow(_seekHost, 0);
            screen.Children.Add(_seekHost);
            _progressBlock = _seekHost;
            stack.Children.Add(HardwareParts.Lcd(hw, pal.Hi ?? "#00000000", screen, new Thickness(10, 10, 10, 6)));

            // ── Gerätetasten ──
            var buttons = center.Children.Cast<UIElement>().ToArray(); // Standard: Zurück, −10, Play, +10, Weiter
            center.Children.Clear();
            center.HorizontalAlignment = HorizontalAlignment.Left;
            foreach (int i in new[] { 1, 0, 2, 4, 3 })
            {
                var inner = (Border)buttons[i];
                bool play = i == 2;
                var key = HardwareParts.Key(hw, play ? 58 : 46, 36, out var face);
                key.Margin = new Thickness(0, 0, 8, 0);
                PrepareKeyButton(inner);
                var content = new Grid();
                content.Children.Add(inner);
                if (play)
                {
                    _hwPlayLed = HardwareParts.KeyLed(22, false, _accent, hw);
                    content.Children.Add(_hwPlayLed);
                }
                face.Child = content;
                key.SetBinding(VisibilityProperty, new Binding(nameof(Visibility)) { Source = inner }); // ±10 per Einstellung ausblendbar
                center.Children.Add(key);
            }
            if (buttons[0] is Border { Child: TextBlock prev }) prev.FontSize = 15;
            if (buttons[4] is Border { Child: TextBlock next }) next.FontSize = 15;
            _playPauseGlyph.FontSize = 15;
            HwSkipLabel(_skipBackBtn, "−10");
            HwSkipLabel(_skipFwdBtn, "+10");
            controls.ColumnDefinitions.Clear();
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.Margin = new Thickness(0, 12, 0, 0);
            Grid.SetRow(controls, 0);
            controls.Children.Add(center);

            var moreKey = HardwareParts.Key(hw, 36, 36, out var moreFace);
            PrepareKeyButton(_moreBtn);
            if (_moreBtn.Child is TextBlock dots) dots.FontSize = 13;
            var hover = Hex(pal.Button);
            _moreBtn.MouseEnter += (_, _) => _moreBtn.Background = hover;
            _moreBtn.MouseLeave += (_, _) => _moreBtn.Background = Brushes.Transparent;
            moreFace.Child = _moreBtn;
            moreKey.SetBinding(VisibilityProperty, new Binding(nameof(Visibility)) { Source = _moreBtn });
            Grid.SetColumn(moreKey, 1);
            controls.Children.Add(moreKey);
            stack.Children.Add(controls);
            _moreHost = moreKey;
            _buttonsPanel = center;

            // ── Eingangswahl (RenderInputs) ──
            _hwInputs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 12, 0, 0), Visibility = Visibility.Collapsed };
            stack.Children.Add(_hwInputs);

            // Lichtkante und Schrauben über allem (fangen keine Maus ab)
            if (pal.HighlightOverlay(_radius - 1) is { } hi) _host.Children.Add(hi);
            var screws = HardwareParts.Screws(0, 0, hw);
            screws.Width = screws.Height = double.NaN; // an den Kartenecken ausrichten
            _host.Children.Add(screws);
        }

        /// <summary>Eigene Farben auf Display und Tasten anwenden.</summary>
        private HwColors OwnHwColors(HwColors hw)
        {
            if (Own(Config.MediaLayout.ColorDisplay) is { } text) hw = hw with { LcdText = text };
            if (Own(Config.MediaLayout.ColorDisplayBg) is { } lcd) hw = hw with { Lcd = lcd };
            if (Own(Config.MediaLayout.ColorButton) is { } key)
                hw = hw with { KeyTop = key, KeyBottom = Mix(Hex(key).Color, Colors.Black, 0.12).ToString() };
            return hw;
        }

        /// <summary>Knopf aus dem Standardaufbau füllt die Tastenoberseite (Hover-Fläche mit gleicher Rundung).</summary>
        private static void PrepareKeyButton(Border inner)
        {
            inner.Width = inner.Height = double.NaN;
            inner.Margin = new Thickness(0);
            inner.CornerRadius = new CornerRadius(5);
            inner.Background = Brushes.Transparent;
            inner.HorizontalAlignment = HorizontalAlignment.Stretch;
            inner.VerticalAlignment = VerticalAlignment.Stretch;
        }

        private void HwSkipLabel(Border button, string text) => button.Child = new TextBlock
        {
            Text = text, FontFamily = _spec!.Font, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = _subBrush,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };

        /// <summary>Display-Texte in Großbuchstaben (Interpret ohne App, die steht in eigener Zeile).</summary>
        private void HwTexts(MediaInfo info)
        {
            _title.Text = _title.Text.ToUpperInvariant();
            _artist.Text = info.Artist.ToUpperInvariant();
            _appName.Text = info.AppName.ToUpperInvariant();
        }

        /// <summary>Status im Display (▶ spielt / ❚❚ pausiert) und LED auf der Play-Taste.</summary>
        private void UpdateHwStatus(bool playing)
        {
            if (_hwStatus == null || _hwPlayLed == null || _hw == null) return;
            _hwStatus.Text = playing ? "\uF5B0" : "\uF8AE"; // PlaySolid : PauseSolid
            _hwPlayLed.Fill = playing ? _accent : Hex(_hw.LedOff);
            _hwPlayLed.Effect = playing ? HudParts.Glow(_accent.Color, 0.9, 5) : null;
        }

        private void LayoutHwProgress(double fraction)
        {
            if (_hwFill == null) return;
            double w = _seekHost.ActualWidth > 0 ? _seekHost.ActualWidth : W - 46;
            _hwFill.Width = Math.Max(0, w * Math.Clamp(fraction, 0, 1));
        }

        /// <summary>
        /// Quellen als Eingangswahl wie am Verstärker: LED-Punkt (gesteuerte Quelle leuchtet orange) + QUELLE. Klick schaltet um;
        /// das kleine Pause-Symbol pausiert eine spielende Quelle direkt (wie bei den Pills).
        /// </summary>
        private void RenderInputs(List<MediaSource> sources)
        {
            if (_hwInputs == null || _hw == null) return;
            _hwInputs.Children.Clear();
            _hwInputs.Visibility = sources.Count < MinSources ? Visibility.Collapsed : Visibility.Visible;
            if (sources.Count < MinSources) return;

            string? activeAppId = ActiveSourceId;
            foreach (var src in sources)
            {
                bool active = src.AppId == activeAppId;
                Brush fg = active ? _fgBrush : _subBrush;
                var item = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent,
                };
                item.Children.Add(new Ellipse
                {
                    Width = 7, Height = 7, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
                    Fill = active ? _accent : Hex(_hw.LedOff), Effect = active ? HudParts.Glow(_accent.Color, 0.9, 6) : null,
                });
                item.Children.Add(new TextBlock
                {
                    Text = HudParts.Caps(src.Label), FontFamily = _spec!.Font, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = fg,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                if (src.IsPlaying && _showSourcePause)
                {
                    var pauseDot = new TextBlock
                    {
                        Text = "\uF8AE", FontFamily = IconFont, FontSize = 8, Margin = new Thickness(6, 0, 0, 0), Foreground = fg, // PauseSolid
                        VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
                    };
                    pauseDot.MouseLeftButtonDown += (_, e) => e.Handled = true;
                    pauseDot.MouseLeftButtonUp += (_, e) =>
                    {
                        _media.PauseSource(src.Session);
                        e.Handled = true;
                        ResetHideTimer();
                    };
                    item.Children.Add(pauseDot);
                }
                item.MouseLeftButtonDown += (_, e) => e.Handled = true;
                item.MouseLeftButtonUp += (_, e) =>
                {
                    if (!active) _media.SwitchTo(src.Session);
                    e.Handled = true;
                    ResetHideTimer();
                };
                _hwInputs.Children.Add(item);
            }
        }
    }
}
