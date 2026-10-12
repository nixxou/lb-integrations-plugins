// The window of a restore that could go to several installs of one emulator and nothing says which (SaveRouter
// .RestoreEmulator, docs\saves-two-installs.md §4c): the user picks the folder it goes to (Mehdi 12/10 - a combobox of
// the full destination folders, the game's own emulator preselected). LaunchBox only; the caller refuses elsewhere.
// Asked on the caller's thread, LaunchBox's UI thread under a restore (as SlotRestore's question).

#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace LbIntegrations.Saves
{
    internal static class InstallChoice
    {
        /// <summary>For the probe: answers instead of the window (the choices, the preselected one → the index chosen, null
        /// for Cancel).</summary>
        internal static Func<IReadOnlyList<(string Label, string Folder)>, int, int?>? Instead;

        /// <summary>The index of the install chosen among <paramref name="choices"/>, or null when cancelled or when the
        /// window cannot be shown.</summary>
        public static int? Ask(string fileName, IReadOnlyList<(string Label, string Folder)> choices, int preselected)
        {
            try
            {
                if (choices == null || choices.Count == 0) return null;
                if (Instead != null) return Instead(choices, preselected);
                return Window(fileName, choices, preselected);
            }
            catch { return null; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int? Window(string fileName, IReadOnlyList<(string Label, string Folder)> choices, int preselected)
        {
            using var form = new Form
            {
                Text = "Restore",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12),
                Font = SystemFonts.MessageBoxFont,
            };
            var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
            var text = new Label
            {
                AutoSize = true, MaximumSize = new Size(640, 0),
                Text = (string.IsNullOrEmpty(fileName) ? "This save" : fileName) + " can go to several installs of this emulator, "
                       + "and nothing says which one it came from.\n\nWhere should it be restored?",
            };
            var list = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 640, Margin = new Padding(0, 10, 0, 10) };
            foreach (var c in choices) list.Items.Add(c.Folder + "   (" + c.Label + ")");
            list.SelectedIndex = Math.Min(Math.Max(0, preselected), choices.Count - 1);
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var ok = new Button { Text = "Restore here", DialogResult = DialogResult.OK, AutoSize = true };
            buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
            layout.Controls.Add(text); layout.Controls.Add(list); layout.Controls.Add(buttons);
            form.Controls.Add(layout);
            form.AcceptButton = ok; form.CancelButton = cancel;
            // LaunchBox's window is WPF: the owner is the window in front (Game Saves), so the box is modal to it.
            IWin32Window? owner = Form.ActiveForm;
            if (owner == null) { var h = GetForegroundWindow(); if (h != IntPtr.Zero) owner = new Hwnd(h); }
            var r = owner != null ? form.ShowDialog(owner) : form.ShowDialog();
            return r == DialogResult.OK && list.SelectedIndex >= 0 ? list.SelectedIndex : (int?)null;
        }

        private sealed class Hwnd : IWin32Window { public Hwnd(IntPtr h) { Handle = h; } public IntPtr Handle { get; } }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }
}
