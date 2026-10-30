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
        [JsonPropertyName("step")] public int Step { get; set; } = 2;
        [JsonPropertyName("step_spotify")] public int StepSpotify { get; set; } = 4;
        [JsonPropertyName("osd")] public bool Osd { get; set; } = true;
        [JsonPropertyName("osd_duration")] public int OsdDuration { get; set; } = 2500;
        /// <summary>OSD für Feststell-, Num- und Rollen-Taste anzeigen.</summary>
        [JsonPropertyName("lock_keys_osd")] public bool LockKeysOsd { get; set; } = true;
        [JsonPropertyName("osd_x")] public int? OsdX { get; set; }
        [JsonPropertyName("osd_y")] public int? OsdY { get; set; }

        [JsonPropertyName("spotify_use_api")] public bool SpotifyUseApi { get; set; } = false;
        [JsonPropertyName("spotify_client_id")] public string SpotifyClientId { get; set; } = "";
        [JsonPropertyName("spotify_client_secret")] public string SpotifyClientSecret { get; set; } = "";
        [JsonPropertyName("spotify_redirect_uri")] public string SpotifyRedirectUri { get; set; } = "http://localhost:5000/callback";

        /// <summary>YT-Bridge: spielt in Firefox/Chrome ein YouTube-Video, regelt der Knopf den YouTube-Player statt der Browser-Session (braucht die Erweiterung aus Browser/firefox bzw. Browser/chrome).</summary>
        [JsonPropertyName("youtube_bridge")] public bool YoutubeBridge { get; set; } = false;

        /// <summary>Einführung: Häkchen „Nicht mehr anzeigen“ – ohne es erscheint sie bei jedem Öffnen der Einstellungen.</summary>
        [JsonPropertyName("tutorial_hidden")] public bool TutorialHidden { get; set; } = false;
        /// <summary>Einführung wurde schon einmal gezeigt (sonst erscheint sie beim Programmstart).</summary>
        [JsonPropertyName("tutorial_started")] public bool TutorialStarted { get; set; } = false;

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

        /// <summary>Neue Apps ohne Rückfrage einsortieren (Profiles/AppClassifier.cs); nicht Erkanntes fragt weiter das Popup.</summary>
        [JsonPropertyName("auto_assign")] public bool AutoAssign { get; set; } = true;

        /// <summary>Art der App (games, browsers, music, comms) → Profil; "" = nachfragen, fehlt = Standard-Profil der Art.</summary>
        [JsonPropertyName("auto_assign_map")] public Dictionary<string, string> AutoAssignMap { get; set; } = new();

        /// <summary>"active" = nur spielende Apps | "assigned" = alle zugewiesenen Apps des Profils.</summary>
        [JsonPropertyName("profile_mode")] public string ProfileMode { get; set; } = "active";
        [JsonPropertyName("skip_empty_profiles")] public bool SkipEmptyProfiles { get; set; } = false;

        /// <summary>Farbschema für Einstellungen, moderne OSD-Karten und Media-Flyout: "system" | "light" | "dark".</summary>
        [JsonPropertyName("theme")] public string Theme { get; set; } = "system";

        /// <summary>"dark" = eigenes Design | "modern"/"windows" = ModernFlyouts-Look (Standard) | "win11" = Windows-11-Look.</summary>
        [JsonPropertyName("osd_design")] public string OsdDesign { get; set; } = "modern";
        [JsonPropertyName("color_profiles")] public Dictionary<string, Dictionary<string, string>> ColorProfiles { get; set; } = new();
        [JsonPropertyName("win_alpha")] public double WinAlpha { get; set; } = 0.88;

        /// <summary>Deckkraft von Text, Symbolen, Leisten und Knöpfen der OSD-Karten und des Neue-App-Popups in % (20–100; Hintergrund: win_alpha).</summary>
        [JsonPropertyName("osd_content_opacity")] public int OsdContentOpacity { get; set; } = 100;

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

        /// <summary>Aufbau des Flyouts je Design (Reihenfolge, was zu sehen ist, Cover, Knöpfe), siehe MediaLayout.</summary>
        [JsonPropertyName("media_layouts")] public Dictionary<string, MediaLayout> MediaLayouts { get; set; } = new();

        /// <summary>Unter einem Namen gespeicherte Flyout-Designs.</summary>
        [JsonPropertyName("media_saved_designs")] public List<SavedMediaDesign> MediaSavedDesigns { get; set; } = new();

        /// <summary>Name des gerade gewählten gespeicherten Designs ("" = keins).</summary>
        [JsonPropertyName("media_saved_active")] public string MediaSavedActive { get; set; } = "";

        // Bis 1.3 gab es den Aufbau nur für „Eigenes“, Zeitleiste/±10 s/„…“ galten für alle Designs: werden beim Laden in
        // media_layouts übernommen (EnsureLayouts) und danach nicht mehr geschrieben.
        [JsonPropertyName("media_custom_order"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? OldCustomOrder { get; set; }
        [JsonPropertyName("media_custom_show_app"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? OldCustomShowApp { get; set; }
        [JsonPropertyName("media_custom_show_times"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? OldCustomShowTimes { get; set; }
        [JsonPropertyName("media_custom_cover"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OldCustomCover { get; set; }
        [JsonPropertyName("media_custom_cover_size"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? OldCustomCoverSize { get; set; }
        [JsonPropertyName("media_custom_buttons"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OldCustomButtons { get; set; }
        [JsonPropertyName("media_custom_buttons_align"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OldCustomButtonsAlign { get; set; }
        [JsonPropertyName("media_custom_button_size"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? OldCustomButtonSize { get; set; }
        [JsonPropertyName("media_custom_more_pos"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OldCustomMorePos { get; set; }
        [JsonPropertyName("media_custom_title_size"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? OldCustomTitleSize { get; set; }
        [JsonPropertyName("media_custom_show_title"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? OldCustomShowTitle { get; set; }
        [JsonPropertyName("media_custom_show_artist"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? OldCustomShowArtist { get; set; }
        [JsonPropertyName("media_custom_show_sources"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? OldCustomShowSources { get; set; }
        [JsonPropertyName("media_custom_show_prev"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? OldCustomShowPrev { get; set; }
        [JsonPropertyName("media_custom_show_play"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? OldCustomShowPlay { get; set; }
        [JsonPropertyName("media_custom_show_next"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? OldCustomShowNext { get; set; }
        [JsonPropertyName("media_skip_buttons"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? OldSkipButtons { get; set; }
        [JsonPropertyName("media_show_progress"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? OldShowProgress { get; set; }
        [JsonPropertyName("media_show_more"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? OldShowMore { get; set; }

        /// <summary>Eigene Farben des Designs „Eigenes“.</summary>
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

        /// <summary>Deckkraft des Media-Flyout-Hintergrunds in % (0–100; Inhalt: media_content_opacity).</summary>
        [JsonPropertyName("media_opacity")] public int MediaOpacity { get; set; } = 97;

        /// <summary>Deckkraft von Text, Symbolen, Cover, Leiste und Knöpfen des Media-Flyouts in % (20–100).</summary>
        [JsonPropertyName("media_content_opacity")] public int MediaContentOpacity { get; set; } = 100;

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

        /// <summary>
        /// Daueranzeige: Solange eines dieser Programme eine Media-Session hat (Name wie in der Quellen-Leiste, z. B. "Spotify"),
        /// bleibt das Flyout stehen, statt nach media_duration_ms auszublenden.
        /// </summary>
        [JsonPropertyName("media_keep_apps")] public List<string> MediaKeepApps { get; set; } = new();

        /// <summary>Wann die Daueranzeige steht: "playing" = nur während das Programm abspielt, "open" = solange es geöffnet ist (auch pausiert).</summary>
        [JsonPropertyName("media_keep_mode")] public string MediaKeepMode { get; set; } = "playing";

        /// <summary>Daueranzeige an eigener Stelle statt an der normalen Flyout-Position: Platz wie media_corner_preset ("custom" = media_keep_x/y), Bildschirm wie media_monitor.</summary>
        [JsonPropertyName("media_keep_own_position")] public bool MediaKeepOwnPosition { get; set; } = false;
        [JsonPropertyName("media_keep_preset")] public string MediaKeepPreset { get; set; } = "bottom_right";
        [JsonPropertyName("media_keep_monitor")] public string MediaKeepMonitor { get; set; } = "";
        [JsonPropertyName("media_keep_x")] public int? MediaKeepX { get; set; }
        [JsonPropertyName("media_keep_y")] public int? MediaKeepY { get; set; }

        /// <summary>±10-Sekunden-Knöpfe im gewählten Design (Teil des Aufbaus).</summary>
        [JsonIgnore] public bool MediaSkipButtons { get => Layout().Shows(MediaLayout.Skip); set => Layout().Set(MediaLayout.Skip, value); }

        /// <summary>Globale Hotkeys für 10 Sekunden vor / zurück (leer = deaktiviert), z.B. "ctrl+alt+right".</summary>
        [JsonPropertyName("media_skip_forward_hotkey")] public string MediaSkipForwardHotkey { get; set; } = "";
        [JsonPropertyName("media_skip_back_hotkey")] public string MediaSkipBackHotkey { get; set; } = "";

        /// <summary>Fortschrittsleiste im gewählten Design (Teil des Aufbaus).</summary>
        [JsonIgnore] public bool MediaShowProgress { get => Layout().Shows(MediaLayout.Progress); set => Layout().Set(MediaLayout.Progress, value); }

        /// <summary>"…"-Knopf im gewählten Design (Teil des Aufbaus).</summary>
        [JsonIgnore] public bool MediaShowMore { get => Layout().Shows(MediaLayout.More); set => Layout().Set(MediaLayout.More, value); }

        /// <summary>„Design anpassen“: Vorschau als echtes Flyout mit dem, was gerade läuft, statt mit dem Beispiel.</summary>
        [JsonPropertyName("media_preview_live")] public bool MediaPreviewLive { get; set; } = false;

        /// <summary>Quellen-Leiste im gewählten Design (Teil des Aufbaus).</summary>
        [JsonIgnore] public bool MediaShowSources { get => Layout().Shows(MediaLayout.Sources); set => Layout().Set(MediaLayout.Sources, value); }

        /// <summary>Pause-Zeichen neben spielenden Quellen im gewählten Design (Teil des Aufbaus).</summary>
        [JsonIgnore] public bool MediaShowSourcePause { get => Layout().Shows(MediaLayout.SourcePause); set => Layout().Set(MediaLayout.SourcePause, value); }

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

        /// <summary>Aufbau des Flyouts für dieses Design (null = das gewählte).</summary>
        public MediaLayout Layout(string? design = null)
        {
            string key = design ?? MediaDesign;
            if (!MediaLayouts.TryGetValue(key, out var layout)) MediaLayouts[key] = layout = new MediaLayout();
            return layout;
        }

        /// <summary>Beim Laden: je Design ein Aufbau; Werte aus älteren Versionen übernehmen.</summary>
        public void EnsureLayouts()
        {
            var designs = new[] { "modernflyouts", "win11", "custom" }.Concat(global::KnobFlyout.Osd.Designs.All.Select(d => d.Key)).ToList();
            foreach (var key in designs) Layout(key);

            var custom = Layout("custom");
            if (OldCustomOrder != null && !OldCustomOrder.SequenceEqual(new[] { "app", "info", "progress", "sources", "controls" })) custom.Order = OldCustomOrder;
            if (OldCustomShowApp == false) custom.Set(MediaLayout.App, false);
            if (OldCustomShowTimes == false) custom.Set(MediaLayout.Times, false);
            if (OldCustomShowTitle == false) custom.Set(MediaLayout.Title, false);
            if (OldCustomShowArtist == false) custom.Set(MediaLayout.Artist, false);
            if (OldCustomShowSources == false) custom.Set(MediaLayout.Sources, false);
            if (OldCustomShowPrev == false) custom.Set(MediaLayout.Prev, false);
            if (OldCustomShowPlay == false) custom.Set(MediaLayout.Play, false);
            if (OldCustomShowNext == false) custom.Set(MediaLayout.Next, false);
            if (OldCustomCover != null && OldCustomCover != "right") custom.Cover = OldCustomCover;
            if (OldCustomCoverSize is { } coverSize && coverSize != 64) custom.CoverSize = coverSize;
            if (OldCustomButtons == "flat") custom.Buttons = "flat";
            if (OldCustomButtonsAlign != null && OldCustomButtonsAlign != "center") custom.ButtonsAlign = OldCustomButtonsAlign;
            if (OldCustomButtonSize is { } buttonSize && buttonSize != 100) custom.ButtonSize = buttonSize;
            if (OldCustomMorePos == "top") custom.MorePos = "top";
            if (OldCustomTitleSize is { } titleSize && titleSize != 14) custom.TitleSize = titleSize;
            foreach (var key in designs)
            {
                var layout = Layout(key);
                if (OldShowProgress == false) layout.Set(MediaLayout.Progress, false);
                if (OldSkipButtons == false) layout.Set(MediaLayout.Skip, false);
                if (OldShowMore == false) layout.Set(MediaLayout.More, false);
            }
            OldCustomOrder = null;
            OldCustomShowApp = OldCustomShowTimes = OldCustomShowTitle = OldCustomShowArtist = OldCustomShowSources = OldCustomShowPrev = OldCustomShowPlay = OldCustomShowNext = null;
            OldCustomCover = OldCustomButtons = OldCustomButtonsAlign = OldCustomMorePos = null;
            OldCustomCoverSize = OldCustomButtonSize = OldCustomTitleSize = null;
            OldShowProgress = OldSkipButtons = OldShowMore = null;
        }

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
