using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using KnobFlyout.Profiles;

namespace KnobFlyout.Osd
{
    /// <summary>
    /// OSD-Karten im Design „macOS“ (Designs.Mac): Lautstärke als dicker Control-Center-Regler mit Symbol im
    /// Regler, Profil als App-Kachel + Name, Feststelltaste mit Schalter, Position mit Seitenpunkten.
    /// </summary>
    internal sealed partial class OsdCard
    {
        private const double MacPad = 13;       // 14 px Innenabstand minus 1 px Rand (die Zeichenfläche liegt im Rand)
        private const double MacSlider = 22;    // Höhe des Reglers = Durchmesser des Knopfs
        private const double MacTileSize = 36;

        private bool IsMac => ReferenceEquals(_spec, Designs.Mac);

        private TranslateTransform? _macShift;  // Füllung + Knopf werden nur verschoben (kein Neuaufbau je Raste)
        private Border? _macFill;
        private double _macTravel;              // Weg des Knopfs von 0 bis 100 %

        /// <summary>Lautstärke: App-Name und Zahl oben, darunter der Regler (Spur, weiße Füllung mit Knopf, Stufen-Symbol links).</summary>
        private void BuildVolumeMac()
        {
            double width = _w - 2 - 2 * MacPad;
            var head = new Grid { Width = width, Height = 16 };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _lapp = MacText("", 13, _fg, FontWeights.SemiBold);
            Cell(head, _lapp, 0);
            _lpct = MacText("", 12, _sub, FontWeights.Normal);
            _lpct.Margin = new Thickness(8, 0, 0, 0);
            System.Windows.Documents.Typography.SetNumeralAlignment(_lpct, FontNumeralAlignment.Tabular);
            Cell(head, _lpct, 1);
            Place(head, MacPad, 9);

            // Spur rund beschnitten; Füllung und Knopf als eine Gruppe, die nach links aus der Spur geschoben wird.
            _macTravel = width - MacSlider;
            var track = new Grid
            {
                Width = width, Height = MacSlider, Background = _trackBrush,
                Clip = new RectangleGeometry(new Rect(0, 0, width, MacSlider), MacSlider / 2, MacSlider / 2),
            };
            _macShift = new TranslateTransform(-_macTravel, 0);
            var mover = new Grid { Width = width, Height = MacSlider, RenderTransform = _macShift };
            _macFill = new Border { CornerRadius = new CornerRadius(MacSlider / 2), Background = _accent };
            mover.Children.Add(_macFill);
            mover.Children.Add(MacParts.Knob(MacSlider, HorizontalAlignment.Right));
            track.Children.Add(mover);
            _glyph = new TextBlock
            {
                Text = "\uE995", FontFamily = IconFont, FontSize = 14, Width = 14, TextAlignment = TextAlignment.Center,
                Foreground = UiColor.Brush(_pal!.OnFill ?? _pal.Sub), Margin = new Thickness(4, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            };
            track.Children.Add(_glyph);
            Place(track, MacPad, 32);
        }

        private void UpdateMac(string clean, int pct, bool muted)
        {
            if (_lapp == null || _lpct == null || _glyph == null || _macShift == null || _macFill == null || _pal == null) return;
            _lapp.Text = clean;
            _glyph.Text = muted ? "\uE74F" : pct == 0 ? "\uE992" : pct < 34 ? "\uE993" : pct < 67 ? "\uE994" : "\uE995";
            _lpct.Text = muted ? Loc.T("Stumm", "Muted") : pct.ToString();
            // Stumm: Füllung bleibt beim Pegel, wird aber grau.
            _macFill.Background = muted ? UiColor.Brush(_pal.MutedFill ?? _pal.Sub) : _accent;
            double x = -_macTravel * (1 - pct / 100.0);
            if (IsVisible)
                _macShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, System.TimeSpan.FromMilliseconds(90)));
            else
            {
                _macShift.BeginAnimation(TranslateTransform.XProperty, null);
                _macShift.X = x;
            }
        }

