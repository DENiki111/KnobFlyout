using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using KnobFlyout.Config;
using static KnobFlyout.Input.NativeMethods;

namespace KnobFlyout.Input
{
    /// <summary>
    /// Entspricht _hook_callback()/_install_hook()/setup_hook() aus knob_service.py.
    /// Läuft auf dem WPF-Main-Thread (Dispatcher liefert die Message-Pump). Die Event-Handler
    /// müssen sofort zurückkehren (LowLevelHooksTimeout).
    ///
    /// Profil-Hotkey (aus config "profile_hotkey"): wird geblockt, Kurzdruck löst beim KeyUp
    /// den Profilwechsel aus. Langdruck (>= snap_hold_ms) löst stattdessen SnapCycleRequested aus
    /// (siehe StartLongPressWatch/_wait_long_press-Äquivalent), kein Profilwechsel in dem Fall.
    /// </summary>
    public class LowLevelKeyboardHook : IDisposable
    {
        private const int HookRefreshMs = 20000;
        private const int HotkeyCfgRefreshMs = 2000;

        private IntPtr _hookHandle = IntPtr.Zero;
        private LowLevelKeyboardProc? _procRef;
        private DispatcherTimer? _watchdog;
        private DispatcherTimer? _cfgTimer;
        private bool _loggedInstall;

        private readonly HashSet<int> _modsHeld = new();
        private readonly HashSet<int> _lockKeysDown = new(); // gedrückte Feststelltasten (Wiederholungen ignorieren)
        private HashSet<int> _targetMods = new();
        private int _targetMain;
        private int _holdMs = 600;
        private bool _repeatFast = true;
        private long _pressStart; // 0 = Hotkey nicht gedrückt
        private bool _longFired;
        private DispatcherTimer? _longPressTimer;

        // Globale Hotkeys für 10 s vor/zurück (leer = deaktiviert); feuern einmal pro Tastendruck.
        private HashSet<int> _skipFwdMods = new();
        private int _skipFwdMain;
        private bool _skipFwdDown;
        private HashSet<int> _skipBackMods = new();
        private int _skipBackMain;
        private bool _skipBackDown;

        /// <summary>
        /// Eine Medien-Aktion auf Taste + Geste. Taste = Medien-Play/Pause (wird NICHT geblockt, Windows
        /// spielt/pausiert weiter normal) oder ein eigener Hotkey (wird geblockt). Geste = lange halten
        /// (media_switch_hold_ms) oder zweimal schnell drücken (media_double_ms).
        /// </summary>
        private sealed class GestureBinding
        {
            public bool Enabled;
            public bool UsePlayPause = true;
            public HashSet<int> Mods = new();
            public int Main;
            public bool Double;
            public bool Down;        // Taste gerade gedrückt (Tasten-Wiederholung ignorieren)
            public long LastPress;   // für "zweimal schnell"
            public DispatcherTimer? HoldTimer;
            public Action? Fire;
        }

        /// <summary>
        /// Zuweisen-Taste eines Profils (profile_list[].assign_hotkey): wirkt nur, solange das Neue-App-Popup
        /// offen ist (AssignPopupOpen), feuert einmal pro Tastendruck und wird dann geblockt; sonst geht sie normal weiter.
        /// </summary>
        private sealed class ProfileBinding
        {
            public string Key = "";
            public HashSet<int> Mods = new();
            public int Main;
            public bool Down; // Taste gerade gedrückt (Tasten-Wiederholung ignorieren)
        }

        private List<ProfileBinding> _profileBindings = new();

        private readonly GestureBinding _switchGesture = new();
        private readonly GestureBinding _nextGesture = new();
        private readonly GestureBinding[] _gestures;
        private int _mediaHoldMs = 600;
        private int _mediaDoubleMs = 400;

        public LowLevelKeyboardHook()
        {
            _switchGesture.Fire = () => MediaSourceSwitchRequested?.Invoke(this, EventArgs.Empty);
            _nextGesture.Fire = () => MediaNextRequested?.Invoke(this, EventArgs.Empty);
            _gestures = new[] { _switchGesture, _nextGesture };
        }

        public event EventHandler? VolumeUp;
        public event EventHandler? VolumeDown;
        public event EventHandler? MutePressed;
        public event EventHandler? ProfileHotkeyPressed;

