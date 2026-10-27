using System;
using System.Collections.Concurrent;
using System.Threading;

namespace KnobFlyout.Audio
{
    /// <summary>
    /// Entspricht _audio_worker()/_audio_q aus knob_service.py: ein dedizierter Thread für alle
    /// Audio-Operationen, damit der Hook-Callback sofort zurückkehrt und WASAPI-Aufrufe nicht
    /// parallel laufen. Schnelle Knob-Drehungen werden akkumuliert (_vol_accum).
    /// </summary>
    public sealed class AudioWorker : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new();
        private readonly Thread _thread;
        private readonly object _accumLock = new();
        private int _volAccum;

        public AudioWorker()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "AudioWorker" };
            _thread.Start();
        }

        /// <summary>Akkumuliert die Drehrichtung; der erste ausgeführte Eintrag verarbeitet die Summe.</summary>
        public void EnqueueVolume(int sign, Action<int> apply)
        {
            lock (_accumLock) { _volAccum += sign; }
            Enqueue(() =>
            {
                int d;
                lock (_accumLock) { d = _volAccum; _volAccum = 0; }
                if (d != 0) apply(d);
            });
        }

        public void Enqueue(Action action)
        {
            try { _queue.Add(action); }
            catch (InvalidOperationException) { /* bereits beendet */ }
        }

        private void Run()
        {
            try
            {
                foreach (var action in _queue.GetConsumingEnumerable())
                {
                    try { action(); }
                    catch (Exception ex) { Logger.Error($"audio_worker: {ex.Message}"); }
                }
            }
            catch (OperationCanceledException) { }
        }

        public void Dispose() => _queue.CompleteAdding();
    }
}
