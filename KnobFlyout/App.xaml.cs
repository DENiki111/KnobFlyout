using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using KnobFlyout.Audio;
using KnobFlyout.Config;
using KnobFlyout.Input;
using KnobFlyout.Osd;
using KnobFlyout.Profiles;
using KnobFlyout.Tray;

namespace KnobFlyout
{
    /// <summary>
    /// Entspricht dem "__main__"-Block sowie _do_vol/_do_mute/_do_profile_cycle aus knob_service.py.
    /// Läuft ohne Admin-Rechte (app.manifest: asInvoker).
    /// </summary>
    public partial class App : Application
    {
        private static Mutex? _singleInstanceMutex;

        private readonly SessionVolumeController _audio = new();
        private readonly SpotifyController _spotify = new();
        private readonly YouTubeBridge _youtube = new();
        private readonly Media.MediaController _media = new();
        private Osd.MediaOsd? _mediaOsd;
        private readonly AudioWorker _worker = new();
        private readonly object _profileLock = new();
        private LowLevelKeyboardHook? _hook;
        private TrayIconManager? _tray;
        private OsdManager? _osd;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Name bewusst vom alten Projektnamen: so laufen auch ältere Versionen nie gleichzeitig mit dieser.
            _singleInstanceMutex = new Mutex(true, "KnobEarTrumpet_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                Shutdown();
                return;
            }

            var startCfg = ConfigManager.Load();
            Loc.Apply(startCfg);
            _osdFollowsMedia = startCfg.OsdFollowsMedia();
            _youtube.Apply(startCfg);

            _osd = new OsdManager();
            _osd.StackChanged += () =>
            {
                // Nur relevant, wenn das Media-Flyout am Lautstärke-Stapel hängt. Sonst hat ein
                // Stapel-Update nichts mit der eigenen Media-Position zu tun - ein laufendes Ziehen
                // dort würde sonst mitten im Drag auf die gespeicherte Position zurückgesetzt.
                if (_mediaOsd == null || !_mediaOsd.IsVisible) return; // Config nur lesen, wenn nötig
                var cfg = ConfigManager.Load();
                if (cfg.MediaFollowsOsd())
                    _mediaOsd.ApplyPosition(cfg, _osd);
                else
                    ResolveOverlap(); // unabhängige Positionen: Flyout darf den Stapel nicht überdecken
            };
            // Für "Stapel hängt am Media-Flyout": aktuelle Lage des Flyouts und Verschieben per Stapel-Drag.
            _osd.MediaBounds = () => _mediaOsd != null && _mediaOsd.IsVisible
                ? new Rect(_mediaOsd.Left, _mediaOsd.Top, _mediaOsd.ActualWidth, _mediaOsd.ActualHeight)
                : null;
            // "Klick blendet aus": bei "Zusammen" verschwinden OSD und Media-Flyout gemeinsam.
            _osd.Dismissed += () =>
            {
                if (_mediaOsd != null && _mediaOsd.IsVisible && IsCombinedLayout()) _mediaOsd.HideNow();
            };
            _osd.MediaAnchorChanged += () =>
            {
                if (_mediaOsd != null && _mediaOsd.IsVisible) _mediaOsd.ApplyPosition(ConfigManager.Load(), _osd);
            };

            _tray = new TrayIconManager();
            _tray.SettingsRequested += OpenSettings;
            _tray.ExitRequested += () => Shutdown();

