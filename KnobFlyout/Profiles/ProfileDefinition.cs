using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KnobFlyout.Config;

namespace KnobFlyout.Profiles
{
    /// <summary>
    /// Ein Profil zur Laufzeit (aus cfg.profile_list): Key (für profiles/active_profile), Anzeigename,
    /// Logo und Farbe. Logo = eigenes Bild (Image, Base64-PNG) oder Windows-Symbol (Glyph); Icon = Emoji
    /// der unveränderten Standard-Profile für das OSD-Design „Eigenes“ (leer = dort ebenfalls das Logo).
    /// </summary>
    public sealed record ProfileDefinition(string Key, string Name, string Glyph, string Icon, string? Image, string Color)
    {
        /// <summary>Logo als Element: eigenes Bild (size×size) oder Symbol in Schriftgröße size und Farbe brush.</summary>
        public FrameworkElement CreateLogo(double size, Brush brush)
        {
            var image = ProfileDefinitions.DecodeImage(Image);
            if (image != null)
                return new Image
                {
                    Source = image, Width = size, Height = size, Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
            return new TextBlock
            {
                Text = Glyph, FontFamily = ProfileDefinitions.IconFont, FontSize = size, Foreground = brush,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
        }
    }

    public static class ProfileDefinitions
    {
        /// <summary>Höchstzahl der Profile insgesamt (Standard-Profile zählen mit).</summary>
        public const int MaxProfiles = 6;

        internal static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

        /// <summary>Die 4 Standard-Profile (entspricht PROFILES in knob_service.py): Vorbelegung und Namen in beiden Sprachen.</summary>
        private static readonly (string Key, string Emoji, string Glyph, string De, string En)[] Builtin =
        {
            ("gaming",        "🎮", "\uE7FC", "Gaming", "Gaming"), // Game
            ("communication", "💬", "\uE8BD", "Komm.",  "Comms"),  // Message
            ("music",         "🎵", "\uE8D6", "Musik",  "Music"),  // Audio
            ("video",         "📺", "\uE714", "Video",  "Video"),  // Video
        };

        /// <summary>Vorgefertigte Logos zur Auswahl (Windows-Symbole aus Segoe Fluent Icons / MDL2 Assets).</summary>
        public static readonly string[] Gallery =
        {
            "\uE7FC", "\uE8BD", "\uE8D6", "\uE714", "\uEC4F", "\uE8B2", "\uE7F4", "\uE7F6", "\uE7F5", "\uE720", "\uE767",
            "\uE768", "\uE77B", "\uE716", "\uE717", "\uE8F2", "\uE715", "\uE722", "\uE91B", "\uE80F", "\uE821", "\uE7BE",
            "\uE774", "\uE753", "\uE945", "\uEB51", "\uE734", "\uE7E8", "\uE713", "\uE765", "\uE962", "\uE8EC", "\uE8B7",
            "\uE8F1", "\uE708", "\uE709", "\uE804", "\uE76E", "\uEA18", "\uE943", "\uE99A", "\uEA86", "\uE790", "\uE7C1",
        };

        /// <summary>Farben für neu angelegte Profile (reihum).</summary>
        public static readonly string[] Palette = { "#a855f7", "#3b82f6", "#4ade80", "#f97316", "#ec4899", "#14b8a6", "#eab308", "#ef4444" };

        /// <summary>Die 4 Standard-Profile als Einträge; Farben aus den alten profile_color_*-Feldern.</summary>
        public static List<ProfileEntry> DefaultEntries(AppConfig cfg) => Builtin.Select(b => new ProfileEntry
        {
            Key = b.Key, Glyph = b.Glyph, Emoji = b.Emoji,
            Color = b.Key switch
            {
                "gaming" => cfg.ProfileColorGaming,
                "communication" => cfg.ProfileColorCommunication,
                "music" => cfg.ProfileColorMusic,
                _ => cfg.ProfileColorVideo,
            },
        }).ToList();

        /// <summary>Alle Profile in Wechsel-Reihenfolge (leere Liste = die 4 Standard-Profile).</summary>
        public static IReadOnlyList<ProfileDefinition> For(AppConfig cfg)
        {
            var entries = cfg.ProfileList.Count > 0 ? cfg.ProfileList : DefaultEntries(cfg);
            return entries.Where(e => !string.IsNullOrEmpty(e.Key)).Take(MaxProfiles)
                .Select(e => new ProfileDefinition(e.Key, DisplayName(e),
                    string.IsNullOrEmpty(e.Glyph) ? "\uE734" : e.Glyph, // FavoriteStar, falls nichts gesetzt
                    e.Emoji, string.IsNullOrEmpty(e.Image) ? null : e.Image, e.Color))
                .ToList();
        }

        /// <summary>Entspricht PROFILE_KEYS = [p[0] for p in PROFILES].</summary>
        public static string[] Keys(AppConfig cfg) => For(cfg).Select(p => p.Key).ToArray();

        /// <summary>Eigener Name, sonst Standardname (Standard-Profile in der eingestellten Sprache).</summary>
        public static string DisplayName(ProfileEntry e)
        {
            if (!string.IsNullOrWhiteSpace(e.Name)) return e.Name.Trim();
            foreach (var b in Builtin)
                if (b.Key == e.Key) return Loc.T(b.De, b.En);
            return Loc.T("Profil", "Profile");
        }

        /// <summary>Beim Laden: Liste anlegen (4 Standard-Profile) und je Profil eine App-Liste sicherstellen.</summary>
        public static void EnsureList(AppConfig cfg)
        {
            if (cfg.ProfileList.Count == 0) cfg.ProfileList = DefaultEntries(cfg);
            // Entspricht: for k in PROFILE_KEYS: b["profiles"].setdefault(k, [])
            foreach (var key in Keys(cfg))
                if (!cfg.Profiles.ContainsKey(key))
                    cfg.Profiles[key] = new List<string>();
        }

        /// <summary>Neuer, eindeutiger Schlüssel für ein eigenes Profil.</summary>
        public static string NewKey() => "p_" + Guid.NewGuid().ToString("N")[..8];

        // ── Eigene Logos (Bilder) ────────────────────────────────────────────

        private static readonly ConcurrentDictionary<string, BitmapSource?> ImageCache = new();

        /// <summary>Base64-PNG → Bild (zwischengespeichert, eingefroren für alle Threads); ungültig = null.</summary>
        public static BitmapSource? DecodeImage(string? base64)
        {
            if (string.IsNullOrEmpty(base64)) return null;
            if (ImageCache.Count > 32) ImageCache.Clear(); // alte (ersetzte) Logos nicht ewig behalten
            return ImageCache.GetOrAdd(base64, b =>
            {
                try
                {
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.StreamSource = new MemoryStream(Convert.FromBase64String(b));
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.EndInit();
                    img.Freeze();
                    return img;
                }
                catch
                {
                    return null;
                }
            });
        }

        /// <summary>
        /// Bilddatei (PNG/JPG/ICO/BMP/GIF) als Logo übernehmen: größtes Einzelbild, auf size×size eingepasst
        /// (Seitenverhältnis bleibt, Rest durchsichtig), als Base64-PNG für die Config.
        /// </summary>
        public static string ImportImage(string path, int size = 64)
        {
            var decoder = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth * f.PixelHeight).First();
            double scale = Math.Min((double)size / frame.PixelWidth, (double)size / frame.PixelHeight);
            double w = frame.PixelWidth * scale, h = frame.PixelHeight * scale;

            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (var dc = visual.RenderOpen())
                dc.DrawImage(frame, new Rect((size - w) / 2, (size - h) / 2, w, h));
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return Convert.ToBase64String(ms.ToArray());
        }
    }
}
