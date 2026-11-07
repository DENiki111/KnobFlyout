using System;
using System.Text.Json;
using KnobFlyout.Config;
using KnobFlyout.Osd;

namespace KnobFlyout.Settings
{
    /// <summary>
    /// „Auf dem Bildschirm“ in „Design anpassen“: Lautstärke-Karte und Flyout mit dem Entwurf echt an ihrer Stelle auf dem Bildschirm,
    /// ohne Ausblenden, bis Close. Show baut nur neu, wenn sich der Schlüssel (Entwurf + Schalter) geändert hat.
    /// </summary>
    internal sealed class ScreenPreview
    {
        private OsdCard? _card;
        private MediaOsd? _flyout;
        private Action? _flyoutDispose;
        private AppConfig? _cfg;
        private string _key = "";

        public bool IsOpen => _card != null || _flyout != null;

        /// <param name="media">Echte Wiedergabe fürs Flyout (Live-Flyout) oder null = Beispiel.</param>
        public void Show(AppConfig cfg, Media.MediaController? media, string name, int pct, bool muted, string key)
        {
            if (IsOpen && key == _key) return;
            Close();
            _key = key;
            try
            {
                // Kopie: Die Karte soll klick-durchlässig sein (kein Ziehen, kein Wegklicken), der Entwurf bleibt unberührt.
                var c = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(cfg)) ?? new AppConfig();
                c.SnapEnabled = false;
                c.ClickToDismiss = false;
                _cfg = c;
                _flyout = MediaOsd.CreateScreen(c, media, out var dispose);
                _flyoutDispose = dispose;
                _card = OsdCard.CreateVolume(c, name);
                _card.UpdateVolume(name, pct, muted);
                Place();
                _card.Show();
                _card.FadeIn();
                _flyout.Show();
                _flyout.SizeChanged += (_, _) => Place(); // echte Wiedergabe: andere Höhe → Stelle neu rechnen
            }
            catch (Exception ex)
            {
                Logger.Error($"Design auf dem Bildschirm: {ex.Message}");
                Close();
            }
        }

        /// <summary>Lautstärke der Karte (Live-OSD: folgt dem echten Knopf).</summary>
        public void SetVolume(string name, int pct, bool muted)
        {
            try { _card?.UpdateVolume(name, pct, muted); }
            catch (Exception ex) { Logger.Error($"Design auf dem Bildschirm: {ex.Message}"); }
        }

        private void Place()
        {
            if (_cfg == null || _flyout == null) return;
            double height = _flyout.ActualHeight > 0 ? _flyout.ActualHeight : _flyout.MeasureHeight();
            var (osd, media) = Placement.Layout(_cfg, height);
            if (_card != null)
            {
                _card.Left = osd.X;
                _card.Top = osd.Y;
            }
            _flyout.Left = media.X;
            _flyout.Top = media.Y;
        }

        public void Close()
        {
            _key = "";
            _flyoutDispose?.Invoke();
            _flyoutDispose = null;
            _flyout = null;
            try { _card?.Close(); } catch { }
            _card = null;
            _cfg = null;
        }
    }
}
