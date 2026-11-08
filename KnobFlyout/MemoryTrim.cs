using System.Runtime;
using System.Runtime.InteropServices;

namespace KnobFlyout
{
    /// <summary>
    /// Gibt Speicher an Windows zurück, wenn große Fenster (Einstellungen, Einführung) geschlossen sind:
    /// Heap komplett aufräumen und verdichten, danach den Arbeitssatz kürzen. Läuft nur im Leerlauf.
    /// </summary>
    internal static class MemoryTrim
    {
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

        public static void Run()
        {
            try
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
            }
            catch (Exception ex)
            {
                Logger.Error($"MemoryTrim: {ex.Message}");
            }
        }
    }
}
