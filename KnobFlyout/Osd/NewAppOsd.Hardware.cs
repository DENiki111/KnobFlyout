using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using KnobFlyout.Config;
using KnobFlyout.Profiles;

namespace KnobFlyout.Osd
{
    /// <summary>Neue-App-Popup im Design „Hardware-Regler“ (Designs.Hardware): App-Name im Display, Tasten mit Profil-LED.</summary>
    internal sealed partial class NewAppOsd
    {
        /// <summary>
        /// „NEUE APP ERKANNT“ (aufgedruckt) · App-Name im bernsteinfarbenen Display · Tasten: ⊘ und je Profil eine Taste mit LED in
        /// Profilfarbe und Logo; eine gesetzte Zuweisen-Taste steht als Aufdruck unten auf der Taste.
        /// </summary>
        private Border BuildHardware(AppConfig cfg, DesignSpec spec, double alpha, string processName)
        {
            bool light = ThemeHelper.IsLight(cfg);
            var pal = spec.Palette(light);
            var hw = HardwareParts.Colors(light);
            double w = spec.PopupWidth, h = spec.PopupHeight;
            var fg = UiColor.Brush(pal.Fg);
            var sub = UiColor.Brush(pal.Sub);

            var root = new StackPanel { Margin = new Thickness(13, 11, 13, 0) };
            root.Children.Add(new TextBlock
            {
                Text = HudParts.Caps(Loc.T("Neue App erkannt", "New app detected")), FontFamily = spec.Font, FontSize = 9.5,
                FontWeight = FontWeights.SemiBold, Foreground = sub,
            });
            var lcd = HardwareParts.Lcd(hw, pal.Hi ?? "#00000000", new TextBlock
            {
                Text = processName.Replace(".exe", "").ToUpperInvariant(), FontFamily = HardwareParts.LcdFont, FontSize = 16,
                FontWeight = FontWeights.Bold, Foreground = UiColor.Brush(hw.LcdText), VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, Effect = HardwareParts.AmberGlow(hw),
            }, new Thickness(10, 0, 10, 0));
            lcd.Height = 34;
            lcd.Margin = new Thickness(0, 6, 0, 0);
            root.Children.Add(lcd);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var profiles = ProfileDefinitions.For(cfg);
            int count = profiles.Count + 1; // ⊘ + bis zu 6 Profile passen immer in die Breite
            double size = Math.Min(38, (w - 28 - 6.0 * (count - 1)) / count);
            row.Children.Add(HwKey(hw, size, new TextBlock
            {
                Text = "\uE733", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 15, Foreground = sub, // Blocked
            }, null, "", spec, sub, Exclude, Loc.T("Ignorieren", "Ignore")));
            foreach (var p in profiles)
            {
                string key = p.Key;
                var logo = p.CreateLogo(15, fg);
                if (logo is Image) logo.Width = logo.Height = 16;
                string hotkey = cfg.ProfileList.FirstOrDefault(e => e.Key == key)?.AssignHotkey ?? "";
                row.Children.Add(HwKey(hw, size, logo, p.Color, hotkey, spec, sub, () => AssignTo(key),
                    hotkey.Length > 0 ? $"{p.Name} ({hotkey})" : p.Name));
            }
            root.Children.Add(row);

            var content = new Grid();
            content.Children.Add(root);
            content.Children.Add(HardwareParts.Screws(w - 2, h - 2, hw));
            return new Border
            {
                CornerRadius = new CornerRadius(spec.PopupRadius), Background = pal.Background(alpha),
                BorderBrush = UiColor.Brush(pal.Edge), BorderThickness = new Thickness(1),
                Child = pal.WithHighlight(content, spec.PopupRadius - 1),
            };
        }

        /// <summary>Taste: LED (Profilfarbe) oben links, Inhalt mittig, Zuweisen-Taste klein unten; Klick erst beim Loslassen.</summary>
        private static Grid HwKey(HwColors hw, double size, FrameworkElement content, string? ledHex, string hotkey, DesignSpec spec,
            Brush legend, Action onClick, string tooltip)
        {
            var key = HardwareParts.Key(hw, size, 34, out var face);
            key.Margin = new Thickness(0, 0, 6, 0);
            key.Cursor = System.Windows.Input.Cursors.Hand;
            key.ToolTip = tooltip;
            key.Background = Brushes.Transparent;
            var inner = new Grid();
            if (ledHex != null)
            {
                var col = UiColor.Brush(ledHex);
                inner.Children.Add(new Ellipse
                {
                    Width = 5, Height = 5, Fill = col, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(3), Effect = HudParts.Glow(col.Color, 0.9, 5),
                });
            }
            content.HorizontalAlignment = HorizontalAlignment.Center;
            content.VerticalAlignment = VerticalAlignment.Center;
            if (hotkey.Length > 0) content.Margin = new Thickness(0, 0, 0, 7);
            inner.Children.Add(content);
            if (hotkey.Length > 0)
            {
                inner.Children.Add(new TextBlock
                {
                    Text = hotkey, FontFamily = spec.Font, FontSize = 8, FontWeight = FontWeights.SemiBold, Foreground = legend,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2),
                    TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = size - 4,
                });
            }
            face.Child = inner;
            key.MouseEnter += (_, _) => key.Opacity = 0.85;
            key.MouseLeave += (_, _) => key.Opacity = 1.0;
            key.MouseLeftButtonDown += (_, e) => e.Handled = true;
            key.MouseLeftButtonUp += (_, e) => { onClick(); e.Handled = true; };
            return key;
        }
    }
}
