using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ModernFlyoutSetup
{
    /// <summary>
    /// Installations-Assistent wie bei üblichen Programmen: Willkommen → Zielordner (der Ordner „ModernFlyout“ wird
    /// automatisch angelegt) → Optionen → Bereit → Installation → Fertig. Kopfzeile mit Titel und App-Symbol, unten
    /// „Zurück“ / „Weiter“ / „Abbrechen“.
    /// </summary>
    internal sealed class WizardForm : Form
    {
        public static readonly Icon AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        private const int Welcome = 0, Folder = 1, Options = 2, Ready = 3, Progress = 4, Finish = 5;

        private readonly Label _title = new Label();
        private readonly Label _subtitle = new Label();
        private readonly Panel _content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 12) };
        private readonly Button _back = new Button(), _next = new Button(), _cancel = new Button();
        private readonly List<Panel> _pages = new List<Panel>();
        private readonly string _existing = InstallEngine.ExistingInstall();
        private readonly long _size = InstallEngine.PayloadSize();
        private int _page;
        private bool _installing, _finished, _restarting;

        private TextBox _folder;
        private Label _folderInfo, _summary, _status;
        private CheckBox _desktop, _startMenu, _autostart, _launch;
        private ProgressBar _bar;

        public WizardForm(SetupArgs args)
        {
            Text = L.T("ModernFlyout Setup", "ModernFlyout Setup");
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(580, 420);
            Icon = AppIcon;

            Controls.Add(_content);
            Controls.Add(BuildBottomBar());
            Controls.Add(BuildHeader());

            _pages.Add(BuildWelcome());
            _pages.Add(BuildFolder(args.Dir));
            _pages.Add(BuildOptions(args));
            _pages.Add(BuildReady());
            _pages.Add(BuildProgress());
            _pages.Add(BuildFinish());
            foreach (var p in _pages)
            {
                p.Dock = DockStyle.Fill;
                p.Visible = false;
                _content.Controls.Add(p);
            }

            FormClosing += OnClosing;
            // Nach dem Neustart als Administrator direkt zur Zusammenfassung.
            ShowPage(args.Dir != null ? Ready : Welcome);
        }

        // ── Aufbau ──────────────────────────────────────────────────────────

        private Control BuildHeader()
        {
            // Breite gleich setzen: rechts verankerte Elemente rechnen sonst mit der Standardbreite und landen außerhalb.
            var header = new Panel { Width = ClientSize.Width, Height = 70, Dock = DockStyle.Top, BackColor = SystemColors.Window };
            _title.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
            _title.Location = new Point(20, 14);
            _title.AutoSize = true;
            _subtitle.Location = new Point(32, 40);
            _subtitle.AutoSize = true;
            header.Controls.Add(_title);
            header.Controls.Add(_subtitle);
            header.Controls.Add(new PictureBox
            {
                Image = AppIcon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(44, 44),
                Location = new Point(ClientSize.Width - 64, 13), Anchor = AnchorStyles.Top | AnchorStyles.Right,
            });
            var line = new Label { Dock = DockStyle.Bottom, Height = 2, BorderStyle = BorderStyle.Fixed3D };
            header.Controls.Add(line);
            return header;
        }

        private Control BuildBottomBar()
        {
            var bar = new Panel { Width = ClientSize.Width, Height = 52, Dock = DockStyle.Bottom };
            bar.Controls.Add(new Label { Dock = DockStyle.Top, Height = 2, BorderStyle = BorderStyle.Fixed3D });
            foreach (var b in new[] { _back, _next, _cancel })
            {
                b.Size = new Size(96, 28);
                b.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                b.Top = 14;
                bar.Controls.Add(b);
            }
            _cancel.Left = ClientSize.Width - 12 - _cancel.Width;
            _next.Left = _cancel.Left - 12 - _next.Width;
            _back.Left = _next.Left - 6 - _back.Width;
            _back.Text = L.T("< Zurück", "< Back");
            _cancel.Text = L.T("Abbrechen", "Cancel");
            _back.Click += (_, __) => ShowPage(_page == Ready ? Options : _page - 1);
            _next.Click += (_, __) => Next();
            _cancel.Click += (_, __) => Close();
            AcceptButton = _next;
            return bar;
        }

        private static Panel Page() => new Panel();

        private static Label AddText(Panel page, string text, int top, int height = 0)
        {
            var label = new Label { Text = text, Location = new Point(0, top), Width = 520, AutoSize = height == 0, MaximumSize = new Size(520, 0) };
            if (height > 0) { label.AutoSize = false; label.Height = height; }
            page.Controls.Add(label);
            return label;
        }

        private Panel BuildWelcome()
        {
            var page = Page();
            string text = L.T(
                $"Dieser Assistent installiert ModernFlyout {InstallEngine.Version} – Lautstärke pro App mit OSD, Media-Flyout und Profilen.\r\n\r\n" +
                "Administratorrechte sind nicht nötig, solange der Zielordner in Ihrem Benutzerprofil liegt.\r\n\r\n",
                $"This wizard installs ModernFlyout {InstallEngine.Version} – per-app volume with OSD, media flyout and profiles.\r\n\r\n" +
                "No administrator rights are needed as long as the target folder is inside your user profile.\r\n\r\n");
            if (_existing != null)
                text += L.T("Eine vorhandene Installation wird aktualisiert – Ihre Einstellungen bleiben erhalten.\r\n\r\n",
                    "An existing installation will be updated – your settings are kept.\r\n\r\n");
            text += L.T("Klicken Sie auf „Weiter“, um fortzufahren.", "Click “Next” to continue.");
            AddText(page, text, 0);
            return page;
        }

        private Panel BuildFolder(string dir)
        {
            var page = Page();
            AddText(page, L.T("ModernFlyout wird in den folgenden Ordner installiert. Zum Ändern einen anderen Ordner eingeben oder „Durchsuchen …“ klicken.",
                "ModernFlyout will be installed into the following folder. To change it, enter another folder or click “Browse …”."), 0);
            _folder = new TextBox
            {
                Location = new Point(0, 52), Width = 412,
                Text = dir ?? (_existing != null ? Path.GetDirectoryName(_existing) : InstallEngine.DefaultBaseDir),
            };
            var browse = new Button { Text = L.T("Durchsuchen …", "Browse …"), Location = new Point(420, 50), Size = new Size(100, 26) };
            browse.Click += (_, __) =>
            {
                using (var dialog = new FolderBrowserDialog
                {
                    Description = L.T("Ordner wählen – darin wird der Ordner „ModernFlyout“ angelegt.", "Choose a folder – the folder “ModernFlyout” is created inside it."),
                    ShowNewFolderButton = true,
                })
                {
                    if (Directory.Exists(_folder.Text)) dialog.SelectedPath = _folder.Text;
                    if (dialog.ShowDialog(this) == DialogResult.OK) _folder.Text = dialog.SelectedPath;
                }
            };
            page.Controls.Add(_folder);
            page.Controls.Add(browse);
            _folderInfo = AddText(page, "", 92);
            _folder.TextChanged += (_, __) => UpdateFolderInfo();
            UpdateFolderInfo();
            return page;
        }

        private void UpdateFolderInfo()
        {
            string target = InstallEngine.TargetDir(_folder.Text);
            string size = _size > 0 ? $"{_size / (1024 * 1024)} MB" : "–";
            _folderInfo.Text = L.T(
                $"Installiert wird nach:\r\n{target}\r\n\r\nDer Ordner „ModernFlyout“ wird dort automatisch angelegt.\r\n\r\nBenötigter Speicherplatz: {size}",
                $"Installing to:\r\n{target}\r\n\r\nThe folder “ModernFlyout” is created there automatically.\r\n\r\nRequired disk space: {size}");
        }

        private Panel BuildOptions(SetupArgs args)
        {
            var page = Page();
            AddText(page, L.T("Welche zusätzlichen Aufgaben sollen ausgeführt werden?", "Which additional tasks should be performed?"), 0);
            _desktop = Check(page, L.T("Verknüpfung auf dem Desktop", "Desktop shortcut"), 36, args.Desktop ?? true);
            _startMenu = Check(page, L.T("Eintrag im Startmenü", "Start menu entry"), 64, args.StartMenu ?? true);
            _autostart = Check(page, L.T("Mit Windows starten (Autostart)", "Start with Windows (autostart)"), 92, args.Autostart ?? true);
            return page;
        }

        private static CheckBox Check(Panel page, string text, int top, bool value)
        {
            var box = new CheckBox { Text = text, Location = new Point(4, top), AutoSize = true, Checked = value };
            page.Controls.Add(box);
            return box;
        }

        private Panel BuildReady()
        {
            var page = Page();
            AddText(page, L.T("Das Setup ist bereit. Klicken Sie auf „Installieren“.", "Setup is ready. Click “Install”."), 0);
            _summary = AddText(page, "", 32);
            return page;
        }

        private Panel BuildProgress()
        {
            var page = Page();
            _status = AddText(page, L.T("Bitte warten …", "Please wait …"), 0, 36);
            _bar = new ProgressBar { Location = new Point(0, 44), Width = 520, Height = 22, Minimum = 0, Maximum = 100 };
            page.Controls.Add(_bar);
            return page;
        }

        private Panel BuildFinish()
        {
            var page = Page();
            AddText(page, L.T("ModernFlyout wurde erfolgreich installiert.\r\n\r\nDas Programm läuft im Infobereich der Taskleiste (neben der Uhr); dort öffnen Sie auch die Einstellungen.",
                "ModernFlyout has been installed successfully.\r\n\r\nThe program runs in the notification area of the taskbar (next to the clock); the settings are opened from there."), 0);
            _launch = Check(page, L.T("ModernFlyout jetzt starten", "Launch ModernFlyout now"), 96, true);
            return page;
        }

        // ── Ablauf ──────────────────────────────────────────────────────────

        private void ShowPage(int page)
        {
            _page = page;
            for (int i = 0; i < _pages.Count; i++) _pages[i].Visible = i == page;
            (string title, string subtitle) = page switch
            {
                Welcome => (L.T("Willkommen", "Welcome"), L.T("Setup von ModernFlyout", "ModernFlyout setup")),
                Folder => (L.T("Zielordner wählen", "Choose the destination folder"), L.T("Wohin soll ModernFlyout installiert werden?", "Where should ModernFlyout be installed?")),
                Options => (L.T("Zusätzliche Aufgaben", "Additional tasks"), L.T("Verknüpfungen und Autostart", "Shortcuts and autostart")),
                Ready => (L.T("Bereit zur Installation", "Ready to install"), L.T("Bitte die Angaben prüfen", "Please check the settings")),
                Progress => (L.T("Installation läuft", "Installing"), L.T("ModernFlyout wird installiert …", "ModernFlyout is being installed …")),
                _ => (L.T("Fertig", "Completed"), L.T("Die Installation ist abgeschlossen", "Setup has finished")),
            };
            _title.Text = title;
            _subtitle.Text = subtitle;

            if (page == Ready)
            {
                string yes = L.T("Ja", "Yes"), no = L.T("Nein", "No");
                _summary.Text = L.T("Zielordner:", "Destination folder:") + "\r\n      " + InstallEngine.TargetDir(_folder.Text) + "\r\n\r\n" +
                    L.T("Desktop-Verknüpfung: ", "Desktop shortcut: ") + (_desktop.Checked ? yes : no) + "\r\n" +
                    L.T("Startmenü-Eintrag: ", "Start menu entry: ") + (_startMenu.Checked ? yes : no) + "\r\n" +
                    L.T("Mit Windows starten: ", "Start with Windows: ") + (_autostart.Checked ? yes : no);
            }

            _back.Visible = page != Welcome && page != Progress && page != Finish;
            _cancel.Visible = page != Finish;
            _cancel.Enabled = page != Progress;
            _next.Enabled = page != Progress;
            _next.Text = page == Ready ? L.T("Installieren", "Install") : page == Finish ? L.T("Fertigstellen", "Finish") : L.T("Weiter >", "Next >");
        }

        private void Next()
        {
            switch (_page)
            {
                case Folder:
                    string target = InstallEngine.TargetDir(_folder.Text);
                    if (target.Length == 0 || !Path.IsPathRooted(target) || target.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                    {
                        MessageBox.Show(this, L.T("Bitte einen gültigen Ordner angeben.", "Please enter a valid folder."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    ShowPage(Options);
                    break;
                case Ready:
                    StartInstall();
                    break;
                case Finish:
                    if (_launch.Checked) Launch();
                    _finished = true;
                    Close();
                    break;
                default:
                    ShowPage(_page + 1);
                    break;
            }
        }

        private async void StartInstall()
        {
            if (!InstallEngine.HasPayload)
            {
                MessageBox.Show(this, L.T("Dieses Setup enthält keine Programmdateien (bitte im Release-Modus bauen).",
                    "This setup contains no program files (build it in Release mode)."), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            var options = new InstallOptions
            {
                TargetDir = InstallEngine.TargetDir(_folder.Text),
                Desktop = _desktop.Checked, StartMenu = _startMenu.Checked, Autostart = _autostart.Checked,
            };

            // Ordner ohne Schreibrechte (z. B. „Programme“): als Administrator neu starten, Angaben mitgeben.
            if (!InstallEngine.CanWrite(options.TargetDir))
            {
                var answer = MessageBox.Show(this, L.T("Für diesen Ordner braucht das Setup Administratorrechte.\r\n\r\nSetup jetzt als Administrator neu starten?",
                    "Setup needs administrator rights for this folder.\r\n\r\nRestart setup as administrator now?"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) return;
                try
                {
                    Process.Start(new ProcessStartInfo(Application.ExecutablePath,
                        SetupArgs.Build(_folder.Text, options.Desktop, options.StartMenu, options.Autostart)) { Verb = "runas", UseShellExecute = true });
                    _restarting = true;
                    Close();
                }
                catch (System.ComponentModel.Win32Exception) { /* Nachfrage abgelehnt */ }
                return;
            }

            // Laufende Version beenden (Datei sonst gesperrt); ihren Ordner merken, um die Einstellungen zu übernehmen.
            var running = InstallEngine.RunningApps();
            var folders = running.Select(InstallEngine.FolderOf).ToList();
            if (running.Count > 0)
            {
                var answer = MessageBox.Show(this, L.T("ModernFlyout läuft gerade und wird für die Installation beendet.", "ModernFlyout is running and will be closed for the installation."),
                    Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                if (answer != DialogResult.OK) return;
                foreach (var p in running)
                {
                    try { p.Kill(); p.WaitForExit(5000); } catch { /* schon beendet */ }
                }
            }

            ShowPage(Progress);
            _installing = true;
            int lastPercent = -1;
            void Report(int percent, string status)
            {
                if (percent == lastPercent && status == null) return;
                lastPercent = percent;
                BeginInvoke(new Action(() =>
                {
                    _bar.Value = Math.Max(0, Math.Min(100, percent));
                    if (status != null) _status.Text = status;
                }));
            }
            try
            {
                await Task.Run(() => InstallEngine.Install(options, folders, Report));
                _installing = false;
                ShowPage(Finish);
            }
            catch (Exception ex)
            {
                _installing = false;
                MessageBox.Show(this, L.T("Die Installation ist fehlgeschlagen:\r\n\r\n", "Installation failed:\r\n\r\n") + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                ShowPage(Ready);
            }
        }

        /// <summary>App starten – über den Explorer, damit sie ohne Administratorrechte läuft, auch wenn das Setup erhöht lief.</summary>
        private void Launch()
        {
            string exe = Path.Combine(InstallEngine.TargetDir(_folder.Text), InstallEngine.ExeName);
            try { Process.Start("explorer.exe", "\"" + exe + "\""); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (_finished || _restarting || _page == Finish) return;
            if (_installing) { e.Cancel = true; return; }
            var answer = MessageBox.Show(this, L.T("Setup wirklich abbrechen?", "Really cancel setup?"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) e.Cancel = true;
        }
    }
}
