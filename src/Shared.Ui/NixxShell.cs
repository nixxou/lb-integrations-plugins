// A game's window dressed as the Nixx window (Mehdi, 05/10: "même traitement à appliquer à chacun des per game menus ... je
// veux uniformiser l'ensemble"): LiteBox's look (NixxTheme), its tabs as a page bar at the left with the page's title above
// it, its OK / Cancel / Apply as LiteBox's footer buttons.
//
//     NixxShell.Dress(this);      // at the end of the window's constructor, or before it is shown
//
// THE TABS STAY: their strip is hidden (the control told its pages fill it all - TCM_ADJUSTRECT) and a list at the left
// chooses the page, both ways - so a window's own code that reads or sets its tab, or waits for a page to be chosen, goes on
// as before. A tab added later shows in the list too. A window without tabs is only themed.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LbIntegrations.Ui
{
    internal static class NixxShell
    {
        public static void Dress(Form form)
        {
            if (form == null) return;
            bool done = false;
            void Now()
            {
                if (done) return;
                done = true;
                try { Pages(form); Footer(form); } catch { }
                NixxTheme.Apply(form);
            }
            // Its controls are all there once it loads; dressed then, before it is drawn.
            form.Load += (_, _) => Now();
            if (form.IsHandleCreated) Now();
        }

        private static int S(Control c, int px) => (int)Math.Round(px * (c.DeviceDpi / 96f));

        // ── the tabs as pages ────────────────────────────────────────────────

        private static TabControl MainTabs(Control root)
        {
            TabControl best = null;
            void Look(Control c, int depth)
            {
                if (depth > 4) return;
                foreach (Control k in c.Controls)
                {
                    if (k is TabControl t && t.TabCount > 1 && (best == null || t.Width * t.Height > best.Width * best.Height)) best = t;
                    else if (!(k is TabControl)) Look(k, depth + 1);
                }
            }
            Look(root, 0);
            return best;
        }

        private static void Pages(Form form)
        {
            var tabs = MainTabs(form);
            if (tabs == null) return;
            var parent = tabs.Parent;
            int index = parent.Controls.GetChildIndex(tabs);

            // The strip hidden: the pages fill the control.
            new StripHider(tabs);

            var wrap = new Panel { Dock = tabs.Dock, Bounds = tabs.Bounds, Anchor = tabs.Anchor, BackColor = NixxTheme.Back };
            var nav = new ListBox
            {
                Dock = DockStyle.Left, Width = S(form, 170), DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = S(form, 30), IntegralHeight = false,
                BorderStyle = BorderStyle.None, BackColor = NixxTheme.Side, ForeColor = NixxTheme.Text, Font = new Font("Segoe UI", 10f), Tag = NixxTheme.Own,
            };
            NixxTheme.NativeDark(nav);
            var right = new Panel { Dock = DockStyle.Fill, BackColor = NixxTheme.Back, Padding = new Padding(S(form, 14), S(form, 8), S(form, 10), 0) };
            var title = new Label { Dock = DockStyle.Top, Height = S(form, 32), Font = new Font("Segoe UI Semibold", 13f), ForeColor = NixxTheme.Text, BackColor = NixxTheme.Back, UseMnemonic = false, Tag = NixxTheme.Own };

            parent.SuspendLayout();
            parent.Controls.Remove(tabs);
            tabs.Dock = DockStyle.Fill;
            right.Controls.Add(tabs);
            right.Controls.Add(title);
            wrap.Controls.Add(right);
            wrap.Controls.Add(nav);
            parent.Controls.Add(wrap);
            parent.Controls.SetChildIndex(wrap, index);
            parent.ResumeLayout();

            // As wide as it was for its pages: the bar is added beside them.
            if (form.WindowState == FormWindowState.Normal) form.Width += nav.Width;

            bool busy = false;
            void Fill()
            {
                busy = true;
                nav.BeginUpdate();
                nav.Items.Clear();
                foreach (TabPage p in tabs.TabPages) nav.Items.Add(p.Text);
                nav.EndUpdate();
                nav.SelectedIndex = Math.Max(0, Math.Min(nav.Items.Count - 1, tabs.SelectedIndex));
                title.Text = tabs.SelectedTab?.Text ?? "";
                busy = false;
            }
            Fill();
            nav.SelectedIndexChanged += (_, _) => { if (!busy && nav.SelectedIndex >= 0 && nav.SelectedIndex < tabs.TabCount) tabs.SelectedIndex = nav.SelectedIndex; };
            tabs.SelectedIndexChanged += (_, _) =>
            {
                title.Text = tabs.SelectedTab?.Text ?? "";
                if (nav.SelectedIndex != tabs.SelectedIndex && tabs.SelectedIndex < nav.Items.Count) { busy = true; nav.SelectedIndex = tabs.SelectedIndex; busy = false; }
            };
            tabs.ControlAdded += (_, _) => Fill();
            tabs.ControlRemoved += (_, _) => nav.BeginInvoke(new Action(Fill));
            nav.DrawItem += (_, e) =>
            {
                if (e.Index < 0) return;
                bool on = (e.State & DrawItemState.Selected) != 0;
                using (var b = new SolidBrush(on ? NixxTheme.Accent : NixxTheme.Side)) e.Graphics.FillRectangle(b, e.Bounds);
                var r = new Rectangle(e.Bounds.X + S(form, 12), e.Bounds.Y, e.Bounds.Width - S(form, 16), e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, nav.Items[e.Index].ToString(), nav.Font, r, on ? Color.White : NixxTheme.Text,
                                      TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
        }

        /// <summary>A tab control's strip taken away: told, when it asks, that its pages take all of it.</summary>
        private sealed class StripHider : NativeWindow
        {
            private const int TCM_ADJUSTRECT = 0x1328;
            public StripHider(TabControl tabs)
            {
                if (tabs.IsHandleCreated) AssignHandle(tabs.Handle);
                tabs.HandleCreated += (_, _) => AssignHandle(tabs.Handle);
                tabs.HandleDestroyed += (_, _) => ReleaseHandle();
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == TCM_ADJUSTRECT) { m.Result = (IntPtr)1; return; }
                base.WndProc(ref m);
            }
        }

        // ── LiteBox's footer ─────────────────────────────────────────────────

        private static void Footer(Form form)
        {
            var buttons = new List<Button>();
            void Find(Control c) { foreach (Control k in c.Controls) { if (k is Button b) buttons.Add(b); else Find(k); } }
            Find(form);
            Button ok = form.AcceptButton as Button ?? buttons.FirstOrDefault(b => b.DialogResult == DialogResult.OK || b.Text == "OK");
            Button cancel = form.CancelButton as Button ?? buttons.FirstOrDefault(b => b.DialogResult == DialogResult.Cancel || b.Text == "Cancel");
            Button apply = buttons.FirstOrDefault(b => b.Text == "Apply");
            void Style(Button b, Color back)
            {
                if (b == null) return;
                b.Tag = NixxTheme.Own;
                b.FlatStyle = FlatStyle.Flat;
                b.UseVisualStyleBackColor = false;
                b.BackColor = back;
                b.ForeColor = Color.White;
                b.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                b.FlatAppearance.BorderSize = 0;
                b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.15f);
                if (b.Height < S(form, 28)) b.Height = S(form, 28);
            }
            Style(ok, NixxTheme.Ok);
            Style(cancel, NixxTheme.ButtonBack);
            Style(apply, NixxTheme.Accent);
            // LiteBox's order, OK last: Cancel, Apply, OK from the left.
            if (ok != null && cancel != null && ok.Parent == cancel.Parent)
            {
                if (ok.Parent is FlowLayoutPanel flow)
                {
                    bool rtl = flow.FlowDirection == FlowDirection.RightToLeft;
                    var order = new[] { cancel, apply, ok }.Where(b => b != null && b.Parent == flow).ToList();
                    if (rtl) order.Reverse();
                    int at = order.Min(b => flow.Controls.GetChildIndex(b));
                    foreach (var b in order) flow.Controls.SetChildIndex(b, at++);
                }
                else if (ok.Left < cancel.Left && ok.Top == cancel.Top && ok.Anchor == cancel.Anchor)
                {
                    // Placed by hand, maybe again on each layout: the two swapped there too.
                    void Swap() { if (ok.Left < cancel.Left) { var x = ok.Left; ok.Left = cancel.Left + cancel.Width - ok.Width; cancel.Left = x; } }
                    Swap();
                    ok.Parent.Layout += (_, _) => Swap();
                }
            }
            // Their bar in the page bar's colour, as LiteBox's footer.
            var bar = (ok ?? cancel)?.Parent;
            if (bar != null && !(bar is Form) && bar.Dock == DockStyle.Bottom) { bar.BackColor = NixxTheme.Side; bar.Tag = NixxTheme.Own; }
        }
    }
}
