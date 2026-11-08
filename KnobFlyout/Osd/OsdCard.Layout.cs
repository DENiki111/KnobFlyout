using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KnobFlyout.Config;

namespace KnobFlyout.Osd
{
    /// <summary>
    /// Eigener Aufbau der OSD-Karten (OsdCardLayout): Jedes Design meldet beim Bauen seine Teile (Symbol, Name, Leiste, Zahl,
    /// Status …) in seiner Reihenfolge. Ist der Aufbau unverändert, bleibt die Karte genau wie gestaltet. Sonst stehen die
    /// sichtbaren Teile in der gewählten Reihenfolge in einer Zeile; die Leiste (bzw. ohne Leiste der Name) füllt den Platz.
    /// </summary>
    internal sealed partial class OsdCard
    {
        private const double CustomGap = 12;

        private readonly List<(string Key, FrameworkElement[] Els, Func<FrameworkElement>? Make)> _parts = new();
        private bool _stretchBar = true;                       // Hardware: der Drehknopf hat eine feste Größe
        private (string Name, int Pct, bool Muted)? _lastVolume; // für Leisten, die erst nach dem Anordnen ihre Breite kennen

        /// <summary>Teil der Karte; mehrere Elemente stehen im eigenen Aufbau untereinander (z. B. Titel und Unterzeile).</summary>
        private void Part(string key, params FrameworkElement[] els) => _parts.Add((key, els, null));

        /// <summary>Teil, das für den eigenen Aufbau neu gebaut wird (Leisten, die sich der Breite anpassen müssen).</summary>
        private void PartMade(string key, Func<FrameworkElement> make, params FrameworkElement[] els) => _parts.Add((key, els, make));

        /// <summary>Teile, die das Design für diese Karte hat, in seiner Reihenfolge (für „Design anpassen“).</summary>
        public static List<string> PartKeys(AppConfig cfg, string kind)
        {
            var card = kind switch
            {
                OsdCardLayout.Volume => CreateVolume(cfg, "spotify.exe"),
                OsdCardLayout.Profile => CreateProfile(cfg),
                OsdCardLayout.LockKey => CreateLockKey(cfg, "CapsLock", true),
                _ => CreateSnapInfo(cfg, 0, 3),
            };
            var keys = card._parts.Select(p => p.Key).ToList();
            card.Close(); // nie angezeigt
            return keys;
        }

        /// <summary>Nach dem Bauen: bei eigenem Aufbau alles, was das Design ab Index built gezeichnet hat, durch eine Zeile der Teile ersetzen.</summary>
        private void ApplyLayout(string kind, int built)
        {
            var keys = _parts.Select(p => p.Key).ToList();
            var layout = _cfg.CardLayout();
            if (keys.Count == 0 || layout.IsDefault(kind, keys)) return;

            var shown = new List<(string Key, FrameworkElement El)>();
            foreach (var key in layout.OrderFor(kind, keys))
            {
                var (_, els, make) = _parts.First(p => p.Key == key);
                if (!layout.Shows(kind, key))
                {
                    foreach (var el in els) Detach(el); // z. B. das Symbol im macOS-Regler
                    continue;
                }
                shown.Add((key, make != null ? make() : Join(els)));
            }
            while (_canvas.Children.Count > built) _canvas.Children.RemoveAt(_canvas.Children.Count - 1);
            if (IsMac && kind == OsdCardLayout.Volume && _glyph != null) _glyph.Foreground = _fg; // steht nicht mehr auf der Füllung

            string? align = layout.Align; // ganze Karte links/mittig/rechts: nichts füllt, die Leiste bekommt eine feste Breite
            bool barStretches = align == null && _stretchBar && shown.Any(s => s.Key == OsdCardLayout.Bar);
            string? stretch = kind == OsdCardLayout.Volume && barStretches ? OsdCardLayout.Bar
                : align == null && shown.Any(s => s.Key == OsdCardLayout.Name) ? OsdCardLayout.Name
                : barStretches ? OsdCardLayout.Bar : null;
            bool lead = stretch == null && align != "left", trail = stretch == null && align != "right";
            double border = _shell.BorderThickness.Left;
            double h = _h - 2 * border;
            var (padLeft, padRight) = CustomPad();
            double width = _w - 2 * border - padLeft - padRight;
            var row = new Grid { Width = width, Height = h };
            if (lead) row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // ohne füllendes Teil: Platz davor bzw. danach
            for (int i = 0; i < shown.Count; i++)
            {
                var (key, el) = shown[i];
                bool fill = key == stretch;
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = fill ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
                el.Margin = new Thickness(i == 0 ? 0 : CustomGap, 0, 0, 0);
                el.VerticalAlignment = VerticalAlignment.Center;
                if (el.Height > h) el.Height = double.NaN;
                if (fill)
                {
                    el.Width = double.NaN;
                    el.HorizontalAlignment = HorizontalAlignment.Stretch;
                }
                else if (key == OsdCardLayout.Name) el.MaxWidth = width * 0.45; // sonst bliebe für die Leiste nichts
                else if (key == OsdCardLayout.Bar && align != null && _stretchBar) el.Width = width * 0.38;
                Grid.SetColumn(el, row.ColumnDefinitions.Count - 1);
                row.Children.Add(el);
            }
            if (trail) row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Place(row, padLeft, 0);
        }

