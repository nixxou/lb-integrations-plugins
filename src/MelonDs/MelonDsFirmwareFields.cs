// The fields of melonDS's Firmware settings window (FirmwareSettingsDialog.ui), in the same order and
// with the same limits - shared by the options window's Firmware tab (a game's own) and the pack's
// configuration window (melonDS's own, MelonDsSettingsPage). Values in and out are the ids of
// MelonDsGameSettings' firmware family, the override excepted: each place has its own box for that.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace LbIntegrations.MelonDs
{
    internal sealed class MelonDsFirmwareFields : Panel
    {
        private static readonly string[] Months =
        {
            "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December",
        };

        private readonly TextBox _user, _message, _mac;
        private readonly ComboBox _language, _day, _month, _colour;
        private readonly GroupBox _userBox, _netBox;

        public MelonDsFirmwareFields()
        {
            Size = new Size(516, 262);

            _userBox = new GroupBox { Text = "User settings", Location = new Point(0, 0), Size = new Size(516, 186) };
            int y = 24;
            void Row(string label, params Control[] controls)
            {
                _userBox.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(12, y + 3) });
                int x = 110;
                foreach (var c in controls) { c.Location = new Point(x, y); x += c.Width + 8; _userBox.Controls.Add(c); }
                y += 31;
            }
            _user = new TextBox { Width = 180, MaxLength = 10 };
            _language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
            _language.Items.AddRange(MelonDsGameSettings.Languages);
            _day = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60 };
            for (int i = 1; i <= 31; i++) _day.Items.Add(i.ToString(CultureInfo.InvariantCulture));
            _month = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 112 };
            _month.Items.AddRange(Months);
            _colour = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, DrawMode = DrawMode.OwnerDrawFixed };
            _colour.Items.AddRange(MelonDsGameSettings.Colours);
            _colour.DrawItem += DrawColour;
            _message = new TextBox { Width = 380, MaxLength = 26 };
            Row("Username:", _user);
            Row("Language:", _language);
            Row("Birthday:", _day, _month);
            Row("Color:", _colour);
            Row("Message:", _message);

            _netBox = new GroupBox { Text = "Network settings", Location = new Point(0, 192), Size = new Size(516, 70) };
            _netBox.Controls.Add(new Label { Text = "MAC address:", AutoSize = true, Location = new Point(12, 27) });
            _mac = new TextBox { Width = 150, MaxLength = 17, Location = new Point(110, 24) };
            _netBox.Controls.Add(_mac);
            _netBox.Controls.Add(new Label { Text = "(leave empty to use default MAC)", AutoSize = true, ForeColor = SystemColors.GrayText, Location = new Point(270, 27) });

            Controls.Add(_userBox);
            Controls.Add(_netBox);
        }

        /// <summary>A small swatch before each name, as melonDS's own list has.</summary>
        private void DrawColour(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index >= 0)
            {
                var swatch = new Rectangle(e.Bounds.Left + 3, e.Bounds.Top + 2, 20, e.Bounds.Height - 4);
                using (var b = new SolidBrush(MelonDsGameSettings.ColourValues[e.Index])) e.Graphics.FillRectangle(b, swatch);
                TextRenderer.DrawText(e.Graphics, MelonDsGameSettings.Colours[e.Index], e.Font,
                                      new Point(swatch.Right + 6, e.Bounds.Top + 1), _colour.Enabled ? e.ForeColor : SystemColors.GrayText);
            }
            e.DrawFocusRectangle();
        }

        public void SetEditable(bool value)
        {
            foreach (Control c in new Control[] { _user, _language, _day, _month, _colour, _message, _mac }) c.Enabled = value;
        }

        /// <summary>Show a set of values - melonDS's default for an id it does not hold.</summary>
        public void ShowValues(Dictionary<string, string> v)
        {
            string Get(string id, string d) => v != null && v.TryGetValue(id, out var x) && x != null ? x : d;
            int I(string id, int d) => int.TryParse(Get(id, null), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : d;
            _user.Text = Get("FwUsername", "melonDS");
            _language.SelectedIndex = Clamp(I("FwLanguage", 1), 0, 5);
            _day.SelectedIndex = Clamp(I("FwBirthdayDay", 1), 1, 31) - 1;
            _month.SelectedIndex = Clamp(I("FwBirthdayMonth", 1), 1, 12) - 1;
            _colour.SelectedIndex = Clamp(I("FwColour", 0), 0, 15);
            _message.Text = Get("FwMessage", "");
            _mac.Text = Get("FwMAC", "");
        }

        private static int Clamp(int v, int min, int max) => Math.Max(min, Math.Min(max, v));

        /// <summary>The values shown - the override not among them.</summary>
        public Dictionary<string, string> Read() => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FwUsername"] = _user.Text,
            ["FwLanguage"] = _language.SelectedIndex.ToString(CultureInfo.InvariantCulture),
            ["FwBirthdayDay"] = (_day.SelectedIndex + 1).ToString(CultureInfo.InvariantCulture),
            ["FwBirthdayMonth"] = (_month.SelectedIndex + 1).ToString(CultureInfo.InvariantCulture),
            ["FwColour"] = _colour.SelectedIndex.ToString(CultureInfo.InvariantCulture),
            ["FwMessage"] = _message.Text,
            ["FwMAC"] = _mac.Text.Trim(),
        };

        /// <summary>What melonDS itself would refuse, in its own words - or null.</summary>
        public string Problem()
            => MelonDsGameSettings.IsMac(_mac.Text.Trim()) ? null
             : "The MAC address you entered isn't valid. It should contain 6 pairs of hexadecimal digits, optionally separated.";
    }
}
