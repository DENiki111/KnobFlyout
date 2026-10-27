using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KnobEarTrumpet.Tray
{
    /// <summary>
    /// App-Symbol, zur Laufzeit gezeichnet: flaches blaues abgerundetes Quadrat mit weißem
    /// Fluent-Lautsprecher-Glyph. Wird für das Settings-Fenster verwendet (das Tray nutzt RenderTray:
    /// nur der Lautsprecher, ohne Hintergrund); app.ico (Symbol der exe und des Setups) ist aus genau
    /// dieser Zeichnung erzeugt (16–256 px) – bei einer Änderung hier app.ico neu erzeugen.
    /// </summary>
    internal static class AppIcon
    {
        private const string SpeakerGlyph = "\uE995"; // Volume3 (Lautsprecher mit drei Wellen)

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

                // Glyph exakt mittig über seine tatsächlichen Umrisse (nicht über die Zeilenhöhe).
                var text = new FormattedText(SpeakerGlyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                    size * 0.56, Brushes.White, 1.0);
                var geometry = text.BuildGeometry(new Point(0, 0));
                var b = geometry.Bounds;
                if (!b.IsEmpty)
                {
                    geometry.Transform = new TranslateTransform((size - b.Width) / 2 - b.X, (size - b.Height) / 2 - b.Y);
                    dc.DrawGeometry(Brushes.White, null, geometry);
                }
            }

            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>
        /// Tray-Variante: nur der Lautsprecher auf durchsichtigem Hintergrund, füllt das Symbol fast ganz aus.
        /// Farbe wie die Windows-eigenen Tray-Symbole: weiß auf dunkler, schwarz auf heller Taskleiste.
        /// </summary>
        public static BitmapSource RenderTray(int size, bool lightTaskbar)
        {
            var brush = lightTaskbar ? Brushes.Black : Brushes.White;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var text = new FormattedText(SpeakerGlyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                    size, brush, 1.0);
                var geometry = text.BuildGeometry(new Point(0, 0));
                var b = geometry.Bounds;
                if (!b.IsEmpty)
                {
                    double scale = size * 0.92 / Math.Max(b.Width, b.Height);
                    var t = new TransformGroup();
                    t.Children.Add(new TranslateTransform(-b.X - b.Width / 2, -b.Y - b.Height / 2));
                    t.Children.Add(new ScaleTransform(scale, scale));
                    t.Children.Add(new TranslateTransform(size / 2.0, size / 2.0));
                    geometry.Transform = t;
                    dc.DrawGeometry(brush, null, geometry);
                }
            }

            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }

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
