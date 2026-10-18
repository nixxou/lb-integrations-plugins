// Where puNES's saves, states, disk writes and screenshots live: <puNES>\Saves\<platform>\<folder>, one folder per
// platform (Mehdi, 07/10, memory "no-saves-beside-games": "dans des répertoires dédiés (par plateforme si possible) dans
// le répertoire de l'émulateur").
//
// puNES HAS NO SETTING FOR THEM: it writes into fixed folders of its data folder - prb\ (cartridge saves), save\ (states),
// diff\ (a disk's writes), screenshot\ - its exe's folder when portable (src\core\common.h:35-48, src\gui\qt.cpp:343-372,
// v0.111; the whole puNES.cfg read: no path key for any of them). So those four names are made JUNCTIONS, pointed before
// each launch at the launching game's platform folder:
//
//     <puNES>\prb         ->  <puNES>\Saves\Nintendo Entertainment System\prb
//     <puNES>\save        ->  <puNES>\Saves\Nintendo Entertainment System\save        (and so on)
//
// MEASURED 18/10 (run4 of the lab, punes64.exe 0.111, Zelda II): with the four folders junctions, F1 wrote save\Zelda...p00
// and WM_CLOSE wrote prb\Zelda...prb INTO the junctions' targets, and the junctions were still junctions after the exit -
// puNES creates a missing folder at start and leaves an existing one as it is.
//
// WHAT IS EVER CHANGED, folder by folder (Ensure, only while puNES is closed):
//   missing                          a junction to the platform's folder
//   a junction into <puNES>\Saves\   pointed at the launching game's platform (only the LINK is removed, never its target)
//   any other junction or link       the user's choice (memory "no-saves-beside-games": a folder the user chose is kept):
//                                    never touched, listed as it is
//   a real folder (puNES ran before the plugin, or an install of the user's): its files MOVED into the platform folder of
//                                    the game they belong to (the library's games, by the name puNES gives them; else the
//                                    NES - a disk's writes the Famicom Disk System), NEVER over a file already there: one
//                                    that differs goes to <puNES>\Saves\Unsorted\<folder>\, so nothing is lost and the folder
//                                    can become the junction. Only when junctions work on that volume (Probe: one made and
//                                    taken away first) - else nothing moves and puNES keeps its own folders (noted in
//                                    lbip-punes-folders.txt; listed there).
//
// puNES STARTED BY HAND keeps writing into the folders of the platform last launched from LaunchBox - its own files under
// another platform's name until the game is launched from LaunchBox again; nothing is lost.
//
// THE ROWS ALWAYS NAME THE REAL PATH (<puNES>\Saves\<platform>\prb\x.prb), never the junction: a path LaunchBox keeps does
// not depend on which platform was launched last.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Punes
{
    internal static class PunesSaveDirs
    {
        /// <summary>puNES's own folder names (common.h: PRB_FOLDER, SAVE_FOLDER, DIFF_FOLDER, SCRSHT_FOLDER).</summary>
        public const string Prb = "prb", States = "save", Diff = "diff", Shots = "screenshot";

        public static readonly string[] Kinds = { Prb, States, Diff, Shots };

        public const string Root = "Saves", Unsorted = "Unsorted";

        /// <summary>Where the plugin notes that junctions could not be made for this install (puNES keeps its own folders).</summary>
        public const string NoteFile = "lbip-punes-folders.txt";

        /// <summary>For the probe: the library is not asked (no LaunchBox).</summary>
        internal static Func<IEnumerable<(string Name, string Platform)>> LibraryForProbe;

        // ── where ────────────────────────────────────────────────────────────

        public static string InstallDir(string exe) => Path.GetDirectoryName(Path.GetFullPath(exe));

        /// <summary>The folder name a platform gets: its name, what Windows refuses in a file name made '_'. An empty one is the
        /// NES's (the main platform).</summary>
        public static string FolderName(string platform)
        {
            var name = (platform ?? "").Trim();
            if (name.Length == 0) name = PunesSystems.Platforms[0].Platform;
            var bad = Path.GetInvalidFileNameChars();
            var s = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
            return s.Length == 0 ? "_" : s;
        }

        /// <summary>&lt;puNES&gt;\Saves\&lt;platform&gt;\&lt;kind&gt;.</summary>
        public static string For(string exe, string platform, string kind) => Path.Combine(InstallDir(exe), Root, FolderName(platform), kind);

        /// <summary>Where puNES itself writes <paramref name="kind"/>: its data folder's (PunesCfg.DataDir).</summary>
        public static string Live(string exe, string kind) => Path.Combine(PunesCfg.DataDir(exe), kind);

        internal enum State { Missing, Ours, Foreign, Real }

        /// <summary>What puNES's folder of <paramref name="kind"/> is now, and where it leads.</summary>
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
        /// restore writes to first: its platform folder - and puNES's own when that is not one of ours (the user's link,
        /// a real folder not moved yet, an install where junctions do not work).</summary>
        public static List<string> Dirs(string exe, string platform, string kind)
        {
            var list = new List<string>();
            var mine = For(exe, platform, kind);
            var state = LbIntegrations.Saves.SaveScan.Memo("punes.state|" + exe + "|" + kind, () => StateOf(exe, kind, out _));
            bool keepsOwn = state == State.Foreign || (state == State.Real && NoJunctions(exe));
            if (keepsOwn) list.Add(Live(exe, kind));
            list.Add(mine);
            if (state == State.Real && !keepsOwn) list.Add(Live(exe, kind));   // moved at the next launch
            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Where a restore of <paramref name="kind"/> for a game of <paramref name="platform"/> goes: where puNES reads
        /// it at that game's next launch.</summary>
        public static string Target(string exe, string platform, string kind) => Dirs(exe, platform, kind)[0];

        // ── at a launch ──────────────────────────────────────────────────────

        /// <summary>puNES's four folders made junctions to <paramref name="platform"/>'s (see the header). Never throws; never
        /// while puNES runs (the caller checks). Returns what was done, for the log.</summary>
        public static string Ensure(string exe, string platform)
        {
            var said = new List<string>();
            try
            {
                if (NoJunctions(exe)) return "junctions do not work for this install (" + NoteFile + "): puNES keeps its own folders";
                var states = Kinds.ToDictionary(k => k, k => (State: StateOf(exe, k, out var t), Target: t));
                if (states.Values.Any(s => s.State == State.Missing || s.State == State.Real) && !Probe(exe, out var why))
                {
                    Note(exe, why);
                    return "junctions cannot be made here (" + why + "): puNES keeps its own folders";
                }
                foreach (var kind in Kinds)
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
                        if (Directory.EnumerateFileSystemEntries(live).Any()) { said.Add(kind + ": " + moved + " moved, some could not be - puNES keeps it this time"); continue; }
                        Directory.Delete(live);
                        said.Add(kind + ": " + moved + " file(s) moved into Saves");
                    }
                    if (!Junction(live, target, out var error))
                    {
                        Directory.CreateDirectory(live);   // puNES makes it anyway; never left without one
                        said.Add(kind + ": no junction (" + error + ")");
                        continue;
                    }
                    said.Add(kind + " -> " + FolderName(platform));
                }
            }
            catch (Exception ex) { Log.Warn("the save folders could not be set for " + platform, ex); said.Add("failed: " + ex.Message); }
            return said.Count == 0 ? "already " + FolderName(platform) : string.Join(", ", said);
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
                    "; Nixx-puNES: puNES keeps its own prb, save, diff and screenshot folders here - junctions to Saves\\<platform>\\ could not be made.\r\n"
                    + "; Delete this file to try again at the next launch.\r\n" + DateTime.UtcNow.ToString("o") + " " + why + "\r\n");
                Log.Warn("save folders: junctions cannot be made in " + InstallDir(exe) + " (" + why + ") - puNES keeps its own folders");
            }
            catch { }
        }

        // ── a real folder emptied ────────────────────────────────────────────

        /// <summary>Every entry of puNES's real folder of <paramref name="kind"/> moved into the platform folder of the game it
        /// is for - never over a file there (one that differs goes to Saves\Unsorted). Returns how many moved.</summary>
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
                    var platform = PlatformOf(name, kind, owners, launching);
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

        /// <summary>A name not taken yet: "x.prb", else "x (2).prb"...</summary>
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

        /// <summary>The name puNES gives a game's files (its played file without its last extension), lower case -> the
        /// platforms of the library's games of that name.</summary>
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
                if (LibraryForProbe != null) { foreach (var (n, p) in LibraryForProbe()) Add(n, p); return map; }
                var dm = PluginHelper.DataManager;
                foreach (var g in dm?.GetAllGames() ?? Array.Empty<IGame>())
                {
                    string platform = null, rom = null;
                    try { platform = g.Platform; rom = g.ApplicationPath; } catch { continue; }
                    var known = PunesSystems.Platform(platform) != null;
                    if (!known && PunesSystems.OfExtension(Path.GetExtension(rom ?? "")) == null && !PunesSystems.IsArchive(rom)) continue;
                    Add(rom, platform);
                    if (!known) continue;
                    try { foreach (var a in g.GetAllAdditionalApplications() ?? Array.Empty<IAdditionalApplication>()) Add(a?.ApplicationPath, platform); } catch { }
                }
            }
            catch (Exception ex) { Log.Warn("save folders: the library could not be read - the files go to the NES's folder", ex); }
            return map;
        }

        /// <summary>The platform a file of puNES's folder belongs to: the library's game of its name (the launching game's
        /// platform when it is one of them), else the NES's - a disk's writes the Famicom Disk System's.</summary>
        internal static string PlatformOf(string fileName, string kind, Dictionary<string, List<string>> owners, string launching)
        {
            var name = fileName;
            if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);   // a state's thumbnail
            var stem = Path.GetFileNameWithoutExtension(name);
            // A screenshot is "<game>_000001.png" (qt.cpp gui_save_screenshot: completeBaseName + "_%06d.png").
            var shot = kind == Shots ? System.Text.RegularExpressions.Regex.Match(fileName, @"^(?<game>.*)_\d{6}\.png$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) : null;
            if (shot != null && shot.Success) stem = shot.Groups["game"].Value;
            if (owners != null && owners.TryGetValue(stem, out var platforms) && platforms.Count > 0)
            {
                var mine = platforms.FirstOrDefault(p => string.Equals(p, launching, StringComparison.OrdinalIgnoreCase));
                if (mine != null) return mine;
                return platforms.OrderBy(p => { int i = Array.FindIndex(PunesSystems.Platforms, x => string.Equals(x.Platform, p, StringComparison.OrdinalIgnoreCase)); return i < 0 ? 99 : i; }).First();
            }
            bool disk = kind == Diff || name.EndsWith(".ipf", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".ipq", StringComparison.OrdinalIgnoreCase);
            return disk ? PunesSystems.Platforms.First(p => p.Default == PunesSystems.Fds).Platform : PunesSystems.Platforms[0].Platform;
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
                var substitute = @"\??\" + target;
                var sub = Encoding.Unicode.GetBytes(substitute);
                var print = Encoding.Unicode.GetBytes(target);
                // REPARSE_DATA_BUFFER, MountPointReparseBuffer: tag, data length, reserved, then the four offsets / lengths
                // and the two names, each followed by a 16-bit NUL.
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
                var info = new DirectoryInfo(link);
                if (!info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("the link is not a junction after it was made");
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
