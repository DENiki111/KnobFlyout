using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using KnobEarTrumpet.Config;

namespace KnobEarTrumpet.Osd
{
    /// <summary>Ein Bildschirm: Gerätename (stabil über Auflösungswechsel), Arbeitsbereich in WPF-Einheiten, Pixelgröße.</summary>
    internal sealed record MonitorArea(string Device, Rect Work, int PixelWidth, int PixelHeight, bool Primary);

    /// <summary>
    /// Zentrale Platzierung von OSD und Media-Flyout:
    /// - Ecken-Voreinstellungen (osd_corner_preset / media_corner_preset) relativ zum Arbeitsbereich
    ///   des Hauptbildschirms (ohne Taskleiste) – passen sich damit automatisch jeder Auflösung an.
    /// - Clamp(): hält ein Fenster vollständig im Arbeitsbereich des nächstgelegenen Bildschirms.
    /// - AvoidOverlap(): schiebt ein Fenster neben ein anderes, statt es zu überdecken.
    /// - Remap(): rechnet gespeicherte Positionen nach Auflösungs-/Monitorwechsel um (gleicher Abstand
    ///   zum nächsten Rand bzw. gleiche relative Lage in der Mitte).
    /// Koordinaten sind WPF-Einheiten (Prozess ist system-DPI-aware: Pixel / System-DPI-Faktor).
    /// </summary>
    internal static class Placement
    {
        public const double Margin = 28;       // Abstand zum Bildschirmrand (wie bisher bei "oben rechts")
        public const double BottomMargin = 12; // unten: direkt über der Taskleiste, damit sie sichtbar bleibt
        public const double Gap = 8;

        public static bool IsPreset(string? preset) => !string.IsNullOrEmpty(preset) && preset != "custom";

        // ── Bildschirme ───────────────────────────────────────────────────────

