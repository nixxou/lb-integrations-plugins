// A game's OWN settings for no$gba (Mehdi, 29/09) - the ones of its Setup window, by the kind of game:
// a GBA cartridge, a DS cartridge, a DSiWare title - each is offered only what applies to it.
//
// no$gba HAS NO PER-GAME SETTING, AND NO COMMAND LINE: everything is NO$GBA.INI, "Key == Value". So a
// game's own values are written there for its session, and the user's own put back after, as for melonDS
// (MelonDsGameSettings) - more simply: no$gba does NOT rewrite its file on exit (NoGbaIni's header,
// measured), only when its Setup window's "Save Now" or OK is used:
//   - a game's values live in <install>\lbip-settings.tsv, one line per LaunchBox game id - only what is
//     set; what is set by hand (the Advanced tab) in <install>\lbip-settings-advanced.tsv;
//   - AT LAUNCH the lines about to change are written down FIRST (<install>\lbip-settings.restore: key TAB
//     1 TAB value, or key TAB 0 for a key that was not there), then the game's are written;
//   - WHEN no$gba HAS QUIT they go back, and a key the session added is taken out;
//   - A NOTE STILL THERE - the host or the machine went mid-session - is put back first: at every launch,
//     at the plugin's start-up check, and when no$gba is opened without a game (LbEmulatorOpened).
//
// THE VALUES ARE no$gba's OWN WORDS, EXACTLY. A value it does not know is IGNORED IN SILENCE (NoGbaIni's
// header) - so every choice offered here comes from no$gba's own table of settings, read out of an
// unpacked copy of NO$GBA.EXE 3.x (29/09): "Key$choice$choice$...", in the order of its drop-downs, each
// choice as the INI holds it - a leading "-" or "/" included: no$gba does not show them in its window
// ("Realtime, Auto") but writes them ("-Realtime, Auto"). Shown here without them, written with them.
//
// NOT THE KEYS THIS PLUGIN SETS FOR EVERY LAUNCH (Managed): the console (NDS Mode/Colors), the boot
// (Reset/Startup Entrypoint), the save format (SAV/SNA File Format) - the DSi mode and the saves depend
// on them; set by hand, they are left out and said so.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LbIntegrations.Dsi;

namespace LbIntegrations.NoGba
{
    [Flags]
    internal enum NoGbaKind { None = 0, Gba = 1, Ds = 2, DsiWare = 4, All = 7 }

