// snes9x.conf, edited in place.
//
// THE FILE (measured 06/10 on RASnes9x 1.2): beside the executable (portable), ASCII, CRLF, no byte-order mark;
// "[Display\Win]" sections, then aligned "    Key:Name   = VALUE   # comment" lines - the key may hold a ':' and the
// value ends at the '#' of its comment. Snes9x WRITES IT BACK WHOLE WHEN IT QUITS (wconfig.cpp, S9xSaveConfigFile),
// from what it has in memory - so an edit made while it runs is lost, and what the plugin writes before a launch is
// what the game starts with. A missing file is made with only the plugin's keys: Snes9x fills in the rest with its
// defaults and writes them out at its first exit.
//
// NOTHING IS REORDERED AND NOTHING IS DROPPED: a key already present gets its new value where it stands, its
// indentation, its alignment and its comment kept; a new one goes at the end of its section.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LbIntegrations.RaSnes9x
{
    internal sealed class RaSnes9xConf
    {
        public const string FileName = "snes9x.conf";

        private readonly List<string> _lines;
        public string Path { get; }
        public bool Changed { get; private set; }

        private RaSnes9xConf(string path, List<string> lines) { Path = path; _lines = lines; }

        public static string Of(string exe) => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(exe)), FileName);

        public static RaSnes9xConf Load(string path)
        {
            var lines = new List<string>();
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path, Encoding.UTF8);
                lines.AddRange(text.Replace("\r\n", "\n").Split('\n'));
                if (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            }
            return new RaSnes9xConf(path, lines);
        }

        /// <summary>The value of <paramref name="key"/> in <paramref name="section"/>, its comment off - or null.</summary>
        public string Get(string section, string key)
        {
            int at = Find(section, key, out _);
            return at < 0 ? null : ValueOf(_lines[at]);
        }

        public void Set(string section, string key, string value)
        {
            value = value ?? "";
            int at = Find(section, key, out int sectionEnd);
            if (at >= 0)
            {
                var line = _lines[at];
                int eq = line.IndexOf('=');
                var rest = line.Substring(eq + 1);
                int hash = rest.IndexOf('#');
                if (string.Equals((hash < 0 ? rest : rest.Substring(0, hash)).Trim(), value, StringComparison.Ordinal)) return;
                var fresh = " " + value;
                // The comment stays at its column when the value fits before it.
                if (hash >= 0) fresh = (fresh.Length < hash ? fresh.PadRight(hash) : fresh + " ") + rest.Substring(hash);
                _lines[at] = line.Substring(0, eq + 1) + fresh;
            }
            else
            {
                var line = key + " = " + value;
                if (sectionEnd >= 0) _lines.Insert(sectionEnd, line);
                else
                {
                    if (_lines.Count > 0 && _lines[_lines.Count - 1].Trim().Length > 0) _lines.Add("");
                    _lines.Add("[" + section + "]");
                    _lines.Add(line);
                }
            }
            Changed = true;
        }

        public void SetBool(string section, string key, bool on) => Set(section, key, on ? "TRUE" : "FALSE");

        public bool? GetBool(string section, string key)
        {
            var v = Get(section, key)?.Trim().ToUpperInvariant();
            return v == "TRUE" || v == "ON" || v == "1" ? true : v == "FALSE" || v == "OFF" || v == "0" ? false : (bool?)null;
        }

        /// <summary>Written only when something changed: ASCII-safe UTF-8 without a BOM, CRLF, as Snes9x's own.</summary>
        public void SaveIfChanged()
        {
            if (!Changed) return;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            var tmp = Path + ".lbip-tmp";
            File.WriteAllText(tmp, string.Join("\r\n", _lines) + "\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tmp, Path, overwrite: true);
            Changed = false;
        }

        /// <summary>The value of a "key = value  # comment" line, trimmed, its comment off - a quoted value (Snes9x quotes one
        /// that holds a '#') taken whole and unquoted.</summary>
        private static string ValueOf(string line)
        {
            int eq = line.IndexOf('=');
            if (eq < 0) return "";
            var rest = line.Substring(eq + 1).TrimStart();
            if (rest.StartsWith("\""))
            {
                int close = rest.IndexOf('"', 1);
                if (close > 0) return rest.Substring(1, close - 1);
            }
            int hash = rest.IndexOf('#');
            return (hash < 0 ? rest : rest.Substring(0, hash)).Trim();
        }
        /// <summary>The line of <paramref name="key"/> in <paramref name="section"/>, or -1; and where a new key of that
        /// section would go (after its last non-blank line), or -1 with no such section.</summary>
        private int Find(string section, string key, out int sectionEnd)
        {
            sectionEnd = -1;
            bool inside = false;
            int lastContent = -1;
            for (int i = 0; i < _lines.Count; i++)
            {
                var t = _lines[i].Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                {
                    if (inside) break;
                    inside = string.Equals(t.Substring(1, t.Length - 2).Trim(), section, StringComparison.OrdinalIgnoreCase);
                    if (inside) lastContent = i;
                    continue;
                }
                if (!inside) continue;
                if (t.Length > 0) lastContent = i;
                int eq = t.IndexOf('=');
                if (eq > 0 && string.Equals(t.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    sectionEnd = lastContent + 1;
                    return i;
                }
            }
            if (lastContent >= 0) sectionEnd = lastContent + 1;
            return -1;
        }
    }
}
