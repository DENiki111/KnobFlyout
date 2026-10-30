using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using KnobFlyout.Config;

namespace KnobFlyout.Osd
{
    /// <summary>
    /// Entspricht der OSD-Klasse aus knob_service.py, aber ohne Stapeln: es ist immer nur EINE Karte
    /// sichtbar (osd_x / osd_y = linke obere Ecke). Eine neue Karte (andere App, Profil, Feststelltaste,
    /// Snap-Position) ersetzt die alte; dieselbe App aktualisiert nur ihre Karte. Steuern mehrere Apps
    /// gleichzeitig, zeigt die Karte die erste mit "+N". Auto-Hide per osd_duration. Alle Aufrufe sind
    /// aus jedem Thread erlaubt und laufen intern auf dem UI-Thread. Konstruktion muss auf dem UI-Thread erfolgen.
    /// </summary>
    internal sealed class OsdManager
    {
        private const string ProfileCardKey = "__profile__";
        private const string LockKeyCardKey = "__lockkey__";
        private const string SnapCardKey = "__snap__";
        private const int ProfileDurationMs = 1800;

        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private readonly Dictionary<string, OsdCard> _cards = new();
        private readonly List<string> _order = new();
        private readonly DispatcherTimer _timer = new();
        // Zuletzt gesehene Höhe des Media-Flyouts: Hängt der Stapel am Flyout und ist es gerade weg, steht der
        // Stapel trotzdem genau dort, wo er mit Flyout stünde (gleiche Position, nichts rutscht beim Erscheinen).
        private double _lastMediaHeight = MediaOsd.EstimatedHeight;

        public OsdManager()
        {
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                HideAll();
            };
        }

        /// <summary>Entspricht osd_show(results, muted=...).</summary>
        public void Show(IReadOnlyList<(string Name, int Value)> results, bool muted)
        {
            _dispatcher.BeginInvoke(new Action(() => ShowCore(results, muted)));
        }

        /// <summary>Entspricht osd_profile_switch().</summary>
        public void ShowProfileSwitch()
        {
            _dispatcher.BeginInvoke(new Action(ShowProfileCore));
        }

        private void ShowCore(IReadOnlyList<(string Name, int Value)> results, bool muted)
        {
            try
            {
                var cfg = ConfigManager.Load();
                if (!cfg.Osd || results.Count == 0) return;

                // Nur eine Karte: dieselbe App aktualisieren, sonst die alte Karte durch eine neue ersetzen.
                var (name, value) = results[0];
                string key = "vol:" + name.ToLowerInvariant();
                RemoveAllExcept(key);
                if (!_cards.TryGetValue(key, out var card))
                {
                    card = OsdCard.CreateVolume(cfg, name);
                    Wire(card);
                    _cards[key] = card;
                    _order.Add(key);
                }
                card.UpdateVolume(results.Count > 1 ? $"{name} +{results.Count - 1}" : name, value, muted);

                Restack(cfg);
                RestartTimer(cfg.OsdDuration);
            }
            catch (Exception ex)
            {
                Logger.Error($"OSD show: {ex.Message}");
            }
        }

        /// <summary>Entspricht dem Lock-Keys-Flyout von ModernFlyouts.</summary>
        public void ShowLockKey(string name, bool on)
        {
            _dispatcher.BeginInvoke(new Action(() => ShowLockKeyCore(name, on)));
        }

        private void ShowLockKeyCore(string name, bool on)
        {
            try
            {
                var cfg = ConfigManager.Load();
                if (!cfg.Osd || !cfg.LockKeysOsd) return;

                RemoveAllExcept(null); // nur eine Karte: vorherige verschwindet

                _cards[LockKeyCardKey] = OsdCard.CreateLockKey(cfg, name, on);
                Wire(_cards[LockKeyCardKey]);
                _order.Insert(0, LockKeyCardKey);

                Restack(cfg);
                RestartTimer(ProfileDurationMs);
            }
            catch (Exception ex)
            {
                Logger.Error($"OSD lock key: {ex.Message}");
            }
        }

        /// <summary>Entspricht osd_snap_move(): zeigt "📍 Position X/Y" beim Langdruck-Zyklen durch snap_positions.</summary>
        public void ShowSnapMove(int index, int total)
        {
            _dispatcher.BeginInvoke(new Action(() => ShowSnapMoveCore(index, total)));
        }

        private void ShowSnapMoveCore(int index, int total)
        {
            try
            {
                var cfg = ConfigManager.Load();
                if (!cfg.Osd) return;

                RemoveAllExcept(null); // nur eine Karte: vorherige verschwindet

                _cards[SnapCardKey] = OsdCard.CreateSnapInfo(cfg, index, total);
                Wire(_cards[SnapCardKey]);
                _order.Insert(0, SnapCardKey);

                Restack(cfg);
                RestartTimer(1500);
            }
            catch (Exception ex)
            {
                Logger.Error($"OSD snap move: {ex.Message}");
            }
        }

