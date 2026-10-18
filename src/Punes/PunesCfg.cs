// puNES's own files - puNES.cfg and input.cfg - edited in place, and where they are.
//
// THE FILES (measured 18/10 on punes64.exe 0.111, docs\punes.md sections 4 and 10): "[group]" lines, then "key=value" -
// keys with spaces ("preferred mode=auto"), nothing around the '=' - each key of puNES.cfg after a "# possible values:"
// comment, a blank line between keys; UTF-8 WITHOUT a byte-order mark, CRLF (objSettings.cpp:2376 wr_cfg_file:
// setGenerateByteOrderMark(false), NEWLINE). input.cfg the same, "name=<keyboard>,<joystick>" with Qt's key names and
// NULL for none.
//
// WHO WRITES THEM: puNES writes puNES.cfg back WHOLE at every exit, from its own key table (a line it does not know is
// dropped, the comments regenerated - measured), and input.cfg at every START (from the file and the --shortcut options of
// the line - measured). A value edited while puNES is closed is kept (measured: size window=3x survived the run). So
// nothing is ever written while it runs (PunesLaunch.Running), and a value the plugin manages is set before the launch.
//
// NOTHING IS REORDERED AND NOTHING IS DROPPED: a key present gets its value where it stands; a new key goes after the
// last line of its group, a new group at the end. Written only when something changed, through a temporary file.
//
// WHERE (src\gui\cmd_line.cpp:362-375 cmd_line_check_portable, src\gui\qt.cpp:343-372): PORTABLE - config and data in the
// exe's own folder - when the exe's name ends with "_p", or a puNES.cfg is beside it, or --portable is on the line; else
// %LOCALAPPDATA%\puNES (QStandardPaths::GenericConfigLocation\puNES, the same for the data on Windows), and with no
// puNES.cfg there a first-run WIZARD that waits for an answer (mainApplication.cpp:91-101) - a launch would hang on it.
// Measured 18/10: an EMPTY puNES.cfg beside the exe makes it portable with no wizard, and puNES fills it at exit
// (EnsurePortable).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Punes
{
    internal sealed class PunesCfg
    {
        public const string MainFile = "puNES.cfg", InputFile = "input.cfg";

        private readonly List<string> _lines;
        public string Path { get; }
        public bool Changed { get; private set; }
        public bool Exists { get; }

        private PunesCfg(string path, List<string> lines, bool exists) { Path = path; _lines = lines; Exists = exists; }

        // ── where ────────────────────────────────────────────────────────────

        /// <summary>Portable as puNES decides it (cmd_line_check_portable), --portable on the line aside: the exe's name ends
        /// with "_p" (Qt's completeBaseName: the name without its last extension), or its puNES.cfg is beside it.</summary>
        public static bool IsPortable(string exe)
        {
            try
            {
                var full = System.IO.Path.GetFullPath(exe);
                if (System.IO.Path.GetFileNameWithoutExtension(full).EndsWith("_p", StringComparison.Ordinal)) return true;
                return File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(full), MainFile));
            }
            catch { return false; }
        }

        /// <summary>puNES's folder for its settings AND its data (bios\, prb\, save\, diff\, screenshot\...): the exe's when
        /// portable, else %LOCALAPPDATA%\puNES.</summary>
        public static string DataDir(string exe)
        {
            if (IsPortable(exe)) return System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(exe));
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "puNES");
        }

        public static string MainOf(string exe) => System.IO.Path.Combine(DataDir(exe), MainFile);
        public static string InputOf(string exe) => System.IO.Path.Combine(DataDir(exe), InputFile);

        /// <summary>An install that would open puNES's first-run wizard - not portable, and no puNES.cfg in
        /// %LOCALAPPDATA%\puNES - made portable by an empty puNES.cfg beside the exe (measured: no wizard, filled at exit).
        /// An install with a configuration somewhere is left as it is. True when the file was made.</summary>
        public static bool EnsurePortable(string exe)
        {
            try
            {
                if (IsPortable(exe)) return false;
                if (File.Exists(MainOf(exe))) return false;   // %LOCALAPPDATA%\puNES\puNES.cfg: an install of the user's, kept so
                var cfg = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(exe)), MainFile);
                File.WriteAllBytes(cfg, Array.Empty<byte>());
                Log.Info("an empty " + MainFile + " made beside " + System.IO.Path.GetFileName(exe) + ": puNES is portable, no first-run wizard");
                return true;
            }
            catch (Exception ex) { Log.Warn("could not make puNES portable - its first-run wizard may wait for an answer", ex); return false; }
        }

        // ── reading ──────────────────────────────────────────────────────────

        public static PunesCfg Load(string path)
        {
            var lines = new List<string>();
            bool exists = File.Exists(path);
            if (exists)
            {
                var text = File.ReadAllText(path, Encoding.UTF8);
                if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
                lines.AddRange(text.Replace("\r\n", "\n").Split('\n'));
                if (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            }
            return new PunesCfg(path, lines, exists);
        }

        /// <summary>The value of <paramref name="key"/> in <paramref name="group"/>, or null.</summary>
        public string Get(string group, string key)
        {
            int at = Find(group, key, out _);
            if (at < 0) return null;
            var line = _lines[at];
            return line.Substring(line.IndexOf('=') + 1).Trim();
        }

        /// <summary>Every "key=value" of <paramref name="group"/>, in the file's order.</summary>
        public List<(string Key, string Value)> Entries(string group)
        {
            var list = new List<(string, string)>();
            bool inside = false;
            foreach (var raw in _lines)
            {
                var t = raw.Trim();
                if (IsGroup(t, out var name)) { if (inside) break; inside = string.Equals(name, group, StringComparison.OrdinalIgnoreCase); continue; }
                int eq = t.IndexOf('=');
                if (inside && eq > 0 && !t.StartsWith("#")) list.Add((t.Substring(0, eq).Trim(), t.Substring(eq + 1).Trim()));
            }
            return list;
        }

        // ── writing ──────────────────────────────────────────────────────────

        public void Set(string group, string key, string value)
        {
            value = value ?? "";
            int at = Find(group, key, out int groupEnd);
            if (at >= 0)
            {
                var line = _lines[at];
                int eq = line.IndexOf('=');
                if (string.Equals(line.Substring(eq + 1).Trim(), value, StringComparison.Ordinal)) return;
                _lines[at] = line.Substring(0, eq + 1) + value;
            }
            else
            {
                var line = key + "=" + value;
                if (groupEnd >= 0) _lines.Insert(groupEnd, line);
                else
                {
                    if (_lines.Count > 0 && _lines[_lines.Count - 1].Trim().Length > 0) _lines.Add("");
                    _lines.Add("[" + group + "]");
                    _lines.Add(line);
                }
            }
            Changed = true;
        }

        /// <summary>Written only when something changed: UTF-8 without a BOM, CRLF, as puNES's own.</summary>
        public void SaveIfChanged()
        {
            if (!Changed) return;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            var tmp = Path + ".lbip-tmp";
            File.WriteAllText(tmp, string.Join("\r\n", _lines) + "\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tmp, Path, overwrite: true);
            Changed = false;
        }

        private static bool IsGroup(string trimmed, out string name)
        {
            name = null;
            if (trimmed.Length < 2 || trimmed[0] != '[' || trimmed[trimmed.Length - 1] != ']') return false;
            name = trimmed.Substring(1, trimmed.Length - 2).Trim();
            return true;
        }

        /// <summary>The line of <paramref name="key"/> in <paramref name="group"/>, or -1; and where a new key of that group
        /// goes (after its last non-blank line), or -1 with no such group.</summary>
        private int Find(string group, string key, out int groupEnd)
        {
            groupEnd = -1;
            bool inside = false;
            int lastContent = -1;
            for (int i = 0; i < _lines.Count; i++)
            {
                var t = _lines[i].Trim();
                if (IsGroup(t, out var name))
                {
                    if (inside) break;
                    inside = string.Equals(name, group, StringComparison.OrdinalIgnoreCase);
                    if (inside) lastContent = i;
                    continue;
                }
                if (!inside) continue;
                if (t.Length > 0) lastContent = i;
                int eq = t.IndexOf('=');
                if (eq > 0 && !t.StartsWith("#") && string.Equals(t.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    groupEnd = lastContent + 1;
                    return i;
                }
            }
            if (lastContent >= 0) groupEnd = lastContent + 1;
            return -1;
        }
    }
}
