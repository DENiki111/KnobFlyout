using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using KnobFlyout.Config;
using KnobFlyout.Profiles;
using KnobFlyout.Settings;

namespace KnobFlyout.Osd
{
    /// <summary>
    /// Entspricht dem "OSD-Popup" zum Zuweisen neu erkannter Apps (siehe settings_app.py-Hinweis
    /// "Zuweisen über die Profil-Buttons im Apps-Tab oder per OSD-Popup"). Anders als die normalen
    /// OSD-Karten ist dieses Fenster klickbar (nicht klick-durchlässig): ein Klick auf ein
    /// Profil-Icon weist die App sofort zu und speichert; ⊘ ignoriert sie dauerhaft (Exclude).
    /// Blendet sich nach 12s ohne Interaktion von selbst aus (App bleibt dann unzugewiesen und
    /// wird bewusst erneut vorgeschlagen, solange sie Ton abspielt – nicht in seen_apps eintragen).
    /// Beim OSD-Design "win11" im Windows-11-Look (Farbschema hell/dunkel, flache Knöpfe).
    /// </summary>
    internal sealed partial class NewAppOsd : Window
    {
        private const double W = 300;
        private const double H = 96;

        /// <param name="previewCfg">Nur für die Vorschau in den Einstellungen: diese (ungespeicherte) Config, kein Auto-Hide.</param>
        private readonly string _processName;
        private readonly bool _preview;

        public NewAppOsd(string processName, AppConfig? previewCfg = null)
        {
            _processName = processName;
            _preview = previewCfg != null;
            var cfg = previewCfg ?? ConfigManager.Load();
            var spec = Designs.Get(cfg.OsdDesign); // Design-System (z. B. Dynamic Island): eigene Form und Größe
            double width = spec?.PopupWidth ?? W, height = spec?.PopupHeight ?? H;
            bool win11 = cfg.OsdDesign == "win11";
            bool modern = cfg.OsdDesign == "modern" || cfg.OsdDesign == "windows";
            bool flat = win11 || modern; // beide: Farbschema hell/dunkel, flache Knöpfe mit Fluent-Symbolen
            bool light = flat && ThemeHelper.IsLight(cfg);

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = true;
            Topmost = true;
            Width = width;
            Height = height;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            double alpha = Math.Clamp(cfg.WinAlpha, 0.0, 1.0); // Deckkraft nur für den Hintergrund
            WindowStartupLocation = WindowStartupLocation.Manual;
            // Am OSD-Anker (gleiche Ecke bzw. eigene Position), immer vollständig im Bildschirm.
            var anchor = Placement.Clamp(new Rect(Placement.OsdAnchor(cfg, width, height), new Size(width, height)));
            Left = anchor.X;
            Top = anchor.Y;

            if (ReferenceEquals(spec, Designs.Mac))
                Content = BuildMac(cfg, spec, alpha, processName);
            else if (ReferenceEquals(spec, Designs.Material))
                Content = BuildMaterial(cfg, spec, alpha, processName);
            else if (ReferenceEquals(spec, Designs.Hud))
                Content = BuildHud(cfg, spec, alpha, processName);
            else if (ReferenceEquals(spec, Designs.Hardware))
                Content = BuildHardware(cfg, spec, alpha, processName);
            else if (spec != null)
                Content = BuildSpec(cfg, spec, alpha, processName);
            else
                BuildClassic(cfg, processName, win11, modern, flat, light, alpha);

            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW); // im Taskwechsler/Taskleiste verstecken, aber klickbar lassen
            };

