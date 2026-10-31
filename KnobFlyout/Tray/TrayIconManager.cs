using System;
using System.Drawing;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using KnobFlyout.Config;
using Microsoft.Win32;

namespace KnobFlyout.Tray
{
    /// <summary>Ersetzt start_tray()/_make_icon() (pystray + PIL) aus knob_service.py.</summary>
    public class TrayIconManager : IDisposable
    {
        private readonly TaskbarIcon _icon;
        private readonly ContextMenu _menu;
        private readonly MenuItem _host;
        private bool _lightTaskbar;

        public event Action? SettingsRequested;
        public event Action? ExitRequested;

        public TrayIconManager()
        {
            _lightTaskbar = ThemeHelper.TaskbarIsLight();
            _icon = new TaskbarIcon
            {
                ToolTipText = "KnobFlyout",
                Icon = CreateIcon(_lightTaskbar),
            };
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            // Menü im Stil des „…“-Menüs: ContextMenu nur als Hülle (Maus und Esc wie gewohnt), Inhalt bei jedem Öffnen frisch nach Design.
            _menu = new ContextMenu
            {
                HasDropShadow = false, Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new System.Windows.Thickness(0), Padding = new System.Windows.Thickness(0),
                Template = Template("ContextMenu", "<ItemsPresenter/>"),
            };
            _host = new MenuItem { Template = Template("MenuItem", "<ContentPresenter ContentSource=\"Header\"/>"), StaysOpenOnClick = true };
            _menu.Items.Add(_host);
            _icon.ContextMenu = _menu;
            _icon.PreviewTrayContextMenuOpen += (_, _) => _host.Header = BuildMenu();
            _icon.TrayLeftMouseUp += (_, _) => SettingsRequested?.Invoke();
        }

        private static ControlTemplate Template(string type, string content) => (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
            $"<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"{type}\">{content}</ControlTemplate>");

        private System.Windows.UIElement BuildMenu()
        {
            var menu = new Osd.FlyoutMenu(Osd.MediaOsd.MenuLookFor(ConfigManager.Load()), () => _menu.IsOpen = false);
            menu.Row(Loc.T("Einstellungen", "Settings"), "\uE713", false, true, () => SettingsRequested?.Invoke());
            menu.Group();
            menu.Row(Loc.T("Beenden", "Exit"), "\uE7E8", false, true, () => ExitRequested?.Invoke());
            return menu.Build();
        }

        /// <summary>Knopf ohne Hintergrund (siehe AppIcon.RenderTray) in der echten Größe des Infobereichs.</summary>
        private static Icon CreateIcon(bool lightTaskbar) => AppIcon.CreateTrayIcon(AppIcon.TraySize, lightTaskbar);

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
