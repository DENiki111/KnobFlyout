using System.Runtime.InteropServices;
using System.Text;

namespace KnobFlyout.Osd
{
    /// <summary>
    /// Blendet das eigene Lautstärke-/Media-Flyout von Windows 10 aus (wie ModernFlyouts/HideVolumeOSD): Explorer zeigt es in
    /// einem Fenster „NativeHWNDHost“ mit Kind „DirectUIHWND“. Minimiert erscheint es nicht mehr. Windows legt das Fenster erst
    /// beim ersten Anzeigen an (und nach einem Explorer-Neustart neu), deshalb fängt ein WinEvent-Hook jedes Erscheinen ab.
    /// Ausschalten und Programmende stellen es wieder her. Unter Windows 11 gibt es dieses Fenster so nicht mehr.
    /// </summary>
    internal static class NativeFlyout
    {
        public static bool Supported => Environment.OSVersion.Version.Major == 10 && Environment.OSVersion.Version.Build < 22000;

        private static bool _hidden;
        private static IntPtr _hook;
        private static WinEventDelegate? _proc; // Verweis halten, sonst sammelt der GC den Rückruf ein

        public static void Apply(bool hide)
        {
            if (!Supported) return;
            try
            {
                if (hide)
                {
                    if (_hook == IntPtr.Zero)
                    {
                        _proc = OnWinEvent;
                        _hook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _proc, 0, 0,
                            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
                    }
                    var host = FindHost();
                    if (host != IntPtr.Zero) ShowWindow(host, SW_MINIMIZE);
                    if (!_hidden) Logger.Info("Windows-Flyout ausgeblendet");
                    _hidden = true;
                }
                else if (_hidden)
                {
                    if (_hook != IntPtr.Zero) { UnhookWinEvent(_hook); _hook = IntPtr.Zero; _proc = null; }
                    var host = FindHost();
                    if (host != IntPtr.Zero) ShowWindow(host, SW_RESTORE);
                    _hidden = false;
                    Logger.Info("Windows-Flyout wieder an");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Windows-Flyout: {ex.Message}");
            }
        }

        private static void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != OBJID_WINDOW || hwnd == IntPtr.Zero || !_hidden) return;
            if (IsHost(hwnd)) ShowWindow(hwnd, SW_MINIMIZE);
        }

        private static IntPtr FindHost()
        {
            IntPtr h = IntPtr.Zero;
            while ((h = FindWindowEx(IntPtr.Zero, h, "NativeHWNDHost", null)) != IntPtr.Zero)
                if (IsHost(h)) return h;
            return IntPtr.Zero;
        }

        /// <summary>Flyout-Fenster: Klasse NativeHWNDHost mit Kind DirectUIHWND, gehört dem Explorer.</summary>
        private static bool IsHost(IntPtr hwnd)
        {
            var cls = new StringBuilder(64);
            if (GetClassName(hwnd, cls, cls.Capacity) == 0 || cls.ToString() != "NativeHWNDHost") return false;
            if (FindWindowEx(hwnd, IntPtr.Zero, "DirectUIHWND", null) == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hwnd, out uint pid);
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById((int)pid);
                return string.Equals(p.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private const uint EVENT_OBJECT_SHOW = 0x8002;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
        private const int OBJID_WINDOW = 0;
        private const int SW_MINIMIZE = 6;
        private const int SW_RESTORE = 9;

        private delegate void WinEventDelegate(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventDelegate proc, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    }
}
