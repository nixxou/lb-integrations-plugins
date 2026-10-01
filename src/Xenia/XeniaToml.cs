// Xenia's config file, xenia-canary.config.toml, read for what it says and written in ONE place: the profile to sign
// into at start (XeniaProfile.SignInAtStart). Every option of this pack goes on the command line, never in here.
//
// The file is the one Xenia writes itself (config.cc SaveConfig): "[Section]" headers, then one "name = value"
// per line, a "#" comment after it, multi-line comments as lines holding only a comment. Every cvar name is unique
// across sections, so a key is looked up on its own. Xenia READS this file at start, then rewrites it whole at
// start and at exit from its cvars - so a value written while it is closed is what it starts on, and keeps; one
// written while it runs is lost at its exit (the callers check).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Xenia
{
    internal static class XeniaToml
    {
        /// <summary>Every "name = value" of the file, the value unquoted - empty when there is no file.</summary>
        public static Dictionary<string, string> ReadAll(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return values;
                foreach (var line in File.ReadAllLines(path))
                    if (Split(line, out var key, out var value, out _)) values[key] = Unquote(value);
            }
            catch (Exception ex) { Log.Warn("could not read " + path, ex); }
            return values;
        }

        public static string Read(string path, string key)
            => ReadAll(path).TryGetValue(key, out var v) ? v : null;

        /// <summary>One value set, the rest of the file as it was: the line replaced where the key is, else added under
        /// its section - the section added at the end when there is none, the file created when there is none.
        /// <paramref name="raw"/> is written as is: quote a string.</summary>
        public static void Write(string path, string section, string key, string raw)
        {
            string text = File.Exists(path) ? File.ReadAllText(path) : "";
            string nl = text.Contains("\r\n") ? "\r\n" : text.Length > 0 && text.Contains("\n") ? "\n" : "\r\n";
            var lines = text.Length == 0 ? new List<string>() : text.Replace("\r\n", "\n").Split('\n').ToList();
            if (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);

            bool done = false;
            for (int i = 0; i < lines.Count && !done; i++)
            {
                if (!Split(lines[i], out var k, out _, out var comment) || !string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) continue;
                lines[i] = Line(key, raw, comment);
                done = true;
            }
            if (!done)
            {
                int header = lines.FindIndex(l => l.Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase));
                if (header < 0)
                {
                    if (lines.Count > 0) lines.Add("");
                    lines.Add("[" + section + "]");
                    lines.Add(Line(key, raw, null));
                }
                else lines.Insert(header + 1, Line(key, raw, null));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, string.Join(nl, lines) + nl, new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
            Log.Info("config: " + key + " = " + raw + " -> " + path);
        }

        /// <summary>The line as Xenia lays it out: the value padded to column 50, then its comment.</summary>
        private static string Line(string key, string raw, string comment)
        {
            var s = key + " = " + raw;
            return comment == null ? s : s.PadRight(50) + "\t" + comment;
        }

        /// <summary>"name = value  # comment" taken apart - false for a header, a comment or a blank line. A "#" inside
        /// a quoted string is the string's.</summary>
        internal static bool Split(string line, out string key, out string value, out string comment)
        {
            key = value = comment = null;
            var t = line.TrimStart();
            if (t.Length == 0 || t[0] == '#' || t[0] == '[') return false;
            int eq = t.IndexOf('=');
            if (eq <= 0) return false;
            key = t.Substring(0, eq).Trim();
            var rest = t.Substring(eq + 1);
            bool quoted = false;
            int hash = -1;
            for (int i = 0; i < rest.Length; i++)
            {
                if (rest[i] == '"' && (i == 0 || rest[i - 1] != '\\')) quoted = !quoted;
                else if (rest[i] == '#' && !quoted) { hash = i; break; }
            }
            value = (hash >= 0 ? rest.Substring(0, hash) : rest).Trim();
            if (hash >= 0) comment = rest.Substring(hash).Trim();
            return key.Length > 0;
        }

        private static string Unquote(string v)
            => v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"' ? v.Substring(1, v.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\") : v;
    }
}
