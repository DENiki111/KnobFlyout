using System.Collections.Generic;
using System.Windows.Media;
using KnobFlyout.Config;

namespace KnobFlyout.Osd
{
    /// <summary>
    /// Eigene Farben je OSD-Design (cfg.osd_colors) für OSD-Karten und Neue-App-Popup; was fehlt, bleibt wie im Design.
    /// Das OSD-Design „Eigenes“ („dark“) hat dafür seine festen Felder color_*.
    /// </summary>
    internal static class OsdDesignColors
    {
        private static bool IsLightHex(string hex)
        {
            var c = UiColor.Brush(hex).Color;
            return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0 > 0.55;
        }

        /// <summary>Hell oder dunkel: Eine eigene Hintergrundfarbe entscheidet, sonst das Farbschema.</summary>
        public static bool Light(AppConfig cfg) =>
            cfg.OsdColorSet().ColorOf(MediaLayout.ColorBg) is { } bg ? IsLightHex(bg) : ThemeHelper.IsLight(cfg);

        private static DesignPalette BasePalette(AppConfig cfg, DesignSpec spec, bool light) =>
            ReferenceEquals(spec, Designs.Material) && cfg.OsdColorSet().ColorOf(MediaLayout.ColorSeed) is { } seed
                ? MaterialColors.Scheme(UiColor.Brush(seed).Color, light) // feste Grundfarbe statt der Windows-Akzentfarbe
                : spec.Palette(light);

        public static DesignPalette Palette(AppConfig cfg, DesignSpec spec, bool light) => BasePalette(cfg, spec, light).WithColors(cfg.OsdColorSet());

        /// <summary>Gerätefarben des Hardware-Reglers mit den eigenen Display- und Tastenfarben.</summary>
        public static HwColors Hw(AppConfig cfg, bool light)
        {
            var own = cfg.OsdColorSet();
            var hw = HardwareParts.Colors(light);
            if (own.ColorOf(MediaLayout.ColorDisplay) is { } text)
            {
                var c = UiColor.Brush(text).Color;
                hw = hw with { LcdText = text, LcdGhost = $"#17{c.R:X2}{c.G:X2}{c.B:X2}" };
            }
            if (own.ColorOf(MediaLayout.ColorDisplayBg) is { } lcd) hw = hw with { Lcd = lcd };
            if (own.ColorOf(MediaLayout.ColorButton) is { } key)
            {
                var c = UiColor.Brush(key).Color;
                hw = hw with { KeyTop = key, KeyBottom = $"#{(byte)(c.R * 0.88):X2}{(byte)(c.G * 0.88):X2}{(byte)(c.B * 0.88):X2}" };
            }
            return hw;
        }

        /// <summary>Farben, die sich am gewählten OSD-Design ändern lassen, mit ihrem Wert im Design. Bei „Eigenes“ leer (dort die Felder color_*).</summary>
        public static List<(string Key, string Label, string Default)> For(AppConfig cfg)
        {
            string design = cfg.OsdDesign == "windows" ? "modern" : cfg.OsdDesign;
            var list = new List<(string, string, string)>();
            if (design == "dark") return list;
            bool light = Light(cfg), win11 = design == "win11", hardware = design == "hardware";
            var spec = Designs.Get(design);
            var pal = spec != null ? BasePalette(cfg, spec, light) : null;
            var hw = HardwareParts.Colors(light);
            string fg = pal?.Fg ?? (light ? (win11 ? "#1B1B1B" : "#000000") : "#FFFFFF");
            if (design == "material") list.Add((MediaLayout.ColorSeed, Loc.T("Grundfarbe", "Base color"), Hex(ThemeHelper.AccentColor())));
            list.Add((MediaLayout.ColorBg, hardware ? Loc.T("Gehäuse", "Casing") : Loc.T("Hintergrund", "Background"),
                pal?.Bg ?? (win11 ? (light ? "#E9E9EB" : "#2C2C2C") : (light ? "#EFEFEF" : "#222222"))));
            list.Add((MediaLayout.ColorFg, Loc.T("Text und Symbole", "Text and icons"), fg));
            list.Add((MediaLayout.ColorSub, Loc.T("Nebentext", "Secondary text"), pal?.Sub ?? (light ? "#5C5C5C" : win11 ? "#C5C5C5" : "#A0A0A0")));
            list.Add((MediaLayout.ColorAccent, hardware ? Loc.T("Zeiger und LEDs", "Pointer and LEDs") : Loc.T("Akzent (Leiste)", "Accent (bar)"), pal?.Fill ?? (win11 ? fg : "#3B82F6")));
            if (design != "hud") // das HUD hat keine Knopf-Flächen
                list.Add((MediaLayout.ColorButton, hardware ? Loc.T("Tasten", "Keys") : Loc.T("Knopf-Fläche", "Button fill"),
                    hardware ? hw.KeyTop : pal?.Tile ?? pal?.Button ?? (win11 ? (light ? "#D6D6DA" : "#3A3A3A") : (light ? "#DADADA" : "#3A3A3A"))));
            if (!hardware) // der Hardware-Regler zeigt die Lautstärke am Knopf, nicht auf einer Leiste
                list.Add((MediaLayout.ColorTrack, Loc.T("Leiste (Hintergrund)", "Bar (background)"), pal?.Track ?? (win11 ? (light ? "#B4B4B8" : "#5C5C5C") : light ? "#D0D0D0" : "#5A5A63")));
            if (hardware)
            {
                list.Add((MediaLayout.ColorDisplay, Loc.T("Display-Schrift", "Display text"), hw.LcdText));
                list.Add((MediaLayout.ColorDisplayBg, Loc.T("Display-Hintergrund", "Display background"), hw.Lcd));
            }
            if (pal != null || win11) list.Add((MediaLayout.ColorEdge, Loc.T("Rand", "Border"), pal?.Edge ?? (light ? "#D0D0D4" : "#404040")));
            return list;
        }

        private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}
