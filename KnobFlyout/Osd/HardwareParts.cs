using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using KnobFlyout.Profiles;

namespace KnobFlyout.Osd
{
    /// <summary>Gerätefarben des Designs „Hardware-Regler“, die über die allgemeine Palette hinausgehen.</summary>
    internal sealed record HwColors(
        string Lcd, string LcdText, string LcdGhost,          // Display: Fläche, Bernstein, Geister-Segmente
        string KnobTop, string KnobBottom, string KnobEdge, string Knurl,
        string KeyTop, string KeyBottom, string KeyEdge,      // Tasten (Verlauf + Kante)
        string ScrewHi, string ScrewLo, string LedOff);

    /// <summary>Bausteine des Designs „Hardware-Regler“ (Designs.Hardware), gemeinsam für OSD-Karten, Neue-App-Popup und Media-Flyout.</summary>
    internal static class HardwareParts
    {
        public static readonly FontFamily LcdFont = new("Cascadia Mono, Consolas");

        public static readonly HwColors Dark = new("#0B0C0D", "#FFB04A", "#17FFB04A", "#4C4D52", "#26272B", "#101113", "#1FFFFFFF",
            "#36373B", "#2A2B2F", "#0E0F11", "#6A6B70", "#2A2B2F", "#36373C");
        public static readonly HwColors Light = new("#17181A", "#FFB04A", "#17FFB04A", "#FBFBF8", "#CDCDC8", "#A3A39E", "#21000000",
            "#F6F6F3", "#DFDFDA", "#A9A9A4", "#FFFFFF", "#A0A09B", "#C4C4BF");

        public static HwColors Colors(bool light) => light ? Light : Dark;

        /// <summary>Leuchten des Displays (Bernstein, ohne Versatz).</summary>
        public static DropShadowEffect AmberGlow(HwColors c, double opacity = 0.55) => HudParts.Glow(UiColor.Brush(c.LcdText).Color, opacity, 6);

        /// <summary>Vier kleine Schrauben in den Ecken (6 px vom Rand), als Überlagerung ohne Maus-Treffer.</summary>
        public static Grid Screws(double w, double h, HwColors c)
        {
            var host = new Grid { Width = w, Height = h, IsHitTestVisible = false };
            foreach (var (ha, va) in new[] { (HorizontalAlignment.Left, VerticalAlignment.Top), (HorizontalAlignment.Right, VerticalAlignment.Top),
                                             (HorizontalAlignment.Left, VerticalAlignment.Bottom), (HorizontalAlignment.Right, VerticalAlignment.Bottom) })
            {
                var screw = new Grid { Width = 6, Height = 6, HorizontalAlignment = ha, VerticalAlignment = va, Margin = new Thickness(6) };
                var head = new RadialGradientBrush(UiColor.Brush(c.ScrewHi).Color, UiColor.Brush(c.ScrewLo).Color)
                {
                    GradientOrigin = new Point(0.35, 0.35), Center = new Point(0.35, 0.35),
                };
                head.Freeze();
                screw.Children.Add(new Ellipse { Fill = head, Stroke = UiColor.Brush("#40000000"), StrokeThickness = 0.75 });
                screw.Children.Add(new Rectangle
                {
                    Width = 4, Height = 1, Fill = UiColor.Brush("#73000000"), RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new RotateTransform(35),
                });
                host.Children.Add(screw);
            }
            return host;
        }

        /// <summary>Vertieftes Display: dunkle Fläche, Schatten oben innen, feine Lichtkante unten.</summary>
        public static Border Lcd(HwColors c, string hiHex, UIElement content, Thickness padding)
        {
            var shade = new LinearGradientBrush(UiColor.Brush("#80000000").Color, UiColor.Brush("#00000000").Color, 90);
            shade.Freeze();
            var inner = new Grid();
            inner.Children.Add(new Border { Padding = padding, Child = content });
            inner.Children.Add(new Border
            {
                Height = 4, VerticalAlignment = VerticalAlignment.Top, Background = shade, CornerRadius = new CornerRadius(6, 6, 0, 0),
                IsHitTestVisible = false,
            });
            return new Border
            {
                CornerRadius = new CornerRadius(6), Background = UiColor.Brush(c.Lcd), BorderBrush = UiColor.Brush(hiHex),
                BorderThickness = new Thickness(0, 0, 0, 1), Child = inner, ClipToBounds = true,
            };
        }

