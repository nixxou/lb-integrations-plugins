// The folder ZiNc EX is given for a launch: the game's own, or one made of links under ZiNc's names.
//
// ZiNc EX checks the game's set, its parent and its BIOS as <name>.zip in ONE folder, under ZiNc's names (games.c
// rom_available: exit code 3 "ROM files missing" otherwise), and ZiNc then finds each ROM by its CRC32 in those zips.
// A romset LaunchBox has is often not there under those names (ZincGames: MAME renamed most of them), so:
//   - when every zip the game needs is in one folder under ZiNc's names - a classic ZiNc folder - that folder is given
//     as it is (and the CHD subfolders the Konami GV games want are there with it);
//   - else lbip-roms\ beside ZiNc-EX.exe, the game's zips put there under ZiNc's names, the romset itself standing in
//     for a parent or a BIOS whose ROMs it carries (a MAME non-merged set holds them all: ZincGames.Identify checked).
//
// NOTHING IS WRITTEN BESIDE THE GAMES, and nothing of the user's is ever moved or deleted: what lbip-roms\ holds is a
// hard link (same volume: no space, no copy), else a symbolic link (allowed unprivileged in Windows' developer mode),
// else a copy - kept from one launch to the next while the original keeps its size and date, a romset of another drive
// is not copied again each time. What the plugin put there is listed in lbip-roms\lbip-served.txt, and only that is
// ever taken away again - a link removed is the link, never its file.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace LbIntegrations.ZincEx
{
    internal static class ZincRoms
    {
        public const string FolderName = "lbip-roms";
        public const string ListName = "lbip-served.txt";

        /// <summary>The folder to give ZiNc EX for <paramref name="match"/>, or null with <paramref name="problem"/> said. Never throws.</summary>
        public static string Prepare(string exeDir, ZincGames.Match match, out string problem)
        {
            problem = null;
            try
            {
                // Every zip under its own name, in one folder: that folder.
                var dirs = match.Zips.Values.Select(p => Path.GetDirectoryName(Path.GetFullPath(p))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                bool ownNames = match.Zips.All(kv => string.Equals(Path.GetFileNameWithoutExtension(kv.Value), kv.Key, StringComparison.OrdinalIgnoreCase));
                if (dirs.Count == 1 && ownNames) return dirs[0];

                var folder = Path.Combine(exeDir, FolderName);
                Directory.CreateDirectory(folder);
                var served = ReadList(folder);
                var now = new Dictionary<string, (string Kind, string Source)>(StringComparer.OrdinalIgnoreCase);
                // What the last launch put and this one does not need: taken away.
                foreach (var old in served.Keys.Where(n => !match.Zips.ContainsKey(Path.GetFileNameWithoutExtension(n))).ToList())
                    Remove(folder, old, served);
                foreach (var kv in match.Zips)
                {
                    var name = kv.Key + ".zip";
                    var kind = Serve(folder, name, kv.Value, served);
                    now[name] = (kind, kv.Value);
                }
                foreach (var kv in served.Where(s => !now.ContainsKey(s.Key))) now[kv.Key] = kv.Value;
                WriteList(folder, now);
                Log.Info("served in " + folder + ": " + string.Join(", ", match.Zips.Select(kv => kv.Key + ".zip <- " + Path.GetFileName(kv.Value) + " (" + now[kv.Key + ".zip"].Kind + ")")));
                return folder;
            }
            catch (Exception ex)
            {
                Log.Warn("could not make the ROMs folder for " + match?.Game?.Set, ex);
                problem = "The ROMs could not be made ready for ZiNc EX: " + ex.Message;
                return null;
            }
        }

        /// <summary><paramref name="source"/> in <paramref name="folder"/> as <paramref name="name"/>: kept when it already is
        /// (a copy still of the same size and date), else made again. Returns how: "hard link", "symbolic link" or "copy".</summary>
        private static string Serve(string folder, string name, string source, Dictionary<string, (string Kind, string Source)> served)
        {
            var target = Path.Combine(folder, name);
            if (served.TryGetValue(name, out var was) && File.Exists(target) && ZincGames.SamePath(was.Source, source) && StillTheSame(target, source, was.Kind))
                return was.Kind;
            if (File.Exists(target) || Directory.Exists(target))
            {
                // Not ours to take away (not in the list): never deleted - refused.
                if (!served.ContainsKey(name)) throw new IOException(target + " is there and was not put there by the plugin");
                Remove(folder, name, served);
            }
            if (SameVolume(source, folder) && CreateHardLink(target, source, IntPtr.Zero)) return "hard link";
            try { File.CreateSymbolicLink(target, source); if (File.Exists(target)) return "symbolic link"; }
            catch (Exception ex) { Log.Verbose("no symbolic link for " + name + " - " + ex.Message); }
            File.Copy(source, target, overwrite: false);
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(source));
            return "copy";
        }

        /// <summary>A symbolic link follows its path. A copy - and a hard link, which is the file the path had when it was made,
        /// not a newer file put there since - is the same while it has the original's size and date.</summary>
        private static bool StillTheSame(string target, string source, string kind)
        {
            try
            {
                if (kind == "symbolic link") return true;
                var t = new FileInfo(target); var s = new FileInfo(source);
                return s.Exists && t.Length == s.Length && t.LastWriteTimeUtc == s.LastWriteTimeUtc;
            }
            catch { return false; }
        }

        /// <summary>One entry the plugin put in <paramref name="folder"/> taken away - a link, never what it points to.</summary>
        private static void Remove(string folder, string name, Dictionary<string, (string Kind, string Source)> served)
        {
            var path = Path.Combine(folder, name);
            if (!served.ContainsKey(name) || !ZincExConfig.SameFolder(Path.GetDirectoryName(Path.GetFullPath(path)), folder)) return;
            try { if (File.Exists(path)) File.Delete(path); }   // a hard link: one name of the file less; a symbolic link: the link
            catch (Exception ex) { Log.Warn("could not take " + path + " away", ex); return; }
            served.Remove(name);
        }

        private static bool SameVolume(string a, string b)
        {
            try { return string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private static Dictionary<string, (string Kind, string Source)> ReadList(string folder)
        {
            var d = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var path = Path.Combine(folder, ListName);
                if (!File.Exists(path)) return d;
                foreach (var line in File.ReadAllLines(path))
                {
                    var f = line.Split('\t');
                    if (f.Length == 3 && f[0].EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && f[0].IndexOfAny(new[] { '\\', '/' }) < 0) d[f[0]] = (f[1], f[2]);
                }
            }
            catch (Exception ex) { Log.Warn("could not read " + ListName, ex); }
            return d;
        }

        private static void WriteList(string folder, Dictionary<string, (string Kind, string Source)> list)
            => File.WriteAllLines(Path.Combine(folder, ListName), list.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => kv.Key + "\t" + kv.Value.Kind + "\t" + kv.Value.Source));

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
    }
}
