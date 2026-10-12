// The "Compatibility" page of the Nixx settings (Mehdi, 09/10 - docs\compat-page-plan.md): every patch of the pack to
// LaunchBox (Shared.Compat\CompatRegistry), grouped, each with its checkbox (on by default), what it relies on in LaunchBox
// (reflection checks) and whether the bug is still there (bug tests), and a text that unfolds: the problem, an example,
// what the patch does, what happens without it.
//
// TWO VIEWS (Mehdi, 13/10): the SIMPLE one, shown first - a line saying whether everything the pack relies on is in place,
// a few cards in plain words (CompatRegistry.Cards), each switching a set of patches with one light for all of them, and a
// safe mode that turns off everything that hooks into LaunchBox or reads its inner workings (CompatRegistry.Internal); the
// ADVANCED one - the list, one patch at a time. Both drive the same boxes: a card is the sum of its patches' boxes (half
// ticked when they differ). The view chosen is remembered ([page] of the file).
//
// NOTHING APPLIES AT ONCE. The boxes are a draft: the window's Apply / OK writes nixx-compat.ini and the banner asks to
// restart LaunchBox - the plugins read the file at their start only. The checks run when the page is first built, in the
// background, read-only.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LbIntegrations.Compat;
using LbIntegrations.Ui;

namespace LbIntegrations.Menus
{
    internal sealed class CompatPanel : Panel
    {
        public const string Title = "Compatibility";

        private readonly string _path;
        private readonly Dictionary<string, CheckBox> _boxes = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Label> _checkLabels = new Dictionary<string, Label>(), _bugLabels = new Dictionary<string, Label>();
        private readonly Dictionary<string, (CompatCheckState State, string Detail)> _checked = new Dictionary<string, (CompatCheckState, string)>(StringComparer.OrdinalIgnoreCase);
        private readonly List<(CompatCard Card, CheckBox Box, Label Light)> _cards = new List<(CompatCard, CheckBox, Label)>();
        private readonly Label _banner, _health, _safeNote;
        private readonly Button _safe;
        private readonly LinkLabel _switchView;
        private readonly FlowLayoutPanel _simple, _advanced;
        private string _view;
        private Dictionary<string, bool> _beforeSafe;
        private bool _loading = true, _checksDone;

        private const int Width0 = 660;

        public CompatPanel()
        {
            Dock = DockStyle.Fill;
            AutoScroll = true;
            Padding = new Padding(12);
            _path = Compat.Compat.PathOf(null);
            var saved = Compat.Compat.Load(_path);
            (_view, _beforeSafe) = Compat.Compat.LoadPage(_path);

            var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top };
            _switchView = new LinkLabel { AutoSize = true, Margin = new Padding(0, 0, 0, 8), LinkColor = NixxTheme.Link, ActiveLinkColor = NixxTheme.Link };
            _switchView.LinkClicked += (_, _) => ShowView(_view == "simple" ? "advanced" : "simple", remember: true);
            root.Controls.Add(_switchView);
            _banner = Text("", NixxTheme.Warn, bold: true);
            _banner.Visible = false;
            root.Controls.Add(_banner);

            // ── the advanced view: the list, as it always was ──
            _advanced = Stack();
            _advanced.Controls.Add(Text("Where the pack works around LaunchBox: what LaunchBox does not do, its bugs routed around, the "
                                      + "safeguards, what the pack changes in your LaunchBox data and where it hooks in. Several rely on "
                                      + "LaunchBox's inner workings, which an update can change - and the day LaunchBox fixes a bug, its "
                                      + "patch can go. Each one can be turned off here; all are on by default.", NixxTheme.Text));
            _advanced.Controls.Add(Text("Changes apply when LaunchBox (or BigBox) is restarted: the plugins read these choices when they start."));
            foreach (var group in CompatRegistry.All.GroupBy(p => p.Group))
            {
                _advanced.Controls.Add(Text(CompatRegistry.GroupTitle(group.Key), NixxTheme.Caption, bold: true, top: 10));
                foreach (var p in group) _advanced.Controls.Add(Card(p, saved));
            }

