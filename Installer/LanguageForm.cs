using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace KnobFlyoutSetup
{
    /// <summary>Erster Dialog: Sprache für die Installation wählen (wie bei üblichen Installern).</summary>
    internal sealed class LanguageForm : Form
    {
        private readonly ComboBox _combo;

        public bool English => _combo.SelectedIndex == 1;

        public LanguageForm()
        {
            Text = "KnobFlyout Setup";
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(400, 150);
            Icon = WizardForm.AppIcon;

            Controls.Add(new PictureBox
            {
                Image = WizardForm.AppIcon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(18, 18), Size = new Size(40, 40),
            });
            Controls.Add(new Label
            {
                Text = "Bitte wählen Sie die Sprache für die Installation:\r\nPlease select the language for the installation:",
                Location = new Point(72, 18), Size = new Size(316, 40),
            });
            _combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(72, 66), Width = 316 };
            _combo.Items.AddRange(new object[] { "Deutsch", "English" });
            _combo.SelectedIndex = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? 0 : 1;
            Controls.Add(_combo);

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(220, 108), Size = new Size(80, 28) };
            var cancel = new Button { Text = "Abbrechen / Cancel", DialogResult = DialogResult.Cancel, Location = new Point(306, 108), Size = new Size(82, 28) };
            cancel.AutoSize = true;
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            Shown += (_, __) => cancel.Left = ClientSize.Width - 12 - cancel.Width; // Breite nach AutoSize
            Shown += (_, __) => ok.Left = cancel.Left - 6 - ok.Width;
        }
    }
}
