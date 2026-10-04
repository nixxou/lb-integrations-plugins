// xemu.toml, read and written by its lines - not through a TOML library: the file is xemu's (toml++ writes it whole on exit),
// so only what is asked is touched and every other line stays as xemu wrote it.
//
// THE SHAPE (config_spec.yml, xemu's own list of its settings): tables, dotted - [general], [general.updates], [input.bindings],
// [display.window], [sys.files] - each running from its header to the next. Inside one, "key = value" lines; a value may run
// over several lines (gamepad_mappings = [ ... { gamepad_id = '...' } ... ]), so a line is a key's only when it starts one at
// the table's level - brackets and braces counted, strings skipped. A header is a line "[name]" of letters, digits, '_', '-'
// and '.' only: an array's line ("[1, 2]", "{ ... }") never is one.
// Strings as TOML literal strings ('C:\path'): no escaping, a backslash is a backslash. A path holding a ' cannot be one -
// refused rather than written broken.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LbIntegrations.Xemu
{
    /// <summary>A TOML file as its tables, each with its lines - enough to read, set, take and put back keys and whole tables.</summary>
    internal sealed class XemuTomlDoc
    {
        private static readonly Regex Header = new Regex(@"^\s*\[\s*([A-Za-z0-9_\-]+(?:\.[A-Za-z0-9_\-]+)*)\s*\]\s*(#.*)?$");

        public sealed class Table
        {
            public string Name;                  // "" for the lines before the first header
            public List<string> Lines = new List<string>();   // the header first (none for "")
        }

        public readonly List<Table> Tables = new List<Table>();

        public static XemuTomlDoc Parse(string text)
        {
            var doc = new XemuTomlDoc();
            var current = new Table { Name = "" };
            doc.Tables.Add(current);
            int depth = 0;                       // inside a multi-line value: brackets and braces still open
            foreach (var line in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                if (depth == 0 && Header.Match(line) is Match m && m.Success)
                {
                    current = new Table { Name = m.Groups[1].Value };
                    current.Lines.Add(line);
                    doc.Tables.Add(current);
                    continue;
                }
                current.Lines.Add(line);
                depth = Math.Max(0, depth + Balance(line));
            }
            // The text's last newline makes one empty line more: dropped, Render puts it back.
            var last = doc.Tables[doc.Tables.Count - 1];
            if (last.Lines.Count > 0 && last.Lines[last.Lines.Count - 1].Length == 0) last.Lines.RemoveAt(last.Lines.Count - 1);
            return doc;
        }

        public static XemuTomlDoc Load(string file) => Parse(File.Exists(file) ? File.ReadAllText(file) : "");

        public string Render()
        {
            var lines = new List<string>();
            foreach (var t in Tables) lines.AddRange(t.Lines);
            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
            return lines.Count == 0 ? "" : string.Join("\n", lines) + "\n";
        }

        public Table Find(string name) => Tables.FirstOrDefault(t => t.Name == name);

        /// <summary>The keys of a table: name, first line, last line (a value over several lines), raw value text.</summary>
        public static List<(string Key, int First, int Last, string Value)> Keys(Table t)
        {
            var keys = new List<(string, int, int, string)>();
            int i = t.Name.Length == 0 ? 0 : 1;
            while (i < t.Lines.Count)
            {
                var line = t.Lines[i];
                var key = KeyOf(line);
                int first = i, depth = Math.Max(0, Balance(line));
                while (depth > 0 && i + 1 < t.Lines.Count) { i++; depth = Math.Max(0, depth + Balance(t.Lines[i])); }
                if (key != null)
                {
                    var text = string.Join("\n", t.Lines.Skip(first).Take(i - first + 1));
                    keys.Add((key, first, i, text.Substring(text.IndexOf('=') + 1).Trim()));
                }
                i++;
            }
            return keys;
        }

        public string Get(string table, string key)
        {
            var t = Find(table);
            return t == null ? null : Keys(t).Where(k => k.Key == key).Select(k => k.Value).FirstOrDefault();
        }

        /// <summary>The lines of <paramref name="key"/> in <paramref name="table"/> (one, or several for a value over lines), or null.</summary>
        public List<string> LinesOf(string table, string key)
        {
            var t = Find(table);
            if (t == null) return null;
            foreach (var k in Keys(t)) if (k.Key == key) return t.Lines.Skip(k.First).Take(k.Last - k.First + 1).ToList();
            return null;
        }

        /// <summary><paramref name="key"/> given <paramref name="lines"/> (null: removed) in <paramref name="table"/> - the table appended
        /// when there is none, the key after the table's last key when it has none. True when anything changed.</summary>
        public bool Put(string table, string key, List<string> lines)
        {
            var t = Find(table);
            if (t == null)
            {
                if (lines == null) return false;
                var prev = Tables[Tables.Count - 1];
                if (prev.Lines.Count > 0 && prev.Lines[prev.Lines.Count - 1].Trim().Length > 0) prev.Lines.Add("");
                t = new Table { Name = table };
                t.Lines.Add("[" + table + "]");
                t.Lines.AddRange(lines);
                Tables.Add(t);
                return true;
            }
            var keys = Keys(t);
            var hit = keys.FirstOrDefault(k => k.Key == key);
            if (hit.Key != null)
            {
                var old = t.Lines.Skip(hit.First).Take(hit.Last - hit.First + 1).ToList();
                if (lines != null && old.SequenceEqual(lines)) return false;
                t.Lines.RemoveRange(hit.First, hit.Last - hit.First + 1);
                if (lines != null) t.Lines.InsertRange(hit.First, lines);
                return true;
            }
            if (lines == null) return false;
            // After the table's last key, before the blank lines that close it.
            int at = keys.Count > 0 ? keys[keys.Count - 1].Last + 1 : (t.Name.Length == 0 ? 0 : 1);
            t.Lines.InsertRange(at, lines);
            return true;
        }

        public bool Set(string table, string key, string value) => Put(table, key, new List<string> { key + " = " + value });

        public static string KeyOf(string line)
        {
            var s = line.Trim();
            if (s.Length == 0 || s[0] == '#' || s[0] == '[' || s[0] == '{' || s[0] == ']' || s[0] == '}') return null;
            int eq = s.IndexOf('=');
            if (eq <= 0) return null;
            var k = s.Substring(0, eq).Trim();
            return Regex.IsMatch(k, @"^[A-Za-z0-9_\-]+$") ? k : null;
        }

        /// <summary>Brackets and braces opened minus closed on a line, outside its strings and its comment.</summary>
        internal static int Balance(string line)
        {
            int n = 0; char quote = '\0';
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quote != '\0')
                {
                    if (c == '\\' && quote == '"') { i++; continue; }
                    if (c == quote) quote = '\0';
                    continue;
                }
                if (c == '\'' || c == '"') quote = c;
                else if (c == '#') break;
                else if (c == '[' || c == '{') n++;
                else if (c == ']' || c == '}') n--;
            }
            return n;
        }

        public void Save(string file)
        {
            var tmp = file + ".lbip-tmp";
            File.WriteAllText(tmp, Render(), new UTF8Encoding(false));
            File.Move(tmp, file, overwrite: true);
        }
    }

    internal static class XemuToml
    {
        public static string Literal(string path)
            => path.IndexOf('\'') >= 0 ? throw new ArgumentException("a path with a ' cannot go into xemu.toml: " + path) : "'" + path + "'";

        /// <summary>Set <paramref name="key"/> = <paramref name="value"/> (already TOML: 'text', true, 4) in [<paramref name="table"/>],
        /// the table appended when there is none. <paramref name="onlyIfAbsent"/>: a value already there - the user's - stays.
        /// True when the file changed.</summary>
        public static bool Set(string file, string table, string key, string value, bool onlyIfAbsent = false)
        {
            var doc = XemuTomlDoc.Load(file);
            if (onlyIfAbsent && doc.Get(table, key) != null) return false;
            if (!doc.Set(table, key, value)) return false;
            doc.Save(file);
            return true;
        }

        /// <summary>The raw value of <paramref name="key"/> in [<paramref name="table"/>], or null.</summary>
        public static string Get(string file, string table, string key) => File.Exists(file) ? XemuTomlDoc.Load(file).Get(table, key) : null;

        /// <summary>A literal or basic string's text, quotes taken off.</summary>
        public static string Text(string raw)
        {
            if (raw == null) return null;
            raw = raw.Trim();
            if (raw.Length >= 2 && (raw[0] == '\'' || raw[0] == '"') && raw[raw.Length - 1] == raw[0]) return raw.Substring(1, raw.Length - 2);
            return raw;
        }
    }
}
