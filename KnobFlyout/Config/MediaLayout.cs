using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KnobFlyout.Config
{
    /// <summary>
    /// Aufbau des Media-Flyouts für ein Design: was zu sehen ist, in welcher Reihenfolge, Cover und Knöpfe.
    /// Leere Werte (null, 0) heißen „wie das Design es vorgibt“; ein unveränderter Aufbau zeigt das Design also genau wie gestaltet.
    /// </summary>
    public sealed class MediaLayout
    {
        public const string App = "app", Title = "title", Artist = "artist", Times = "times", Progress = "progress",
            Sources = "sources", Prev = "prev", Skip = "skip", Play = "play", Next = "next", More = "more",
            SourcePause = "source_pause"; // Pause-Zeichen neben spielenden Quellen

        /// <summary>Schlüssel der eigenen Farben (Colors).</summary>
        public const string ColorBg = "bg", ColorFg = "fg", ColorSub = "sub", ColorAccent = "accent", ColorButton = "button", ColorTrack = "track",
            ColorEdge = "edge", ColorWave = "wave", ColorSeed = "seed", ColorDisplay = "display", ColorDisplayBg = "display_bg";

        /// <summary>Wert in Colors: Die Farbe kommt aus dem Titelbild.</summary>
        public const string CoverColor = "cover";

        /// <summary>Gewählte Vorlage (null = „standard“): darauf setzt ↺ zurück.</summary>
        [JsonPropertyName("template"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Template { get; set; }

        /// <summary>Reihenfolge der Bereiche von oben nach unten: "app", "info", "progress", "sources", "controls".</summary>
        [JsonPropertyName("order"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Order { get; set; }

        /// <summary>Ausgeblendete Elemente (die Konstanten oben).</summary>
        [JsonPropertyName("hidden"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Hidden { get; set; }

        /// <summary>Cover: "right" | "left" | "top" | "none".</summary>
        [JsonPropertyName("cover"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Cover { get; set; }
        [JsonPropertyName("cover_size"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int CoverSize { get; set; }
        [JsonPropertyName("title_size"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int TitleSize { get; set; }

        /// <summary>Knöpfe: "round" | "flat", Ausrichtung "center" | "left" | "right", Größe in %.</summary>
        [JsonPropertyName("buttons"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Buttons { get; set; }
        [JsonPropertyName("buttons_align"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ButtonsAlign { get; set; }
        [JsonPropertyName("button_size"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int ButtonSize { get; set; }

        /// <summary>„…“-Knopf: "controls" (bei den Knöpfen) | "top" (oben rechts).</summary>
        [JsonPropertyName("more_pos"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? MorePos { get; set; }

        /// <summary>Eigene Farben dieses Designs (Schlüssel oben → Hex oder CoverColor); was fehlt, bleibt wie im Design. Das Design „Eigenes“ hat seine festen Farben in media_custom_*.</summary>
        [JsonPropertyName("colors"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, string>? Colors { get; set; }

        public bool Shows(string element) => Hidden?.Contains(element) != true;

        public void Set(string element, bool show)
        {
            var hidden = Hidden ?? new List<string>();
            hidden.Remove(element);
            if (!show) hidden.Add(element);
            Hidden = hidden.Count > 0 ? hidden : null;
        }

        public bool FollowsCover(string key) => Colors != null && Colors.TryGetValue(key, out var value) && value == CoverColor;

        public void SetCover(string key)
        {
            var colors = Colors ?? new Dictionary<string, string>();
            colors[key] = CoverColor;
            Colors = colors;
        }

        /// <summary>Eigene Farbe als Hex, null = wie im Design oder aus dem Titelbild (auch bei einem ungültigen Wert).</summary>
        public string? ColorOf(string key) => Colors != null && Colors.TryGetValue(key, out var hex) ? ValidColor(hex) : null;

        /// <summary>Eigene Farbe setzen, null = wieder wie im Design. Ungültige Werte (halb getippt) ändern nichts.</summary>
        public void SetColor(string key, string? hex)
        {
            if (hex != null && ValidColor(hex) == null) return;
            var colors = Colors ?? new Dictionary<string, string>();
            if (hex == null) colors.Remove(key);
            else colors[key] = hex;
            Colors = colors.Count > 0 ? colors : null;
        }

        private static string? ValidColor(string hex)
        {
            try
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                return c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Aufbau unverändert: Das Design bleibt genau wie gestaltet (Farben und das Pause-Zeichen ändern den Aufbau nicht).</summary>
        [JsonIgnore]
        public bool IsDefault => Order == null && (Hidden == null || Hidden.All(h => h == SourcePause)) && Cover == null && CoverSize == 0 && TitleSize == 0
            && Buttons == null && ButtonsAlign == null && ButtonSize == 0 && MorePos == null;

        private static readonly string[] ContentElements = { Progress, Skip, More, Sources, SourcePause };

        /// <summary>Nur die Inhalt-Schalter der Einstellungsseite sind geändert, kein Aufbau und keine Farben.</summary>
        [JsonIgnore]
        public bool OnlyContent => Template == null && Colors == null && Order == null && Cover == null && CoverSize == 0 && TitleSize == 0
            && Buttons == null && ButtonsAlign == null && ButtonSize == 0 && MorePos == null && (Hidden == null || Hidden.All(ContentElements.Contains));

        /// <summary>Kopie, die nur die Inhalt-Schalter behält.</summary>
        public MediaLayout ContentOnly()
        {
            var kept = Hidden?.Where(ContentElements.Contains).ToList();
            return new MediaLayout { Hidden = kept is { Count: > 0 } ? kept : null };
        }

        public MediaLayout Clone() => JsonSerializer.Deserialize<MediaLayout>(JsonSerializer.Serialize(this)) ?? new MediaLayout();

        /// <summary>Vorlagen, die bei jedem Design gehen: Standard, Kompakt, Minimal, Großes Cover.</summary>
        public static readonly string[] TemplateKeys = { "standard", "compact", "minimal", "cover" };

        public static MediaLayout FromTemplate(string? key)
        {
            var layout = new MediaLayout { Template = key == "standard" ? null : key };
            switch (key)
            {
                case "compact": // kleines Cover links, flache Knöpfe ohne ±10 s
                    layout.Hidden = new List<string> { App, Times, Skip };
                    layout.Cover = "left";
                    layout.CoverSize = 48;
                    layout.TitleSize = 13;
                    layout.Buttons = "flat";
                    layout.ButtonSize = 90;
                    break;
                case "minimal": // nur Titel, Interpret und Zurück/Play/Weiter
                    layout.Hidden = new List<string> { App, Sources, Progress, Skip, More };
                    layout.Cover = "none";
                    layout.Buttons = "flat";
                    break;
                case "cover": // großes Cover oben, „…“ oben rechts
                    layout.Cover = "top";
                    layout.CoverSize = 140;
                    layout.TitleSize = 16;
                    layout.ButtonSize = 110;
                    layout.MorePos = "top";
                    break;
            }
            return layout;
        }
    }

    /// <summary>Unter einem Namen gespeichertes OSD-Design: Grund-Design, eigene Farben, Deckkraft und Ecken.</summary>
    public sealed class SavedOsdDesign
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("design")] public string Design { get; set; } = "";
        [JsonPropertyName("colors")] public Dictionary<string, string>? Colors { get; set; }
        [JsonPropertyName("opacity")] public double Opacity { get; set; }
        [JsonPropertyName("content_opacity")] public int ContentOpacity { get; set; }
        [JsonPropertyName("corner_radius")] public int CornerRadius { get; set; }

        /// <summary>Felder des Designs „Eigenes“, in der Reihenfolge von Fields.</summary>
        [JsonPropertyName("custom")] public List<string> Custom { get; set; } = new();

        private static readonly (System.Func<AppConfig, string> Get, System.Action<AppConfig, string> Set)[] Fields =
        {
            (c => c.ColorBg, (c, v) => c.ColorBg = v), (c => c.ColorVolBorder, (c, v) => c.ColorVolBorder = v), (c => c.ColorVolText, (c, v) => c.ColorVolText = v),
            (c => c.ColorVolPct, (c, v) => c.ColorVolPct = v), (c => c.ColorVolIcon, (c, v) => c.ColorVolIcon = v), (c => c.ColorBarBg, (c, v) => c.ColorBarBg = v),
            (c => c.ColorBarHi, (c, v) => c.ColorBarHi = v), (c => c.ColorBarMid, (c, v) => c.ColorBarMid = v), (c => c.ColorBarLo, (c, v) => c.ColorBarLo = v),
            (c => c.ColorProfBorder, (c, v) => c.ColorProfBorder = v), (c => c.ColorProfText, (c, v) => c.ColorProfText = v),
            (c => c.ColorSnapBorder, (c, v) => c.ColorSnapBorder = v), (c => c.ColorSnapText, (c, v) => c.ColorSnapText = v),
        };

        /// <summary>Farben des Designs „Eigenes“ auf Standard.</summary>
        public static void ResetCustom(AppConfig c)
        {
            var d = new AppConfig();
            foreach (var (get, set) in Fields) set(c, get(d));
        }

        public static SavedOsdDesign From(AppConfig c, string name) => new()
        {
            Name = name,
            Design = c.OsdDesign,
            Colors = c.OsdColorSet().Colors is { } colors ? new Dictionary<string, string>(colors) : null,
            Opacity = c.WinAlpha,
            ContentOpacity = c.OsdContentOpacity,
            CornerRadius = c.OsdCornerRadius,
            Custom = Fields.Select(f => f.Get(c)).ToList(),
        };

        /// <summary>Dieses Design auswählen; seine Farben liest OsdColorSet direkt von hier, die Voreinstellung bleibt unberührt.</summary>
        public void ApplyTo(AppConfig c)
        {
            if (!c.OsdSavedDesigns.Any(s => s.Name == Name)) c.OsdSavedDesigns.Add(this);
            c.OsdDesign = Design;
            c.WinAlpha = Opacity;
            c.OsdContentOpacity = ContentOpacity;
            c.OsdCornerRadius = CornerRadius;
            if (Custom.Count == Fields.Length)
                for (int i = 0; i < Fields.Length; i++) Fields[i].Set(c, Custom[i]);
            c.OsdSavedActive = Name;
        }
    }

    /// <summary>Unter einem Namen gespeichertes Flyout-Design: Grund-Design, Aufbau, Deckkraft, Ecken, Cover-Hintergrund und eigene Farben.</summary>
    public sealed class SavedMediaDesign
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("design")] public string Design { get; set; } = "";
        [JsonPropertyName("layout")] public MediaLayout Layout { get; set; } = new();
        [JsonPropertyName("opacity")] public int Opacity { get; set; }
        [JsonPropertyName("content_opacity")] public int ContentOpacity { get; set; }
        [JsonPropertyName("corner_radius")] public int CornerRadius { get; set; }
        [JsonPropertyName("art_background")] public bool ArtBackground { get; set; }
        [JsonPropertyName("art_blur")] public int ArtBlur { get; set; }
        [JsonPropertyName("art_dim")] public int ArtDim { get; set; }
        [JsonPropertyName("colors")] public List<string> Colors { get; set; } = new();

        public static SavedMediaDesign From(AppConfig c, string name) => From(c, name, c.MediaDesign, c.Layout());

        public static SavedMediaDesign From(AppConfig c, string name, string design, MediaLayout layout) => new()
        {
            Name = name,
            Design = design,
            Layout = layout.Clone(),
            Opacity = c.MediaOpacity,
            ContentOpacity = c.MediaContentOpacity,
            CornerRadius = c.MediaCornerRadius,
            ArtBackground = c.MediaArtBackground,
            ArtBlur = c.MediaArtBlur,
            ArtDim = c.MediaArtDim,
            Colors = new List<string> { c.MediaCustomBg, c.MediaCustomFg, c.MediaCustomSub, c.MediaCustomAccent, c.MediaCustomButtonBg, c.MediaCustomTrack },
        };

        /// <summary>Dieses Design auswählen; seinen Aufbau liest cfg.Layout() direkt von hier, die Voreinstellung bleibt unberührt.</summary>
        public void ApplyTo(AppConfig c)
        {
            if (!c.MediaSavedDesigns.Any(s => s.Name == Name)) c.MediaSavedDesigns.Add(this);
            c.MediaDesign = Design;
            c.MediaOpacity = Opacity;
            c.MediaContentOpacity = ContentOpacity;
            c.MediaCornerRadius = CornerRadius;
            c.MediaArtBackground = ArtBackground;
            c.MediaArtBlur = ArtBlur;
            c.MediaArtDim = ArtDim;
            if (Colors.Count == 6 && Design == "custom")
            {
                c.MediaCustomBg = Colors[0];
                c.MediaCustomFg = Colors[1];
                c.MediaCustomSub = Colors[2];
                c.MediaCustomAccent = Colors[3];
                c.MediaCustomButtonBg = Colors[4];
                c.MediaCustomTrack = Colors[5];
            }
            c.MediaSavedActive = Name;
        }
    }
}
