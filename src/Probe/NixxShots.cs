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
                Console.WriteLine("  " + i.ToString("00") + " " + title);
            }
            form.Close();
            return true;
        }
    }
}
