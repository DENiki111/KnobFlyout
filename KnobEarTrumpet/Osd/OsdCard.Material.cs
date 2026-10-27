using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using KnobEarTrumpet.Profiles;

namespace KnobEarTrumpet.Osd
{
    /// <summary>
    /// OSD-Karten im Design „Material You“ (Designs.Material): Symbol-Container links, Lautstärke als M3-Expressive-Regler
    /// (aktive Spur · Lücke · Griff · Lücke · inaktive Spur mit Stopp-Punkt), Profil in Cookie-Form, M3-Schalter,
    /// Seitenanzeige mit länglichem aktiven Punkt. Farben aus der Windows-Akzentfarbe (DesignSpec.Dynamic).
    /// </summary>
    internal sealed partial class OsdCard
    {
        private const double M3PadLeft = 15;   // 16 px Innenabstand minus 1 px Rand
        private const double M3PadRight = 17;
        private const double M3Icon = 44;
        private const double M3Gap = 6;        // Lücke zwischen Spur und Griff
        private const double M3Grip = 4;

        private bool IsMaterial => ReferenceEquals(_spec, Designs.Material);

        private Border? _m3IconBox;
        private Border? _m3Active;
        private Border? _m3Inactive;
        private Border? _m3Handle;
        private Ellipse? _m3Stop;
        private double _m3Width;

        // Reglerposition 0–1 als animierbare Eigenschaft: eine Raste animiert nur diesen Wert, LayoutM3Slider setzt drei Elemente.
        private static readonly DependencyProperty M3PosProperty = DependencyProperty.Register(
            "M3Pos", typeof(double), typeof(OsdCard), new PropertyMetadata(0.0, (d, e) => ((OsdCard)d).LayoutM3Slider((double)e.NewValue)));

        /// <summary>Cookie-Form (Kreis mit 9 sanften Wellen, Material 3 Expressive) für das Profil-Logo, 48×48.</summary>
        private static readonly Geometry CookieShape = CreateCookie(48);

        /// <summary>Lautstärke: Symbol-Container · App-Name + Zahl (Primärfarbe) · Regler.</summary>
        private void BuildVolumeMaterial()
        {
            var pal = _pal!;
            _m3IconBox = M3IconBox("\uE995", pal.Container ?? pal.Track, pal.OnContainer ?? pal.Fg);
            _glyph = (TextBlock)_m3IconBox.Child;
            Place(_m3IconBox, M3PadLeft, (_h - 2 - M3Icon) / 2);

            double left = M3PadLeft + M3Icon + 14;
            _m3Width = _w - 2 - left - M3PadRight;
            var head = new Grid { Width = _m3Width, Height = 18 };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _lapp = M3Text("", 14, _fg, FontWeights.SemiBold);
            Cell(head, _lapp, 0);
            _lpct = M3Text("", 14, _accent, FontWeights.SemiBold);
            _lpct.Margin = new Thickness(8, 0, 0, 0);
            System.Windows.Documents.Typography.SetNumeralAlignment(_lpct, FontNumeralAlignment.Tabular);
            Cell(head, _lpct, 1);
            Place(head, left, 10);

            var slider = new Canvas { Width = _m3Width, Height = 32 };
            _m3Inactive = new Border { Height = 16, CornerRadius = new CornerRadius(2, 8, 8, 2), Background = _trackBrush };
            _m3Active = new Border { Height = 16, CornerRadius = new CornerRadius(8, 2, 2, 8) };
            _m3Handle = new Border { Width = M3Grip, Height = 32, CornerRadius = new CornerRadius(2) };
            _m3Stop = new Ellipse { Width = 4, Height = 4 };
            Canvas.SetTop(_m3Inactive, 8);
            Canvas.SetTop(_m3Active, 8);
            Canvas.SetTop(_m3Stop, 14);
            Canvas.SetLeft(_m3Stop, _m3Width - 10);
            slider.Children.Add(_m3Inactive);
            slider.Children.Add(_m3Active);
            slider.Children.Add(_m3Handle);
            slider.Children.Add(_m3Stop);
            Place(slider, left, 32);
            SetM3SliderColor(false);
            LayoutM3Slider(0);
        }

