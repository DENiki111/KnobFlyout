using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using KnobFlyout.Config;

namespace KnobFlyout.Osd
{
    /// <summary>Was ein Flyout-Design von sich aus mitbringt: Reihenfolge, Cover, Schrift und was sich daran ändern lässt.</summary>
    /// <param name="Order">Verschiebbare Bereiche in der Reihenfolge des Designs.</param>
    /// <param name="AppFixed">App-Zeile lässt sich nur ein- und ausblenden (Abzeichen am Cover bzw. Zeile im Display).</param>
    /// <param name="ProgressFixed">Zeitleiste ist Teil des Displays.</param>
    /// <param name="AppMerged">App-Zeile steht im Textblock; wird sie verschoben, bekommt sie eine eigene Zeile.</param>
    internal sealed record MediaDesignInfo(string[] Order, bool AppFixed, bool ProgressFixed, bool AppMerged, string Cover, int CoverSize, int TitleSize,
        string MorePos, string Align, bool ButtonStyle, bool Alignment)
    {
        private static readonly string[] Standard = { "app", "info", "progress", "sources", "controls" };
        private static readonly string[] SourcesLast = { "app", "info", "progress", "controls", "sources" };

        public static MediaDesignInfo For(string design) => design switch
        {
            "win11" => new(Standard, false, false, false, "right", 42, 14, "top", "center", true, true),
            "custom" => new(Standard, false, false, false, "right", 64, 14, "controls", "center", true, true),
            "island" => new(new[] { "info", "progress", "controls", "sources" }, true, false, false, "left", 52, 15, "controls", "center", false, true),
            "mac" => new(SourcesLast, false, false, true, "left", 64, 14, "top", "center", false, true),
            "material" => new(SourcesLast, false, false, false, "left", 64, 16, "top", "center", false, false), // Knopfgruppe füllt die Breite
            "hud" => new(SourcesLast, false, false, false, "left", 60, 18, "top", "center", false, true),
            "hardware" => new(new[] { "info", "controls", "sources" }, true, true, false, "left", 56, 14, "controls", "left", false, true),
            _ => new(Standard, false, false, true, "right", 64, 14, "controls", "center", true, true), // ModernFlyouts
        };

        /// <summary>Verschiebbare Bereiche in gültiger Reihenfolge: jeder genau einmal, fehlende hinten angehängt.</summary>
        public List<string> Movable(MediaLayout layout)
        {
            var result = (layout.Order ?? new List<string>()).Where(Order.Contains).Distinct().ToList();
            result.AddRange(Order.Where(k => !result.Contains(k)));
            return result;
        }

        /// <summary>Alle Bereiche für die Liste im Editor: feste stehen bei „Titel und Cover“.</summary>
        public List<string> Display(MediaLayout layout)
        {
            var result = Movable(layout);
            if (AppFixed) result.Insert(result.IndexOf("info"), "app");
            if (ProgressFixed) result.Insert(result.IndexOf("info") + 1, "progress");
            return result;
        }

        public string FlatDefault(string design) => design == "win11" ? "flat" : "round";
    }

    /// <summary>Farben, die sich am gewählten Flyout-Design ändern lassen, mit ihrem Wert im Design (das Design „Eigenes“ hat media_custom_*).</summary>
    internal static class MediaDesignColors
    {
        private static DesignPalette? Palette(AppConfig cfg, bool light)
        {
            var spec = Designs.Get(cfg.MediaDesign);
            return ReferenceEquals(spec, Designs.Material)
                ? MaterialColors.Scheme(UiColor.Brush(cfg.Layout().ColorOf(MediaLayout.ColorSeed) ?? ColorHex(ThemeHelper.AccentColor())).Color, light)
                : spec?.Palette(light);
        }

