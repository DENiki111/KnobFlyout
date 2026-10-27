using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using KnobFlyout.Config;
using KnobFlyout.Profiles;

namespace KnobFlyout.Osd
{
    /// <summary>
    /// OSD-Karten im Design „Gaming-HUD“ (Designs.Hud): abgeschrägte Fläche mit Scanlines und Eck-Klammern; Lautstärke als
    /// große leuchtende Zahl + 20 schräge LED-Segmente, Profil im Sechseck, Feststelltaste mit Status-Schild, Position als „02/03“.
    /// </summary>
    internal sealed partial class OsdCard
    {
        private const double HudPadLeft = 15;  // 16 px minus 1 px (die Zeichenfläche liegt im unsichtbaren Rand)
        private const double HudPadRight = 17;

        private bool IsHud => ReferenceEquals(_spec, Designs.Hud);

        private TextBlock? _hudMute;
        private TextBlock? _hudTag;
        private Rectangle[]? _hudSegs;
        private DropShadowEffect? _hudSegGlow;
        private double _hudGlow;

        /// <summary>Statt der rechteckigen Kartenfläche: abgeschrägte Fläche mit Rand, Scanlines und Eck-Klammern (ganz hinten).</summary>
        private void SetupHudShell()
        {
            var pal = _pal!;
            _hudGlow = ThemeHelper.IsLight(_cfg) ? 0.3 : 0.6;
            _shell.Background = Brushes.Transparent; // bleibt für Maus-Treffer (Ziehen/Wegklicken) durchsichtig
            _shell.BorderBrush = Brushes.Transparent;
            _shell.CornerRadius = new CornerRadius(0);
            double c = _spec!.Chamfer;
            // Eigene Ebene unter der Zeichenfläche: die Deckkraft des Inhalts (_canvas.Opacity) soll die Fläche nicht mit abblenden.
            var back = new Canvas();
            void Back(UIElement el)
            {
                Canvas.SetLeft(el, -1);
                Canvas.SetTop(el, -1);
                back.Children.Add(el);
            }
            Back(HudParts.ChamferPath(_w, _h, c, pal.Background(_targetOpacity), UiColor.Brush(pal.Edge)));
            if (pal.Scan != null)
            {
                Back(new Rectangle
                {
                    Width = _w, Height = _h, Fill = HudParts.Scanlines(pal.Scan), Clip = HudParts.Chamfer(_w, _h, c), IsHitTestVisible = false,
                });
            }
            Back(HudParts.Brackets(_w, _h, _accent));
            var front = _shell.Child;
            _shell.Child = null;
            var layers = new Grid();
            layers.Children.Add(back);
            layers.Children.Add(front);
            _shell.Child = layers;
        }

