using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KnobFlyout.Osd;
using static KnobFlyout.Loc;
using static KnobFlyout.Settings.UiHelpers;

namespace KnobFlyout.Settings
{
    /// <summary>
    /// Platz-Auswahl für OSD und Media-Flyout bei Anordnung „Getrennt“ in einem Bildschirm mit 3×3 Kacheln:
    /// Rechtsklick setzt das OSD, Linksklick das Flyout (auch auf „Eigene Position“). Die Bildschirm-Auswahl zeigt nur einen
    /// Bildschirm an; ein Klick setzt das jeweilige Teil auf diesen Bildschirm. Liegt eins auf einem anderen Bildschirm, steht das darunter.
    /// </summary>
    internal sealed class DualTilePicker : StackPanel
    {
        private readonly List<MonitorArea> _monitors;
        private readonly ComboBox _monitorCombo;
        private readonly double _width, _maxScreenHeight;
        private readonly Border _screen;
        private readonly Border[] _tiles = new Border[9];
        private readonly TextBlock[] _labels = new TextBlock[9];
        private readonly TextBlock _status;
        private readonly Button? _customButton;
        private List<(string Tile, string Device, string Text)> _marks = new();
        private string _shown; // angezeigter Bildschirm ("" = Hauptbildschirm)

        // Feste Farben, damit man beide auseinanderhält: OSD blau, Flyout gelb
        public static readonly Color OsdColor = Color.FromRgb(0x3B, 0x82, 0xF6);
        public static readonly Color FlyoutColor = Color.FromRgb(0xF5, 0xC2, 0x18);
        private static readonly SolidColorBrush OsdBrush = Frozen(OsdColor), FlyoutBrush = Frozen(FlyoutColor);
        private static SolidColorBrush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
        private int _hover = -1;

        public string OsdPreset { get; private set; }
        public string OsdDevice { get; private set; }
        public string MediaPreset { get; private set; }
        public string MediaDevice { get; private set; }

        /// <summary>Vom Benutzer geändert: true = OSD, false = Flyout.</summary>
        public event Action<bool>? Changed;

        /// <param name="allowCustom">Knopf „Eigene Position“ (nicht beim Anlegen gespeicherter Positionen).</param>
        public DualTilePicker(List<MonitorArea> monitors, string? osdDevice, string? osdPreset, string? mediaDevice, string? mediaPreset,
            double width = 300, double maxScreenHeight = 190, bool allowCustom = true)
        {
            _monitors = monitors;
            _width = width;
            _maxScreenHeight = maxScreenHeight;
            OsdDevice = osdDevice ?? "";
            MediaDevice = mediaDevice ?? "";
            OsdPreset = Valid(osdPreset);
            MediaPreset = Valid(mediaPreset);
            _shown = OsdDevice;

            _monitorCombo = Combo(width, ScreenTilePicker.MonitorIndexOf(monitors, _shown), ScreenTilePicker.MonitorItems(monitors));
            _monitorCombo.HorizontalAlignment = HorizontalAlignment.Left;
            _monitorCombo.SelectionChanged += (_, _) =>
            {
                _shown = ScreenTilePicker.DeviceAt(_monitors, _monitorCombo.SelectedIndex);
                SizeScreen();
                Paint();
            };
            Children.Add(_monitorCombo);

            var grid = new Grid { Margin = new Thickness(3) };
            for (int i = 0; i < 3; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition());
            }
            for (int i = 0; i < ScreenTilePicker.Tiles.Length; i++)
            {
                int idx = i;
                var label = new TextBlock { FontSize = 10, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var tile = new Border
                {
                    CornerRadius = new CornerRadius(3), Margin = new Thickness(2), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Child = label,
                    ToolTip = ScreenTilePicker.TileName(ScreenTilePicker.Tiles[i]) + "\n" + T("Linksklick: Flyout · Rechtsklick: OSD", "Left click: flyout · Right click: OSD"),
                };
                tile.MouseEnter += (_, _) => { _hover = idx; Paint(); };
                tile.MouseLeave += (_, _) => { if (_hover == idx) _hover = -1; Paint(); };
                tile.MouseLeftButtonUp += (_, _) => Pick(false, ScreenTilePicker.Tiles[idx]);
                tile.MouseRightButtonUp += (_, e) =>
                {
                    e.Handled = true; // kein Kontextmenü
                    Pick(true, ScreenTilePicker.Tiles[idx]);
                };
                Grid.SetRow(tile, i / 3);
                Grid.SetColumn(tile, i % 3);
                grid.Children.Add(tile);
                _tiles[i] = tile;
                _labels[i] = label;
            }
            _screen = new Border
            {
                Child = grid, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(2),
                BorderBrush = FieldBorder, Background = ContentBg, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0),
            };
            Children.Add(_screen);

            var footer = new DockPanel { Margin = new Thickness(0, 4, 0, 0), Width = width, HorizontalAlignment = HorizontalAlignment.Left };
            if (allowCustom)
            {
                var custom = _customButton = Btn(T("Eigene Position", "Own position"));
                custom.Margin = new Thickness(6, 0, 0, 0);
                custom.ToolTip = T("Verschobene Position bzw. Snap-Punkt verwenden\nLinksklick: Flyout · Rechtsklick: OSD",
                    "Use the dragged position or snap point\nLeft click: flyout · Right click: OSD");
                custom.Click += (_, _) => Pick(false, "custom");
                custom.MouseRightButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    Pick(true, "custom");
                };
                DockPanel.SetDock(custom, Dock.Right);
                footer.Children.Add(custom);
            }
            _status = new TextBlock { Foreground = SubFg, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            footer.Children.Add(_status);
            Children.Add(footer);

            SizeScreen();
            Paint();
        }

