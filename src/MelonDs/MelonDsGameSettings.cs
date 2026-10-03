// A game's OWN settings for melonDS (Mehdi, 29/09) - two families, each with its own tab and its own
// "Overwrite" box in the options window:
//   VIDEO     the ones of melonDS's Video settings window;
//   FIRMWARE  the ones of its Firmware settings window - the name, language, birthday, colour and
//             message the console says it belongs to, and the MAC address.
//
// melonDS HAS NO PER-GAME SETTING, AND NO WAY TO BE GIVEN ANOTHER CONFIG FILE (CLI.cpp: -b, -f, -a, -A,
// nothing else). These are global keys of melonDS.toml (Config.cpp):
//     [3D]       Renderer          0 software, 1 OpenGL (classic), 2 OpenGL (compute shader)
//     [3D.Soft]  Threaded
//     [3D.GL]    ScaleFactor 1-16, BetterPolygons, HiresCoordinates
//     [Screen]   UseGL, VSync, VSyncInterval 1-20
//     [Instance0.Firmware]  OverrideSettings, Username (10 characters), Language 0-5 (Japanese, English,
//                French, German, Italian, Spanish), BirthdayMonth 1-12, BirthdayDay 1-31,
//                FavouriteColour 0-15, Message (26 characters), MAC (6 hex pairs, or empty)
// So a game's own values are written into it for the length of its session, and the user's own values
// put back afterwards:
//   - a game's values live in <install>\lbip-settings.tsv, one line per LaunchBox game id - only the
//     games that have their own; the others run on melonDS's settings as they are;
//   - AT LAUNCH, the values about to be replaced are written down FIRST (<install>\lbip-settings.restore),
//     then the game's are written - melonDS reads its file when it starts;
//   - WHEN melonDS HAS QUIT - it rewrites its whole file on exit, the game's values with it - the values
//     written down go back, and the note is deleted. A change made to these settings DURING such a
//     game is therefore not kept (the log says the session ended; the user's settings come back);
//   - melonDS.toml AS IT WAS is kept too (<install>\lbip-settings.original.toml): melonDS that cannot parse its
//     file starts from an EMPTY document and writes it over the file as it quits (Config.cpp, Load and Save - the
//     recovery line is commented out). A file left without one table of the many it had is that: put back whole.
//     And a value that is not a plain TOML value (set by hand) is never written - it would be the cause;
//   - A session's note names it (Mehdi / adversarial review, 30/09): a launch's watcher puts back ONLY its own session - a
//     later launch's, started while it was still waiting, is not its to touch.
//   - A NOTE STILL THERE - the host or the machine went mid-session - is put back by the next launch and
//     by the plugin's start-up check, before anything else. Opening melonDS on its own in between shows
//     the game's values; changed there, they are put back over later (Mehdi: "pas un drame").
//
// A GAME'S OWN FIRMWARE ALWAYS CARRIES OverrideSettings = true: melonDS applies the values only with it
// (EmuInstance.cpp, customizeFirmware and loadNAND), except on its built-in firmware, where it always
// does. In DSi mode those values are also written INTO THE NAND at every boot - see
// DsiWorkspace.MarkForced for what that means for a DSiWare save, and ForcesFirmware below.
//
// SET BY HAND (Mehdi, 29/09): the options window's "Advanced" tab shows what the game overrides as a TOML
// fragment - only the keys set, by table - and, "Edit by hand" ticked, takes the user's own text instead:
// any table, any key. Kept in <install>\lbip-settings-advanced.tsv, live when ticked; the Video and
// Firmware values are then set aside (kept, not used). At launch its keys go in as the tabs' do - the
// values they replace written down first, put back once melonDS has quit, and a key the session ADDED
// taken out again. NOT the keys this plugin sets for a launch itself (Managed): the console, the boot, the
// NAND, the BIOS and the save folders - a DSiWare session depends on them; they are left out, and said so.
//
// The restore note is by table and key (table TAB key TAB 1 TAB value as written, or 0 for a key that was
// not there); a note of the first form (Id=value;...) still reads.
//
// The first version of this file held the video family alone, in lbip-video.tsv and lbip-video.restore:
// an install that has those has them renamed at the first use (Migrate).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LbIntegrations.Dsi;

namespace LbIntegrations.MelonDs
{
    internal static class MelonDsGameSettings
    {
        internal enum Kind { Bool, Int, Text }

        internal sealed class Setting
        {
            public string Id, Family, Table, Key;
            public Kind Kind;
            public int Min, Max;        // an Int's range; a Text's longest length (Max)
            public string Default;
        }

        public const string Video = "Video", Firmware = "Firmware";
        internal const string FirmwareTable = MelonDsPaths.InstanceTable + ".Firmware";

        /// <summary>The firmware's "Override settings from external firmware" - in every game's own firmware.</summary>
        internal const string OverrideId = "FwOverride";

        private static Setting V(string id, string table, Kind kind, string def, int min = 0, int max = 0)
            => new Setting { Id = id, Family = Video, Table = table, Key = id, Kind = kind, Min = min, Max = max, Default = def };

