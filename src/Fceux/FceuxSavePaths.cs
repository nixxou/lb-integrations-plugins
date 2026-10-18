// Nothing beside the game, and every platform's own folders (Mehdi, 07/10: "rien ne doit être sauvegardé à côté du jeu,
// mais dans des répertoires dédiés, par plateforme si possible, dans le répertoire de l'émulateur").
//
// THE QT BUILD HAS NO SETTING FOR ITS FOLDERS (drivers\Qt\config.cpp CreateDirs at the fceux-2.7.0 tag: fcs, snaps,
// gameinfo, sav, cheats... made in its base folder, the exe's - bin\; no "od*" override as the Windows build has). It never
// writes beside the game (measured 18/10). So, as Nixx-puNES does, its four folders of the game's files are made
// JUNCTIONS, pointed before each launch at the launching game's platform:
//
//     bin\sav      <FCEUX>\Saves\<platform>                   <rom>.sav, a disk's <rom>.fds
//     bin\fcs      <FCEUX>\Saves\<platform>\States            <rom>.fc0-.fc9 and FCEUX's backups of them
//     bin\snaps    <FCEUX>\Saves\<platform>\Screenshots       <rom>-<n>.png
//     bin\cheats   <FCEUX>\Saves\<platform>\Cheats            <rom>.cht
//
// MEASURED 18/10 (runqt2 of the lab, qfceux.exe 2.7.0, Zelda II): with the four junctions, F2 wrote States\Zelda...fc0,
// F8 Screenshots\Zelda...-0.png and the exit Zelda....sav INTO their targets; the junctions were still junctions after the
// exit (FCEUX's mkdir of an existing folder does nothing).
//
// WHAT IS EVER CHANGED, folder by folder (Ensure, only while FCEUX is closed):
//   missing                         a junction to the platform's folder
//   a junction into <FCEUX>\Saves\  pointed at the launching game's platform (only the LINK is removed, never its target)
//   any other junction or link      the user's choice (memory "no-saves-beside-games": a folder the user chose is kept):
//                                   never touched, listed as it is
//   a real folder (FCEUX ran before the plugin, or an install of the user's): its files MOVED into the platform folder of
//                                   the game they belong to (the library's games, by the name FCEUX gives them; else the
//                                   NES's), NEVER over a file already there: one that differs goes to <FCEUX>\Saves\Unsorted\,
//                                   so nothing is lost and the folder can become the junction. Only when junctions work on
//                                   that volume (one made and taken away first) - else nothing moves and FCEUX keeps its own
//                                   folders (noted in lbip-fceux-folders.txt).
//
// THE ROWS ALWAYS NAME THE REAL PATH (<FCEUX>\Saves\<platform>\x.sav), never the junction: a path LaunchBox keeps does
// not depend on which platform was launched last.
//
// THE NAME of a game's files is FileBase: the file name without its extension - for an archive, the ROM INSIDE it
// (measured on the Windows build, docs\fceux.md §8: the core's naming).

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Fceux
{
    internal static class FceuxSavePaths
    {
        public const string Root = "Saves", Unsorted = "Unsorted";

        /// <summary>FCEUX's folders of a game's files (drivers\Qt\config.cpp CreateDirs) and where each goes under the platform's.</summary>
        public const string Sav = "sav", States = "fcs", Shots = "snaps", Cheats = "cheats";
        internal static readonly (string Kind, string Sub)[] Kinds = { (Sav, ""), (States, "States"), (Shots, "Screenshots"), (Cheats, "Cheats") };

        /// <summary>Where the plugin notes that junctions could not be made for this install (FCEUX keeps its own folders).</summary>
        public const string NoteFile = "lbip-fceux-folders.txt";

        /// <summary>For the probe: the library is not asked (no LaunchBox).</summary>
        internal static Func<IEnumerable<(string Rom, string Platform)>> LibraryForProbe;

        // ── where ────────────────────────────────────────────────────────────

        /// <summary>The install's folder: the one holding bin\qfceux.exe (the zip's layout), else the exe's own.</summary>
        public static string InstallDir(string exe)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(exe));
            return string.Equals(Path.GetFileName(dir), "bin", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(dir) : dir;
        }

        private static string SubOf(string kind) => Kinds.First(k => k.Kind == kind).Sub;

        /// <summary>&lt;FCEUX&gt;\Saves\&lt;platform&gt;[\sub] for a kind of file.</summary>
        public static string For(string exe, string platform, string kind)
        {
            var dir = Path.Combine(InstallDir(exe), Root, FceuxSystems.FolderOf(platform));
            var sub = SubOf(kind);
            return sub.Length == 0 ? dir : Path.Combine(dir, sub);
        }

        /// <summary>Where FCEUX itself writes <paramref name="kind"/>: its base folder's, the exe's.</summary>
        public static string Live(string exe, string kind) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), kind);

        internal enum State { Missing, Ours, Foreign, Real }

        /// <summary>What FCEUX's folder of <paramref name="kind"/> is now, and where it leads.</summary>
        internal static State StateOf(string exe, string kind, out string target)
        {
            target = null;
            var live = Live(exe, kind);
            try
            {
                var info = new DirectoryInfo(live);
                if (!info.Exists && (info.Attributes == (FileAttributes)(-1) || !info.Attributes.HasFlag(FileAttributes.ReparsePoint))) return State.Missing;
                if (!info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return State.Real;
                target = info.LinkTarget;
                if (target == null) return State.Foreign;
                if (target.StartsWith(@"\??\", StringComparison.Ordinal)) target = target.Substring(4);
                var root = Path.Combine(InstallDir(exe), Root) + "\\";
                return Path.GetFullPath(target).StartsWith(root, StringComparison.OrdinalIgnoreCase) ? State.Ours : State.Foreign;
            }
            catch { return State.Foreign; }   // unreadable: never touched
        }

        /// <summary>Junctions known not to work for this install (NoteFile written by Ensure).</summary>
        public static bool NoJunctions(string exe)
        {
            try { return File.Exists(Path.Combine(InstallDir(exe), NoteFile)); } catch { return false; }
        }

        /// <summary>Every folder a game of <paramref name="platform"/> may have files of <paramref name="kind"/> in, the one a
        /// restore writes to first: its platform folder - and FCEUX's own when that is not one of ours (the user's link, a
        /// real folder not moved yet, an install where junctions do not work).</summary>
        public static List<string> Dirs(string exe, string platform, string kind)
        {
            var list = new List<string>();
            var mine = For(exe, platform, kind);
            var state = LbIntegrations.Saves.SaveScan.Memo("fceux.state|" + exe + "|" + kind, () => StateOf(exe, kind, out _));
            bool keepsOwn = state == State.Foreign || (state == State.Real && NoJunctions(exe));
            if (keepsOwn) list.Add(Live(exe, kind));
            list.Add(mine);
            if (state == State.Real && !keepsOwn) list.Add(Live(exe, kind));   // moved at the next launch
            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Where a restore of <paramref name="kind"/> for a game of <paramref name="platform"/> goes: where FCEUX reads
        /// it at that game's next launch.</summary>
        public static string Target(string exe, string platform, string kind) => Dirs(exe, platform, kind)[0];

        /// <summary>The name FCEUX gives a game's files (FileBase): the file's name without its extension, for an archive
        /// the one ROM inside it - null when that cannot be told.</summary>
        public static string NameOf(string rom)
        {
            if (string.IsNullOrWhiteSpace(rom)) return null;
            if (Archives.IsArchive(rom))
            {
                var inside = Archives.RomInside(rom);
                return inside == null ? null : Path.GetFileNameWithoutExtension(inside);
            }
            return Path.GetFileNameWithoutExtension(rom);
        }

        // ── at a launch ──────────────────────────────────────────────────────

        /// <summary>FCEUX's four folders made junctions to <paramref name="platform"/>'s (see the header). Never throws; never
        /// while FCEUX runs (the caller checks). Returns what was done, for the log.</summary>
        public static string Ensure(string exe, string platform)
        {
            var said = new List<string>();
            try
            {
                if (NoJunctions(exe)) return "junctions do not work for this install (" + NoteFile + "): FCEUX keeps its own folders";
                var states = Kinds.ToDictionary(k => k.Kind, k => (State: StateOf(exe, k.Kind, out var t), Target: t));
                if (states.Values.Any(s => s.State == State.Missing || s.State == State.Real) && !Probe(exe, out var why))
                {
                    Note(exe, why);
                    return "junctions cannot be made here (" + why + "): FCEUX keeps its own folders";
                }
                foreach (var (kind, _) in Kinds)
                {
                    var live = Live(exe, kind);
                    var target = For(exe, platform, kind);
                    var (state, now) = states[kind];
                    if (state == State.Foreign) { said.Add(kind + ": the user's link, kept"); continue; }
                    Directory.CreateDirectory(target);
                    if (state == State.Ours)
                    {
                        if (string.Equals(Path.GetFullPath(now).TrimEnd('\\'), Path.GetFullPath(target).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                        Directory.Delete(live);   // a junction: the link only, never what it leads to
                    }
                    else if (state == State.Real)
                    {
                        int moved = MoveOut(exe, kind, platform);
                        if (Directory.EnumerateFileSystemEntries(live).Any()) { said.Add(kind + ": " + moved + " moved, some could not be - FCEUX keeps it this time"); continue; }
                        Directory.Delete(live);
                        said.Add(kind + ": " + moved + " file(s) moved into Saves");
                    }
                    if (!Junction(live, target, out var error))
                    {
                        Directory.CreateDirectory(live);   // FCEUX makes it anyway; never left without one
                        said.Add(kind + ": no junction (" + error + ")");
                        continue;
                    }
                    said.Add(kind + " -> " + FceuxSystems.FolderOf(platform));
                }
            }
            catch (Exception ex) { Log.Warn("the save folders could not be set for " + platform, ex); said.Add("failed: " + ex.Message); }
            return said.Count == 0 ? "already " + FceuxSystems.FolderOf(platform) : string.Join(", ", said);
        }

        /// <summary>A junction made and taken away in the install: false (and why) when the volume does not have them.</summary>
        private static bool Probe(string exe, out string why)
        {
            why = null;
            var dir = InstallDir(exe);
            var target = Path.Combine(dir, Root);
            var link = Path.Combine(dir, "lbip-junction-test");
            try
            {
                Directory.CreateDirectory(target);
                if (Directory.Exists(link)) Directory.Delete(link);
                bool ok = Junction(link, target, out why);
                try { if (Directory.Exists(link)) Directory.Delete(link); } catch { }
                return ok;
            }
            catch (Exception ex) { why = ex.Message; return false; }
        }

        private static void Note(string exe, string why)
        {
            try
            {
                File.WriteAllText(Path.Combine(InstallDir(exe), NoteFile),
                    "; Nixx-FCEUX: FCEUX keeps its own sav, fcs, snaps and cheats folders here - junctions to Saves\\<platform>\\ could not be made.\r\n"
                    + "; Delete this file to try again at the next launch.\r\n" + DateTime.UtcNow.ToString("o") + " " + why + "\r\n");
                Log.Warn("save folders: junctions cannot be made in " + InstallDir(exe) + " (" + why + ") - FCEUX keeps its own folders");
            }
            catch { }
        }

        // ── a real folder emptied ────────────────────────────────────────────

        /// <summary>Every entry of FCEUX's real folder of <paramref name="kind"/> moved into the platform folder of the game
        /// it is for - never over a file there (one that differs goes to Saves\Unsorted). Returns how many moved.</summary>
        private static int MoveOut(string exe, string kind, string launching)
        {
            var live = Live(exe, kind);
            var owners = Owners();
            int moved = 0;
            foreach (var entry in Directory.EnumerateFileSystemEntries(live).ToList())
            {
                try
                {
                    var name = Path.GetFileName(entry);
                    var platform = PlatformOf(name, owners, launching);
                    var to = Path.Combine(For(exe, platform, kind), name);
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    bool isDir = Directory.Exists(entry) && !File.Exists(entry);
                    if (!isDir && File.Exists(to))
                    {
                        if (Same(entry, to)) { File.Delete(entry); moved++; continue; }
                        to = Free(Path.Combine(InstallDir(exe), Root, Unsorted, kind, name));
                    }
                    else if (isDir && Directory.Exists(to)) to = Free(Path.Combine(InstallDir(exe), Root, Unsorted, kind, name));
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    if (isDir) Directory.Move(entry, to); else File.Move(entry, to);
                    Log.Info("save folders: " + kind + "\\" + name + " -> " + Path.GetRelativePath(InstallDir(exe), to));
                    moved++;
                }
                catch (Exception ex) { Log.Warn("save folders: " + entry + " not moved", ex); }
            }
            return moved;
        }

        /// <summary>A name not taken yet: "x.sav", else "x (2).sav"...</summary>
        private static string Free(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path);
            var stem = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            for (int i = 2; ; i++)
            {
                var p = Path.Combine(dir, stem + " (" + i + ")" + ext);
                if (!File.Exists(p) && !Directory.Exists(p)) return p;
            }
        }

        private static bool Same(string a, string b)
        {
            try
            {
                var fa = new FileInfo(a); var fb = new FileInfo(b);
                if (fa.Length != fb.Length) return false;
                return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
            }
            catch { return false; }
        }

        /// <summary>The name FCEUX gives a game's files -> the platforms of the library's games of that name. Archives are
        /// named by their own name here (opening every archive of a library is not done for this rare move); a game whose
        /// ROM is named otherwise inside goes to the NES's folder, where a launch of it finds nothing and FCEUX makes anew.</summary>
        private static Dictionary<string, List<string>> Owners()
        {
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            void Add(string rom, string platform)
            {
                if (string.IsNullOrWhiteSpace(rom) || string.IsNullOrWhiteSpace(platform)) return;
                string name;
                try { name = Path.GetFileNameWithoutExtension(rom.Trim().Trim('"')); } catch { return; }
                if (name.Length == 0) return;
                if (!map.TryGetValue(name, out var list)) map[name] = list = new List<string>();
                if (!list.Contains(platform, StringComparer.OrdinalIgnoreCase)) list.Add(platform);
            }
            try
            {
                if (LibraryForProbe != null) { foreach (var (r, p) in LibraryForProbe()) Add(r, p); return map; }
                foreach (var g in PluginHelper.DataManager?.GetAllGames() ?? Array.Empty<IGame>())
                {
                    string platform = null, rom = null;
                    try { platform = g.Platform; rom = g.ApplicationPath; } catch { continue; }
                    if (FceuxSystems.Platform(platform) == null) continue;
                    Add(rom, platform);
                    try { foreach (var a in g.GetAllAdditionalApplications() ?? Array.Empty<IAdditionalApplication>()) Add(a?.ApplicationPath, platform); } catch { }
                }
            }
            catch (Exception ex) { Log.Warn("save folders: the library could not be read - the files go to the NES's folder", ex); }
            return map;
        }

        /// <summary>The game a file of FCEUX's folders is for: "x.sav", "x.fds", "x.fc3", "x.fc3.png", "x-bak.fc3",
        /// "x.bak.fc0", "x-resume.fcs", "x-autosave2.fcs", "x-12.png", "x.cht" -&gt; "x".</summary>
        internal static string GameOf(string fileName)
        {
            var m = Regex.Match(fileName ?? "", @"^(?<g>.+?)(?:(?:-bak|\.bak)?\.fc\d(?:\.png)?|-resume\.fcs|-autosave\d+\.fcs|-\d+\.png|\.sav|\.fds|\.cht)$",
                                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return m.Success ? m.Groups["g"].Value : Path.GetFileNameWithoutExtension(fileName ?? "");
        }

        /// <summary>The platform a file of FCEUX's folders belongs to: the library's game of its name (the launching game's
        /// platform when it is one of them), else the NES's.</summary>
        internal static string PlatformOf(string fileName, Dictionary<string, List<string>> owners, string launching)
        {
            var stem = GameOf(fileName);
            if (owners != null && owners.TryGetValue(stem, out var platforms) && platforms.Count > 0)
            {
                var mine = platforms.FirstOrDefault(p => string.Equals(p, launching, StringComparison.OrdinalIgnoreCase));
                if (mine != null) return mine;
                return platforms.OrderBy(p => { int i = Array.FindIndex(FceuxSystems.Platforms, x => string.Equals(x.Platform, p, StringComparison.OrdinalIgnoreCase)); return i < 0 ? 99 : i; }).First();
            }
            return FceuxSystems.NesPlatform;
        }

        // ── the junction ─────────────────────────────────────────────────────

        /// <summary>A directory junction <paramref name="link"/> -&gt; <paramref name="target"/> (a mount-point reparse point,
        /// FSCTL_SET_REPARSE_POINT - what "mklink /J" makes; no privilege needed, NTFS only). The link must not exist.</summary>
        internal static bool Junction(string link, string target, out string error)
        {
            error = null;
            bool made = false;
            try
            {
                target = Path.GetFullPath(target);
                Directory.CreateDirectory(link);
                made = true;
                var sub = Encoding.Unicode.GetBytes(@"\??\" + target);
                var print = Encoding.Unicode.GetBytes(target);
                int pathBytes = sub.Length + 2 + print.Length + 2;
                var buf = new byte[8 + 8 + pathBytes];
                BitConverter.GetBytes(0xA0000003u).CopyTo(buf, 0);                 // IO_REPARSE_TAG_MOUNT_POINT
                BitConverter.GetBytes((ushort)(8 + pathBytes)).CopyTo(buf, 4);     // ReparseDataLength
                BitConverter.GetBytes((ushort)0).CopyTo(buf, 8);                   // SubstituteNameOffset
                BitConverter.GetBytes((ushort)sub.Length).CopyTo(buf, 10);         // SubstituteNameLength
                BitConverter.GetBytes((ushort)(sub.Length + 2)).CopyTo(buf, 12);   // PrintNameOffset
                BitConverter.GetBytes((ushort)print.Length).CopyTo(buf, 14);       // PrintNameLength
                sub.CopyTo(buf, 16);
                print.CopyTo(buf, 16 + sub.Length + 2);
                using (var h = CreateFileW(link, GenericWrite, 0, IntPtr.Zero, OpenExisting, BackupSemantics | OpenReparsePoint, IntPtr.Zero))
                {
                    if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (!DeviceIoControl(h, FsctlSetReparsePoint, buf, buf.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                if (!new DirectoryInfo(link).Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("the link is not a junction after it was made");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                try { if (made && Directory.Exists(link) && !Directory.EnumerateFileSystemEntries(link).Any()) Directory.Delete(link); } catch { }
                return false;
            }
        }

        private const uint GenericWrite = 0x40000000, OpenExisting = 3, BackupSemantics = 0x02000000, OpenReparsePoint = 0x00200000, FsctlSetReparsePoint = 0x000900A4;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] inBuffer, int inSize, IntPtr outBuffer, int outSize, out int returned, IntPtr overlapped);
    }
}