            // ── the simple view ──
            _simple = Stack();
            _health = Text("Checking what the pack relies on in LaunchBox...", NixxTheme.Text, bold: true);
            _health.MaximumSize = new Size(Width0 - 30, 0);
            _simple.Controls.Add(_health);
            foreach (var c in CompatRegistry.Cards) _simple.Controls.Add(SimpleCard(c));
            var safeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 10, 0, 2) };
            safeRow.Controls.Add(Text("Something broke after a LaunchBox update?", NixxTheme.Text, top: 6));
            _safe = new Button { AutoSize = true, Margin = new Padding(10, 0, 0, 0) };
            _safe.Click += (_, _) => ToggleSafe();
            safeRow.Controls.Add(_safe);
            _simple.Controls.Add(safeRow);
            _safeNote = Text("");
            _simple.Controls.Add(_safeNote);
            _simple.Controls.Add(Text("The pack also sets up its own emulators - their entries, scripts and archive extraction: see the "
                                    + "advanced view.", top: 6));
            _simple.Controls.Add(Text("Changes apply when LaunchBox (or BigBox) is restarted."));

            root.Controls.Add(_simple);
            root.Controls.Add(_advanced);
            Controls.Add(root);

            foreach (var b in _boxes.Values) b.CheckedChanged += (_, _) => { if (!_loading) RefreshCards(); };
            _loading = false;
            ShowView(_view, remember: false);
            RefreshCards();
            Pending(saved);
            HandleCreated += (_, _) => RunChecks();
        }

        private static FlowLayoutPanel Stack() => new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0) };

        private static Label Text(string t, Color? color = null, bool bold = false, int top = 0) => new Label
        {
            Text = t, AutoSize = true, MaximumSize = new Size(Width0, 0), Margin = new Padding(0, top, 0, 6),
            ForeColor = color ?? NixxTheme.Dim, Font = bold ? new Font("Segoe UI Semibold", 10f) : null, UseMnemonic = false,
        };

        private void ShowView(string view, bool remember)
        {
            _view = view == "advanced" ? "advanced" : "simple";
            _simple.Visible = _view == "simple";
            _advanced.Visible = _view == "advanced";
            _switchView.Text = _view == "simple" ? "Advanced view: every patch, one by one ▸" : "◂ Simple view";
            if (!remember) return;
            // Only the view: the switches stay as written until Apply.
            try { Compat.Compat.Save(_path, Compat.Compat.Load(_path), _view, Compat.Compat.LoadPage(_path).BeforeSafe); } catch { }
        }

        // ── the simple view's cards ──

        private Control SimpleCard(CompatCard c)
        {
            var card = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = NixxTheme.Card,
                                             Padding = new Padding(10, 8, 10, 8), Margin = new Padding(0, 0, 0, 6), MinimumSize = new Size(Width0, 0) };
            var head = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            var box = new CheckBox { Text = c.Title, AutoSize = true, Font = new Font("Segoe UI Semibold", 10f), UseMnemonic = false, AutoCheck = false };
            var light = new Label { AutoSize = true, Margin = new Padding(14, 5, 0, 0), UseMnemonic = false, ForeColor = NixxTheme.Dim, Text = "Checking..." };
            box.Click += (_, _) => CardClicked(c, box);
            head.Controls.Add(box);
            head.Controls.Add(light);
            card.Controls.Add(head);
            card.Controls.Add(new Label { Text = c.Line, AutoSize = true, MaximumSize = new Size(Width0 - 40, 0), Margin = new Padding(18, 0, 0, 2), ForeColor = NixxTheme.Text, UseMnemonic = false });

            var details = new Label { AutoSize = true, MaximumSize = new Size(Width0 - 40, 0), Margin = new Padding(18, 2, 0, 2), ForeColor = NixxTheme.Dim, Visible = false, UseMnemonic = false,
                                      Text = "It covers: " + string.Join("; ", c.Ids.Select(id => CompatRegistry.Find(id)?.Title ?? id)) + ". Each one is in the advanced view." };
            var more = new LinkLabel { Text = "What it covers ▸", AutoSize = true, Margin = new Padding(18, 0, 0, 0), LinkColor = NixxTheme.Link, ActiveLinkColor = NixxTheme.Link };
            more.LinkClicked += (_, _) => { details.Visible = !details.Visible; more.Text = details.Visible ? "What it covers ▾" : "What it covers ▸"; };
            card.Controls.Add(more);
            card.Controls.Add(details);
            _cards.Add((c, box, light));
            return card;
        }

        /// <summary>A card clicked: every patch of it on - or off, after its warning - from wherever it was.</summary>
        private void CardClicked(CompatCard c, CheckBox box)
        {
            bool on = box.CheckState != CheckState.Checked;
            if (!on && c.OffWarning != null
                && MessageBox.Show(FindForm(), c.OffWarning + "\n\nTurn \"" + c.Title + "\" off?", Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            Set(c.Ids, on);
        }

        /// <summary>Boxes set without their own questions (the card asked once).</summary>
        private void Set(IEnumerable<string> ids, bool on)
        {
            _loading = true;
            foreach (var id in ids) if (_boxes.TryGetValue(id, out var b)) b.Checked = on;
            _loading = false;
            RefreshCards();
        }

        /// <summary>On for the draft: its box ticked, and the box of what it needs too.</summary>
        private bool Effective(string id)
        {
            if (!_boxes.TryGetValue(id, out var b) || !b.Checked) return false;
            var need = CompatRegistry.Find(id)?.Needs;
            return need == null || !_boxes.ContainsKey(need) || Effective(need);
        }

        private bool Problem(string id, out string detail)
        {
            detail = "";
            if (!_checked.TryGetValue(id, out var r) || r.State != CompatCheckState.Missing) return false;
            detail = r.Detail;
            return true;
        }

        /// <summary>The cards, the health line, the dependencies and the safe-mode button, from the boxes and the checks.</summary>
        private void RefreshCards()
        {
            Dependencies();
            var broken = new List<string>();
            foreach (var (c, box, light) in _cards)
            {
                var ids = c.Ids.Where(_boxes.ContainsKey).ToList();
                int on = ids.Count(Effective);
                box.CheckState = on == 0 ? CheckState.Unchecked : on == ids.Count ? CheckState.Checked : CheckState.Indeterminate;
                var bad = ids.Where(Effective).FirstOrDefault(id => Problem(id, out _));
                if (on == 0) { light.Text = "○ Off"; light.ForeColor = NixxTheme.Dim; }
                else if (bad != null)
                {
                    light.Text = "● Problem found: LaunchBox changed";
                    light.ForeColor = NixxTheme.Error;
                    broken.Add(c.Title);
                }
                else if (!_checksDone) { light.Text = "Checking..."; light.ForeColor = NixxTheme.Dim; }
                else if (on < ids.Count) { light.Text = "◐ Partly on (changed in the advanced view)"; light.ForeColor = NixxTheme.Warn; }
                else { light.Text = "● Working"; light.ForeColor = Color.MediumSeaGreen; }
            }
            if (_checksDone)
            {
                var v = CompatChecks.LaunchBoxVersion();
                _health.Text = broken.Count == 0
                    ? "✔ Everything the pack relies on is in place in your LaunchBox" + (v == null ? "." : " (" + v + ").")
                    : "⚠ LaunchBox changed: " + string.Join(", ", broken.Select(t => "\"" + t + "\"")) + " can no longer work fully. Turn it off below, or update the pack.";
                _health.ForeColor = broken.Count == 0 ? Color.MediumSeaGreen : NixxTheme.Warn;
            }
            bool safe = _beforeSafe.Count > 0;
            _safe.Text = safe ? "Leave safe mode" : "Safe mode";
            _safeNote.Text = safe
                ? "Safe mode: everything that relies on LaunchBox's inner workings is off. \"Leave safe mode\" puts back the switches as they were."
                : "Safe mode turns off everything that relies on LaunchBox's inner workings, and keeps what only the pack's own code does.";
            _safeNote.ForeColor = safe ? NixxTheme.Warn : NixxTheme.Dim;
        }

        private void ToggleSafe()
        {
            if (_beforeSafe.Count == 0)
            {
                _beforeSafe = _boxes.ToDictionary(kv => kv.Key, kv => kv.Value.Checked, StringComparer.OrdinalIgnoreCase);
                Set(CompatRegistry.Internal, false);
            }
            else
            {
                var back = _beforeSafe;
                _beforeSafe = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                _loading = true;
                foreach (var kv in back) if (_boxes.TryGetValue(kv.Key, out var b)) b.Checked = kv.Value;
                _loading = false;
                RefreshCards();
            }
        }

        // ── the advanced view's cards ──

        private Control Card(CompatPatch p, Dictionary<string, bool> saved)
        {
            var card = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = NixxTheme.Card,
                                             Padding = new Padding(10, 6, 10, 6), Margin = new Padding(0, 0, 0, 6), MinimumSize = new Size(Width0, 0) };
            if (p.Switchable)
            {
                var box = new CheckBox { Text = p.Title, AutoSize = true, Checked = !saved.TryGetValue(p.Id, out var on) || on, Font = new Font("Segoe UI Semibold", 9.5f), UseMnemonic = false };
                box.CheckedChanged += (_, _) =>
                {
                    if (_loading || box.Checked || p.OffWarning == null) return;
                    var yes = MessageBox.Show(FindForm(), p.OffWarning + "\n\nTurn \"" + p.Title + "\" off?", Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
                    if (!yes) { _loading = true; box.Checked = true; _loading = false; }
                };
                _boxes[p.Id] = box;
                card.Controls.Add(box);
            }
            else card.Controls.Add(new Label { Text = p.Title + "   (always on - shown for information)", AutoSize = true, Font = new Font("Segoe UI Semibold", 9.5f), ForeColor = NixxTheme.Text, UseMnemonic = false });

            var status = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(Width0 - 20, 0), Margin = new Padding(18, 0, 0, 0) };
            if (p.Checks.Length > 0) { var l = Status("Reflection: checking..."); _checkLabels[p.Id] = l; status.Controls.Add(l); }
            if (p.BugTest != null) { var l = Status("LaunchBox bug: testing..."); _bugLabels[p.Id] = l; status.Controls.Add(l); }
            if (p.Needs != null) status.Controls.Add(Status("Needs: " + (CompatRegistry.Find(p.Needs)?.Title ?? p.Needs)));
            if (status.Controls.Count > 0) card.Controls.Add(status);

            var details = new TextBox
            {
                Multiline = true, ReadOnly = true, WordWrap = true, BorderStyle = BorderStyle.None, Visible = false, TabStop = false,
                Width = Width0 - 40, Margin = new Padding(18, 4, 0, 4), BackColor = NixxTheme.Field, ForeColor = NixxTheme.Text,
                Text = string.Join("\r\n\r\n", new[]
                {
                    "THE PROBLEM\r\n" + p.Problem,
                    "EXAMPLE\r\n" + p.Example,
                    "WHAT THE PATCH DOES\r\n" + p.Does,
                    "WITHOUT IT\r\n" + p.Without,
                    p.OwnSwitches == null ? null : "ITS SWITCHES\r\n" + p.OwnSwitches,
                }.Where(s => s != null)),
            };
            var more = new LinkLabel { Text = "Explain ▸", AutoSize = true, Margin = new Padding(18, 2, 0, 0), LinkColor = NixxTheme.Link, ActiveLinkColor = NixxTheme.Link };
            more.LinkClicked += (_, _) =>
            {
                details.Visible = !details.Visible;
                more.Text = details.Visible ? "Explain ▾" : "Explain ▸";
                if (details.Visible) Fit(details);
            };
            card.Controls.Add(more);
            card.Controls.Add(details);
            return card;
        }

        private static Label Status(string text) => new Label { Text = text, AutoSize = true, ForeColor = NixxTheme.Dim, Margin = new Padding(0, 1, 14, 1), UseMnemonic = false };

        private static void Fit(TextBox t)
        {
            var size = TextRenderer.MeasureText(t.Text, t.Font, new Size(t.Width - 6, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            t.Height = size.Height + 8;
        }

        /// <summary>A patch whose patch it needs is off: greyed.</summary>
        private void Dependencies()
        {
            foreach (var p in CompatRegistry.All.Where(p => p.Switchable && p.Needs != null))
                if (_boxes.TryGetValue(p.Id, out var box) && _boxes.TryGetValue(p.Needs!, out var need)) box.Enabled = need.Checked;
        }

        private Dictionary<string, bool> Draft()
            => _boxes.ToDictionary(kv => kv.Key, kv => kv.Value.Checked, StringComparer.OrdinalIgnoreCase);

        /// <summary>The banner: what is written differs from what this session runs with.</summary>
        private void Pending(Dictionary<string, bool> saved)
        {
            var differ = CompatRegistry.All.Where(p => p.Switchable)
                .Any(p => (!saved.TryGetValue(p.Id, out var s) || s) != Compat.Compat.On(p.Id) && (p.Needs == null || Compat.Compat.On(p.Needs)));
            _banner.Text = "Restart LaunchBox (or BigBox) for the saved changes to take effect.";
            _banner.Visible = differ;
        }

        /// <summary>The window's Apply / OK: null when saved.</summary>
        public string Save()
        {
            try
            {
                var before = Compat.Compat.Load(_path);
                var page = Compat.Compat.LoadPage(_path);
                var draft = Draft();
                bool changed = draft.Any(kv => (!before.TryGetValue(kv.Key, out var b) || b) != kv.Value) || !System.IO.File.Exists(_path)
                               || page.BeforeSafe.Count != _beforeSafe.Count;
                if (!changed) return null;
                Compat.Compat.Save(_path, draft, _view, _beforeSafe);
                Pending(Compat.Compat.Load(_path));
                _banner.Visible = true;
                return null;
            }
            catch (Exception ex) { return "The compatibility choices could not be written to " + _path + ": " + ex.Message; }
        }

        private void RunChecks()
        {
            var patches = CompatRegistry.All.ToList();
            Task.Run(() =>
            {
                foreach (var p in patches)
                {
                    string check = null, bug = null; Color checkColor = NixxTheme.Dim, bugColor = NixxTheme.Dim;
                    (CompatCheckState, string)? worst = null;
                    if (p.Checks.Length > 0)
                    {
                        var results = p.Checks.Select(CompatChecks.Check).ToList();
                        var missing = results.Where(r => r.State == CompatCheckState.Missing).ToList();
                        if (missing.Count > 0) { check = "Reflection: MISSING - " + string.Join("; ", missing.Select(r => r.Detail)); checkColor = NixxTheme.Error; worst = (CompatCheckState.Missing, string.Join("; ", missing.Select(r => r.Detail))); }
                        else if (results.All(r => r.State == CompatCheckState.NotHere)) { check = "Reflection: not loaded now - " + string.Join("; ", results.Select(r => r.Detail)); worst = (CompatCheckState.NotHere, ""); }
                        else { check = "Reflection: OK - " + string.Join("; ", results.Where(r => r.State == CompatCheckState.Ok).Select(r => r.Detail)); checkColor = Color.MediumSeaGreen; worst = (CompatCheckState.Ok, ""); }
                    }
                    if (p.BugTest != null)
                    {
                        var (state, detail) = CompatChecks.Bug(p.BugTest);
                        bug = "LaunchBox bug: " + (state == CompatBugState.Present ? "still there" : state == CompatBugState.Fixed ? "FIXED - this patch may no longer be needed" : "not testable here") + " (" + detail + ")";
                        bugColor = state == CompatBugState.Present ? NixxTheme.Dim : state == CompatBugState.Fixed ? NixxTheme.Warn : NixxTheme.Dim;
                    }
                    void Show()
                    {
                        if (worst != null) _checked[p.Id] = worst.Value;
                        if (check != null && _checkLabels.TryGetValue(p.Id, out var cl)) { cl.Text = check; cl.ForeColor = checkColor; }
                        if (bug != null && _bugLabels.TryGetValue(p.Id, out var bl)) { bl.Text = bug; bl.ForeColor = bugColor; }
                    }
                    try { if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)Show); } catch { }
                }
                try { if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)(() => { _checksDone = true; RefreshCards(); })); } catch { }
            });
        }
    }
}
