// What a launch decides and writes for Ymir: which install, its profile, the disc, its keys, the line.
//
// THE EXE is ymir-sdl3.exe (docs\ymir.md §1). Claimed by its name.
//
// THE PROFILE (§3): ALWAYS the exe's folder, given with -p - LaunchBox starts emulators from ITS root (memory
// "lb-cwd-is-root"), and Ymir without -p would look there, then beside the exe, then in the user profile, then ask
// ("Installed" / "Portable"). With -p on a folder with nothing in it Ymir asks nothing and makes its folders (measured 20/10).
// Everything is there: Ymir.toml, roms\ipl\ (BIOS), backup\, state\, savestates\, screenshots\.
//
// ITS FIRST SETTINGS (Bootstrap), when the profile has no Ymir.toml yet - Ymir writes its own only when it quits: a few
// lines Ymir completes with its defaults (settings.cpp Settings::Load: ResetToDefaults, then each key present), with
// ConfigVersion 6 (0.4.1's), so they are read as today's. And ALWAYS the marker state\updates\.onboarded: without it Ymir
// opens "Automatic update checks" at every start, which takes the keyboard (measured 20/10: F2 did nothing) - Mehdi 20/10:
// no popup, updates are LiteBox's.
//
// THE PACK'S KEYS (Mehdi 20/10, BindPackKeys) in Ymir.toml, each only where it still holds Ymir's default:
//   F2 save state         Ymir's own QuickSaveState
//   F4 load state         QuickLoadState F3 -> F4 (Shift+F3, its undo, stays)
//   F11 full screen       added to ToggleFullScreen (Alt+Enter kept) - taken off ToggleDebugTrace, Ymir's default on F11,
//                         or bound when no action of Ymir's holds F11
//   F6 / F7 slot -/+      the AHK script (YmirAhk): Ymir has no "next slot" - it selects slot 1-10 with keys 1-0
//   PageDown / PageUp     the next / previous disc, the AHK script through Ymir's Load Disc (Ctrl+O) - RetroArch's keys
//   Esc quit              the AHK script: Ymir refuses Escape in its bindings (settings.cpp 2155, measured)
//
// THE DISC: what Ymir opens (YmirDisc.Native - .chd .cue .ccd .mds .iso). A .m3u is read here (Ymir has no playlist): its
// first disc is given. An archive (.zip, .7z) is unpacked whole into <install>\Unpacked\<name>-<hash>\ - a .cue's .bin
// files with it - and its .m3u, else its first disc, given. THE DISCS OF A GAME (for "next disc"): the .m3u's, else the files
// beside the disc named the same but for "(Disc N)"; written in lbip-discs.txt in the profile for the AHK script.
//
// THE LINE: -p "<profile>" [-f] "<disc>" and what the user put on the platform's line - the game LaunchBox appended is
// replaced by the disc.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SharpCompress.Archives;

namespace LbIntegrations.Ymir
{
    internal static class YmirLaunch
    {
        public const string ExecutableName = "ymir-sdl3.exe";
        public const string DiscsFile = "lbip-discs.txt";

        /// <summary>The ConfigVersion of the Ymir.toml written when there is none (0.4.1's, settings.cpp kConfigVersion).</summary>
        internal const int ConfigVersion = 6;

        // ── the exe ──────────────────────────────────────────────────────────

        public static bool IsYmirExe(string path)
        {
            try { return string.Equals(Path.GetFileName((path ?? "").Trim().Trim('"')), ExecutableName, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        public static bool Running(string exe)
        {
            try
            {
                var full = Path.GetFullPath(exe);
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExecutableName)))
                    using (p)
                        try { if (string.Equals(p.MainModule?.FileName, full, StringComparison.OrdinalIgnoreCase)) return true; } catch { }
            }
            catch { }
            return false;
        }

        public static string ProfileOf(string exe) => Path.GetDirectoryName(Path.GetFullPath(exe));

        public static string SettingsOf(string exe) => Path.Combine(ProfileOf(exe), YmirToml.FileName);