        private void UpdateMaterial(string clean, int pct, bool muted)
        {
            if (_lapp == null || _lpct == null || _glyph == null || _m3IconBox == null || _pal == null) return;
            _lapp.Text = clean;
            _glyph.Text = muted ? "\uE74F" : pct == 0 ? "\uE992" : pct < 34 ? "\uE993" : pct < 67 ? "\uE994" : "\uE995";
            // Stumm: Container in Fehlerfarbe, Regler „deaktiviert“ (Textfarbe 38 %) wie bei Material.
            _m3IconBox.Background = UiColor.Brush(muted ? _pal.MuteContainer ?? _pal.Mute : _pal.Container ?? _pal.Track);
            _glyph.Foreground = UiColor.Brush(muted ? _pal.OnMuteContainer ?? _pal.Fg : _pal.OnContainer ?? _pal.Fg);
            _lpct.Text = muted ? Loc.T("Stumm", "Muted") : pct.ToString();
            _lpct.FontSize = muted ? 12 : 14;
            _lpct.FontWeight = muted ? FontWeights.Normal : FontWeights.SemiBold;
            _lpct.Foreground = muted ? _sub : _accent;
            SetM3SliderColor(muted);

            double target = pct / 100.0;
            if (IsVisible)
                BeginAnimation(M3PosProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(90)));
            else
            {
                BeginAnimation(M3PosProperty, null);
                SetValue(M3PosProperty, target);
            }
        }

        private void SetM3SliderColor(bool muted)
        {
            if (_m3Active == null || _m3Handle == null || _m3Stop == null) return;
            Brush active = muted ? UiColor.Brush(_pal!.Fg, 0.38) : _accent;
            _m3Active.Background = active;
            _m3Handle.Background = active;
            _m3Stop.Fill = active;
        }

