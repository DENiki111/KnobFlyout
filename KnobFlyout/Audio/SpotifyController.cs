using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using KnobFlyout.Config;

namespace KnobFlyout.Audio
{
    internal sealed class SpotifyTokenCache
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
        [JsonPropertyName("expires_at")] public long ExpiresAtUnix { get; set; }
        [JsonPropertyName("scope")] public string Scope { get; set; } = "";
    }

    /// <summary>
    /// Ersetzt SpotifyController (spotipy) aus knob_service.py: OAuth-Authorization-Code-Flow
    /// gegen die echte Spotify-Web-API, Token-Cache als .spotify_cache neben der exe, Lautstärke
    /// mit sofort gecachtem Wert + gedrosseltem echten API-Call (Senderate passt sich an, siehe ScheduleFlush),
    /// Hintergrund-Abgleich alle 10 s, nur wenn Spotify läuft. redirect_uri ist wie im Python-Original fest verdrahtet
    /// ("http://127.0.0.1:5000/callback/" inkl. Trailing-Slash wegen HttpListener) – dieselbe
    /// Adresse muss im Spotify-Dashboard als Redirect-URI eingetragen sein.
    /// </summary>
    public sealed class SpotifyController : IDisposable
    {
        public const string RedirectUri = "http://127.0.0.1:5000/callback/";
        private const string Scope = "user-modify-playback-state user-read-playback-state";
        private static readonly string CacheFile = Path.Combine(AppContext.BaseDirectory, ".spotify_cache");

        private readonly HttpClient _http = new();
        private readonly object _lock = new();
        private readonly Timer _syncTimer;
        private readonly Timer _debounceTimer;

        private SpotifyTokenCache? _token;
        private int _cachedVol = 50;
        private int _lastVolBeforeMute = 50;
        private int _pendingVol = 50;

        public SpotifyController()
        {
            LoadCacheFromDisk();
            _syncTimer = new Timer(_ => SyncLoopTick(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
            _debounceTimer = new Timer(_ => _ = FlushVolumeAsync(), null, Timeout.Infinite, Timeout.Infinite);
        }

        private void LoadCacheFromDisk()
        {
            try
            {
                if (!File.Exists(CacheFile)) { _token = null; return; }
                _token = JsonSerializer.Deserialize<SpotifyTokenCache>(File.ReadAllText(CacheFile));
            }
            catch (Exception ex)
            {
                Logger.Error($"Spotify Cache lesen: {ex.Message}");
                _token = null;
            }
        }

        private void SaveCacheToDisk()
        {
            try
            {
                if (_token == null) return;
                File.WriteAllText(CacheFile, JsonSerializer.Serialize(_token));
            }
            catch (Exception ex)
            {
                Logger.Error($"Spotify Cache schreiben: {ex.Message}");
            }
        }

        public bool HasToken { get { lock (_lock) { return _token != null; } } }

        /// <summary>Entspricht is_active(): Token vorhanden UND in der Config aktiviert.</summary>
        public bool IsActive(AppConfig cfg) => cfg.SpotifyUseApi && HasToken;

        public void Disconnect()
        {
            lock (_lock) { _token = null; }
            try { if (File.Exists(CacheFile)) File.Delete(CacheFile); }
            catch (Exception ex) { Logger.Error($"Spotify Disconnect: {ex.Message}"); }
        }

        /// <summary>Entspricht _sp_connect(): Authorize-URL im Browser öffnen, lokal auf den Redirect warten, Code gegen Token tauschen.</summary>
        public async Task<(bool Ok, string Message)> ConnectAsync(string clientId, string clientSecret)
        {
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
                return (false, Loc.T("Bitte zuerst Client ID & Secret eingeben.", "Enter Client ID & Secret first."));

            HttpListener? listener = null;
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add(RedirectUri);
                listener.Start();
            }
            catch (Exception ex)
            {
                Logger.Error($"Spotify Listener: {ex.Message}");
                return (false, Loc.T("Port 5000 konnte nicht geöffnet werden: ", "Could not open port 5000: ") + ex.Message);
            }

            try
            {
                // Zufälliger state: nur der Rückruf genau dieser Anmeldung wird angenommen.
                string state = Guid.NewGuid().ToString("N");
                var authUrl = "https://accounts.spotify.com/authorize" +
                    $"?client_id={Uri.EscapeDataString(clientId)}" +
                    "&response_type=code" +
                    $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
                    $"&scope={Uri.EscapeDataString(Scope)}" +
                    $"&state={state}";

                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(authUrl) { UseShellExecute = true }); }
                catch (Exception ex) { Logger.Error($"Spotify Browser öffnen: {ex.Message}"); }

                var getContextTask = listener.GetContextAsync();
                var completed = await Task.WhenAny(getContextTask, Task.Delay(TimeSpan.FromSeconds(120)));
                if (completed != getContextTask)
                    return (false, Loc.T($"Keine Antwort von Spotify (2 Minuten). Zeigt Spotify „Invalid redirect URI“? Dann im Dashboard genau {RedirectUri} eintragen.",
                        $"No answer from Spotify (2 minutes). Does Spotify show “Invalid redirect URI”? Then enter exactly {RedirectUri} in the dashboard."));

                var ctx = getContextTask.Result;
                string query = ctx.Request.Url?.Query ?? "";
                var code = ParseQueryParam(query, "code");
                var error = ParseQueryParam(query, "error");

                (bool Ok, string Message) result;
                if (!string.IsNullOrEmpty(error))
                    result = (false, error == "access_denied"
                        ? Loc.T("Abgebrochen – bei Spotify nicht zugestimmt.", "Cancelled – not approved at Spotify.")
                        : Loc.T("Spotify meldet: ", "Spotify reports: ") + error);
                else if (ParseQueryParam(query, "state") != state)
                    result = (false, Loc.T("Ungültige Antwort (passt nicht zu dieser Anmeldung) – bitte erneut verbinden.", "Invalid response (does not match this sign-in) – please connect again."));
                else if (string.IsNullOrEmpty(code))
                    result = (false, Loc.T("Kein Code empfangen.", "No code received."));
                else
                    result = await ExchangeCodeAsync(clientId, clientSecret, code);

                // Erst nach dem Token-Tausch antworten, damit die Seite im Browser das echte Ergebnis zeigt.
                await RespondAsync(ctx, result.Ok, result.Message);
                return result;
            }
            catch (Exception ex)
            {
                Logger.Error($"Spotify Connect: {ex.Message}");
                return (false, Loc.T("Fehler: ", "Error: ") + ex.Message);
            }
            finally
            {
                try { listener.Stop(); listener.Close(); } catch { }
            }
        }

        private static string? ParseQueryParam(string query, string name)
        {
            if (query.StartsWith('?')) query = query[1..];
            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2 && Uri.UnescapeDataString(kv[0]) == name)
                    return Uri.UnescapeDataString(kv[1]);
            }
            return null;
        }

        /// <summary>Antwortseite im Browser: Erfolg nur, wenn der Token wirklich gespeichert wurde, sonst Fehler mit Grund.</summary>
        private static async Task RespondAsync(HttpListenerContext ctx, bool ok, string message)
        {
            try
            {
                string title = ok
                    ? Loc.T("&#10003; Verbunden! Du kannst dieses Fenster schlie&szlig;en.", "&#10003; Connected! You can close this window.")
                    : Loc.T("Verbinden fehlgeschlagen", "Connection failed");
                string html = "<html><head><meta charset='utf-8'></head><body style='background:#0d0d1c;color:" + (ok ? "#4ade80" : "#f87171") +
                    ";font-family:sans-serif;display:flex;flex-direction:column;align-items:center;justify-content:center;height:100vh;margin:0'>" +
                    $"<h2>{title}</h2>" + (ok ? "" : $"<p style='color:#cccccc'>{WebUtility.HtmlEncode(message)}</p>") + "</body></html>";
                var buf = Encoding.UTF8.GetBytes(html);
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = buf.Length;
                await ctx.Response.OutputStream.WriteAsync(buf);
                ctx.Response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Logger.Error($"Spotify Antwortseite: {ex.Message}");
            }
        }

        /// <summary>Fehlerantwort des Token-Endpunkts in eine verständliche Meldung übersetzen.</summary>
        private static string TokenError(string body)
        {
            string? error = null, description = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) error = e.GetString();
                if (doc.RootElement.TryGetProperty("error_description", out var d) && d.ValueKind == JsonValueKind.String) description = d.GetString();
            }
            catch { /* kein JSON */ }
            if (error == "invalid_client")
                return Loc.T("Client ID oder Client Secret ist falsch.", "Client ID or client secret is wrong.");
            if (error == "invalid_grant" && description != null && description.Contains("redirect", StringComparison.OrdinalIgnoreCase))
                return Loc.T($"Redirect-URI stimmt nicht – im Dashboard genau {RedirectUri} eintragen.", $"Redirect URI does not match – enter exactly {RedirectUri} in the dashboard.");
            if (error == "invalid_grant")
                return Loc.T("Anmeldecode ungültig oder abgelaufen – bitte erneut verbinden.", "Sign-in code invalid or expired – please connect again.");
            return Loc.T("Token-Fehler: ", "Token error: ") + (description ?? error ?? body);
        }

        /// <summary>
        /// „Testen“: Token holen (läuft er ab, wird er erneuert) und das Spotify-Konto abfragen. Zeigt, ob Verbinden und
        /// Erneuern wirklich funktionieren.
        /// </summary>
        public async Task<(bool Ok, string Message)> TestAsync(AppConfig cfg)
        {
            if (!HasToken) return (false, Loc.T("Nicht verbunden – zuerst „Verbinden“.", "Not connected – use “Connect” first."));
            try
            {
                var accessToken = await EnsureAccessTokenAsync(cfg);
                if (accessToken == null) return (false, Loc.T("Kein Token vorhanden – bitte verbinden.", "No token – please connect."));
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me");
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                using var resp = await _http.SendAsync(req);
                var body = await resp.Content.ReadAsStringAsync();
                if (resp.StatusCode == HttpStatusCode.Unauthorized)
                    return (false, Loc.T("Token ungültig oder abgelaufen – bitte neu verbinden.", "Token invalid or expired – please connect again."));
                if (resp.StatusCode == HttpStatusCode.Forbidden)
                    return (false, Loc.T("Spotify verweigert den Zugriff – im Dashboard unter „User Management“ dein Spotify-Konto eintragen.",
                        "Spotify denies access – add your Spotify account under “User Management” in the dashboard."));
                if (!resp.IsSuccessStatusCode)
                {
                    Logger.Error($"Spotify Test: {resp.StatusCode} {body}");
                    return (false, $"Spotify: {(int)resp.StatusCode} {resp.ReasonPhrase}");
                }
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                string name = root.TryGetProperty("display_name", out var dn) && dn.ValueKind == JsonValueKind.String ? dn.GetString() ?? ""
                    : root.TryGetProperty("id", out var idp) && idp.ValueKind == JsonValueKind.String ? idp.GetString() ?? "" : "";
                return (true, Loc.T($"Funktioniert – angemeldet als {name}.", $"Works – signed in as {name}."));
            }
            catch (Exception ex)
            {
                Logger.Error($"Spotify Test: {ex.Message}");
                return (false, Loc.T("Fehler: ", "Error: ") + ex.Message);
            }
        }

        private async Task<(bool Ok, string Message)> ExchangeCodeAsync(string clientId, string clientSecret, string code)
        {
            try
            {
                var form = new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = RedirectUri,
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                };
                using var resp = await _http.PostAsync("https://accounts.spotify.com/api/token", new FormUrlEncodedContent(form));
                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) { Logger.Error($"Spotify Token-Tausch: {resp.StatusCode} {body}"); return (false, TokenError(body)); }

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var token = new SpotifyTokenCache
                {
                    AccessToken = root.GetProperty("access_token").GetString() ?? "",
                    RefreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? "" : "",
                    Scope = root.TryGetProperty("scope", out var sc) ? sc.GetString() ?? "" : "",
                    ExpiresAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + root.GetProperty("expires_in").GetInt32() - 30,
                };

                lock (_lock) { _token = token; }
                SaveCacheToDisk();
                _ = FetchRealVolumeAsync();
                return (true, Loc.T("Verbunden – Token gespeichert!", "Connected – token saved!"));
            }
            catch (Exception ex)
            {
                Logger.Error($"Spotify Token-Tausch: {ex.Message}");
                return (false, Loc.T("Token-Fehler: ", "Token error: ") + ex.Message);
            }
        }

        /// <summary>Erneuert den Access Token per Refresh Token, falls er in &lt;=30s abläuft.</summary>
        private async Task<string?> EnsureAccessTokenAsync(AppConfig cfg)
        {
            SpotifyTokenCache? token;
            lock (_lock) { token = _token; }
            if (token == null) return null;
            if (token.ExpiresAtUnix > DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return token.AccessToken;
            if (string.IsNullOrEmpty(token.RefreshToken) || string.IsNullOrEmpty(cfg.SpotifyClientId)) return token.AccessToken;

            try
            {
                var form = new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = token.RefreshToken,
                    ["client_id"] = cfg.SpotifyClientId,
                    ["client_secret"] = cfg.SpotifyClientSecret,
                };
                using var resp = await _http.PostAsync("https://accounts.spotify.com/api/token", new FormUrlEncodedContent(form));
                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) { Logger.Error($"Spotify Refresh: {resp.StatusCode} {body}"); return token.AccessToken; }

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                token.AccessToken = root.GetProperty("access_token").GetString() ?? token.AccessToken;
                if (root.TryGetProperty("refresh_token", out var rt2)) token.RefreshToken = rt2.GetString() ?? token.RefreshToken;
                token.ExpiresAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + root.GetProperty("expires_in").GetInt32() - 30;

                lock (_lock) { _token = token; }
                SaveCacheToDisk();
                return token.AccessToken;
            }
            catch (Exception ex)
            {
                Logger.Error($"Spotify Refresh: {ex.Message}");
                return token.AccessToken;
            }
        }

        // ── Senderate ──
        // Spotify zählt Anfragen in einem gleitenden 30-s-Fenster und nennt keine feste Grenze. Deshalb passt sich der
        // Abstand selbst an: beim Drehen höchstens alle MinIntervalMs eine Anfrage (Wert folgt also schon während des
        // Drehens), immer nur eine gleichzeitig und immer der neueste Wert. Meldet Spotify 429, wird bis Retry-After
        // pausiert und der Abstand verdoppelt; ohne weitere 429 sinkt er langsam wieder. Gedreht wird während einer
        // Pause trotzdem: Die Anzeige zeigt sofort den neuen Wert, gesendet wird der letzte Stand danach.
        private const int CoalesceMs = 30;      // Rasten eines schnellen Drehs zusammenfassen
        private const int MinIntervalMs = 200;  // schnellster Abstand zwischen zwei Anfragen (Start zu Start, also höchstens 5/s)
        private const int MaxIntervalMs = 1600;
        private int _intervalMs = MinIntervalMs;
        private DateTime _flushAt = DateTime.MinValue; // geplanter Sendezeitpunkt
        private int _sentVol = 50; // = _pendingVol am Anfang: nichts zu senden
        private bool _sending;
        private DateTime _lastSendAt = DateTime.MinValue;
        private DateTime _blockedUntil = DateTime.MinValue;
        private DateTime _last429 = DateTime.MinValue;
        private DateTime _lastChange = DateTime.MinValue;
        private long _changeSeq;
        private AppConfig? _cfg;
        private long _cfgStamp;

        /// <summary>Entspricht change_volume(delta): sofort gecachter Wert, echter API-Call gedrosselt (siehe Senderate).</summary>
        public int? ChangeVolume(int delta)
        {
            if (!HasToken) return null;
            int updated;
            lock (_lock)
            {
                updated = Math.Clamp(_cachedVol + delta, 0, 100);
                _cachedVol = updated;
                _pendingVol = updated;
                _lastChange = DateTime.UtcNow;
                _changeSeq++;
            }
            ScheduleFlush();
            return updated;
        }

        /// <summary>Nächste Anfrage planen: frühestens nach dem Abstand zur letzten bzw. nach einer 429-Pause.</summary>
        private void ScheduleFlush()
        {
            int delay;
            lock (_lock)
            {
                if (_sending) return; // die laufende Anfrage plant danach selbst nach
                var now = DateTime.UtcNow;
                if (_flushAt != DateTime.MinValue) return; // schon geplant (Timer kann etwas später feuern): weitere Rasten verschieben den Termin nicht, sonst käme beim schnellen Drehen nie etwas an
                double wait = Math.Max(CoalesceMs, (_lastSendAt.AddMilliseconds(_intervalMs) - now).TotalMilliseconds);
                wait = Math.Max(wait, (_blockedUntil - now).TotalMilliseconds);
                delay = (int)Math.Ceiling(wait);
                _flushAt = now.AddMilliseconds(delay);
            }
            try { _debounceTimer.Change(delay, Timeout.Infinite); }
            catch (ObjectDisposedException) { /* App wird beendet */ }
        }

        private AppConfig CurrentConfig()
        {
            if (_cfg == null || ConfigManager.ChangedSince(ref _cfgStamp)) _cfg = ConfigManager.Load();
            return _cfg;
        }

        private async Task FlushVolumeAsync()
        {
            int target;
            lock (_lock)
            {
                _flushAt = DateTime.MinValue;
                if (_sending || _pendingVol == _sentVol) return;
                target = _pendingVol;
                _sending = true;
                _lastSendAt = DateTime.UtcNow; // Abstand zählt von Start zu Start
            }
            bool again = false;
            try
            {
                var accessToken = await EnsureAccessTokenAsync(CurrentConfig());
                if (accessToken == null) { lock (_lock) _sentVol = target; return; }

                using var req = new HttpRequestMessage(HttpMethod.Put, $"https://api.spotify.com/v1/me/player/volume?volume_percent={target}");
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                using var resp = await _http.SendAsync(req);
                var now = DateTime.UtcNow;
                lock (_lock)
                {
                    if ((int)resp.StatusCode == 429)
                    {
                        double retry = resp.Headers.RetryAfter?.Delta?.TotalSeconds
                            ?? (resp.Headers.RetryAfter?.Date is DateTimeOffset d ? (d - DateTimeOffset.UtcNow).TotalSeconds : 1);
                        retry = Math.Clamp(retry, 0.5, 60);
                        _blockedUntil = now.AddSeconds(retry);
                        _intervalMs = Math.Min(MaxIntervalMs, _intervalMs * 2);
                        _last429 = now;
                        Logger.Info($"Spotify bremst (429): {retry:0.#} s Pause, danach Abstand {_intervalMs} ms");
                    }
                    else if (resp.IsSuccessStatusCode)
                    {
                        _sentVol = target;
                        // 30 s ohne 429: Abstand langsam wieder verkürzen
                        if (_intervalMs > MinIntervalMs && now - _last429 > TimeSpan.FromSeconds(30))
                            _intervalMs = Math.Max(MinIntervalMs, _intervalMs - 50);
                    }
                    else
                    {
                        _sentVol = target; // anderer Fehler (z. B. kein aktives Gerät): nicht endlos wiederholen
                        Logger.Error($"Spotify Volume-Flush: {resp.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                lock (_lock) _sentVol = target; // z. B. kein Netz: nicht endlos wiederholen, der nächste Dreh sendet neu
                Logger.Error($"Spotify Volume-Flush: {ex.Message}");
            }
            finally
            {
                lock (_lock)
                {
                    _sending = false;
                    again = _pendingVol != _sentVol;
                }
            }
            if (again) ScheduleFlush();
        }

        /// <summary>Entspricht toggle_mute().</summary>
        public bool? ToggleMute()
        {
            if (!HasToken) return null;
            int cur;
            lock (_lock) { cur = _cachedVol; }
            if (cur > 0)
            {
                lock (_lock) { _lastVolBeforeMute = cur; }
                ChangeVolume(-cur);
                return true;
            }
            int restore;
            lock (_lock) { restore = _lastVolBeforeMute; }
            ChangeVolume(restore - cur);
            return false;
        }

        /// <summary>
        /// Abgleich mit dem echten Spotify-Wert (falls in Spotify selbst geregelt wurde). Nur wenn Spotify hier läuft,
        /// nicht kurz nach eigenem Drehen (sonst springt der Wert zurück) und nicht während einer 429-Pause.
        /// </summary>
        private void SyncLoopTick()
        {
            if (!HasToken) return;
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                if (_sending || _pendingVol != _sentVol || now - _lastChange < TimeSpan.FromSeconds(4) || now < _blockedUntil) return;
            }
            if (!SpotifyRunning()) return;
            _ = FetchRealVolumeAsync();
        }

        private static bool SpotifyRunning()
        {
            var procs = System.Diagnostics.Process.GetProcessesByName("Spotify");
            foreach (var p in procs) p.Dispose();
            return procs.Length > 0;
        }

        private async Task FetchRealVolumeAsync()
        {
            try
            {
                var cfg = CurrentConfig();
                if (!cfg.SpotifyUseApi) return;
                long seq;
                lock (_lock) seq = _changeSeq;
                var accessToken = await EnsureAccessTokenAsync(cfg);
                if (accessToken == null) return;

                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me/player");
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                using var resp = await _http.SendAsync(req);
                if ((int)resp.StatusCode == 429)
                {
                    double retry = Math.Clamp(resp.Headers.RetryAfter?.Delta?.TotalSeconds ?? 5, 1, 60);
                    lock (_lock) _blockedUntil = DateTime.UtcNow.AddSeconds(retry);
                    return;
                }
                if (resp.StatusCode == HttpStatusCode.NoContent || !resp.IsSuccessStatusCode) return;

                var body = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("device", out var device) &&
                    device.TryGetProperty("volume_percent", out var vp) && vp.ValueKind == JsonValueKind.Number)
                {
                    lock (_lock)
                    {
                        // Inzwischen gedreht: der eigene Wert ist neuer als die Antwort
                        if (seq != _changeSeq || _sending) return;
                        _cachedVol = _pendingVol = _sentVol = vp.GetInt32();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Spotify Sync: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _syncTimer.Dispose();
            _debounceTimer.Dispose();
            _http.Dispose();
        }
    }
}
