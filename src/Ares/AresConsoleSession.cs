// Each game its own console - what ares keeps for a whole console, and what it names after the disc in the drive, made
// the game's (decided with Mehdi on 06/10, read off ares v148's code):
//
// THE CONSOLE'S MEMORY (AresConsoleFile.PerGame: the Mega CD's, the PC Engine's - HuCard and CD - and the LaserActive's
// backup RAM, the 64DD's clock). ares writes ONE file for every game of the system (mia\system\*.cpp: Pak::save with no
// location -> <home>\<Pak>.sys\<file>). Before ares starts, the game's own copy - <game><ext> beside its saves - is put
// there; when ares has quit, what it left there is copied back to the game. A game with no copy yet starts from the
// memory as it was before the plugin took it over (kept once as <file>.before-nixx: no progress lost to the change), or,
// with none, from a blank one - ares formats it (the Mega CD's header, mia\system\mega-cd.cpp).
//
// THE DISC IN THE DRIVE. ares names a disc game's memory card after the disc it was given AND the one swapped in by Change
// Disc (desktop-ui\emulator\playstation.cpp:171, memoryCard->save(..., game->location) at unload): Final Fantasy VII
// (Disc 2).card, read back by nobody. The game's card is <its .m3u's name>.card: put under the launched disc's name before,
// and the newest of its discs' cards brought back to it after - the discs' copies then deleted. The card stays in memory
// through a Change Disc (load(Menu) saves, then loads the new disc only), so the newest is the session's. Save states stay
// per disc: one taken on disc 2 needs disc 2 in the drive.
//
// A CRASH IN BETWEEN (LaunchBox gone before ares): what was put in place is written down first (session.txt beside the
// settings); the next launch, or the next listing, finishes it before anything else.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LbIntegrations.Lbip;

namespace LbIntegrations.Ares
{
    internal static class AresConsoleSession
    {
        private const int AppearSeconds = 60, GoneSeconds = 2;
        internal const string BeforeSuffix = ".before-nixx";

        private static readonly object Gate = new object();

        private static string PlanPath => Path.Combine(Path.GetDirectoryName(AresSettings.SettingsPath), "session.txt");