        private void ShowProfileCore()
        {
            try
            {
                var cfg = ConfigManager.Load();

                RemoveAllExcept(null); // Lautstärke-Karte sofort weg, sobald das Profil wechselt

                _cards[ProfileCardKey] = OsdCard.CreateProfile(cfg);
                Wire(_cards[ProfileCardKey]);
                _order.Insert(0, ProfileCardKey);

                Restack(cfg);
                RestartTimer(ProfileDurationMs);
            }
            catch (Exception ex)
            {
                Logger.Error($"OSD profile: {ex.Message}");
            }
        }

        /// <summary>
        /// Wird ausgelöst, sobald der Nutzer eine Karte per Maus losgelassen hat (nur wenn
        /// cfg.SnapEnabled aktiv ist). Berechnet daraus die neue Stapel-Anker-Position, rastet
        /// in eine gespeicherte Snap-Position ein, falls nahe genug, speichert sie in der Config
        /// und richtet den ganzen Stapel neu daran aus.
        /// </summary>
        private void OnCardDragged(OsdCard dragged)
        {
            try
            {
                var cfg = ConfigManager.Load();
                var (_, h, gap) = OsdCard.Dims(cfg);

                string? key = null;
                foreach (var kv in _cards) if (ReferenceEquals(kv.Value, dragged)) { key = kv.Key; break; }
                if (key == null) return;
                int index = _order.IndexOf(key);
                if (index < 0) return;

                double x = dragged.Left;
                double anchorY = dragged.Top - index * (h + gap); // osd_y ist IMMER die oberste Kante des Stapels, designunabhängig

                // Nicht außerhalb des Bildschirms ablegen.
                var (w, _, _) = OsdCard.Dims(cfg);
                var clamped = Placement.Clamp(new Rect(x, anchorY, w, StackHeight(cfg)));
                x = clamped.X;
                anchorY = clamped.Y;

                if (cfg.OsdFollowsMedia())
                {
                    // Stapel hängt am Media-Flyout: Ziehen verschiebt den Media-Anker so, dass der
                    // Stapel an der losgelassenen Stelle bleibt.
                    // Auch bei gerade unsichtbarem Flyout: dessen (zuletzt gesehene) Höhe zählt, damit der Stapel
                    // genau dort bleibt, wo er losgelassen wurde.
                    double mediaTop = cfg.MediaCombinedSide == "above"
                        ? anchorY - gap - MediaHeight()
                        : anchorY + _order.Count * (h + gap);
                    cfg.MediaOsdX = (int)Math.Round(x - Placement.CombinedOffset(cfg, OsdCard.Dims(cfg).W));
                    cfg.MediaOsdY = (int)Math.Round(mediaTop);
                    cfg.MediaCornerPreset = "custom"; // selbst verschoben → keine Ecken-Voreinstellung mehr
                    ConfigManager.Save(cfg);
                    MediaAnchorChanged?.Invoke();
                    Restack(cfg);
                    return;
                }

                const double snapThreshold = 40.0;
                foreach (var p in cfg.SnapPositions)
                {
                    if (Math.Abs(p.X - x) <= snapThreshold && Math.Abs(p.Y - anchorY) <= snapThreshold)
                    {
                        x = p.X;
                        anchorY = p.Y;
                        break;
                    }
                }

                cfg.OsdX = (int)Math.Round(x);
                cfg.OsdY = (int)Math.Round(anchorY);
                cfg.OsdCornerPreset = "custom"; // selbst verschoben → keine Ecken-Voreinstellung mehr
                ConfigManager.Save(cfg);

                Restack(cfg);
            }
            catch (Exception ex)
            {
                Logger.Error($"OSD drag: {ex.Message}");
            }
        }

        /// <summary>
        /// Entspricht _restack(): Karten von oben nach unten stapeln. osd_x/osd_y sind IMMER die
        /// linke obere Ecke des Stapels, für beide Designs identisch – so bleibt die Position beim
        /// Wechsel zwischen "dark" und "modern" exakt an derselben Bildschirmstelle (vorher sprang
        /// sie, weil "modern" oben und "dark" unten verankerte).
        /// </summary>
        private void Restack(AppConfig cfg)
        {
            var (_, h, gap) = OsdCard.Dims(cfg);
            int n = _order.Count;

            var (x, top) = StackOrigin(cfg);

            for (int i = 0; i < n; i++)
            {
                if (!_cards.TryGetValue(_order[i], out var card)) continue;
                card.Left = x;
                card.Top = top + i * (h + gap);
                if (!card.IsVisible)
                {
                    card.Show();
                    card.FadeIn();
                }
            }
            StackChanged?.Invoke();
        }

