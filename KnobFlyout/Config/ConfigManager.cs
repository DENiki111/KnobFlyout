using System;
using System.IO;
using System.Text.Json;
using KnobFlyout.Profiles;

namespace KnobFlyout.Config
{
    /// <summary>
    /// Entspricht load_config()/save_config() aus knob_service.py: config.json liegt neben der exe,
    /// Zugriff ist per Lock abgesichert.
    ///
    /// Schutz vor Datenverlust: Ist die Datei gerade leer/defekt/gesperrt (z. B. weil sie
    /// kopiert oder im Editor gespeichert wird), wird die zuletzt gültige Config geliefert und
    /// NICHT auf Standardwerte zurückgefallen. Solange die Datei nicht lesbar ist, wird sie auch
    /// nicht überschrieben. Gespeichert wird atomar (temp-Datei + Austausch).
    /// </summary>
    public static class ConfigManager
    {
        private static readonly string ConfigFile = Path.Combine(AppContext.BaseDirectory, "config.json");
        private static readonly object Lock = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
        };

        private static string? _lastGoodJson;
        private static bool _diskUnreadable;
        private static string? _lastError;

        public static AppConfig Load()
        {
            lock (Lock)
            {
                if (!File.Exists(ConfigFile))
                {
                    _diskUnreadable = false;
                    return FromLastGoodOrDefault();
                }

                string json;
                try
                {
                    using var fs = new FileStream(ConfigFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var sr = new StreamReader(fs);
                    json = sr.ReadToEnd();
                }
                catch (Exception ex)
                {
                    return Fail($"load_config (lesen): {ex.Message}");
                }

                if (string.IsNullOrWhiteSpace(json))
                    return Fail("load_config: Datei ist leer (wird vermutlich gerade geschrieben)");

                try
                {
                    var cfg = Parse(json);
                    _lastGoodJson = json;
                    _diskUnreadable = false;
                    _lastError = null;
                    return cfg;
                }
                catch (Exception ex)
                {
                    BackupBroken(json);
                    return Fail($"load_config (JSON): {ex.Message}");
                }
            }
        }

        public static void Save(AppConfig cfg)
        {
            lock (Lock)
            {
                if (_diskUnreadable)
                {
                    Logger.Error("save_config übersprungen: config.json ist gerade nicht lesbar (wird nicht überschrieben)");
                    return;
                }

                try
                {
                    var json = JsonSerializer.Serialize(cfg, JsonOptions);
                    var tmp = ConfigFile + ".tmp";
                    File.WriteAllText(tmp, json);
                    File.Move(tmp, ConfigFile, true);
                    _lastGoodJson = json;
                }
                catch (Exception ex)
                {
                    Logger.Error($"save_config: {ex.Message}");
                }
            }
        }

        private static AppConfig Parse(string json)
        {
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            ProfileDefinitions.EnsureList(cfg); // Profil-Liste anlegen + je Profil eine App-Liste
            cfg.EnsureLayouts(); // je Design ein Aufbau, Werte älterer Versionen übernehmen
            return cfg;
        }

        /// <summary>Liefert immer eine frische Kopie (Aufrufer verändern die Config).</summary>
        private static AppConfig FromLastGoodOrDefault()
        {
            if (_lastGoodJson != null)
            {
                try { return Parse(_lastGoodJson); }
                catch { /* fällt auf Default zurück */ }
            }
            var fresh = new AppConfig();
            fresh.EnsureLayouts();
            return fresh;
        }

        private static AppConfig Fail(string message)
        {
            _diskUnreadable = true;
            if (message != _lastError)
            {
                _lastError = message;
                Logger.Error(message);
            }
            return FromLastGoodOrDefault();
        }

        private static void BackupBroken(string json)
        {
            try { File.WriteAllText(ConfigFile + ".broken", json); }
            catch { }
        }
    }
}
