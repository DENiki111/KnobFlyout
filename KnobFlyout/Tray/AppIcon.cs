using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KnobFlyout.Tray
{
    /// <summary>
    /// App-Symbol, zur Laufzeit gezeichnet: flaches blaues abgerundetes Quadrat mit schwarzem Drehknopf
    /// (weißer Zeiger, Skala aus Punkten). Wird für das Settings-Fenster verwendet (das Tray nutzt RenderTray:
    /// nur der Knopf, ohne Hintergrund); app.ico (Symbol der exe und des Setups) ist aus genau
    /// dieser Zeichnung erzeugt (16–256 px) – bei einer Änderung hier app.ico neu erzeugen.
    /// </summary>
    internal static class AppIcon
    {
        /// <summary>Symbol als WPF-Bild in size×size Pixeln.</summary>
        public static BitmapSource Render(int size)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                double radius = size * 0.24;
                var rect = new Rect(0, 0, size, size);

                // Flach und klar: einfarbiges Blau (ModernFlyouts-Akzent), kein Verlauf, kein Rand.
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)), null, rect, radius, radius);
                DrawKnob(dc, size, Brushes.Black, Brushes.White, size * 0.28, size * 0.415, Math.Max(0.85, size * 0.04));
            }

            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>
        /// Tray-Variante: nur der Knopf auf durchsichtigem Hintergrund, die Punkte reichen bis an den Rand und sind
        /// größer als im Logo, damit man sie auch bei 16 px sieht. Der Zeiger ist ausgespart.
        /// Farbe wie die Windows-eigenen Tray-Symbole: weiß auf dunkler, schwarz auf heller Taskleiste.
        /// </summary>
        public static BitmapSource RenderTray(int size, bool lightTaskbar)
        {
            var brush = lightTaskbar ? Brushes.Black : Brushes.White;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                double dot = size * 0.07; // etwas über 1 px Radius bei 16 px: füllt dort 2 × 2 Pixel fast ganz
                DrawKnob(dc, size, brush, null, size * 0.28, size * 0.4375, dot);
            }

            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>
        /// Drehknopf: Scheibe mit Zeiger auf etwa 1 Uhr und Skala aus sieben Punkten, unten offen wie am echten Regler.
        /// pointer = null: Zeiger aus der Scheibe ausgespart (durchsichtig).
        /// </summary>
        private static void DrawKnob(DrawingContext dc, int size, Brush brush, Brush? pointer, double knob, double ring, double dot)
        {
            var center = new Point(size / 2.0, size / 2.0);
            Point At(double degrees, double distance)
            {
                double rad = degrees * Math.PI / 180;
                return new Point(center.X + Math.Cos(rad) * distance, center.Y + Math.Sin(rad) * distance);
            }

            var line = new LineGeometry(At(-60, knob * 0.3), At(-60, pointer == null ? knob * 1.15 : knob * 0.8)); // ausgespart: bis über den Rand, sonst bleibt er klein unsichtbar
            var pen = new Pen(pointer ?? brush, Math.Max(pointer == null ? 1.8 : 1.4, size * 0.085)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            Geometry disc = new EllipseGeometry(center, knob, knob);
            if (pointer == null) disc = Geometry.Combine(disc, line.GetWidenedPathGeometry(pen), GeometryCombineMode.Exclude, null);
            dc.DrawGeometry(brush, null, disc);
            if (pointer != null) dc.DrawGeometry(null, pen, line);

            for (int degrees = -225; degrees <= 45; degrees += 45)
            {
                var p = At(degrees, ring);
                if (size <= 32) p = new Point(Math.Round(p.X), Math.Round(p.Y)); // klein: auf ganze Pixel, sonst verschwimmen die Punkte
                dc.DrawEllipse(brush, null, p, dot, dot);
            }
        }

        /// <summary>Echte Größe eines Symbols im Infobereich (16 px bei 100 %): heruntergerechnet würden die Punkte verschwimmen.</summary>
        public static int TraySize => Math.Max(16, GetSystemMetrics(SM_CXSMICON));

        private const int SM_CXSMICON = 49;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        /// <summary>Tray-Symbol als GDI-Icon (mit Alphakanal, eigener Handle – Aufrufer gibt es per Dispose frei).</summary>
        public static System.Drawing.Icon CreateTrayIcon(int size, bool lightTaskbar)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(RenderTray(size, lightTaskbar)));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            stream.Position = 0;
            using var bitmap = new System.Drawing.Bitmap(stream);
            IntPtr handle = bitmap.GetHicon();
            try
            {
                using var temp = System.Drawing.Icon.FromHandle(handle);
                return (System.Drawing.Icon)temp.Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }
}