        private static string ColorHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        public static List<(string Key, string Label, string Default)> For(AppConfig cfg)
        {
            string design = cfg.MediaDesign;
            bool win11 = design == "win11", hardware = design == "hardware";
            bool light = MediaOsd.LightFor(cfg);
            var pal = Palette(cfg, light);
            var hw = HardwareParts.Colors(light);
            string accentLabel = design switch
            {
                "material" => Loc.T("Akzent (Welle, Play)", "Accent (wave, play)"),
                "hud" => Loc.T("Akzent (Segmente, Play, Klammern)", "Accent (segments, play, brackets)"),
                "hardware" => Loc.T("LEDs", "LEDs"),
                _ => Loc.T("Akzent (Leiste, Quelle)", "Accent (bar, source)"),
            };
            var list = new List<(string, string, string)>
            {
                (MediaLayout.ColorBg, hardware ? Loc.T("Gehäuse", "Casing") : Loc.T("Hintergrund", "Background"),
                    pal?.Bg ?? (win11 ? (light ? "#E9E9EB" : "#2C2C2C") : (light ? "#EFEFEF" : "#222222"))),
                (MediaLayout.ColorFg, hardware ? Loc.T("Tasten und Quellen", "Keys and sources") : Loc.T("Text und Symbole", "Text and icons"),
                    pal?.Fg ?? (light ? "#1B1B1B" : "#FFFFFF")),
                (MediaLayout.ColorSub, Loc.T("Nebentext", "Secondary text"), pal?.Sub ?? (light ? "#5C5C5C" : win11 ? "#C5C5C5" : "#A0A0A6")),
                (MediaLayout.ColorAccent, accentLabel, pal?.Scrub ?? pal?.Fill ?? (win11 ? (light ? "#1B1B1B" : "#FFFFFF") : "#3B82F6")),
                (MediaLayout.ColorButton, hardware ? Loc.T("Tasten", "Keys") : Loc.T("Knopf-Fläche", "Button fill"),
                    hardware ? hw.KeyTop : pal?.Tile ?? pal?.Button ?? (win11 ? (light ? "#0F000000" : "#14FFFFFF") : (light ? "#DADADA" : "#3A3A3A"))),
                (MediaLayout.ColorTrack, Loc.T("Leiste (Hintergrund)", "Bar (background)"),
                    hardware ? "#1FFFB04A" : pal?.Track ?? (light ? "#BDBDBD" : "#5A5A5A")),
            };
            if (hardware)
            {
                list.Add((MediaLayout.ColorDisplay, Loc.T("Display-Schrift und Leiste", "Display text and bar"), hw.LcdText));
                list.Add((MediaLayout.ColorDisplayBg, Loc.T("Display-Hintergrund", "Display background"), hw.Lcd));
            }
            if (pal != null || win11) list.Add((MediaLayout.ColorEdge, Loc.T("Rand", "Border"), pal?.Edge ?? (light ? "#D0D0D4" : "#404040")));
            return list;
        }

        /// <summary>Farbe, die dem Cover folgt, bis man eine feste wählt: die Wellen bei Dynamic Island, alle Farben bei Material You.</summary>
        public static (string Key, string Toggle, string Label, string Default)? CoverColor(AppConfig cfg)
        {
            var pal = Palette(cfg, MediaOsd.LightFor(cfg));
            return cfg.MediaDesign switch
            {
                "island" => (MediaLayout.ColorWave, Loc.T("Wellen in der Farbe des Covers", "Waves in the album art color"), Loc.T("Farbe der Wellen", "Wave color"), pal!.Fg),
                "material" => (MediaLayout.ColorSeed, Loc.T("Farben aus dem Cover", "Colors from the album art"), Loc.T("Grundfarbe", "Base color"), ColorHex(ThemeHelper.AccentColor())),
                _ => null,
            };
        }
    }

    internal sealed partial class MediaOsd
    {
        // Bausteine, die jedes Design beim Aufbau meldet; ApplyLayout hängt sie um.
        private FrameworkElement? _artBlock;   // Cover samt Hülle
        private FrameworkElement? _textBlock;  // Titel und Interpret
        private FrameworkElement? _infoExtra;  // drittes Element in der Zeile (Wellenform, „…“)
        private FrameworkElement? _moreHost;   // „…“ samt Hülle
        private FrameworkElement? _buttonsPanel;
        private Grid? _infoRow;
        private Grid? _winTopBar;
        private FrameworkElement? _islandBadge;
        private FrameworkElement? _hudStatus;
        private System.Windows.Shapes.Path? _hudArtCorners;
        private bool _showTimes = true;
        private bool _showAppText = true;    // Dynamic Island: App hinter dem Interpreten
        private bool _showArtistText = true;

