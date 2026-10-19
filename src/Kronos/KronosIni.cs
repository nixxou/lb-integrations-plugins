// kronos.ini as Kronos's Qt build writes it (QSettings, IniFormat) - edited line by line, every line the plugin does not
// change kept as it is (Kronos's own "@ByteArray(...)" geometry, the keys of a newer Kronos).
//
// THE FORMAT (docs\kronos.md §3, measured 19/10): every setting sits in ONE section, [1.0] (Settings.cpp: beginGroup("1.0")),
// its groups joined by a backslash - "General\Bios=...". A key written at the root, or in [General], is never read.
// Values are Qt's escaped strings (qsettings.cpp iniEscapedString / iniUnescapedStringList):
//   - a backslash and a double quote escaped (\\ \"), a control character or one above 0x7E as \x<hex> - an accented path
//     included - and a hex digit right after such an escape escaped too, so it is not read as part of it;
//   - the whole value in double quotes when it holds ',' ';' '=' or starts / ends with a space (a ',' outside quotes
//     makes a LIST in Qt).
// Kronos rewrites the whole file when it quits (QSettings::sync): what it read is written back as it read it.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Kronos
{
    internal sealed class KronosIniDoc
    {
        public const string Section = "1.0";
        private readonly List<string> _lines;

        private KronosIniDoc(List<string> lines) { _lines = lines; }

        public static KronosIniDoc Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : Parse("");

        public static KronosIniDoc Parse(string text)
            => new KronosIniDoc((text ?? "").Replace("\r\n", "\n").Split('\n').ToList());

        public void Save(string path)
        {
            var lines = _lines.ToList();
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
        }

        public string Text => string.Join("\r\n", _lines);

        /// <summary>The lines of [1.0]: first and past-the-end indexes; -1 when there is none.</summary>
        private (int Start, int End) Range()
        {
            int start = -1;
            for (int i = 0; i < _lines.Count; i++)
            {
                var t = _lines[i].Trim();
                if (!t.StartsWith("[") || !t.EndsWith("]")) continue;
                if (start >= 0) return (start, i);
                if (t.Substring(1, t.Length - 2).Trim() == Section) start = i + 1;
            }
            return (start, start < 0 ? -1 : _lines.Count);
        }

        private static string KeyOf(string line)
        {
            var at = line.IndexOf('=');
            if (at <= 0) return null;
            var t = line.TrimStart();
            if (t.StartsWith(";") || t.StartsWith("#")) return null;
            return line.Substring(0, at).Trim();
        }

        private int Find(string key)
        {
            var (start, end) = Range();
            if (start < 0) return -1;
            for (int i = start; i < end; i++)
                if (string.Equals(KeyOf(_lines[i]), KeyText(key), StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>The raw value of <paramref name="key"/> ("General\Bios"), as written; null when absent.</summary>
        public string GetRaw(string key)
        {
            var i = Find(key);
            if (i < 0) return null;
            var line = _lines[i];
            return line.Substring(line.IndexOf('=') + 1).Trim();
        }

        /// <summary>The value of <paramref name="key"/> unescaped; null when absent.</summary>
        public string Get(string key) => GetRaw(key) is string raw ? Unescape(raw) : null;

        /// <summary>The raw value written (null: the key taken out). True when the file changed.</summary>
        public bool SetRaw(string key, string raw)
        {
            var i = Find(key);
            if (raw == null)
            {
                if (i < 0) return false;
                _lines.RemoveAt(i);
                return true;
            }
            var line = KeyText(key) + "=" + raw;
            if (i >= 0)
            {
                if (_lines[i] == line) return false;
                _lines[i] = line;
                return true;
            }
            var (start, end) = Range();
            if (start < 0)
            {
                while (_lines.Count > 0 && _lines[_lines.Count - 1].Length == 0) _lines.RemoveAt(_lines.Count - 1);
                if (_lines.Count > 0) _lines.Add("");
                _lines.Add("[" + Section + "]");
                _lines.Add(line);
                return true;
            }
            int at = end;
            while (at > start && _lines[at - 1].Trim().Length == 0) at--;
            _lines.Insert(at, line);
            return true;
        }

        /// <summary>A text value written as Qt writes it.</summary>
        public bool Set(string key, string value) => SetRaw(key, value == null ? null : Escape(value));

        /// <summary>A key as it stands in the file: Qt writes a space in a key as %20 (iniEscapedKey).</summary>
        private static string KeyText(string key) => (key ?? "").Replace("/", "\\").Replace(" ", "%20");

        // ── Qt's escaping ─────────────────────────────────────────────────────

        private static bool IsHex(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        /// <summary>A text as Qt writes it in an ini (iniEscapedString).</summary>
        public static string Escape(string s)
        {
            if (s == null) return null;
            var b = new StringBuilder();
            bool quote = s.Length > 0 && (s[0] == ' ' || s[s.Length - 1] == ' ');
            bool escapeNextIfHex = false;
            foreach (var c in s)
            {
                if (c == ';' || c == ',' || c == '=') quote = true;
                if (escapeNextIfHex && IsHex(c)) { b.Append("\\x").Append(((int)c).ToString("x")); continue; }
                escapeNextIfHex = false;
                switch (c)
                {
                    case '\0': b.Append("\\0"); escapeNextIfHex = true; break;
                    case '\a': b.Append("\\a"); break;
                    case '\b': b.Append("\\b"); break;
                    case '\f': b.Append("\\f"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    case '\v': b.Append("\\v"); break;
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    default:
                        if (c <= 0x1F || c >= 0x7F) { b.Append("\\x").Append(((int)c).ToString("x")); escapeNextIfHex = true; }
                        else b.Append(c);
                        break;
                }
            }
            return quote ? "\"" + b + "\"" : b.ToString();
        }

        /// <summary>A value as Qt reads it back (iniUnescapedStringList) - a list (a ',' outside quotes) joined by ", ".</summary>
        public static string Unescape(string raw)
        {
            if (raw == null) return null;
            var items = new List<string>();
            var b = new StringBuilder();
            bool inQuotes = false;
            int i = 0;
            while (i < raw.Length)
            {
                char c = raw[i];
                if (c == '"') { inQuotes = !inQuotes; i++; continue; }
                if (c == ',' && !inQuotes) { items.Add(b.ToString().Trim()); b.Clear(); i++; continue; }
                if (c != '\\' || i + 1 >= raw.Length) { b.Append(c); i++; continue; }
                char e = raw[i + 1];
                i += 2;
                switch (e)
                {
                    case 'a': b.Append('\a'); break;
                    case 'b': b.Append('\b'); break;
                    case 'f': b.Append('\f'); break;
                    case 'n': b.Append('\n'); break;
                    case 'r': b.Append('\r'); break;
                    case 't': b.Append('\t'); break;
                    case 'v': b.Append('\v'); break;
                    case 'x':
                        {
                            int start = i;
                            while (i < raw.Length && IsHex(raw[i])) i++;
                            if (i > start && int.TryParse(raw.Substring(start, i - start), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                                b.Append((char)code);
                            break;
                        }
                    default:
                        if (e >= '0' && e <= '7')
                        {
                            int code = e - '0';
                            while (i < raw.Length && raw[i] >= '0' && raw[i] <= '7') code = code * 8 + (raw[i++] - '0');
                            b.Append((char)code);
                        }
                        else b.Append(e);   // \\ \" \' \? and the rest
                        break;
                }
            }
            items.Add(inQuotes ? b.ToString() : b.ToString().Trim());
            return string.Join(", ", items);
        }

        /// <summary>A Qt boolean: "true" / "false".</summary>
        public static string Bool(bool v) => v ? "true" : "false";

        /// <summary>A path as Kronos writes it: forward slashes.</summary>
        public static string PathText(string path) => (path ?? "").Replace('\\', '/');
    }
}
