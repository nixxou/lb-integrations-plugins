// Nothing beside the game (lot 9, docs\saves-lot9.md): WHEN an emulator stopped writing beside the games, and how a file
// beside a game is brought into the emulator's folders.
//
// A folder setting is global to an install, a save beside a game is per game: the launch where a plugin finds a setting empty
// and sets it is the launch of ONE game, and the other games' saves are still beside them - files the emulator read as theirs
// until that moment (review 13/10: moved for the first game only, every other game's save vanished from the game and from
// Game Saves). So the moment is noted, per install and per setting, in <install>\lbip-beside-game.ini: a generic file beside a
// game (.srm, .sav, .cht - other emulators write those names too) last written at or before it is one the emulator was
// reading, and is brought into its folder at that game's launch; one written after it is somebody else's, and stays.
//
// Noted only when the emulator HAD been running with the setting empty - the setting PRESENT and empty in its settings file
// (the emulators write every key they know): a setting filled at a fresh install, before the emulator ever ran, says nothing
// about the files beside the games. The LATEST moment is kept: a setting emptied again in the emulator and set again later
// had it writing beside the games in between too.
//
// NOTED BEFORE the setting is changed (review 13/10): a moment that cannot be written would leave the setting set and every
// other game's save stranded - so the caller changes the setting only when Note says it is written.
//
// A GENERIC file is COPIED, never moved: another emulator may read the same file beside the game (RetroArch saving in its
// content's folder reads <game>.srm there too) - moving it would take its save away. The emulator gets its copy in its own
// folder and never writes beside the game again; the file beside the game is left to whoever else reads it. The emulator's
// OWN files (its states, its own extensions) are moved. Neither ever goes over a file already in the target.
//
// A COPY IS MADE ONCE (review 13/10): the file left beside the game keeps its old date, so without a record it would be
// copied in again whenever the emulator's own is gone - the user deleted it to start over, a switch to an empty named
// branch - and an old save would come back. Each copy is noted in <install>\lbip-beside-game-copied.txt (the file's path,
// size and date); the same file, unchanged, is never copied again.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LbIntegrations.Lbip
{
    internal static class LbipBesideGame
    {
        public const string FileName = "lbip-beside-game.ini";
        public const string CopiedName = "lbip-beside-game-copied.txt";

        /// <summary>Notes that the emulator at <paramref name="installDir"/> wrote beside the games for <paramref name="setting"/>
        /// until now (the latest moment kept). False when it could not be written: the caller must not change the setting then.</summary>
        public static bool Note(string installDir, string setting)
        {
            try
            {
                if (string.IsNullOrEmpty(installDir) || string.IsNullOrEmpty(setting)) return false;
                var file = Path.Combine(installDir, FileName);
                var all = Read(file);
                all[setting] = DateTime.UtcNow;
                var tmp = file + ".tmp";
                File.WriteAllLines(tmp, new[] { "; Nixx integration plugins - when this emulator stopped writing beside the games (UTC). Its files beside a game",
                                                "; from before are brought into its folders at that game's launch; later ones are another emulator's." }
                                        .Concat(all.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value.ToString("o", CultureInfo.InvariantCulture))));
                File.Move(tmp, file, overwrite: true);
                return Read(file).ContainsKey(setting);
            }
            catch { return false; }
        }

        /// <summary>The moment noted for <paramref name="setting"/>, or null when the emulator never wrote beside the games for it.</summary>
        public static DateTime? Since(string installDir, string setting)
        {
            try
            {
                if (string.IsNullOrEmpty(installDir)) return null;
                return Read(Path.Combine(installDir, FileName)).TryGetValue(setting, out var at) ? at : (DateTime?)null;
            }
            catch { return null; }
        }

        /// <summary>A file beside a game the emulator wrote before it stopped writing there (<see cref="Since"/>).</summary>
        public static bool WrittenBefore(string file, DateTime? since)
        {
            try { return since != null && File.Exists(file) && File.GetLastWriteTimeUtc(file) <= since.Value; }
            catch { return false; }
        }

        /// <summary><paramref name="from"/> brought into <paramref name="targetDir"/> under its name: moved, or copied when
        /// <paramref name="copy"/> (a generic file, see the header). Never over a file there, never <paramref name="except"/>
        /// (the file a restore reads from) nor its thumbnail, never into the folder it is in, a copy never twice
        /// (<paramref name="installDir"/>: where the copies are noted; none, a copy is made each time it is missing). True when
        /// brought. Logs through <paramref name="say"/>.</summary>
        public static bool Bring(string from, string targetDir, bool copy, Action<string> say, string except = null, string installDir = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(targetDir) || !File.Exists(from)) return false;
                var full = Path.GetFullPath(from);
                if (except != null)
                {
                    var ex = Path.GetFullPath(except);
                    if (string.Equals(full, ex, StringComparison.OrdinalIgnoreCase) || string.Equals(full, ex + LbipStateThumbs.Suffix, StringComparison.OrdinalIgnoreCase)) return false;
                }
                var dir = Path.GetDirectoryName(full);
                if (string.Equals(Path.GetFullPath(targetDir).TrimEnd('\\'), dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
                var to = Path.Combine(targetDir, Path.GetFileName(from));
                if (File.Exists(to)) { say?.Invoke("saves: " + from + " left where it is - " + to + " is already there"); return false; }
                var stamp = copy ? Stamp(full) : null;
                if (stamp != null && installDir != null && Copied(installDir).Contains(stamp)) return false;   // copied once already
                Directory.CreateDirectory(targetDir);
                if (copy) File.Copy(from, to); else File.Move(from, to);
                if (stamp != null && installDir != null) NoteCopied(installDir, stamp, say);
                say?.Invoke("saves: " + from + (copy ? " copied to " : " -> ") + to + (copy ? " (left beside the game for any other emulator that reads it)" : ""));
                return true;
            }
            catch (Exception ex) { say?.Invoke("saves: " + from + " could not be brought into " + targetDir + ": " + ex.Message); return false; }
        }

        private static string Stamp(string full)
        {
            try { var i = new FileInfo(full); return i.Length + "|" + i.LastWriteTimeUtc.Ticks + "|" + full.ToLowerInvariant(); }
            catch { return null; }
        }

        private static HashSet<string> Copied(string installDir)
        {
            try
            {
                var f = Path.Combine(installDir, CopiedName);
                return File.Exists(f) ? new HashSet<string>(File.ReadAllLines(f).Where(l => l.Length > 0 && !l.StartsWith(";")), StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
            }
            catch { return new HashSet<string>(StringComparer.Ordinal); }
        }

        private static void NoteCopied(string installDir, string stamp, Action<string> say)
        {
            try
            {
                var f = Path.Combine(installDir, CopiedName);
                if (!File.Exists(f)) File.WriteAllText(f, "; Nixx integration plugins - files beside a game already copied into this emulator's folders (size|date|path): never copied again." + Environment.NewLine);
                File.AppendAllText(f, stamp + Environment.NewLine);
            }
            catch (Exception ex) { say?.Invoke("saves: the copy of " + stamp + " could not be noted: " + ex.Message); }
        }

        /// <summary>A folder setting the PACK wrote that no longer points into this install - the install moved, or a portable
        /// LaunchBox got another drive letter (review 13/10): absolute, not under <paramref name="installDir"/>, gone, and in the
        /// pack's own shape - &lt;a folder named as the install's&gt;\&lt;<paramref name="folder"/>&gt;, as "...\Nixx-bsnes\Saves"
        /// (a user's own "N:\Sync\Saves" on a drive not mounted is NOT that, and is never replaced). Set again then.</summary>
        public static bool IsStalePackPath(string value, string folder, string installDir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(installDir)) return false;
                var v = value.Trim().Trim('"', '\'').Replace('/', '\\').TrimEnd('\\');
                if (!Path.IsPathRooted(v)) return false;
                var full = Path.GetFullPath(v);
                if (full.StartsWith(Path.GetFullPath(installDir).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return false;
                var install = Path.GetFileName(Path.GetFullPath(installDir).TrimEnd('\\'));
                return string.Equals(Path.GetFileName(full), folder, StringComparison.OrdinalIgnoreCase)
                       && string.Equals(Path.GetFileName(Path.GetDirectoryName(full) ?? ""), install, StringComparison.OrdinalIgnoreCase)
                       && !Directory.Exists(full);
            }
            catch { return false; }
        }

        private static Dictionary<string, DateTime> Read(string file)
        {
            var d = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            if (!File.Exists(file)) return d;
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (DateTime.TryParse(line.Substring(eq + 1).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at))
                    d[line.Substring(0, eq).Trim()] = at;
            }
            return d;
        }
    }
}
