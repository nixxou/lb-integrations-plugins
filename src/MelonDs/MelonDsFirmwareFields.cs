// The fields of melonDS's Firmware settings window (FirmwareSettingsDialog.ui), in the same order and
// with the same limits - shared by the options window's Firmware tab (a game's own) and the pack's
// configuration window (melonDS's own, MelonDsSettingsPage). Values in and out are the ids of
// MelonDsGameSettings' firmware family, the override excepted: each place has its own box for that.
//
// VARIOUS (the options window over several games, Mehdi 06/10 - see LbipSelection): a field the games do not agree on
// shows "Various (n of N)" - a first item of its list, or the cue of an empty text box - and reads back as
// LbipSelection.Various until the user changes it.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using LbIntegrations.Lbip;

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
        private readonly HashSet<Control> _various = new HashSet<Control>();   // a field shown Various
        private bool _filling;

        /// <summary>A field changed by the user.</summary>
        public event EventHandler Changed;

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

            foreach (var box in new[] { _language, _day, _month, _colour })
                box.SelectedIndexChanged += (_, _) => { if (!_filling) Changed?.Invoke(this, EventArgs.Empty); };
            foreach (var box in new[] { _user, _message, _mac })
                box.TextChanged += (_, _) =>
                {
                    if (_filling) return;
                    if (_various.Remove(box)) box.PlaceholderText = "";
                    Changed?.Invoke(this, EventArgs.Empty);
                };
        }

        /// <summary>A small swatch before each name, as melonDS's own list has.</summary>
        private void DrawColour(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            int off = _various.Contains(_colour) ? 1 : 0;
            if (e.Index >= 0 && e.Index < off)
                TextRenderer.DrawText(e.Graphics, _colour.Items[e.Index].ToString(), e.Font, new Point(e.Bounds.Left + 3, e.Bounds.Top + 1),
                                      _colour.Enabled ? e.ForeColor : SystemColors.GrayText);
            else if (e.Index >= 0)
            {
                int i = e.Index - off;
                var swatch = new Rectangle(e.Bounds.Left + 3, e.Bounds.Top + 2, 20, e.Bounds.Height - 4);
                using (var b = new SolidBrush(MelonDsGameSettings.ColourValues[i])) e.Graphics.FillRectangle(b, swatch);
                TextRenderer.DrawText(e.Graphics, MelonDsGameSettings.Colours[i], e.Font,
                                      new Point(swatch.Right + 6, e.Bounds.Top + 1), _colour.Enabled ? e.ForeColor : SystemColors.GrayText);
            }
            e.DrawFocusRectangle();
        }

        public void SetEditable(bool value)
        {
            foreach (Control c in new Control[] { _user, _language, _day, _month, _colour, _message, _mac }) c.Enabled = value;
        }

        /// <summary>Show a set of values - melonDS's default for an id it does not hold. A value LbipSelection.Various shows
        /// as <paramref name="various"/> says it (the options window over several games); without it, as the default.</summary>
        public void ShowValues(Dictionary<string, string> v, Func<string, string> various = null)
        {
            bool Var(string id) => various != null && v != null && v.TryGetValue(id, out var x) && x == LbipSelection.Various;
            string Get(string id, string d) => v != null && v.TryGetValue(id, out var x) && x != null && x != LbipSelection.Various ? x : d;
            int I(string id, int d) => int.TryParse(Get(id, null), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : d;
            void Pick(ComboBox box, string id, int index)
            {
                if (_various.Remove(box)) box.Items.RemoveAt(0);
                if (Var(id)) { box.Items.Insert(0, various(id)); _various.Add(box); box.SelectedIndex = 0; }
                else box.SelectedIndex = index;
            }
            void Put(TextBox box, string id, string d)
            {
                bool many = Var(id);
                _various.Remove(box);
                box.Text = many ? "" : Get(id, d);
                box.PlaceholderText = many ? various(id) : "";
                if (many) _various.Add(box);
            }
            _filling = true;
            Put(_user, "FwUsername", "melonDS");
            Pick(_language, "FwLanguage", Clamp(I("FwLanguage", 1), 0, 5));
            Pick(_day, "FwBirthdayDay", Clamp(I("FwBirthdayDay", 1), 1, 31) - 1);
            Pick(_month, "FwBirthdayMonth", Clamp(I("FwBirthdayMonth", 1), 1, 12) - 1);
            Pick(_colour, "FwColour", Clamp(I("FwColour", 0), 0, 15));
            Put(_message, "FwMessage", "");
            Put(_mac, "FwMAC", "");
            _filling = false;
        }

        /// <summary>Is a field still shown Various: a list on its first item, a text box not typed in.</summary>
        private bool IsVarious(Control c) => _various.Contains(c) && (!(c is ComboBox box) || box.SelectedIndex == 0);

        private int Index(ComboBox box) => box.SelectedIndex - (_various.Contains(box) ? 1 : 0);

        /// <summary>A field is still shown Various.</summary>
        public bool AnyVarious => Read().Values.Any(x => x == LbipSelection.Various);

        private static int Clamp(int v, int min, int max) => Math.Max(min, Math.Min(max, v));

        /// <summary>The values shown - the override not among them.</summary>
        public Dictionary<string, string> Read() => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FwUsername"] = IsVarious(_user) ? LbipSelection.Various : _user.Text,
            ["FwLanguage"] = IsVarious(_language) ? LbipSelection.Various : Index(_language).ToString(CultureInfo.InvariantCulture),
            ["FwBirthdayDay"] = IsVarious(_day) ? LbipSelection.Various : (Index(_day) + 1).ToString(CultureInfo.InvariantCulture),
            ["FwBirthdayMonth"] = IsVarious(_month) ? LbipSelection.Various : (Index(_month) + 1).ToString(CultureInfo.InvariantCulture),
            ["FwColour"] = IsVarious(_colour) ? LbipSelection.Various : Index(_colour).ToString(CultureInfo.InvariantCulture),
            ["FwMessage"] = IsVarious(_message) ? LbipSelection.Various : _message.Text,
            ["FwMAC"] = IsVarious(_mac) ? LbipSelection.Various : _mac.Text.Trim(),
        };

        /// <summary>What melonDS itself would refuse, in its own words - or null.</summary>
        public string Problem()
            => IsVarious(_mac) || MelonDsGameSettings.IsMac(_mac.Text.Trim()) ? null
             : "The MAC address you entered isn't valid. It should contain 6 pairs of hexadecimal digits, optionally separated.";
    }
}