        private static string Valid(string? preset) => Array.IndexOf(ScreenTilePicker.Tiles, preset) >= 0 ? preset! : "custom";

        private void Pick(bool osd, string preset)
        {
            if (osd)
            {
                OsdPreset = preset;
                if (preset != "custom") OsdDevice = _shown;
            }
            else
            {
                MediaPreset = preset;
                if (preset != "custom") MediaDevice = _shown;
            }
            Paint();
            Changed?.Invoke(osd);
        }

        /// <summary>Von außen setzen (z. B. „Position zurücksetzen“), ohne Changed auszulösen; device null = Bildschirm behalten.</summary>
        public void SetOsd(string preset, string? device = null)
        {
            OsdPreset = Valid(preset);
            if (device != null) OsdDevice = device;
            Paint();
        }

        public void SetMedia(string preset, string? device = null)
        {
            MediaPreset = Valid(preset);
            if (device != null) MediaDevice = device;
            Paint();
        }

        /// <summary>Weitere Beschriftungen in den Kacheln (z. B. „1 OSD“ für gespeicherte Positionen).</summary>
        public void SetMarks(IEnumerable<(string Tile, string Device, string Text)> marks)
        {
            _marks = marks.ToList();
            Paint();
        }

        /// <summary>Gleicher Bildschirm? "" und der Gerätename des Hauptbildschirms sind dasselbe.</summary>
        private bool Same(string a, string b)
        {
            string Norm(string d) => string.IsNullOrEmpty(d) ? _monitors.FirstOrDefault(m => m.Primary)?.Device ?? "" : d;
            return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
        }

        private void SizeScreen()
        {
            var mon = _monitors.FirstOrDefault(m => !string.IsNullOrEmpty(_shown) && m.Device == _shown)
                      ?? _monitors.FirstOrDefault(m => m.Primary) ?? _monitors.FirstOrDefault();
            double ratio = mon != null && mon.PixelWidth > 0 && mon.PixelHeight > 0 ? (double)mon.PixelHeight / mon.PixelWidth : 9.0 / 16.0;
            double w = _width, h = w * ratio;
            if (h > _maxScreenHeight)
            {
                h = _maxScreenHeight;
                w = h / ratio;
            }
            _screen.Width = w;
            _screen.Height = h;
        }

        private string MonitorName(string device)
        {
            if (_monitors.Count == 0) return T("Hauptbildschirm", "Main display");
            int i = ScreenTilePicker.MonitorIndexOf(_monitors, device);
            return Placement.MonitorLabel(_monitors[i], i);
        }

        private void Paint()
        {
            bool osdHere = OsdPreset != "custom" && Same(OsdDevice, _shown);
            bool mediaHere = MediaPreset != "custom" && Same(MediaDevice, _shown);
            for (int i = 0; i < _tiles.Length; i++)
            {
                string tile = ScreenTilePicker.Tiles[i];
                bool osd = osdHere && OsdPreset == tile, media = mediaHere && MediaPreset == tile;
                bool selected = osd || media;
                // Beide auf einer Kachel: links blau, rechts gelb
                Brush back = osd && media
                    ? new LinearGradientBrush(new GradientStopCollection { new(OsdColor, 0), new(OsdColor, 0.5), new(FlyoutColor, 0.5), new(FlyoutColor, 1) }, 0)
                    : osd ? OsdBrush : media ? FlyoutBrush : i == _hover ? HoverBg : FieldBg;
                _tiles[i].Background = back;
                _tiles[i].BorderBrush = osd ? OsdBrush : media ? FlyoutBrush : FieldBorder;
                var label = _labels[i];
                label.Inlines.Clear();
                void Line(string text, Brush color)
                {
                    if (label.Inlines.Count > 0) label.Inlines.Add(new System.Windows.Documents.LineBreak());
                    label.Inlines.Add(new System.Windows.Documents.Run(text) { Foreground = color });
                }
                // Auf farbiger Fläche dunkle bzw. weiße Schrift, gespeicherte Positionen in der Farbe ihres Teils
                Brush onFill = media && !osd ? Brushes.Black : Brushes.White;
                if (osd) Line("OSD", onFill);
                if (media) Line("Flyout", onFill);
                foreach (var m in _marks.Where(m => m.Tile == tile && Same(m.Device, _shown)))
                    Line(m.Text, selected ? onFill : m.Text.EndsWith("OSD") ? OsdBrush : m.Text.EndsWith("Flyout") ? FlyoutBrush : Fg);
            }
            string Where(string preset, string device) => preset == "custom" ? T("Eigene Position", "Own position")
                : ScreenTilePicker.TileName(preset) + (Same(device, _shown) ? "" : $" ({MonitorName(device)})");
            _status.Text = $"OSD: {Where(OsdPreset, OsdDevice)}\nFlyout: {Where(MediaPreset, MediaDevice)}";
            if (_customButton != null) _customButton.IsEnabled = OsdPreset != "custom" || MediaPreset != "custom";
        }
    }
}
