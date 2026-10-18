// What a launch decides and writes: the game's file as puNES must be given it, the pack's free hotkeys, and the line.
//
// THE LINE (src\gui\cmd_line.cpp, v0.111): the plugin's options go FIRST, "--option=value" each (PunesSettings.Args), then
// what LaunchBox built - the platform row's line and the quoted game - untouched: puNES reads its arguments in order and
// the last of an option wins, so an option the user put on the platform's line wins over the plugin's. Nothing given on
// the line is kept by puNES (measured 18/10, PunesSettings). A non-option argument is the game, taken only when the file
// exists (QFileInfo::exists).
//
// A PATH HOLDING '=' CANNOT BE GIVEN: cmd_line_parse splits EVERY argument on '=' first (arg.split("=")), the game's path
// included - measured 18/10: "a=b Zelda.nes" loaded nothing, recent.cfg stayed empty. Such a game is refused with a
// message saying so (PunesPlugin.PrepareCore) rather than started on an empty screen.
//
// ARCHIVES (measured 18/10, docs\punes.md section 6): puNES opens .zip and .7z itself (7z.dll), but
//   - it names the saves after the file INSIDE (prb\inner name.prb, save\inner name.p00), not after the archive;
//   - an archive of two games opens a choice dialog - unattended, and a WM_CLOSE then left puNES running with no window;
//   - .rar is offered only when 7z.dll has it - not measured.
// So an archive is given AS IT IS only when it holds one file, a .zip or a .7z, named after the archive: puNES then names
// the saves exactly as for the bare file. Any other is unpacked by the plugin (SharpCompress) under the ARCHIVE's name into
// a STABLE folder of the install, <puNES>\Unpacked\<name>-<hash of the archive's path>\ (AresLaunch.Unpacked's model):
// the saves are named after the game, the archive's name, whatever the archive holds - never %TEMP%, never beside the game.
//
// THE HOTKEYS (Mehdi, 18/10): input.cfg's [shortcuts] ("name=<keyboard>,<joystick>" - ONE key per action, the second
// field is the pad's: settings_inp_rd_sc(..., KEYBOARD / JOYSTICK), objSettings.cpp v0.111) are set to the pack's:
//
//   save state F2, load state F4, decrement / increment state slot F6 / F7, video fullscreen F11, quit Esc,
//   hard reset Shift+Esc, mode pal F10, mode ntsc Shift+F10, toggle menubar in fullscreen none
//   and, as RetroArch has them: pause P, fast forward Space (held: L), save screenshot F8, audio enable F9 (mute),
//   soft reset H - mode dendy / mode auto, which held F8 / F9, beside PAL / NTSC: Ctrl+F10 / Alt+F10
//
// puNES 0.111's defaults on those actions (measured on a fresh input.cfg): F1, F4, F2, F3, Alt+Return, Alt+Q, F11, F6,
// F7, Esc, Pause, Tab, none, Alt+X, Alt+A, F12, F8, F9 - so the pack's keys are puNES's for something else, and the
// actions that held them are moved too.
// An action is set only when it holds puNES's default, nothing, or the pack's key already: one the user changed in
// puNES's Settings > Input is theirs. Its pad button is kept. A pack key another action holds (one of the user's, or a
// pad's keyboard button) leaves that action as it is.
//
// MEASURED 18/10 (lab run5a-c, punes64.exe 0.111, Zelda II, WM_CLOSE): puNES keeps the lines it is given in a MINIMAL
// input.cfg and writes every other action with its default - so the moved actions must be written too (run5a, without
// them: F11 both full screen and hard reset, Esc both quit and the menu bar, F6 / F7 both slot and PAL / NTSC); with
// them, no key on two actions (run5c, run5d with the RetroArch keys: F8 / F9 were mode dendy / auto's), Shift+Esc,
// Shift+F10, Ctrl+F10, Alt+F10 and Space kept as written. So a missing input.cfg is MADE with these lines before the
// first start.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using SharpCompress.Archives;

namespace LbIntegrations.Punes
{
    internal static class PunesLaunch
    {
        /// <summary>The 64-bit build's exe - what the plugin installs.</summary>
        public const string ExecutableName = "punes64.exe";

