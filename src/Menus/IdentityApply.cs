// "Apply to my emulators" (Mehdi, 03/10): the identity of the "Your console" tab written into the emulators already set up -
// what a first install does for a new one, done again over what each holds. Each plugin says what it would change through a
// type of its own, found by its name like the tab's Settings:
//     LbIntegrations.<assembly>.IdentityTarget
//         string[][] Plan()          rows of { key, what, now, would be, why not ("" when it can) }
//         string Apply(string key)   null when written, else why not
// The window lists every row with a box: ticked when something would change and nothing stands in the way, greyed with the
// reason when something does (the emulator running, nothing to write). Only the ticked rows are written.

#nullable disable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace LbIntegrations.Menus
{
    internal static class IdentityApply
    {
        private sealed class Target
        {
            public string Plugin, Key, Label, Now, Next, Problem;
            public MethodInfo Apply;
        }

        private static List<Target> Targets()
        {
            var list = new List<Target>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (asm.IsDynamic) continue;
                    var name = asm.GetName().Name;
                    var type = string.IsNullOrEmpty(name) ? null : asm.GetType("LbIntegrations." + name + ".IdentityTarget", throwOnError: false);
                    if (type == null) continue;
                    var flags = BindingFlags.Public | BindingFlags.Static;
                    var plan = type.GetMethod("Plan", flags, null, Type.EmptyTypes, null);
                    var apply = type.GetMethod("Apply", flags, null, new[] { typeof(string) }, null);
                    if (plan?.ReturnType != typeof(string[][]) || apply?.ReturnType != typeof(string)) { RelayLog.Warn(type.FullName + " is there but not as expected"); continue; }
                    foreach (var r in (string[][])plan.Invoke(null, null) ?? new string[0][])
                        if (r != null && r.Length >= 5) list.Add(new Target { Plugin = name, Key = r[0], Label = r[1], Now = r[2], Next = r[3], Problem = r[4], Apply = apply });
                }
                catch (Exception ex) { RelayLog.Warn("asking " + asm.GetName().Name + " what your console would change", ex); }
            }
            return list.OrderBy(t => t.Label, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The window. The identity must be saved first: each plugin reads it from identity.ini.</summary>
        public static void Show(IWin32Window owner)
        {
            var targets = Targets();
            using var form = new Form
            {
                Text = "Your console - apply to my emulators", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.Sizable,
                MinimizeBox = false, ShowInTaskbar = false, Font = new Font("Segoe UI", 9f), ClientSize = new Size(900, 420), MinimumSize = new Size(600, 300),
            };
            var intro = new Label
            {
                Dock = DockStyle.Top, Height = 46, Padding = new Padding(10, 8, 10, 0), ForeColor = SystemColors.GrayText,
                Text = "What your console would change in the emulators already set up. Ticked: something changes and it can be written now. "
                     + "The DSi consoles already made are never rewritten. Only the ticked rows are written.",
            };
            var list = new ListView { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
            list.Columns.Add("Emulator", 230);
            list.Columns.Add("Now", 260);
            list.Columns.Add("Will be", 260);
            list.Columns.Add("Note", 200);
            foreach (var t in targets)
            {
                bool same = t.Now == t.Next;
                var item = new ListViewItem(new[] { t.Label, t.Now, t.Next, t.Problem.Length > 0 ? t.Problem : same ? "already so" : "" }) { Tag = t };
                item.Checked = t.Problem.Length == 0 && !same;
                if (t.Problem.Length > 0) item.ForeColor = SystemColors.GrayText;
                list.Items.Add(item);
            }
            list.ItemCheck += (_, e) => { if (((Target)list.Items[e.Index].Tag).Problem.Length > 0) e.NewValue = CheckState.Unchecked; };
            if (targets.Count == 0)
                list.Items.Add(new ListViewItem(new[] { "No emulator of the pack is set up yet", "", "", "" }) { ForeColor = SystemColors.GrayText, Tag = null });

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(8) };
            var close = new Button { Text = "Close", Width = 90, DialogResult = DialogResult.Cancel };
            var apply = new Button { Text = "Apply the ticked", Width = 130 };
            apply.Click += (_, _) =>
            {
                var said = new List<string>();
                foreach (ListViewItem item in list.Items)
                {
                    if (!item.Checked || item.Tag is not Target t) continue;
                    string error;
                    try { error = (string)t.Apply.Invoke(null, new object[] { t.Key }); }
                    catch (Exception ex) { error = (ex.InnerException ?? ex).Message; }
                    said.Add((error == null ? "✔ " : "✖ ") + t.Label + (error == null ? "" : " - " + error));
                }
                if (said.Count == 0) return;
                MessageBox.Show(form, string.Join(Environment.NewLine, said), form.Text, MessageBoxButtons.OK,
                                said.Any(s => s.StartsWith("✖")) ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                form.DialogResult = DialogResult.OK;
            };
            bottom.Controls.Add(close);
            bottom.Controls.Add(apply);
            form.Controls.Add(list);
            form.Controls.Add(intro);
            form.Controls.Add(bottom);
            form.CancelButton = close;
            form.ShowDialog(owner);
        }
    }
}
