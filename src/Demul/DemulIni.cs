// Demul's .ini files, edited in place.
//
// The files Demul ships (gpuDX11.ini, padDemul.ini, spuDemul.ini...) are UTF-8 WITH a byte-order mark,
// CRLF, "[section]" then "key = value" - read off demul_251220.7z. Demul.ini itself is not in the
// archive: the emulator writes it, and Batocera's launcher (batocera_launch/emulators/demul.py) writes
// it from nothing when it is missing, in the same shape and encoding, and Demul starts from that.
//
// NOTHING IS REORDERED AND NOTHING IS DROPPED: a key already present is rewritten where it stands, a
// new one goes at the end of its section, and every other line survives as it was.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LbIntegrations.Demul
{
    internal sealed class DemulIni
    {
        private readonly List<string> _lines;
        public string Path { get; }
        public bool Changed { get; private set; }

        private DemulIni(string path, List<string> lines) { Path = path; _lines = lines; }

        public static DemulIni Load(string path)
        {
            var lines = new List<string>();
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path, Encoding.UTF8);   // drops a BOM
                lines.AddRange(text.Replace("\r\n", "\n").Split('\n'));
                if (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            }
            return new DemulIni(path, lines);
        }

        public string Get(string section, string key)
        {
            int at = Find(section, key, out _);
            return at < 0 ? null : ValueOf(_lines[at]);
        }

        public void Set(string section, string key, string value)
        {
            int at = Find(section, key, out int sectionEnd);
            var line = key + " = " + (value ?? "");
            if (at >= 0)
            {
                if (string.Equals(ValueOf(_lines[at]), value ?? "", StringComparison.Ordinal)) return;
                _lines[at] = line;
            }
            else if (sectionEnd >= 0)
                _lines.Insert(sectionEnd, line);
            else
            {
                if (_lines.Count > 0 && _lines[_lines.Count - 1].Trim().Length > 0) _lines.Add("");
                _lines.Add("[" + section + "]");
                _lines.Add(line);
            }
            Changed = true;
        }

        /// <summary>Every "key = value" line, with its section - in file order.</summary>
        public IEnumerable<(string Section, string Key, string Value)> Entries()
        {
            string section = "";
            foreach (var line in _lines)
            {
                var t = line.Trim();
                if (t.StartsWith("[") && t.EndsWith("]")) { section = t.Substring(1, t.Length - 2).Trim(); continue; }
                int eq = t.IndexOf('=');
                if (eq > 0) yield return (section, t.Substring(0, eq).Trim(), ValueOf(t));
            }
        }

        public void Remove(string section, string key)
        {
            int at = Find(section, key, out _);
            if (at < 0) return;
            _lines.RemoveAt(at);
            Changed = true;
        }

        /// <summary>Written only when something changed: UTF-8 with its BOM, CRLF, as Demul's own.</summary>
        public void SaveIfChanged()
        {
            if (!Changed) return;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            File.WriteAllText(Path, string.Join("\r\n", _lines) + "\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            Changed = false;
        }

        /// <summary>The line of <paramref name="key"/> in <paramref name="section"/>, or -1; and where a
        /// new key of that section would go (after its last non-blank line), or -1 with no such section.</summary>
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

        private static string ValueOf(string line)
        {
            int eq = line.IndexOf('=');
            return eq < 0 ? "" : line.Substring(eq + 1).Trim();
        }
    }
}