        /// <summary>
        /// Linke obere Ecke des Stapels. Normalfall: osd_x/osd_y (bzw. Standardecke). Hängt der Stapel
        /// am Media-Flyout (cfg.OsdFollowsMedia), IMMER direkt unter/über dem Flyout – ist es gerade nicht
        /// sichtbar, unter/über der Stelle, an der es erscheinen würde (gleiche Position mit und ohne Flyout).
        /// </summary>
        private (double X, double Top) StackOrigin(AppConfig cfg)
        {
            var (w, _, gap) = OsdCard.Dims(cfg);
            double stackH = StackHeight(cfg);
            Point origin;
            if (cfg.OsdFollowsMedia())
            {
                var m = MediaBounds?.Invoke();
                if (m != null && m.Value.Height > 0) _lastMediaHeight = m.Value.Height;
                var media = m != null
                    ? new Rect(m.Value.TopLeft, new Size(m.Value.Width, MediaHeight()))
                    : ExpectedMediaRect(cfg);

                // Gewünschte Seite; passt sie nicht auf den Bildschirm, automatisch die andere.
                var wa = Placement.WorkAreaFor(media);
                double below = media.Bottom + gap, above = media.Top - gap - stackH;
                bool wantBelow = cfg.MediaCombinedSide == "above"; // Media oben → Stapel darunter
                double top = wantBelow
                    ? (below + stackH <= wa.Bottom ? below : above)
                    : (above >= wa.Top ? above : below);
                origin = new Point(media.Left + Placement.CombinedOffset(cfg, w), top); // ggf. mittig (Design-System)
            }
            else
            {
                origin = Placement.OsdAnchor(cfg, w, stackH);
            }
            // Immer vollständig im Bildschirm halten.
            var c = Placement.Clamp(new Rect(origin, new Size(w, stackH)));
            // Eine feststehende Daueranzeige nicht überdecken: sie bleibt stehen, der Stapel weicht aus.
            if (FixedMediaBounds?.Invoke() is Rect fixedMedia)
            {
                var stack = new Rect(c, new Size(w, stackH));
                if (Placement.Overlaps(stack, fixedMedia)) c = Placement.AvoidOverlap(stack, fixedMedia);
            }
            return (c.X, c.Y);
        }

        /// <summary>Höhe des Media-Flyouts: sichtbar die echte, sonst die zuletzt gesehene (anfangs geschätzt).</summary>
        private double MediaHeight()
        {
            var m = MediaBounds?.Invoke();
            return m != null && m.Value.Height > 0 ? m.Value.Height : _lastMediaHeight;
        }

        /// <summary>Wo das gerade unsichtbare Media-Flyout erscheinen würde (wie MediaOsd.ApplyPosition).</summary>
        private Rect ExpectedMediaRect(AppConfig cfg)
        {
            var size = new Size(MediaOsd.FlyoutWidth, _lastMediaHeight);
            var p = Placement.Clamp(new Rect(Placement.MediaAnchor(cfg, size.Width, size.Height), size));
            return new Rect(p, size);
        }

        /// <summary>Höhe des Stapels (mindestens eine Karte).</summary>
        private double StackHeight(AppConfig cfg)
        {
            var (_, h, gap) = OsdCard.Dims(cfg);
            int n = Math.Max(1, _order.Count);
            return n * (h + gap) - gap;
        }

        /// <summary>Bildschirm-Rechteck des sichtbaren Stapels (null = keine Karte sichtbar).</summary>
        public Rect? StackBounds
        {
            get
            {
                if (_order.Count == 0 || !_cards.TryGetValue(_order[0], out var first)) return null;
                var cfg = ConfigManager.Load();
                var (w, _, _) = OsdCard.Dims(cfg);
                return new Rect(first.Left, first.Top, w, StackHeight(cfg));
            }
        }

        /// <summary>
        /// Aktuelle Position(en) als Snap-Punkt – so, wie sie tatsächlich erscheinen (Placement.Layout, wie
        /// „Position testen“): hängt das OSD am Media-Flyout, zählt die angehängte Stelle, nicht der eigene,
        /// dann unbenutzte OSD-Anker (sonst landete das OSD beim Wechseln auf diesen Punkt unter dem Flyout).
        /// Media-Position nur, wenn sie eigenständig ist.
        /// </summary>
        public static SnapPosition CurrentSnapPoint(AppConfig cfg)
        {
            var (osd, media) = Placement.Layout(cfg, MediaOsd.EstimatedHeight);
            var point = new SnapPosition { X = (int)Math.Round(osd.X), Y = (int)Math.Round(osd.Y) };
            if (!cfg.MediaFollowsOsd()) // hängt das Flyout am OSD, hat es keine eigene Position
            {
                point.MediaX = (int)Math.Round(media.X);
                point.MediaY = (int)Math.Round(media.Y);
            }
            return point;
        }

