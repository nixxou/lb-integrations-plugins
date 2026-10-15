// A game's session, watched from the launch to its end - for what has to be put back when ZiNc EX has gone: the user's
// ROMs folder in zinc-settings.cfg (ZincExConfig), and what ZiNc EX said when it refused to start the game.
//
// As the Demul plugin's DemulSession: the end is when no ZiNc EX of this folder is left, two seconds in a row (it waits for
// ZiNc.exe itself, settings.c run_game_cli, so it lives as long as the game); one that never starts within a minute ends it
// too. The launch gate is held meanwhile (LbipLaunchGate.HoldOpen): no second launch can point the folder elsewhere while
// the first one still has it.
//
// A REFUSAL: ZiNc EX exits with 2, 3, 4 or 5 and writes the reason to ZiNc-EX_launch.log beside it (cli_log - written on
// those paths only, and over the last one). A log written during the session is shown, so a launch that did nothing says why.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LbIntegrations.Lbip;

namespace LbIntegrations.ZincEx
{
    internal static class ZincExSession
    {
        private const int AppearSeconds = 60, GoneSeconds = 2;

        public static void Watch(string exe, string packName, Action ended)
        {
            var hold = LbipLaunchGate.HoldOpen();
            var started = DateTime.UtcNow.AddSeconds(-2);
            var exeDir = Path.GetDirectoryName(Path.GetFullPath(exe));
            Task.Run(() =>
            {
                using var held = hold;
                try
                {
                    var armed = Stopwatch.StartNew();
                    while (!Running(exe) && armed.Elapsed.TotalSeconds < AppearSeconds) Thread.Sleep(400);
                    if (!Running(exe)) Log.Info("session: ZiNc EX never started");
                    else
                    {
                        var lastSeen = Stopwatch.StartNew();
                        while (lastSeen.Elapsed.TotalSeconds < GoneSeconds)
                        {
                            if (Running(exe)) lastSeen.Restart();
                            Thread.Sleep(400);
                        }
                        Log.Info("session: over");
                    }
                }
                catch (Exception ex) { Log.Warn("session watch", ex); }
                finally
                {
                    ZincExConfig.Restore(exeDir);
                    try { ended?.Invoke(); } catch { }
                    SayRefusal(exeDir, started, packName);
                }
            });
        }

        private static void SayRefusal(string exeDir, DateTime since, string packName)
        {
            try
            {
                var log = Path.Combine(exeDir, ZincExLaunch.LaunchLogName);
                if (!File.Exists(log) || File.GetLastWriteTimeUtc(log) < since) return;
                var text = string.Join("\n", File.ReadAllLines(log).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("ZiNc EX v", StringComparison.Ordinal)).Take(4));
                // A log of ZiNc EX's own "Write log files" option (run_headless) carries the command line, not a refusal.
                if (text.Length == 0 || text.Contains("--roms-directory=")) return;
                Log.Info("ZiNc EX refused the game: " + text.Replace("\n", " | "));
                LbipNotice.Show(packName, "ZiNc EX did not start the game:\n\n" + text);
            }
            catch (Exception ex) { Log.Warn("could not read " + ZincExLaunch.LaunchLogName, ex); }
        }

        /// <summary>A ZiNc EX of this path is running.</summary>
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
