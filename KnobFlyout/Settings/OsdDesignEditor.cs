using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using KnobFlyout.Config;
using KnobFlyout.Osd;
using static KnobFlyout.Loc;
using static KnobFlyout.Settings.UiHelpers;

namespace KnobFlyout.Settings
{
    /// <summary>
    /// Baukasten für die OSD-Karten des gewählten Designs (wie MediaDesignEditor fürs Flyout): Vorlagen als Startpunkt,
    /// je Karte die Teile in ihrer Reihenfolge (von oben nach unten = von links nach rechts) mit Ein/Aus und Pfeilen, die Farben.
    /// Genutzt von der Einstellungs-Unterseite und von der Einführung. Ändert cfg direkt und meldet jede Änderung über changed;
    /// eine Vorlage über rebuild (die Liste muss dann die neuen Werte zeigen).
    /// </summary>
    internal sealed class OsdDesignEditor
    {
        private readonly AppConfig _cfg;
        private readonly Action _changed;
        private readonly Action _rebuild;
        private readonly bool _compact; // schmale Spalte (Einführung)

        public OsdDesignEditor(AppConfig cfg, Action changed, Action rebuild, bool compact = false)
        {
            _cfg = cfg;
            _changed = changed;
            _rebuild = rebuild;
            _compact = compact;
        }

        public static string KindName(string kind) => kind switch
        {
            OsdCardLayout.Profile => T("Profil", "Profile"),
            OsdCardLayout.LockKey => T("Feststelltaste", "Lock key"),
            OsdCardLayout.Snap => "Position",
            _ => T("Lautstärke", "Volume"),
        };

        /// <summary>Auswahl der Karte als Reiter.</summary>
        public static FrameworkElement KindTabs(string current, Action<string> pick) =>
            Segments(OsdCardLayout.Kinds.Select(k => (k, KindName(k))).ToArray(), current, pick);

        private string PartName(string kind, string part) => (part, kind) switch
        {
            (OsdCardLayout.Icon, OsdCardLayout.LockKey) when _cfg.OsdDesign == "hardware" => "LED",
            (OsdCardLayout.Icon, OsdCardLayout.Profile) when _cfg.OsdDesign is "hardware" or "knob" => T("Wahlschalter", "Selector"),
            (OsdCardLayout.Icon, _) => T("Symbol", "Icon"),
            (OsdCardLayout.Name, OsdCardLayout.Volume) => T("App-Name", "App name"),
            (OsdCardLayout.Name, OsdCardLayout.Profile) => T("Profilname", "Profile name"),
            (OsdCardLayout.Name, _) => T("Titel", "Title"),
            (OsdCardLayout.Bar, OsdCardLayout.Volume) when _cfg.OsdDesign is "hardware" or "knob" => T("Drehknopf", "Knob"),
            (OsdCardLayout.Bar, OsdCardLayout.Snap) => T("Anzeige", "Indicator"),
            (OsdCardLayout.Bar, _) => T("Leiste", "Bar"),
            (OsdCardLayout.Value, _) => T("Zahl", "Number"),
            (OsdCardLayout.State, _) => T("An/Aus", "On/Off"),
            (OsdCardLayout.Profiles, _) => T("Profile", "Profiles"),
            _ => part,
        };

        private static string PartGlyph(string part) => part switch
        {
            OsdCardLayout.Icon => "\uE91B",     // Photo
            OsdCardLayout.Name => "\uE8D2",     // Font
            OsdCardLayout.Bar => "\uE767",      // Volume
            OsdCardLayout.Value => "\uE8EF",    // Calculator
            OsdCardLayout.State => "\uE72E",    // Lock
            OsdCardLayout.Profiles => "\uE71D", // AllApps
            _ => "\uE8FD",
        };

        /// <summary>Ausrichtung des Inhalts aller Karten: wie im Design oder links, mittig, rechts.</summary>
        public FrameworkElement AlignTabs(Action rebuild)
        {
            string current = _cfg.CardLayout().Align ?? "design";
            return Segments(new[] { ("design", T("Wie im Design", "As designed")), ("left", T("Links", "Left")), ("center", T("Mitte", "Center")), ("right", T("Rechts", "Right")) },
                current, key =>
                {
                    var l = _cfg.CardLayout();
                    l.Align = key == "design" ? null : key;
                    _cfg.SetCardLayout(l);
                    _changed();
                    rebuild();
                });
        }

