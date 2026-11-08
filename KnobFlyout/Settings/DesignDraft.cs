using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using KnobFlyout.Config;
using KnobFlyout.Osd;
using static KnobFlyout.Loc;
using static KnobFlyout.Settings.UiHelpers;

namespace KnobFlyout.Settings
{
    /// <summary>
    /// Arbeitskopie für „Design anpassen“: Was man dort ändert, ist nur ein Entwurf. Die Voreinstellung bleibt, wie sie ist;
    /// gespeichert wird als eigenes Design (SaveTo), beim Verlassen ohne Speichern fragt der Aufrufer nach (DesignSaveDialog).
    /// </summary>
    internal sealed class DesignDraft
    {
        public AppConfig Cfg { get; }
        private string _media = "", _osd = "";

        public DesignDraft(AppConfig source)
        {
            Cfg = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(source)) ?? new AppConfig();
            Rebase();
        }

        private string MediaKey => JsonSerializer.Serialize(SavedMediaDesign.From(Cfg, ""));
        private string OsdKey => JsonSerializer.Serialize(SavedOsdDesign.From(Cfg, ""));

        public bool Dirty => MediaKey != _media || OsdKey != _osd;

        /// <summary>Der jetzige Stand gilt als gespeichert.</summary>
        public void Rebase()
        {
            _media = MediaKey;
            _osd = OsdKey;
        }

        /// <summary>Name fürs Speichern: das gerade bearbeitete eigene Design, sonst „Mein …“ ohne Dopplung.</summary>
        public string SuggestedName()
        {
            bool osd = OsdKey != _osd && MediaKey == _media; // nur das OSD geändert: nach dem OSD-Design benennen
            string active = osd ? Cfg.OsdSavedActive : Cfg.MediaSavedActive;
            if (active.Length > 0) return active;
            string design = (osd ? Cfg.OsdDesign : Cfg.MediaDesign) switch
            {
                "modernflyouts" or "modern" or "windows" => "ModernFlyouts",
                "win11" => "Windows 11",
                "custom" or "dark" => T("Eigenes", "Custom"),
                var key => Designs.Get(key)?.Name ?? key,
            };
            string first = T($"Mein {design}", $"My {design}");
            string name = first;
            bool Taken(string n) => osd ? Cfg.OsdSavedDesigns.Any(s => string.Equals(s.Name, n, StringComparison.OrdinalIgnoreCase))
                : Cfg.MediaSavedDesigns.Any(s => string.Equals(s.Name, n, StringComparison.OrdinalIgnoreCase));
            for (int i = 2; Taken(name); i++) name = $"{first} {i}";
            return name;
        }

