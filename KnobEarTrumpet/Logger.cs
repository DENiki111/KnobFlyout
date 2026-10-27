using System;
using System.IO;

namespace KnobEarTrumpet
{
    /// <summary>
    /// Minimaler Datei-Logger, Format angelehnt an das Python-Original
    /// (logging.basicConfig(filename=knob_service.log, level=ERROR)).
    /// Darf niemals selbst eine Exception werfen.
    /// </summary>
    internal static class Logger
    {
        private static readonly string LogFile = Path.Combine(AppContext.BaseDirectory, "knob_service.log");
        private static readonly object Lock = new();

        public static void Error(string message) => Write("ERROR", message);
        public static void Info(string message) => Write("INFO", message);

        private static void Write(string level, string message)
        {
            try
            {
                lock (Lock)
                {
                    var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss,fff} {level} {message}{Environment.NewLine}";
                    File.AppendAllText(LogFile, line);
                }
            }
            catch
            {
                // Logging darf die App nie zum Absturz bringen.
            }
        }
    }
}
