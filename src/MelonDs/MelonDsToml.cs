// Minimal, surgical reader/writer for melonDS.toml.
//
// Deliberately NOT a TOML library. It reads named keys out of one table and writes named keys back
// into that table, preserving every other line. That is enough because the plugin only ever touches
// five keys, and it keeps a whole dependency out of the merged assembly.
//
// CREATING THE FILE IS LEGITIMATE HERE, which is the opposite of the rule that applies to PPSSPP's
// controls.ini. melonDS declares its defaults as sparse maps and resolves a missing key by walking
// the key backwards until one matches (Config.cpp:49-128 and FindDefault, Config.cpp:663-679), so a
// key absent from the file keeps its default instead of becoming unset. A file holding nothing but
// our two paths is therefore a complete, valid configuration.
//
// WE NEVER WRITE WHILE melonDS IS RUNNING. Config::Save (Config.cpp:809-819) truncates the file and
// reserialises the whole in-memory document on exit, so an edit made underneath it is silently
// discarded - the worst kind of failure, because everything reports success. The same mechanism is
// also why our keys survive melonDS's own rewrites: the document round-trips entries it does not
// know, losing only comments and formatting.
//
// PATHS ARE WRITTEN AS TOML LITERAL STRINGS, in single quotes, where a backslash is just a
// backslash. A Windows path in a basic string would need every separator doubled, and one missed
// escape makes toml::parse throw - at which point melonDS keeps an empty document and writes it over
// the user's configuration on exit (Config.cpp:785-807, where the recovery line is commented out).
// A literal string cannot contain a single quote, so the rare path that holds one falls back to a
// basic string with the escaping done properly.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace LbIntegrations.MelonDs
{
    internal static class MelonDsToml
    {
        /// <summary>Read the given keys from one table. Missing file, missing table or missing key all
        /// come back as an absent dictionary entry rather than an exception. String values are
        /// returned decoded; anything else comes back as the raw token, which is what the callers
        /// that read integers want.</summary>
        public static Dictionary<string, string> Read(string tomlPath, string table, params string[] keys)
        {
            var wanted = new HashSet<string>(keys, StringComparer.Ordinal);
            var found = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(tomlPath)) return found;
                bool inTable = table.Length == 0;          // "" means the root table
                foreach (var raw in File.ReadLines(tomlPath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    if (line[0] == '[')
                    {
                        int close = line.IndexOf(']');
                        if (close < 0) continue;
                        inTable = string.Equals(line.Substring(1, close - 1).Trim(), table, StringComparison.Ordinal);
                        continue;
                    }
                    if (!inTable) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = line.Substring(0, eq).Trim();
                    if (wanted.Contains(key)) found[key] = Decode(line.Substring(eq + 1).Trim());
                }
            }
            catch (Exception ex) { Log.Warn("reading " + tomlPath, ex); }
            return found;
        }

        /// <summary>Set keys inside a table, creating the table - and the file - if needed. Values must
        /// already be TOML-encoded: use <see cref="Text"/> for a string and plain digits for an
        /// integer. Returns null on success, or a message explaining why nothing was written.</summary>
        public static string Write(string tomlPath, string table, IDictionary<string, string> values)
        {
            if (values == null || values.Count == 0) return null;

            string running = RunningEmulatorProcess();
            if (running != null)
                return "melonDS is running (" + running + "). It rewrites its configuration when it exits, "
                     + "which would discard these changes. Close melonDS and try again.";

            try
            {
                var lines = File.Exists(tomlPath)
                    ? new List<string>(File.ReadAllLines(tomlPath))
                    : new List<string>();

                var pending = new Dictionary<string, string>(values, StringComparer.Ordinal);

                int tableStart = table.Length == 0 ? 0 : -1, tableEnd = lines.Count;
                for (int i = 0; i < lines.Count; i++)
                {
                    var t = lines[i].Trim();
                    if (t.Length < 2 || t[0] != '[') continue;
                    int close = t.IndexOf(']');
                    if (close < 0) continue;
                    if (tableStart < 0)
                    {
                        if (string.Equals(t.Substring(1, close - 1).Trim(), table, StringComparison.Ordinal))
                            tableStart = i;
                    }
                    else { tableEnd = i; break; }         // the next header closes ours
                }

                if (tableStart < 0)
                {
                    if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add("");
                    lines.Add("[" + table + "]");
                    foreach (var kv in pending) lines.Add(kv.Key + " = " + kv.Value);
                }
                else
                {
                    for (int i = tableStart + 1; i < tableEnd; i++)
                    {
                        var t = lines[i].Trim();
                        if (t.Length == 0 || t[0] == '#') continue;
                        int eq = t.IndexOf('=');
                        if (eq <= 0) continue;
                        var key = t.Substring(0, eq).Trim();
                        if (!pending.TryGetValue(key, out var v)) continue;
                        lines[i] = key + " = " + v;
                        pending.Remove(key);
                    }
                    int insertAt = tableEnd;
                    while (insertAt > tableStart + 1 && lines[insertAt - 1].Trim().Length == 0) insertAt--;
                    foreach (var kv in pending) lines.Insert(insertAt++, kv.Key + " = " + kv.Value);
                }

                WriteAtomic(tomlPath, lines);
                return null;
            }
            catch (Exception ex)
            {
                Log.Warn("writing " + tomlPath, ex);
                return "Could not write " + tomlPath + ": " + ex.Message;
            }
        }

        /// <summary>A string, encoded for TOML. Literal (single-quoted) so a Windows path needs no
        /// escaping at all; basic (double-quoted, escaped) for the rare value holding a quote, which a
        /// literal string cannot represent.</summary>
        public static string Text(string value)
        {
            value = value ?? "";
            if (value.IndexOf('\'') < 0 && value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0)
                return "'" + value + "'";

            var sb = new StringBuilder("\"");
            foreach (var c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.Append('"').ToString();
        }

        /// <summary>The other direction. A value that is not a quoted string is handed back as it was
        /// written, which is what an integer key needs.</summary>
        private static string Decode(string token)
        {
            if (string.IsNullOrEmpty(token)) return token;

            // A trailing comment is only a comment outside a string, so it is stripped after quoting
            // has been settled rather than before.
            if (token[0] == '\'')
            {
                int end = token.IndexOf('\'', 1);
                return end < 0 ? token.Substring(1) : token.Substring(1, end - 1);
            }
            if (token[0] == '"')
            {
                var sb = new StringBuilder();
                for (int i = 1; i < token.Length; i++)
                {
                    var c = token[i];
                    if (c == '"') break;
                    if (c != '\\') { sb.Append(c); continue; }
                    if (++i >= token.Length) break;
                    switch (token[i])
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(token[i]); break;     // covers \\ and \"
                    }
                }
                return sb.ToString();
            }

            int hash = token.IndexOf('#');
            return (hash < 0 ? token : token.Substring(0, hash)).Trim();
        }

        /// <summary>Write bytes without ever leaving the file missing or half-written: temp file beside
        /// the target, then File.Replace (atomic on NTFS).
        ///
        /// FILE.REPLACE IS NOT RELIABLE ON ITS OWN, and that is measured rather than defensive
        /// programming: the probe caught an "Unable to remove the file to be replaced" on a file
        /// nothing else was using. On Windows a virus scanner or the indexer can hold a transient
        /// handle on a file that was just written, and Replace then fails outright. Losing the
        /// emulator's configuration to that would be absurd.
        ///
        /// So: a few tries, then a plain overwrite. The fallback gives up atomicity - the target can
        /// in principle be caught half-written - but it never leaves it missing, and it is reached
        /// only when the atomic route has already refused three times.</summary>
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
                        // Whoever is holding the file is not letting go. Write over it rather than
                        // report a failure the caller can do nothing about.
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

        // toml11 writes UTF-8 without a BOM; match that so we do not introduce one. melonDS strips a
        // BOM on read anyway (IniFile-era behaviour aside, toml::parse handles it), but a file that
        // differs from what the emulator writes is a needless difference in a diff.
        private static void WriteAtomic(string path, List<string> lines)
            => WriteAtomicBytes(path, new UTF8Encoding(false).GetBytes(string.Join("\r\n", lines) + "\r\n"));

        /// <summary>The name of a running melonDS process, or null.</summary>
        private static string RunningEmulatorProcess()
        {
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    using (p)
                    {
                        string n;
                        try { n = p.ProcessName; } catch { continue; }
                        if (n != null && n.StartsWith("melonDS", StringComparison.OrdinalIgnoreCase))
                            return n;
                    }
                }
            }
            catch (Exception ex) { Log.Warn("could not enumerate processes", ex); }
            return null;
        }

        public static int AsInt(string v, int fallback)
            => int.TryParse((v ?? "").Trim(), out var n) ? n : fallback;
    }
}
