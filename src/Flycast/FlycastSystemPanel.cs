// Flycast's SYSTEM settings for every game, in the Nixx window's Flycast tab (Mehdi, 04/10): region, language, broadcast,
// cable, SH4 clock, volume, DSP, HLE BIOS, fast GD-ROM loading, the 32 MB RAM mod, Naomi free play - the same list as a
// game's System tab (FlycastGameSettings.Offered), but EDITING FLYCAST'S OWN emu.cfg, its [config] section: what every
// game runs on. Not a value of ours laid over it - so what Flycast's own Settings window shows, and changes, is the same.
//
//   SHOWN    each value as emu.cfg holds it now, else Flycast's built-in default; one no entry has is shown as it is.
//   WRITTEN  at OK, only what the user changed (LbipGameEdit), key by key (FlycastIni.Write: the rest of the file as it
//            was) - refused while Flycast runs: it rewrites emu.cfg when it quits.
//   OVER IT  a game's own Flycast game config ("Make Game Config" in Flycast, [<product>] of emu.cfg)
//            win for that game; "Your console" (Apply to my emulators) writes Dreamcast Language here too.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LbIntegrations.Lbip;

namespace LbIntegrations.Flycast
{
    internal sealed class FlycastSystemPanel : GroupBox
    {
        private sealed class Field { public FlycastGameSettings.Setting Setting; public ComboBox Combo; public CheckBox Box; public NumericUpDown Number; public string Kept; }

        private readonly List<Field> _fields = new List<Field>();
        private readonly FlycastLayout _layout;
        private readonly Dictionary<string, string> _atOpen;

        public FlycastSystemPanel(int width)
        {
            Text = "Flycast's system settings - every game";
            Size = new Size(width, 100);
            _layout = FindLayout();

            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Location = new Point(14, 22) };
            // One short sentence, the whole text on hover (LbipHint).
            stack.Controls.Add(LbipHint.Note(
                "Flycast's own emu.cfg, as in its Settings window; a game's own config wins.",
                "Flycast's own settings (emu.cfg), what every game runs on - the same as in Flycast's Settings window. "
              + "A game's own Flycast config (Make Game Config, in Flycast's settings while the game runs) wins over them for that game. "
              + "\"Your console\" sets the Dreamcast language here too.",
                width - 30, new Padding(0, 0, 0, 8)));
            if (_layout == null || string.IsNullOrEmpty(_layout.ConfigFile))
            {
                stack.Controls.Add(new Label { AutoSize = true, Text = "Nixx-Flycast is not installed in this LaunchBox.", ForeColor = Color.Firebrick });
                Controls.Add(stack);
                Height = 90;
                return;
            }

            var current = FlycastGameSettings.DefaultsOf(_layout, null, out _);
            var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
            // 240: "Dreamcast 32MB RAM Mod  (Dreamcast)" on one line (it wrapped in 200).
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width - 280));
            foreach (var s in FlycastGameSettings.Offered.Where(x => x.Tab == FlycastGameSettings.SystemTab))
            {
                current.TryGetValue(s.Id, out var value);
                var f = new Field { Setting = s };
                Control editor;
                switch (s.Kind)
                {
                    case FlycastGameSettings.Kind.Choice:
                        f.Combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, Margin = new Padding(3, 2, 0, 2) };
                        foreach (var c in s.Choices) f.Combo.Items.Add(c.Text);
                        int at = Array.FindIndex(s.Choices, c => c.Value == (value ?? "").Trim());
                        if (at < 0 && !string.IsNullOrWhiteSpace(value)) { f.Kept = value.Trim(); f.Combo.Items.Add("As it is (" + f.Kept + ", not in this list)"); at = f.Combo.Items.Count - 1; }
                        f.Combo.SelectedIndex = Math.Max(0, at);
                        editor = f.Combo;
                        break;
                    case FlycastGameSettings.Kind.Bool:
                        f.Box = new CheckBox { AutoSize = true, Checked = FlycastGameSettings.IsYes(value), Margin = new Padding(3, 4, 0, 2) };
                        editor = f.Box;
                        break;
                    default:
                        f.Number = new NumericUpDown { Minimum = s.Min, Maximum = s.Max, Width = 80, Margin = new Padding(3, 2, 0, 2) };
                        f.Number.Value = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Min(s.Max, Math.Max(s.Min, n)) : s.Min;
                        editor = f.Number;
                        break;
                }
                var label = s.Label + (s.For == FlycastGameSettings.Games.Dreamcast ? "  (Dreamcast)" : s.For == FlycastGameSettings.Games.Arcade ? "  (arcade)" : "");
                int r = table.RowCount++;
                table.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 4, 0) }, 0, r);
                table.Controls.Add(editor, 1, r);
                _fields.Add(f);
            }
            stack.Controls.Add(table);
            stack.Controls.Add(new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 8, 0, 0), Text = "File: " + _layout.ConfigFile });
            Controls.Add(stack);
            Height = stack.PreferredSize.Height + 36;
            _atOpen = Shown();
        }

        /// <summary>Each field as emu.cfg would hold it (section:key -> value).</summary>
        private Dictionary<string, string> Shown()
        {
            var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in _fields)
            {
                var s = f.Setting;
                if (f.Combo != null) v[s.Id] = f.Combo.SelectedIndex < s.Choices.Length ? s.Choices[f.Combo.SelectedIndex].Value : f.Kept;
                else if (f.Box != null) v[s.Id] = f.Box.Checked ? "yes" : "no";
                else v[s.Id] = ((int)f.Number.Value).ToString(CultureInfo.InvariantCulture);
            }
            return v;
        }

        /// <summary>What the user changed, written into emu.cfg. Null, or why not.</summary>
        public string Save()
        {
            if (_atOpen == null) return null;
            var now = Shown();
            if (!LbipGameEdit.Changed(_atOpen, now)) return null;
            var changed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in now)
                if (!_atOpen.TryGetValue(kv.Key, out var before) || before != kv.Value) changed[kv.Key] = kv.Value;
            foreach (var section in changed.GroupBy(kv => kv.Key.Substring(0, kv.Key.IndexOf(':'))))
            {
                var error = FlycastIni.Write(_layout.ConfigFile, section.Key, section.ToDictionary(kv => kv.Key.Substring(kv.Key.IndexOf(':') + 1), kv => kv.Value, StringComparer.OrdinalIgnoreCase));
                if (error != null) return error;
            }
            foreach (var kv in changed) _atOpen[kv.Key] = kv.Value;
            Log.Info("system settings for every game written to emu.cfg: " + string.Join(", ", changed.Select(kv => kv.Key + "=" + kv.Value)));
            return null;
        }

#pragma warning disable CS0649
        /// <summary>For the probe: the Flycast to edit. Set by reflection.</summary>
        internal static FlycastLayout LayoutOverride;
#pragma warning restore CS0649

        private static FlycastLayout FindLayout()
        {
            try
            {
                if (LayoutOverride != null) return LayoutOverride;
                var root = LbIntegrations.Identity.PackIdentity.LaunchBoxRoot();
                var exe = root == null ? null : FlycastPaths.FindExecutable(Path.Combine(root, "Emulators", "Nixx-Flycast"));
                return exe == null ? null : FlycastPaths.Resolve(exe);
            }
            catch { return null; }
        }
    }
}
