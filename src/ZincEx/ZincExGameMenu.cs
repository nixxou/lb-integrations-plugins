// The right-click entry on the games ZiNc EX runs - one game or a selection: "Nixx-ZiNc-EX : Options...". The window,
// dressed as every Nixx window (NixxShell), in tabs as ZiNc EX's own Game Settings window (gui-c\gamecfg.c
// dlg_game_settings): General, Video (the Direct3D 11 renderer's), Audio, Other.
//
// THE OPTIONS ARE ZINC EX'S OWN per-game settings (Mehdi, 06/10: the per-game window rich, the emulator's settings the
// emulator's): read from and written into zinc-games.cfg beside it, under the ZiNc game the romset was found to be
// (ZincExGameCfg) - so ZiNc EX's own window shows the same, and a launch from LaunchBox or from ZiNc EX takes them. Each one
// "As set in ZiNc EX (<its value for every game>)" until changed, read from its settings. A bar at the left of each says
// where it comes from (OptionMarks); "Reset to defaults" puts them all back to ZiNc EX's.
//
// The General tab also says what the game is to ZiNc - the game its romset was found to be, the zips ZiNc EX is given, or
// why it cannot start - with the ZiNc game to start it as when telling it by its ROMs is not what is wanted (kept in the
// plugin's games\<game id>.ini). At the bottom, the command line the launch gives ZiNc EX.
//
// SEVERAL GAMES (the pack's rule, Mehdi, 06/10 - LbipSelection): at the top "All selected games", the default, then the
// games grouped by identical options. A setting they do not agree on shows "Various (n of N)", purple: left so, each keeps
// its own; changed, it goes to all. OK asks first, then writes only what was changed. A game whose romset is none of ZiNc's
// games has no ZiNc settings: it is left out, and said.
//
// NOT AN IGameMenuItemPlugin, as for the other plugins of the pack: LaunchBox 14 gives a plugin of Local\Plugins its
// emulator role only; Nixx-Menus relays to the GameMenu class below by its name - src\Menus\Menus.cs has the contract.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LbIntegrations.Lbip;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.ZincEx
{
    internal static class ZincExGameMenu
    {
        public const string Caption = "Nixx-ZiNc-EX : Options...";

        public static Image Icon => LbipMenuIcon.Of(ZincExLaunch.IsZincEx, ZincExPlugin.ResolveFullPathForUi);

        /// <summary>A game of ours: its own emulator is a ZiNc EX - Arcade is MAME's platform, and only 71 of its games are
        /// ZiNc's, so the platform alone does not make one. Asked at every right-click: lookups in memory only.</summary>
        internal static bool IsOurs(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null || game == null || string.IsNullOrWhiteSpace(game.EmulatorId)) return false;
                var own = dm.GetEmulatorById(game.EmulatorId);
                return own != null && ZincExLaunch.IsZincEx(own.ApplicationPath);
            }
            catch { return false; }
        }

        internal static string ExeOf(IGame game)
        {
            try
            {
                var own = PluginHelper.DataManager?.GetEmulatorById(game.EmulatorId);
                var path = ZincExPlugin.ResolveFullPathForUi(own?.ApplicationPath);
                return File.Exists(path) ? path : null;
            }
            catch { return null; }
        }

        public static void Open(IGame[] games)
        {
            try
            {
                var ours = games.Where(IsOurs).ToArray();
                if (ours.Length == 0) return;
                using var form = new ZincExGameForm(ours);
                form.ShowDialog();
            }
            catch (Exception ex) { Log.Warn("the game's window", ex); MessageBox.Show("The window could not open: " + ex.Message, ZincExPlugin.PackName, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }

    internal sealed class ZincExGameForm : Form
    {
        /// <summary>One of ZiNc EX's per-game settings: its tab, its list (what its items stand for: Various, null = as set in
        /// ZiNc EX, a value as zinc-games.cfg writes it), its bar.</summary>
        private sealed class Option
        {
            public string Key, Tab, Label, Hint;
            public List<(string Value, string Text)> Items;
            public ComboBox Combo;
            public Panel Bar;
            public readonly List<string> Shown = new List<string>();
            public string Chosen() => Combo.SelectedIndex < 0 ? null : Shown[Combo.SelectedIndex];
            public string TextOf(string value) => Items.FirstOrDefault(i => i.Value == value).Text ?? value;
        }

        /// <summary>A game of the selection: its ZiNc game (null: none) and its ZiNc EX's folder.</summary>
        private sealed class Entry
        {
            public IGame Game;
            public string Id, Title, ExeDir, Rom, Problem;
            public ZincGames.Match Match;
        }

        private readonly List<Entry> _entries;
        private readonly List<Entry> _ours;
        private readonly LbipSelection _selection;
        private readonly List<Option> _options = new List<Option>();
        private readonly string _exeDir;
        private Dictionary<string, string> _atOpen;
        private ComboBox _set;
        private string _setAtOpen;
        private Label _count;
        private TextBox _line;

        public ZincExGameForm(IGame[] games)
        {
            _entries = games.Select(g =>
            {
                var e = new Entry { Game = g, Id = Safe(() => g.Id), Title = Safe(() => g.Title) ?? "?", Rom = ZincExPlugin.ResolveFullPathForUi(Safe(() => g.ApplicationPath)) ?? "" };
                var exe = ZincExGameMenu.ExeOf(g);
                e.ExeDir = exe == null ? null : Path.GetDirectoryName(exe);
                if (exe == null) e.Problem = "Its ZiNc EX is not there - the emulator's path in LaunchBox points nowhere.";
                else e.Match = ZincExLaunch.Identify(exe, e.Rom, e.Id, out e.Problem);
                return e;
            }).ToList();
            _ours = _entries.Where(e => e.Match != null).ToList();
            _exeDir = (_ours.FirstOrDefault() ?? _entries[0]).ExeDir;
            _options.AddRange(Options(_exeDir));
            var members = (_ours.Count > 0 ? _ours : _entries.Take(1)).Select(e => new LbipSelection.Member { Title = e.Title, Stored = StoredOf(e), Tag = e }).ToList();
            _selection = new LbipSelection(members);
            bool several = _selection.Several;
            AddStoredValues();

            Text = games.Length == 1 ? ZincExPlugin.PackName + " - " + _entries[0].Title : ZincExPlugin.PackName + " - " + games.Length + " games";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ClientSize = new Size(760, 620);
            MinimumSize = new Size(640, 480);
            Font = new Font("Segoe UI", 9f);

            // ── the top: the game, or which games and where to start from ──
            var top = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(12, 8, 12, 0) };
            var left = _entries.Count - _ours.Count;
            top.Controls.Add(new Label
            {
                Dock = DockStyle.Top, Height = 24, UseMnemonic = false, Font = new Font("Segoe UI Semibold", 11f),
                Text = games.Length == 1 ? _entries[0].Title : games.Length + " games" + (left > 0 ? " - " + left + " of them none of ZiNc's, left out" : ""),
            });
            var pick = _selection.Top(_options.Select(o => o.Key), Describe, Fill);

            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 4) };
            var tables = new Dictionary<string, TableLayoutPanel>();
            foreach (var name in new[] { "General", "Video", "Audio", "Other" }) tables[name] = Table();

            // ── General: what the game is to ZiNc (one game) ──
            if (games.Length == 1)
            {
                var e = _entries[0];
                AddText(tables["General"], "File", e.Rom);
                if (e.Match == null) AddText(tables["General"], "ZiNc", e.Problem, bold: true);
                else
                {
                    AddText(tables["General"], "ZiNc's game", e.Match.Game.Title + " - set " + e.Match.Game.Set + ", game " + e.Match.Game.Id
                                                             + (ZincExSettings.SetFor(e.Id) != null ? " (chosen below)" : e.Match.ByName ? "" : " (told by its ROMs)"), bold: true);
                    AddText(tables["General"], "Hardware", ZincCompat.Board(e.Match.Game));
                    var notes = ZincCompat.Notes(e.Match.Game);
                    var known = AddText(tables["General"], "Known issues", notes.Count == 0 ? "None in ZiNc's notes." : string.Join("\n", notes));
                    if (notes.Count > 0) { known.ForeColor = Color.FromArgb(0xE3, 0xA3, 0x4A); known.Tag = LbIntegrations.Ui.NixxTheme.Own; }
                    LbipHint.Attach(known, "From ZiNc 1.1's own readme, which lists its faults by board - there is no game-by-game compatibility list of ZiNc.");
                    AddText(tables["General"], "Given as", string.Join(", ", e.Match.Zips.Select(kv => kv.Key + ".zip" + (ZincGames.SamePath(kv.Value, e.Rom)
                                                             ? (kv.Key == e.Match.Game.Set ? "" : " (this romset)") : " <- " + Path.GetFileName(kv.Value)))));
                    var save = e.ExeDir == null ? null : Path.Combine(e.ExeDir, "cfg", e.Match.Game.Set + ".cfg");
                    AddText(tables["General"], "High scores", save != null && File.Exists(save) ? "cfg\\" + Path.GetFileName(save) + ", " + File.GetLastWriteTime(save).ToString("g") : "none yet");
                }
                _set = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
                _set.Items.Add("Told by its ROMs (recommended)");
                foreach (var g in Sorted()) _set.Items.Add(g.Title + "  [" + g.Set + "]");
                _setAtOpen = ZincExSettings.SetFor(e.Id);
                _set.SelectedIndex = _setAtOpen == null ? 0 : 1 + Sorted().FindIndex(g => g.Set == _setAtOpen);
                _set.SelectedIndexChanged += (_, _) => Changed();
                LbipHint.Attach(_set, "The game a romset is is told by what it holds: every ROM of one of ZiNc's 71 games in it, its parent's or its BIOS's zip. "
                                    + "Choose one only when that is not the one you want (a merged set holds several). Its settings below are those of the game found when the window opened.");
                AddRow(tables["General"], null, "Start it as", _set);
            }

            // ── the options, each on its tab ──
            bool any = _ours.Count > 0;
            foreach (var o in _options)
            {
                o.Combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, Enabled = any };
                o.Bar = new Panel { Width = 4, Height = 18, Margin = new Padding(0, 6, 8, 0) };
                o.Combo.SelectedIndexChanged += (_, _) => Changed();
                if (o.Hint != null) LbipHint.Attach(o.Combo, o.Hint);
                AddRow(tables[o.Tab], o.Bar, o.Label, o.Combo);
            }
            AddText(tables["Video"], "", "The Direct3D 11 renderer's settings - the other renderers do not have them.");
            tabs.TabPages.Add(Page("General", tables["General"]));
            tabs.TabPages.Add(Page("Video", tables["Video"]));
            tabs.TabPages.Add(Page("Audio", tables["Audio"]));
            tabs.TabPages.Add(Page("Other", tables["Other"]));

            // ── the bottom: the command line, then the pack's legend, reset, OK and Cancel ──
            var summary = new Panel { Dock = DockStyle.Bottom, Height = 96, Padding = new Padding(12, 6, 12, 2) };
            _count = new Label { Dock = DockStyle.Top, Height = 20, ForeColor = SystemColors.GrayText };
            _line = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, Font = new Font("Consolas", 9f), BackColor = SystemColors.Window, TabStop = false };
            var note = LbipHint.Note("Written into ZiNc EX's zinc-games.cfg: its own Game Settings window shows them too.",
                                     "\"As set in ZiNc EX\" writes nothing: the game follows ZiNc EX's main settings. The window option on the Nixx page (--fullscreen / "
                                     + "--windowed) is not passed for a game that has its own Window here.", 720, new Padding(0, 2, 0, 0));
            note.Dock = DockStyle.Bottom;
            summary.Controls.Add(_line);
            summary.Controls.Add(_count);
            summary.Controls.Add(note);

            var ok = new Button { Text = "OK", Width = 96, Height = 28 };
            var cancel = new Button { Text = "Cancel", Width = 96, Height = 28, DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => { try { if (Save()) { DialogResult = DialogResult.OK; Close(); } } catch (Exception ex) { MessageBox.Show(this, "The options could not be written: " + ex.Message, ZincExPlugin.PackName); } };
            var bottom = OptionMarks.Bottom(OptionMarks.Legend("ZiNc EX", false, false, various: several), OptionMarks.ResetButton(Reset), ok, cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(tabs);
            if (pick != null) Controls.Add(pick);
            Controls.Add(top);
            Controls.Add(summary);
            Controls.Add(bottom);
            if (_selection.Source != null) _selection.Source.SelectedIndex = 0;
            else Fill(_selection.From(_selection.Members[0], _options.Select(o => o.Key)));
            LbIntegrations.Ui.NixxShell.Dress(this);
        }

        private static List<ZincGame> Sorted() => ZincGames.All.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToList();

        // ── the options ──────────────────────────────────────────────────────

        /// <summary>ZiNc EX's per-game settings, its words (gamecfg.c dlg_game_settings) - "As set in ZiNc EX" is its "Same As
        /// Global". The renderers, resolutions, bezels and profiles are this install's.</summary>
        private static IEnumerable<Option> Options(string exeDir)
        {
            var onOff = new List<(string, string)> { ("1", "On"), ("0", "Off") };
            List<(string, string)> L(params string[] pairs) { var l = new List<(string, string)>(); for (int i = 0; i + 1 < pairs.Length; i += 2) l.Add((pairs[i], pairs[i + 1])); return l; }
            var renderers = (exeDir == null ? new List<string>() : ZincExGameCfg.Renderers(exeDir)).Select(r => (r, r == "Direct3D11" ? "Direct3D 11" : r)).ToList();
            var sizes = Modes().Select(m => (m.W + "x" + m.H, m.W + " x " + m.H + (m.W * 3 == m.H * 4 ? " (4:3)" : ""))).ToList();
            var bezels = new List<(string, string)> { ("-", "None") };
            if (exeDir != null) bezels.AddRange(ZincExGameCfg.Bezels(exeDir).Select(b => (b, b)));
            var profiles = exeDir == null ? new List<(string, string)>() : ZincExGameCfg.Profiles(exeDir).Select(p => (p, p)).ToList();

            yield return new Option { Key = "renderer", Tab = "General", Label = "Renderer", Items = renderers };
            yield return new Option { Key = "rotate", Tab = "General", Label = "Rotation", Items = L("-1", "Game default", "0", "None", "1", "90°", "2", "180°", "3", "270°"),
                                      Hint = "90 / 270 degrees are for vertical (tate) games shown on a turned monitor." };
            yield return new Option { Key = "fullscreen", Tab = "General", Label = "Window", Items = L("0", "Window", "1", "Full screen") };
            yield return new Option { Key = "size", Tab = "General", Label = "Resolution", Items = sizes, Hint = "The screen's mode for full screen (Direct3D 11: when Fullscreen Mode is not Borderless)." };
            yield return new Option { Key = "bezel", Tab = "General", Label = "Bezel (D3D11)", Items = bezels, Hint = "A PNG of ZiNc EX's bezels folder. As set in ZiNc EX: bezels\\<set>.png when there is one, else the main settings' bezel." };
            yield return new Option { Key = "borderless", Tab = "Video", Label = "Fullscreen mode", Items = L("0", "Fullscreen", "1", "Borderless"),
                                      Hint = "Fullscreen: the screen is switched to the Resolution. Borderless: a borderless window over the desktop, faster to start and to switch away." };
            yield return new Option { Key = "aspect", Tab = "Video", Label = "Aspect ratio", Items = L("0", "Stretch to window", "1", "4:3", "2", "16:9", "3", "Pixel perfect") };
            yield return new Option { Key = "scale", Tab = "Video", Label = "Internal resolution", Items = L("0", "Auto", "1", "1x (console resolution)", "2", "2x", "3", "3x", "4", "4x") };
            yield return new Option { Key = "xbrz", Tab = "Video", Label = "xBRZ filter", Items = L("0", "Off", "1", "All graphics", "2", "2D objects only"),
                                      Hint = "All graphics: the game drawn at the console's resolution and enlarged with xBRZ. 2D objects only: sprites and backgrounds get it, 3D stays sharp." };
            yield return new Option { Key = "texture_smoothing", Tab = "Video", Label = "3D texture smoothing", Items = onOff };
            yield return new Option { Key = "fxaa", Tab = "Video", Label = "FXAA filter", Items = onOff };
            yield return new Option { Key = "dither_smoothing", Tab = "Video", Label = "Dither smoothing", Items = L("0", "Off", "1", "Low", "2", "Medium", "3", "High") };
            yield return new Option { Key = "vsync", Tab = "Video", Label = "V-Sync", Items = onOff };
            yield return new Option { Key = "overscan", Tab = "Video", Label = "Overscan crop", Items = L("0", "Off", "4", "4 pixels", "8", "8 pixels", "12", "12 pixels", "16", "16 pixels", "24", "24 pixels", "32", "32 pixels") };
            yield return new Option { Key = "slow_geometry", Tab = "Video", Label = "Slow geometry", Items = onOff, Hint = "Slower but more accurate geometry: Gallop Racer 3's ground breaks up without it (ZiNc's readme)." };
            yield return new Option { Key = "sound", Tab = "Audio", Label = "Sound", Items = onOff };
            yield return new Option { Key = "sound_filter", Tab = "Audio", Label = "Sound filter", Items = onOff };
            yield return new Option { Key = "surround", Tab = "Audio", Label = "Lite surround", Items = onOff };
            yield return new Option { Key = "exciter", Tab = "Audio", Label = "Stereo exciter", Items = onOff };
            yield return new Option { Key = "profile", Tab = "Other", Label = "Controls profile", Items = profiles, Hint = "A profile saved in ZiNc EX's Controls tab. As set in ZiNc EX: the current controls." };
            yield return new Option { Key = "trainer", Tab = "Other", Label = "Start trainer", Items = L("1", "Yes", "0", "No"), Hint = "The trainer program of ZiNc EX's General tab, started with the game." };
        }

        /// <summary>A value a game has that the list does not offer (a resolution of another screen, a bezel since taken away): offered
        /// too, so it shows and is kept.</summary>
        private void AddStoredValues()
        {
            foreach (var o in _options)
                foreach (var m in _selection.Members)
                    if (m.Stored.TryGetValue(o.Key, out var v) && v != null && !o.Items.Any(i => i.Value == v)) o.Items.Add((v, v));
        }

        /// <summary>The game's own values in zinc-games.cfg, under its ZiNc game.</summary>
        private Dictionary<string, string> StoredOf(Entry e)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (e.Match == null || e.ExeDir == null) return d;
            foreach (var kv in ZincExGameCfg.Of(e.ExeDir, e.Match.Game.Id))
                if (_options.Any(o => o.Key == kv.Key)) d[kv.Key] = kv.Value;
            return d;
        }

        private string Describe(LbipSelection.Member m)
        {
            var mine = _options.Where(o => m.Stored.ContainsKey(o.Key)).Select(o => o.Label).ToList();
            return mine.Count == 0 ? "ZiNc EX's" : "its own: " + string.Join(", ", mine);
        }

        /// <summary>The lists, with what they show now: "Various (n of N)" first only for a key the games do not agree on.</summary>
        private void Fill(Dictionary<string, string> shown)
        {
            foreach (var o in _options)
            {
                shown.TryGetValue(o.Key, out var value);
                o.Combo.Items.Clear(); o.Shown.Clear();
                if (value == LbipSelection.Various) { o.Combo.Items.Add(_selection.VariousText(o.Key)); o.Shown.Add(LbipSelection.Various); }
                var global = _exeDir == null ? "" : ZincExGameCfg.Global(_exeDir, o.Key);
                var globalText = o.Key == "profile" ? "current controls" : o.Key == "bezel" && global.Length == 0 ? "bezels\\<set>.png, else none" : o.TextOf(global);
                if (o.Key == "size") globalText = global.Replace("x", " x ");
                o.Combo.Items.Add("As set in ZiNc EX" + (string.IsNullOrEmpty(globalText) ? "" : " (" + globalText + ")")); o.Shown.Add(null);
                foreach (var (v, t) in o.Items) { o.Combo.Items.Add(t); o.Shown.Add(v); }
                o.Combo.SelectedIndex = Math.Max(0, o.Shown.IndexOf(value));
            }
            _atOpen = ShownNow();
            Changed();
        }

        private Dictionary<string, string> ShownNow() => _options.ToDictionary(o => o.Key, o => o.Chosen(), StringComparer.Ordinal);

        /// <summary>The bars, and the line ZiNc EX is given.</summary>
        private void Changed()
        {
            foreach (var o in _options.Where(x => x.Bar != null)) o.Bar.BackColor = LbipSelection.BarOf(o.Chosen());
            if (_line == null) return;
            var window = _options.First(o => o.Key == "fullscreen").Chosen();
            var screen = window == null ? ZincExSettings.Screen : "zincex";   // a game with its own Window: no flag over it
            string set;
            if (_selection.Several) set = "<each game's ZiNc set>";
            else
            {
                var chosen = _set != null && _set.SelectedIndex > 0 ? Sorted()[_set.SelectedIndex - 1].Set : null;
                set = chosen ?? _entries[0].Match?.Game.Set ?? "?";
            }
            var mine = _options.Count(o => o.Chosen() != null && o.Chosen() != LbipSelection.Various);
            _count.Text = "The command line given to ZiNc EX" + (mine > 0 ? " - and " + mine + " setting(s) of the game's own in zinc-games.cfg" : "") + ":";
            _line.Text = ZincExLaunch.CommandLine("", "", set, screen);
            _line.Select(0, 0);
        }

        /// <summary>"Reset to defaults": every option back to ZiNc EX's - a Various too - and the game told by its ROMs. Nothing is
        /// saved before OK.</summary>
        private void Reset()
        {
            foreach (var o in _options) o.Combo.SelectedIndex = o.Shown.IndexOf(null);
            if (_set != null) _set.SelectedIndex = 0;
            Changed();
        }

        /// <summary>OK: over several games, asked first; then each game's ZiNc settings get only what the user changed, the rest of
        /// its own kept (LbipSelection.MergeFor) - a Various left as it is writes nothing. Not while ZiNc EX is open: it writes
        /// zinc-games.cfg whole from what it holds.</summary>
        private bool Save()
        {
            var now = ShownNow();
            var set = _set != null && _set.SelectedIndex > 0 ? Sorted()[_set.SelectedIndex - 1].Set : null;
            bool setChanged = _set != null && !string.Equals(set, _setAtOpen, StringComparison.OrdinalIgnoreCase);
            bool optionsChanged = _ours.Count > 0 && LbipSelection.Changed(_atOpen, now);
            if (!optionsChanged && !setChanged) return true;
            if (optionsChanged)
            {
                var running = _ours.Select(e => e.ExeDir).Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(ZincExBuild.Running);
                if (running != null)
                {
                    MessageBox.Show(this, "ZiNc EX is open (" + running + "): close it first - it writes its per-game settings back from what it holds.", ZincExPlugin.PackName,
                                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }
            }
            if (!_selection.Confirm(this, Text)) return false;
            if (setChanged)
            {
                var id = _entries[0].Id;
                if (!string.IsNullOrEmpty(id)) ZincExSettings.SetGameValue(id, "Set", set);
            }
            if (optionsChanged)
            {
                // Per ZiNc EX folder, per ZiNc game: two LaunchBox games of one ZiNc game share its settings.
                var writes = new Dictionary<string, Dictionary<int, IDictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in _selection.Members)
                {
                    var e = (Entry)m.Tag;
                    if (e.Match == null || e.ExeDir == null) continue;
                    var merged = _selection.MergeFor(m, _atOpen, now);
                    var change = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var o in _options)
                    {
                        merged.TryGetValue(o.Key, out var v);
                        m.Stored.TryGetValue(o.Key, out var before);
                        if (!string.Equals(v, before, StringComparison.Ordinal)) change[o.Key] = v;
                    }
                    if (change.Count == 0) continue;
                    if (!writes.TryGetValue(e.ExeDir, out var byGame)) writes[e.ExeDir] = byGame = new Dictionary<int, IDictionary<string, string>>();
                    byGame[e.Match.Game.Id] = change;
                }
                foreach (var w in writes) ZincExGameCfg.Write(w.Key, w.Value);
                Log.Info("options saved: " + writes.Sum(w => w.Value.Count) + " ZiNc game(s) changed in zinc-games.cfg");
            }
            if (setChanged) Log.Info("started as " + (set ?? "told by its ROMs") + ": " + _entries[0].Title);
            return true;
        }

        // ── the rows ─────────────────────────────────────────────────────────

        private static TableLayoutPanel Table()
        {
            var t = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(8, 8) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 12));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
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
            t.Controls.Add(new Label { Text = label, AutoSize = true, MaximumSize = new Size(166, 0), Margin = new Padding(0, 7, 4, 0), UseMnemonic = false }, 1, r);
            t.Controls.Add(field, 2, r);
        }

        private Label AddText(TableLayoutPanel t, string label, string text, bool bold = false)
        {
            var l = new Label { Text = text, AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(3, 7, 0, 4), UseMnemonic = false, Font = bold ? new Font(Font, FontStyle.Bold) : Font };
            AddRow(t, null, label, l);
            return l;
        }

        // ── the screen's modes, for Resolution ───────────────────────────────

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        /// <summary>The main screen's modes, largest first - as ZiNc EX lists them (ui_mode_count), 640 x 480 and up.</summary>
        private static List<(int W, int H)> Modes()
        {
            var set = new HashSet<(int, int)>();
            try
            {
                var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                for (int i = 0; EnumDisplaySettings(null, i, ref dm); i++)
                    if (dm.dmPelsWidth >= 640 && dm.dmPelsHeight >= 480 && dm.dmBitsPerPel >= 16) set.Add((dm.dmPelsWidth, dm.dmPelsHeight));
            }
            catch { }
            return set.OrderByDescending(m => m.Item1).ThenByDescending(m => m.Item2).ToList();
        }

        private static T Safe<T>(Func<T> get) { try { return get(); } catch { return default; } }
    }

    /// <summary>What Nixx-Menus calls, by name - see src\Menus\Menus.cs. Public, its signatures fixed.</summary>
    public static class GameMenu
    {
        public static string[] Entries(IGame[] games)
            => games != null && games.Any(ZincExGameMenu.IsOurs) ? new[] { ZincExGameMenu.Caption } : new string[0];

        public static void Selected(string entry, IGame[] games)
        {
            if (entry == ZincExGameMenu.Caption) ZincExGameMenu.Open(games ?? new IGame[0]);
        }

        public static Image Icon => ZincExGameMenu.Icon;
    }
}
