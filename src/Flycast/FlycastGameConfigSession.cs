// What a session of the plugin's per-game layer (gone 04/10, Mehdi: a game's own settings are Flycast's own, "Make Game
// Config") took out of a game's OWN Flycast config - a [<ID>] section of emu.cfg - put back. Until then, the keys a game's
// section set that the plugin's -config gave were taken out for the session (a section's key wins over a global
// -config), noted first in <install>\lbip-gameconfig.restore, and put back once Flycast had quit. Nothing is taken out
// any more; a note left behind by such a session (the host or the machine went mid-session) is still honoured:
//
//   each key noted goes back into its section - unless the section now holds that key (set again in Flycast: the user's
//   is kept) or the section is gone (deleted in Flycast: not brought back). Not the whole file: what Flycast wrote at
//   exit stays. At every launch, at the plugin's start-up check, and when Flycast is opened without a game.
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

        /// <summary>Is a session's note still there - the game's keys not put back yet?</summary>
        public static bool Pending(FlycastLayout layout)
        {
            var note = NotePath(layout);
            return note != null && File.Exists(note);
        }

        /// <summary>Put back what a session took out of a game's section - see the header. Never while Flycast
        /// runs; never throws.</summary>
        public static void Restore(FlycastLayout layout, string why, string session = null)
        {
            var note = NotePath(layout);
            try
            {
                if (note == null || !File.Exists(note)) return;
                if (FlycastIni.RunningEmulatorProcess() != null) { Log.Info("game config: Flycast is running - the game's keys go back once it has quit"); return; }
                string section = null, id = null;
                var noted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(note))
                {
                    var f = line.Split('\t');
                    if (f.Length == 2 && f[0] == "session") id = f[1];
                    else if (f.Length == 2 && f[0] == "section") section = Uri.UnescapeDataString(f[1]);
                    else if (f.Length == 3 && f[0] == "key") noted[Uri.UnescapeDataString(f[1])] = Uri.UnescapeDataString(f[2]);
                }
                if (session != null && id != session) { Log.Info("game config: the session on is a later launch's - left to it (" + why + ")"); return; }
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
    }
}
