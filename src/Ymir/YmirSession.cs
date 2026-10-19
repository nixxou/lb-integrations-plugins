// What a launch changes in Ymir.toml for ONE game, put back once Ymir has quit - MesenSession's model.
//
// WHY (Mehdi 20/10, docs\ymir.md §7, §12): Ymir has no per-game settings (read 20/10: only its per-title backup RAM option);
// its Ymir.toml is one for every game. So what a game needs for itself is written there before its launch and put back after:
//   - the console's internal backup RAM, ONE file in Ymir ([System] InternalBackupRAMImagePath) - pointed at the GAME's file,
//     <install>\Saves\<platform>\<game>.bin (YmirSaves.RamPath), InternalBackupRAMPerGame held false (it splits a game's discs);
//   - its backup cartridge when it has one (YmirCart);
//   - its own options from its window (YmirGameOptions): picture, emulation, pads, sound, expansion cartridge.
//
// AFTER: Ymir writes Ymir.toml whole when it quits (and as soon as a setting is changed), our paths in it RELATIVE to their
// folder - the state folder for the RAM and the cartridge ('..\..\Saves\...', measured 20/10), roms\ipl for a BIOS, roms\cart for
// a ROM cartridge. Then:
//   - the plugin's own keys (RAM, cartridge): each still holding the value forced (a path compared resolved) gets its old value
//     back, or is taken out again; one the user changed in Ymir's settings during the game is his and stays;
//   - a GAME OPTION (Mehdi 20/10): one the user changed in Ymir during the game becomes THE GAME'S OWN (written in its options,
//     whatever the value - one this plugin does not list included), and every game's value is put back either way: nothing of
//     one game reaches the others or Ymir opened by hand. An option's keys are written down even when every game's value already
//     was the game's, so such a change is seen.
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
        /// Base: for a path Ymir may write back relative, the folder it is relative to (StateBase for Ymir's state folder; null:
        /// not a path); Option: the game option it is part of (YmirGameOptions), null for the plugin's own (RAM, cartridge).</summary>
        internal sealed class Forced
        {
            public string Table, Key, Before, Value, Base, Option;
        }

        public const string StateBase = "state";

        /// <summary>The keys forced in <paramref name="doc"/> (written by the caller), with what they held.</summary>
        public static List<Forced> Force(YmirTomlDoc doc, IEnumerable<(string Table, string Key, string Value, bool Path)> keys)
            => Force(doc, keys.Select(k => (k.Table, k.Key, k.Value, k.Path ? StateBase : null, (string)null)));

        /// <summary>The same, with each key's path base and option (header: an option's key is written down even when it already
        /// holds the value).</summary>
        public static List<Forced> Force(YmirTomlDoc doc, IEnumerable<(string Table, string Key, string Value, string Base, string Option)> keys)
        {
            var list = new List<Forced>();
            foreach (var (table, key, value, pathBase, option) in keys)
            {
                var before = doc.Get(table, key);
                if (option == null && before != null && Same(before, value, pathBase, null)) continue;
                list.Add(new Forced { Table = table, Key = key, Before = before, Value = value, Base = pathBase, Option = option });
                doc.Set(table, key, value);
            }
            return list;
        }

        private static bool Same(string now, string forced, string pathBase, string stateDir)
        {
            if (pathBase == null) return string.Equals((now ?? "").Trim(), (forced ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
            var dir = pathBase == StateBase ? stateDir : pathBase;
            string Full(string raw)
            {
                var v = YmirToml.Text(raw);
                if (string.IsNullOrWhiteSpace(v)) return "";
                try { return Path.GetFullPath(Path.IsPathRooted(v) || dir == null ? v : Path.Combine(dir, v)); } catch { return v; }
            }
            return string.Equals(Full(now), Full(forced), StringComparison.OrdinalIgnoreCase);
        }

        // ── before ───────────────────────────────────────────────────────────

        /// <summary>The plan written down and the watch on Ymir started. Never throws.</summary>
        public static void Begin(string exe, string settings, List<Forced> forced, Action after = null, string gameId = null)
        {
            try
            {
                if (forced == null || forced.Count == 0) { Watch(exe, after); return; }
                lock (Gate)
                {
                    var lines = new List<string> { "exe|" + exe, "settings|" + settings, "state|" + YmirLaunch.StateDirOf(exe), "game|" + (gameId ?? "") };
                    foreach (var f in forced)
                        lines.Add("key|" + f.Table + "|" + f.Key + "|" + Escape(f.Before) + "|" + Escape(f.Value) + "|" + (f.Base == null ? "-" : Escape(f.Base)) + "|" + (f.Option ?? "-"));
                    Directory.CreateDirectory(Path.GetDirectoryName(PlanPath));
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
                var gameId = Field("game");
                if (exe != null && YmirLaunch.Running(exe)) return;
                try
                {
                    if (settings != null && File.Exists(settings))
                    {
                        var doc = YmirTomlDoc.Load(settings);
                        var back = new List<string>();
                        bool changed = false;
                        var keys = new List<Forced>();
                        foreach (var l in lines.Where(x => x.StartsWith("key|")))
                        {
                            var p = l.Split('|');
                            if (p.Length < 6) continue;
                            // The first build's lines (6 fields): "path" was the state folder, "value" no path.
                            string pathBase = p[5] == "path" ? StateBase : p[5] == "value" || p[5] == "-" ? null : Unescape(p[5]);
                            keys.Add(new Forced { Table = p[1], Key = p[2], Before = Unescape(p[3]), Value = Unescape(p[4]), Base = pathBase,
                                                  Option = p.Length > 6 && p[6] != "-" ? p[6] : null });
                        }
                        void PutBack(Forced f)
                        {
                            changed |= f.Before == null ? doc.Put(f.Table, f.Key, null) : doc.Set(f.Table, f.Key, f.Before);
                            back.Add(f.Table + "." + f.Key + (f.Before == null ? " (taken out)" : "=" + f.Before));
                        }
                        // The plugin's own keys: put back while they hold what was forced; changed in Ymir, they are the user's.
                        foreach (var f in keys.Where(k => k.Option == null))
                        {
                            var now = doc.Get(f.Table, f.Key);
                            if (now == null || !Same(now, f.Value, f.Base, stateDir)) continue;
                            PutBack(f);
                        }
                        // A game option: changed in Ymir during the game, it becomes the game's own; every game's value put back.
                        foreach (var group in keys.Where(k => k.Option != null).GroupBy(k => k.Option))
                        {
                            bool moved = group.Any(f => { var now = doc.Get(f.Table, f.Key); return now == null || !Same(now, f.Value, f.Base, stateDir); });
                            if (moved && !string.IsNullOrEmpty(gameId))
                            {
                                var value = YmirGameOptions.FromToml(group.Key, doc, exe);
                                if (value != null)
                                {
                                    YmirGameOptions.SetGameValue(gameId, group.Key, value);
                                    Log.Info("session: " + group.Key + " changed in Ymir during the game - now the game's own: " + value);
                                }
                            }
                            foreach (var f in group) PutBack(f);
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