    internal static class NoGbaGameSettings
    {
        /// <summary>no$gba's own table of settings and their choices, as NO$GBA.EXE 3.x holds it - see the header.</summary>
        internal static readonly Dictionary<string, string[]> Choices = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["GBA Mode/Colors"] = new[] { "GBA (no backlight)", "GBA SP (backlight)", "NDS in GBA mode (blurry)", "NDS-Lite in GBA mode (poppy)" },
            ["NDS Mode/Colors"] = new[] { "-Nintendo DS (retail/4MB)", "-Nintendo DS (debug/8MB)", "-DS-Lite (retail/4MB)", "-DS-Lite (debug 8MB)", "DSi (retail/16MB)", "DSi (debug/32MB)" },
            ["Emulation Speed, LCD Refresh"] = new[] { "-Realtime, Auto", "Realtime*1.5, Auto", "Realtime*2, Auto", "Realtime/2, Auto", "Realtime/10, Auto", "Unlimited MHz Disaster, 100%", "Unlimited MHz Disaster, 50%", "/Unlimited MHz Disaster, 10%" },
            ["Reset/Startup Entrypoint"] = new[] { "GBA/NDS BIOS (Nintendo logo)", "-Start Cartridge directly" },
            ["Topmost Display Lines"] = new[] { "-Display Normal", "-Emulate Dark Shadow" },
            ["Sound Output Mode"] = new[] { "None", "8bit mono", "8bit stereo", "16bit mono", "16bit stereo" },
            ["Volume Control"] = new[] { "None", "Hardware mixer", "Software multiply" },
            ["Sound Desired Sample Rate"] = new[] { "Low (10kHz) (fast)", "-Medium (22kHz)", "-High (44kHz) (best)" },
            ["Video Output"] = new[] { "8bit Monochrome", "15bit Color", "24bit True Color" },
            ["Emulate BIOS Functions"] = new[] { "By real GBA.ROM (accurate)", "By 80x86 code (fast)" },
            ["Solar Sensor Level"] = new[] { "Darkness", "100 Watts", "Bright Sunlight" },
            ["GBA Cartridge Backup Media"] = new[] { "None", "-Auto", "SRAM 32KBytes", "FLASH 64K SST", "FLASH 64K Atmel", "FLASH 64K Macronix", "FLASH 64K Panasonic", "FLASH 128K Sanyo", "FLASH 128K Macronix", "EEPROM 0.5KBytes", "EEPROM 8KBytes" },
            ["NDS-Cartridge Backup Media"] = new[] { "None", "-Auto", "EEPROM 0.5Kbytes", "EEPROM 8KBytes", "EEPROM 64KBytes", "FLASH 256KBytes", "FLASH 512KBytes", "FLASH 1024KBytes", "FLASH 8192KBytes", "FRAM 32KBytes", "(General 0.5Kbytes)", "(General 8K..64Kbytes)", "(General 256K..8192Kbytes)" },
            ["Multiboot Port"] = new[] { "-None/Disabled", "-LPT1/378H", "-LPT2/278H", "-LPT3/3BCH", "Dslink/Wifi (NDS/DSi)" },
            ["Multiboot Completion"] = new[] { "Keep Upload Box Displayed", "-Auto-close Upload Box" },
            ["Multiboot Normal/BurstDelays"] = new[] { "-Fast/Fast (best)", "Fast/Medium", "Fast/Slow", "Medium/Fast", "-Medium/Medium (stable)", "Medium/Slow", "Slow/Fast", "Slow/Medium", "Slow/Slow" },
            ["Joystick Polling"] = new[] { "Every Frame (slow)", "Each 2nd Frame (fast)" },
            ["Game Screen Filter"] = new[] { "None (fast)", "Scale2x" },
            ["Create Game Window at"] = new[] { "Normal/custom Position", "Upper/right of Debug Window" },
            ["Game Screen Sizing"] = new[] { "Free", "Force 50% step", "Force Aspect Ratio", "Strict" },
            ["IIgame_size"] = new[] { "normal", "maximized" },
            ["Number of Emulated Gameboys"] = new[] { "-Single Machine", "-Two Machines", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12" },
            ["Link Gamepaks"] = new[] { "-Gamepaks in all GBAs", "Master only (Single Gamepak)" },
            ["Link Cable Type"] = new[] { "None", "-Automatic", "-Normal (Two Players)", "-Multiplay (Four Players)", "Wireless Adapter" },
            ["Performance Indicator"] = new[] { "None", "-Show Timing only if <>100%", "Timing relative to real GBA", "Show Timing and Frameskips" },
            ["Autosave Options"] = new[] { "-Nope", "Save on Exit" },
            ["Load ROM-Images to"] = new[] { "-All machines", "1st machine", "2nd machine", "3rd machine", "4th machine" },
            ["FilesysPreview"] = new[] { "Auto", "Hex", "Txt" },
            ["SAV/SNA File Format"] = new[] { "None", "Raw", "Uncompressed", "Compressed (fast/rlu)", "Compressed (good/lz)" },
            ["IIautoRun"] = new[] { "no", "yes" },
            ["IIsnapRun"] = new[] { "no", "yes" },
            ["Firmware Boot"] = new[] { "Manual", "Autostart", "Unchanged" },
            ["DSi RSA signatures"] = new[] { "Insist on RSA", "Allow unencrypted/homebrew" },
            ["3D Renderer"] = new[] { "nocash", "opengl", "none" },
            ["IIsnd"] = new[] { "None", "8bit mono", "8bit stereo", "16bit mono", "16bit stereo" },
            ["IIreg"] = new[] { "show", "fullskip" },
            ["IIcrk"] = new[] { "show", "skip" },
        };

        /// <summary>A setting the options window offers, the kinds of game it applies to, and its tab.</summary>
        internal sealed class Setting
        {
            public string Key, Tab;
            public NoGbaKind Kinds;
        }

        public const string EmulationTab = "Emulation", CartridgeTab = "Cartridge", DisplayTab = "Display & sound", LinkTab = "Link";

