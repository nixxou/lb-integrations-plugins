// The four settings Vita3K tells its games - language, date, time, enter button - in a small window,
// opened when the user answers No to the install's notification (Vita3kPlugin, Vita3kNotify.Ask). What it
// saves goes straight into portable\config.yml (Vita3kConfig.Write): the emulator reads it at its next
// start, and nothing of the console has to be rebuilt for it (see Vita3kConfig's header).
//
// MODAL TO THE HOST'S MAIN WINDOW, and never to whatever is in front. Measured 28/09 in LaunchBox: shown
// with no owner, Windows made the notification toast - the active window at the click - its owner, and
// when the toast faded Windows destroyed the windows it owned: ours closed by itself seconds later,
// answering "Cancel". So the owner is the host's main window (LaunchBox's WPF MainWindow, or the
// process's main window - LiteBox), and the window opens once the notification's click has returned.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace LbIntegrations.Vita3k
{
    internal sealed class Vita3kSystemSettingsForm : Form
    {
        private readonly ComboBox _language, _date, _time, _enter;

        public Vita3kSystemSettingsForm(VitaSystemSettings current)
        {
            Text = "Nixx-Vita3K - system settings";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(380, 214);

            var intro = new Label
            {
                Text = "What Vita3K tells the games. The library keeps naming games in US English.",
                Location = new Point(14, 12), Size = new Size(352, 34),
            };
            Controls.Add(intro);

            int y = 52;
            ComboBox Row(string label, string[] items, int value)
            {
                Controls.Add(new Label { Text = label, Location = new Point(14, y + 3), AutoSize = true });
                var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(130, y), Width = 236 };
                box.Items.AddRange(items);
                box.SelectedIndex = value >= 0 && value < items.Length ? value : 0;
                Controls.Add(box);
                y += 30;
                return box;
            }
            _language = Row("Language", Vita3kConfig.Languages, current.Language);
            _date = Row("Date format", Vita3kConfig.DateFormats, current.DateFormat);
            _time = Row("Time format", Vita3kConfig.TimeFormats, current.TimeFormat);
            _enter = Row("Enter button", Vita3kConfig.EnterButtons, current.EnterButton);

            // Japanese goes with the circle button, as on a Japanese console - changed with the language,
            // and still free to be changed back.
            _language.SelectedIndexChanged += (_, _) => _enter.SelectedIndex = _language.SelectedIndex == 0 ? 0 : 1;

            var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Location = new Point(196, y + 8), Width = 82 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(284, y + 8), Width = 82 };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public VitaSystemSettings Chosen => new VitaSystemSettings
        {
            Language = _language.SelectedIndex, DateFormat = _date.SelectedIndex,
            TimeFormat = _time.SelectedIndex, EnterButton = _enter.SelectedIndex,
        };

        /// <summary>Open it from a notification's button: once the click has returned, modal to the
        /// host's main window - see the header.</summary>
        public static void EditLater(Vita3kLayout layout)
        {
            try
            {
                var app = System.Windows.Application.Current;
                if (app != null) { app.Dispatcher.BeginInvoke(new Action(() => Edit(layout))); return; }
            }
            catch { }
            Edit(layout);
        }

        /// <summary>Show it over the settings in config.yml, and save what is chosen. True when saved.</summary>
        public static bool Edit(Vita3kLayout layout)
        {
            try
            {
                using var form = new Vita3kSystemSettingsForm(Vita3kConfig.Read(layout));
                var owner = HostWindow();
                if (owner != null) form.StartPosition = FormStartPosition.CenterParent;
                if (form.ShowDialog(owner) != DialogResult.OK) { Log.Info("system settings: left as they were"); return false; }
                var chosen = form.Chosen;
                bool saved = Vita3kConfig.Write(layout, chosen);
                Log.Info("system settings: " + (saved ? "saved - " + chosen : "could not be saved"));
                return saved;
            }
            catch (Exception ex) { Log.Warn("system settings: the window failed", ex); return false; }
        }

        /// <summary>The host's main window: LaunchBox's WPF MainWindow, else the process's main window.</summary>
        private static IWin32Window HostWindow()
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                var main = System.Windows.Application.Current?.MainWindow;
                if (main != null) handle = new System.Windows.Interop.WindowInteropHelper(main).Handle;
            }
            catch { }
            if (handle == IntPtr.Zero)
                try { handle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle; } catch { }
            return handle == IntPtr.Zero ? null : new Owner(handle);
        }

        private sealed class Owner : IWin32Window
        {
            public Owner(IntPtr handle) { Handle = handle; }
            public IntPtr Handle { get; }
        }
    }
}
