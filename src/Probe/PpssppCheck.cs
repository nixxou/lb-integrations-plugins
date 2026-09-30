// The PPSSPP plugin's per-game settings, on a FORGED install in the temp folder: nothing real is touched.
//
//   --ppsspp-settings              the engine: kept, laid over the game's config for a session, put back
//   --ppsspp-options-shot <png>    the options window, one picture per tab

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
            var m = T(type).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                           .First(x => x.Name == method && x.GetParameters().Length == args.Length);
            return m.Invoke(null, args);
        }

        private static bool Check(string what, bool ok, string detail = null)
        {
            Console.WriteLine("    " + (ok ? "ok   " : "FAIL ") + what + (!ok && detail != null ? "\n          got: " + detail : ""));
            if (!ok) _bad++;
            return ok;
        }

        public static bool Settings(Assembly pluginAssembly)
        {
            _asm = pluginAssembly;
            _bad = 0;
            Console.WriteLine();
            Console.WriteLine("-- PPSSPP, a game's own settings, on a FORGED install  [WRITES, in the temp folder] --");
            var root = Path.Combine(Path.GetTempPath(), "lbip-ppsspp-" + Guid.NewGuid().ToString("N"));
            try
            {
                var install = Path.Combine(root, "Nixx-PPSSPP");
                var system = Path.Combine(install, "memstick", "PSP", "SYSTEM");
                Directory.CreateDirectory(system);
                var exe = Path.Combine(install, "PPSSPPWindows64.exe");
                File.WriteAllText(exe, "not really an executable");

                // A PPSSPP never started: no ppsspp.ini yet - "default" is then PPSSPP's own.
                var fresh = (Dictionary<string, string>)Call("PpssppGameSettings", "DefaultsOf", Call("PpssppPaths", "Resolve", exe), "ULUS10064");
                Check("never started: PPSSPP's own defaults - Vulkan, Auto resolution, VSync on, anisotropic 16x (its 4), every setting known",
                      fresh["cli/graphics"] == "vulkan" && fresh["Graphics/InternalResolution"] == "0" && fresh["Graphics/VerticalSync"] == "True"
                      && fresh["Graphics/AnisotropyLevel"] == "4" && fresh.Count == 21, string.Join(", ", fresh.Select(kv => kv.Key + "=" + kv.Value)));
                File.WriteAllText(Path.Combine(system, "ppsspp.ini"), "[Graphics]\r\nGraphicsBackend = 2 (D3D11)\r\nInternalResolution = 2\r\nVerticalSync = True\r\nFrameSkip = 0\r\n[CPU]\r\nCPUSpeed = 0\r\n");
                File.WriteAllText(Path.Combine(system, "controls.ini"), "[ControlMapping]\r\nUp = 1-19\r\nSave State = 1-131\r\n");
                var layout = Call("PpssppPaths", "Resolve", exe);
                Console.WriteLine("  memstick " + Path.Combine(install, "memstick"));
                const string id = "ULUS10064";
                var ini = Path.Combine(system, id + "_ppsspp.ini");
                var bak = ini + ".lbip-bak";

                var own = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Graphics/InternalResolution"] = "3", ["Graphics/VerticalSync"] = "False", ["cli/graphics"] = "vulkan",
                };
                Call("PpssppGameSettings", "Save", layout, "g1", own);
                var loaded = (Dictionary<string, string>)Call("PpssppGameSettings", "Load", layout, "g1");
                Check("kept, and read back", loaded != null && loaded.Count == 3 && loaded["Graphics/InternalResolution"] == "3");

                // 1. A game with no config of its own.
                var keysArgs = new object[] { layout, "g1", null, null };
                var keys = Call("PpssppGameSettings", "KeysOf", keysArgs);
                Check("the renderer is kept for the command line, not the ini", (string)keysArgs[2] == "vulkan");
                var pv = new object[] { layout, id, keys, null, null };
                var preview = (string)Call("PpssppGameSettings", "Preview", pv);
                Check("the preview writes nothing", !File.Exists(ini) && !File.Exists(bak), (string)pv[4]);
                Check("its session applied", (bool)Call("PpssppGameSettings", "Apply", layout, "g1", id));
                var text = File.Exists(ini) ? File.ReadAllText(ini) : "";
                Console.WriteLine("            " + text.Replace("\r\n", " | "));
                Check("an EMPTY set-aside file: there was none", File.Exists(bak) && new FileInfo(bak).Length == 0);
                Check("ONLY the game's keys, in their sections", text.Contains("InternalResolution = 3") && text.Contains("VerticalSync = False") && !text.Contains("FrameSkip") && !text.Contains("graphics"));
                Check("controls.ini's mapping copied in - no [ControlMapping] would mean PPSSPP's built-in one", text.Contains("[ControlMapping]") && text.Contains("Save State = 1-131"));
                Check("the preview is exactly what the launch wrote", preview == text, preview);
                Call("PpssppGameSettings", "Restore", layout, "the session is over");
                Check("once over: no game config again, nothing set aside", !File.Exists(ini) && !File.Exists(bak));

                // 2. A game config of the user's.
                var mine = "; Game config for ULUS10064 - A Game\r\n[Graphics]\r\nFrameSkip = 2\r\nInternalResolution = 1\r\n[ControlMapping]\r\nUp = 10-19\r\n";
                File.WriteAllText(ini, mine);
                var defaults = (Dictionary<string, string>)Call("PpssppGameSettings", "DefaultsOf", layout, id);
                Check("'default' is the game's own config over ppsspp.ini: 1x, frame skip 2, VSync from ppsspp.ini",
                      defaults["Graphics/InternalResolution"] == "1" && defaults["Graphics/FrameSkip"] == "2" && defaults["Graphics/VerticalSync"] == "True");
                Check("the renderer's default is ppsspp.ini's GraphicsBackend (D3D11), a key it lacks PPSSPP's own (fast memory on)",
                      defaults["cli/graphics"] == "d3d11" && defaults["CPU/FastMemoryAccess"] == "True");
                Call("PpssppGameSettings", "Apply", layout, "g1", id);
                text = File.ReadAllText(ini);
                Check("set aside as it was", File.ReadAllText(bak) == mine);
                Check("ours over the user's (3x), the user's kept (frame skip 2)", text.Contains("InternalResolution = 3") && !text.Contains("InternalResolution = 1") && text.Contains("FrameSkip = 2"));
                Check("the user's own mapping kept, not controls.ini's", text.Contains("Up = 10-19") && !text.Contains("Save State = 1-131"));
                Call("PpssppGameSettings", "Restore", layout, "left behind by a session that did not end");
                Check("once over: the user's file byte for byte", File.ReadAllText(ini) == mine && !File.Exists(bak));

                // 3. Set by hand.
                Call("PpssppGameSettings", "SaveAdvanced", layout, "g1", "[Graphics]\r\nInternalResolution = 5\r\n[Achievements]\r\nAchievementsEnable = False\r\n", true);
                Call("PpssppGameSettings", "Apply", layout, "g1", id);
                text = File.ReadAllText(ini);
                Check("set by hand, in use: its keys, not the tabs'", text.Contains("InternalResolution = 5") && !text.Contains("VerticalSync = False"));
                Check("[Achievements] left out - this plugin keeps it in ppsspp.ini", !text.Contains("Achievements"));
                Call("PpssppGameSettings", "Restore", layout, "the session is over");

                // The renderer, on the command line.
                Check("--graphics= in front of the line", (string)Call("PpssppGameSettings", "WithBackend", "--fullscreen \"C:\\g.iso\"", "vulkan") == "--graphics=vulkan --fullscreen \"C:\\g.iso\"");
                Check("not when the line names one already", Call("PpssppGameSettings", "WithBackend", "--graphics=opengl \"C:\\g.iso\"", "vulkan") == null);

                // The checks.
                var a = new object[] { layout, "[Graphics]\r\nVerticalSync = yes\r\nInternalResolution = 42\r\nNoSuchKey = 1\r\n[ControlMapping]\r\nUp = 1-19", null };
                var w = (List<string>)Call("PpssppGameSettings", "CheckHand", a);
                Console.WriteLine("            " + string.Join(" | ", w));
                Check("said: a bool not True/False, a choice out of range, an unknown key, a section kept elsewhere",
                      a[2] == null && w.Any(x => x.Contains("VerticalSync")) && w.Any(x => x.Contains("InternalResolution")) && w.Any(x => x.Contains("NoSuchKey")) && w.Any(x => x.Contains("ControlMapping")));
                var b = new object[] { layout, "InternalResolution = 3", null };
                Call("PpssppGameSettings", "CheckHand", b);
                Check("a key before any [Section]: refused", b[2] is string e && e.StartsWith("line 1"), b[2] as string);
            }
            catch (Exception ex) { Console.WriteLine("  EXCEPTION: " + ex); _bad++; }
            finally { try { Directory.Delete(root, true); } catch { } }
            Console.WriteLine();
            Console.WriteLine(_bad == 0 ? "  OK - a game's settings are laid over its config for a session, and taken back" : "  " + _bad + " FAILURE(S) - see above");
            return _bad == 0;
        }

        /// <summary>The window's "Reset to defaults", pressed: counts the bars shown before and after, and what
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
        /// only - the plugin's own Preview, what the options window's "Preview result" shows.</summary>
        public static bool PreviewReal(Assembly pluginAssembly, string exe, string discId, string set)
        {
            _asm = pluginAssembly;
            if (exe == null || discId == null || set == null || set.IndexOf('=') < 0) { Console.WriteLine("  --emu <exe> --disc <id> --set Section/Key=value"); return false; }
            var layout = Call("PpssppPaths", "Resolve", exe);
            var raws = Call("PpssppGameSettings", "ToRaw", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [set.Substring(0, set.IndexOf('='))] = set.Substring(set.IndexOf('=') + 1) });
            var a = new object[] { layout, discId, raws, null, null };
            var result = (string)Call("PpssppGameSettings", "Preview", a);
            Console.WriteLine(result ?? ("  no preview: " + a[4]));
            return result != null;
        }

        /// <summary>The options window, fed a fake game, one picture per tab. Nothing written.</summary>
        public static bool OptionsShot(Assembly pluginAssembly, string outPath)
        {
            _asm = pluginAssembly;
            System.Windows.Forms.Application.EnableVisualStyles();
            var formType = T("PpssppOptionsForm");
            var entryType = formType.GetNestedType("Entry", BindingFlags.NonPublic | BindingFlags.Public);
            var e = Activator.CreateInstance(entryType);
            void Set(string f, object v) => entryType.GetField(f).SetValue(e, v);
            Set("Title", "Monster Hunter Freedom Unite"); Set("GameId", "probe"); Set("DiscId", "ULUS10391");
            var defaults = (Dictionary<string, string>)Call("PpssppGameSettings", "DefaultsOf", null, "ULUS10391");   // no ppsspp.ini: PPSSPP's own
            defaults["Graphics/FrameSkip"] = "2";   // as if its own game config set it: amber
            Set("Defaults", defaults);
            Set("GameConfig", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Graphics/FrameSkip" });
            Set("Own", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Graphics/InternalResolution"] = "4", ["cli/graphics"] = "vulkan" });
            var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(entryType));
            list.Add(e);
            using var form = (System.Windows.Forms.Form)Activator.CreateInstance(formType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { list }, null);
            form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            form.Location = new System.Drawing.Point(-3000, -3000);
            form.Show();
            var tabs = form.Controls.OfType<System.Windows.Forms.TabControl>().First();
            var shots = new List<System.Drawing.Bitmap>();
            foreach (System.Windows.Forms.TabPage page in tabs.TabPages)
            {
                tabs.SelectedTab = page;
                System.Windows.Forms.Application.DoEvents();
                var bmp = new System.Drawing.Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height));
                shots.Add(bmp);
            }
            // LBIP_SHOT_RESET=1: "Reset to defaults" pressed once the pictures are taken - see PpssppCheck.CheckReset.
            if (Environment.GetEnvironmentVariable("LBIP_SHOT_RESET") == "1") PpssppCheck.CheckReset(form, "_handText");
            form.Close();
            using var all = new System.Drawing.Bitmap(shots.Count * shots[0].Width, shots[0].Height);
            using (var g = System.Drawing.Graphics.FromImage(all))
            {
                int x = 0;
                foreach (var s in shots) { g.DrawImage(s, x, 0); x += s.Width; s.Dispose(); }
            }
            all.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine("  " + outPath);
            return true;
        }
    }
}
