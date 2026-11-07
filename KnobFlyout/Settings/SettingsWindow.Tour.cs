using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using static KnobFlyout.Loc;
using static KnobFlyout.Settings.UiHelpers;

namespace KnobFlyout.Settings
{
    /// <summary>
    /// „Tour durch die Einstellungen“ (wählbar am Ende der Einführung): führt durch das echte Fenster, hebt je Schritt eine
    /// Einstellung hervor und erklärt sie in einer Sprechblase mit Zurück, Weiter und Beenden. Ändert nichts an den Einstellungen.
    /// </summary>
    public sealed partial class SettingsWindow
    {
        /// <summary>Rückgabe der Einführung, wenn dort „Tour durch die Einstellungen“ gewählt wurde.</summary>
        internal const string TourPage = "__tour";

        private sealed record TourStep(string Page, string Target, Func<string> Title, Func<string> Text);

        private List<TourStep>? _tour;
        private int _tourIndex;
        private Popup? _tourBubble;
        private TourHighlight? _tourHighlight;
        private Button? _saveButton;

        private List<TourStep> TourSteps() => new()
        {
            new("general", "Mit Windows starten|Start with Windows", () => T("Mit Windows starten", "Start with Windows"),
                () => T("KnobFlyout läuft dann im Hintergrund, sobald Windows startet.", "KnobFlyout then runs in the background as soon as Windows starts.")),
            new("volume", "Schrittweite|Step size", () => T("Schrittweite", "Step size"),
                () => T("Wie viel ein Klick am Knopf ändert.", "How much one notch of the knob changes.")),
            new("profiles", "Profil wechseln|Switch profile", () => T("Profil wechseln", "Switch profile"),
                () => T("Kurz drücken: nächstes Profil. Halten: nächste gespeicherte Position.", "Tap: next profile. Hold: next saved position.")),
            new("profiles", "Profile bearbeiten|Edit profiles", () => T("Profile", "Profiles"),
                () => T("Name, Logo, Farbe und Reihenfolge deiner Profile.", "Name, logo, color and order of your profiles.")),
            new("profiles", "Automatisch einsortieren|Sort automatically", () => T("Automatisch einsortieren", "Sort automatically"),
                () => T("Neue Apps landen von selbst im passenden Profil.", "New apps go to the right profile by themselves.")),
            new("profiles", "Ignorierte Apps|Ignored apps", () => T("Ignorierte Apps", "Ignored apps"),
                () => T("Diese Apps regelt der Knopf nie.", "The knob never controls these apps.")),
            new("osd", "Design anpassen|Customize the design", () => T("Design anpassen", "Customize the design"),
                () => T("Aufbau, Farben und Deckkraft des OSD, gespeichert als eigenes Design.", "Layout, colors and opacity of the OSD, saved as your own design.")),
            new("media", "Dauerhaft anzeigen|Keep on screen", () => T("Dauerhaft anzeigen", "Keep on screen"),
                () => T("Das Flyout bleibt stehen, solange z. B. Spotify spielt.", "The flyout stays while e.g. Spotify is playing.")),
            new("position", "OSD und Media-Flyout|OSD and media flyout", () => T("Platz auf dem Bildschirm", "Place on screen"),
                () => _cfg.OsdLayout == "combined"
                    ? T("Klick auf einen Platz setzt OSD und Flyout zusammen dorthin.", "Click a place to put the OSD and flyout there together.")
                    : T("Linksklick setzt das Flyout (gelb), Rechtsklick das OSD (blau).", "Left click sets the flyout (yellow), right click the OSD (blue).")),
            new("position", "Gespeicherte Positionen|Saved positions", () => T("Gespeicherte Positionen", "Saved positions"),
                () => T("Mehrere Plätze speichern und mit dem Profil-Hotkey (halten) wechseln.", "Save several places and switch by holding the profile key.")),
            new("design", "Beide Farben anpassen|Customize the colors of both", () => T("Beide Farben anpassen", "Customize the colors of both"),
                () => T("Farben von OSD und Flyout zusammen oder getrennt.", "Colors of the OSD and flyout, together or separately.")),
            new("hotkeys", "Skip vor|Skip forward", () => T("Hotkeys", "Shortcuts"),
                () => T("Alle Hotkeys an einem Ort, z. B. zum Skippen.", "All keys in one place, e.g. for skipping.")),
            new("spotify", "Über die Spotify-API steuern|Control via the Spotify API", () => "Spotify",
                () => T("Regelt die Lautstärke direkt in Spotify statt über Windows. Darunter steht, wie du es einmal verbindest.",
                    "Controls the volume right in Spotify instead of through Windows. Below it shows how to connect it once.")),
            new("youtube", "Lautstärke im Video-Player regeln|Control volume in the video player", () => "Browser-Addon",
                () => T("Mit dem Browser-Addon regelt der Knopf den Video-Player selbst, jede Webseite einzeln abschaltbar. Einrichten steht darunter.",
                    "With the browser add-on the knob controls the video player itself, each website can be switched off. Setup is shown below.")),
            new("", "#save", () => T("Speichern", "Save"),
                () => T("Änderungen gelten erst mit „Speichern“. ↶ daneben nimmt sie zurück.", "Changes apply with “Save”. ↶ next to it discards them.")),
        };

