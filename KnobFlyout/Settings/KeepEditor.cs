using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KnobFlyout.Config;
using KnobFlyout.Osd;
using static KnobFlyout.Loc;
using static KnobFlyout.Settings.UiHelpers;

namespace KnobFlyout.Settings
{
    /// <summary>
    /// Auswahl für die Daueranzeige des Flyouts: Programme als Chips (laufende, gespeicherte, Spotify, eigener Eintrag),
    /// wann es stehen bleibt und wo. Ohne gewähltes Programm nur die Chips. Für Einstellungen und Einführung; ändert
    /// cfg direkt und meldet jede Änderung über changed.
    /// </summary>
    internal sealed class KeepEditor : StackPanel
    {
        private readonly AppConfig _cfg;
        private readonly Func<List<string>> _running;
        private readonly Action _changed;
        private readonly Action? _placeChanged;
        private bool _adding;

        /// <param name="running">Programme, die gerade eine Media-Session haben (Namen wie in der Quellen-Leiste).</param>
        /// <param name="placeChanged">Zusätzlich, wenn Platz oder Bildschirm gewählt wurden.</param>
        public KeepEditor(AppConfig cfg, Func<List<string>> running, Action changed, Action? placeChanged = null)
        {
            _cfg = cfg;
            _running = running;
            _changed = changed;
            _placeChanged = placeChanged;
            Render();
            Loaded += (_, _) => Render(); // Seite erneut geöffnet: inzwischen gestartete Programme anbieten
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private bool IsOn(string name) => _cfg.MediaKeepApps.Any(a => Same(a, name));

        private void Render()
        {
            Children.Clear();
            Children.Add(Chips());
            if (_cfg.MediaKeepApps.Count == 0) return;

            Children.Add(Row(T("Stehen bleiben", "Stay on screen"), Segments(new[]
            {
                ("playing", T("Beim Abspielen", "While playing")),
                ("open", T("Solange geöffnet", "While open")),
            }, _cfg.MediaKeepMode == "open" ? "open" : "playing", key =>
            {
                _cfg.MediaKeepMode = key;
                _changed();
                Render();
            })));

            Children.Add(Row(T("Position", "Position"), Segments(new[]
            {
                ("normal", T("Wie sonst", "As usual")),
                ("own", T("Eigene", "Own")),
            }, _cfg.MediaKeepOwnPosition ? "own" : "normal", key =>
            {
                _cfg.MediaKeepOwnPosition = key == "own";
                _changed();
                Render();
            })));

            if (_cfg.MediaKeepOwnPosition) Children.Add(Place());
        }

        private static Grid Row(string label, UIElement control)
        {
            var row = OptionRow(label, control);
            row.Margin = new Thickness(0, 8, 0, 0);
            return row;
        }

        // ── Programme ───────────────────────────────────────────────────────

        private WrapPanel Chips()
        {
            var names = new List<string>(_cfg.MediaKeepApps);
            void Offer(string name) { if (!names.Any(n => Same(n, name))) names.Add(name); }
            foreach (var name in _running()) Offer(name);
            Offer("Spotify");

            var chips = new WrapPanel { Margin = new Thickness(0, 0, -6, 0) };
            foreach (var name in names)
            {
                string program = name;
                bool on = IsOn(program);
                var chip = Chip(program, on, () =>
                {
                    if (on) _cfg.MediaKeepApps.RemoveAll(a => Same(a, program));
                    else _cfg.MediaKeepApps.Add(program);
                    _changed();
                    Render();
                }, check: true);
                chip.Margin = new Thickness(0, 0, 6, 6);
                chips.Children.Add(chip);
            }
            if (_adding) chips.Children.Add(AddBox());
            else
            {
                var add = Chip(T("+ Anderes Programm", "+ Another program"), false, () =>
                {
                    _adding = true;
                    Render();
                });
                add.Margin = new Thickness(0, 0, 6, 6);
                chips.Children.Add(add);
            }
            return chips;
        }

        /// <summary>Eigener Eintrag für ein Programm, das gerade nicht läuft: Enter übernimmt, Esc oder leer bricht ab.</summary>
        private TextBox AddBox()
        {
            var box = TextField(width: 170);
            box.Margin = new Thickness(0, 0, 6, 6);
            InputHint(box, T("Name wie im Flyout", "Name as in the flyout"));
            void Finish(bool take)
            {
                if (!_adding) return;
                _adding = false;
                string name = box.Text.Trim();
                if (take && name.Length > 0 && !IsOn(name))
                {
                    _cfg.MediaKeepApps.Add(name);
                    _changed();
                }
                Render();
            }
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; Finish(true); }
                else if (e.Key == Key.Escape) { e.Handled = true; Finish(false); }
            };
            box.LostKeyboardFocus += (_, _) => Finish(true);
            box.Loaded += (_, _) => box.Focus();
            return box;
        }

        // ── Eigene Stelle ───────────────────────────────────────────────────

        private Grid Place()
        {
            var picker = new ScreenTilePicker(Placement.Monitors(), _cfg.MediaKeepMonitor, _cfg.MediaKeepPreset, allowCustom: false, width: 176, maxScreenHeight: 100);
            picker.Changed += () =>
            {
                _cfg.MediaKeepPreset = picker.Preset;
                _cfg.MediaKeepMonitor = picker.Device;
                _placeChanged?.Invoke();
                _changed();
            };
            var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hint = Hint(T("Kachel wählen oder das stehende Flyout mit der Maus ziehen. Es hängt dann nicht am OSD, das OSD weicht ihm aus.",
                "Pick a tile or drag the flyout with the mouse. It is then not attached to the OSD, and the OSD moves out of its way."));
            hint.Margin = new Thickness(0, 4, 16, 0);
            grid.Children.Add(hint);
            Grid.SetColumn(picker, 1);
            grid.Children.Add(picker);
            return grid;
        }
    }
}
