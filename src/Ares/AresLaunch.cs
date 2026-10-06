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
// --fullscreen or --pseudofullscreen, --no-file-prompt (else ares opens a file dialog for a 64DD disk, a Super Game Boy or
// Sufami Turbo slot...), then the game. Anything else the user put on the line is kept.
//
// THE OPTIONS (AresSettings.All) go into settings.bml before the launch - ares writes the file back whole at every unload
// and at exit (AresBml), so a node the plugin manages is the plugin's. The state hotkeys are bound when empty: ares ships
// none (input\hotkeys.cpp) - F5 saves, F7 loads, as for bsnes; the binding format is "0x1/0/<key>;;" (keyboard,
// F1..F12 = 1..12 - RetroBat's Ares.Generator.cs).
//
// FILES ares cannot open: a .7z (it reads zip, gzip, chd, cue - nall\decode) is unpacked when it holds a cartridge, under
// the archive's name so its save keeps that name; a .m3u (no playlist support) gives its first disc.

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
        public const string SaveKey = "0x1/0/5;;";   // F5
        public const string LoadKey = "0x1/0/7;;";   // F7

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
            if (given != null && AresSystems.System(given) != null) return (given, "the platform's command line");
            var p = AresSystems.Platform(platform);
            var ext = Path.GetExtension(rom ?? "");
            if (p != null && p.ByExtension.TryGetValue(ext, out var byExt)) return (byExt, "the file's extension");
            if (p != null) return (p.Default, "the platform");
            return (given, given != null ? "the command line (not a system this plugin knows)" : null);
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
                var bml = AresBml.Load(SettingsOf(exe));
                var output = AresSettings.For(gameId, "Output");
                bml.Set("Video/Output", output == "integer" ? "Integer" : output == "stretch" ? "Stretch" : "Scale");
                bml.Set("Video/AspectCorrectionMode", AresSettings.For(gameId, "Aspect") == "none" ? "None" : "Standard");
                bml.SetBool("Video/Blocking", AresSettings.BoolFor(gameId, "VSync"));
                bml.Set("Video/Driver", AresSettings.For(gameId, "Driver") == "d3d" ? "Direct3D 9.0" : "OpenGL 3.2");
                var region = AresSettings.For(gameId, "Region");
                bml.Set("Boot/Prefer", region == "ntsc-j" ? "NTSC-J" : region == "pal" ? "PAL" : "NTSC-U");
                bml.SetBool("Boot/Fast", AresSettings.BoolFor(gameId, "FastBoot"));
                if (string.IsNullOrWhiteSpace(bml.Get("Hotkey/SaveState")) || bml.Get("Hotkey/SaveState") == ";;") bml.Set("Hotkey/SaveState", SaveKey);
                if (string.IsNullOrWhiteSpace(bml.Get("Hotkey/LoadState")) || bml.Get("Hotkey/LoadState") == ";;") bml.Set("Hotkey/LoadState", LoadKey);
                bool changed = bml.Changed;
                bml.SaveIfChanged();
                Log.Info("settings.bml: " + (changed ? "written" : "already as wanted") + " - " + output + ", " + AresSettings.For(gameId, "Aspect") + ", " + region);
            }
            catch (Exception ex) { Log.Warn("settings.bml could not be written - the game starts with it as it was", ex); }
        }

        // ── the line ─────────────────────────────────────────────────────────

        public static string CommandLine(string current, string exe, string system, string screen, string oldRom, string newRom)
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
                int at = t.FindLastIndex(a => string.Equals(a, oldRom, StringComparison.OrdinalIgnoreCase));
                if (at >= 0) t[at] = newRom; else t.Add(newRom);
            }
            var head = new List<string> { "--settings-file", SettingsOf(exe) };
            if (system != null) { head.Add("--system"); head.Add(system); }
            if (screen == "full") head.Add("--fullscreen");
            else if (screen == "pseudo") head.Add("--pseudofullscreen");
            head.Add("--no-file-prompt");
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
        public static string RomFor(string rom, AresSystem system, out string problem)
        {
            problem = null;
            var ext = Path.GetExtension(rom ?? "").ToLowerInvariant();
            try
            {
                if (ext == ".m3u")
                {
                    var dir = Path.GetDirectoryName(rom);
                    var first = File.ReadAllLines(rom).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith("#"));
                    if (first == null) { problem = Path.GetFileName(rom) + " lists no disc."; return null; }
                    var disc = Path.IsPathRooted(first) ? first : Path.GetFullPath(Path.Combine(dir, first));
                    if (!File.Exists(disc)) { problem = Path.GetFileName(rom) + " names " + first + ", which is not there."; return null; }
                    Log.Info("a .m3u: ares has no playlists - its first disc, " + Path.GetFileName(disc));
                    return disc;
                }
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
                var target = Path.Combine(Path.GetTempPath(), "lbip-ares", name, name + Path.GetExtension(pick.Key).ToLowerInvariant());
                if (File.Exists(target) && new FileInfo(target).Length == pick.Size && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(rom)) return target;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                foreach (var old in Directory.GetFiles(Path.GetDirectoryName(target))) try { File.Delete(old); } catch { }
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