        /// <summary>The folder Ymir keeps its persistent state in: [General.PathOverrides] PersistentState, else state\.</summary>
        public static string StateDirOf(string exe, YmirTomlDoc doc = null) => Override(exe, doc, "PersistentState", "state");

        public static string SaveStatesDirOf(string exe, YmirTomlDoc doc = null) => Override(exe, doc, "SaveStates", "savestates");

        public static string IplDirOf(string exe, YmirTomlDoc doc = null) => Override(exe, doc, "IPLROMImages", Path.Combine("roms", "ipl"));

        private static string Override(string exe, YmirTomlDoc doc, string key, string standard)
        {
            var profile = ProfileOf(exe);
            try
            {
                doc ??= YmirTomlDoc.Load(SettingsOf(exe));
                var v = YmirToml.Text(doc.Get("General.PathOverrides", key));
                if (!string.IsNullOrWhiteSpace(v)) return Path.IsPathRooted(v) ? v : Path.GetFullPath(Path.Combine(profile, v));
            }
            catch { }
            return Path.Combine(profile, standard);
        }

        /// <summary>The marker that keeps "Automatic update checks" from showing (header). True when written now.</summary>
        public static bool Onboard(string exe)
        {
            try
            {
                var marker = Path.Combine(StateDirOf(exe), "updates", ".onboarded");
                if (File.Exists(marker)) return false;
                Directory.CreateDirectory(Path.GetDirectoryName(marker));
                File.WriteAllBytes(marker, Array.Empty<byte>());
                Log.Info("wrote " + marker + " - Ymir's update popup will not show (updates through LaunchBox)");
                return true;
            }
            catch (Exception ex) { Log.Warn("could not write Ymir's onboarding marker - its update popup will show", ex); return false; }
        }

        /// <summary>Ymir's first settings when it has none (header). True when written.</summary>
        public static bool Bootstrap(string exe)
        {
            try
            {
                var path = SettingsOf(exe);
                if (File.Exists(path)) return false;
                var lines = new[]
                {
                    "ConfigVersion = " + ConfigVersion.ToString(CultureInfo.InvariantCulture),
                    "",
                    "[General]",
                    "CheckForUpdates = false",
                    "RememberLastLoadedDisc = false",
                };
                File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
                Log.Info("wrote " + path + " - Ymir's first settings (it adds its defaults when it quits)");
                return true;
            }
            catch (Exception ex) { Log.Warn("could not write Ymir's first settings", ex); return false; }
        }

        // ── the keys ─────────────────────────────────────────────────────────

        /// <summary>A TOML list of strings as Ymir writes it ("[ 'F2', 'Ctrl+Alpha1' ]"), its strings.</summary>
        internal static List<string> ListOf(string raw)
        {
            var list = new List<string>();
            if (raw == null) return list;
            foreach (Match m in Regex.Matches(raw, @"'([^']*)'|""((?:[^""\\]|\\.)*)"""))
                list.Add(m.Groups[1].Success ? m.Groups[1].Value : YmirToml.Text("\"" + m.Groups[2].Value + "\""));
            return list;
        }

        internal static string ListText(IEnumerable<string> items)
        {
            var a = items.ToList();
            return a.Count == 0 ? "[]" : "[ " + string.Join(", ", a.Select(YmirToml.Literal)) + " ]";
        }

        private static bool Same(List<string> have, params string[] want)
            => have.Count == want.Length && have.Zip(want, (a, b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase)).All(x => x);

