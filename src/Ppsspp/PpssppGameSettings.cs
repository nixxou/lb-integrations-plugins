// A game's OWN settings for PPSSPP (Mehdi, 29/09), laid over the game's own config for ONE SESSION.
//
// PPSSPP HAS PER-GAME CONFIGS OF ITS OWN: <memstick>\PSP\SYSTEM\<GAME ID>_ppsspp.ini (Core/Util/PathUtil.cpp,
// the id the PARAM.SFO's DISC_ID - PspDiscId), loaded when the game boots, a command-line launch included
// (Core/PSPLoaders.cpp, LoadGameConfig). A PARTIAL FILE IS VALID: a key it lacks keeps ppsspp.ini's value
// (ReadFromIniSection with applyDefaultIfMissing false) - so only what the game sets here is written.
// ONE EXCEPTION, THE CONTROLS: a game file with no [ControlMapping] does NOT fall back to controls.ini but
// to the built-in mapping (Core/KeyMap.cpp, LoadFromIni -> RestoreDefault) - the user's own and the save /
// load / exit keys this plugin adds (PpssppHotkeys) would be gone. So a file written for a session without
// one gets controls.ini's.
//
// THE SAME WAY AS THE OTHER PLUGINS:
//   - a game's values live in <install>\lbip-settings.tsv (LaunchBox game id -> "Section/Key=value;..."),
//     what is set by hand (the Advanced tab) in <install>\lbip-settings-advanced.tsv;
//   - AT LAUNCH <ID>_ppsspp.ini is set aside FIRST as <ID>_ppsspp.ini.lbip-bak - a copy, or an EMPTY file
//     when there was none - then the game's keys are written over it (and nothing else);
//   - WHEN PPSSPP HAS QUIT the .lbip-bak goes back - over the file, or, empty, the file is deleted. A change
//     made in PPSSPP's own game settings DURING such a session is therefore not kept;
//   - a .lbip-bak still there (the host or the machine went mid-session) is put back first: at every
//     launch, at the plugin's start-up check, and when PPSSPP is opened without a game (LbEmulatorOpened).
//
// THE RENDERER IS NOT A PER-GAME SETTING in PPSSPP ([Graphics] GraphicsBackend) - but --graphics= on its
// command line is, and is not saved (Core/CmdLine.cpp): the game's choice goes there, at launch.
//
// NOT THE KEYS THIS PLUGIN KEEPS ELSEWHERE: [Achievements] (the RetroAchievements login, ppsspp.ini's) and
// [ControlMapping] - set by hand, they are left out and said so.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Ppsspp
{
    internal static class PpssppGameSettings
    {
        internal enum Kind { Bool, Choice, Int }

        internal sealed class Setting
        {
            public string Section, Key, Label, Tab;
            public Kind Kind;
            public (string Value, string Text)[] Choices;
            public int Min, Max;
            public string Builtin;      // PPSSPP's own default (Core/Config.cpp), for a key ppsspp.ini lacks
            public string Id => Section + "/" + Key;
        }

        public const string GraphicsTab = "Graphics", SystemTab = "System", HacksTab = "Hacks";
        public const string CliSection = "cli";
        internal const string BackendId = CliSection + "/graphics";

        private static (string, string)[] Range(int from, params string[] texts)
            => texts.Select((t, i) => ((from + i).ToString(CultureInfo.InvariantCulture), t)).ToArray();

        private static Setting S(string tab, string section, string key, string label, Kind kind, (string, string)[] choices = null, int min = 0, int max = 0)
            => new Setting { Tab = tab, Section = section, Key = key, Label = label, Kind = kind, Choices = choices, Min = min, Max = max };

        /// <summary>What the options window offers - PPSSPP's own words and values (UI/GameSettingsScreen.cpp,
        /// Core/Config.cpp), in the order of its settings screen.</summary>
        internal static readonly Setting[] Offered =
        {
            S(GraphicsTab, CliSection, "graphics", "Backend", Kind.Choice,
              new[] { ("vulkan", "Vulkan"), ("d3d11", "Direct3D 11"), ("opengl", "OpenGL") }),
            S(GraphicsTab, "Graphics", "SoftwareRenderer", "Software Rendering (slow)", Kind.Bool),
            S(GraphicsTab, "Graphics", "InternalResolution", "Rendering Resolution", Kind.Choice,
              Range(0, "Auto (1:1)", "1x PSP", "2x PSP", "3x PSP", "4x PSP", "5x PSP", "6x PSP", "7x PSP", "8x PSP", "9x PSP", "10x PSP")),
            S(GraphicsTab, "Graphics", "FrameSkip", "Frame Skipping", Kind.Choice, Range(0, "Off", "1", "2", "3", "4", "5", "6", "7", "8")),
            S(GraphicsTab, "Graphics", "AutoFrameSkip", "Auto FrameSkip", Kind.Bool),
            S(GraphicsTab, "Graphics", "VerticalSync", "VSync", Kind.Bool),
            S(GraphicsTab, "Graphics", "HardwareTransform", "Hardware Transform", Kind.Bool),
            S(GraphicsTab, "Graphics", "TexScalingType", "CPU texture upscaler (slow)", Kind.Choice, Range(0, "xBRZ", "Hybrid", "Bicubic", "Hybrid + Bicubic")),
            S(GraphicsTab, "Graphics", "TexScalingLevel", "Upscale Level", Kind.Choice, Range(1, "Off", "2x", "3x", "4x", "5x")),
            S(GraphicsTab, "Graphics", "AnisotropyLevel", "Anisotropic Filtering", Kind.Choice, Range(0, "Off", "2x", "4x", "8x", "16x")),
            S(GraphicsTab, "Graphics", "TextureFiltering", "Texture Filter", Kind.Choice, Range(1, "Auto", "Nearest", "Linear", "Auto Max Quality")),

            S(SystemTab, "SystemParam", "GameLanguage", "Game language", Kind.Choice,
              Range(-1, "Auto", "Japanese", "English", "French", "Spanish", "German", "Italian", "Dutch", "Portuguese", "Russian", "Korean", "Chinese (traditional)", "Chinese (simplified)")),
            S(SystemTab, "SystemParam", "ButtonPreference", "Confirmation Button", Kind.Choice, Range(0, "Use O to confirm", "Use X to confirm")),
            S(SystemTab, "SystemParam", "PSPModel", "PSP Model", Kind.Choice, Range(0, "PSP-1000", "PSP-2000/3000")),
            S(SystemTab, "CPU", "FastMemoryAccess", "Fast Memory", Kind.Bool),
            S(SystemTab, "CPU", "IOTimingMethod", "I/O timing method", Kind.Choice,
              Range(0, "Fast (lag on slow storage)", "Host (bugs, less lag)", "Simulate UMD delays", "Simulate UMD slow reading speed")),
            S(SystemTab, "CPU", "CPUSpeed", "Change CPU Clock (unstable) - MHz, 0: default", Kind.Int, min: 0, max: 1000),

            S(HacksTab, "Graphics", "SkipBufferEffects", "Skip Buffer Effects", Kind.Bool),
            S(HacksTab, "Graphics", "SkipGPUReadbackMode", "Skip GPU Readbacks", Kind.Choice, Range(0, "No", "Skip", "Copy to texture")),
            S(HacksTab, "Graphics", "DepthRasterMode", "Lens flare occlusion", Kind.Choice, Range(0, "Auto", "Low", "Off", "Always on")),
            S(HacksTab, "Graphics", "BloomHack", "Lower resolution for effects", Kind.Choice, Range(0, "Off", "Safe", "Balanced", "Aggressive")),
        };

        /// <summary>PPSSPP's own defaults, the Windows x64 build (Core/Config.cpp): what a key ppsspp.ini lacks
        /// is - a PPSSPP never started has no ppsspp.ini at all. The renderer: Vulkan from Windows 10 on
        /// (DefaultGPUBackend); the resolution: Auto (DefaultInternalResolution); lens flares: Auto on SSE2.</summary>
        private static readonly Dictionary<string, string> Builtins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["graphics"] = "vulkan",
            ["SoftwareRenderer"] = "False",
            ["InternalResolution"] = "0",
            ["FrameSkip"] = "0",
            ["AutoFrameSkip"] = "False",
            ["VerticalSync"] = "True",
            ["HardwareTransform"] = "True",
            ["TexScalingType"] = "0",
            ["TexScalingLevel"] = "1",
            ["AnisotropyLevel"] = "4",
            ["TextureFiltering"] = "1",
            ["GameLanguage"] = "-1",
            ["ButtonPreference"] = "1",
            ["PSPModel"] = "1",
            ["FastMemoryAccess"] = "True",
            ["IOTimingMethod"] = "0",
            ["CPUSpeed"] = "0",
            ["SkipBufferEffects"] = "False",
            ["SkipGPUReadbackMode"] = "0",
            ["DepthRasterMode"] = "0",
            ["BloomHack"] = "0",
        };

        static PpssppGameSettings()
        {
            foreach (var s in Offered) s.Builtin = Builtins.TryGetValue(s.Key, out var v) ? v : null;
        }

        /// <summary>Sections this plugin keeps elsewhere - never taken from a game's settings.</summary>
        internal static readonly string[] Managed = { "Achievements", "ControlMapping" };

        internal static bool IsManaged(string section) => Managed.Any(m => string.Equals(m, section, StringComparison.OrdinalIgnoreCase));

        private const string StoreName = "lbip-settings.tsv", AdvancedName = "lbip-settings-advanced.tsv", BakSuffix = ".lbip-bak";

        private static string InstallPath(PpssppLayout layout, string name)
            => layout?.InstallDir == null ? null : Path.Combine(layout.InstallDir, name);

        internal static string GameIni(PpssppLayout layout, string discId)
            => layout?.SystemDir == null || string.IsNullOrWhiteSpace(discId) ? null : Path.Combine(layout.SystemDir, discId + "_ppsspp.ini");

        // ── a game's own ─────────────────────────────────────────────────────

        public static Dictionary<string, string> Load(PpssppLayout layout, string gameId)
        {
            var f = ReadLine(InstallPath(layout, StoreName), gameId);
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

        public static void Save(PpssppLayout layout, string gameId, Dictionary<string, string> values)
            => WriteLine(InstallPath(layout, StoreName), gameId, values == null || values.Count == 0 ? null
                : string.Join(";", values.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value))));

        public static string LoadAdvanced(PpssppLayout layout, string gameId, out bool on)
        {
            var f = ReadLine(InstallPath(layout, AdvancedName), gameId);
            on = f != null && f.Length >= 3 && f[1] == "on";
            try { return f != null && f.Length >= 3 ? Uri.UnescapeDataString(f[2]) : null; } catch { return null; }
        }

        public static void SaveAdvanced(PpssppLayout layout, string gameId, string text, bool on)
            => WriteLine(InstallPath(layout, AdvancedName), gameId, string.IsNullOrWhiteSpace(text) ? null : (on ? "on" : "off") + "\t" + Uri.EscapeDataString(text.Trim()));

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
            catch (Exception ex) { Log.Warn("game settings: could not read " + Path.GetFileName(path), ex); }
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
                PpssppIni.WriteAtomicBytes(path, new UTF8Encoding(false).GetBytes(string.Join("\r\n", lines) + (lines.Count > 0 ? "\r\n" : "")));
                Log.Info("game settings of " + gameId + " (" + Path.GetFileName(path) + "): " + (rest == null ? "none" : "saved"));
            }
            catch (Exception ex) { Log.Warn("game settings: could not write " + Path.GetFileName(path), ex); }
        }

        // ── what the game runs on without ours ───────────────────────────────

        /// <summary>Section/Key -> value the game runs on WITHOUT settings of its own here - "default" in the
        /// window: its own game config (its .lbip-bak mid-session), else ppsspp.ini, else PPSSPP's built-in
        /// default. The renderer: ppsspp.ini's GraphicsBackend, else Vulkan.</summary>
        public static Dictionary<string, string> DefaultsOf(PpssppLayout layout, string discId) => DefaultsOf(layout, discId, out _);

        /// <summary>The same, and in <paramref name="fromGame"/> the settings the game's own config sets.</summary>
        public static Dictionary<string, string> DefaultsOf(PpssppLayout layout, string discId, out HashSet<string> fromGame)
        {
            fromGame = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var game = GameIni(layout, discId);
            string own = null;
            if (game != null) own = File.Exists(game + BakSuffix) ? (new FileInfo(game + BakSuffix).Length == 0 ? null : game + BakSuffix) : (File.Exists(game) ? game : null);
            foreach (var group in Offered.Where(s => s.Section != CliSection).GroupBy(s => s.Section))
            {
                var keys = group.Select(s => s.Key).ToArray();
                var global = layout?.ConfigFile == null ? new Dictionary<string, string>() : PpssppIni.Read(layout.ConfigFile, group.Key, keys);
                var mine = own == null ? new Dictionary<string, string>() : PpssppIni.Read(own, group.Key, keys);
                foreach (var s in group)
                {
                    if (mine.TryGetValue(s.Key, out var v)) { values[s.Id] = v; fromGame.Add(s.Id); }
                    else if (global.TryGetValue(s.Key, out v)) values[s.Id] = v;
                    else if (s.Builtin != null) values[s.Id] = s.Builtin;
                }
            }
            // "3 (VULKAN)", as PPSSPP writes it (GPUBackendTranslator): the number is the GPUBackend.
            var backend = layout?.ConfigFile == null ? null
                : PpssppIni.Read(layout.ConfigFile, "Graphics", "GraphicsBackend").TryGetValue("GraphicsBackend", out var gb) ? gb : null;
            var number = (backend ?? "").Trim().Split(' ')[0];
            values[BackendId] = number == "0" ? "opengl" : number == "2" ? "d3d11" : number == "3" ? "vulkan"
                              : number == "1" ? "Direct3D 9" : Offered.First(o => o.Id == BackendId).Builtin;
            return values;
        }

        // ── set by hand ──────────────────────────────────────────────────────

        internal sealed class Raw { public string Section, Key, Value; }

        /// <summary>[Section] headers and "Key = Value" lines, ";" or "#" comments. Null, with the line, when a
        /// line is neither.</summary>
        public static List<Raw> ParseHand(string text, out string error)
        {
            error = null;
            var list = new List<Raw>();
            string section = null;
            var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[')
                {
                    int close = line.IndexOf(']');
                    if (close < 2) { error = "line " + (i + 1) + ": a section header is [Name]"; return null; }
                    section = line.Substring(1, close - 1).Trim();
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) { error = "line " + (i + 1) + ": expected Key = Value"; return null; }
                if (section == null) { error = "line " + (i + 1) + ": a key before any [Section]"; return null; }
                var key = line.Substring(0, eq).Trim();
                if (list.Any(r => r.Section.Equals(section, StringComparison.OrdinalIgnoreCase) && r.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
                { error = "line " + (i + 1) + ": " + key + " is set twice in [" + section + "]"; return null; }
                list.Add(new Raw { Section = section, Key = key, Value = line.Substring(eq + 1).Trim() });
            }
            return list;
        }

        /// <summary>The fragment of a set of keys - what the Advanced tab shows.</summary>
        public static string Fragment(IEnumerable<Raw> keys)
        {
            var sb = new StringBuilder();
            foreach (var g in keys.GroupBy(k => k.Section, StringComparer.OrdinalIgnoreCase))
            {
                if (sb.Length > 0) sb.Append("\r\n");
                sb.Append("[" + g.Key + "]\r\n");
                foreach (var k in g) sb.Append(k.Key + " = " + k.Value + "\r\n");
            }
            return sb.ToString();
        }

        /// <summary>A set of the tabs' values as ini keys - the command-line one left out.</summary>
        public static List<Raw> ToRaw(Dictionary<string, string> values)
            => (values ?? new Dictionary<string, string>())
               .Where(kv => !kv.Key.StartsWith(CliSection + "/", StringComparison.OrdinalIgnoreCase))
               .Select(kv => { int at = kv.Key.IndexOf('/'); return new Raw { Section = kv.Key.Substring(0, at), Key = kv.Key.Substring(at + 1), Value = kv.Value }; })
               .ToList();

        public static List<string> CheckHand(PpssppLayout layout, string text, out string error)
        {
            var warnings = new List<string>();
            var keys = ParseHand(text, out error);
            if (keys == null) return warnings;
            if (keys.Count == 0) warnings.Add("nothing is set: the game runs on its own config, else PPSSPP's settings");
            foreach (var k in keys)
            {
                if (IsManaged(k.Section)) { warnings.Add("[" + k.Section + "] " + k.Key + ": kept elsewhere by this plugin - left out"); continue; }
                var known = Offered.FirstOrDefault(s => s.Section.Equals(k.Section, StringComparison.OrdinalIgnoreCase) && s.Key.Equals(k.Key, StringComparison.OrdinalIgnoreCase));
                if (known != null)
                {
                    if (known.Kind == Kind.Bool && k.Value != "True" && k.Value != "False")
                        warnings.Add("[" + k.Section + "] " + k.Key + " = " + k.Value + ": PPSSPP writes True or False");
                    else if (known.Kind == Kind.Choice && known.Choices.All(c => c.Value != k.Value))
                        warnings.Add("[" + k.Section + "] " + k.Key + " = " + k.Value + ": not one of " + string.Join(", ", known.Choices.Select(c => c.Value)));
                    continue;
                }
                var held = layout?.ConfigFile == null ? new Dictionary<string, string>() : PpssppIni.Read(layout.ConfigFile, k.Section, k.Key);
                if (!held.ContainsKey(k.Key))
                    warnings.Add("[" + k.Section + "] " + k.Key + ": not in ppsspp.ini - PPSSPP writes every key it knows there, so check its name");
            }
            return warnings;
        }

        // ── the session ──────────────────────────────────────────────────────

        /// <summary>The ini keys a launch of this game writes, and its renderer for --graphics= (null for none).</summary>
        internal static List<Raw> KeysOf(PpssppLayout layout, string gameId, out string backend, out string why)
        {
            backend = null; why = null;
            var own = Load(layout, gameId);
            if (own != null && own.TryGetValue(BackendId, out var b)) backend = b;
            var hand = LoadAdvanced(layout, gameId, out var on);
            List<Raw> keys;
            if (on && hand != null)
            {
                keys = ParseHand(hand, out var error);
                if (keys == null) { why = "the settings set by hand cannot be read (" + error + ")"; return null; }
            }
            else keys = ToRaw(own);
            var left = keys.Where(k => IsManaged(k.Section)).ToList();
            if (left.Count > 0) Log.Info("game settings: left out, this plugin keeps them elsewhere - " + string.Join(", ", left.Select(k => "[" + k.Section + "] " + k.Key)));
            keys = keys.Where(k => !IsManaged(k.Section)).ToList();
            return keys.Count > 0 ? keys : null;
        }

        /// <summary>The game's config as a session writes it: its own lines (or a fresh file), these keys over
        /// them, and controls.ini's [ControlMapping] when it has none. Writes nothing.</summary>
        internal static List<string> Build(PpssppLayout layout, string discId, string existing, List<Raw> keys)
        {
            var lines = existing == null
                ? new List<string> { "; Game config for " + discId + " - written for one session by Nixx-PPSSPP", "" }
                : existing.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').ToList();
            foreach (var k in keys) Set(lines, k.Section, k.Key, k.Value);
            if (!lines.Any(l => l.Trim().Equals("[ControlMapping]", StringComparison.OrdinalIgnoreCase)))
            {
                var controls = layout?.SystemDir == null ? null : Path.Combine(layout.SystemDir, "controls.ini");
                var mapping = SectionLines(controls, "ControlMapping");
                if (mapping.Count > 0) { lines.Add(""); lines.AddRange(mapping); }
            }
            return lines;
        }

        private static void Set(List<string> lines, string section, string key, string value)
        {
            int start = lines.FindIndex(l => l.Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase));
            if (start < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add("");
                lines.Add("[" + section + "]");
                lines.Add(key + " = " + value);
                return;
            }
            int end = lines.FindIndex(start + 1, l => l.TrimStart().StartsWith("["));
            if (end < 0) end = lines.Count;
            for (int i = start + 1; i < end; i++)
            {
                var t = lines[i].Trim();
                int eq = t.IndexOf('=');
                if (eq > 0 && t.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) { lines[i] = key + " = " + value; return; }
            }
            int at = end;
            while (at > start + 1 && lines[at - 1].Trim().Length == 0) at--;
            lines.Insert(at, key + " = " + value);
        }

        /// <summary>A section of a file, its header included; empty when there is none.</summary>
        private static List<string> SectionLines(string path, string section)
        {
            var result = new List<string>();
            try
            {
                if (path == null || !File.Exists(path)) return result;
                bool inside = false;
                foreach (var line in File.ReadAllLines(path))
                {
                    var t = line.Trim();
                    if (t.StartsWith("["))
                    {
                        if (inside) break;
                        inside = t.Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase);
                    }
                    if (inside) result.Add(line);
                }
                while (result.Count > 0 && result[result.Count - 1].Trim().Length == 0) result.RemoveAt(result.Count - 1);
            }
            catch (Exception ex) { Log.Warn("game settings: could not read " + Path.GetFileName(path), ex); }
            return result;
        }

        /// <summary>Lay the game's own settings over its config for this session, the file set aside first.
        /// True when there were any. Never throws.</summary>
        public static bool Apply(PpssppLayout layout, string gameId, string discId)
        {
            try
            {
                var keys = KeysOf(layout, gameId, out _, out var why);
                if (why != null) Log.Warn("game settings: " + why + " - this game runs without its own settings this time");
                var ini = GameIni(layout, discId);
                if (keys == null || ini == null) return false;
                var bak = ini + BakSuffix;
                if (File.Exists(bak)) { Log.Warn("game settings: the config of a previous session could not be put back - this game runs without its own settings this time"); return false; }
                string existing = File.Exists(ini) ? File.ReadAllText(ini) : null;
                // 1. SET ASIDE FIRST: before a byte of the game's config changes.
                Directory.CreateDirectory(Path.GetDirectoryName(ini));
                if (existing != null) File.Copy(ini, bak, overwrite: false); else File.WriteAllBytes(bak, new byte[0]);
                // 2. The file for the session.
                PpssppIni.WriteAtomicBytes(ini, new UTF8Encoding(false).GetBytes(string.Join("\r\n", Build(layout, discId, existing, keys)) + "\r\n"));
                Log.Info("game settings: this game's own for its session (" + Path.GetFileName(ini) + ") - "
                         + string.Join(", ", keys.Select(k => "[" + k.Section + "] " + k.Key + " = " + k.Value))
                         + (existing != null ? " - its own config is set aside and comes back when PPSSPP quits" : ""));
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("game settings: could not apply them", ex);
                Restore(layout, "the write failed");
                return false;
            }
        }

        /// <summary>Put back every set-aside game config of this install. Never while PPSSPP runs.</summary>
        public static void Restore(PpssppLayout layout, string why)
        {
            try
            {
                if (layout?.SystemDir == null || !Directory.Exists(layout.SystemDir)) return;
                var baks = Directory.GetFiles(layout.SystemDir, "*_ppsspp.ini" + BakSuffix);
                if (baks.Length == 0) return;
                if (PpssppIni.RunningEmulatorProcess() != null) { Log.Info("game settings: PPSSPP is running - the game configs go back once it has quit"); return; }
                foreach (var bak in baks)
                {
                    var ini = bak.Substring(0, bak.Length - BakSuffix.Length);
                    try
                    {
                        if (new FileInfo(bak).Length == 0) { if (File.Exists(ini)) File.Delete(ini); File.Delete(bak); Log.Info(Path.GetFileName(ini) + ": the session's is removed - the game had no config of its own (" + why + ")"); }
                        else { File.Move(bak, ini, overwrite: true); Log.Info(Path.GetFileName(ini) + ": the game's own config is back (" + why + ")"); }
                    }
                    catch (Exception ex) { Log.Warn("could not put " + Path.GetFileName(ini) + " back", ex); }
                }
            }
            catch (Exception ex) { Log.Warn("game settings: could not put the game configs back", ex); }
        }

        /// <summary>Wait for the PPSSPP of this launch to come and go, then put the game's config back.</summary>
        public static void RestoreWhenDone(PpssppLayout layout)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var armed = DateTime.UtcNow;
                    bool appeared = false;
                    while ((DateTime.UtcNow - armed).TotalSeconds < 120)
                    {
                        if (PpssppIni.RunningEmulatorProcess() != null) { appeared = true; break; }
                        System.Threading.Thread.Sleep(500);
                    }
                    if (appeared)
                        while (PpssppIni.RunningEmulatorProcess() != null) System.Threading.Thread.Sleep(500);
                    System.Threading.Thread.Sleep(1000);
                    Restore(layout, appeared ? "the session is over" : "PPSSPP never started");
                }
                catch (Exception ex) { Log.Warn("game settings: watching for the end of the session", ex); }
            });
        }

        /// <summary>The game's config as a launch would write it, and, in <paramref name="before"/>, as it is
        /// now. Null, with why, when there is nothing to write.</summary>
        public static string Preview(PpssppLayout layout, string discId, List<Raw> keys, out string before, out string error)
        {
            before = ""; error = null;
            try
            {
                var ini = GameIni(layout, discId);
                if (ini == null) { error = "this game has no game id PPSSPP names its config by (a homebrew, say)"; return null; }
                var used = (keys ?? new List<Raw>()).Where(k => !IsManaged(k.Section)).ToList();
                if (used.Count == 0) { error = "nothing is set: the game runs on its own config, else PPSSPP's settings"; return null; }
                var bak = ini + BakSuffix;
                string existing = File.Exists(bak) ? (new FileInfo(bak).Length == 0 ? null : File.ReadAllText(bak)) : (File.Exists(ini) ? File.ReadAllText(ini) : null);
                before = existing ?? "";
                return string.Join("\r\n", Build(layout, discId, existing, used)) + "\r\n";
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        /// <summary>The line PPSSPP is started with, --graphics= in front when the game has a renderer of its
        /// own and the line does not already say one. Null when it stays as it is.</summary>
        public static string WithBackend(string line, string backend)
        {
            if (string.IsNullOrWhiteSpace(backend)) return null;
            if ((line ?? "").IndexOf("--graphics", StringComparison.OrdinalIgnoreCase) >= 0)
            { Log.Info("the command line names a renderer already - this game's (" + backend + ") is not added"); return null; }
            return "--graphics=" + backend + (string.IsNullOrWhiteSpace(line) ? "" : " " + line);
        }
    }
}