        /// <summary>Entwurf als eigenes Design in die Listen der Ziele schreiben (gleicher Name überschreibt) und dort auswählen. Das OSD nur, wenn es sich geändert hat.</summary>
        public void SaveTo(string name, params AppConfig[] targets)
        {
            bool osd = OsdKey != _osd;
            bool media = MediaKey != _media || !osd;
            var mediaEntry = SavedMediaDesign.From(Cfg, name);
            var osdEntry = SavedOsdDesign.From(Cfg, name);
            foreach (var target in targets)
            {
                if (media)
                {
                    var entry = JsonSerializer.Deserialize<SavedMediaDesign>(JsonSerializer.Serialize(mediaEntry))!;
                    int index = target.MediaSavedDesigns.FindIndex(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0) target.MediaSavedDesigns[index] = entry;
                    else target.MediaSavedDesigns.Add(entry);
                    entry.ApplyTo(target);
                }
                if (osd)
                {
                    var entry = JsonSerializer.Deserialize<SavedOsdDesign>(JsonSerializer.Serialize(osdEntry))!;
                    int index = target.OsdSavedDesigns.FindIndex(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0) target.OsdSavedDesigns[index] = entry;
                    else target.OsdSavedDesigns.Add(entry);
                    entry.ApplyTo(target);
                }
            }
        }
    }

    internal enum LeaveChoice { Save, Discard, Stay }

    /// <summary>Nachfrage beim Verlassen mit ungespeicherten Änderungen und Namensfeld fürs eigene Design.</summary>
    internal static class DesignSaveDialog
    {
        /// <param name="leaving">Beim Verlassen: mit „Ohne Speichern verlassen“; sonst nur Speichern und Abbrechen.</param>
        public static (LeaveChoice Choice, string Name) AskDesign(Window? owner, string suggested, bool leaving)
        {
            var name = TextField(suggested);
            name.MaxLength = 32;
            name.Margin = new Thickness(0, 0, 0, 16);
            string text = T("Gespeichert wird als eigenes Design, die Voreinstellung bleibt, wie sie ist. Ein vorhandener Name wird überschrieben.",
                "It is saved as your own design, the preset stays as it is. An existing name is overwritten.");
            string lead = leaving ? T("Du hast das Design geändert und noch nicht gespeichert.", "You changed the design and haven't saved it yet.") + " " : "";
            var choice = Show(owner, T("KnobFlyout – Design speichern", "KnobFlyout – Save design"),
                leaving ? T("Änderungen speichern?", "Save your changes?") : T("Als eigenes Design speichern", "Save as your own design"),
                lead + text, name, leaving ? T("Ohne Speichern verlassen", "Leave without saving") : null);
            return (choice, name.Text.Trim());
        }

        /// <summary>Rückfrage vor dem Löschen eines Farbprofils bzw. eigenen Designs. true = löschen.</summary>
        public static bool ConfirmDelete(Window? owner, string name, bool design) => Show(owner, T("KnobFlyout – Löschen", "KnobFlyout – Delete"),
            T("Wirklich löschen?", "Really delete?"),
            design ? T($"Das eigene Design „{name}“ wird gelöscht.", $"Your own design “{name}” will be deleted.")
                   : T($"Das Farbprofil „{name}“ wird gelöscht.", $"The color profile “{name}” will be deleted."),
            null, null, T("Löschen", "Delete")) == LeaveChoice.Save;

        /// <summary>Name für ein neues Farbprofil; null = abgebrochen.</summary>
        public static string? AskProfileName(Window? owner)
        {
            var name = TextField("");
            name.MaxLength = 32;
            name.Margin = new Thickness(0, 0, 0, 16);
            var choice = Show(owner, T("KnobFlyout – Farbprofil speichern", "KnobFlyout – Save color profile"), T("Als Farbprofil speichern", "Save as color profile"),
                T("Die Farben stehen danach bei OSD und Flyout unter „Farbprofil“. Ein vorhandener Name wird überschrieben.",
                  "The colors are then listed under “Color profile” for the OSD and the flyout. An existing name is overwritten."), name, null);
            return choice == LeaveChoice.Save && name.Text.Trim().Length > 0 ? name.Text.Trim() : null;
        }

        public static LeaveChoice AskSettings(Window? owner) => Show(owner, T("KnobFlyout – Einstellungen speichern", "KnobFlyout – Save settings"),
            T("Änderungen speichern?", "Save your changes?"), T("Du hast Einstellungen geändert, die noch nicht gespeichert sind.", "You changed settings that haven't been saved yet."),
            null, T("Ohne Speichern schließen", "Close without saving"));

        private static LeaveChoice Show(Window? owner, string title, string heading, string text, TextBox? input, string? discardText, string? saveText = null)
        {
            var result = LeaveChoice.Stay;
            var window = new Window
            {
                Title = title, Width = 480, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Background = ContentBg,
                WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            };
            if (owner is { IsVisible: true }) window.Owner = owner;
            ApplyControlTheme(window);
            window.SourceInitialized += (_, _) => ApplyDarkTitleBar(window);

            var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            panel.Children.Add(new TextBlock { Text = heading, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Fg, Margin = new Thickness(0, 0, 0, 8) });
            panel.Children.Add(new TextBlock { Text = text, Foreground = SubFg, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
            if (input != null) panel.Children.Add(input);

            void Finish(LeaveChoice choice)
            {
                result = choice;
                window.Close();
            }
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var save = AccentBtn(saveText ?? T("Speichern", "Save"));
            save.IsDefault = true;
            save.Tag = "save";
            save.Click += (_, _) =>
            {
                if (input != null && input.Text.Trim().Length == 0)
                {
                    input.Focus();
                    return;
                }
                Finish(LeaveChoice.Save);
            };
            buttons.Children.Add(save);
            if (discardText != null)
            {
                var discard = Btn(discardText);
                discard.Margin = new Thickness(8, 0, 0, 0);
                discard.Tag = "discard";
                discard.Click += (_, _) => Finish(LeaveChoice.Discard);
                buttons.Children.Add(discard);
            }
            var cancel = Btn(T("Abbrechen", "Cancel"));
            cancel.Margin = new Thickness(8, 0, 0, 0);
            cancel.IsCancel = true;
            cancel.Tag = "stay";
            cancel.Click += (_, _) => Finish(LeaveChoice.Stay);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            window.Content = panel;
            if (input != null)
                window.Loaded += (_, _) =>
                {
                    input.Focus();
                    input.SelectAll();
                };
            window.ShowDialog();
            return result;
        }
    }
}
