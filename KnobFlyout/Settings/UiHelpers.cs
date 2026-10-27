using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KnobFlyout.Settings
{
    /// <summary>Kleine Fabrikmethoden, damit SettingsWindow.cs nicht in Boilerplate erstickt.</summary>
    internal static class UiHelpers
    {
        // ── Theme: "system" folgt wie ModernFlyouts dem Windows-Thema, sonst fest hell/dunkel ──
        // Die Brushes sind absichtlich NICHT readonly: SetTheme() tauscht sie beim Wechsel aus,
        // das Settings-Fenster baut sich danach neu auf (siehe SettingsWindow.ReopenWithTheme).
        public static bool Light { get; private set; }

        private static SolidColorBrush B(string hex) =>
            new((Color)ColorConverter.ConvertFromString(hex));

        public static Brush Fg = Brushes.White;
        public static Brush SubFg = Brushes.Gray;
        public static Brush CardBg = Brushes.Black;      // Einstellungs-Karten wie in den Windows-11-Einstellungen
        public static Brush CardBorder = Brushes.Black;
        public static Brush ButtonBorder = Brushes.Gray;
        public static Brush FieldBg = Brushes.Black;
        public static Brush FieldBorder = Brushes.Gray;
        public static Brush ContentBg = Brushes.Black;
        public static Brush NavBg = Brushes.Black;
        public static Brush NavSelectedBg = Brushes.Black;
        public static Brush NavHoverBg = Brushes.Black;
        public static readonly Brush Accent = B("#0078D4");
        public static Brush ButtonBg = Brushes.Gray;
        public static Brush HoverBg = Brushes.Gray;
        public static Brush PressedBg = Brushes.Gray;
        public static Brush ThumbBg = Brushes.Gray;
        public static Brush ThumbHoverBg = Brushes.Gray;
        public static Brush TrackBg = Brushes.Gray;

        static UiHelpers()
        {
            SetTheme(Config.ThemeHelper.IsLight(Config.ConfigManager.Load()));
        }

        /// <summary>Stellt alle Theme-Farben (und die Toggle-Vorlage) auf hell oder dunkel um.</summary>
        public static void SetTheme(bool light)
        {
            Light = light;
            // Werte angelehnt an die Windows-11-Einstellungen (Mica-Hintergrund, helle Karten darauf).
            // Hell bewusst etwas gedämpft (nicht reinweiß), damit es nicht blendet.
            Fg = light ? B("#1b1b1b") : B("#ffffff");
            SubFg = light ? B("#5c5c5c") : B("#c5c5c5");
            CardBg = light ? B("#F2F2F3") : B("#2b2b2b");
            CardBorder = light ? B("#D9D9DC") : B("#1d1d1d");
            ButtonBorder = light ? B("#CBCBCF") : B("#454545");
            FieldBg = light ? B("#F7F7F8") : B("#323232");
            FieldBorder = light ? B("#C6C6CA") : B("#454545");
            ContentBg = light ? B("#E6E6E8") : B("#202020");
            NavBg = ContentBg;
            NavSelectedBg = light ? B("#D9D9DC") : B("#2d2d2d");
            NavHoverBg = light ? B("#DEDEE1") : B("#292929");
            ButtonBg = light ? B("#F7F7F8") : B("#373737");
            HoverBg = light ? B("#E4E4E7") : B("#3c3c3c");
            PressedBg = light ? B("#D6D6DA") : B("#4a4a4a");
            ThumbBg = light ? B("#B4B4B4") : B("#606060");
            ThumbHoverBg = light ? B("#8E8E8E") : B("#8a8a8a");
            TrackBg = light ? B("#C2C2C2") : B("#5a5a5a");
            SwitchTemplate = BuildSwitchTemplate();
        }

        public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

        /// <summary>Seitentitel wie "Allgemein" in den Windows-11-Einstellungen.</summary>
        public static TextBlock PageTitle(string text) => new()
        {
            Text = text,
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
            Foreground = Fg,
            Margin = new Thickness(0, 0, 0, 4),
        };

        /// <summary>Zwischenüberschrift über einer Kartengruppe ("Verhalten", "Profile", ...).</summary>
        public static TextBlock SectionTitle(string text) => new()
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Fg,
            Margin = new Thickness(2, 16, 0, 6),
        };

        /// <summary>Kleiner Hinweistext unter einer Kartengruppe.</summary>
        public static TextBlock Hint(string text) => new()
        {
            Text = text,
            Foreground = SubFg,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 2, 0, 6),
        };

        // ── Einstellungs-Karten (SettingsCard / SettingsExpander der Windows-11-Einstellungen) ──

        private static Border CardShell(UIElement child) => new()
        {
            Background = CardBg,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 0, 3),
            Child = child,
        };

        /// <summary>Kopfzeile einer Karte: Symbol links, Titel + Beschreibung, Bedienelement rechts.</summary>
        private static Grid CardHeader(string glyph, string title, string? description, UIElement? control)
        {
            var grid = new Grid { MinHeight = 30 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new TextBlock
            {
                Text = glyph, FontFamily = IconFont, FontSize = 16, Foreground = Fg,
                VerticalAlignment = VerticalAlignment.Center,
            };
            grid.Children.Add(icon);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            text.Children.Add(new TextBlock { Text = title, FontSize = 14, Foreground = Fg, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrEmpty(description))
                text.Children.Add(new TextBlock { Text = description, FontSize = 12, Foreground = SubFg, TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            if (control is FrameworkElement fe)
            {
                fe.VerticalAlignment = VerticalAlignment.Center;
                if (fe.HorizontalAlignment == HorizontalAlignment.Stretch) fe.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(fe, 2);
                grid.Children.Add(fe);
            }
            return grid;
        }

        /// <summary>Einzeilige Einstellung: Symbol, Titel, Beschreibung und rechts das Bedienelement.</summary>
        public static Border SettingCard(string glyph, string title, string? description, UIElement? control)
        {
            var header = CardHeader(glyph, title, description, control);
            header.Margin = new Thickness(16, 9, 16, 9);
            return CardShell(header);
        }

        /// <summary>Karte mit Kopfzeile und darunter frei gestaltetem Inhalt (Listen, Felder, ...).</summary>
        public static Border ContentCard(string glyph, string title, string? description, UIElement content, UIElement? headerControl = null)
        {
            var panel = new StackPanel { Margin = new Thickness(16, 9, 16, 12) };
            panel.Children.Add(CardHeader(glyph, title, description, headerControl));
            if (content is FrameworkElement fe) fe.Margin = new Thickness(0, 10, 0, 0);
            panel.Children.Add(content);
            return CardShell(panel);
        }

        /// <summary>Aufklappbare Karte (SettingsExpander): Kopf mit Pfeil, Inhalt erst nach Klick sichtbar.</summary>
        public static Border ExpanderCard(string glyph, string title, string? description, UIElement content, bool expanded = false,
            UIElement? headerControl = null, Action<bool>? onExpandedChanged = null)
        {
            var chevron = new TextBlock { FontFamily = IconFont, FontSize = 12, Foreground = Fg, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            UIElement right = chevron;
            if (headerControl != null)
            {
                // z.B. ein Ein/Aus-Schalter neben dem Pfeil; dessen Klick klappt nicht mit auf (Event ist dann behandelt).
                var both = new StackPanel { Orientation = Orientation.Horizontal };
                both.Children.Add(headerControl);
                both.Children.Add(chevron);
                right = both;
            }
            var header = CardHeader(glyph, title, description, right);
            header.Margin = new Thickness(16, 9, 16, 9);

            var head = new Border { Child = header, Background = Brushes.Transparent, CornerRadius = new CornerRadius(8), Cursor = System.Windows.Input.Cursors.Hand };
            var body = new Border
            {
                BorderBrush = CardBorder, BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(48, 10, 16, 12), Child = content,
            };

            void Apply()
            {
                body.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                chevron.Text = expanded ? "\uE70E" : "\uE70D"; // ChevronUp / ChevronDown
            }
            Apply();
            head.MouseEnter += (_, _) => head.Background = HoverBg;
            head.MouseLeave += (_, _) => head.Background = Brushes.Transparent;
            head.MouseLeftButtonUp += (_, _) => { expanded = !expanded; Apply(); onExpandedChanged?.Invoke(expanded); };

            var panel = new StackPanel();
            panel.Children.Add(head);
            panel.Children.Add(body);
            return CardShell(panel);
        }

        /// <summary>Ein/Aus-Schalter für die rechte Kartenseite (Beschriftung "Ein"/"Aus" links daneben).</summary>
        public static CheckBox Toggle(bool value, Action<bool> onChange)
        {
            string On() => Loc.T("Ein", "On");
            string Off() => Loc.T("Aus", "Off");
            var cb = new CheckBox { IsChecked = value, Foreground = Fg, Content = value ? On() : Off(), Cursor = System.Windows.Input.Cursors.Hand };
            if (SwitchTemplate != null) cb.Template = SwitchTemplate;
            cb.Checked += (_, _) => { cb.Content = On(); onChange(true); };
            cb.Unchecked += (_, _) => { cb.Content = Off(); onChange(false); };
            return cb;
        }

        /// <summary>
        /// Slider für die rechte Kartenseite: aktueller Wert links, Slider rechts. format darf eine
        /// Einheit enthalten (z.B. "0' ms'"); tick > 0 rastet auf Vielfache davon ein.
        /// </summary>
        public static StackPanel SliderControl(double min, double max, double value, string format, Action<double> onChange,
            double tick = 0, double width = 180)
        {
            var val = new TextBlock
            {
                Text = value.ToString(format), Foreground = SubFg, MinWidth = 56, TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
            };
            var slider = new Slider { Minimum = min, Maximum = max, Value = value, Width = width, VerticalAlignment = VerticalAlignment.Center };
            if (tick > 0)
            {
                slider.TickFrequency = tick;
                slider.IsSnapToTickEnabled = true;
            }
            slider.ValueChanged += (_, e) =>
            {
                val.Text = e.NewValue.ToString(format);
                onChange(e.NewValue);
            };
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(val);
            panel.Children.Add(slider);
            return panel;
        }

        /// <summary>Flacher Symbol-Knopf (nur Glyph, Fläche erst beim Überfahren).</summary>
        public static Button FlatIconButton(string glyph, string tooltip, Action onClick)
        {
            var b = new Button
            {
                Content = glyph, FontFamily = IconFont, FontSize = 12,
                Width = 28, Height = 28, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = SubFg,
                ToolTip = tooltip, Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
            };
            b.MouseEnter += (_, _) => b.Background = HoverBg;
            b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
            b.Click += (_, _) => onClick();
            return b;
        }

        /// <summary>Kleiner ↺-Knopf "Auf Standard zurücksetzen".</summary>
        public static Button ResetButton(Action onClick) =>
            FlatIconButton("\uE7A7", Loc.T("Auf Standard zurücksetzen", "Reset to default"), onClick); // Undo

        /// <summary>Bedienelement mit vorangestelltem ↺-Knopf; reset setzt das Element auf den Standardwert.</summary>
        public static StackPanel WithReset(FrameworkElement control, Action reset)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(ResetButton(reset));
            control.VerticalAlignment = VerticalAlignment.Center;
            panel.Children.Add(control);
            return panel;
        }

        /// <summary>SliderControl mit ↺-Knopf (setzt den Slider und damit auch den Config-Wert auf def).</summary>
        public static StackPanel ResetSlider(StackPanel sliderControl, double def) =>
            WithReset(sliderControl, () =>
            {
                foreach (var child in sliderControl.Children)
                    if (child is Slider s) s.Value = def;
            });

        /// <summary>Auswahlliste für die rechte Kartenseite.</summary>
        public static ComboBox Combo(double width, int selectedIndex, params string[] items)
        {
            var combo = new ComboBox { Width = width };
            foreach (var i in items) combo.Items.Add(i);
            combo.SelectedIndex = selectedIndex;
            return combo;
        }

        public static TextBlock Label(string text) => new()
        {
            Text = text,
            Foreground = Fg,
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 3),
        };

        public static TextBox TextField(string text = "", double width = double.NaN) => new()
        {
            Text = text,
            Width = width,
            Background = FieldBg,
            Foreground = Fg,
            BorderBrush = FieldBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 0, 6),
        };

        // Toggle-Schalter wie in den Windows-Einstellungen ("Ein"/"Aus" links, Schalter rechts). Wird als
        // XAML-Vorlage zur Laufzeit geparst; schlägt das fehl, bleibt es still beim normalen CheckBox-Look.
        private static ControlTemplate? SwitchTemplate;

        private static ControlTemplate? BuildSwitchTemplate()
        {
            try
            {
                string off = Light ? "#6b6b6b" : "#c8c8c8";
                string xaml =
                    "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"CheckBox\">" +
                    "<StackPanel Orientation=\"Horizontal\" Background=\"Transparent\">" +
                    "<ContentPresenter VerticalAlignment=\"Center\" Margin=\"0,0,12,0\"/>" +
                    "<Grid Width=\"40\" Height=\"20\" VerticalAlignment=\"Center\">" +
                    $"<Border x:Name=\"Track\" CornerRadius=\"10\" BorderThickness=\"1\" BorderBrush=\"{off}\" Background=\"Transparent\"/>" +
                    $"<Ellipse x:Name=\"Thumb\" Width=\"12\" Height=\"12\" Fill=\"{off}\" HorizontalAlignment=\"Left\" Margin=\"4,0,0,0\"/>" +
                    "</Grid>" +
                    "</StackPanel>" +
                    "<ControlTemplate.Triggers>" +
                    "<Trigger Property=\"IsChecked\" Value=\"True\">" +
                    "<Setter TargetName=\"Track\" Property=\"Background\" Value=\"#0078D4\"/>" +
                    "<Setter TargetName=\"Track\" Property=\"BorderBrush\" Value=\"#0078D4\"/>" +
                    "<Setter TargetName=\"Thumb\" Property=\"Fill\" Value=\"White\"/>" +
                    "<Setter TargetName=\"Thumb\" Property=\"HorizontalAlignment\" Value=\"Right\"/>" +
                    "<Setter TargetName=\"Thumb\" Property=\"Margin\" Value=\"0,0,4,0\"/>" +
                    "</Trigger>" +
                    "</ControlTemplate.Triggers>" +
                    "</ControlTemplate>";
                return System.Windows.Markup.XamlReader.Parse(xaml) as ControlTemplate;
            }
            catch (Exception ex)
            {
                KnobFlyout.Logger.Error($"Toggle-Vorlage: {ex.Message}");
                return null;
            }
        }


        public static Button Btn(string text, double width = double.NaN) => new()
        {
            Content = text,
            Width = width,
            Background = ButtonBg,
            Foreground = Fg,
            BorderBrush = ButtonBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 6, 0),
        };

        /// <summary>Hervorgehobener Knopf in Akzentfarbe (z.B. "Speichern").</summary>
        public static Button AccentBtn(string text, double width = double.NaN)
        {
            var b = Btn(text, width);
            b.Background = Accent;
            b.BorderBrush = Accent;
            b.Foreground = Brushes.White;
            return b;
        }

        public static StackPanel Row(params UIElement[] children)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            foreach (var c in children) p.Children.Add(c);
            return p;
        }

        /// <summary>Einfacher Listeneditor: TextBox + "Hinzufügen" + ListBox mit "Entfernen" je Zeile.</summary>
        public static StackPanel ListEditor(List<string> model, string placeholder, Func<List<string>>? liveSource = null)
        {
            var outer = new StackPanel();
            var listBox = new ListBox { Height = 120, Background = FieldBg, Foreground = Fg, BorderBrush = FieldBorder, Margin = new Thickness(0, 0, 0, 6) };
            foreach (var item in model) listBox.Items.Add(item);

            var input = TextField(width: 220);
            input.Margin = new Thickness(0, 0, 6, 0);
            InputHint(input, placeholder);

            var addBtn = Btn(Loc.T("Hinzufügen", "Add"));
            addBtn.Click += (_, _) =>
            {
                var v = input.Text.Trim();
                if (v.Length == 0 || model.Contains(v)) return;
                model.Add(v);
                listBox.Items.Add(v);
                input.Text = "";
            };

            var removeBtn = Btn(Loc.T("Entfernen", "Remove"));
            removeBtn.Click += (_, _) =>
            {
                if (listBox.SelectedItem is not string sel) return;
                model.Remove(sel);
                listBox.Items.Remove(sel);
            };

            var row = Row(input, addBtn, removeBtn);

            outer.Children.Add(listBox);
            outer.Children.Add(row);

            if (liveSource != null)
            {
                var liveBtn = Btn(Loc.T("Aus laufenden Apps …", "From running apps …"));
                liveBtn.HorizontalAlignment = HorizontalAlignment.Left;
                liveBtn.Click += (_, _) =>
                {
                    var apps = liveSource();
                    var menu = new ContextMenu();
                    foreach (var app in apps)
                    {
                        var mi = new MenuItem { Header = app };
                        mi.Click += (_, _) =>
                        {
                            if (model.Contains(app)) return;
                            model.Add(app);
                            listBox.Items.Add(app);
                        };
                        menu.Items.Add(mi);
                    }
                    if (menu.Items.Count == 0)
                        menu.Items.Add(new MenuItem { Header = Loc.T("(keine Apps mit Ton gefunden)", "(no apps playing sound)"), IsEnabled = false });
                    menu.PlacementTarget = liveBtn;
                    menu.IsOpen = true;
                };
                outer.Children.Add(liveBtn);
            }

            return outer;
        }

        public static Brush FromHex(string hex, string fallback = "#2a2a3a")
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)); }
        }

        /// <summary>Platzhaltertext als ToolTip, da WPF TextBox keinen nativen Platzhalter kennt.</summary>
        private static void InputHint(TextBox box, string hint)
        {
            box.ToolTip = hint;
        }
    }
}