        /// <summary>Fenster-Rechteck des Media-Flyouts, falls sichtbar (von App.xaml.cs gesetzt).</summary>
        public Func<Rect?>? MediaBounds { get; set; }

        /// <summary>Fenster-Rechteck der Daueranzeige, wenn sie fest steht und nicht am Stapel hängt (von App.xaml.cs gesetzt).</summary>
        public Func<Rect?>? FixedMediaBounds { get; set; }

        /// <summary>Ein gezogener Stapel hat den Media-Anker verschoben (nur bei cfg.OsdFollowsMedia).</summary>
        public event Action? MediaAnchorChanged;

        /// <summary>Sichtbaren Stapel neu ausrichten (z.B. weil das Media-Flyout, an dem er hängt, bewegt wurde).</summary>
        public void Reposition()
        {
            if (_order.Count == 0) return;
            try { Restack(ConfigManager.Load()); }
            catch (Exception ex) { Logger.Error($"OSD reposition: {ex.Message}"); }
        }

        /// <summary>Feuert nach jedem Restack – App.xaml.cs nutzt das, um ein kombiniertes Media-Flyout neu zu positionieren.</summary>
        public event Action? StackChanged;

        /// <summary>
        /// Position für das kombinierte Media-Flyout direkt unterhalb (cfg.MediaCombinedSide "below")
        /// bzw. oberhalb ("above", dann zählt mediaHeight) des aktuellen Lautstärke-/Profil-Stapels.
        /// Ist der Stapel gerade leer (z.B. weil die Lautstärke-Karte gerade ausgeblendet wurde),
        /// liefert diese Methode null – der Aufrufer soll die Karte dann an ihrer aktuellen Stelle
        /// belassen statt sie an den bloßen Anker zurückzuziehen.
        /// </summary>
        /// <returns>Position und ob das Flyout tatsächlich oberhalb sitzt (passt die gewünschte Seite
        /// nicht auf den Bildschirm, wird automatisch die andere genommen).</returns>
        public (Point Pos, bool Above)? GetCombinedMediaPosition(AppConfig cfg, double mediaHeight)
        {
            if (_order.Count == 0) return null;

            var (w, _, gap) = OsdCard.Dims(cfg);
            var (x, top) = StackOrigin(cfg);
            double bottom = top + StackHeight(cfg);
            double h = mediaHeight > 0 ? mediaHeight : MediaOsd.EstimatedHeight;
            var wa = Placement.WorkAreaFor(new Rect(x, top, w, bottom - top));

            bool above = cfg.MediaCombinedSide == "above";
            if (above && top - gap - h < wa.Top) above = false;          // oben kein Platz → darunter
            else if (!above && bottom + gap + h > wa.Bottom) above = true; // unten kein Platz → darüber

            double y = above ? top - gap - mediaHeight : bottom + gap; // "oben": Höhe wächst nach oben (SizeChanged)
            double mediaX = x - Placement.CombinedOffset(cfg, w); // ggf. mittig unter/über dem OSD (Design-System)
            double clampedX = Placement.Clamp(new Rect(mediaX, top, MediaOsd.FlyoutWidth, 1)).X; // breiter als der Stapel → nicht rechts raus
            return (new Point(clampedX, y), above);
        }

        private void RestartTimer(int ms)
        {
            _timer.Stop();
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(100, ms));
            _timer.Start();
        }

        private void RemoveCard(string key)
        {
            if (_cards.TryGetValue(key, out var card))
            {
                try { card.Close(); } catch { }
                _cards.Remove(key);
            }
            _order.Remove(key);
        }

        /// <summary>Alle Karten außer keep sofort schließen (keep null = alle).</summary>
        private void RemoveAllExcept(string? keep)
        {
            foreach (var key in new List<string>(_cards.Keys))
                if (key != keep) RemoveCard(key);
        }

        private void HideAll()
        {
            foreach (var card in _cards.Values)
            {
                try { card.CloseAnimated(); } catch { }
            }
            _cards.Clear();
            _order.Clear();
        }

        /// <summary>Drag- und Klick-Ereignisse einer neuen Karte verbinden.</summary>
        private void Wire(OsdCard card)
        {
            card.Dragged += OnCardDragged;
            card.DismissRequested += () =>
            {
                HideNow();
                Dismissed?.Invoke();
            };
        }

        /// <summary>Stapel sofort ausblenden (ohne Dismissed auszulösen).</summary>
        public void HideNow()
        {
            _timer.Stop();
            HideAll();
        }

        /// <summary>Der Nutzer hat den Stapel per Klick ausgeblendet (cfg.ClickToDismiss).</summary>
        public event Action? Dismissed;
    }
}
