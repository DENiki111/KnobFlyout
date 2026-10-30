using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using KnobFlyout.Config;
using Microsoft.Win32;

namespace KnobFlyout.Profiles
{
    /// <summary>
    /// Erkennt, was für eine App gerade Ton abspielt: Spiel, Browser/Video, Musik oder Kommunikation. Mit
    /// auto_assign landet eine neue App so ohne Rückfrage im passenden Profil; was hier nicht erkannt wird
    /// (oder in Art/Profil auf „Nachfragen“ steht), geht wie bisher ins Neue-App-Popup.
    /// </summary>
    public static class AppClassifier
    {
        public const string Games = "games", Browsers = "browsers", Music = "music", Comms = "comms";

        public static readonly string[] Categories = { Games, Browsers, Music, Comms };

        /// <summary>Standard-Profil je Art; nicht mehr vorhanden = nachfragen.</summary>
        public static string DefaultProfile(string category) => category switch
        {
            Games => "gaming",
            Browsers => "video",
            Music => "music",
            _ => "communication",
        };

        // Prozessnamen ohne „.exe“, klein
        private static readonly HashSet<string> CommApps = new()
        {
            "discord", "discordptb", "discordcanary", "vesktop", "legcord", "webcord", "teams", "ms-teams", "msteams", "zoom", "slack", "skype",
            "telegram", "whatsapp", "signal", "mumble", "ts3client_win64", "ts3client_win32", "teamspeak", "element", "guilded", "webex", "ciscocollabhost", "viber", "threema", "revolt",
        };

        private static readonly HashSet<string> MusicApps = new()
        {
            "spotify", "applemusic", "itunes", "tidal", "deezer", "amazon music", "amazonmusic", "foobar2000", "musicbee", "winamp", "aimp", "plexamp", "qobuz",
            "soundcloud", "youtube music", "youtube music desktop app", "ytmdesktop", "cider", "audacious", "mediamonkey", "groove", "microsoft.media.player",
        };

        private static readonly HashSet<string> BrowserApps = new()
        {
            "firefox", "chrome", "msedge", "opera", "brave", "vivaldi", "arc", "zen", "waterfox", "librewolf", "floorp", "thorium", "chromium", "iexplore", "maxthon", "yandex",
            "vlc", "mpc-hc", "mpc-hc64", "mpc-be", "mpc-be64", "mpv", "potplayer", "potplayer64", "potplayermini", "potplayermini64", "plex", "kodi", "stremio", "netflix", "disneyplus", "primevideo", "wmplayer",
        };

        /// <summary>Spiele, die nicht über Pfad oder Windows-Spieleliste auffallen (Emulatoren, eigene Launcher).</summary>
        private static readonly HashSet<string> GameApps = new()
        {
            "cs2", "csgo", "valorant", "league of legends", "dota2", "rocketleague", "robloxplayerbeta", "overwatch", "wow", "r5apex", "tslgame", "minecraft", "osu!",
            "ryujinx", "dolphin", "yuzu", "suyu", "retroarch", "pcsx2", "pcsx2-qt", "rpcs3", "cemu", "citra-qt", "duckstation-qt", "ppsspp", "ppssppwindows64",
        };

        /// <summary>Launcher und Hilfsprogramme rund um Spiele: lieber nachfragen als falsch einsortieren.</summary>
        private static readonly HashSet<string> NotGameApps = new()
        {
            "steam", "upc", "ubisoftconnect", "galaxyclient", "gog galaxy", "eadesktop", "origin", "battle.net", "riotclientservices", "riot client", "epicgameslauncher", "rockstarservice",
        };

        private static readonly string[] NotGameParts =
        {
            "launcher", "webhelper", "overlay", "crash", "bootstrap", "redist", "setup", "gamebar", "wallpaper",
        };

