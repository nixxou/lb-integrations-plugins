// --compat-checks <LaunchBox root>: the Compatibility page's reflection checks, out of LaunchBox (docs\lb141-plan.md,
// step 1). Loads what the checks look into - LaunchBox's integrations (System\Plugins), Unbroken.LaunchBox, LaunchBox.dll,
// Microsoft.Data.Sqlite - from that install, nothing run, then prints the host's shape and every check of the page.
//
// The SDK is the probe's own (14.0.1.2, Private=true). To check a LaunchBox 14.1 install as LaunchBox itself runs us -
// its SDK, our code built against 14.0 - run a COPY of the probe's folder with that install's Core\Unbroken.LaunchBox.Plugins.dll
// put in place of the probe's: a 14.1 integration references the 14.1 SDK and does not load beside the 14.0 one.
//
// Out of LaunchBox, the checks bound to its process (the wizard, Launch With, Open emulator) say "not inside LaunchBox".

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using LbIntegrations.Compat;

namespace LbIntegrations.Probe
{
    internal static class CompatChecksRun
    {
        public static bool Run(string root)
        {
            if (root == null || !Directory.Exists(Path.Combine(root, "Core"))) { Console.Error.WriteLine("--compat-checks <LaunchBox root>"); return false; }
            root = Path.GetFullPath(root);
            var core = Path.Combine(root, "Core");
            var dirs = new List<string> { core };
            var plugins = Path.Combine(root, "System", "Plugins");
            if (Directory.Exists(plugins)) dirs.AddRange(Directory.GetDirectories(plugins));
            AssemblyLoadContext.Default.Resolving += (ctx, name) =>
            {
                foreach (var d in dirs)
                {
                    var p = Path.Combine(d, name.Name + ".dll");
                    if (File.Exists(p)) return ctx.LoadFromAssemblyPath(p);
                }
                return null;
            };

            var sdk = typeof(Unbroken.LaunchBox.Plugins.EmulatorPlugin).Assembly;
            Console.WriteLine("SDK in this process: " + sdk.GetName().Version + "  (" + sdk.Location + ")");
            var hostSdk = Path.Combine(core, "Unbroken.LaunchBox.Plugins.dll");
            if (File.Exists(hostSdk) && System.Reflection.AssemblyName.GetAssemblyName(hostSdk).Version != sdk.GetName().Version)
                Console.WriteLine("  ! the install's SDK is " + System.Reflection.AssemblyName.GetAssemblyName(hostSdk).Version
                                  + " - run a copy of the probe with it in place of the probe's own (see the header)");

            int loaded = 0;
            // Unbroken.LaunchBox.Windows for the registry's shape - its registry is never built here, so lb.pluginRegistry says
            // "not built yet" in the probe.
            foreach (var path in new[] { Path.Combine(core, "Unbroken.LaunchBox.dll"), Path.Combine(core, "Unbroken.LaunchBox.Windows.dll"), Path.Combine(core, "LaunchBox.dll"), Path.Combine(core, "Microsoft.Data.Sqlite.dll") }
                         .Concat(Directory.Exists(plugins) ? Directory.GetFiles(plugins, "Unbroken.LaunchBox.Windows.*.dll", SearchOption.AllDirectories) : Array.Empty<string>()))
            {
                if (!File.Exists(path)) continue;
                try { AssemblyLoadContext.Default.LoadFromAssemblyPath(path); loaded++; }
                catch (Exception ex) { Console.WriteLine("  not loaded: " + Path.GetFileName(path) + " - " + ex.GetType().Name + ": " + ex.Message); }
            }
            Console.WriteLine(loaded + " assemblies loaded from " + root);
            Console.WriteLine("shape: " + LbHostShape.Describe());
            Console.WriteLine();

            bool anyMissing = false;
            foreach (var id in CompatRegistry.All.SelectMany(p => p.Checks).Distinct())
            {
                var (state, detail) = CompatChecks.Check(id);
                if (state == CompatCheckState.Missing) anyMissing = true;
                Console.WriteLine(("  " + id).PadRight(24) + state.ToString().PadRight(12) + detail);
            }
            foreach (var id in CompatRegistry.All.Select(p => p.BugTest).Where(b => b != null).Distinct())
            {
                var (state, detail) = CompatChecks.Bug(id!);
                Console.WriteLine(("  bug " + id).PadRight(24) + state.ToString().PadRight(12) + detail);
            }
            Console.WriteLine();
            Console.WriteLine(anyMissing ? "MISSING checks above" : "no check missing");
            return true;
        }
    }
}
