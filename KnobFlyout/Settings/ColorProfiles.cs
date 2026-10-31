using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using KnobFlyout.Config;
using KnobFlyout.Osd;

namespace KnobFlyout.Settings
{
    /// <summary>
    /// Farbprofile (cfg.color_profiles) gemeinsam für OSD und Media-Flyout: Die Farben heißen überall media_color_*,
    /// ein Profil passt also in jedes Design und auf beide. Was ein Design nicht hat, wird übersprungen; „aus dem Titelbild“ gibt es nur beim Flyout.
    /// </summary>
    internal static class ColorProfiles
    {
        public const string Prefix = "media_color_";

        /// <summary>OSD-Design „Eigenes“: feste Felder; mit Shared die gemeinsame Farbe, zu der das Feld gehört.</summary>
        private static readonly (string Json, string? Shared, Func<AppConfig, string> Get, Action<AppConfig, string> Set)[] DarkFields =
        {
            ("color_bg", MediaLayout.ColorBg, c => c.ColorBg, (c, v) => c.ColorBg = v),
            ("color_vol_border", MediaLayout.ColorEdge, c => c.ColorVolBorder, (c, v) => c.ColorVolBorder = v),
            ("color_vol_text", MediaLayout.ColorFg, c => c.ColorVolText, (c, v) => c.ColorVolText = v),
            ("color_vol_pct", MediaLayout.ColorSub, c => c.ColorVolPct, (c, v) => c.ColorVolPct = v),
            ("color_vol_icon", null, c => c.ColorVolIcon, (c, v) => c.ColorVolIcon = v),
            ("color_bar_bg", MediaLayout.ColorTrack, c => c.ColorBarBg, (c, v) => c.ColorBarBg = v),
            ("color_bar_hi", MediaLayout.ColorAccent, c => c.ColorBarHi, (c, v) => c.ColorBarHi = v),
            ("color_bar_mid", null, c => c.ColorBarMid, (c, v) => c.ColorBarMid = v),
            ("color_bar_lo", null, c => c.ColorBarLo, (c, v) => c.ColorBarLo = v),
            ("color_prof_border", null, c => c.ColorProfBorder, (c, v) => c.ColorProfBorder = v),
            ("color_prof_text", null, c => c.ColorProfText, (c, v) => c.ColorProfText = v),
        };

        /// <summary>Ältere Profile: Flyout-Design „Eigenes“ (media_custom_*) und OSD-Design „Eigenes“ (color_*) auf die gemeinsamen Namen abbilden.</summary>
        public static string Normalize(string key)
        {
            if (key == "media_custom_button_bg") return Prefix + MediaLayout.ColorButton;
            if (key.StartsWith("media_custom_")) return Prefix + key["media_custom_".Length..];
            foreach (var field in DarkFields)
                if (field.Json == key && field.Shared != null) return Prefix + field.Shared;
            return key;
        }

        /// <summary>Enthält das Profil Farben für OSD oder Flyout (und nicht nur z. B. Profilfarben)?</summary>
        public static bool Usable(IReadOnlyDictionary<string, string> preset) => preset.Keys.Any(k => Normalize(k).StartsWith(Prefix));