        internal static readonly Setting[] Offered =
        {
            new Setting { Key = "Emulation Speed, LCD Refresh", Tab = EmulationTab, Kinds = NoGbaKind.All },
            new Setting { Key = "GBA Mode/Colors",              Tab = EmulationTab, Kinds = NoGbaKind.Gba },
            new Setting { Key = "3D Renderer",                  Tab = EmulationTab, Kinds = NoGbaKind.Ds | NoGbaKind.DsiWare },
            new Setting { Key = "Video Output",                 Tab = EmulationTab, Kinds = NoGbaKind.All },
            new Setting { Key = "GBA Cartridge Backup Media",   Tab = CartridgeTab, Kinds = NoGbaKind.Gba },
            new Setting { Key = "NDS-Cartridge Backup Media",   Tab = CartridgeTab, Kinds = NoGbaKind.Ds },
            new Setting { Key = "Solar Sensor Level",           Tab = CartridgeTab, Kinds = NoGbaKind.Gba },
            new Setting { Key = "Game Screen Sizing",           Tab = DisplayTab,   Kinds = NoGbaKind.All },
            new Setting { Key = "Game Screen Filter",           Tab = DisplayTab,   Kinds = NoGbaKind.All },
            new Setting { Key = "Sound Output Mode",            Tab = DisplayTab,   Kinds = NoGbaKind.All },
            new Setting { Key = "Sound Desired Sample Rate",    Tab = DisplayTab,   Kinds = NoGbaKind.All },
            new Setting { Key = "Volume Control",               Tab = DisplayTab,   Kinds = NoGbaKind.All },
            new Setting { Key = "Number of Emulated Gameboys",  Tab = LinkTab,      Kinds = NoGbaKind.Gba },
            new Setting { Key = "Link Gamepaks",                Tab = LinkTab,      Kinds = NoGbaKind.Gba },
            new Setting { Key = "Link Cable Type",              Tab = LinkTab,      Kinds = NoGbaKind.Gba },
        };

        /// <summary>The keys a launch sets itself - never taken from a game's settings.</summary>
        internal static readonly string[] Managed = { NoGbaDsi.ModeKey, NoGbaDsi.EntryKey, NoGbaConfig.SaveFormatKey };

        internal static bool IsManaged(string key) => Managed.Any(m => string.Equals(m, key, StringComparison.OrdinalIgnoreCase));

        /// <summary>A choice as no$gba's window shows it: without the "-" or "/" it writes before some.</summary>
        internal static string Shown(string raw) => string.IsNullOrEmpty(raw) || (raw[0] != '-' && raw[0] != '/') ? raw : raw.Substring(1);

        private const string StoreName = "lbip-settings.tsv", AdvancedName = "lbip-settings-advanced.tsv", RestoreName = "lbip-settings.restore";

        private static string PathIn(NoGbaLayout layout, string name) => layout?.InstallDir == null ? null : Path.Combine(layout.InstallDir, name);

        // ── a game's own ─────────────────────────────────────────────────────

        /// <summary>The game's own values, key -> value as the INI holds it; null for none.</summary>
        public static Dictionary<string, string> Load(NoGbaLayout layout, string gameId)
        {
            var f = ReadLine(PathIn(layout, StoreName), gameId);
            if (f == null || f.Length < 2) return null;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in f[1].Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                try { values[Uri.UnescapeDataString(part.Substring(0, eq))] = Uri.UnescapeDataString(part.Substring(eq + 1)); } catch { }
            }
            return values.Count > 0 ? values : null;
        }

