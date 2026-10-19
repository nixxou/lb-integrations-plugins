// The right-click entry on the games Ymir can run - one game or a selection: "Nixx-Ymir : Options...". The window, dressed
// as every Nixx window (NixxShell), ThreeDSenGameMenu's model:
//   - ITS DISCS (Mehdi 20/10: the compatibility list "in the game's window"): each disc Ymir is given - its file, its title,
//     product code and area from its header, its hash - and its state in Ymir's compatibility list, the state's colour, the
//     list's notes, a link to the list. Several games: each game's state, one line each;
//   - ITS SCREEN, "as every game" until changed (games\<game id>.ini - YmirSettings);
//   - ITS OWN OPTIONS (Mehdi 20/10: picture, emulation, pads, sound, expansion cartridge - YmirGameOptions): "As set in Ymir"
//     (Ymir's own value now, read in Ymir.toml) until changed; a value Ymir wrote that is not in the list "(set in Ymir)"; an
//     option this Ymir does not have says so, and is left out at launch;
//   - what goes on Ymir's command line, live; the pack's legend, "Reset to defaults", OK and Cancel (OptionMarks).
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

namespace LbIntegrations.Ymir
{
    internal static class YmirGameMenu
    {
        public const string Caption = "Nixx-Ymir : Options...";

        public static Image Icon => LbipMenuIcon.Of(YmirLaunch.IsYmirExe, YmirPlugin.ResolveFullPathForUi);

        /// <summary>The Ymir a game runs on: its own emulator when it is one, else the first Ymir of the library.</summary>
        internal static string ExeOf(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var own = string.IsNullOrWhiteSpace(game?.EmulatorId) ? null : dm?.GetEmulatorById(game.EmulatorId);
                var path = own != null && YmirLaunch.IsYmirExe(own.ApplicationPath) ? own.ApplicationPath
                         : (dm?.GetAllEmulators() ?? new IEmulator[0]).FirstOrDefault(e => e != null && YmirLaunch.IsYmirExe(e.ApplicationPath))?.ApplicationPath;
                return string.IsNullOrWhiteSpace(path) ? null : YmirPlugin.ResolveFullPathForUi(path);
            }
            catch { return null; }
        }

