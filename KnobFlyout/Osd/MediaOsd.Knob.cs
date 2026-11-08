using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KnobFlyout.Osd
{
    /// <summary>Media-Flyout im Design „Drehknopf“ (Designs.Knob): schlichte Fläche, Play als runder Knopf, sonst flache Symbole.</summary>
    internal sealed partial class MediaOsd
    {
        private bool IsKnob => ReferenceEquals(_spec, Designs.Knob);

        /// <summary>
        /// Oben Cover 60 · Titel / Interpret / App-Zeile · „…“ rund; darunter dünne Leiste mit Zeiten darunter; dann
        /// −10 · Zurück · Play (runder Knopf mit Rand) · Weiter · +10; zuletzt die Quellen als Pills. Wie ArrangeMac:
        /// die fertigen Elemente aus dem Standardaufbau lösen und neu anordnen.
        /// </summary>
        private void ArrangeKnob(Grid root, Grid header, StackPanel left, StackPanel appRow, Grid controls, StackPanel center)
        {
            var pal = _pal!;
            var font = _spec!.Font;
            root.Children.Clear();
            root.RowDefinitions.Clear();
            header.Children.Clear();
            left.Children.Clear();
            appRow.Children.Clear();
            controls.Children.Clear();
            root.Margin = new Thickness(15, 15, 15, 13);
            var stack = new StackPanel();
            root.Children.Add(stack);

            // ── Kopf: Cover | Titel / Interpret / App | „…“ ──
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            const double art = 60;
            _art.Width = _art.Height = art;
            _art.CornerRadius = new CornerRadius(10);
            _art.Clip = new RectangleGeometry(new Rect(0, 0, art, art), 10, 10);
            _art.Background = Hex(pal.ArtPlaceholder);
            _art.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_art, 0);
            top.Children.Add(_art);

            _title.FontFamily = font;
            _title.FontSize = 15;
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
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 8, 0) };
            text.Children.Add(_title);
            text.Children.Add(_artist);
            text.Children.Add(appRow);
            Grid.SetColumn(text, 1);
            top.Children.Add(text);

            _moreBtn.Width = _moreBtn.Height = 26;
            _moreBtn.CornerRadius = new CornerRadius(13);
            _moreBtn.Background = Role(Config.MediaLayout.ColorButton, pal.Tile ?? pal.Button);
            _moreBtn.BorderBrush = Role(Config.MediaLayout.ColorEdge, pal.Edge);
            _moreBtn.BorderThickness = new Thickness(1);
            _moreBtn.HorizontalAlignment = HorizontalAlignment.Right;
            _moreBtn.VerticalAlignment = VerticalAlignment.Top;
            if (_moreBtn.Child is TextBlock dots) dots.FontSize = 12;
            _moreBtn.MouseEnter += (_, _) => _moreBtn.Opacity = 0.8;
            _moreBtn.MouseLeave += (_, _) => _moreBtn.Opacity = 1.0;
            Grid.SetColumn(_moreBtn, 2);
            top.Children.Add(_moreBtn);
            stack.Children.Add(top);
            _artBlock = _art;
            _textBlock = text;
            _infoRow = top;
            _infoExtra = _moreBtn;
            _moreHost = _moreBtn;
            _buttonsPanel = center;

            // ── Fortschritt: dünne Leiste, Zeiten darunter (Griff nur beim Überfahren, siehe Konstruktor) ──
            _seekHost.Height = 16;
            _seekHost.Margin = new Thickness(0, 10, 0, 0);
            foreach (var bar in _seekHost.Children.OfType<Border>())
            {
                bar.Height = 3;
                bar.CornerRadius = new CornerRadius(1.5);
            }
            _seekThumb.Width = _seekThumb.Height = 12;
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

            // ── Knöpfe: −10 · Zurück · Play (runder Knopf) · Weiter · +10 ──
            var buttons = center.Children.Cast<UIElement>().ToArray(); // Standard: Zurück, −10, Play, +10, Weiter
            center.Children.Clear();
            foreach (int i in new[] { 1, 0, 2, 4, 3 }) center.Children.Add(buttons[i]);
            foreach (var b in buttons.OfType<Border>()) b.Margin = new Thickness(5, 0, 5, 0);
            if (buttons[0] is Border { Child: TextBlock prev }) prev.FontSize = 18;
            if (buttons[4] is Border { Child: TextBlock next }) next.FontSize = 18;
            var playBody = Role(Config.MediaLayout.ColorButton, pal.Tile ?? pal.Button);
            _playBtn.Width = _playBtn.Height = 50;
            _playBtn.CornerRadius = new CornerRadius(25);
            _playBtn.Background = playBody;
            _playBtn.BorderBrush = Role(Config.MediaLayout.ColorEdge, pal.Edge);
            _playBtn.BorderThickness = new Thickness(1);
            _playBtn.MouseEnter += (_, _) => _playBtn.Opacity = 0.85;
            _playBtn.MouseLeave += (_, _) =>
            {
                _playBtn.Opacity = 1.0;
                _playBtn.Background = playBody; // MakeButton setzt beim Verlassen die Fläche des Standardaufbaus
            };
            _playPauseGlyph.FontSize = 22;
            KnobSkipLabel(_skipBackBtn, SkipText(-1));
            KnobSkipLabel(_skipFwdBtn, SkipText(+1));
            controls.Margin = new Thickness(0, 4, 0, 0);
            Grid.SetRow(controls, 0);
            controls.Children.Add(center);
            stack.Children.Add(controls);

            // ── Quellen als Pills (Standard; nur bei mehreren Quellen sichtbar) ──
            _sourcesRow.Margin = new Thickness(0, 10, 0, 0);
            Grid.SetRow(_sourcesRow, 0);
            stack.Children.Add(_sourcesRow);
        }

        private void KnobSkipLabel(Border button, string text) => button.Child = new TextBlock
        {
            Text = text, FontFamily = _spec!.Font, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = _subBrush,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
    }
}
