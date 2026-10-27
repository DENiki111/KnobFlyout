using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KnobFlyout.Config
{
    /// <summary>
    /// Eine gespeicherte Snap-Position. X/Y = Lautstärke-Anker. MediaX/MediaY (optional) = zugehörige
    /// Media-Flyout-Position im "separate"-Layout, damit ein Klick auf "Position übernehmen" bzw.
    /// ein Hotkey-Zyklus beide Flyouts zusammen an ihre jeweils gespeicherte Stelle bewegt.
    /// </summary>
    public class SnapPosition
    {
        [JsonPropertyName("x")] public int X { get; set; }
        [JsonPropertyName("y")] public int Y { get; set; }
        [JsonPropertyName("media_x")] public int? MediaX { get; set; }
        [JsonPropertyName("media_y")] public int? MediaY { get; set; }
    }

    /// <summary>Gespeicherter Arbeitsbereich eines Bildschirms (WPF-Einheiten), um Auflösungswechsel zu erkennen.</summary>
    public class ScreenRef
    {
        [JsonPropertyName("device")] public string Device { get; set; } = "";
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
        [JsonPropertyName("w")] public double W { get; set; }
        [JsonPropertyName("h")] public double H { get; set; }
        [JsonPropertyName("primary")] public bool Primary { get; set; }
    }

    /// <summary>
    /// 1:1-Abbild von DEFAULT_CONFIG aus knob_service.py. JSON-Keys bleiben identisch
    /// (snake_case), damit eine vorhandene config.json kompatibel bleibt.
    /// </summary>
    public class AppConfig
    {
        [JsonPropertyName("apps")] public List<string> Apps { get; set; } = new();
        [JsonPropertyName("exclude")] public List<string> Exclude { get; set; } = new();
        [JsonPropertyName("mode")] public string Mode { get; set; } = "active";
        [JsonPropertyName("step")] public int Step { get; set; } = 3;
        [JsonPropertyName("step_spotify")] public int StepSpotify { get; set; } = 4;
        [JsonPropertyName("osd")] public bool Osd { get; set; } = true;
        [JsonPropertyName("osd_duration")] public int OsdDuration { get; set; } = 2500;
        [JsonPropertyName("osd_x")] public int? OsdX { get; set; }
        [JsonPropertyName("osd_y")] public int? OsdY { get; set; }

        [JsonPropertyName("spotify_use_api")] public bool SpotifyUseApi { get; set; } = false;
        [JsonPropertyName("spotify_client_id")] public string SpotifyClientId { get; set; } = "";
        [JsonPropertyName("spotify_client_secret")] public string SpotifyClientSecret { get; set; } = "";
        [JsonPropertyName("spotify_redirect_uri")] public string SpotifyRedirectUri { get; set; } = "http://localhost:5000/callback";

        [JsonPropertyName("active_profile")] public string ActiveProfile { get; set; } = "music";
        [JsonPropertyName("profile_hotkey")] public string ProfileHotkey { get; set; } = "ctrl+shift+p";

        [JsonPropertyName("profiles")]
        public Dictionary<string, List<string>> Profiles { get; set; } = new()
        {
            ["gaming"] = new(),
            ["communication"] = new(),
            ["music"] = new(),
            ["video"] = new(),
        };

        [JsonPropertyName("seen_apps")] public List<string> SeenApps { get; set; } = new();

        /// <summary>"active" = nur spielende Apps | "assigned" = alle zugewiesenen Apps des Profils.</summary>
        [JsonPropertyName("profile_mode")] public string ProfileMode { get; set; } = "active";
        [JsonPropertyName("skip_empty_profiles")] public bool SkipEmptyProfiles { get; set; } = false;

        /// <summary>Farbschema für Einstellungen, moderne OSD-Karten und Media-Flyout: "system" | "light" | "dark".</summary>
        [JsonPropertyName("theme")] public string Theme { get; set; } = "system";

        /// <summary>"dark" = eigenes Design | "modern"/"windows" = ModernFlyouts-Look (Standard) | "win11" = Windows-11-Look.</summary>
        [JsonPropertyName("osd_design")] public string OsdDesign { get; set; } = "modern";
        [JsonPropertyName("color_profiles")] public Dictionary<string, Dictionary<string, string>> ColorProfiles { get; set; } = new();
        [JsonPropertyName("win_alpha")] public double WinAlpha { get; set; } = 0.88;

        /// <summary>
        /// Profile (max. 6, Reihenfolge = Wechsel-Reihenfolge): Schlüssel für profiles/active_profile, Name,
        /// Logo und Farbe. Leer = die 4 Standard-Profile (ConfigManager füllt beim Laden auf, Farben aus
        /// profile_color_*). Alle Profile sind frei änderbar und löschbar (mindestens eins bleibt).
        /// </summary>
        [JsonPropertyName("profile_list")] public List<ProfileEntry> ProfileList { get; set; } = new();

        // Farben der 4 Standard-Profile vor profile_list – nur noch Startwerte beim Anlegen der Liste.
        [JsonPropertyName("profile_color_gaming")] public string ProfileColorGaming { get; set; } = "#a855f7";
        [JsonPropertyName("profile_color_communication")] public string ProfileColorCommunication { get; set; } = "#3b82f6";
        [JsonPropertyName("profile_color_music")] public string ProfileColorMusic { get; set; } = "#4ade80";
        [JsonPropertyName("profile_color_video")] public string ProfileColorVideo { get; set; } = "#f97316";

        [JsonPropertyName("snap_positions")] public List<SnapPosition> SnapPositions { get; set; } = new();
        [JsonPropertyName("snap_enabled")] public bool SnapEnabled { get; set; } = false;
        [JsonPropertyName("snap_hold_ms")] public int SnapHoldMs { get; set; } = 600;

        /// <summary>"combined" = Media-Flyout hängt direkt am Lautstärke-Stapel (ModernFlyouts-Look) |
        /// "separate" = beide unabhängig frei positionierbar. "combined" wirkt nur bei osd_design "modern"/"windows"/"win11".</summary>
        [JsonPropertyName("osd_layout")] public string OsdLayout { get; set; } = "combined";

        /// <summary>Nur bei "combined": "below" = Media-Flyout unter dem Lautstärke-Stapel | "above" = darüber.
        /// Standard zusammen mit combined_anchor "media": OSD hängt unter dem Media-Flyout.</summary>
        [JsonPropertyName("media_combined_side")] public string MediaCombinedSide { get; set; } = "above";

        /// <summary>Nur bei "combined": "osd" = Media-Flyout hängt am Lautstärke-Stapel | "media" = Stapel hängt am Media-Flyout.
        /// Die Einstellungen setzen ihn passend zum gewählten Platz (Placement.CombinedAnchorFor).</summary>
        [JsonPropertyName("combined_anchor")] public string CombinedAnchor { get; set; } = "media";

        /// <summary>Sprache der Oberfläche: "de" | "en".</summary>
        [JsonPropertyName("language")] public string Language { get; set; } = "de";

        /// <summary>Design des Media-Flyouts, unabhängig vom OSD: "modernflyouts" | "win11" | "custom" (eigenes, media_custom_*).</summary>
        [JsonPropertyName("media_design")] public string MediaDesign { get; set; } = "modernflyouts";

        /// <summary>
        /// Eigenes Media-Design (media_design "custom"): Reihenfolge der Bereiche von oben nach unten
        /// ("app" = App-Zeile, "info" = Titel/Interpret + Cover, "progress" = Fortschritt, "sources" =
        /// Quellen-Leiste, "controls" = Knöpfe), Cover "right" | "left" | "top" | "none" mit Größe in px,
        /// Knöpfe "round" | "flat", Ausrichtung "center" | "left" | "right", Größe in %, „…“-Knopf
        /// "controls" (bei den Knöpfen) | "top" (rechts in der App-Zeile), Titelgröße und eigene Farben.
        /// </summary>
        [JsonPropertyName("media_custom_order")] public List<string> MediaCustomOrder { get; set; } = new() { "app", "info", "progress", "sources", "controls" };
        [JsonPropertyName("media_custom_show_app")] public bool MediaCustomShowApp { get; set; } = true;
        [JsonPropertyName("media_custom_show_times")] public bool MediaCustomShowTimes { get; set; } = true;
        [JsonPropertyName("media_custom_cover")] public string MediaCustomCover { get; set; } = "right";
        [JsonPropertyName("media_custom_cover_size")] public int MediaCustomCoverSize { get; set; } = 64;
        [JsonPropertyName("media_custom_buttons")] public string MediaCustomButtons { get; set; } = "round";
        [JsonPropertyName("media_custom_buttons_align")] public string MediaCustomButtonsAlign { get; set; } = "center";
        [JsonPropertyName("media_custom_button_size")] public int MediaCustomButtonSize { get; set; } = 100;
        [JsonPropertyName("media_custom_more_pos")] public string MediaCustomMorePos { get; set; } = "controls";
        [JsonPropertyName("media_custom_title_size")] public int MediaCustomTitleSize { get; set; } = 14;
        [JsonPropertyName("media_custom_bg")] public string MediaCustomBg { get; set; } = "#222222";
        [JsonPropertyName("media_custom_fg")] public string MediaCustomFg { get; set; } = "#ffffff";
        [JsonPropertyName("media_custom_sub")] public string MediaCustomSub { get; set; } = "#a0a0a6";
        [JsonPropertyName("media_custom_accent")] public string MediaCustomAccent { get; set; } = "#3b82f6";
        [JsonPropertyName("media_custom_button_bg")] public string MediaCustomButtonBg { get; set; } = "#3a3a3a";
        [JsonPropertyName("media_custom_track")] public string MediaCustomTrack { get; set; } = "#5a5a5a";

        /// <summary>
        /// Einträge im "…"-Menü des Media-Flyouts (Reihenfolge egal): "shuffle", "repeat", "switch_source",
        /// "skip_hotkeys", "pin", "save_position", "next_position", "reset_position", "settings", "close".
        /// </summary>
        [JsonPropertyName("media_more_items")] public List<string> MediaMoreItems { get; set; } = new() { "shuffle", "repeat" };

        /// <summary>Hotkeys für ±10 s aktiv (per "…"-Menü schnell umschaltbar, ohne die Tasten zu löschen).</summary>
        [JsonPropertyName("media_skip_hotkeys_enabled")] public bool MediaSkipHotkeysEnabled { get; set; } = true;

        /// <summary>Eckenradius in px (0 = eckig) der OSD-Karten/Neue-App-Popup bzw. des Media-Flyouts.</summary>
        [JsonPropertyName("osd_corner_radius")] public int OsdCornerRadius { get; set; } = 8;
        [JsonPropertyName("media_corner_radius")] public int MediaCornerRadius { get; set; } = 8;

        /// <summary>Klick auf OSD-Karte oder Media-Flyout (nicht auf einen Knopf) blendet es sofort aus.</summary>
        [JsonPropertyName("click_to_dismiss")] public bool ClickToDismiss { get; set; } = false;

        /// <summary>Deckkraft des Media-Flyout-Hintergrunds in % (Inhalt bleibt deckend).</summary>
        [JsonPropertyName("media_opacity")] public int MediaOpacity { get; set; } = 97;

        /// <summary>Cover-Hintergrund des Media-Flyouts: Unschärfe-Radius (0–60) und Abdunkelung/Aufhellung darüber (0–100 %).</summary>
        [JsonPropertyName("media_art_blur")] public int MediaArtBlur { get; set; } = 28;
        [JsonPropertyName("media_art_dim")] public int MediaArtDim { get; set; } = 75;

        /// <summary>
        /// Medien-Gesten: "Nächster Titel" und "Quelle wechseln" (media_switch_*) je mit eigener Taste
        /// ("" = Medien-Play/Pause-Taste, sonst Hotkey wie "ctrl+alt+n") und Geste "hold" (lange halten,
        /// media_switch_hold_ms) oder "double" (zweimal schnell drücken, media_double_ms).
        /// </summary>
        [JsonPropertyName("media_next_enabled")] public bool MediaNextEnabled { get; set; } = false;
        [JsonPropertyName("media_next_key")] public string MediaNextKey { get; set; } = "";
        [JsonPropertyName("media_next_gesture")] public string MediaNextGesture { get; set; } = "double";
        [JsonPropertyName("media_switch_key")] public string MediaSwitchKey { get; set; } = "";
        [JsonPropertyName("media_switch_gesture")] public string MediaSwitchGesture { get; set; } = "hold";
        [JsonPropertyName("media_double_ms")] public int MediaDoubleMs { get; set; } = 400;

        /// <summary>Snap-Zyklus: weiter gehalten → jeder weitere Wechsel nach halber Haltedauer (sonst nach voller).</summary>
        [JsonPropertyName("snap_repeat_fast")] public bool SnapRepeatFast { get; set; } = true;
        [JsonPropertyName("media_osd_x")] public int? MediaOsdX { get; set; }
        [JsonPropertyName("media_osd_y")] public int? MediaOsdY { get; set; }

        /// <summary>
        /// Platz-Voreinstellung (passt sich jeder Auflösung an): "custom" (osd_x/osd_y bzw. media_osd_x/y,
        /// leer = oben rechts bzw. oben mittig) | 3×3-Plätze "top_left" | "top_center" | "top_right" | "middle_left"
        /// | "center" | "middle_right" | "bottom_left" | "bottom_center" | "bottom_right" (Kacheln in den Einstellungen).
        /// Verschieben per Maus/Snap-Punkt setzt wieder "custom".
        /// </summary>
        [JsonPropertyName("osd_corner_preset")] public string OsdCornerPreset { get; set; } = "custom";
        [JsonPropertyName("media_corner_preset")] public string MediaCornerPreset { get; set; } = "custom";

        /// <summary>Bildschirm für die Ecken-Voreinstellungen (Gerätename wie "\\.\DISPLAY2", leer = Hauptbildschirm).</summary>
        [JsonPropertyName("osd_monitor")] public string OsdMonitor { get; set; } = "";
        [JsonPropertyName("media_monitor")] public string MediaMonitor { get; set; } = "";

        /// <summary>Bildschirmaufbau beim letzten Speichern der Positionen (für Auflösungswechsel).</summary>
        [JsonPropertyName("screen_layout")] public List<ScreenRef> ScreenLayout { get; set; } = new();

        /// <summary>
        /// Ein aktiver Snap-Punkt mit eigener OSD- UND Flyout-Position setzt das Anhängen ("Zusammen")
        /// aus, damit beide genau dort stehen. Endet bei Zurücksetzen oder Änderung von Anordnung/Ecke.
        /// </summary>
        [JsonPropertyName("snap_independent")] public bool SnapIndependent { get; set; } = false;

        /// <summary>Album-Cover als weichgezeichneter Hintergrund des Media-Flyouts (wie im Original-ModernFlyouts als Option).</summary>
        [JsonPropertyName("media_art_background")] public bool MediaArtBackground { get; set; } = false;

        /// <summary>Media-Flyout überhaupt anzeigen (aus = erscheint nie, Quellenwechsel per Taste funktioniert trotzdem).</summary>
        [JsonPropertyName("media_enabled")] public bool MediaEnabled { get; set; } = true;

        /// <summary>Wie lange das Media-Flyout sichtbar bleibt (ms), gerechnet ab der letzten Aktualisierung/Bedienung.</summary>
        [JsonPropertyName("media_duration_ms")] public int MediaDurationMs { get; set; } = 6000;

        /// <summary>±10-Sekunden-Knöpfe direkt neben Play/Pause im Media-Flyout anzeigen.</summary>
        [JsonPropertyName("media_skip_buttons")] public bool MediaSkipButtons { get; set; } = true;

        /// <summary>Globale Hotkeys für 10 Sekunden vor / zurück (leer = deaktiviert), z.B. "ctrl+alt+right".</summary>
        [JsonPropertyName("media_skip_forward_hotkey")] public string MediaSkipForwardHotkey { get; set; } = "";
        [JsonPropertyName("media_skip_back_hotkey")] public string MediaSkipBackHotkey { get; set; } = "";

        /// <summary>Fortschrittsleiste (mit Zeiten) im Media-Flyout anzeigen.</summary>
        [JsonPropertyName("media_show_progress")] public bool MediaShowProgress { get; set; } = true;

        /// <summary>"…"-Knopf (Zufallswiedergabe/Wiederholen) im Media-Flyout anzeigen.</summary>
        [JsonPropertyName("media_show_more")] public bool MediaShowMore { get; set; } = true;

        /// <summary>Medien-Wiedergabe-/Pause-Taste lange gedrückt halten wechselt die Medienquelle (statt nur Play/Pause).</summary>
        [JsonPropertyName("media_switch_enabled")] public bool MediaSwitchEnabled { get; set; } = false;
        [JsonPropertyName("media_switch_hold_ms")] public int MediaSwitchHoldMs { get; set; } = 600;

        // ── OSD-Farben ───────────────────────────────────────────────
        [JsonPropertyName("color_bg")] public string ColorBg { get; set; } = "#0d0d1c";
        [JsonPropertyName("color_vol_border")] public string ColorVolBorder { get; set; } = "#252540";
        [JsonPropertyName("color_vol_text")] public string ColorVolText { get; set; } = "#aaaadd";
        [JsonPropertyName("color_vol_pct")] public string ColorVolPct { get; set; } = "#ffffff";
        [JsonPropertyName("color_vol_icon")] public string ColorVolIcon { get; set; } = "#aaaadd";
        [JsonPropertyName("color_bar_bg")] public string ColorBarBg { get; set; } = "#1a1a30";
        [JsonPropertyName("color_bar_hi")] public string ColorBarHi { get; set; } = "#4ade80";
        [JsonPropertyName("color_bar_mid")] public string ColorBarMid { get; set; } = "#facc15";
        [JsonPropertyName("color_bar_lo")] public string ColorBarLo { get; set; } = "#f87171";
        [JsonPropertyName("color_prof_border")] public string ColorProfBorder { get; set; } = "#a855f7";
        [JsonPropertyName("color_prof_text")] public string ColorProfText { get; set; } = "#a855f7";
        [JsonPropertyName("color_snap_border")] public string ColorSnapBorder { get; set; } = "#facc15";
        [JsonPropertyName("color_snap_text")] public string ColorSnapText { get; set; } = "#facc15";

        // "Zusammen" wirkt nur beim modernen OSD-Design; sonst verhalten sich beide wie "Getrennt".
        // (Methoden statt Properties, damit sie nicht in die config.json serialisiert werden.)
        private bool CombinedModern() =>
            OsdLayout == "combined" && !SnapIndependent && (OsdDesign == "modern" || OsdDesign == "windows" || OsdDesign == "win11"
                || global::KnobFlyout.Osd.Designs.Get(OsdDesign) != null);

        /// <summary>Media-Flyout hängt am Lautstärke-Stapel (über oder unter ihm).</summary>
        public bool MediaFollowsOsd() => CombinedModern() && CombinedAnchor != "media";

        /// <summary>Lautstärke-Stapel hängt am Media-Flyout (über oder unter ihm).</summary>
        public bool OsdFollowsMedia() => CombinedModern() && CombinedAnchor == "media";

        /// <summary>Entspricht cfg.get(f"profile_color_{key}", color) – jetzt aus profile_list.</summary>
        public string GetProfileColor(string key, string fallback) =>
            global::KnobFlyout.Profiles.ProfileDefinitions.For(this).FirstOrDefault(p => p.Key == key)?.Color ?? fallback;
    }

    /// <summary>Ein Eintrag in cfg.profile_list (siehe ProfileDefinitions).</summary>
    public sealed class ProfileEntry
    {
        [JsonPropertyName("key")] public string Key { get; set; } = "";
        /// <summary>Leer = Standardname (bei den 4 Standard-Profilen in der eingestellten Sprache).</summary>
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        /// <summary>Windows-Symbol (Segoe Fluent Icons) als Logo.</summary>
        [JsonPropertyName("glyph")] public string Glyph { get; set; } = "";
        /// <summary>Emoji der unveränderten Standard-Profile (Design „Eigenes“); leer, sobald das Logo geändert wurde.</summary>
        [JsonPropertyName("emoji")] public string Emoji { get; set; } = "";
        /// <summary>Eigenes Logo als Base64-PNG (64×64); hat Vorrang vor glyph.</summary>
        [JsonPropertyName("image")] public string Image { get; set; } = "";
        [JsonPropertyName("color")] public string Color { get; set; } = "#3b82f6";
        /// <summary>Zuweisen-Taste: weist im Neue-App-Popup die angezeigte App diesem Profil zu (leer = keine), z. B. "ctrl+alt+1".</summary>
        [JsonPropertyName("assign_hotkey")] public string AssignHotkey { get; set; } = "";
    }
}