            // Hook-Handler reihen nur ein (Hook-Callback muss sofort zurückkehren).
            _hook = new LowLevelKeyboardHook();
            _hook.VolumeUp += (_, _) => _worker.EnqueueVolume(+1, ApplyVolume);
            _hook.VolumeDown += (_, _) => _worker.EnqueueVolume(-1, ApplyVolume);
            _hook.MutePressed += (_, _) => _worker.Enqueue(ApplyMute);
            _hook.ProfileHotkeyPressed += (_, _) => Task.Run(CycleProfile);
            // Zuweisen-Taste im offenen Neue-App-Popup: angezeigte App diesem Profil zuweisen.
            _hook.ProfileAssignRequested += key => Dispatcher.BeginInvoke(new Action(() => _newAppOsd?.AssignTo(key)));
            _hook.LockKeyToggled += (name, on) => Dispatcher.BeginInvoke(new Action(() => _osd?.ShowLockKey(name, on)));
            _hook.SnapCycleRequested += (_, _) => Dispatcher.BeginInvoke(new Action(CycleSnapPosition));
            _hook.MediaSourceSwitchRequested += (_, _) => Dispatcher.BeginInvoke(new Action(() => _media.CycleSource()));
            _hook.MediaNextRequested += (_, _) => Dispatcher.BeginInvoke(new Action(() => _media.Next()));
            _hook.MediaSkipRequested += dir => Dispatcher.BeginInvoke(new Action(() =>
                _media.SkipBy(TimeSpan.FromSeconds(Media.MediaController.SkipSeconds * dir))));
            _hook.Install();

            StartNewAppDetection();
            _ = InitializeMediaAsync();

            // Auflösung/Monitore: beim Start mit dem gespeicherten Aufbau vergleichen, danach live verfolgen.
            OnDisplayChanged();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
            Dispatcher.BeginInvoke(new Action(OnDisplayChanged));

        /// <summary>
        /// Bildschirmaufbau hat sich geändert (oder erster Start): eigene Positionen (OSD, Media-Flyout,
        /// Snap-Punkte) vom alten auf den neuen Aufbau umrechnen, sodass sie wieder an derselben Stelle
        /// sitzen; Ecken-Voreinstellungen passen sich ohnehin selbst an. Danach Sichtbares neu ausrichten.
        /// </summary>
        private void OnDisplayChanged()
        {
            try
            {
                var cfg = ConfigManager.Load();
                var current = Placement.Monitors();
                var currentRefs = Placement.ToRefs(current);
                if (cfg.ScreenLayout.Count > 0 && Placement.SameLayout(cfg.ScreenLayout, currentRefs)) return;

                if (cfg.ScreenLayout.Count > 0)
                {
                    var old = Placement.FromRefs(cfg.ScreenLayout);
                    (int?, int?) Map(int? x, int? y)
                    {
                        if (x == null || y == null) return (x, y);
                        var p = Placement.Remap(new Point(x.Value, y.Value), old, current);
                        return ((int)Math.Round(p.X), (int)Math.Round(p.Y));
                    }
                    (cfg.OsdX, cfg.OsdY) = Map(cfg.OsdX, cfg.OsdY);
                    (cfg.MediaOsdX, cfg.MediaOsdY) = Map(cfg.MediaOsdX, cfg.MediaOsdY);
                    foreach (var sp in cfg.SnapPositions)
                    {
                        var (x, y) = Map(sp.X, sp.Y);
                        sp.X = x ?? sp.X;
                        sp.Y = y ?? sp.Y;
                        (sp.MediaX, sp.MediaY) = Map(sp.MediaX, sp.MediaY);
                    }
                    Logger.Info("Bildschirmaufbau geändert – Positionen umgerechnet");
                }

                cfg.ScreenLayout = currentRefs;
                ConfigManager.Save(cfg);

                _osd?.Reposition();
                if (_mediaOsd != null && _mediaOsd.IsVisible) _mediaOsd.ApplyPosition(cfg, _osd!);
                ResolveOverlap();
            }
            catch (Exception ex)
            {
                Logger.Error($"Bildschirmwechsel: {ex.Message}");
            }
        }

        /// <summary>
        /// Bei unabhängigen Positionen (Getrennt bzw. "Zusammen" ohne modernes Design) darf das Media-
        /// Flyout den sichtbaren Lautstärke-Stapel nicht überdecken: es wird direkt daneben geschoben
        /// (nur angezeigt, die gespeicherte Position bleibt).
        /// </summary>
        private void ResolveOverlap()
        {
            if (_osd == null || _mediaOsd == null || !_mediaOsd.IsVisible || _mediaOsd.IsDragging) return;
            var stack = _osd.StackBounds;
            if (stack == null) return;
            var cfg = ConfigManager.Load();
            if (cfg.MediaFollowsOsd() || cfg.OsdFollowsMedia()) return; // hängen ohnehin aneinander
            var media = new Rect(_mediaOsd.Left, _mediaOsd.Top, _mediaOsd.ActualWidth, _mediaOsd.ActualHeight);
            if (!Placement.Overlaps(media, stack.Value)) return;
            var p = Placement.AvoidOverlap(media, stack.Value);
            _mediaOsd.Left = p.X;
            _mediaOsd.Top = p.Y;
        }