        /// <summary>A game of ours: its own emulator is a Ymir, or it is a Saturn game and a Ymir is in the library. Asked at every
        /// right-click: lookups in memory only.</summary>
        internal static bool IsOurs(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null || game == null) return false;
                var own = string.IsNullOrWhiteSpace(game.EmulatorId) ? null : dm.GetEmulatorById(game.EmulatorId);
                if (own != null && YmirLaunch.IsYmirExe(own.ApplicationPath)) return true;
                if (!string.Equals((game.Platform ?? "").Trim(), YmirPlugin.Platform, StringComparison.OrdinalIgnoreCase)) return false;
                return (dm.GetAllEmulators() ?? new IEmulator[0]).Any(e => e != null && YmirLaunch.IsYmirExe(e.ApplicationPath));
            }
            catch { return false; }
        }

        public static void Open(IGame[] games)
        {
            try
            {
                var ours = games.Where(IsOurs).ToArray();
                if (ours.Length == 0) return;
                using var form = new YmirGameForm(ours);
                form.ShowDialog();
            }
            catch (Exception ex) { Log.Warn("the game's window", ex); MessageBox.Show("The window could not open: " + ex.Message, YmirPlugin.PackName, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        /// <summary>A game's discs as the window shows them: each disc's header and hash, and its line in the list. The discs of an
        /// archive only once it was unpacked by a launch.</summary>
        internal static List<(SaturnDisc Disc, YmirCompatResult Compat)> DiscsOf(IGame game, string exe)
        {
            var list = new List<(SaturnDisc, YmirCompatResult)>();
            try
            {
                var rom = YmirPlugin.ResolveFullPathForUi(game?.ApplicationPath);
                if (string.IsNullOrEmpty(rom)) return list;
                foreach (var d in YmirPlugin.DiscsForListing(rom, exe ?? rom))
                {
                    var disc = YmirDisc.Read(d);
                    list.Add((disc, YmirCompat.Lookup(disc)));
                }
            }
            catch (Exception ex) { Log.Warn("the game's discs", ex); }
            return list;
        }
    }

    internal sealed class YmirGameForm : Form
    {
        private sealed class Row
        {
            public string Key, Label;
            /// <summary>A game option (YmirGameOptions), Key "opt." + its key; null: a setting of the plugin's (YmirSettings).</summary>
            public YmirGameOption Option;
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
        private readonly YmirTomlDoc _doc;
        private bool Several => _sel.Several;
        private static IEnumerable<YmirOption> Options => YmirSettings.All.Where(o => o.PerGame);
        private const string Opt = "opt.";
        private static IEnumerable<string> Keys => Options.Select(o => o.Key).Concat(YmirGameOptions.All.Select(o => Opt + o.Key));

        public YmirGameForm(IGame[] games)
        {
            _games = games;
            _exe = YmirGameMenu.ExeOf(games[0]);
            try { if (_exe != null && File.Exists(YmirLaunch.SettingsOf(_exe))) _doc = YmirTomlDoc.Load(YmirLaunch.SettingsOf(_exe)); } catch { }
            _sel = new LbipSelection(games.Select(g => new LbipSelection.Member { Title = Safe(() => g.Title) ?? "?", Stored = StoredOf(Safe(() => g.Id)), Tag = g }));
            var game = games[0];
            Text = !Several ? YmirPlugin.PackName + " - " + Safe(() => game.Title) : YmirPlugin.PackName + " - " + games.Length + " games";
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
            Control RowOf(string key, string label, string hint, YmirGameOption option = null)
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
            Control StateLine(string head, YmirCompatResult c)
            {
                var line = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
                line.Controls.Add(new Panel { Width = 12, Height = 12, Margin = new Padding(0, 4, 8, 0), BackColor = c?.State != null ? YmirCompat.ColorOf(c.State) : Color.FromArgb(0x9A, 0x9A, 0x9A) });
                line.Controls.Add(Text_((head == null ? "" : head + ": ") + YmirCompat.Line(c), font: c?.Exact != null ? new Font("Segoe UI Semibold", 9f) : null));
                return line;
            }

            var top = _sel.Top(Keys, Describe, ShowValues);
            if (!Several)
                page.Controls.Add(new Label { Text = Safe(() => game.Title), AutoSize = true, UseMnemonic = false, Font = new Font("Segoe UI Semibold", 12f), Margin = new Padding(0, 2, 0, 10) });

            // ── its discs, and the list ──
            var discCard = new List<Control>();
            int discTotal = 0;
            if (!Several)
            {
                discCard.Add(Text_("File: " + (Safe(() => game.ApplicationPath) ?? "")));
                var discs = YmirGameMenu.DiscsOf(game, _exe);
                discTotal = discs.Count;
                if (discs.Count == 0)
                    discCard.Add(Text_(YmirLaunch.IsArchive(Safe(() => game.ApplicationPath)) ? "An archive: its discs are read once a launch has unpacked it." : "No disc Ymir opens was found for this game.", Color.FromArgb(0xC0, 0x60, 0x30)));
                int n = 0;
                foreach (var (disc, compat) in discs)
                {
                    n++;
                    var head = discs.Count > 1 ? "Disc " + n : null;
                    discCard.Add(StateLine(head, compat));
                    if (disc.Hash == null) { discCard.Add(Text_("    " + Path.GetFileName(disc.Path) + " - " + disc.Problem, Color.FromArgb(0xC0, 0x60, 0x30))); continue; }
                    discCard.Add(Text_("    " + Path.GetFileName(disc.Path) + " - " + disc.Title + " [" + disc.Product + (string.IsNullOrEmpty(disc.Version) ? "" : " " + disc.Version) + "], area " + disc.Area,
                                       Color.FromArgb(0x70, 0x70, 0x70)));
                    discCard.Add(Text_("    Ymir's hash: " + disc.Hash, Color.FromArgb(0x70, 0x70, 0x70)));
                    if (compat.Exact != null && !string.IsNullOrWhiteSpace(compat.Exact.Notes)) discCard.Add(Text_("    Notes: " + compat.Exact.Notes));
                    if (compat.Exact != null && !string.IsNullOrWhiteSpace(compat.Exact.Bios)) discCard.Add(Text_("    Tested with the BIOS " + compat.Exact.Bios, Color.FromArgb(0x70, 0x70, 0x70)));
                }
            }
            else
                foreach (var g in games)
                {
                    var first = YmirGameMenu.DiscsOf(g, _exe).FirstOrDefault();
                    discCard.Add(StateLine(Safe(() => g.Title) ?? "?", first.Compat));
                }
            var link = new LinkLabel { Text = "Ymir's compatibility list", AutoSize = true, Margin = new Padding(0, 6, 0, 2) };
            link.LinkClicked += (_, _) => { try { Process.Start(new ProcessStartInfo(YmirCompat.Page) { UseShellExecute = true }); } catch { } };
            discCard.Add(link);
            if (YmirCompat.Count() == 0) discCard.Add(Text_("The list is downloaded at install, and from the Nixx window (page Ymir).", Color.FromArgb(0x70, 0x70, 0x70)));
            page.Controls.Add(Card(!Several ? (discTotal > 1 ? "Its discs" : "Its disc") : "Their state in Ymir's compatibility list", discCard.ToArray()));

            // ── its save, with RetroArch (YmirRetroArch) ──
            if (!Several && _exe != null) page.Controls.Add(Card("Its save, with RetroArch", RetroArchCard(game).ToArray()));

            // ── its screen ──
            var screen = new List<Control>();
            foreach (var o in Options) screen.Add(RowOf(o.Key, o.Label, o.Hint));
            screen.AddRange(OptionRows("Picture", RowOf));
            screen.Add(LbipHint.Note("Kept " + (Several ? "per game" : "for this game alone") + " (games\\<game id>.ini).",
                "Screen: every game's value is in the Nixx window, page Ymir. The others are Ymir's own settings (F10 in Ymir), written in Ymir.toml "
                + "for this game's session only and put back after it - changed in Ymir during the game, they become the game's own.", 640, new Padding(0, 4, 0, 2)));
            page.Controls.Add(Card(!Several ? "Its screen" : "Their screen", screen.ToArray()));
            foreach (var group in YmirGameOptions.Groups.Where(g => g != "Picture"))
                page.Controls.Add(Card((!Several ? "Its " : "Their ") + group.ToLowerInvariant(), OptionRows(group, RowOf).ToArray()));

            _line = new Label { AutoSize = true, MaximumSize = new Size(640, 0), UseMnemonic = false, Font = new Font("Consolas", 9f), Margin = new Padding(0, 2, 0, 2) };
            page.Controls.Add(Card("Command line", _line));

            var ok = new Button { Text = "OK", Width = 96, Height = 28 };
            var cancel = new Button { Text = "Cancel", Width = 96, Height = 28, DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => { try { if (Save()) { DialogResult = DialogResult.OK; Close(); } } catch (Exception ex) { MessageBox.Show(this, "The options could not be written: " + ex.Message, YmirPlugin.PackName); } };
            var bottom = OptionMarks.Bottom(OptionMarks.Legend("Ymir", false, false, Several, "As every game"), OptionMarks.ResetButton(Reset), ok, cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(page);
            if (top != null) Controls.Add(top);
            Controls.Add(bottom);
            if (_sel.Source != null) _sel.Source.SelectedIndex = 0; else ShowValues(_sel.From(_sel.Members[0], Keys));
            LbIntegrations.Ui.NixxShell.Dress(this);
        }

        private static List<Control> OptionRows(string group, Func<string, string, string, YmirGameOption, Control> rowOf)
            => YmirGameOptions.All.Where(o => o.Group == group).Select(o => rowOf(Opt + o.Key, o.Label, o.Hint, o)).ToList();

        /// <summary>The game's backup RAM in Ymir and in RetroArch's Beetle Saturn, and the two copies between them.</summary>
        private List<Control> RetroArchCard(IGame game)
        {
            var rows = new List<Control>();
            var link = YmirRetroArch.Find(game);
            var rom = YmirPlugin.ResolveFullPathForUi(Safe(() => game.ApplicationPath));
            var platform = Safe(() => game.Platform);
            var ymir = YmirPlugin.RamPath(_exe, platform, rom);
            var ymirCart = YmirPlugin.CartPath(_exe, platform, rom);
            var replaced = Path.Combine(YmirPlugin.RamDir(_exe, platform), "replaced");
            Label Line(string t) => new Label { Text = t, AutoSize = true, MaximumSize = new Size(640, 0), UseMnemonic = false, Margin = new Padding(0, 2, 0, 2) };
            string State(string path) => File.Exists(path) ? "saved " + File.GetLastWriteTime(path).ToString("g") : "none yet";
            string Cart(string path) => !File.Exists(path) ? "no cartridge" : YmirCart.HasSaves(path) ? "cartridge with saves" : "empty cartridge";
            var ymirLine = Line("");
            var raLine = Line("");
            void Show()
            {
                ymirLine.Text = "Ymir: " + State(ymir) + ", " + Cart(ymirCart) + " - " + ymir;
                raLine.Text = link.Problem != null ? "RetroArch: " + link.Problem
                            : "RetroArch (" + link.CoreName + "): " + State(link.SaveFile) + ", " + Cart(link.CartFile) + " - " + link.SaveFile;
            }
            Show();
            rows.Add(ymirLine);
            rows.Add(raLine);
            if (link.Problem != null) return rows;

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Margin = new Padding(0, 6, 0, 2) };
            var import = new Button { Text = "Import from RetroArch", AutoSize = true };
            var export = new Button { Text = "Export to RetroArch", AutoSize = true, Margin = new Padding(8, 3, 3, 3) };
            // The console's RAM, and the cartridge when the side it comes from has saves on one (an empty one is not carried).
            void Carry(string fromRam, string toRam, string fromCart, string toCart, string what)
            {
                bool ram = File.Exists(fromRam), cart = YmirCart.HasSaves(fromCart);
                if (!ram && !cart) { MessageBox.Show(this, "There is no save to copy: " + fromRam, YmirPlugin.PackName, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                var over = new List<string>();
                if (ram && File.Exists(toRam)) over.Add("the backup RAM (" + State(toRam) + ")");
                if (cart && File.Exists(toCart)) over.Add("the cartridge (" + State(toCart) + ")");
                var carried = (ram ? "the backup RAM" : "") + (ram && cart ? " and " : "") + (cart ? "the cartridge" : "");
                if (over.Count > 0 && MessageBox.Show(this, what + " - " + carried + ".\n\nWhat is there now (" + string.Join(", ", over) + ") is kept in " + replaced + ".",
                                                      YmirPlugin.PackName, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                var why = ram ? YmirRetroArch.Copy(fromRam, toRam, replaced, _exe, link.RetroArchExe) : null;
                if (why == null && cart) why = YmirRetroArch.Copy(fromCart, toCart, replaced, _exe, link.RetroArchExe, cart: true);
                if (why != null) MessageBox.Show(this, "Not copied: " + why, YmirPlugin.PackName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Show();
            }
            import.Click += (_, _) => Carry(link.SaveFile, ymir, link.CartFile, ymirCart, "RetroArch's save replaces the game's save in Ymir");
            export.Click += (_, _) => Carry(ymir, link.SaveFile, ymirCart, link.CartFile, "Ymir's save replaces the game's save in RetroArch");
            LbipHint.Attach(import, "Copies RetroArch's save of this game over Ymir's: " + link.CoreName + "'s .bkr (the console's backup RAM, 32 KB) and, when it holds saves, "
                                  + "its .bcr (the backup cartridge, 512 KB) - the same formats, no conversion. Ymir then plugs that cartridge for this game. "
                                  + "What is replaced is kept, dated, in " + replaced + ".");
            LbipHint.Attach(export, "Copies Ymir's save of this game over RetroArch's (" + Path.GetFileName(link.SaveFile) + ", and its cartridge when it holds saves). "
                                  + "What is replaced is kept, dated, in " + replaced + ".");
            buttons.Controls.Add(import);
            buttons.Controls.Add(export);
            rows.Add(buttons);
            rows.Add(LbipHint.Note("The backup RAM and cartridge only: the save states of each emulator stay their own.",
                "A save state holds the whole machine as one emulator built it: Ymir and RetroArch cannot read each other's. Close both emulators before copying.", 640, new Padding(0, 4, 0, 2)));
            return rows;
        }

        private static Dictionary<string, string> StoredOf(string id)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(id)) return d;
            foreach (var o in Options) { var v = YmirSettings.GameValue(id, o.Key); if (v != null) d[o.Key] = v; }
            foreach (var o in YmirGameOptions.All) { var v = YmirGameOptions.GameValue(id, o.Key); if (v != null) d[Opt + o.Key] = v; }
            return d;
        }

        private string Describe(LbipSelection.Member m)
        {
            var mine = Options.Where(o => m.Stored.ContainsKey(o.Key)).Select(o => o.Label + " " + o.TextOf(m.Stored[o.Key]).ToLowerInvariant())
                .Concat(YmirGameOptions.All.Where(o => m.Stored.ContainsKey(Opt + o.Key)).Select(o => o.Label.ToLowerInvariant() + " " + YmirGameOptions.TextOf(o, m.Stored[Opt + o.Key], _exe)))
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
            var o = YmirSettings.Option(r.Key);
            if (value == LbipSelection.Various) { r.Combo.Items.Add(_sel.VariousText(r.Key)); r.Values.Add(LbipSelection.Various); }
            r.Combo.Items.Add("As every game (" + o.TextOf(YmirSettings.Value(o.Key)) + ")"); r.Values.Add(null);
            for (int i = 0; i < o.Values.Length; i++) { r.Combo.Items.Add(o.Texts[i]); r.Values.Add(o.Values[i]); }
            r.Combo.SelectedIndex = Math.Max(0, r.Values.IndexOf(value));
            r.Bar.BackColor = LbipSelection.BarOf(r.Chosen());
        }

        /// <summary>A game option's list: Ymir's own value first ("As set in Ymir"), the values offered, and the game's when it is
        /// none of them (one Ymir wrote, a BIOS or ROM no longer there).</summary>
        private void FillOption(Row r, string value)
        {
            var o = r.Option;
            if (value == LbipSelection.Various) { r.Combo.Items.Add(_sel.VariousText(r.Key)); r.Values.Add(LbipSelection.Various); }
            string head;
            if (!YmirGameOptions.InYmir(o, _doc)) head = "As set in Ymir - not in this Ymir (ConfigVersion " + YmirLaunch.ConfigVersionOf(_doc) + "), never written";
            else
            {
                var now = YmirGameOptions.Current(o, _doc);
                head = "As set in Ymir" + (now != null ? " (" + YmirGameOptions.TextOf(o, now, _exe) + ")" : _doc == null ? "" : " (Ymir's default)");
            }
            r.Combo.Items.Add(head); r.Values.Add(null);
            foreach (var (v, text) in o.Choices(_exe)) { r.Combo.Items.Add(text); r.Values.Add(v); }
            if (value != null && value != LbipSelection.Various && !r.Values.Contains(value)) { r.Combo.Items.Add(YmirGameOptions.TextOf(o, value, _exe)); r.Values.Add(value); }
            r.Combo.SelectedIndex = Math.Max(0, r.Values.IndexOf(value));
            r.Bar.BackColor = LbipSelection.BarOf(r.Chosen());
        }

        /// <summary>What goes on Ymir's command line with what the window shows - for the one game, or the first of several.</summary>
        private void ShowLine()
        {
            if (_line == null || _rows.Count == 0) return;
            var r = _rows.First(x => x.Key == YmirSettings.Fullscreen);
            var v = r.Chosen();
            bool various = v == LbipSelection.Various;
            bool full = (v == null || various ? YmirSettings.Value(YmirSettings.Fullscreen) : v) == "true";
            var profile = _exe == null ? "<Ymir's folder>" : YmirLaunch.ProfileOf(_exe);
            _line.Text = "-p \"" + profile + "\"" + (various ? " [-f: per game]" : full ? " -f" : "") + " \"<" + (Several ? "each game's disc" : "the game's first disc") + ">\"";
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
                    if (k.StartsWith(Opt)) YmirGameOptions.SetGameValue(id, k.Substring(Opt.Length), v);
                    else YmirSettings.SetGameValue(id, k, v);
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
            => games != null && games.Any(YmirGameMenu.IsOurs) ? new[] { YmirGameMenu.Caption } : new string[0];

        public static void Selected(string entry, IGame[] games)
        {
            if (entry == YmirGameMenu.Caption) YmirGameMenu.Open(games ?? new IGame[0]);
        }

        public static Image Icon => YmirGameMenu.Icon;
    }
}
