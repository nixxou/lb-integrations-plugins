// ZiNc EX's ROMs folder, pointed at the game's for the length of a launch, then given back.
//
// ZINC EX READS ONE FOLDER. Its command line ("ZiNc-EX.exe --game <set or ROM file>", settings.c run_game_cli) keeps only
// the set's NAME of a path given to it - "the extension and the folder are ignored" (its readme) - then checks that the
// set, its parent and its BIOS are zips of the ROMs folder of its settings (games.c rom_available) and hands that folder
// to ZiNc (--roms-directory=). There is no option for another folder on a launch. So the plugin writes the folder it
// wants into zinc-settings.cfg ("roms_directory=", settings.c ini_fields; UTF-8, a key=value per line, "[ZiNc-EX]" and
// ";" comments skipped) before the launch, and puts the user's back when the game is over (ZincExSession). The CLI path
// reads the settings and writes none (run_game_cli never calls settings_save), so nothing of ZiNc EX's is lost.
//
// THE USER'S VALUE IS KEPT ON DISK while it is away - lbip-roms-directory.txt beside ZiNc-EX.exe: "present", "absent" or
// "nofile" (no zinc-settings.cfg at all: the one the launch makes is taken away again), then the value - so a LaunchBox
// that dies mid-game still gives it back, at the next launch or the next time the plugin looks (Restore).
// While that file is there, the value in zinc-settings.cfg is ours, never taken for the user's.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.ZincEx
{
    internal static class ZincExConfig
    {
        public const string FileName = "zinc-settings.cfg";
        public const string Key = "roms_directory";
        public const string KeptName = "lbip-roms-directory.txt";

        /// <summary>ZiNc EX's default (settings.c settings_defaults): roms\ beside it.</summary>
        public const string DefaultFolder = "roms";

        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>The user's ROMs folder as a full path: the one kept aside while a launch has it, else zinc-settings.cfg's,
        /// else ZiNc EX's default. A relative one is from ZiNc EX's folder, as ZiNc EX takes it (main.c roms_dir_abs).</summary>
        public static string UserFolder(string exeDir)
        {
            string value;
            if (ReadKept(exeDir, out var present, out var kept)) value = present ? kept : null;
            else value = Read(exeDir);
            if (string.IsNullOrWhiteSpace(value)) value = DefaultFolder;
            try { return Path.IsPathRooted(value) ? Path.GetFullPath(value) : Path.GetFullPath(Path.Combine(exeDir, value)); }
            catch { return null; }
        }

        /// <summary>roms_directory= set to <paramref name="folder"/>, the user's value kept aside first (unless it already is:
        /// an earlier launch that never gave it back). False when nothing was written - the folder is already the user's.
        /// Throws on an I/O failure.</summary>
        public static bool Point(string exeDir, string folder)
        {
            if (!ReadKept(exeDir, out _, out _))
            {
                var current = Read(exeDir);
                if (current != null && SameFolder(Resolve(exeDir, current), folder)) return false;
                var state = !File.Exists(Path.Combine(exeDir, FileName)) ? "nofile" : current == null ? "absent" : "present";
                var keep = new[] { state, current ?? "" };
                WriteAtomic(Path.Combine(exeDir, KeptName), string.Join("\r\n", keep) + "\r\n");
            }
            Write(exeDir, folder);
            Log.Info("ZiNc EX's ROMs folder set to " + folder + " for this launch");
            return true;
        }

        /// <summary>The user's value put back, when one is kept aside - and nothing else. Never throws.</summary>
        public static void Restore(string exeDir)
        {
            try
            {
                if (string.IsNullOrEmpty(exeDir) || !ReadKept(exeDir, out var present, out var value)) return;
                Write(exeDir, present ? value : null);
                // There was no zinc-settings.cfg: the one the launch made goes again, when nothing else was written into it.
                if (NoFile(exeDir)) TakeAwayIfEmpty(exeDir);
                File.Delete(Path.Combine(exeDir, KeptName));
                Log.Info("ZiNc EX's ROMs folder given back: " + (present ? value : "(none - ZiNc EX's default)"));
            }
            catch (Exception ex) { Log.Warn("could not give ZiNc EX's ROMs folder back - kept in " + KeptName + " for the next try", ex); }
        }

        /// <summary>Is a value kept aside - a launch has the folder, or one ended without giving it back?</summary>
        public static bool Pointed(string exeDir) => !string.IsNullOrEmpty(exeDir) && File.Exists(Path.Combine(exeDir, KeptName));

        // ── zinc-settings.cfg ────────────────────────────────────────────────

        /// <summary>roms_directory's value in zinc-settings.cfg - the last one, as ZiNc EX reads it - or null when it has none.</summary>
        internal static string Read(string exeDir)
        {
            var path = Path.Combine(exeDir, FileName);
            if (!File.Exists(path)) return null;
            string value = null;
            foreach (var line in File.ReadAllLines(path, Utf8))
                if (IsKey(line, out var v)) value = v;
            return value;
        }

        /// <summary>roms_directory= set (null: taken out), every other line as it was. The first such line is replaced, any
        /// other dropped; none, and it is added at the end.</summary>
        private static void Write(string exeDir, string value)
        {
            var path = Path.Combine(exeDir, FileName);
            var lines = File.Exists(path) ? File.ReadAllLines(path, Utf8).ToList() : new List<string>();
            var output = new List<string>();
            bool done = false;
            foreach (var line in lines)
            {
                if (!IsKey(line, out _)) { output.Add(line); continue; }
                if (!done && value != null) output.Add(Key + "=" + value);
                done = true;
            }
            if (!done && value != null) output.Add(Key + "=" + value);
            WriteAtomic(path, string.Join("\r\n", output) + "\r\n");
        }

        private static bool IsKey(string line, out string value)
        {
            value = null;
            var t = (line ?? "").Trim();
            if (t.Length == 0 || t[0] == ';' || t[0] == '#' || t[0] == '[') return false;
            var at = t.IndexOf('=');
            if (at <= 0 || !string.Equals(t.Substring(0, at).Trim(), Key, StringComparison.OrdinalIgnoreCase)) return false;
            value = t.Substring(at + 1).Trim();
            return true;
        }

        private static bool NoFile(string exeDir)
        {
            try { var l = File.ReadAllLines(Path.Combine(exeDir, KeptName), Utf8); return l.Length > 0 && l[0].Trim() == "nofile"; }
            catch { return false; }
        }

        private static void TakeAwayIfEmpty(string exeDir)
        {
            var path = Path.Combine(exeDir, FileName);
            if (File.Exists(path) && File.ReadAllLines(path, Utf8).All(l => l.Trim().Length == 0)) File.Delete(path);
        }

        private static bool ReadKept(string exeDir, out bool present, out string value)
        {
            present = false; value = null;
            try
            {
                var path = Path.Combine(exeDir ?? "", KeptName);
                if (!File.Exists(path)) return false;
                var lines = File.ReadAllLines(path, Utf8);
                present = lines.Length > 0 && lines[0].Trim() == "present";
                value = present && lines.Length > 1 ? lines[1] : null;
                return true;
            }
            catch { return false; }
        }

        /// <summary>A temporary file beside the target, then put in its place: a failure half way leaves the old file whole.</summary>
        private static void WriteAtomic(string path, string text)
        {
            var temp = path + ".lbip-tmp";
            File.WriteAllText(temp, text, Utf8);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        private static string Resolve(string exeDir, string value)
        {
            try { return Path.IsPathRooted(value) ? Path.GetFullPath(value) : Path.GetFullPath(Path.Combine(exeDir, value)); }
            catch { return value; }
        }

        internal static bool SameFolder(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }
    }
}
