// A game's session, watched from the launch to its end - for what has to be undone when Demul has gone: a CHD served
// uncompressed by the RAM disk helper (DemulChd), detached then. As the xemu plugin's XemuSession: the end is when no
// demul.exe of this folder is left, two seconds in a row; a Demul that never starts within a minute ends it too.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LbIntegrations.Lbip;

namespace LbIntegrations.Demul
{
    internal static class DemulSession
    {
        private const int AppearSeconds = 60, GoneSeconds = 2;

        public static void Watch(string exe, string servedRoot)
        {
            if (servedRoot == null) return;
            var hold = LbipLaunchGate.HoldOpen();
            Task.Run(() =>
            {
                using var held = hold;
                try
                {
                    var armed = Stopwatch.StartNew();
                    while (!Running(exe) && armed.Elapsed.TotalSeconds < AppearSeconds) Thread.Sleep(500);
                    if (!Running(exe)) { Log.Info("session: Demul never started"); return; }
                    var lastSeen = Stopwatch.StartNew();
                    while (lastSeen.Elapsed.TotalSeconds < GoneSeconds)
                    {
                        if (Running(exe)) lastSeen.Restart();
                        Thread.Sleep(400);
                    }
                    Log.Info("session: over");
                }
                catch (Exception ex) { Log.Warn("session watch", ex); }
                finally { DemulChd.Release(servedRoot); }
            });
        }

        /// <summary>A demul.exe of this folder is running.</summary>
        public static bool Running(string exe)
        {
            try
            {
                var full = Path.GetFullPath(exe);
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(full)))
                {
                    using (p)
                    {
                        try { if (string.Equals(p.MainModule?.FileName, full, StringComparison.OrdinalIgnoreCase)) return true; }
                        catch { return true; }      // not ours to read: taken as running, the safe way
                    }
                }
            }
            catch { }
            return false;
        }
    }
}
