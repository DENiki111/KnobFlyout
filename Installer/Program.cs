using System;
using System.Globalization;
using System.Windows.Forms;

namespace ModernFlyoutSetup
{
    /// <summary>Sprache des Setups (und bei Neuinstallation auch der App).</summary>
    internal static class L
    {
        public static bool En;
        public static string Code => En ? "en" : "de";
        public static string T(string de, string en) => En ? en : de;
    }

    internal static class Program
    {
        /// <summary>
        /// Ablauf wie bei üblichen Installern: erst die Sprache wählen, dann der Assistent. Nach einem Neustart als
        /// Administrator (geschützter Zielordner) kommen Sprache, Ordner und Optionen als Argumente mit.
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var restart = SetupArgs.Parse(args);
            if (restart.Lang == null)
            {
                using (var language = new LanguageForm())
                {
                    if (language.ShowDialog() != DialogResult.OK) return;
                    L.En = language.English;
                }
            }
            else L.En = restart.Lang == "en";

            Application.Run(new WizardForm(restart));
        }
    }

    /// <summary>Argumente für den Neustart als Administrator: /lang=de /dir="…" /desktop=1 /startmenu=1 /autostart=0.</summary>
    internal sealed class SetupArgs
    {
        public string Lang;
        public string Dir;
        public bool? Desktop, StartMenu, Autostart;

        public static SetupArgs Parse(string[] args)
        {
            var a = new SetupArgs();
            foreach (var raw in args)
            {
                int eq = raw.IndexOf('=');
                if (!raw.StartsWith("/") || eq < 0) continue;
                string key = raw.Substring(1, eq - 1).ToLowerInvariant(), value = raw.Substring(eq + 1).Trim('"');
                switch (key)
                {
                    case "lang": a.Lang = value == "en" ? "en" : "de"; break;
                    case "dir": a.Dir = value; break;
                    case "desktop": a.Desktop = value == "1"; break;
                    case "startmenu": a.StartMenu = value == "1"; break;
                    case "autostart": a.Autostart = value == "1"; break;
                }
            }
            return a;
        }

        public static string Build(string dir, bool desktop, bool startMenu, bool autostart) =>
            string.Format(CultureInfo.InvariantCulture, "/lang={0} \"/dir={1}\" /desktop={2} /startmenu={3} /autostart={4}",
                L.Code, dir.TrimEnd('\\'), desktop ? 1 : 0, startMenu ? 1 : 0, autostart ? 1 : 0);
    }
}
