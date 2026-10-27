using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using KnobFlyout.Config;

namespace KnobFlyout.Audio
{
    /// <summary>Eine einzelne Audio-Session, entspricht dem Tupel (name, state, control) aus get_sessions().</summary>
    public class AudioSessionInfo
    {
        public string ProcessName { get; }
        public AudioSessionState State { get; }
        public AudioSessionControl Control { get; }

        public AudioSessionInfo(string processName, AudioSessionState state, AudioSessionControl control)
        {
            ProcessName = processName;
            State = state;
            Control = control;
        }
    }

    /// <summary>
    /// Ersetzt den pycaw/ISimpleAudioVolume-Teil von knob_service.py durch NAudio (WASAPI).
    /// Session-Scan wird wie im Original 2s gecacht. Das Audio-Gerät bleibt so lange offen,
    /// wie seine Sessions im Cache liegen.
    /// </summary>
    public class SessionVolumeController : IDisposable
    {
        private const double CacheTtlSeconds = 2.0;

        private readonly object _cacheLock = new();
        private List<AudioSessionInfo> _cache = new();
        private DateTime _cacheTime = DateTime.MinValue;
        private MMDeviceEnumerator? _enumerator;
        private MMDevice? _device;

        /// <summary>Entspricht "_sess_cache_time = 0.0".</summary>
        public void InvalidateCache()
        {
            lock (_cacheLock) { _cacheTime = DateTime.MinValue; }
        }

