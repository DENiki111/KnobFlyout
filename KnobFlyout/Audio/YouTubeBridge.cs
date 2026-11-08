using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using KnobFlyout.Config;

namespace KnobFlyout.Audio
{
    /// <summary>
    /// YT-Bridge: regelt die Lautstärke eines YouTube-Videos in Firefox bzw. Chrome über den YouTube-Player
    /// selbst statt über die Browser-Session im Windows-Mixer. Gegenstück sind die Erweiterungen in
    /// Browser/firefox und Browser/chrome: deren Hintergrund verbindet sich als WebSocket-Client mit
    /// ws://127.0.0.1:8975/ytb, die Content-Scripts melden je Tab Lautstärke/Stumm/Wiedergabe und setzen
    /// Werte per setVolume. Server über einen schlichten TcpListener (kein http.sys → kein URL-ACL, läuft
    /// ohne Admin); angenommen werden nur Erweiterungs-Origins, der Browser ergibt sich aus der Origin
    /// (moz-extension:// = Firefox, chrome-extension:// = Chrome). Beide Browser dürfen gleichzeitig
    /// verbunden sein, jede Verbindung hat ihre eigenen Tabs.
    /// Wie bei Spotify liefert ChangeVolume sofort den gecachten neuen Wert fürs OSD, der Befehl geht
    /// asynchron raus (Antwort im Test nach 2–6 ms, deshalb ohne Debounce).
    /// </summary>
    public sealed class YouTubeBridge : IDisposable
    {
        public const int Port = 8975;
        public const string Firefox = "firefox";
        public const string Chrome = "chrome";
        public const string Opera = "opera";
        public static readonly string[] Browsers = { Firefox, Chrome, Opera };
        /// <summary>Webseiten, die die Erweiterung erkennt (Namen wie in sites.js), einzeln abschaltbar über bridge_sites_off.</summary>
        public static readonly string[] Sites = { "YouTube", "Twitch", "TikTok", "Instagram", "Netflix", "Prime Video", "Disney+", "Crunchyroll", "Joyn", "VOE" };
        private const string WsGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        private sealed class Tab
        {
            public int Volume = 100;
            public bool Muted;
            public bool Playing;
            public string Title = "";
            public string Site = "YouTube"; // Erweiterung 1.0.x kennt nur YouTube und schickt keine Seite
            public DateTime PlayingSince;
            public int LastRid;     // zuletzt gesendeter Befehl
            public int AnsweredRid; // zuletzt beantworteter Befehl
        }

        private sealed class Connection
        {
            public readonly WebSocket Socket;
            public string Browser; // Chromium-Verbindung wird bei "hello" ggf. zu Opera
            public readonly Dictionary<int, Tab> Tabs = new();
            public readonly Channel<string> Outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
            public Connection(WebSocket socket, string browser) { Socket = socket; Browser = browser; }
        }

        /// <summary>Stand je Browser für die Statuszeile der Settings.</summary>
        public sealed record BrowserStatus(string Browser, bool Connected, int Tabs, string? PlayingTitle);

        private readonly object _lock = new();
        private readonly List<Connection> _conns = new();
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private int _rid;
        private readonly Dictionary<int, TaskCompletionSource<(double Pos, double Dur)?>> _seeks = new(); // offene Sprünge je rid
        private HashSet<string> _sitesOff = new(StringComparer.OrdinalIgnoreCase);

        public bool IsListening { get { lock (_lock) return _listener != null; } }

        /// <summary>Browser einer Audio-Session ("firefox.exe" → Firefox, "chrome.exe" → Chrome, "opera.exe" → Opera/Opera GX), sonst null.</summary>
        public static string? BrowserOf(string processName)
        {
            if (processName.Contains("firefox", StringComparison.OrdinalIgnoreCase)) return Firefox;
            if (processName.Contains("chrome", StringComparison.OrdinalIgnoreCase)) return Chrome;
            if (processName.Contains("opera", StringComparison.OrdinalIgnoreCase)) return Opera;
            return null;
        }

        public static string DisplayName(string browser) => browser switch { Chrome => "Chrome", Opera => "Opera", _ => "Firefox" };

