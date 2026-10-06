// bsnes's settings.bml, edited in place.
//
// THE FILE (read 06/10 off the nightly's own): beside bsnes.exe - bsnes's locate() takes the program's folder first,
// so the install is portable - UTF-8 without a BOM, LF, two spaces of indent per level; a node is "Name: value" or
// "Name" alone (no value). A setting's path is its names joined by '/': "Video/Output", "Emulator/Hack/PPU/Mode7/Scale".
// bsnes WRITES IT BACK WHOLE WHEN IT QUITS (settings.save() at the end of main), so an edit while it runs is lost and
// what is written before a launch is what the game starts with. A missing file is made with the plugin's nodes alone:
// bsnes fills in the rest with its defaults.
//
// NOTHING IS REORDERED AND NOTHING IS DROPPED: a node present gets its value where it stands; a new one goes after
// the last line of its parent, its parents made first when they are missing.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LbIntegrations.Bsnes
{
    internal sealed class BsnesBml
    {
        public const string FileName = "settings.bml";

        private readonly List<string> _lines;
        public string Path { get; }
        public bool Changed { get; private set; }

        private BsnesBml(string path, List<string> lines) { Path = path; _lines = lines; }

        public static string Of(string exe) => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(exe)), FileName);

        public static BsnesBml Load(string path)
        {
            var lines = new List<string>();
            if (File.Exists(path))
            {
                lines.AddRange(File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n").Split('\n'));
                if (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            }
            return new BsnesBml(path, lines);
        }

        private static int Depth(string line) { int n = 0; while (n < line.Length && line[n] == ' ') n++; return n / 2; }

        private static string NameOf(string line)
        {
            var t = line.Trim();
            int colon = t.IndexOf(':');
            return colon < 0 ? t : t.Substring(0, colon).Trim();
        }

        private static string ValueOf(string line)
        {
            var t = line.Trim();
            int colon = t.IndexOf(':');
            return colon < 0 ? "" : t.Substring(colon + 1).Trim();
        }

        /// <summary>The line of the node at <paramref name="names"/>[0..count), or -1; and where its subtree ends.</summary>
        private int Find(string[] names, int count, out int end)
        {
            int from = 0, to = _lines.Count;
            int found = -1;
            for (int level = 0; level < count; level++)
            {
                found = -1;
                for (int i = from; i < to; i++)
                {
                    if (_lines[i].Trim().Length == 0) continue;
                    if (Depth(_lines[i]) != level) continue;
                    if (string.Equals(NameOf(_lines[i]), names[level], StringComparison.Ordinal)) { found = i; break; }
                }
                if (found < 0) { end = -1; return -1; }
                // The node's subtree: the lines after it that are deeper.
                int stop = found + 1;
                while (stop < to && (_lines[stop].Trim().Length == 0 || Depth(_lines[stop]) > level)) stop++;
                from = found + 1; to = stop;
            }
            end = to;
            return found;
        }

        public string Get(string path)
        {
            var names = path.Split('/');
            int at = Find(names, names.Length, out _);
            return at < 0 ? null : ValueOf(_lines[at]);
        }

        public void Set(string path, string value)
        {
            value = value ?? "";
            var names = path.Split('/');
            int at = Find(names, names.Length, out _);
            if (at >= 0)
            {
                if (string.Equals(ValueOf(_lines[at]), value, StringComparison.Ordinal)) return;
                _lines[at] = new string(' ', 2 * (names.Length - 1)) + names[names.Length - 1] + (value.Length > 0 ? ": " + value : "");
                Changed = true;
                return;
            }
            // The deepest parent there is, then the missing ones under it.
            int have = names.Length - 1;
            int parent = -1, insertAt = _lines.Count;
            while (have > 0 && (parent = Find(names, have, out insertAt)) < 0) have--;
            if (have == 0) insertAt = _lines.Count;
            for (int level = have; level < names.Length; level++)
            {
                var line = new string(' ', 2 * level) + names[level] + (level == names.Length - 1 && value.Length > 0 ? ": " + value : "");
                _lines.Insert(insertAt++, line);
            }
            Changed = true;
        }

        public void SetBool(string path, bool on) => Set(path, on ? "true" : "false");

        /// <summary>Whether a node other than <paramref name="except"/> holds <paramref name="binding"/> among its
        /// ';'-separated assignments - a hotkey or a pad button the user put on that key.</summary>
        public bool BoundElsewhere(string binding, string except)
        {
            var names = except.Split('/');
            int skip = Find(names, names.Length, out _);
            for (int i = 0; i < _lines.Count; i++)
            {
                if (i == skip) continue;
                foreach (var a in ValueOf(_lines[i]).Split(';'))
                    if (string.Equals(a.Trim(), binding, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>Written only when something changed: UTF-8 without a BOM, LF, as bsnes's own.</summary>
        public void SaveIfChanged()
        {
            if (!Changed) return;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            var tmp = Path + ".lbip-tmp";
            File.WriteAllText(tmp, string.Join("\n", _lines) + "\n", new UTF8Encoding(false));
            File.Move(tmp, Path, overwrite: true);
            Changed = false;
        }
    }
}
