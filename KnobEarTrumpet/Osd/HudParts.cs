using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace KnobEarTrumpet.Osd
{
    /// <summary>Bausteine des Designs „Gaming-HUD“ (Designs.Hud), gemeinsam für OSD-Karten, Neue-App-Popup und Media-Flyout.</summary>
    internal static class HudParts
    {
        public const double SegmentSkew = -18; // schräge LED-Segmente
        private const char Hair = (char)0x200A; // Haarspatium

        /// <summary>Fläche mit abgeschrägten Ecken oben links und unten rechts (c = Schräge), optional um inset nach innen gerückt.</summary>
        public static Geometry Chamfer(double w, double h, double c, double inset = 0)
        {
            double l = inset, t = inset, r = w - inset, b = h - inset;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(l + c, t), true, true);
                ctx.PolyLineTo(new[]
                {
                    new Point(r, t), new Point(r, b - c), new Point(r - c, b), new Point(l, b), new Point(l, t + c),
                }, true, false);
            }
            g.Freeze();
            return g;
        }

        /// <summary>Abgeschrägter Kasten (Fläche + Umriss) als Hintergrund für Knöpfe, Schilder und Karten.</summary>
        public static System.Windows.Shapes.Path ChamferPath(double w, double h, double c, Brush? fill, Brush? stroke, double strokeWidth = 1) => new()
        {
            Data = Chamfer(w, h, c, strokeWidth / 2), Fill = fill, Stroke = stroke, StrokeThickness = stroke == null ? 0 : strokeWidth,
            IsHitTestVisible = false,
        };

        /// <summary>Eck-Klammern oben rechts und unten links (Akzentfarbe, 2 px, 10 px lang, 4 px vom Rand).</summary>
        public static System.Windows.Shapes.Path Brackets(double w, double h, Brush brush) => new()
        {
            Data = BracketGeometry(w, h), Stroke = brush, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Miter, IsHitTestVisible = false,
        };

        public static Geometry BracketGeometry(double w, double h)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(w - 15, 5), false, false);
                ctx.PolyLineTo(new[] { new Point(w - 5, 5), new Point(w - 5, 15) }, true, false);
                ctx.BeginFigure(new Point(5, h - 15), false, false);
                ctx.PolyLineTo(new[] { new Point(5, h - 5), new Point(15, h - 5) }, true, false);
            }
            g.Freeze();
            return g;
        }

        /// <summary>Klammern an allen vier Ecken eines Rechtecks (w × h), um out nach außen gerückt (Cover).</summary>
        public static Geometry CornerGeometry(double w, double h, double arm, double out_)
        {
            double l = -out_, t = -out_, r = w + out_, b = h + out_;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                void L(Point a, Point m, Point z)
                {
                    ctx.BeginFigure(a, false, false);
                    ctx.PolyLineTo(new[] { m, z }, true, false);
                }
                L(new Point(l, t + arm), new Point(l, t), new Point(l + arm, t));
                L(new Point(r - arm, t), new Point(r, t), new Point(r, t + arm));
                L(new Point(l, b - arm), new Point(l, b), new Point(l + arm, b));
                L(new Point(r - arm, b), new Point(r, b), new Point(r, b - arm));
            }
            g.Freeze();
            return g;
        }

        /// <summary>Feine waagrechte Scanlines (1 px Linie, 2 px frei) als gekachelte Bitmap – schnell, auch beim Animieren.</summary>
        public static Brush Scanlines(string hex)
        {
            var c = UiColor.Brush(hex).Color;
            // Pbgra32: vormultipliziert
            byte a = c.A, r = (byte)(c.R * a / 255), g = (byte)(c.G * a / 255), b = (byte)(c.B * a / 255);
            var px = new byte[] { b, g, r, a, 0, 0, 0, 0, 0, 0, 0, 0 };
            var bmp = BitmapSource.Create(1, 3, 96, 96, PixelFormats.Pbgra32, null, px, 4);
            bmp.Freeze();
            var brush = new ImageBrush(bmp)
            {
                TileMode = TileMode.Tile, Stretch = Stretch.Fill,
                Viewport = new Rect(0, 0, 1, 3), ViewportUnits = BrushMappingMode.Absolute,
            };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
            brush.Freeze();
            return brush;
        }

        /// <summary>Leuchten in der Akzentfarbe (Schatten ohne Versatz).</summary>
        public static DropShadowEffect Glow(Color color, double opacity, double blur = 8) => new()
        {
            Color = color, ShadowDepth = 0, BlurRadius = blur, Opacity = opacity,
        };

        /// <summary>Großbuchstaben mit Sperrung (WPF kennt keinen Zeichenabstand: Haarspatien zwischen den Zeichen).</summary>
        public static string Caps(string text) => string.Join<char>(Hair, text.ToUpperInvariant());

        /// <summary>
        /// Segmentleiste: unten alle Segmente in Spurfarbe, darüber die leuchtenden (front, gleiche Anzahl) – so leuchten nur die
        /// aktiven. Segmente füllen die Breite gleichmäßig (UniformGrid), schräg gestellt.
        /// </summary>
        public static Grid Segments(int count, double height, double gap, Brush track, out System.Windows.Shapes.Rectangle[] front, out UniformGrid frontGrid)
        {
            UniformGrid Row(Func<System.Windows.Shapes.Rectangle> make, out System.Windows.Shapes.Rectangle[] cells)
            {
                var grid = new UniformGrid { Rows = 1, Columns = count, Height = height };
                cells = Enumerable.Range(0, count).Select(i =>
                {
                    var r = make();
                    r.Margin = new Thickness(i == 0 ? 0 : gap / 2, 0, i == count - 1 ? 0 : gap / 2, 0);
                    r.RenderTransformOrigin = new Point(0.5, 0.5);
                    r.RenderTransform = new SkewTransform(SegmentSkew, 0);
                    grid.Children.Add(r);
                    return r;
                }).ToArray();
                return grid;
            }
            var host = new Grid { Height = height, Margin = new Thickness(2, 0, 2, 0) }; // Schräge ragt sonst über den Rand
            host.Children.Add(Row(() => new System.Windows.Shapes.Rectangle { Fill = track }, out _));
            frontGrid = Row(() => new System.Windows.Shapes.Rectangle { Fill = Brushes.Transparent }, out front);
            host.Children.Add(frontGrid);
            return host;
        }
    }
}