        public BrowserStatus Status(string browser)
        {
            lock (_lock)
            {
                var conns = _conns.Where(c => c.Browser == browser).ToList();
                return new BrowserStatus(browser, conns.Count > 0, conns.Sum(c => c.Tabs.Count), PlayingTabLocked(browser)?.Tab.Title);
            }
        }

        /// <summary>Server je nach youtube_bridge starten oder stoppen (beim Start und nach jedem Speichern).</summary>
        public void Apply(AppConfig cfg)
        {
            // Nur Seiten mit Schalter (Sites) lassen sich abschalten; ein alter Eintrag einer Seite ohne Schalter (z. B. Jellyfin) gilt nicht mehr.
            lock (_lock) _sitesOff = new HashSet<string>((cfg.BridgeSitesOff ?? new List<string>()).Where(s => Sites.Contains(s, StringComparer.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
            if (cfg.YoutubeBridge) Start();
            else Stop();
        }

        /// <summary>Nur wenn eingeschaltet und in diesem Browser gerade ein YouTube-Video spielt – sonst bleibt es bei der Browser-Session.</summary>
        public bool IsActive(AppConfig cfg, string browser)
        {
            if (!cfg.YoutubeBridge) return false;
            lock (_lock) return PlayingTabLocked(browser) != null;
        }

        /// <summary>Seite des Players, den der Knopf in diesem Browser gerade regelt (YouTube, Crunchyroll, Twitch, VOE, Jellyfin) – Name fürs OSD.</summary>
        public string? PlayingSite(string browser)
        {
            lock (_lock) return PlayingTabLocked(browser)?.Tab.Site;
        }

        /// <summary>Neuer Wert sofort aus dem Cache (fürs OSD), der Befehl an den Tab geht asynchron raus.</summary>
        public int? ChangeVolume(string browser, int delta)
        {
            lock (_lock)
            {
                var pick = PlayingTabLocked(browser);
                if (pick == null) return null;
                var (conn, id, t) = pick.Value;
                t.Volume = Math.Clamp(t.Volume + delta, 0, 100);
                SendLocked(conn, id, t, "set", t.Volume);
                return t.Volume;
            }
        }

        public bool? ToggleMute(string browser)
        {
            lock (_lock)
            {
                var pick = PlayingTabLocked(browser);
                if (pick == null) return null;
                var (conn, id, t) = pick.Value;
                t.Muted = !t.Muted;
                SendLocked(conn, id, t, "mute", t.Muted);
                return t.Muted;
            }
        }

        /// <summary>
        /// Springt im Video dieses Browsers um seconds Sekunden. Tab: Titel passt zur Windows-Wiedergabe (title),
        /// sonst der spielende Tab, wenn Windows „spielt“ meldet. Liefert neue Position und Dauer, oder null, wenn
        /// die Erweiterung aus/nicht verbunden ist, kein passendes Video kennt, die Seite nicht springen kann
        /// (Livestream, Netflix, Erweiterung vor 1.2) oder nicht innerhalb von timeoutMs antwortet – dann springt
        /// KnobFlyout über Windows.
        /// </summary>
        public async Task<(double Pos, double Dur)?> SeekAsync(string browser, double seconds, string? title, bool playing, int timeoutMs = 700)
        {
            int rid;
            TaskCompletionSource<(double Pos, double Dur)?> tcs;
            lock (_lock)
            {
                if (_listener == null) return null;
                var pick = TabForTitleLocked(browser, title) ?? (playing ? PlayingTabLocked(browser) : null);
                if (pick == null) return null;
                var (conn, id, t) = pick.Value;
                tcs = new TaskCompletionSource<(double Pos, double Dur)?>(TaskCreationOptions.RunContinuationsAsynchronously);
                rid = SendLocked(conn, id, t, "seek", Math.Round(seconds, 3));
                _seeks[rid] = tcs;
            }
            var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)).ConfigureAwait(false);
            lock (_lock) _seeks.Remove(rid);
            if (done != tcs.Task)
            {
                Logger.Info($"Browser-Bridge {DisplayName(browser)}: Skip ohne Antwort nach {timeoutMs} ms, springe über Windows");
                return null;
            }
            return tcs.Task.Result;
        }

        /// <summary>Tab, dessen Titel zur Windows-Wiedergabe passt (gleich oder einer enthält den anderen); spielende zuerst, dann der zuletzt gestartete.</summary>
        private (Connection Conn, int Id, Tab Tab)? TabForTitleLocked(string browser, string? title)
        {
            title = title?.Trim();
            if (string.IsNullOrEmpty(title) || title.Length < 3) return null;
            (Connection Conn, int Id, Tab Tab)? best = null;
            foreach (var c in _conns)
            {
                if (c.Browser != browser) continue;
                foreach (var (id, t) in c.Tabs)
                {
                    if (_sitesOff.Contains(t.Site) || t.Title.Trim().Length < 3) continue;
                    bool match = t.Title.Contains(title, StringComparison.OrdinalIgnoreCase) || title.Contains(t.Title.Trim(), StringComparison.OrdinalIgnoreCase);
                    if (!match) continue;
                    if (best == null || (t.Playing && !best.Value.Tab.Playing)
                        || (t.Playing == best.Value.Tab.Playing && t.PlayingSince > best.Value.Tab.PlayingSince))
                        best = (c, id, t);
                }
            }
            return best;
        }

        /// <summary>Spielen mehrere Tabs dieses Browsers, gewinnt der zuletzt gestartete. Abgeschaltete Webseiten zählen nicht.</summary>
        private (Connection Conn, int Id, Tab Tab)? PlayingTabLocked(string browser)
        {
            (Connection Conn, int Id, Tab Tab)? best = null;
            foreach (var c in _conns)
            {
                if (c.Browser != browser) continue;
                foreach (var (id, t) in c.Tabs)
                    if (t.Playing && !_sitesOff.Contains(t.Site) && (best == null || t.PlayingSince > best.Value.Tab.PlayingSince))
                        best = (c, id, t);
            }
            return best;
        }

        private int SendLocked(Connection conn, int tabId, Tab t, string op, object value)
        {
            t.LastRid = ++_rid;
            conn.Outbox.Writer.TryWrite(JsonSerializer.Serialize(new { type = "cmd", rid = t.LastRid, tabId, op, value }));
            return t.LastRid;
        }

        private void Start()
        {
            lock (_lock)
            {
                if (_listener != null) return;
                try
                {
                    var listener = new TcpListener(IPAddress.Loopback, Port);
                    listener.Start();
                    _listener = listener;
                    _cts = new CancellationTokenSource();
                    _ = AcceptLoopAsync(listener, _cts.Token);
                }
                catch (Exception ex)
                {
                    Logger.Error($"YT-Bridge Start (Port {Port}): {ex.Message}");
                }
            }
        }

        private void Stop()
        {
            TcpListener? listener;
            CancellationTokenSource? cts;
            List<Connection> conns;
            lock (_lock)
            {
                listener = _listener;
                cts = _cts;
                conns = new List<Connection>(_conns);
                _listener = null;
                _cts = null;
                _conns.Clear();
            }
            try { cts?.Cancel(); } catch { }
            try { listener?.Stop(); } catch { }
            foreach (var c in conns)
                try { c.Socket.Abort(); } catch { }
        }

        private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(ct); }
                catch { return; } // Stop()
                _ = HandleClientAsync(client, ct);
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            Connection? conn = null;
            try
            {
                using (client)
                {
                    var stream = client.GetStream();
                    string? head = await ReadHeadAsync(stream, ct);
                    string? key = head == null ? null : Header(head, "Sec-WebSocket-Key");
                    string origin = head == null ? "" : Header(head, "Origin") ?? "";
                    // Nur die Erweiterungen: Webseiten schicken ihre eigene Origin (https://…) mit
                    string? browser = origin.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase) ? Firefox
                                    : origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ? Chrome : null;
                    if (key == null || browser == null || !head!.StartsWith("GET /ytb ", StringComparison.Ordinal))
                    {
                        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), ct);
                        return;
                    }

