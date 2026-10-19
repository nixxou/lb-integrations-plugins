// The right-click entry on the games Kronos can run - one game or a selection: "Nixx-Kronos : Options...". The window, dressed
// as every Nixx window (NixxShell), ThreeDSenGameMenu's model:
//   - ITS DISCS: each disc Kronos is given - its file, its title, product code and area from its header;
//   - ITS SAVE, WITH RETROARCH: the backup RAM and cartridge copied to / from Beetle Saturn (KronosRetroArch);
//   - ITS SCREEN, "as every game" until changed (games\<game id>.ini - KronosSettings);
//   - ITS OWN OPTIONS (KronosGameOptions: picture, emulation, expansion cartridge): "As set in Kronos"
//     (Kronos's own value now, read in kronos.ini) until changed; a value Kronos wrote that is not in the list "(set in Kronos)"; an
//     option this Kronos does not have says so, and is left out at launch;
//   - what goes on Kronos's command line, live; the pack's legend, "Reset to defaults", OK and Cancel (OptionMarks).
//
// SEVERAL GAMES (the pack's rule, LbipSelection): "All selected games", then the games grouped by identical options; an
// option they disagree on shows "Various (n of N)" (purple) - left so, each keeps its own; OK asks, then writes only what
// was changed.
//
// NOT AN IGameMenuItemPlugin: Nixx-Menus relays to the GameMenu class below by its name - src\Menus\Menus.cs.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LbIntegrations.Lbip;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Kronos
{
    internal static class KronosGameMenu
    {
        public const string Caption = "Nixx-Kronos : Options...";

        public static Image Icon => LbipMenuIcon.Of(KronosLaunch.IsKronosExe, KronosPlugin.ResolveFullPathForUi);

        /// <summary>The Kronos a game runs on: its own emulator when it is one, else the first Kronos of the library.</summary>
        internal static string ExeOf(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var own = string.IsNullOrWhiteSpace(game?.EmulatorId) ? null : dm?.GetEmulatorById(game.EmulatorId);
                var path = own != null && KronosLaunch.IsKronosExe(own.ApplicationPath) ? own.ApplicationPath
                         : (dm?.GetAllEmulators() ?? new IEmulator[0]).FirstOrDefault(e => e != null && KronosLaunch.IsKronosExe(e.ApplicationPath))?.ApplicationPath;
                return string.IsNullOrWhiteSpace(path) ? null : KronosPlugin.ResolveFullPathForUi(path);
            }
            catch { return null; }
        }

        /// <summary>A game of ours: its own emulator is a Kronos, or it is a Saturn game and a Kronos is in the library. Asked at every
        /// right-click: lookups in memory only.</summary>
        internal static bool IsOurs(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null || game == null) return false;
                var own = string.IsNullOrWhiteSpace(game.EmulatorId) ? null : dm.GetEmulatorById(game.EmulatorId);
                if (own != null && KronosLaunch.IsKronosExe(own.ApplicationPath)) return true;
                if (!string.Equals((game.Platform ?? "").Trim(), KronosPlugin.Platform, StringComparison.OrdinalIgnoreCase)) return false;
                return (dm.GetAllEmulators() ?? new IEmulator[0]).Any(e => e != null && KronosLaunch.IsKronosExe(e.ApplicationPath));
            }
            catch { return false; }
        }

        public static void Open(IGame[] games)
        {
            try
            {
                var ours = games.Where(IsOurs).ToArray();
                if (ours.Length == 0) return;
                using var form = new KronosGameForm(ours);
                form.ShowDialog();
            }
            catch (Exception ex) { Log.Warn("the game's window", ex); MessageBox.Show("The window could not open: " + ex.Message, KronosPlugin.PackName, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        /// <summary>A game's discs as the window shows them: each disc's header and hash, and its line in the list. The discs of an
        /// archive only once it was unpacked by a launch.</summary>
        internal static List<SaturnDisc> DiscsOf(IGame game, string exe)
        {
            var list = new List<SaturnDisc>();
            try
            {
                var rom = KronosPlugin.ResolveFullPathForUi(game?.ApplicationPath);
                if (string.IsNullOrEmpty(rom)) return list;
                foreach (var d in KronosPlugin.DiscsForListing(rom, exe ?? rom))
                {
                    var disc = KronosDisc.Read(d);
                    list.Add(disc);
                }
            }
            catch (Exception ex) { Log.Warn("the game's discs", ex); }
            return list;
        }
    }

    internal sealed class KronosGameForm : Form
    {
        private sealed class Row
        {
            public string Key, Label;
            /// <summary>A game option (KronosGameOptions), Key "opt." + its key; null: a setting of the plugin's (KronosSettings).</summary>
            public KronosGameOption Option;
            public ComboBox Combo;
            public readonly List<string> Values = new List<string>();
            public Panel Bar;
            public string Chosen() => Combo.SelectedIndex < 0 ? null : Values[Combo.SelectedIndex];
        }

        private readonly LbipSelection _sel;
        private readonly List<Row> _rows = new List<Row>();
        private readonly IGame[] _games;
        private readonly string _exe;
        private Dictionary<string, string> _atOpen;
        private readonly Label _line;
        private readonly KronosIniDoc _doc;
        private bool Several => _sel.Several;
        /// <summary>Every game of the window an ST-V game (KronosStv); one of them at least.</summary>
        private readonly bool _stv, _anyStv;
        private bool Shows(KronosGameOption o) => o.Stv == null || (o.Stv == true ? _stv : !_anyStv);
        private static IEnumerable<KronosOption> Options => KronosSettings.All.Where(o => o.PerGame);
        private const string Opt = "opt.";
        private static IEnumerable<string> Keys => Options.Select(o => o.Key).Concat(KronosGameOptions.All.Select(o => Opt + o.Key));

        public KronosGameForm(IGame[] games)
        {
            _games = games;
            _exe = KronosGameMenu.ExeOf(games[0]);
            _stv = games.All(g => KronosStv.IsOurs(Safe(() => g.Platform)));
            _anyStv = games.Any(g => KronosStv.IsOurs(Safe(() => g.Platform)));
            try { if (_exe != null && File.Exists(KronosLaunch.SettingsOf(_exe))) _doc = KronosIniDoc.Load(KronosLaunch.SettingsOf(_exe)); } catch { }
            _sel = new LbipSelection(games.Select(g => new LbipSelection.Member { Title = Safe(() => g.Title) ?? "?", Stored = StoredOf(Safe(() => g.Id)), Tag = g }));
            var game = games[0];
            Text = !Several ? KronosPlugin.PackName + " - " + Safe(() => game.Title) : KronosPlugin.PackName + " - " + games.Length + " games";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ClientSize = new Size(720, 760);
            Font = new Font("Segoe UI", 9f);

            var page = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(14, 10, 14, 6) };
            GroupBox Card(string title, params Control[] rows)
            {
                var box = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 6, 10, 8), Margin = new Padding(0, 0, 0, 10), MinimumSize = new Size(670, 0) };
                var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill };
                flow.Controls.AddRange(rows);
                box.Controls.Add(flow);
                return box;
            }
            Label Text_(string text, Color? color = null, Font font = null) => new Label
            {
                Text = text, AutoSize = true, MaximumSize = new Size(640, 0), UseMnemonic = false, Margin = new Padding(0, 2, 0, 2),
                ForeColor = color ?? SystemColors.ControlText, Font = font ?? Font,
            };
            Control RowOf(string key, string label, string hint, KronosGameOption option = null)
            {
                var r = new Row { Key = key, Label = label, Option = option, Bar = new Panel { Width = 4, Height = 18, Margin = new Padding(0, 3, 8, 0) } };
                var line = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
                line.Controls.Add(r.Bar);
                line.Controls.Add(new Label { Text = label, Width = 130, Height = 24, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
                r.Combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
                r.Combo.SelectedIndexChanged += (_, _) => { r.Bar.BackColor = LbipSelection.BarOf(r.Chosen()); ShowLine(); };
                LbipHint.Attach(r.Combo, hint);
                line.Controls.Add(r.Combo);
                _rows.Add(r);
                return line;
            }
            var top = _sel.Top(Keys, Describe, ShowValues);
            if (!Several)
                page.Controls.Add(new Label { Text = Safe(() => game.Title), AutoSize = true, UseMnemonic = false, Font = new Font("Segoe UI Semibold", 12f), Margin = new Padding(0, 2, 0, 10) });

            // ── an ST-V game: its set (KronosStv) - what Kronos will find, read now ──
            var discCard = new List<Control>();
            int discTotal = 0;
            if (_stv)
            {
                foreach (var g in Several ? games : new[] { game })
                {
                    var rom = Safe(() => KronosPlugin.ResolveFullPathForUi(g.ApplicationPath));
                    var head = Several ? (Safe(() => g.Title) ?? "?") + ": " : "";
                    if (!Several) discCard.Add(Text_("File: " + (rom ?? "")));
                    var why = KronosStv.WhyNotByName(rom, Safe(() => g.Platform));
                    var set = KronosStv.SetOf(rom);
                    if (why != null) { discCard.Add(Text_(head + why, Color.FromArgb(0xC0, 0x60, 0x30))); continue; }
                    discCard.Add(Text_(head + set.Title + " [" + set.Name + "]" + (set.Parent != null ? ", a clone of " + set.Parent : ""), font: Several ? null : new Font("Segoe UI Semibold", 9f)));
                    if (Several) continue;
                    string problem = null;
                    var zips = _exe == null ? null : KronosStv.ZipsFor(rom, set, _exe, out problem);
                    if (_exe == null) discCard.Add(Text_("    Its files are checked once Kronos is installed.", Color.FromArgb(0x70, 0x70, 0x70)));
                    else if (zips == null) discCard.Add(Text_("    " + problem.Replace("\n\n", " "), Color.FromArgb(0xC0, 0x60, 0x30)));
                    else discCard.Add(Text_("    Its " + set.Files.Length + " files are there" + (zips.Count > 1 ? " (" + string.Join(", ", zips.Skip(1).Select(Path.GetFileName)) + " beside it)" : ", the BIOS in its zip")
                                            + " - its EEPROM is " + set.Name + ".nv, its states _<slot>.yss in its own folder.", Color.FromArgb(0x70, 0x70, 0x70)));
                }
                page.Controls.Add(Card(!Several ? "Its ST-V set" : "Their ST-V sets", discCard.ToArray()));
                discCard.Clear();
            }
            // ── its discs ──
            else if (!Several)
            {
                discCard.Add(Text_("File: " + (Safe(() => game.ApplicationPath) ?? "")));
                var discs = KronosGameMenu.DiscsOf(game, _exe);
                discTotal = discs.Count;
                if (discs.Count == 0)
                    discCard.Add(Text_(KronosLaunch.IsArchive(Safe(() => game.ApplicationPath)) ? "An archive: its discs are read once a launch has unpacked it." : "No disc Kronos opens was found for this game.", Color.FromArgb(0xC0, 0x60, 0x30)));
                int n = 0;
                foreach (var disc in discs)
                {
                    n++;
                    var head = discs.Count > 1 ? "Disc " + n + ": " : "";
                    if (disc.Hash == null) { discCard.Add(Text_(head + Path.GetFileName(disc.Path) + " - " + disc.Problem, Color.FromArgb(0xC0, 0x60, 0x30))); continue; }
                    discCard.Add(Text_(head + Path.GetFileName(disc.Path), font: new Font("Segoe UI Semibold", 9f)));
                    discCard.Add(Text_("    " + disc.Title + " [" + disc.Product + (string.IsNullOrEmpty(disc.Version) ? "" : " " + disc.Version) + "], area " + disc.Area
                                       + " - its states are named " + disc.Product + "_<slot>.yss", Color.FromArgb(0x70, 0x70, 0x70)));
                }
            }
            else
                foreach (var g in games)
                {
                    var first = KronosGameMenu.DiscsOf(g, _exe).FirstOrDefault();
                    discCard.Add(Text_((Safe(() => g.Title) ?? "?") + ": " + (first == null ? "no disc found" : first.Title + " [" + first.Product + "]")));
                }
            if (discCard.Count > 0 && !_anyStv) page.Controls.Add(Card(!Several ? (discTotal > 1 ? "Its discs" : "Its disc") : "Their discs", discCard.ToArray()));

            // ── its save, with RetroArch and Ymir (KronosRetroArch, KronosYmir) - a Saturn game's ──
            if (!Several && _exe != null && !_stv) page.Controls.Add(Card("Its save, with RetroArch and Ymir", RetroArchCard(game).ToArray()));

            // ── its screen ──
            var screen = new List<Control>();
            foreach (var o in Options) screen.Add(RowOf(o.Key, o.Label, o.Hint));
            screen.AddRange(OptionRows("Picture", RowOf));
            screen.Add(LbipHint.Note("Kept " + (Several ? "per game" : "for this game alone") + " (games\\<game id>.ini).",
                "Screen: every game's value is in the Nixx window, page Kronos. The others are Kronos's own settings, written in kronos.ini "
                + "for this game's session only and put back after it - changed in Kronos during the game, they become the game's own.", 640, new Padding(0, 4, 0, 2)));
            page.Controls.Add(Card(!Several ? "Its screen" : "Their screen", screen.ToArray()));
            foreach (var group in KronosGameOptions.Groups.Where(g => g != "Picture" && KronosGameOptions.All.Any(o => o.Group == g && Shows(o))))
                page.Controls.Add(Card((!Several ? "Its " : "Their ") + (group == "Sound and cartridge" && _stv ? "sound" : group.ToLowerInvariant()), OptionRows(group, RowOf).ToArray()));

            _line = new Label { AutoSize = true, MaximumSize = new Size(640, 0), UseMnemonic = false, Font = new Font("Consolas", 9f), Margin = new Padding(0, 2, 0, 2) };
            page.Controls.Add(Card("Command line", _line));

            var ok = new Button { Text = "OK", Width = 96, Height = 28 };
            var cancel = new Button { Text = "Cancel", Width = 96, Height = 28, DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => { try { if (Save()) { DialogResult = DialogResult.OK; Close(); } } catch (Exception ex) { MessageBox.Show(this, "The options could not be written: " + ex.Message, KronosPlugin.PackName); } };
            var bottom = OptionMarks.Bottom(OptionMarks.Legend("Kronos", false, false, Several, "As every game"), OptionMarks.ResetButton(Reset), ok, cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(page);
            if (top != null) Controls.Add(top);
            Controls.Add(bottom);
            if (_sel.Source != null) _sel.Source.SelectedIndex = 0; else ShowValues(_sel.From(_sel.Members[0], Keys));
            LbIntegrations.Ui.NixxShell.Dress(this);
        }

        private List<Control> OptionRows(string group, Func<string, string, string, KronosGameOption, Control> rowOf)
            => KronosGameOptions.All.Where(o => o.Group == group && Shows(o)).Select(o => rowOf(Opt + o.Key, o.Label, o.Hint, o)).ToList();

        /// <summary>The game's backup RAM in Kronos, in RetroArch's Beetle Saturn and in Ymir, and the copies between Kronos and each.</summary>
        private List<Control> RetroArchCard(IGame game)
        {
            var rows = new List<Control>();
            var rom = KronosPlugin.ResolveFullPathForUi(Safe(() => game.ApplicationPath));
            var platform = Safe(() => game.Platform);
            var kronos = KronosPlugin.RamPath(_exe, platform, rom);
            var kronosCart = KronosPlugin.CartPath(_exe, platform, rom);
            var replaced = Path.Combine(KronosPlugin.RamDir(_exe, platform), "replaced");
            Label Line(string t) => new Label { Text = t, AutoSize = true, MaximumSize = new Size(640, 0), UseMnemonic = false, Margin = new Padding(0, 2, 0, 2) };
            string State(string path) => File.Exists(path) ? "saved " + File.GetLastWriteTime(path).ToString("g") : "none yet";
            string Cart(string path) => !File.Exists(path) ? "no cartridge" : KronosCart.HasSaves(path) ? "cartridge with saves" : "empty cartridge";
            var kronosLine = Line("");
            var links = new[] { KronosRetroArch.Find(game), KronosYmir.Find(game) };
            var otherLines = links.Select(_ => Line("")).ToArray();
            void Show()
            {
                kronosLine.Text = "Kronos: " + State(kronos) + ", " + Cart(kronosCart) + " - " + kronos;
                for (int i = 0; i < links.Length; i++)
                {
                    var l = links[i];
                    otherLines[i].Text = l.Problem != null ? l.Name + ": " + l.Problem
                        : l.Name + (l.CoreName != l.Name ? " (" + l.CoreName + ")" : "") + ": " + State(l.SaveFile) + ", " + Cart(l.CartFile) + " - " + l.SaveFile;
                }
            }
            Show();
            rows.Add(kronosLine);
            for (int i = 0; i < links.Length; i++)
            {
                rows.Add(otherLines[i]);
                if (links[i].Problem == null) rows.Add(CarryButtons(links[i]));
            }
            if (links.All(l => l.Problem != null)) return rows;
            rows.Add(LbipHint.Note("The backup RAM and cartridge only: the save states of each emulator stay their own.",
                "A save state holds the whole machine as one emulator built it: Kronos, RetroArch and Ymir cannot read each other's. Nor is the "
                + "console's clock carried - each emulator keeps its own. Close both emulators before copying.", 640, new Padding(0, 4, 0, 2)));
            return rows;

            // Import and Export with one other emulator: the console's RAM, and the cartridge when the side it comes from has
            // saves on one (an empty one is not carried).
            Control CarryButtons(KronosRaLink link)
            {
                var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Margin = new Padding(0, 2, 0, 6) };
                var import = new Button { Text = "Import from " + link.Name, AutoSize = true };
                var export = new Button { Text = "Export to " + link.Name, AutoSize = true, Margin = new Padding(8, 3, 3, 3) };
                void Carry(string fromRam, string toRam, string fromCart, string toCart, string what)
                {
                    bool ram = File.Exists(fromRam), cart = KronosCart.HasSaves(fromCart);
                    if (!ram && !cart) { MessageBox.Show(this, "There is no save to copy: " + fromRam, KronosPlugin.PackName, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                    var over = new List<string>();
                    if (ram && File.Exists(toRam)) over.Add("the backup RAM (" + State(toRam) + ")");
                    if (cart && File.Exists(toCart)) over.Add("the cartridge (" + State(toCart) + ")");
                    var carried = (ram ? "the backup RAM" : "") + (ram && cart ? " and " : "") + (cart ? "the cartridge" : "");
                    if (over.Count > 0 && MessageBox.Show(this, what + " - " + carried + ".\n\nWhat is there now (" + string.Join(", ", over) + ") is kept in " + replaced + ".",
                                                          KronosPlugin.PackName, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                    var why = ram ? KronosRetroArch.Copy(fromRam, toRam, replaced, _exe, link.RetroArchExe, otherName: link.Name) : null;
                    if (why == null && cart) why = KronosRetroArch.Copy(fromCart, toCart, replaced, _exe, link.RetroArchExe, cart: true, otherName: link.Name);
                    if (why != null) MessageBox.Show(this, "Not copied: " + why, KronosPlugin.PackName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Show();
                }
                import.Click += (_, _) => Carry(link.SaveFile, kronos, link.CartFile, kronosCart, link.Name + "'s save replaces the game's save in Kronos");
                export.Click += (_, _) => Carry(kronos, link.SaveFile, kronosCart, link.CartFile, "Kronos's save replaces the game's save in " + link.Name);
                LbipHint.Attach(import, "Copies " + link.Name + "'s save of this game over Kronos's: " + (link.Name == "RetroArch" ? link.CoreName + "'s" : "its") + " "
                                      + Path.GetExtension(link.SaveFile) + " (the console's backup RAM, 32 KB) and, when it holds saves, its .bcr (the backup cartridge, "
                                      + "512 KB) - the same formats, no conversion. Kronos then plugs that cartridge for this game. "
                                      + "What is replaced is kept, dated, in " + replaced + ".");
                LbipHint.Attach(export, "Copies Kronos's save of this game over " + link.Name + "'s (" + Path.GetFileName(link.SaveFile) + ", and its cartridge when it holds saves). "
                                      + "What is replaced is kept, dated, in " + replaced + ".");
                buttons.Controls.Add(import);
                buttons.Controls.Add(export);
                return buttons;
            }
        }

        private static Dictionary<string, string> StoredOf(string id)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(id)) return d;
            foreach (var o in Options) { var v = KronosSettings.GameValue(id, o.Key); if (v != null) d[o.Key] = v; }
            foreach (var o in KronosGameOptions.All) { var v = KronosGameOptions.GameValue(id, o.Key); if (v != null) d[Opt + o.Key] = v; }
            return d;
        }

        private string Describe(LbipSelection.Member m)
        {
            var mine = Options.Where(o => m.Stored.ContainsKey(o.Key)).Select(o => o.Label + " " + o.TextOf(m.Stored[o.Key]).ToLowerInvariant())
                .Concat(KronosGameOptions.All.Where(o => m.Stored.ContainsKey(Opt + o.Key)).Select(o => o.Label.ToLowerInvariant() + " " + KronosGameOptions.TextOf(o, m.Stored[Opt + o.Key], _exe)))
                .ToList();
            return mine.Count == 0 ? "as every game" : "its own: " + string.Join(", ", mine);
        }

        private void ShowValues(Dictionary<string, string> shown)
        {
            foreach (var r in _rows) Fill(r, shown.TryGetValue(r.Key, out var v) ? v : null);
            _atOpen = ShownNow();
            ShowLine();
        }

        private void Fill(Row r, string value)
        {
            r.Combo.Items.Clear(); r.Values.Clear();
            if (r.Option != null) { FillOption(r, value); return; }
            var o = KronosSettings.Option(r.Key);
            if (value == LbipSelection.Various) { r.Combo.Items.Add(_sel.VariousText(r.Key)); r.Values.Add(LbipSelection.Various); }
            r.Combo.Items.Add("As every game (" + o.TextOf(KronosSettings.Value(o.Key)) + ")"); r.Values.Add(null);
            for (int i = 0; i < o.Values.Length; i++) { r.Combo.Items.Add(o.Texts[i]); r.Values.Add(o.Values[i]); }
            r.Combo.SelectedIndex = Math.Max(0, r.Values.IndexOf(value));
            r.Bar.BackColor = LbipSelection.BarOf(r.Chosen());
        }

        /// <summary>A game option's list: Kronos's own value first ("As set in Kronos"), the values offered, and the game's when it is
        /// none of them (one Kronos wrote, a BIOS or ROM no longer there).</summary>
        private void FillOption(Row r, string value)
        {
            var o = r.Option;
            if (value == LbipSelection.Various) { r.Combo.Items.Add(_sel.VariousText(r.Key)); r.Values.Add(LbipSelection.Various); }
            string head;
            if (!KronosGameOptions.InKronos(o, _doc)) head = "As set in Kronos - not in this Kronos, never written";
            else
            {
                var now = KronosGameOptions.Current(o, _doc);
                head = "As set in Kronos" + (now != null ? " (" + KronosGameOptions.TextOf(o, now, _exe) + ")" : _doc == null ? "" : " (Kronos's default)");
            }
            r.Combo.Items.Add(head); r.Values.Add(null);
            foreach (var (v, text) in o.Choices(_exe)) { r.Combo.Items.Add(text); r.Values.Add(v); }
            if (value != null && value != LbipSelection.Various && !r.Values.Contains(value)) { r.Combo.Items.Add(KronosGameOptions.TextOf(o, value, _exe)); r.Values.Add(value); }
            r.Combo.SelectedIndex = Math.Max(0, r.Values.IndexOf(value));
            r.Bar.BackColor = LbipSelection.BarOf(r.Chosen());
        }

        /// <summary>What goes on Kronos's command line with what the window shows - for the one game, or the first of several.</summary>
        private void ShowLine()
        {
            if (_line == null || _rows.Count == 0) return;
            var r = _rows.First(x => x.Key == KronosSettings.Fullscreen);
            var v = r.Chosen();
            bool various = v == LbipSelection.Various;
            bool full = (v == null || various ? KronosSettings.Value(KronosSettings.Fullscreen) : v) == "true";
            // An ST-V game's: nothing - its set is in kronos.ini, and the plugin starts it once Kronos is open (KronosStvStart).
            if (_stv) { _line.Text = "(nothing - the set is in kronos.ini; the plugin starts it once Kronos is open" + (full ? ", then full screen)" : ")"); return; }
            // KronosLaunch.CommandLine's: -a (start at once), -f, the BIOS of the disc's region, -i the disc - the rest is in kronos.ini.
            _line.Text = "-a" + (various ? " [-f: per game]" : full ? " -f" : "") + " --bios=\"<the BIOS of the disc's region>\" -i \"<"
                         + (Several ? "each game's disc" : "the game's first disc") + ">\"";
        }

        private void Reset()
        {
            foreach (var r in _rows) Fill(r, null);
            ShowLine();
        }

        private Dictionary<string, string> ShownNow()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var r in _rows) d[r.Key] = r.Chosen();
            return d;
        }

        private bool Save()
        {
            var now = ShownNow();
            if (!LbipSelection.Changed(_atOpen, now)) return true;
            if (!_sel.Confirm(this, Text)) return false;
            int written = 0;
            foreach (var m in _sel.Members)
            {
                var id = Safe(() => ((IGame)m.Tag).Id);
                if (string.IsNullOrEmpty(id)) continue;
                var merged = _sel.MergeFor(m, _atOpen, now);
                bool any = false;
                foreach (var k in Keys)
                {
                    merged.TryGetValue(k, out var v);
                    m.Stored.TryGetValue(k, out var before);
                    if (string.Equals(v, before, StringComparison.Ordinal)) continue;
                    if (k.StartsWith(Opt)) KronosGameOptions.SetGameValue(id, k.Substring(Opt.Length), v);
                    else KronosSettings.SetGameValue(id, k, v);
                    any = true;
                }
                if (any) written++;
            }
            Log.Info("options saved: " + written + " of " + _sel.Members.Count + " game(s) changed");
            return true;
        }

        private static T Safe<T>(Func<T> get) { try { return get(); } catch { return default; } }
    }

    /// <summary>What Nixx-Menus calls, by name - see src\Menus\Menus.cs. Public, its signatures fixed.</summary>
    public static class GameMenu
    {
        public static string[] Entries(IGame[] games)
            => games != null && games.Any(KronosGameMenu.IsOurs) ? new[] { KronosGameMenu.Caption } : new string[0];

        public static void Selected(string entry, IGame[] games)
        {
            if (entry == KronosGameMenu.Caption) KronosGameMenu.Open(games ?? new IGame[0]);
        }

        public static Image Icon => KronosGameMenu.Icon;
    }
}
