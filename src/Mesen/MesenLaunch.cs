// What a launch decides and writes for Mesen: which install it is, its home folder, the game's SYSTEM, the hotkeys, the line.
//
// THE EXE (docs\mesen.md §1, §13): Mesen.exe for SourMesen 2.1.x, for MesenCE 2.2.x - and for the old Mesen 0.9.x (NES
// only, .NET Framework, another program). So Mesen.exe is ours only when its version resource says 2 or more
// (FileVersion "2.1.1" / "2.2.1", measured); a Mesen.exe that is not on the disk is claimed by its name alone. MesenCE is
// told from 2.1 by its version (2.2 and later, measured: FileVersion 2.2.1, window title "MesenCE - <game>").
//
// THE HOME FOLDER (ConfigManager.HomeFolder, both tags): the exe's folder when a settings.json is there ("portable"), else
// Documents\MesenCE (CE; Documents\Mesen2 when only that one has a settings.json) / Documents\Mesen2 (2.1). Everything is
// there: settings.json, Firmware\, Saves\, SaveStates\, RecentGames\. Without any settings.json Mesen shows its setup
// wizard and IGNORES the game on the line (measured 18/10): the plugin writes a first one beside the exe then (Bootstrap) -
// {"ConfigUpgrade":1,...} is what the wizard writes (Configuration.CreateConfig: ConfigUpgradeHint.FirstRun), which makes
// Mesen set every system's default keys at its start (measured: the full 230 KB file came back).
//
// THE SYSTEM, the ares rule (Mehdi 06/10), the first that says (SystemFor):
//   1. the game's own choice in its window (MesenSettings.GameSystem) - its model switch put on the line, the line's own
//      switch of that key taken off;
//   2. the platform row's line, handed over by LaunchBox in CurrentCommandLine (a --gameboy.model=..., --ws.model=... the
//      plugin wrote there, or the user) - except that an extension which says another system wins over the line the PLUGIN
//      wrote (a .wsc among WonderSwan games, a .gbc among Game Boy ones): that line's switch is then replaced by the system's;
//   3. the table (MesenSystems): the extension, else the platform's default - and NO switch added: Mesen's own model setting
//      (its automatic choice by the cartridge's header unless the user set one) decides.
//
// THE LINE: the switches first, --fullscreen (MesenSettings "Fullscreen"; Mesen toggles full screen once the game is loaded -
// CommandLineHelper.ProcessPostLoadCommandSwitches, read at both tags, not measured on the screen), then what the user put on
// the line and the game. Every argument naming a file is loaded, so the order of the switches does not matter.
//
// ARCHIVES: Mesen opens .zip and .7z itself (ArchiveReader::GetReader, measured with a zip) and names the saves after the
// file INSIDE (measured: ZeldaArchive.zip holding "Inner Zelda.nes" wrote Saves\Inner Zelda.sav). One with several games
// opens a selection dialog (SelectRomWindow): the plugin then unpacks the one named after the archive into the install's
// Unpacked\<archive>-<hash>\ and gives that file, else refuses with a message (RomFor).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using SharpCompress.Archives;

namespace LbIntegrations.Mesen
{
    internal static class MesenLaunch
    {
        public const string ExecutableName = "Mesen.exe";

        // ── the exe ──────────────────────────────────────────────────────────

        private static readonly Dictionary<string, (long Size, DateTime When, Version Version)> Versions =
            new Dictionary<string, (long, DateTime, Version)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Mesen.exe of Mesen 2 or later (SourMesen 2.x, MesenCE): by name, then by its version resource when the
        /// file is there - the old Mesen 0.9.x (NES only) has the same name. Relative paths are the library's.</summary>
        public static bool IsMesenExe(string path)
        {
            try
            {
                var p = (path ?? "").Trim().Trim('"');
                if (!string.Equals(Path.GetFileName(p), ExecutableName, StringComparison.OrdinalIgnoreCase)) return false;
                var full = MesenPlugin.ResolveFullPathForUi(p);
                if (string.IsNullOrEmpty(full) || !File.Exists(full)) return true;
                var v = VersionOf(full);
                return v == null || v.Major >= 2;
            }
            catch { return false; }
        }

