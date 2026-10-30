using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace KnobFlyoutSetup
{
    /// <summary>Optionen aus dem Assistenten.</summary>
    internal sealed class InstallOptions
    {
        public string TargetDir;
        public bool Desktop, StartMenu, Autostart;
    }

    /// <summary>
    /// Die eigentliche Installation (pro Benutzer, ohne Admin, solange der Zielordner beschreibbar ist): Programmdatei
    /// aus dem eingebetteten payload.zip entpacken, vorhandene Einstellungen behalten, Verknüpfungen, Autostart,
    /// Eintrag unter „Apps &amp; Features“ und uninstall.cmd anlegen.
    /// </summary>
    internal static class InstallEngine
    {
        public const string AppName = "KnobFlyout";
        public const string ExeName = "KnobFlyout.exe";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\KnobFlyout";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        // Vorgänger unter dem alten Namen: wird bei der Installation übernommen (Einstellungen) und entfernt.
        private const string LegacyName = "ModernFlyout";
        private const string LegacyExe = "ModernFlyout.exe";
        private const string LegacyUninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ModernFlyout";

        public static string DefaultBaseDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");

        public static string DesktopShortcut =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");

        public static string StartMenuShortcut =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");

        public static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        /// <summary>Zielordner = gewählter Ordner + „KnobFlyout“ (außer er heißt schon so).</summary>
        public static string TargetDir(string baseDir)
        {
            string dir = (baseDir ?? "").Trim().TrimEnd('\\', '/');
            if (dir.Length == 0) return "";
            return string.Equals(Path.GetFileName(dir), AppName, StringComparison.OrdinalIgnoreCase) ? dir : Path.Combine(dir, AppName);
        }

        /// <summary>Ordner einer vorhandenen Installation (für Updates), sonst null.</summary>
        public static string ExistingInstall() => InstalledAt(UninstallKey, ExeName);

        /// <summary>Ordner einer installierten Vorgängerversion „ModernFlyout“, sonst null.</summary>
        public static string LegacyInstall() => InstalledAt(LegacyUninstallKey, LegacyExe);

        /// <summary>Version der vorhandenen Installation laut „Apps &amp; Features“, sonst null.</summary>
        public static string ExistingVersion() => VersionIn(UninstallKey);

        /// <summary>Version der Vorgängerversion „ModernFlyout“, sonst null.</summary>
        public static string LegacyVersion() => VersionIn(LegacyUninstallKey);

        private static string VersionIn(string uninstallKey)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(uninstallKey))
                    return key?.GetValue("DisplayVersion") as string;
            }
            catch { return null; }
        }

        /// <summary>Installierte Version gegen dieses Setup: kleiner 0 = älter oder unbekannt, 0 = gleich, größer 0 = neuer.</summary>
        public static int CompareInstalled(string installed) =>
            System.Version.TryParse(installed, out var other) && System.Version.TryParse(Version, out var own) ? other.CompareTo(own) : -1;

        /// <summary>Sprache der vorhandenen Installation aus ihrer config.json ("de"/"en"), sonst null.</summary>
        public static string InstalledLanguage()
        {
            string dir = ExistingInstall() ?? LegacyInstall();
            if (dir == null) return null;
            try
            {
                var match = Regex.Match(File.ReadAllText(Path.Combine(dir, "config.json")), "\"language\"\\s*:\\s*\"(de|en)\"");
                return match.Success ? match.Groups[1].Value : null;
            }
            catch { return null; }
        }

        private static string InstalledAt(string uninstallKey, string exeName)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(uninstallKey))
                {
                    var dir = key?.GetValue("InstallLocation") as string;
                    return !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, exeName)) ? dir : null;
                }
            }
            catch { return null; }
        }

        public static bool HasPayload => Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains("payload.zip");

        /// <summary>Größe der Programmdateien in Bytes (für die Anzeige „benötigter Speicherplatz“).</summary>
        public static long PayloadSize()
        {
            if (!HasPayload) return 0;
            using (var zip = OpenPayload()) return zip.Entries.Sum(e => e.Length);
        }

        private static ZipArchive OpenPayload() =>
            new ZipArchive(Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"), ZipArchiveMode.Read);

        /// <summary>Kann in den Ordner geschrieben werden? (sonst braucht das Setup Administratorrechte)</summary>
        public static bool CanWrite(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".setup-probe");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch (UnauthorizedAccessException) { return false; }
            catch (System.Security.SecurityException) { return false; }
        }

        /// <summary>Laufende KnobFlyout-Prozesse (installiert, als Vorgänger ModernFlyout.exe oder portabel als KnobEarTrumpet.exe).</summary>
        public static List<Process> RunningApps() =>
            new[] { "KnobFlyout", LegacyName, "KnobEarTrumpet" }.SelectMany(Process.GetProcessesByName).ToList();

        /// <summary>Ordner, aus dem eine laufende Version gestartet wurde (für die Übernahme ihrer Einstellungen).</summary>
        public static string FolderOf(Process p)
        {
            try { return Path.GetDirectoryName(p.MainModule.FileName); }
            catch { return null; }
        }

        public static void Install(InstallOptions o, List<string> runningFolders, Action<int, string> progress)
        {
            string dir = o.TargetDir;
            Directory.CreateDirectory(dir);

            // Einstellungen: bei einem Update bleiben config.json & Co. unangetastet; bei einer Neuinstallation (auch beim
            // Umzug in einen anderen Ordner) die der vorhandenen Installation, der Vorgängerversion „ModernFlyout“ bzw. einer
            // gerade laufenden (portablen) Version übernehmen, sonst nur die gewählte Sprache vorgeben.
            string legacy = LegacyInstall();
            string existing = ExistingInstall();
            string config = Path.Combine(dir, "config.json");
            if (!File.Exists(config))
            {
                string source = new[] { existing, legacy }.Concat(runningFolders).FirstOrDefault(f => f != null && File.Exists(Path.Combine(f, "config.json"))
                    && !SamePath(f, dir));
                if (source != null)
                {
                    progress(2, L.T("Einstellungen werden übernommen …", "Taking over settings …"));
                    foreach (var name in new[] { "config.json", ".spotify_cache" })
                    {
                        string from = Path.Combine(source, name);
                        if (File.Exists(from)) File.Copy(from, Path.Combine(dir, name), true);
                    }
                }
                else File.WriteAllText(config, "{\r\n  \"language\": \"" + L.Code + "\"\r\n}\r\n", new UTF8Encoding(false));
            }

            // Programmdateien entpacken (mit Fortschritt nach Bytes)
            long total = 0;
            using (var zip = OpenPayload())
            {
                total = Math.Max(1, zip.Entries.Sum(e => e.Length));
                long done = 0;
                var buffer = new byte[1 << 16];
                foreach (var entry in zip.Entries)
                {
                    string path = Path.GetFullPath(Path.Combine(dir, entry.FullName));
                    if (!path.StartsWith(Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase)) continue; // nur innerhalb des Zielordners
                    if (entry.FullName.EndsWith("/")) { Directory.CreateDirectory(path); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    progress(5 + (int)(85 * done / total), L.T("Kopiere ", "Copying ") + entry.FullName + " …");
                    string temp = path + ".new";
                    using (var input = entry.Open())
                    using (var output = File.Create(temp))
                    {
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            output.Write(buffer, 0, read);
                            done += read;
                            progress(5 + (int)(85 * done / total), null);
                        }
                    }
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(temp, path); // erst nach vollständigem Entpacken ersetzen
                }
            }

            string exe = Path.Combine(dir, ExeName);
            progress(92, L.T("Verknüpfungen werden angelegt …", "Creating shortcuts …"));
            SetShortcut(DesktopShortcut, o.Desktop, exe, dir);
            SetShortcut(StartMenuShortcut, o.StartMenu, exe, dir);

            progress(96, L.T("Programm wird registriert …", "Registering the program …"));
            // Autostart: Run-Eintrag immer anlegen, an/aus wie der Task-Manager über StartupApproved – so steht KnobFlyout
            // immer unter „Autostart-Apps“ im Task-Manager und lässt sich dort oder in den Einstellungen umschalten.
            using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
                run.SetValue(AppName, "\"" + exe + "\"");
            SetStartupApproved(AppName, o.Autostart);

            string uninstall = Path.Combine(dir, "uninstall.cmd");
            File.WriteAllText(uninstall, UninstallScript(dir), new UTF8Encoding(false));
            using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", AppName);
                key.SetValue("DisplayVersion", Version);
                key.SetValue("Publisher", AppName);
                key.SetValue("DisplayIcon", exe);
                key.SetValue("InstallLocation", dir);
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                key.SetValue("UninstallString", "\"" + Path.Combine(Environment.SystemDirectory, "cmd.exe") + "\" /c \"\"" + uninstall + "\"\"");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, total / 1024), RegistryValueKind.DWord);
            }

            if (legacy != null)
            {
                progress(98, L.T("Alte Version „ModernFlyout“ wird entfernt …", "Removing the old version “ModernFlyout” …"));
                RemoveLegacy(legacy, dir);
            }
            if (existing != null && !SamePath(existing, dir))
            {
                progress(98, L.T("Alter Programmordner wird entfernt …", "Removing the old program folder …"));
                RemoveOldFolder(existing, dir, AppName, ExeName);
            }
            progress(100, L.T("Fertig.", "Done."));
        }

        /// <summary>Autostart gerade an? (Run-Eintrag vorhanden und nicht im Task-Manager deaktiviert)</summary>
        public static bool AutostartEnabled()
        {
            try
            {
                using (var run = Registry.CurrentUser.OpenSubKey(RunKey))
                using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey))
                    return run?.GetValue(AppName) != null
                        && !(approved?.GetValue(AppName) is byte[] state && state.Length > 0 && (state[0] & 1) == 1);
            }
            catch { return false; }
        }

        /// <summary>An/aus wie im Task-Manager: 12 Bytes, erstes Byte 2 = an, 3 = aus (dahinter der Zeitpunkt des Abschaltens).</summary>
        private static void SetStartupApproved(string name, bool enabled)
        {
            var state = new byte[12];
            state[0] = enabled ? (byte)2 : (byte)3;
            if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(state, 4);
            using (var key = Registry.CurrentUser.CreateSubKey(ApprovedKey))
                key.SetValue(name, state, RegistryValueKind.Binary);
        }

        private static bool SamePath(string a, string b) =>
            string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Vorgänger „ModernFlyout“ entfernen (erst nach erfolgreicher Installation): Autostart, Verknüpfungen, Eintrag unter
        /// „Apps &amp; Features“ und den alten Programmordner. Liegt der neue Ordner darin, nur die alten Programmdateien.
        /// </summary>
        private static void RemoveLegacy(string legacyDir, string newDir)
        {
            using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
                if (run.GetValue(LegacyName) != null) run.DeleteValue(LegacyName, false);
            using (var approved = Registry.CurrentUser.CreateSubKey(ApprovedKey))
                if (approved.GetValue(LegacyName) != null) approved.DeleteValue(LegacyName, false);
            foreach (var folder in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.Programs })
            {
                string path = Path.Combine(Environment.GetFolderPath(folder), LegacyName + ".lnk");
                try { if (File.Exists(path)) File.Delete(path); } catch { /* bleibt liegen */ }
            }
            Registry.CurrentUser.DeleteSubKeyTree(LegacyUninstallKey, false);
            RemoveOldFolder(legacyDir, newDir, LegacyName, LegacyExe);
        }

        /// <summary>Alten Programmordner löschen. Nur einen, der wirklich vom Setup stammt (trägt dessen Namen); liegt der neue Ordner darin, nur die Programmdateien.</summary>
        private static void RemoveOldFolder(string oldDir, string newDir, string folderName, string exeName)
        {
            if (!string.Equals(Path.GetFileName(oldDir.TrimEnd('\\')), folderName, StringComparison.OrdinalIgnoreCase)) return;
            string full = Path.GetFullPath(oldDir).TrimEnd('\\') + "\\";
            bool containsNew = (Path.GetFullPath(newDir).TrimEnd('\\') + "\\").StartsWith(full, StringComparison.OrdinalIgnoreCase);
            try
            {
                if (!containsNew) Directory.Delete(oldDir, true);
                else foreach (var name in new[] { exeName, "uninstall.cmd" })
                    if (File.Exists(Path.Combine(oldDir, name))) File.Delete(Path.Combine(oldDir, name));
            }
            catch { /* gerade gesperrte Datei: Ordner bleibt liegen, die neue Version läuft trotzdem */ }
        }

        /// <summary>Verknüpfung anlegen (WScript.Shell) bzw. eine frühere entfernen, wenn die Option abgewählt ist.</summary>
        private static void SetShortcut(string link, bool wanted, string exe, string dir)
        {
            if (!wanted)
            {
                if (File.Exists(link)) File.Delete(link);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(link));
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            try
            {
                dynamic shortcut = shell.CreateShortcut(link);
                shortcut.TargetPath = exe;
                shortcut.WorkingDirectory = dir;
                shortcut.IconLocation = exe + ",0";
                shortcut.Description = AppName;
                shortcut.Save();
                Marshal.FinalReleaseComObject(shortcut);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        /// <summary>
        /// Deinstallation (auch über „Apps &amp; Features“): kopiert sich nach %TEMP%, fragt nach, beendet die App, entfernt
        /// Autostart, Registrierung und Verknüpfungen und löscht den Programmordner samt Einstellungen.
        /// </summary>
        private static string UninstallScript(string dir)
        {
            string yes = L.T("J", "Y"), no = L.T("N", "N");
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine("if /i not \"%~1\"==\"/run\" (");
            sb.AppendLine("  copy /y \"%~f0\" \"%TEMP%\\KnobFlyout-uninstall.cmd\" >nul");
            sb.AppendLine("  start \"KnobFlyout\" \"%TEMP%\\KnobFlyout-uninstall.cmd\" /run");
            sb.AppendLine("  exit /b");
            sb.AppendLine(")");
            sb.AppendLine("title KnobFlyout");
            sb.AppendLine("choice /c " + yes + no + " /m \"" + L.T("KnobFlyout wirklich deinstallieren", "Really uninstall KnobFlyout") + "\"");
            sb.AppendLine("if errorlevel 2 goto :end");
            sb.AppendLine("taskkill /f /im " + ExeName + " >nul 2>&1");
            sb.AppendLine("timeout /t 2 /nobreak >nul");
            sb.AppendLine("reg delete \"HKCU\\" + RunKey + "\" /v " + AppName + " /f >nul 2>&1");
            sb.AppendLine("reg delete \"HKCU\\" + ApprovedKey + "\" /v " + AppName + " /f >nul 2>&1");
            sb.AppendLine("reg delete \"HKCU\\" + UninstallKey + "\" /f >nul 2>&1");
            sb.AppendLine("del /f /q \"" + DesktopShortcut + "\" >nul 2>&1");
            sb.AppendLine("del /f /q \"" + StartMenuShortcut + "\" >nul 2>&1");
            sb.AppendLine("rd /s /q \"" + dir + "\"");
            sb.AppendLine("echo " + L.T("KnobFlyout wurde deinstalliert.", "KnobFlyout has been uninstalled."));
            sb.AppendLine("timeout /t 3 >nul");
            sb.AppendLine(":end");
            sb.AppendLine("(goto) 2>nul & del \"%~f0\"");
            return sb.ToString();
        }
    }
}
