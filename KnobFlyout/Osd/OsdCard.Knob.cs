using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using KnobFlyout.Config;
using KnobFlyout.Profiles;

namespace KnobFlyout.Osd
{
    /// <summary>
    /// OSD-Karten im Design „Drehknopf“ (Designs.Knob): Lautstärke als flacher Knopf mit Pegel-Bogen, daneben App-Name und Zahl
    /// in normaler Schrift; Profil als Wahlschalter, Feststelltaste und Position mit rundem Symbol-Knopf.
    /// </summary>
    internal sealed partial class OsdCard
    {
        private const double KnobPad = 13; // 14 px minus 1 px Rand

        private bool IsKnob => ReferenceEquals(_spec, Designs.Knob);

        private CleanKnob? _knob;

        private Grid KnobRow(params GridLength[] columns)
        {
            var row = new Grid { Width = _w - 2 - 2 * KnobPad, Height = _h - 2 };
            foreach (var c in columns) row.ColumnDefinitions.Add(new ColumnDefinition { Width = c });
            Place(row, KnobPad, 0);
            return row;
        }

        private TextBlock KnobText(string text, double size, Brush fg, FontWeight weight) => new()
        {
            Text = text, FontFamily = _spec!.Font, FontSize = size, FontWeight = weight, Foreground = fg,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };

        /// <summary>Titel mit kleiner Unterzeile rechts neben dem Knopf.</summary>
        private StackPanel KnobTitle(TextBlock title, string sub)
        {
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 10, 0) };
            panel.Children.Add(title);
            var s = KnobText(sub, 12, _sub, FontWeights.Normal);
            s.Margin = new Thickness(0, 1, 0, 0);
            panel.Children.Add(s);
            return panel;
        }

        private Grid KnobIcon(string glyph) => KnobParts.Round(44, UiColor.Brush(_pal!.Tile ?? _pal.Button), UiColor.Brush(_pal.Edge), new TextBlock
        {
            Text = glyph, FontFamily = IconFont, FontSize = 17, Foreground = _fg,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });

        /// <summary>Lautstärke: Knopf · App-Name (darunter „Lautstärke“) · Zahl.</summary>
        private void BuildVolumeKnob()
        {
            var pal = _pal!;
            var row = KnobRow(GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto);
            _knob = new CleanKnob(48, _trackBrush, _accent, UiColor.Brush(pal.Tile ?? pal.Button), UiColor.Brush(pal.Edge), _fg,
                UiColor.Brush(pal.Mute), UiColor.Brush(pal.MutedFill ?? pal.Track));
            _knob.VerticalAlignment = VerticalAlignment.Center;
            Cell(row, _knob, 0);
            _lapp = KnobText("", 15, _fg, FontWeights.SemiBold);
            var title = KnobTitle(_lapp, Loc.T("Lautstärke", "Volume"));
            Cell(row, title, 1);
            _lpct = KnobText("", 24, _fg, FontWeights.SemiBold);
            _lpct.MinWidth = 40;
            _lpct.TextAlignment = TextAlignment.Right;
            System.Windows.Documents.Typography.SetNumeralAlignment(_lpct, FontNumeralAlignment.Tabular);
            Cell(row, _lpct, 2);

            _stretchBar = false; // der Knopf behält seine Größe, der Name füllt
            Part(OsdCardLayout.Bar, _knob);
            Part(OsdCardLayout.Name, title);
            Part(OsdCardLayout.Value, _lpct);
        }

        private void UpdateKnob(string clean, int pct, bool muted)
        {
            if (_lapp == null || _lpct == null || _knob == null) return;
            _lapp.Text = clean;
            _lpct.Text = muted ? Loc.T("Stumm", "Muted") : pct.ToString();
            _lpct.FontSize = muted ? 14 : 24;
            _lpct.Foreground = muted ? _sub : _fg;
            _knob.Set(pct, muted, IsVisible);
        }

        /// <summary>Profilwechsel: Wahlschalter (Punkte in Profilfarben, Zeiger aufs aktive) · Profilname / „Profil“.</summary>
        private void BuildProfileKnob()
        {
            var pal = _pal!;
            var profiles = ProfileDefinitions.For(_cfg);
            int idx = Math.Max(0, profiles.ToList().FindIndex(p => p.Key == _cfg.ActiveProfile));
            var row = KnobRow(GridLength.Auto, new GridLength(1, GridUnitType.Star));
            var dial = new CleanDial(48, profiles, profiles.Count > 0 ? idx : -1, UiColor.Brush(pal.Tile ?? pal.Button), UiColor.Brush(pal.Edge), _accent)
            {
                VerticalAlignment = VerticalAlignment.Center,
            };
            Cell(row, dial, 0);
            var title = KnobTitle(KnobText(profiles.Count > 0 ? profiles[idx].Name : "Profile", 16, _fg, FontWeights.SemiBold), Loc.T("Profil", "Profile"));
            Cell(row, title, 1);
            Part(OsdCardLayout.Icon, dial);
            Part(OsdCardLayout.Name, title);
        }

        /// <summary>Feststelltasten: Schloss im runden Knopf · Name / „An“ bzw. „Aus“ · Punkt (an = Akzent, aus = Ring).</summary>
        private void BuildLockKeyKnob(string name, bool on)
        {
            var pal = _pal!;
            string label = LockKeyName(_cfg, name);
            var row = KnobRow(GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto);
            var icon = KnobIcon(on ? LockGlyph : UnlockGlyph);
            Cell(row, icon, 0);
            var title = KnobTitle(KnobText(label, 15, _fg, FontWeights.SemiBold), on ? Loc.T("An", "On") : Loc.T("Aus", "Off"));
            Cell(row, title, 1);
            var state = new Ellipse
            {
                Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0),
                Fill = on ? UiColor.Brush(pal.On) : Brushes.Transparent, Stroke = on ? null : UiColor.Brush(pal.Sub), StrokeThickness = 1.5,
            };
            Cell(row, state, 2);
            Part(OsdCardLayout.Icon, icon);
            Part(OsdCardLayout.Name, title);
            Part(OsdCardLayout.State, state);
        }

        /// <summary>Positionswechsel: Stecknadel im runden Knopf · „Position n“ / „von N“ · Punkte (bis 8).</summary>
        private void BuildSnapInfoKnob(int index, int total)
        {
            var row = KnobRow(GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto);
            var icon = KnobIcon("\uE707"); // MapPin
            Cell(row, icon, 0);
            var title = KnobTitle(KnobText($"Position {index + 1}", 15, _fg, FontWeights.SemiBold), Loc.T($"von {total}", $"of {total}"));
            Cell(row, title, 1);
            Part(OsdCardLayout.Icon, icon);
            Part(OsdCardLayout.Name, title);
            if (total > 8) return;
            var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < total; i++)
                dots.Children.Add(new Ellipse
                {
                    Width = 7, Height = 7, Margin = new Thickness(i == 0 ? 0 : 7, 0, 0, 0),
                    Fill = i == index ? _accent : _trackBrush,
                });
            Cell(row, dots, 2);
            Part(OsdCardLayout.Bar, dots);
        }
    }
}