        /// <summary>The exe's FileVersion ("2.2.1"), cached by size and date; null when it carries none.</summary>
        public static Version VersionOf(string exe)
        {
            try
            {
                var info = new FileInfo(exe);
                if (!info.Exists) return null;
                lock (Versions)
                    if (Versions.TryGetValue(info.FullName, out var c) && c.Size == info.Length && c.When == info.LastWriteTimeUtc) return c.Version;
                var fv = FileVersionInfo.GetVersionInfo(info.FullName);
                Version v = fv.FileMajorPart > 0 || fv.FileMinorPart > 0 ? new Version(fv.FileMajorPart, fv.FileMinorPart, Math.Max(0, fv.FileBuildPart)) : null;
                lock (Versions) Versions[info.FullName] = (info.Length, info.LastWriteTimeUtc, v);
                return v;
            }
            catch { return null; }
        }

        /// <summary>MesenCE (2.2 and later): Pocket Challenge V2, Documents\MesenCE.</summary>
        public static bool IsCe(string exe) { var v = VersionOf(exe); return v != null && (v.Major > 2 || (v.Major == 2 && v.Minor >= 2)); }

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

        // ── the home folder ──────────────────────────────────────────────────

        /// <summary>Overridden by the probe: where Documents is.</summary>
        internal static string DocumentsOverride;