        /// <summary>
        /// Aufbau auf ein fertig gestaltetes Design anwenden: Elemente ein- und ausblenden, Bereiche umsortieren, Cover
        /// umsetzen, Größen ändern. Unveränderter Aufbau: nichts tun, das Design bleibt genau wie gestaltet.
        /// (Das eigene Design baut sich direkt danach auf, siehe ArrangeCustom.)
        /// </summary>
        private void ApplyLayout(MediaLayout layout, string design, Grid root, Grid header, StackPanel left, StackPanel appRow, Grid controls, StackPanel center)
        {
            if (layout.IsDefault) return;
            var info = MediaDesignInfo.For(design);
            bool island = ReferenceEquals(_spec, Designs.Island);

            // ── Bereiche einsammeln ──
            StackPanel stack;
            var blocks = new Dictionary<string, FrameworkElement>();
            if (_spec == null)
            {
                _artBlock = _art;
                _textBlock = left;
                _infoRow = header;
                _moreHost = _moreBtn;
                _buttonsPanel = center;
                root.Children.Clear();
                root.RowDefinitions.Clear();
                stack = new StackPanel();
                root.Children.Add(stack);
                var progress = new StackPanel { Margin = _seekHost.Margin };
                _seekHost.Margin = new Thickness(0);
                progress.Children.Add(_seekHost);
                progress.Children.Add(_timesRow);
                _progressBlock = progress;
                if (_winTopBar != null) blocks["app"] = _winTopBar;
                blocks["info"] = header;
                blocks["progress"] = progress;
                blocks["sources"] = _sourcesRow;
                blocks["controls"] = controls;
            }
            else
            {
                stack = (StackPanel)root.Children[0];
                var keys = info.Order.Where(k => !(k == "app" && info.AppMerged)).ToList();
                var children = stack.Children.Cast<FrameworkElement>().ToList();
                for (int i = 0; i < keys.Count && i < children.Count; i++) blocks[keys[i]] = children[i];
            }
            stack.Children.Clear();

            bool showApp = layout.Shows(MediaLayout.App), showTitle = layout.Shows(MediaLayout.Title), showArtist = layout.Shows(MediaLayout.Artist);
            bool showMore = layout.Shows(MediaLayout.More);
            string morePos = layout.MorePos ?? info.MorePos;
            // Eigene Zeile für App und „…“: Ohne App wandert „…“ in den ersten Bereich, sonst bliebe dafür eine fast leere Zeile stehen.
            bool moveMore = morePos != info.MorePos || (!showApp && morePos == "top" && blocks.ContainsKey("app"));
            if (moveMore && _moreHost != null)
            {
                Detach(_moreHost);
                if (ReferenceEquals(_infoExtra, _moreHost)) _infoExtra = null;
            }

            // ── App ──
            var order = info.Movable(layout);
            bool ownAppRow = info.AppMerged && order.IndexOf("app") + 1 != order.IndexOf("info");
            if (island && _islandBadge != null) _islandBadge.Visibility = showApp ? Visibility.Visible : Visibility.Collapsed;
            else if (IsHardware) _appName.Visibility = showApp ? Visibility.Visible : Visibility.Collapsed;
            else appRow.Visibility = showApp ? Visibility.Visible : Visibility.Collapsed;
            if (_hudStatus != null) _hudStatus.Visibility = showApp ? Visibility.Visible : Visibility.Collapsed;
            if (ownAppRow)
            {
                Detach(appRow);
                appRow.Margin = new Thickness(0);
                appRow.HorizontalAlignment = HorizontalAlignment.Left;
                blocks["app"] = appRow;
            }
            if (_spec == null && !_win11 && (!showApp || ownAppRow)) _title.Margin = new Thickness(0); // ModernFlyouts: Abstand zur App darüber
            if (!showApp && blocks.TryGetValue("app", out var appBlock) && !ReferenceEquals(appBlock, appRow))
            {
                // Zeile mit App und „…“: bleibt nur stehen, wenn „…“ noch drin ist
                bool moreInside = !moveMore && showMore && morePos == "top";
                if (!moreInside) appBlock.Visibility = Visibility.Collapsed;
            }

            // ── Titel und Interpret ──
            _title.Visibility = showTitle ? Visibility.Visible : Visibility.Collapsed;
            bool artistVisible = showArtist || (island && showApp);
            _artist.Visibility = artistVisible ? Visibility.Visible : Visibility.Collapsed;
            if (!showTitle) _artist.Margin = new Thickness(0);
            if (layout.TitleSize > 0) _title.FontSize = Math.Clamp(layout.TitleSize, 11, 22);
            bool appInText = info.AppMerged && showApp && !ownAppRow;
            bool hasText = showTitle || artistVisible || appInText || IsHardware; // Display: Zeile mit App und Zeit bleibt

            // ── Cover ──
            string cover = layout.Cover ?? info.Cover;
            int coverSize = layout.CoverSize > 0 ? Math.Clamp(layout.CoverSize, 32, 140) : info.CoverSize;
            if (coverSize != info.CoverSize) SetCoverSize(coverSize);
            if (cover == "none")
            {
                if (_artBlock != null) _artBlock.Visibility = Visibility.Collapsed;
                if (_textBlock != null) _textBlock.Margin = new Thickness(0, 0, _textBlock.Margin.Right, 0);
            }
            else if (cover != info.Cover) PlaceCover(cover, coverSize);
            if (!hasText && _textBlock != null) _textBlock.Visibility = Visibility.Collapsed;
            bool extraStays = _infoExtra != null && !(ReferenceEquals(_infoExtra, _moreHost) && !showMore) && !island;
            if (!hasText && cover == "none" && !extraStays && !IsHardware) blocks["info"].Visibility = Visibility.Collapsed;

            // ── Knöpfe ──
            bool showPrev = layout.Shows(MediaLayout.Prev), showPlay = layout.Shows(MediaLayout.Play), showNext = layout.Shows(MediaLayout.Next);
            _prevBtn.Visibility = showPrev ? Visibility.Visible : Visibility.Collapsed;
            _playBtn.Visibility = showPlay ? Visibility.Visible : Visibility.Collapsed;
            _nextBtn.Visibility = showNext ? Visibility.Visible : Visibility.Collapsed;
            if (_spec != null && layout.ButtonSize > 0 && layout.ButtonSize != 100 && _buttonsPanel != null)
            {
                double scale = Math.Clamp(layout.ButtonSize, 70, 140) / 100.0;
                _buttonsPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double available = W - root.Margin.Left - root.Margin.Right - (showMore && morePos == "controls" ? 44 : 0);
                if (_buttonsPanel.DesiredSize.Width > 0) scale = Math.Min(scale, available / _buttonsPanel.DesiredSize.Width);
                _buttonsPanel.LayoutTransform = new ScaleTransform(scale, scale);
            }
            if (info.Alignment && layout.ButtonsAlign != null)
            {
                center.HorizontalAlignment = layout.ButtonsAlign switch
                {
                    "left" => HorizontalAlignment.Left,
                    "right" => HorizontalAlignment.Right,
                    _ => HorizontalAlignment.Center,
                };
                // „…“ liegt in derselben Zelle wie die Knöpfe: auf die freie Seite
                if (!moveMore && controls.ColumnDefinitions.Count <= 1 && ReferenceEquals(_moreBtn.Parent, controls))
                {
                    if (layout.ButtonsAlign == "left") _moreBtn.HorizontalAlignment = HorizontalAlignment.Right;
                    else if (layout.ButtonsAlign == "right") _moreBtn.HorizontalAlignment = HorizontalAlignment.Left;
                }
            }
            bool anyButton = showPrev || showPlay || showNext || layout.Shows(MediaLayout.Skip);
            bool moreInControls = showMore && !moveMore && morePos == "controls";
            if (!anyButton && !moreInControls) blocks["controls"].Visibility = Visibility.Collapsed;

            // ── Reihenfolge ──
            string designFirst = info.Order.First(k => blocks.ContainsKey(k) && !(k == "app" && ownAppRow));
            bool first = true;
            foreach (var key in order)
            {
                if (!blocks.TryGetValue(key, out var block)) continue;
                var m = block.Margin;
                if (first)
                {
                    block.Margin = new Thickness(m.Left, 0, m.Right, m.Bottom);
                    if (key == "progress" && _spec != null) _seekHost.Margin = new Thickness(0);
                }
                else if (key == designFirst || m.Top <= 0) block.Margin = new Thickness(m.Left, key == "progress" && _spec != null && _seekHost.Margin.Top > 0 ? 0 : 12, m.Right, m.Bottom);
                stack.Children.Add(block);
                if (block.Visibility == Visibility.Visible) first = false;
            }

            // ── „…“ an die andere Stelle ──
            if (moveMore && _moreHost != null)
            {
                var target = morePos == "top"
                    ? stack.Children.Cast<FrameworkElement>().FirstOrDefault(b => b.Visibility == Visibility.Visible)
                    : blocks["controls"];
                target ??= blocks["controls"];
                int index = stack.Children.IndexOf(target);
                if (index >= 0) stack.Children.RemoveAt(index);
                else index = stack.Children.Count;
                var wrap = new Grid { Margin = target.Margin };
                target.Margin = new Thickness(0);
                wrap.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                wrap.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetRow(target, 0);
                Grid.SetColumn(target, 0);
                wrap.Children.Add(target);
                _moreHost.HorizontalAlignment = HorizontalAlignment.Right;
                _moreHost.VerticalAlignment = morePos == "top" ? VerticalAlignment.Top : VerticalAlignment.Center;
                _moreHost.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetRow(_moreHost, 0);
                Grid.SetColumn(_moreHost, 1);
                wrap.Children.Add(_moreHost);
                stack.Children.Insert(index, wrap);
            }
        }

