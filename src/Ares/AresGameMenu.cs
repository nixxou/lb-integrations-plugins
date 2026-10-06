// The right-click entry on the games ares can run - one game or a selection: "Nixx-ares : Options...". The window, dressed
// as every Nixx window (NixxShell), in tabs (Mehdi, 06/10): GAME - the system ares runs it as (the third level of
// AresLaunch.SystemFor), its file, its BIOS, its saves, its screen; then a tab per group of options, those of one system
// (Nintendo 64...) only for a game of that system. At the bottom, what goes on ares's command line for it, as the SUPER
// ZSNES window shows it - updated at every change. The shader is picked from a menu built from the install's Shaders
// folder when it opens, folders as sub-menus - as ares's own Settings > Shader menu (presentation.cpp, loadShaders).
// Kept in games\<game id>.ini beside settings.ini (AresSettings).
//
// NOT AN IGameMenuItemPlugin, as for the other plugins of the pack: Nixx-Menus relays to the GameMenu class below by its
// name - src\Menus\Menus.cs has the contract.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LbIntegrations.Lbip;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Ares
{
    internal static class AresGameMenu
    {
        public const string Caption = "Nixx-ares : Options...";

        public static Image Icon => LbipMenuIcon.Of(AresLaunch.IsAresExe, p => p);

        /// <summary>A game of ours: its own emulator is an ares, or its platform is one ares runs and an ares is in the
        /// library. Asked at every right-click: lookups in memory only.</summary>
        internal static bool IsOurs(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null || game == null) return false;
                var own = string.IsNullOrWhiteSpace(game.EmulatorId) ? null : dm.GetEmulatorById(game.EmulatorId);
                if (own != null && AresLaunch.IsAresExe(own.ApplicationPath)) return true;
                if (AresSystems.Platform(game.Platform) == null) return false;
                return (dm.GetAllEmulators() ?? new IEmulator[0]).Any(e => e != null && AresLaunch.IsAresExe(e.ApplicationPath));
            }
            catch { return false; }
        }

        public static void Open(IGame[] games)
        {
            try
            {
                var ours = games.Where(IsOurs).ToArray();
                if (ours.Length == 0) return;
                using var form = new AresGameForm(ours);
                form.ShowDialog();
            }
            catch (Exception ex) { Log.Warn("the game's window", ex); MessageBox.Show("The window could not open: " + ex.Message, "Nixx-ares", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }

    internal sealed class AresGameForm : Form
    {
        private static readonly Color Passed = Color.FromArgb(0x3C, 0xB3, 0x71), NotPassed = Color.FromArgb(0xD0, 0xD0, 0xD0);
        private const string Keep = "\u0001keep";   // several games, left as each had it

        /// <summary>One option's row: its list (or, the shader, its menu button), and the bar that says it is passed.</summary>
        private sealed class Row
        {
            public AresOption O;
            public ComboBox Combo;
            public string[] Values;      // what the list's items stand for: Keep, null (follow), or a value
            public Button Shader;
            public string ShaderValue;   // Keep, null, "none" or a preset path
            public object Initial;
            public Panel Bar;

            public string Chosen() => Combo != null ? (Combo.SelectedIndex < 0 ? null : Values[Combo.SelectedIndex]) : ShaderValue;
        }

        private readonly IGame[] _games;
        private readonly List<Row> _rows = new List<Row>();
        private readonly ComboBox _system;
        private readonly List<string> _systemValues = new List<string>();   // Keep, null (as its platform), or a system
        private readonly string _asPlatform, _exe;
        private readonly TextBox _line;
        private readonly Label _count;
        private bool Several => _games.Length > 1;
        /// <summary>For the probe, with no LaunchBox: the ares whose Shaders folder the window lists.</summary>
        internal static string ExeForProbe;

        public AresGameForm(IGame[] games)
        {
            _games = games;
            var game = games[0];
            Text = !Several ? "Nixx-ares - " + Safe(() => game.Title) : "Nixx-ares - " + games.Length + " games";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ClientSize = new Size(760, 640);
            MinimumSize = new Size(640, 480);
            Font = new Font("Segoe UI", 9f);

            var id = Safe(() => game.Id);
            var dm = PluginHelper.DataManager;
            var emu = dm?.GetAllEmulators()?.FirstOrDefault(e => AresLaunch.IsAresExe(Safe(() => e.ApplicationPath)));
            _exe = emu == null ? ExeForProbe : AresPlugin.ResolveFullPathForUi(Safe(() => emu.ApplicationPath));
            var rom = Safe(() => game.ApplicationPath) ?? "";
            _asPlatform = AresPlugin.SystemOfForUi(emu, null, Safe(() => game.Platform), rom);
            var own = !Several ? AresSettings.GameSystem(id) : null;
            var gameSystem = !Several ? (own ?? _asPlatform) : null;

            var title = new Label { Text = !Several ? Safe(() => game.Title) : games.Length + " games - what is set here goes to each of them",
                                    Dock = DockStyle.Top, Height = 34, Padding = new Padding(12, 8, 12, 0), UseMnemonic = false, Font = new Font("Segoe UI Semibold", 11f) };
            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 4) };

            // ── Game ──
            var platform = AresSystems.Platform(Safe(() => game.Platform));
            _system = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
            bool samePlatform = games.All(g => string.Equals(Safe(() => g.Platform), Safe(() => game.Platform), StringComparison.OrdinalIgnoreCase));
            if (Several) { _system.Items.Add(samePlatform ? "Each game keeps its own" : "Each game keeps its own (they are of different platforms)"); _systemValues.Add(Keep); }
            _system.Items.Add("As its platform (" + (_asPlatform ?? "none") + ")"); _systemValues.Add(null);
            if (platform != null)
                foreach (var s in platform.Systems.Concat(platform.ByExtension.Values).Distinct())
                    if (!_systemValues.Contains(s)) { _systemValues.Add(s); _system.Items.Add(s); }
            if (own != null && !_systemValues.Contains(own)) { _systemValues.Add(own); _system.Items.Add(own); }
            _system.SelectedIndex = Several ? 0 : Math.Max(0, _systemValues.IndexOf(own));
            _system.Enabled = samePlatform;
            _system.SelectedIndexChanged += (_, _) => Refresh();
            LbipHint.Attach(_system, "As its platform: the --system on the platform's line in LaunchBox (Edit Emulator > Associated Platforms), else the plugin's "
                                   + "choice for the platform and the file's extension. A system chosen here is this game's alone.");

            var gameTab = Table();
            AddRow(gameTab, null, "System", _system);
            if (!Several)
            {
                AddText(gameTab, "File", rom);
                var system = AresSystems.System(gameSystem);
                if (_exe != null && system != null)
                {
                    var (owner, specs) = AresSystems.FirmwareFor(system);
                    if (specs.Length > 0)
                    {
                        var states = AresFirmware.Ensure(_exe, new[] { system }, readOnly: true);
                        AddText(gameTab, "BIOS", owner.Name + ": " + string.Join(";  ", states.Select(st => st.Spec.Region + " " + (st.Path == null ? (st.Spec.Optional ? "-" : "missing") : Path.GetFileName(st.Path))))
                                                 + (AresFirmware.Enough(states) ? "" : "  -  the game will not start without one"));
                    }
                    var list = new List<GameSaveBase>();
                    AresPlugin.Collect(list, new HashSet<string>(StringComparer.OrdinalIgnoreCase), _exe, system, AresPlugin.ResolveFullPathForUi(rom), id ?? "x", null);
                    AddText(gameTab, "Saves", list.Count == 0 ? "none yet" : string.Join(", ", list.Select(l => Path.GetFileName(l.FileLocation))));
                }
            }
            foreach (var o in AresSettings.All.Where(x => x.OnPage)) AddOption(gameTab, o, id);
            tabs.TabPages.Add(Page("Game", gameTab));

            // ── a tab per group ──
            foreach (var group in AresSettings.Groups)
            {
                var options = AresSettings.All.Where(x => x.Group == group && !x.OnPage && x.AppliesTo(gameSystem)).ToList();
                if (options.Count == 0) continue;
                var table = Table();
                foreach (var o in options) AddOption(table, o, id);
                tabs.TabPages.Add(Page(group == "Start and emulation" ? "Emulation" : group == "Colour and effects" ? "Colour" : group, table));
            }

            // ── the bottom: what goes on the line ──
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 150, Padding = new Padding(12, 6, 12, 4) };
            var legend = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 22, WrapContents = false };
            void Item(Color c, string text)
            {
                legend.Controls.Add(new Panel { BackColor = c, Size = new Size(4, 14), Margin = new Padding(0, 3, 4, 0) });
                legend.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(0, 2, 14, 0) });
            }
            Item(Passed, "Passed on the command line");
            Item(NotPassed, "Not passed: ares's own setting");
            _count = new Label { Dock = DockStyle.Top, Height = 20, ForeColor = SystemColors.GrayText };
            _line = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f), BackColor = SystemColors.Window, TabStop = false };
            var note = LbipHint.Note("Given to ares when the game starts; its own settings come back when it quits.",
                "Kept in games\\<game id>.ini. Each option is passed as --setting \"<key>=<value>\", before what the platform's line in LaunchBox has "
                + "(a --setting there wins). \"As set in ares\" passes nothing: what ares's own menus have is used - every game's settings are ares's.", 700, new Padding(0, 2, 0, 0));
            note.Dock = DockStyle.Bottom;
            bottom.Controls.Add(_line);
            bottom.Controls.Add(_count);
            bottom.Controls.Add(legend);
            bottom.Controls.Add(note);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 46, Padding = new Padding(10, 8, 10, 8) };
            var ok = new Button { Text = "OK", Width = 96, Height = 28, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Width = 96, Height = 28, DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => { try { Save(); } catch (Exception ex) { MessageBox.Show(this, "The options could not be written: " + ex.Message, "Nixx-ares"); DialogResult = DialogResult.None; } };
            bar.Controls.Add(ok);
            bar.Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(tabs);
            Controls.Add(title);
            Controls.Add(bottom);
            Controls.Add(bar);
            LbIntegrations.Ui.NixxShell.Dress(this);
            Refresh();
        }

        // ── the rows ─────────────────────────────────────────────────────────

        private static TableLayoutPanel Table()
        {
            var t = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(8, 8) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 12));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return t;
        }

        private static TabPage Page(string text, Control table)
        {
            var page = new TabPage(text) { AutoScroll = true, Padding = new Padding(6) };
            page.Controls.Add(table);
            return page;
        }

        private static void AddRow(TableLayoutPanel t, Panel bar, string label, Control field)
        {
            int r = t.RowCount++;
            t.Controls.Add(bar ?? new Panel { Width = 4, Height = 18 }, 0, r);
            t.Controls.Add(new Label { Text = label, AutoSize = true, MaximumSize = new Size(186, 0), Margin = new Padding(0, 7, 4, 0), UseMnemonic = false }, 1, r);
            t.Controls.Add(field, 2, r);
        }

        private static void AddText(TableLayoutPanel t, string label, string text)
            => AddRow(t, null, label, new Label { Text = text, AutoSize = true, MaximumSize = new Size(500, 0), Margin = new Padding(3, 7, 0, 4), UseMnemonic = false });

        private void AddOption(TableLayoutPanel t, AresOption o, string id)
        {
            var row = new Row { O = o, Bar = new Panel { Width = 4, Height = 18, Margin = new Padding(0, 6, 8, 0) } };
            var mine = !Several ? AresSettings.GameValue(id, o.Key) : null;
            if (o.Key == "Shader")
            {
                row.ShaderValue = Several ? Keep : mine;
                row.Initial = row.ShaderValue;
                row.Shader = new Button { Width = 330, Height = 26, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
                row.Shader.Click += (_, _) => ShaderMenu(row).Show(row.Shader, new Point(0, row.Shader.Height));
                ShowShader(row);
                LbipHint.Attach(row.Shader, o.Hint);
                AddRow(t, row.Bar, o.Label, row.Shader);
            }
            else
            {
                row.Combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330, MaxDropDownItems = 20 };
                var values = new List<string>();
                if (Several) { row.Combo.Items.Add("Each game keeps its own"); values.Add(Keep); }
                // The first choice follows: every game's (the screen) or ares's own (the rest).
                row.Combo.Items.Add(o.OnPage ? "As every game (" + o.TextOf(AresSettings.Value(o.Key)) + ")" : "As set in ares"); values.Add(null);
                for (int i = 0; i < o.Values.Length; i++)
                {
                    if (o.Values[i] == AresSettings.Ares) continue;
                    row.Combo.Items.Add(o.Texts[i]); values.Add(o.Values[i]);
                }
                row.Values = values.ToArray();
                row.Combo.SelectedIndex = Several ? 0 : Math.Max(0, Array.IndexOf(row.Values, mine));
                row.Initial = row.Combo.SelectedIndex;
                row.Combo.SelectedIndexChanged += (_, _) => Refresh();
                LbipHint.Attach(row.Combo, o.Hint);
                AddRow(t, row.Bar, o.Label, row.Combo);
            }
            _rows.Add(row);
        }

        // ── the shader, as ares's menu ───────────────────────────────────────

        private void ShowShader(Row row)
        {
            var v = row.ShaderValue;
            row.Shader.Text = (v == Keep ? "Each game keeps its own" : v == null ? "As set in ares" : v == "none" ? "None" : AresOption.ShaderText(v)) + "   ▾";
            Refresh();
        }

        /// <summary>The menu, built now from the install's Shaders folder: each folder a sub-menu, each preset an item
        /// (ares: directory::files(location, "*.slangp"), the folders nested by their path).</summary>
        private ContextMenuStrip ShaderMenu(Row row)
        {
            var menu = new ContextMenuStrip();
            void Pick(string value) { row.ShaderValue = value; ShowShader(row); }
            ToolStripMenuItem Item(string text, string value)
            {
                var it = new ToolStripMenuItem(text) { Checked = string.Equals(row.ShaderValue, value, StringComparison.OrdinalIgnoreCase) };
                it.Click += (_, _) => Pick(value);
                return it;
            }
            if (Several) menu.Items.Add(Item("Each game keeps its own", Keep));
            menu.Items.Add(Item("As set in ares", null));
            menu.Items.Add(Item("None", "none"));
            var presets = AresSettings.Shaders(_exe);
            if (presets.Count == 0) { menu.Items.Add(new ToolStripMenuItem(_exe == null ? "(no ares in the library)" : "(no Shaders folder in " + Path.GetDirectoryName(_exe) + ")") { Enabled = false }); return menu; }
            menu.Items.Add(new ToolStripSeparator());
            // ares's order: the folders sorted by their path parts (a parent before its children), each one's sub-menu made
            // at the end of its parent's, then its presets - so in a folder, its presets come before its sub-folders.
            var folders = new Dictionary<string, ToolStripMenuItem>(StringComparer.OrdinalIgnoreCase);
            ToolStripItemCollection Under(string dir)
            {
                if (string.IsNullOrEmpty(dir)) return menu.Items;
                if (folders.TryGetValue(dir, out var f)) return f.DropDownItems;
                var cut = dir.LastIndexOf('/');
                var parent = Under(cut < 0 ? "" : dir.Substring(0, cut));
                f = new ToolStripMenuItem(cut < 0 ? dir : dir.Substring(cut + 1));
                folders[dir] = f;
                parent.Add(f);
                return f.DropDownItems;
            }
            string DirOf(string p) { var cut = p.LastIndexOf('/'); return cut < 0 ? "" : p.Substring(0, cut); }
            var byDir = presets.GroupBy(DirOf, StringComparer.OrdinalIgnoreCase)
                               .OrderBy(g => g.Key, Comparer<string>.Create((a, b) =>
                               {
                                   var pa = a.Length == 0 ? new string[0] : a.Split('/');
                                   var pb = b.Length == 0 ? new string[0] : b.Split('/');
                                   for (int i = 0; i < Math.Min(pa.Length, pb.Length); i++)
                                   {
                                       int c = string.CompareOrdinal(pa[i], pb[i]);
                                       if (c != 0) return c;
                                   }
                                   return pa.Length.CompareTo(pb.Length);
                               }));
            foreach (var dir in byDir)
            {
                var items = Under(dir.Key);
                foreach (var p in dir.OrderBy(x => x, StringComparer.Ordinal))
                {
                    var it = Item(AresOption.ShaderText(p.Substring(p.LastIndexOf('/') + 1)), p);
                    items.Add(it);
                    if (it.Checked) for (var o = it.OwnerItem as ToolStripMenuItem; o != null; o = o.OwnerItem as ToolStripMenuItem) o.Checked = true;
                }
            }
            return menu;
        }

        // ── what goes on the line ────────────────────────────────────────────

        /// <summary>What a game starts with for an option, as chosen in the window: its own, else every game's (the screen),
        /// else ares's own; null for "each game keeps its own".</summary>
        private string Effective(Row row)
        {
            var c = row.Chosen();
            if (c == Keep) return null;
            return c ?? AresSettings.Value(row.O.Key);
        }

        private new void Refresh()
        {
            if (_line == null) return;
            var parts = new List<string>();
            var sys = _system.SelectedIndex < 0 ? null : _systemValues[_system.SelectedIndex];
            if (sys == Keep) parts.Add("--system <each game's>");
            else if ((sys ?? _asPlatform) != null) parts.Add("--system \"" + (sys ?? _asPlatform) + "\"");
            var byKey = _rows.ToDictionary(r => r.O.Key);
            var screen = byKey.TryGetValue("Screen", out var sr) ? Effective(sr) : null;
            if (screen == "full") parts.Add("--fullscreen"); else if (screen == "pseudo") parts.Add("--pseudofullscreen");
            var settings = AresSettings.SettingArgs(key => byKey.TryGetValue(key, out var r) ? Effective(r) : AresSettings.Ares);
            parts.AddRange(settings.Select(s => "--setting " + (s.Any(char.IsWhiteSpace) ? "\"" + s + "\"" : s)));
            foreach (var r in _rows)
            {
                bool passed = r.O.Key == "Screen" ? screen == "full" || screen == "pseudo"
                            : r.O.Key == "Output" ? settings.Any(s => s.StartsWith("Video/Output="))
                            : r.O.Bml != null && settings.Any(s => s.StartsWith(r.O.Bml + "="));
                r.Bar.BackColor = passed ? Passed : NotPassed;
            }
            int n = parts.Count;
            _count.Text = n == 0 ? "Nothing added to the command line." : n + " option(s) added to the command line, before the ROM path:";
            _line.Text = string.Join(" ", parts);
            _line.Select(0, 0);
        }

        private void Save()
        {
            foreach (var g in _games)
            {
                var id = Safe(() => g.Id);
                if (string.IsNullOrEmpty(id)) continue;
                var sys = _system.SelectedIndex < 0 ? Keep : _systemValues[_system.SelectedIndex];
                if (sys != Keep) AresSettings.SetGameSystem(id, sys);
                foreach (var r in _rows)
                {
                    var v = r.Chosen();
                    if (v == Keep) continue;   // several games, left as each had it
                    AresSettings.SetGameValue(id, r.O.Key, v);
                }
            }
            Log.Info("options saved for " + _games.Length + " game(s)");
        }

        private static T Safe<T>(Func<T> get) { try { return get(); } catch { return default; } }
    }

    /// <summary>What Nixx-Menus calls, by name - see src\Menus\Menus.cs. Public, its signatures fixed.</summary>
    public static class GameMenu
    {
        public static string[] Entries(IGame[] games)
            => games != null && games.Any(AresGameMenu.IsOurs) ? new[] { AresGameMenu.Caption } : new string[0];

        public static void Selected(string entry, IGame[] games)
        {
            if (entry == AresGameMenu.Caption) AresGameMenu.Open(games ?? new IGame[0]);
        }

        public static Image Icon => AresGameMenu.Icon;
    }
}
