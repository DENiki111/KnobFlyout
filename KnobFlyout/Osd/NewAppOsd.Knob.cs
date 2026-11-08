using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KnobFlyout.Config;
using KnobFlyout.Profiles;

namespace KnobFlyout.Osd
{
    /// <summary>Neue-App-Popup im Design „Drehknopf“ (Designs.Knob): App-Name in normaler Schrift, darunter runde Knöpfe mit Profilring.</summary>
    internal sealed partial class NewAppOsd
    {
        /// <summary>„Neue App erkannt“ + App-Name; darunter ⊘ und je Profil ein runder Knopf (Ring und Logo in Profilfarbe).</summary>
        private Border BuildKnob(AppConfig cfg, DesignSpec spec, double alpha, string processName)
        {
            var pal = OsdDesignColors.Palette(cfg, spec, OsdDesignColors.Light(cfg));
            var fg = UiColor.Brush(pal.Fg);
            var body = UiColor.Brush(pal.Tile ?? pal.Button);
            var root = new StackPanel { Margin = new Thickness(15, 12, 15, 0), Opacity = _contentAlpha };
            root.Children.Add(new TextBlock
            {
                Text = Loc.T("Neue App erkannt", "New app detected"), FontFamily = spec.Font, FontSize = 12, Foreground = UiColor.Brush(pal.Sub),
            });
            root.Children.Add(new TextBlock
            {
                Text = processName.Replace(".exe", ""), FontFamily = spec.Font, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = fg,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0),
            });

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 11, 0, 0) };
            var profiles = ProfileDefinitions.For(cfg);
            int count = profiles.Count + 1; // ⊘ + bis zu 6 Profile passen immer in die Breite
            double size = Math.Min(38, (spec.PopupWidth - 30 - 8.0 * (count - 1)) / count);
            row.Children.Add(KnobButton(KnobParts.Round(size, body, UiColor.Brush(pal.Edge), new TextBlock
            {
                Text = "\uE733", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 15, Foreground = fg, // Blocked
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            }), Exclude, Loc.T("Ignorieren", "Ignore")));
            foreach (var p in profiles)
            {
                string key = p.Key;
                var color = UiColor.Brush(p.Color);
                var logo = p.CreateLogo(16, color);
                if (logo is Image) logo.Width = logo.Height = 20;
                logo.HorizontalAlignment = HorizontalAlignment.Center;
                logo.VerticalAlignment = VerticalAlignment.Center;
                var knob = KnobParts.Round(size, body, color, logo);
                ((System.Windows.Shapes.Ellipse)knob.Children[0]).StrokeThickness = 2.5; // Ring in Profilfarbe
                string hotkey = cfg.ProfileList.FirstOrDefault(e => e.Key == key)?.AssignHotkey ?? "";
                row.Children.Add(KnobButton(knob, () => AssignTo(key), hotkey.Length > 0 ? $"{p.Name} ({hotkey})" : p.Name));
            }
            root.Children.Add(row);

            return new Border
            {
                CornerRadius = new CornerRadius(spec.PopupRadius), Background = pal.Background(alpha),
                BorderBrush = UiColor.Brush(pal.Edge), BorderThickness = new Thickness(1), Child = root,
            };
        }

        private static Border KnobButton(Grid knob, Action onClick, string tooltip) =>
            TileButton(new Border { Background = Brushes.Transparent, CornerRadius = new CornerRadius(knob.Width / 2), Child = knob }, onClick, tooltip);
    }
}