        /// <summary>A game's name for its saves: a .m3u's own (the identity of a multi-disc save), else the file ares is given.</summary>
        internal static string IdentityOf(string rom, string exe)
            => string.Equals(Path.GetExtension(rom ?? ""), ".m3u", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(rom) : AresPlugin.NameOf(rom, exe);

        /// <summary>The names ares may give a disc game's files: its discs' (a .m3u's every disc), else its own.</summary>
        internal static List<string> DiscNames(string rom, AresSystem system, string exe)
        {
            if (string.Equals(Path.GetExtension(rom ?? ""), ".m3u", StringComparison.OrdinalIgnoreCase))
                return AresLaunch.M3uDiscs(rom).Select(d => Path.GetFileNameWithoutExtension(AresLaunch.DiscFile(d, system))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return new List<string> { AresPlugin.NameOf(rom, exe) };
        }

        /// <summary>Where a game's own copy of a console file is kept: beside its saves, under its name.</summary>
        internal static string OwnCopy(string exe, string rom, AresSystem system, AresConsoleFile f)
        {
            var dir = AresPlugin.FoldersFor(exe, rom, system).SaveTarget;
            return dir == null ? null : Path.Combine(dir, IdentityOf(rom, exe) + f.Ext);
        }

        // ── before the launch ────────────────────────────────────────────────

        /// <summary>The game's console put in place - and the watch on ares started, which takes it back. Never throws.</summary>
        public static void Begin(string exe, AresSystem system, string rom, string played)
        {
            try
            {
                if (system == null || string.IsNullOrEmpty(rom)) return;
                lock (Gate)
                {
                    Recover();
                    if (AresLaunch.Running(exe)) { Log.Warn("console memory: ares is already running - left as it is"); return; }
                    var plan = new List<string> { "exe|" + exe };

                    foreach (var cf in system.ConsoleFiles.Where(c => c.PerGame))
                    {
                        var live = AresPlugin.ConsolePath(exe, cf);
                        var own = OwnCopy(exe, rom, system, cf);
                        if (live == null || own == null) continue;
                        var before = live + BeforeSuffix;
                        if (!File.Exists(before) && File.Exists(live)) { File.Copy(live, before); Log.Info("console memory: " + live + " kept as it was, " + Path.GetFileName(before)); }
                        Directory.CreateDirectory(Path.GetDirectoryName(live));
                        if (File.Exists(own)) { File.Copy(own, live, true); Log.Info("console memory: the game's own " + Path.GetFileName(own) + " -> " + live); }
                        else if (File.Exists(before)) { File.Copy(before, live, true); Log.Info("console memory: no copy of its own yet - the memory as it was before (" + Path.GetFileName(before) + ")"); }
                        else if (File.Exists(live)) { File.Delete(live); Log.Info("console memory: no copy of its own yet - a blank one (ares formats it)"); }
                        plan.Add("swap|" + live + "|" + own);
                    }

                    // The memory card of a disc game: under the launched disc's name.
                    if (system.Disc && system.SaveExtensions.Contains(".card"))
                    {
                        var dir = AresPlugin.FoldersFor(exe, rom, system).SaveTarget;
                        var identity = IdentityOf(rom, exe);
                        var launched = Path.GetFileNameWithoutExtension(played ?? rom);
                        if (dir != null)
                        {
                            var game = Path.Combine(dir, identity + ".card");
                            var discs = DiscNames(rom, system, exe).Concat(new[] { launched }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                            if (!string.Equals(identity, launched, StringComparison.OrdinalIgnoreCase))
                            {
                                var asDisc = Path.Combine(dir, launched + ".card");
                                if (File.Exists(game)) { File.Copy(game, asDisc, true); Log.Info("memory card: " + Path.GetFileName(game) + " -> " + Path.GetFileName(asDisc)); }
                            }
                            plan.Add("card|" + game + "|" + string.Join("|", discs.Select(d => Path.Combine(dir, d + ".card"))));
                        }
                    }

                    if (plan.Count == 1) return;
                    Directory.CreateDirectory(Path.GetDirectoryName(PlanPath));
                    File.WriteAllLines(PlanPath, plan);
                }
                Watch(exe);
            }
            catch (Exception ex) { Log.Warn("console memory: could not put the game's in place", ex); }
        }

        // ── after ────────────────────────────────────────────────────────────

        private static void Watch(string exe)
        {
            var hold = LbipLaunchGate.HoldOpen();
            Task.Run(() =>
            {
                using var held = hold;
                try
                {
                    var armed = Stopwatch.StartNew();
                    while (!AresLaunch.Running(exe) && armed.Elapsed.TotalSeconds < AppearSeconds) Thread.Sleep(500);
                    if (AresLaunch.Running(exe))
                    {
                        var lastSeen = Stopwatch.StartNew();
                        while (lastSeen.Elapsed.TotalSeconds < GoneSeconds)
                        {
                            if (AresLaunch.Running(exe)) lastSeen.Restart();
                            Thread.Sleep(400);
                        }
                    }
                    else Log.Info("session: ares never started");
                    lock (Gate) Recover();
                }
                catch (Exception ex) { Log.Warn("session watch", ex); }
            });
        }

        /// <summary>A session written down and not finished, finished: the console's memory and the card taken back to the
        /// game. Nothing while its ares still runs.</summary>
        public static void Recover()
        {
            lock (Gate)
            {
                string[] lines;
                try { if (!File.Exists(PlanPath)) return; lines = File.ReadAllLines(PlanPath); }
                catch (Exception ex) { Log.Warn("session: could not read " + PlanPath, ex); return; }
                var exe = lines.Where(l => l.StartsWith("exe|")).Select(l => l.Substring(4)).FirstOrDefault();
                if (exe != null && AresLaunch.Running(exe)) return;
                bool done = true;
                foreach (var l in lines)
                {
                    var p = l.Split('|');
                    try
                    {
                        if (p[0] == "swap" && p.Length == 3 && File.Exists(p[1]))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(p[2]));
                            File.Copy(p[1], p[2], true);
                            Log.Info("console memory: taken back to the game, " + p[2]);
                        }
                        else if (p[0] == "card" && p.Length >= 3)
                        {
                            var game = p[1];
                            var copies = p.Skip(2).Where(c => !string.Equals(c, game, StringComparison.OrdinalIgnoreCase) && File.Exists(c)).ToList();
                            var newest = copies.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                            if (newest != null && (!File.Exists(game) || File.GetLastWriteTimeUtc(newest) >= File.GetLastWriteTimeUtc(game)))
                            {
                                File.Copy(newest, game, true);
                                Log.Info("memory card: " + Path.GetFileName(newest) + " -> " + Path.GetFileName(game));
                            }
                            if (File.Exists(game) && newest != null)
                                foreach (var c in copies) { File.Delete(c); Log.Info("memory card: the disc's copy deleted, " + Path.GetFileName(c)); }
                        }
                    }
                    catch (Exception ex) { done = false; Log.Warn("session: " + l, ex); }
                }
                if (done) try { File.Delete(PlanPath); } catch { }
            }
        }
    }
}
