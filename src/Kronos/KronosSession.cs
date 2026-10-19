// What a launch changes in kronos.ini for ONE game, put back once Kronos has quit - Ymir's model (YmirSession).
//
// WHY (docs\kronos.md §3, §4): Kronos has no per-game settings - its kronos.ini is one for every game, and its command line
// cannot say where the saves go. So what a game needs for itself is written there before its launch and put back after:
//   - its internal backup RAM, ONE file in Kronos (Memory\Path) - pointed at the GAME's file, <install>\Saves\<platform>\
//     <game>.bkr, the standard 32 KB size forced (Memory\ExtendMemory false: Kronos's default, an 8 MB memory no Saturn has,
//     would make a new file of 8 MB);
//   - its save states' folder (General\SaveStates): Kronos names a state by the disc's product code alone, so each game gets
//     its own folder;
//   - its backup cartridge when it has one (Cartridge\Type 2 = 4 Mbit, Cartridge\Path).
//
// AFTER: Kronos writes kronos.ini whole when it quits, every key it read written back as it read it (measured 19/10). Each key
// still holding the value forced (a path compared resolved) gets its old value back, or is taken out again; one the user
// changed in Kronos's settings during the game is his and stays.
//
// A CRASH IN BETWEEN (LaunchBox gone before Kronos): the plan is on disk (session.txt beside the plugin's settings); the next
// launch, the next install and the plugin's next start finish it first - never while that Kronos runs.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LbIntegrations.Lbip;

namespace LbIntegrations.Kronos
{
    internal static class KronosSession
    {
        private const int AppearSeconds = 60, GoneSeconds = 2;
        private static readonly object Gate = new object();

        internal static string PlanPath => Path.Combine(KronosSettings.DataDir, "session.txt");

        /// <summary>A key forced for the session: its name ("Memory\Path"), the raw value it had (null: absent), the raw value
        /// forced, whether it is a path (compared resolved); Option: the game option it is part of, null for the plugin's own.</summary>
        internal sealed class Forced
        {
            public string Key, Before, Value, Option;
            public bool IsPath;
        }

        /// <summary>The keys forced in <paramref name="doc"/> (saved by the caller), with what they held - each a TEXT value,
        /// escaped here as Qt writes it.</summary>
        public static List<Forced> Force(KronosIniDoc doc, IEnumerable<(string Key, string Value, bool IsPath, string Option)> keys)
        {
            var list = new List<Forced>();
            foreach (var (key, value, isPath, option) in keys)
            {
                var before = doc.GetRaw(key);
                var raw = KronosIniDoc.Escape(value);
                if (option == null && before != null && Same(before, raw, isPath)) continue;
                list.Add(new Forced { Key = key, Before = before, Value = raw, IsPath = isPath, Option = option });
                doc.SetRaw(key, raw);
            }
            return list;
        }

        private static bool Same(string nowRaw, string forcedRaw, bool isPath)
        {
            var now = KronosIniDoc.Unescape(nowRaw) ?? "";
            var forced = KronosIniDoc.Unescape(forcedRaw) ?? "";
            if (!isPath) return string.Equals(now.Trim(), forced.Trim(), StringComparison.OrdinalIgnoreCase);
            string Full(string v)
            {
                if (string.IsNullOrWhiteSpace(v)) return "";
                try { return Path.GetFullPath(v.Replace('/', '\\')).TrimEnd('\\'); } catch { return v; }
            }
            return string.Equals(Full(now), Full(forced), StringComparison.OrdinalIgnoreCase);
        }

        // ── before ───────────────────────────────────────────────────────────