        private static bool IsColor(string text)
        {
            try
            {
                ColorConverter.ConvertFromString(text);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static string CustomColor(AppConfig c, string key) => key switch
        {
            MediaLayout.ColorBg => c.MediaCustomBg,
            MediaLayout.ColorFg => c.MediaCustomFg,
            MediaLayout.ColorSub => c.MediaCustomSub,
            MediaLayout.ColorAccent => c.MediaCustomAccent,
            MediaLayout.ColorButton => c.MediaCustomButtonBg,
            _ => c.MediaCustomTrack,
        };

        internal static void SetCustomColor(AppConfig c, string key, string hex)
        {
            switch (key)
            {
                case MediaLayout.ColorBg: c.MediaCustomBg = hex; break;
                case MediaLayout.ColorFg: c.MediaCustomFg = hex; break;
                case MediaLayout.ColorSub: c.MediaCustomSub = hex; break;
                case MediaLayout.ColorAccent: c.MediaCustomAccent = hex; break;
                case MediaLayout.ColorButton: c.MediaCustomButtonBg = hex; break;
                default: c.MediaCustomTrack = hex; break;
            }
        }

        /// <summary>Die aktuellen Farben des gewählten Designs, so wie ein Profil sie speichert.</summary>
        public static Dictionary<string, string> Current(AppConfig c, bool osd)
        {
            var result = new Dictionary<string, string>();
            if (osd)
            {
                if (c.OsdDesign == "dark")
                {
                    foreach (var field in DarkFields)
                    {
                        result[field.Json] = field.Get(c);
                        if (field.Shared != null) result[Prefix + field.Shared] = field.Get(c);
                    }
                    return result;
                }
                var own = c.OsdColorSet();
                foreach (var (key, _, def) in OsdDesignColors.For(c)) result[Prefix + key] = own.ColorOf(key) ?? def;
                return result;
            }

            var layout = c.Layout();
            if (MediaDesignColors.CoverColor(c) is { } cover) result[Prefix + cover.Key] = layout.ColorOf(cover.Key) ?? MediaLayout.CoverColor;
            foreach (var (key, _, def) in MediaDesignColors.For(c))
                result[Prefix + key] = layout.FollowsCover(key) ? MediaLayout.CoverColor : c.MediaDesign == "custom" ? CustomColor(c, key) : layout.ColorOf(key) ?? def;
            return result;
        }

        /// <summary>Profil auf das gewählte Design anwenden; Farben, die es dort nicht gibt, bleiben außen vor.</summary>
        public static void Apply(AppConfig c, IReadOnlyDictionary<string, string> preset, bool osd)
        {
            if (osd) ApplyOsd(c, preset);
            else ApplyFlyout(c, preset);
        }

        private static void ApplyFlyout(AppConfig c, IReadOnlyDictionary<string, string> preset)
        {
            var layout = c.Layout();
            var known = MediaDesignColors.For(c).ToDictionary(row => row.Key, row => row.Default);
            string? coverKey = MediaDesignColors.CoverColor(c)?.Key;
            foreach (var (raw, value) in preset)
            {
                string name = Normalize(raw);
                if (!name.StartsWith(Prefix)) continue;
                string key = name[Prefix.Length..];
                bool cover = value == MediaLayout.CoverColor;
                if (!cover && !IsColor(value)) continue;
                if (key == coverKey) layout.SetColor(key, cover ? null : value);
                else if (!known.TryGetValue(key, out var def)) continue;
                else if (cover) layout.SetCover(key);
                else if (c.MediaDesign == "custom")
                {
                    layout.SetColor(key, null);
                    SetCustomColor(c, key, value);
                }
                else layout.SetColor(key, string.Equals(value, def, StringComparison.OrdinalIgnoreCase) ? null : value);
            }
        }

        private static void ApplyOsd(AppConfig c, IReadOnlyDictionary<string, string> preset)
        {
            if (c.OsdDesign == "dark")
            {
                foreach (var field in DarkFields)
                {
                    // Eigener Wert des Felds vor der gemeinsamen Farbe: So kommt ein hier gespeichertes Profil genau wieder.
                    string? value = preset.TryGetValue(field.Json, out var own) ? own
                        : field.Shared != null && preset.TryGetValue(Prefix + field.Shared, out var shared) ? shared
                        : field.Shared == MediaLayout.ColorBg && preset.TryGetValue("media_custom_bg", out var old) ? old : null;
                    if (value != null && IsColor(value)) field.Set(c, value);
                }
                return;
            }
            var known = OsdDesignColors.For(c).ToDictionary(row => row.Key, row => row.Default);
            foreach (var (raw, value) in preset)
            {
                string name = Normalize(raw);
                if (!name.StartsWith(Prefix) || !IsColor(value)) continue; // „aus dem Titelbild“ gibt es beim OSD nicht
                string key = name[Prefix.Length..];
                if (known.TryGetValue(key, out var def))
                    c.SetOsdColor(key, string.Equals(value, def, StringComparison.OrdinalIgnoreCase) ? null : value);
            }
        }

        /// <summary>Hat das gewählte Design gerade die Farben dieses Profils?</summary>
        public static bool Matches(AppConfig c, IReadOnlyDictionary<string, string> preset, bool osd)
        {
            var current = Current(c, osd);
            bool any = false;
            foreach (var (raw, value) in preset)
            {
                string name = current.ContainsKey(raw) ? raw : Normalize(raw);
                if (!current.TryGetValue(name, out var now) || (osd && value == MediaLayout.CoverColor)) continue;
                any = true;
                if (!string.Equals(now, value, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return any;
        }

        /// <summary>Alle Farben des gewählten Designs zurück auf das Design (bei „Eigenes“ auf die Standardfarben).</summary>
        public static void Clear(AppConfig c, bool osd)
        {
            var defaults = new AppConfig();
            if (osd)
            {
                if (c.OsdDesign == "dark")
                    foreach (var field in DarkFields) field.Set(c, field.Get(defaults));
                else c.ClearOsdColors();
                return;
            }
            c.Layout().Colors = null;
            if (c.MediaDesign != "custom") return;
            foreach (var key in new[] { MediaLayout.ColorBg, MediaLayout.ColorFg, MediaLayout.ColorSub, MediaLayout.ColorAccent, MediaLayout.ColorButton, MediaLayout.ColorTrack })
                SetCustomColor(c, key, CustomColor(defaults, key));
        }
    }
}
