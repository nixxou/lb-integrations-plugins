// What a launch decides and writes: the SYSTEM ares runs the game as, the options in settings.bml, and the line.
//
// THE SYSTEM, three levels (Mehdi, 06/10), the first that says:
//   1. the game's own choice in its window (AresSettings.GameSystem) - "Mega CD 32X" for a Sega CD game that needs it;
//   2. the platform's line in LaunchBox - each platform row of the emulator carries --system "<name>" in its command
//      line (set when the plugin creates the emulator, editable in Edit Emulator > Associated Platforms, as RetroArch's
//      cores); LaunchBox hands it to us inside CurrentCommandLine;
//   3. the plugin's table (AresSystems): an extension that says it (.32x, .sgx, .ndd, .fds), else the platform's default.
// --system is ALWAYS given: without it ares guesses from the extension (program\load.cpp, Program::identify) and cannot
// tell .bin, .cue or .chd apart, nor ever pick Neo Geo, Mega CD 32X, SuperGrafx CD, Dendy or ZX Spectrum 128.
//
// THE LINE: --settings-file <the install's settings.bml> (so %LOCALAPPDATA%\ares\ can never take over), --system,
// --fullscreen or --pseudofullscreen, --no-file-prompt for a CARTRIDGE system (else ares opens a file dialog for a 64DD
// disk, a Super Game Boy or Sufami Turbo slot...), then the game. Anything else the user put on the line is kept.
// NOT for a disc system: --no-file-prompt also silences the dialog of System > Change Disc (desktop-ui\emulator\
// emulator.cpp, Emulator::load: the dialog only "else if(!program.noFilePrompt)"), the one way ares changes discs - and a
// disc system asks for no second medium at start. The dialog opens in the game's own folder: ares keeps
// <System>/Path = the folder of the last game it loaded, the one on the line included.
//
// THE OPTIONS (AresSettings.All) go on the line, --setting "<node>=<value>" each (desktop-ui.cpp v148: set after the file
// is read, the last of a key wins, and put back to the file's value when ares quits - measured 06/10 on all of them: a
// window size passed so is the window's, and settings.bml came back byte for byte). So a game's choice never stays for
// the next game, and "as set in ares" is ares's own. What has to last is written in settings.bml: the state hotkeys,
// bound when empty (ares ships
// none (input\hotkeys.cpp) - F5 saves, F7 loads, as for bsnes; the binding format is "0x1/0/<key>;;" (keyboard,
// F1..F12 = 1..12 - RetroBat's Ares.Generator.cs).
//
// FILES ares cannot open: a .7z (it reads zip, gzip, chd, cue - nall\decode) is unpacked when it holds a cartridge, under
// the archive's name, into a STABLE folder of the install - <ares>\Unpacked\<name>-<hash of the archive's path>\ - because
// ares writes the game's saves and states beside the file it is given (Paths/Saves empty): they live there, are found
// there by AresSaves, and are never deleted - only the ROM is replaced when the archive changes. A .m3u (no playlist
// support) gives its first disc.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using SharpCompress.Archives;

namespace LbIntegrations.Ares
{
    internal static class AresLaunch
    {
        public const string ExecutableName = "ares.exe";
        public const string SaveKey = "0x1/0/2;;";   // F2
        public const string LoadKey = "0x1/0/4;;";   // F4

        /// <summary>The hotkeys bound when ares has none - RetroArch's keys (Mehdi, 06/10): F2 save state, F4 load it, F6 / F7
        /// the slot down / up, F11 full screen (Mehdi, 07/10: F11 rather than RetroArch's F), Esc quit, P pause, H reset, F8 screenshot, F9 mute, R rewind (with ares's
        /// Rewind option on). Keyboard = device 0x1, group 0, the key's index in ruby's list (keyboard\rawinput.cpp: Escape 0,
        /// F1..F12 1..12, F 40, H 42, P 50, R 52).</summary>
        internal static readonly (string Node, string Key)[] Hotkeys =
        {
            ("SaveState", SaveKey), ("LoadState", LoadKey), ("DecrementStateSlot", "0x1/0/6;;"), ("IncrementStateSlot", "0x1/0/7;;"),
            ("ToggleFullscreen", "0x1/0/11;;"), ("QuitEmulator", "0x1/0/0;;"), ("PauseEmulation", "0x1/0/50;;"), ("ResetSystem", "0x1/0/42;;"),
            ("CaptureScreenshot", "0x1/0/8;;"), ("MuteAudio", "0x1/0/9;;"), ("Rewind", "0x1/0/52;;"),
        };