        private static Setting F(string id, string key, Kind kind, string def, int min = 0, int max = 0)
            => new Setting { Id = id, Family = Firmware, Table = FirmwareTable, Key = key, Kind = kind, Min = min, Max = max, Default = def };

        internal static readonly Setting[] Settings =
        {
            V("Renderer",         "3D",      Kind.Int,  "0", 0, 2),
            V("Threaded",         "3D.Soft", Kind.Bool, "true"),
            V("ScaleFactor",      "3D.GL",   Kind.Int,  "1", 1, 16),
            V("BetterPolygons",   "3D.GL",   Kind.Bool, "false"),
            V("HiresCoordinates", "3D.GL",   Kind.Bool, "true"),
            V("UseGL",            "Screen",  Kind.Bool, "false"),
            V("VSync",            "Screen",  Kind.Bool, "false"),
            V("VSyncInterval",    "Screen",  Kind.Int,  "1", 1, 20),

            F(OverrideId,         "OverrideSettings", Kind.Bool, "false"),
            F("FwUsername",       "Username",         Kind.Text, "melonDS", max: 10),
            F("FwLanguage",       "Language",         Kind.Int,  "1", 0, 5),
            F("FwBirthdayMonth",  "BirthdayMonth",    Kind.Int,  "1", 1, 12),
            F("FwBirthdayDay",    "BirthdayDay",      Kind.Int,  "1", 1, 31),
            F("FwColour",         "FavouriteColour",  Kind.Int,  "0", 0, 15),
            F("FwMessage",        "Message",          Kind.Text, "", max: 26),
            F("FwMAC",            "MAC",              Kind.Text, "", max: 17),
        };

        /// <summary>melonDS's own names, in its order (FirmwareSettingsDialog.h).</summary>
        internal static readonly string[] Languages = { "Japanese", "English", "French", "German", "Italian", "Spanish" };
        internal static readonly string[] Colours =
        {
            "Greyish blue", "Brown", "Red", "Light pink", "Orange", "Yellow", "Lime", "Light green",
            "Dark green", "Turquoise", "Light blue", "Blue", "Dark blue", "Dark purple", "Light purple", "Dark pink",
        };
        internal static readonly System.Drawing.Color[] ColourValues =
        {
            System.Drawing.Color.FromArgb(97, 130, 154), System.Drawing.Color.FromArgb(186, 73, 0),
            System.Drawing.Color.FromArgb(251, 0, 24),   System.Drawing.Color.FromArgb(251, 138, 251),
            System.Drawing.Color.FromArgb(251, 146, 0),  System.Drawing.Color.FromArgb(243, 227, 0),
            System.Drawing.Color.FromArgb(170, 251, 0),  System.Drawing.Color.FromArgb(0, 251, 0),
            System.Drawing.Color.FromArgb(0, 162, 56),   System.Drawing.Color.FromArgb(73, 219, 138),
            System.Drawing.Color.FromArgb(48, 186, 243), System.Drawing.Color.FromArgb(0, 89, 243),
            System.Drawing.Color.FromArgb(0, 0, 146),    System.Drawing.Color.FromArgb(138, 0, 211),
            System.Drawing.Color.FromArgb(211, 0, 235),  System.Drawing.Color.FromArgb(251, 0, 246),
        };

        private const string StoreName = "lbip-settings.tsv", RestoreName = "lbip-settings.restore";
        private const string OldStoreName = "lbip-video.tsv", OldRestoreName = "lbip-video.restore";

        private static string StorePath(string installDir) => installDir == null ? null : Migrate(installDir, OldStoreName, StoreName);
        private static string RestorePath(string installDir) => installDir == null ? null : Migrate(installDir, OldRestoreName, RestoreName);

        /// <summary>The first version's file under its new name - see the header.</summary>
        private static string Migrate(string installDir, string oldName, string name)
        {
            var path = Path.Combine(installDir, name);
            try
            {
                var old = Path.Combine(installDir, oldName);
                if (!File.Exists(path) && File.Exists(old)) { File.Move(old, path); Log.Info("game settings: " + oldName + " is now " + name); }
            }
            catch (Exception ex) { Log.Warn("game settings: could not rename " + oldName, ex); }
            return path;
        }

        internal static IEnumerable<Setting> Of(string family) => Settings.Where(s => s.Family == family);

        /// <summary>Does this set hold values of that family?</summary>
        internal static bool Has(Dictionary<string, string> values, string family)
            => values != null && Of(family).Any(s => values.ContainsKey(s.Id));

        /// <summary>A MAC as melonDS accepts it (FirmwareSettingsDialog::verifyMAC): empty, or six hex pairs,
        /// all together or with one separator between each.</summary>
        internal static bool IsMac(string mac)
        {
            mac = mac ?? "";
            if (mac.Length == 0) return true;
            if (mac.Length != 12 && mac.Length != 17) return false;
            bool sep = mac.Length == 17;
            for (int i = 0, pos = 0; i < mac.Length;)
            {
                if (!Uri.IsHexDigit(mac[i])) return false;
                i++;
                if (++pos >= 2) { pos = 0; if (sep) i++; }
            }
            return true;
        }