        /// <summary>Tour starten (Fenster muss sichtbar sein).</summary>
        public void StartTour()
        {
            EndTour();
            _tour = TourSteps();
            _tourIndex = 0;
            PreviewKeyDown += TourKeyDown;
            LocationChanged += TourReposition;
            SizeChanged += TourReposition;
            ShowTourStep();
        }

        private void TourKeyDown(object sender, KeyEventArgs e)
        {
            if (_tour == null || e.Key != Key.Escape) return;
            e.Handled = true;
            EndTour();
        }

        private void TourReposition(object? sender, EventArgs e)
        {
            if (_tourBubble is not { IsOpen: true }) return;
            _tourBubble.HorizontalOffset += 0.01; // Popup folgt dem Fenster nicht von selbst
            _tourBubble.HorizontalOffset -= 0.01;
        }

        private void EndTour()
        {
            if (_tourBubble != null)
            {
                _tourBubble.IsOpen = false;
                if (_tourBubble.Parent is Panel p) p.Children.Remove(_tourBubble);
            }
            _tourBubble = null;
            RemoveTourHighlight();
            if (_tour == null) return;
            _tour = null;
            PreviewKeyDown -= TourKeyDown;
            LocationChanged -= TourReposition;
            SizeChanged -= TourReposition;
        }

        private void RemoveTourHighlight()
        {
            if (_tourHighlight == null) return;
            AdornerLayer.GetAdornerLayer(_tourHighlight.AdornedElement)?.Remove(_tourHighlight);
            _tourHighlight = null;
        }