        private static void Detach(FrameworkElement element)
        {
            if (element.Parent is Panel panel) panel.Children.Remove(element);
            else if (element.Parent is Border border) border.Child = null;
        }

        /// <summary>Cover-Größe ändern; Windows 11 behält sein flaches Format, Hüllen und Klammern wachsen mit.</summary>
        private void SetCoverSize(int size)
        {
            double w = _win11 ? Math.Round(size * 72.0 / 42.0) : size, h = size;
            _art.Width = w;
            _art.Height = h;
            if (_art.Clip != null)
            {
                double r = _art.CornerRadius.TopLeft;
                _art.Clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
            }
            if (_artBlock != null && !ReferenceEquals(_artBlock, _art))
            {
                _artBlock.Width = w;
                _artBlock.Height = h;
            }
            if (_hudArtCorners != null) _hudArtCorners.Data = HudParts.CornerGeometry(w, h, 9, 4);
        }

        /// <summary>Cover links, rechts oder über dem Text; ein drittes Element der Zeile (Wellenform, „…“) bleibt rechts vom Text.</summary>
        private void PlaceCover(string position, int coverSize)
        {
            if (_infoRow == null || _artBlock == null || _textBlock == null) return;
            var row = _infoRow;
            row.Children.Clear();
            row.ColumnDefinitions.Clear();
            row.RowDefinitions.Clear();
            const double gap = 12;
            double beforeExtra = _infoExtra != null ? 8 : 0;
            _artBlock.Margin = IsHud ? new Thickness(4) : new Thickness(0);
            _artBlock.VerticalAlignment = VerticalAlignment.Center;
            foreach (var e in new[] { _artBlock, _textBlock, _infoExtra })
            {
                if (e == null) continue;
                Grid.SetRow(e, 0);
                Grid.SetColumn(e, 0);
                Grid.SetColumnSpan(e, 1);
            }
            switch (position)
            {
                case "top":
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Grid.SetColumnSpan(_artBlock, 2);
                    _artBlock.HorizontalAlignment = HorizontalAlignment.Center;
                    _textBlock.Margin = new Thickness(0, 10, beforeExtra, 0);
                    Grid.SetRow(_textBlock, 1);
                    if (_infoExtra != null)
                    {
                        Grid.SetRow(_infoExtra, 1);
                        Grid.SetColumn(_infoExtra, 1);
                    }
                    break;
                case "right":
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    _textBlock.Margin = new Thickness(0, 0, gap, 0);
                    if (_infoExtra != null)
                    {
                        Grid.SetColumn(_infoExtra, 1);
                        _infoExtra.Margin = new Thickness(0, 0, gap, 0);
                    }
                    Grid.SetColumn(_artBlock, 2);
                    if (_hwStatus != null) _hwStatus.Margin = new Thickness(0, 1, coverSize + gap, 0); // nicht auf dem Cover
                    break;
                default: // "left"
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    _textBlock.Margin = new Thickness(gap, 0, beforeExtra, 0);
                    Grid.SetColumn(_textBlock, 1);
                    if (_infoExtra != null) Grid.SetColumn(_infoExtra, 2);
                    break;
            }
            row.Children.Add(_artBlock);
            row.Children.Add(_textBlock);
            if (_infoExtra != null) row.Children.Add(_infoExtra);
        }
    }
}
