using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using static KnobFlyout.Loc;
using static KnobFlyout.Settings.UiHelpers;

namespace KnobFlyout.Settings
{
    /// <summary>
    /// Suche oben in der Navigation: findet Einstellungen nach Titel, Abschnitt, Seite und allem Text auf der Karte
    /// (Beschreibung, Auswahl-Einträge, Unterpunkte). Verzeiht Tippfehler (Damerau-Levenshtein), passt auf Wortanfänge
    /// und Wortteile (zusammengesetzte Wörter) und kennt verwandte Begriffe (z. B. „transparent“ → Deckkraft).
    /// Ein Treffer öffnet die Seite, klappt die Karte bei Bedarf auf und hebt sie kurz hervor.
    /// </summary>
    public sealed partial class SettingsWindow
    {
        private sealed record SearchEntry(string Page, string PageLabel, string Section, string Title, string? Parent, bool IsPage,
            string[] TitleWords, string[] ContextWords, string[] BodyWords, string TitleNorm);

        private List<SearchEntry>? _searchIndex;
        private TextBox? _settingsSearch;
        private FrameworkElement? _searchHost;
        private Popup? _searchPopup;
        private StackPanel? _searchList;
        private List<SearchEntry> _searchHits = new();
        private int _searchSel;
        private TourHighlight? _searchHighlight;
        private System.Windows.Threading.DispatcherTimer? _searchHighlightTimer;

