// What a launch changes in Mesen's settings for ONE game, put back once Mesen has quit.
//
// WHY (measured 18/10, docs\mesen.md §5, §13): a configuration switch on Mesen's line (--gameboy.model=AutoFavorGb, the
// platform row's; a game's options) is not a session value - Mesen sets it in its configuration and WRITES IT INTO
// settings.json (Nes.Region "Pal" and ShowFps true were there after a run with --nes.region=Pal --preferences.showFps=true).
// And the save / state folders are one setting for every system, set to the game's platform before each launch
// (MesenSavePaths). Left so, the next game - or Mesen opened by hand - would inherit the last game's. --doNotSaveSettings is
// NOT the way: it drops every change of the session, the user's keys rebound and Mesen's recent games with them.
//
// SO, before the launch, each key the plugin is about to force is written down with its value in settings.json (or its
// absence); once Mesen has quit, each key STILL HOLDING the forced value gets its old value back (or is taken out again); a
// key the user changed during the session - in Mesen's own settings - is his and stays. Hotkeys are not in it: the pack's
// keys are meant to last (MesenLaunch.BindPackKeys binds them only where Mesen has none).
//
// A CRASH IN BETWEEN (LaunchBox gone before Mesen): the plan is on disk (session.txt beside the plugin's settings); the next
// launch, the next install and the plugin's next start finish it first - never while that Mesen runs.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using LbIntegrations.Lbip;

namespace LbIntegrations.Mesen
{
    internal static class MesenSession
    {
        private const int AppearSeconds = 60, GoneSeconds = 2;
        private static readonly object Gate = new object();

        internal static string PlanPath => Path.Combine(Path.GetDirectoryName(MesenSettings.SettingsPath), "session.txt");

        /// <summary>A key forced for the session: its path in settings.json, the value it had (its JSON text; null: absent), the
        /// value forced (as a switch gives it).</summary>
        internal sealed class Forced
        {
            public string Path, Before, Value;
        }

        /// <summary>The settings.json path of a switch's key ("gameboy.model" -> "Gameboy/Model", matched without regard to case).</summary>
        internal static string PathOfSwitch(string key) => string.Join("/", (key ?? "").Split('.'));

        /// <summary>The keys the line's switches force, with what settings.json holds for them now.</summary>
        internal static List<Forced> FromLine(MesenJson json, string line)
        {
            var list = new List<Forced>();
            foreach (var token in MesenLaunch.ConfigSwitches(line))
            {
                var (key, value) = MesenSystems.SwitchParts(token).Value;
                var path = PathOfSwitch(key);
                list.RemoveAll(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));   // the last one wins, as in Mesen
                list.Add(new Forced { Path = path, Before = json.Get(path)?.ToJsonString(), Value = value });
            }
            return list;
        }

        // ── before ───────────────────────────────────────────────────────────

        /// <summary>The plan written down and the watch on Mesen started, which puts the keys back. <paramref name="forced"/>
        /// holds what the plugin wrote itself (with what was there before) and what the line will make Mesen write. Never throws.</summary>
        public static void Begin(string exe, string settings, List<Forced> forced, Action after = null)
        {
            try
            {
                if (forced == null || forced.Count == 0) return;
                lock (Gate)
                {
                    var lines = new List<string> { "exe|" + exe, "settings|" + settings };
                    foreach (var f in forced) lines.Add("key|" + f.Path + "|" + Escape(f.Before) + "|" + Escape(f.Value));
                    Directory.CreateDirectory(Path.GetDirectoryName(PlanPath));
                    File.WriteAllLines(PlanPath, lines);
                    Log.Info("session: " + forced.Count + " setting(s) of Mesen's for this game only, put back after it - " + string.Join(", ", forced.Select(f => f.Path + "=" + f.Value)));
                }
                Watch(exe, after);
            }
            catch (Exception ex) { Log.Warn("session: could not write the plan - the game's settings will stay in Mesen", ex); }
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
                    while (!MesenLaunch.Running(exe) && armed.Elapsed.TotalSeconds < AppearSeconds) Thread.Sleep(500);
                    if (MesenLaunch.Running(exe))
                    {
                        var lastSeen = Stopwatch.StartNew();
                        while (lastSeen.Elapsed.TotalSeconds < GoneSeconds)
                        {
                            if (MesenLaunch.Running(exe)) lastSeen.Restart();
                            Thread.Sleep(400);
                        }
                    }
                    else Log.Info("session: Mesen never started");
                    Recover();
                    after?.Invoke();
                }
                catch (Exception ex) { Log.Warn("session watch", ex); }
            });
        }

        // ── after ────────────────────────────────────────────────────────────

        /// <summary>A plan written down and not finished, finished: each key still holding its forced value put back. Nothing
        /// while that Mesen runs. Never throws.</summary>
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
                if (exe != null && MesenLaunch.Running(exe)) return;
                try
                {
                    if (settings != null && File.Exists(settings))
                    {
                        var json = MesenJson.Load(settings);
                        var back = new List<string>();
                        foreach (var l in lines.Where(x => x.StartsWith("key|")))
                        {
                            var p = l.Split('|');
                            if (p.Length < 4) continue;
                            var path = p[1]; var before = Unescape(p[2]); var value = Unescape(p[3]);
                            var now = MesenJson.TextOf(json.Get(path));
                            if (!string.Equals(now, value, StringComparison.OrdinalIgnoreCase)) continue;   // changed in Mesen: the user's
                            if (before == null) json.Remove(path);
                            else json.Set(path, JsonNode.Parse(before));
                            back.Add(path + (before == null ? " (taken out)" : "=" + before));
                        }
                        if (json.SaveIfChanged()) Log.Info("session: put back in settings.json - " + string.Join(", ", back));
                    }
                    File.Delete(PlanPath);
                }
                catch (Exception ex) { Log.Warn("session: could not put Mesen's settings back - tried again at the next launch", ex); }
            }
        }

        private static string Escape(string s) => s == null ? "\u0001" : s.Replace("%", "%25").Replace("|", "%7C").Replace("\r", "%0D").Replace("\n", "%0A");
        private static string Unescape(string s) => s == "\u0001" ? null : s.Replace("%0A", "\n").Replace("%0D", "\r").Replace("%7C", "|").Replace("%25", "%");
    }
}
