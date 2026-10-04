// What the other options windows' pictures share (CheckReset). The PPSSPP plugin's per-game settings and its options window are gone (04/10): their checks with them.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LbIntegrations.Probe
{
    internal static class PpssppCheck
    {
        private static Assembly _asm;
        private static int _bad;

        private static Type T(string name) => _asm.GetType("LbIntegrations.Ppsspp." + name, throwOnError: true);

        private static object Call(string type, string method, params object[] args)
        {
            // The optional parameters left out are given their defaults (and out values go back to the caller).
            var m = T(type).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                           .First(x => x.Name == method && x.GetParameters().Length >= args.Length && x.GetParameters().Skip(args.Length).All(p => p.IsOptional));
            var all = args.Concat(m.GetParameters().Skip(args.Length).Select(p => p.DefaultValue)).ToArray();
            var result = m.Invoke(null, all);
            Array.Copy(all, args, args.Length);
            return result;
        }

        private static bool Check(string what, bool ok, string detail = null)
        {
            Console.WriteLine("    " + (ok ? "ok   " : "FAIL ") + what + (!ok && detail != null ? "\n          got: " + detail : ""));
            if (!ok) _bad++;
            return ok;
        }

        /// the tabs still set (their Advanced text). Said on the console; true when nothing is left.</summary>
        internal static bool CheckReset(System.Windows.Forms.Form form, string advancedField)
        {
            IEnumerable<System.Windows.Forms.Control> All(System.Windows.Forms.Control c) => new[] { c }.Concat(c.Controls.Cast<System.Windows.Forms.Control>().SelectMany(All));
            int Bars() => All(form).Count(c => c is System.Windows.Forms.Panel && c.Width == 4 && c.Parent is not System.Windows.Forms.FlowLayoutPanel && c.Visible
                                && (c.BackColor.ToArgb() == System.Drawing.Color.FromArgb(0, 120, 215).ToArgb() || c.BackColor.ToArgb() == System.Drawing.Color.FromArgb(205, 45, 45).ToArgb()));   // ours, blue or red: amber is the game's own config, which a reset leaves
            var tabs = form.Controls.OfType<System.Windows.Forms.TabControl>().First();
            int before = 0;
            foreach (System.Windows.Forms.TabPage page in tabs.TabPages) { tabs.SelectedTab = page; System.Windows.Forms.Application.DoEvents(); before += Bars(); }
            form.GetType().GetMethod("ResetDefaults", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
            int after = 0;
            foreach (System.Windows.Forms.TabPage page in tabs.TabPages) { tabs.SelectedTab = page; System.Windows.Forms.Application.DoEvents(); after += Bars(); }
            var text = ((System.Windows.Forms.TextBox)form.GetType().GetField(advancedField, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form)).Text;
            bool empty = text.Replace("<config>", "").Replace("</config>", "").Replace("<config />", "").Trim().Length == 0;
            Console.WriteLine("  reset: " + before + " bar(s) before, " + after + " after; what the tabs set after: " + (empty ? "nothing" : text.Replace("\r\n", " | ")));
            Console.WriteLine("    " + (after == 0 && empty ? "ok   " : "FAIL ") + "Reset to defaults: every bar of ours gone, nothing set");
            return after == 0 && empty;
        }

        /// <summary>The game config a launch would write for a disc id and one setting, on a real install. Read
    }
}