        /// <summary>Entspricht dem ModernFlyouts-Media-Flyout (GSMTC): zeigt sich bei Titel-/Wiedergabewechsel.</summary>
        private async Task InitializeMediaAsync()
        {
            try
            {
                await _media.InitializeAsync();
                _media.Changed += () => Dispatcher.BeginInvoke(new Action(async () =>
                {
                    if (!ConfigManager.Load().MediaEnabled) { _mediaOsd?.Hide(); return; }
                    if (!_media.HasSession) { _mediaOsd?.Hide(); return; }
                    var info = await _media.GetInfoAsync();
                    if (info == null) return;

                    // Position NUR beim allerersten Erscheinen setzen - nicht bei jeder
                    // Metadaten-Aktualisierung (Titelwechsel, Play/Pause, ...), sonst würde ein
                    // laufendes Ziehen des Fensters durch diese Zeile wieder zurückgerissen.
                    if (_mediaOsd != null && _mediaOsd.StyleChanged)
                    {
                        // Farbschema oder Media-Design wurde geändert: Fenster neu aufbauen.
                        _mediaOsd.Close();
                        _mediaOsd = null;
                    }

                    // Beim ersten UND bei jedem erneuten Erscheinen (war ausgeblendet) an die aktuelle
                    // Position setzen - sonst käme es nach einem Snap-Punkt-Wechsel an der alten Stelle
                    // zurück. Solange es sichtbar ist, nicht (laufendes Ziehen bleibt unberührt).
                    bool appearing = _mediaOsd == null || !_mediaOsd.IsVisible;
                    _mediaOsd ??= CreateMediaOsd();
                    if (appearing) _mediaOsd.ApplyPosition(ConfigManager.Load(), _osd!);
                    _mediaOsd.UpdateFrom(info);
                }));
            }
            catch (Exception ex)
            {
                Logger.Error($"Media init: {ex.Message}");
            }
        }

        private bool _osdFollowsMedia;

        /// <summary>
        /// Neues Media-Flyout. Hängt der Lautstärke-Stapel daran (cfg.OsdFollowsMedia), zieht jede
        /// Bewegung/Größenänderung/jedes Erscheinen des Flyouts den sichtbaren Stapel mit.
        /// </summary>
        private Osd.MediaOsd CreateMediaOsd()
        {
            var m = new Osd.MediaOsd(_media);
            void Follow() { if (_osdFollowsMedia) _osd?.Reposition(); }
            // Hängt das Flyout am Stapel, nach jeder Größenänderung neu anhängen (Seite neu prüfen): sonst
            // schiebt KeepOnScreen ein gewachsenes Flyout über den Stapel. Dort ist es nicht ziehbar, also gefahrlos.
            void Reattach()
            {
                if (!m.IsVisible || _osd == null) return;
                var cfg = ConfigManager.Load();
                if (cfg.MediaFollowsOsd()) m.ApplyPosition(cfg, _osd);
            }
            m.LocationChanged += (_, _) => Follow();
            m.SizeChanged += (_, _) => { Follow(); Reattach(); ResolveOverlap(); };
            m.IsVisibleChanged += (_, e) => { if (e.NewValue is true) { Follow(); ResolveOverlap(); } };
            m.DragFinished += ResolveOverlap;
            m.SettingsRequested += OpenSettings;
            m.ConfigChanged += () => _hook?.ReloadConfig();
            m.MenuActionRequested += OnMediaMenuAction;
            m.DismissRequested += () => { if (IsCombinedLayout()) _osd?.HideNow(); };
            return m;
        }

