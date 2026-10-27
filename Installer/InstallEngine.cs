using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ModernFlyoutSetup
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
        public const string AppName = "ModernFlyout";
        public const string ExeName = "ModernFlyout.exe";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ModernFlyout";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static string DefaultBaseDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");

        public static string DesktopShortcut =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");

        public static string StartMenuShortcut =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");

        public static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        /// <summary>Zielordner = gewählter Ordner + „ModernFlyout“ (außer er heißt schon so).</summary>
        public static string TargetDir(string baseDir)
        {
            string dir = (baseDir ?? "").Trim().TrimEnd('\\', '/');
            if (dir.Length == 0) return "";
            return string.Equals(Path.GetFileName(dir), AppName, StringComparison.OrdinalIgnoreCase) ? dir : Path.Combine(dir, AppName);
        }

        /// <summary>Ordner einer vorhandenen Installation (für Updates), sonst null.</summary>
        public static string ExistingInstall()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(UninstallKey))
                {
                    var dir = key?.GetValue("InstallLocation") as string;
                    return !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, ExeName)) ? dir : null;
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

        /// <summary>Laufende ModernFlyout-Prozesse (installiert oder portabel als KnobEarTrumpet.exe).</summary>
        public static List<Process> RunningApps() =>
            Process.GetProcessesByName("ModernFlyout").Concat(Process.GetProcessesByName("KnobEarTrumpet")).ToList();

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

            // Einstellungen: bei einem Update bleiben config.json & Co. unangetastet; bei einer Neuinstallation die einer
            // gerade laufenden (portablen) Version übernehmen, sonst nur die gewählte Sprache vorgeben.
            string config = Path.Combine(dir, "config.json");
            if (!File.Exists(config))
            {
                string source = runningFolders.FirstOrDefault(f => f != null && File.Exists(Path.Combine(f, "config.json"))
                    && !string.Equals(Path.GetFullPath(f).TrimEnd('\\'), Path.GetFullPath(dir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
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
            using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (o.Autostart) run.SetValue(AppName, "\"" + exe + "\"");
                else if (run.GetValue(AppName) != null) run.DeleteValue(AppName, false);
            }

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
            progress(100, L.T("Fertig.", "Done."));
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
            sb.AppendLine("  copy /y \"%~f0\" \"%TEMP%\\ModernFlyout-uninstall.cmd\" >nul");
            sb.AppendLine("  start \"ModernFlyout\" \"%TEMP%\\ModernFlyout-uninstall.cmd\" /run");
            sb.AppendLine("  exit /b");
            sb.AppendLine(")");
            sb.AppendLine("title ModernFlyout");
            sb.AppendLine("choice /c " + yes + no + " /m \"" + L.T("ModernFlyout wirklich deinstallieren", "Really uninstall ModernFlyout") + "\"");
            sb.AppendLine("if errorlevel 2 goto :end");
            sb.AppendLine("taskkill /f /im " + ExeName + " >nul 2>&1");
            sb.AppendLine("timeout /t 2 /nobreak >nul");
            sb.AppendLine("reg delete \"HKCU\\" + RunKey + "\" /v " + AppName + " /f >nul 2>&1");
            sb.AppendLine("reg delete \"HKCU\\" + UninstallKey + "\" /f >nul 2>&1");
            sb.AppendLine("del /f /q \"" + DesktopShortcut + "\" >nul 2>&1");
            sb.AppendLine("del /f /q \"" + StartMenuShortcut + "\" >nul 2>&1");
            sb.AppendLine("rd /s /q \"" + dir + "\"");
            sb.AppendLine("echo " + L.T("ModernFlyout wurde deinstalliert.", "ModernFlyout has been uninstalled."));
            sb.AppendLine("timeout /t 3 >nul");
            sb.AppendLine(":end");
            sb.AppendLine("(goto) 2>nul & del \"%~f0\"");
            return sb.ToString();
        }
    }
}
