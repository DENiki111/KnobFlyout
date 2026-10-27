using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KnobFlyout.Config;
using KnobFlyout.Profiles;

namespace KnobFlyout.Osd
{
    /// <summary>
    /// OSD-Karten im Design „Hardware-Regler“ (Designs.Hardware): Lautstärke als Drehknopf mit LED-Kranz + Sieben-Segment-Anzeige,
    /// Profil als Wahlschalter + Name im Display, Feststelltaste mit LED, Position als Speichertasten.
    /// </summary>
    internal sealed partial class OsdCard
    {
        private const double HwPad = 13;   // 14 px minus 1 px Rand
        private const double HwText = 81;  // Beschriftung rechts neben dem Knopf (Fenster-x 82)

        private bool IsHardware => ReferenceEquals(_spec, Designs.Hardware);

        private KnobView? _hwKnob;
        private SevenSegment? _hwSeg;
        private TextBlock? _hwMuteText;

        private HwColors Hw => HardwareParts.Colors(ThemeHelper.IsLight(_cfg));

        /// <summary>Lautstärke: Drehknopf mit LED-Kranz · APP-NAME / „LAUTSTÄRKE“ · Display mit dreistelliger Sieben-Segment-Zahl.</summary>
        private void BuildVolumeHardware()
        {
            var pal = _pal!;
            var hw = Hw;
            _hwKnob = new KnobView(hw, _accent, UiColor.Brush(pal.Mute));
            Place(_hwKnob, 9, 5);

            const double lcdW = 88, lcdH = 46;
            double lcdX = _w - 2 - HwPad - lcdW;
            _lapp = PrintText("", 12, _fg);
            Place(Labels(_lapp, HudParts.Caps(Loc.T("Lautstärke", "Volume")), lcdX - 12 - HwText), HwText, 0);

            _hwSeg = new SevenSegment(hw);
            _hwMuteText = new TextBlock
            {
                Text = Loc.T("STUMM", "MUTE"), FontFamily = HardwareParts.LcdFont, FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = UiColor.Brush(pal.Mute), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed, Effect = HudParts.Glow(UiColor.Brush(pal.Mute).Color, 0.6, 6),
            };
            var content = new Grid();
            content.Children.Add(_hwSeg);
            content.Children.Add(_hwMuteText);
            var lcd = HardwareParts.Lcd(hw, pal.Hi ?? "#00000000", content, new Thickness(0));
            lcd.Width = lcdW;
            lcd.Height = lcdH;
            Place(lcd, lcdX, (_h - 2 - lcdH) / 2);
        }

