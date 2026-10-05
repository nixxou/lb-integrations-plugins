// --nixx-shots <out dir> <plugin.dll>...: the Nixx window as LaunchBox opens it - the menu relay (the probe's dll) and the plugins
// given loaded beside it - each of its pages chosen in turn and drawn off screen into a PNG. Closed without OK: nothing saved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows.Forms;

namespace LbIntegrations.Probe
{
    internal static class NixxShots
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        public static bool Run(Assembly menus, string outDir, IEnumerable<string> plugins)
        {
            Directory.CreateDirectory(outDir);
            foreach (var dll in plugins)
            {
                var dir = Path.GetDirectoryName(dll);
                AssemblyLoadContext.Default.Resolving += (ctx, name) => File.Exists(Path.Combine(dir, name.Name + ".dll")) ? ctx.LoadFromAssemblyPath(Path.Combine(dir, name.Name + ".dll")) : null;
                try { AssemblyLoadContext.Default.LoadFromAssemblyPath(dll); Console.WriteLine("  loaded " + Path.GetFileName(dll)); }
                catch (Exception ex) { Console.WriteLine("  not loaded " + dll + ": " + ex.Message); }
            }
            const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            var providers = menus.GetType("LbIntegrations.Menus.SettingsProviders", true).GetMethod("All", Any).Invoke(null, null);
            var form = (Form)Activator.CreateInstance(menus.GetType("LbIntegrations.Menus.NixxSettingsForm", true), Any, null, new[] { providers }, null);
            var nav = (ListBox)form.GetType().GetField("_nav", Any).GetValue(form);
            var entries = (System.Collections.IList)form.GetType().GetField("_entries", Any).GetValue(form);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new System.Drawing.Point(-4000, 0);
            form.Show();
            void Pump(int n) { for (int i = 0; i < n; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(60); } }
            Pump(10);
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var title = (string)e.GetType().GetField("Title").GetValue(e);
                if (e.GetType().GetField("View").GetValue(e) == null) continue;
                nav.SelectedIndex = i;
                Pump(12);
                using var bmp = new System.Drawing.Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                var name = new string(title.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
                bmp.Save(Path.Combine(outDir, i.ToString("00") + "-" + name + ".png"));
                // Every text shown on it longer than 100 characters - what is still to shorten (LBIP_LONG_TEXTS=1).
                if (Environment.GetEnvironmentVariable("LBIP_LONG_TEXTS") == "1")
                {
                    void Walk(Control c)
                    {
                        if ((c is Label || c is ButtonBase) && c.Visible && (c.Text ?? "").Length > 100) Console.WriteLine("      [" + c.Text.Length + "] " + c.Text.Replace("\n", " ").Substring(0, 100));
                        foreach (Control k in c.Controls) Walk(k);
                    }
                    Walk((Control)e.GetType().GetField("View").GetValue(e));
                }
                Console.WriteLine("  " + i.ToString("00") + " " + title);
            }
            // The wheel over a closed list (LBIP_WHEEL_TEST=1): its value kept, the page scrolled.
            bool ok = true;
            if (Environment.GetEnvironmentVariable("LBIP_WHEEL_TEST") == "1")
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    var view = (Control)e.GetType().GetField("View").GetValue(e);
                    if (view == null) continue;
                    nav.SelectedIndex = i;
                    Pump(6);
                    ComboBox First(Control c) { if (c is ComboBox b && b.Visible && b.Items.Count > 1) return b; foreach (Control k in c.Controls) if (First(k) is ComboBox f) return f; return null; }
                    var combo = First(view);
                    if (combo == null) continue;
                    ScrollableControl page = null;
                    for (var p = combo.Parent; p != null; p = p.Parent) if (p is ScrollableControl s && s.AutoScroll && s.VerticalScroll.Visible) { page = s; break; }
                    if (page == null) continue;
                    int before = combo.SelectedIndex, y0 = -page.AutoScrollPosition.Y;
                    var at = combo.PointToScreen(new System.Drawing.Point(combo.Width / 2, combo.Height / 2));
                    SendMessage(combo.Handle, 0x020A, (IntPtr)((-120) << 16), (IntPtr)((at.Y << 16) | (at.X & 0xFFFF)));
                    Pump(4);
                    int y1 = -page.AutoScrollPosition.Y;
                    var title = (string)e.GetType().GetField("Title").GetValue(e);
                    bool good = combo.SelectedIndex == before && y1 > y0;
                    ok &= good;
                    Console.WriteLine("  wheel on " + title + ": value " + (combo.SelectedIndex == before ? "kept" : "CHANGED " + before + " -> " + combo.SelectedIndex) + ", page " + y0 + " -> " + y1 + (good ? "  ok" : "  FAIL"));
                }
            }
            form.Close();
            return ok;
        }
    }
}
