using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KnobFlyout.Config;
using KnobFlyout.Profiles;

namespace KnobFlyout.Osd
{
    /// <summary>Neue-App-Popup im Design „macOS“ (Designs.Mac): Mitteilung mit App-Symbol, darunter Kacheln wie im Dock.</summary>
    internal sealed partial class NewAppOsd
    {
        /// <summary>
        /// Mitteilung: KnobFlyout-Symbol · „Neue App erkannt“ + App-Name · „jetzt“; darunter ⊘-Kachel und je Profil eine
        /// App-Kachel (Profilfarbe mit weißem Logo bzw. eigenes Bild). Farben, Radius und Schrift aus DesignSpec.
        /// </summary>
        private Border BuildMac(AppConfig cfg, DesignSpec spec, double alpha, string processName)
        {
            var pal = OsdDesignColors.Palette(cfg, spec, OsdDesignColors.Light(cfg));
            var fg = UiColor.Brush(pal.Fg);
            var root = new StackPanel { Margin = new Thickness(13, 11, 13, 0), Opacity = _contentAlpha };

            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new Image { Source = Tray.AppIcon.Render(76), Width = 38, Height = 38, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            top.Children.Add(icon);

            var text = new StackPanel { Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = Loc.T("Neue App erkannt", "New app detected"), FontFamily = spec.Font, FontSize = 13,
                FontWeight = FontWeights.SemiBold, Foreground = fg,
            });
            text.Children.Add(new TextBlock
            {
                Text = processName.Replace(".exe", ""), FontFamily = spec.Font, FontSize = 13, Foreground = fg,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0),
            });
            Grid.SetColumn(text, 1);
            top.Children.Add(text);

            var when = new TextBlock
            {
                Text = Loc.T("jetzt", "now"), FontFamily = spec.Font, FontSize = 11, Foreground = UiColor.Brush(pal.Sub),
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0),
            };
            Grid.SetColumn(when, 2);
            top.Children.Add(when);
            root.Children.Add(top);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 11, 0, 0) };
            var profiles = ProfileDefinitions.For(cfg);
            int count = profiles.Count + 1; // ⊘ + bis zu 6 Profile passen immer in die Breite
            double size = Math.Min(34, (spec.PopupWidth - 28 - 8.0 * (count - 1)) / count);
            row.Children.Add(TileButton(MacParts.GlyphTile("\uE733", size, 16, UiColor.Brush(pal.Tile ?? pal.Button), fg), // Blocked
                Exclude, Loc.T("Ignorieren", "Ignore")));
            foreach (var p in profiles)
            {
                string key = p.Key;
                string hotkey = cfg.ProfileList.FirstOrDefault(e => e.Key == key)?.AssignHotkey ?? "";
                row.Children.Add(TileButton(MacParts.ProfileTile(p, size, 16), () => AssignTo(key),
                    hotkey.Length > 0 ? $"{p.Name} ({hotkey})" : p.Name));
            }
            root.Children.Add(row);

            return new Border
            {
                CornerRadius = new CornerRadius(spec.PopupRadius), Background = pal.Background(alpha),
                BorderBrush = UiColor.Brush(pal.Edge), BorderThickness = new Thickness(1),
                Child = pal.WithHighlight(root, spec.PopupRadius - 1),
            };
        }

        /// <summary>Kachel als Knopf (beim Überfahren leicht abgeblendet); Klick erst beim Loslassen.</summary>
        private static Border TileButton(Border tile, Action onClick, string tooltip)
        {
            tile.Margin = new Thickness(0, 0, 8, 0);
            tile.Cursor = System.Windows.Input.Cursors.Hand;
            tile.ToolTip = tooltip;
            tile.MouseEnter += (_, _) => tile.Opacity = 0.85;
            tile.MouseLeave += (_, _) => tile.Opacity = 1.0;
            tile.MouseLeftButtonDown += (_, e) => e.Handled = true;
            tile.MouseLeftButtonUp += (_, e) => { onClick(); e.Handled = true; };
            return tile;
        }
    }
}
