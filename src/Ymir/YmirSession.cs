// What a launch changes in Ymir.toml for ONE game, put back once Ymir has quit - MesenSession's model.
//
// WHY (Mehdi 20/10, docs\ymir.md §7, §12): the console's internal backup RAM - where a Saturn game keeps its saves - is ONE
// file in Ymir ([System] InternalBackupRAMImagePath, state\bup-int.bin), as on a real Saturn; Ymir's own "per game" option
// splits the discs of one game (each disc its title and product code). So before each launch the path is pointed at the
// GAME's file - <install>\Saves\<platform>\<game>.bin, its .m3u's name, or its disc's without "(Disc N)" (YmirSaves.RamPath) -
// and InternalBackupRAMPerGame is held false for the session.
//
// AFTER: Ymir writes Ymir.toml whole when it quits, our path in it RELATIVE to its state folder ('..\..\Saves\...',
// measured 20/10). Each key still holding the value forced (the path compared resolved) gets its old value back, or is
// taken out again; a key the user changed in Ymir's settings during the game is his and stays. So Ymir opened by hand, or
// another game, never writes into this game's saves.
//
// A CRASH IN BETWEEN (LaunchBox gone before Ymir): the plan is on disk (session.txt beside the plugin's settings); the next
// launch, the next install and the plugin's next start finish it first - never while that Ymir runs.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LbIntegrations.Lbip;

namespace LbIntegrations.Ymir
{
    internal static class YmirSession
    {
        private const int AppearSeconds = 60, GoneSeconds = 2;
        private static readonly object Gate = new object();

        internal static string PlanPath => Path.Combine(YmirSettings.DataDir, "session.txt");

        /// <summary>A key forced for the session: its table and name, the raw TOML it had (null: absent), the raw TOML forced;
        /// Path: the value is a path Ymir may write back relative to its state folder.</summary>
        internal sealed class Forced
        {
            public string Table, Key, Before, Value;
            public bool Path;
        }

        /// <summary>The keys forced in <paramref name="doc"/> (written by the caller), with what they held.</summary>
        public static List<Forced> Force(YmirTomlDoc doc, IEnumerable<(string Table, string Key, string Value, bool Path)> keys)
        {
            var list = new List<Forced>();
            foreach (var (table, key, value, path) in keys)
            {
                var before = doc.Get(table, key);
                if (before != null && Same(before, value, path, null)) continue;
                list.Add(new Forced { Table = table, Key = key, Before = before, Value = value, Path = path });
                doc.Set(table, key, value);
            }
            return list;
        }

        private static bool Same(string now, string forced, bool path, string stateDir)
        {
            if (!path) return string.Equals((now ?? "").Trim(), (forced ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
            string Full(string raw)
            {
                var v = YmirToml.Text(raw);
                if (string.IsNullOrWhiteSpace(v)) return "";
                try { return System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(v) || stateDir == null ? v : System.IO.Path.Combine(stateDir, v)); } catch { return v; }
            }
            return string.Equals(Full(now), Full(forced), StringComparison.OrdinalIgnoreCase);
        }

        // ── before ───────────────────────────────────────────────────────────

        /// <summary>The plan written down and the watch on Ymir started. Never throws.</summary>
        public static void Begin(string exe, string settings, List<Forced> forced, Action after = null)
        {
            try
            {
                if (forced == null || forced.Count == 0) { Watch(exe, after); return; }
                lock (Gate)
                {
                    var lines = new List<string> { "exe|" + exe, "settings|" + settings, "state|" + YmirLaunch.StateDirOf(exe) };
                    foreach (var f in forced) lines.Add("key|" + f.Table + "|" + f.Key + "|" + Escape(f.Before) + "|" + Escape(f.Value) + "|" + (f.Path ? "path" : "value"));
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PlanPath));
                    File.WriteAllLines(PlanPath, lines);
                    Log.Info("session: " + forced.Count + " setting(s) of Ymir's for this game only, put back after it - " + string.Join(", ", forced.Select(f => f.Table + "." + f.Key + "=" + f.Value)));
                }
                Watch(exe, after);
            }
            catch (Exception ex) { Log.Warn("session: could not write the plan - the game's settings will stay in Ymir", ex); }
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
                    while (!YmirLaunch.Running(exe) && armed.Elapsed.TotalSeconds < AppearSeconds) Thread.Sleep(500);
                    if (YmirLaunch.Running(exe))
                    {
                        var lastSeen = Stopwatch.StartNew();
                        while (lastSeen.Elapsed.TotalSeconds < GoneSeconds)
                        {
                            if (YmirLaunch.Running(exe)) lastSeen.Restart();
                            Thread.Sleep(400);
                        }
                    }
                    else Log.Info("session: Ymir never started");
                    Recover();
                    after?.Invoke();
                }
                catch (Exception ex) { Log.Warn("session watch", ex); }
            });
        }

        // ── after ────────────────────────────────────────────────────────────

        /// <summary>A plan written down and not finished, finished (header). Nothing while that Ymir runs. Never throws.</summary>
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
                var stateDir = Field("state");
                if (exe != null && YmirLaunch.Running(exe)) return;
                try
                {
                    if (settings != null && File.Exists(settings))
                    {
                        var doc = YmirTomlDoc.Load(settings);
                        var back = new List<string>();
                        bool changed = false;
                        foreach (var l in lines.Where(x => x.StartsWith("key|")))
                        {
                            var p = l.Split('|');
                            if (p.Length < 6) continue;
                            string table = p[1], key = p[2], before = Unescape(p[3]), value = Unescape(p[4]);
                            bool path = p[5] == "path";
                            var now = doc.Get(table, key);
                            if (now == null || !Same(now, value, path, stateDir)) continue;   // changed in Ymir: the user's
                            changed |= before == null ? doc.Put(table, key, null) : doc.Set(table, key, before);
                            back.Add(table + "." + key + (before == null ? " (taken out)" : "=" + before));
                        }
                        if (changed) { doc.Save(settings); Log.Info("session: put back in Ymir.toml - " + string.Join(", ", back)); }
                    }
                    File.Delete(PlanPath);
                }
                catch (Exception ex) { Log.Warn("session: could not put Ymir's settings back - tried again at the next launch", ex); }
            }
        }

        private static string Escape(string s) => s == null ? "\u0001" : s.Replace("%", "%25").Replace("|", "%7C").Replace("\r", "%0D").Replace("\n", "%0A");
        private static string Unescape(string s) => s == "\u0001" ? null : s.Replace("%0A", "\n").Replace("%0D", "\r").Replace("%7C", "|").Replace("%25", "%");
    }
}
