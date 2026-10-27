using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using static KnobFlyout.Settings.UiHelpers;

namespace KnobFlyout.Settings
{
    /// <summary>
    /// Farbkreis zum Auswählen einer Farbe: Farbton rundherum, Sättigung nach außen, darunter ein
    /// Helligkeits-Slider sowie Vorschau + Hex-Feld. Jede Änderung meldet sofort den neuen Hex-Wert
    /// (onChanged). Der Aufrufer hängt das Popup in seinen Fensterbaum, damit die Theme-Stile greifen.
    /// </summary>
    internal sealed class ColorWheelPopup : Popup
    {
        private const int WheelSize = 180;
        private const double Radius = WheelSize / 2.0;
        private static BitmapSource? _wheelBitmap;

        private readonly Action<string> _onChanged;
        private readonly Ellipse _marker;
        private readonly Ellipse _shade;
        private readonly Border _preview;
        private readonly TextBox _hex;
        private readonly Slider _value;
        private double _h, _s, _v = 1;
        private bool _dragging;
        private bool _updating;

        public ColorWheelPopup(UIElement target, string currentHex, Action<string> onChanged)
        {
            _onChanged = onChanged;
            PlacementTarget = target;
            Placement = PlacementMode.Bottom;
            StaysOpen = false;
            AllowsTransparency = true;
            PopupAnimation = PopupAnimation.Fade;

            // ── Farbkreis + Abdunkelung (Helligkeit) + Markierung ──
            var wheel = new Grid { Width = WheelSize, Height = WheelSize, Background = Brushes.Transparent, Cursor = Cursors.Cross };
            wheel.Children.Add(new Image { Source = WheelBitmap(), Width = WheelSize, Height = WheelSize });
            _shade = new Ellipse { Fill = Brushes.Black, Opacity = 0, IsHitTestVisible = false };
            wheel.Children.Add(_shade);
            var markerLayer = new Canvas { IsHitTestVisible = false };
            _marker = new Ellipse
            {
                Width = 16, Height = 16, Stroke = Brushes.White, StrokeThickness = 2.5,
                Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.8 },
            };
            markerLayer.Children.Add(_marker);
            wheel.Children.Add(markerLayer);

            wheel.MouseLeftButtonDown += (_, e) =>
            {
                _dragging = true;
                wheel.CaptureMouse();
                Pick(e.GetPosition(wheel));
                e.Handled = true;
            };
            wheel.MouseMove += (_, e) => { if (_dragging) Pick(e.GetPosition(wheel)); };
            wheel.MouseLeftButtonUp += (_, e) =>
            {
                _dragging = false;
                wheel.ReleaseMouseCapture();
                e.Handled = true;
            };

            // ── Helligkeit ──
            _value = new Slider { Minimum = 0, Maximum = 100, Width = WheelSize, Margin = new Thickness(0, 10, 0, 0) };
            _value.ValueChanged += (_, e) =>
            {
                if (_updating) return;
                _v = e.NewValue / 100.0;
                Refresh(notify: true);
            };

            // ── Vorschau + Hex ──
            _preview = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(6), BorderBrush = FieldBorder, BorderThickness = new Thickness(1) };
            _hex = TextField("", 110);
            _hex.Margin = new Thickness(10, 0, 0, 0);
            _hex.VerticalAlignment = VerticalAlignment.Center;
            _hex.TextChanged += (_, _) =>
            {
                if (_updating || !TryParse(_hex.Text, out var c)) return;
                (_h, _s, _v) = ToHsv(c);
                Refresh(notify: true, keepHexText: true);
            };
            var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            hexRow.Children.Add(_preview);
            hexRow.Children.Add(_hex);

            var content = new StackPanel();
            content.Children.Add(wheel);
            content.Children.Add(new TextBlock { Text = Loc.T("Helligkeit", "Brightness"), Foreground = SubFg, FontSize = 12, Margin = new Thickness(0, 10, 0, 0) });
            content.Children.Add(_value);
            content.Children.Add(hexRow);