        public static void Save(NoGbaLayout layout, string gameId, Dictionary<string, string> values)
            => WriteLine(PathIn(layout, StoreName), gameId, values == null || values.Count == 0 ? null
                : string.Join(";", values.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value))));

        /// <summary>The game's text set by hand, and whether it is the one in use; null for none.</summary>
        public static string LoadAdvanced(NoGbaLayout layout, string gameId, out bool on)
        {
            var f = ReadLine(PathIn(layout, AdvancedName), gameId);
            on = f != null && f.Length >= 3 && f[1] == "on";
            try { return f != null && f.Length >= 3 ? Uri.UnescapeDataString(f[2]) : null; } catch { return null; }
        }

        public static void SaveAdvanced(NoGbaLayout layout, string gameId, string text, bool on)
            => WriteLine(PathIn(layout, AdvancedName), gameId, string.IsNullOrWhiteSpace(text) ? null : (on ? "on" : "off") + "\t" + Uri.EscapeDataString(text.Trim()));

        private static string[] ReadLine(string path, string gameId)
        {
            try
            {
                if (path == null || string.IsNullOrWhiteSpace(gameId) || !File.Exists(path)) return null;
                foreach (var line in File.ReadAllLines(path))
                {
                    var f = line.Split('\t');
                    if (f[0].Equals(gameId, StringComparison.OrdinalIgnoreCase)) return f;
                }
            }
            catch (Exception ex) { Log.Warn("game settings: could not read " + Path.GetFileName(path) + " - " + ex.Message); }
            return null;
        }

        private static void WriteLine(string path, string gameId, string rest)
        {
            try
            {
                if (path == null || string.IsNullOrWhiteSpace(gameId)) return;
                var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
                lines.RemoveAll(l => l.Split('\t')[0].Equals(gameId, StringComparison.OrdinalIgnoreCase));
                if (rest != null) lines.Add(gameId + "\t" + rest);
                NoGbaIni.WriteAtomicBytes(path, new UTF8Encoding(false).GetBytes(string.Join("\r\n", lines) + (lines.Count > 0 ? "\r\n" : "")));
                Log.Info("game settings of " + gameId + " (" + Path.GetFileName(path) + "): " + (rest == null ? "none" : "saved"));
            }
            catch (Exception ex) { Log.Warn("game settings: could not write " + Path.GetFileName(path) + " - " + ex.Message); }
        }

        /// <summary>no$gba's own values now, for the keys it holds.</summary>
        public static Dictionary<string, string> Current(NoGbaLayout layout)
            => layout?.IniFile == null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : NoGbaIni.Read(layout.IniFile);

        // ── set by hand ──────────────────────────────────────────────────────

        /// <summary>"Key == Value" lines, ";" comments. Null, with the line, when a line is not one.</summary>
        public static List<KeyValuePair<string, string>> ParseHand(string text, out string error)
        {
            error = null;
            var list = new List<KeyValuePair<string, string>>();
            var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith(";")) continue;
                int at = line.IndexOf("==", StringComparison.Ordinal);
                if (at <= 0) { error = "line " + (i + 1) + ": expected Key == Value"; return null; }
                var key = line.Substring(0, at).Trim();
                var value = line.Substring(at + 2).Trim();
                if (list.Any(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase))) { error = "line " + (i + 1) + ": " + key + " is set twice"; return null; }
                list.Add(new KeyValuePair<string, string>(key, value));
            }
            return list;
        }

        /// <summary>The fragment of a set of keys - what the Advanced tab shows.</summary>
        public static string Fragment(IEnumerable<KeyValuePair<string, string>> keys)
            => string.Concat(keys.Select(kv => kv.Key + " == " + kv.Value + "\r\n"));

        /// <summary>Is this text usable? Null error when it is; the warnings say what it will not do - above
        /// all a value no$gba does not know, which it would ignore without a word.</summary>
        public static List<string> CheckHand(NoGbaLayout layout, string text, out string error)
        {
            var warnings = new List<string>();
            var keys = ParseHand(text, out error);
            if (keys == null) return warnings;
            if (keys.Count == 0) warnings.Add("nothing is set: the game runs on no$gba's settings");
            var held = Current(layout);
            foreach (var kv in keys)
            {
                if (IsManaged(kv.Key)) { warnings.Add(kv.Key + ": set by this plugin for every launch - left out"); continue; }
                if (Choices.TryGetValue(kv.Key, out var choices))
                {
                    if (!choices.Contains(kv.Value, StringComparer.Ordinal))
                        warnings.Add(kv.Key + " == " + kv.Value + ": not one of no$gba's choices - it would ignore it in silence (they are: "
                                     + string.Join(" | ", choices) + ")");
                }
                else if (!held.ContainsKey(kv.Key))
                    warnings.Add(kv.Key + ": not a key of no$gba's table nor of NO$GBA.INI - check its name");
            }
            return warnings;
        }

        // ── the session ──────────────────────────────────────────────────────

        /// <summary>The keys a launch of this game writes: its text set by hand when in use, else its own
        /// values - the managed keys left out either way. Null when none.</summary>
        internal static List<KeyValuePair<string, string>> KeysOf(NoGbaLayout layout, string gameId, out string why)
        {
            why = null;
            var hand = LoadAdvanced(layout, gameId, out var on);
            List<KeyValuePair<string, string>> keys;
            if (on && hand != null)
            {
                keys = ParseHand(hand, out var error);
                if (keys == null) { why = "the settings set by hand cannot be read (" + error + ")"; return null; }
            }
            else keys = Load(layout, gameId)?.ToList();
            if (keys == null) return null;
            var left = keys.Where(kv => IsManaged(kv.Key)).Select(kv => kv.Key).ToList();
            if (left.Count > 0) Log.Info("game settings: left out, this plugin sets them - " + string.Join(", ", left));
            keys = keys.Where(kv => !IsManaged(kv.Key)).ToList();
            return keys.Count > 0 ? keys : null;
        }

        /// <summary>Write the game's own settings for its session, the lines they replace written down
        /// first. True when there were any - the caller then watches for no$gba to quit.</summary>
        public static bool Apply(NoGbaLayout layout, string gameId)
        {
            try
            {
                var keys = KeysOf(layout, gameId, out var why);
                if (why != null) Log.Warn("game settings: " + why + " - this game runs on no$gba's own this time");
                if (keys == null || layout?.IniFile == null) return false;
                var before = Current(layout);
                if (keys.All(kv => before.TryGetValue(kv.Key, out var was) && was == kv.Value))
                { Log.Info("game settings: this game's own are no$gba's already - nothing to write"); return false; }
                // THE NOTE FIRST: written down before a byte of NO$GBA.INI changes.
                var note = string.Concat(keys.Select(kv => kv.Key + "\t" + (before.TryGetValue(kv.Key, out var was) ? "1\t" + was : "0\t") + "\r\n"));
                NoGbaIni.WriteAtomicBytes(PathIn(layout, RestoreName), new UTF8Encoding(false).GetBytes(note));
                var error = NoGbaIni.Write(layout.IniFile, keys.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase));
                if (error != null) { Log.Warn("game settings: not written - " + error); Restore(layout, "the write failed"); return false; }
                Log.Info("game settings: this game's own for its session - " + string.Join(", ", keys.Select(kv => kv.Key + " == " + kv.Value)));
                return true;
            }
            catch (Exception ex) { Log.Warn("game settings: could not apply them", ex); return false; }
        }

        /// <summary>Put back what a session replaced, if a note says so. Never while no$gba runs.</summary>
        public static void Restore(NoGbaLayout layout, string why)
        {
            try
            {
                var note = PathIn(layout, RestoreName);
                if (note == null || !File.Exists(note) || layout.IniFile == null) return;
                if (DsiNand.EmulatorRunning()) { Log.Info("game settings: no$gba is running - its settings go back once it has quit"); return; }
                var rows = File.ReadAllLines(note).Select(l => l.Split(new[] { '\t' }, 3)).Where(f => f.Length >= 2).ToList();
                var back = rows.Where(f => f[1] == "1" && f.Length == 3).ToDictionary(f => f[0], f => f[2], StringComparer.OrdinalIgnoreCase);
                var gone = rows.Where(f => f[1] == "0").Select(f => f[0]).ToList();
                var error = NoGbaIni.Write(layout.IniFile, back) ?? NoGbaIni.Remove(layout.IniFile, gone);
                if (error != null) { Log.Warn("game settings: no$gba's own could not go back yet - " + error); return; }
                File.Delete(note);
                Log.Info("game settings: no$gba's own are back (" + why + ")");
            }
            catch (Exception ex) { Log.Warn("game settings: could not put no$gba's own back", ex); }
        }

        /// <summary>Wait for the no$gba of this launch to come and go, then put its settings back.</summary>
        public static void RestoreWhenDone(NoGbaLayout layout)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var armed = DateTime.UtcNow;
                    bool appeared = false;
                    while ((DateTime.UtcNow - armed).TotalSeconds < 120)
                    {
                        if (DsiNand.EmulatorRunning()) { appeared = true; break; }
                        System.Threading.Thread.Sleep(500);
                    }
                    if (appeared)
                        while (DsiNand.EmulatorRunning()) System.Threading.Thread.Sleep(500);
                    System.Threading.Thread.Sleep(500);
                    Restore(layout, appeared ? "the session is over" : "no$gba never started");
                }
                catch (Exception ex) { Log.Warn("game settings: watching for the end of the session", ex); }
            });
        }

        /// <summary>NO$GBA.INI as a launch would write it with these keys - the managed ones left out -
        /// and, in <paramref name="before"/>, as it is now. Worked out on a copy, by the very write a launch
        /// does.</summary>
        public static string Preview(NoGbaLayout layout, List<KeyValuePair<string, string>> keys, out string before, out string error)
        {
            before = ""; error = null;
            var copy = Path.Combine(Path.GetTempPath(), "lbip-preview-" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                if (layout?.IniFile != null && File.Exists(layout.IniFile)) before = File.ReadAllText(layout.IniFile);
                File.WriteAllText(copy, before);
                var used = (keys ?? new List<KeyValuePair<string, string>>()).Where(kv => !IsManaged(kv.Key)).ToList();
                if (used.Count == 0) { error = "nothing is set: the game runs on no$gba's settings as they are"; return null; }
                error = NoGbaIni.Write(copy, used.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase));
                return error == null ? File.ReadAllText(copy) : null;
            }
            catch (Exception ex) { error = ex.Message; return null; }
            finally { try { File.Delete(copy); } catch { } }
        }
    }
}
