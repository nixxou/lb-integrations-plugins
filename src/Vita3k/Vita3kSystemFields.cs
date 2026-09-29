// The four system settings a game is told - language, date format, time format, enter button - as
// fields, shared by the Options window's "System" tab (a game's own, Vita3kGameConfig) and the pack's
// configuration window (Vita3K's own, config.yml). The install's small window (Vita3kSystemSettingsForm)
// asks the same four.

using System.Drawing;
using System.Windows.Forms;

namespace LbIntegrations.Vita3k
{
    internal sealed class Vita3kSystemFields : Panel
    {
        private readonly ComboBox _language, _date, _time, _enter;
        private readonly CheckBox _pstv;
        private bool _showing;

        public Vita3kSystemFields()
        {
            Size = new Size(380, 152);
            int y = 0;
            ComboBox Row(string label, string[] items)
            {
                Controls.Add(new Label { Text = label, Location = new Point(0, y + 3), AutoSize = true });
                var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(116, y), Width = 250 };
                box.Items.AddRange(items);
                Controls.Add(box);
                y += 31;
                return box;
            }
            _language = Row("Language:", Vita3kConfig.Languages);
            _date = Row("Date format:", Vita3kConfig.DateFormats);
            _time = Row("Time format:", Vita3kConfig.TimeFormats);
            _enter = Row("Enter button:", Vita3kConfig.EnterButtons);
            _pstv = new CheckBox { Text = "PlayStation TV mode (PSTV)", AutoSize = true, Location = new Point(116, y + 2) };
            Controls.Add(_pstv);
            new ToolTip().SetToolTip(_pstv, "Vita3K answers the game as a PlayStation TV: each controller on its own port (local multiplayer),\n"
                                            + "no camera, the TV's model and resolution. A game that needs the touch screen or the camera may refuse it.");

            // Japanese goes with the circle button, as on a Japanese console - when chosen, not when shown.
            _language.SelectedIndexChanged += (_, _) => { if (!_showing) _enter.SelectedIndex = _language.SelectedIndex == 0 ? 0 : 1; };
        }

        public void ShowValues(VitaSystemSettings s)
        {
            s ??= new VitaSystemSettings();
            _showing = true;
            _language.SelectedIndex = Pick(s.Language, Vita3kConfig.Languages.Length);
            _date.SelectedIndex = Pick(s.DateFormat, Vita3kConfig.DateFormats.Length);
            _time.SelectedIndex = Pick(s.TimeFormat, Vita3kConfig.TimeFormats.Length);
            _enter.SelectedIndex = Pick(s.EnterButton, Vita3kConfig.EnterButtons.Length);
            _pstv.Checked = s.Pstv == true;
            _showing = false;
        }

        private static int Pick(int value, int count) => value >= 0 && value < count ? value : 0;

        public VitaSystemSettings Read() => new VitaSystemSettings
        {
            Language = _language.SelectedIndex, DateFormat = _date.SelectedIndex,
            TimeFormat = _time.SelectedIndex, EnterButton = _enter.SelectedIndex, Pstv = _pstv.Checked,
        };

        public void SetEditable(bool value)
        {
            foreach (var c in new Control[] { _language, _date, _time, _enter, _pstv }) c.Enabled = value;
        }

        /// <summary>The four, in a form a group of games can be told apart by.</summary>
        internal static string Key(VitaSystemSettings s)
            => s == null ? "" : s.Language + "/" + s.DateFormat + "/" + s.TimeFormat + "/" + s.EnterButton + "/" + (s.Pstv == true ? "tv" : "");
    }
}
