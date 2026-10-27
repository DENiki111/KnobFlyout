using Microsoft.Win32;

namespace KnobFlyout.Config
{
    /// <summary>
    /// „Mit Windows starten“ – genauso wie Setup und Task-Manager: Der Eintrag „KnobFlyout“ unter HKCU\…\Run bleibt
    /// bestehen, an/aus steht in Explorer\StartupApproved\Run (12 Bytes, erstes Byte 2 = an, 3 = aus). So taucht
    /// KnobFlyout immer im Task-Manager unter „Autostart-Apps“ auf und lässt sich dort wie in den Einstellungen umschalten.
    /// </summary>
    public static class Autostart
    {
        public const string Name = "KnobFlyout";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        /// <summary>Run-Eintrag vorhanden und nicht im Task-Manager deaktiviert.</summary>
        public static bool IsEnabled()
        {
            try
            {
                using var run = Registry.CurrentUser.OpenSubKey(RunKey);
                if (run?.GetValue(Name) == null) return false;
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
                return !(approved?.GetValue(Name) is byte[] state && state.Length > 0 && (state[0] & 1) == 1);
            }
            catch { return false; }
        }

        /// <summary>
        /// Ein: Run-Eintrag auf die laufende exe setzen und freigeben. Aus: nur abschalten – der Eintrag bleibt
        /// (fehlt er, wird er angelegt), damit KnobFlyout im Task-Manager sichtbar bleibt.
        /// </summary>
        public static void SetEnabled(bool enabled)
        {
            using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled || run.GetValue(Name) == null)
                    run.SetValue(Name, "\"" + Environment.ProcessPath + "\"");
            }
            var state = new byte[12];
            state[0] = enabled ? (byte)2 : (byte)3;
            if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(state, 4);
            using var approved = Registry.CurrentUser.CreateSubKey(ApprovedKey);
            approved.SetValue(Name, state, RegistryValueKind.Binary);
        }
    }
}
