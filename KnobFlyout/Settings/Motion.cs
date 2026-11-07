using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KnobFlyout.Settings
{
    /// <summary>
    /// Animationen der Einstellungen wie in Windows 11: Hintergrund blendet beim Überfahren weich über (<see cref="Fade"/>),
    /// Klick drückt kurz ein (<see cref="EnablePress"/>), Seiten gleiten herein (<see cref="PageIn"/>), aufklappbare Karten
    /// klappen weich auf (<see cref="Expand"/>), der Auswahlbalken der Navigation gleitet (<see cref="SlideFrom"/>).
    /// </summary>
    internal static class Motion
    {
        private static readonly IEasingFunction Out = new CubicEase { EasingMode = EasingMode.EaseOut };

        /// <summary>Animationen an (Einstellung settings_animations). Aus: alles wechselt sofort.</summary>
        public static bool Enabled = true;

        private static DoubleAnimation To(double to, double ms, double? from = null) =>
            new() { To = to, From = from, Duration = TimeSpan.FromMilliseconds(ms), EasingFunction = Out };

        /// <summary>Hintergrund weich auf eine neue Farbe blenden (statt sie hart zu setzen). Nur einfarbige Pinsel, sonst sofort.</summary>
        public static void Fade(FrameworkElement el, Brush? to, double ms = 120)
        {
            DependencyProperty? dp = el switch
            {
                Border => Border.BackgroundProperty,
                Control => Control.BackgroundProperty,
                Panel => Panel.BackgroundProperty,
                _ => null,
            };
            if (dp == null) return;
            if (!Enabled || to is not SolidColorBrush target || target.Opacity < 1)
            {
                el.SetValue(dp, to);
                return;
            }
            var cur = el.GetValue(dp) as SolidColorBrush;
            Color from = cur?.Color ?? Colors.Transparent;
            if (cur != null && cur.Opacity < 1) from = Color.FromArgb((byte)(from.A * cur.Opacity), from.R, from.G, from.B);
            Color end = target.Color;
            // Von/zu durchsichtig: Farbe gleich lassen, nur die Deckkraft ändern (sonst blitzt Weiß/Schwarz durch)
            if (from.A == 0) from = Color.FromArgb(0, end.R, end.G, end.B);
            if (end.A == 0) end = Color.FromArgb(0, from.R, from.G, from.B);
            var brush = new SolidColorBrush(from);
            el.SetValue(dp, brush);
            brush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(end, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Out });
        }

        // ── Klick drückt ein ──────────────────────────────────────────────────
        private static FrameworkElement? _pressed;

        /// <summary>Klickbare Flächen (Hand-Cursor, Knöpfe) im Fenster werden beim Drücken kurz kleiner.</summary>
        public static void EnablePress(Window w)
        {
            w.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, e) =>
            {
                Release();
                if (!Enabled) return;
                var el = PressTarget(e.OriginalSource as DependencyObject);
                if (el == null) return;
                _pressed = el;
                Scale(el, 1 - Math.Min(0.04, 3.0 / Math.Max(1, el.ActualWidth)), 70);
            }), true);
            w.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler((_, _) => Release()), true);
            w.MouseLeave += (_, _) => Release();
            w.Deactivated += (_, _) => Release();
        }

        private static void Release()
        {
            if (_pressed == null) return;
            Scale(_pressed, 1, 160);
            _pressed = null;
        }

        private static FrameworkElement? PressTarget(DependencyObject? d)
        {
            while (d != null && d is not Window)
            {
                switch (d)
                {
                    case TextBoxBase or ScrollBar or Slider or Thumb or ComboBox or ToggleButton:
                        return null; // eigene Rückmeldung oder Ziehen
                    case ButtonBase b:
                        return b;
                    case Border bd when bd.Cursor == Cursors.Hand:
                        return bd;
                }
                d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        private static void Scale(FrameworkElement el, double to, double ms)
        {
            if (el.RenderTransform is not ScaleTransform st || st.IsFrozen)
            {
                if (el.RenderTransform != null && el.RenderTransform != Transform.Identity && el.RenderTransform is not ScaleTransform) return;
                st = new ScaleTransform(1, 1);
                el.RenderTransform = st;
                el.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            st.BeginAnimation(ScaleTransform.ScaleXProperty, To(to, ms));
            st.BeginAnimation(ScaleTransform.ScaleYProperty, To(to, ms));
        }

        // ── Seitenwechsel ─────────────────────────────────────────────────────
        /// <summary>Neue Seite blendet ein und gleitet ein Stück von unten herein.</summary>
        public static void PageIn(UIElement page)
        {
            if (!Enabled) return;
            var tt = page.RenderTransform as TranslateTransform;
            if (tt == null || tt.IsFrozen)
            {
                tt = new TranslateTransform();
                page.RenderTransform = tt;
            }
            tt.BeginAnimation(TranslateTransform.YProperty, To(0, 220, 16));
            page.BeginAnimation(UIElement.OpacityProperty, To(1, 160, 0));
        }

        // ── Auswahlbalken ─────────────────────────────────────────────────────
        /// <summary>Balken gleitet von der Stelle des alten Balkens (in Koordinaten von relativeTo) an seinen Platz.</summary>
        public static void SlideFrom(FrameworkElement bar, FrameworkElement? oldBar, Visual relativeTo)
        {
            if (!Enabled || oldBar == null || oldBar == bar || !bar.IsLoaded || !oldBar.IsLoaded) return;
            try
            {
                double dy = oldBar.TranslatePoint(new Point(), (UIElement)relativeTo).Y - bar.TranslatePoint(new Point(), (UIElement)relativeTo).Y;
                if (Math.Abs(dy) < 1) return;
                var tt = new TranslateTransform(0, dy);
                bar.RenderTransform = tt;
                tt.BeginAnimation(TranslateTransform.YProperty, To(0, 260));
            }
            catch (InvalidOperationException) { /* nicht im selben Baum */ }
        }

        // ── Navigation auf-/zuklappen ─────────────────────────────────────────
        private static int _paneVersion;

        /// <summary>
        /// Seitenleiste auf eine neue Breite gleiten lassen. Aufklappen: Beschriftungen erscheinen gleich und blenden
        /// leicht verzögert ein; zuklappen: sie blenden schnell aus und verschwinden am Ende.
        /// </summary>
        public static void Pane(FrameworkElement pane, double width, IList<FrameworkElement> labels, bool open)
        {
            int version = ++_paneVersion;
            void Finish()
            {
                pane.BeginAnimation(FrameworkElement.WidthProperty, null);
                pane.Width = width;
                foreach (var l in labels)
                {
                    l.BeginAnimation(UIElement.OpacityProperty, null);
                    l.Opacity = 1;
                    l.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            if (!Enabled || !pane.IsLoaded)
            {
                Finish();
                return;
            }
            double from = pane.ActualWidth > 0 ? pane.ActualWidth : pane.Width;
            var anim = To(width, open ? 240 : 200, from);
            anim.Completed += (_, _) => { if (version == _paneVersion) Finish(); };
            foreach (var l in labels)
            {
                if (open)
                {
                    l.Visibility = Visibility.Visible;
                    l.BeginAnimation(UIElement.OpacityProperty, null);
                    l.Opacity = 0; // sonst blitzt die Beschriftung vor dem verzögerten Einblenden kurz voll auf
                    l.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
                        { BeginTime = TimeSpan.FromMilliseconds(70), EasingFunction = Out, FillBehavior = FillBehavior.HoldEnd });
                }
                else
                    l.BeginAnimation(UIElement.OpacityProperty, To(0, 90));
            }
            pane.BeginAnimation(FrameworkElement.WidthProperty, anim);
        }

        // ── Aufklappen ────────────────────────────────────────────────────────
        /// <summary>Inhalt weich auf- bzw. zuklappen (Höhe + Deckkraft), Pfeil dreht sich.</summary>
        public static void Expand(FrameworkElement body, RotateTransform? chevron, bool open, double width)
        {
            if (!Enabled)
            {
                body.Tag = (body.Tag is int t ? t + 1 : 1); // laufende Animation wirkungslos machen
                chevron?.BeginAnimation(RotateTransform.AngleProperty, null);
                if (chevron != null) chevron.Angle = open ? 180 : 0;
                body.BeginAnimation(FrameworkElement.HeightProperty, null);
                body.BeginAnimation(UIElement.OpacityProperty, null);
                body.Height = double.NaN;
                body.Opacity = 1;
                body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                return;
            }
            chevron?.BeginAnimation(RotateTransform.AngleProperty, To(open ? 180 : 0, 200));
            int version = (int)(body.Tag is int v ? v + 1 : 1);
            body.Tag = version;
            body.ClipToBounds = true;
            double from = body.Visibility == Visibility.Visible ? body.ActualHeight : 0;
            double target = 0;
            if (open)
            {
                body.Visibility = Visibility.Visible;
                body.BeginAnimation(FrameworkElement.HeightProperty, null);
                body.Height = double.NaN;
                body.Measure(new Size(width > 0 ? width : double.PositiveInfinity, double.PositiveInfinity));
                target = body.DesiredSize.Height;
            }
            var anim = To(target, open ? 220 : 180, from);
            anim.Completed += (_, _) =>
            {
                if (!Equals(body.Tag, version)) return; // inzwischen erneut umgeschaltet
                body.BeginAnimation(FrameworkElement.HeightProperty, null);
                body.Height = double.NaN;
                if (!open) body.Visibility = Visibility.Collapsed;
            };
            body.BeginAnimation(FrameworkElement.HeightProperty, anim);
            body.BeginAnimation(UIElement.OpacityProperty, To(open ? 1 : 0, open ? 220 : 140, open ? 0 : null));
        }
    }
}