        /// <summary>Every name puNES's exe goes by (README of v0.111, "Portable mode": punes64.exe, punes.exe, and the "_p"
        /// suffix that forces portable mode). No other emulator of the pack nor of LaunchBox's base ships these names.</summary>
        public static readonly string[] ExecutableNames = { "punes64.exe", "punes.exe", "punes64_p.exe", "punes_p.exe" };

        public static bool IsPunesExe(string path)
        {
            try
            {
                var name = Path.GetFileName((path ?? "").Trim().Trim('"'));
                return ExecutableNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        /// <summary>A puNES running from that path: an edit of its files now would be lost when it quits.</summary>
        public static bool Running(string exe)
        {
            try
            {
                var full = Path.GetFullPath(exe);
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(full)))
                    using (p)
                        try { if (string.Equals(p.MainModule?.FileName, full, StringComparison.OrdinalIgnoreCase)) return true; } catch { }
            }
            catch { }
            return false;
        }

        // ── the game's file ──────────────────────────────────────────────────

        /// <summary>The file puNES is given for <paramref name="rom"/>: itself, or an archive's game (as it is, or unpacked
        /// into the install's Unpacked\ folder). <paramref name="innerExtension"/>: the extension of the game in an archive.
        /// Null with <paramref name="problem"/> said when it cannot be played.</summary>
        public static string RomFor(string exe, string rom, out string innerExtension, out string problem)
        {
            innerExtension = null; problem = null;
            if (string.IsNullOrEmpty(rom) || !PunesSystems.IsArchive(rom)) return rom;
            try
            {
                using var archive = ArchiveFactory.Open(rom);
                var files = archive.Entries.Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key)).ToList();
                var games = files.Where(e => PunesSystems.OfExtension(Path.GetExtension(e.Key)) != null).ToList();
                var name = Path.GetFileNameWithoutExtension(rom);
                var pick = games.Count == 1 ? games[0] : games.FirstOrDefault(e => string.Equals(Path.GetFileNameWithoutExtension(e.Key), name, StringComparison.OrdinalIgnoreCase));
                if (pick == null)
                {
                    problem = games.Count == 0 ? "there is no NES or Famicom Disk System game in " + Path.GetFileName(rom) + "."
                            : Path.GetFileName(rom) + " holds " + games.Count + " games, and none is named after the archive - puNES would ask which to play. "
                              + "Keep one game per archive, or name the one to play \"" + name + Path.GetExtension(games[0].Key) + "\".";
                    return null;
                }
                innerExtension = Path.GetExtension(pick.Key).ToLowerInvariant();
                var ext = Path.GetExtension(rom).ToLowerInvariant();
                if (files.Count == 1 && (ext == ".zip" || ext == ".7z") && string.Equals(Path.GetFileNameWithoutExtension(pick.Key), name, StringComparison.Ordinal))
                {
                    Log.Info("an archive of one game named after it: given to puNES as it is - " + Path.GetFileName(rom));
                    return rom;
                }
                var target = Path.Combine(UnpackDir(exe, rom), name + innerExtension);
                if (File.Exists(target) && new FileInfo(target).Length == pick.Size && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(rom)) return target;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (File.Exists(target)) File.Delete(target);
                using (var src = pick.OpenEntryStream())
                using (var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                    LbIntegrations.Lbip.LbipLaunchStatus.CopyStream(src, dst, pick.Size, "Extracting " + Path.GetFileName(pick.Key) + "…");
                Log.Info("unpacked " + pick.Key + " from " + Path.GetFileName(rom) + " -> " + target);
                return target;
            }
            catch (Exception ex)
            {
                Log.Warn("could not open " + rom, ex);
                problem = Path.GetFileName(rom) + " could not be opened: " + ex.Message;
                return null;
            }
        }

