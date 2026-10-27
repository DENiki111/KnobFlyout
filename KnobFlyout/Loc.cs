using KnobFlyout.Config;

namespace KnobFlyout
{
    /// <summary>
    /// Zweisprachige Oberfläche (cfg.language "de" | "en"). Texte stehen direkt am Verwendungsort als
    /// Paar: Loc.T("Speichern", "Save"). Apply() wird beim Start und nach jeder Änderung aus den
    /// Einstellungen aufgerufen; OSD-Karten und Popups lesen die Sprache beim nächsten Erscheinen.
    /// </summary>
    public static class Loc
    {
        public static bool English { get; private set; }

        public static void Apply(AppConfig cfg) => English = cfg.Language == "en";

        public static string T(string de, string en) => English ? en : de;
    }
}
