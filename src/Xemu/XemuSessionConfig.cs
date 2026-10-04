// A game's xemu.toml for its session (Mehdi, 04/10: "ça m'a l'air propre"): <xemu>\xemu-session.toml, the user's xemu.toml with
// the game's options and the plugin's own keys (its console, its EEPROM, its disc) set over it, given to xemu by -config_path.
// The user's file is not written while the game runs.
//
// BACK AFTER THE SESSION, BY TABLE (Mehdi, 04/10: "les sections qu'on n'utilise pas, ce serait bien de les rapatrier dans
// leur globalité"). xemu writes its whole configuration on exit - what the user changed in its windows meanwhile (a pad
// bound, a key mapped) is in the session's file. So:
//   a table the session set nothing in ([input], [input.bindings], [net]...)   comes back WHOLE, as xemu left it
//   a table the session set keys in ([display], [sys.files]...)                is the USER'S, whole, as it was before the game:
//                                                                               nothing changed in it during the session is kept
//                                                                               (Mehdi, 04/10: "le plus sûr ce serait de pas accepter
//                                                                               de modif sur les sections qu'on altère") - and gone
//                                                                               when the user had none
//   a table that appeared during the session                                   added
// and the user's file is written once, through a temporary file, only when that changed anything. The keys the session set
// are listed beside it (xemu-session.keys) - so a session left behind (the host killed mid-game) is merged back at the next
// launch or end the same way.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Xemu
{
    internal static class XemuSessionConfig
    {
        public static string SessionPath(string exe) => XemuPaths.Dir(exe) is string d ? Path.Combine(d, "xemu-session.toml") : null;
        private static string KeysPath(string session) => Path.ChangeExtension(session, ".keys");

        /// <summary>The session's file: the user's with <paramref name="set"/> (table, key, TOML value) over it, and
        /// <paramref name="keepUsers"/> - keys xemu may write in the session (the disc it was given) that must come back as the
        /// user's. Returns the session file's path.</summary>
        public static string Make(string userToml, string sessionToml, IEnumerable<(string Table, string Key, string Value)> set, IEnumerable<(string Table, string Key)> keepUsers = null)
        {
            var doc = XemuTomlDoc.Load(userToml);
            var touched = new List<(string, string)>();
            foreach (var (table, key, value) in set)
            {
                doc.Set(table, key, value);
                touched.Add((table, key));
            }
            foreach (var k in keepUsers ?? Enumerable.Empty<(string, string)>()) touched.Add(k);
            doc.Save(sessionToml);
            var keys = touched.Distinct().Select(k => k.Item1 + "\t" + k.Item2);
            var tmp = KeysPath(sessionToml) + ".lbip-tmp";
            File.WriteAllLines(tmp, keys, new UTF8Encoding(false));
            File.Move(tmp, KeysPath(sessionToml), overwrite: true);
            return sessionToml;
        }

        /// <summary>The user's xemu.toml from the session's, by the rule above - then the session's files removed. Nothing when
        /// there is no session left. True when the user's file changed.</summary>
        public static bool MergeBack(string userToml, string sessionToml)
        {
            if (sessionToml == null || !File.Exists(sessionToml)) return false;
            var keysFile = KeysPath(sessionToml);
            if (!File.Exists(keysFile)) { Discard(sessionToml); return false; }   // half made: nothing of it is the user's
            var touched = File.ReadAllLines(keysFile).Select(l => l.Split('\t')).Where(p => p.Length == 2).Select(p => (Table: p[0], Key: p[1])).ToList();
            var user = XemuTomlDoc.Load(userToml);
            var merged = Merge(user, XemuTomlDoc.Load(sessionToml), touched);
            bool changed = merged.Render() != user.Render();
            if (changed) merged.Save(userToml);
            Discard(sessionToml);
            return changed;
        }

        /// <summary>The rule, on documents: the session's tables in its order - an untouched one as the session has it, a touched
        /// one as the user has it (or none) - then the user's tables the session no longer has.</summary>
        internal static XemuTomlDoc Merge(XemuTomlDoc user, XemuTomlDoc session, IReadOnlyCollection<(string Table, string Key)> touched)
        {
            var touchedTables = new HashSet<string>(touched.Select(t => t.Table));
            var result = new XemuTomlDoc();
            XemuTomlDoc.Table Copy(XemuTomlDoc.Table t) => new XemuTomlDoc.Table { Name = t.Name, Lines = new List<string>(t.Lines) };
            // The lines before the first table: the user's (xemu writes none).
            result.Tables.Add(user.Find("") is XemuTomlDoc.Table pre ? Copy(pre) : new XemuTomlDoc.Table { Name = "" });
            foreach (var t in session.Tables.Where(t => t.Name.Length > 0))
            {
                if (!touchedTables.Contains(t.Name)) result.Tables.Add(Copy(t));
                else if (user.Find(t.Name) is XemuTomlDoc.Table mine) result.Tables.Add(Copy(mine));
            }
            foreach (var t in user.Tables)
                if (t.Name.Length > 0 && result.Find(t.Name) == null) result.Tables.Add(Copy(t));
            // Each table but the last ends with one blank line - a user's table moved last keeps reading as before.
            for (int i = 0; i < result.Tables.Count - 1; i++)
            {
                var lines = result.Tables[i].Lines;
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add("");
            }
            return result;
        }

        private static void Discard(string sessionToml)
        {
            try { File.Delete(sessionToml); } catch { }
            try { File.Delete(KeysPath(sessionToml)); } catch { }
        }
    }
}
