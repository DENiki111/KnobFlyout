using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KnobEarTrumpet.Osd
{
    /// <summary>Farben eines Designs für hell oder dunkel (Hex, mit Alpha als #AARRGGBB).</summary>
    internal sealed record DesignPalette(
        string Bg,      // Kartenfläche (Deckkraft-Regler wirkt nur hierauf); mit BgTop unteres Ende des Verlaufs
        string Fg,      // Text, Symbole
        string Sub,     // Nebentext, Zeiten
        string Track,   // Spur von Lautstärke- und Fortschrittsleiste
        string Fill,    // Füllung der Leisten, aktive Quelle
        string Button,  // Knopf-/Hover-Fläche
        string Edge,    // 1-px-Rand
        string Mute,    // Stumm-Symbol
        string On,      // „AN“-Schild bzw. Schalter (Feststelltasten)
        string OnText,
        string Off,     // „AUS“-Schild bzw. Schalter
        string OffText,
        string ArtPlaceholder, // Cover-Platzhalter ohne Cover
        // ── optional, nur von einzelnen Designs genutzt ──
        string? BgTop = null,     // oberes Ende eines senkrechten Verlaufs der Kartenfläche
        string? Hi = null,        // Lichtkante innen (oben) …
        string? Hi2 = null,       // … verläuft nach unten zu dieser Farbe
        string? OnFill = null,    // Symbol auf der Reglerfüllung
        string? MutedFill = null, // Reglerfüllung bei „Stumm“
        string? Tile = null,      // neutrale Kachel (Schloss, Stecknadel, Ignorieren)
        string? Scrub = null,     // Füllung der Fortschrittsleiste (wenn anders als Fill)
        string? Seg = null,       // Segment-Steuerung: Hintergrund …
        string? SegOn = null,     // … gewähltes Segment …
        string? SegOnText = null, // … und dessen Text
        string? OnAccent = null,  // Inhalt auf Fill (z. B. Play-Symbol auf Primärfarbe)
        string? Container = null, // Symbol-Container (Material: primaryContainer) …
        string? OnContainer = null, // … und dessen Symbol
        string? Outline = null,   // Umriss (Schalter aus)
        string? OutlineVariant = null, // schwacher Umriss (Chips, Seitenpunkte)
        string? MuteContainer = null,  // Symbol-Container bei „Stumm“ …
        string? OnMuteContainer = null, // … und dessen Symbol
        string? OnTrack = null,   // Inhalt auf Track (gewählter Chip)
        string? Scan = null)      // Scanlines (Gaming-HUD)
    {
        /// <summary>Kartenfläche mit Deckkraft (nur der Hintergrund) – einfarbig oder als senkrechter Verlauf.</summary>
        public Brush Background(double opacity)
        {
            if (BgTop == null) return UiColor.Brush(Bg, opacity);
            var b = new LinearGradientBrush(UiColor.Brush(BgTop, opacity).Color, UiColor.Brush(Bg, opacity).Color, 90);
            b.Freeze();
            return b;
        }

        /// <summary>Lichtkante innen (1 px, oben hell, unten schwächer) als Überlagerung, ohne Maus-Treffer; null ohne Hi.</summary>
        public Border? HighlightOverlay(double radius)
        {
            if (Hi == null) return null;
            var brush = new LinearGradientBrush(UiColor.Brush(Hi).Color, UiColor.Brush(Hi2 ?? Hi).Color, 90);
            brush.Freeze();
            return new Border
            {
                CornerRadius = new CornerRadius(System.Math.Max(0, radius)), BorderBrush = brush,
                BorderThickness = new Thickness(1), IsHitTestVisible = false,
            };
        }

        /// <summary>Inhalt plus Lichtkante darüber (ohne Hi: der Inhalt selbst).</summary>
        public UIElement WithHighlight(UIElement content, double radius)
        {
            var hi = HighlightOverlay(radius);
            if (hi == null) return content;
            var g = new Grid();
            g.Children.Add(content);
            g.Children.Add(hi);
            return g;
        }
    }

    /// <summary>
    /// Ein Design aus dem Design-System: Farben (hell/dunkel), Formen, Größen, Schrift und Animation.
    /// OsdCard, NewAppOsd und MediaOsd lesen daraus; ein neues Design = neuer Eintrag in Designs.All plus
    /// seine Aufbau-Methoden (je Fenster in einer eigenen Teil-Datei, z. B. OsdCard.Mac.cs). Die älteren Designs
    /// ("dark"/"modern"/"win11" beim OSD, "modernflyouts"/"win11"/"custom" beim Media-Flyout) bleiben
    /// eigenständig und unverändert.
    /// </summary>
    internal sealed record DesignSpec
    {
        public required string Key { get; init; }
        public required string NameDe { get; init; }
        public required string NameEn { get; init; }
        public required DesignPalette Dark { get; init; }
        public required DesignPalette Light { get; init; }

        // OSD-Karte (Lautstärke, Profil, Feststelltasten, Position)
        public double OsdWidth { get; init; }
        public double OsdHeight { get; init; }
        public double OsdRadius { get; init; }

        // Neue-App-Popup
        public double PopupWidth { get; init; }
        public double PopupHeight { get; init; }
        public double PopupRadius { get; init; }

        // Media-Flyout (Breite = MediaOsd.FlyoutWidth)
        public double MediaRadius { get; init; }

        /// <summary>Abgeschrägte Ecken oben links/unten rechts statt Rundung (Gaming-HUD): OSD/Popup bzw. Media-Flyout, 0 = aus.</summary>
        public double Chamfer { get; init; }
        public double MediaChamfer { get; init; }

        public required FontFamily Font { get; init; }

        /// <summary>„Zusammen“: OSD und Flyout mittig untereinander statt linksbündig.</summary>
        public bool CenterCombined { get; init; }

        // Animation: Erscheinen (Aufziehen aus AppearFromScale × AppearFromScaleY) und Ausblenden, in ms
        public int AppearMs { get; init; } = 300;
        public int HideMs { get; init; } = 180;
        public double AppearFromScale { get; init; } = 1.0;
        public double AppearFromScaleY { get; init; } = 0.8;
        public int AppearFadeMs { get; init; } = 120;    // Kartenfläche einblenden
        public int ContentDelayMs { get; init; } = 90;   // Inhalt erst danach einblenden (0 = sofort mit der Fläche)
        public double MediaAppearFromScale { get; init; } = 0.9;
        public double? MediaAppearFromScaleY { get; init; }  // null = wie MediaAppearFromScale

        /// <summary>OSD-Karten blenden nur per Verblassen aus (HideMs), ohne Schrumpfen. Das Media-Flyout verblasst bei allen Design-System-Designs.</summary>
        public bool SlowFade { get; init; }

        /// <summary>Farben zur Laufzeit errechnet (Material You: aus der Windows-Akzentfarbe); null = feste Dark/Light.</summary>
        public System.Func<bool, DesignPalette>? Dynamic { get; init; }

        public DesignPalette Palette(bool light) => Dynamic?.Invoke(light) ?? (light ? Light : Dark);
        public string Name => Loc.T(NameDe, NameEn);
    }

    internal static class Designs
    {
        /// <summary>
        /// Design 1 · Dynamic Island: schwarze (hell: fast weiße) Kapsel für kurze Meldungen, aufgeklappte
        /// Insel mit großer Rundung für Neue-App-Popup und Media; weißer Text, gefärbt wird nur das Wichtige
        /// (Profilfarbe, Stumm rot, „AN“ grün, Wellenform in Cover-Farbe); zieht sich beim Erscheinen auf.
        /// </summary>
        public static readonly DesignSpec Island = new()
        {
            Key = "island", NameDe = "Dynamic Island", NameEn = "Dynamic Island",
            Dark = new(Bg: "#000000", Fg: "#FFFFFF", Sub: "#8E8E93", Track: "#38FFFFFF", Fill: "#FFFFFF",
                Button: "#24FFFFFF", Edge: "#14FFFFFF", Mute: "#FF453A", On: "#30D158", OnText: "#04210D",
                Off: "#29FFFFFF", OffText: "#8E8E93", ArtPlaceholder: "#1C1C1E"),
            Light = new(Bg: "#FBFBFD", Fg: "#1C1C1E", Sub: "#6E6E73", Track: "#1F000000", Fill: "#1C1C1E",
                Button: "#12000000", Edge: "#26000000", Mute: "#FF3B30", On: "#34C759", OnText: "#FFFFFF",
                Off: "#14000000", OffText: "#6E6E73", ArtPlaceholder: "#E9E9EE"),
            OsdWidth = 354, OsdHeight = 60, OsdRadius = 30, // so groß wie die übrigen Designs (Jasper: vorher 300×48 zu eng)
            PopupWidth = 340, PopupHeight = 114, PopupRadius = 30,
            MediaRadius = 32,
            Font = new FontFamily("Segoe UI Variable Display, Segoe UI"),
            CenterCombined = true,
            AppearMs = 300, HideMs = 180, AppearFromScale = 0.45,
        };

        /// <summary>
        /// Design 2 · macOS: Control-Center-Look – getöntes Glas (simuliert: WPF kann den Hintergrund nicht
        /// weichzeichnen) mit Lichtkante innen und dunkler Kante außen, dicker Regler mit Symbol im Regler,
        /// App-Kacheln, Schalter, Segment-Steuerung für die Quellen. Erscheint kurz vergrößernd, verblasst langsam.
        /// OSD so breit wie das Flyout: „Zusammen“ bündig wie Control-Center-Module.
        /// </summary>
        public static readonly DesignSpec Mac = new()
        {
            Key = "mac", NameDe = "macOS", NameEn = "macOS",
            Dark = new(Bg: "#EB222224", Fg: "#F5F5F7", Sub: "#98989D", Track: "#2EFFFFFF", Fill: "#F5F5F7",
                Button: "#1FFFFFFF", Edge: "#B8000000", Mute: "#FF453A", On: "#30D158", OnText: "#FFFFFF",
                Off: "#38FFFFFF", OffText: "#98989D", ArtPlaceholder: "#3A3A3C",
                BgTop: "#EB323235", Hi: "#29FFFFFF", Hi2: "#0DFFFFFF", OnFill: "#58585D", MutedFill: "#7C7C80",
                Tile: "#24FFFFFF", Scrub: "#E6F5F5F7", Seg: "#3D767680", SegOn: "#636366", SegOnText: "#FFFFFF"),
            Light = new(Bg: "#EDECECF0", Fg: "#1D1D1F", Sub: "#6E6E73", Track: "#1F000000", Fill: "#FFFFFF",
                Button: "#0F000000", Edge: "#29000000", Mute: "#FF3B30", On: "#34C759", OnText: "#FFFFFF",
                Off: "#1F000000", OffText: "#6E6E73", ArtPlaceholder: "#E5E5EA",
                BgTop: "#EDF8F8FA", Hi: "#E6FFFFFF", Hi2: "#59FFFFFF", OnFill: "#636368", MutedFill: "#C7C7CC",
                Tile: "#0F000000", Scrub: "#C71D1D1F", Seg: "#1F767680", SegOn: "#FFFFFF", SegOnText: "#1D1D1F"),
            OsdWidth = 354, OsdHeight = 64, OsdRadius = 16,
            PopupWidth = 354, PopupHeight = 110, PopupRadius = 18,
            MediaRadius = 18,
            Font = new FontFamily("SF Pro Text, Segoe UI Variable Text, Segoe UI"),
            CenterCombined = false,
            AppearMs = 160, HideMs = 400, AppearFromScale = 0.96, AppearFromScaleY = 0.96,
            AppearFadeMs = 160, ContentDelayMs = 0, MediaAppearFromScale = 0.96, SlowFade = true,
        };

        /// <summary>
        /// Design 3 · Material You (Material 3 Expressive): Farbrollen aus der Windows-Akzentfarbe (Media-Flyout: aus dem
        /// Cover), große Rundungen (28), Symbol-Container, Regler mit Griff/Lücke/Stopp-Punkt, Cookie-Form fürs Profil,
        /// M3-Schalter, verbundene Knopfgruppen, wellige Fortschrittsleiste, Filter-Chips. Zoomt beim Erscheinen auf.
        /// </summary>
        public static readonly DesignSpec Material = new()
        {
            Key = "material", NameDe = "Material You", NameEn = "Material You",
            Dark = MaterialColors.Scheme(MaterialColors.DefaultSeed, false),
            Light = MaterialColors.Scheme(MaterialColors.DefaultSeed, true),
            Dynamic = light => MaterialColors.Scheme(Config.ThemeHelper.AccentColor(), light),
            OsdWidth = 354, OsdHeight = 76, OsdRadius = 28,
            PopupWidth = 354, PopupHeight = 132, PopupRadius = 28,
            MediaRadius = 28,
            Font = new FontFamily("Google Sans Flex, Google Sans, Roboto Flex, Roboto, Segoe UI Variable Display, Segoe UI"),
            CenterCombined = false,
            AppearMs = 350, HideMs = 200, AppearFromScale = 0.8, AppearFromScaleY = 0.8,
            AppearFadeMs = 150, ContentDelayMs = 0, MediaAppearFromScale = 0.85,
        };

        /// <summary>
        /// Design 4 · Gaming-HUD: taktisches HUD – abgeschrägte Ecken, Eck-Klammern, feine Scanlines, schmale DIN-Schrift
        /// (Bahnschrift SemiCondensed, in Windows enthalten) in Großbuchstaben, schräge LED-Segmente mit leichtem Leuchten,
        /// Neon-Cyan auf Blau-Schwarz; Rot nur für „Stumm“, Grün für „An“. Klappt beim Erscheinen senkrecht auf.
        /// </summary>
        public static readonly DesignSpec Hud = new()
        {
            Key = "hud", NameDe = "Gaming-HUD", NameEn = "Gaming HUD",
            Dark = new(Bg: "#EB0A0E14", Fg: "#E6F0F7", Sub: "#7D8C9B", Track: "#1C2733", Fill: "#19E3FF",
                Button: "#1F19E3FF", Edge: "#5919E3FF", Mute: "#FF3B5C", On: "#3DFF8B", OnText: "#3DFF8B",
                Off: "#2A3644", OffText: "#7D8C9B", ArtPlaceholder: "#121A23",
                OnAccent: "#03141A", MutedFill: "#4A1622", Scan: "#09FFFFFF"),
            Light = new(Bg: "#F0EEF2F6", Fg: "#0B1520", Sub: "#566576", Track: "#C8D2DC", Fill: "#0089A3",
                Button: "#1A0089A3", Edge: "#730089A3", Mute: "#D7263D", On: "#12A150", OnText: "#12A150",
                Off: "#C8D2DC", OffText: "#566576", ArtPlaceholder: "#DCE3EA",
                OnAccent: "#FFFFFF", MutedFill: "#F2C4CB", Scan: "#08000000"),
            OsdWidth = 354, OsdHeight = 64, OsdRadius = 0,
            PopupWidth = 354, PopupHeight = 124, PopupRadius = 0,
            MediaRadius = 0, Chamfer = 10, MediaChamfer = 12,
            Font = new FontFamily("Bahnschrift SemiCondensed, Bahnschrift, Segoe UI"),
            CenterCombined = false,
            AppearMs = 160, HideMs = 140, AppearFromScale = 1.0, AppearFromScaleY = 0.6,
            AppearFadeMs = 100, ContentDelayMs = 0, MediaAppearFromScale = 1.0, MediaAppearFromScaleY = 0.7,
        };

        /// <summary>
        /// Design 5 · Hardware-Regler: wie ein echtes Gerät (Richtung Teenage Engineering / Braun) – mattes Gehäuse mit Lichtkante
        /// und Schrauben, Drehknopf mit Rändel und LED-Kranz, Sieben-Segment-Anzeige, bernsteinfarbenes Display (auch hell dunkel),
        /// Tasten mit Kante und LED, Eingangswahl mit LED-Punkten; Orange als einziger Akzent. Weitere Gerätefarben: HardwareParts.
        /// </summary>
        public static readonly DesignSpec Hardware = new()
        {
            Key = "hardware", NameDe = "Hardware-Regler", NameEn = "Hardware knob",
            Dark = new(Bg: "#1B1C1F", Fg: "#E2E2DE", Sub: "#8C8D91", Track: "#36373C", Fill: "#FF6A1F",
                Button: "#14FFFFFF", Edge: "#B3000000", Mute: "#FF4A3D", On: "#43E26F", OnText: "#B8FFCB",
                Off: "#36373C", OffText: "#8C8D91", ArtPlaceholder: "#1A1B1D",
                BgTop: "#27282C", Hi: "#14FFFFFF", Hi2: "#00FFFFFF"),
            Light = new(Bg: "#DBDBD6", Fg: "#1B1B1B", Sub: "#6E6E6A", Track: "#C4C4BF", Fill: "#FF5A1F",
                Button: "#0F000000", Edge: "#2E000000", Mute: "#E5322D", On: "#22B84F", OnText: "#B8FFCB",
                Off: "#C4C4BF", OffText: "#6E6E6A", ArtPlaceholder: "#1A1B1D",
                BgTop: "#ECECE8", Hi: "#D9FFFFFF", Hi2: "#00FFFFFF"),
            OsdWidth = 354, OsdHeight = 72, OsdRadius = 12,
            PopupWidth = 354, PopupHeight = 128, PopupRadius = 12,
            MediaRadius = 14,
            Font = new FontFamily("Bahnschrift, Segoe UI"),
            CenterCombined = false,
            AppearMs = 150, HideMs = 200, AppearFromScale = 0.97, AppearFromScaleY = 0.97,
            AppearFadeMs = 150, ContentDelayMs = 0, MediaAppearFromScale = 0.97,
        };

        /// <summary>Alle Designs des Design-Systems (Reihenfolge = Reihenfolge in den Auswahllisten).</summary>
        public static readonly DesignSpec[] All = { Island, Mac, Material, Hud, Hardware };

        /// <summary>Design zum Schlüssel aus osd_design/media_design, null = eines der älteren Designs.</summary>
        public static DesignSpec? Get(string? key) => All.FirstOrDefault(d => d.Key == key);
    }
}