        /// <summary>A value as melonDS.toml wants it: true/false, a number in range, or a text no longer
        /// than melonDS keeps. Null when it is none of those.</summary>
        internal static string Normal(Setting s, string value)
        {
            if (s.Kind == Kind.Text)
            {
                value = (value ?? "").Replace("\r", "").Replace("\n", " ");
                if (s.Id == "FwMAC") return IsMac(value.Trim()) ? value.Trim() : null;
                return value.Length > s.Max ? value.Substring(0, s.Max) : value;
            }
            value = (value ?? "").Trim();
            if (s.Kind == Kind.Bool)
                return value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true"
                     : value.Equals("false", StringComparison.OrdinalIgnoreCase) ? "false" : null;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= s.Min && n <= s.Max
                ? n.ToString(CultureInfo.InvariantCulture) : null;
        }

        // ── a game's own values ──────────────────────────────────────────────

        /// <summary>The game's own values, or null when it runs on melonDS's settings.</summary>
        public static Dictionary<string, string> Load(string installDir, string gameId)
        {
            try
            {
                var path = StorePath(installDir);
                if (path == null || string.IsNullOrWhiteSpace(gameId) || !File.Exists(path)) return null;
                foreach (var line in File.ReadAllLines(path))
                {
                    var f = line.Split('\t');
                    if (f.Length < 2 || !string.Equals(f[0], gameId, StringComparison.OrdinalIgnoreCase)) continue;
                    var values = Parse(f[1]);
                    return values.Count > 0 ? values : null;
                }
            }
            catch (Exception ex) { Log.Warn("game settings: could not read the games' own settings", ex); }
            return null;
        }

        /// <summary>Give a game its own values - or take them away, with null. A game's own firmware is
        /// always saved with the override on - see the header.</summary>
        public static void Save(string installDir, string gameId, Dictionary<string, string> values)
        {
            try
            {
                var path = StorePath(installDir);
                if (path == null || string.IsNullOrWhiteSpace(gameId)) return;
                if (values != null && Has(values, Firmware)) values = new Dictionary<string, string>(values, StringComparer.Ordinal) { [OverrideId] = "true" };
                var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
                lines.RemoveAll(l => l.Split('\t')[0].Equals(gameId, StringComparison.OrdinalIgnoreCase));
                if (values != null && values.Count > 0) lines.Add(gameId + "\t" + Format(values));
                MelonDsToml.WriteAtomicBytes(path, Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + (lines.Count > 0 ? "\r\n" : "")));
            }
            catch (Exception ex) { Log.Warn("game settings: could not save the game's own settings", ex); }
        }

        /// <summary>melonDS's own values now - its default for a key the file does not hold.</summary>
        public static Dictionary<string, string> Current(string configFile) => TryCurrent(configFile, out var v) ? v : v;

