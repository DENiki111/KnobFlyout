using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KnobEarTrumpet.Osd;
using static KnobEarTrumpet.Loc;
using static KnobEarTrumpet.Settings.UiHelpers;

namespace KnobEarTrumpet.Settings
{
    /// <summary>
    /// Positions-Auswahl als Mini-Vorschau des Bildschirms: oben der Bildschirm (Auswahl wie bisher),
    /// darunter dieser Bildschirm im echten Seitenverhältnis (z. B. 16:9) mit 3×3 Kacheln. Klick auf eine
    /// Kachel = Platz-Voreinstellung (osd_corner_preset / media_corner_preset, siehe Placement.Corner).
    /// Keine Kachel markiert = eigene Position (verschoben, Snap-Punkt oder Pixel-Eingabe).
    /// </summary>
    internal sealed class ScreenTilePicker : StackPanel
    {
        /// <summary>Die neun Plätze, zeilenweise von oben links nach unten rechts.</summary>
        public static readonly string[] Tiles =
        {
            "top_left", "top_center", "top_right",
            "middle_left", "center", "middle_right",
            "bottom_left", "bottom_center", "bottom_right",
        };

        private const double MaxScreenHeight = 150; // Hochkant-Bildschirme nicht riesig werden lassen

        private readonly List<MonitorArea> _monitors;
        private readonly ComboBox _monitorCombo;
        private readonly double _width;
        private bool _silent; // Set() ändert den Bildschirm, ohne Changed auszulösen
        private readonly Border _screen;
        private readonly Border[] _tiles = new Border[9];
        private readonly TextBlock _status;
        private readonly Button? _customButton;
        private int _hover = -1;

        /// <summary>Gewählter Platz (einer aus Tiles) oder "custom".</summary>
        public string Preset { get; private set; }

        /// <summary>Gerätename des Bildschirms ("" = Hauptbildschirm).</summary>
        public string Device { get; private set; }

        /// <summary>Kachel, Bildschirm oder „Eigene Position“ vom Benutzer geändert (nicht bei Set()).</summary>
        public event Action? Changed;

        /// <param name="allowCustom">Knopf „Eigene Position“ anbieten (nicht beim Anlegen von Snap-Punkten).</param>
        public ScreenTilePicker(List<MonitorArea> monitors, string? device, string? preset, bool allowCustom = true, double width = 224)
        {
            _monitors = monitors;
            _width = width;
            Device = device ?? "";
            Preset = Array.IndexOf(Tiles, preset) >= 0 ? preset! : "custom";

            var items = new List<string> { T("Hauptbildschirm", "Main display") };
            items.AddRange(monitors.Select((m, i) => Placement.MonitorLabel(m, i)));
            _monitorCombo = Combo(width, MonitorIndex(Device), items.ToArray());
            _monitorCombo.HorizontalAlignment = HorizontalAlignment.Left;
            _monitorCombo.SelectionChanged += (_, _) =>
            {
                Device = _monitorCombo.SelectedIndex <= 0 ? "" : _monitors[_monitorCombo.SelectedIndex - 1].Device;
                SizeScreen();
                if (!_silent) Changed?.Invoke();
            };
            Children.Add(_monitorCombo);

            var grid = new Grid { Margin = new Thickness(3) };
            for (int i = 0; i < 3; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition());
            }
            for (int i = 0; i < Tiles.Length; i++)
            {
                int idx = i;
                var tile = new Border
                {
                    CornerRadius = new CornerRadius(3), Margin = new Thickness(2), BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand, ToolTip = TileName(Tiles[i]),
                };
                tile.MouseEnter += (_, _) => { _hover = idx; Paint(); };
                tile.MouseLeave += (_, _) => { if (_hover == idx) _hover = -1; Paint(); };
                tile.MouseLeftButtonUp += (_, _) =>
                {
                    Preset = Tiles[idx];
                    Paint();
                    Changed?.Invoke();
                };
                Grid.SetRow(tile, i / 3);
                Grid.SetColumn(tile, i % 3);
                grid.Children.Add(tile);
                _tiles[i] = tile;
            }

