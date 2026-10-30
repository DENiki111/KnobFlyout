using System;
using System.Collections.Generic;
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
    /// Baukasten für das eigene Media-Design: Vorlagen als Startpunkt und alle Bereiche in der Reihenfolge des
    /// Flyouts (Ein/Aus, ↑/↓, aufgeklappt die Elemente als Kacheln und die Feineinstellungen). Genutzt von der
    /// Einstellungs-Unterseite und von der Einführung. Ändert cfg direkt und meldet jede Änderung über changed;
    /// eine gewählte Vorlage über rebuild (die Schalter müssen dann die neuen Werte zeigen).
    /// </summary>
    internal sealed class MediaDesignEditor
    {
        private static readonly AppConfig Defaults = new();

        private readonly AppConfig _cfg;
        private readonly Action _changed;
        private readonly Action _rebuild;
        private readonly HashSet<string> _expanded;
        private readonly bool _compact; // schmale Spalte: kürzere Karten, kleinere Kacheln und Slider

        public MediaDesignEditor(AppConfig cfg, HashSet<string> expanded, Action changed, Action rebuild, bool compact = false)
        {
            _cfg = cfg;
            _expanded = expanded;
            _changed = changed;
            _rebuild = rebuild;
            _compact = compact;
        }

        private double SliderW => _compact ? 110 : 180;
        private double ComboW => _compact ? 130 : 160;

        /// <summary>Setter, der danach die Änderung meldet.</summary>
        private Action<T> Set<T>(Action<T> set) => v =>
        {
            set(v);
            _changed();
        };

        // ── Bereiche ────────────────────────────────────────────────────────

        /// <summary>Alle Bereiche in Flyout-Reihenfolge; ↑/↓ zeichnet die Liste neu.</summary>
        public StackPanel Elements()
        {
            var elements = new StackPanel();
            void Render()
            {
                elements.Children.Clear();
                var order = _cfg.MediaCustomOrder = MediaOsd.NormalizeOrder(_cfg.MediaCustomOrder);
                for (int i = 0; i < order.Count; i++)
                {
                    elements.Children.Add(BlockCard(order[i], i, order.Count, (from, delta) =>
                    {
                        int to = from + delta;
                        if (to < 0 || to >= order.Count) return;
                        (order[from], order[to]) = (order[to], order[from]);
                        _changed();
                        Render();
                    }));
                }
            }
            Render();
            return elements;
        }

        /// <summary>Ein Bereich des Flyouts als Karte: ↑/↓ und ggf. Ein/Aus im Kopf, aufgeklappt seine Elemente und Einstellungen.</summary>
        private Border BlockCard(string key, int index, int count, Action<int, int> move)
        {
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var up = FlatIconButton("\uE70E", T("Nach oben", "Move up"), () => move(index, -1));     // ChevronUp
            var down = FlatIconButton("\uE70D", T("Nach unten", "Move down"), () => move(index, +1)); // ChevronDown
            up.IsEnabled = index > 0;
            down.IsEnabled = index < count - 1;
            up.Margin = down.Margin = new Thickness(0, 0, 2, 0);
            head.Children.Add(up);
            head.Children.Add(down);
            void Switch(bool value, Action<bool> set)
            {
                var toggle = Toggle(value, Set(set));
                toggle.Margin = new Thickness(10, 0, 0, 0);
                toggle.VerticalAlignment = VerticalAlignment.Center;
                head.Children.Add(toggle);
            }

            string glyph, title, desc;
            StackPanel? body = new StackPanel();
            switch (key)
            {
                case "app":
                    glyph = "\uE71D"; // AllApps
                    title = T("App-Zeile", "App row");
                    desc = T("Name und Symbol der App, die gerade spielt", "Name and icon of the app that is playing");
                    Switch(_cfg.MediaCustomShowApp, v => _cfg.MediaCustomShowApp = v);
                    body.Children.Add(OptionRow(T("„…“-Knopf rechts in dieser Zeile (statt bei den Knöpfen)", "“…” button on the right of this row (instead of with the buttons)"),
                        Toggle(_cfg.MediaCustomMorePos == "top", Set<bool>(v => _cfg.MediaCustomMorePos = v ? "top" : "controls"))));
                    break;
                case "info":
                    glyph = "\uE8D6"; // Audio
                    title = T("Titel und Cover", "Title and album art");
                    desc = T("Titel, Interpret und Cover", "Title, artist and album art");
                    AddInfoOptions(body);
                    break;
                case "progress":
                    glyph = "\uE823"; // Recent
                    title = T("Zeitleiste", "Timeline");
                    desc = T("Zeigt, wie weit der Titel ist · zum Springen anklicken oder ziehen", "Shows how far the track is · click or drag to jump");
                    Switch(_cfg.MediaShowProgress, v => _cfg.MediaShowProgress = v);
                    body.Children.Add(OptionRow(T("Zeiten darunter", "Times below"),
                        Toggle(_cfg.MediaCustomShowTimes, Set<bool>(v => _cfg.MediaCustomShowTimes = v))));
                    break;
                case "sources":
                    glyph = "\uE8AB"; // Switch
                    title = T("Quellen-Leiste", "Sources bar");
                    desc = T("Nur sichtbar, wenn mehrere Apps gleichzeitig Medien melden", "Only visible when several apps report media at once");
                    Switch(_cfg.MediaCustomShowSources, v => _cfg.MediaCustomShowSources = v);
                    body = null;
                    break;
                default: // "controls"
                    glyph = "\uE768"; // Play
                    title = T("Knöpfe", "Buttons");
                    desc = T("Zurück, ±10 s, Play/Pause, Weiter und „…“", "Previous, ±10 s, play/pause, next and “…”");
                    AddButtonOptions(body);
                    break;
            }
            string? shown = _compact ? null : desc; // schmale Spalte: nur der Titel, sonst bricht die Zeile mehrfach um
            if (body == null) return SettingCard(glyph, title, shown, head);
            return ExpanderCard(glyph, title, shown, body, _expanded.Contains(key), head,
                open => { if (open) _expanded.Add(key); else _expanded.Remove(key); });
        }

        /// <summary>Titel/Interpret/Cover als Kacheln, dazu Platz und Größe des Covers und die Titelgröße.</summary>
        private void AddInfoOptions(StackPanel body)
        {
            string[] covers = { "right", "left", "top" };
            string coverPos = Array.IndexOf(covers, _cfg.MediaCustomCover) >= 0 ? _cfg.MediaCustomCover : "right"; // bei „aus“: Platz fürs Wiedereinschalten
            var tiles = new WrapPanel();
            tiles.Children.Add(ElementTile("\uE8D2", T("Titel", "Title"), _cfg.MediaCustomShowTitle, Set<bool>(v => _cfg.MediaCustomShowTitle = v)));    // Font
            tiles.Children.Add(ElementTile("\uE77B", T("Interpret", "Artist"), _cfg.MediaCustomShowArtist, Set<bool>(v => _cfg.MediaCustomShowArtist = v))); // Contact
            tiles.Children.Add(ElementTile("\uE91B", "Cover", _cfg.MediaCustomCover != "none", Set<bool>(v => _cfg.MediaCustomCover = v ? coverPos : "none"))); // Photo
            body.Children.Add(tiles);

            var coverCombo = Combo(ComboW, Array.IndexOf(covers, coverPos), T("Rechts", "Right"), T("Links", "Left"), T("Oben", "Top"));
            coverCombo.SelectionChanged += (_, _) =>
            {
                coverPos = covers[Math.Max(0, coverCombo.SelectedIndex)];
                if (_cfg.MediaCustomCover != "none") _cfg.MediaCustomCover = coverPos;
                _changed();
            };
            body.Children.Add(OptionRow(T("Platz des Covers", "Album art position"),
                WithReset(coverCombo, () => coverCombo.SelectedIndex = Math.Max(0, Array.IndexOf(covers, Defaults.MediaCustomCover)))));
            body.Children.Add(OptionRow(T("Größe des Covers", "Album art size"),
                ResetSlider(SliderControl(32, 140, Math.Clamp(_cfg.MediaCustomCoverSize, 32, 140), "0' px'", Set<double>(v => _cfg.MediaCustomCoverSize = (int)v), tick: 2, width: SliderW),
                    Defaults.MediaCustomCoverSize)));
            body.Children.Add(OptionRow(T("Titelgröße", "Title size"),
                ResetSlider(SliderControl(11, 22, Math.Clamp(_cfg.MediaCustomTitleSize, 11, 22), "0' pt'", Set<double>(v => _cfg.MediaCustomTitleSize = (int)v), tick: 1, width: SliderW),
                    Defaults.MediaCustomTitleSize)));
        }

        /// <summary>Jeder Knopf als Kachel (in der Reihenfolge des Flyouts), dazu Stil, Ausrichtung und Größe.</summary>
        private void AddButtonOptions(StackPanel body)
        {
            var tiles = new WrapPanel();
            tiles.Children.Add(ElementTile("\uE892", T("Zurück", "Previous"), _cfg.MediaCustomShowPrev, Set<bool>(v => _cfg.MediaCustomShowPrev = v))); // Previous
            tiles.Children.Add(ElementTile("\uED3C", "±10 s", _cfg.MediaSkipButtons, Set<bool>(v => _cfg.MediaSkipButtons = v)));                       // SkipBack10
            tiles.Children.Add(ElementTile("\uE768", "Play/Pause", _cfg.MediaCustomShowPlay, Set<bool>(v => _cfg.MediaCustomShowPlay = v)));            // Play
            tiles.Children.Add(ElementTile("\uE893", T("Weiter", "Next"), _cfg.MediaCustomShowNext, Set<bool>(v => _cfg.MediaCustomShowNext = v)));     // Next
            tiles.Children.Add(ElementTile("\uE712", T("„…“-Menü", "“…” menu"), _cfg.MediaShowMore, Set<bool>(v => _cfg.MediaShowMore = v)));          // More
            body.Children.Add(tiles);

            string[] styles = { "round", "flat" };
            var styleCombo = Combo(ComboW, Math.Max(0, Array.IndexOf(styles, _cfg.MediaCustomButtons)), T("Rund", "Round"), T("Flach", "Flat"));
            styleCombo.SelectionChanged += (_, _) => { _cfg.MediaCustomButtons = styles[Math.Max(0, styleCombo.SelectedIndex)]; _changed(); };
            string[] aligns = { "center", "left", "right" };
            var alignCombo = Combo(ComboW, Math.Max(0, Array.IndexOf(aligns, _cfg.MediaCustomButtonsAlign)), T("Mittig", "Center"), T("Links", "Left"), T("Rechts", "Right"));
            alignCombo.SelectionChanged += (_, _) => { _cfg.MediaCustomButtonsAlign = aligns[Math.Max(0, alignCombo.SelectedIndex)]; _changed(); };
            body.Children.Add(OptionRow(T("Stil", "Style"),
                WithReset(styleCombo, () => styleCombo.SelectedIndex = Math.Max(0, Array.IndexOf(styles, Defaults.MediaCustomButtons)))));
            body.Children.Add(OptionRow(T("Ausrichtung", "Alignment"),
                WithReset(alignCombo, () => alignCombo.SelectedIndex = Math.Max(0, Array.IndexOf(aligns, Defaults.MediaCustomButtonsAlign)))));
            body.Children.Add(OptionRow(T("Größe", "Size"),
                ResetSlider(SliderControl(70, 140, Math.Clamp(_cfg.MediaCustomButtonSize, 70, 140), "0' %'", Set<double>(v => _cfg.MediaCustomButtonSize = (int)v), tick: 5, width: SliderW),
                    Defaults.MediaCustomButtonSize)));
            if (!_compact)
                body.Children.Add(Hint(T("Einträge im „…“-Menü: Seite „Media-Flyout“ → Inhalt.", "Items in the “…” menu: “Media flyout” page → Content.")));
        }

        /// <summary>Kachel für ein einzelnes Element (Symbol + Name): an = Akzentfarbe mit Häkchen, aus = grau; Klick schaltet um.</summary>
        private Border ElementTile(string glyph, string label, bool on, Action<bool> set)
        {
            var icon = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center };
            var text = new TextBlock { Text = label, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
            var check = new TextBlock
            {
                Text = "\uE73E", FontFamily = IconFont, FontSize = 10, // CheckMark
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 6, 0),
            };
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(icon);
            stack.Children.Add(text);
            var grid = new Grid();
            grid.Children.Add(stack);
            grid.Children.Add(check);
            var tile = new Border
            {
                Width = _compact ? 66 : 84, Height = 62, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, _compact ? 6 : 8, 8), Cursor = System.Windows.Input.Cursors.Hand, Child = grid,
                ToolTip = T("Klicken zum Ein- oder Ausblenden", "Click to show or hide"),
            };
            void Apply()
            {
                tile.Background = on ? Accent : FieldBg;
                tile.BorderBrush = on ? Accent : FieldBorder;
                icon.Foreground = text.Foreground = check.Foreground = on ? Brushes.White : SubFg;
                check.Visibility = on ? Visibility.Visible : Visibility.Hidden;
            }
            Apply();
            tile.MouseLeftButtonDown += (_, e) => e.Handled = true;
            tile.MouseLeftButtonUp += (_, e) =>
            {
                on = !on;
                Apply();
                set(on);
                e.Handled = true;
            };
            return tile;
        }

        // ── Vorlagen ────────────────────────────────────────────────────────

        private static readonly (string Key, string De, string En)[] MediaTemplates =
        {
            ("standard", "Standard", "Standard"),
            ("compact", "Kompakt", "Compact"),
            ("minimal", "Minimal", "Minimal"),
            ("cover", "Großes Cover", "Large album art"),
        };

        /// <summary>Vorlage auf c anwenden: Aufbau, Elemente und Größen (die Farben bleiben). „standard“ = Ausgangszustand.</summary>
        public static void ApplyTemplate(AppConfig c, string key)
        {
            var d = Defaults;
            c.MediaCustomOrder = new List<string>(d.MediaCustomOrder);
            c.MediaCustomShowApp = d.MediaCustomShowApp;
            c.MediaCustomShowTimes = d.MediaCustomShowTimes;
            c.MediaCustomCover = d.MediaCustomCover;
            c.MediaCustomCoverSize = d.MediaCustomCoverSize;
            c.MediaCustomButtons = d.MediaCustomButtons;
            c.MediaCustomButtonsAlign = d.MediaCustomButtonsAlign;
            c.MediaCustomButtonSize = d.MediaCustomButtonSize;
            c.MediaCustomMorePos = d.MediaCustomMorePos;
            c.MediaCustomTitleSize = d.MediaCustomTitleSize;
            c.MediaCustomShowTitle = d.MediaCustomShowTitle;
            c.MediaCustomShowArtist = d.MediaCustomShowArtist;
            c.MediaCustomShowSources = d.MediaCustomShowSources;
            c.MediaCustomShowPrev = d.MediaCustomShowPrev;
            c.MediaCustomShowPlay = d.MediaCustomShowPlay;
            c.MediaCustomShowNext = d.MediaCustomShowNext;
            c.MediaShowProgress = d.MediaShowProgress;
            c.MediaSkipButtons = d.MediaSkipButtons;
            c.MediaShowMore = d.MediaShowMore;
            switch (key)
            {
                case "compact": // eine Zeile Titel mit kleinem Cover links, flache Knöpfe ohne ±10 s
                    c.MediaCustomShowApp = false;
                    c.MediaCustomCover = "left";
                    c.MediaCustomCoverSize = 48;
                    c.MediaCustomTitleSize = 13;
                    c.MediaCustomShowTimes = false;
                    c.MediaCustomButtons = "flat";
                    c.MediaCustomButtonSize = 90;
                    c.MediaSkipButtons = false;
                    break;
                case "minimal": // nur Titel, Interpret und Zurück/Play/Weiter
                    c.MediaCustomShowApp = false;
                    c.MediaCustomCover = "none";
                    c.MediaCustomButtons = "flat";
                    c.MediaCustomShowSources = false;
                    c.MediaShowProgress = false;
                    c.MediaSkipButtons = false;
                    c.MediaShowMore = false;
                    break;
                case "cover": // großes Cover oben, „…“ in der App-Zeile
                    c.MediaCustomCover = "top";
                    c.MediaCustomCoverSize = 140;
                    c.MediaCustomTitleSize = 16;
                    c.MediaCustomButtonSize = 110;
                    c.MediaCustomMorePos = "top";
                    break;
            }
        }

        /// <summary>Vorlagen als Kacheln mit echter Mini-Vorschau (Flyout mit angewendeter Vorlage und den eigenen Farben).</summary>
        public UIElement Templates()
        {
            var grid = new UniformGrid { Columns = MediaTemplates.Length };
            for (int i = 0; i < MediaTemplates.Length; i++)
            {
                var (key, de, en) = MediaTemplates[i];
                var sample = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(System.Text.Json.JsonSerializer.Serialize(_cfg)) ?? new AppConfig();
                ApplyTemplate(sample, key);
                FrameworkElement thumb;
                try { thumb = MediaOsd.CreatePreview(sample); }
                catch (Exception ex)
                {
                    Logger.Error($"Vorlagen-Vorschau: {ex.Message}");
                    thumb = new Border();
                }
                thumb.IsHitTestVisible = false;
                var content = new StackPanel();
                content.Children.Add(new Viewbox { Height = _compact ? 72 : 96, Stretch = Stretch.Uniform, Child = thumb });
                content.Children.Add(new TextBlock
                {
                    Text = T(de, en), FontSize = _compact ? 12 : 14, Foreground = Fg, HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, _compact ? 6 : 8, 0, 0),
                });
                var tile = new Border
                {
                    Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(_compact ? 6 : 10), Margin = new Thickness(0, 0, i < MediaTemplates.Length - 1 ? (_compact ? 6 : 8) : 0, 0),
                    Cursor = System.Windows.Input.Cursors.Hand, Child = content,
                    ToolTip = T("Als Startpunkt übernehmen", "Use as starting point"),
                };
                tile.MouseEnter += (_, _) => tile.Background = HoverBg;
                tile.MouseLeave += (_, _) => tile.Background = CardBg;
                tile.MouseLeftButtonUp += (_, _) =>
                {
                    ApplyTemplate(_cfg, key);
                    _changed();
                    _rebuild();
                };
                grid.Children.Add(tile);
            }
            return grid;
        }
    }
}
