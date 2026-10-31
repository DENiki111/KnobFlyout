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
            bool autostart = e.Args.Any(a => string.Equals(a, Autostart.Argument, StringComparison.OrdinalIgnoreCase));

            // Name bewusst vom alten Projektnamen: so laufen auch ältere Versionen nie gleichzeitig mit dieser.
            _singleInstanceMutex = new Mutex(true, "KnobEarTrumpet_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                if (!autostart) SignalRunningInstance();
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
            // (Eine Daueranzeige an eigener Stelle zählt nicht: das OSD bleibt dann, wo es ohne sie stünde.)
            _osd.MediaBounds = () => _mediaOsd != null && _mediaOsd.IsVisible && !_mediaOsd.KeepOwnPosition
                ? new Rect(_mediaOsd.Left, _mediaOsd.Top, _mediaOsd.ActualWidth, _mediaOsd.ActualHeight)
                : null;
            _osd.FixedMediaBounds = () => _mediaOsd != null && _mediaOsd.IsVisible && _mediaOsd.KeepVisible && (_mediaOsd.KeepOwnPosition || !IsCombinedLayout())
                ? new Rect(_mediaOsd.Left, _mediaOsd.Top, _mediaOsd.ActualWidth, _mediaOsd.ActualHeight)
                : null;
            // "Klick blendet aus": bei "Zusammen" verschwinden OSD und Media-Flyout gemeinsam.
            _osd.Dismissed += () =>
            {
                if (_mediaOsd != null && _mediaOsd.IsVisible && !_mediaOsd.KeepVisible && IsCombinedLayout()) _mediaOsd.HideNow();
            };
            _osd.MediaAnchorChanged += () =>
            {
                if (_mediaOsd != null && _mediaOsd.IsVisible) _mediaOsd.ApplyPosition(ConfigManager.Load(), _osd);
            };

            _tray = new TrayIconManager();
            _tray.SettingsRequested += OpenSettings;
            _tray.ExitRequested += () =>
            {
                IsExiting = true;
                Shutdown();
            };

            // Hook-Handler reihen nur ein (Hook-Callback muss sofort zurückkehren).
            _hook = new LowLevelKeyboardHook();
            _hook.VolumeUp += (_, _) => { ToTutorial(t => t.OnKnobKey()); _worker.EnqueueVolume(+1, ApplyVolume); };
            _hook.VolumeDown += (_, _) => { ToTutorial(t => t.OnKnobKey()); _worker.EnqueueVolume(-1, ApplyVolume); };
            _hook.MutePressed += (_, _) => { ToTutorial(t => t.OnKnobKey()); _worker.Enqueue(ApplyMute); };
            _hook.ProfileHotkeyPressed += (_, _) => Task.Run(() => { CycleProfile(); ToTutorial(t => t.OnProfileHotkey()); });
            // Zuweisen-Taste im offenen Neue-App-Popup: angezeigte App diesem Profil zuweisen.
            _hook.ProfileAssignRequested += key => Dispatcher.BeginInvoke(new Action(() => _newAppOsd?.AssignTo(key)));
            _hook.LockKeyToggled += (name, on) => Dispatcher.BeginInvoke(new Action(() => { _osd?.ShowLockKey(name, on); _tutorial?.OnLockKey(name, on); }));
            _hook.SnapCycleRequested += (_, _) => Dispatcher.BeginInvoke(new Action(() => { CycleSnapPosition(); _tutorial?.OnSnapCycle(); }));
            _hook.MediaSourceSwitchRequested += (_, _) => Dispatcher.BeginInvoke(new Action(() => { _media.CycleSource(); _tutorial?.OnMediaGesture("source"); }));
            _hook.MediaNextRequested += (_, _) => Dispatcher.BeginInvoke(new Action(() => { _media.Next(); _tutorial?.OnMediaGesture("next"); }));
            _hook.MediaSkipRequested += dir => Dispatcher.BeginInvoke(new Action(() =>
            {
                _media.SkipBy(TimeSpan.FromSeconds(Media.MediaController.SkipSeconds * dir));
                _tutorial?.OnSkipKey(dir);
            }));
            _hook.Install();

            StartNewAppDetection();
            _ = InitializeMediaAsync();

            // Auflösung/Monitore: beim Start mit dem gespeicherten Aufbau vergleichen, danach live verfolgen.
            OnDisplayChanged();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            Autostart.EnsureArgument();
            ListenForOpenSettings();

            // Von Hand gestartet: gleich in die Einstellungen (davor die Einführung, solange sie nicht abgestellt ist). Mit Windows: nur im Hintergrund.
            if (!autostart) OpenSettings();
            else if (Settings.TutorialWindow.ShowOnStart(ConfigManager.Load())) ShowTutorial(false);
        }

        private const string OpenSettingsSignalName = "KnobFlyout_OpenSettings";
        private EventWaitHandle? _openSettingsSignal;
        private RegisteredWaitHandle? _openSettingsWait;

        /// <summary>Wird die exe noch einmal gestartet, meldet sie sich hier und beendet sich; diese Instanz öffnet dann die Einstellungen.</summary>
        private void ListenForOpenSettings()
        {
            _openSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, OpenSettingsSignalName);
            _openSettingsWait = ThreadPool.RegisterWaitForSingleObject(_openSettingsSignal,
                (_, _) => Dispatcher.BeginInvoke(new Action(OpenSettings)), null, Timeout.Infinite, false);
        }

        private static void SignalRunningInstance()
        {
            try
            {
                AllowSetForegroundWindow(-1); // die laufende Instanz darf ihr Fenster nach vorn holen
                using var signal = EventWaitHandle.OpenExisting(OpenSettingsSignalName);
                signal.Set();
            }
            catch
            {
                // Läuft eine ältere Version ohne dieses Signal: nur beenden wie bisher.
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);

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
                    (cfg.MediaKeepX, cfg.MediaKeepY) = Map(cfg.MediaKeepX, cfg.MediaKeepY);
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
            if (_mediaOsd.KeepVisible) return; // Daueranzeige bleibt stehen, der Stapel weicht aus (OsdManager.FixedMediaBounds)
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
                    try { await ShowMediaAsync(); }
                    catch (Exception ex) { Logger.Error($"Media: {ex.Message}"); }
                }));
                _media.SourcesChanged += () => Dispatcher.BeginInvoke(new Action(RefreshKeep));
                _keepRecheck.Tick += (_, _) =>
                {
                    _keepRecheck.Stop();
                    RefreshKeep();
                };
                _ = Dispatcher.BeginInvoke(new Action(RefreshKeep)); // Programm läuft vielleicht schon beim Start
            }
            catch (Exception ex)
            {
                Logger.Error($"Media init: {ex.Message}");
            }
        }

        private int _mediaRequest;

        // Daueranzeige (media_keep_*)
        private bool _keepActive;     // Flyout bleibt gerade stehen
        private bool _keepSuspended;  // ein anderes Programm spielt: Daueranzeige ruht, bis es fertig ist
        private bool _keepSeen;       // Startzustand schon erfasst
        private HashSet<string> _playingApps = new();
        private readonly Dictionary<string, DateTime> _stoppedAt = new();
        private readonly System.Windows.Threading.DispatcherTimer _keepRecheck = new() { Interval = TimeSpan.FromSeconds(2.1) };
        private const double KeepFlipSeconds = 2; // kürzere Pausen (nächster Titel, Werbung) zählen nicht als Stopp

        /// <summary>Programm aus media_keep_apps spielt, pausiert, startet oder endet bzw. Liste geändert: Flyout anzeigen oder normal auslaufen lassen.</summary>
        private async void RefreshKeep()
        {
            try
            {
                var cfg = ConfigManager.Load();
                if (cfg.MediaKeepApps.Count == 0 && !_keepActive) return;
                bool wasActive = _keepActive;
                bool otherStarted = EvaluateKeep(cfg);
                if (_keepActive || wasActive || otherStarted) await ShowMediaAsync();
            }
            catch (Exception ex)
            {
                Logger.Error($"Daueranzeige: {ex.Message}");
            }
        }

        /// <summary>
        /// Neu bewerten, ob das Flyout stehen bleibt. Spielt ein Programm außerhalb der Liste los, zeigt das Flyout einmal
        /// dieses und läuft dann normal aus; die Daueranzeige ruht, bis es nicht mehr spielt oder ein Programm der Liste
        /// neu losspielt. Liefert true, wenn gerade so ein anderes Programm losgespielt hat.
        /// </summary>
        private bool EvaluateKeep(AppConfig cfg)
        {
            var names = cfg.MediaKeepApps;
            if (!cfg.MediaEnabled || names.Count == 0)
            {
                _keepActive = _keepSuspended = _keepSeen = false;
                _playingApps.Clear();
                return false;
            }

            bool Listed(Media.MediaSource s) => Media.MediaController.IsListed(s, names);
            var now = DateTime.UtcNow;
            var sources = _media.GetSources();
            var playing = sources.Where(s => s.IsPlaying).ToList();
            var started = playing.Where(s => !_playingApps.Contains(s.AppId)
                && (!_stoppedAt.TryGetValue(s.AppId, out var at) || (now - at).TotalSeconds > KeepFlipSeconds)).ToList();
            foreach (var id in _playingApps)
                if (!playing.Any(s => s.AppId == id)) _stoppedAt[id] = now;
            _playingApps = playing.Select(s => s.AppId).ToHashSet();

            var otherStarted = _keepSeen ? started.FirstOrDefault(s => !Listed(s)) : null;
            bool otherPlaying = playing.Any(s => !Listed(s));
            if (!_keepSeen) _keepSuspended = otherPlaying && !playing.Any(Listed);
            else if (otherStarted != null) _keepSuspended = true;
            else if (started.Any(Listed)) _keepSuspended = false;
            else if (_keepSuspended && !otherPlaying)
            {
                bool justStopped = sources.Any(s => !Listed(s) && _stoppedAt.TryGetValue(s.AppId, out var at) && (now - at).TotalSeconds <= KeepFlipSeconds);
                if (justStopped) _keepRecheck.Start();
                else _keepSuspended = false;
            }
            _keepSeen = true;

            var listed = sources.Where(s => Listed(s) && (cfg.MediaKeepMode == "open" || s.IsPlaying)).ToList();
            var keep = _keepSuspended ? null
                : listed.FirstOrDefault(s => s.AppId == _media.CurrentAppId) ?? listed.FirstOrDefault(s => s.IsPlaying) ?? listed.FirstOrDefault();
            bool wasActive = _keepActive;
            _keepActive = keep != null;
            if (otherStarted != null) _media.ShowSource(otherStarted.Session);
            else if (keep != null && !wasActive && keep.AppId != _media.CurrentAppId) _media.ShowSource(keep.Session);
            return otherStarted != null;
        }

        /// <summary>Flyout mit der aktuellen Quelle anzeigen bzw. aktualisieren.</summary>
        private async Task ShowMediaAsync()
        {
            var cfg = ConfigManager.Load();
            EvaluateKeep(cfg);

            if (!cfg.MediaEnabled) { _mediaOsd?.Hide(); return; }
            if (!_media.HasSession) { _mediaOsd?.Hide(); return; }
            // Abfragen überlappen sich (das Cover zu laden dauert): Eine ältere darf die neuere nicht überschreiben,
            // sonst steht nach einem Quellenwechsel noch der Titel der vorigen Quelle da.
            int request = ++_mediaRequest;
            var info = await _media.GetInfoAsync();
            if (info == null || request != _mediaRequest) return;

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
            // Endet die Daueranzeige (Pause, anderes Programm), läuft das sichtbare Flyout an seiner Stelle aus.
            bool ownPlace = _keepActive ? cfg.MediaKeepOwnPosition : !appearing && _mediaOsd.KeepOwnPosition;
            bool moved = _mediaOsd.SetKeep(_keepActive, ownPlace);
            if (appearing || moved) _mediaOsd.ApplyPosition(ConfigManager.Load(), _osd!);
            _mediaOsd.UpdateFrom(info);
            if (moved) _osd?.Reposition();
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
            m.ConfigChanged += () =>
            {
                _hook?.ReloadConfig();
                RefreshKeep(); // „…“-Menü: Daueranzeige an oder aus
            };
            m.MenuActionRequested += OnMediaMenuAction;
            m.DismissRequested += () => { if (!m.KeepOwnPosition && IsCombinedLayout()) _osd?.HideNow(); };
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
            ToTutorial(t => t.OnVolumeResult(results, steps));
        }

        /// <summary>Entspricht _exec() in _do_mute().</summary>
        private void ApplyMute()
        {
            var cfg = ConfigManager.Load();
            var r = _audio.ToggleMute(cfg, _spotify, _youtube);
            ToTutorial(t => t.OnMuteResult(r));
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
                var autoAssigned = new System.Collections.Generic.List<(string Process, string Profile)>();
                if (!cfg.NewAppPopup) _newAppOsd?.Close(); // gerade abgeschaltet

                foreach (var s in _audio.GetSessions())
                {
                    if (s.State != NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive) continue;
                    var key = s.ProcessName.ToLowerInvariant();
                    if (seen.Contains(key) || excluded.Contains(key) || assigned.Contains(key)) continue;
                    // Gerade angezeigt oder schon eingereiht: nicht erneut einreihen (sonst kommt das
                    // Popup derselben App nach dem Zuweisen gleich noch einmal).
                    if (string.Equals(_newAppShown, s.ProcessName, StringComparison.OrdinalIgnoreCase)) continue;

                    // Erkannte Art von App (Spiel, Browser, Musik, Kommunikation): ohne Popup ins passende Profil
                    var target = cfg.AutoAssign ? AppClassifier.TargetProfile(cfg, s.ProcessName, AppClassifier.ExePath(s.ProcessId)) : null;
                    if (target != null)
                    {
                        Settings.AppRow.Assign(cfg, s.ProcessName, target);
                        autoAssigned.Add((s.ProcessName, target));
                        assigned.Add(key);
                        seen.Add(key);
                        continue;
                    }

                    if (!cfg.NewAppPopup) continue; // nicht als gesehen gemerkt: Eingeschaltet fragt das Popup wieder
                    if (_newAppQueue.Any(q => string.Equals(q, s.ProcessName, StringComparison.OrdinalIgnoreCase))) continue;

                    _newAppQueue.Enqueue(s.ProcessName);
                    seen.Add(key); // im selben Durchlauf nicht zweimal einreihen
                }

                if (autoAssigned.Count > 0)
                {
                    ConfigManager.Save(cfg);
                    _audio.InvalidateCache();
                    foreach (var (process, profile) in autoAssigned) Logger.Info($"Automatisch einsortiert: {process} → {profile}");
                    ToTutorial(t => t.OnAppsAutoAssigned(autoAssigned));
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
            // Während der Einführung kein Popup: Die App bleibt eingereiht und kommt beim nächsten Durchlauf danach.
            if (_newAppOsd != null || _tutorial != null) return;
            if (_newAppQueue.Count > 0 && !ConfigManager.Load().NewAppPopup) _newAppQueue.Clear();
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
            if (_tutorial != null)
            {
                if (_settingsWindow == null) _settingsAfterTutorial = true;
                BringToFront(_tutorial);
                return;
            }
            if (_settingsWindow != null)
            {
                BringToFront(_settingsWindow);
                return;
            }

            // Einführung kommt beim Öffnen der Einstellungen, bis sie per Häkchen abgestellt ist.
            if (Settings.TutorialWindow.ShowWithSettings(ConfigManager.Load())) ShowTutorial(true);
            else OpenSettingsWindow("general");
        }

        private static void BringToFront(Window window)
        {
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }

        private Settings.TutorialWindow? _tutorial;
        private bool _settingsAfterTutorial; // Einführung schließen → Einstellungen öffnen

        /// <summary>Einführung ohne Besitzer (Programmstart oder vor den Einstellungen).</summary>
        private void ShowTutorial(bool thenSettings)
        {
            _settingsAfterTutorial = thenSettings;
            var t = NewTutorial();
            t.Closed += (_, _) =>
            {
                _tutorial = null;
                bool open = _settingsAfterTutorial || t.RequestedSettingsPage != null;
                _settingsAfterTutorial = false;
                if (open) OpenSettingsWindow(t.RequestedSettingsPage ?? "general");
            };
            _tutorial = t;
            t.Show();
            t.Activate();
        }

        /// <summary>Einführung aus den Einstellungen heraus (Allgemein → Einführung): Dialog über dem Fenster.</summary>
        private string? ShowTutorialDialog(Window owner)
        {
            var t = NewTutorial();
            t.Owner = owner;
            _tutorial = t;
            t.ShowDialog();
            _tutorial = null;
            return t.RequestedSettingsPage;
        }

        private Settings.TutorialWindow NewTutorial()
        {
            var t = new Settings.TutorialWindow(_media);
            t.ConfigChanged += OnSettingsConfigApplied; // Taste, Sprache usw. wirken sofort
            // Knöpfe unter der Vorschau wirken wie der echte Knopf
            t.VolumeRequested += sign => _worker.EnqueueVolume(sign, ApplyVolume);
            t.MuteRequested += () => _worker.Enqueue(ApplyMute);
            t.ProfileOsdRequested += () => _osd?.ShowProfileSwitch();
            t.AutoAssignRequested += CheckForNewApps; // Automatik eingeschaltet: laufende Apps gleich einsortieren
            return t;
        }

        /// <summary>Tasten-Ereignis (Hook-Thread) an die offene Einführung weiterreichen, damit „Probier es aus“ reagiert.</summary>
        private void ToTutorial(Action<Settings.TutorialWindow> action)
        {
            if (_tutorial == null) return;
            Dispatcher.BeginInvoke(new Action(() => { if (_tutorial != null) action(_tutorial); }));
        }

        private void OpenSettingsWindow(string page)
        {
            var window = new Settings.SettingsWindow(_spotify, _youtube, null, page, _media);
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
            window.TutorialRequested += ShowTutorialDialog;
        }

        /// <summary>Settings hat gespeichert, Positionen zurückgesetzt oder die Config neu geladen: Sprache übernehmen, Flyouts neu ausrichten.</summary>
        private void OnSettingsConfigApplied()
        {
            var cfg = ConfigManager.Load();
            Loc.Apply(cfg);
            _hook?.ReloadConfig();
            _osdFollowsMedia = cfg.OsdFollowsMedia();
            _audio.InvalidateCache();
            _youtube.Apply(cfg);
            if (_mediaOsd != null && _mediaOsd.IsVisible)
                _mediaOsd.ApplyPosition(cfg, _osd!);
            _osd?.Reposition();
            RefreshKeep();
        }

        /// <summary>Das Programm wird beendet: Fenster fragen nicht mehr nach ungespeicherten Änderungen.</summary>
        public bool IsExiting { get; private set; }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            IsExiting = true;
            base.OnSessionEnding(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            IsExiting = true;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _newAppTimer?.Stop();
            _newAppOsd?.Close();
            _settingsAfterTutorial = false;
            _tutorial?.Close();
            _settingsWindow?.Close();
            _hook?.Dispose();
            _tray?.Dispose();
            _worker.Dispose();
            _audio.Dispose();
            _spotify.Dispose();
            _youtube.Dispose();
            _media.Dispose();
            _mediaOsd?.Close();
            _openSettingsWait?.Unregister(null);
            _openSettingsSignal?.Dispose();
            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