        /// <summary>Teile einer Karte in ihrer Reihenfolge: Pfeile verschieben (oben = links), der Schalter blendet aus. Das letzte sichtbare Teil bleibt an.</summary>
        public StackPanel Elements(string kind)
        {
            var list = new StackPanel();
            void Render()
            {
                list.Children.Clear();
                var parts = OsdCard.PartKeys(_cfg, kind);
                var layout = _cfg.CardLayout();
                var order = layout.OrderFor(kind, parts);
                int visible = order.Count(p => layout.Shows(kind, p));
                for (int i = 0; i < order.Count; i++)
                {
                    string part = order[i];
                    int index = i;
                    void Move(int delta)
                    {
                        int to = index + delta;
                        if (to < 0 || to >= order.Count) return;
                        (order[index], order[to]) = (order[to], order[index]);
                        var l = _cfg.CardLayout();
                        l.SetOrder(kind, order, parts);
                        _cfg.SetCardLayout(l);
                        _changed();
                        Render();
                    }
                    var head = new StackPanel { Orientation = Orientation.Horizontal };
                    var up = FlatIconButton("\uE70E", T("Nach links (nach oben in der Liste)", "Move left (up in the list)"), () => Move(-1));  // ChevronUp
                    var down = FlatIconButton("\uE70D", T("Nach rechts (nach unten in der Liste)", "Move right (down in the list)"), () => Move(+1)); // ChevronDown
                    up.IsEnabled = index > 0;
                    down.IsEnabled = index < order.Count - 1;
                    up.Margin = down.Margin = new Thickness(0, 0, 2, 0);
                    head.Children.Add(up);
                    head.Children.Add(down);
                    bool shows = layout.Shows(kind, part);
                    var toggle = Toggle(shows, v =>
                    {
                        var l = _cfg.CardLayout();
                        l.Set(kind, part, v);
                        _cfg.SetCardLayout(l);
                        _changed();
                        Render();
                    });
                    toggle.IsEnabled = !shows || visible > 1;
                    toggle.Margin = new Thickness(10, 0, 0, 0);
                    toggle.VerticalAlignment = VerticalAlignment.Center;
                    head.Children.Add(toggle);
                    var card = SettingCard(PartGlyph(part), PartName(kind, part), null, head);
                    if (_compact && card.Child is FrameworkElement inner) inner.Margin = new Thickness(14, 2, 14, 2); // schmale Spalte: ohne Scrollen
                    list.Children.Add(card);
                }
            }
            Render();
            return list;
        }

        /// <summary>Echte Karte mit dem aktuellen Entwurf (reine Ansicht).</summary>
        public static FrameworkElement Preview(AppConfig cfg, string kind)
        {
            FrameworkElement card;
            try
            {
                if (kind == OsdCardLayout.LockKey) card = OsdCard.CreateLockPreview(cfg, "CapsLock", true);
                else if (kind == OsdCardLayout.Volume) card = OsdCard.CreateLiveVolumePreview(cfg, "spotify.exe", 64, false, out _);
                else card = OsdCard.CreatePreview(cfg, kind);
            }
            catch (Exception ex)
            {
                Logger.Error($"OSD-Vorschau: {ex.Message}");
                card = new Border();
            }
            card.IsHitTestVisible = false;
            return card;
        }

        // ── Vorlagen ────────────────────────────────────────────────────────

        private static string TemplateName(string key) => key switch
        {
            "compact" => T("Kompakt", "Compact"),
            "minimal" => T("Minimal", "Minimal"),
            "mirrored" => T("Gespiegelt", "Mirrored"),
            _ => T("Standard", "Standard"),
        };

