// The profile and the console of one Xenia Canary install, edited: the Nixx window's Xenia tab shows it, and so
// does the window the install's notification opens on "Change..." (XeniaSetup).
//
// Both are the WHOLE console's, never a game's: the profile is who signs in (XeniaProfile), the console settings are
// xconfig.settings (XeniaConsole), which Xenia greys out while a game runs. They are written to Xenia's own files -
// the one place this pack does so - and only while that Xenia is closed: it reads them at start and rewrites its
// TOML at exit, so a write while it runs would be lost or fought over.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LbIntegrations.Xenia
{
    internal sealed class XeniaConsolePanel : FlowLayoutPanel
    {
        private readonly string _exe;
        private readonly XeniaLayout _layout;
        private readonly XeniaProfileInfo _profile;      // the one signed in at start, else the first; null for none
        private readonly bool _signedIn;
        private bool _zoneTouched;
        private readonly XeniaConsoleValues _read;

        private readonly TextBox _gamertag;
        private readonly Label _gamertagState;
        private readonly ComboBox _language, _country, _zone, _region, _resolution;
        private readonly CheckBox _hour24, _dstOff;
        private readonly TextBox _volume;

        private readonly List<(byte Value, string Name)> _countries;

        public XeniaConsolePanel(string exe)
        {
            _exe = exe;
            _layout = XeniaPaths.Resolve(exe);
            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = Padding.Empty;

            // ── the profile ──
            var profiles = XeniaProfile.Find(_layout.ContentRoot);
            var signed = XeniaProfile.SignedIn(_layout.ConfigFile);
            _profile = profiles.FirstOrDefault(p => p.Xuid == signed) ?? profiles.FirstOrDefault();
            _signedIn = _profile != null && _profile.Xuid == signed;

            var profileBox = Group("Profile");
            var pt = Table(150, 340);
            _gamertag = new TextBox { Width = 200, MaxLength = 15, Text = _profile?.Gamertag ?? XeniaProfile.GamertagFrom(), Margin = new Padding(3, 3, 0, 0) };
            _gamertagState = new Label { AutoSize = true, MaximumSize = new Size(470, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 2, 0, 6) };
            _gamertag.TextChanged += (_, _) => ShowGamertag();
            AddRow(pt, "Gamertag", _gamertag);
            AddWide(pt, _gamertagState);
            ShowGamertag();
            profileBox.Controls.Add(pt);
            Controls.Add(profileBox);

            // ── the console ──
            _read = XeniaConsole.Read(_layout.StorageRoot);
            var consoleBox = Group("Console (every game)");
            var ct = Table(150, 340);

            // A value not in our lists (Mehdi, 04/10: a Xenia update, a code our tables lack) is shown as it is and KEPT -
            // never the first entry written back behind the user's back. Only choosing another entry changes it.
            _language = Combo(XeniaConsole.Languages.Select(l => l.Name));
            Select(_language, Array.FindIndex(XeniaConsole.Languages, l => l.Value == _read.Language), "language " + _read.Language);
            AddRow(ct, "Language", _language);

            _countries = XeniaConsole.Countries.Select(c => (c.Value, XeniaConsole.CountryName(c.Value) + " (" + c.Code + ")"))
                                               .OrderBy(c => c.Item2, StringComparer.CurrentCultureIgnoreCase).ToList();
            _country = Combo(_countries.Select(c => c.Name));
            Select(_country, _countries.FindIndex(c => c.Value == _read.Country), "country " + _read.Country);
            AddRow(ct, "Country", _country);

            _zone = Combo(XeniaConsole.TimeZones.Select(z => z.Name));
            _zone.SelectedIndex = _read.TimeZone >= 0 ? _read.TimeZone : XeniaConsole.London;
            _zone.SelectedIndexChanged += (_, _) => _zoneTouched = true;
            AddRow(ct, "Time zone", _zone);

            _hour24 = new CheckBox { Text = "24-hour clock", AutoSize = true, Checked = _read.Hour24, Margin = new Padding(3, 4, 0, 0) };
            AddRow(ct, "", _hour24);
            _dstOff = new CheckBox { Text = "No daylight saving time", AutoSize = true, Checked = _read.DstOff, Margin = new Padding(3, 2, 0, 0) };
            AddRow(ct, "", _dstOff);

            _region = Combo(XeniaConsole.AvRegions.Select(r => r.Name));
            Select(_region, Array.FindIndex(XeniaConsole.AvRegions, r => r.Value == _read.AvRegion), "0x" + _read.AvRegion.ToString("X8"));
            AddRow(ct, "Video region", _region);

            _resolution = Combo(XeniaConsole.Resolutions.Select(XeniaConsole.ResolutionName));
            Select(_resolution, Array.IndexOf(XeniaConsole.Resolutions, _read.Resolution), XeniaConsole.ResolutionName(_read.Resolution));
            AddRow(ct, "Console resolution", _resolution);

            _volume = new TextBox { Width = 80, Text = _read.MusicVolume.ToString("0.##", CultureInfo.InvariantCulture), Margin = new Padding(3, 3, 0, 0) };
            AddRow(ct, "Music player volume", _volume);

            var windows = new Button { Text = "Use this Windows' language, country, time zone and clock", AutoSize = true, Margin = new Padding(3, 8, 0, 4) };
            windows.Click += (_, _) => Put(XeniaConsole.FromWindows(Values() ?? _read));
            AddWide(ct, windows);
            AddWide(ct, new Label
            {
                AutoSize = true, MaximumSize = new Size(470, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 4, 0, 4),
                Text = "What Xenia's Console settings window edits - the language is the one games show. Written to "
                       + XeniaConsole.PathIn(_layout.StorageRoot) + (XeniaConsole.Exists(_layout.StorageRoot) ? "" : " (not there yet: Xenia's defaults are shown)") + ".",
            });
            consoleBox.Controls.Add(ct);
            Controls.Add(consoleBox);
        }

        public string Executable => _exe;

        private void ShowGamertag()
        {
            var tag = _gamertag.Text.Trim();
            string state;
            if (!XeniaProfile.IsValidGamertag(tag))
                state = "Not a gamertag Xenia accepts: 1 to 15 characters, a letter first, then letters, digits and single spaces.";
            else if (_profile == null)
                state = "No profile yet: one is created with this gamertag, and Xenia signs into it at start.";
            else
                state = (_signedIn ? "Signed in at start." : "Not signed in at start: it will be.") + "  " + _profile.Xuid
                        + (tag != _profile.Gamertag ? "  - renamed from " + _profile.Gamertag : "");
            _gamertagState.Text = state;
            _gamertagState.ForeColor = XeniaProfile.IsValidGamertag(tag) ? SystemColors.GrayText : Color.Firebrick;
        }

        private void Put(XeniaConsoleValues v)
        {
            int language = Array.FindIndex(XeniaConsole.Languages, l => l.Value == v.Language);
            if (language >= 0) _language.SelectedIndex = language;
            int country = _countries.FindIndex(c => c.Value == v.Country);
            if (country >= 0) _country.SelectedIndex = country;
            if (v.TimeZone >= 0) _zone.SelectedIndex = v.TimeZone;
            _hour24.Checked = v.Hour24;
            _dstOff.Checked = v.DstOff;
        }

        /// <summary>The console as the controls say - null when the volume does not read.</summary>
        private XeniaConsoleValues Values()
        {
            if (!float.TryParse(_volume.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var volume)) return null;
            return new XeniaConsoleValues
            {
                Language = _language.SelectedIndex < XeniaConsole.Languages.Length ? XeniaConsole.Languages[_language.SelectedIndex].Value : _read.Language,
                Country = _country.SelectedIndex < _countries.Count ? _countries[_country.SelectedIndex].Value : _read.Country,
                // A zone not in the list reads -1 and shows London: only a change of the box writes one.
                TimeZone = _read.TimeZone < 0 && !_zoneTouched ? -1 : _zone.SelectedIndex,
                Hour24 = _hour24.Checked, DstOff = _dstOff.Checked,
                AvRegion = _region.SelectedIndex < XeniaConsole.AvRegions.Length ? XeniaConsole.AvRegions[_region.SelectedIndex].Value : _read.AvRegion,
                Resolution = _resolution.SelectedIndex < XeniaConsole.Resolutions.Length ? XeniaConsole.Resolutions[_resolution.SelectedIndex] : _read.Resolution,
                MusicVolume = volume,
            };
        }

        private bool ConsoleChanged(XeniaConsoleValues v) => !v.SameAs(_read);

        /// <summary>Why it cannot be saved, or null.</summary>
        public string Problem()
        {
            var v = Values();
            if (v == null || v.MusicVolume < 0 || v.MusicVolume > 1) return "The music player volume is a number from 0 to 1.";
            var tag = _gamertag.Text.Trim();
            if (!XeniaProfile.IsValidGamertag(tag)) return "\"" + tag + "\" is not a gamertag Xenia accepts: 1 to 15 characters, a letter first, then letters, digits and single spaces.";
            bool profileChanges = _profile == null || tag != _profile.Gamertag || !_signedIn;
            if ((profileChanges || ConsoleChanged(v)) && IsRunning(_exe))
                return "Xenia is running. Close it first: it rewrites these files when it exits.";
            return null;
        }

        /// <summary>The profile and the console written where they changed. Call after Problem() said null.</summary>
        public void Save()
        {
            var tag = _gamertag.Text.Trim();
            if (_profile == null)
            {
                var xuid = XeniaProfile.Create(_layout.ContentRoot, tag);
                XeniaProfile.SignInAtStart(_layout.ConfigFile, xuid);
            }
            else
            {
                if (tag != _profile.Gamertag) XeniaProfile.Rename(_layout.ContentRoot, _profile.Xuid, tag);
                if (!_signedIn) XeniaProfile.SignInAtStart(_layout.ConfigFile, _profile.Xuid);
            }
            var v = Values();
            if (v != null && ConsoleChanged(v)) XeniaConsole.Write(_layout.StorageRoot, v);
        }

        /// <summary>Is this executable running? Unsure (a process whose path cannot be read) counts as yes.</summary>
        internal static bool IsRunning(string exe)
        {
            try
            {
                var full = Path.GetFullPath(exe);
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
                {
                    try { if (string.Equals(p.MainModule?.FileName, full, StringComparison.OrdinalIgnoreCase)) return true; }
                    catch { return true; }
                    finally { p.Dispose(); }
                }
            }
            catch { }
            return false;
        }

        // ── layout ───────────────────────────────────────────────────────────

        /// <summary>The entry at <paramref name="index"/> - or, when the file's value is in no entry (-1), one more entry
        /// saying it, chosen: the value stays what it is until another entry is picked.</summary>
        private static void Select(ComboBox box, int index, string raw)
        {
            if (index >= 0) { box.SelectedIndex = index; return; }
            box.Items.Add("As it is (" + raw + ", not in this list)");
            box.SelectedIndex = box.Items.Count - 1;
        }

        private static ComboBox Combo(IEnumerable<string> items)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, Margin = new Padding(3, 3, 0, 0), MaxDropDownItems = 20 };
            c.Items.AddRange(items.Cast<object>().ToArray());
            return c;
        }

        internal static GroupBox Group(string title)
            => new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 4, 8, 8), Margin = new Padding(4, 4, 4, 10) };

        private static TableLayoutPanel Table(params int[] widths)
        {
            var table = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = widths.Length, Location = new Point(8, 20) };
            foreach (var w in widths) table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, w));
            return table;
        }

        private static void AddRow(TableLayoutPanel t, string label, Control editor)
        {
            int r = t.RowCount++;
            t.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 7, 4, 0) }, 0, r);
            t.Controls.Add(editor, 1, r);
        }

        private static void AddWide(TableLayoutPanel t, Control c)
        {
            int r = t.RowCount++;
            t.Controls.Add(c, 0, r);
            t.SetColumnSpan(c, 2);
        }
    }
}
