// A game's session, watched from the launch to its end - then xemu.toml put back on the stand-alone console.
//
// THE END IS WHEN XEMU HAS GONE, by name and folder: one process for the whole game (no reboot into a new one, unlike
// Cxbx-Reloaded's loader). Then [sys.files] hdd_path goes back to hdd\standalone.qcow2: xemu opened on its own afterwards
// boots its own console, never the last game's - and never the pristine base.qcow2. The launch gate stays shut until this
// is done (LbipLaunchGate.HoldOpen).

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LbIntegrations.Lbip;

namespace LbIntegrations.Xemu
{
    internal static class XemuSession
    {
        private const int AppearSeconds = 60, GoneSeconds = 2;

        public static void Watch(string exe, string gameHdd)
        {
            var hold = LbipLaunchGate.HoldOpen();
            Task.Run(() =>
            {
                using var held = hold;
                try
                {
                    var armed = Stopwatch.StartNew();
                    while (!XemuPaths.Running(exe) && armed.Elapsed.TotalSeconds < AppearSeconds) Thread.Sleep(500);
                    if (!XemuPaths.Running(exe)) { Log.Info("session: xemu never started"); return; }
                    Log.Info("session: xemu is running on " + System.IO.Path.GetFileName(gameHdd));
                    var lastSeen = Stopwatch.StartNew();
                    while (lastSeen.Elapsed.TotalSeconds < GoneSeconds)
                    {
                        if (XemuPaths.Running(exe)) lastSeen.Restart();
                        Thread.Sleep(400);
                    }
                    Log.Info("session: over");
                }
                catch (Exception ex) { Log.Warn("session watch", ex); }
                finally { Standalone(exe, "its game is over"); }
            });
        }

        /// <summary>xemu.toml pointed back at the stand-alone console (made when it is not there). Never while xemu runs.</summary>
        public static void Standalone(string exe, string why)
        {
            try
            {
                if (XemuPaths.Running(exe)) return;
                var toml = XemuPaths.TomlOf(exe);
                var standalone = XemuPaths.StandaloneHdd(exe);
                var base_ = XemuPaths.BaseHdd(exe);
                if (toml == null || standalone == null || base_ == null || !System.IO.File.Exists(base_)) return;
                if (!System.IO.File.Exists(standalone))
                {
                    var error = Qcow2Overlay.Create(base_, standalone);
                    if (error != null) { Log.Warn("console: the stand-alone one could not be made - " + error); return; }
                }
                if (XemuToml.Set(toml, "sys.files", "hdd_path", XemuToml.Literal(standalone)))
                    Log.Info("xemu.toml: back on the stand-alone console (" + why + ")");
            }
            catch (Exception ex) { Log.Warn("xemu.toml: could not point it back at the stand-alone console", ex); }
        }
    }
}
