// This plugin's settings over a game's OWN Flycast config when the command line cannot reach it (Mehdi, 29/09:
// this plugin's > the game's own config > the emulator's).
//
// A game's own config is a SECTION of emu.cfg, [<ID>], read after the command line's global values - so a key
// it sets wins over this plugin's -config. When the id can be named on the command line, the key is given in
// that section too (FlycastGameSettings.Argument) and nothing is written. When it cannot - an id with a space
// in it, an arcade board's title - the section's own keys are TAKEN OUT for the session, Mehdi's .bak made to
// fit a section of a file Flycast rewrites whole:
//
//   1. a NOTE FIRST, <install>\lbip-gameconfig.restore: the section and each key taken, with its value -
//      written before a byte of emu.cfg changes;
//   2. the keys taken out of [<ID>] - its header and its other keys stay, so Flycast's per-game mode is as it was;
//   3. ONCE FLYCAST HAS QUIT (it rewrites emu.cfg from memory at exit), each key noted is PUT BACK into the
//      section - unless the section now holds that key (set again by the user in Flycast during the session:
//      theirs is kept) or the section is gone (deleted by the user in Flycast: not brought back). NOT the whole
//      file put back: what Flycast wrote at exit (its window, a setting the user changed) must stay.
//
// A note still there - the host or the machine went mid-session - is put back first: at every launch, at the
// plugin's start-up check, and when Flycast is opened without a game (LbEmulatorOpened).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Flycast
{
    internal static class FlycastGameConfigSession
    {
        public const string NoteName = "lbip-gameconfig.restore";

        private static string NotePath(FlycastLayout layout)
            => string.IsNullOrEmpty(layout?.InstallDir) ? null : Path.Combine(layout.InstallDir, NoteName);

        /// <summary>Take out of the game's section the keys this plugin gives, for the session. True when done
        /// (or nothing to take). Never throws.</summary>
        public static bool Apply(FlycastLayout layout, string section, IEnumerable<FlycastGameSettings.Raw> keys)
        {
            var note = NotePath(layout);
            try
            {
                if (note == null || string.IsNullOrEmpty(layout.ConfigFile)) return false;
                if (File.Exists(note)) { Log.Warn("game config: a previous session's could not be put back - this game's own config wins this time"); return false; }
                var names = (keys ?? Enumerable.Empty<FlycastGameSettings.Raw>()).Select(k => k.Section + "." + k.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (names.Length == 0) return true;
                var held = FlycastIni.Read(layout.ConfigFile, section, names);
                if (held.Count == 0) return true;
                // 1. THE NOTE FIRST.
                var lines = new List<string> { "section\t" + Uri.EscapeDataString(section) };
                lines.AddRange(held.Select(kv => "key\t" + Uri.EscapeDataString(kv.Key) + "\t" + Uri.EscapeDataString(kv.Value)));
                FlycastIni.WriteAtomicBytes(note, new UTF8Encoding(false).GetBytes(string.Join("\r\n", lines) + "\r\n"));
                // 2. The keys out.
                var why = FlycastIni.Remove(layout.ConfigFile, section, held.Keys);
                if (why != null)
                {
                    try { File.Delete(note); } catch { }
                    Log.Warn("game config: " + why + " - this game's own config wins this time");
                    return false;
                }
                Log.Info("game config [" + section + "]: " + string.Join(", ", held.Select(kv => kv.Key + " = " + kv.Value))
                         + " taken out for the session - this plugin's are given; they come back when Flycast quits");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("game config: could not take the game's keys out", ex);
                Restore(layout, "the write failed");
                return false;
            }
        }

        /// <summary>Put back what a session took out of a game's section - see the header. Never while Flycast
        /// runs; never throws.</summary>
        public static void Restore(FlycastLayout layout, string why)
        {
            var note = NotePath(layout);
            try
            {
                if (note == null || !File.Exists(note)) return;
                if (FlycastIni.RunningEmulatorProcess() != null) { Log.Info("game config: Flycast is running - the game's keys go back once it has quit"); return; }
                string section = null;
                var noted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(note))
                {
                    var f = line.Split('\t');
                    if (f.Length == 2 && f[0] == "section") section = Uri.UnescapeDataString(f[1]);
                    else if (f.Length == 3 && f[0] == "key") noted[Uri.UnescapeDataString(f[1])] = Uri.UnescapeDataString(f[2]);
                }
                if (section == null) { File.Delete(note); return; }
                if (!FlycastIni.HasSection(layout.ConfigFile, section))
                {
                    File.Delete(note);
                    Log.Info("game config [" + section + "]: gone from emu.cfg (deleted in Flycast) - not brought back (" + why + ")");
                    return;
                }
                var now = FlycastIni.Read(layout.ConfigFile, section, noted.Keys.ToArray());
                var back = noted.Where(kv => !now.ContainsKey(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
                var failed = back.Count == 0 ? null : FlycastIni.Write(layout.ConfigFile, section, back);
                if (failed != null) { Log.Warn("game config [" + section + "]: " + failed + " - kept to put back later"); return; }
                File.Delete(note);
                Log.Info("game config [" + section + "]: " + (back.Count == 0 ? "nothing to put back" : string.Join(", ", back.Keys) + " back")
                         + (noted.Count > back.Count ? ", " + (noted.Count - back.Count) + " set again in Flycast meanwhile, kept" : "") + " (" + why + ")");
            }
            catch (Exception ex) { Log.Warn("game config: could not put the game's keys back", ex); }
        }

        /// <summary>Wait for the Flycast of this launch to come and go, then put the game's keys back and learn
        /// its id from the log, when those were asked for.</summary>
        public static void WhenDone(FlycastLayout layout, bool restore, FlycastGameIdentity.Learning learning)
        {
            if (!restore && learning == null) return;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var armed = DateTime.UtcNow;
                    bool appeared = false;
                    while ((DateTime.UtcNow - armed).TotalSeconds < 120)
                    {
                        if (FlycastIni.RunningEmulatorProcess() != null) { appeared = true; break; }
                        System.Threading.Thread.Sleep(500);
                    }
                    if (appeared)
                        while (FlycastIni.RunningEmulatorProcess() != null) System.Threading.Thread.Sleep(500);
                    System.Threading.Thread.Sleep(1000);   // emu.cfg and the log, written at exit
                    if (restore) Restore(layout, appeared ? "the session is over" : "Flycast never started");
                    if (learning != null) FlycastGameIdentity.AfterExit(layout, learning);
                }
                catch (Exception ex) { Log.Warn("game config: watching for the end of the session", ex); }
            });
        }
    }
}
