// A game's SESSION options (the ones that were flags on its command line: --no-ramdisk, --use-vhdx[=dir], --ramdisk-margin=, --vita3k-ram=) - kept by this
// plugin, per install, in <install>\lbip-session.tsv: one line per LaunchBox game id, the flags as a line
// would carry them (Mehdi, 29/09).
//
// WHY NOT IN THE GAME'S COMMAND LINE ANY MORE: that line is its DEFAULT emulator's. A game launched with
// "Launch With" on this emulator while another is its default never saw them - and writing them there
// would have broken that other emulator's launches. Kept here, they are the game's whichever emulator of
// ours runs it, and the plugin applies them itself at launch.
//
// AT LAUNCH what is kept here wins; with nothing kept, the flags on the command line still count - the
// emulator's own line can still carry them for every game. An EMPTY line kept here is a choice too: "none
// of them", over the emulator's line.

using System;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Vita3k
{
    internal static class Vita3kSessionStore
    {
        private const string FileName = "lbip-session.tsv";

        /// <summary>The flags kept for the game, "" for "none of them"; null when nothing is kept.</summary>
        public static string Load(string installDir, string gameId)
        {
            try
            {
                var path = installDir == null ? null : Path.Combine(installDir, FileName);
                if (path == null || string.IsNullOrWhiteSpace(gameId) || !File.Exists(path)) return null;
                foreach (var line in File.ReadAllLines(path))
                {
                    int tab = line.IndexOf('\t');
                    if (tab > 0 && line.Substring(0, tab).Equals(gameId, StringComparison.OrdinalIgnoreCase)) return line.Substring(tab + 1).Trim();
                }
            }
            catch (Exception ex) { Log.Warn("session options: could not read them - " + ex.Message); }
            return null;
        }

        /// <summary>Keep the game's flags - "" for none of them - or forget them, with null.</summary>
        public static void Save(string installDir, string gameId, string flags)
        {
            try
            {
                if (installDir == null || string.IsNullOrWhiteSpace(gameId)) return;
                var path = Path.Combine(installDir, FileName);
                var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new System.Collections.Generic.List<string>();
                lines.RemoveAll(l => { int t = l.IndexOf('\t'); return t > 0 && l.Substring(0, t).Equals(gameId, StringComparison.OrdinalIgnoreCase); });
                if (flags != null) lines.Add(gameId + "\t" + flags.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim());
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, string.Join("\r\n", lines) + (lines.Count > 0 ? "\r\n" : ""), new UTF8Encoding(false));
                File.Move(tmp, path, overwrite: true);
                Log.Info("session options of " + gameId + ": " + (flags == null ? "forgotten" : flags.Length == 0 ? "none" : flags));
            }
            catch (Exception ex) { Log.Warn("session options: could not save them - " + ex.Message); }
        }
    }
}