        /// <summary>The pack's keys bound where Ymir still has its defaults (header). What was bound; never throws.</summary>
        public static List<string> BindPackKeys(YmirTomlDoc doc)
        {
            var bound = new List<string>();
            try
            {
                const string States = "Hotkeys.SaveStates", Hot = "Hotkeys";
                var load = doc.Get(States, "QuickLoadState");
                if (load == null || Same(ListOf(load), "F3"))
                {
                    doc.Set(States, "QuickLoadState", ListText(new[] { "F4" }));
                    bound.Add("F4 load state");
                }
                var trace = doc.Get(Hot, "ToggleDebugTrace");
                var full = doc.Get(Hot, "ToggleFullScreen");
                var fullKeys = full == null ? new List<string> { "Alt+Return" } : ListOf(full);
                if (!fullKeys.Contains("F11", StringComparer.OrdinalIgnoreCase))
                {
                    bool traceDefault = trace == null || Same(ListOf(trace), "F11");
                    if (traceDefault) doc.Set(Hot, "ToggleDebugTrace", ListText(Array.Empty<string>()));
                    if (traceDefault || !Holds(doc, "F11"))
                    {
                        doc.Set(Hot, "ToggleFullScreen", ListText(fullKeys.Concat(new[] { "F11" })));
                        bound.Add("F11 full screen" + (traceDefault ? " (taken off the debug trace)" : ""));
                    }
                }
            }
            catch (Exception ex) { Log.Warn("hotkeys: could not bind the pack's keys", ex); }
            return bound;
        }

        /// <summary>Some action of Ymir's holds <paramref name="key"/> alone, in [Hotkeys] or one of its sub-tables.</summary>
        private static bool Holds(YmirTomlDoc doc, string key)
            => doc.Tables.Where(t => t.Name == "Hotkeys" || t.Name.StartsWith("Hotkeys.", StringComparison.Ordinal))
                  .SelectMany(YmirTomlDoc.Keys).Any(k => ListOf(k.Value).Contains(key, StringComparer.OrdinalIgnoreCase));

