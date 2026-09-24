// NO$GBA.INI, and the three things about it that are not obvious.
//
// THE SEPARATOR IS "==", NOT "=". Every line no$gba writes reads `Key == Value`, and the file opens
// with `;no$gba 3.0 generated config file - do not edit`. It is edited anyway, below, and the note
// is worth taking seriously only in the sense that the emulator's own Options > Save Options
// rewrites the whole file - so anything this plugin sets has to be set again rather than once.
//
// A PARTIAL FILE IS VALID. Measured on a fresh installation: a hand-written NO$GBA.INI of three
// lines was read, applied, and every setting it did not mention kept its default. So this never has
// to reproduce the other fifty keys, and a file that does not exist can simply be created with the
// one line that matters.
//
// NO$GBA NEVER REWRITES IT ON EXIT. Measured: 108 bytes in, 108 bytes out, byte for byte, across a
// full session. That is the opposite of melonDS, which truncates and rewrites its whole config when
// it quits - so there is no "do not write while the emulator is running" hazard here, and no need
// for the force/merge dance MelonDsToml does.
//
// AND A WRONG VALUE IS IGNORED IN SILENCE. This is the trap. `Reset/Startup Entrypoint == GBA/NDS
// BIOS` does nothing at all; the value no$gba accepts is `GBA/NDS BIOS (Nintendo logo)`, the exact
// label of the entry in the drop-down. There is no error, no log, no fallback - the setting simply
// stays as it was, and the only way to find out is to open Options > Emulation Setup and look. So
// every value written from here is a named constant carrying the menu label it was read from.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LbIntegrations.Dsi;

namespace LbIntegrations.NoGba
{
    internal static class NoGbaIni
    {
        /// <summary>What separates a key from its value. Two characters, and the space either side
        /// is part of how no$gba writes it.</summary>
        private const string Separator = " == ";

        /// <summary>Read the values of the named keys. Missing keys are simply absent from the
        /// result; a missing file gives an empty map rather than a failure.</summary>
        public static Dictionary<string, string> Read(string iniPath, params string[] keys)
        {
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (string.IsNullOrWhiteSpace(iniPath) || !File.Exists(iniPath)) return found;
                var wanted = new HashSet<string>(keys ?? Array.Empty<string>(),
                                                 StringComparer.OrdinalIgnoreCase);

                foreach (var line in File.ReadAllLines(iniPath))
                {
                    if (!Split(line, out var key, out var value)) continue;
                    if (wanted.Count > 0 && !wanted.Contains(key)) continue;
                    found[key] = value;
                }
            }
            catch (Exception ex) { Log.Verbose("could not read " + iniPath + " - " + ex.Message); }
            return found;
        }

        /// <summary>Set the given keys, leaving every other line exactly as it was.
        ///
        /// NOTHING IS REORDERED AND NOTHING IS DROPPED. A key already present is rewritten in place;
        /// a new one is appended. Comments, blank lines, the `;do not edit` banner and the fifty
        /// settings this plugin knows nothing about all survive character for character - which is
        /// also what makes the "a second pass changes nothing" assertion in the probe meaningful.
        ///
        /// Answers null when it wrote, or a reason when it did not. Writing nothing because nothing
        /// differed counts as writing.</summary>
        public static string Write(string iniPath, IDictionary<string, string> values)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(iniPath)) return "no configuration file to write to";
                if (values == null || values.Count == 0) return null;

                var lines = new List<string>();
                var newline = Environment.NewLine;
                bool trailing = true;

                if (File.Exists(iniPath))
                {
                    var text = File.ReadAllText(iniPath);
                    newline = text.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
                    trailing = text.Length == 0 || text.EndsWith("\n", StringComparison.Ordinal);
                    lines.AddRange(text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n'));
                }
                else
                {
                    // A file of our own. no$gba writes this banner itself, and starting with it
                    // makes a plugin-created file indistinguishable from one the emulator wrote.
                    lines.Add(";no$gba generated config file - do not edit");
                    lines.Add("");
                    newline = "\r\n";
                }

                var left = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
                bool changed = false;

                for (int i = 0; i < lines.Count; i++)
                {
                    if (!Split(lines[i], out var key, out var was)) continue;
                    if (!left.TryGetValue(key, out var want)) continue;
                    left.Remove(key);

                    if (string.Equals(was, want, StringComparison.Ordinal)) continue;
                    lines[i] = key + Separator + want;
                    changed = true;
                }

                foreach (var pair in left)
                {
                    lines.Add(pair.Key + Separator + pair.Value);
                    changed = true;
                }

                if (!changed) return null;

                var body = string.Join(newline, lines) + (trailing ? newline : "");
                WriteAtomicBytes(iniPath, new UTF8Encoding(false).GetBytes(body));
                return null;
            }
            catch (Exception ex) { return ex.GetType().Name + ": " + ex.Message; }
        }

        /// <summary>One line into a key and a value, or false for a comment, a blank, or anything
        /// that does not carry the separator.</summary>
        private static bool Split(string line, out string key, out string value)
        {
            key = null;
            value = null;
            if (line == null) return false;

            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(";", StringComparison.Ordinal)) return false;

            var at = trimmed.IndexOf("==", StringComparison.Ordinal);
            if (at <= 0) return false;

            key = trimmed.Substring(0, at).Trim();
            value = trimmed.Substring(at + 2).Trim();
            return key.Length > 0;
        }

        /// <summary>Write through a temporary file and move it into place, so an interrupted write
        /// never leaves a half-configuration. Taken from MelonDsToml, where the retries earned
        /// themselves against a host holding the file open.</summary>
        public static void WriteAtomicBytes(string path, byte[] content)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(tmp, content);

            try
            {
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        if (File.Exists(path)) File.Replace(tmp, path, null);
                        else File.Move(tmp, path);
                        return;
                    }
                    catch (IOException) when (attempt < 2)
                    {
                        System.Threading.Thread.Sleep(50 * (attempt + 1));
                    }
                    catch (IOException)
                    {
                        File.Copy(tmp, path, overwrite: true);
                        Log.Verbose("could not replace " + path + " atomically; overwrote it instead");
                        return;
                    }
                }
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }
    }
}
