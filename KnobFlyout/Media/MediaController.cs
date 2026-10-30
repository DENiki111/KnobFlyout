using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Media.Control;

namespace KnobFlyout.Media
{
    public sealed class MediaInfo
    {
        public string Title = "";
        public string Artist = "";
        public bool IsPlaying;
        public byte[]? Thumbnail;

        /// <summary>Lesbarer Name der aktiven Quelle (z.B. "Spotify", "Firefox") und, falls Windows eins liefert, deren Logo.</summary>
        public string AppName = "";
        public byte[]? AppLogo;

        // Zeitleiste (Fortschrittsbalken). Position gilt zum Zeitpunkt PositionStamp.
        public bool HasTimeline;
        public bool CanSeek;
        public TimeSpan Duration;
        public TimeSpan Position;
        public DateTimeOffset PositionStamp = DateTimeOffset.Now;

        // "..."-Menü
        public bool ShuffleEnabled;
        public bool ShuffleActive;
        public bool RepeatEnabled;
        public int RepeatMode; // 0 = aus, 1 = Titel, 2 = Liste
    }

    /// <summary>Leichtgewichtige Zeitleisten-Abfrage (ohne Cover/Titel), für das regelmäßige Nachsynchronisieren.</summary>
    public readonly record struct MediaTimeline(TimeSpan Duration, TimeSpan Position, DateTimeOffset Stamp);

    /// <summary>Eine auswählbare Quelle in der Quellen-Leiste (z.B. "Spotify", "Edge").</summary>
    public sealed class MediaSource
    {
        public required GlobalSystemMediaTransportControlsSession Session { get; init; }
        public required string Label { get; init; }
        public required string AppId { get; init; }
        public bool IsPlaying { get; init; }
    }

    /// <summary>
    /// Kapselt die Windows-GSMTC-API (GlobalSystemMediaTransportControlsSessionManager) –
    /// dieselbe Schnittstelle, über die ModernFlyouts sein Media-Flyout speist. Liefert
    /// Titel/Interpret/Cover der gerade aktiven Wiedergabe-App (Spotify, Browser, VLC, …) und
    /// erlaubt Play/Pause/Weiter/Zurück, unabhängig von der per-App-Lautstärke.
    /// </summary>
    public sealed class MediaController : IDisposable
    {
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession? _session;
        private TimeSpan _timelineStart; // StartTime der Zeitleiste der aktuellen Session (meist 0)

        // Zuletzt von uns gesetzte Position (Sprung per Knopf/Hotkey/Leiste). Viele Quellen (z.B. Firefox)
        // melden nach einem Sprung KEINE neue Position - die Zeitleiste bliebe auf dem alten Stand, und der
        // nächste Sprung würde vom alten Stand aus gerechnet (hebt den ersten wieder auf: "geht nur einmal").
        // Deshalb gilt unsere Position, solange die Quelle nichts Neueres (spätere LastUpdatedTime) meldet.
        private (TimeSpan Pos, DateTimeOffset Stamp)? _manualPos;
        private string? _manualApp;
        // Zuletzt gültige Zeitleiste (für Quellen, die beim Puffern nach einem Sprung kurz Dauer 0 melden).
        private (string App, MediaTimeline Timeline, DateTimeOffset ReadAt)? _lastGood;

        /// <summary>Feuert bei Titel-/Wiedergabe-Wechsel. Läuft auf einem WinRT-Callback-Thread, Aufrufer muss ggf. auf den UI-Thread wechseln.</summary>
        public event Action? Changed;

        /// <summary>Ein Programm mit Media-Session kam dazu oder ging (Daueranzeige). Läuft ebenfalls auf einem WinRT-Callback-Thread.</summary>
        public event Action? SourcesChanged;