        public static List<MonitorArea> Monitors()
        {
            var list = new List<MonitorArea>();
            double scale = 1.0;
            try { scale = Math.Max(1, GetDpiForSystem()) / 96.0; } catch { /* vor Windows 10 1607 */ }
            var (taskbar, taskbarEdge, autoHide) = Taskbar();
            try
            {
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT bounds, IntPtr data) =>
                {
                    var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                    if (GetMonitorInfo(hMon, ref info))
                    {
                        var w = info.rcWork;
                        var m = info.rcMonitor;
                        // Automatisch ausgeblendete Taskleiste zählt Windows nicht zum belegten Bereich –
                        // trotzdem freihalten, damit OSD/Flyout sie beim Einblenden nicht verdecken.
                        if (autoHide && taskbar.Right > m.Left && taskbar.Left < m.Right && taskbar.Bottom > m.Top && taskbar.Top < m.Bottom)
                        {
                            int th = taskbar.Bottom - taskbar.Top, tw = taskbar.Right - taskbar.Left;
                            switch (taskbarEdge)
                            {
                                case 0: if (w.Left == m.Left) w.Left += tw; break;       // links
                                case 1: if (w.Top == m.Top) w.Top += th; break;          // oben
                                case 2: if (w.Right == m.Right) w.Right -= tw; break;    // rechts
                                default: if (w.Bottom == m.Bottom) w.Bottom -= th; break; // unten
                            }
                        }
                        list.Add(new MonitorArea(info.szDevice ?? "",
                            new Rect(w.Left / scale, w.Top / scale, (w.Right - w.Left) / scale, (w.Bottom - w.Top) / scale),
                            m.Right - m.Left, m.Bottom - m.Top, (info.dwFlags & 1) != 0));
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Logger.Error($"Bildschirme ermitteln: {ex.Message}");
            }
            if (list.Count == 0)
                list.Add(new MonitorArea("", SystemParameters.WorkArea, (int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight, true));
            return list;
        }

        public static Rect PrimaryWork()
        {
            var mons = Monitors();
            return (mons.FirstOrDefault(m => m.Primary) ?? mons[0]).Work;
        }

        /// <summary>Arbeitsbereich des Bildschirms mit diesem Gerätenamen (leer/unbekannt = Hauptbildschirm).</summary>
        public static Rect MonitorWork(string? device)
        {
            var mons = Monitors();
            return (mons.FirstOrDefault(m => !string.IsNullOrEmpty(device) && m.Device == device)
                    ?? mons.FirstOrDefault(m => m.Primary) ?? mons[0]).Work;
        }

        /// <summary>Lesbarer Name für die Auswahl, z.B. "Bildschirm 2 · 1920 × 1080".</summary>
        public static string MonitorLabel(MonitorArea m, int index) =>
            Loc.T("Bildschirm ", "Display ") + (index + 1) + $" · {m.PixelWidth} × {m.PixelHeight}" + (m.Primary ? Loc.T(" (Haupt)", " (main)") : "");

        /// <summary>Arbeitsbereich des Bildschirms, auf dem r (Mitte) liegt – sonst des nächstgelegenen.</summary>
        public static Rect WorkAreaFor(Rect r, List<MonitorArea>? monitors = null)
        {
            var mons = monitors ?? Monitors();
            var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            var hit = mons.FirstOrDefault(m => m.Work.Contains(c));
            if (hit != null) return hit.Work;
            return mons.OrderBy(m => Distance(m.Work, c)).First().Work;
        }

        private static double Distance(Rect r, Point p)
        {
            double dx = Math.Max(Math.Max(r.Left - p.X, 0), p.X - r.Right);
            double dy = Math.Max(Math.Max(r.Top - p.Y, 0), p.Y - r.Bottom);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // ── Platzierung ───────────────────────────────────────────────────────

        /// <summary>
        /// Linke obere Ecke eines Fensters der Größe size am gewählten Platz des Bildschirms (leer = Hauptbildschirm):
        /// 3×3 Plätze top/middle/bottom × left/center/right (Mitte = "center"), wie die Kacheln in den Einstellungen.
        /// </summary>
        public static Point Corner(string preset, Size size, string? device = null)
        {
            var wa = MonitorWork(device);
            double left = wa.Left + Margin, right = wa.Right - Margin - size.Width;
            double top = wa.Top + Margin, bottom = wa.Bottom - BottomMargin - size.Height;
            double centerX = wa.Left + (wa.Width - size.Width) / 2, middleY = wa.Top + (wa.Height - size.Height) / 2;
            return preset switch
            {
                "top_left" => new Point(left, top),
                "top_center" => new Point(centerX, top),
                "middle_left" => new Point(left, middleY),
                "center" => new Point(centerX, middleY),
                "middle_right" => new Point(right, middleY),
                "bottom_left" => new Point(left, bottom),
                "bottom_center" => new Point(centerX, bottom),
                "bottom_right" => new Point(right, bottom),
                _ => new Point(right, top), // "top_right"
            };
        }

        /// <summary>Platz in der mittleren Reihe (Mitte links/Mitte/Mitte rechts)?</summary>
        public static bool IsMiddle(string? preset) => preset == "center" || (preset?.StartsWith("middle") ?? false);

        /// <summary>
        /// Zusammen angezeigt und mittlere Reihe: den ganzen Block (Anker + angehängtes Element) senkrecht
        /// mittig setzen, nicht nur den Anker – sonst hinge der Block unter bzw. über der Bildschirmmitte.
        /// </summary>
        private static Point CenterBlock(Point anchor, string? device, double anchorHeight, double otherHeight, bool otherAbove, double gap)
        {
            var wa = MonitorWork(device);
            double top = wa.Top + (wa.Height - (anchorHeight + gap + otherHeight)) / 2;
            return new Point(anchor.X, otherAbove ? top + otherHeight + gap : top);
        }

        /// <summary>Anker (linke obere Ecke) des Lautstärke-Stapels: Platz-Voreinstellung oder eigene Position.</summary>
        public static Point OsdAnchor(AppConfig cfg, double width, double stackHeight)
        {
            if (IsPreset(cfg.OsdCornerPreset))
            {
                var p = Corner(cfg.OsdCornerPreset, new Size(width, stackHeight), cfg.OsdMonitor);
                if (cfg.MediaFollowsOsd() && IsMiddle(cfg.OsdCornerPreset))
                {
                    var (_, _, gap) = OsdCard.Dims(cfg);
                    p = CenterBlock(p, cfg.OsdMonitor, stackHeight, MediaOsd.EstimatedHeight, cfg.MediaCombinedSide == "above", gap);
                }
                return p;
            }
            var fallback = Corner("top_right", new Size(width, stackHeight), cfg.OsdMonitor);
            return new Point(cfg.OsdX ?? fallback.X, cfg.OsdY ?? fallback.Y);
        }

        /// <summary>Anker des eigenständigen Media-Flyouts: Platz-Voreinstellung oder eigene Position (Standard oben mittig).</summary>
        public static Point MediaAnchor(AppConfig cfg, double width, double height)
        {
            if (IsPreset(cfg.MediaCornerPreset))
            {
                var p = Corner(cfg.MediaCornerPreset, new Size(width, height), cfg.MediaMonitor);
                if (cfg.OsdFollowsMedia() && IsMiddle(cfg.MediaCornerPreset))
                {
                    var (_, h, gap) = OsdCard.Dims(cfg);
                    p = CenterBlock(p, cfg.MediaMonitor, height, h, cfg.MediaCombinedSide != "above", gap);
                }
                return p;
            }
            var fallback = Corner("top_center", new Size(width, height), cfg.MediaMonitor);
            return new Point(cfg.MediaOsdX ?? fallback.X, cfg.MediaOsdY ?? fallback.Y);
        }

        /// <summary>
        /// „Zusammen“ bei einem Design mit CenterCombined (z. B. Dynamic Island): OSD mittig über/unter dem Flyout.
        /// Liefert den Versatz der OSD-X gegenüber der Flyout-X (sonst 0 = linksbündig wie bisher).
        /// </summary>
        public static double CombinedOffset(AppConfig cfg, double osdWidth) =>
            Designs.Get(cfg.OsdDesign)?.CenterCombined == true ? (MediaOsd.FlyoutWidth - osdWidth) / 2 : 0;

        /// <summary>
        /// Bei „zusammen“: welches Element am gewählten Platz sitzt. Oben/Mitte das obere, unten das untere –
        /// so bleibt die Reihenfolge erhalten, ohne dass das angehängte Element über den Rand müsste.
        /// side = media_combined_side ("above" = Media-Flyout über dem OSD).
        /// </summary>
        public static string CombinedAnchorFor(string side, string preset)
        {
            string upper = side == "above" ? "media" : "osd";
            string lower = upper == "media" ? "osd" : "media";
            return preset.StartsWith("bottom") ? lower : upper;
        }

        /// <summary>
        /// Wo OSD-Karte und Media-Flyout mit dieser Config erscheinen würden (für "Testen"): beachtet
        /// Anordnung, Ecken, Bildschirm, Ausweichen auf die andere Seite und Überlappung. osdAt/mediaAt
        /// überschreiben die Anker (z.B. ein gespeicherter Snap-Punkt).
        /// </summary>
        /// <param name="osdYields">Bei Überlappung (getrennt) weicht das OSD aus statt des Flyouts – z.B. beim
        /// Anlegen eines Snap-Punkts: Flyout bleibt in seiner Ecke, OSD bündig darunter/darüber.</param>
        public static (Rect Osd, Rect Media) Layout(AppConfig cfg, double mediaHeight, Point? osdAt = null, Point? mediaAt = null,
            bool osdYields = false)
        {
            var (w, h, gap) = OsdCard.Dims(cfg);
            var osdSize = new Size(w, h);
            var mediaSize = new Size(MediaOsd.FlyoutWidth, mediaHeight);

            Rect Place(Point p, Size s) => new(Clamp(new Rect(p, s)), s);
            double Side(Rect anchor, double height, bool wantBelow)
            {
                var wa = WorkAreaFor(anchor);
                double below = anchor.Bottom + gap, above = anchor.Top - gap - height;
                return wantBelow ? (below + height <= wa.Bottom ? below : above) : (above >= wa.Top ? above : below);
            }

            Rect osd, media;
            if (cfg.OsdFollowsMedia())
            {
                media = Place(mediaAt ?? MediaAnchor(cfg, mediaSize.Width, mediaSize.Height), mediaSize);
                osd = Place(new Point(media.X + CombinedOffset(cfg, w), Side(media, h, cfg.MediaCombinedSide == "above")), osdSize);
            }
            else
            {
                osd = Place(osdAt ?? OsdAnchor(cfg, w, h), osdSize);
                if (cfg.MediaFollowsOsd())
                {
                    media = Place(new Point(osd.X - CombinedOffset(cfg, w), Side(osd, mediaSize.Height, cfg.MediaCombinedSide != "above")), mediaSize);
                }
                else
                {
                    media = Place(mediaAt ?? MediaAnchor(cfg, mediaSize.Width, mediaSize.Height), mediaSize);
                    if (Overlaps(media, osd))
                    {
                        if (osdYields) osd = new Rect(AvoidOverlap(osd, media), osdSize);
                        else media = new Rect(AvoidOverlap(media, osd), mediaSize);
                    }
                }
            }
            return (osd, media);
        }

        /// <summary>Position so verschieben, dass das Rechteck vollständig im Arbeitsbereich seines Bildschirms liegt.</summary>
        public static Point Clamp(Rect r)
        {
            var wa = WorkAreaFor(r);
            double x = r.Width >= wa.Width ? wa.Left : Math.Min(Math.Max(r.X, wa.Left), wa.Right - r.Width);
            double y = r.Height >= wa.Height ? wa.Top : Math.Min(Math.Max(r.Y, wa.Top), wa.Bottom - r.Height);
            return new Point(x, y);
        }

        /// <summary>Echte Überlappung (bloßes Berühren der Kanten zählt nicht).</summary>
        public static bool Overlaps(Rect a, Rect b) =>
            a.Left < b.Right - 1 && b.Left < a.Right - 1 && a.Top < b.Bottom - 1 && b.Top < a.Bottom - 1;

        /// <summary>
        /// Neue Position für moving, damit es fixedRect nicht überdeckt. Bevorzugt direkt darunter bzw.
        /// darüber (je nachdem, was näher ist) und dann auf derselben X-Koordinate wie fixedRect (bündig
        /// ausgerichtet; passt das nicht, rechtsbündig). Erst wenn senkrecht kein Platz ist: links/rechts daneben.
        /// </summary>
        public static Point AvoidOverlap(Rect moving, Rect fixedRect)
        {
            var wa = WorkAreaFor(moving);
            bool Fits(Point p) =>
                p.X >= wa.Left && p.Y >= wa.Top && p.X + moving.Width <= wa.Right && p.Y + moving.Height <= wa.Bottom;

            double below = fixedRect.Bottom + Gap, above = fixedRect.Top - Gap - moving.Height;
            var verticalOrder = Math.Abs(below - moving.Y) <= Math.Abs(above - moving.Y)
                ? new[] { below, above } : new[] { above, below };
            foreach (var y in verticalOrder)
            {
                var left = new Point(fixedRect.Left, y);                    // gleiche X-Koordinate
                if (Fits(left)) return left;
                var right = new Point(fixedRect.Right - moving.Width, y);   // rechtsbündig, falls breiter und am Rand
                if (Fits(right)) return right;
            }

            var sideways = new[]
            {
                new Point(fixedRect.Left - Gap - moving.Width, fixedRect.Top),
                new Point(fixedRect.Right + Gap, fixedRect.Top),
            };
            var fitting = sideways.Where(Fits).ToList();
            return fitting.Count > 0 ? fitting[0] : Clamp(new Rect(new Point(fixedRect.Left, below), moving.Size));
        }

        // ── Auflösungs-/Monitorwechsel ────────────────────────────────────────

        public static List<ScreenRef> ToRefs(List<MonitorArea> mons) =>
            mons.Select(m => new ScreenRef { Device = m.Device, X = m.Work.X, Y = m.Work.Y, W = m.Work.Width, H = m.Work.Height, Primary = m.Primary }).ToList();

        public static List<MonitorArea> FromRefs(List<ScreenRef> refs) =>
            refs.Select(r => new MonitorArea(r.Device, new Rect(r.X, r.Y, r.W, r.H), 0, 0, r.Primary)).ToList();

        public static bool SameLayout(List<ScreenRef> a, List<ScreenRef> b) =>
            a.Count == b.Count && a.All(x => b.Any(y => y.Device == x.Device
                && Math.Abs(y.X - x.X) < 0.5 && Math.Abs(y.Y - x.Y) < 0.5 && Math.Abs(y.W - x.W) < 0.5 && Math.Abs(y.H - x.H) < 0.5));

        /// <summary>
        /// Punkt vom alten auf den neuen Bildschirmaufbau übertragen: gleicher Bildschirm (Gerätename,
        /// sonst Hauptbildschirm); je Achse gleicher Abstand zum näheren Rand, in der Mitte gleiche relative Lage.
        /// </summary>
        public static Point Remap(Point p, List<MonitorArea> oldMons, List<MonitorArea> newMons)
        {
            var oldWork = WorkAreaFor(new Rect(p, new Size(1, 1)), oldMons);
            var oldMon = oldMons.FirstOrDefault(m => m.Work == oldWork);
            var newMon = newMons.FirstOrDefault(m => oldMon != null && m.Device == oldMon.Device)
                         ?? newMons.FirstOrDefault(m => m.Primary) ?? newMons[0];
            var nw = newMon.Work;

            static double Axis(double v, double oldStart, double oldLen, double newStart, double newLen)
            {
                if (oldLen <= 0) return newStart;
                double rel = (v - oldStart) / oldLen;
                if (rel < 0.33) return newStart + (v - oldStart);                  // Abstand zum linken/oberen Rand
                if (rel > 0.67) return newStart + newLen - (oldStart + oldLen - v); // Abstand zum rechten/unteren Rand
                return newStart + rel * newLen;                                    // Mitte: relativ
            }

            return new Point(Axis(p.X, oldWork.X, oldWork.Width, nw.X, nw.Width),
                             Axis(p.Y, oldWork.Y, oldWork.Height, nw.Y, nw.Height));
        }

        // ── Win32 ─────────────────────────────────────────────────────────────

        /// <summary>Rechteck (Pixel), Kante (0 links, 1 oben, 2 rechts, 3 unten) und Auto-Ausblenden der Haupt-Taskleiste.</summary>
        private static (RECT Rect, uint Edge, bool AutoHide) Taskbar()
        {
            try
            {
                var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
                bool autoHide = (SHAppBarMessage(ABM_GETSTATE, ref data).ToInt64() & ABS_AUTOHIDE) != 0;
                if (SHAppBarMessage(ABM_GETTASKBARPOS, ref data) == IntPtr.Zero) return (default, 3, false);
                return (data.rc, data.uEdge, autoHide);
            }
            catch
            {
                return (default, 3, false);
            }
        }

        private const uint ABM_GETSTATE = 4;
        private const uint ABM_GETTASKBARPOS = 5;
        private const long ABS_AUTOHIDE = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        [DllImport("shell32.dll")]
        private static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();
    }
}
