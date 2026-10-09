// The "Compatibility" page of the Nixx settings (Mehdi, 09/10 - docs\compat-page-plan.md): every patch of the pack to
// LaunchBox (Shared.Compat\CompatRegistry), grouped, each with its checkbox (on by default), what it relies on in LaunchBox
// (reflection checks) and whether the bug is still there (bug tests), and a text that unfolds: the problem, an example,
// what the patch does, what happens without it.
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
        private readonly Label _banner;
        private bool _loading = true;

        private const int Width0 = 660;

        public CompatPanel()
        {
            Dock = DockStyle.Fill;
            AutoScroll = true;
            Padding = new Padding(12);
            _path = Compat.Compat.PathOf(null);
            var saved = Compat.Compat.Load(_path);

            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top };
            Label Text(string t, Color? color = null, bool bold = false, int top = 0) => new Label
            {
                Text = t, AutoSize = true, MaximumSize = new Size(Width0, 0), Margin = new Padding(0, top, 0, 6),
                ForeColor = color ?? NixxTheme.Dim, Font = bold ? new Font("Segoe UI Semibold", 10f) : null, UseMnemonic = false,
            };

            stack.Controls.Add(Text("Where the pack works around LaunchBox: what LaunchBox does not do, its bugs routed around, the "
                                  + "safeguards, what the pack changes in your LaunchBox data and where it hooks in. Several rely on "
                                  + "LaunchBox's inner workings, which an update can change - and the day LaunchBox fixes a bug, its "
                                  + "patch can go. Each one can be turned off here; all are on by default.", NixxTheme.Text));
            _banner = Text("", NixxTheme.Warn, bold: true);
            _banner.Visible = false;
            stack.Controls.Add(_banner);
            stack.Controls.Add(Text("Changes apply when LaunchBox (or BigBox) is restarted: the plugins read these choices when they start.", top: 0));

            foreach (var group in CompatRegistry.All.GroupBy(p => p.Group))
            {
                stack.Controls.Add(Text(CompatRegistry.GroupTitle(group.Key), NixxTheme.Caption, bold: true, top: 10));
                foreach (var p in group) stack.Controls.Add(Card(p, saved));
            }
            Controls.Add(stack);

            foreach (var b in _boxes.Values) b.CheckedChanged += (_, _) => { if (!_loading) Dependencies(); };
            _loading = false;
            Dependencies();
            Pending(saved);
            HandleCreated += (_, _) => RunChecks();
        }

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
                var draft = Draft();
                bool changed = draft.Any(kv => (!before.TryGetValue(kv.Key, out var b) || b) != kv.Value) || !System.IO.File.Exists(_path);
                if (!changed) return null;
                Compat.Compat.Save(_path, draft);
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
                    if (p.Checks.Length > 0)
                    {
                        var results = p.Checks.Select(CompatChecks.Check).ToList();
                        var missing = results.Where(r => r.State == CompatCheckState.Missing).ToList();
                        if (missing.Count > 0) { check = "Reflection: MISSING - " + string.Join("; ", missing.Select(r => r.Detail)); checkColor = NixxTheme.Error; }
                        else if (results.All(r => r.State == CompatCheckState.NotHere)) check = "Reflection: not loaded now - " + string.Join("; ", results.Select(r => r.Detail));
                        else { check = "Reflection: OK - " + string.Join("; ", results.Where(r => r.State == CompatCheckState.Ok).Select(r => r.Detail)); checkColor = Color.MediumSeaGreen; }
                    }
                    if (p.BugTest != null)
                    {
                        var (state, detail) = CompatChecks.Bug(p.BugTest);
                        bug = "LaunchBox bug: " + (state == CompatBugState.Present ? "still there" : state == CompatBugState.Fixed ? "FIXED - this patch may no longer be needed" : "not testable here") + " (" + detail + ")";
                        bugColor = state == CompatBugState.Present ? NixxTheme.Dim : state == CompatBugState.Fixed ? NixxTheme.Warn : NixxTheme.Dim;
                    }
                    void Show()
                    {
                        if (check != null && _checkLabels.TryGetValue(p.Id, out var cl)) { cl.Text = check; cl.ForeColor = checkColor; }
                        if (bug != null && _bugLabels.TryGetValue(p.Id, out var bl)) { bl.Text = bug; bl.ForeColor = bugColor; }
                    }
                    try { if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)Show); } catch { }
                }
            });
        }
    }
}