        /// <summary>Karte auf der aktuellen Seite, deren Titel einer der Texte ist (Deutsch|Englisch).</summary>
        private FrameworkElement? FindTourTarget(string target)
        {
            if (target == "#save") return _saveButton;
            if (_pageScroll?.Content is not Panel page) return null;
            var names = target.Split('|');
            foreach (UIElement child in page.Children)
                if (child is FrameworkElement card && Descendants(card).OfType<TextBlock>().Any(t => names.Contains(t.Text))) return card;
            return null;
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var d in Descendants(child)) yield return d;
            }
        }

        private void ShowTourStep()
        {
            if (_tour == null) return;
            var step = _tour[_tourIndex];
            if (step.Page.Length > 0 && !IsPageShown(step.Page))
            {
                if (_tourIndex < _tour.Count - 1) { _tourIndex++; ShowTourStep(); }
                else EndTour();
                return;
            }
            if (step.Page.Length > 0 && _currentPage != step.Page)
            {
                SelectPage(step.Page);
                if (_currentPage != step.Page) // Seite nicht verlassen (z. B. ungespeichertes Design): Tour endet
                {
                    EndTour();
                    return;
                }
            }
            UpdateLayout();
            var target = FindTourTarget(step.Target);
            if (target == null)
            {
                // Karte gibt es hier nicht (z. B. andere Anordnung): Schritt überspringen
                if (_tourIndex < _tour.Count - 1) { _tourIndex++; ShowTourStep(); }
                else EndTour();
                return;
            }
            target.BringIntoView();
            Dispatcher.BeginInvoke(new Action(() => PlaceTourBubble(target, step)), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void PlaceTourBubble(FrameworkElement target, TourStep step)
        {
            if (_tour == null) return;
            RemoveTourHighlight();
            if (AdornerLayer.GetAdornerLayer(target) is { } layer)
            {
                _tourHighlight = new TourHighlight(target);
                layer.Add(_tourHighlight);
            }

            var count = new TextBlock { Text = $"{_tourIndex + 1} / {_tour.Count}", FontSize = 12, Foreground = SubFg, VerticalAlignment = VerticalAlignment.Center };
            var close = FlatIconButton("", T("Tour beenden", "End tour"), EndTour); // Cancel
            close.Width = close.Height = 26;
            close.FontSize = 10;
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
            head.Children.Add(count);

            var panel = new StackPanel();
            panel.Children.Add(head);
            panel.Children.Add(new TextBlock { Text = step.Title(), FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Fg, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = step.Text(), FontSize = 13, Foreground = SubFg, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) });

            bool last = _tourIndex == _tour.Count - 1;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            if (_tourIndex > 0)
            {
                var back = Btn(T("Zurück", "Back"), 80);
                back.Margin = new Thickness(0, 0, 8, 0);
                back.Click += (_, _) =>
                {
                    _tourIndex--;
                    ShowTourStep();
                };
                buttons.Children.Add(back);
            }
            var next = AccentBtn(last ? T("Fertig", "Done") : T("Weiter", "Next"), 80);
            next.Margin = new Thickness(0);
            next.Click += (_, _) =>
            {
                if (last) EndTour();
                else
                {
                    _tourIndex++;
                    ShowTourStep();
                }
            };
            buttons.Children.Add(next);
            panel.Children.Add(buttons);

            var card = new Border
            {
                Width = 300, Background = CardBg, BorderBrush = Accent, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 10, 12, 14), Child = panel,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.35 },
            };

            if (_tourBubble == null)
            {
                // im Fensterbaum, damit die Theme-Stile der Knöpfe greifen
                _tourBubble = new Popup { AllowsTransparency = true, StaysOpen = true, Placement = PlacementMode.Bottom };
                if (Content is Panel root) root.Children.Add(_tourBubble);
            }
            _tourBubble.IsOpen = false;
            _tourBubble.Child = new Border { Padding = new Thickness(8), Child = card };
            _tourBubble.PlacementTarget = target;
            // Speichern-Knopf unten rechts: Blase darüber, rechtsbündig
            _tourBubble.Placement = step.Target == "#save" ? PlacementMode.Top : PlacementMode.Bottom;
            _tourBubble.HorizontalOffset = step.Target == "#save" ? target.ActualWidth - 316 : 0;
            _tourBubble.IsOpen = true;
            next.Focus();
        }

        /// <summary>Rahmen in Akzentfarbe um die erklärte Einstellung.</summary>
        private sealed class TourHighlight : Adorner
        {
            public TourHighlight(UIElement adorned) : base(adorned)
            {
                IsHitTestVisible = false;
                // Karte wird größer (z. B. aufgeklappt): Rahmen mitziehen
                if (adorned is FrameworkElement fe) fe.SizeChanged += (_, _) => InvalidateVisual();
            }

            protected override void OnRender(DrawingContext dc)
            {
                var size = ((FrameworkElement)AdornedElement).RenderSize;
                var pen = new Pen(Accent, 2.5);
                dc.DrawRoundedRectangle(null, pen, new Rect(-3, -3, size.Width + 6, size.Height + 6), 10, 10);
            }
        }
    }
}
