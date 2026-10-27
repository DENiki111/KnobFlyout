using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using KnobEarTrumpet.Config;
using KnobEarTrumpet.Profiles;

namespace KnobEarTrumpet.Osd
{
    /// <summary>Neue-App-Popup im Design „Gaming-HUD“ (Designs.Hud): abgeschrägte Knöpfe, Zuweisen-Taste klein im Knopf.</summary>
    internal sealed partial class NewAppOsd
    {
        /// <summary>
        /// „▲ NEUE APP ERKANNT“ · APP-NAME; darunter abgeschrägte Knöpfe: ⊘ (neutral) und je Profil Rahmen + Symbol in Profilfarbe.
        /// Ist eine Zuweisen-Taste gesetzt, steht sie klein unten im Knopf (im Spiel wird eher getippt als geklickt).
        /// </summary>
        private Grid BuildHud(AppConfig cfg, DesignSpec spec, double alpha, string processName)
        {
            bool light = ThemeHelper.IsLight(cfg);
            var pal = spec.Palette(light);
            double w = spec.PopupWidth, h = spec.PopupHeight, c = spec.Chamfer;
            var accent = UiColor.Brush(pal.Fill);

            var root = new Grid();
            root.Children.Add(HudParts.ChamferPath(w, h, c, pal.Background(alpha), UiColor.Brush(pal.Edge)));
            if (pal.Scan != null)
                root.Children.Add(new Rectangle { Fill = HudParts.Scanlines(pal.Scan), Clip = HudParts.Chamfer(w, h, c), IsHitTestVisible = false });
            root.Children.Add(HudParts.Brackets(w, h, accent));
            // Ganze Fläche klickbar halten (sonst fallen Klicks zwischen den Knöpfen durch das durchsichtige Fenster)
            root.Background = Brushes.Transparent;

            var panel = new StackPanel { Margin = new Thickness(16, 12, 16, 0) };
            panel.Children.Add(new TextBlock
            {
                Text = "▲ " + HudParts.Caps(Loc.T("Neue App erkannt", "New app detected")), FontFamily = spec.Font, FontSize = 11,
                FontWeight = FontWeights.Bold, Foreground = accent,
            });
            panel.Children.Add(new TextBlock
            {
                Text = HudParts.Caps(processName.Replace(".exe", "")), FontFamily = spec.Font, FontSize = 22, FontWeight = FontWeights.Bold,
                Foreground = UiColor.Brush(pal.Fg), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0),
            });

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 0) };
            var profiles = ProfileDefinitions.For(cfg);
            int count = profiles.Count + 1; // ⊘ + bis zu 6 Profile passen immer in die Breite
            double size = Math.Min(40, (w - 32 - 6.0 * (count - 1)) / count);
            var sub = UiColor.Brush(pal.Sub);
            row.Children.Add(HudButton(new TextBlock
            {
                Text = "\uE733", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 17, Foreground = sub, // Blocked
            }, null, sub, size, "", spec, pal, Exclude, Loc.T("Ignorieren", "Ignore")));
            foreach (var p in profiles)
            {
                string key = p.Key;
                var color = UiColor.Brush(p.Color);
                var logo = p.CreateLogo(17, color);
                if (logo is Image) logo.Width = logo.Height = 18;
                string hotkey = cfg.ProfileList.FirstOrDefault(e => e.Key == key)?.AssignHotkey ?? "";
                row.Children.Add(HudButton(logo, UiColor.Brush(p.Color, 0.13), color, size, hotkey, spec, pal, () => AssignTo(key),
                    hotkey.Length > 0 ? $"{p.Name} ({hotkey})" : p.Name));
            }
            panel.Children.Add(row);
            root.Children.Add(panel);
            return root;
        }

        /// <summary>Abgeschrägter Knopf (Rahmen + leicht gefüllt), Inhalt mittig; mit Taste: Inhalt etwas höher, Taste klein darunter.</summary>
        private static Grid HudButton(FrameworkElement content, Brush? fill, Brush stroke, double size, string hotkey, DesignSpec spec,
            DesignPalette pal, Action onClick, string tooltip)
        {
            var b = new Grid
            {
                Width = size, Height = size, Margin = new Thickness(0, 0, 6, 0), Background = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand, ToolTip = tooltip,
            };
            b.Children.Add(HudParts.ChamferPath(size, size, 6, fill, stroke, 1.5));
            content.HorizontalAlignment = HorizontalAlignment.Center;
            content.VerticalAlignment = VerticalAlignment.Center;
            if (hotkey.Length > 0) content.Margin = new Thickness(0, 0, 0, 9);
            b.Children.Add(content);
            if (hotkey.Length > 0)
            {
                b.Children.Add(new TextBlock
                {
                    Text = hotkey, FontFamily = spec.Font, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = UiColor.Brush(pal.Fg),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 3), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = size - 4,
                });
            }
            b.MouseEnter += (_, _) => b.Opacity = 0.85;
            b.MouseLeave += (_, _) => b.Opacity = 1.0;
            b.MouseLeftButtonDown += (_, e) => e.Handled = true;
            b.MouseLeftButtonUp += (_, e) => { onClick(); e.Handled = true; };
            return b;
        }
    }
}
