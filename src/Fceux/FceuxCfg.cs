// fceux.cfg of the Qt build (bin\fceux.cfg), edited in place.
//
// THE FILE (read in src\drivers\common\configSys.cpp at the fceux-2.7.0 tag, measured 18/10 on qfceux.exe 2.7.0 - the lab's
// runqt2): beside the executable (drivers\Qt\config.cpp GetBaseDirectory: the exe's folder on Windows unless FCEUX_HOME or
// FCEUX_CONFIG_DIR is set - portable, nothing under AppData, measured). One "name = value" per line: the name runs to the
// first space or '=', the value from the first non-space after '=' to the end; '#' starts a comment. Made at the first
// exit, 610 lines, CRLF (a text-mode stream), no BOM.
//
// FCEUX REWRITES IT WHOLE AT EVERY EXIT (Config::save: every option it knows, in its maps' order) and DROPS THE NAMES IT
// DOES NOT KNOW (only its own options are written). A name that is missing keeps FCEUX's compiled default - so a file
// holding only the plugin's lines is a valid one, filled in by FCEUX at its first exit. So: written before the launch,
// never while it runs; whatever FCEUX holds in memory at its exit wins. AN OPTION ON THE COMMAND LINE IS STORED TOO
// (measured: "--pal 1" left "SDL.PAL = 1" after the exit) - so the plugin writes what it wants here rather than on the line.
//
// NOTHING REORDERED, NOTHING DROPPED: a line is kept as it is; a name set gets its value where it stands; a new one goes at
// the end. Bytes kept as they are (read and written as Latin-1, a lossless round trip): FCEUX's paths and texts are never
// re-encoded. Written only when something changed, through a temporary file.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace LbIntegrations.Fceux
{
    internal sealed class FceuxCfg
    {
        public const string FileName = "fceux.cfg";

        // The names the plugin reads or writes (drivers\Qt\config.cpp InitConfig at the tag).
        public const string FullScreen = "SDL.Fullscreen";
        public const string Pal = "SDL.PAL";
        public const string Hotkey = "SDL.Hotkeys.";

        private static readonly Encoding Bytes = Encoding.Latin1;

        private readonly List<string> _lines;
        private readonly string _eol;
        public string Path { get; }
        public bool Changed { get; private set; }
        /// <summary>The file was there when loaded - false on an install FCEUX never ran.</summary>
        public bool Existed { get; }

        private FceuxCfg(string path, List<string> lines, bool existed, string eol) { Path = path; _lines = lines; Existed = existed; _eol = eol; }

        public static string Of(string exe) => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(exe)), FileName);

        public static FceuxCfg Load(string path)
        {
            var lines = new List<string>();
            bool existed = File.Exists(path);
            string eol = "\r\n";
            if (existed)
            {
                var text = Bytes.GetString(File.ReadAllBytes(path));
                if (text.IndexOf('\n') >= 0 && text.IndexOf("\r\n", StringComparison.Ordinal) < 0) eol = "\n";
                lines.AddRange(text.Replace("\r\n", "\n").Split('\n'));
                if (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            }
            return new FceuxCfg(path, lines, existed, eol);
        }

        // ── ANSI ──

        [DllImport("kernel32.dll")]
        private static extern int GetACP();

        private static Encoding _ansiStrict;

        private static Encoding AnsiStrict => _ansiStrict ??= Make();

        private static Encoding Make()
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                int cp = 1252;
                try { cp = GetACP(); } catch { }
                return Encoding.GetEncoding(cp, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            }
            catch { return Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback); }
        }

        /// <summary>Can FCEUX be given this path - every character in the ANSI code page? FCEUX's main takes char arguments
        /// (measured on the Windows build: a Japanese name fails with "Error opening ...??????.nes!"); the Qt build's main is
        /// the same char main - kept as the rule until measured otherwise.</summary>
        public static bool Representable(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            try { var back = AnsiStrict.GetString(AnsiStrict.GetBytes(text)); return back == text; }
            catch { return false; }
        }

        // ── reading ──

        /// <summary>The name of a line as Config::_loadFile reads it (to the first space or '='), or null for a comment or a
        /// line without '='.</summary>
        private static string NameOf(string line)
        {
            if (string.IsNullOrEmpty(line) || line[0] == '#') return null;
            int eq = line.IndexOf('=');
            if (eq < 0) return null;
            int sp = line.IndexOf(' ');
            int end = sp >= 0 && sp < eq ? sp : eq;
            return line.Substring(0, end);
        }

        private int Find(string name)
        {
            for (int i = 0; i < _lines.Count; i++)
                if (string.Equals(NameOf(_lines[i]), name, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>The value of <paramref name="name"/>, or null when the file does not have it.</summary>
        public string Get(string name)
        {
            int at = Find(name);
            if (at < 0) return null;
            var line = _lines[at];
            int eq = line.IndexOf('=');
            return line.Substring(eq + 1).TrimStart(' ');
        }

        public int? GetInt(string name) => int.TryParse(Get(name), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (int?)null;

        /// <summary>Every "name = value" line whose name starts with <paramref name="prefix"/>, in the file's order.</summary>
        public IEnumerable<(string Name, string Value)> Entries(string prefix)
        {
            foreach (var line in _lines)
            {
                var name = NameOf(line);
                if (name == null || !name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                yield return (name, line.Substring(line.IndexOf('=') + 1).TrimStart(' '));
            }
        }

        // ── writing ──

        /// <summary>"name = value" where the name stands, else at the end.</summary>
        public void Set(string name, string value)
        {
            value ??= "";
            var line = name + " = " + value;
            int at = Find(name);
            if (at >= 0)
            {
                if (string.Equals(Get(name), value, StringComparison.Ordinal)) return;
                _lines[at] = line;
            }
            else _lines.Add(line);
            Changed = true;
        }

        public void SetInt(string name, int value) => Set(name, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        /// <summary>Written only when something changed, in its own line endings (CRLF as FCEUX writes it), no BOM.</summary>
        public void SaveIfChanged()
        {
            if (!Changed) return;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            var tmp = Path + ".lbip-tmp";
            File.WriteAllBytes(tmp, Bytes.GetBytes(string.Join(_eol, _lines) + _eol));
            File.Move(tmp, Path, overwrite: true);
            Changed = false;
        }
    }
}
