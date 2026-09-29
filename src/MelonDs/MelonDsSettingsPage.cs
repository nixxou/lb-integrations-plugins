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
        private readonly CheckBox _override;
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

            var box = new GroupBox { Text = "Firmware settings - melonDS's own", Location = new Point(12, 12), Size = new Size(560, 384) };
            _override = new CheckBox
            {
                Text = "Override settings from external firmware", AutoSize = true, Location = new Point(14, 24),
                Checked = _shown.TryGetValue(MelonDsGameSettings.OverrideId, out var o) && o == "true",
            };
            box.Controls.Add(_override);
            box.Controls.Add(new Label
            {
                AutoSize = false, Location = new Point(32, 48), Size = new Size(516, 46), ForeColor = SystemColors.GrayText,
                Text = "The console's name, language, birthday, colour and message for every game that has none of its own "
                     + "(a game's own are in its Options window). On a DSiWare title the override does not reach its save: "
                     + "the save keeps the console's own settings.",
            });
            _fields = new MelonDsFirmwareFields { Location = new Point(14, 104) };
            _fields.ShowValues(_shown);
            box.Controls.Add(_fields);

            var where = new Label
            {
                AutoSize = false, Location = new Point(12, 404), Size = new Size(560, 34), ForeColor = SystemColors.GrayText,
                Text = "Written into " + _layout.ConfigFile + " - the same keys as melonDS's Config > Firmware settings.",
            };
            Controls.Add(box);
            Controls.Add(where);

            // ANYTHING ELSE: melonDS's own file, opened as it is - melonDS.toml IS its settings, and its own
            // windows already edit it; a game's own, any key, are in that game's Options, Advanced tab.
            var open = new Button { Text = "Open melonDS.toml...", AutoSize = true, Location = new Point(12, 444) };
            open.Click += (_, _) => OpenToml();
            Controls.Add(open);
            Controls.Add(new Label
            {
                AutoSize = true, Location = new Point(160, 449), ForeColor = SystemColors.GrayText,
                Text = "every other setting - melonDS must be closed, it rewrites the file as it quits",
            });
        }

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
            chosen[MelonDsGameSettings.OverrideId] = _override.Checked ? "true" : "false";

            var changed = chosen.Where(kv => !_shown.TryGetValue(kv.Key, out var was) || was != kv.Value)
                                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            if (changed.Count == 0) return null;
            var error = MelonDsGameSettings.WriteOwn(_layout, changed);
            if (error != null) return "melonDS's firmware settings were not saved: " + error;
            foreach (var kv in changed) _shown[kv.Key] = kv.Value;
            return null;
        }
    }
}