        /// <summary>The same, FALSE when the file could not be read: then the values are only defaults,
        /// and must never be written down as somebody's own.</summary>
        public static bool TryCurrent(string configFile, out Dictionary<string, string> values)
        {
            bool known = true;
            values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var group in Settings.GroupBy(s => s.Table))
            {
                if (!MelonDsToml.TryRead(configFile, group.Key, out var read, group.Select(s => s.Key).ToArray())) known = false;
                foreach (var s in group)
                    values[s.Id] = (read.TryGetValue(s.Key, out var v) ? Normal(s, v) : null) ?? s.Default;
            }
            return known;
        }

        /// <summary>Will this game's session start with the console's settings FORCED - its own firmware,
        /// or melonDS's override on? Asked before the session is prepared: a DSiWare image started that
        /// way has the forced settings written into it (DsiWorkspace.MarkForced).</summary>
        public static bool ForcesFirmware(MelonDsLayout layout, string gameId)
        {
            try
            {
                var hand = LoadAdvanced(layout?.InstallDir, gameId, out var on);
                if (on && hand != null)
                {
                    var over = ParseHand(hand, out _)?.FirstOrDefault(k => k.Table == FirmwareTable && k.Key == "OverrideSettings");
                    if (over != null) return over.Token == "true";
                    return GlobalOverride(layout);
                }
                var own = Load(layout?.InstallDir, gameId);
                if (own != null && own.TryGetValue(OverrideId, out var o)) return o == "true";
                return GlobalOverride(layout);
            }
            catch { return false; }
        }

        // ── melonDS's own, changed from outside a session ────────────────────

        /// <summary>Write melonDS's OWN values - the pack's configuration window (MelonDsSettingsPage).
        /// Refused while melonDS runs: it writes its whole file as it quits. A session that never ended
        /// is put back first, so what is changed is melonDS's own and not a game's. Null, or why not.</summary>
        public static string WriteOwn(MelonDsLayout layout, Dictionary<string, string> values)
        {
            try
            {
                if (layout?.ConfigFile == null) return "melonDS's configuration file was not found";
                if (DsiNand.EmulatorRunning()) return "melonDS is running - close it first";
                Restore(layout, "melonDS's own settings are being changed");
                if (File.Exists(RestorePath(layout.InstallDir))) return "a game's settings are still in melonDS and could not be put back";
                var error = WriteValues(layout.ConfigFile, values, force: true);
                if (error == null) Log.Info("game settings: melonDS's own changed - " + Format(values));
                return error;
            }
            catch (Exception ex) { return ex.Message; }
        }

        // ── set by hand ──────────────────────────────────────────────────────

        private const string AdvancedName = "lbip-settings-advanced.tsv";

        /// <summary>The keys a launch sets itself - never taken from a text set by hand.</summary>
        internal static readonly (string Table, string Key)[] Managed =
        {
            (MelonDsPaths.DSiTable, "NANDPath"), (MelonDsPaths.DSiTable, "BIOS7Path"), (MelonDsPaths.DSiTable, "BIOS9Path"), (MelonDsPaths.DSiTable, "FirmwarePath"),
            (MelonDsPaths.DsTable, "BIOS7Path"), (MelonDsPaths.DsTable, "BIOS9Path"), (MelonDsPaths.DsTable, "FirmwarePath"),
            (MelonDsPaths.EmuTable, "ConsoleType"), (MelonDsPaths.EmuTable, "DirectBoot"), (MelonDsPaths.EmuTable, MelonDsPaths.KeyExternalBios),
            (MelonDsPaths.InstanceTable, MelonDsPaths.KeySaveFilePath), (MelonDsPaths.InstanceTable, MelonDsPaths.KeySavestatePath),
        };

        internal static bool IsManaged(string table, string key) => Managed.Any(m => m.Table == table && m.Key == key);

        /// <summary>One key of melonDS.toml, its value as written.</summary>
        internal sealed class Raw
        {
            public string Table, Key, Token;
        }

        /// <summary>The game's text set by hand, and whether it is the one in use; null when it has none.</summary>
        public static string LoadAdvanced(string installDir, string gameId, out bool on)
        {
            on = false;
            try
            {
                var path = installDir == null ? null : Path.Combine(installDir, AdvancedName);
                if (path == null || string.IsNullOrWhiteSpace(gameId) || !File.Exists(path)) return null;
                foreach (var line in File.ReadAllLines(path))
                {
                    var f = line.Split('\t');
                    if (f.Length < 3 || !f[0].Equals(gameId, StringComparison.OrdinalIgnoreCase)) continue;
                    on = f[1] == "on";
                    return Uri.UnescapeDataString(f[2]);
                }
            }
            catch (Exception ex) { Log.Warn("game settings: could not read the settings set by hand", ex); }
            return null;
        }

        public static void SaveAdvanced(string installDir, string gameId, string text, bool on)
        {
            try
            {
                if (installDir == null || string.IsNullOrWhiteSpace(gameId)) return;
                var path = Path.Combine(installDir, AdvancedName);
                var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
                lines.RemoveAll(l => l.Split('\t')[0].Equals(gameId, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(text)) lines.Add(gameId + "\t" + (on ? "on" : "off") + "\t" + Uri.EscapeDataString(text.Trim()));
                MelonDsToml.WriteAtomicBytes(path, Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + (lines.Count > 0 ? "\r\n" : "")));
                Log.Info("game settings of " + gameId + ": set by hand " + (string.IsNullOrWhiteSpace(text) ? "- none" : on ? "- in use" : "- kept, not in use"));
            }
            catch (Exception ex) { Log.Warn("game settings: could not save the settings set by hand", ex); }
        }

        /// <summary>A set of the tabs' values as TOML keys, the override with the firmware.</summary>
        internal static List<Raw> ToRaw(Dictionary<string, string> values)
        {
            var list = new List<Raw>();
            if (values == null) return list;
            if (Has(values, Firmware) && !values.ContainsKey(OverrideId)) values = new Dictionary<string, string>(values, StringComparer.Ordinal) { [OverrideId] = "true" };
            foreach (var s in Settings.Where(x => values.ContainsKey(x.Id)))
                list.Add(new Raw { Table = s.Table, Key = s.Key, Token = s.Kind == Kind.Text ? MelonDsToml.Text(values[s.Id]) : values[s.Id] });
            return list;
        }

        /// <summary>The TOML fragment of a set of keys - what the Advanced tab shows.</summary>
        public static string Fragment(IEnumerable<Raw> keys)
        {
            var sb = new StringBuilder();
            foreach (var t in keys.GroupBy(k => k.Table))
            {
                if (sb.Length > 0) sb.Append("\r\n");
                if (t.Key.Length > 0) sb.Append("[" + t.Key + "]\r\n");
                foreach (var k in t) sb.Append(k.Key + " = " + k.Token + "\r\n");
            }
            return sb.ToString();
        }

        /// <summary>Read a TOML fragment: [table] headers, key = value lines, # comments. A value must be
        /// true/false, a number, a quoted string or a one-line array. Null, with the line, when it is not.</summary>
        public static List<Raw> ParseHand(string text, out string error)
        {
            error = null;
            var list = new List<Raw>();
            var table = "";
            var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (line[0] == '[')
                {
                    int close = line.IndexOf(']');
                    var name = close < 0 ? "" : line.Substring(1, close - 1).Trim();
                    if (close < 0 || name.Length == 0 || name.StartsWith("[") || line.Substring(close + 1).Trim().Length > 0 && line.Substring(close + 1).Trim()[0] != '#')
                    { error = "line " + (i + 1) + ": a table header is [name]"; return null; }
                    table = name;
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) { error = "line " + (i + 1) + ": expected key = value"; return null; }
                var key = line.Substring(0, eq).Trim();
                var token = line.Substring(eq + 1).Trim();
                if (!System.Text.RegularExpressions.Regex.IsMatch(key, "^[A-Za-z0-9_-]+$")) { error = "line " + (i + 1) + ": \"" + key + "\" is not a key name"; return null; }
                if (!IsToken(token)) { error = "line " + (i + 1) + ": " + key + " = " + token + " is not a value (true/false, a number, 'text', \"text\" or [...])"; return null; }
                if (list.Any(r => r.Table == table && r.Key == key)) { error = "line " + (i + 1) + ": " + key + " is set twice in [" + table + "]"; return null; }
                list.Add(new Raw { Table = table, Key = key, Token = token });
            }
            return list;
        }

        private static bool IsToken(string t)
        {
            if (t.Length == 0) return false;
            if (t == "true" || t == "false") return true;
            if (t[0] == '\'') return t.Length >= 2 && t.IndexOf('\'', 1) == t.Length - 1;
            if (t[0] == '"') return t.Length >= 2 && t[t.Length - 1] == '"';
            if (t[0] == '[') return t[t.Length - 1] == ']';
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }

        /// <summary>Is this text usable? Null error when it is; the warnings say what it will not do.</summary>
        public static List<string> CheckHand(MelonDsLayout layout, string text, out string error)
        {
            var warnings = new List<string>();
            var keys = ParseHand(text, out error);
            if (keys == null) return warnings;
            if (keys.Count == 0) warnings.Add("nothing is set: the game runs on melonDS's settings");
            var known = layout?.ConfigFile == null ? new Dictionary<string, HashSet<string>>() : MelonDsToml.Keys(layout.ConfigFile);
            foreach (var k in keys)
            {
                if (IsManaged(k.Table, k.Key))
                    warnings.Add("[" + k.Table + "] " + k.Key + ": set by this plugin for every launch - left out");
                else if (!(known.TryGetValue(k.Table, out var inTable) && inTable.Contains(k.Key)) && !Settings.Any(s => s.Table == k.Table && s.Key == k.Key))
                    warnings.Add("[" + k.Table + "] " + k.Key + ": not in melonDS.toml - melonDS only writes the keys it has read or been set, so check its name");
            }
            bool firmware = keys.Any(k => k.Table == FirmwareTable && k.Key != "OverrideSettings");
            var over = keys.FirstOrDefault(k => k.Table == FirmwareTable && k.Key == "OverrideSettings");
            if (firmware && (over == null ? !GlobalOverride(layout) : over.Token != "true"))
                warnings.Add("[" + FirmwareTable + "] without OverrideSettings = true: melonDS applies these only on its built-in firmware");
            return warnings;
        }

        /// <summary>melonDS.toml as a launch would write it with these keys - the keys a launch sets itself
        /// left out - and, in <paramref name="before"/>, as it is now. Worked out on a copy: the very write a
        /// launch does, nothing of melonDS's own file touched.</summary>
        public static string Preview(MelonDsLayout layout, List<Raw> keys, out string before, out string error)
        {
            before = ""; error = null;
            var copy = Path.Combine(Path.GetTempPath(), "lbip-preview-" + Guid.NewGuid().ToString("N") + ".toml");
            try
            {
                if (layout?.ConfigFile != null && File.Exists(layout.ConfigFile)) before = File.ReadAllText(layout.ConfigFile);
                File.WriteAllText(copy, before);
                var used = (keys ?? new List<Raw>()).Where(k => !IsManaged(k.Table, k.Key)).ToList();
                if (used.Count == 0) { error = "nothing is set: the game runs on melonDS's settings as they are"; return null; }
                foreach (var t in used.GroupBy(k => k.Table))
                {
                    error = MelonDsToml.Write(copy, t.Key, t.ToDictionary(k => k.Key, k => k.Token, StringComparer.Ordinal), force: true);
                    if (error != null) return null;
                }
                return File.ReadAllText(copy);
            }
            catch (Exception ex) { error = ex.Message; return null; }
            finally { try { File.Delete(copy); } catch { } }
        }

        private static bool GlobalOverride(MelonDsLayout layout)
        {
            if (layout?.ConfigFile == null) return false;
            MelonDsToml.TryRead(layout.ConfigFile, FirmwareTable, out var read, "OverrideSettings");
            return read.TryGetValue("OverrideSettings", out var v) && Normal(Settings.First(s => s.Id == OverrideId), v) == "true";
        }

        // ── the session ──────────────────────────────────────────────────────

        /// <summary>Write the game's own values for its session, having written down the ones they
        /// replace. True when there were any - the caller then watches for melonDS to quit.</summary>
        public static bool Apply(MelonDsLayout layout, string gameId)
        {
            // The DSi console's language when the override would force one its region does not have - in the same session.
            var fix = DsiLanguageFix(layout, gameId);
            var hand = LoadAdvanced(layout?.InstallDir, gameId, out var on);
            if (on && hand != null)
            {
                var keys = ParseHand(hand, out var error);
                if (keys == null) { Log.Warn("game settings: the settings set by hand cannot be read (" + error + ") - this game runs on melonDS's this time"); return fix != null && ApplyRaw(layout, new List<Raw> { fix }, "the DSi console's language"); }
                var left = keys.Where(k => IsManaged(k.Table, k.Key)).ToList();
                if (left.Count > 0) Log.Info("game settings: left out of the settings set by hand, this plugin sets them - " + string.Join(", ", left.Select(k => "[" + k.Table + "] " + k.Key)));
                return ApplyRaw(layout, WithFix(keys.Where(k => !IsManaged(k.Table, k.Key)).ToList(), fix), "this game's own settings, set by hand");
            }
            var own = Load(layout?.InstallDir, gameId);
            if (own == null) return fix != null && ApplyRaw(layout, new List<Raw> { fix }, "the DSi console's language");
            if (Has(own, Firmware)) own[OverrideId] = "true";
            return fix == null ? ApplyValues(layout, own, "this game's own settings") : ApplyRaw(layout, WithFix(ToRaw(own), fix), "this game's own settings");
        }

        private static List<Raw> WithFix(List<Raw> keys, Raw fix)
        {
            if (fix == null) return keys;
            keys.RemoveAll(k => k.Table == fix.Table && k.Key == fix.Key);
            keys.Add(fix);
            return keys;
        }

        /// <summary>IN DSi MODE melonDS's firmware override writes its Language into the NAND at every boot (EmuInstance.cpp,
        /// loadNAND) - unchecked: a French identity on a Japanese console, whose region has Japanese alone (HWINFO_S.dat's
        /// language mask). So for a DSi session with the override on, a language the console's region does not have is
        /// replaced, for that session, by the console's own - its TWLCFG0.dat, else English, else its first. Null when nothing
        /// is to change (a DS launch, no override, a language the console has, or the NAND unreadable).</summary>
        internal static Raw DsiLanguageFix(MelonDsLayout layout, string gameId)
        {
            string dir = null;
            try
            {
                if (layout?.ConfigFile == null) return null;
                var emu = MelonDsToml.Read(layout.ConfigFile, MelonDsPaths.EmuTable, "ConsoleType");
                if (MelonDsToml.AsInt(emu.TryGetValue("ConsoleType", out var ct) ? ct : null, 0) != 1) return null;
                if (!ForcesFirmware(layout, gameId)) return null;

                // The language the session would force: set by hand, the game's own, else melonDS's.
                int language = 1;
                var hand = LoadAdvanced(layout.InstallDir, gameId, out var on);
                var handKey = on && hand != null ? ParseHand(hand, out _)?.FirstOrDefault(k => k.Table == FirmwareTable && k.Key == "Language") : null;
                var own = on ? null : Load(layout.InstallDir, gameId);
                if (handKey != null) int.TryParse(handKey.Token, out language);
                else if (own != null && own.TryGetValue("FwLanguage", out var ol)) int.TryParse(ol, out language);
                else
                {
                    var fw = MelonDsToml.Read(layout.ConfigFile, FirmwareTable, "Language");
                    language = MelonDsToml.AsInt(fw.TryGetValue("Language", out var gl) ? gl : null, 1);
                }

                var dsi = MelonDsToml.Read(layout.ConfigFile, MelonDsPaths.DSiTable, "NANDPath", "BIOS7Path");
                string nand = dsi.TryGetValue("NANDPath", out var n) ? Absolute(layout, n) : null;
                string bios7 = dsi.TryGetValue("BIOS7Path", out var b) ? Absolute(layout, b) : null;
                if (nand == null || bios7 == null || !File.Exists(nand) || !File.Exists(bios7)) return null;

                dir = Path.Combine(Path.GetTempPath(), "lbip-dsilang-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                uint mask;
                int consoles;
                using (var session = DsiNand.Open(nand, bios7, out _))
                {
                    if (session == null) return null;
                    string hw = Path.Combine(dir, "hw"), cfg = Path.Combine(dir, "cfg");
                    if (!session.ExportFile(DsiRegions.HardwareInfoInNand, hw, out _)) return null;
                    mask = DsiUserSettings.LanguageMask(File.ReadAllBytes(hw));
                    if ((mask & (1u << language)) != 0) return null;
                    consoles = session.ExportFile(DsiUserSettings.Settings0, cfg, out _) ? File.ReadAllBytes(cfg)[0x8E] : -1;
                }
                int kept = consoles >= 0 && (mask & (1u << consoles)) != 0 ? consoles : (mask & 2u) != 0 ? 1 : Enumerable.Range(0, 8).FirstOrDefault(i => (mask & (1u << i)) != 0);
                Log.Info("game settings: language " + language + " is not one of this DSi console's (mask 0x" + mask.ToString("X") + ") - "
                         + "language " + kept + " for this session");
                return new Raw { Table = FirmwareTable, Key = "Language", Token = kept.ToString(CultureInfo.InvariantCulture) };
            }
            catch (Exception ex) { Log.Verbose("game settings: the DSi console's languages could not be read - " + ex.Message); return null; }
            finally { try { if (dir != null) Directory.Delete(dir, true); } catch { } }
        }

        private static string Absolute(MelonDsLayout layout, string path)
        {
            try { return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(layout.ConfigDir, path)); } catch { return path; }
        }

        /// <summary>Write <paramref name="values"/> until the next Restore - the note first. For a game's
        /// session, and for anything else melonDS must run with once (the console's setup).</summary>
        public static bool ApplyValues(MelonDsLayout layout, Dictionary<string, string> values, string what)
            => values != null && values.Count > 0 && ApplyRaw(layout, ToRaw(values), what);

        /// <summary>Write these keys until the next Restore, the values they replace - or their absence -
        /// written down first.</summary>
        internal static bool ApplyRaw(MelonDsLayout layout, List<Raw> keys, string what)
        {
            try
            {
                if (keys == null || keys.Count == 0 || layout?.ConfigFile == null) return false;
                // NEVER A VALUE melonDS COULD NOT PARSE: its file would come back empty (see the header).
                var bad = keys.Where(k => !MelonDsToml.IsPlainValue(k.Token)).ToList();
                foreach (var k in bad) Log.Warn("game settings: [" + k.Table + "] " + k.Key + " = " + k.Token + " is not a plain TOML value - not written");
                keys = keys.Except(bad).ToList();
                if (keys.Count == 0) return false;
                // melonDS's own, to be put back - and so they must be REALLY its own: a file that could not
                // be read writes nothing.
                var session = Guid.NewGuid().ToString("N");
                var note = new StringBuilder("session\t" + session + "\r\n");
                bool same = true;
                foreach (var t in keys.GroupBy(k => k.Table))
                {
                    if (!MelonDsToml.TryReadRaw(layout.ConfigFile, t.Key, out var was, t.Select(k => k.Key).ToArray()))
                    {
                        Log.Warn("game settings: melonDS's configuration could not be read - " + what + " are not written this time");
                        return false;
                    }
                    foreach (var k in t)
                    {
                        bool there = was.TryGetValue(k.Key, out var old);
                        if (!there || old != k.Token) same = false;
                        note.Append(k.Table + "\t" + k.Key + "\t" + (there ? "1\t" + old : "0\t") + "\r\n");
                    }
                }
                if (same) { Log.Info("game settings: " + what + " are melonDS's already - nothing to write"); return false; }
                // THE FILE AS IT IS, then THE NOTE: both before a byte of melonDS.toml changes.
                if (File.Exists(layout.ConfigFile))
                    MelonDsToml.WriteAtomicBytes(OriginalPath(layout.InstallDir), File.ReadAllBytes(layout.ConfigFile));
                MelonDsToml.WriteAtomicBytes(RestorePath(layout.InstallDir), Encoding.UTF8.GetBytes(note.ToString()));
                foreach (var t in keys.GroupBy(k => k.Table))
                {
                    var error = MelonDsToml.Write(layout.ConfigFile, t.Key, t.ToDictionary(k => k.Key, k => k.Token, StringComparer.Ordinal), force: true);
                    if (error != null) { Log.Warn("game settings: " + what + " were not written - " + error); Restore(layout, "the write failed"); return false; }
                }
                Log.Info("game settings: " + what + " for the session - " + string.Join(", ", keys.Select(k => "[" + k.Table + "] " + k.Key + " = " + k.Token)));
                LastSession = session;
                return true;
            }
            catch (Exception ex) { Log.Warn("game settings: could not apply " + what, ex); return false; }
        }

        /// <summary>Put back the values a session replaced, if a note says there are any. Never while
        /// melonDS runs - it would write its own over them when it quits.</summary>
        /// <summary>The id of the session the last ApplyRaw started - for its watcher.</summary>
        internal static string LastSession;

        /// <summary>The session a note names, or null (a note of before 30/09).</summary>
        private static string SessionOf(string note)
        {
            try { var first = File.ReadLines(note).FirstOrDefault() ?? ""; return first.StartsWith("session\t", StringComparison.Ordinal) ? first.Substring(8) : null; }
            catch { return null; }
        }

        public static void Restore(MelonDsLayout layout, string why, string session = null)
        {
            try
            {
                var note = RestorePath(layout?.InstallDir);
                var original = OriginalPath(layout?.InstallDir);
                if (note == null || !File.Exists(note) || layout.ConfigFile == null)
                {
                    if (original != null && File.Exists(original) && !DsiNand.EmulatorRunning()) File.Delete(original);   // no session: nothing it guards
                    return;
                }
                if (DsiNand.EmulatorRunning()) { Log.Info("game settings: melonDS is running - its settings go back once it has quit"); return; }
                if (session != null && SessionOf(note) != session) { Log.Info("game settings: the session on is a later launch's - left to it (" + why + ")"); return; }
                // melonDS emptied its file (it could not parse it): the whole of it back, ours not in it.
                if (File.Exists(original) && Wiped(File.ReadAllText(original), File.Exists(layout.ConfigFile) ? File.ReadAllText(layout.ConfigFile) : null))
                {
                    MelonDsToml.WriteAtomicBytes(layout.ConfigFile, File.ReadAllBytes(original));
                    File.Delete(note);
                    File.Delete(original);
                    Log.Warn("game settings: melonDS left its settings file empty (it could not read it) - put back whole, as it was before the session (" + why + ")");
                    return;
                }
                var text = File.ReadAllText(note);
                string error = null;
                if (text.IndexOf('\t') < 0) error = WriteValues(layout.ConfigFile, Parse(text), force: false);   // the first form
                else
                {
                    var rows = text.Replace("\r\n", "\n").Split('\n').Select(l => l.Split(new[] { '\t' }, 4)).Where(f => f.Length >= 3).ToList();
                    foreach (var t in rows.GroupBy(f => f[0]))
                    {
                        var back = t.Where(f => f[2] == "1" && f.Length == 4).ToDictionary(f => f[1], f => f[3], StringComparer.Ordinal);
                        var gone = t.Where(f => f[2] == "0").Select(f => f[1]).ToList();
                        error = MelonDsToml.Write(layout.ConfigFile, t.Key, back, force: false) ?? MelonDsToml.Remove(layout.ConfigFile, t.Key, gone);
                        if (error != null) break;
                    }
                }
                if (error != null) { Log.Warn("game settings: melonDS's own could not go back yet - " + error); return; }
                File.Delete(note);
                if (File.Exists(original)) File.Delete(original);
                Log.Info("game settings: melonDS's own are back (" + why + ")");
            }
            catch (Exception ex) { Log.Warn("game settings: could not put melonDS's own back", ex); }
        }

        private static string OriginalPath(string installDir) => installDir == null ? null : Path.Combine(installDir, "lbip-settings.original.toml");

        /// <summary>Is a session's note still there - its settings not put back yet?</summary>
        internal static bool Pending(MelonDsLayout layout)
        {
            var note = RestorePath(layout?.InstallDir);
            return note != null && File.Exists(note);
        }

        /// <summary>Did melonDS write an empty document over its file: the original had tables, what it left has none
        /// (or is gone)?</summary>
        internal static bool Wiped(string original, string now)
        {
            int Tables(string t) => (t ?? "").Replace("\r\n", "\n").Split('\n').Count(l => l.TrimStart().StartsWith("[", StringComparison.Ordinal));
            return Tables(original) > 0 && (now == null || Tables(now) == 0);
        }

        /// <summary>Wait for the melonDS of this launch to come and go, then put its settings back.</summary>
        public static void RestoreWhenDone(MelonDsLayout layout)
        {
            var session = LastSession;
            var hold = LbIntegrations.Lbip.LbipLaunchGate.HoldOpen();
            System.Threading.Tasks.Task.Run(() =>
            {
                using var held = hold;
                try
                {
                    var armed = DateTime.UtcNow;
                    bool appeared = false;
                    while ((DateTime.UtcNow - armed).TotalSeconds < 120)
                    {
                        if (DsiNand.EmulatorRunning()) { appeared = true; break; }
                        System.Threading.Thread.Sleep(500);
                    }
                    // Whether it came or not, never put back while one runs: one that started just after the last look
                    // would otherwise find the end skipped (the adversarial review of Flycast's, 30/09).
                    while (DsiNand.EmulatorRunning()) { appeared = true; System.Threading.Thread.Sleep(500); }
                    // melonDS writes its file as it quits: a moment for that to land.
                    System.Threading.Thread.Sleep(1000);
                    while (DsiNand.EmulatorRunning()) { appeared = true; System.Threading.Thread.Sleep(500); }
                    Restore(layout, appeared ? "the session is over" : "melonDS never started", session);
                }
                catch (Exception ex) { Log.Warn("game settings: watching for the end of the session", ex); }
            });
        }

        private static string WriteValues(string configFile, Dictionary<string, string> values, bool force)
        {
            foreach (var group in Settings.Where(s => values.ContainsKey(s.Id)).GroupBy(s => s.Table))
            {
                var wanted = group.ToDictionary(s => s.Key, s => s.Kind == Kind.Text ? MelonDsToml.Text(values[s.Id]) : values[s.Id], StringComparer.Ordinal);
                var error = MelonDsToml.Write(configFile, group.Key, wanted, force);
                if (error != null) return error;
            }
            return null;
        }

        /// <summary>Id=value;... in the order of Settings. A value is escaped (%XX), so a message may say
        /// anything: the numbers and true/false of the first version read back unchanged.</summary>
        internal static string Format(Dictionary<string, string> values)
            => values == null ? "" : string.Join(";", Settings.Where(s => values.ContainsKey(s.Id)).Select(s => s.Id + "=" + Uri.EscapeDataString(values[s.Id] ?? "")));

        private static Dictionary<string, string> Parse(string text)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var part in (text ?? "").Trim().Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                var s = Settings.FirstOrDefault(x => x.Id == part.Substring(0, eq).Trim());
                string raw;
                try { raw = Uri.UnescapeDataString(part.Substring(eq + 1)); } catch { continue; }
                var v = s == null ? null : Normal(s, raw);
                if (v != null) values[s.Id] = v;
            }
            return values;
        }
    }
}
