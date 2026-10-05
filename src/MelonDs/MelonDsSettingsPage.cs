// The melonDS tab of the pack's configuration window. Nixx-Menus owns the window (its Tools menu entry
// "Nixx Integration Plugins Configuration..."), this plugin builds what is inside its own tab - the
// contract is a name, as for the game menus (src\Menus\Settings.cs):
//
//     public static class LbIntegrations.<assembly name>.Settings
//         string Title { get; }  /  Control CreatePage()  /  string Save(Control page)
//
// WHAT IT HOLDS: melonDS's OWN firmware settings - those of its Firmware settings window - for the
// melonDS THIS PACK INSTALLED (Emulators\Nixx-melonDS), and no other (Mehdi, 29/09). They are melonDS's
// own keys in its melonDS.toml, the ones every game without firmware of its own runs on; a game's own
// are in its Options window. Written only when something changed, and refused while melonDS runs - it
// writes its whole file as it quits.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LbIntegrations.MelonDs
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class Settings
    {
        public static string Title => "melonDS";

        /// <summary>Its emulators LaunchBox has, "<title>\t<path>", for the Nixx window's Open buttons - opened as LaunchBox's "Open
        /// emulator" menu opens them, with what this plugin does around it (Shared.Lbip\LbipOpenEmulator).</summary>
        public static string[] Emulators() => LbIntegrations.Lbip.LbipOpenEmulator.Find(p => MelonDsPaths.IsMelonDsExecutable(p));
        public static string OpenEmulator(string path) => LbIntegrations.Lbip.LbipOpenEmulator.Open(path);

        public static Control CreatePage() => new MelonDsSettingsPage();

        public static string Save(Control page)
        {
            if (!(page is MelonDsSettingsPage ours)) return "this is not the melonDS page";
            try { return ours.Save(); }
            catch (Exception ex) { return "melonDS's settings could not be written: " + ex.Message; }
        }
    }

    internal sealed class MelonDsSettingsPage : UserControl
    {
        private readonly MelonDsLayout _layout;
        private readonly Dictionary<string, string> _shown;
        private readonly CheckBox _dsiWare;
        private readonly RadioButton _dumpMine, _dumpTheirs;
        private readonly bool _dsiWareShown, _dumpMineShown;
        private readonly MelonDsFirmwareFields _fields;

        public MelonDsSettingsPage()
        {
            AutoScroll = true;
            Padding = new Padding(12);

            var exe = MelonDsPlugin.OurExecutable();
            _layout = exe == null ? null : MelonDsPaths.Resolve(exe);
            if (_layout?.ConfigFile == null)
            {
                Controls.Add(new Label
                {
                    Dock = DockStyle.Top, Height = 60, ForeColor = SystemColors.GrayText,
                    Text = "Nixx-melonDS is not installed: there is nothing to configure yet. Add it from LaunchBox's "
                         + "Tools > Manage > Emulators, and its settings appear here.",
                });
                return;
            }

            // melonDS's own, with a session that never ended put back first: a game's are not melonDS's.
            MelonDsGameSettings.Restore(_layout, "the configuration window is opened");
            _shown = MelonDsGameSettings.Current(_layout.ConfigFile);

            // WHOSE CONSOLE EACH GAME SHOWS (Mehdi, 03/10) - see MelonDsFirmware: the override is the plugin's, by kind of game.
            int y = 12;
            var whose = new GroupBox { Text = "Whose console the games show", Location = new Point(12, y), Size = new Size(560, 10) };
            int wy = 22;
            _dsiWareShown = MelonDsFirmware.DsiWareOverride(_layout);
            _dsiWare = new CheckBox { Text = "DSiWare: show your console, over each DSi console's own owner", AutoSize = true, Location = new Point(14, wy), Checked = _dsiWareShown };
            whose.Controls.Add(_dsiWare);
            whose.Controls.Add(Grey("The owner below, for the length of each session: a DSi console is never rewritten, and its saves keep its own "
                                    + "settings. A language its region does not have stays the console's.", 34, wy + 22));
            wy += 64;
            var active = MelonDsFirmware.Active(_layout);
            if (active != null)
            {
                var a = active.Value;
                whose.Controls.Add(new Label { Text = "DS games run on your firmware dump " + a.Dump + ". Its owner in melonDS:", AutoSize = true, Location = new Point(14, wy) });
                wy += 22;
                _dumpMineShown = a.Identity;
                _dumpMine = new RadioButton { Text = "your console (\"Your console\" tab), written into melonDS's copy of the dump", AutoSize = true, Location = new Point(30, wy), Checked = a.Identity };
                _dumpTheirs = new RadioButton { Text = "the dump's own: " + a.DumpOwner.Describe(), AutoSize = true, Location = new Point(30, wy + 22), Checked = !a.Identity };
                whose.Controls.Add(_dumpMine);
                whose.Controls.Add(_dumpTheirs);
                whose.Controls.Add(Grey("Your dump in melonDS's bios folder is never written: melonDS boots on its copy.", 48, wy + 46));
                wy += 72;
            }
            else
            {
                whose.Controls.Add(Grey("DS games run on melonDS's own firmware, which always shows the owner below. DSi cartridges show your "
                                        + "console too: it is written into the copy of the console they run on.", 14, wy));
                wy += 40;
            }
            whose.Size = new Size(560, wy + 6);
            Controls.Add(whose);
            y += whose.Height + 10;

            var box = new GroupBox { Text = "The owner melonDS shows - its Firmware settings, filled from \"Your console\"", Location = new Point(12, y), Size = new Size(560, 290) };
            _fields = new MelonDsFirmwareFields { Location = new Point(14, 24) };
            _fields.ShowValues(_shown);
            box.Controls.Add(_fields);
            Controls.Add(box);
            y += box.Height + 8;

            var where = new Label
            {
                AutoSize = false, Location = new Point(12, y), Size = new Size(560, 34), ForeColor = SystemColors.GrayText,
                Text = "Written into " + _layout.ConfigFile + " - the same keys as melonDS's Config > Firmware settings.",
            };
            Controls.Add(where);
            y += 40;

            // ANYTHING ELSE: melonDS's own file, opened as it is - melonDS.toml IS its settings, and its own
            // windows already edit it; a game's own, any key, are in that game's Options, Advanced tab.
            var open = new Button { Text = "Open melonDS.toml...", AutoSize = true, Location = new Point(12, y) };
            open.Click += (_, _) => OpenToml();
            Controls.Add(open);
            Controls.Add(new Label
            {
                AutoSize = true, Location = new Point(160, y + 5), ForeColor = SystemColors.GrayText,
                Text = "every other setting - melonDS must be closed, it rewrites the file as it quits",
            });
        }

        private static Label Grey(string text, int x, int y) => new Label
        {
            AutoSize = true, MaximumSize = new Size(530 - x, 0), Location = new Point(x, y), ForeColor = SystemColors.GrayText, Text = text,
        };

        private void OpenToml()
        {
            try
            {
                if (LbIntegrations.Dsi.DsiNand.EmulatorRunning())
                {
                    MessageBox.Show(this, "melonDS is running: close it first - it rewrites melonDS.toml as it quits, over any change made meanwhile.",
                                    "Nixx-melonDS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                // A session that never ended is put back first: what is opened is melonDS's own.
                MelonDsGameSettings.Restore(_layout, "melonDS.toml is opened to be edited");
                if (!System.IO.File.Exists(_layout.ConfigFile)) { MessageBox.Show(this, "There is no melonDS.toml yet: melonDS writes it the first time it runs.", "Nixx-melonDS"); return; }
                // Notepad, not the file's association: .toml often has none.
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", "\"" + _layout.ConfigFile + "\"") { UseShellExecute = true });
                Log.Info("configuration window: melonDS.toml opened in Notepad");
            }
            catch (Exception ex) { Log.Warn("could not open melonDS.toml", ex); }
        }

        /// <summary>Null when saved (or nothing changed), otherwise why not.</summary>
        public string Save()
        {
            if (_layout?.ConfigFile == null) return null;
            var problem = _fields.Problem();
            if (problem != null) return problem;

            var chosen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in _fields.Read())
            {
                var s = MelonDsGameSettings.Settings.First(x => x.Id == kv.Key);
                chosen[kv.Key] = MelonDsGameSettings.Normal(s, kv.Value) ?? s.Default;
            }
            // melonDS's own override is the plugin's now (set for each launch): its file keeps it off.
            chosen[MelonDsGameSettings.OverrideId] = "false";

            if (_dsiWare.Checked != _dsiWareShown) MelonDsFirmware.SetDsiWareOverride(_layout, _dsiWare.Checked);

            var changed = chosen.Where(kv => !_shown.TryGetValue(kv.Key, out var was) || was != kv.Value)
                                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            if (changed.Count > 0)
            {
                var error = MelonDsGameSettings.WriteOwn(_layout, changed);
                if (error != null) return "melonDS's firmware settings were not saved: " + error;
                foreach (var kv in changed) _shown[kv.Key] = kv.Value;
            }
            // Then the dump's copy, from the settings just written - when its owner is set to yours, or that changed.
            if (_dumpMine != null && (_dumpMine.Checked != _dumpMineShown || (_dumpMine.Checked && changed.Count > 0)))
            {
                var why = MelonDsFirmware.SetDumpOwner(_layout, _dumpMine.Checked);
                if (why != null) return "the DS firmware's owner was not changed: " + why;
            }
            return null;
        }
    }
}
