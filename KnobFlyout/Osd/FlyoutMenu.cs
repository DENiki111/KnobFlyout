using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace KnobFlyout.Osd
{
    /// <summary>Aussehen eines Menüs im Flyout-Stil, abgeleitet vom gewählten Media-Design (MediaOsd.MenuLookFor).</summary>
    internal sealed record MenuLook(Brush Background, Brush Edge, Brush Fg, bool Light, double ContentOpacity, FontFamily? Font, double Radius);

    /// <summary>
    /// Menü im Stil des Media-Flyouts: das „…“-Menü und das Menü am Tray-Symbol. Farben, Deckkraft, Schrift und Ecken
    /// kommen vom Design. Build() liefert nur den Inhalt, das Öffnen und Schließen übernimmt der Aufrufer.
    /// </summary>
    internal sealed class FlyoutMenu
    {
        private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

        private readonly StackPanel _list = new() { Margin = new Thickness(4) };
        private readonly MenuLook _look;
        private readonly Action _close;
        private readonly Action? _afterClick;
        private readonly Brush _hover, _separator;

        /// <param name="close">Schließt das Menü, bevor die gewählte Aktion läuft.</param>
        /// <param name="afterClick">Läuft nach jeder gewählten Aktion.</param>
        public FlyoutMenu(MenuLook look, Action close, Action? afterClick = null)
        {
            _look = look;
            _close = close;
            _afterClick = afterClick;
            _hover = UiColor.Brush(look.Light ? "#14000000" : "#1AFFFFFF");
            _separator = UiColor.Brush(look.Light ? "#1F000000" : "#26FFFFFF");
        }

        public bool IsEmpty => _list.Children.Count == 0;

        /// <summary>Trennlinie nur zwischen Gruppen, nie am Anfang.</summary>
        public void Group()
        {
            if (!IsEmpty) _list.Children.Add(new Border { Height = 1, Background = _separator, Margin = new Thickness(8, 4, 8, 4) });
        }

        /// <summary>Eine Menüzeile: links Häkchen (aktiv) bzw. Symbol, daneben der Text.</summary>
        public void Row(string label, string? glyph, bool isChecked, bool enabled, Action onClick)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            string? icon = isChecked ? "\uE73E" : glyph; // CheckMark
            if (icon != null)
            {
                grid.Children.Add(new TextBlock
                {
                    Text = icon, FontFamily = IconFont, FontSize = 12, Foreground = _look.Fg,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            var text = new TextBlock { Text = label, FontSize = 13, Foreground = _look.Fg, VerticalAlignment = VerticalAlignment.Center };
            if (_look.Font != null) text.FontFamily = _look.Font;
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var row = new Border
            {
                Child = grid, Padding = new Thickness(10, 7, 16, 7), CornerRadius = new CornerRadius(Math.Min(_look.Radius, 4)),
                Background = Brushes.Transparent, Opacity = enabled ? 1.0 : 0.4,
                Cursor = enabled ? Cursors.Hand : Cursors.Arrow,
            };
            if (enabled)
            {
                row.MouseEnter += (_, _) => row.Background = _hover;
                row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
                row.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    _close();
                    onClick();
                    _afterClick?.Invoke();
                };
            }
            _list.Children.Add(row);
        }

        public Border Build()
        {
            _list.Opacity = _look.ContentOpacity;
            return new Border
            {
                MinWidth = 210, CornerRadius = new CornerRadius(_look.Radius), Margin = new Thickness(8), // Rand = Platz für den Schatten
                Background = _look.Background,
                BorderBrush = _look.Edge, BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Direction = 270, Opacity = 0.35 },
                Child = _list,
            };
        }
    }
}