        /// <summary>Entspricht osd_snap_move(): feuert beim Erreichen von snap_hold_ms und danach – solange der Hotkey gehalten wird – alle snap_hold_ms/2 (snap_repeat_fast) bzw. snap_hold_ms.</summary>
        public event EventHandler? SnapCycleRequested;

        /// <summary>Feuert NACH dem Umschalten (nicht blockierend), name = "CapsLock"/"NumLock"/"ScrollLock".</summary>
        public event Action<string, bool>? LockKeyToggled;

        /// <summary>Geste für "Quelle wechseln" erkannt (media_switch_enabled/_key/_gesture).</summary>
        public event EventHandler? MediaSourceSwitchRequested;

        /// <summary>Geste für "Nächster Titel" erkannt (media_next_enabled/_key/_gesture).</summary>
        public event EventHandler? MediaNextRequested;

        /// <summary>Feuert bei einem der Skip-Hotkeys: +1 = 10 s vor, -1 = 10 s zurück. Die Taste wird geblockt.</summary>
        public event Action<int>? MediaSkipRequested;

        /// <summary>Zuweisen-Taste eines Profils im offenen Neue-App-Popup gedrückt (Argument = Profil-Key). Die Taste wird geblockt.</summary>
        public event Action<string>? ProfileAssignRequested;

        /// <summary>Neue-App-Popup gerade sichtbar: nur dann wirken die Zuweisen-Tasten (von App.xaml.cs gesetzt).</summary>
        public bool AssignPopupOpen { get; set; }

        public void Install()
        {
            RefreshHotkeyTarget();
            InstallHook();

            _watchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HookRefreshMs) };
            _watchdog.Tick += (_, _) => InstallHook();
            _watchdog.Start();