        /// <summary>Lautstärke: große Zahl (bzw. rotes Stumm-Symbol) | Trennlinie | Symbol + APP-NAME (+ „STUMM“) über 20 Segmenten.</summary>
        private void BuildVolumeHud()
        {
            var pal = _pal!;
            var numHost = new Grid { Width = 58, Height = _h - 2 };
            _lpct = HudText("", 34, _accent, FontWeights.Bold);
            _lpct.HorizontalAlignment = HorizontalAlignment.Right;
            _lpct.Effect = HudParts.Glow(_accent.Color, _hudGlow);
            System.Windows.Documents.Typography.SetNumeralAlignment(_lpct, FontNumeralAlignment.Tabular);
            _hudMute = new TextBlock
            {
                Text = "\uE74F", FontFamily = IconFont, FontSize = 28, Foreground = UiColor.Brush(pal.Mute), Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            };
            numHost.Children.Add(_lpct);
            numHost.Children.Add(_hudMute);
            Place(numHost, HudPadLeft, 0);
            Place(new Rectangle { Width = 1, Height = 34, Fill = UiColor.Brush(pal.Edge) }, HudPadLeft + 58 + 12, (_h - 2 - 34) / 2);

            double left = HudPadLeft + 58 + 12 + 1 + 14;
            double width = _w - 2 - left - HudPadRight;
            var row = new Grid { Width = width, Height = 16 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _glyph = new TextBlock { Text = "\uE995", FontFamily = IconFont, FontSize = 13, Foreground = _sub, VerticalAlignment = VerticalAlignment.Center };
            Cell(row, _glyph, 0);
            _lapp = HudText("", 13, _sub, FontWeights.SemiBold);
            _lapp.Margin = new Thickness(6, 0, 6, 0);
            Cell(row, _lapp, 1);
            _hudTag = HudText(HudParts.Caps(Loc.T("Stumm", "Muted")), 11, UiColor.Brush(pal.Mute), FontWeights.Bold);
            _hudTag.Visibility = Visibility.Collapsed;
            Cell(row, _hudTag, 2);
            Place(row, left, 14);

            var segs = HudParts.Segments(20, 10, 3, _trackBrush, out _hudSegs, out var front);
            segs.Width = width;
            _hudSegGlow = HudParts.Glow(_accent.Color, _hudGlow, 6);
            front.Effect = _hudSegGlow; // nur die leuchtenden Segmente (die übrigen sind durchsichtig)
            Place(segs, left, 38);
        }

        private void UpdateHud(string clean, int pct, bool muted)
        {
            if (_lapp == null || _lpct == null || _glyph == null || _hudMute == null || _hudTag == null || _hudSegs == null || _pal == null) return;
            _lapp.Text = HudParts.Caps(clean);
            _glyph.Text = muted ? "\uE74F" : pct == 0 ? "\uE992" : pct < 34 ? "\uE993" : pct < 67 ? "\uE994" : "\uE995";
            _lpct.Text = pct.ToString();
            _lpct.Visibility = muted ? Visibility.Collapsed : Visibility.Visible;
            _hudMute.Visibility = _hudTag.Visibility = muted ? Visibility.Visible : Visibility.Collapsed;
            // 5 % je Segment; Stumm: Segmente dunkelrot ohne Leuchten
            int lit = (int)Math.Round(pct / 5.0);
            Brush on = muted ? UiColor.Brush(_pal.MutedFill ?? _pal.Mute) : _accent;
            for (int i = 0; i < _hudSegs.Length; i++) _hudSegs[i].Fill = i < lit ? on : Brushes.Transparent;
            if (_hudSegGlow != null) _hudSegGlow.Opacity = muted ? 0 : _hudGlow;
        }

        /// <summary>Profilwechsel: Logo im leuchtenden Sechseck (Profilfarbe) und PROFILNAME.</summary>
        private void BuildProfileHud()
        {
            var profiles = ProfileDefinitions.For(_cfg);
            var def = profiles.FirstOrDefault(p => p.Key == _cfg.ActiveProfile) ?? profiles.FirstOrDefault();
            string hex = def?.Color ?? _pal!.Fill;
            var color = UiColor.Brush(hex);
            var row = HudRow(GridLength.Auto, new GridLength(1, GridUnitType.Star));
            var badge = new Grid { Width = 40, Height = 44, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            badge.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Hexagon(40, 44), Fill = UiColor.Brush(hex, 0.18), Stroke = color, StrokeThickness = 2,
                Effect = HudParts.Glow(color.Color, 0.55, 10),
            });
            FrameworkElement logo = def != null ? def.CreateLogo(18, color)
                : new TextBlock { Text = "\uE734", FontFamily = IconFont, FontSize = 18, Foreground = color };
            if (logo is Image) logo.Width = logo.Height = 20;
            logo.HorizontalAlignment = HorizontalAlignment.Center;
            logo.VerticalAlignment = VerticalAlignment.Center;
            badge.Children.Add(logo);
            Cell(row, badge, 0);
            Cell(row, HudText(HudParts.Caps(def?.Name ?? "Profile"), 21, _fg, FontWeights.Bold), 1);
        }

