// The "Patches" tab of a game's options window (Mehdi, 03/10): ONE list of the game's patches, each on or off - whatever
// version of the game the user has. See XeniaPatches for what Xenia does with them.
//
// WHY ONE LIST AND NOT ONE FILE TO PICK (Mehdi, 03/10, on Minecraft's six files TU0...TU75: "je comprends pas ta
// fenêtre"): a game has a patch file per version - per title update - and Xenia applies only the file whose hash list
// holds the hash of the game as it runs. So every file of the game goes to Xenia's folder and Xenia picks: the user
// ticks a patch by its name, it is ticked in every file that has it, and the one file for his version is the one that
// counts. Nothing is downloaded until a patch is ticked; a patch only some versions have says which.
//
// AFTER A LAUNCH the plugin knows the hash of the user's version (Xenia's log, XeniaPatches.Seen): the tab then says
// which file is his version's, greys what that file does not have, and which ticked patches Xenia actually applied.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LbIntegrations.Xenia
{
    internal sealed class XeniaPatchesTab : Panel
    {
        private static readonly Color Good = Color.FromArgb(0x2E, 0xA0, 0x43), Bad = Color.FromArgb(0xD7, 0x3A, 0x49);
        private readonly string _titleId;
        private readonly XeniaLayout _layout;
        private readonly FlowLayoutPanel _stack;
        private readonly List<(string Name, CheckBox Box)> _boxes = new List<(string, CheckBox)>();
        /// <summary>Each patch name's choice, kept across rebuilds - the files are written only at OK.</summary>
        private readonly Dictionary<string, bool> _wanted = new Dictionary<string, bool>(StringComparer.Ordinal);
        /// <summary>The index's files for the game, read - null until asked for.</summary>
        private List<(XeniaPatchSource Source, XeniaPatchFile File)> _remote;
        private bool _asked;
        private string _note;
        private readonly string _rom;
        /// <summary>The title update chosen in the Updates &amp; DLC tab, as it stands in the window (not saved yet).</summary>
        private readonly Func<Dictionary<string, string>> _choice;
        private readonly string _gameId;

        public XeniaPatchesTab(string titleId, string exe, string rom, string gameId, Func<Dictionary<string, string>> choice)
        {
            _titleId = titleId;
            _rom = rom;
            _gameId = gameId;
            _choice = choice;
            _layout = exe != null && XeniaPaths.ForkOf(exe) == XeniaFork.Canary ? XeniaPaths.Resolve(exe) : null;
            Dock = DockStyle.Fill;
            AutoScroll = true;
            Padding = new Padding(10);
            _stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top };
            Controls.Add(_stack);
            if (_layout != null && _titleId != null)
                foreach (var f in XeniaPatches.ForTitle(_layout, _titleId))
                    foreach (var p in f.Patches.Where(p => p.Name != null))
                        _wanted[p.Name] = (_wanted.TryGetValue(p.Name, out var w) && w) || p.Enabled;
            _remote = Remote(TimeSpan.Zero);     // what the copies hold: asked for when the tab is shown
            Build();
        }

        /// <summary>The index's files for the game, each read - from the copies (zero) or within <paramref name="timeout"/>.</summary>
        private List<(XeniaPatchSource, XeniaPatchFile)> Remote(TimeSpan timeout)
        {
            if (_titleId == null) return null;
            var sources = XeniaPatches.Sources(_titleId, timeout);
            if (sources == null) return null;
            var list = new List<(XeniaPatchSource, XeniaPatchFile)>();
            foreach (var s in sources)
            {
                var text = XeniaPatches.SourceText(s, timeout);
                var f = text == null ? null : XeniaPatches.Parse(text, s.Name);
                if (f != null) list.Add((s, f));
                else if (timeout > TimeSpan.Zero) return list.Count == 0 ? null : list;     // the network gave up: what was had
                else return null;                                                           // a copy missing: ask when shown
            }
            return list;
        }

        /// <summary>The tab shown: the index and its files asked for once, in the background.</summary>
        public void Showing()
        {
            // Again at every showing: the title update may have been changed in the Updates & DLC tab meanwhile.
            KeepChoices();
            Build();
            if (_asked || _titleId == null || _layout == null) return;
            _asked = true;
            Task.Run(() => Remote(TimeSpan.FromSeconds(15))).ContinueWith(t =>
            {
                try
                {
                    if (IsDisposed || !IsHandleCreated || t.Status != TaskStatus.RanToCompletion) return;
                    BeginInvoke(new Action(() =>
                    {
                        KeepChoices();
                        _remote = t.Result ?? _remote;
                        _note = t.Result == null ? "xenia-canary/game-patches could not be reached: what the copies hold is shown." : null;
                        Build();
                    }));
                }
                catch { }
            });
        }

        private Label Line(string text, Color? color = null, int indent = 0) => new Label
        {
            Text = text, AutoSize = true, MaximumSize = new Size(560 - indent, 0), Margin = new Padding(indent, 2, 0, 4),
            ForeColor = color ?? SystemColors.GrayText, UseMnemonic = false,     // "Updates & DLC", not "Updates _DLC"
        };

        /// <summary>Every patch file of the game, by name - the one in Xenia's folder when there is one, else the index's.</summary>
        private List<XeniaPatchFile> AllFiles()
        {
            var local = XeniaPatches.ForTitle(_layout, _titleId);
            var all = new List<XeniaPatchFile>(local);
            foreach (var (s, f) in _remote ?? new List<(XeniaPatchSource, XeniaPatchFile)>())
                if (!local.Any(l => string.Equals(l.FileName, s.Name, StringComparison.OrdinalIgnoreCase))) all.Add(f);
            return all;
        }

        private static string Label(XeniaPatchFile f) => XeniaPatches.VersionLabel(f.FileName) ?? f.FileName;

        /// <summary>"XBLA, TU0", "XBLA, TU1" -> "XBLA: TU0, TU1" - what they share said once.</summary>
        private static string Labels(IEnumerable<XeniaPatchFile> files)
        {
            var labels = files.Select(Label).ToList();
            var heads = labels.Select(l => { int c = l.LastIndexOf(", ", StringComparison.Ordinal); return c > 0 ? l.Substring(0, c) : null; }).ToList();
            if (labels.Count > 1 && heads.All(h => h != null) && heads.Distinct().Count() == 1)
                return heads[0] + ": " + string.Join(", ", labels.Select(l => l.Substring(l.LastIndexOf(", ", StringComparison.Ordinal) + 2)));
            return string.Join("; ", labels);
        }

        private static string VersionWords(string version)
            => version.Length == 0 ? "no title update, TU0" : "TU" + (XeniaPatches.TuNumber(version)?.ToString() ?? "?") + ", version " + version;

        /// <summary>The version that will run - the title update chosen in Updates &amp; DLC ("" for none), else the last
        /// launch's - and its patch file: by the hash that version ran with when one launch showed it (sure), else by the
        /// title update number in the files' names (to be made sure of). <c>what</c>: the version in words, null when
        /// nothing is known.</summary>
        private (string Version, XeniaPatchFile Mine, bool Sure, string What) Mine(List<XeniaPatchFile> files, XeniaPatchSeen seen)
        {
            string version = null, source = null;
            try
            {
                var extras = string.IsNullOrEmpty(_rom) ? null : XeniaExtras.For(_rom, _layout);
                if (extras != null)
                {
                    var (update, _) = XeniaExtras.Chosen(extras, _choice?.Invoke() ?? XeniaExtras.ReadChoice(_gameId));
                    version = update == null ? "" : XeniaScan.VersionText(update.Entry.PatchTo);
                    source = "the title update chosen in Updates & DLC";
                }
            }
            catch (Exception ex) { Log.Info("patches: the chosen title update: " + ex.Message); }
            if (version == null && seen != null) { version = seen.Version ?? ""; source = "the last launch"; }
            if (version == null) return (null, null, false, null);

            var what = "The game runs as " + VersionWords(version) + " (" + source + ")";
            var (file, sure) = XeniaPatches.FileFor(files, _titleId, version);
            return (version, file, sure, what);
        }

        private void Build()
        {
            _stack.SuspendLayout();
            _stack.Controls.Clear();
            _boxes.Clear();
            if (_layout == null) { _stack.Controls.Add(Line("No Xenia Canary for this game in the library.", SystemColors.ControlText)); _stack.ResumeLayout(); return; }
            if (_titleId == null) { _stack.Controls.Add(Line("This game's title id could not be read off its file: its patches cannot be found.", SystemColors.ControlText)); _stack.ResumeLayout(); return; }

            var files = AllFiles();
            var seen = XeniaPatches.Seen(_titleId);
            var (version, mine, sure, what) = Mine(files, seen);
            bool decided = mine != null || sure;       // sure and no file: no patch is for this version

            _stack.Controls.Add(Line("Tick a patch and Xenia applies it each time the game starts. A game has one patch file per version (per title "
                                     + "update): they all go to Xenia's folder, and Xenia uses the one for the version you play. Title id " + _titleId + "."));
            if (files.Count == 0)
            {
                _stack.Controls.Add(Line(_remote == null ? (_note ?? "Looking at xenia-canary/game-patches...") : "There is no patch for this game in xenia-canary/game-patches.",
                                         SystemColors.ControlText));
            }
            else
            {
                // The version that will run, and its file.
                if (what == null)
                    _stack.Controls.Add(Line("Patches exist for: " + Labels(files) + ". Which one is yours depends on the title update the game runs with "
                                             + "(Updates & DLC tab); after a launch, this tab knows it for sure."));
                else if (mine != null)
                    _stack.Controls.Add(Line(what + ": its patch file is " + Label(mine) + (sure ? "." : " - by its name, made sure of at the next launch."), Good));
                else
                    _stack.Controls.Add(Line(what + ": " + (sure ? "no patch file is for it" : "no patch file is named for it") + ". Patches exist for " + Labels(files)
                                             + " - another title update (Updates & DLC tab) may be the one they need.", Bad));
                if (seen != null && seen.Applied.Count > 0 && (version == null || version == (seen.Version ?? "")))
                    _stack.Controls.Add(Line("At the last launch, Xenia applied: " + string.Join(", ", seen.Applied) + ".", SystemColors.ControlText));

                // One line per patch name, every version together - those the version that runs does not have greyed.
                var names = files.SelectMany(f => f.Patches.Where(p => p.Name != null).Select(p => p.Name)).Distinct().ToList();
                foreach (var name in names)
                {
                    var having = files.Where(f => f.Patches.Any(p => p.Name == name)).ToList();
                    var patch = having.SelectMany(f => f.Patches).First(p => p.Name == name);
                    bool forMine = !decided || (mine != null && having.Contains(mine));
                    var cb = new CheckBox
                    {
                        Text = name + (string.IsNullOrWhiteSpace(patch.Author) ? "" : "  (" + patch.Author + ")"), AutoSize = true, Margin = new Padding(0, 6, 0, 0),
                        Checked = _wanted.TryGetValue(name, out var w) && w, Enabled = forMine,
                    };
                    _stack.Controls.Add(cb);
                    _boxes.Add((name, cb));
                    if (!string.IsNullOrWhiteSpace(patch.Desc)) _stack.Controls.Add(Line(patch.Desc, null, 18));
                    if (having.Count < files.Count) _stack.Controls.Add(Line("Only for " + Labels(having) + ".", null, 18));
                    if (decided && !forMine) _stack.Controls.Add(Line("Not for the version that runs" + (version != null ? " (" + VersionWords(version) + ")" : "") + ".", null, 18));
                    else if (sure && cb.Checked && seen != null && version == (seen.Version ?? "") && !seen.Applied.Contains(name, StringComparer.OrdinalIgnoreCase))
                        _stack.Controls.Add(Line("Ticked, but not applied at the last launch.", Bad, 18));
                }
                if (_remote == null) _stack.Controls.Add(Line(_note ?? "Looking at xenia-canary/game-patches for more..."));
                else if (_note != null) _stack.Controls.Add(Line(_note));
            }

            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 10, 0, 0) };
            var open = new Button { Text = "Open the patches folder", AutoSize = true };
            open.Click += (_, _) =>
            {
                try { Directory.CreateDirectory(XeniaPatches.Folder(_layout)); Process.Start(new ProcessStartInfo(XeniaPatches.Folder(_layout)) { UseShellExecute = true }); }
                catch (Exception ex) { Log.Warn("could not open the patches folder", ex); }
            };
            row.Controls.Add(open);
            _stack.Controls.Add(row);
            _stack.ResumeLayout();
        }

        private void KeepChoices()
        {
            foreach (var (name, box) in _boxes) _wanted[name] = box.Checked;
        }

        /// <summary>OK in the window: when a patch is ticked, every patch file of the game in Xenia's folder - the missing
        /// ones downloaded, those the index has newer brought up to date - then each file's patches ticked as chosen. With
        /// nothing ticked, nothing is downloaded: the files already there are only turned off.</summary>
        public void Save()
        {
            if (_layout == null || _titleId == null) return;
            KeepChoices();
            try
            {
                if (_wanted.Values.Any(v => v) && _remote != null)
                {
                    var local = XeniaPatches.ForTitle(_layout, _titleId);
                    foreach (var (s, _) in _remote)
                    {
                        var here = local.FirstOrDefault(l => string.Equals(l.FileName, s.Name, StringComparison.OrdinalIgnoreCase));
                        string sha = null;
                        if (here != null) { try { sha = XeniaPatches.BlobSha(here.Path); } catch { } }
                        if (here == null || (sha != null && !string.Equals(sha, s.Sha, StringComparison.OrdinalIgnoreCase)))
                            XeniaPatches.Download(_layout, s, TimeSpan.FromSeconds(20), out _);
                    }
                }
                foreach (var f in XeniaPatches.ForTitle(_layout, _titleId))
                {
                    var mine = f.Patches.Where(p => p.Name != null && _wanted.ContainsKey(p.Name)).Select(p => p.Name).Distinct().ToDictionary(n => n, n => _wanted[n]);
                    if (mine.Count > 0) XeniaPatches.SetEnabled(f.Path, mine);
                }
            }
            catch (Exception ex) { Log.Warn("patches: the choices could not be written", ex); }
        }
    }
}