        /// <summary>Ordner, in denen Spiele liegen (Steam auf jedem Laufwerk, Epic, GOG, Ubisoft, EA, Xbox, Riot, Rockstar).</summary>
        private static readonly string[] GamePathParts =
        {
            @"\steamapps\common\", @"\epic games\", @"\gog games\", @"\gog galaxy\games\", @"\ubisoft game launcher\games\", @"\ea games\", @"\origin games\",
            @"\xboxgames\", @"\riot games\", @"\rockstar games\", @"\games\", @"\spiele\",
        };

        private static string Bare(string process)
        {
            string name = process.Trim().ToLowerInvariant();
            return name.EndsWith(".exe") ? name[..^4] : name;
        }

        /// <summary>Art der App oder null, wenn sie nicht sicher erkannt wird. exePath ist optional, hilft aber bei Spielen.</summary>
        public static string? Classify(string process, string? exePath)
        {
            string name = Bare(process);
            if (CommApps.Contains(name)) return Comms;
            if (MusicApps.Contains(name)) return Music;
            if (BrowserApps.Contains(name)) return Browsers;
            if (GameApps.Contains(name)) return Games;
            if (NotGameApps.Contains(name) || NotGameParts.Any(name.Contains)) return null;

            // Unreal-Engine-Spiele heißen fast immer so („Palworld-Win64-Shipping“)
            if (name.Contains("-win64-shipping") || name.Contains("-wingdk-shipping")) return Games;

            string path = (exePath ?? "").ToLowerInvariant();
            if (path.Length > 0)
            {
                if (path.Contains(@"\launcher\")) return null;
                if (GamePathParts.Any(path.Contains)) return Games;
            }
            return WindowsGames().Contains(name) ? Games : null;
        }

        /// <summary>Profil für eine neue App, wenn auto_assign an ist und Art und Profil feststehen; sonst null = nachfragen.</summary>
        public static string? TargetProfile(AppConfig cfg, string process, string? exePath)
        {
            if (!cfg.AutoAssign) return null;
            var category = Classify(process, exePath);
            if (category == null) return null;
            string key = cfg.AutoAssignMap.TryGetValue(category, out var chosen) ? chosen : DefaultProfile(category);
            return key.Length > 0 && ProfileDefinitions.Keys(cfg).Contains(key) ? key : null;
        }

        // ── Windows-Spieleliste ─────────────────────────────────────────────

        private static HashSet<string> _windowsGames = new();
        private static DateTime _windowsGamesRead = DateTime.MinValue;
        private static readonly object GamesLock = new();

        /// <summary>
        /// Die Spiele, die Windows selbst kennt (Game Bar, HKCU\System\GameConfigStore): Programmnamen, eine Minute zwischengespeichert.
        /// </summary>
        private static HashSet<string> WindowsGames()
        {
            lock (GamesLock)
            {
                if ((DateTime.UtcNow - _windowsGamesRead).TotalSeconds < 60) return _windowsGames;
                _windowsGamesRead = DateTime.UtcNow;
                var names = new HashSet<string>();
                try
                {
                    using var children = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore\Children");
                    if (children != null)
                        foreach (var id in children.GetSubKeyNames())
                        {
                            using var child = children.OpenSubKey(id);
                            if (child?.GetValue("MatchedExeFullPath") is string exe && exe.Length > 0)
                                names.Add(Bare(Path.GetFileName(exe)));
                        }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Windows-Spieleliste: {ex.Message}");
                }
                return _windowsGames = names;
            }
        }

        // ── Programmpfad ────────────────────────────────────────────────────

        private const uint ProcessQueryLimitedInformation = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        /// <summary>Voller Pfad der Programmdatei; geht auch ohne Admin-Rechte bei den meisten Prozessen, sonst null.</summary>
        public static string? ExePath(int pid)
        {
            var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
            if (handle == IntPtr.Zero) return null;
            try
            {
                int size = 1024;
                var text = new StringBuilder(size);
                return QueryFullProcessImageName(handle, 0, text, ref size) ? text.ToString() : null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }
    }
}
