// fceux.cfg, edited in place.
//
// THE FILE (measured 18/10 on FCEUX 2.7.0 win64 and win32, and read in drivers\common\config.cpp at the tag): beside the
// executable, always (drivers\win\main.cpp: GetBaseDirectory is the exe's folder - portable, nothing under AppData). One
// "key value" per line, the keys of a std::map written in BYTE ORDER (so the quoted keys - "fs", "odnonvol"... - come
// first, after "!version 1"), one space between, the value to the end of the line (cfg_Parse: leading whitespace skipped,
// the rest kept, spaces included; '#' at the start of a line is a comment; CR and LF both end a line). Integers of 1, 2 or
// 4 bytes are decimal, every other blob "base64:" (standard alphabet, '=' padding - measured, all 22 blobs round-trip),
// strings raw, an empty string nothing after the space. THE KEY IS WRITTEN AS IT IS IN THE FILE: "\"fs\"" with its
// quotes, CurrentState without.
//
// LF ONLY, NO BOM, BYTES IN THE ANSI CODE PAGE (fopen "wb"; measured 412 LF, 0 CRLF; the Windows build is not Unicode -
// main(int argc, char *argv[]) - and a Japanese path fails, measured).
//
// FCEUX REWRITES IT WHOLE AT EVERY EXIT (main.cpp:589 DriverKill -> SaveConfig, measured: its date changes at every close)
// and KEEPS THE KEYS IT DOES NOT KNOW (measured: "zzzUnknownKey 42" survived a session; the map is loaded, then updated).
// A key that is missing keeps FCEUX's compiled default (config.cpp cfg_NewToOld: "if the key was not found, skip it"), so
// a file holding only the plugin's keys is a valid one - FCEUX fills in the rest at its first exit. So: written before the
// launch, never while it runs; whatever FCEUX holds in memory at its exit wins.
//
// NOTHING REORDERED, NOTHING DROPPED: a line is kept as its bytes; a key set gets its value where it stands (a CR the
// line had kept); a new key goes where FCEUX's byte order would put it. Written only when something changed, through a
// temporary file.

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

        // The keys the plugin reads or writes (drivers\win\config.cpp at the tag), spelt as in the file.
        public const string FullScreen = "\"fs\"";
        public const string ModeWidth = "\"vmcx\"", ModeHeight = "\"vmcy\"", ModeBits = "\"vmcb\"";
        public const string SaveDir = "\"odnonvol\"", StateDir = "\"odstates\"", SnapDir = "\"odsnaps\"", CheatDir = "\"odcheats\"";
        public const string FdsRomDir = "\"odfdsrom\"", BaseDir = "\"odbase\"";
        public const string CommandMapping = "FCEUD_CommandMapping_V2";
        public const string GamePads = "GamePadConfig_V2";
        public const string AutoHold = "autoHoldKeys", AutoHoldClear = "autoHoldClearKeys";

        private readonly List<byte[]> _lines;
        public string Path { get; }
        public bool Changed { get; private set; }
        /// <summary>The file was there when loaded - false on an install FCEUX never ran.</summary>
        public bool Existed { get; }

        private FceuxCfg(string path, List<byte[]> lines, bool existed) { Path = path; _lines = lines; Existed = existed; }

        public static string Of(string exe) => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(exe)), FileName);

        public static FceuxCfg Load(string path)
        {
            var lines = new List<byte[]>();
            bool existed = File.Exists(path);
            if (existed)
            {
                var bytes = File.ReadAllBytes(path);
                int start = 0;
                for (int i = 0; i < bytes.Length; i++)
                {
                    if (bytes[i] != (byte)'\n') continue;
                    lines.Add(bytes.AsSpan(start, i - start).ToArray());
                    start = i + 1;
                }
                if (start < bytes.Length) lines.Add(bytes.AsSpan(start).ToArray());
            }
            return new FceuxCfg(path, lines, existed);
        }

        // ── the ANSI code page ──

        [DllImport("kernel32.dll")] private static extern int GetACP();

        private static Encoding _ansi, _ansiStrict;

        /// <summary>What FCEUX's char strings are in: the system's ANSI code page (a non-Unicode Windows program).</summary>
        internal static Encoding Ansi => _ansi ??= Make(strict: false);
        private static Encoding AnsiStrict => _ansiStrict ??= Make(strict: true);

        private static Encoding Make(bool strict)
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                int cp = 1252;
                try { cp = GetACP(); } catch { }
                return strict ? Encoding.GetEncoding(cp, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
                              : Encoding.GetEncoding(cp);
            }
            catch { return strict ? Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback) : Encoding.GetEncoding(1252); }
        }

        /// <summary>Can FCEUX be given this text (a path) - every character in the ANSI code page? Measured 18/10: "Zelda é.nes"
        /// loads and saves, a Japanese name fails with "Error opening ...??????.nes!".</summary>
        public static bool Representable(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            try { var back = AnsiStrict.GetString(AnsiStrict.GetBytes(text)); return back == text; }
            catch { return false; }
        }

        // ── reading ──

        /// <summary>The key of a line as cfg_Parse reads it (leading whitespace skipped, to the next space, tab, CR), or null
        /// for a blank line or a comment.</summary>
        private static string KeyOf(byte[] line)
        {
            int i = 0;
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
            if (i >= line.Length || line[i] == '#' || line[i] == '\r') return null;
            int s = i;
            while (i < line.Length && line[i] != ' ' && line[i] != '\t' && line[i] != '\r') i++;
            return Encoding.ASCII.GetString(line, s, i - s);
        }

        private static string ValueOf(byte[] line)
        {
            int i = 0;
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
            while (i < line.Length && line[i] != ' ' && line[i] != '\t' && line[i] != '\r') i++;
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
            int end = i;
            while (end < line.Length && line[end] != '\r') end++;
            return Ansi.GetString(line, i, end - i);
        }

        private int Find(string key)
        {
            for (int i = 0; i < _lines.Count; i++)
                if (string.Equals(KeyOf(_lines[i]), key, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>The value of <paramref name="key"/> (spelt as in the file), or null when the file does not have it.</summary>
        public string Get(string key)
        {
            int at = Find(key);
            return at < 0 ? null : ValueOf(_lines[at]);
        }

        public int? GetInt(string key)
            => int.TryParse(Get(key)?.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (int?)null;

        /// <summary>A "base64:" value decoded, or null (missing, or not a blob).</summary>
        public byte[] GetBlob(string key)
        {
            var v = Get(key)?.Trim();
            if (v == null || !v.StartsWith("base64:", StringComparison.Ordinal)) return null;
            try { return Convert.FromBase64String(v.Substring(7)); }
            catch { return null; }
        }

        /// <summary>Every key of the file, in its order.</summary>
        public IEnumerable<string> Keys => _lines.Select(KeyOf).Where(k => k != null);

        // ── writing ──

        /// <summary><paramref name="key"/> set to <paramref name="value"/> - in place, or where FCEUX's byte order puts it.</summary>
        public void Set(string key, string value)
        {
            value ??= "";
            int at = Find(key);
            if (at >= 0)
            {
                if (string.Equals(ValueOf(_lines[at]), value, StringComparison.Ordinal)) return;
                bool cr = _lines[at].Length > 0 && _lines[at][_lines[at].Length - 1] == (byte)'\r';
                _lines[at] = Line(key, value, cr);
            }
            else
            {
                int where = _lines.Count;
                for (int i = 0; i < _lines.Count; i++)
                {
                    var k = KeyOf(_lines[i]);
                    if (k != null && string.CompareOrdinal(k, key) > 0) { where = i; break; }
                }
                // A file FCEUX never wrote starts as FCEUX's do (LoadFCEUConfig sets it in any case).
                if (_lines.Count == 0 && key != "!version") { _lines.Add(Line("!version", "1", false)); where = 1; }
                _lines.Insert(where, Line(key, value, false));
            }
            Changed = true;
        }

        public void SetInt(string key, int value) => Set(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public void SetBlob(string key, byte[] bytes) => Set(key, "base64:" + Convert.ToBase64String(bytes));

        private static byte[] Line(string key, string value, bool cr)
        {
            var bytes = Encoding.ASCII.GetBytes(key + " ").Concat(Ansi.GetBytes(value));
            return (cr ? bytes.Concat(new[] { (byte)'\r' }) : bytes).ToArray();
        }

        /// <summary>Written only when something changed: the lines' own bytes, LF after each, no BOM - as FCEUX writes it.</summary>
        public void SaveIfChanged()
        {
            if (!Changed) return;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            var tmp = Path + ".lbip-tmp";
            using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                foreach (var line in _lines) { f.Write(line, 0, line.Length); f.WriteByte((byte)'\n'); }
            File.Move(tmp, Path, overwrite: true);
            Changed = false;
        }
    }
}
