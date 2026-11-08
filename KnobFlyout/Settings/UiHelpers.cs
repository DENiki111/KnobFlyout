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
        public static Brush Accent = B("#0078D4");
        public static string AccentHex = "#0078D4"; // für Vorlagen aus XAML-Text (Schalter, Häkchen)

        /// <summary>Aussehen der Einstellungen: "classic", "midnight" oder "tiles" (siehe ApplyTheme).</summary>
        public static string Design { get; private set; } = "classic";
        /// <summary>Farbkacheln: Kartensymbole stehen in einer Kachel, Farbe aus der Ressource "PageTile" (je Seite).</summary>
        public static bool IconTiles => Design == "tiles";
        public static double CardRadius = 8;
        /// <summary>Fensterhintergrund (Mitternacht: Verlauf) und Hintergrund der angedockten Leisten (Mitternacht: durchsichtig).</summary>
        public static Brush WindowBg = Brushes.Black;
        public static Brush PaneBg = Brushes.Black;
        private static Color _fadeTop = Colors.Black;
        public static Brush ButtonBg = Brushes.Gray;
        public static Brush HoverBg = Brushes.Gray;
        public static Brush PressedBg = Brushes.Gray;
        public static Brush ThumbBg = Brushes.Gray;
        public static Brush ThumbHoverBg = Brushes.Gray;
        public static Brush TrackBg = Brushes.Gray;

        static UiHelpers()
        {
            ApplyTheme(Config.ConfigManager.Load());
        }

        /// <summary>Theme (hell/dunkel) und Design der Einstellungen aus der Config übernehmen.</summary>
        public static void ApplyTheme(Config.AppConfig cfg) =>
            SetTheme(Config.ThemeHelper.IsLight(cfg), cfg.SettingsDesign, cfg.SettingsFadeTop, cfg.SettingsFadeBottom);

        private static Color Hex(string? hex, string fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(hex) ? fallback : hex.Trim()); }
            catch { return (Color)ColorConverter.ConvertFromString(fallback); }
        }

        private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
            (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

        private static double Lum(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

        private static SolidColorBrush C(Color c) => new(c);

        /// <summary>Stellt alle Theme-Farben (und die Toggle-Vorlage) auf hell oder dunkel und das gewählte Design um.</summary>
        public static void SetTheme(bool light, string design = "classic", string? fadeTop = null, string? fadeBottom = null)
        {
            Design = design is "midnight" or "tiles" ? design : "classic";
            if (Design == "midnight")
                SetMidnight(Hex(fadeTop, Config.AppConfig.DefaultFadeTop), Hex(fadeBottom, Config.AppConfig.DefaultFadeBottom));
            else
                SetClassic(light);
            SwitchTemplate = BuildSwitchTemplate(true);
            SwitchTemplateStill = BuildSwitchTemplate(false);
        }

        private static void SetAccent(Color c)
        {
            Accent = C(c);
            AccentHex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        /// <summary>
        /// Mitternacht: Hintergrund verläuft von oben (Farbe 1, dazu ein Lichtschein) nach unten (Farbe 2). Karten, Felder
        /// und Navigation werden daraus abgeleitet; Akzent = Farbe 1, aufgehellt. Ist Farbe 2 hell, wird alles hell.
        /// </summary>
        private static void SetMidnight(Color top, Color bottom)
        {
            _fadeTop = top;
            bool light = Lum(bottom) > 0.6;
            Light = light;
            CardRadius = 10;
            var w = Colors.White;
            var k = Colors.Black;
            if (!light)
            {
                var card = Mix(Mix(bottom, top, 0.07), w, 0.02);
                Fg = C(Color.FromRgb(0xEE, 0xF1, 0xF8));
                SubFg = C(Mix(Color.FromRgb(0xA4, 0xA8, 0xB4), top, 0.22));
                CardBg = C(card);
                CardBorder = C(Mix(card, w, 0.05));
                ButtonBorder = C(Mix(card, w, 0.14));
                FieldBg = C(Mix(bottom, top, 0.05));
                FieldBorder = C(Mix(card, w, 0.14));
                ContentBg = C(Mix(bottom, top, 0.06));
                NavBg = C(Mix(bottom, k, 0.3));
                NavSelectedBg = C(Mix(bottom, top, 0.22));
                NavHoverBg = C(Mix(bottom, top, 0.1));
                ButtonBg = C(Mix(card, w, 0.05));
                HoverBg = C(Mix(card, w, 0.1));
                PressedBg = C(Mix(card, w, 0.16));
                ThumbBg = C(Mix(card, w, 0.25));
                ThumbHoverBg = C(Mix(card, w, 0.4));
                TrackBg = C(Mix(card, w, 0.18));
            }
            else
            {
                var card = Mix(bottom, w, 0.75);
                Fg = C(Color.FromRgb(0x1B, 0x1B, 0x1F));
                SubFg = C(Color.FromRgb(0x5E, 0x5E, 0x66));
                CardBg = C(card);
                CardBorder = C(Mix(card, k, 0.08));
                ButtonBorder = C(Mix(card, k, 0.15));
                FieldBg = C(Mix(bottom, w, 0.85));
                FieldBorder = C(Mix(card, k, 0.15));
                ContentBg = C(Mix(bottom, w, 0.5));
                NavBg = C(Mix(bottom, w, 0.35));
                NavSelectedBg = C(Mix(bottom, w, 0.8));
                NavHoverBg = C(Mix(bottom, w, 0.6));
                ButtonBg = C(Mix(bottom, w, 0.85));
                HoverBg = C(Mix(card, k, 0.05));
                PressedBg = C(Mix(card, k, 0.1));
                ThumbBg = C(Color.FromRgb(0xB0, 0xB0, 0xB8));
                ThumbHoverBg = C(Color.FromRgb(0x8E, 0x8E, 0x96));
                TrackBg = C(Color.FromRgb(0xCF, 0xCF, 0xD6));
            }
            // Akzent: Farbe 1 kräftig genug für Schalter und Regler (graue Farbe → Blau)
            var (h, s) = HueSat(top);
            SetAccent(s < 0.12 ? (light ? Color.FromRgb(0x00, 0x67, 0xC0) : Color.FromRgb(0x5B, 0x8C, 0xFF)) : FromHsl(h, Math.Clamp(s, 0.6, 0.95), light ? 0.42 : 0.66));
            WindowBg = new LinearGradientBrush(Mix(bottom, top, light ? 0.18 : 0.12), bottom, 90);
            PaneBg = Brushes.Transparent;
        }

        /// <summary>Farbton (0–360) und HSL-Sättigung (0–1).</summary>
        private static (double H, double S) HueSat(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min, l = (max + min) / 2;
            if (d < 1e-6) return (0, 0);
            double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            return (h * 60, s);
        }

        private static Color FromHsl(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - c / 2;
            (double r, double g, double b) = h < 60 ? (c, x, 0d) : h < 120 ? (x, c, 0d) : h < 180 ? (0d, c, x) : h < 240 ? (0d, x, c) : h < 300 ? (x, 0d, c) : (c, 0d, x);
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }

        /// <summary>Mitternacht: Lichtschein in Farbe 1 oben über dem Verlauf (hinter allem, fängt keine Maus). Sonst null.</summary>
        public static FrameworkElement? Backdrop()
        {
            if (Design != "midnight") return null;
            var g = new Grid { IsHitTestVisible = false, ClipToBounds = true };
            g.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 1000, Height = 520, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(220, -300, 0, 0),
                Fill = new RadialGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb((byte)(Light ? 0x55 : 0x40), _fadeTop.R, _fadeTop.G, _fadeTop.B), 0),
                    new GradientStop(Colors.Transparent, 1),
                }),
            });
            return g;
        }

        private static void SetClassic(bool light)
        {
            Light = light;
            CardRadius = 8;
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
            SetAccent(Color.FromRgb(0x00, 0x78, 0xD4));
            if (Design == "tiles")
            {
                // Farbkacheln: statisch (kein Verlauf), ruhige neutrale Flächen, Karten ohne sichtbaren Rand, runder
                CardRadius = 12;
                Fg = light ? B("#1C1C1F") : B("#F2F2F4");
                SubFg = light ? B("#6B6B73") : B("#A1A1A8");
                CardBg = light ? B("#FFFFFF") : B("#1F1F23");
                CardBorder = light ? B("#E9E9EE") : B("#232327");
                ButtonBorder = light ? B("#DCDCE2") : B("#34343A");
                FieldBg = light ? B("#F4F4F7") : B("#2A2A2F");
                FieldBorder = light ? B("#DCDCE2") : B("#34343A");
                ContentBg = light ? B("#F2F2F5") : B("#141416");
                NavBg = ContentBg;
                NavSelectedBg = light ? B("#FFFFFF") : B("#26262B");
                NavHoverBg = light ? B("#E8E8EC") : B("#1D1D21");
                ButtonBg = light ? B("#F4F4F7") : B("#2A2A2F");
                HoverBg = light ? B("#EBEBEF") : B("#323238");
                PressedBg = light ? B("#E0E0E6") : B("#3A3A41");
                ThumbBg = light ? B("#B8B8C0") : B("#4A4A52");
                ThumbHoverBg = light ? B("#94949C") : B("#6A6A73");
                TrackBg = light ? B("#D5D5DB") : B("#3A3A41");
                SetAccent(light ? Color.FromRgb(0x00, 0x7A, 0xFF) : Color.FromRgb(0x0A, 0x84, 0xFF));
            }
            WindowBg = ContentBg;
            PaneBg = ContentBg;
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
            CornerRadius = new CornerRadius(CardRadius),
            Margin = new Thickness(0, 0, 0, 3),
            Child = child,
        };

        private const double CardMinTextWidth = 120; // schmaler wird der Text nicht: Das Bedienelement rutscht darunter

        /// <summary>Kopfzeile einer Karte: Symbol links, Titel + Beschreibung, Bedienelement rechts.</summary>
        private static Grid CardHeader(string glyph, string title, string? description, UIElement? control)
        {
            var grid = new Grid { MinHeight = 30 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            UIElement icon = new TextBlock
            {
                Text = glyph, FontFamily = IconFont, FontSize = 16, Foreground = Fg,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (IconTiles && !string.IsNullOrEmpty(glyph))
            {
                // Farbkacheln: Symbol weiß in einer Kachel in der Farbe der Seite (Ressource "PageTile")
                var tile = new Border
                {
                    Width = 30, Height = 30, CornerRadius = new CornerRadius(8),
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = glyph, FontFamily = IconFont, FontSize = 13, Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                tile.SetResourceReference(Border.BackgroundProperty, "PageTile");
                icon = tile;
                grid.ColumnDefinitions[0].Width = new GridLength(44);
            }
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

                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var alignment = fe.HorizontalAlignment;
                var margin = fe.Margin;
                grid.SizeChanged += (_, e) =>
                {
                    if (!e.WidthChanged) return;
                    fe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    bool below = e.NewSize.Width - grid.ColumnDefinitions[0].Width.Value - text.Margin.Right - fe.DesiredSize.Width < CardMinTextWidth;
                    if (below == (Grid.GetRow(fe) == 1)) return;
                    Grid.SetRow(fe, below ? 1 : 0);
                    Grid.SetColumn(fe, below ? 1 : 2);
                    Grid.SetColumnSpan(fe, below ? 2 : 1);
                    fe.HorizontalAlignment = below ? HorizontalAlignment.Left : alignment;
                    fe.Margin = below ? new Thickness(margin.Left, 8, margin.Right, 0) : margin;
                };
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

            var head = new Border { Child = header, Background = Brushes.Transparent, CornerRadius = new CornerRadius(CardRadius), Cursor = System.Windows.Input.Cursors.Hand };
            var body = new Border
            {
                BorderBrush = CardBorder, BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(48, 10, 16, 12), Child = content,
            };

            // Pfeil nach unten, dreht sich beim Aufklappen (statt das Zeichen zu tauschen)
            chevron.Text = "\uE70D"; // ChevronDown
            var turn = new RotateTransform(expanded ? 180 : 0);
            chevron.RenderTransformOrigin = new Point(0.5, 0.5);
            chevron.RenderTransform = turn;
            body.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            head.MouseEnter += (_, _) => Motion.Fade(head, HoverBg);
            head.MouseLeave += (_, _) => Motion.Fade(head, Brushes.Transparent);
            head.MouseLeftButtonUp += (_, _) =>
            {
                expanded = !expanded;
                Motion.Expand(body, turn, expanded, head.ActualWidth);
                onExpandedChanged?.Invoke(expanded);
            };

            var panel = new StackPanel();
            panel.Children.Add(head);
            panel.Children.Add(body);
            return CardShell(panel);
        }

        /// <summary>Zeile im aufgeklappten Teil einer Karte: Beschriftung links, Bedienelement rechts.</summary>
        /// <summary>Umschalter mit wenigen Möglichkeiten nebeneinander; pick läuft nur beim Wechsel.</summary>
        public static FrameworkElement Segments((string Key, string Label)[] items, string current, Action<string> pick)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (key, label) in items)
            {
                bool on = key == current;
                string chosen = key;
                var seg = new Border
                {
                    Background = on ? Accent : Brushes.Transparent, CornerRadius = new CornerRadius(5), Padding = new Thickness(12, 4, 12, 5),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Child = new TextBlock { Text = label, FontSize = 12, Foreground = on ? Brushes.White : Fg },
                };
                if (!on)
                {
                    seg.MouseEnter += (_, _) => Motion.Fade(seg, HoverBg);
                    seg.MouseLeave += (_, _) => Motion.Fade(seg, Brushes.Transparent);
                    seg.MouseLeftButtonUp += (_, _) => pick(chosen);
                }
                row.Children.Add(seg);
            }
            return new Border
            {
                Background = FieldBg, BorderBrush = FieldBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(2), Child = row,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
        }

        /// <summary>Auswahl-Pille: an = Akzentfarbe (mit check zusätzlich Häkchen), aus = Feld mit Rand.</summary>
        public static Border Chip(string label, bool on, Action click, bool check = false)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (on && check)
                row.Children.Add(new TextBlock { Text = "\uE73E", FontFamily = IconFont, FontSize = 10, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 6, 0) });
            row.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = on ? Brushes.White : Fg, VerticalAlignment = VerticalAlignment.Center });
            var chip = new Border
            {
                Background = on ? Accent : FieldBg, BorderBrush = on ? Accent : FieldBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13),
                Padding = new Thickness(11, 4, 11, 5), Cursor = System.Windows.Input.Cursors.Hand, Child = row,
            };
            if (!on)
            {
                chip.MouseEnter += (_, _) => Motion.Fade(chip, HoverBg);
                chip.MouseLeave += (_, _) => Motion.Fade(chip, FieldBg);
            }
            chip.MouseLeftButtonUp += (_, _) => click();
            return chip;
        }

        public static Grid OptionRow(string label, UIElement control)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = label, Foreground = Fg, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 10, 0) });
            if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            row.Children.Add(control);
            return row;
        }

        /// <summary>Ein/Aus-Schalter für die rechte Kartenseite (Beschriftung "Ein"/"Aus" links daneben).</summary>
        public static CheckBox Toggle(bool value, Action<bool> onChange)
        {
            string On() => Loc.T("Ein", "On");
            string Off() => Loc.T("Aus", "Off");
            var cb = new CheckBox { IsChecked = value, Foreground = Fg, Content = value ? On() : Off(), Cursor = System.Windows.Input.Cursors.Hand };
            var template = Motion.Enabled ? SwitchTemplate : SwitchTemplateStill;
            if (template != null) cb.Template = template;
            cb.Loaded += (_, _) => SwitchState(cb, false); // beim Erscheinen sofort richtig, nicht gleitend
            cb.Checked += (_, _) => { cb.Content = On(); SwitchState(cb, true); onChange(true); };
            cb.Unchecked += (_, _) => { cb.Content = Off(); SwitchState(cb, true); onChange(false); };
            return cb;
        }

        /// <summary>Schalter-Knopf an seine Stelle (an = rechts, Füllung in Akzentfarbe); animate = gleitend.</summary>
        private static void SwitchState(CheckBox cb, bool animate)
        {
            // In einer zugeklappten Karte ist die Vorlage beim Laden noch nicht angewendet (wird erst beim Messen gebaut):
            // ohne das bliebe der Schalter nach dem Aufklappen auf „aus“ stehen, obwohl daneben „Ein“ steht.
            cb.ApplyTemplate();
            if (cb.Template?.FindName("Thumb", cb) is not FrameworkElement thumb) return;
            if (thumb.RenderTransform is not TransformGroup group || group.Children.Count < 2) return;
            if (group.IsFrozen) { group = group.Clone(); thumb.RenderTransform = group; }
            if (group.Children[1] is not TranslateTransform tt) return;
            bool on = cb.IsChecked == true;
            if (!Motion.Enabled) animate = false;
            void Set(System.Windows.Media.Animation.Animatable target, DependencyProperty dp, double value, double ms)
            {
                if (animate)
                    target.BeginAnimation(dp, new System.Windows.Media.Animation.DoubleAnimation(value, TimeSpan.FromMilliseconds(ms))
                    { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
                else
                {
                    target.BeginAnimation(dp, null);
                    target.SetValue(dp, value);
                }
            }
            void SetEl(UIElement? el, double value, double ms)
            {
                if (el == null) return;
                if (animate) el.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(value, TimeSpan.FromMilliseconds(ms)));
                else { el.BeginAnimation(UIElement.OpacityProperty, null); el.Opacity = value; }
            }
            Set(tt, TranslateTransform.XProperty, on ? 20 : 0, 180);
            SetEl(cb.Template.FindName("OnFill", cb) as UIElement, on ? 1 : 0, 150);
            SetEl(cb.Template.FindName("ThumbOn", cb) as UIElement, on ? 1 : 0, 150);
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
            b.MouseEnter += (_, _) => Motion.Fade(b, HoverBg);
            b.MouseLeave += (_, _) => Motion.Fade(b, Brushes.Transparent);
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
        private static ControlTemplate? SwitchTemplateStill; // Animationen aus: Knopf wächst/streckt sich sofort

        private static ControlTemplate? BuildSwitchTemplate(bool animated)
        {
            try
            {
                string off = Light ? "#6b6b6b" : "#c8c8c8";
                // Schalter gleitet (TranslateTransform), Füllung und Knopffarbe blenden über, Knopf wächst beim Überfahren
                // und streckt sich beim Drücken – wie in Windows 11.
                const string ease = "<DoubleAnimation.EasingFunction><CubicEase EasingMode=\"EaseOut\"/></DoubleAnimation.EasingFunction>";
                string Anim(string target, string prop, string to, string ms) =>
                    $"<DoubleAnimation Storyboard.TargetName=\"{target}\" Storyboard.TargetProperty=\"{prop}\" To=\"{to}\" Duration=\"0:0:0.{(animated ? ms : "0")}\">{ease}</DoubleAnimation>";
                string Story(params string[] anims) => "<BeginStoryboard><Storyboard>" + string.Concat(anims) + "</Storyboard></BeginStoryboard>";
                const string SX = "(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)";
                const string SY = "(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)";
                string xaml =
                    "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"CheckBox\">" +
                    "<StackPanel Orientation=\"Horizontal\" Background=\"Transparent\">" +
                    "<ContentPresenter VerticalAlignment=\"Center\" Margin=\"0,0,12,0\"/>" +
                    "<Grid Width=\"40\" Height=\"20\" VerticalAlignment=\"Center\">" +
                    $"<Border x:Name=\"Track\" CornerRadius=\"10\" BorderThickness=\"1\" BorderBrush=\"{off}\" Background=\"Transparent\"/>" +
                    $"<Border x:Name=\"OnFill\" CornerRadius=\"10\" Background=\"{AccentHex}\" Opacity=\"0\"/>" +
                    "<Grid x:Name=\"Thumb\" Width=\"12\" Height=\"12\" HorizontalAlignment=\"Left\" Margin=\"4,0,0,0\" RenderTransformOrigin=\"0.5,0.5\">" +
                    "<Grid.RenderTransform><TransformGroup><ScaleTransform ScaleX=\"1\" ScaleY=\"1\"/><TranslateTransform X=\"0\"/></TransformGroup></Grid.RenderTransform>" +
                    $"<Ellipse Fill=\"{off}\"/>" +
                    "<Ellipse x:Name=\"ThumbOn\" Fill=\"White\" Opacity=\"0\"/>" +
                    "</Grid>" +
                    "</Grid>" +
                    "</StackPanel>" +
                    "<ControlTemplate.Triggers>" +
                    "<Trigger Property=\"IsMouseOver\" Value=\"True\">" +
                    "<Trigger.EnterActions>" + Story(Anim("Thumb", SX, "1.17", "12"), Anim("Thumb", SY, "1.17", "12")) + "</Trigger.EnterActions>" +
                    "<Trigger.ExitActions>" + Story(Anim("Thumb", SX, "1", "15"), Anim("Thumb", SY, "1", "15")) + "</Trigger.ExitActions>" +
                    "</Trigger>" +
                    "<Trigger Property=\"IsPressed\" Value=\"True\">" +
                    "<Trigger.EnterActions>" + Story(Anim("Thumb", SX, "1.45", "08")) + "</Trigger.EnterActions>" +
                    "<Trigger.ExitActions>" + Story(Anim("Thumb", SX, "1.17", "12")) + "</Trigger.ExitActions>" +
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

        /// <summary>
        /// Lädt Settings/Theme.xaml (eigene Stile für Scrollbar, Dropdown, Aufklapp-Pfeile, Tabs,
        /// Slider, Buttons, Listen, Kontextmenü) und stellt die Theme-Farben als Ressourcen bereit.
        /// Schlägt das Laden fehl, bleibt es still beim Windows-Standardlook. Erneut aufrufen = nur Farben neu.
        /// </summary>
        public static void ApplyControlTheme(Window w)
        {
            w.Resources["ThemeFg"] = Fg;
            w.Resources["ThemeField"] = FieldBg;
            w.Resources["ThemeBorder"] = FieldBorder;
            w.Resources["ThemeHover"] = HoverBg;
            w.Resources["ThemePressed"] = PressedBg;
            w.Resources["ThemeAccent"] = Accent;
            w.Resources["ThemeThumb"] = ThumbBg;
            w.Resources["ThemeThumbHover"] = ThumbHoverBg;
            w.Resources["ThemeTrack"] = TrackBg;
            w.Resources["PageTile"] = Accent; // Farbkacheln: Kachelfarbe außerhalb einer Seite (Seiten setzen ihre eigene)
            if (w.Resources.MergedDictionaries.Count > 0) return;

            Motion.Enabled = Config.ConfigManager.Load().SettingsAnimations;
            try
            {
                w.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/KnobFlyout;component/Settings/Theme.xaml", UriKind.Relative),
                });
            }
            catch (Exception ex)
            {
                KnobFlyout.Logger.Error($"Theme.xaml: {ex.Message}");
            }
            SetMotionStyles(w, Motion.Enabled);
        }

        private static readonly Uri MotionStyles = new("/KnobFlyout;component/Settings/ThemeMotion.xaml", UriKind.Relative);

        /// <summary>Animierte Scrollleiste und Knöpfe (ThemeMotion.xaml) über die ruhigen Stile legen bzw. wieder entfernen.</summary>
        public static void SetMotionStyles(Window w, bool on)
        {
            try
            {
                var have = w.Resources.MergedDictionaries.FirstOrDefault(d => d.Source != null && d.Source.OriginalString == MotionStyles.OriginalString);
                if (on && have == null) w.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = MotionStyles });
                else if (!on && have != null) w.Resources.MergedDictionaries.Remove(have);
            }
            catch (Exception ex)
            {
                KnobFlyout.Logger.Error($"ThemeMotion.xaml: {ex.Message}");
            }
        }

        public static void ApplyDarkTitleBar(Window w)
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                int on = Light ? 0 : 1;
                DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
            }
            catch
            {
                // Rein kosmetisch - ohne dunkle Titelleiste funktioniert alles genauso.
            }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // ── Hotkey-Feld (Aufnahme per Tastendruck, optional ✕ zum Entfernen) ──

        /// <summary>
        /// Klick ins Feld, dann Tastenkombination drücken (Esc bricht ab). Mit allowClear entfernt
        /// "✕" den Hotkey (leer = deaktiviert). onChanged bekommt z.B. "ctrl+alt+right" bzw. "".
        /// Ein Statustext erscheint nur während der Aufnahme oder bei einem Problem.
        /// </summary>
        /// <param name="emptyText">Anzeige, wenn kein Hotkey gesetzt ist (z.B. "Play/Pause-Taste" als Vorgabe).</param>
        /// <param name="format">Anzeige eines gesetzten Hotkeys (z.B. "Strg + P"), sonst der gespeicherte Text.</param>
        public static UIElement HotkeyCapture(string current, Action<string> onChanged, bool allowClear = true, string? emptyText = null,
            Func<string, string>? format = null, double width = 180)
        {
            string saved = current ?? "";
            string Shown(string v) => string.IsNullOrWhiteSpace(v) ? emptyText ?? Loc.T("Nicht festgelegt", "Not set") : format?.Invoke(v) ?? v;

            var box = TextField(Shown(saved), width);
            box.IsReadOnly = true;
            box.Cursor = System.Windows.Input.Cursors.Hand;
            box.Margin = new Thickness(0);

            var status = new TextBlock { Foreground = SubFg, FontSize = 11, Margin = new Thickness(0, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            void SetStatus(string text)
            {
                status.Text = text;
                status.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            void ShowValidity()
            {
                // Nur ein ungültiger gespeicherter Wert wird angezeigt (z.B. von Hand in der Config eingetragen).
                bool invalid = saved.Length > 0 && Input.HotkeyParser.Parse(saved).Item2 == 0;
                SetStatus(invalid ? Loc.T("Ungültig – wird ignoriert", "Invalid – ignored") : "");
            }
            ShowValidity();

            bool capturing = false;

            box.GotFocus += (_, _) =>
            {
                capturing = true;
                box.Text = Loc.T("Taste(n) drücken …", "Press key(s) …");
                SetStatus(Loc.T("Esc = abbrechen", "Esc = cancel"));
            };

            box.LostFocus += (_, _) =>
            {
                if (!capturing) return;
                capturing = false;
                box.Text = Shown(saved);
                ShowValidity();
            };

            box.PreviewKeyDown += (_, e) =>
            {
                if (!capturing) return;
                e.Handled = true;

                var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;

                if (key == System.Windows.Input.Key.Escape)
                {
                    capturing = false;
                    box.Text = Shown(saved);
                    ShowValidity();
                    System.Windows.Input.Keyboard.ClearFocus();
                    return;
                }

                bool pureModifier = key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl
                    or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift
                    or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt
                    or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin;

                var mods = new List<string>();
                var held = System.Windows.Input.Keyboard.Modifiers;
                if ((held & System.Windows.Input.ModifierKeys.Control) != 0) mods.Add("ctrl");
                if ((held & System.Windows.Input.ModifierKeys.Alt) != 0) mods.Add("alt");
                if ((held & System.Windows.Input.ModifierKeys.Shift) != 0) mods.Add("shift");
                if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.LWin) || System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.RWin)) mods.Add("win");

                if (pureModifier)
                {
                    // Nur Vorschau anzeigen, noch nicht übernehmen – wartet auf die Haupttaste.
                    box.Text = mods.Count > 0 ? string.Join("+", mods) + "+…" : Loc.T("Taste(n) drücken …", "Press key(s) …");
                    return;
                }

                int vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
                string? name = Input.HotkeyParser.NameForVk(vk);
                if (name == null)
                {
                    SetStatus(Loc.T("Taste nicht unterstützt", "Key not supported"));
                    return;
                }

                mods.Remove(name); // falls Haupttaste selbst ein Modifier-Name wäre (sollte nicht vorkommen)
                string combo = mods.Count > 0 ? string.Join("+", mods) + "+" + name : name;

                saved = combo;
                capturing = false;
                box.Text = Shown(combo);
                SetStatus("");
                onChanged(combo);
                System.Windows.Input.Keyboard.ClearFocus();
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            row.Children.Add(box);

            if (allowClear)
            {
                var clear = Btn("✕");
                clear.ToolTip = Loc.T("Hotkey entfernen", "Remove hotkey");
                clear.Margin = new Thickness(6, 0, 0, 0);
                clear.Click += (_, _) =>
                {
                    saved = "";
                    capturing = false;
                    box.Text = Shown(saved);
                    SetStatus("");
                    onChanged("");
                };
                row.Children.Add(clear);
            }

            var panel = new StackPanel();
            panel.Children.Add(row);
            panel.Children.Add(status);
            return panel;
        }

        public static Brush FromHex(string hex, string fallback = "#2a2a3a")
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)); }
        }

        /// <summary>Platzhaltertext als ToolTip, da WPF TextBox keinen nativen Platzhalter kennt.</summary>
        public static void InputHint(TextBox box, string hint)
        {
            box.ToolTip = hint;
        }
    }
}
