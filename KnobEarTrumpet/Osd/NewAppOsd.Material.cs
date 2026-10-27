using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KnobEarTrumpet.Config;
using KnobEarTrumpet.Profiles;

namespace KnobEarTrumpet.Osd
{
    /// <summary>Neue-App-Popup im Design „Material You“ (Designs.Material): Dialog mit verbundener Knopfgruppe.</summary>
    internal sealed partial class NewAppOsd
    {
        /// <summary>
        /// „Neue App erkannt“ · App-Name; darunter eine verbundene Knopfgruppe (außen rund, innen kleine Rundung, 2 px Lücke):
        /// ⊘ in neutraler Fläche, je Profil eine gedämpfte Fläche der Profilfarbe mit dem Logo in kräftiger Profilfarbe.
        /// </summary>
        private Border BuildMaterial(AppConfig cfg, DesignSpec spec, double alpha, string processName)
        {
            bool light = ThemeHelper.IsLight(cfg);
            var pal = spec.Palette(light);
            var root = new StackPanel { Margin = new Thickness(15, 15, 15, 0) };
            root.Children.Add(new TextBlock
            {
                Text = Loc.T("Neue App erkannt", "New app detected"), FontFamily = spec.Font, FontSize = 13, Foreground = UiColor.Brush(pal.Sub),
            });
            root.Children.Add(new TextBlock
            {
                Text = processName.Replace(".exe", ""), FontFamily = spec.Font, FontSize = 18, FontWeight = FontWeights.SemiBold,
                Foreground = UiColor.Brush(pal.Fg), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0),
            });

            var profiles = ProfileDefinitions.For(cfg);
            var group = new Grid { Height = 48, Margin = new Thickness(0, 12, 0, 0) };
            int count = profiles.Count + 1;
            for (int i = 0; i < count; i++) group.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            void Add(int index, UIElement content, string bgHex, Action onClick, string tooltip)
            {
                // Außen voll rund (24), innen kleine Rundung (8) – wie die verbundenen Knopfgruppen von Material 3 Expressive.
                double l = index == 0 ? 24 : 8, r = index == count - 1 ? 24 : 8;
                var b = new Border
                {
                    CornerRadius = new CornerRadius(l, r, r, l), Background = UiColor.Brush(bgHex), Child = content,
                    Margin = new Thickness(index == 0 ? 0 : 1, 0, index == count - 1 ? 0 : 1, 0),
                    Cursor = System.Windows.Input.Cursors.Hand, ToolTip = tooltip,
                };
                b.MouseEnter += (_, _) => b.Opacity = 0.85;
                b.MouseLeave += (_, _) => b.Opacity = 1.0;
                b.MouseLeftButtonDown += (_, e) => e.Handled = true;
                b.MouseLeftButtonUp += (_, e) => { onClick(); e.Handled = true; };
                Grid.SetColumn(b, index);
                group.Children.Add(b);
            }

            Add(0, Glyph("\uE733", pal.Sub), pal.Tile ?? pal.Button, Exclude, Loc.T("Ignorieren", "Ignore")); // Blocked
            for (int i = 0; i < profiles.Count; i++)
            {
                var p = profiles[i];
                string key = p.Key;
                string container = MaterialColors.Tone(p.Color, light ? 90 : 30, 0.11);
                string icon = MaterialColors.Tone(p.Color, light ? 40 : 80, 0.17);
                var logo = p.CreateLogo(18, UiColor.Brush(icon));
                if (logo is Image) logo.Width = logo.Height = 22;
                string hotkey = cfg.ProfileList.FirstOrDefault(e => e.Key == key)?.AssignHotkey ?? "";
                Add(i + 1, logo, container, () => AssignTo(key), hotkey.Length > 0 ? $"{p.Name} ({hotkey})" : p.Name);
            }
            root.Children.Add(group);

            return new Border
            {
                CornerRadius = new CornerRadius(spec.PopupRadius), Background = pal.Background(alpha),
                BorderBrush = UiColor.Brush(pal.Edge), BorderThickness = new Thickness(1), Child = root,
            };
        }

        private static TextBlock Glyph(string glyph, string fgHex) => new()
        {
            Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 18,
            Foreground = UiColor.Brush(fgHex), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
    }
}
