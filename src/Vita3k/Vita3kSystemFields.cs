// The four system settings a game is told - language, date format, time format, enter button - as
// fields, shared by the Options window's "System" tab (a game's own, Vita3kGameConfig) and the pack's
// configuration window (Vita3K's own, config.yml). The install's small window (Vita3kSystemSettingsForm)
// asks the same four.
//
// A value no entry has (Mehdi, 04/10: a Vita3K update adding a language, a hand-edited config.yml) is shown as it is,
// one more entry, and KEPT: reading the fields gives it back until another entry is chosen - never entry 0 written over it.

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
            LbIntegrations.Lbip.LbipHint.Attach(_pstv, "Vita3K answers the game as a PlayStation TV: each controller on its own port (local multiplayer),\n"
                                            + "no camera, the TV's model and resolution. A game that needs the touch screen or the camera may refuse it.");

            // Japanese goes with the circle button, as on a Japanese console - when chosen, not when shown.
            _language.SelectedIndexChanged += (_, _) => { if (!_showing) _enter.SelectedIndex = _language.SelectedIndex == 0 ? 0 : 1; };
        }

        public void ShowValues(VitaSystemSettings s)
        {
            s ??= new VitaSystemSettings();
            _showing = true;
            Pick(_language, s.Language, Vita3kConfig.Languages);
            Pick(_date, s.DateFormat, Vita3kConfig.DateFormats);
            Pick(_time, s.TimeFormat, Vita3kConfig.TimeFormats);
            Pick(_enter, s.EnterButton, Vita3kConfig.EnterButtons);
            _pstv.Checked = s.Pstv == true;
            _showing = false;
        }

        /// <summary>The entry of <paramref name="value"/>, or - not one of ours - one more entry saying it, chosen.</summary>
        private static void Pick(ComboBox box, int value, string[] entries)
        {
            while (box.Items.Count > entries.Length) box.Items.RemoveAt(box.Items.Count - 1);
            box.Tag = null;
            if (value >= 0 && value < entries.Length) { box.SelectedIndex = value; return; }
            box.Tag = value;
            box.Items.Add("As it is (" + value + ", not in this list)");
            box.SelectedIndex = box.Items.Count - 1;
        }

        /// <summary>The chosen entry's value - the kept one for the "as it is" entry.</summary>
        private static int Value(ComboBox box, string[] entries)
            => box.SelectedIndex >= entries.Length && box.Tag is int kept ? kept : box.SelectedIndex;

        public VitaSystemSettings Read() => new VitaSystemSettings
        {
            Language = Value(_language, Vita3kConfig.Languages), DateFormat = Value(_date, Vita3kConfig.DateFormats),
            TimeFormat = Value(_time, Vita3kConfig.TimeFormats), EnterButton = Value(_enter, Vita3kConfig.EnterButtons), Pstv = _pstv.Checked,
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