        /// <summary>Entspricht get_sessions().</summary>
        public List<AudioSessionInfo> GetSessions()
        {
            lock (_cacheLock)
            {
                if ((DateTime.UtcNow - _cacheTime).TotalSeconds < CacheTtlSeconds && _cache.Count > 0)
                    return new List<AudioSessionInfo>(_cache);
            }

            var result = new List<AudioSessionInfo>();
            MMDeviceEnumerator? enumerator = null;
            MMDevice? device = null;
            try
            {
                enumerator = new MMDeviceEnumerator();
                device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var sessions = device.AudioSessionManager.Sessions;

                for (int i = 0; i < sessions.Count; i++)
                {
                    try
                    {
                        var session = sessions[i];
                        int pid = (int)session.GetProcessID;
                        if (pid == 0) continue;

                        string name;
                        try
                        {
                            using var proc = Process.GetProcessById(pid);
                            name = proc.ProcessName.ToLowerInvariant() + ".exe";
                        }
                        catch
                        {
                            continue;
                        }

                        result.Add(new AudioSessionInfo(name, session.State, session));
                    }
                    catch
                    {
                        // Einzelne Session übersprungen (wie "except: pass" im Original)
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"GetSessions: {ex.Message}");
                DisposeQuietly(device);
                DisposeQuietly(enumerator);
                device = null;
                enumerator = null;
                result.Clear();
            }

            MMDeviceEnumerator? oldEnumerator;
            MMDevice? oldDevice;
            lock (_cacheLock)
            {
                oldEnumerator = _enumerator;
                oldDevice = _device;
                _enumerator = enumerator;
                _device = device;
                _cache = result;
                _cacheTime = DateTime.UtcNow;
            }
            DisposeQuietly(oldDevice);
            DisposeQuietly(oldEnumerator);

            return new List<AudioSessionInfo>(result);
        }

        /// <summary>Entspricht get_targets() (ohne _session_exclude, das kommt mit den Assign-Karten).</summary>
        public List<AudioSessionInfo> GetTargets(AppConfig cfg)
        {
            var sess = GetSessions();
            var exc = cfg.Exclude.Select(e => e.ToLowerInvariant()).ToList();
            bool Ok(string n) => !exc.Any(e => n.Contains(e));

            var pa = ProfileApps(cfg, cfg.ActiveProfile);

            if (cfg.ProfileMode == "assigned")
            {
                // Alle zugewiesenen Apps des aktiven Profils – egal ob sie gerade Sound abspielen
                return sess.Where(s => Ok(s.ProcessName) && (pa.Count == 0 || pa.Any(p => s.ProcessName.Contains(p)))).ToList();
            }

            // "active": nur Apps, die gerade aktiv Sound abspielen
            if (pa.Count == 0) return new List<AudioSessionInfo>();
            return sess.Where(s => s.State == AudioSessionState.AudioSessionStateActive
                                   && Ok(s.ProcessName)
                                   && pa.Any(p => s.ProcessName.Contains(p))).ToList();
        }

        /// <summary>Entspricht _profile_has_active().</summary>
        public bool ProfileHasActive(string key, AppConfig cfg, List<AudioSessionInfo> sess)
        {
            var pa = ProfileApps(cfg, key);
            if (pa.Count == 0) return false;
            var exc = cfg.Exclude.Select(e => e.ToLowerInvariant()).ToList();
            return sess.Any(s => s.State == AudioSessionState.AudioSessionStateActive
                                 && !exc.Any(e => s.ProcessName.Contains(e))
                                 && pa.Any(p => s.ProcessName.Contains(p)));
        }

        /// <summary>
        /// Entspricht change_volume(delta_sign). "steps" = akkumulierte Drehungen (±1 pro Rast).
        /// Sessions, deren Name "spotify" enthält, laufen bei aktiver Spotify-API über deren
        /// eigenen Lautstärke-Regler (step_spotify statt step) statt über WASAPI – 1:1 wie im
        /// Python-Original (change_volume()).
        /// </summary>
        public List<(string Name, int Value)> ChangeVolume(AppConfig cfg, int steps, SpotifyController? spotify = null)
        {
            var results = new List<(string Name, int Value)>();
            foreach (var t in GetTargets(cfg))
            {
                if (spotify != null && t.ProcessName.Contains("spotify", StringComparison.OrdinalIgnoreCase) && spotify.IsActive(cfg))
                {
                    var nv = spotify.ChangeVolume(steps * cfg.StepSpotify);
                    results.Add((t.ProcessName, nv ?? SafeReadPercent(t)));
                    continue;
                }

                try
                {
                    var vol = t.Control.SimpleAudioVolume;
                    float cur = vol.Volume;
                    float next = Math.Clamp(cur + steps * cfg.Step / 100f, 0f, 1f);
                    vol.Volume = next;
                    results.Add((t.ProcessName, (int)Math.Round(next * 100)));
                }
                catch (Exception ex)
                {
                    Logger.Error($"ChangeVolume {t.ProcessName}: {ex.Message}");
                }
            }
            return results;
        }

        private static int SafeReadPercent(AudioSessionInfo t)
        {
            try { return (int)Math.Round(t.Control.SimpleAudioVolume.Volume * 100); }
            catch { return 0; }
        }

        /// <summary>Entspricht do_toggle_mute(): jede Ziel-Session wird einzeln umgeschaltet, Spotify-Sessions über die API.</summary>
        public List<(string Name, bool Muted)> ToggleMute(AppConfig cfg, SpotifyController? spotify = null)
        {
            var results = new List<(string Name, bool Muted)>();
            foreach (var t in GetTargets(cfg))
            {
                if (spotify != null && t.ProcessName.Contains("spotify", StringComparison.OrdinalIgnoreCase) && spotify.IsActive(cfg))
                {
                    var m = spotify.ToggleMute();
                    if (m.HasValue) { results.Add((t.ProcessName, m.Value)); continue; }
                }

                try
                {
                    var vol = t.Control.SimpleAudioVolume;
                    bool muted = vol.Mute;
                    vol.Mute = !muted;
                    results.Add((t.ProcessName, !muted));
                }
                catch (Exception ex)
                {
                    Logger.Error($"ToggleMute {t.ProcessName}: {ex.Message}");
                }
            }
            return results;
        }

        private static List<string> ProfileApps(AppConfig cfg, string? key)
        {
            if (key != null && cfg.Profiles.TryGetValue(key, out var apps))
                return apps.Select(a => a.ToLowerInvariant()).ToList();
            return new List<string>();
        }

        private static void DisposeQuietly(IDisposable? d)
        {
            try { d?.Dispose(); } catch { }
        }

        public void Dispose()
        {
            lock (_cacheLock)
            {
                DisposeQuietly(_device);
                DisposeQuietly(_enumerator);
                _device = null;
                _enumerator = null;
                _cache = new List<AudioSessionInfo>();
            }
        }
    }
}
