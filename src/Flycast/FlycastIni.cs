// Minimal, surgical reader/writer for emu.cfg.
//
// Deliberately NOT a general INI library. It reads named keys out of one section, and writes named
// keys back into that section while preserving every other line — including whatever Flycast
// itself keeps across saves. Anything it does not understand it leaves untouched.
//
// Two rules the LaunchBox plugins get wrong and we do not:
//
//  * Writes go through a temp file and File.Replace. PCSX2's integration deletes the config and
//    then rewrites it; an exception in between leaves the user with no configuration at all.
//  * We refuse to write while Flycast is running. Flycast rewrites emu.cfg from memory when it
//    exits, so an edit made underneath it is silently discarded — the worst kind of failure,
//    because everything reports success.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace LbIntegrations.Flycast
{
    internal static class FlycastIni
    {
        /// <summary>Read the given keys from one section. Missing file, missing section or missing key
        /// all come back as an absent dictionary entry rather than an exception.</summary>
        public static Dictionary<string, string> Read(string iniPath, string section, params string[] keys)
        {
            var wanted = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(iniPath)) return found;
                bool inSection = false;
                foreach (var raw in File.ReadLines(iniPath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                    if (line[0] == '[' && line[line.Length - 1] == ']')
                    {
                        inSection = string.Equals(line.Substring(1, line.Length - 2).Trim(), section,
                                                  StringComparison.OrdinalIgnoreCase);
                        continue;
                    }
                    if (!inSection) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = line.Substring(0, eq).Trim();
                    if (wanted.Contains(key)) found[key] = line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception ex) { Log.Warn("reading " + iniPath, ex); }
            return found;
        }

        /// <summary>Set keys inside a section, creating the section (and the file) if needed. Returns
        /// null on success, or a message explaining why nothing was written.</summary>
        public static string Write(string iniPath, string section, IDictionary<string, string> values)
        {
            if (values == null || values.Count == 0) return null;

            string running = RunningEmulatorProcess();
            if (running != null)
                return "Flycast is running (" + running + "). It rewrites emu.cfg when it exits, "
                     + "which would discard these changes. Close Flycast and try again.";

            try
            {
                var lines = File.Exists(iniPath)
                    ? new List<string>(File.ReadAllLines(iniPath))
                    : new List<string>();

                var pending = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);

                int sectionStart = -1, sectionEnd = lines.Count;
                for (int i = 0; i < lines.Count; i++)
                {
                    var t = lines[i].Trim();
                    if (t.Length < 2 || t[0] != '[' || t[t.Length - 1] != ']') continue;
                    if (sectionStart < 0)
                    {
                        if (string.Equals(t.Substring(1, t.Length - 2).Trim(), section, StringComparison.OrdinalIgnoreCase))
                            sectionStart = i;
                    }
                    else { sectionEnd = i; break; }      // the next section closes ours
                }

                if (sectionStart < 0)
                {
                    // No such section: append it, with a blank line before if the file has content.
                    if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add("");
                    lines.Add("[" + section + "]");
                    foreach (var kv in pending) lines.Add(kv.Key + " = " + kv.Value);
                }
                else
                {
                    // Rewrite in place the keys that are already there.
                    for (int i = sectionStart + 1; i < sectionEnd; i++)
                    {
                        var t = lines[i].Trim();
                        int eq = t.IndexOf('=');
                        if (eq <= 0 || t[0] == ';' || t[0] == '#') continue;
                        var key = t.Substring(0, eq).Trim();
                        if (!pending.TryGetValue(key, out var v)) continue;
                        lines[i] = key + " = " + v;
                        pending.Remove(key);
                    }
                    // Whatever is left is new: insert at the end of the section, before any trailing
                    // blank lines, so the file keeps the shape Flycast writes.
                    int insertAt = sectionEnd;
                    while (insertAt > sectionStart + 1 && lines[insertAt - 1].Trim().Length == 0) insertAt--;
                    foreach (var kv in pending) lines.Insert(insertAt++, kv.Key + " = " + kv.Value);
                }

                WriteAtomic(iniPath, lines);
                return null;
            }
            catch (Exception ex)
            {
                Log.Warn("writing " + iniPath, ex);
                return "Could not write " + iniPath + ": " + ex.Message;
            }
        }

        /// <summary>Write bytes to a path without ever leaving it missing or half-written: temp file
        /// beside the target, then File.Replace (atomic on NTFS). Used for the config and for the
        /// RetroAchievements token.</summary>
        public static void WriteAtomicBytes(string path, byte[] content)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(tmp, content);
            try
            {
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
        }

        // Flycast writes emu.cfg as plain UTF-8 without a BOM; match that so we don't introduce one.
        private static void WriteAtomic(string path, List<string> lines)
            => WriteAtomicBytes(path, new UTF8Encoding(false).GetBytes(string.Join("\r\n", lines) + "\r\n"));

        /// <summary>The name of a running Flycast process, or null. Matching is on the process name,
        /// which is "flycast" for every Windows build upstream ships.</summary>
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
                        if (n != null && n.StartsWith("flycast", StringComparison.OrdinalIgnoreCase))
                            return n;
                    }
                }
            }
            catch (Exception ex) { Log.Warn("could not enumerate processes", ex); }
            return null;
        }

        public static bool AsBool(string v, bool fallback)
        {
            // Flycast's own reader (core/cfg/ini.cpp) accepts "yes", "true" and "1", case-insensitive,
            // and treats anything else as false. We accept those three plus their opposites, and fall
            // back only when the key is absent - a value we cannot read is worth not hiding.
            if (string.IsNullOrWhiteSpace(v)) return fallback;
            v = v.Trim();
            if (v.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1") return true;
            if (v.Equals("no", StringComparison.OrdinalIgnoreCase)
                || v.Equals("false", StringComparison.OrdinalIgnoreCase) || v == "0") return false;
            return fallback;
        }

        /// <summary>Flycast READS yes/true/1 but WRITES std::to_string(bool), i.e. "1"/"0" - measured in
        /// core/cfg/ini.h. We write what Flycast writes so a diff of the file
        /// before and after our edit shows only the values that actually changed.</summary>
        public static string FromBool(bool b) => b ? "1" : "0";
    }
}