        /// <summary>The keys Ymir selects slot 1..10 with - what the AHK script sends for F6 / F7 (Ymir's defaults Alpha1..Alpha0).
        /// False when one was rebound: the script then cannot follow, said in the log.</summary>
        public static bool SlotKeysAreDefault(YmirTomlDoc doc)
        {
            for (int i = 1; i <= 10; i++)
            {
                var raw = doc.Get("Hotkeys.SaveStates", "SelectState" + i.ToString(CultureInfo.InvariantCulture));
                if (raw != null && !ListOf(raw).Contains("Alpha" + (i % 10).ToString(CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        // ── the discs ────────────────────────────────────────────────────────

        internal static bool IsArchive(string path)
        {
            var e = Path.GetExtension(path ?? "").ToLowerInvariant();
            return e == ".zip" || e == ".7z";
        }

        internal static bool IsNative(string path) => YmirDisc.Native.Contains(Path.GetExtension(path ?? "").ToLowerInvariant());

        /// <summary>A .m3u's discs, in order: each line a file (relative to the .m3u), # comments and blanks skipped.</summary>
        internal static List<string> M3uDiscs(string m3u)
        {
            var list = new List<string>();
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(m3u));
                foreach (var raw in File.ReadAllLines(m3u))
                {
                    var line = raw.Trim().Trim('﻿');
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    var p = Path.IsPathRooted(line) ? line : Path.GetFullPath(Path.Combine(dir, line));
                    list.Add(p);
                }
            }
            catch (Exception ex) { Log.Warn("could not read " + m3u, ex); }
            return list;
        }

        private static readonly Regex DiscTag = new Regex(@"\s*[\(\[]\s*(?:Disc|Disk|CD)\s*(?<n>\d+)(?:\s*of\s*\d+)?\s*[\)\]]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>A file name without its "(Disc N)" - the name the discs of one game share.</summary>
        internal static string WithoutDisc(string name) => DiscTag.Replace(name ?? "", "").Trim();

        internal static int? DiscNumber(string name)
        {
            var m = DiscTag.Match(name ?? "");
            return m.Success && int.TryParse(m.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : (int?)null;
        }

        /// <summary>The discs of the game <paramref name="disc"/> is one of: the .m3u's when it came from one, else the files
        /// beside it of the same name but its "(Disc N)", in their order - at least the disc itself.</summary>
        internal static List<string> DiscsOf(string disc, string m3u = null)
        {
            if (m3u != null) { var l = M3uDiscs(m3u).Where(File.Exists).ToList(); if (l.Count > 0) return l; }
            var list = new List<string> { disc };
            try
            {
                var name = Path.GetFileNameWithoutExtension(disc);
                if (DiscNumber(name) == null) return list;
                var stem = WithoutDisc(name);
                var ext = Path.GetExtension(disc);
                var others = Directory.EnumerateFiles(Path.GetDirectoryName(disc), "*" + ext)
                    .Where(p => DiscNumber(Path.GetFileNameWithoutExtension(p)) != null
                                && string.Equals(WithoutDisc(Path.GetFileNameWithoutExtension(p)), stem, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => DiscNumber(Path.GetFileNameWithoutExtension(p))).ToList();
                if (others.Count > 1) return others;
            }
            catch { }
            return list;
        }

        internal sealed class Plan
        {
            public string Disc;                       // what Ymir opens
            public List<string> Discs = new List<string>();
            public string Problem;
        }

        /// <summary>What Ymir is given for <paramref name="rom"/> (header). Problem said when it cannot be played.</summary>
        public static Plan DiscFor(string rom, string exe)
        {
            var plan = new Plan();
            try
            {
                if (string.IsNullOrEmpty(rom) || !File.Exists(rom)) { plan.Problem = "its file is not there: " + rom; return plan; }
                var ext = Path.GetExtension(rom).ToLowerInvariant();
                string m3u = null, disc = null;
                if (ext == ".m3u") m3u = rom;
                else if (IsArchive(rom))
                {
                    var dir = Unpack(rom, exe);
                    var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
                    m3u = files.FirstOrDefault(p => p.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase));
                    if (m3u == null)
                    {
                        // A .cue before its .bin; the first of each kind Ymir opens.
                        disc = files.FirstOrDefault(p => p.EndsWith(".cue", StringComparison.OrdinalIgnoreCase)) ?? files.FirstOrDefault(IsNative);
                        if (disc == null) { plan.Problem = Path.GetFileName(rom) + " holds no disc image Ymir opens (.chd, .cue, .ccd, .mds, .iso)."; return plan; }
                    }
                }
                else if (IsNative(rom)) disc = rom;
                else
                {
                    plan.Problem = "Ymir does not open " + ext + " files: give the game its .chd, .cue, .ccd, .mds or .iso"
                                   + (ext == ".bin" || ext == ".img" ? " (the .cue or .ccd beside the " + ext + ")." : ".");
                    return plan;
                }
                if (m3u != null)
                {
                    var discs = M3uDiscs(m3u);
                    disc = discs.FirstOrDefault();
                    if (disc == null) { plan.Problem = Path.GetFileName(m3u) + " lists no disc."; return plan; }
                    if (!File.Exists(disc)) { plan.Problem = Path.GetFileName(m3u) + " lists " + disc + ", which is not there."; return plan; }
                    if (!IsNative(disc)) { plan.Problem = Path.GetFileName(m3u) + " lists " + Path.GetFileName(disc) + ", which Ymir does not open."; return plan; }
                }
                plan.Disc = disc;
                plan.Discs = DiscsOf(disc, m3u);
            }
            catch (Exception ex) { Log.Warn("could not prepare " + rom, ex); plan.Problem = Path.GetFileName(rom) + " could not be opened: " + ex.Message; }
            return plan;
        }

        /// <summary>The archive unpacked whole, once (again when it is newer than what was unpacked).</summary>
        private static string Unpack(string archive, string exe)
        {
            using var md5 = MD5.Create();
            var hash = Convert.ToHexString(md5.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(archive).ToLowerInvariant()))).Substring(0, 8).ToLowerInvariant();
            var dir = Path.Combine(ProfileOf(exe), "Unpacked", Path.GetFileNameWithoutExtension(archive) + "-" + hash);
            var done = Path.Combine(dir, ".lbip-unpacked");
            if (File.Exists(done) && File.GetLastWriteTimeUtc(done) >= File.GetLastWriteTimeUtc(archive)) return dir;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            Directory.CreateDirectory(dir);
            var root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            using (var a = ArchiveFactory.Open(archive))
            {
                var entries = a.Entries.Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key)).ToList();
                long total = entries.Sum(e => e.Size), done2 = 0;
                foreach (var e in entries)
                {
                    var target = Path.GetFullPath(Path.Combine(dir, e.Key.Replace('/', '\\')));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Archive entry escapes the folder: " + e.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using var src = e.OpenEntryStream();
                    using var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                    LbIntegrations.Lbip.LbipLaunchStatus.CopyStream(src, dst, e.Size, "Extracting " + Path.GetFileName(target) + "…");
                    done2 += e.Size;
                }
                Log.Info("unpacked " + entries.Count + " file(s), " + (done2 >> 20) + " MB of " + (total >> 20) + ", from " + Path.GetFileName(archive) + " -> " + dir);
            }
            File.WriteAllBytes(done, Array.Empty<byte>());
            return dir;
        }

        /// <summary>The discs written down for the AHK script ("next disc"): the disc started first, then one per line.</summary>
        public static void WriteDiscs(string exe, Plan plan)
        {
            var path = Path.Combine(ProfileOf(exe), DiscsFile);
            try
            {
                int at = Math.Max(0, plan.Discs.FindIndex(d => string.Equals(Path.GetFullPath(d), Path.GetFullPath(plan.Disc), StringComparison.OrdinalIgnoreCase)));
                var lines = new List<string> { (at + 1).ToString(CultureInfo.InvariantCulture) };
                lines.AddRange(plan.Discs.Select(Path.GetFullPath));
                File.WriteAllLines(path, lines, new UTF8Encoding(true));
            }
            catch (Exception ex) { Log.Warn("could not write " + path + " - the next-disc key will do nothing", ex); }
        }

        // ── the line ─────────────────────────────────────────────────────────

        /// <summary>The line Ymir is started with (header): the user's switches kept, ours set, the game replaced by the disc.</summary>
        public static string CommandLine(string current, string profile, bool fullscreen, string oldRom, string disc)
        {
            var t = Tokenize(current);
            // Ours, put again below: -p / --profile and its folder, -f / --fullscreen, -d / --disc and its file.
            // Case matters (cxxopts): -P starts paused and -F fast-forwarded, and are the user's.
            for (int i = 0; i < t.Count; i++)
            {
                var a = t[i];
                if (a == "-p" || a == "--profile" || a == "-d" || a == "--disc") { t.RemoveAt(i); if (i < t.Count) t.RemoveAt(i); i--; continue; }
                if (a.StartsWith("--profile=") || a.StartsWith("--disc=") || a == "-f" || a == "--fullscreen") { t.RemoveAt(i); i--; }
            }
            string Full(string a) { try { return Path.GetFullPath(a.Replace('/', '\\')); } catch { return a; } }
            if (oldRom != null)
            {
                var full = Full(oldRom);
                t.RemoveAll(a => string.Equals(Full(a), full, StringComparison.OrdinalIgnoreCase));
            }
            var head = new List<string> { "-p", profile };
            if (fullscreen) head.Add("-f");
            return string.Join(" ", head.Concat(t).Concat(new[] { disc }).Select(Quote));
        }

        private static string Quote(string a) => a.Length == 0 || a.Any(char.IsWhiteSpace) || a.Contains('"') ? "\"" + a.Replace("\"", "\\\"") + "\"" : a;

        /// <summary>A command line cut as Windows does for a C program: spaces apart, quotes kept together.</summary>
        internal static List<string> Tokenize(string line)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(line)) return list;
            var cur = new StringBuilder();
            bool quoted = false, any = false;
            foreach (var c in line)
            {
                if (c == '"') { quoted = !quoted; any = true; continue; }
                if (char.IsWhiteSpace(c) && !quoted) { if (any || cur.Length > 0) list.Add(cur.ToString()); cur.Clear(); any = false; continue; }
                cur.Append(c);
            }
            if (any || cur.Length > 0) list.Add(cur.ToString());
            return list;
        }
    }
}
