using System;
using System.Drawing;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using KnobEarTrumpet.Config;
using Microsoft.Win32;

namespace KnobEarTrumpet.Tray
{
    /// <summary>Ersetzt start_tray()/_make_icon() (pystray + PIL) aus knob_service.py.</summary>
    public class TrayIconManager : IDisposable
    {
        private readonly TaskbarIcon _icon;
        private readonly MenuItem _settingsItem;
        private readonly MenuItem _exitItem;
        private bool _lightTaskbar;

        public event Action? SettingsRequested;
        public event Action? ExitRequested;

        public TrayIconManager()
        {
            _lightTaskbar = ThemeHelper.TaskbarIsLight();
            _icon = new TaskbarIcon
            {
                ToolTipText = "ModernFlyout",
                Icon = CreateIcon(_lightTaskbar),
            };
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            var menu = new ContextMenu();

            _settingsItem = new MenuItem();
            _settingsItem.Click += (_, _) => SettingsRequested?.Invoke();
            menu.Items.Add(_settingsItem);

            menu.Items.Add(new Separator());

            _exitItem = new MenuItem();
            _exitItem.Click += (_, _) => ExitRequested?.Invoke();
            menu.Items.Add(_exitItem);

            _icon.ContextMenu = menu;
            UpdateTexts();
        }

        /// <summary>Menütexte in der aktuellen Sprache (nach Sprachwechsel erneut aufrufen).</summary>
        public void UpdateTexts()
        {
            _settingsItem.Header = "⚙️ " + Loc.T("Einstellungen", "Settings");
            _exitItem.Header = Loc.T("Beenden", "Exit");
        }

        /// <summary>Lautsprecher ohne Hintergrund (siehe AppIcon.RenderTray), in 32 px – Windows skaliert für 16/20/24 px herunter.</summary>
        private static Icon CreateIcon(bool lightTaskbar) => AppIcon.CreateTrayIcon(32, lightTaskbar);

        /// <summary>Taskleiste hell/dunkel umgestellt → Symbolfarbe anpassen.</summary>
        private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General) return;
            _icon.Dispatcher.BeginInvoke(() =>
            {
                bool light = ThemeHelper.TaskbarIsLight();
                if (light == _lightTaskbar) return;
                _lightTaskbar = light;
                var old = _icon.Icon;
                _icon.Icon = CreateIcon(light);
                old?.Dispose();
            });
        }

        public void Dispose()
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _icon.Dispose();
        }
    }
}