        /// <summary>What an earlier build of this plugin bound (F5/F7, F6/F8, F11): replaced by the keys above - a key the
        /// user bound himself is never touched.</summary>
        private static readonly Dictionary<string, string> Superseded = new Dictionary<string, string>
        {
            ["SaveState"] = "0x1/0/5;;", ["LoadState"] = "0x1/0/7;;", ["DecrementStateSlot"] = "0x1/0/6;;", ["IncrementStateSlot"] = "0x1/0/8;;",
            ["ToggleFullscreen"] = "0x1/0/40;;",   // F, bound by the build of 06/10
        };

        public static bool IsAresExe(string path)
        {
            try { return string.Equals(Path.GetFileName((path ?? "").Trim().Trim('"')), ExecutableName, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        public static string SettingsOf(string exe) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), AresBml.FileName);

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

        // ── the system ───────────────────────────────────────────────────────

        /// <summary>The system a game runs as, and where that came from ("game", "platform line", "extension", "platform").</summary>
        public static (string System, string From) SystemFor(string gameId, string platform, string rom, string currentLine)
        {
            var own = AresSettings.GameSystem(gameId);
            if (own != null) return (own, "the game's window");
            var given = SystemOnLine(currentLine);
            var p = AresSystems.Platform(platform);
            var ext = Path.GetExtension(rom ?? "");
            bool byExtension = p != null && p.ByExtension.TryGetValue(ext, out _);
            // The line the PLUGIN writes is the platform's default: an extension that says otherwise (.32x among Genesis
            // games) still wins over it. A line the user changed to another system is the user's and wins.
            if (given != null && !(byExtension && string.Equals(given, p.Default, StringComparison.Ordinal)))
            {
                if (AresSystems.System(given) == null) Log.Info("--system \"" + given + "\" on the line is not in the plugin's table - passed to ares as it is");
                return (given, "the platform's command line");
            }
            if (byExtension) return (p.ByExtension[ext], "the file's extension");
            if (p != null) return (p.Default, "the platform");
            return (null, null);
        }

        /// <summary>The value of --system on a line, or null.</summary>
        public static string SystemOnLine(string line)
        {
            var t = Tokenize(line);
            int at = t.FindIndex(a => a == "--system");
            return at >= 0 && at + 1 < t.Count ? t[at + 1] : null;
        }

        // ── the options ──────────────────────────────────────────────────────

        public static void PrepareSettings(string exe, string gameId)
        {
            try
            {
                // The options are not written here any more: they go on the line (AresSettings.SettingArgs). Only what has to
                // last is: the state hotkeys, bound when ares has none.
                var bml = AresBml.Load(SettingsOf(exe));
                // ares ships every hotkey unbound (Mehdi, 06/10: "no key works"): the usual ones, each only when ares has none.
                // Keyboard = device 0x1, group 0, the key's index in ruby's list (keyboard\rawinput.cpp: Escape 0, F1..F12 1..12).
                foreach (var (node, key) in Hotkeys)
                {
                    var v = bml.Get("Hotkey/" + node);
                    bool ours = Superseded.TryGetValue(node, out var old) && string.Equals(v?.Trim(), old, StringComparison.Ordinal);
                    if (string.IsNullOrWhiteSpace(v) || v.Trim(';').Length == 0 || ours) bml.Set("Hotkey/" + node, key);
                }
                bool changed = bml.Changed;
                bml.SaveIfChanged();
                if (changed) Log.Info("settings.bml: hotkeys bound as RetroArch's (F2/F4 states, F6/F7 slot, F11 full screen, Esc quit, P pause, H reset)");
            }
            catch (Exception ex) { Log.Warn("settings.bml could not be written - the game starts with it as it was", ex); }
        }

        // ── the line ─────────────────────────────────────────────────────────

        public static string CommandLine(string current, string exe, string system, string screen, string oldRom, string newRom, bool noFilePrompt = true,
                                         IEnumerable<string> settings = null)
        {
            var t = Tokenize(current);
            // Ours out, wherever they were.
            for (int i = 0; i < t.Count; i++)
            {
                if (t[i] == "--system" || t[i] == "--settings-file") { t.RemoveAt(i); if (i < t.Count) t.RemoveAt(i); i--; continue; }
                if (t[i] == "--fullscreen" || t[i] == "--pseudofullscreen" || t[i] == "--no-file-prompt") { t.RemoveAt(i); i--; }
            }
            if (newRom != null && oldRom != null)
            {
                // The game LaunchBox put on the line, however it spelt it (relative, forward slashes): compared as full
                // paths, else the last token that is a path - never a second game added beside the first.
                string Full(string a) { try { return Path.GetFullPath(a.Replace('/', '\\')); } catch { return a; } }
                var full = Full(oldRom);
                int at = t.FindLastIndex(a => string.Equals(Full(a), full, StringComparison.OrdinalIgnoreCase));
                if (at < 0) at = t.FindLastIndex(a => !a.StartsWith("--") && (a.Contains('\\') || a.Contains('/') || a.Contains('.')));
                if (at >= 0) t[at] = newRom; else t.Add(newRom);
            }
            var head = new List<string> { "--settings-file", SettingsOf(exe) };
            if (system != null) { head.Add("--system"); head.Add(system); }
            if (screen == "full") head.Add("--fullscreen");
            else if (screen == "pseudo") head.Add("--pseudofullscreen");
            if (noFilePrompt) head.Add("--no-file-prompt");
            // The game's options, BEFORE what the user put on the platform's line: for ares the last --setting of a key wins.
            foreach (var s in settings ?? Enumerable.Empty<string>()) { head.Add("--setting"); head.Add(s); }
            return string.Join(" ", head.Concat(t).Select(Quote));
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

        // ── files ares cannot open ───────────────────────────────────────────

        /// <summary>For a .7z: the cartridge inside, unpacked under the archive's name; for a .m3u: its first disc. Any other
        /// file as it is. Null with <paramref name="problem"/> said when it cannot be played.</summary>
        /// <summary>Where a .7z of <paramref name="rom"/> is unpacked for the ares at <paramref name="exe"/>: one folder per
        /// archive path, so two archives of the same name never share it.</summary>
        public static string UnpackDir(string exe, string rom)
        {
            var name = Path.GetFileNameWithoutExtension(rom);
            using var md5 = System.Security.Cryptography.MD5.Create();
            var hash = Convert.ToHexString(md5.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(rom).ToLowerInvariant()))).Substring(0, 8).ToLowerInvariant();
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), "Unpacked", name + "-" + hash);
        }

        /// <summary>The file a .7z was or would be unpacked to, when it is there (for the saves beside it), or null.</summary>
        public static string Unpacked(string exe, string rom)
        {
            try
            {
                var dir = UnpackDir(exe, rom);
                var name = Path.GetFileNameWithoutExtension(rom);
                return Directory.Exists(dir) ? Directory.GetFiles(dir, name + ".*").FirstOrDefault(f => AresSystems.All.Any(s => !s.Disc && s.Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))) : null;
            }
            catch { return null; }
        }

        /// <summary>A disc image with no .cue (an .iso, a .bin, an .img): ares opens a disc only through a .cue or a .chd
        /// (mia\medium: cue, chd), and follows no absolute path in a .cue's FILE (measured 06/10) - so a "&lt;name&gt;.cue"
        /// is written naming it relatively, one track, its mode read off the content. NOT BESIDE THE GAME (Mehdi, 07/10 -
        /// lot 9, 13/10): in the install's own &lt;ares&gt;\Discs\&lt;name&gt;-&lt;hash of the image's path&gt;\ (DiscDir),
        /// beside a link to the image under the image's own name - a symbolic link (it needs Windows' developer mode or the right
        /// to make one), else a hard link when both are on one volume - so the .cue's relative FILE reaches it and the
        /// medium's name, the one ares names the saves after, is the game's as before. Only when neither link can be made does
        /// the .cue go beside the image, hidden, as before 13/10, said in the log.
        /// Measured on Sonic CD (Mega CD): a 2048-byte ISO through such a .cue boots; the ISO alone does not.
        /// WHAT IT CANNOT GIVE BACK (Mehdi, 06/10: "qui vivra verra"): a cooked (2048) image has no CD audio tracks and, on
        /// the PlayStation, no XA / Form 2 sectors; a raw image of a whole disc has its audio tracks, but no record of where
        /// each starts - all of it is one track then. Said in the log; the game is launched anyway.</summary>
        /// <summary>The discs a .m3u lists, full paths, in order (they may not all be there).</summary>
        internal static List<string> M3uDiscs(string m3u)
        {
            var list = new List<string>();
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(m3u));
                foreach (var raw in File.ReadAllLines(m3u))
                {
                    var l = raw.Trim().Trim('﻿');
                    if (l.Length == 0 || l.StartsWith("#")) continue;
                    list.Add(Path.IsPathRooted(l) ? l : Path.GetFullPath(Path.Combine(dir, l)));
                }
            }
            catch (Exception ex) { Log.Warn("could not read " + m3u, ex); }
            return list;
        }

        /// <summary>The file ares opens for a disc: its .cue when it is an .iso/.bin/.img with one beside it, else itself. Only
        /// its NAME is read by the callers (the saves are named after it) - and the .cue the plugin makes in the install's
        /// Discs\ folder bears the image's name too.</summary>
        internal static string DiscFile(string disc, AresSystem system)
        {
            var e = Path.GetExtension(disc ?? "").ToLowerInvariant();
            if (system == null || !system.Disc || !(e == ".iso" || e == ".bin" || e == ".img")) return disc;
            var cue = Path.Combine(Path.GetDirectoryName(disc), Path.GetFileNameWithoutExtension(disc) + ".cue");
            return File.Exists(cue) ? cue : disc;
        }

        /// <summary>Where the .cues of a game are made for the ares at <paramref name="exe"/>: one folder per game path (the
        /// image's, or a .m3u's for all its discs: ares's Change Disc dialog opens in the folder of the disc it last loaded,
        /// and must show the others), as UnpackDir.</summary>
        public static string DiscDir(string exe, string image)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            var hash = Convert.ToHexString(md5.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(image).ToLowerInvariant()))).Substring(0, 8).ToLowerInvariant();
            // The hash alone names the folder: the image's name is in it once already, and a long Redump name twice would pass
            // MAX_PATH, which ares does not handle (review 13/10).
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), "Discs", hash);
        }

        /// <summary>The .cue text that names <paramref name="file"/> in <paramref name="mode"/> - one track, as CueFor writes it.</summary>
        internal static string CueText(string file, string mode) => "FILE \"" + file + "\" BINARY\r\n  TRACK 01 " + mode + "\r\n    INDEX 01 00:00:00\r\n";

        /// <summary>A .cue this plugin wrote beside an image before 13/10: hidden, and exactly our one-track text naming it.
        /// The user's own .cue is never taken for one.</summary>
        internal static bool IsOurOldCue(string cue, string image)
        {
            try
            {
                if (!File.Exists(cue) || (File.GetAttributes(cue) & FileAttributes.Hidden) == 0 || new FileInfo(cue).Length > 512) return false;
                var text = File.ReadAllText(cue);
                return new[] { "MODE1/2048", "MODE1/2352", "MODE2/2352" }.Any(m => text == CueText(Path.GetFileName(image), m));
            }
            catch { return false; }
        }

        /// <summary>The .cue ares opens for <paramref name="image"/>. <paramref name="exe"/> null (an older caller): beside the
        /// image, as before 13/10.</summary>
        internal static string CueFor(string image, out string problem, string exe = null, string game = null)
        {
            problem = null;
            var dir = Path.GetDirectoryName(image);
            var beside = Path.Combine(dir, Path.GetFileNameWithoutExtension(image) + ".cue");
            // The user's own .cue beside the image is theirs to keep: ares opens it, nothing of ours is made.
            if (File.Exists(beside) && (exe == null || !IsOurOldCue(beside, image))) return beside;
            try
            {
                long size = new FileInfo(image).Length;
                var head = new byte[16];
                using (var s = File.OpenRead(image)) s.Read(head, 0, 16);
                bool sync = head[0] == 0 && head[11] == 0 && head.Skip(1).Take(10).All(b => b == 0xFF);
                string mode;
                if (sync && size % 2352 == 0) mode = head[15] == 2 ? "MODE2/2352" : "MODE1/2352";
                else if (size % 2048 == 0) mode = "MODE1/2048";
                else if (size % 2352 == 0) mode = "MODE2/2352";
                else { problem = Path.GetFileName(image) + " is not a disc image ares can read (" + size + " bytes: neither 2048- nor 2352-byte sectors)."; return null; }

                if (mode == "MODE1/2048") Log.Info(Path.GetFileName(image) + ": a 2048-byte image - no CD audio tracks in it, nor XA sectors for a PlayStation game");
                else
                {
                    // Audio after the data: the last sector carries no sync - the disc's audio tracks, one track for ares.
                    var tail = new byte[12];
                    using (var s = File.OpenRead(image)) { s.Seek(size - 2352, SeekOrigin.Begin); s.Read(tail, 0, 12); }
                    bool tailSync = tail[0] == 0 && tail[11] == 0 && tail.Skip(1).Take(10).All(b => b == 0xFF);
                    if (!tailSync) Log.Info(Path.GetFileName(image) + ": audio after the data track, with no .cue saying where each track starts - read as one track");
                }
                if (exe != null && InDiscDir(exe, game ?? image, image, mode) is string ours)
                {
                    // Ours of before 13/10 beside the image goes: the one in the install now takes its place.
                    if (File.Exists(beside) && IsOurOldCue(beside, image))
                        try { File.SetAttributes(beside, FileAttributes.Normal); File.Delete(beside); Log.Info("removed " + beside + " - the plugin's own .cue of before, now in " + Path.GetDirectoryName(ours)); }
                        catch (Exception ex) { Log.Warn("could not remove the old " + beside, ex); }
                    return ours;
                }
                if (File.Exists(beside)) return beside;   // ours of before, and no link could be made: kept, as it was
                File.WriteAllText(beside, CueText(Path.GetFileName(image), mode));
                try { File.SetAttributes(beside, File.GetAttributes(beside) | FileAttributes.Hidden); } catch { }
                Log.Warn("wrote " + beside + " (hidden, " + mode + ") BESIDE the game: no link to the image could be made in the install's "
                         + "Discs folder (another volume, and no right to make a symbolic link - Windows' developer mode gives it)");
                return beside;
            }
            catch (Exception ex)
            {
                Log.Warn("could not write a .cue beside " + image, ex);
                problem = "ares opens a disc only through a .cue, and none could be written beside " + Path.GetFileName(image) + ": " + ex.Message;
                return null;
            }
        }

        /// <summary>The .cue of <paramref name="image"/> in the DiscDir of <paramref name="game"/>, beside a link to the image under the image's name;
        /// null when no link can be made. A link left from an earlier launch is kept while it is still the image (same size
        /// and date - a hard link is the very file; a file replaced since has another), else made again.</summary>
        private static bool NoteSource(string link, string source)
        {
            try { File.WriteAllText(link + SourceSuffix, source); return true; }
            catch (Exception ex) { Log.Info("Discs: the source of " + link + " could not be noted (" + ex.Message + ") - no hard link"); return false; }
        }

        private static string InDiscDir(string exe, string game, string image, string mode)
        {
            var dir = DiscDir(exe, game);
            var link = Path.Combine(dir, Path.GetFileName(image));
            var cue = Path.Combine(dir, Path.GetFileNameWithoutExtension(image) + ".cue");
            try
            {
                var src = new FileInfo(image);
                // Two discs of one .m3u with one name (CD1\Game.iso and CD2\Game.iso, or Game.iso and Game.bin): the shared folder
                // holds the other's - this one gets a folder of its own (review 13/10: the second launch re-pointed the first's).
                if (!string.Equals(game, image, StringComparison.OrdinalIgnoreCase))
                {
                    var other = LinkSource(link);
                    bool cueOfAnother = File.Exists(cue) && !File.ReadAllText(cue).StartsWith("FILE \"" + Path.GetFileName(image) + "\"", StringComparison.Ordinal);
                    if (cueOfAnother || (other != null && !string.Equals(other, src.FullName, StringComparison.OrdinalIgnoreCase) && File.Exists(other)))
                        return InDiscDir(exe, image, image, mode);
                }
                Directory.CreateDirectory(dir);
                bool good = false;
                if (File.Exists(link))
                {
                    var l = new FileInfo(link);
                    if (l.LinkTarget != null)
                        // A symbolic link: good while it names the image.
                        good = string.Equals(Path.GetFullPath(Path.Combine(dir, l.LinkTarget)), src.FullName, StringComparison.OrdinalIgnoreCase);
                    else
                        // A hard link IS the file it was made from: an image replaced since is another file under that name - the
                        // link then still holds the old one. Told by the file's own identity (volume and file index), not its size.
                        good = SameFile(link, src.FullName);
                    // Deleting a link never touches the image: a hard link's file lives on under the image's name.
                    if (!good) DeleteLink(link);
                }
                if (!good)
                {
                    // A hard link first when the image is on the install's volume: it IS the file, every reader sees it as one (a
                    // symbolic link's size is read as 0 by a C runtime that does not follow it - review 13/10, ares unmeasured on
                    // one). It keeps the image's bytes alive while it exists, so its source is noted beside it and SweepDiscs
                    // removes it once the image is gone or replaced. A symbolic link otherwise (another volume; it needs
                    // Windows' developer mode or the right to make one).
                    string how = null;
                    // The source noted FIRST (review 13/10): a hard link without it is never swept, and would keep a deleted
                    // image's bytes for good. No note, no hard link.
                    if (!NoHardLinkForTests && NoteSource(link, src.FullName))
                    {
                        if (CreateHardLink(link, src.FullName, IntPtr.Zero)) how = "hard link";
                        else try { File.Delete(link + SourceSuffix); } catch { }
                    }
                    if (how == null)
                    {
                        try { if (NoSymlinkForTests) throw new UnauthorizedAccessException("the probe forbids it"); File.CreateSymbolicLink(link, src.FullName); how = "symbolic link"; }
                        catch (Exception ex)
                        {
                            Log.Info("Discs: no link to " + src.FullName + " can be made (hard: another volume or refused; symbolic: " + ex.Message + ")");
                            try { if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
                            return null;
                        }
                    }
                    Log.Info("Discs: " + how + " " + link + " -> " + src.FullName);
                }
                var text = CueText(Path.GetFileName(image), mode);
                if (!File.Exists(cue) || File.ReadAllText(cue) != text) { File.WriteAllText(cue, text); Log.Info("wrote " + cue + " (" + mode + ") - ares opens a disc only through a .cue"); }
                return cue;
            }
            catch (Exception ex) { Log.Warn("Discs: could not make the .cue of " + image + " in " + dir, ex); return null; }
        }

        /// <summary>The probe's stand-ins for a machine without the right to a symbolic link / an image on another volume.</summary>
        internal static bool NoSymlinkForTests, NoHardLinkForTests;

        /// <summary>Beside a hard link: the full path of the image it was made from (SweepDiscs).</summary>
        internal const string SourceSuffix = ".source";

        /// <summary>The links of the install's Discs\ folder whose image is gone or replaced, removed with their .cue - a hard
        /// link would keep a deleted image's bytes on the disk. Empty folders go too. Never throws; never touches an image.</summary>
        public static void SweepDiscs(string exe)
        {
            try
            {
                var root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), "Discs");
                if (!Directory.Exists(root)) return;
                foreach (var dir in Directory.GetDirectories(root))
                {
                    foreach (var link in Directory.GetFiles(dir).Where(f => !f.EndsWith(".cue", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(SourceSuffix, StringComparison.OrdinalIgnoreCase)))
                    {
                        try
                        {
                            var l = new FileInfo(link);
                            bool gone;
                            if (l.LinkTarget != null) gone = !File.Exists(Path.GetFullPath(Path.Combine(dir, l.LinkTarget)));
                            else if (File.Exists(link + SourceSuffix))
                            {
                                // A hard link of ours (its source noted): gone when its image is no longer this very file.
                                var source = File.ReadAllText(link + SourceSuffix).Trim();
                                gone = !File.Exists(source) || !SameFile(link, source);
                            }
                            // Neither: not a link of ours - a file ares wrote here (a save, with Paths/Saves emptied in ares), or
                            // anybody's. NEVER deleted (review 13/10); the launch's migration brings ares's own out.
                            else continue;
                            if (!gone) continue;
                            DeleteLink(link);
                            foreach (var f in new[] { link + SourceSuffix, Path.Combine(dir, Path.GetFileNameWithoutExtension(link) + ".cue") })
                                if (File.Exists(f)) File.Delete(f);
                            Log.Info("Discs: " + Path.GetFileName(link) + " removed from " + dir + " - its image is gone or replaced");
                        }
                        catch (Exception ex) { Log.Warn("Discs: could not check " + link, ex); }
                    }
                    try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
                }
            }
            catch (Exception ex) { Log.Warn("Discs: could not sweep the folder", ex); }
        }

        /// <summary>The same file on the disk under both names (a hard link and its image): same volume and same file index.
        /// False when either cannot be opened.</summary>
        internal static bool SameFile(string a, string b)
        {
            try
            {
                using var fa = File.Open(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var fb = File.Open(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (!GetFileInformationByHandle(fa.SafeFileHandle, out var ia) || !GetFileInformationByHandle(fb.SafeFileHandle, out var ib)) return false;
                return ia.VolumeSerialNumber == ib.VolumeSerialNumber && ia.FileIndexHigh == ib.FileIndexHigh && ia.FileIndexLow == ib.FileIndexLow;
            }
            catch { return false; }
        }

        // FILETIMEs are two DWORDs: Pack = 4, or a long would be aligned to 8 and shift every field after the first.
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
        private struct ByHandleFileInformation
        {
            public uint FileAttributes; public long CreationTime, LastAccessTime, LastWriteTime;
            public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle, out ByHandleFileInformation info);
        /// <summary>The image a link of Discs\ was made from: a symbolic link's target, a hard link's noted source; null for none.</summary>
        private static string LinkSource(string link)
        {
            try
            {
                if (!File.Exists(link)) return null;
                var l = new FileInfo(link);
                if (l.LinkTarget != null) return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(link), l.LinkTarget));
                return File.Exists(link + SourceSuffix) ? File.ReadAllText(link + SourceSuffix).Trim() : null;
            }
            catch { return null; }
        }
        /// <summary>A link removed WITHOUT touching its attributes: on a hard link they are the image's own - lifting a read-only
        /// flag to delete it would lift the user's ISO's (review 13/10). A plain delete first; a read-only one is deleted through
        /// Windows' own "ignore the read-only attribute" disposition (Windows 10 1809 and later), and left - said - if even that is
        /// refused. Never the image: only this name of the file goes.</summary>
        private static void DeleteLink(string link)
        {
            try { File.Delete(link); return; }
            catch (UnauthorizedAccessException) when (File.Exists(link) && (File.GetAttributes(link) & FileAttributes.ReadOnly) != 0) { }
            if (!DeleteIgnoringReadOnly(link)) throw new IOException("the link " + link + " is read-only (its image is) and could not be removed without changing that - left");
        }

        private static bool DeleteIgnoringReadOnly(string path)
        {
            const uint DELETE = 0x00010000, OPEN_EXISTING = 3, FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
            const int FileDispositionInfoEx = 21;
            var h = CreateFileW(path, DELETE, 7, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
            if (h == new IntPtr(-1)) return false;
            try
            {
                // FILE_DISPOSITION_FLAG_DELETE | POSIX_SEMANTICS | IGNORE_READONLY_ATTRIBUTE
                uint flags = 0x1 | 0x2 | 0x10;
                return SetFileInformationByHandle(h, FileDispositionInfoEx, ref flags, 4);
            }
            finally { CloseHandle(h); }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFileInformationByHandle(IntPtr handle, int infoClass, ref uint info, uint size);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string newFile, string existingFile, IntPtr security);
        public static string RomFor(string rom, AresSystem system, string exe, out string problem)
        {
            problem = null;
            var ext = Path.GetExtension(rom ?? "").ToLowerInvariant();
            try
            {
                if (ext == ".m3u")
                {
                    var discs = M3uDiscs(rom);
                    if (discs.Count == 0) { problem = Path.GetFileName(rom) + " lists no disc."; return null; }
                    if (!File.Exists(discs[0])) { problem = Path.GetFileName(rom) + " names " + Path.GetFileName(discs[0]) + ", which is not there."; return null; }
                    // Every disc an .iso/.bin/.img gets its .cue now, not only the first: ares's Change Disc opens the others.
                    string first = null;
                    foreach (var d in discs.Where(File.Exists))
                    {
                        var e = Path.GetExtension(d).ToLowerInvariant();
                        var playable = system != null && system.Disc && (e == ".iso" || e == ".bin" || e == ".img") ? CueFor(d, out _, exe, rom) : d;
                        first = first ?? playable;
                    }
                    if (first == null) { problem = Path.GetFileName(rom) + ": its first disc cannot be given to ares."; return null; }
                    Log.Info("a .m3u: ares has no playlists - its first disc, " + Path.GetFileName(first));
                    return first;
                }
                if (system != null && system.Disc && (ext == ".iso" || ext == ".bin" || ext == ".img")) return CueFor(rom, out problem, exe);
                if (ext != ".7z") return rom;
                if (system != null && system.Disc) { problem = "ares cannot read a disc from a .7z - extract it (.cue and its tracks, or a .chd)."; return null; }

                using var archive = ArchiveFactory.Open(rom);
                var wanted = system?.Extensions ?? Array.Empty<string>();
                var inside = archive.Entries.Where(e => !e.IsDirectory && wanted.Contains(Path.GetExtension(e.Key ?? "").ToLowerInvariant())).ToList();
                var name = Path.GetFileNameWithoutExtension(rom);
                var pick = inside.Count == 1 ? inside[0] : inside.FirstOrDefault(e => string.Equals(Path.GetFileNameWithoutExtension(e.Key), name, StringComparison.OrdinalIgnoreCase));
                if (pick == null)
                {
                    problem = inside.Count == 0 ? "there is no " + (system?.Name ?? "") + " game in " + Path.GetFileName(rom)
                            : Path.GetFileName(rom) + " holds " + inside.Count + " games and none is named after the archive - keep one per archive.";
                    return null;
                }
                var target = Path.Combine(UnpackDir(exe, rom), name + Path.GetExtension(pick.Key).ToLowerInvariant());
                if (File.Exists(target) && new FileInfo(target).Length == pick.Size && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(rom)) return target;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                // Only the ROM goes: the saves and states beside it are the game's.
                if (File.Exists(target)) File.Delete(target);
                using (var src = pick.OpenEntryStream())
                using (var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                    src.CopyTo(dst);
                Log.Info("unpacked " + pick.Key + " from " + Path.GetFileName(rom) + " -> " + target);
                return target;
            }
            catch (Exception ex) { Log.Warn("could not open " + rom, ex); problem = Path.GetFileName(rom) + " could not be opened: " + ex.Message; return null; }
        }
    }
}
