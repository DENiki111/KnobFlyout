using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using KnobFlyout.Config;
using KnobFlyout.Media;
using KnobFlyout.Osd;
using static KnobFlyout.Loc;
using static KnobFlyout.Settings.UiHelpers;

namespace KnobFlyout.Settings
{
    /// <summary>
    /// Einführung: erscheint beim ersten Programmstart und danach bei jedem Öffnen der Einstellungen, bis das
    /// Häkchen „Nicht mehr anzeigen“ gesetzt ist (tutorial_hidden); Überspringen schließt sie nur für dieses Mal.
    /// Links (Bühne) echte Vorschauen, die live reagieren (Knopf, Lautstärke, laufende Wiedergabe), rechts kurzer
    /// Text und direkt wirksame Bedienelemente. Änderungen werden sofort gespeichert (Update), damit z. B. eine neue
    /// Profil-Taste gleich funktioniert. Neben den Schritten gibt es Unterseiten (Eigenes Design, Alle Funktionen).
    /// Die Seiten stehen in TutorialWindow.Pages.cs und TutorialWindow.Features.cs.
    /// </summary>
    public sealed partial class TutorialWindow : Window
    {
        private const double DesignW = 1000, DesignH = 680, StageW = 480;

        private sealed record Page(string Hue, Func<string> Title, Func<string> Lead, Func<FrameworkElement> Stage, Action<StackPanel> Body)
        {
            // Nur Unterseiten: Text über dem Fortschritt, Beschriftung und Wirkung der Knöpfe unten (leer = Standard).
            public Func<string>? Crumb { get; init; }
            public Func<string>? NextText { get; init; }
            public Action? Next { get; init; }
            public Func<string>? BackText { get; init; }
        }

        private readonly List<Page> _pages;
        private int _index;
        private readonly Stack<Page> _subPages = new(); // geöffnete Unterseiten; _index bleibt dabei stehen
        private Page Current => _subPages.Count > 0 ? _subPages.Peek() : _pages[_index];
        private readonly MediaController _media;
        private readonly bool _ownsMedia;

        private ContentControl _stageHost = null!;
        private ContentControl _bodyHost = null!;
        private ScrollViewer _bodyScroll = null!;
        private GradientStop _gradTop = null!, _gradBottom = null!, _glow = null!;
        private TextBlock _counter = null!;
        private Border[] _segments = Array.Empty<Border>();
        private Button _skip = null!, _back = null!, _next = null!;
        private DispatcherTimer? _pageTimer;
        private readonly DispatcherTimer _stageTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
        private bool _stageDirty; // Einstellung geändert: Bühne neu aufbauen (gebündelt, Slider feuern dauernd)

        /// <summary>Etwas direkt gespeichert: App übernimmt die Config (Hotkey, Sprache, Flyouts).</summary>
        public event Action? ConfigChanged;

        /// <summary>Knöpfe unter der Vorschau: wie am echten Knopf drehen (Vorzeichen = Richtung) bzw. drücken.</summary>
        public event Action<int>? VolumeRequested;
        public event Action? MuteRequested;

        /// <summary>Profil per Klick gewechselt: das echte OSD soll es anzeigen.</summary>
        public event Action? ProfileOsdRequested;

        /// <summary>Automatik eingeschaltet oder geändert: laufende Apps gleich einsortieren.</summary>
        public event Action? AutoAssignRequested;

        /// <summary>„Öffnen“ auf der letzten Seite: diese Einstellungs-Seite soll nach dem Schließen aufgehen.</summary>
        public string? RequestedSettingsPage { get; private set; }

        public int PageCount => _pages.Count;

        /// <summary>Erster Programmstart (auch nach einem Update, wenn sie noch nie zu sehen war).</summary>
        public static bool ShowOnStart(AppConfig cfg) => !cfg.TutorialHidden && !cfg.TutorialStarted;

        /// <summary>Beim Öffnen der Einstellungen, bis das Häkchen gesetzt ist.</summary>
        public static bool ShowWithSettings(AppConfig cfg) => !cfg.TutorialHidden;