        /// <summary>Where an archive is unpacked for the puNES at <paramref name="exe"/>: one folder per archive PATH, so two
        /// archives of one name never share it.</summary>
        public static string UnpackDir(string exe, string rom)
        {
            var name = Path.GetFileNameWithoutExtension(rom);
            using var md5 = System.Security.Cryptography.MD5.Create();
            var hash = Convert.ToHexString(md5.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(rom).ToLowerInvariant()))).Substring(0, 8).ToLowerInvariant();
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), "Unpacked", name + "-" + hash);
        }

        /// <summary>puNES cuts every argument at its first '=' (cmd_line.cpp, measured): a path holding one is never opened.</summary>
        public static bool Givable(string path) => path == null || path.IndexOf('=') < 0;

        // ── the line ─────────────────────────────────────────────────────────

        /// <summary>The host's line with the plugin's options in front and, when the game was given another file (an
        /// archive unpacked), that file in place of the host's.</summary>
        public static string CommandLine(string current, IEnumerable<string> options, string oldRom = null, string newRom = null)
        {
            var line = (current ?? "").Trim();
            if (newRom != null && oldRom != null && !string.Equals(newRom, oldRom, StringComparison.OrdinalIgnoreCase)) line = SwapRom(line, oldRom, newRom);
            var head = string.Join(" ", (options ?? Enumerable.Empty<string>()).Select(Quote));
            return (head + " " + line).Trim();
        }

        private static string Quote(string a) => a.Length == 0 || a.Any(char.IsWhiteSpace) || a.Contains('"') ? "\"" + a.Replace("\"", "\\\"") + "\"" : a;

        /// <summary>The host's line with its game path swapped for <paramref name="newRom"/>.</summary>
        public static string SwapRom(string line, string oldRom, string newRom)
        {
            line = line ?? "";
            foreach (var form in new[] { "\"" + oldRom + "\"", oldRom })
            {
                int at = line.LastIndexOf(form, StringComparison.OrdinalIgnoreCase);
                if (at >= 0) return line.Substring(0, at) + "\"" + newRom + "\"" + line.Substring(at + form.Length);
            }
            // Spelt otherwise by the host (relative, forward slashes): the last quoted token, else the last token - never a
            // second game beside the first.
            var m = System.Text.RegularExpressions.Regex.Match(line, "\"[^\"]*\"\\s*$");
            if (m.Success) return line.Substring(0, m.Index) + "\"" + newRom + "\"";
            var cut = line.TrimEnd().LastIndexOf(' ');
            return cut < 0 ? "\"" + newRom + "\"" : line.Substring(0, cut) + " \"" + newRom + "\"";
        }

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

        /// <summary>--portable on a line (the user's platform line): puNES is portable whatever its folder holds.</summary>
        public static bool PortableOnLine(string line) => Tokenize(line).Any(t => t == "--portable");

        // ── the hotkeys ──────────────────────────────────────────────────────

        private const string Shortcuts = "shortcuts";

        /// <summary>The pack's keys on puNES's actions (input.cfg [shortcuts]), with puNES 0.111's default for each (measured).
        /// Null: no key.</summary>
        internal static readonly (string Action, string Key, string PunesDefault)[] Hotkeys =
        {
            ("save state", "F2", "F1"), ("load state", "F4", "F4"),
            ("decrement state slot", "F6", "F2"), ("increment state slot", "F7", "F3"),
            ("video fullscreen", "F11", "Alt+Return"), ("quit", "Esc", "Alt+Q"),
            ("hard reset", "Shift+Esc", "F11"), ("mode pal", "F10", "F6"), ("mode ntsc", "Shift+F10", "F7"),
            ("toggle menubar in fullscreen", null, "Esc"),
            // RetroArch's keys (Mehdi 18/10).
            ("pause", "P", "Pause"), ("fast forward", "Space", "Tab"), ("hold fast forward", "L", null),
            ("save screenshot", "F8", "Alt+X"), ("audio enable", "F9", "Alt+A"), ("soft reset", "H", "F12"),
            ("mode dendy", "Ctrl+F10", "F8"), ("mode auto", "Alt+F10", "F9"),
        };

        /// <summary>The keyboard half of a "name=keyboard,joystick" value; null for none (NULL, empty).</summary>
        internal static string KeyboardOf(string value)
        {
            if (value == null) return null;
            var k = value.Split(',')[0].Trim();
            return k.Length == 0 || string.Equals(k, "NULL", StringComparison.OrdinalIgnoreCase) ? null : k;
        }

        private static string JoystickOf(string value)
        {
            var parts = (value ?? "").Split(',');
            return parts.Length > 1 && parts[1].Trim().Length > 0 ? parts[1].Trim() : "NULL";
        }

        /// <summary>The pack's keys set in input.cfg (see the header) - the file made with them when puNES has not written one
        /// yet. Never throws, never writes while puNES runs.</summary>
        public static void BindHotkeys(string exe)
        {
            try
            {
                if (Running(exe)) return;
                var path = PunesCfg.InputOf(exe);
                bool fresh = !File.Exists(path);
                var cfg = PunesCfg.Load(path);
                // The actions this pass gives a pack key: they free the key they held (an action of the user's keeps it).
                var ours = new HashSet<string>(Hotkeys.Where(h => IsOurs(KeyboardOf(cfg.Get(Shortcuts, h.Action)), h.Key, h.PunesDefault)).Select(h => h.Action), StringComparer.OrdinalIgnoreCase);
                var set = new List<string>();
                var left = new List<string>();
                foreach (var (action, key, dflt) in Hotkeys)
                {
                    var now = cfg.Get(Shortcuts, action);
                    var k = KeyboardOf(now);
                    if (!ours.Contains(action)) { left.Add(action + " (the user's " + k + ")"); continue; }
                    if (key != null)
                    {
                        var holder = Holder(cfg, key, action, ours);
                        if (holder != null)
                        {
                            // Its old key goes to another action of this pass (mode pal's F6 to the slot): taken off, never on two.
                            bool wanted = Hotkeys.Any(h => h.Key != null && !string.Equals(h.Action, action, StringComparison.OrdinalIgnoreCase)
                                                           && ours.Contains(h.Action) && Same(k, h.Key));
                            if (wanted) { cfg.Set(Shortcuts, action, "NULL," + JoystickOf(now)); set.Add(action + " = none (" + key + " is " + holder + "'s, " + k + " goes to another)"); }
                            else left.Add(action + " (" + key + " is " + holder + "'s)");
                            continue;
                        }
                    }
                    var value = (key ?? "NULL") + "," + JoystickOf(now);
                    if (!string.Equals(now, value, StringComparison.Ordinal)) { cfg.Set(Shortcuts, action, value); set.Add(action + " = " + (key ?? "none")); }
                }
                cfg.SaveIfChanged();
                if (set.Count > 0) Log.Info("input.cfg" + (fresh ? " made" : "") + ": " + string.Join(", ", set));
                if (left.Count > 0) Log.Info("input.cfg: left as it is " + string.Join(", ", left));
            }
            catch (Exception ex) { Log.Warn("input.cfg could not be written - puNES starts with its keys as they were", ex); }
        }

        /// <summary>An action this plugin may set: nothing on it, puNES's default, or the pack's key already.</summary>
        private static bool IsOurs(string k, string key, string dflt) => k == null || Same(k, dflt) || Same(k, key);

        private static bool Same(string a, string b) => b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>Who holds <paramref name="key"/>: another shortcut, or a keyboard button of a pad ("P1K A=S"); null: free.
        /// The actions in <paramref name="moving"/> do not count: they are given their own pack key in the same pass.</summary>
        internal static string Holder(PunesCfg cfg, string key, string except, ICollection<string> moving = null)
        {
            foreach (var (k, v) in cfg.Entries(Shortcuts))
                if (!string.Equals(k, except, StringComparison.OrdinalIgnoreCase) && !(moving?.Contains(k) ?? false)
                    && string.Equals(KeyboardOf(v), key, StringComparison.OrdinalIgnoreCase)) return k;
            for (int port = 1; port <= 4; port++)
                foreach (var (k, v) in cfg.Entries("port " + port))
                    if (k.StartsWith("P" + port + "K ", StringComparison.OrdinalIgnoreCase) && string.Equals(v, key, StringComparison.OrdinalIgnoreCase)) return k;
            return null;
        }

        /// <summary>The key puNES has on an action now (input.cfg), else <paramref name="dflt"/> - puNES's default.</summary>
        public static string KeyOf(string exe, string action, string dflt)
        {
            try { return KeyboardOf(PunesCfg.Load(PunesCfg.InputOf(exe)).Get(Shortcuts, action)) ?? dflt; }
            catch { return dflt; }
        }
    }
}