        public async Task InitializeAsync()
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager.CurrentSessionChanged += (_, _) =>
                {
                    // Nur automatisch wechseln, wenn gerade keine gültige Auswahl besteht (Start,
                    // oder die gewählte App wurde geschlossen). Eine manuelle Auswahl per SwitchTo()
                    // bleibt sonst bestehen, auch wenn Windows kurz eine andere Session als
                    // "aktuell" meldet (z.B. weil ein Video kurz anspielt) - das war der Grund,
                    // warum Umschalten in der Quellen-Leiste vorher sofort wieder rückgängig
                    // gemacht wurde.
                    if (_session == null || !IsStillValid(_session)) Rehook();
                };
                _manager.SessionsChanged += (_, _) =>
                {
                    if (_session == null || !IsStillValid(_session)) Rehook();
                    SourcesChanged?.Invoke();
                };
                Rehook();
            }
            catch (Exception ex)
            {
                Logger.Error($"Media init: {ex.Message}");
            }
        }

        private void Rehook()
        {
            try
            {
                if (_session != null)
                {
                    _session.MediaPropertiesChanged -= OnPropsChanged;
                    _session.PlaybackInfoChanged -= OnPlaybackChanged;
                }

                _session = _manager?.GetCurrentSession();
                if (_session == null)
                {
                    // Windows meldet oft gar keine "aktuelle" Session mehr, sobald ALLES pausiert
                    // ist (z.B. Spotify pausiert, sonst läuft nichts) - dann die erste verfügbare
                    // Session nehmen, statt das Flyout komplett zu verstecken. Das war der Grund,
                    // warum das Flyout beim Pausieren verschwand.
                    _session = _manager?.GetSessions()?.FirstOrDefault();
                }

                if (_session != null)
                {
                    _session.MediaPropertiesChanged += OnPropsChanged;
                    _session.PlaybackInfoChanged += OnPlaybackChanged;
                }
                Changed?.Invoke();
            }
            catch (Exception ex)
            {
                Logger.Error($"Media rehook: {ex.Message}");
            }
        }

        /// <summary>Prüft, ob eine Session (per AppId) noch unter den aktuell laufenden Mediensessions ist.</summary>
        private bool IsStillValid(GlobalSystemMediaTransportControlsSession s)
        {
            try
            {
                var id = s.SourceAppUserModelId;
                return _manager?.GetSessions()?.Any(x => x.SourceAppUserModelId == id) == true;
            }
            catch
            {
                return false;
            }
        }

        private void OnPropsChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => Changed?.Invoke();
        private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => Changed?.Invoke();

        public bool HasSession => _session != null;

        /// <summary>
        /// Entspricht ModernFlyouts' Quellen-Leiste: alle Apps mit einer aktiven GSMTC-Session (z.B. Spotify + Edge/YouTube gleichzeitig).
        /// Ein Eintrag pro App: Firefox meldet z. B. mehrere Sessions mit derselben AppId (mehrere Tabs/Fenster
        /// mit Medien) - die wären als doppelte, gleichzeitig „aktive“ Pills erschienen. Die abspielende Session vertritt die App.
        /// </summary>
        public List<MediaSource> GetSources()
        {
            var result = new List<MediaSource>();
            try
            {
                var sessions = _manager?.GetSessions();
                if (sessions == null) return result;

                foreach (var s in sessions)
                {
                    try
                    {
                        string appId = s.SourceAppUserModelId ?? "";
                        bool playing = s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                        int existing = result.FindIndex(r => r.AppId == appId);
                        if (existing >= 0 && (result[existing].IsPlaying || !playing)) continue;

                        var source = new MediaSource
                        {
                            Session = s,
                            AppId = appId,
                            Label = FriendlyAppName(appId),
                            IsPlaying = playing,
                        };
                        if (existing >= 0) result[existing] = source;
                        else result.Add(source);
                    }
                    catch { /* einzelne Session übersprungen */ }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Media GetSources: {ex.Message}");
            }
            return result;
        }

        /// <summary>Gehört die Quelle zu einem Programm der Liste? Name wie in der Quellen-Leiste, Groß-/Kleinschreibung und „.exe“ egal, Teilname reicht.</summary>
        public static bool IsListed(MediaSource source, IEnumerable<string> names) => names.Any(n =>
        {
            string name = n.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            return name.Length > 0 && (source.Label.Contains(name, StringComparison.OrdinalIgnoreCase) || source.AppId.Contains(name, StringComparison.OrdinalIgnoreCase));
        });

        /// <summary>Quelle eines Programms aus der Liste (die gerade gezeigte zuerst, dann eine spielende), sonst null.</summary>
        public MediaSource? FindListed(IReadOnlyCollection<string> names)
        {
            if (names.Count == 0) return null;
            var listed = GetSources().Where(s => IsListed(s, names)).ToList();
            return listed.FirstOrDefault(s => s.AppId == CurrentAppId) ?? listed.FirstOrDefault(s => s.IsPlaying) ?? listed.FirstOrDefault();
        }

        /// <summary>
        /// Ermittelt einen lesbaren App-Namen zur AppUserModelId. Reihenfolge:
        /// 1. Windows' eigene Namensauflösung (AppInfo) - funktioniert für die meisten paketierten
        ///    Apps und viele moderne Browser-Installationen zuverlässig mit dem echten Anzeigenamen.
        /// 2. Ist die Id ein Dateipfad/.exe (klassische Win32-Apps), der Dateiname ohne Endung.
        /// 3. Bekannte Teilstrings (Fallback für Sonderfälle, die AppInfo nicht auflöst).
        /// 4. Rohe Id gekürzt, als letzter Ausweg - nie eine lange Zahlenfolge unbeschnitten zeigen.
        /// </summary>
        public static string FriendlyAppName(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId)) return "App";

            var displayName = DisplayNameCache.GetOrAdd(appId, ResolveDisplayName);
            if (displayName != null) return displayName;

            if (appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var fileName = System.IO.Path.GetFileNameWithoutExtension(appId);
                if (fileName.Length > 0) return char.ToUpper(fileName[0]) + fileName[1..];
            }

            var id = appId.ToLowerInvariant();
            if (id.Contains("308046b0af4a39cb")) return "Firefox"; // Firefox nutzt diese feste Hash-ID als AppUserModelId
            if (id.Contains("spotify")) return "Spotify";
            if (id.Contains("msedge") || id.Contains("microsoftedge")) return "Edge";
            if (id.Contains("chrome")) return "Chrome";
            if (id.Contains("firefox")) return "Firefox";
            if (id.Contains("vivaldi")) return "Vivaldi";
            if (id.Contains("brave")) return "Brave";
            if (id.Contains("opera")) return "Opera";
            if (id.Contains("vlc")) return "VLC";
            if (id.Contains("wmplayer") || id.Contains("zune") || id.Contains("groove") || id.Contains("media.player")) return "Media Player";
            if (id.Contains("discord")) return "Discord";

            // Firefox meldet als AppUserModelId einen 16-stelligen Hex-Hash (z.B. "308046B0AF4A39CB",
            // aus dem Installationspfad abgeleitet) - Windows kann dazu keinen Namen auflösen.
            if (System.Text.RegularExpressions.Regex.IsMatch(appId, "^[0-9A-Fa-f]{16}$")) return "Firefox";

            // Rein hexadezimale IDs (Hash-AppUserModelIds, wie sie manche Browser vergeben) sagen
            // nichts aus: Dann ist es sehr wahrscheinlich ein Browser - den laufenden nennen.
            if (LooksLikeHash(appId)) return GuessRunningBrowser() ?? "Browser";

            int cut = id.IndexOfAny(new[] { '.', '_', '!' });
            if (cut > 0) return char.ToUpper(appId[0]) + appId[1..cut];

            return appId.Length > 10 ? appId[..10] + "…" : appId;
        }

        // Ergebnis der AppInfo-Abfrage pro AppId (auch "kein Treffer"), da FriendlyAppName bei jeder
        // Flyout-Aktualisierung für jede Quelle läuft und AppInfo bei Win32-Apps meist per WinRT-Exception scheitert.
        private static readonly ConcurrentDictionary<string, string?> DisplayNameCache = new();

        private static string? ResolveDisplayName(string appId)
        {
            try
            {
                var name = AppInfo.GetFromAppUserModelId(appId)?.DisplayInfo?.DisplayName;
                return string.IsNullOrWhiteSpace(name) ? null : name;
            }
            catch
            {
                // Kein Treffer (z.B. normale Win32-exe ohne registrierte AUMID) - weiter mit Fallbacks.
                return null;
            }
        }

        /// <summary>
        /// Wechselt die angezeigte/gesteuerte Session (Quellen-Leiste). Die physische Medientaste
        /// (ein globaler, nicht an eine App gebundener Tastendruck) entscheidet komplett Windows
        /// selbst, welche App sie trifft - unabhängig von unserer Auswahl, und reines Pausieren der
        /// alten Quelle allein reicht offenbar nicht, um das zuverlässig umzulenken. Stattdessen:
        /// die alte Quelle pausieren UND die neue direkt per TryPlayAsync starten - derselbe
        /// zuverlässige Weg, den auch der Play/Pause-Button im Flyout selbst nutzt (eine gezielte,
        /// pro-Session-COM-Anfrage statt der mehrdeutigen globalen Taste). Entspricht also einem
        /// "virtuellen Druck" auf den Play-Button der neu gewählten Quelle.
        /// </summary>
        public void SwitchTo(GlobalSystemMediaTransportControlsSession session)
        {
            if (ReferenceEquals(session, _session)) return;

            var previous = _session;
            if (previous != null)
            {
                _ = SafeCall(() => previous.TryPauseAsync().AsTask());
                previous.MediaPropertiesChanged -= OnPropsChanged;
                previous.PlaybackInfoChanged -= OnPlaybackChanged;
            }
            _session = session;
            _session.MediaPropertiesChanged += OnPropsChanged;
            _session.PlaybackInfoChanged += OnPlaybackChanged;
            _ = SafeCall(() => session.TryPlayAsync().AsTask());
            Changed?.Invoke();
        }

        /// <summary>Zeigt und steuert ab jetzt diese Session, ohne etwas zu pausieren oder zu starten (anders als SwitchTo).</summary>
        public void ShowSource(GlobalSystemMediaTransportControlsSession session)
        {
            if (ReferenceEquals(session, _session)) return;
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnPropsChanged;
                _session.PlaybackInfoChanged -= OnPlaybackChanged;
            }
            _session = session;
            _session.MediaPropertiesChanged += OnPropsChanged;
            _session.PlaybackInfoChanged += OnPlaybackChanged;
        }

        private static bool LooksLikeHash(string appId)
        {
            int hex = 0;
            foreach (char c in appId)
            {
                if (Uri.IsHexDigit(c)) hex++;
                else if (c is ';' or '-' or '{' or '}') continue;
                else return false;
            }
            return hex >= 12;
        }

        /// <summary>Nennt den Browser, wenn genau eine bekannte Browser-Familie läuft (sonst null).</summary>
        private static string? GuessRunningBrowser()
        {
            var known = new (string Process, string Name)[]
            {
                ("firefox", "Firefox"), ("chrome", "Chrome"), ("msedge", "Edge"),
                ("brave", "Brave"), ("opera", "Opera"), ("vivaldi", "Vivaldi"),
            };

            string? found = null;
            foreach (var (process, name) in known)
            {
                try
                {
                    var procs = System.Diagnostics.Process.GetProcessesByName(process);
                    bool running = procs.Length > 0;
                    foreach (var pr in procs) pr.Dispose();
                    if (!running) continue;
                    if (found != null) return null; // mehrere Browser laufen - nicht raten
                    found = name;
                }
                catch
                {
                    // Prozess nicht lesbar - ignorieren.
                }
            }
            return found;
        }

        /// <summary>Wechselt reihum zur nächsten verfügbaren Medienquelle (für "Taste lange halten").</summary>
        public void CycleSource()
        {
            var sources = GetSources();
            if (sources.Count < 2) return;

            string? currentId = CurrentAppId;
            int idx = sources.FindIndex(s => s.AppId == currentId);
            int next = (idx + 1) % sources.Count;
            SwitchTo(sources[next].Session);
        }

        /// <summary>Pausiert eine beliebige Quelle direkt, ohne zu ihr zu wechseln (z.B. Spotify stummschalten, während YouTube aktiv bleibt).</summary>
        public void PauseSource(GlobalSystemMediaTransportControlsSession session) =>
            _ = SafeCall(() => session.TryPauseAsync().AsTask());

        /// <summary>Entspricht GetAppId() - aktuell aktive Quelle, für den Vergleich in der Quellen-Leiste.</summary>
        public string? CurrentAppId => _session?.SourceAppUserModelId;

        public async Task<MediaInfo?> GetInfoAsync()
        {
            var session = _session;
            if (session == null) return null;
            try
            {
                var props = await session.TryGetMediaPropertiesAsync();
                var playback = session.GetPlaybackInfo();
                var info = new MediaInfo
                {
                    Title = props.Title ?? "",
                    Artist = props.Artist ?? "",
                    IsPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    AppName = FriendlyAppName(session.SourceAppUserModelId ?? ""),
                };

                if (playback != null)
                {
                    info.CanSeek = playback.Controls.IsPlaybackPositionEnabled;
                    info.ShuffleEnabled = playback.Controls.IsShuffleEnabled;
                    info.ShuffleActive = playback.IsShuffleActive == true;
                    info.RepeatEnabled = playback.Controls.IsRepeatEnabled;
                    var rm = playback.AutoRepeatMode;
                    info.RepeatMode = rm == Windows.Media.MediaPlaybackAutoRepeatMode.Track ? 1
                                    : rm == Windows.Media.MediaPlaybackAutoRepeatMode.List ? 2 : 0;
                }

                var tl = ReadTimeline(session);
                if (tl != null)
                {
                    info.HasTimeline = true;
                    info.Duration = tl.Value.Duration;
                    info.Position = tl.Value.Position;
                    info.PositionStamp = tl.Value.Stamp;
                }

                info.AppLogo = await TryGetAppLogoAsync(session.SourceAppUserModelId);

                if (props.Thumbnail != null)
                {
                    using var stream = await props.Thumbnail.OpenReadAsync();
                    using var ms = new MemoryStream();
                    await stream.AsStreamForRead().CopyToAsync(ms);
                    if (ms.Length > 0) info.Thumbnail = ms.ToArray();
                }
                return info;
            }
            catch (Exception ex)
            {
                Logger.Error($"Media info: {ex.Message}");
                return null;
            }
        }

        /// <summary>Liest die Zeitleiste einer Session; null, wenn keine (z.B. Livestream ohne Dauer).</summary>
        private MediaTimeline? ReadTimeline(GlobalSystemMediaTransportControlsSession session)
        {
            try
            {
                var tl = session.GetTimelineProperties();
                if (tl == null) return null;

                var app = session.SourceAppUserModelId;
                var manual = _manualPos;
                bool manualHere = manual != null && _manualApp == app;

                var duration = tl.EndTime - tl.StartTime;
                if (duration <= TimeSpan.Zero)
                {
                    // Firefox/YouTube meldet nach einem Sprung (beim Puffern) mehrere Sekunden lang Dauer 0.
                    // Dann mit der letzten gültigen Zeitleiste (bzw. der gemerkten Sprungposition) weiterrechnen.
                    var last = _lastGood;
                    if (last == null || last.Value.App != app) return null;
                    if (manualHere && DateTimeOffset.Now - manual!.Value.Stamp < ManualPosTimeout)
                        return new MediaTimeline(last.Value.Timeline.Duration, manual.Value.Pos, manual.Value.Stamp);
                    if (DateTimeOffset.Now - last.Value.ReadAt < LastGoodTimeout)
                        return last.Value.Timeline;
                    return null;
                }

                var result = ReadValidTimeline(session, tl, duration, manualHere ? manual : null);
                _lastGood = (app, result, DateTimeOffset.Now);
                return result;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Zeitleiste mit gültiger Dauer; die gemerkte Sprungposition (falls vorhanden) hat Vorrang, solange die Quelle sie nicht bestätigt.</summary>
        private MediaTimeline ReadValidTimeline(GlobalSystemMediaTransportControlsSession session,
            GlobalSystemMediaTransportControlsSessionTimelineProperties tl, TimeSpan duration,
            (TimeSpan Pos, DateTimeOffset Stamp)? manual)
        {
            _timelineStart = tl.StartTime;
            var stamp = tl.LastUpdatedTime;
            if (stamp.Year < 2000) stamp = DateTimeOffset.Now; // nie aktualisiert gemeldet
            if (manual != null)
            {
                var m = manual.Value;
                if (m.Stamp > stamp)
                    return new MediaTimeline(duration, m.Pos, m.Stamp);

                // Browser (YouTube) melden kurz nach dem Sprung oft einen neuen Zeitstempel, aber noch
                // die ALTE Position. Die gemerkte Position gilt deshalb weiter, bis die gemeldete dazu
                // passt - spätestens nach ManualPosTimeout (dann gewinnt die Quelle, z.B. eigener Sprung im Browser).
                var reported = tl.Position - tl.StartTime;
                var expected = m.Pos;
                if (IsPlaying(session)) expected += stamp - m.Stamp;
                bool matches = (reported - expected).Duration() <= ManualPosTolerance;
                if (!matches && DateTimeOffset.Now - m.Stamp < ManualPosTimeout)
                    return new MediaTimeline(duration, m.Pos, m.Stamp);
                _manualPos = null; // die Quelle hat inzwischen selbst aktualisiert
            }

            return new MediaTimeline(duration, tl.Position - tl.StartTime, stamp);
        }

        private static readonly TimeSpan ManualPosTolerance = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ManualPosTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan LastGoodTimeout = TimeSpan.FromSeconds(10);

        private static bool IsPlaying(GlobalSystemMediaTransportControlsSession session)
        {
            try { return session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
            catch { return false; }
        }

        /// <summary>Rohwerte der Zeitleiste fürs Log (Position/Dauer/Alter des letzten Updates, Status).</summary>
        private static string DescribeRawTimeline(GlobalSystemMediaTransportControlsSession session)
        {
            try
            {
                var tl = session.GetTimelineProperties();
                string status = session.GetPlaybackInfo()?.PlaybackStatus.ToString() ?? "?";
                if (tl == null) return $"keine Timeline, Status={status}";
                var age = DateTimeOffset.Now - tl.LastUpdatedTime;
                return $"Pos={tl.Position - tl.StartTime:m\\:ss} Dauer={tl.EndTime - tl.StartTime:m\\:ss} " +
                       $"Update vor {age.TotalSeconds:0.0}s, Status={status}";
            }
            catch (Exception ex)
            {
                return $"Fehler: {ex.Message}";
            }
        }

        /// <summary>Aktuelle Zeitleiste der gewählten Session (leichtgewichtig, ohne Cover/Titel).</summary>
        public MediaTimeline? GetTimeline() => _session == null ? null : ReadTimeline(_session);

        /// <summary>Sprungweite für die ±-Knöpfe und die Skip-Hotkeys.</summary>
        public const int SkipSeconds = 10;

        /// <summary>
        /// Springt relativ zur aktuellen Stelle (z.B. +10 s / -10 s). Die Position wird aus der
        /// Zeitleiste hochgerechnet (Position + seit dem letzten Update vergangene Zeit, falls
        /// gerade gespielt wird) und auf 0..Dauer begrenzt. Zeigt danach das Flyout kurz erneut.
        /// </summary>
        public void SkipBy(TimeSpan delta)
        {
            var session = _session;
            if (session == null) return;

            var tl = ReadTimeline(session);
            if (tl == null)
            {
                Logger.Info($"Media-Skip ignoriert: keine Zeitleiste ({session.SourceAppUserModelId}, {DescribeRawTimeline(session)})");
                return;
            }

            var pos = tl.Value.Position;
            try
            {
                if (session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    pos += DateTimeOffset.Now - tl.Value.Stamp;
            }
            catch { /* ohne Status einfach vom gemeldeten Stand ausgehen */ }

            var target = pos + delta;
            if (target < TimeSpan.Zero) target = TimeSpan.Zero;
            if (target > tl.Value.Duration) target = tl.Value.Duration;
            SeekTo(target);

            // Die Quelle meldet die neue Position erst kurz nach dem Sprung - dann Flyout aktualisieren.
            _ = Task.Run(async () =>
            {
                await Task.Delay(400);
                Changed?.Invoke();
            });
        }

        /// <summary>Springt an eine Stelle (relativ zum Anfang) – nur sinnvoll, wenn die Quelle das erlaubt (CanSeek).</summary>
        public void SeekTo(TimeSpan position)
        {
            var session = _session;
            if (session == null) return;

            // Optimistisch sofort merken (auch für schnell aufeinanderfolgende Sprünge); lehnt die
            // Quelle den Sprung ab, wird die Merkung wieder verworfen.
            _manualPos = (position, DateTimeOffset.Now);
            _manualApp = session.SourceAppUserModelId;

            long ticks = (_timelineStart + position).Ticks;
            _ = SafeCall(async () =>
            {
                bool ok = await session.TryChangePlaybackPositionAsync(ticks).AsTask();
                if (!ok)
                {
                    _manualPos = null;
                    Logger.Info($"Media-Seek von Quelle abgelehnt: {session.SourceAppUserModelId} -> {position:m\\:ss}");
                }
            });
        }

        public void SetShuffle(bool on) =>
            _ = SafeCall(() => _session?.TryChangeShuffleActiveAsync(on).AsTask());

        /// <summary>0 = aus, 1 = Titel wiederholen, 2 = Liste wiederholen.</summary>
        public void SetRepeat(int mode) =>
            _ = SafeCall(() => _session?.TryChangeAutoRepeatModeAsync(
                mode == 1 ? Windows.Media.MediaPlaybackAutoRepeatMode.Track
                : mode == 2 ? Windows.Media.MediaPlaybackAutoRepeatMode.List
                : Windows.Media.MediaPlaybackAutoRepeatMode.None).AsTask());

        private readonly Dictionary<string, byte[]> _logoCache = new();

        /// <summary>
        /// Logo der Quell-App fürs Flyout. Reihenfolge:
        /// 1. Das Icon der laufenden Programmdatei (klappt auch für Browser wie Firefox, deren
        ///    Mediensession nur eine Hash-ID liefert, und zeigt das echte Programm-Logo statt eines
        ///    Store-Kachel-Logos).
        /// 2. Windows' eigenes App-Logo (paketierte Apps), falls kein Prozess/Icon gefunden wurde.
        /// Erfolgreiche Treffer werden pro AppId zwischengespeichert.
        /// </summary>
        private async Task<byte[]?> TryGetAppLogoAsync(string? appId)
        {
            if (string.IsNullOrWhiteSpace(appId)) return null;
            if (_logoCache.TryGetValue(appId, out var cached)) return cached;

            byte[]? logo = TryGetProcessIcon(appId) ?? await TryGetPackagedLogoAsync(appId);
            if (logo != null) _logoCache[appId] = logo;
            return logo;
        }

        private static byte[]? TryGetProcessIcon(string appId)
        {
            try
            {
                foreach (var name in CandidateProcessNames(appId))
                {
                    System.Diagnostics.Process[] procs;
                    try { procs = System.Diagnostics.Process.GetProcessesByName(name); }
                    catch { continue; }

                    try
                    {
                        foreach (var proc in procs)
                        {
                            string? path = null;
                            try { path = proc.MainModule?.FileName; }
                            catch { /* kein Zugriff auf diesen Prozess - nächsten versuchen */ }
                            if (string.IsNullOrEmpty(path)) continue;

                            try
                            {
                                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                                if (icon == null) continue;
                                using var bmp = icon.ToBitmap();
                                using var ms = new MemoryStream();
                                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                                if (ms.Length > 0) return ms.ToArray();
                            }
                            catch { /* Icon nicht lesbar - nächsten Prozess versuchen */ }
                        }
                    }
                    finally
                    {
                        foreach (var proc in procs) proc.Dispose();
                    }
                }
            }
            catch
            {
                // Nur Zierde - ohne Logo bleibt das Standard-Symbol.
            }
            return null;
        }

        /// <summary>Mögliche Prozessnamen zur Quelle: .exe-Name, bekannter Name zum Anzeigenamen, Teile der AppId.</summary>
        private static IEnumerable<string> CandidateProcessNames(string appId)
        {
            var names = new List<string>();
            void Add(string? n)
            {
                if (!string.IsNullOrWhiteSpace(n) && !names.Contains(n, StringComparer.OrdinalIgnoreCase)) names.Add(n);
            }

            if (appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                Add(Path.GetFileNameWithoutExtension(appId));

            switch (FriendlyAppName(appId))
            {
                case "Firefox": Add("firefox"); break;
                case "Chrome": Add("chrome"); break;
                case "Edge": Add("msedge"); break;
                case "Brave": Add("brave"); break;
                case "Opera": Add("opera"); break;
                case "Vivaldi": Add("vivaldi"); break;
                case "Spotify": Add("Spotify"); break;
                case "VLC": Add("vlc"); break;
                case "Discord": Add("Discord"); break;
            }

            foreach (var part in appId.Split('.', '_', '!', ';', ' '))
            {
                if (part.Length >= 3 && !LooksLikeHash(part)) Add(part);
            }
            return names;
        }

        /// <summary>Logo einer paketierten App (Microsoft Store) über Windows' App-Infos; sonst null.</summary>
        private static async Task<byte[]?> TryGetPackagedLogoAsync(string appId)
        {
            try
            {
                var info = AppInfo.GetFromAppUserModelId(appId);
                var logoRef = info?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(64, 64));
                if (logoRef == null) return null;

                using var stream = await logoRef.OpenReadAsync();
                using var ms = new MemoryStream();
                await stream.AsStreamForRead().CopyToAsync(ms);
                return ms.Length > 0 ? ms.ToArray() : null;
            }
            catch
            {
                return null;
            }
        }

        public void TogglePlayPause() => _ = SafeCall(() => _session?.TryTogglePlayPauseAsync().AsTask());
        public void Next() => _ = SafeCall(() => _session?.TrySkipNextAsync().AsTask());
        public void Previous() => _ = SafeCall(() => _session?.TrySkipPreviousAsync().AsTask());

        private static async Task SafeCall(Func<Task?> action)
        {
            try
            {
                var t = action();
                if (t != null) await t;
            }
            catch (Exception ex)
            {
                Logger.Error($"Media control: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnPropsChanged;
                _session.PlaybackInfoChanged -= OnPlaybackChanged;
            }
        }
    }
}