        private void UpdateHardware(string clean, int pct, bool muted)
        {
            if (_lapp == null || _hwKnob == null || _hwSeg == null || _hwMuteText == null) return;
            _lapp.Text = HudParts.Caps(clean);
            _hwKnob.Set(pct, muted, IsVisible);
            _hwSeg.Show(muted ? null : pct);
            _hwMuteText.Visibility = muted ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Profilwechsel: Wahlschalter (Punkte = Profile in ihren Farben, Zeiger + Logo des aktiven) und Name im Display.</summary>
        private void BuildProfileHardware()
        {
            var hw = Hw;
            var profiles = ProfileDefinitions.For(_cfg);
            int idx = Math.Max(0, profiles.ToList().FindIndex(p => p.Key == _cfg.ActiveProfile));
            Place(new DialView(hw, profiles, profiles.Count > 0 ? idx : -1), 9, 5);
            var name = LcdText(profiles.Count > 0 ? profiles[idx].Name.ToUpperInvariant() : "PROFILE", 19, hw);
            var lcd = HardwareParts.Lcd(hw, _pal!.Hi ?? "#00000000", name, new Thickness(12, 0, 12, 0));
            lcd.Width = _w - 2 - HwText - HwPad;
            lcd.Height = 44;
            Place(lcd, HwText, (_h - 2 - 44) / 2);
        }

        /// <summary>Feststelltasten: LED (grün leuchtend bzw. matt) · NAME / Tastenname · Display „AN“/„AUS“.</summary>
        private void BuildLockKeyHardware(string name, bool on)
        {
            var pal = _pal!;
            var hw = Hw;
            var (label, code) = name switch
            {
                "CapsLock" => (Loc.T("Feststelltaste", "Caps Lock"), "Caps Lock"),
                "NumLock" => (Loc.T("Nummernblock", "Num Lock"), "Num Lock"),
                _ => (Loc.T("Rollen", "Scroll Lock"), "Scroll Lock"),
            };
            Place(HardwareParts.Led(14, on ? pal.On : null, hw), 17, (_h - 2 - 14) / 2);
            const double lcdW = 64, lcdH = 36;
            double lcdX = _w - 2 - HwPad - lcdW;
            var title = PrintText(HudParts.Caps(label), 13, _fg);
            Place(Labels(title, HudParts.Caps(Loc.T(code, "Lock key")), lcdX - 12 - 45), 45, 0);
            var state = LcdText(on ? Loc.T("AN", "ON") : Loc.T("AUS", "OFF"), 16, hw);
            state.HorizontalAlignment = HorizontalAlignment.Center;
            if (!on)
            {
                state.Effect = null;
                state.Opacity = 0.35;
            }
            var lcd = HardwareParts.Lcd(hw, pal.Hi ?? "#00000000", state, new Thickness(0));
            lcd.Width = lcdW;
            lcd.Height = lcdH;
            Place(lcd, lcdX, (_h - 2 - lcdH) / 2);
        }

        /// <summary>Positionswechsel: „POSITION“ / „2 VON 3“ · Speichertasten 1–N (aktive gedrückt mit LED); ab 9 Positionen „2/12“ im Display.</summary>
        private void BuildSnapInfoHardware(int index, int total)
        {
            var hw = Hw;
            const double left = 21;
            var title = PrintText(HudParts.Caps("Position"), 13, _fg);
            Place(Labels(title, HudParts.Caps(Loc.T($"{index + 1} von {total}", $"{index + 1} of {total}")), 100), left, 0);

            var host = new Grid { Width = _w - 2 - HwPad - left - 110, Height = _h - 2 };
            if (total <= 8)
            {
                double size = Math.Min(28, (host.Width - 6.0 * (total - 1)) / Math.Max(1, total));
                var keys = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                for (int i = 0; i < total; i++)
                {
                    bool on = i == index;
                    var key = HardwareParts.Key(hw, size, size, out var face, on);
                    key.Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0);
                    var inner = new Grid();
                    inner.Children.Add(HardwareParts.KeyLed(10, on, _accent, hw));
                    inner.Children.Add(new TextBlock
                    {
                        Text = (i + 1).ToString(), FontFamily = _spec!.Font, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = _fg,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 0, 0),
                    });
                    face.Child = inner;
                    keys.Children.Add(key);
                }
                host.Children.Add(keys);
            }
            else
            {
                var lcd = HardwareParts.Lcd(hw, _pal!.Hi ?? "#00000000", LcdText($"{index + 1}/{total}", 16, hw), new Thickness(10, 0, 10, 0));
                lcd.Height = 36;
                lcd.HorizontalAlignment = HorizontalAlignment.Right;
                lcd.VerticalAlignment = VerticalAlignment.Center;
                host.Children.Add(lcd);
            }
            Place(host, left + 110, 0);
        }

        /// <summary>Beschriftung wie aufgedruckt: Titel und kleine graue Unterzeile, senkrecht mittig in der Kartenhöhe.</summary>
        private Grid Labels(TextBlock title, string sub, double width)
        {
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(title);
            var s = PrintText(sub, 9.5, _sub);
            s.Margin = new Thickness(0, 4, 0, 0);
            panel.Children.Add(s);
            var host = new Grid { Width = Math.Max(10, width), Height = _h - 2 };
            host.Children.Add(panel);
            return host;
        }

        private TextBlock PrintText(string text, double size, Brush fg) => new()
        {
            Text = text, FontFamily = _spec!.Font, FontSize = size, FontWeight = FontWeights.SemiBold, Foreground = fg,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        /// <summary>Display-Schrift (Bernstein, Mono) mit leichtem Schein.</summary>
        private static TextBlock LcdText(string text, double size, HwColors hw) => new()
        {
            Text = text, FontFamily = HardwareParts.LcdFont, FontSize = size, FontWeight = FontWeights.Bold,
            Foreground = UiColor.Brush(hw.LcdText), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            Effect = HardwareParts.AmberGlow(hw),
        };
    }
}
