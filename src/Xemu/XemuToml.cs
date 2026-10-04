// xemu.toml, the few keys this plugin sets - by line, not by a TOML library: the file is xemu's (toml++ writes it whole on
// exit), so only the lines named are touched and everything else is left as xemu wrote it.
//
// THE SHAPE, as Unbroken's own plugin writes it and as xemu reads it (config_spec.yml): a table per section, dotted -
// [general] show_welcome, [general.updates] check, [sys.files] bootrom_path / flashrom_path / eeprom_path / hdd_path.
// Strings as TOML literal strings ('C:\path'): no escaping, a backslash is a backslash. A path holding a ' cannot be one -
// refused rather than written broken.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Xemu
{
    internal static class XemuToml
    {
        public static string Literal(string path)
            => path.IndexOf('\'') >= 0 ? throw new ArgumentException("a path with a ' cannot go into xemu.toml: " + path) : "'" + path + "'";

        /// <summary>Set <paramref name="key"/> = <paramref name="value"/> (already TOML: 'text', true, 4) in [<paramref name="table"/>],
        /// the table appended when there is none. <paramref name="onlyIfAbsent"/>: a value already there - the user's - stays.
        /// True when the file changed.</summary>
        public static bool Set(string file, string table, string key, string value, bool onlyIfAbsent = false)
        {
            var lines = File.Exists(file) ? File.ReadAllLines(file).ToList() : new List<string>();
            int head = lines.FindIndex(l => l.Trim() == "[" + table + "]");
            if (head < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add("");
                lines.Add("[" + table + "]");
                lines.Add(key + " = " + value);
                Write(file, lines);
                return true;
            }
            int end = head + 1;
            while (end < lines.Count && !lines[end].TrimStart().StartsWith("[")) end++;
            for (int i = head + 1; i < end; i++)
            {
                if (KeyOf(lines[i]) != key) continue;
                if (onlyIfAbsent) return false;
                var line = key + " = " + value;
                if (lines[i].Trim() == line) return false;
                lines[i] = line;
                Write(file, lines);
                return true;
            }
            // After the table's last key, before the blank lines that close it.
            int at = end;
            while (at > head + 1 && lines[at - 1].Trim().Length == 0) at--;
            lines.Insert(at, key + " = " + value);
            Write(file, lines);
            return true;
        }

        /// <summary>The raw value of <paramref name="key"/> in [<paramref name="table"/>], or null.</summary>
        public static string Get(string file, string table, string key)
        {
            if (!File.Exists(file)) return null;
            var lines = File.ReadAllLines(file);
            int head = Array.FindIndex(lines, l => l.Trim() == "[" + table + "]");
            if (head < 0) return null;
            for (int i = head + 1; i < lines.Length && !lines[i].TrimStart().StartsWith("["); i++)
                if (KeyOf(lines[i]) == key) { var eq = lines[i].IndexOf('='); return lines[i].Substring(eq + 1).Trim(); }
            return null;
        }

        /// <summary>A literal or basic string's text, quotes taken off.</summary>
        public static string Text(string raw)
        {
            if (raw == null) return null;
            raw = raw.Trim();
            if (raw.Length >= 2 && (raw[0] == '\'' || raw[0] == '"') && raw[raw.Length - 1] == raw[0]) return raw.Substring(1, raw.Length - 2);
            return raw;
        }

        private static string KeyOf(string line)
        {
            var t = line.Trim();
            if (t.Length == 0 || t[0] == '#') return null;
            int eq = t.IndexOf('=');
            return eq <= 0 ? null : t.Substring(0, eq).Trim();
        }

        private static void Write(string file, List<string> lines)
        {
            var tmp = file + ".lbip-tmp";
            File.WriteAllText(tmp, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
            File.Move(tmp, file, overwrite: true);
        }
    }
}
