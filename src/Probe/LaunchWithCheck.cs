// --launchwith-level <files...> --emu <exe> [--platform <name>]: what any plugin's LaunchWith.Level answers for these files
// (the verdict Nixx-Menus shows in "Launch With"), twice each - the second time from its cache, its time said.

using System;
using System.IO;
using System.Reflection;

namespace LbIntegrations.Probe
{
    internal static class LaunchWithCheck
    {
        public static bool Run(Assembly asm, string exe, string platform, string[] roms)
        {
            var type = asm.GetType("LbIntegrations." + asm.GetName().Name + ".LaunchWith");
            var m = type?.GetMethod("Level", BindingFlags.Public | BindingFlags.Static);
            if (m == null) { Console.WriteLine("  " + asm.GetName().Name + " has no LaunchWith.Level"); return false; }
            // Two passes over all the files, a pause between: an answer worked out in the background (Flycast's set table) is
            // there for the second.
            var games = new System.Collections.Generic.List<Unbroken.LaunchBox.Plugins.Data.IGame>();
            foreach (var rom in roms)
            {
                var game = StubGame.Create(Guid.NewGuid().ToString(), Path.GetFileNameWithoutExtension(rom), rom);
                if (platform != null) StubGame.With(game, "Platform", platform);
                games.Add(game);
            }
            for (int pass = 1; pass <= 2; pass++)
            {
                if (pass == 2) System.Threading.Thread.Sleep(5000);
                for (int i = 0; i < roms.Length; i++)
                {
                    var w = System.Diagnostics.Stopwatch.StartNew();
                    var level = (string)m.Invoke(null, new object[] { games[i], exe });
                    Console.WriteLine("  " + Path.GetFileName(roms[i]).PadRight(40) + " " + (level ?? "null").PadRight(12) + " " + w.Elapsed.TotalMilliseconds.ToString("0.0") + " ms (pass " + pass + ")");
                    var note = pass == 2 ? type.GetMethod("Note", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { games[i], exe }) as string : null;
                    if (note != null) Console.WriteLine("      note: " + note.Replace("\n", "\n            "));
                    var url = pass == 2 ? type.GetMethod("Url", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { games[i], exe }) as string : null;
                    if (url != null) Console.WriteLine("      url:  " + url);
                }
            }
            var other = (string)m.Invoke(null, new object[] { StubGame.Create("x", "x", roms.Length > 0 ? roms[0] : "x.zip"), @"C:\Emu\mame.exe" });
            Console.WriteLine("  another emulator: " + (other == null ? "no answer (ok)" : "answered " + other + " (WRONG)"));
            return other == null;
        }
    }
}