        /// <summary>Spuren, Griff und Stopp-Punkt für die Position pos (0–1) setzen.</summary>
        private void LayoutM3Slider(double pos)
        {
            if (_m3Active == null || _m3Inactive == null || _m3Handle == null || _m3Stop == null) return;
            double x = Math.Clamp(pos, 0, 1) * (_m3Width - M3Grip);
            double activeW = Math.Max(0, x - M3Gap);
            double inactiveW = Math.Max(0, _m3Width - x - M3Grip - M3Gap);
            _m3Active.Width = activeW;
            _m3Active.Visibility = activeW > 0 ? Visibility.Visible : Visibility.Collapsed;
            Canvas.SetLeft(_m3Handle, x);
            Canvas.SetLeft(_m3Inactive, x + M3Grip + M3Gap);
            _m3Inactive.Width = inactiveW;
            _m3Inactive.Visibility = inactiveW > 0 ? Visibility.Visible : Visibility.Collapsed;
            _m3Stop.Visibility = inactiveW > 12 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Profilwechsel: Logo in der Cookie-Form (Profilfarbe, weißes Symbol bzw. eigenes Bild) und Profilname.</summary>
        private void BuildProfileMaterial()
        {
            var profiles = ProfileDefinitions.For(_cfg);
            var def = profiles.FirstOrDefault(p => p.Key == _cfg.ActiveProfile) ?? profiles.FirstOrDefault();
            var row = M3Row(GridLength.Auto, new GridLength(1, GridUnitType.Star));
            var cookie = new Grid { Width = 48, Height = 48, VerticalAlignment = VerticalAlignment.Center };
            cookie.Children.Add(new System.Windows.Shapes.Path { Data = CookieShape, Fill = UiColor.Brush(def?.Color ?? _pal!.Fill) });
            FrameworkElement logo = def != null ? def.CreateLogo(20, Brushes.White) : M3Glyph("\uE734", 20, Brushes.White);
            if (logo is Image) logo.Width = logo.Height = 24;
            cookie.Children.Add(logo);
            Cell(row, cookie, 0);
            var name = M3Text(def?.Name ?? "Profile", 16, _fg, FontWeights.SemiBold);
            name.Margin = new Thickness(14, 0, 0, 0);
            Cell(row, name, 1);
        }

        /// <summary>Feststelltasten: Schloss im Container (an: Primär-Container) · Name + „An“/„Aus“ · M3-Schalter.</summary>
        private void BuildLockKeyMaterial(string name, bool on)
        {
            var pal = _pal!;
            string label = name switch
            {
                "CapsLock" => Loc.T("Feststelltaste", "Caps Lock"),
                "NumLock" => Loc.T("Nummernblock", "Num Lock"),
                _ => Loc.T("Rollen", "Scroll Lock"),
            };
            var row = M3Row(GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto);
            Cell(row, on ? M3IconBox(LockGlyph, pal.Container ?? pal.Track, pal.OnContainer ?? pal.Fg)
                         : M3IconBox(UnlockGlyph, pal.Tile ?? pal.Off, pal.Sub), 0);
            Cell(row, M3Title(label, on ? Loc.T("An", "On") : Loc.T("Aus", "Off")), 1);
            Cell(row, M3Switch(on), 2);
        }

        /// <summary>Positionswechsel: Stecknadel im Container · „Position n“ + „von N“ · Seitenanzeige (bis 8).</summary>
        private void BuildSnapInfoMaterial(int index, int total)
        {
            var pal = _pal!;
            var row = M3Row(GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto);
            Cell(row, M3IconBox("\uE707", pal.Container ?? pal.Track, pal.OnContainer ?? pal.Fg), 0); // MapPin
            Cell(row, M3Title($"Position {index + 1}", Loc.T($"von {total}", $"of {total}")), 1);
            if (total > 8) return;
            var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < total; i++)
                dots.Children.Add(new Border
                {
                    Width = i == index ? 18 : 6, Height = 6, CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0),
                    Background = i == index ? _accent : UiColor.Brush(pal.OutlineVariant ?? pal.Track),
                });
            Cell(row, dots, 2);
        }

        /// <summary>M3-Schalter: an = Spur in Primärfarbe, großer Griff mit Häkchen; aus = Umriss, kleiner Griff.</summary>
        private Border M3Switch(bool on)
        {
            var pal = _pal!;
            var thumb = new Border
            {
                Width = on ? 24 : 16, Height = on ? 24 : 16, CornerRadius = new CornerRadius(on ? 12 : 8),
                Background = UiColor.Brush(on ? pal.OnAccent ?? "#FFFFFF" : pal.Outline ?? pal.Sub),
                HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(on ? 0 : 6, 0, on ? 4 : 0, 0),
            };
            if (on) thumb.Child = M3Glyph("\uE73E", 13, UiColor.Brush(pal.OnContainer ?? pal.Fg)); // CheckMark
            return new Border
            {
                Width = 52, Height = 32, CornerRadius = new CornerRadius(16), VerticalAlignment = VerticalAlignment.Center,
                Background = UiColor.Brush(on ? pal.Fill : pal.Tile ?? pal.Off),
                BorderBrush = on ? null : UiColor.Brush(pal.Outline ?? pal.Sub), BorderThickness = new Thickness(on ? 0 : 2),
                Child = thumb,
            };
        }

        /// <summary>Runder Symbol-Container 44 px.</summary>
        private static Border M3IconBox(string glyph, string bgHex, string fgHex) => new()
        {
            Width = M3Icon, Height = M3Icon, CornerRadius = new CornerRadius(M3Icon / 2), Background = UiColor.Brush(bgHex),
            VerticalAlignment = VerticalAlignment.Center, Child = M3Glyph(glyph, 20, UiColor.Brush(fgHex)),
        };

        private static TextBlock M3Glyph(string glyph, double size, Brush fg) => new()
        {
            Text = glyph, FontFamily = IconFont, FontSize = size, Foreground = fg,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };

        /// <summary>Zeile über die ganze Karte (Innenabstand links 16, rechts 18), senkrecht mittig.</summary>
        private Grid M3Row(params GridLength[] columns)
        {
            var row = new Grid { Width = _w - 2 - M3PadLeft - M3PadRight, Height = _h - 2 };
            foreach (var c in columns) row.ColumnDefinitions.Add(new ColumnDefinition { Width = c });
            Place(row, M3PadLeft, 0);
            return row;
        }

        /// <summary>Titel mit Unterzeile rechts neben dem Container.</summary>
        private StackPanel M3Title(string title, string sub)
        {
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 10, 0) };
            panel.Children.Add(M3Text(title, 14, _fg, FontWeights.SemiBold));
            var s = M3Text(sub, 12, _sub, FontWeights.Normal);
            s.Margin = new Thickness(0, 2, 0, 0);
            panel.Children.Add(s);
            return panel;
        }

        private TextBlock M3Text(string text, double size, Brush fg, FontWeight weight) => new()
        {
            Text = text, FontFamily = _spec!.Font, FontSize = size, FontWeight = weight, Foreground = fg,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };

        private static Geometry CreateCookie(double size)
        {
            const int waves = 9;
            const double depth = 0.075;
            double r0 = size / 2;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                for (int i = 0; i <= 180; i++)
                {
                    double t = i / 180.0 * 2 * Math.PI;
                    double r = r0 * (1 - depth) * (1 + depth * Math.Cos(waves * t));
                    var p = new Point(r0 + r * Math.Cos(t), r0 + r * Math.Sin(t));
                    if (i == 0) ctx.BeginFigure(p, true, true);
                    else ctx.LineTo(p, true, true);
                }
            }
            g.Freeze();
            return g;
        }
    }
}
