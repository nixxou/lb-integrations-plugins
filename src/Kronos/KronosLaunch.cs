// What a launch decides and writes for Kronos: which install, its settings, the disc, its keys, the line.
//
// THE EXE is kronos.exe (docs\kronos.md §1) - Tradu-France's Windows build (Benjamin Siskoo's). Claimed by its name.
//
// ITS SETTINGS (§3): kronos.ini BESIDE the exe when there is one - the 2.7.5 zip ships an empty one, Kronos's portable mode -
// else Kronos's own in %AppData%; none at all: one is made beside the exe (nothing the user set is lost that way).
// Everything in the [1.0] section (KronosIniDoc). ITS FIRST SETTINGS (Bootstrap), each only when absent:
//   Advanced\SH2Interpreter=8   the SH-2 interpreter - an almost empty ini fails with "Cannot initialize SH2" (measured 19/10).
//
// THE PACK'S KEYS (BindPackKeys), each only where it still holds Kronos's default - Kronos's own digit shortcuts (Ctrl+0-9
// save to a slot, Alt+0-9 load it) do NOTHING (measured 19/10: Ctrl+2 wrote no state, twice), rebound keys work (F8,
// Shift+F5, Ctrl+F6, Alt+F7, Ctrl+Shift+F9 each wrote its slot):
//   Save<n> -> Ctrl+Shift+F<n>, Load<n> -> Shift+F<n> (slot 0 on F10) - what the AHK script sends (KronosAhk):
//   F2 save state / F4 load state  in the slot the script counts (1 at the start), F6 / F7 slot down / up
//   F11 full screen                Kronos's Alt+Enter, sent by the script
//   PageDown / PageUp              the next / previous disc, the script through Kronos's Eject/Load ISO (Ctrl+I)
//   Esc quit                       the script: WM_CLOSE (Kronos quits cleanly, measured 19/10); Kronos has no quit on Escape
// The script's F2 and F4 catch the keys: Kronos's own (pause, vertical sync) never see them.
//
// THE DISC: what Kronos opens (KronosDisc.Native - .chd .cue .ccd .mds .iso; §6). A .m3u is read here (Kronos has no
// playlist): its first disc is given. An archive (.zip, .7z) is unpacked whole into <install>\Unpacked\<name>-<hash>\ - a
// .cue's .bin files with it - and its .m3u, else its first disc, given. THE DISCS OF A GAME (for "next disc"): the .m3u's,
// else the files beside the disc named the same but for "(Disc N)"; written in lbip-discs.txt beside the exe for the script.
//
// THE LINE: -a [-f] --bios=<file> -i "<disc>" and what the user put on the platform's line - the game LaunchBox appended is
// replaced by the disc. Kronos's switches are VOLATILE (§2): nothing of them is written to kronos.ini.

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

namespace LbIntegrations.Kronos
{
    internal static class KronosLaunch
    {
        public const string ExecutableName = "kronos.exe";
        public const string IniName = "kronos.ini";
        public const string DiscsFile = "lbip-discs.txt";
        /// <summary>Where the plugin keeps the Saturn BIOS for Kronos, beside the exe (Kronos is given one with --bios=).</summary>
        public const string BiosFolder = "bios";

        // ── the exe ──────────────────────────────────────────────────────────

        public static bool IsKronosExe(string path)
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

        /// <summary>The kronos.ini this Kronos reads (header).</summary>
        public static string SettingsOf(string exe)
        {
            var beside = Path.Combine(ProfileOf(exe), IniName);
            if (File.Exists(beside)) return beside;
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                foreach (var candidate in new[] { Path.Combine(appData, "kronos", IniName), Path.Combine(appData, "Kronos", "kronos", IniName) })
                    if (File.Exists(candidate)) return candidate;
            }
            catch { }
            return beside;
        }

        public static string BiosDirOf(string exe) => Path.Combine(ProfileOf(exe), BiosFolder);

        /// <summary>Where the ROM cartridges a game's window offers are (KronosGameOptions' Expansion): Kronos's "cart" folder.</summary>
        public static string CartDirOf(string exe) => Path.Combine(ProfileOf(exe), "cart");

        // ── its settings ─────────────────────────────────────────────────────

        /// <summary>Its first settings, each only when absent (header). True when the file changed.</summary>
        public static bool Bootstrap(KronosIniDoc doc)
        {
            bool changed = false;
            if (doc.GetRaw(@"Advanced\SH2Interpreter") == null) changed |= doc.SetRaw(@"Advanced\SH2Interpreter", "8");
            return changed;
        }

        /// <summary>Slot <paramref name="n"/> (0-9) as the keys name it: F1-F9, slot 0 on F10.</summary>
        internal static int FKeyOf(int n) => n == 0 ? 10 : n;

        public static string SaveKey(int n) => "Ctrl+Shift+F" + FKeyOf(n);
        public static string LoadKey(int n) => "Shift+F" + FKeyOf(n);

        /// <summary>The slot keys the AHK script sends, bound where Kronos's default is (header). What was bound.</summary>
        public static List<string> BindPackKeys(KronosIniDoc doc)
        {
            var bound = new List<string>();
            for (int n = 0; n <= 9; n++)
            {
                foreach (var (action, key) in new[] { ("Save" + n, SaveKey(n)), ("Load" + n, LoadKey(n)) })
                {
                    var name = @"Shortcuts\" + action;
                    if (doc.GetRaw(name) != null) continue;   // the user's, or ours already
                    doc.Set(name, key);
                    bound.Add(action + "=" + key);
                }
            }
            return bound;
        }