        private static bool IsCombinedLayout()
        {
            var cfg = ConfigManager.Load();
            return cfg.MediaFollowsOsd() || cfg.OsdFollowsMedia();
        }

        /// <summary>Positions-Aktionen aus dem "…"-Menü des Media-Flyouts.</summary>
        private void OnMediaMenuAction(string action)
        {
            try
            {
                switch (action)
                {
                    case "save_position":
                    {
                        var cfg = ConfigManager.Load();
                        cfg.SnapPositions.Add(OsdManager.CurrentSnapPoint(cfg));
                        ConfigManager.Save(cfg);
                        _snapIdx = cfg.SnapPositions.Count - 1;
                        _osd?.ShowSnapMove(_snapIdx, cfg.SnapPositions.Count); // Rückmeldung: "Position n/n"
                        break;
                    }
                    case "next_position":
                        CycleSnapPosition();
                        break;
                    case "reset_position":
                    {
                        var cfg = ConfigManager.Load();
                        cfg.OsdX = cfg.OsdY = null;
                        cfg.MediaOsdX = cfg.MediaOsdY = null;
                        cfg.OsdCornerPreset = cfg.MediaCornerPreset = "custom";
                        cfg.SnapIndependent = false;
                        ConfigManager.Save(cfg);
                        OnSettingsConfigApplied();
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Media-Menü {action}: {ex.Message}");
            }
        }

        /// <summary>Entspricht _exec() in _do_vol().</summary>
        private void ApplyVolume(int steps)
        {
            var cfg = ConfigManager.Load();
            var results = _audio.ChangeVolume(cfg, steps, _spotify, _youtube);
            if (results.Count > 0) _osd?.Show(results, false);
        }

        /// <summary>Entspricht _exec() in _do_mute().</summary>
        private void ApplyMute()
        {
            var cfg = ConfigManager.Load();
            var r = _audio.ToggleMute(cfg, _spotify, _youtube);
            if (r.Count == 0) return;
            _osd?.Show(r.Select(x => (x.Name, 0)).ToList(), r[0].Muted);
        }

        /// <summary>Entspricht _do_profile_cycle()/_do_profile_cycle_impl() inkl. Skip-Modus.</summary>
        private void CycleProfile()
        {
            if (!Monitor.TryEnter(_profileLock)) return; // läuft schon ein Wechsel → ignorieren
            try
            {
                var cfg = ConfigManager.Load();
                var keys = ProfileDefinitions.Keys(cfg);
                string current = keys.Contains(cfg.ActiveProfile) ? cfg.ActiveProfile : keys[0];

                if (!cfg.SkipEmptyProfiles)
                {
                    int i = Array.IndexOf(keys, current);
                    cfg.ActiveProfile = keys[(i + 1) % keys.Length];
                    ConfigManager.Save(cfg);
                    _audio.InvalidateCache();
                    _osd?.ShowProfileSwitch();
                    return;
                }

                _audio.InvalidateCache();
                var sess = _audio.GetSessions();
                var active = keys.Where(k => _audio.ProfileHasActive(k, cfg, sess)).ToList();

                if (active.Count == 0)
                {
                    _osd?.ShowProfileSwitch();
                    return;
                }

                if (active.Count == 1)
                {
                    cfg.ActiveProfile = active[0];
                    ConfigManager.Save(cfg);
                    _audio.InvalidateCache();
                    _osd?.ShowProfileSwitch();
                    return;
                }

                int idx = Array.IndexOf(keys, current);
                for (int step = 1; step <= keys.Length; step++)
                {
                    string next = keys[(idx + step) % keys.Length];
                    if (!active.Contains(next)) continue;
                    cfg.ActiveProfile = next;
                    ConfigManager.Save(cfg);
                    _audio.InvalidateCache();
                    _osd?.ShowProfileSwitch();
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"CycleProfile: {ex.Message}");
            }
            finally
            {
                Monitor.Exit(_profileLock);
            }
        }

        private Settings.SettingsWindow? _settingsWindow;

        private System.Windows.Threading.DispatcherTimer? _newAppTimer;
        private Osd.NewAppOsd? _newAppOsd;
        private string? _newAppShown; // App des gerade sichtbaren Popups (steht nicht mehr in der Warteschlange)
        private readonly System.Collections.Generic.Queue<string> _newAppQueue = new();

        /// <summary>
        /// Entspricht der "OSD-Popup"-Zuweisung aus dem Python-Original: prüft alle 3s, ob eine
        /// Session mit aktivem Sound auftaucht, die noch keinem Profil zugewiesen, nicht
        /// ausgeschlossen und noch nicht "gesehen" wurde, und zeigt dafür ein klickbares
        /// Zuweisungs-OSD (siehe Osd/NewAppOsd.cs). Läuft nur ein Popup gleichzeitig; weitere
        /// neue Apps werden angehängt und danach nacheinander gezeigt.
        /// </summary>
        private void StartNewAppDetection()
        {
            _newAppTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _newAppTimer.Tick += (_, _) => CheckForNewApps();
            _newAppTimer.Start();
        }

        private void CheckForNewApps()
        {
            try
            {
                var cfg = ConfigManager.Load();
                var seen = cfg.SeenApps.Select(a => a.ToLowerInvariant()).ToHashSet();
                var excluded = cfg.Exclude.Select(a => a.ToLowerInvariant()).ToHashSet();
                var assigned = cfg.Profiles.Values.SelectMany(v => v).Select(a => a.ToLowerInvariant()).ToHashSet();

                foreach (var s in _audio.GetSessions())
                {
                    if (s.State != NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive) continue;
                    var key = s.ProcessName.ToLowerInvariant();
                    if (seen.Contains(key) || excluded.Contains(key) || assigned.Contains(key)) continue;
                    // Gerade angezeigt oder schon eingereiht: nicht erneut einreihen (sonst kommt das
                    // Popup derselben App nach dem Zuweisen gleich noch einmal).
                    if (string.Equals(_newAppShown, s.ProcessName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (_newAppQueue.Any(q => string.Equals(q, s.ProcessName, StringComparison.OrdinalIgnoreCase))) continue;

                    _newAppQueue.Enqueue(s.ProcessName);
                    seen.Add(key); // im selben Durchlauf nicht zweimal einreihen
                }

                ShowNextNewAppPopup();
            }
            catch (Exception ex)
            {
                Logger.Error($"CheckForNewApps: {ex.Message}");
            }
        }

        private void ShowNextNewAppPopup()
        {
            if (_newAppOsd != null) return;
            while (_newAppQueue.Count > 0)
            {
                var proc = _newAppQueue.Dequeue();
                if (IsKnownApp(ConfigManager.Load(), proc)) continue; // inzwischen zugewiesen/ausgeschlossen (z. B. in den Einstellungen)

                _newAppShown = proc;
                _newAppOsd = new Osd.NewAppOsd(proc);
                _newAppOsd.Closed += (_, _) =>
                {
                    _newAppOsd = null;
                    _newAppShown = null;
                    if (_hook != null) _hook.AssignPopupOpen = false;
                    ShowNextNewAppPopup();
                };
                _newAppOsd.Show();
                if (_hook != null) _hook.AssignPopupOpen = true; // Zuweisen-Tasten wirken nur jetzt
                return;
            }
        }

        private static bool IsKnownApp(AppConfig cfg, string proc)
        {
            var key = proc.ToLowerInvariant();
            return cfg.SeenApps.Any(a => a.ToLowerInvariant() == key)
                || cfg.Exclude.Any(a => a.ToLowerInvariant() == key)
                || cfg.Profiles.Values.Any(v => v.Any(a => a.ToLowerInvariant() == key));
        }

        private int _snapIdx = -1;

        /// <summary>Entspricht show_snap_move(): zyklt bei gehaltenem Profil-Hotkey durch cfg.snap_positions.</summary>
        private void CycleSnapPosition()
        {
            try
            {
                var cfg = ConfigManager.Load();
                if (!cfg.SnapEnabled || cfg.SnapPositions.Count == 0) return; // Snap aus → Hotkey macht in diesem Fall nichts (kein Profilwechsel, kein Snap)

                _snapIdx = (_snapIdx + 1) % cfg.SnapPositions.Count;
                var pos = cfg.SnapPositions[_snapIdx];
                cfg.OsdX = pos.X;
                cfg.OsdY = pos.Y;
                cfg.OsdCornerPreset = "custom"; // Snap-Punkt = eigene Position

                // Hat der Punkt eine eigene Flyout-Position, stehen OSD und Flyout genau dort - auch bei
                // "Zusammen": das Anhängen wird dafür ausgesetzt (snap_independent), sonst würde eins
                // der beiden die gespeicherte Stelle ignorieren und am anderen kleben.
                cfg.SnapIndependent = false;
                bool hasMedia = pos.MediaX.HasValue && pos.MediaY.HasValue;
                if (hasMedia)
                {
                    cfg.MediaOsdX = pos.MediaX;
                    cfg.MediaOsdY = pos.MediaY;
                    cfg.MediaCornerPreset = "custom";
                    cfg.SnapIndependent = cfg.OsdLayout == "combined";
                }
                else if (cfg.OsdFollowsMedia())
                {
                    // Nur eine Position im Punkt: sie gilt für den Anker (hier das Flyout).
                    cfg.MediaOsdX = pos.X;
                    cfg.MediaOsdY = pos.Y;
                    cfg.MediaCornerPreset = "custom";
                }

                ConfigManager.Save(cfg);
                _osdFollowsMedia = cfg.OsdFollowsMedia();

                _osd?.ShowSnapMove(_snapIdx, cfg.SnapPositions.Count);
                // ApplyPosition liest cfg.MediaOsdX/Y bzw. folgt dem Lautstärke-Anker - deckt alle Fälle ab.
                // Ist das Flyout gerade unsichtbar, setzt es sich beim nächsten Erscheinen selbst richtig.
                if (_mediaOsd != null && _mediaOsd.IsVisible)
                    _mediaOsd.ApplyPosition(cfg, _osd!);
            }
            catch (Exception ex)
            {
                Logger.Error($"CycleSnapPosition: {ex.Message}");
            }
        }

        private void OpenSettings()
        {
            if (_settingsWindow != null)
            {
                _settingsWindow.Activate();
                return;
            }

            var window = new Settings.SettingsWindow(_spotify, _youtube);
            AttachSettings(window);
            window.Show();
            window.Activate();
        }

        /// <summary>
        /// Merkt sich das aktuelle Settings-Fenster. Bei einem Farbschema-Wechsel ersetzt sich das
        /// Fenster durch ein neues (Replaced) - dann zeigt _settingsWindow auf das neue, und das
        /// Schließen des alten setzt den Verweis nicht fälschlich auf null.
        /// </summary>
        private void AttachSettings(Settings.SettingsWindow window)
        {
            _settingsWindow = window;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_settingsWindow, window)) _settingsWindow = null;
            };
            window.Replaced += AttachSettings;
            window.ConfigApplied += OnSettingsConfigApplied;
        }

        /// <summary>Settings hat gespeichert, Positionen zurückgesetzt oder die Config neu geladen: Sprache übernehmen, Flyouts neu ausrichten.</summary>
        private void OnSettingsConfigApplied()
        {
            var cfg = ConfigManager.Load();
            Loc.Apply(cfg);
            _tray?.UpdateTexts();
            _hook?.ReloadConfig();
            _osdFollowsMedia = cfg.OsdFollowsMedia();
            _audio.InvalidateCache();
            _youtube.Apply(cfg);
            if (_mediaOsd != null && _mediaOsd.IsVisible)
                _mediaOsd.ApplyPosition(cfg, _osd!);
            _osd?.Reposition();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _newAppTimer?.Stop();
            _newAppOsd?.Close();
            _settingsWindow?.Close();
            _hook?.Dispose();
            _tray?.Dispose();
            _worker.Dispose();
            _audio.Dispose();
            _spotify.Dispose();
            _youtube.Dispose();
            _media.Dispose();
            _mediaOsd?.Close();
            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
