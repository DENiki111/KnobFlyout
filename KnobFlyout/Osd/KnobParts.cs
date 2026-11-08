using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using KnobFlyout.Profiles;

namespace KnobFlyout.Osd
{
    /// <summary>
    /// Bausteine des Designs „Drehknopf“ (Designs.Knob): ein schlichter, flacher Drehknopf ohne Rändel, Schrauben und Leuchten –
    /// runder Knopf mit Zeiger, darum ein dünner Bogen (270°) als Pegel. Gemeinsam für OSD-Karten, Neue-App-Popup und Media-Flyout.
    /// </summary>
    internal static class KnobParts
    {
        /// <summary>Punkt auf dem Kreis um (cx, cy); 0° = oben, im Uhrzeigersinn.</summary>
        public static Point OnCircle(double cx, double cy, double r, double degrees)
        {
            double a = degrees * Math.PI / 180;
            return new Point(cx + r * Math.Sin(a), cy - r * Math.Cos(a));
        }

        /// <summary>Kreisbogen von from bis to (Grad, 0° = oben); leer, wenn kürzer als ein halbes Grad.</summary>
        public static Geometry Arc(double c, double r, double from, double to)
        {
            if (to - from < 0.5) return Geometry.Empty;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(OnCircle(c, c, r, from), false, false);
                ctx.ArcTo(OnCircle(c, c, r, to), new Size(r, r), 0, to - from > 180, SweepDirection.Clockwise, true, true);
            }
            g.Freeze();
            return g;
        }

        /// <summary>Runde Fläche mit feinem Rand und Inhalt in der Mitte (Symbol-Knopf, Popup-Knöpfe).</summary>
        public static Grid Round(double size, Brush body, Brush edge, UIElement? content)
        {
            var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(new Ellipse { Fill = body, Stroke = edge, StrokeThickness = 1 });
            if (content != null) g.Children.Add(content);
            return g;
        }
    }

    /// <summary>
    /// Flacher Drehknopf: Bogen (Spur) mit Füllung bis zum Pegel, darin der Knopf mit Zeiger. Set animiert den Wert
    /// (Bogen und Zeiger zugleich); bei „Stumm“ wird der Bogen grau und der Zeiger rot.
    /// </summary>
    internal sealed class CleanKnob : Grid
    {
        private const double Start = -135, Sweep = 270;

        private static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            "Value", typeof(double), typeof(CleanKnob), new PropertyMetadata(0.0, (d, e) => ((CleanKnob)d).Draw((double)e.NewValue)));

        private readonly Path _fill;
        private readonly Rectangle _pointer;
        private readonly RotateTransform _rot = new(Start);
        private readonly double _c, _r;
        private readonly Brush _fillBrush, _pointerBrush, _muteBrush, _mutedFill;

        public CleanKnob(double size, Brush track, Brush fill, Brush body, Brush edge, Brush pointer, Brush mute, Brush mutedFill, double ring = 3)
        {
            Width = Height = size;
            _fillBrush = fill;
            _pointerBrush = pointer;
            _muteBrush = mute;
            _mutedFill = mutedFill;
            _c = size / 2;
            _r = _c - ring / 2;
            Children.Add(new Path
            {
                Data = KnobParts.Arc(_c, _r, Start, Start + Sweep), Stroke = track, StrokeThickness = ring,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            });
            _fill = new Path { Stroke = fill, StrokeThickness = ring, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            Children.Add(_fill);

            double bodySize = size - 2 * ring - 8;
            var knob = new Grid
            {
                Width = bodySize, Height = bodySize, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _rot,
            };
            knob.Children.Add(new Ellipse { Fill = body, Stroke = edge, StrokeThickness = 1 });
            _pointer = new Rectangle
            {
                Width = 2.5, Height = Math.Max(4, bodySize * 0.3), RadiusX = 1.25, RadiusY = 1.25, Fill = pointer,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, bodySize * 0.12, 0, 0),
            };
            knob.Children.Add(_pointer);
            Children.Add(knob);
        }

        /// <summary>Pegel 0–100 zeigen (animiert, wenn sichtbar).</summary>
        public void Set(int pct, bool muted, bool animate)
        {
            _fill.Stroke = muted ? _mutedFill : _fillBrush;
            _pointer.Fill = muted ? _muteBrush : _pointerBrush;
            double target = Math.Clamp(pct, 0, 100) / 100.0;
            if (animate) BeginAnimation(ValueProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(110)));
            else
            {
                BeginAnimation(ValueProperty, null);
                SetValue(ValueProperty, target);
                Draw(target);
            }
        }

        private void Draw(double value)
        {
            _fill.Data = KnobParts.Arc(_c, _r, Start, Start + Sweep * value);
            _rot.Angle = Start + Sweep * value;
        }
    }

    /// <summary>Wahlschalter fürs Profil: Knopf mit Zeiger auf das aktive Profil, die Profile als Punkte in ihren Farben rundherum.</summary>
    internal sealed class CleanDial : Grid
    {
        public CleanDial(double size, IReadOnlyList<ProfileDefinition> profiles, int active, Brush body, Brush edge, Brush fallback)
        {
            Width = Height = size;
            double c = size / 2, ring = c - 3;
            int n = Math.Max(1, profiles.Count);
            double AngleOf(int i) => n == 1 ? 0 : -135 + i * 270.0 / (n - 1);
            var dots = new Canvas();
            for (int i = 0; i < profiles.Count; i++)
            {
                bool on = i == active;
                double r = on ? 3 : 2;
                var p = KnobParts.OnCircle(c, c, ring, AngleOf(i));
                var dot = new Ellipse { Width = r * 2, Height = r * 2, Fill = UiColor.Brush(profiles[i].Color), Opacity = on ? 1 : 0.5 };
                Canvas.SetLeft(dot, p.X - r);
                Canvas.SetTop(dot, p.Y - r);
                dots.Children.Add(dot);
            }
            Children.Add(dots);

            double bodySize = size - 16;
            var knob = new Grid
            {
                Width = bodySize, Height = bodySize, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(active >= 0 ? AngleOf(active) : 0),
            };
            knob.Children.Add(new Ellipse { Fill = body, Stroke = edge, StrokeThickness = 1 });
            knob.Children.Add(new Rectangle
            {
                Width = 2.5, Height = Math.Max(4, bodySize * 0.3), RadiusX = 1.25, RadiusY = 1.25,
                Fill = active >= 0 && active < profiles.Count ? UiColor.Brush(profiles[active].Color) : fallback,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, bodySize * 0.12, 0, 0),
            });
            Children.Add(knob);
        }
    }
}