        /// <summary>Every slot key is the one the script sends.</summary>
        public static bool SlotKeysAreOurs(KronosIniDoc doc)
        {
            for (int n = 0; n <= 9; n++)
                if (!string.Equals(doc.Get(@"Shortcuts\Save" + n), SaveKey(n), StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(doc.Get(@"Shortcuts\Load" + n), LoadKey(n), StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        // ── the discs ────────────────────────────────────────────────────────

        internal static bool IsArchive(string path)
        {
            var e = Path.GetExtension(path ?? "").ToLowerInvariant();
            return e == ".zip" || e == ".7z";
        }

        internal static bool IsNative(string path) => KronosDisc.Native.Contains(Path.GetExtension(path ?? "").ToLowerInvariant());

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
            public string Disc;                       // what Kronos opens
            public List<string> Discs = new List<string>();
            public string Problem;
        }

        /// <summary>What Kronos is given for <paramref name="rom"/> (header). Problem said when it cannot be played.</summary>
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
                        // A .cue before its .bin; the first of each kind Kronos opens.
                        disc = files.FirstOrDefault(p => p.EndsWith(".cue", StringComparison.OrdinalIgnoreCase)) ?? files.FirstOrDefault(IsNative);
                        if (disc == null) { plan.Problem = Path.GetFileName(rom) + " holds no disc image Kronos opens (.chd, .cue, .ccd, .mds, .iso)."; return plan; }
                    }
                }
                else if (IsNative(rom)) disc = rom;
                else
                {
                    plan.Problem = "Kronos does not open " + ext + " files: give the game its .chd, .cue, .ccd, .mds or .iso"
                                   + (ext == ".bin" || ext == ".img" ? " (the .cue or .ccd beside the " + ext + ")." : ".");
                    return plan;
                }
                if (m3u != null)
                {
                    var discs = M3uDiscs(m3u);
                    disc = discs.FirstOrDefault();
                    if (disc == null) { plan.Problem = Path.GetFileName(m3u) + " lists no disc."; return plan; }
                    if (!File.Exists(disc)) { plan.Problem = Path.GetFileName(m3u) + " lists " + disc + ", which is not there."; return plan; }
                    if (!IsNative(disc)) { plan.Problem = Path.GetFileName(m3u) + " lists " + Path.GetFileName(disc) + ", which Kronos does not open."; return plan; }
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
        /// <summary>One line per disc: its path, then a tab and the name the next-disc key says when <paramref name="shownAs"/>
        /// gives one (a disc handed over as another file - a CHD served as K:\game.chd says its own name).</summary>
        public static void WriteDiscs(string exe, Plan plan, IList<string> shownAs = null)
        {
            var path = Path.Combine(ProfileOf(exe), DiscsFile);
            try
            {
                int at = Math.Max(0, plan.Discs.FindIndex(d => string.Equals(Path.GetFullPath(d), Path.GetFullPath(plan.Disc), StringComparison.OrdinalIgnoreCase)));
                var lines = new List<string> { (at + 1).ToString(CultureInfo.InvariantCulture) };
                lines.AddRange(plan.Discs.Select((d, i) => Path.GetFullPath(d) + (shownAs != null && i < shownAs.Count && !string.IsNullOrEmpty(shownAs[i]) ? "\t" + shownAs[i] : "")));
                File.WriteAllLines(path, lines, new UTF8Encoding(true));
            }
            catch (Exception ex) { Log.Warn("could not write " + path + " - the next-disc key will do nothing", ex); }
        }

        // ── the line ─────────────────────────────────────────────────────────

        /// <summary>The line Kronos is started with (header): the user's switches kept, ours set, the game replaced by the disc.</summary>
        public static string CommandLine(string current, bool fullscreen, string bios, string oldRom, string disc)
        {
            var t = Tokenize(current);
            // Ours, put again below: -a / --autostart, -f / --fullscreen, -i / --iso= and its file, -b / --bios= and its file.
            for (int i = 0; i < t.Count; i++)
            {
                var a = t[i];
                if (a == "-i" || a == "-b") { t.RemoveAt(i); if (i < t.Count) t.RemoveAt(i); i--; continue; }
                if (a == "-a" || a == "--autostart" || a == "-f" || a == "--fullscreen" || a.StartsWith("--iso=") || a.StartsWith("--bios=")) { t.RemoveAt(i); i--; }
            }
            string Full(string a) { try { return Path.GetFullPath(a.Replace('/', '\\')); } catch { return a; } }
            if (oldRom != null)
            {
                var full = Full(oldRom);
                t.RemoveAll(a => string.Equals(Full(a), full, StringComparison.OrdinalIgnoreCase));
            }
            var head = new List<string> { "-a" };
            if (fullscreen) head.Add("-f");
            if (!string.IsNullOrEmpty(bios)) head.Add("--bios=" + bios);
            return string.Join(" ", head.Concat(t).Concat(new[] { "-i", disc }).Select(Quote));
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
