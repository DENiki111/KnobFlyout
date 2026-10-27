using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Windows.Media;

namespace KnobEarTrumpet.Osd
{
    /// <summary>
    /// Tonale Farben im Sinne von Material You für das Design „Material You“: aus einer Grundfarbe (Windows-Akzent
    /// bzw. Cover-Farbe) werden die Farbrollen errechnet. Ton = Helligkeit L* (wie bei Material), Farbton und
    /// Sättigung in OKLCH (bleibt farbtreu, Blau kippt bei dunklen Tönen nicht ins Lila); zu kräftige Farben werden
    /// in den sRGB-Farbraum zurückgenommen.
    /// </summary>
    internal static class MaterialColors
    {
        public static readonly Color DefaultSeed = Color.FromRgb(0x00, 0x78, 0xD4);

        // Sättigung je Rolle (OKLCH): Primär kräftig, Sekundär gedämpft, Flächen nur leicht getönt
        private const double P = 0.13, S = 0.045, N = 0.012, NV = 0.022;

        private static readonly ConcurrentDictionary<(Color, bool), DesignPalette> Schemes = new();

        /// <summary>Alle Farbrollen zur Grundfarbe (zwischengespeichert je Grundfarbe und hell/dunkel).</summary>
        public static DesignPalette Scheme(Color seed, bool light) => Schemes.GetOrAdd((seed, light), k => Build(k.Item1, k.Item2));

        private static DesignPalette Build(Color seed, bool light)
        {
            string T(double tone, double chroma) => Tone(seed, tone, chroma);
            return light
                ? new DesignPalette(
                    Bg: T(92, N), Fg: T(10, N), Sub: T(30, NV), Track: T(88, S), Fill: T(40, P), Button: "#14000000",
                    Edge: "#0F000000", Mute: "#B3261E", On: T(40, P), OnText: "#FFFFFF", Off: T(88, N), OffText: T(50, NV),
                    ArtPlaceholder: T(88, N), Tile: T(88, N),
                    OnAccent: "#FFFFFF", Container: T(90, P), OnContainer: T(10, P), Outline: T(50, NV), OutlineVariant: T(80, NV),
                    MuteContainer: "#F9DEDC", OnMuteContainer: "#410E0B", OnTrack: T(10, S))
                : new DesignPalette(
                    Bg: T(17, N), Fg: T(90, N), Sub: T(80, NV), Track: T(30, S), Fill: T(80, P), Button: "#14FFFFFF",
                    Edge: "#0FFFFFFF", Mute: "#F2B8B5", On: T(80, P), OnText: T(20, P), Off: T(24, N), OffText: T(60, NV),
                    ArtPlaceholder: T(24, N), Tile: T(24, N),
                    OnAccent: T(20, P), Container: T(30, P), OnContainer: T(90, P), Outline: T(60, NV), OutlineVariant: T(30, NV),
                    MuteContainer: "#8C1D18", OnMuteContainer: "#F9DEDC", OnTrack: T(90, S));
        }

        /// <summary>Farbe mit dem Farbton von seed, Helligkeit tone (L*, 0–100) und Sättigung chroma (OKLCH) als "#RRGGBB".</summary>
        public static string Tone(Color seed, double tone, double chroma)
        {
            var (_, _, h) = ToOk(seed);
            double y = LabInv((tone + 16) / 116.0); // gewünschte Helligkeit als Leuchtdichte Y

            // Bei Sättigung c die OK-Helligkeit L suchen, deren Leuchtdichte zum Ton passt.
            double[] Make(double c)
            {
                double lo = 0, hi = 1;
                for (int i = 0; i < 22; i++)
                {
                    double mid = (lo + hi) / 2;
                    var v = OkToLinear(mid, c, h);
                    if (0.2126 * v[0] + 0.7152 * v[1] + 0.0722 * v[2] < y) lo = mid; else hi = mid;
                }
                return OkToLinear((lo + hi) / 2, c, h);
            }

            // Größte Sättigung ≤ chroma, die noch in sRGB passt.
            double cLo = 0, cHi = chroma;
            var best = Make(0);
            for (int i = 0; i < 16; i++)
            {
                double mid = (cLo + cHi) / 2;
                var v = Make(mid);
                if (v.All(x => x >= -0.0005 && x <= 1.0005)) { best = v; cLo = mid; } else cHi = mid;
            }
            byte B(double v) => (byte)Math.Round(Math.Clamp(Gamma(Math.Clamp(v, 0, 1)), 0, 255));
            return $"#{B(best[0]):X2}{B(best[1]):X2}{B(best[2]):X2}";
        }

        public static string Tone(string hex, double tone, double chroma) => Tone(UiColor.Brush(hex).Color, tone, chroma);

        private static double Linear(byte c)
        {
            double v = c / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        private static double Gamma(double v) => (v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055) * 255;

        private static double LabInv(double t)
        {
            double t3 = t * t * t;
            return t3 > 216.0 / 24389 ? t3 : (116 * t - 16) / (24389.0 / 27);
        }

        private static (double L, double C, double H) ToOk(Color c)
        {
            double r = Linear(c.R), g = Linear(c.G), b = Linear(c.B);
            double l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
            double m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
            double s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
            double L = 0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s;
            double A = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s;
            double Bv = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;
            return (L, Math.Sqrt(A * A + Bv * Bv), Math.Atan2(Bv, A));
        }

        private static double[] OkToLinear(double L, double C, double h)
        {
            double A = C * Math.Cos(h), Bv = C * Math.Sin(h);
            double l = Math.Pow(L + 0.3963377774 * A + 0.2158037573 * Bv, 3);
            double m = Math.Pow(L - 0.1055613458 * A - 0.0638541728 * Bv, 3);
            double s = Math.Pow(L - 0.0894841775 * A - 1.2914855480 * Bv, 3);
            return new[]
            {
                4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
                -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
                -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s,
            };
        }
    }
}