        /// <summary>Taste mit Kante: Unterteil (2 px tiefer) in Kantenfarbe, Oberseite mit Verlauf; gedrückt = 1 px tiefer.</summary>
        public static Grid Key(HwColors c, double w, double h, out Border face, bool pressed = false)
        {
            var key = new Grid { Width = w, Height = h + 2 };
            key.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = UiColor.Brush(c.KeyEdge), Margin = new Thickness(0, 2, 0, 0) });
            var top = new LinearGradientBrush(UiColor.Brush(c.KeyTop).Color, UiColor.Brush(c.KeyBottom).Color, 90);
            top.Freeze();
            face = new Border
            {
                CornerRadius = new CornerRadius(6), Background = top, BorderBrush = UiColor.Brush(c.KeyEdge), BorderThickness = new Thickness(1),
                Margin = new Thickness(0, pressed ? 1 : 0, 0, pressed ? 1 : 2),
            };
            key.Children.Add(face);
            return key;
        }

        /// <summary>LED-Streifen oben auf einer Taste (aus bzw. in Akzentfarbe leuchtend).</summary>
        public static Rectangle KeyLed(double width, bool on, Brush accent, HwColors c) => new()
        {
            Width = width, Height = 2, RadiusX = 1, RadiusY = 1, VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0), IsHitTestVisible = false,
            Fill = on ? accent : UiColor.Brush(c.LedOff), Effect = on ? HudParts.Glow(((SolidColorBrush)accent).Color, 0.9, 5) : null,
        };

        /// <summary>Runde LED (an: heller Kern + Leuchten, aus: matt).</summary>
        public static Ellipse Led(double size, string? onHex, HwColors c)
        {
            if (onHex == null)
                return new Ellipse { Width = size, Height = size, Fill = UiColor.Brush(c.LedOff), Stroke = UiColor.Brush("#33000000"), StrokeThickness = 1 };
            var col = UiColor.Brush(onHex).Color;
            var core = new RadialGradientBrush(Color.FromRgb((byte)((col.R + 255 * 2) / 3), (byte)((col.G + 255 * 2) / 3), (byte)((col.B + 255 * 2) / 3)), col)
            {
                GradientOrigin = new Point(0.4, 0.35), Center = new Point(0.4, 0.35),
            };
            core.Freeze();
            return new Ellipse { Width = size, Height = size, Fill = core, Effect = HudParts.Glow(col, 0.9, 10) };
        }

        /// <summary>Punkt auf dem Kreis um (cx, cy) mit Radius r beim Winkel deg (0° = oben, im Uhrzeigersinn).</summary>
        public static Point OnCircle(double cx, double cy, double r, double deg)
        {
            double a = deg * Math.PI / 180;
            return new Point(cx + r * Math.Sin(a), cy - r * Math.Cos(a));
        }

        /// <summary>Knopf (Kante + Fläche mit Verlauf) als Kreise um die Mitte.</summary>
        public static void AddKnobBody(Panel host, double size, double kr, HwColors c)
        {
            host.Children.Add(new Ellipse { Width = (kr + 2) * 2, Height = (kr + 2) * 2, Fill = UiColor.Brush(c.KnobEdge) });
            var face = new RadialGradientBrush(UiColor.Brush(c.KnobTop).Color, UiColor.Brush(c.KnobBottom).Color)
            {
                GradientOrigin = new Point(0.35, 0.3), Center = new Point(0.35, 0.3), RadiusX = 0.8, RadiusY = 0.8,
            };
            face.Freeze();
            host.Children.Add(new Ellipse { Width = kr * 2, Height = kr * 2, Fill = face });
        }
    }

    /// <summary>
    /// Drehknopf mit LED-Kranz (21 LEDs von −135° bis +135°, 5 % je LED): Knopf mit Rändel und Zeiger dreht sich zum Wert
    /// (animiert), LEDs bis zum Wert leuchten (eigene Lage mit Leuchten, die übrigen LEDs matt darunter).
    /// </summary>
    internal sealed class KnobView : Grid
    {
        private const int Leds = 21;
        private readonly Ellipse[] _front = new Ellipse[Leds];
        private readonly RotateTransform _rot = new(-135);
        private readonly Line _pointer;
        private readonly Brush _accent;
        private readonly Brush _mute;

        public KnobView(HwColors c, Brush accent, Brush mute, double size = 60)
        {
            _accent = accent;
            _mute = mute;
            Width = Height = size;
            double cx = size / 2, ringR = size * 0.45, kr = size * 0.283;
            var back = new Canvas();
            var front = new Canvas { Effect = HudParts.Glow(((SolidColorBrush)accent).Color, 0.8, 4) };
            for (int i = 0; i < Leds; i++)
            {
                var p = HardwareParts.OnCircle(cx, cx, ringR, -135 + i * 13.5);
                var off = new Ellipse { Width = 4, Height = 4, Fill = UiColor.Brush(c.LedOff) };
                Canvas.SetLeft(off, p.X - 2);
                Canvas.SetTop(off, p.Y - 2);
                back.Children.Add(off);
                _front[i] = new Ellipse { Width = 4, Height = 4, Fill = Brushes.Transparent };
                Canvas.SetLeft(_front[i], p.X - 2);
                Canvas.SetTop(_front[i], p.Y - 2);
                front.Children.Add(_front[i]);
            }
            Children.Add(back);
            Children.Add(front);
            HardwareParts.AddKnobBody(this, size, kr, c);

            // Rändel + Zeiger drehen sich gemeinsam
            var turn = new Canvas { Width = size, Height = size, RenderTransform = _rot };
            _rot.CenterX = _rot.CenterY = cx;
            var knurl = new StreamGeometry();
            using (var ctx = knurl.Open())
            {
                for (int i = 0; i < 24; i++)
                {
                    ctx.BeginFigure(HardwareParts.OnCircle(cx, cx, kr - 2.5, i * 15), false, false);
                    ctx.LineTo(HardwareParts.OnCircle(cx, cx, kr, i * 15), true, false);
                }
            }
            knurl.Freeze();
            turn.Children.Add(new System.Windows.Shapes.Path { Data = knurl, Stroke = UiColor.Brush(c.Knurl), StrokeThickness = 1 });
            _pointer = new Line
            {
                X1 = cx, Y1 = cx - 5, X2 = cx, Y2 = cx - (kr - 5), Stroke = accent, StrokeThickness = 2.5,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            };
            turn.Children.Add(_pointer);
            Children.Add(turn);
        }

        /// <summary>Wert 0–100 zeigen; Stumm: Zeiger rot, LEDs aus. animate: Knopf dreht weich zur neuen Stellung.</summary>
        public void Set(int pct, bool muted, bool animate)
        {
            double angle = -135 + Math.Clamp(pct, 0, 100) * 2.7;
            if (animate)
                _rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(angle, TimeSpan.FromMilliseconds(120))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
            else
            {
                _rot.BeginAnimation(RotateTransform.AngleProperty, null);
                _rot.Angle = angle;
            }
            _pointer.Stroke = muted ? _mute : _accent;
            for (int i = 0; i < Leds; i++) _front[i].Fill = !muted && pct > 0 && i * 5 <= pct ? _accent : Brushes.Transparent;
        }
    }

    /// <summary>Wahlschalter fürs Profil: ein Punkt je Profil in dessen Farbe (aktiv größer und leuchtend), Zeiger aufs aktive, Logo auf dem Knopf.</summary>
    internal sealed class DialView : Grid
    {
        public DialView(HwColors c, IReadOnlyList<ProfileDefinition> profiles, int active, double size = 60)
        {
            Width = Height = size;
            double cx = size / 2, ringR = size * 0.45, kr = size * 0.283;
            int n = Math.Max(1, profiles.Count);
            double AngleOf(int i) => n == 1 ? 0 : -135 + i * 270.0 / (n - 1);
            var dots = new Canvas();
            for (int i = 0; i < profiles.Count; i++)
            {
                bool on = i == active;
                double r = on ? 3.2 : 2.2;
                var col = UiColor.Brush(profiles[i].Color);
                var p = HardwareParts.OnCircle(cx, cx, ringR, AngleOf(i));
                var dot = new Ellipse
                {
                    Width = r * 2, Height = r * 2, Fill = col, Opacity = on ? 1 : 0.45,
                    Effect = on ? HudParts.Glow(col.Color, 0.9, 6) : null,
                };
                Canvas.SetLeft(dot, p.X - r);
                Canvas.SetTop(dot, p.Y - r);
                dots.Children.Add(dot);
            }
            Children.Add(dots);
            HardwareParts.AddKnobBody(this, size, kr, c);
            if (active < 0 || active >= profiles.Count) return;
            var activeColor = UiColor.Brush(profiles[active].Color);
            var a = HardwareParts.OnCircle(cx, cx, kr - 6, AngleOf(active));
            var b = HardwareParts.OnCircle(cx, cx, kr - 1.5, AngleOf(active));
            var pointer = new Canvas();
            pointer.Children.Add(new Line
            {
                X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = activeColor, StrokeThickness = 2.5,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            });
            Children.Add(pointer);
            var logo = profiles[active].CreateLogo(15, activeColor);
            if (logo is Image) logo.Width = logo.Height = 14;
            logo.HorizontalAlignment = HorizontalAlignment.Center;
            logo.VerticalAlignment = VerticalAlignment.Center;
            Children.Add(logo);
        }
    }

    /// <summary>Drei Sieben-Segment-Ziffern (leicht kursiv): unbenutzte Segmente schwach sichtbar, leuchtende mit Schein.</summary>
    internal sealed class SevenSegment : Grid
    {
        private const string Order = "abcdefg";
        private static readonly Dictionary<char, string> Map = new()
        {
            ['0'] = "abcdef", ['1'] = "bc", ['2'] = "abged", ['3'] = "abgcd", ['4'] = "fgbc",
            ['5'] = "afgcd", ['6'] = "afgedc", ['7'] = "abc", ['8'] = "abcdefg", ['9'] = "abcdfg",
        };
        private readonly System.Windows.Shapes.Path[,] _lit = new System.Windows.Shapes.Path[3, 7];
        private readonly Brush _on;

        public SevenSegment(HwColors c)
        {
            Width = 66;
            Height = 28;
            _on = UiColor.Brush(c.LcdText);
            var ghost = new Canvas();
            var front = new Canvas { Effect = HardwareParts.AmberGlow(c, 0.7) };
            for (int d = 0; d < 3; d++)
                for (int k = 0; k < 7; k++)
                {
                    var geo = Segment(Order[k], d * 22 + 2);
                    ghost.Children.Add(new System.Windows.Shapes.Path { Data = geo, Fill = UiColor.Brush(c.LcdGhost) });
                    _lit[d, k] = new System.Windows.Shapes.Path { Data = geo, Fill = Brushes.Transparent };
                    front.Children.Add(_lit[d, k]);
                }
            Children.Add(ghost);
            Children.Add(front);
        }

        /// <summary>Wert rechtsbündig zeigen (null = alle Segmente aus).</summary>
        public void Show(int? value)
        {
            string s = value == null ? "   " : Math.Clamp(value.Value, 0, 999).ToString().PadLeft(3);
            for (int d = 0; d < 3; d++)
            {
                string on = Map.TryGetValue(s[d], out var m) ? m : "";
                for (int k = 0; k < 7; k++) _lit[d, k].Fill = on.Contains(Order[k]) ? _on : Brushes.Transparent;
            }
        }

        private static Geometry Segment(char seg, double x0)
        {
            const double w = 16, h = 28, t = 3.4, g = 0.8, H = h / 2;
            (double, double)[] pts = seg switch
            {
                'a' => new[] { (g + t / 2, 0.0), (w - g - t / 2, 0.0), (w - g - t, t), (g + t, t) },
                'd' => new[] { (g + t, h - t), (w - g - t, h - t), (w - g - t / 2, h), (g + t / 2, h) },
                'g' => new[] { (g + t, H - t / 2), (w - g - t, H - t / 2), (w - g - t / 2, H), (w - g - t, H + t / 2), (g + t, H + t / 2), (g + t / 2, H) },
                'f' => new[] { (0.0, g + t / 2), (t, g + t), (t, H - g - t / 2), (0.0, H - g) },
                'b' => new[] { (w, g + t / 2), (w, H - g), (w - t, H - g - t / 2), (w - t, g + t) },
                'e' => new[] { (0.0, H + g), (t, H + g + t / 2), (t, h - g - t), (0.0, h - g - t / 2) },
                _ => new[] { (w, H + g), (w, h - g - t / 2), (w - t, h - g - t), (w - t, H + g + t / 2) }, // c
            };
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                Point P((double X, double Y) p) => new(p.X + x0 + (h - p.Y) * 0.09, p.Y); // leicht kursiv
                ctx.BeginFigure(P(pts[0]), true, true);
                for (int i = 1; i < pts.Length; i++) ctx.LineTo(P(pts[i]), false, false);
            }
            geo.Freeze();
            return geo;
        }
    }
}