        /// <summary>Innenabstand links/rechts wie im jeweiligen Design.</summary>
        private (double Left, double Right) CustomPad() =>
            IsMac ? (MacPad, MacPad) : IsMaterial ? (M3PadLeft, M3PadRight) : IsHud ? (HudPadLeft, HudPadRight) : IsHardware ? (HwPad, HwPad) : IsKnob ? (KnobPad, KnobPad)
            : _spec != null ? (20, 22) : _win11 ? (12, 12) : _modern ? (16, 16) : (12, 12);

        /// <summary>Elemente aus ihrem bisherigen Platz lösen; mehrere stehen untereinander.</summary>
        private static FrameworkElement Join(FrameworkElement[] els)
        {
            foreach (var el in els)
            {
                Detach(el);
                if (el is TextBlock text)
                {
                    text.Width = double.NaN;
                    text.Height = double.NaN;
                    text.TextTrimming = TextTrimming.CharacterEllipsis;
                }
            }
            if (els.Length == 1) return els[0];
            var stack = new StackPanel();
            for (int i = 0; i < els.Length; i++)
            {
                els[i].Margin = new Thickness(0, i == 0 ? 0 : 2, 0, 0);
                stack.Children.Add(els[i]);
            }
            return stack;
        }

        private static void Detach(FrameworkElement el)
        {
            switch (el.Parent)
            {
                case Panel panel: panel.Children.Remove(el); break;
                case Decorator decorator: decorator.Child = null; break;
                case ContentControl control: control.Content = null; break;
            }
        }

        /// <summary>Leiste, deren Breite erst die Zeile festlegt: bei jeder Größenänderung neu ausrichten und den letzten Wert neu zeigen.</summary>
        private void Track(FrameworkElement host, Action<double> resize) => host.SizeChanged += (_, e) =>
        {
            if (!e.WidthChanged) return;
            resize(e.NewSize.Width);
            if (_lastVolume is { } v) UpdateVolume(v.Name, v.Pct, v.Muted);
        };

        /// <summary>Feste Leiste der Infokarten (Anteil gefüllt), dehnbar.</summary>
        private static Grid StaticBar(double fraction, Brush track, Brush fill, double height, double radius)
        {
            fraction = Math.Clamp(fraction, 0, 1);
            var bar = new Grid { Width = 80, Height = height };
            bar.Children.Add(new Border { Background = track, CornerRadius = new CornerRadius(radius) });
            var fillHost = new Grid();
            fillHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star) });
            fillHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fraction, GridUnitType.Star) });
            fillHost.Children.Add(new Border { Background = fill, CornerRadius = new CornerRadius(radius) });
            bar.Children.Add(fillHost);
            return bar;
        }

        /// <summary>Regler von ModernFlyouts und Windows 11 (Spur, Füllung, Griff) als eigene, dehnbare Zeichenfläche.</summary>
        private FrameworkElement CustomSliderBar()
        {
            var bg = _barBg!;
            var fill = _barFill!;
            var thumb = _thumb!;
            double size = thumb.Height;
            var host = new Canvas { Height = size };
            foreach (var el in new FrameworkElement[] { bg, fill, thumb })
            {
                Detach(el);
                host.Children.Add(el);
            }
            Canvas.SetLeft(bg, 0);
            Canvas.SetTop(bg, (size - bg.Height) / 2);
            Canvas.SetLeft(fill, 0);
            Canvas.SetTop(fill, (size - fill.Height) / 2);
            Canvas.SetTop(thumb, 0);
            _trackLeft = 0;
            Track(host, w =>
            {
                _trackW = w;
                bg.Width = w;
            });
            return host;
        }
    }
}
