namespace KnobEarTrumpet.Config
{
    /// <summary>
    /// Farbschema-Auswahl: cfg.Theme = "system" (Standard, folgt Windows) | "light" | "dark".
    /// Gemeinsam genutzt von Settings-Fenster, modernen OSD-Karten und Media-Flyout.
    /// </summary>
    public static class ThemeHelper
    {
        public static bool IsLight(AppConfig cfg) => cfg.Theme switch
        {
            "light" => true,
            "dark" => false,
            _ => SystemIsLight(),
        };

        public static bool SystemIsLight() => ReadPersonalize("AppsUseLightTheme");

        /// <summary>Helle Taskleiste (Windows-Modus „Hell“) – unabhängig vom App-Farbschema.</summary>
        public static bool TaskbarIsLight() => ReadPersonalize("SystemUsesLightTheme");

        /// <summary>Windows-Akzentfarbe (Einstellungen → Personalisierung → Farben); Fallback Windows-Blau.</summary>
        public static System.Windows.Media.Color AccentColor()
        {
            try
            {
                var c = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
                return System.Windows.Media.Color.FromRgb(c.R, c.G, c.B);
            }
            catch
            {
                return System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4);
            }
        }

        private static bool ReadPersonalize(string name)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue(name) is int v && v == 1;
            }
            catch
            {
                return false;
            }
        }
    }
}