        /// <summary>The plan written down and the watch on Kronos started. Never throws.</summary>
        public static void Begin(string exe, string settings, List<Forced> forced, Action after = null, string gameId = null)
        {
            try
            {
                if (forced == null || forced.Count == 0) { Watch(exe, after); return; }
                lock (Gate)
                {
                    var lines = new List<string> { "exe|" + exe, "settings|" + settings, "game|" + (gameId ?? "") };
                    foreach (var f in forced)
                        lines.Add("key|" + Escape(f.Key) + "|" + Escape(f.Before) + "|" + Escape(f.Value) + "|" + (f.IsPath ? "path" : "-") + "|" + (f.Option ?? "-"));
                    Directory.CreateDirectory(Path.GetDirectoryName(PlanPath));
                    File.WriteAllLines(PlanPath, lines);
                    Log.Info("session: " + forced.Count + " setting(s) of Kronos's for this game only, put back after it - " + string.Join(", ", forced.Select(f => f.Key + "=" + f.Value)));
                }
                Watch(exe, after);
            }
            catch (Exception ex) { Log.Warn("session: could not write the plan - the game's settings will stay in Kronos", ex); }
        }

        private static void Watch(string exe, Action after)
        {
            var hold = LbipLaunchGate.HoldOpen();
            Task.Run(() =>
            {
                using var held = hold;
                try
                {
                    var armed = Stopwatch.StartNew();
                    while (!KronosLaunch.Running(exe) && armed.Elapsed.TotalSeconds < AppearSeconds) Thread.Sleep(500);
                    if (KronosLaunch.Running(exe))
                    {
                        var lastSeen = Stopwatch.StartNew();
                        while (lastSeen.Elapsed.TotalSeconds < GoneSeconds)
                        {
                            if (KronosLaunch.Running(exe)) lastSeen.Restart();
                            Thread.Sleep(400);
                        }
                    }
                    else Log.Info("session: Kronos never started");
                    Recover();
                    after?.Invoke();
                }
                catch (Exception ex) { Log.Warn("session watch", ex); }
            });
        }

        // ── after ────────────────────────────────────────────────────────────

        /// <summary>A plan written down and not finished, finished (header). Nothing while that Kronos runs. Never throws.</summary>
        public static void Recover()
        {
            lock (Gate)
            {
                string[] lines;
                try { if (!File.Exists(PlanPath)) return; lines = File.ReadAllLines(PlanPath); }
                catch (Exception ex) { Log.Warn("session: could not read " + PlanPath, ex); return; }
                string Field(string name) => lines.Where(l => l.StartsWith(name + "|")).Select(l => l.Substring(name.Length + 1)).FirstOrDefault();
                var exe = Field("exe");
                var settings = Field("settings");
                if (exe != null && KronosLaunch.Running(exe)) return;
                try
                {
                    if (settings != null && File.Exists(settings))
                    {
                        var doc = KronosIniDoc.Load(settings);
                        var back = new List<string>();
                        bool changed = false;
                        foreach (var l in lines.Where(x => x.StartsWith("key|")))
                        {
                            var p = l.Split('|');
                            if (p.Length < 6) continue;
                            var f = new Forced { Key = Unescape(p[1]), Before = Unescape(p[2]), Value = Unescape(p[3]), IsPath = p[4] == "path", Option = p[5] == "-" ? null : p[5] };
                            var now = doc.GetRaw(f.Key);
                            if (now == null || !Same(now, f.Value, f.IsPath)) continue;   // changed in Kronos: the user's
                            changed |= doc.SetRaw(f.Key, f.Before);
                            back.Add(f.Key + (f.Before == null ? " (taken out)" : "=" + f.Before));
                        }
                        if (changed) { doc.Save(settings); Log.Info("session: put back in kronos.ini - " + string.Join(", ", back)); }
                    }
                    File.Delete(PlanPath);
                }
                catch (Exception ex) { Log.Warn("session: could not put Kronos's settings back - tried again at the next launch", ex); }
            }
        }

        private static string Escape(string s) => s == null ? "\u0001" : s.Replace("%", "%25").Replace("|", "%7C").Replace("\r", "%0D").Replace("\n", "%0A");
        private static string Unescape(string s) => s == "\u0001" ? null : s.Replace("%0A", "\n").Replace("%0D", "\r").Replace("%7C", "|").Replace("%25", "%");
    }
}