            if (previewCfg != null) return; // Vorschau: kein Auto-Hide

            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            timer.Tick += (_, _) => { timer.Stop(); try { Close(); } catch { } };
            timer.Start();
            Closed += (_, _) => timer.Stop();
        }

        /// <summary>
        /// Design-System (z. B. Dynamic Island): aufgeklappte Insel – „Neue App erkannt“, App-Name, darunter runde
        /// Knöpfe (⊘ und je Profil in Profilfarbe mit weißem Logo). Farben, Radius und Schrift aus DesignSpec.
        /// </summary>
        private Border BuildSpec(AppConfig cfg, DesignSpec spec, double alpha, string processName)
        {
            var pal = spec.Palette(ThemeHelper.IsLight(cfg));
            var panel = new StackPanel { Margin = new Thickness(16, 13, 16, 0) };
            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("Neue App erkannt", "New app detected"), FontFamily = spec.Font, FontSize = 12, Foreground = UiColor.Brush(pal.Sub),
            });
            panel.Children.Add(new TextBlock
            {
                Text = processName.Replace(".exe", ""), FontFamily = spec.Font, FontSize = 17, FontWeight = FontWeights.SemiBold,
                Foreground = UiColor.Brush(pal.Fg), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0),
            });

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 11, 0, 0) };
            var profiles = ProfileDefinitions.For(cfg);
            int count = profiles.Count + 1; // ⊘ + bis zu 6 Profile passen immer in die Breite
            double size = Math.Min(36, (spec.PopupWidth - 32 - 8.0 * (count - 1)) / count);
            row.Children.Add(CircleButton(new TextBlock
            {
                Text = "\uE733", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 15, // Blocked
                Foreground = UiColor.Brush(pal.Fg), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            }, pal.Button, size, Exclude, Loc.T("Ignorieren", "Ignore")));
            foreach (var p in profiles)
            {
                string key = p.Key;
                var logo = p.CreateLogo(16, Brushes.White);
                if (logo is Image) logo.Width = logo.Height = 20;
                string hotkey = cfg.ProfileList.FirstOrDefault(e => e.Key == key)?.AssignHotkey ?? "";
                row.Children.Add(CircleButton(logo, p.Color, size, () => AssignTo(key), hotkey.Length > 0 ? $"{p.Name} ({hotkey})" : p.Name));
            }
            panel.Children.Add(row);

            return new Border
            {
                CornerRadius = new CornerRadius(spec.PopupRadius), Background = UiColor.Brush(pal.Bg, alpha),
                BorderBrush = UiColor.Brush(pal.Edge), BorderThickness = new Thickness(1), Child = panel,
            };
        }

        /// <summary>Runder Knopf der Insel (beim Überfahren leicht abgeblendet).</summary>
        private static Border CircleButton(UIElement content, string bgHex, double size, Action onClick, string tooltip)
        {
            var b = new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = UiColor.Brush(bgHex),
                Margin = new Thickness(0, 0, 8, 0), Cursor = System.Windows.Input.Cursors.Hand, Child = content, ToolTip = tooltip,
            };
            b.MouseEnter += (_, _) => b.Opacity = 0.85;
            b.MouseLeave += (_, _) => b.Opacity = 1.0;
            b.MouseLeftButtonDown += (_, e) => e.Handled = true;
            b.MouseLeftButtonUp += (_, e) => { onClick(); e.Handled = true; };
            return b;
        }

        /// <summary>Angezeigte App ignorieren (Exclude), speichern und schließen.</summary>
        private void Exclude()
        {
            if (_preview) return;
            var fresh = ConfigManager.Load();
            AppRow.ToggleExclude(fresh, _processName);
            MarkSeenAndClose(fresh, _processName);
        }

        /// <summary>Ältere Designs („Eigenes“, ModernFlyouts, Windows 11): Karte mit Text oben und Knopfreihe.</summary>
        private void BuildClassic(AppConfig cfg, string processName, bool win11, bool modern, bool flat, bool light, double alpha)
        {
            var canvas = new Canvas();
            Content = new Border
            {
                CornerRadius = new CornerRadius(Math.Clamp(cfg.OsdCornerRadius, 0, 30)),
                Background = UiColor.Brush(win11 ? (light ? "#E9E9EB" : "#2C2C2C")
                    : modern ? (light ? "#EFEFEF" : "#222222") : cfg.ColorBg, alpha), // modern: wie die ModernFlyouts-Karten
                BorderBrush = win11 ? UiColor.Brush(light ? "#D0D0D4" : "#404040", alpha) : null,
                BorderThickness = new Thickness(win11 ? 1 : 0),
                Child = canvas,
            };

            string clean = processName.Replace(".exe", "");
            Place(canvas, new TextBlock
            {
                Text = (flat ? "" : "🆕 ") + Loc.T("Neue App erkannt", "New app detected"), FontSize = flat ? 12 : 11,
                Foreground = UiColor.Brush(flat ? (light ? "#5c5c5c" : win11 ? "#c5c5c5" : "#a0a0a0") : cfg.ColorVolText),
            }, 12, 8);

            Place(canvas, new TextBlock
            {
                Text = clean.Length > 24 ? clean[..24] + "…" : clean, FontSize = 14, FontWeight = flat ? FontWeights.SemiBold : FontWeights.Bold,
                Foreground = UiColor.Brush(light ? "#1b1b1b" : "#ffffff"),
            }, 12, 26);

            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            Canvas.SetLeft(btns, 12);
            Canvas.SetTop(btns, 56);
            canvas.Children.Add(btns);

            // Beim Klick frisch von der Platte laden: das Popup ist bis zu 12 s offen, in der Zeit
            // gespeicherte Änderungen (z. B. vorheriges Popup) sonst mit altem Stand überschrieben.
            Action exclude = () =>
            {
                var fresh = ConfigManager.Load();
                AppRow.ToggleExclude(fresh, processName);
                MarkSeenAndClose(fresh, processName);
            };
            // Windows 11: schwarz/weiß – neutrale Flächen mit einfarbigen Fluent-Symbolen.
            // ModernFlyouts: gleiche flache Knöpfe, Symbole in der jeweiligen Profilfarbe.
            string flatBg = win11 ? (light ? "#D6D6DA" : "#3A3A3A") : (light ? "#DADADA" : "#3a3a3a");
            var profiles = ProfileDefinitions.For(cfg);
            // Bis zu 6 Profile + ⊘: Knöpfe schmaler, damit alle in die Kartenbreite passen.
            int count = profiles.Count + 1;
            double buttonW = Math.Min(flat ? 40 : 34, (W - 24 - 6.0 * (count - 1)) / count);
            btns.Children.Add(flat
                ? FlatButton(TextIcon("⊘", light ? "#1b1b1b" : "#ffffff", false), flatBg, exclude, buttonW)
                : MakeButton(TextIcon("⊘", "#ffffff", false), "#2a2a3a", exclude, buttonW));

            foreach (var p in profiles)
            {
                string key = p.Key;
                Action assign = () => AssignTo(key);
                // ModernFlyouts: Logo in Profilfarbe, Win11: schwarz/weiß, Eigenes: Emoji bzw. Logo weiß auf Profilfarbe.
                FrameworkElement button = flat
                    ? FlatButton(p.CreateLogo(14, UiColor.Brush(modern ? p.Color : light ? "#1b1b1b" : "#ffffff")), flatBg, assign, buttonW)
                    : MakeButton(p.Icon.Length > 0 ? TextIcon(p.Icon, "#ffffff", false) : p.CreateLogo(14, Brushes.White), p.Color, assign, buttonW);
                // Name und ggf. Zuweisen-Taste als Hinweis
                string hotkey = cfg.ProfileList.FirstOrDefault(e => e.Key == key)?.AssignHotkey ?? "";
                button.ToolTip = hotkey.Length > 0 ? $"{p.Name} ({hotkey})" : p.Name;
                btns.Children.Add(button);
            }
        }

        /// <summary>
        /// Angezeigte App dem Profil zuweisen, speichern und schließen – per Klick auf den Profil-Knopf oder
        /// über dessen Zuweisen-Taste (profile_list[].assign_hotkey, siehe LowLevelKeyboardHook).
        /// </summary>
        public void AssignTo(string profileKey)
        {
            if (_preview) return;
            var fresh = ConfigManager.Load();
            if (!ProfileDefinitions.Keys(fresh).Contains(profileKey)) return; // Profil inzwischen gelöscht
            AppRow.Assign(fresh, _processName, profileKey);
            MarkSeenAndClose(fresh, _processName);
        }

        /// <summary>Popup-Inhalt für die Live-Vorschau in den Einstellungen (Fenster wird verworfen).</summary>
        public static FrameworkElement CreatePreview(AppConfig cfg)
        {
            var w = new NewAppOsd("Discord.exe", cfg);
            var content = (FrameworkElement)w.Content;
            w.Content = null;
            content.Width = w.Width;
            content.Height = w.Height;
            w.Close();
            return content;
        }

        private void MarkSeenAndClose(AppConfig cfg, string processName)
        {
            var key = processName.ToLowerInvariant();
            if (!cfg.SeenApps.Any(a => a.ToLowerInvariant() == key)) cfg.SeenApps.Add(key);
            ConfigManager.Save(cfg);
            try { Close(); } catch { }
        }

        /// <summary>Text/Emoji als Knopf-Inhalt (⊘ bzw. Emoji der Standard-Profile im Design „Eigenes“).</summary>
        private static TextBlock TextIcon(string text, string fgHex, bool glyphFont) => new()
        {
            Text = text, FontSize = 14, Foreground = UiColor.Brush(fgHex),
            FontFamily = glyphFont ? new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets") : new FontFamily("Segoe UI, Segoe UI Emoji"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };

        private static Button MakeButton(FrameworkElement content, string colorHex, Action onClick, double width)
        {
            var b = new Button
            {
                Content = content,
                Width = width,
                Height = 30,
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)),
                Foreground = Brushes.White,
                FontSize = 14,
            };
            b.Click += (_, _) => onClick();
            return b;
        }

        /// <summary>Flacher Windows-11-Knopf (abgerundete Fläche, beim Überfahren leicht aufgehellt).</summary>
        private static Border FlatButton(FrameworkElement content, string colorHex, Action onClick, double width)
        {
            var b = new Border
            {
                Width = width, Height = 30, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 6, 0),
                Background = UiColor.Brush(colorHex), Cursor = System.Windows.Input.Cursors.Hand,
                Child = content,
            };
            b.MouseEnter += (_, _) => b.Opacity = 0.85;
            b.MouseLeave += (_, _) => b.Opacity = 1.0;
            b.MouseLeftButtonUp += (_, _) => onClick();
            return b;
        }

        private static void Place(Canvas canvas, UIElement element, double left, double top)
        {
            Canvas.SetLeft(element, left);
            Canvas.SetTop(element, top);
            canvas.Children.Add(element);
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x80;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
