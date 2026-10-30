using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KnobFlyout.Config;
using KnobFlyout.Profiles;
using static KnobFlyout.Settings.UiHelpers;

namespace KnobFlyout.Settings
{
    /// <summary>
    /// Baut eine Zeile wie in _make_assign_row() des Python-Originals, im Windows-11-Listenstil:
    /// grüner Punkt (läuft), Name + Prozessname untereinander, rechts eine Segment-Leiste mit
    /// ⊘ (Ignorieren) und einem Feld je Profil (in Profilfarbe gefüllt, wenn zugewiesen).
    /// Die Segment-Leiste hat eine feste Breite, damit sie in jeder Zeile exakt untereinander steht.
    /// </summary>
    internal static class AppRow
    {
        private static readonly Dictionary<string, string> KnownLabels = new()
        {
            ["spotify.exe"] = "Spotify", ["chrome.exe"] = "Chrome", ["firefox.exe"] = "Firefox",
            ["msedge.exe"] = "Edge", ["opera.exe"] = "Opera", ["brave.exe"] = "Brave",
            ["vivaldi.exe"] = "Vivaldi", ["discord.exe"] = "Discord", ["vlc.exe"] = "VLC",
            ["steam.exe"] = "Steam", ["epicgameslauncher.exe"] = "Epic Games", ["teams.exe"] = "Teams",
            ["slack.exe"] = "Slack", ["zoom.exe"] = "Zoom", ["obs64.exe"] = "OBS Studio",
        };

        private const string ExcludeGlyph = "\uE733"; // Blocked

        internal static string Label(string proc)
        {
            var key = proc.ToLowerInvariant();
            if (KnownLabels.TryGetValue(key, out var l)) return l;
            var name = proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? proc[..^4] : proc;
            return name.Length > 0 ? char.ToUpper(name[0]) + name[1..] : proc;
        }

        public static string? CurrentProfile(AppConfig cfg, string proc)
        {
            var key = proc.ToLowerInvariant();
            foreach (var kv in cfg.Profiles)
                if (kv.Value.Any(a => a.ToLowerInvariant() == key)) return kv.Key;
            return null;
        }

        public static void Assign(AppConfig cfg, string proc, string pkey)
        {
            var key = proc.ToLowerInvariant();
            foreach (var k in cfg.Profiles.Keys.ToList())
                cfg.Profiles[k] = cfg.Profiles[k].Where(a => a.ToLowerInvariant() != key).ToList();

            if (!cfg.Profiles.TryGetValue(pkey, out var list))
                cfg.Profiles[pkey] = list = new List<string>();
            if (!list.Any(a => a.ToLowerInvariant() == key)) list.Add(proc);

            if (!cfg.SeenApps.Any(a => a.ToLowerInvariant() == key)) cfg.SeenApps.Add(key);
            cfg.Exclude = cfg.Exclude.Where(e => e.ToLowerInvariant() != key).ToList();
        }

        public static void ToggleExclude(AppConfig cfg, string proc)
        {
            var key = proc.ToLowerInvariant();
            if (cfg.Exclude.Any(e => e.ToLowerInvariant() == key))
                cfg.Exclude = cfg.Exclude.Where(e => e.ToLowerInvariant() != key).ToList();
            else
                cfg.Exclude.Add(key);
        }

        /// <summary>Erzeugt eine Zeile. onChanged wird nach jedem Klick aufgerufen, damit der Aufrufer neu zeichnen kann.</summary>
        public static Border Build(AppConfig cfg, string proc, bool running, Action onChanged)
        {
            string current = CurrentProfile(cfg, proc) ?? "";
            bool excluded = cfg.Exclude.Any(e => e.ToLowerInvariant() == proc.ToLowerInvariant());

            var row = new Border
            {
                Background = Brushes.Transparent, CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 5, 4, 5), Margin = new Thickness(0, 0, 0, 1),
            };
            row.MouseEnter += (_, _) => row.Background = HoverBg;
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Child = grid;

            if (running)
            {
                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = 7, Height = 7, Fill = FromHex("#4ade80"), VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = Loc.T("Spielt gerade Ton ab", "Playing sound"),
                };
                grid.Children.Add(dot);
            }

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var name = new TextBlock
            {
                Text = Label(proc), FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = excluded ? SubFg : Fg, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            if (excluded) name.TextDecorations = TextDecorations.Strikethrough;
            text.Children.Add(name);
            text.Children.Add(new TextBlock
            {
                Text = proc.ToLowerInvariant(), FontSize = 11, Foreground = SubFg, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            // Segment-Leiste: ⊘ | Gaming | Komm. | Musik | Video
            var segments = new StackPanel { Orientation = Orientation.Horizontal };
            var bar = new Border
            {
                Background = FieldBg, BorderBrush = FieldBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(2), Child = segments,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(bar, 2);
            grid.Children.Add(bar);

            bool selectedExclude = excluded;
            segments.Children.Add(Segment(new TextBlock
                {
                    Text = ExcludeGlyph, FontFamily = IconFont, FontSize = 13,
                    Foreground = selectedExclude ? Brushes.White : SubFg,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                }, excluded ? "#C42B1C" : null,
                Loc.T("Ignorieren", "Ignore"), () => { ToggleExclude(cfg, proc); onChanged(); }));

            foreach (var p in ProfileDefinitions.For(cfg))
            {
                bool active = current == p.Key && !excluded;
                string key = p.Key;
                segments.Children.Add(Segment(p.CreateLogo(13, active ? Brushes.White : SubFg),
                    active ? p.Color : null, p.Name,
                    () => { Assign(cfg, proc, key); onChanged(); }));
            }

            return row;
        }

        /// <summary>Ein Feld der Segment-Leiste mit Logo; fillHex != null = ausgewählt (in Profilfarbe gefüllt).</summary>
        private static Border Segment(FrameworkElement logo, string? fillHex, string tooltip, Action onClick)
        {
            bool selected = fillHex != null;
            Brush normal = selected ? FromHex(fillHex!) : Brushes.Transparent;
            if (logo is Image) logo.Width = logo.Height = 16; // eigenes Bild etwas größer als das Symbol
            var seg = new Border
            {
                Width = 32, Height = 26, CornerRadius = new CornerRadius(8), Background = normal,
                Cursor = System.Windows.Input.Cursors.Hand, ToolTip = tooltip,
                Child = logo,
            };
            if (!selected)
            {
                seg.MouseEnter += (_, _) => seg.Background = HoverBg;
                seg.MouseLeave += (_, _) => seg.Background = Brushes.Transparent;
            }
            seg.MouseLeftButtonUp += (_, e) => { onClick(); e.Handled = true; };
            return seg;
        }
    }
}
