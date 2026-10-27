using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace KnobEarTrumpet.Input
{
    /// <summary>Entspricht _parse_hotkey()/_HK_NAME2VK/_VK_NORMALIZE aus knob_service.py.</summary>
    internal static class HotkeyParser
    {
        private static readonly HashSet<int> ModifierVks = new()
        {
            0x10, 0x11, 0x12, 0x5B, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5C,
        };

        private static readonly Dictionary<int, int> VkNormalize = new()
        {
            { 0xA0, 0x10 }, { 0xA1, 0x10 }, // Shift L/R
            { 0xA2, 0x11 }, { 0xA3, 0x11 }, // Ctrl  L/R
            { 0xA4, 0x12 }, { 0xA5, 0x12 }, // Alt   L/R
            { 0x5C, 0x5B },                 // Win   R
        };

        private static readonly Dictionary<string, int> NameToVk = BuildNameMap();
        private static readonly Dictionary<int, string> VkToName = BuildReverseMap();
        private static string? _lastUnknownPart;

        /// <summary>Für die Hotkey-Aufnahme im Settings-Fenster: VK-Code → kanonischer Name (z.B. 0x11 → "ctrl").</summary>
        public static string? NameForVk(int vk)
        {
            if (VkToName.TryGetValue(vk, out var n)) return n;

            // Alles, was nicht in der festen Namensliste steht (F13–F24, Oem-Tasten wie ß/ü/#, Medien-,
            // Browser-Tasten, Apps-Taste, ...): Name aus WPFs Tasten-Aufzählung (z.B. "f13", "oemplus").
            try
            {
                var key = System.Windows.Input.KeyInterop.KeyFromVirtualKey(vk);
                if (key != System.Windows.Input.Key.None) return key.ToString().ToLowerInvariant();
            }
            catch
            {
                // unbekannter Code - dann eben nicht unterstützt
            }
            return null;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern short VkKeyScanW(char ch);

        public static bool IsModifier(int vk) => ModifierVks.Contains(vk);

        public static int Normalize(int vk) => VkNormalize.TryGetValue(vk, out var n) ? n : vk;

        private static Dictionary<string, int> BuildNameMap()
        {
            var d = new Dictionary<string, int>
            {
                ["ctrl"] = 0x11, ["control"] = 0x11, ["shift"] = 0x10, ["alt"] = 0x12, ["win"] = 0x5B,
                ["home"] = 0x24, ["end"] = 0x23, ["pageup"] = 0x21, ["pagedown"] = 0x22,
                ["insert"] = 0x2D, ["delete"] = 0x2E, ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28,
                ["space"] = 0x20, ["enter"] = 0x0D, ["tab"] = 0x09, ["esc"] = 0x1B, ["backspace"] = 0x08,
                ["printscreen"] = 0x2C, ["scrolllock"] = 0x91, ["numlock"] = 0x90, ["capslock"] = 0x14, ["pause"] = 0x13,
                ["num+"] = 0x6B, ["num-"] = 0x6D, ["num*"] = 0x6A, ["num/"] = 0x6F, ["num."] = 0x6E, ["numenter"] = 0x0D,
            };
            for (char c = 'a'; c <= 'z'; c++) d[c.ToString()] = 0x41 + (c - 'a');
            for (int i = 0; i < 10; i++)
            {
                d[i.ToString()] = 0x30 + i;
                d["num" + i] = 0x60 + i;
            }
            for (int n = 1; n <= 12; n++) d["f" + n] = 0x70 + n - 1;
            return d;
        }

        /// <summary>
        /// Kehrt NameToVk um, erster Eintrag pro VK-Code gewinnt – daher "ctrl" vor "control" und
        /// "a".."z" vor einem eventuell kollidierenden Spezialnamen, da BuildNameMap in dieser
        /// Reihenfolge befüllt. Für die Hotkey-Aufnahme per Tastendruck im Settings-Fenster.
        /// </summary>
        private static Dictionary<int, string> BuildReverseMap()
        {
            var rev = new Dictionary<int, string>();
            foreach (var kv in NameToVk)
            {
                if (!rev.ContainsKey(kv.Value)) rev[kv.Value] = kv.Key;
            }
            return rev;
        }

        /// <summary>Config-String (z.B. "ctrl+shift+p") → (Modifier-VKs, Haupt-VK). Bei Fehler (leer, 0).</summary>
        public static (HashSet<int> Mods, int Main) Parse(string? s)
        {
            var mods = new HashSet<int>();
            int main = 0;
            if (string.IsNullOrWhiteSpace(s)) return (mods, 0);

            foreach (var raw in s.Split('+'))
            {
                var p = raw.Trim().ToLowerInvariant();
                int vk = 0;
                if (!NameToVk.TryGetValue(p, out vk))
                {
                    if (p.Length == 1)
                    {
                        short res = VkKeyScanW(p[0]);
                        vk = res == -1 ? 0 : (res & 0xFF);
                    }
                    else if (p.Length > 1 && char.IsLetter(p[0])
                             && Enum.TryParse<System.Windows.Input.Key>(p, true, out var wpfKey)
                             && wpfKey != System.Windows.Input.Key.None)
                    {
                        // Gegenstück zu NameForVk: beliebige Taste per WPF-Namen (Buchstabe zuerst, damit
                        // reine Zahlen nicht als Enum-Wert gelesen werden).
                        vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(wpfKey);
                    }
                }

                if (vk == 0)
                {
                    // Nur bei neuem Wert loggen: Parse läuft per Timer alle 2 s, sonst Log-Flut bei ungültigem Hotkey.
                    if (p != _lastUnknownPart)
                    {
                        _lastUnknownPart = p;
                        Logger.Error($"Hotkey-Teil unbekannt: {p}");
                    }
                    return (new HashSet<int>(), 0);
                }

                if (ModifierVks.Contains(vk)) mods.Add(vk);
                else main = vk;
            }
            return (mods, main);
        }
    }
}