        private static string Documents => DocumentsOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        /// <summary>Mesen's home for <paramref name="exe"/>, as ConfigManager.HomeFolder works it out (header).</summary>
        public static string HomeOf(string exe)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(exe));
            if (File.Exists(Path.Combine(dir, MesenJson.FileName))) return dir;
            var legacy = Path.Combine(Documents, "Mesen2");
            if (!IsCe(exe)) return legacy;
            var ce = Path.Combine(Documents, "MesenCE");
            bool legacyHas = File.Exists(Path.Combine(legacy, MesenJson.FileName));
            return !legacyHas || File.Exists(Path.Combine(ce, MesenJson.FileName)) ? ce : legacy;
        }

        public static string SettingsOf(string exe) => Path.Combine(HomeOf(exe), MesenJson.FileName);

        public static bool IsPortable(string exe) => string.Equals(HomeOf(exe), Path.GetDirectoryName(Path.GetFullPath(exe)), StringComparison.OrdinalIgnoreCase);

        /// <summary>The settings.json a first run needs, beside the exe, when Mesen has none anywhere (header) - never over one.
        /// True when written.</summary>
        public static bool Bootstrap(string exe)
        {
            try
            {
                if (File.Exists(SettingsOf(exe))) return false;
                var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), MesenJson.FileName);
                var json = MesenJson.Load(path);
                json.Set("ConfigUpgrade", JsonValue.Create(1));
                // Its update check stays Mesen's to make: off here, as LaunchBox's Download button is the plugin's.
                json.Set("Preferences/AutomaticallyCheckForUpdates", false);
                json.SaveIfChanged();
                Log.Info("wrote " + path + " - Mesen's first settings (portable: everything in its own folder)");
                return true;
            }
            catch (Exception ex) { Log.Warn("could not write Mesen's first settings.json - it will show its setup wizard", ex); return false; }
        }

        // ── the system ───────────────────────────────────────────────────────

        internal sealed class SystemChoice
        {
            public MesenSystem System;
            public string From;
            /// <summary>Switches to put on the line, and the line's tokens to take off for them.</summary>
            public List<string> Add = new List<string>();
            public List<string> Remove = new List<string>();
        }

        /// <summary>The system a game runs as (header): <paramref name="ext"/> is the extension of the file Mesen will load
        /// (the one INSIDE an archive).</summary>
        public static SystemChoice SystemFor(string gameId, string platform, string ext, string currentLine)
        {
            var choice = new SystemChoice();
            var p = MesenSystems.Platform(platform);
            var core = MesenSystems.CoreOf(ext);
            var tokens = Tokenize(currentLine);

            // 1. The game's own choice.
            var own = MesenSystems.System(MesenSettings.GameSystem(gameId));
            if (own != null)
            {
                choice.System = own; choice.From = "the game's window";
                Force(choice, own, tokens);
                return choice;
            }

            // 2. The platform row's line - the user's: the plugin writes none (Mehdi 19/10, Mesen's own detection).
            bool byExtension = p != null && p.ByExtension.ContainsKey(ext ?? "");
            var onLine = tokens.Select(t => MesenSystems.SystemOfSwitch(t, core)).LastOrDefault(s => s != null);
            if (onLine != null)
            {
                choice.System = onLine; choice.From = "the platform's command line";
                return choice;
            }

            // 3. The table.
            if (byExtension)
            {
                choice.System = MesenSystems.System(p.ByExtension[ext]); choice.From = "the file's extension";
                return choice;
            }
            if (p != null)
            {
                // The platform's default - unless the extension is none of its systems' and says one alone (a .fds anywhere).
                var def = MesenSystems.System(p.Default);
                var alone = MesenSystems.ByExtensionAlone(ext);
                choice.System = def != null && def.Extensions.Contains((ext ?? "").ToLowerInvariant()) ? def : alone ?? def;
                choice.From = "the platform";
                return choice;
            }
            choice.System = MesenSystems.ByExtensionAlone(ext);
            choice.From = choice.System == null ? null : "the file's extension";
            return choice;
        }

        private static void Force(SystemChoice choice, MesenSystem system, List<string> tokens)
        {
            if (system.Switch == null) return;
            var key = MesenSystems.SwitchParts(system.Switch)?.Key;
            choice.Remove.AddRange(tokens.Where(t => MesenSystems.SwitchParts(t)?.Key == key));
            choice.Add.Add(system.Switch);
        }

        // ── the hotkeys ──────────────────────────────────────────────────────

        /// <summary>Mesen's key codes as it writes them in settings.json (measured 18/10, 2.1.1 and CE identical): Esc 13,
        /// F1..F12 90..101.</summary>
        internal const int Esc = 13, F2 = 91, F4 = 93, F6 = 95, F7 = 96;

        /// <summary>The pack's keys (Mehdi 07/10): F2 save state, F4 load state, F6 / F7 the slot down / up, Esc quit - each
        /// bound to Mesen's action when that action has no key (all five are unbound in Mesen's defaults, measured). F11 is
        /// already Mesen's ToggleFullscreen.</summary>
        internal static readonly (string Shortcut, int Key)[] PackKeys =
        {
            ("SaveState", F2), ("LoadState", F4), ("MoveToPreviousStateSlot", F6), ("MoveToNextStateSlot", F7), ("Exit", Esc),
        };

        /// <summary>Mesen's OWN defaults on those keys (PreferencesConfig.InitializeDefaultShortcuts; measured in the file it
        /// writes): F2/F4/F6/F7 load slot 2/4/6/7, Esc pauses. When the pack takes such a key the default is unbound, as
        /// RaSnes9xLaunch does - else both would fire. Any other action on the key is the user's: the key is left alone.</summary>
        internal static readonly Dictionary<int, string> MesenDefaultOn = new Dictionary<int, string>
        {
            [F2] = "LoadStateSlot2", [F4] = "LoadStateSlot4", [F6] = "LoadStateSlot6", [F7] = "LoadStateSlot7", [Esc] = "Pause",
        };

        /// <summary>The pack's keys bound in settings.json where the rule of PackKeys allows; a list Mesen has not written yet
        /// is started with them (Mesen adds the rest of its defaults at its start - measured 18/10: a list of ten kept as it was,
        /// 103 more added). Returns what was bound. Never throws.</summary>
        public static List<string> BindPackKeys(MesenJson json)
        {
            var bound = new List<string>();
            try
            {
                if (!json.Readable) return bound;
                var prefs = json.Get("Preferences") as JsonObject;
                if (prefs == null) { prefs = new JsonObject(); json.Root[MesenJson.NameIn(json.Root, "Preferences") ?? "Preferences"] = prefs; json.Touch(); }
                var listName = MesenJson.NameIn(prefs, "ShortcutKeys") ?? "ShortcutKeys";
                if (!(prefs[listName] is JsonArray list)) { list = new JsonArray(); prefs[listName] = list; json.Touch(); }

                JsonObject Entry(string shortcut) => list.OfType<JsonObject>().FirstOrDefault(e => string.Equals(MesenJson.TextOf(e[MesenJson.NameIn(e, "Shortcut") ?? "Shortcut"]), shortcut, StringComparison.OrdinalIgnoreCase));
                bool Empty(JsonObject e) => e == null || (Combo(e, "KeyCombination").All(k => k == 0) && Combo(e, "KeyCombination2").All(k => k == 0));
                bool IsOnly(JsonObject e, int key) => e != null && (Is(Combo(e, "KeyCombination"), key) || Is(Combo(e, "KeyCombination2"), key));

                foreach (var (shortcut, key) in PackKeys)
                {
                    var mine = Entry(shortcut);
                    if (!Empty(mine)) continue;   // the user's, or ours already
                    // Who else holds the key alone: Mesen's own default for it (present, or absent - Mesen would add it), or anybody.
                    MesenDefaultOn.TryGetValue(key, out var def);
                    var holders = list.OfType<JsonObject>().Where(e => IsOnly(e, key)).ToList();
                    var defEntry = def == null ? null : Entry(def);
                    bool defPending = def != null && defEntry == null;   // Mesen adds it at its start, on this key
                    if (holders.Any(h => h != defEntry)) continue;     // somebody else's binding: the key is the user's
                    if (defEntry != null && IsOnly(defEntry, key)) Clear(defEntry, key);
                    else if (defPending) list.Add(NewEntry(def, 0));
                    if (mine == null) list.Add(NewEntry(shortcut, key));
                    else SetCombo(mine, "KeyCombination", key);
                    json.Touch();
                    bound.Add(shortcut + "=" + KeyName(key) + (def != null ? " (" + def + " unbound from it)" : ""));
                }
            }
            catch (Exception ex) { Log.Warn("hotkeys: could not bind the pack's keys", ex); }
            return bound;
        }

        internal static string KeyName(int key) => key == Esc ? "Esc" : key >= 90 && key <= 101 ? "F" + (key - 89) : key.ToString();

        private static int[] Combo(JsonObject e, string name)
        {
            var c = e[MesenJson.NameIn(e, name) ?? name] as JsonObject;
            if (c == null) return new[] { 0, 0, 0 };
            int K(string n) { try { var v = c[MesenJson.NameIn(c, n) ?? n]; return v == null ? 0 : v.GetValue<int>(); } catch { return 0; } }
            return new[] { K("Key1"), K("Key2"), K("Key3") };
        }

        private static bool Is(int[] combo, int key) => combo[0] == key && combo[1] == 0 && combo[2] == 0;

        private static void Clear(JsonObject e, int key)
        {
            foreach (var n in new[] { "KeyCombination", "KeyCombination2" })
                if (Is(Combo(e, n), key)) SetCombo(e, n, 0);
        }

        private static JsonObject NewCombo(int key) => new JsonObject { ["Key1"] = key, ["Key2"] = 0, ["Key3"] = 0 };

        private static void SetCombo(JsonObject e, string name, int key) => e[MesenJson.NameIn(e, name) ?? name] = NewCombo(key);

        private static JsonObject NewEntry(string shortcut, int key)
            => new JsonObject { ["Shortcut"] = shortcut, ["KeyCombination"] = NewCombo(key), ["KeyCombination2"] = NewCombo(0) };

        // ── the line ─────────────────────────────────────────────────────────

        /// <summary>The line Mesen is started with (header). <paramref name="newRom"/>: the file given in place of the game
        /// LaunchBox put on the line (an unpacked archive), or null.</summary>
        public static string CommandLine(string current, bool fullscreen, IEnumerable<string> add, IEnumerable<string> remove, string oldRom = null, string newRom = null)
        {
            var t = Tokenize(current);
            t.RemoveAll(a => IsSwitch(a, "fullscreen"));
            var gone = new HashSet<string>(remove ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            t.RemoveAll(a => gone.Contains(a));
            if (newRom != null && oldRom != null)
            {
                string Full(string a) { try { return Path.GetFullPath(a.Replace('/', '\\')); } catch { return a; } }
                var full = Full(oldRom);
                int at = t.FindLastIndex(a => string.Equals(Full(a), full, StringComparison.OrdinalIgnoreCase));
                if (at < 0) at = t.FindLastIndex(a => !a.StartsWith("-") && !a.StartsWith("/") && (a.Contains('\\') || a.Contains('.')));
                if (at >= 0) t[at] = newRom; else t.Add(newRom);
            }
            var head = new List<string>();
            foreach (var s in add ?? Enumerable.Empty<string>())
            {
                var key = MesenSystems.SwitchParts(s)?.Key;
                t.RemoveAll(a => key != null && MesenSystems.SwitchParts(a)?.Key == key);
                if (!head.Contains(s, StringComparer.OrdinalIgnoreCase)) head.Add(s);
            }
            if (fullscreen) head.Add("--fullscreen");
            return string.Join(" ", head.Concat(t).Select(Quote));
        }

        /// <summary>A switch as Mesen reads it: -x, --x or /x, without regard to case (CommandLineHelper.ConvertArg).</summary>
        internal static bool IsSwitch(string token, string name)
        {
            var a = (token ?? "").Trim();
            if (a.StartsWith("--")) a = a.Substring(2); else if (a.StartsWith("-") || a.StartsWith("/")) a = a.Substring(1); else return false;
            return string.Equals(a, name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The configuration switches of a line (--section.prop=value), in order.</summary>
        internal static List<string> ConfigSwitches(string line) => Tokenize(line).Where(a => MesenSystems.SwitchParts(a) != null).ToList();

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

        // ── archives ─────────────────────────────────────────────────────────

        internal static bool IsArchive(string path)
        {
            var e = Path.GetExtension(path ?? "").ToLowerInvariant();
            return e == ".zip" || e == ".7z";
        }

        /// <summary>The games an archive holds - entries with an extension of one of Mesen's systems.</summary>
        internal static List<string> GamesIn(string archive)
        {
            var known = new HashSet<string>(MesenSystems.All.SelectMany(s => s.Extensions), StringComparer.OrdinalIgnoreCase);
            using var a = ArchiveFactory.Open(archive);
            return a.Entries.Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key) && known.Contains(Path.GetExtension(e.Key)))
                            .Select(e => e.Key).ToList();
        }

        /// <summary>The name of the file Mesen loads from <paramref name="rom"/>: the archive's game inside, else the file
        /// itself. What the saves are named after (header).</summary>
        public static string Inner(string rom)
        {
            try
            {
                if (!IsArchive(rom) || !File.Exists(rom)) return rom;
                var games = GamesIn(rom);
                if (games.Count == 1) return games[0];
                var name = Path.GetFileNameWithoutExtension(rom);
                return games.FirstOrDefault(g => string.Equals(Path.GetFileNameWithoutExtension(g), name, StringComparison.OrdinalIgnoreCase)) ?? rom;
            }
            catch { return rom; }
        }

        /// <summary>Where a several-game archive is unpacked for the Mesen at <paramref name="exe"/>: one folder per archive path.</summary>
        public static string UnpackDir(string exe, string rom)
        {
            var name = Path.GetFileNameWithoutExtension(rom);
            using var md5 = System.Security.Cryptography.MD5.Create();
            var hash = Convert.ToHexString(md5.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(rom).ToLowerInvariant()))).Substring(0, 8).ToLowerInvariant();
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), "Unpacked", name + "-" + hash);
        }

        /// <summary>The file Mesen is given for <paramref name="rom"/>: itself, or the game of a several-game archive unpacked
        /// (header). Null, with <paramref name="problem"/> said, when it cannot be played.</summary>
        public static string RomFor(string rom, string exe, out string problem)
        {
            problem = null;
            try
            {
                if (!IsArchive(rom)) return rom;
                var games = GamesIn(rom);
                if (games.Count == 0) { problem = "there is no game Mesen runs in " + Path.GetFileName(rom) + "."; return null; }
                if (games.Count == 1) return rom;   // Mesen opens it itself, the game picked directly
                var name = Path.GetFileNameWithoutExtension(rom);
                var pick = games.FirstOrDefault(g => string.Equals(Path.GetFileNameWithoutExtension(g), name, StringComparison.OrdinalIgnoreCase));
                if (pick == null)
                {
                    problem = Path.GetFileName(rom) + " holds " + games.Count + " games and none is named after the archive - keep one game per archive.";
                    return null;
                }
                using var archive = ArchiveFactory.Open(rom);
                var entry = archive.Entries.First(e => e.Key == pick);
                // Under the game's own name inside: the name Mesen gives its saves, as when it opens the archive itself.
                var target = Path.Combine(UnpackDir(exe, rom), Path.GetFileName(pick.Replace('/', '\\')));
                if (File.Exists(target) && new FileInfo(target).Length == entry.Size && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(rom)) return target;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (File.Exists(target)) File.Delete(target);
                using (var src = entry.OpenEntryStream())
                using (var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                    LbIntegrations.Lbip.LbipLaunchStatus.CopyStream(src, dst, entry.Size, "Extracting " + Path.GetFileName(target) + "…");
                Log.Info("unpacked " + pick + " from " + Path.GetFileName(rom) + " (" + games.Count + " games in it) -> " + target);
                return target;
            }
            catch (Exception ex) { Log.Warn("could not open " + rom, ex); problem = Path.GetFileName(rom) + " could not be opened: " + ex.Message; return null; }
        }
    }
}