            // Bildschirm-Rahmen; Höhe folgt dem Seitenverhältnis des gewählten Bildschirms (SizeScreen).
            _screen = new Border
            {
                Child = grid, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(2),
                BorderBrush = FieldBorder, Background = ContentBg, HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 0, 0),
            };
            Children.Add(_screen);

            var footer = new DockPanel { Margin = new Thickness(0, 4, 0, 0), Width = width, HorizontalAlignment = HorizontalAlignment.Left };
            if (allowCustom)
            {
                _customButton = Btn(T("Eigene Position", "Own position"));
                _customButton.Margin = new Thickness(6, 0, 0, 0);
                _customButton.ToolTip = T("Verschobene Position bzw. Snap-Punkt verwenden", "Use the dragged position or snap point");
                _customButton.Click += (_, _) =>
                {
                    Preset = "custom";
                    Paint();
                    Changed?.Invoke();
                };
                DockPanel.SetDock(_customButton, Dock.Right);
                footer.Children.Add(_customButton);
            }
            _status = new TextBlock { Foreground = SubFg, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            footer.Children.Add(_status);
            Children.Add(footer);

            SizeScreen();
            Paint();
        }

        /// <summary>Von außen setzen (z. B. „Position zurücksetzen“), ohne Changed auszulösen; device null = Bildschirm behalten.</summary>
        public void Set(string preset, string? device = null)
        {
            Preset = Array.IndexOf(Tiles, preset) >= 0 ? preset : "custom";
            if (device != null)
            {
                _silent = true;
                _monitorCombo.SelectedIndex = MonitorIndex(device); // SelectionChanged setzt Device und Größe
                _silent = false;
                Device = device;
                SizeScreen();
            }
            Paint();
        }

        /// <summary>Eintrag in der Bildschirm-Auswahl: 0 = Hauptbildschirm, sonst Position in der Liste + 1.</summary>
        private int MonitorIndex(string? device) =>
            string.IsNullOrEmpty(device) ? 0 : Math.Max(0, _monitors.FindIndex(m => m.Device == device) + 1);

        public static string TileName(string preset) => preset switch
        {
            "top_left" => T("Oben links", "Top left"),
            "top_center" => T("Oben mittig", "Top center"),
            "top_right" => T("Oben rechts", "Top right"),
            "middle_left" => T("Mitte links", "Middle left"),
            "center" => T("Mitte", "Center"),
            "middle_right" => T("Mitte rechts", "Middle right"),
            "bottom_left" => T("Unten links", "Bottom left"),
            "bottom_center" => T("Unten mittig", "Bottom center"),
            "bottom_right" => T("Unten rechts", "Bottom right"),
            _ => T("Eigene Position", "Own position"),
        };

        /// <summary>Rahmen im Seitenverhältnis des gewählten Bildschirms (Pixelgröße, sonst 16:9).</summary>
        private void SizeScreen()
        {
            var mon = _monitors.FirstOrDefault(m => !string.IsNullOrEmpty(Device) && m.Device == Device)
                      ?? _monitors.FirstOrDefault(m => m.Primary) ?? _monitors.FirstOrDefault();
            double ratio = mon != null && mon.PixelWidth > 0 && mon.PixelHeight > 0
                ? (double)mon.PixelHeight / mon.PixelWidth
                : 9.0 / 16.0;
            double w = _width, h = w * ratio;
            if (h > MaxScreenHeight)
            {
                h = MaxScreenHeight;
                w = h / ratio;
            }
            _screen.Width = w;
            _screen.Height = h;
        }

        private void Paint()
        {
            for (int i = 0; i < _tiles.Length; i++)
            {
                bool selected = Tiles[i] == Preset;
                _tiles[i].Background = selected ? Accent : i == _hover ? HoverBg : FieldBg;
                _tiles[i].BorderBrush = selected ? Accent : FieldBorder;
            }
            bool custom = Preset == "custom";
            _status.Text = custom ? T("Eigene Position", "Own position") : TileName(Preset);
            if (_customButton != null) _customButton.IsEnabled = !custom;
        }
    }
}