            Child = new Border
            {
                Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = new Thickness(8), // Rand = Platz für den Schatten
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.35 },
                Child = content,
            };

            if (TryParse(currentHex, out var start)) (_h, _s, _v) = ToHsv(start);
            Refresh(notify: false);
        }

        /// <summary>Punkt im Kreis → Farbton (Winkel) und Sättigung (Abstand zur Mitte).</summary>
        private void Pick(Point p)
        {
            double dx = p.X - Radius, dy = p.Y - Radius;
            _s = Math.Min(1.0, Math.Sqrt(dx * dx + dy * dy) / Radius);
            _h = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
            if (_v < 0.05) _v = 1; // bei Schwarz wäre der gewählte Farbton unsichtbar
            Refresh(notify: true);
        }

        private void Refresh(bool notify, bool keepHexText = false)
        {
            double angle = _h * Math.PI / 180;
            Canvas.SetLeft(_marker, Radius + Math.Cos(angle) * _s * Radius - _marker.Width / 2);
            Canvas.SetTop(_marker, Radius + Math.Sin(angle) * _s * Radius - _marker.Height / 2);
            _shade.Opacity = 1 - _v;

            var color = FromHsv(_h, _s, _v);
            string hex = $"#{color.R:x2}{color.G:x2}{color.B:x2}";
            _preview.Background = new SolidColorBrush(color);

            _updating = true;
            _value.Value = _v * 100;
            if (!keepHexText) _hex.Text = hex;
            _updating = false;

            if (notify) _onChanged(hex);
        }

        // ── Farbrechnung ──────────────────────────────────────────────────────

        private static bool TryParse(string? text, out Color color)
        {
            color = Colors.Transparent;
            var t = (text ?? "").Trim();
            if (t.Length != 7 || t[0] != '#') return false; // nur #rrggbb, damit halb getippte Werte nicht springen
            try { color = (Color)ColorConverter.ConvertFromString(t); return true; }
            catch { return false; }
        }

        private static (double H, double S, double V) ToHsv(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            double h = 0;
            if (d > 0)
            {
                if (max == r) h = 60 * (((g - b) / d) % 6);
                else if (max == g) h = 60 * ((b - r) / d + 2);
                else h = 60 * ((r - g) / d + 4);
            }
            if (h < 0) h += 360;
            return (h, max == 0 ? 0 : d / max, max);
        }

        private static Color FromHsv(double h, double s, double v)
        {
            double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
            (double r, double g, double b) = (h % 360) switch
            {
                < 60 => (c, x, 0d),
                < 120 => (x, c, 0d),
                < 180 => (0d, c, x),
                < 240 => (0d, x, c),
                < 300 => (x, 0d, c),
                _ => (c, 0d, x),
            };
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }

        /// <summary>Farbkreis (volle Helligkeit) einmalig berechnen; Rand weich geglättet.</summary>
        private static BitmapSource WheelBitmap()
        {
            if (_wheelBitmap != null) return _wheelBitmap;
            int n = WheelSize;
            var pixels = new byte[n * n * 4];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    double dx = x + 0.5 - Radius, dy = y + 0.5 - Radius;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d > Radius) continue;
                    double h = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                    var c = FromHsv(h, d / Radius, 1);
                    int i = (y * n + x) * 4;
                    pixels[i] = c.B;
                    pixels[i + 1] = c.G;
                    pixels[i + 2] = c.R;
                    pixels[i + 3] = (byte)Math.Round(Math.Clamp(Radius - d, 0, 1) * 255);
                }
            }
            var bmp = BitmapSource.Create(n, n, 96, 96, PixelFormats.Bgra32, null, pixels, n * 4);
            bmp.Freeze();
            return _wheelBitmap = bmp;
        }
    }
}
