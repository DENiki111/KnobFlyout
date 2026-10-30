using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Baukasten für das gewählte Flyout-Design: Vorlagen als Startpunkt, alle Bereiche in der Reihenfolge des Flyouts
    /// (Ein/Aus, ↑/↓, aufgeklappt die Elemente als Kacheln und die Feineinstellungen) und unter einem Namen gespeicherte
    /// Designs. Genutzt von der Einstellungs-Unterseite und von der Einführung. Ändert cfg direkt und meldet jede
    /// Änderung über changed; Vorlage oder gespeichertes Design über rebuild (die Schalter müssen dann die neuen Werte zeigen).
    /// </summary>
    internal sealed class MediaDesignEditor
    {
        private readonly AppConfig _cfg;
        private readonly Action _changed;
        private readonly Action _rebuild;
        private readonly Action? _savedChanged;
        private readonly HashSet<string> _expanded;
        private readonly bool _compact; // schmale Spalte: kürzere Karten, kleinere Kacheln und Slider

        /// <param name="savedChanged">Die Liste der gespeicherten Designs hat sich geändert (Einstellungen speichern sie sofort).</param>
        public MediaDesignEditor(AppConfig cfg, HashSet<string> expanded, Action changed, Action rebuild, bool compact = false, Action? savedChanged = null)
        {
            _cfg = cfg;
            _expanded = expanded;
            _changed = changed;
            _rebuild = rebuild;
            _compact = compact;
            _savedChanged = savedChanged;
        }

        private double SliderW => _compact ? 110 : 180;
        private double ComboW => _compact ? 130 : 160;

        private string Design => _cfg.MediaDesign;
        private MediaDesignInfo Info => MediaDesignInfo.For(Design);
        private MediaLayout Layout => _cfg.Layout();

        /// <summary>Werte der gewählten Vorlage: Darauf setzt ↺ zurück.</summary>
        private MediaLayout Base => MediaLayout.FromTemplate(Layout.Template);

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
                var movable = Info.Movable(Layout);
                var display = Info.Display(Layout);
                // Schmale Spalte (Einführung): aufgeklappt steht nur dieser Bereich da, sonst passt es nicht ohne Scrollen.
                string? only = _compact ? display.Find(_expanded.Contains) : null;
                foreach (var key in display)
                {
                    if (only != null && key != only) continue;
                    elements.Children.Add(BlockCard(key, movable, only != null, Render));
                }
            }
            Render();
            return elements;
        }

        /// <summary>Ein Bereich des Flyouts als Karte: ↑/↓ und ggf. Ein/Aus im Kopf, aufgeklappt seine Elemente und Einstellungen.</summary>
        private Border BlockCard(string key, List<string> movable, bool alone, Action render)
        {
            var layout = Layout;
            var info = Info;
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            int index = movable.IndexOf(key);
            if (index >= 0 && !alone) // fest eingebaute Bereiche und allein sichtbare lassen sich nicht verschieben
            {
                void Move(int delta)
                {
                    int to = index + delta;
                    if (to < 0 || to >= movable.Count) return;
                    (movable[index], movable[to]) = (movable[to], movable[index]);
                    layout.Order = movable.SequenceEqual(info.Order) ? null : new List<string>(movable);
                    _changed();
                    render();
                }
                var up = FlatIconButton("\uE70E", T("Nach oben", "Move up"), () => Move(-1));     // ChevronUp
                var down = FlatIconButton("\uE70D", T("Nach unten", "Move down"), () => Move(+1)); // ChevronDown
                up.IsEnabled = index > 0;
                down.IsEnabled = index < movable.Count - 1;
                up.Margin = down.Margin = new Thickness(0, 0, 2, 0);
                head.Children.Add(up);
                head.Children.Add(down);
            }
            void Switch(string element)
            {
                var toggle = Toggle(layout.Shows(element), Set<bool>(v => layout.Set(element, v)));
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
                    desc = Design == "island" ? T("Logo am Cover und Name hinter dem Interpreten", "Logo on the album art and name after the artist")
                        : Design == "hardware" ? T("Name der App im Display", "Name of the app on the display")
                        : T("Name und Symbol der App, die gerade spielt", "Name and icon of the app that is playing");
                    Switch(MediaLayout.App);
                    if (Design == "custom")
                        body.Children.Add(OptionRow(T("„…“-Knopf rechts in dieser Zeile (statt bei den Knöpfen)", "“…” button on the right of this row (instead of with the buttons)"),
                            Toggle(layout.MorePos == "top", Set<bool>(v => layout.MorePos = v ? "top" : null))));
                    else body = null;
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
                    Switch(MediaLayout.Progress);
                    body.Children.Add(OptionRow(T("Zeiten", "Times"),
                        Toggle(layout.Shows(MediaLayout.Times), Set<bool>(v => layout.Set(MediaLayout.Times, v)))));
                    break;
                case "sources":
                    glyph = "\uE8AB"; // Switch
                    title = T("Quellen-Leiste", "Sources bar");
                    desc = T("Im Betrieb nur sichtbar, wenn mehrere Apps gleichzeitig Medien melden", "In use only visible when several apps report media at once");
                    Switch(MediaLayout.Sources);
                    body.Children.Add(OptionRow(T("Pause-Zeichen neben spielenden Quellen", "Pause symbol next to playing sources"),
                        Toggle(layout.Shows(MediaLayout.SourcePause), Set<bool>(v => layout.Set(MediaLayout.SourcePause, v)))));
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
                open =>
                {
                    if (_compact) _expanded.Clear();
                    if (open) _expanded.Add(key); else _expanded.Remove(key);
                    if (_compact) head.Dispatcher.BeginInvoke(new Action(render));
                });
        }

        /// <summary>Titel/Interpret/Cover als Kacheln, dazu Platz und Größe des Covers und die Titelgröße.</summary>
        private void AddInfoOptions(StackPanel body)
        {
            var layout = Layout;
            var info = Info;
            var template = Base;
            string[] covers = { "right", "left", "top" };
            string baseCover = template.Cover is { } c && c != "none" ? c : info.Cover;
            string current = layout.Cover ?? info.Cover;
            string coverPos = Array.IndexOf(covers, current) >= 0 ? current : baseCover; // bei „aus“: Platz fürs Wiedereinschalten
            var tiles = new WrapPanel();
            tiles.Children.Add(ElementTile("\uE8D2", T("Titel", "Title"), layout.Shows(MediaLayout.Title), Set<bool>(v => layout.Set(MediaLayout.Title, v))));    // Font
            tiles.Children.Add(ElementTile("\uE77B", T("Interpret", "Artist"), layout.Shows(MediaLayout.Artist), Set<bool>(v => layout.Set(MediaLayout.Artist, v)))); // Contact
            tiles.Children.Add(ElementTile("\uE91B", "Cover", current != "none", Set<bool>(v => layout.Cover = v ? coverPos : "none"))); // Photo
            body.Children.Add(tiles);

            var coverCombo = Combo(ComboW, Array.IndexOf(covers, coverPos), T("Rechts", "Right"), T("Links", "Left"), T("Oben", "Top"));
            coverCombo.SelectionChanged += (_, _) =>
            {
                coverPos = covers[Math.Max(0, coverCombo.SelectedIndex)];
                if ((layout.Cover ?? info.Cover) != "none") layout.Cover = coverPos;
                _changed();
            };
            body.Children.Add(OptionRow(T("Platz des Covers", "Album art position"),
                WithReset(coverCombo, () => coverCombo.SelectedIndex = Array.IndexOf(covers, baseCover))));
            body.Children.Add(OptionRow(T("Größe des Covers", "Album art size"),
                ResetSlider(SliderControl(32, 140, Math.Clamp(layout.CoverSize > 0 ? layout.CoverSize : info.CoverSize, 32, 140), "0' px'", Set<double>(v => layout.CoverSize = (int)v), tick: 2, width: SliderW),
                    template.CoverSize > 0 ? template.CoverSize : info.CoverSize)));
            body.Children.Add(OptionRow(T("Titelgröße", "Title size"),
                ResetSlider(SliderControl(11, 22, Math.Clamp(layout.TitleSize > 0 ? layout.TitleSize : info.TitleSize, 11, 22), "0' pt'", Set<double>(v => layout.TitleSize = (int)v), tick: 1, width: SliderW),
                    template.TitleSize > 0 ? template.TitleSize : info.TitleSize)));
        }

        /// <summary>Jeder Knopf als Kachel (in der Reihenfolge des Flyouts), dazu was das Design an Stil, Ausrichtung und Größe zulässt.</summary>
        private void AddButtonOptions(StackPanel body)
        {
            var layout = Layout;
            var info = Info;
            var template = Base;
            var tiles = new WrapPanel();
            tiles.Children.Add(ElementTile("\uE892", T("Zurück", "Previous"), layout.Shows(MediaLayout.Prev), Set<bool>(v => layout.Set(MediaLayout.Prev, v)))); // Previous
            tiles.Children.Add(ElementTile("\uED3C", "±10 s", layout.Shows(MediaLayout.Skip), Set<bool>(v => layout.Set(MediaLayout.Skip, v))));                 // SkipBack10
            tiles.Children.Add(ElementTile("\uE768", "Play/Pause", layout.Shows(MediaLayout.Play), Set<bool>(v => layout.Set(MediaLayout.Play, v))));            // Play
            tiles.Children.Add(ElementTile("\uE893", T("Weiter", "Next"), layout.Shows(MediaLayout.Next), Set<bool>(v => layout.Set(MediaLayout.Next, v))));     // Next
            tiles.Children.Add(ElementTile("\uE712", T("„…“-Menü", "“…” menu"), layout.Shows(MediaLayout.More), Set<bool>(v => layout.Set(MediaLayout.More, v)))); // More
            body.Children.Add(tiles);

            if (info.ButtonStyle)
            {
                string[] styles = { "round", "flat" };
                string flatDefault = info.FlatDefault(Design);
                var styleCombo = Combo(ComboW, Math.Max(0, Array.IndexOf(styles, layout.Buttons ?? flatDefault)), T("Rund", "Round"), T("Flach", "Flat"));
                styleCombo.SelectionChanged += (_, _) => { layout.Buttons = styles[Math.Max(0, styleCombo.SelectedIndex)]; _changed(); };
                body.Children.Add(OptionRow(T("Stil", "Style"),
                    WithReset(styleCombo, () => styleCombo.SelectedIndex = Math.Max(0, Array.IndexOf(styles, template.Buttons ?? flatDefault)))));
            }
            if (info.Alignment)
            {
                string[] aligns = { "center", "left", "right" };
                var alignCombo = Combo(ComboW, Math.Max(0, Array.IndexOf(aligns, layout.ButtonsAlign ?? info.Align)), T("Mittig", "Center"), T("Links", "Left"), T("Rechts", "Right"));
                alignCombo.SelectionChanged += (_, _) => { layout.ButtonsAlign = aligns[Math.Max(0, alignCombo.SelectedIndex)]; _changed(); };
                body.Children.Add(OptionRow(T("Ausrichtung", "Alignment"),
                    WithReset(alignCombo, () => alignCombo.SelectedIndex = Math.Max(0, Array.IndexOf(aligns, template.ButtonsAlign ?? info.Align)))));
            }
            body.Children.Add(OptionRow(T("Größe", "Size"),
                ResetSlider(SliderControl(70, 140, Math.Clamp(layout.ButtonSize > 0 ? layout.ButtonSize : 100, 70, 140), "0' %'", Set<double>(v => layout.ButtonSize = (int)v), tick: 5, width: SliderW),
                    template.ButtonSize > 0 ? template.ButtonSize : 100)));
            if (Design != "custom") // beim eigenen Design steht das bei der App-Zeile
                body.Children.Add(OptionRow(T("„…“-Knopf oben rechts (statt bei den Knöpfen)", "“…” button at the top right (instead of with the buttons)"),
                    Toggle((layout.MorePos ?? info.MorePos) == "top", Set<bool>(v => layout.MorePos = v ? "top" : "controls"))));
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

        private static string TemplateName(string key) => key switch
        {
            "compact" => T("Kompakt", "Compact"),
            "minimal" => T("Minimal", "Minimal"),
            "cover" => T("Großes Cover", "Large album art"),
            _ => T("Standard", "Standard"),
        };

        /// <summary>Vorlage auf das gewählte Design von c anwenden: Aufbau, Elemente und Größen; Farben, Deckkraft und das Pause-Zeichen bleiben.</summary>
        public static void ApplyTemplate(AppConfig c, string key)
        {
            var old = c.Layout();
            var layout = MediaLayout.FromTemplate(key);
            layout.Colors = old.Colors;
            layout.Set(MediaLayout.SourcePause, old.Shows(MediaLayout.SourcePause));
            c.MediaLayouts[c.MediaDesign] = layout;
        }

        /// <summary>Vorlagen als Kacheln mit echter Mini-Vorschau im gewählten Design; die aktive ist markiert.</summary>
        public UIElement Templates()
        {
            var keys = MediaLayout.TemplateKeys;
            string active = Layout.Template ?? "standard";
            var grid = new UniformGrid { Columns = keys.Length };
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                bool on = key == active;
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
                    Text = TemplateName(key), FontSize = _compact ? 12 : 14, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Fg,
                    HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, _compact ? 6 : 8, 0, 0),
                });
                double pad = _compact ? 6 : 10;
                var tile = new Border
                {
                    Background = CardBg, BorderBrush = on ? Accent : CardBorder, BorderThickness = new Thickness(on ? 2 : 1), CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(on ? pad - 1 : pad), Margin = new Thickness(0, 0, i < keys.Length - 1 ? (_compact ? 6 : 8) : 0, 0),
                    Cursor = System.Windows.Input.Cursors.Hand, Child = content,
                    ToolTip = T("Als Startpunkt übernehmen", "Use as starting point"),
                };
                tile.MouseEnter += (_, _) => tile.Background = HoverBg;
                tile.MouseLeave += (_, _) => tile.Background = CardBg;
                tile.MouseLeftButtonUp += (_, _) =>
                {
                    ApplyTemplate(_cfg, key);
                    _cfg.MediaSavedActive = "";
                    _changed();
                    _rebuild();
                };
                grid.Children.Add(tile);
            }
            return grid;
        }

        // ── Farben ──────────────────────────────────────────────────────────

        /// <summary>
        /// Eigene Farben des gewählten Designs. Der Wert im Design (auch über ↺) heißt: keine eigene Farbe.
        /// Breite Spalte einzeilig mit Hex-Feld, schmale Spalte zweispaltig ohne.
        /// </summary>
        /// <param name="boxes">Hex-Felder für die Farbprofile der Einstellungen.</param>
        public UIElement DesignColors(Dictionary<string, TextBox>? boxes = null)
        {
            var layout = Layout;
            var panel = new StackPanel();
            if (MediaDesignColors.CoverColor(_cfg) is { } cover)
            {
                string last = layout.ColorOf(cover.Key) ?? cover.Default;
                var field = ColorRow(cover.Label, last, cover.Default, hex =>
                {
                    last = hex;
                    layout.SetColor(cover.Key, hex);
                    _changed();
                }, !_compact);
                field.Visibility = layout.ColorOf(cover.Key) != null ? Visibility.Visible : Visibility.Collapsed;
                panel.Children.Add(OptionRow(cover.Toggle, Toggle(layout.ColorOf(cover.Key) == null, Set<bool>(fromCover =>
                {
                    layout.SetColor(cover.Key, fromCover ? null : last);
                    field.Visibility = fromCover ? Visibility.Collapsed : Visibility.Visible;
                }))));
                panel.Children.Add(field);
            }

            Panel fields = _compact ? new UniformGrid { Columns = 2 } : new StackPanel();
            foreach (var (key, label, def) in MediaDesignColors.For(_cfg))
            {
                var row = ColorRow(label, layout.ColorOf(key) ?? def, def, hex =>
                {
                    layout.SetColor(key, string.Equals(hex, def, StringComparison.OrdinalIgnoreCase) ? null : hex);
                    _changed();
                }, !_compact);
                if (boxes != null && row.Tag is TextBox box) boxes["media_color_" + key] = box;
                fields.Children.Add(row);
            }
            panel.Children.Add(fields);
            return panel;
        }

        /// <summary>Farbzeile: Name, auf Wunsch das Hex-Feld (steht dann in Tag), Farbfeld (Klick = Farbkreis), ↺.</summary>
        internal static Grid ColorRow(string label, string value, string def, Action<string> set, bool hexField = false)
        {
            string current = value;
            var row = new Grid { Height = 32, Margin = new Thickness(0, 0, hexField ? 0 : 10, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = Fg, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });

            var swatch = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(6), BorderBrush = FieldBorder, BorderThickness = new Thickness(1),
                Background = FromHex(value), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = T("Farbe auswählen", "Pick a color"), VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(hexField ? 8 : 0, 0, 0, 0),
            };
            TextBox? box = null;
            void Apply(string hex)
            {
                if (box != null && box.Text != hex)
                {
                    box.Text = hex; // TextChanged kommt hierher zurück
                    return;
                }
                current = hex;
                swatch.Background = FromHex(hex, value);
                set(hex);
            }
            if (hexField)
            {
                box = TextField(value, 100);
                box.Margin = new Thickness(0);
                box.VerticalAlignment = VerticalAlignment.Center;
                box.TextChanged += (_, _) => Apply(box.Text.Trim());
                Grid.SetColumn(box, 1);
                row.Children.Add(box);
                row.Tag = box;
            }
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                var picker = new ColorWheelPopup(swatch, current, Apply);
                row.Children.Add(picker); // im Fensterbaum, damit die Theme-Stile greifen
                picker.Closed += (_, _) => row.Children.Remove(picker);
                picker.IsOpen = true;
            };
            Grid.SetColumn(swatch, 2);
            row.Children.Add(swatch);

            var reset = ResetButton(() => Apply(def));
            reset.Margin = new Thickness(2, 0, 0, 0);
            reset.Width = reset.Height = 24;
            reset.Focusable = false;
            Grid.SetColumn(reset, 3);
            row.Children.Add(reset);
            return row;
        }

        // ── Gespeicherte Designs ────────────────────────────────────────────

        /// <summary>Eigene Designs als Pillen (Klick wendet an, ✕ löscht) und das Speichern des aktuellen Stands unter einem Namen.</summary>
        public UIElement SavedDesigns()
        {
            var panel = new StackPanel();
            var saved = _cfg.MediaSavedDesigns;
            if (saved.Count > 0)
            {
                var chips = new WrapPanel { Margin = new Thickness(0, 0, -6, 0) };
                foreach (var design in saved)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 6) };
                    row.Children.Add(Chip(design.Name, design.Name == _cfg.MediaSavedActive, () =>
                    {
                        design.ApplyTo(_cfg);
                        _changed();
                        _rebuild();
                    }, check: true));
                    var remove = FlatIconButton("\uE711", T($"„{design.Name}“ löschen", $"Delete “{design.Name}”"), () =>
                    {
                        saved.Remove(design);
                        if (_cfg.MediaSavedActive == design.Name) _cfg.MediaSavedActive = "";
                        _savedChanged?.Invoke();
                        _changed();
                        _rebuild();
                    });
                    remove.Width = remove.Height = 24;
                    remove.FontSize = 10;
                    remove.Margin = new Thickness(2, 0, 0, 0);
                    row.Children.Add(remove);
                    chips.Children.Add(row);
                }
                panel.Children.Add(new ScrollViewer { MaxHeight = _compact ? 68 : 120, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = chips, Focusable = false, Margin = new Thickness(0, 0, 0, 4) });
            }

            var name = TextField(_cfg.MediaSavedActive);
            name.MaxLength = 32;
            InputHint(name, T("Name, z. B. Mein Island", "Name, e.g. My Island"));
            var save = AccentBtn(T("Als eigenes Design speichern", "Save as own design"));
            save.Margin = new Thickness(8, 0, 0, 0);
            void Save()
            {
                string text = name.Text.Trim();
                if (text.Length == 0)
                {
                    name.Focus();
                    return;
                }
                var entry = SavedMediaDesign.From(_cfg, text);
                int index = saved.FindIndex(s => string.Equals(s.Name, text, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) saved[index] = entry;
                else saved.Add(entry);
                _cfg.MediaSavedActive = text;
                _savedChanged?.Invoke();
                _changed();
                _rebuild();
            }
            save.Click += (_, _) => Save();
            name.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Enter) return;
                e.Handled = true;
                Save();
            };
            var saveRow = new Grid();
            saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            saveRow.Children.Add(name);
            Grid.SetColumn(save, 1);
            saveRow.Children.Add(save);
            panel.Children.Add(saveRow);
            panel.Children.Add(Hint(T("Speichert Aufbau, Deckkraft, Ecken und Farben. Das Design steht danach auch in der Design-Auswahl. Gleicher Name überschreibt.",
                "Saves the layout, opacity, corners and colors. The design then also appears in the design list. The same name overwrites.")));
            return panel;
        }
    }
}
