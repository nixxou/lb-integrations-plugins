// This plugin's settings: every game's in one key=value per line in
//
//     <plugin root>\.data\<PluginId>\settings.ini
//
// and a game's own beside it in games\<game id>.ini, the same keys - a key absent there follows every game's. The same
// files and rules as the bsnes plugin's (BsnesSettings). Read at the moment a setting is used, never cached: the Nixx
// window and a game's window write the files, the next launch sees them.
//
// THE OPTIONS are a table (All): each a choice among a few values, written into fceux.cfg before the launch by FceuxLaunch
// as the lines of Writes - the Qt build stores even its command-line options (measured 18/10: --pal 1 left SDL.PAL = 1),
// so nothing goes on the line. The screen is the plugin's own (OnPage, the Nixx page sets it for every game): written at
// every launch and kept. Every other option is "As set in FCEUX" until a game's window chooses one (Mehdi, 06/10: the
// general settings are the emulator's own): then its lines are written for that session ONLY, FCEUX's values noted
// beside fceux.cfg and put back once FCEUX has quit (FceuxLaunch.Session) - so a game's choice never leaks into the next
// game, nor into FCEUX's own menus.
//
// THE NAMES AND VALUES read in drivers\Qt\config.cpp InitConfig at the fceux-2.7.0 tag (each line's compiled default in
// Defaults): SDL.PAL 0 NTSC / 1 PAL / 2 Dendy with SDL.AutoDetectPAL 1 (then FCEUX picks NTSC / PAL from the game,
// fceuWrapper.cpp LoadGame, and writes it back into SDL.PAL); SDL.NewPPU; SDL.DisableSpriteLimit; SDL.ClipSides;
// SDL.VideoDriver 0 OpenGL / 1 SDL / 2 QPainter / 3 Vulkan (ConsoleViewerInterface).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Fceux
{
    internal sealed class FceuxOption
    {
        public string Key, Label, Hint, Group;
        public string[] Values, Texts;    // the stored values, and what the lists show for them
        public string Default;
        public bool OnPage;               // set for every game on the Nixx page, kept in fceux.cfg; the others a game's alone
        public Dictionary<string, (string Name, string Value)[]> Writes = new Dictionary<string, (string, string)[]>();

        public string TextOf(string value) { int i = Array.IndexOf(Values, value); return i < 0 ? value : Texts[i]; }
    }

    internal static class FceuxSettings
    {
        /// <summary>manifest.json's PluginId.</summary>
        public const string PluginId = "f805dff2-11aa-4a4c-8117-9deb2698c755";

        /// <summary>For the probe: a settings file somewhere else than beside the install.</summary>
        internal static string PathOverride;

        /// <summary>The value of an option that leaves FCEUX's own: nothing written.</summary>
        public const string Fceux = "fceux";

        public static readonly string[] Groups = { "Picture", "Emulation" };

        /// <summary>FCEUX 2.7.0's compiled default of each line an option writes (config.cpp): what is put back when fceux.cfg
        /// had no such line.</summary>
        public static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SDL.PAL"] = "0", ["SDL.AutoDetectPAL"] = "1", ["SDL.NewPPU"] = "0", ["SDL.DisableSpriteLimit"] = "0",
            ["SDL.ClipSides"] = "0", ["SDL.VideoDriver"] = "0",
        };

        public static readonly FceuxOption[] All =
        {
            new FceuxOption { Key = "Fullscreen", Group = "Game", Label = "Screen", Default = "true", OnPage = true,
                Values = new[] { "true", "false" }, Texts = new[] { "Full screen", "Windowed" },
                Writes = { ["true"] = new[] { (FceuxCfg.FullScreen, "1") }, ["false"] = new[] { (FceuxCfg.FullScreen, "0") } },
                Hint = "FCEUX's SDL.Fullscreen, written into fceux.cfg before the launch. Its full screen is a borderless window over the "
                     + "display (the resolution is never changed). F11 switches in the game." },
            Pick("Region", "Game", "Region (timing)", new[] { "auto", "ntsc", "pal", "dendy" },
                 new[] { "Auto - NTSC or PAL from the game", "NTSC (60 Hz)", "PAL (50 Hz)", "Dendy (the Russian clone, 50 Hz)" },
                 new Dictionary<string, (string, string)[]>
                 {
                     ["auto"] = new[] { ("SDL.AutoDetectPAL", "1"), ("SDL.PAL", "0") },
                     ["ntsc"] = new[] { ("SDL.AutoDetectPAL", "0"), ("SDL.PAL", "0") },
                     ["pal"] = new[] { ("SDL.AutoDetectPAL", "0"), ("SDL.PAL", "1") },
                     ["dendy"] = new[] { ("SDL.AutoDetectPAL", "0"), ("SDL.PAL", "2") },
                 },
                 "FCEUX's Video > Region (SDL.PAL and SDL.AutoDetectPAL). Auto takes NTSC or PAL from the game's header or name."),
            Pick("VideoDriver", "Picture", "Video driver", new[] { "0", "3", "1", "2" },
                 new[] { "OpenGL (FCEUX's default)", "Vulkan", "SDL", "QPainter (software)" },
                 Lines("SDL.VideoDriver", "0", "3", "1", "2"),
                 "FCEUX's Video > Driver (SDL.VideoDriver). OpenGL is the one ReShade hooks best."),
            Pick("ClipSides", "Picture", "Clip the sides", new[] { "on", "off" }, new[] { "On - the 8 columns at each edge hidden", "Off" },
                 OnOff("SDL.ClipSides"), "FCEUX's Video > Clip Left and Right Sides: hides the garbage some games draw at the edges."),
            Pick("NewPpu", "Emulation", "PPU", new[] { "on", "off" }, new[] { "New PPU - more accurate, slower", "Old PPU (FCEUX's default)" },
                 OnOff("SDL.NewPPU"), "FCEUX's Emulation > PPU: the new, cycle-closer picture unit some games need."),
            Pick("SpriteLimit", "Emulation", "Sprite limit", new[] { "on", "off" },
                 new[] { "8 per line - the console's limit (FCEUX's default)", "No limit - less flicker" },
                 new Dictionary<string, (string, string)[]> { ["on"] = new[] { ("SDL.DisableSpriteLimit", "0") }, ["off"] = new[] { ("SDL.DisableSpriteLimit", "1") } },
                 "FCEUX's Video > Disable Sprite Limit. Off removes the flicker of busy scenes; a few games rely on the limit."),
        };

        private static FceuxOption Pick(string key, string group, string label, string[] values, string[] texts, Dictionary<string, (string, string)[]> writes, string hint)
            => new FceuxOption { Key = key, Group = group, Label = label, Default = Fceux, Hint = hint, Writes = writes,
                                 Values = new[] { Fceux }.Concat(values).ToArray(), Texts = new[] { "As set in FCEUX" }.Concat(texts).ToArray() };

        private static Dictionary<string, (string, string)[]> OnOff(string name)
            => new Dictionary<string, (string, string)[]> { ["on"] = new[] { (name, "1") }, ["off"] = new[] { (name, "0") } };

        private static Dictionary<string, (string, string)[]> Lines(string name, params string[] values)
            => values.ToDictionary(v => v, v => new[] { (name, v) });

        public static FceuxOption Option(string key) => All.First(o => o.Key == key);

        /// <summary>The fceux.cfg lines of a game's options: the screen's always, the others when chosen.</summary>
        public static List<(FceuxOption Option, string Name, string Value)> LinesFor(Func<string, string> valueOf)
        {
            var list = new List<(FceuxOption, string, string)>();
            foreach (var o in All)
            {
                var v = valueOf(o.Key);
                if (v == null || v == Fceux || !o.Writes.TryGetValue(v, out var lines)) continue;
                foreach (var (name, value) in lines) list.Add((o, name, value));
            }
            return list;
        }

        public static List<(FceuxOption Option, string Name, string Value)> LinesFor(string gameId) => LinesFor(key => For(gameId, key));

        /// <summary>What fceux.cfg has for an option, as its list says it - null when it has nothing to tell.</summary>
        public static string CfgHas(FceuxOption o, FceuxCfg cfg)
        {
            if (cfg == null) return null;
            // Auto writes the game's region back into SDL.PAL when it loads one: auto is the switch alone.
            if (o.Key == "Region" && (cfg.Get("SDL.AutoDetectPAL") ?? Defaults["SDL.AutoDetectPAL"]) != "0") return "Auto";
            foreach (var v in o.Values)
            {
                if (v == Fceux || !o.Writes.TryGetValue(v, out var lines)) continue;
                if (lines.All(l => (cfg.Get(l.Name) ?? (Defaults.TryGetValue(l.Name, out var d) ? d : null)) == l.Value))
                {
                    var t = o.TextOf(v);
                    if (t.StartsWith("On - ")) return "On";
                    int p = t.IndexOf(" (FCEUX's default", StringComparison.Ordinal);
                    if (p < 0) p = t.IndexOf(" - ", StringComparison.Ordinal);
                    return p > 0 ? t.Substring(0, p) : t;
                }
            }
            return null;
        }

        // ── every game's ──

        /// <summary>Every game's value of <paramref name="key"/>: what the Nixx page set (the options it shows), else the
        /// option's default.</summary>
        public static string Value(string key)
        {
            var o = Option(key);
            if (!o.OnPage) return o.Default;
            var v = Read(SettingsPath).TryGetValue(key, out var s) ? (s ?? "").Trim().ToLowerInvariant() : null;
            return v != null && o.Values.Contains(v) ? v : o.Default;
        }

        public static void SetValue(string key, string value) => Write(SettingsPath, key, value);

        // ── a game's own ──

        public static string GamePath(string gameId)
        {
            var safe = string.Concat((gameId ?? "").Where(c => char.IsLetterOrDigit(c) || c == '-'));
            return safe.Length == 0 ? null : Path.Combine(Path.GetDirectoryName(SettingsPath), "games", safe + ".ini");
        }

        /// <summary>The game's own value of <paramref name="key"/>, or null: it follows every game's.</summary>
        public static string GameValue(string gameId, string key)
        {
            var path = GamePath(gameId);
            if (path == null) return null;
            var v = Read(path).TryGetValue(key, out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim().ToLowerInvariant() : null;
            return v != null && Option(key).Values.Contains(v) ? v : null;
        }

        /// <summary>The game's own value set, or taken away with null.</summary>
        public static void SetGameValue(string gameId, string key, string value)
        {
            var path = GamePath(gameId);
            if (path != null) Write(path, key, value);
        }

        /// <summary>What a game starts with: its own value, else every game's.</summary>
        public static string For(string gameId, string key) => GameValue(gameId, key) ?? Value(key);

        public static bool BoolFor(string gameId, string key) => For(gameId, key) == "true";

        // ── the files ──

        public static string SettingsPath
        {
            get
            {
                if (PathOverride != null) return PathOverride;
                var dll = typeof(FceuxSettings).Assembly.Location;
                var pluginDir = Path.GetDirectoryName(dll);
                var root = Path.GetDirectoryName(pluginDir) ?? pluginDir;
                return Path.Combine(root, ".data", PluginId, "settings.ini");
            }
        }

        private static Dictionary<string, string> Read(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(path)) return values;
                foreach (var line in File.ReadAllLines(path))
                {
                    var at = line.IndexOf('=');
                    if (at <= 0 || line.TrimStart().StartsWith("#")) continue;
                    values[line.Substring(0, at).Trim()] = line.Substring(at + 1).Trim();
                }
            }
            catch (Exception ex) { Log.Warn("settings: could not read " + path, ex); }
            return values;
        }

        /// <summary><paramref name="key"/> set in <paramref name="path"/> - taken out with a null value.</summary>
        private static void Write(string path, string key, string value)
        {
            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
            lines.RemoveAll(l => l.IndexOf('=') > 0 && string.Equals(l.Substring(0, l.IndexOf('=')).Trim(), key, StringComparison.OrdinalIgnoreCase));
            if (value != null) lines.Add(key + "=" + value);
            if (value == null && lines.Count == 0 && !File.Exists(path)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, lines);
        }
    }
}