        /// <summary>Feststelltasten: Schloss · NAME · Status-Schild „AN“ (grün leuchtend) bzw. „AUS“.</summary>
        private void BuildLockKeyHud(string name, bool on)
        {
            string label = name switch
            {
                "CapsLock" => Loc.T("Feststelltaste", "Caps Lock"),
                "NumLock" => Loc.T("Nummernblock", "Num Lock"),
                _ => Loc.T("Rollen", "Scroll Lock"),
            };
            var row = HudRow(GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto);
            Cell(row, HudIcon(on ? LockGlyph : UnlockGlyph, on ? _fg : _sub), 0);
            Cell(row, HudText(HudParts.Caps(label), 17, _fg, FontWeights.Bold), 1);

            var pal = _pal!;
            var col = UiColor.Brush(on ? pal.On : pal.Sub);
            var status = new Grid { Width = 48, Height = 24, VerticalAlignment = VerticalAlignment.Center };
            status.Children.Add(HudParts.ChamferPath(48, 24, 6, on ? UiColor.Brush(pal.On, 0.12) : null, col));
            var text = HudText(HudParts.Caps(on ? Loc.T("An", "On") : Loc.T("Aus", "Off")), 13, col, FontWeights.Bold);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            if (on) text.Effect = HudParts.Glow(col.Color, _hudGlow);
            status.Children.Add(text);
            Cell(row, status, 2);
        }

        /// <summary>Positionswechsel: Stecknadel · „POSITION“ über „02/03“ (Zahl leuchtend) · Mini-Segmente (bis 8).</summary>
        private void BuildSnapInfoHud(int index, int total)
        {
            var row = HudRow(GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto);
            Cell(row, HudIcon("\uE707", _fg), 0); // MapPin
            var block = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            block.Children.Add(HudText(HudParts.Caps("Position"), 11, _sub, FontWeights.SemiBold));
            var nums = new StackPanel { Orientation = Orientation.Horizontal };
            var big = HudText($"{index + 1:00}", 26, _accent, FontWeights.Bold);
            big.Effect = HudParts.Glow(_accent.Color, _hudGlow);
            nums.Children.Add(big);
            var of = HudText($"/{total:00}", 14, _sub, FontWeights.SemiBold);
            of.VerticalAlignment = VerticalAlignment.Bottom;
            of.Margin = new Thickness(3, 0, 0, 4);
            nums.Children.Add(of);
            block.Children.Add(nums);
            Cell(row, block, 1);
            if (total > 8) return;
            var segs = HudParts.Segments(total, 10, 4, _trackBrush, out var front, out var frontGrid);
            segs.Width = total * 16;
            segs.VerticalAlignment = VerticalAlignment.Center;
            front[index].Fill = _accent;
            frontGrid.Effect = HudParts.Glow(_accent.Color, _hudGlow, 6);
            Cell(row, segs, 2);
        }

        private Grid HudRow(params GridLength[] columns)
        {
            var row = new Grid { Width = _w - 2 - HudPadLeft - HudPadRight, Height = _h - 2 };
            foreach (var c in columns) row.ColumnDefinitions.Add(new ColumnDefinition { Width = c });
            Place(row, HudPadLeft, 0);
            return row;
        }

        private static TextBlock HudIcon(string glyph, Brush fg) => new()
        {
            Text = glyph, FontFamily = IconFont, FontSize = 19, Foreground = fg,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0),
        };

        private TextBlock HudText(string text, double size, Brush fg, FontWeight weight) => new()
        {
            Text = text, FontFamily = _spec!.Font, FontSize = size, FontWeight = weight, Foreground = fg,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };

        /// <summary>Sechseck mit Spitze oben (w × h).</summary>
        private static Geometry Hexagon(double w, double h)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(w / 2, 1), true, true);
                ctx.PolyLineTo(new[]
                {
                    new Point(w - 1, h * 0.25), new Point(w - 1, h * 0.75), new Point(w / 2, h - 1),
                    new Point(1, h * 0.75), new Point(1, h * 0.25),
                }, true, true);
            }
            g.Freeze();
            return g;
        }
    }
}