        /// <summary>
        /// Überträgt, was sich von before zu after geändert hat, auf to (die Einführung kann fast alles einstellen).
        /// Für offene Einstellungen mit ungespeichertem Stand und für den Arbeitsstand der Seite „Eigenes Design“.
        /// </summary>
        public static void CopyChangedFields(AppConfig before, AppConfig after, AppConfig to)
        {
            foreach (var prop in typeof(AppConfig).GetProperties())
            {
                if (!prop.CanRead || !prop.CanWrite) continue;
                if (prop.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), false).Length > 0) continue; // berechnet oder nur noch zum Übernehmen
                var value = prop.GetValue(after);
                if (System.Text.Json.JsonSerializer.Serialize(prop.GetValue(before)) != System.Text.Json.JsonSerializer.Serialize(value))
                    prop.SetValue(to, value);
            }
        }

        public TutorialWindow(MediaController? media = null)
        {
            _media = media ?? new MediaController();
            _ownsMedia = media == null;
            Icon = Tray.AppIcon.Render(48);
            ResizeMode = ResizeMode.CanMinimize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            // Kleine Bildschirme: alles verkleinern statt abschneiden (siehe Viewbox in BuildShell).
            MaxWidth = SystemParameters.WorkArea.Width;
            MaxHeight = SystemParameters.WorkArea.Height;
            SourceInitialized += (_, _) => ApplyDarkTitleBar(this);

            _pages = BuildPages();
            Update(c => c.TutorialStarted = true, notify: false);
            BuildShell();
            ShowPage(0, 0);

            _stageTimer.Tick += (_, _) => FlushStage();
            _stageTimer.Start();
            Closed += (_, _) =>
            {
                _stageTimer.Stop();
                FlushDesign();
                if (_stageDirty) ConfigChanged?.Invoke();
                _pageTimer?.Stop();
                DisposeLiveMedia();
                _audio.Dispose();
                if (_ownsMedia) _media.Dispose();
            };
        }

        // ── Rahmen ──────────────────────────────────────────────────────────

        /// <summary>Baut das Fenster (auch nach einem Wechsel von Sprache oder Farbschema).</summary>
        private void BuildShell()
        {
            SetTheme(ThemeHelper.IsLight(Cfg()));
            ApplyControlTheme(this);
            ApplyDarkTitleBar(this);
            Title = T("KnobFlyout – Einführung", "KnobFlyout – Introduction");
            Background = ContentBg;

            var root = new Grid { Width = DesignW, Height = DesignH, Background = ContentBg };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(StageW) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(BuildStageShell());

            var right = BuildRight();
            Grid.SetColumn(right, 1);
            root.Children.Add(right);

            Content = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = root };
        }

        private FrameworkElement BuildStageShell()
        {
            _gradTop = new GradientStop(Colors.Black, 0);
            _gradBottom = new GradientStop(Colors.Black, 1);
            _glow = new GradientStop(Colors.Transparent, 0);

            var stage = new Grid { ClipToBounds = true };
            stage.Children.Add(new Border
            {
                Background = new LinearGradientBrush(new GradientStopCollection { _gradTop, _gradBottom }, new Point(0, 0), new Point(1, 1)),
            });
            // Weicher Lichtschein hinter der Vorschau
            stage.Children.Add(new Ellipse
            {
                Width = 620, Height = 620, IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Fill = new RadialGradientBrush(new GradientStopCollection { _glow, new GradientStop(Colors.Transparent, 1) }),
            });
            _stageHost = new ContentControl
            {
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = new TranslateTransform(),
                Focusable = false,
            };
            stage.Children.Add(new Border { Padding = new Thickness(24, 24, 24, 52), Child = _stageHost });

            // Absender unten links
            var brand = new StackPanel
            {
                Orientation = Orientation.Horizontal, Margin = new Thickness(24, 0, 0, 20),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false,
            };
            brand.Children.Add(new Image { Source = Tray.AppIcon.Render(32), Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center });
            brand.Children.Add(new TextBlock
            {
                Text = "KnobFlyout", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Fg, Opacity = 0.85,
                Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            });
            stage.Children.Add(brand);
            return stage;
        }

        private FrameworkElement BuildRight()
        {
            var dock = new DockPanel();

            // Kopf: Schritt, Fortschritt zum Anklicken, Überspringen
            var head = new StackPanel { Margin = new Thickness(40, 22, 40, 0) };
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            _skip = new Button
            {
                Content = T("Überspringen", "Skip"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = SubFg,
                FontSize = 13, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, -8, 0), Cursor = System.Windows.Input.Cursors.Hand,
                IsCancel = true, ToolTip = T("Einführung schließen (Esc)", "Close the introduction (Esc)"),
            };
            _skip.Click += (_, _) => Close();
            DockPanel.SetDock(_skip, Dock.Right);
            top.Children.Add(_skip);
            _counter = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Light ? Accent : FromHex("#6CB2F5"), VerticalAlignment = VerticalAlignment.Center };
            top.Children.Add(_counter);
            head.Children.Add(top);

            var bar = new UniformGrid { Rows = 1, Columns = _pages.Count, Margin = new Thickness(-1, 0, -1, 0) };
            _segments = new Border[_pages.Count];
            for (int i = 0; i < _pages.Count; i++)
            {
                int page = i;
                _segments[i] = new Border { Height = 4, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
                var hit = new Border
                {
                    Background = Brushes.Transparent, Height = 14, Padding = new Thickness(1, 0, 1, 0),
                    Cursor = System.Windows.Input.Cursors.Hand, Child = _segments[i], ToolTip = _pages[i].Title(),
                };
                hit.MouseLeftButtonUp += (_, _) => ShowPage(page, Math.Sign(page - _index));
                bar.Children.Add(hit);
            }
            head.Children.Add(bar);
            DockPanel.SetDock(head, Dock.Top);
            dock.Children.Add(head);

            // Fuß: Häkchen und Weiter
            var foot = new DockPanel { Margin = new Thickness(40, 12, 40, 20) };
            _next = AccentBtn("");
            _next.MinWidth = 112;
            _next.Margin = new Thickness(8, 0, 0, 0);
            _next.Padding = new Thickness(12, 7, 12, 7);
            _next.IsDefault = true;
            _next.Click += (_, _) =>
            {
                if (Current.Next != null) Current.Next();
                else if (_subPages.Count > 0) CloseSub();
                else if (_index >= _pages.Count - 1) Close();
                else ShowPage(_index + 1, 1);
            };
            DockPanel.SetDock(_next, Dock.Right);
            foot.Children.Add(_next);
            _back = Btn(T("Zurück", "Back"));
            _back.MinWidth = 92;
            _back.Margin = new Thickness(0);
            _back.Padding = new Thickness(12, 7, 12, 7);
            _back.Click += (_, _) =>
            {
                if (_subPages.Count > 0) CloseSub();
                else ShowPage(_index - 1, -1);
            };
            DockPanel.SetDock(_back, Dock.Right);
            foot.Children.Add(_back);

            var hide = new CheckBox
            {
                Content = T("Nicht mehr anzeigen", "Don't show again"), Foreground = SubFg, FontSize = 13, IsChecked = Cfg().TutorialHidden,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = T("Sonst erscheint die Einführung wieder, wenn du die Einstellungen öffnest. Wieder aufrufen: Einstellungen → Allgemein → Einführung.",
                    "Otherwise the introduction comes back when you open the settings. To see it again: Settings → General → Introduction."),
            };
            if (CheckTemplate() is { } template) hide.Template = template;
            hide.Checked += (_, _) => Update(c => c.TutorialHidden = true, notify: false);
            hide.Unchecked += (_, _) => Update(c => c.TutorialHidden = false, notify: false);
            foot.Children.Add(hide);
            DockPanel.SetDock(foot, Dock.Bottom);
            dock.Children.Add(foot);

            _bodyHost = new ContentControl { RenderTransform = new TranslateTransform(), Focusable = false };
            _bodyScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Content = _bodyHost,
                Padding = new Thickness(40, 14, 40, 8),
            };
            dock.Children.Add(_bodyScroll);
            return dock;
        }

        /// <summary>Häkchen im Stil der Einstellungen (dort gibt es sonst nur Schalter).</summary>
        private static ControlTemplate? CheckTemplate()
        {
            try
            {
                string off = Light ? "#8a8a8a" : "#9a9a9a";
                string hover = Light ? "#505050" : "#d0d0d0";
                return System.Windows.Markup.XamlReader.Parse(
                    "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"CheckBox\">" +
                    "<StackPanel Orientation=\"Horizontal\" Background=\"Transparent\">" +
                    "<Grid Width=\"18\" Height=\"18\" VerticalAlignment=\"Center\">" +
                    $"<Border x:Name=\"Box\" CornerRadius=\"4\" BorderThickness=\"1\" BorderBrush=\"{off}\" Background=\"Transparent\"/>" +
                    "<TextBlock x:Name=\"Check\" Text=\"&#xE73E;\" FontFamily=\"Segoe Fluent Icons, Segoe MDL2 Assets\" FontSize=\"12\" Foreground=\"White\" " +
                    "HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Visibility=\"Collapsed\"/>" +
                    "</Grid>" +
                    "<ContentPresenter VerticalAlignment=\"Center\" Margin=\"9,0,0,0\"/>" +
                    "</StackPanel>" +
                    "<ControlTemplate.Triggers>" +
                    $"<Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter TargetName=\"Box\" Property=\"BorderBrush\" Value=\"{hover}\"/></Trigger>" +
                    "<Trigger Property=\"IsChecked\" Value=\"True\">" +
                    "<Setter TargetName=\"Box\" Property=\"Background\" Value=\"#0078D4\"/>" +
                    "<Setter TargetName=\"Box\" Property=\"BorderBrush\" Value=\"#0078D4\"/>" +
                    "<Setter TargetName=\"Check\" Property=\"Visibility\" Value=\"Visible\"/>" +
                    "</Trigger>" +
                    "</ControlTemplate.Triggers>" +
                    "</ControlTemplate>") as ControlTemplate;
            }
            catch (Exception ex)
            {
                Logger.Error($"Einführung, Häkchen: {ex.Message}");
                return null;
            }
        }

        // ── Seitenwechsel ───────────────────────────────────────────────────

        /// <param name="dir">1 = vorwärts, -1 = zurück (Richtung der Bewegung), 0 = ohne Bewegung.</param>
        public void ShowPage(int index, int dir)
        {
            if (index < 0 || index >= _pages.Count) return;
            _subPages.Clear();
            _index = index;
            ShowCurrent(dir);
        }

        /// <summary>Unterseite öffnen (bleibt in der Einführung, Zurück führt wieder dorthin).</summary>
        private void OpenSub(Page page)
        {
            _subPages.Push(page);
            ShowCurrent(1);
        }

        private void CloseSub()
        {
            if (_subPages.Count == 0) return;
            _subPages.Pop();
            ShowCurrent(-1);
        }

        /// <summary>Oberste Unterseite durch eine andere ersetzen (nächste Funktion).</summary>
        private void ReplaceSub(Page page)
        {
            if (_subPages.Count > 0) _subPages.Pop();
            _subPages.Push(page);
            ShowCurrent(1);
        }

        private void ShowCurrent(int dir)
        {
            FlushDesign();
            StopPageTimer();
            ClearLiveFields();
            var page = Current;
            bool sub = _subPages.Count > 0;
            bool last = !sub && _index == _pages.Count - 1;

            _counter.Text = sub ? page.Crumb?.Invoke() ?? "" : T($"Schritt {_index + 1} von {_pages.Count}", $"Step {_index + 1} of {_pages.Count}");
            for (int i = 0; i < _segments.Length; i++)
                _segments[i].Background = i <= _index ? Accent : (Light ? FromHex("#CFCFD4") : FromHex("#404044"));
            _skip.Visibility = last ? Visibility.Hidden : Visibility.Visible;
            _skip.Content = sub ? T("Schließen", "Close") : T("Überspringen", "Skip");
            _back.Visibility = !sub && _index == 0 ? Visibility.Collapsed : Visibility.Visible;
            _back.Content = page.BackText?.Invoke() ?? T("Zurück", "Back");
            _next.Visibility = last ? Visibility.Collapsed : Visibility.Visible; // letzte Seite: Auswahl im Textbereich
            _next.Content = page.NextText?.Invoke() ?? T("Weiter", "Next");

            _stageHost.Content = page.Stage();
            _bodyHost.Content = BuildBody(page);
            _bodyScroll.ScrollToTop();

            Tint(page.Hue, dir != 0);
            SlideIn(_stageHost, dir, 0);
            SlideIn(_bodyHost, dir, 40);
        }

        /// <summary>Aktuelle Seite mit dem neuen Stand neu aufbauen, ohne Bewegung (z. B. nach der Wahl eines Designs).</summary>
        private void RefreshPage()
        {
            StopPageTimer();
            ClearStageFields();
            var page = Current;
            _stageHost.Content = page.Stage();
            double offset = _bodyScroll.VerticalOffset;
            _bodyHost.Content = BuildBody(page);
            _bodyScroll.ScrollToVerticalOffset(offset);
        }

        /// <summary>Nur die Bühne neu aufbauen: Bedienelemente rechts behalten Fokus und Popups.</summary>
        private void RefreshStage()
        {
            ClearStageFields();
            _stageHost.Content = Current.Stage();
        }

        /// <summary>Gebündelte Änderungen übernehmen: speichern, App benachrichtigen, Bühne zeigt den neuen Stand.</summary>
        private void FlushStage()
        {
            if (!_stageDirty) return;
            _stageDirty = false;
            SaveDesign();
            ConfigChanged?.Invoke();
            RefreshStage();
        }

        private static StackPanel BuildBody(Page page, Action<StackPanel> body)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = page.Title(), FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"), FontSize = 29,
                FontWeight = FontWeights.SemiBold, Foreground = Fg, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
            });
            panel.Children.Add(new TextBlock
            {
                Text = page.Lead(), FontSize = 15, LineHeight = 22, Foreground = SubFg, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18),
            });
            body(panel);
            return panel;
        }

        private StackPanel BuildBody(Page page) => BuildBody(page, page.Body);

        private void StartPageTimer(TimeSpan interval, Action tick)
        {
            StopPageTimer();
            _pageTimer = new DispatcherTimer { Interval = interval };
            _pageTimer.Tick += (_, _) => tick();
            _pageTimer.Start();
        }

        private void StopPageTimer()
        {
            _pageTimer?.Stop();
            _pageTimer = null;
        }

        private static void SlideIn(FrameworkElement element, int dir, int delayMs)
        {
            var move = (TranslateTransform)element.RenderTransform;
            var delay = TimeSpan.FromMilliseconds(delayMs);
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { BeginTime = delay, FillBehavior = FillBehavior.Stop });
            if (dir == 0) { move.BeginAnimation(TranslateTransform.XProperty, null); move.X = 0; return; }
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dir * 18, 0, TimeSpan.FromMilliseconds(300))
            {
                BeginTime = delay, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop,
            });
        }

        /// <summary>Bühne in der Farbe der Seite einfärben (weich überblenden, sonst sofort).</summary>
        private void Tint(string hueHex, bool animate)
        {
            var hue = ((SolidColorBrush)FromHex(hueHex)).Color;
            var bg = Light ? Color.FromRgb(0xF1, 0xF2, 0xF5) : Color.FromRgb(0x17, 0x17, 0x1A);
            void To(GradientStop stop, Color color)
            {
                if (animate) stop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(color, TimeSpan.FromMilliseconds(420)));
                else
                {
                    stop.BeginAnimation(GradientStop.ColorProperty, null);
                    stop.Color = color;
                }
            }
            To(_gradTop, Mix(bg, hue, Light ? 0.20 : 0.26));
            To(_gradBottom, Mix(bg, hue, Light ? 0.04 : 0.05));
            To(_glow, Color.FromArgb((byte)(Light ? 0x48 : 0x50), hue.R, hue.G, hue.B));
        }

        private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        // ── Config ──────────────────────────────────────────────────────────

        private static AppConfig Cfg() => ConfigManager.Load();

        private void Update(Action<AppConfig> change, bool notify = true)
        {
            try
            {
                var cfg = ConfigManager.Load();
                change(cfg);
                ConfigManager.Save(cfg);
                if (notify) ConfigChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Logger.Error($"Einführung speichern: {ex.Message}");
            }
        }

        /// <summary>Einstellung ändern, die die Bühne zeigt: sofort speichern, Bühne und App folgen gebündelt.</summary>
        private void Live(Action<AppConfig> change)
        {
            Update(change, notify: false);
            _stageDirty = true;
        }

        private static AppConfig Clone(AppConfig c) =>
            System.Text.Json.JsonSerializer.Deserialize<AppConfig>(System.Text.Json.JsonSerializer.Serialize(c)) ?? new AppConfig();

        /// <summary>Sprache oder Farbschema geändert: Fenster neu aufbauen, gleiche Seite.</summary>
        private void Rebuild()
        {
            Loc.Apply(Cfg());
            BuildShell();
            foreach (var (seg, page) in _segments.Zip(_pages))
                if (seg.Parent is FrameworkElement hit) hit.ToolTip = page.Title();
            ShowCurrent(0);
        }

        // ── Bausteine ───────────────────────────────────────────────────────

        private static Brush Surface => Light ? UiColor.Brush("#F2FFFFFF") : UiColor.Brush("#D92A2A2E");
        private static Brush SurfaceEdge => Light ? UiColor.Brush("#1A000000") : UiColor.Brush("#24FFFFFF");

        private static DropShadowEffect Soft() => new()
        {
            BlurRadius = 28, ShadowDepth = 8, Direction = 270, Opacity = Light ? 0.16 : 0.42, Color = Colors.Black,
        };

        /// <summary>Vorschau mittig auf der Bühne, darunter eine kleine Beschriftung.</summary>
        private static FrameworkElement OnStage(FrameworkElement visual, string? caption = null) => OnStage(visual, caption, out _);

        private static FrameworkElement OnStage(FrameworkElement visual, string? caption, out TextBlock? captionBlock)
        {
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            visual.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(visual);
            captionBlock = null;
            if (caption != null)
            {
                captionBlock = new TextBlock
                {
                    Text = caption, FontSize = 13, LineHeight = 19, Foreground = SubFg, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 360, Margin = new Thickness(0, 20, 0, 0), HorizontalAlignment = HorizontalAlignment.Center,
                };
                panel.Children.Add(captionBlock);
            }
            return panel;
        }

        /// <summary>Zeile mit Symbol-Kachel, Überschrift und Erklärung.</summary>
        private static Grid Feature(string glyph, string title, string text)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 13) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new Border
            {
                Width = 36, Height = 36, CornerRadius = new CornerRadius(9), Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = glyph, FontFamily = IconFont, FontSize = 16, Foreground = Fg,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            });
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Fg, TextWrapping = TextWrapping.Wrap });
            words.Children.Add(new TextBlock { Text = text, FontSize = 13, LineHeight = 18, Foreground = SubFg, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) });
            Grid.SetColumn(words, 1);
            row.Children.Add(words);
            return row;
        }

        /// <summary>Hinweis-Kasten; warn = wichtig (gelb).</summary>
        private static Border Note(string text, bool warn = false)
        {
            string bg = warn ? (Light ? "#FFF4D6" : "#332B12") : (Light ? "#EDEEF1" : "#29292C");
            string edge = warn ? (Light ? "#EBCF85" : "#57481C") : (Light ? "#D9D9DC" : "#37373B");
            string fg = warn ? (Light ? "#5F4600" : "#F3D98A") : (Light ? "#4A4A4A" : "#C5C5C5");
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock
            {
                Text = warn ? "\uE7BA" : "\uE946", FontFamily = IconFont, FontSize = 14, Foreground = FromHex(fg), Margin = new Thickness(0, 2, 0, 0),
            });
            var words = new TextBlock { Text = text, FontSize = 13, LineHeight = 18, Foreground = FromHex(fg), TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(words, 1);
            row.Children.Add(words);
            return new Border
            {
                Background = FromHex(bg), BorderBrush = FromHex(edge), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 4, 0, 10), Child = row,
            };
        }

        /// <summary>Kleine Zwischenüberschrift im Textbereich.</summary>
        private static TextBlock Heading(string text) => new()
        {
            Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Fg, Margin = new Thickness(0, 4, 0, 7),
        };

        /// <summary>Anzeige „Probier es aus“: wartet (pulsierender Punkt), bis etwas passiert ist (grüner Haken).</summary>
        private sealed class StatusPill : Border
        {
            private readonly TextBlock _text;
            private readonly Ellipse _dot;
            private readonly TextBlock _check;

            public StatusPill(string waiting)
            {
                CornerRadius = new CornerRadius(8);
                Padding = new Thickness(12, 8, 12, 8);
                Margin = new Thickness(0, 2, 0, 12);
                BorderThickness = new Thickness(1);
                _dot = new Ellipse { Width = 8, Height = 8, Fill = Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 10, 0) };
                _check = new TextBlock
                {
                    Text = "\uE73E", FontFamily = IconFont, FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 9, 0), Visibility = Visibility.Collapsed,
                };
                _text = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                var row = new Grid(); // Textspalte begrenzt, damit lange Rückmeldungen umbrechen
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(_dot);
                row.Children.Add(_check);
                Grid.SetColumn(_text, 1);
                row.Children.Add(_text);
                Child = row;
                Waiting(waiting);
            }

            public void Waiting(string text)
            {
                _text.Text = text;
                _text.Foreground = Fg;
                Background = CardBg;
                BorderBrush = CardBorder;
                _check.Visibility = Visibility.Collapsed;
                _dot.Visibility = Visibility.Visible;
                _dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(800)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            }

            public void Done(string text)
            {
                _text.Text = text;
                var fg = FromHex(Light ? "#116B3A" : "#5EE08F");
                _text.Foreground = fg;
                _check.Foreground = fg;
                Background = FromHex(Light ? "#DDF3E5" : "#16321F");
                BorderBrush = FromHex(Light ? "#B4E2C5" : "#245A36");
                _dot.BeginAnimation(OpacityProperty, null);
                _dot.Visibility = Visibility.Collapsed;
                _check.Visibility = Visibility.Visible;
            }
        }

        // ── Tastenanzeige ───────────────────────────────────────────────────

        private static IEnumerable<string> KeyNames(string combo) => (combo ?? "")
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(k => k.ToLowerInvariant() switch
            {
                "ctrl" => T("Strg", "Ctrl"),
                "shift" => T("Umschalt", "Shift"),
                "alt" => "Alt",
                "win" => "Win",
                "space" => T("Leertaste", "Space"),
                _ => k.Length <= 3 ? k.ToUpperInvariant() : char.ToUpperInvariant(k[0]) + k[1..],
            });

        /// <summary>„ctrl+shift+p“ als lesbarer Text: „Strg + Umschalt + P“.</summary>
        internal static string FormatHotkey(string combo) => string.Join(" + ", KeyNames(combo));

        /// <summary>Tastenkombination als kleine Tastenkappen.</summary>
        private static StackPanel KeyCaps(string combo)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            bool first = true;
            foreach (var name in KeyNames(combo))
            {
                if (!first)
                    row.Children.Add(new TextBlock { Text = "+", FontSize = 12, Foreground = SubFg, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                first = false;
                row.Children.Add(KeyCap(name));
            }
            return row;
        }

        private static Border KeyCap(string name) => new()
        {
            Background = FieldBg, BorderBrush = FieldBorder, BorderThickness = new Thickness(1, 1, 1, 2), CornerRadius = new CornerRadius(5),
            Padding = new Thickness(7, 2, 7, 2),
            Child = new TextBlock { Text = name, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Fg },
        };
    }
}