        /// <summary>Profilwechsel: nur Kachel (Profilfarbe mit weißem Logo bzw. eigenes Bild) und Profilname.</summary>
        private void BuildProfileMac()
        {
            var profiles = ProfileDefinitions.For(_cfg);
            var def = profiles.FirstOrDefault(p => p.Key == _cfg.ActiveProfile) ?? profiles.FirstOrDefault();
            var row = MacRow(GridLength.Auto, new GridLength(1, GridUnitType.Star));
            Cell(row, def != null ? MacParts.ProfileTile(def, MacTileSize, 18)
                : MacParts.GlyphTile("\uE734", MacTileSize, 18, UiColor.Brush(_pal!.Tile ?? _pal.Button), _fg), 0);
            var name = MacText(def?.Name ?? "Profile", 15, _fg, FontWeights.SemiBold);
            name.Margin = new Thickness(12, 0, 0, 0);
            Cell(row, name, 1);
        }

        /// <summary>Feststelltasten: Schloss-Kachel · Name + „Ein“/„Aus“ · Schalter.</summary>
        private void BuildLockKeyMac(string name, bool on)
        {
            string label = name switch
            {
                "CapsLock" => Loc.T("Feststelltaste", "Caps Lock"),
                "NumLock" => Loc.T("Nummernblock", "Num Lock"),
                _ => Loc.T("Rollen", "Scroll Lock"),
            };
            var row = MacRow(GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto);
            Cell(row, MacParts.GlyphTile(on ? LockGlyph : UnlockGlyph, MacTileSize, 18, UiColor.Brush(_pal!.Tile ?? _pal.Button), _fg), 0);
            Cell(row, MacTitle(label, on ? Loc.T("An", "On") : Loc.T("Aus", "Off")), 1);
            Cell(row, MacParts.Switch(on, _pal), 2);
        }

        /// <summary>Positionswechsel: Stecknadel-Kachel · „Position n“ + „von N“ · Seitenpunkte (bis 8).</summary>
        private void BuildSnapInfoMac(int index, int total)
        {
            var row = MacRow(GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto);
            Cell(row, MacParts.GlyphTile("\uE707", MacTileSize, 18, UiColor.Brush(_pal!.Tile ?? _pal.Button), _fg), 0); // MapPin
            Cell(row, MacTitle($"Position {index + 1}", Loc.T($"von {total}", $"of {total}")), 1);
            if (total > 8) return;
            var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < total; i++)
                dots.Children.Add(new Ellipse
                {
                    Width = 7, Height = 7, Margin = new Thickness(i == 0 ? 0 : 7, 0, 0, 0),
                    Fill = i == index ? _fg : _trackBrush,
                });
            Cell(row, dots, 2);
        }

        /// <summary>Zeile über die ganze Karte (Innenabstand 14 px), senkrecht mittig.</summary>
        private Grid MacRow(params GridLength[] columns)
        {
            var row = new Grid { Width = _w - 2 - 2 * MacPad, Height = _h - 2 };
            foreach (var c in columns) row.ColumnDefinitions.Add(new ColumnDefinition { Width = c });
            Place(row, MacPad, 0);
            return row;
        }

        /// <summary>Titel (fett) mit Unterzeile, links neben der Kachel.</summary>
        private StackPanel MacTitle(string title, string sub)
        {
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 10, 0) };
            panel.Children.Add(MacText(title, 13, _fg, FontWeights.SemiBold));
            var s = MacText(sub, 12, _sub, FontWeights.Normal);
            s.Margin = new Thickness(0, 1, 0, 0);
            panel.Children.Add(s);
            return panel;
        }

        private TextBlock MacText(string text, double size, Brush fg, FontWeight weight) => new()
        {
            Text = text, FontFamily = _spec!.Font, FontSize = size, FontWeight = weight, Foreground = fg,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
    }
}