            // Hotkey-Config regelmäßig außerhalb des Hook-Callbacks nachladen: Datei-I/O im Callback
            // kann LowLevelHooksTimeout überschreiten, dann entfernt Windows den Hook stillschweigend.
            _cfgTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HotkeyCfgRefreshMs) };
            _cfgTimer.Tick += (_, _) => RefreshHotkeyTarget();
            _cfgTimer.Start();
        }

        private void InstallHook()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }

            _procRef = HookCallback;
            _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _procRef, GetModuleHandle(null), 0);

            if (_hookHandle == IntPtr.Zero)
                Logger.Error("Keyboard-Hook (neu) fehlgeschlagen");
            else if (!_loggedInstall)
            {
                _loggedInstall = true;
                Logger.Info("Keyboard-Hook installiert");
            }
        }

        /// <summary>Hotkey-Einstellungen sofort neu einlesen (z.B. nach "Speichern" oder dem "…"-Menü).</summary>
        public void ReloadConfig() => RefreshHotkeyTarget();

        private void RefreshHotkeyTarget()
        {
            try
            {
                var cfg = ConfigManager.Load();
                (_targetMods, _targetMain) = HotkeyParser.Parse(cfg.ProfileHotkey);
                _holdMs = cfg.SnapHoldMs;
                _repeatFast = cfg.SnapRepeatFast;
                // Abgeschaltet: wie "kein Hotkey" behandeln (Main = 0), die Tasten bleiben gespeichert.
                (_skipFwdMods, _skipFwdMain) = HotkeyParser.Parse(cfg.MediaSkipHotkeysEnabled ? cfg.MediaSkipForwardHotkey : "");
                (_skipBackMods, _skipBackMain) = HotkeyParser.Parse(cfg.MediaSkipHotkeysEnabled ? cfg.MediaSkipBackHotkey : "");
                Configure(_switchGesture, cfg.MediaSwitchEnabled, cfg.MediaSwitchKey, cfg.MediaSwitchGesture);
                Configure(_nextGesture, cfg.MediaNextEnabled, cfg.MediaNextKey, cfg.MediaNextGesture);
                _mediaHoldMs = cfg.MediaSwitchHoldMs;
                _mediaDoubleMs = cfg.MediaDoubleMs;

                // Zuweisen-Tasten je Profil; „gedrückt“-Zustand übernehmen, sonst feuert eine gehaltene Taste nach dem Nachladen erneut.
                var old = _profileBindings;
                var bindings = new List<ProfileBinding>();
                foreach (var p in cfg.ProfileList)
                {
                    if (string.IsNullOrWhiteSpace(p.AssignHotkey)) continue;
                    var (mods, main) = HotkeyParser.Parse(p.AssignHotkey);
                    if (main == 0) continue; // ungültig → ignorieren
                    bindings.Add(new ProfileBinding
                    {
                        Key = p.Key, Mods = mods, Main = main,
                        Down = old.Exists(o => o.Key == p.Key && o.Main == main && o.Down),
                    });
                }
                _profileBindings = bindings;
            }
            catch (Exception ex)
            {
                Logger.Error($"Hotkey-Config: {ex.Message}");
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            string? lockKeyName = null;
            int lockKeyVk = 0;

            try
            {
                if (nCode >= 0)
                {
                    var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    int vk = (int)kb.vkCode;
                    int vkNorm = HotkeyParser.Normalize(vk);
                    long msg = wParam.ToInt64();

                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        if (HotkeyParser.IsModifier(vk))
                        {
                            _modsHeld.Add(vkNorm);
                        }
                        else if (vk == VK_VOLUME_UP)
                        {
                            VolumeUp?.Invoke(this, EventArgs.Empty);
                            return (IntPtr)1;
                        }
                        else if (vk == VK_VOLUME_DOWN)
                        {
                            VolumeDown?.Invoke(this, EventArgs.Empty);
                            return (IntPtr)1;
                        }
                        else if (vk == VK_VOLUME_MUTE)
                        {
                            MutePressed?.Invoke(this, EventArgs.Empty);
                            return (IntPtr)1;
                        }
                        else if ((vk == VK_CAPITAL || vk == VK_NUMLOCK || vk == VK_SCROLL) && _lockKeysDown.Add(vk))
                        {
                            // Nicht blocken - Windows schaltet selbst um. Nur der erste KeyDown zählt
                            // (gehaltene Taste sendet Wiederholungen, schaltet aber nicht erneut um).
                            lockKeyVk = vk;
                            lockKeyName = vk == VK_CAPITAL ? "CapsLock" : vk == VK_NUMLOCK ? "NumLock" : "ScrollLock";
                        }

                        // Medien-Gesten (Nächster Titel / Quelle wechseln). Play/Pause läuft trotzdem an
                        // Windows weiter, ein eigener Hotkey wird geblockt.
                        bool blockGesture = false;
                        foreach (var g in _gestures)
                        {
                            if (!g.Enabled) continue;
                            bool hit = g.UsePlayPause ? vk == VK_MEDIA_PLAY_PAUSE : vk == g.Main && g.Mods.IsSubsetOf(_modsHeld);
                            if (!hit) continue;
                            GestureDown(g);
                            if (!g.UsePlayPause) blockGesture = true;
                        }
                        if (blockGesture) return (IntPtr)1;

                        if (_skipFwdMain != 0 && vk == _skipFwdMain && _skipFwdMods.IsSubsetOf(_modsHeld))
                        {
                            if (!_skipFwdDown) { _skipFwdDown = true; MediaSkipRequested?.Invoke(+1); }
                            return (IntPtr)1;
                        }

                        if (_skipBackMain != 0 && vk == _skipBackMain && _skipBackMods.IsSubsetOf(_modsHeld))
                        {
                            if (!_skipBackDown) { _skipBackDown = true; MediaSkipRequested?.Invoke(-1); }
                            return (IntPtr)1;
                        }

                        if (AssignPopupOpen)
                        {
                            foreach (var b in _profileBindings)
                            {
                                if (vk != b.Main || !b.Mods.IsSubsetOf(_modsHeld)) continue;
                                if (!b.Down) { b.Down = true; ProfileAssignRequested?.Invoke(b.Key); }
                                return (IntPtr)1;
                            }
                        }

                        if (_targetMain != 0 && vk == _targetMain && _targetMods.IsSubsetOf(_modsHeld))
                        {
                            if (_pressStart == 0) // erstes KeyDown (kein Repeat)
                            {
                                _pressStart = Environment.TickCount64;
                                _longFired = false;
                                StartLongPressWatch();
                            }
                            return (IntPtr)1;
                        }
                    }
                    else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                    {
                        _lockKeysDown.Remove(vk);
                        if (_skipFwdMain != 0 && vk == _skipFwdMain && _skipFwdDown) { _skipFwdDown = false; return (IntPtr)1; }
                        if (_skipBackMain != 0 && vk == _skipBackMain && _skipBackDown) { _skipBackDown = false; return (IntPtr)1; }
                        foreach (var b in _profileBindings)
                            if (b.Down && vk == b.Main) { b.Down = false; return (IntPtr)1; }

                        bool blockGestureUp = false;
                        foreach (var g in _gestures)
                        {
                            if (!g.Down || vk != (g.UsePlayPause ? VK_MEDIA_PLAY_PAUSE : g.Main)) continue;
                            g.Down = false;
                            g.HoldTimer?.Stop();
                            if (!g.UsePlayPause) blockGestureUp = true;
                        }
                        if (blockGestureUp) return (IntPtr)1;

                        if (_targetMain != 0 && vk == _targetMain && _pressStart != 0)
                        {
                            _pressStart = 0;
                            _longPressTimer?.Stop();
                            if (!_longFired)
                                ProfileHotkeyPressed?.Invoke(this, EventArgs.Empty);
                            return (IntPtr)1;
                        }
                        _modsHeld.Remove(vkNorm);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"hook callback: {ex.Message}");
            }

            var result = CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            if (lockKeyName != null)
            {
                // Im Hook liefert GetKeyState noch den Zustand VOR dem Umschalten (Windows schaltet erst
                // nach der Hook-Kette um) - der neue Zustand ist also das Gegenteil.
                bool on = (GetKeyState(lockKeyVk) & 1) == 0;
                LockKeyToggled?.Invoke(lockKeyName, on);
            }

            return result;
        }

        /// <summary>
        /// Entspricht _wait_long_press(): Timer statt Python-Thread-Polling-Schleife. Löst nach
        /// snap_hold_ms SnapCycleRequested aus (falls Taste noch gehalten) und danach erneut alle
        /// snap_hold_ms/2 (snap_repeat_fast, mind. 150 ms) bzw. snap_hold_ms, bis die Taste losgelassen wird.
        /// </summary>
        private void StartLongPressWatch()
        {
            _longPressTimer?.Stop();
            _longPressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(50, _holdMs)) };
            _longPressTimer.Tick += FirstLongPressTick;
            _longPressTimer.Start();
        }

        private void FirstLongPressTick(object? sender, EventArgs e)
        {
            _longPressTimer!.Stop();
            if (_pressStart == 0) return; // in der Zwischenzeit losgelassen → Kurzdruck

            _longFired = true;
            SnapCycleRequested?.Invoke(this, EventArgs.Empty);

            int cycleMs = _repeatFast ? Math.Max(150, _holdMs / 2) : Math.Max(50, _holdMs);
            _longPressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(cycleMs) };
            _longPressTimer.Tick += RepeatLongPressTick;
            _longPressTimer.Start();
        }

        private void RepeatLongPressTick(object? sender, EventArgs e)
        {
            if (_pressStart == 0) { _longPressTimer?.Stop(); return; }
            SnapCycleRequested?.Invoke(this, EventArgs.Empty);
        }

        private static void Configure(GestureBinding g, bool enabled, string key, string gesture)
        {
            g.UsePlayPause = string.IsNullOrWhiteSpace(key);
            (g.Mods, g.Main) = g.UsePlayPause ? (new HashSet<int>(), 0) : HotkeyParser.Parse(key);
            g.Enabled = enabled && (g.UsePlayPause || g.Main != 0); // ungültiger Hotkey → aus
            g.Double = gesture == "double";
        }

        /// <summary>
        /// Taste gedrückt: "zweimal schnell" feuert beim zweiten Druck innerhalb von media_double_ms,
        /// "lange halten" einmal nach media_switch_hold_ms, falls die Taste dann noch gehalten wird.
        /// </summary>
        private void GestureDown(GestureBinding g)
        {
            if (g.Down) return; // Tasten-Wiederholung
            g.Down = true;

            if (g.Double)
            {
                long now = Environment.TickCount64;
                if (g.LastPress != 0 && now - g.LastPress <= _mediaDoubleMs)
                {
                    g.LastPress = 0;
                    g.Fire?.Invoke();
                }
                else g.LastPress = now;
                return;
            }

            g.HoldTimer?.Stop();
            g.HoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(50, _mediaHoldMs)) };
            g.HoldTimer.Tick += (_, _) =>
            {
                g.HoldTimer!.Stop();
                if (g.Down) g.Fire?.Invoke(); // in der Zwischenzeit losgelassen → nichts
            };
            g.HoldTimer.Start();
        }

        public void Dispose()
        {
            _watchdog?.Stop();
            _cfgTimer?.Stop();
            _longPressTimer?.Stop();
            foreach (var g in _gestures) g.HoldTimer?.Stop();
            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
        }
    }
}