        // ── Feld ──────────────────────────────────────────────────────────────
        private FrameworkElement BuildSearchBox()
        {
            var box = _settingsSearch = new TextBox
            {
                Background = FieldBg, Foreground = Fg, BorderBrush = FieldBorder, BorderThickness = new Thickness(1),
                Padding = new Thickness(24, 3, 6, 3), Height = 30, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center,
            };
            System.Windows.Automation.AutomationProperties.SetName(box, T("Einstellungen durchsuchen", "Search settings"));
            var icon = new TextBlock
            {
                Text = "", FontFamily = IconFont, FontSize = 12, Foreground = SubFg, // Search
                Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
            };
            var hint = new TextBlock
            {
                Text = T("Suchen", "Search"), FontSize = 13, Foreground = SubFg,
                Margin = new Thickness(27, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
            };
            var host = new Grid { Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            host.Children.Add(box);
            host.Children.Add(icon);
            host.Children.Add(hint);

            box.TextChanged += (_, _) =>
            {
                hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                RunSearch();
            };
            box.GotKeyboardFocus += (_, _) =>
            {
                _searchIndex = null; // Seiten können sich seit dem letzten Mal geändert haben
                if (box.Text.Length > 0) RunSearch();
            };
            // Klick in die Vorschläge nimmt dem Feld kurz den Fokus: dann offen lassen, sonst ginge der Klick ans Fenster dahinter
            box.LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_searchPopup is { IsMouseOver: true } || box.IsKeyboardFocusWithin) return;
                CloseSearchPopup();
            }), System.Windows.Threading.DispatcherPriority.Input);
            box.PreviewKeyDown += SearchKeyDown;
            _searchHost = host;
            return host;
        }

        private void SearchKeyDown(object sender, KeyEventArgs e)
        {
            bool open = _searchPopup is { IsOpen: true };
            switch (e.Key)
            {
                case Key.Down when open && _searchHits.Count > 0:
                    _searchSel = (_searchSel + 1) % _searchHits.Count;
                    RenderSearch();
                    e.Handled = true;
                    break;
                case Key.Up when open && _searchHits.Count > 0:
                    _searchSel = (_searchSel - 1 + _searchHits.Count) % _searchHits.Count;
                    RenderSearch();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    if (_searchHits.Count > 0) OpenSearchHit(_searchHits[Math.Clamp(_searchSel, 0, _searchHits.Count - 1)]);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    if (open) CloseSearchPopup();
                    else _settingsSearch!.Text = "";
                    e.Handled = true;
                    break;
            }
        }

        private void RunSearch()
        {
            if (_settingsSearch == null) return;
            string q = _settingsSearch.Text.Trim();
            if (q.Length == 0) { _searchHits.Clear(); CloseSearchPopup(); return; }
            _searchIndex ??= BuildSearchIndex();
            _searchHits = Search(_searchIndex, q).Take(8).ToList();
            _searchSel = 0;
            EnsureSearchPopup();
            RenderSearch();
            _searchPopup!.IsOpen = true;
        }

        private void EnsureSearchPopup()
        {
            if (_searchPopup != null) return;
            _searchList = new StackPanel();
            var card = new Border
            {
                Width = 380, Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(4),
                Child = new ScrollViewer { MaxHeight = 440, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _searchList },
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.3 },
            };
            // im Fensterbaum, damit die Theme-Stile greifen; bleibt offen, solange das Feld den Fokus hat
            _searchPopup = new Popup
            {
                AllowsTransparency = true, StaysOpen = true, Placement = PlacementMode.Bottom, PlacementTarget = _settingsSearch,
                VerticalOffset = 4, Child = new Border { Padding = new Thickness(0, 0, 10, 10), Child = card },
            };
            if (Content is Panel root) root.Children.Add(_searchPopup);
            LocationChanged += (_, _) => CloseSearchPopup();
            Deactivated += (_, _) => { if (_searchPopup is not { IsMouseOver: true }) CloseSearchPopup(); };
        }

        private void CloseSearchPopup()
        {
            if (_searchPopup != null) _searchPopup.IsOpen = false;
        }

        private void RenderSearch()
        {
            if (_searchList == null) return;
            _searchList.Children.Clear();
            if (_searchHits.Count == 0)
            {
                _searchList.Children.Add(new TextBlock
                {
                    Text = T("Nichts gefunden", "Nothing found"), FontSize = 13, Foreground = SubFg, Margin = new Thickness(10, 8, 10, 8),
                });
                return;
            }
            for (int i = 0; i < _searchHits.Count; i++)
            {
                var hit = _searchHits[i];
                bool sel = i == _searchSel;
                var path = new List<string> { hit.PageLabel };
                if (hit.Section.Length > 0) path.Add(hit.Section);
                if (hit.Parent != null) path.Add(hit.Parent);
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = hit.Title, FontSize = 13, Foreground = Fg, TextTrimming = TextTrimming.CharacterEllipsis });
                if (!hit.IsPage)
                    text.Children.Add(new TextBlock { Text = string.Join(" › ", path), FontSize = 11.5, Foreground = SubFg, TextTrimming = TextTrimming.CharacterEllipsis });
                var row = new Border
                {
                    Child = text, Padding = new Thickness(10, 6, 10, 6), CornerRadius = new CornerRadius(6),
                    Background = sel ? HoverBg : Brushes.Transparent, Cursor = Cursors.Hand,
                };
                int index = i;
                row.MouseEnter += (_, _) => { if (index != _searchSel) Motion.Fade(row, HoverBg); };
                row.MouseLeave += (_, _) => { if (index != _searchSel) Motion.Fade(row, Brushes.Transparent); };
                // Maus beim Drücken festhalten und beim Loslassen öffnen: so landet weder Drücken noch Loslassen
                // im Fenster hinter der Liste (sonst öffnete sich z. B. „Allgemein“ statt des Treffers)
                row.PreviewMouseLeftButtonDown += (_, e) => { e.Handled = true; row.CaptureMouse(); };
                row.PreviewMouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    bool inside = row.IsMouseOver || new Rect(row.RenderSize).Contains(e.GetPosition(row));
                    row.ReleaseMouseCapture();
                    if (inside) OpenSearchHit(hit);
                };
                _searchList.Children.Add(row);
            }
        }

        // ── Treffer öffnen ────────────────────────────────────────────────────
        private void OpenSearchHit(SearchEntry hit)
        {
            CloseSearchPopup();
            RemoveSearchHighlight();
            if (_currentPage != hit.Page)
            {
                SelectPage(hit.Page);
                if (_currentPage != hit.Page) return; // Seite nicht verlassen (ungespeichertes Design)
            }
            UpdateLayout();
            if (hit.IsPage) { _pageScroll?.ScrollToTop(); return; }
            if (_pageScroll?.Content is not Panel page) return;

            FrameworkElement? card = null;
            foreach (UIElement child in page.Children)
            {
                if (child is not FrameworkElement fe) continue;
                var titles = CardTexts(fe).Where(t => t.IsTitle).Select(t => t.Text).ToList();
                if (titles.Count == 0) continue;
                if (hit.Parent == null ? titles[0] == hit.Title : titles[0] == hit.Parent && titles.Contains(hit.Title)) { card = fe; break; }
            }
            if (card == null) return;

            // Zugeklappte Karte aufklappen (der Treffer kann im aufklappbaren Teil stehen)
            if (card is Border { Child: StackPanel { Children.Count: 2 } sp }
                && sp.Children[0] is Border { Cursor: not null } head && sp.Children[1] is Border { Visibility: Visibility.Collapsed })
            {
                head.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                UpdateLayout();
            }

            FrameworkElement target = card;
            if (hit.Parent != null)
                target = Descendants(card).OfType<TextBlock>().FirstOrDefault(t => t.Text == hit.Title && t.IsVisible) ?? card;
            target.BringIntoView();
            if (AdornerLayer.GetAdornerLayer(card) is { } layer)
            {
                _searchHighlight = new TourHighlight(card);
                layer.Add(_searchHighlight);
                _searchHighlightTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
                _searchHighlightTimer.Tick -= SearchHighlightTick;
                _searchHighlightTimer.Tick += SearchHighlightTick;
                _searchHighlightTimer.Stop();
                _searchHighlightTimer.Start();
            }
        }

        private void SearchHighlightTick(object? sender, EventArgs e)
        {
            _searchHighlightTimer?.Stop();
            RemoveSearchHighlight();
        }

        private void RemoveSearchHighlight()
        {
            if (_searchHighlight == null) return;
            AdornerLayer.GetAdornerLayer(_searchHighlight.AdornedElement)?.Remove(_searchHighlight);
            _searchHighlight = null;
        }

        // ── Index ─────────────────────────────────────────────────────────────
        /// <summary>Alle sichtbaren Seiten durchgehen (noch nicht gebaute werden gebaut und behalten, wie beim Anklicken).</summary>
        private List<SearchEntry> BuildSearchIndex()
        {
            var list = new List<SearchEntry>();
            foreach (var nav in MainNav().Where(e => IsPageShown(e.Key)).Append(InfoNav()))
            {
                if (!_pages.TryGetValue(nav.Key, out var built))
                {
                    built = BuildPage(nav.Key);
                    _pages[nav.Key] = built;
                    if (DesignPages.Contains(nav.Key)) _pageSnapshots[nav.Key] = ConfigSnapshot();
                }
                var pageWords = Words(nav.Label);
                list.Add(new SearchEntry(nav.Key, nav.Label, "", nav.Label, null, true, pageWords, Array.Empty<string>(), Array.Empty<string>(), Normalize(nav.Label)));
                if (built is not Panel panel) continue;

                string section = "";
                foreach (UIElement child in panel.Children)
                {
                    if (child is TextBlock { FontSize: 13 } st && st.FontWeight == FontWeights.SemiBold) { section = st.Text; continue; } // SectionTitle
                    if (child is not FrameworkElement fe) continue;
                    var texts = CardTexts(fe);
                    var titles = texts.Where(t => t.IsTitle).Select(t => t.Text).Distinct().ToList();
                    if (titles.Count == 0) continue;
                    var context = Words(section).Concat(pageWords).Distinct().ToArray();
                    var body = texts.Where(t => !t.IsTitle).SelectMany(t => Words(t.Text)).Distinct().ToArray();
                    list.Add(new SearchEntry(nav.Key, nav.Label, section, titles[0], null, false, Words(titles[0]), context, body, Normalize(titles[0])));
                    var cardWords = Words(titles[0]);
                    foreach (var sub in titles.Skip(1).Where(t => Words(t).Length > 0)) // nur echte Wörter (keine Symbole)
                        list.Add(new SearchEntry(nav.Key, nav.Label, section, sub, titles[0], false, Words(sub),
                            context.Concat(cardWords).Distinct().ToArray(), Array.Empty<string>(), Normalize(sub)));
                }
            }
            return list;
        }

        /// <summary>Texte einer Karte über den logischen Baum (geht auch für nicht angezeigte Seiten). Titel = Schrift 14 wie in CardHeader.</summary>
        private static List<(string Text, bool IsTitle)> CardTexts(FrameworkElement card)
        {
            var into = new List<(string, bool)>();
            void Walk(object node, int depth)
            {
                if (depth > 80) return;
                switch (node)
                {
                    case string s:
                        if (!string.IsNullOrWhiteSpace(s)) into.Add((s, false));
                        return;
                    case TextBox:
                        return; // Eingaben (z. B. Client ID) nicht durchsuchen
                    case TextBlock tb:
                        if (tb.FontFamily == IconFont) return; // Symbol
                        string text = tb.Text;
                        if (string.IsNullOrWhiteSpace(text)) text = string.Concat(tb.Inlines.OfType<Run>().Select(r => r.Text));
                        if (!string.IsNullOrWhiteSpace(text)) into.Add((text, tb.FontSize == 14));
                        return;
                }
                if (node is DependencyObject d)
                    foreach (var c in LogicalTreeHelper.GetChildren(d)) Walk(c, depth + 1);
            }
            Walk(card, 0);
            return into;
        }

        // ── Vergleich ─────────────────────────────────────────────────────────
        private static readonly HashSet<string> StopWords = new()
        {
            "der", "die", "das", "den", "dem", "des", "und", "oder", "mit", "fuer", "ein", "eine", "einen", "einem", "ist", "wird", "im", "am",
            "zu", "von", "auf", "bei", "an", "in", "aus", "als", "wie", "wo", "was", "nicht", "nur", "auch", "es", "sich", "man", "ich", "kann",
            "the", "and", "or", "of", "to", "a", "an", "is", "on", "for", "with", "by", "it", "be", "as", "at", "how", "where", "not", "only",
        };

        /// <summary>Verwandte Begriffe (normalisiert). Ein Suchwort, das zu einem davon passt, sucht die anderen mit.</summary>
        private static readonly string[][] Related =
        {
            new[] { "lautstaerke", "laut", "leise", "volume", "ton", "sound", "audio", "pegel", "regeln" },
            new[] { "farbe", "farben", "color", "colors", "colour", "farbprofil", "akzent", "accent", "einfaerben" },
            new[] { "hell", "dunkel", "farbschema", "theme", "dark", "light", "darkmode", "nachtmodus" },
            new[] { "position", "platz", "ort", "ecke", "verschieben", "place", "location", "stelle", "positionen" },
            new[] { "bildschirm", "monitor", "screen", "display" },
            new[] { "taste", "tasten", "hotkey", "shortcut", "tastenkuerzel", "kuerzel", "key", "keys", "tastatur" },
            new[] { "autostart", "starten", "start", "startup", "hochfahren", "windows" },
            new[] { "sprache", "language", "deutsch", "englisch", "english", "german" },
            new[] { "deckkraft", "transparent", "transparenz", "durchsichtig", "opacity", "alpha", "durchscheinend" },
            new[] { "media", "medien", "musik", "music", "song", "lied", "titel", "flyout", "player", "wiedergabe" },
            new[] { "spotify", "api" },
            new[] { "youtube", "browser", "addon", "erweiterung", "extension", "firefox", "chrome", "opera", "video" },
            new[] { "profil", "profile", "gruppe" },
            new[] { "app", "apps", "programm", "programme", "anwendung", "prozess" },
            new[] { "ignorieren", "ignoriert", "ignorierte", "ausschliessen", "exclude", "ignore", "ignored" },
            new[] { "osd", "anzeige", "overlay", "einblendung", "popup" },
            new[] { "design", "aussehen", "look", "stil", "style", "optik" },
            new[] { "cover", "bild", "albumcover", "artwork", "hintergrund", "titelbild" },
            new[] { "spulen", "vorspulen", "zurueckspulen", "skip", "seek", "sekunden" },
            new[] { "snap", "gespeichert", "gespeicherte", "speicherplatz" },
            new[] { "lite", "einfach", "simple", "minimal" },
            new[] { "einfuehrung", "tutorial", "tour", "hilfe", "help", "anleitung", "erklaerung" },
            new[] { "ecken", "radius", "rund", "abgerundet", "corner", "corners" },
            new[] { "dauer", "zeit", "duration", "ausblenden", "anzeigedauer", "lange" },
            new[] { "schrittweite", "schritt", "step", "raste", "stufe", "schritte" },
            new[] { "stumm", "mute", "stummschalten" },
            new[] { "zuruecksetzen", "reset", "standard", "default" },
            new[] { "protokoll", "log", "fehler", "problem", "probleme", "konfiguration", "config", "problembehebung" },
            new[] { "knopf", "drehknopf", "regler", "knob", "dial" },
            new[] { "dauerhaft", "immer", "permanent", "stehen", "keep" },
            new[] { "feststelltaste", "capslock", "numlock", "rollen", "lock" },
            new[] { "neue", "neu", "erkannt", "zuweisen", "zuweisung", "einsortieren", "assign" },
        };

        internal static string Normalize(string s)
        {
            var sb = new StringBuilder(s.Length + 4);
            foreach (char ch in s.ToLowerInvariant())
            {
                switch (ch)
                {
                    case 'ä': sb.Append("ae"); break;
                    case 'ö': sb.Append("oe"); break;
                    case 'ü': sb.Append("ue"); break;
                    case 'ß': sb.Append("ss"); break;
                    default: sb.Append(char.IsLetterOrDigit(ch) ? ch : ' '); break;
                }
            }
            return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        internal static string[] Words(string s) =>
            Normalize(s).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 2 && !StopWords.Contains(w)).Distinct().ToArray();

        /// <summary>Wie gut passt ein Suchwort auf ein Wort: 1 gleich, 0,85 Wortanfang, 0,7 Wortteil, bis 0,6 mit Tippfehler.</summary>
        internal static double WordScore(string q, string w)
        {
            if (q == w) return 1;
            if (w.StartsWith(q, StringComparison.Ordinal)) return q.Length >= 2 ? 0.85 : 0;
            if (q.Length >= 3 && w.Contains(q, StringComparison.Ordinal)) return 0.7;
            if (q.Length < 4) return 0;
            int allowed = q.Length >= 8 ? 2 : 1;
            int d = Distance(q, w, allowed);
            if (d <= allowed) return 0.62 - 0.08 * d;
            // angefangenes Wort mit Tippfehler: gegen den Wortanfang gleicher Länge (±1) vergleichen
            for (int len = q.Length - 1; len <= q.Length + 1; len++)
            {
                if (len < 4 || len >= w.Length) continue;
                int dp = Distance(q, w[..len], allowed);
                if (dp <= allowed) return 0.55 - 0.08 * dp;
            }
            // Tippfehler in einem Wortteil (zusammengesetzte Wörter): Fenster gleicher Länge im Wort
            if (q.Length >= 5 && w.Length > q.Length + 1)
                for (int start = 1; start + q.Length <= w.Length; start++)
                    if (Distance(q, w.Substring(start, q.Length), 1) <= 1) return 0.45;
            return 0;
        }

        /// <summary>Damerau-Levenshtein (Vertauschen zweier Nachbarn zählt 1); bricht ab, sobald mehr als max sicher ist.</summary>
        private static int Distance(string a, string b, int max)
        {
            if (Math.Abs(a.Length - b.Length) > max) return max + 1;
            var prev2 = new int[b.Length + 1];
            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                int rowMin = cur[0];
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    int v = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) v = Math.Min(v, prev2[j - 2] + 1);
                    cur[j] = v;
                    if (v < rowMin) rowMin = v;
                }
                if (rowMin > max) return max + 1;
                (prev2, prev, cur) = (prev, cur, prev2);
            }
            return prev[b.Length];
        }

        private static double Best(string q, string[] words)
        {
            double best = 0;
            foreach (var w in words)
            {
                double s = WordScore(q, w);
                if (s > best) { best = s; if (best >= 1) break; }
            }
            return best;
        }

        private static IEnumerable<SearchEntry> Search(List<SearchEntry> index, string query)
        {
            var tokens = Words(query);
            if (tokens.Length == 0) tokens = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return Enumerable.Empty<SearchEntry>();
            string whole = Normalize(query);

            // verwandte Begriffe je Suchwort
            var expansions = tokens.Select(t => Related
                .Where(g => g.Any(m => WordScore(t, m) >= 0.5))
                .SelectMany(g => g).Where(m => m != t).Distinct().ToArray()).ToArray();

            double Field(string q, SearchEntry e) =>
                Math.Max(3 * Best(q, e.TitleWords), Math.Max(1.2 * Best(q, e.ContextWords), Best(q, e.BodyWords)));

            var scored = new List<(SearchEntry Entry, int Matched, double Score)>();
            foreach (var e in index)
            {
                int matched = 0;
                double total = 0;
                for (int i = 0; i < tokens.Length; i++)
                {
                    double s = Field(tokens[i], e);
                    foreach (var x in expansions[i])
                    {
                        if (s >= 3) break;
                        s = Math.Max(s, 0.7 * Field(x, e));
                    }
                    if (s > 0) { matched++; total += s; }
                }
                if (matched == 0) continue;
                if (whole.Length >= 3 && e.TitleNorm.Contains(whole, StringComparison.Ordinal)) total += 2; // ganze Eingabe im Titel
                if (e.IsPage) total += 0.5;
                scored.Add((e, matched, total));
            }
            if (scored.Count == 0) return Enumerable.Empty<SearchEntry>();
            int most = scored.Max(s => s.Matched);
            double top = scored.Where(s => s.Matched == most).Max(s => s.Score);
            return scored
                .Where(s => s.Matched == most && s.Score >= Math.Max(0.5, top * 0.3)) // schwache Zufallstreffer weglassen
                .OrderByDescending(s => s.Score)
                .ThenBy(s => s.Entry.Title.Length)
                .Select(s => s.Entry)
                .GroupBy(e => (e.Page, e.Title)).Select(g => g.First()); // gleiche Karte nur einmal je Seite
        }
    }
}
