// A game's OWN settings for Flycast (Mehdi, 29/09), given to Flycast on its COMMAND LINE for one session.
//
// FLYCAST HAS PER-GAME CONFIGS OF ITS OWN - a [<GAME ID>] section of emu.cfg, "Make Game Config" in its
// settings - but emu.cfg is rewritten WHOLE from memory, sorted and uncommented, at every save and at exit
// (core/cfg/cfg.cpp, the window's size saved by sdl/sdl.cpp), and an arcade game's id is its cartridge
// header's title (hw/naomi/naomi_cart.cpp). So no file is written here. Instead:
//
//   -config section:key=value,...   (core/cfg/cl.cpp) - a TRANSIENT value: read like emu.cfg's, never
//                                   written back to it (IniFile::save skips it, Option::save refuses an
//                                   overridden option). Nothing to set aside, nothing to put back, nothing a
//                                   crash can leave behind.
//
// Put IN FRONT of the line: Flycast reads every -config in turn, the last one wins, so a key the user wrote
// on the game's own line keeps winning over this plugin's.
//
// OVER THE GAME'S OWN CONFIG TOO (Mehdi, 29/09: this plugin's > the game's own config > the emulator's). A game
// config of the user's - [<GAME ID>] in emu.cfg - is read after the global values (Option::load in per-game
// mode), so a global -config loses to it. But a transient value wins over the file's for the SAME entry
// (cfg/ini.cpp, IniFile::get: transient first): a key the game's section sets is given in that section too,
// -config <GAME ID>:config.rend.Resolution=1920. Only there - naming the section otherwise would turn Flycast's
// per-game mode on for a game that has none - and only for an id the command line can carry (cl.cpp drops the
// spaces of a section name: an arcade board's title cannot be named). Flycast's own fixes for known games
// (loadSpecialSettings) still win over a global value.
//
// THE SAME WAY AS THE OTHER PLUGINS: a game's values live in <install>\lbip-settings.tsv (LaunchBox game id
// -> "section:key=value;..."), what is set by hand (the Advanced tab) in <install>\lbip-settings-advanced.tsv.
// A setting that is the Dreamcast's only (cable, language...) is not given to an arcade game, and back.
//
// NOT [achievements]: the RetroAchievements login is this plugin's, written into emu.cfg at launch.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Flycast
{
    internal static class FlycastGameSettings
    {
        internal enum Kind { Bool, Choice, Int }

        /// <summary>Which games a setting means something for.</summary>
        [Flags] internal enum Games { Dreamcast = 1, Arcade = 2, All = 3 }

        internal sealed class Setting
        {
            public string Section = "config", Key, Label, Tab;
            public Kind Kind;
            public (string Value, string Text)[] Choices;
            public int Min, Max;
            public string Builtin;          // Flycast's own default (core/cfg/option.cpp), for a key emu.cfg lacks
            public Games For = Games.All;
            public string Id => Section + ":" + Key;
        }

        public const string VideoTab = "Video", RenderingTab = "Rendering", SystemTab = "System";

        private static (string, string)[] Range(int from, params string[] texts)
            => texts.Select((t, i) => ((from + i).ToString(CultureInfo.InvariantCulture), t)).ToArray();

        private static Setting B(string tab, string key, string label, bool builtin, Games games = Games.All)
            => new Setting { Tab = tab, Key = key, Label = label, Kind = Kind.Bool, Builtin = builtin ? "yes" : "no", For = games };

        private static Setting C(string tab, string key, string label, string builtin, (string, string)[] choices, Games games = Games.All)
            => new Setting { Tab = tab, Key = key, Label = label, Kind = Kind.Choice, Builtin = builtin, Choices = choices, For = games };

        private static Setting I(string tab, string key, string label, int builtin, int min, int max, Games games = Games.All)
            => new Setting { Tab = tab, Key = key, Label = label, Kind = Kind.Int, Builtin = builtin.ToString(CultureInfo.InvariantCulture), Min = min, Max = max, For = games };

        private static (string, string)[] Resolutions()
        {
            var scale = new[] { 0.5, 1, 1.5, 2, 2.5, 3, 4, 4.5, 5, 6, 7, 8, 9 };
            var names = new[] { "Half", "Native", "x1.5", "x2", "x2.5", "x3", "x4", "x4.5", "x5", "x6", "x7", "x8", "x9" };
            return scale.Select((s, i) => (((int)(s * 480)).ToString(CultureInfo.InvariantCulture),
                                           (int)(s * 640) + "x" + (int)(s * 480) + " (" + names[i] + ")")).ToArray();
        }

        /// <summary>What the options window offers - Flycast's own words, keys and defaults (ui/settings_video.cpp,
        /// ui/settings_general.cpp, ui/settings.cpp, core/cfg/option.cpp), in the order of its settings.</summary>
        internal static readonly Setting[] Offered =
        {
            C(VideoTab, "pvr.rend", "Renderer", "2", new[]
            {
                ("0", "OpenGL"), ("3", "OpenGL - per-pixel sorting"), ("4", "Vulkan"), ("5", "Vulkan - per-pixel sorting"),
                ("1", "DirectX 9"), ("2", "DirectX 11"), ("6", "DirectX 11 - per-pixel sorting"),
            }),
            C(VideoTab, "rend.Resolution", "Internal Resolution", "480", Resolutions()),
            C(VideoTab, "rend.AnisotropicFiltering", "Anisotropic Filtering", "1", new[] { ("1", "Off"), ("2", "2x"), ("4", "4x"), ("8", "8x"), ("16", "16x") }),
            C(VideoTab, "rend.TextureFiltering", "Texture Filtering", "0", Range(0, "Default", "Force Nearest-Neighbor", "Force Linear")),
            I(VideoTab, "rend.ScreenStretching", "Horizontal Stretching - %", 100, 100, 250),
            B(VideoTab, "rend.PerStripSorting", "Per-strip sorting (when not per-pixel)", false),
            B(VideoTab, "rend.WideScreen", "Widescreen", false),
            B(VideoTab, "rend.SuperWideScreen", "Super Widescreen", false),
            B(VideoTab, "rend.WidescreenGameHacks", "Widescreen Game Cheats", false),
            B(VideoTab, "rend.IntegerScale", "Integer Scaling", false),
            B(VideoTab, "rend.LinearInterpolation", "Linear Interpolation", true),
            B(VideoTab, "rend.vsync", "VSync", true),
            B(VideoTab, "rend.FramePacing", "Frame Pacing", true),
            B(VideoTab, "rend.ThreadedRendering", "Threaded Rendering", true),
            B(VideoTab, "rend.ShowFPS", "Show FPS Counter", false),

            C(RenderingTab, "pvr.AutoSkipFrame", "Automatic Frame Skipping", "0", Range(0, "Disabled", "Normal", "Maximum")),
            C(RenderingTab, "ta.skip", "Frame Skipping", "0", Range(0, "0", "1", "2", "3", "4", "5", "6")),
            I(RenderingTab, "rend.PerPixelLayers", "Maximum Layers (per-pixel sorting)", 32, 8, 128),
            B(RenderingTab, "rend.RenderToTextureBuffer", "Copy Rendered Textures to VRAM", false),
            B(RenderingTab, "rend.EmulateFramebuffer", "Full Framebuffer Emulation", false),
            B(RenderingTab, "rend.TranslucentPolygonDepthMask", "Translucent Polygon Depth Mask", false),
            B(RenderingTab, "rend.ModifierVolumes", "Shadows", true),
            B(RenderingTab, "rend.Fog", "Fog", true),
            B(RenderingTab, "rend.NativeDepthInterpolation", "Native Depth Interpolation", false),
            B(RenderingTab, "rend.FixUpscaleBleedingEdge", "Fix Upscale Bleeding Edge", true),
            B(RenderingTab, "rend.DelayFrameSwapping", "Delay Frame Swapping", true),
            B(RenderingTab, "rend.DupeFrames", "Duplicate frames", false),
            B(RenderingTab, "rend.UseMipmaps", "Mipmapping", true),

            C(SystemTab, "Dreamcast.Region", "Region", "1", Range(0, "Japan", "USA", "Europe (arcade: Export)", "Default (arcade: Korea)")),
            C(SystemTab, "Dreamcast.Language", "Dreamcast Language", "1", Range(0, "Japanese", "English", "German", "French", "Spanish", "Italian", "Default"), Games.Dreamcast),
            C(SystemTab, "Dreamcast.Broadcast", "Broadcast", "0", Range(0, "NTSC", "PAL", "PAL/M", "PAL/N", "Default"), Games.Dreamcast),
            C(SystemTab, "Dreamcast.Cable", "Cable", "3", new[] { ("0", "VGA"), ("2", "RGB Component"), ("3", "TV Composite") }, Games.Dreamcast),
            I(SystemTab, "Sh4Clock", "SH4 Clock - MHz", 200, 100, 300),
            I(SystemTab, "aica.Volume", "Volume Level - %", 100, 0, 100),
            B(SystemTab, "aica.DSPEnabled", "Enable DSP", false),
            B(SystemTab, "UseReios", "HLE BIOS", false, Games.Dreamcast),
            B(SystemTab, "FastGDRomLoad", "Fast GD-ROM Loading", false, Games.Dreamcast),
            B(SystemTab, "Dreamcast.RamMod32MB", "Dreamcast 32MB RAM Mod", false, Games.Dreamcast),
            B(SystemTab, "ForceFreePlay", "Naomi Free Play", true, Games.Arcade),
        };

        /// <summary>Sections this plugin keeps elsewhere - never taken from a game's settings.</summary>
        internal static readonly string[] Managed = { "achievements" };

        internal static bool IsManaged(string section) => Managed.Any(m => string.Equals(m, section, StringComparison.OrdinalIgnoreCase));

        internal static Setting Find(string section, string key)
            => Offered.FirstOrDefault(s => s.Section.Equals(section, StringComparison.OrdinalIgnoreCase) && s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        internal static bool IsYes(string v)
            => v != null && new[] { "yes", "true", "on", "1" }.Any(t => t.Equals(v.Trim(), StringComparison.OrdinalIgnoreCase));

        private const string StoreName = "lbip-settings.tsv", AdvancedName = "lbip-settings-advanced.tsv";

        private static string InstallPath(FlycastLayout layout, string name)
            => string.IsNullOrEmpty(layout?.InstallDir) ? null : Path.Combine(layout.InstallDir, name);

        // ── a game's own ─────────────────────────────────────────────────────

        public static Dictionary<string, string> Load(FlycastLayout layout, string gameId)
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

        public static void Save(FlycastLayout layout, string gameId, Dictionary<string, string> values)
            => WriteLine(InstallPath(layout, StoreName), gameId, values == null || values.Count == 0 ? null
                : string.Join(";", values.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value))));

        public static string LoadAdvanced(FlycastLayout layout, string gameId, out bool on)
        {
            var f = ReadLine(InstallPath(layout, AdvancedName), gameId);
            on = f != null && f.Length >= 3 && f[1] == "on";
            try { return f != null && f.Length >= 3 ? Uri.UnescapeDataString(f[2]) : null; } catch { return null; }
        }

        public static void SaveAdvanced(FlycastLayout layout, string gameId, string text, bool on)
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
                FlycastIni.WriteAtomicBytes(path, new UTF8Encoding(false).GetBytes(string.Join("\r\n", lines) + (lines.Count > 0 ? "\r\n" : "")));
                Log.Info("game settings of " + gameId + " (" + Path.GetFileName(path) + "): " + (rest == null ? "none" : "saved"));
            }
            catch (Exception ex) { Log.Warn("game settings: could not write " + Path.GetFileName(path), ex); }
        }

        // ── what the game runs on without ours ───────────────────────────────

        /// <summary>section:key -> value the game runs on WITHOUT settings of its own here - "default" in the
        /// window: its own Flycast game config ([<paramref name="product"/>] of emu.cfg), else emu.cfg's value,
        /// else Flycast's built-in one. In <paramref name="gameConfig"/> the keys the game's own config sets:
        /// they win over this plugin's too.</summary>
        public static Dictionary<string, string> DefaultsOf(FlycastLayout layout, string product, out HashSet<string> gameConfig)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            gameConfig = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cfg = layout?.ConfigFile;
            foreach (var group in Offered.GroupBy(s => s.Section))
            {
                var held = string.IsNullOrEmpty(cfg) ? new Dictionary<string, string>() : FlycastIni.Read(cfg, group.Key, group.Select(s => s.Key).ToArray());
                foreach (var s in group) values[s.Id] = held.TryGetValue(s.Key, out var v) ? v : s.Builtin;
            }
            if (!string.IsNullOrWhiteSpace(product) && !string.IsNullOrEmpty(cfg))
            {
                var mine = FlycastIni.Read(cfg, product, Offered.Select(s => s.Section + "." + s.Key).ToArray());
                foreach (var s in Offered)
                    if (mine.TryGetValue(s.Section + "." + s.Key, out var v)) { values[s.Id] = v; gameConfig.Add(s.Id); }
            }
            return values;
        }

        // ── set by hand ──────────────────────────────────────────────────────

        internal sealed class Raw { public string Section, Key, Value; public string Id => Section + ":" + Key; }

        /// <summary>[section] headers and "key = value" lines, as emu.cfg writes them; ";" or "#" comments.
        /// Null, with the line, when a line is neither.</summary>
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
                    if (close < 2) { error = "line " + (i + 1) + ": a section header is [name]"; return null; }
                    section = line.Substring(1, close - 1).Trim();
                    if (section.IndexOfAny(new[] { ' ', ':', ',', '=', '"', '\'' }) >= 0)
                    { error = "line " + (i + 1) + ": [" + section + "] cannot be given on Flycast's command line (a space, : , = or a quote)"; return null; }
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) { error = "line " + (i + 1) + ": expected key = value"; return null; }
                if (section == null) { error = "line " + (i + 1) + ": a key before any [section]"; return null; }
                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim();
                if (key.IndexOfAny(new[] { ' ', ':', ',', '"', '\'' }) >= 0)
                { error = "line " + (i + 1) + ": " + key + " cannot be given on Flycast's command line (a space, : , or a quote)"; return null; }
                if (value.IndexOfAny(new[] { ',', '"', '\'' }) >= 0)
                { error = "line " + (i + 1) + ": the value of " + key + " cannot be given on Flycast's command line (a comma or a quote)"; return null; }
                if (list.Any(r => r.Section.Equals(section, StringComparison.OrdinalIgnoreCase) && r.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
                { error = "line " + (i + 1) + ": " + key + " is set twice in [" + section + "]"; return null; }
                list.Add(new Raw { Section = section, Key = key, Value = value });
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

        /// <summary>A set of the tabs' values as keys.</summary>
        public static List<Raw> ToRaw(Dictionary<string, string> values)
            => (values ?? new Dictionary<string, string>())
               .Select(kv => { int at = kv.Key.IndexOf(':'); return new Raw { Section = kv.Key.Substring(0, at), Key = kv.Key.Substring(at + 1), Value = kv.Value }; })
               .ToList();

        public static List<string> CheckHand(FlycastLayout layout, string text, out string error)
        {
            var warnings = new List<string>();
            var keys = ParseHand(text, out error);
            if (keys == null) return warnings;
            if (keys.Count == 0) warnings.Add("nothing is set: the game runs on Flycast's settings");
            foreach (var k in keys)
            {
                if (IsManaged(k.Section)) { warnings.Add("[" + k.Section + "] " + k.Key + ": kept elsewhere by this plugin - left out"); continue; }
                var known = Find(k.Section, k.Key);
                if (known != null)
                {
                    if (known.Kind == Kind.Bool && !new[] { "yes", "no", "true", "false", "on", "off", "1", "0" }.Contains(k.Value.ToLowerInvariant()))
                        warnings.Add("[" + k.Section + "] " + k.Key + " = " + k.Value + ": Flycast writes yes or no");
                    else if (known.Kind == Kind.Choice && known.Choices.All(c => c.Value != k.Value))
                        warnings.Add("[" + k.Section + "] " + k.Key + " = " + k.Value + ": not one of " + string.Join(", ", known.Choices.Select(c => c.Value)));
                    else if (known.Kind == Kind.Int && !(int.TryParse(k.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= known.Min && n <= known.Max))
                        warnings.Add("[" + k.Section + "] " + k.Key + " = " + k.Value + ": Flycast offers " + known.Min + " to " + known.Max);
                    continue;
                }
                var held = string.IsNullOrEmpty(layout?.ConfigFile) ? new Dictionary<string, string>() : FlycastIni.Read(layout.ConfigFile, k.Section, k.Key);
                if (!held.ContainsKey(k.Key))
                    warnings.Add("[" + k.Section + "] " + k.Key + ": not in emu.cfg - Flycast writes every key it knows there, so check its name");
            }
            return warnings;
        }

        // ── the session ──────────────────────────────────────────────────────

        /// <summary>The keys a launch of this game gives Flycast: what is set by hand when that is in use, else
        /// the tabs' - less what is kept elsewhere and, from the tabs, what is not for this kind of game.</summary>
        internal static List<Raw> KeysOf(FlycastLayout layout, string gameId, Games games, out string why)
        {
            why = null;
            var hand = LoadAdvanced(layout, gameId, out var on);
            List<Raw> keys;
            if (on && hand != null)
            {
                keys = ParseHand(hand, out var error);
                if (keys == null) { why = "the settings set by hand cannot be read (" + error + ")"; return null; }
            }
            else
            {
                keys = ToRaw(Load(layout, gameId));
                var other = keys.Where(k => Find(k.Section, k.Key) is Setting s && (s.For & games) == 0).ToList();
                if (other.Count > 0) Log.Info("game settings: not for this game (" + games + ") - " + string.Join(", ", other.Select(k => k.Id)));
                keys = keys.Except(other).ToList();
            }
            var left = keys.Where(k => IsManaged(k.Section)).ToList();
            if (left.Count > 0) Log.Info("game settings: left out, this plugin keeps them elsewhere - " + string.Join(", ", left.Select(k => k.Id)));
            keys = keys.Where(k => !IsManaged(k.Section)).ToList();
            return keys.Count > 0 ? keys : null;
        }

        /// <summary>Those of the keys the game's own config ([<paramref name="product"/>] in emu.cfg) sets too.</summary>
        public static List<Raw> SetByGame(FlycastLayout layout, string product, List<Raw> keys)
        {
            if (keys == null || string.IsNullOrWhiteSpace(product) || string.IsNullOrEmpty(layout?.ConfigFile)) return new List<Raw>();
            var held = FlycastIni.Read(layout.ConfigFile, product, keys.Select(k => k.Section + "." + k.Key).ToArray());
            return keys.Where(k => held.ContainsKey(k.Section + "." + k.Key)).ToList();
        }

        /// <summary>The -config argument of a set of keys, quoted when a value holds a space - GLOBAL values only, never in a
        /// game's section: Flycast would save those as the user's (30/09, see FlycastGameConfigSession). Null for none.</summary>
        public static string Argument(List<Raw> keys)
        {
            if (keys == null || keys.Count == 0) return null;
            var items = keys.Select(k => k.Section + ":" + k.Key + "=" + k.Value).ToList();
            var list = string.Join(",", items);
            return "-config " + (list.IndexOf(' ') >= 0 ? "\"" + list + "\"" : list);
        }

        /// <summary>The line Flycast is started with, this game's -config in front - a -config the line already
        /// has comes after, and so wins. Null when it stays as it is.</summary>
        public static string WithSettings(string line, List<Raw> keys)
        {
            var arg = Argument(keys);
            if (arg == null) return null;
            return arg + (string.IsNullOrWhiteSpace(line) ? "" : " " + line.Trim());
        }

        /// <summary>For the Preview window: the line as it is and as a launch makes it, and each key with what
        /// Flycast has without it. Nothing is written.</summary>
        public static string Preview(FlycastLayout layout, string product, string line, List<Raw> keys, Games games, out string before, out string error)
        {
            before = ""; error = null;
            var used = (keys ?? new List<Raw>()).Where(k => !IsManaged(k.Section) && !(Find(k.Section, k.Key) is Setting s && (s.For & games) == 0)).ToList();
            if (used.Count == 0) { error = "nothing is set: the game runs on Flycast's settings"; return null; }
            var defaults = DefaultsOf(layout, product, out var wins);
            var now = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in used)
            {
                if (defaults.TryGetValue(k.Id, out var v)) { now[k.Id] = v; continue; }
                var held = string.IsNullOrEmpty(layout?.ConfigFile) ? new Dictionary<string, string>() : FlycastIni.Read(layout.ConfigFile, k.Section, k.Key);
                now[k.Id] = held.TryGetValue(k.Key, out var h) ? h : "(not set)";
            }
            var overGame = SetByGame(layout, product, used);
            string Note(Raw k) => !overGame.Any(o => o.Id == k.Id) ? "" : "      <- taken out of its own game config for the session";
            string Table(Func<Raw, string> value) => string.Join("\r\n", used.Select(k => "[" + k.Section + "] " + k.Key + " = " + value(k) + Note(k)));
            before = "Command line:\r\n" + (line ?? "").Trim() + "\r\n\r\nWhat Flycast uses:\r\n" + Table(k => now[k.Id]) + "\r\n";
            return "Command line:\r\n" + WithSettings(line, used) + "\r\n\r\nWhat Flycast uses:\r\n"
                   + Table(k => k.Value) + "\r\n";
        }
    }
}