        /// <summary>Vorlagen als Kacheln mit echter Lautstärke-Karte im gewählten Design; die aktive ist markiert.</summary>
        public UIElement Templates()
        {
            var keys = OsdCardLayout.TemplateKeys;
            string active = _cfg.CardLayout().Template ?? "standard";
            var grid = new UniformGrid { Columns = _compact ? 2 : keys.Length };
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                bool on = key == active;
                var sample = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(_cfg)) ?? new AppConfig();
                sample.SetCardLayout(OsdCardLayout.FromTemplate(key));
                var content = new StackPanel();
                content.Children.Add(new Viewbox { Height = _compact ? 30 : 44, Stretch = Stretch.Uniform, Child = Preview(sample, OsdCardLayout.Volume) });
                content.Children.Add(new TextBlock
                {
                    Text = TemplateName(key), FontSize = _compact ? 12 : 14, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Fg,
                    HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, _compact ? 4 : 8, 0, 0),
                });
                double pad = _compact ? 6 : 10;
                var tile = new Border
                {
                    Background = CardBg, BorderBrush = on ? Accent : CardBorder, BorderThickness = new Thickness(on ? 2 : 1), CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(on ? pad - 1 : pad), Margin = new Thickness(0, 0, _compact ? 6 : (i < keys.Length - 1 ? 8 : 0), _compact ? 6 : 0),
                    Cursor = System.Windows.Input.Cursors.Hand, Child = content,
                    ToolTip = T("Als Startpunkt übernehmen", "Use as starting point"),
                };
                tile.MouseEnter += (_, _) => Motion.Fade(tile, HoverBg);
                tile.MouseLeave += (_, _) => Motion.Fade(tile, CardBg);
                tile.MouseLeftButtonUp += (_, _) =>
                {
                    _cfg.SetCardLayout(OsdCardLayout.FromTemplate(key));
                    _changed();
                    _rebuild();
                };
                grid.Children.Add(tile);
            }
            return grid;
        }

        // ── Farben ──────────────────────────────────────────────────────────

        /// <summary>Farben des gewählten OSD-Designs; beim Design „Eigenes“ seine festen Farben (auch die der Positions-Karte).</summary>
        /// <param name="boxes">Hex-Felder für die Farbprofile (Schlüssel wie in den Profilen).</param>
        public UIElement Colors(Dictionary<string, TextBox>? boxes = null)
        {
            if (_cfg.OsdDesign != "dark") return MediaDesignEditor.OsdColors(_cfg, _changed, _compact, boxes);

            var c = _cfg;
            var d = new AppConfig();
            Panel fields = _compact ? new UniformGrid { Columns = 2 } : new StackPanel();
            void Add(string json, string label, string value, string def, Action<string> set)
            {
                var row = MediaDesignEditor.ColorRow(label, value, def, hex => { set(hex); _changed(); }, !_compact);
                if (boxes != null && row.Tag is TextBox box) boxes[ColorProfiles.Normalize(json)] = box;
                fields.Children.Add(row);
            }
            Add("color_bg", T("Hintergrund", "Background"), c.ColorBg, d.ColorBg, v => c.ColorBg = v);
            Add("color_vol_border", T("Rahmen", "Border"), c.ColorVolBorder, d.ColorVolBorder, v => c.ColorVolBorder = v);
            Add("color_vol_text", T("App-Name", "App name"), c.ColorVolText, d.ColorVolText, v => c.ColorVolText = v);
            Add("color_vol_pct", T("Prozent", "Percent"), c.ColorVolPct, d.ColorVolPct, v => c.ColorVolPct = v);
            Add("color_vol_icon", T("Symbol", "Icon"), c.ColorVolIcon, d.ColorVolIcon, v => c.ColorVolIcon = v);
            Add("color_bar_bg", T("Balken leer", "Bar empty"), c.ColorBarBg, d.ColorBarBg, v => c.ColorBarBg = v);
            Add("color_bar_hi", T("Balken hoch", "Bar high"), c.ColorBarHi, d.ColorBarHi, v => c.ColorBarHi = v);
            Add("color_bar_mid", T("Balken mittel", "Bar medium"), c.ColorBarMid, d.ColorBarMid, v => c.ColorBarMid = v);
            Add("color_bar_lo", T("Balken niedrig", "Bar low"), c.ColorBarLo, d.ColorBarLo, v => c.ColorBarLo = v);
            Add("color_prof_border", T("Profil-Rahmen", "Profile border"), c.ColorProfBorder, d.ColorProfBorder, v => c.ColorProfBorder = v);
            Add("color_prof_text", T("Profil-Text", "Profile text"), c.ColorProfText, d.ColorProfText, v => c.ColorProfText = v);
            Add("color_snap_border", T("Position: Balken", "Position: bar"), c.ColorSnapBorder, d.ColorSnapBorder, v => c.ColorSnapBorder = v);
            Add("color_snap_text", T("Position: Text", "Position: text"), c.ColorSnapText, d.ColorSnapText, v => c.ColorSnapText = v);
            return fields;
        }
    }
}