                    string accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + WsGuid)));
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(
                        "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n"), ct);

                    conn = new Connection(WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(30)), browser);
                    lock (_lock) _conns.Add(conn); // die Erweiterung schickt nach "hello" alle Tabs

                    _ = SendLoopAsync(conn, ct);
                    await ReceiveLoopAsync(conn, ct);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or IOException or ObjectDisposedException)
            {
                // Browser geschlossen, Erweiterung neu geladen, Service Worker beendet oder Stop() – normal
            }
            catch (Exception ex)
            {
                Logger.Error($"YT-Bridge Verbindung: {ex.Message}");
            }
            finally
            {
                if (conn != null)
                {
                    lock (_lock) _conns.Remove(conn);
                    Logger.Info($"YT-Bridge: Verbindung zur {DisplayName(conn.Browser)}-Erweiterung getrennt");
                    conn.Outbox.Writer.TryComplete();
                    conn.Socket.Dispose();
                }
            }
        }

        /// <summary>HTTP-Kopf des Upgrade-Requests bis zur Leerzeile (max. 8 KB, 5 s).</summary>
        private static async Task<string?> ReadHeadAsync(NetworkStream stream, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var buf = new byte[1];
            var head = new StringBuilder();
            while (head.Length < 8192)
            {
                if (await stream.ReadAsync(buf, timeout.Token) == 0) return null;
                head.Append((char)buf[0]);
                if (head.Length >= 4 && head[^1] == '\n' && head[^2] == '\r' && head[^3] == '\n' && head[^4] == '\r')
                    return head.ToString();
            }
            return null;
        }

        private static string? Header(string head, string name)
        {
            foreach (var line in head.Split("\r\n"))
            {
                int i = line.IndexOf(':');
                if (i > 0 && line[..i].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                    return line[(i + 1)..].Trim();
            }
            return null;
        }

        private static async Task SendLoopAsync(Connection conn, CancellationToken ct)
        {
            try
            {
                await foreach (var msg in conn.Outbox.Reader.ReadAllAsync(ct))
                    await conn.Socket.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, ct);
            }
            catch
            {
                // Verbindung weg – HandleClientAsync räumt auf
            }
        }

        private async Task ReceiveLoopAsync(Connection conn, CancellationToken ct)
        {
            var buf = new byte[8192];
            using var msg = new MemoryStream();
            while (conn.Socket.State == WebSocketState.Open)
            {
                var r = await conn.Socket.ReceiveAsync(buf.AsMemory(), ct);
                if (r.MessageType == WebSocketMessageType.Close) return;
                msg.Write(buf, 0, r.Count);
                if (!r.EndOfMessage)
                {
                    if (msg.Length > 1 << 20) return; // so groß wird keine Meldung
                    continue;
                }
                if (r.MessageType == WebSocketMessageType.Text)
                {
                    try { Handle(conn, Encoding.UTF8.GetString(msg.GetBuffer(), 0, (int)msg.Length)); }
                    catch (Exception ex) { Logger.Error($"YT-Bridge Meldung: {ex.Message}"); }
                }
                msg.SetLength(0);
            }
        }

        /// <summary>
        /// Meldungen der Erweiterung: hello | state (Tab-Zustand) | res (Antwort auf einen Befehl) | gone (Tab ohne Player)
        /// | error (Skriptfehler im Tab → Protokoll) | ping (Chrome hält damit seinen Service Worker wach, wird ignoriert).
        /// Tabs, Play/Pause und Verbindung landen in knob_service.log.
        /// Solange ein eigener Befehl unterwegs ist, überschreibt eine ältere Meldung den Cache nicht –
        /// sonst springt der Wert bei schnellem Drehen zurück.
        /// </summary>
        private void Handle(Connection conn, string json)
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            string type = r.TryGetProperty("type", out var ty) ? ty.GetString() ?? "" : "";
            if (type == "hello")
            {
                // Alle Chromium-Browser haben chrome-extension:// – die Erweiterung sagt, ob es Opera ist
                if (conn.Browser == Chrome && r.TryGetProperty("browser", out var bn) && bn.ValueKind == JsonValueKind.String && bn.GetString() == Opera)
                    lock (_lock) conn.Browser = Opera;
                Logger.Info($"YT-Bridge: {DisplayName(conn.Browser)}-Erweiterung verbunden");
                return;
            }
            string name = DisplayName(conn.Browser);
            if (type == "unknown")
            {
                // Testphase: Video auf einer Seite, die die Erweiterung nicht kennt (z. B. neue VOE-Adresse)
                Logger.Info($"Browser-Bridge {name}: Video auf nicht erkannter Seite {(r.TryGetProperty("host", out var h) ? h.GetString() : "?")}"
                            + (r.TryGetProperty("frame", out var fr) && fr.ValueKind == JsonValueKind.True ? " (iframe)" : ""));
                return;
            }
            if (!r.TryGetProperty("tabId", out var tid) || !tid.TryGetInt32(out int tabId)) return;
            if (type == "error")
            {
                Logger.Error($"YT-Bridge {name} Tab {tabId}: {(r.TryGetProperty("message", out var em) ? em.GetString() : "?")}");
                return;
            }

            string? note = null; // Protokoll erst nach dem Lock schreiben
            lock (_lock)
            {
                // Antwort auf einen Sprung: „seeked“ + neue Position/Dauer; fehlt „seeked“ (Erweiterung vor 1.2) oder
                // ist es false (Livestream, Netflix), springt KnobFlyout über Windows
                if (type == "res" && r.TryGetProperty("rid", out var sr) && sr.TryGetInt32(out int seekRid) && _seeks.Remove(seekRid, out var seek))
                {
                    bool seeked = r.TryGetProperty("seeked", out var sk) && sk.ValueKind == JsonValueKind.True
                                  && r.TryGetProperty("pos", out var sp) && sp.TryGetDouble(out _);
                    if (seeked)
                        seek.TrySetResult((r.GetProperty("pos").GetDouble(), r.TryGetProperty("dur", out var sd) && sd.TryGetDouble(out double dur) ? dur : 0));
                    else
                    {
                        seek.TrySetResult(null);
                        note = "Skip auf dieser Seite nicht möglich, springe über Windows";
                    }
                }
                var tabs = conn.Tabs;
                if (type == "gone" || (type == "res" && !(r.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)))
                {
                    if (tabs.Remove(tabId)) note = $"Tab {tabId} ohne Player (geschlossen oder andere Seite)";
                }
                else if (type == "state" || type == "res")
                {
                    bool isNew = !tabs.TryGetValue(tabId, out var t);
                    if (isNew) tabs[tabId] = t = new Tab();
                    note = Apply(r, type, t!, tabId);
                    if (isNew) note = $"Tab {tabId} mit {t!.Site}-Player gefunden" + (note == null ? "" : "; " + note);
                }
            }
            if (note != null) Logger.Info($"YT-Bridge {name}: {note}");
        }

        /// <summary>Zustand aus einer state-/res-Meldung übernehmen (unter _lock). Liefert eine Protokollzeile bei Play/Pause.</summary>
        private static string? Apply(JsonElement r, string type, Tab t, int tabId)
        {
            if (type == "res")
            {
                int rid = r.TryGetProperty("rid", out var ri) && ri.TryGetInt32(out int v) ? v : 0;
                t.AnsweredRid = Math.Max(t.AnsweredRid, rid);
                if (rid != t.LastRid) return null; // ältere Antwort, der nächste Befehl ist schon unterwegs
            }

            if (t.AnsweredRid >= t.LastRid)
            {
                if (r.TryGetProperty("volume", out var vol) && vol.TryGetDouble(out double d))
                    t.Volume = Math.Clamp((int)Math.Round(d), 0, 100);
                if (r.TryGetProperty("muted", out var mu)) t.Muted = mu.ValueKind == JsonValueKind.True;
            }
            if (r.TryGetProperty("title", out var ti) && ti.ValueKind == JsonValueKind.String) t.Title = ti.GetString() ?? "";
            if (r.TryGetProperty("site", out var si) && si.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(si.GetString())) t.Site = si.GetString()!;
            bool playing = r.TryGetProperty("playing", out var pl) && pl.ValueKind == JsonValueKind.True;
            string? note = playing == t.Playing ? null
                : $"Tab {tabId} {(playing ? "spielt" : "pausiert")} ({t.Site}): {t.Title} (Lautstärke {t.Volume}{(t.Muted ? ", stumm" : "")})";
            if (playing && !t.Playing) t.PlayingSince = DateTime.UtcNow;
            t.Playing = playing;
            return note;
        }

        public void Dispose() => Stop();
    }
}
