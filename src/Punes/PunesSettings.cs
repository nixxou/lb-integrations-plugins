// This plugin's settings: every game's in one key=value per line in
//
//     <plugin root>\.data\<PluginId>\settings.ini
//
// and a game's own beside it in games\<game id>.ini, the same keys - a key absent there follows every game's. The same
// files and rules as ares's (AresSettings). Read at the moment a setting is used, never cached: the Nixx window and a
// game's window write the files, the next launch sees them.
//
// THE OPTIONS are a table (All), each a puNES command-line option with its values, given BEFORE THE ROM at the launch
// (Args) - read in src\gui\cmd_line.cpp and src\gui\settings.h at v0.111: "--long=value" (an argument is split on '=';
// measured 18/10: --fullscreen=yes, --mode=ntsc). A value is matched EXACTLY against puNES's own names (objSettings.cpp
// val_to_int, case and all) - the names below are those of settings.h's opt_* tables, not of its help text where the two
// differ ("none", not the help's "nofilter"). NOTHING PASSED ON THE LINE STAYS (measured 18/10: --fullscreen=yes, then
// --fullscreen=yes --fullscreen-window=yes --mode=ntsc; puNES.cfg still said fullscreen=no, fullscreen in window=no,
// preferred mode=auto after the exit; then --fullscreen=no --mode=pal --size=3x --filter=hq2x --palette=sony
// --rewind-minutes=disabled: the window's title said "PAL, 3x, Hq2X, Sony CXA2025AS US", and puNES.cfg still had 2x, none,
// ntsc, 15, auto after the exit) - so a game's choice never leaks into the next game, and "As set in puNES" is
// puNES's own. Every option but the screen is "As set in puNES" until a game's window chooses one (Mehdi, 06/10: the
// general settings are the emulator's own); the Nixx page sets the screen alone (OnPage).
//
// LEFT OUT, measured or read: --stretch-fullscreen (cmd_line.cpp case 't' sets cfg_from_file.scale = !value - the window
// size, not the stretch: broken in 0.111); --fullscreen-window (measured 18/10: a maximized window with its frame, not a
// borderless one); --shortcut.* (measured: puNES WRITES it into input.cfg at start - never a per-launch choice); "file"
// for the shader and the palette (a path of its own); --portable (an empty puNES.cfg does it, PunesCfg.EnsurePortable).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Punes
{
    internal sealed class PunesOption
    {
        public string Key, Label, Hint, Group;
        public string[] Values, Texts;    // the stored values, and what the lists show for them
        public string Default;
        public string Switch;             // the long option, "--mode"; null: PunesSettings.Args writes it (Screen)
        public string CfgGroup, CfgKey;   // its line in puNES.cfg - what "As set in puNES" reads
        public bool OnPage;               // set for every game on the Nixx page; the others are a game's alone, else puNES's own
        public string[] Systems;          // the systems it means anything for - null: both

        public string TextOf(string value) { int i = Array.IndexOf(Values, value); return i < 0 ? value : Texts[i]; }
        public bool AppliesTo(string system) => Systems == null || system == null || Systems.Contains(system);
    }

    internal static class PunesSettings
    {
        /// <summary>manifest.json's PluginId.</summary>
        public const string PluginId = "44210e28-d6be-44e4-a69d-4780f6546aee";

        /// <summary>For the probe: a settings file somewhere else than beside the install.</summary>
        internal static string PathOverride;

        /// <summary>The value of an option that leaves puNES's own: nothing passed, what puNES's menus have stays.</summary>
        public const string Punes = "punes";

        public static readonly string[] Groups = { "Picture", "Emulation" };

        private static readonly string[] YesNo = { "yes", "no" };

        public static readonly PunesOption[] All =
        {
            // -- the game --
            new PunesOption { Key = "Screen", Group = "Game", Label = "Screen", Default = "full", OnPage = true, CfgGroup = "video", CfgKey = "fullscreen",
                Values = new[] { "full", "windowed", Punes }, Texts = new[] { "Full screen", "Windowed", "As set in puNES" },
                Hint = "puNES's --fullscreen=yes / =no, given at each launch and not kept: puNES's own setting comes back when it quits. Alt+Enter "
                     + "switches while playing (puNES's default key)." },
            Pick("Region", "Game", "Region (timing)", "--mode", "system", "preferred mode", new[] { "auto", "ntsc", "pal", "dendy" },
                 new[] { "Auto - from puNES's game database", "NTSC (America, Japan)", "PAL (Europe)", "Dendy (the Famiclone timing)" },
                 "puNES's preferred mode (--mode): the console's timing. Auto takes the game's from puNES's database (nes20db.xml), else NTSC; "
                 + "a PAL game run as NTSC goes too fast, and the other way too slow. Dendy is the Russian Famiclone's timing."),

            // -- Picture --
            Pick("Size", "Picture", "Window size", "--size", "video", "size window", new[] { "1x", "2x", "3x", "4x", "5x", "6x" },
                 new[] { "1x", "2x (puNES's default)", "3x", "4x", "5x", "6x" }, "puNES's Video > Size: the window's size when not in full screen."),
            // "scaliung": puNES's own spelling of the key (settings.h:1139, measured in the puNES.cfg it writes).
            Bool("IntegerScaling", "Picture", "Integer scaling", "--int-scl-fullscreen", "video", "use integer scaliung in fullscreen", "whole multiples only",
                 "puNES's Video > Fullscreen > Use integer scaling: in full screen, the picture grows by whole multiples only - every pixel the same "
                 + "size, borders around."),
            Pick("PixelAspect", "Picture", "Pixel aspect ratio", "--pixel-aspect-ratio", "video", "pixel aspect ratio", new[] { "1:1", "5:4", "8:7", "11:8" },
                 new[] { "1:1 (square pixels)", "5:4", "8:7 (puNES's default, a TV's)", "11:8" }, "puNES's Video > Pixel Aspect Ratio: the shape of a pixel - 8:7 is the "
                 + "picture a NES gave on a television, 1:1 the console's pixels square."),
            Pick("Filter", "Picture", "Filter", "--filter", "video", "filter",
                 new[] { "none", "scale2x", "scale3x", "scale4x", "hq2x", "hq3x", "hq4x", "xbrz2x", "xbrz3x", "xbrz4x", "xbrz5x", "xbrz6x", "2xsai", "super2xsai", "supereagle",
                         "tv2x", "tv3x", "tv4x", "dotmatrix", "ntsc", "paltv1x", "paltv2x", "paltv3x", "paltv4x", "ntscbisqwit2x", "ntscbisqwit4x", "ntscbisqwit8x" },
                 new[] { "None (puNES's default)", "Scale2x", "Scale3x", "Scale4x", "hq2x", "hq3x", "hq4x", "xBRZ 2x", "xBRZ 3x", "xBRZ 4x", "xBRZ 5x", "xBRZ 6x", "2xSaI", "Super 2xSaI",
                         "Super Eagle", "TV 2x", "TV 3x", "TV 4x", "Dot matrix", "NTSC (blargg)", "PAL TV 1x", "PAL TV 2x", "PAL TV 3x", "PAL TV 4x", "NTSC Bisqwit 2x",
                         "NTSC Bisqwit 4x", "NTSC Bisqwit 8x" },
                 "puNES's Video > Filter: a software filter on the picture - pixel-art smoothers (Scale, hq, xBRZ, SaI), TV and NTSC signal looks."),
            Pick("Shader", "Picture", "Shader", "--shader", "video", "shader", new[] { "none", "crtdotmask", "crtscanlines", "crtcurve", "emboss", "noise", "ntsc2phcomp", "oldtv" },
                 new[] { "None (puNES's default)", "CRT dot mask", "CRT scanlines", "CRT with curve", "Emboss", "Noise", "NTSC 2-phase composite", "Old TV" },
                 "puNES's Video > Shader: its built-in shaders, on the graphics card. A shader file of your own is set in puNES's menus."),
            Pick("Palette", "Picture", "Palette", "--palette", "video", "palette", new[] { "ntsc", "pal", "sony", "frbnst", "frbyuv", "mono", "green", "raw" },
                 new[] { "NTSC (puNES's default)", "PAL", "Sony CXA2025AS US", "FirebrandX Nostalgia", "FirebrandX YUV", "Monochrome", "Green", "Raw" },
                 "puNES's Video > Palette: the colours the NES's picture is drawn with."),
            new PunesOption { Key = "VSync", Group = "Picture", Label = "VSync", Default = Punes, Switch = "--vsync", CfgGroup = "video", CfgKey = "vsync",
                Values = new[] { Punes, "on", "off" }, Texts = new[] { "As set in puNES", "On - against tearing (puNES's default)", "Off" },
                Hint = "puNES's Video > VSync: waits for the screen's refresh." },
            Bool("Interpolation", "Picture", "Interpolation", "--interpolation", "video", "interpolation", "smoothed",
                 "puNES's Video > Interpolation: the scaled picture smoothed (bilinear) instead of sharp pixels."),

            // -- Emulation --
            Bool("BackgroundPause", "Emulation", "Pause in the background", "--background-pause", "system", "pause when in background", "when puNES loses the focus",
                 "puNES's Settings > Pause when in background: the game stops while another window has the focus. On is puNES's default."),
            Bool("SaveBattery", "Emulation", "Save the cartridge every 3 min", "--save-battery-ram-file", "system", "save battery ram file every 180 sec", "nothing lost if puNES is killed",
                 "puNES's Settings > Save battery RAM file every 3 minutes: the cartridge's save written as the game goes, not only when puNES quits.", PunesSystems.Nes),
            Pick("Rewind", "Emulation", "Rewind", "--rewind-minutes", "system", "rewind minutes", new[] { "disabled", "2", "5", "15", "30", "60", "unlimited" },
                 new[] { "Off", "2 minutes", "5 minutes", "15 minutes (puNES's default)", "30 minutes", "60 minutes", "Unlimited" },
                 "puNES's rewind buffer: how far back its rewind keys (Ctrl+Left, then the arrows) can go."),
        };

        private static PunesOption Bool(string key, string group, string label, string sw, string cfgGroup, string cfgKey, string on, string hint, params string[] systems)
            => new PunesOption { Key = key, Group = group, Label = label, Switch = sw, CfgGroup = cfgGroup, CfgKey = cfgKey, Default = Punes, Hint = hint,
                                 Systems = systems.Length == 0 ? null : systems,
                                 Values = new[] { Punes, "yes", "no" }, Texts = new[] { "As set in puNES", "On - " + on, "Off" } };

        private static PunesOption Pick(string key, string group, string label, string sw, string cfgGroup, string cfgKey, string[] values, string[] texts, string hint, params string[] systems)
            => new PunesOption { Key = key, Group = group, Label = label, Switch = sw, CfgGroup = cfgGroup, CfgKey = cfgKey, Default = Punes, Hint = hint,
                                 Systems = systems.Length == 0 ? null : systems,
                                 Values = new[] { Punes }.Concat(values).ToArray(), Texts = new[] { "As set in puNES" }.Concat(texts).ToArray() };

        public static PunesOption Option(string key) => All.First(o => o.Key == key);

        // ── the line ─────────────────────────────────────────────────────────

        /// <summary>A game's options as puNES's "--option=value" arguments, the screen first; nothing for one left "As set in
        /// puNES", nor for one that means nothing for the game's system.</summary>
        public static List<string> Args(string gameId, string system) => Args(key => For(gameId, key), system);

        /// <summary>The same, from values given (a game's window, before it saves them).</summary>
        public static List<string> Args(Func<string, string> valueOf, string system)
        {
            var list = new List<string>();
            foreach (var o in All)
            {
                var value = valueOf(o.Key);
                if (value == null || value == Punes || !o.AppliesTo(system) || !o.Values.Contains(value)) continue;
                if (o.Key == "Screen") { list.Add("--fullscreen=" + (value == "full" ? "yes" : "no")); continue; }
                if (o.Switch != null) list.Add(o.Switch + "=" + value);
            }
            return list;
        }

        /// <summary>The long options the plugin owns on a line: taken out before its own go in (never twice).</summary>
        public static IEnumerable<string> Switches => All.Where(o => o.Switch != null).Select(o => o.Switch).Concat(new[] { "--fullscreen" });

        /// <summary>What puNES.cfg has for an option, as its list says it - null when it has nothing (no file, no line).</summary>
        public static string CfgHas(PunesOption o, PunesCfg cfg)
        {
            if (cfg == null || o.CfgKey == null) return null;
            var raw = cfg.Get(o.CfgGroup, o.CfgKey);
            if (string.IsNullOrEmpty(raw)) return null;
            if (o.Key == "Screen") return raw == "yes" ? "Full screen" : raw == "no" ? "Windowed" : raw;
            int i = Array.IndexOf(o.Values, raw);
            if (i <= 0) return raw;
            var t = o.Texts[i];
            if (t.StartsWith("On - ")) return "On";
            int p = t.IndexOf(" (puNES's default", StringComparison.Ordinal);
            return p > 0 ? t.Substring(0, p) : t;
        }

        // ── every game's ──

        /// <summary>A value as stored: one of the option's, lower case - else null.</summary>
        private static string Stored(PunesOption o, string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var v = s.Trim().ToLowerInvariant();
            return o.Values.Contains(v) ? v : null;
        }

        /// <summary>Every game's value of <paramref name="key"/>: what the Nixx page set (the options it shows), else the
        /// option's default.</summary>
        public static string Value(string key)
        {
            var o = Option(key);
            if (!o.OnPage) return o.Default;
            return (Read(SettingsPath).TryGetValue(key, out var s) ? Stored(o, s) : null) ?? o.Default;
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
            return Read(path).TryGetValue(key, out var s) ? Stored(Option(key), s) : null;
        }

        /// <summary>The game's own value set, or taken away with null.</summary>
        public static void SetGameValue(string gameId, string key, string value)
        {
            var path = GamePath(gameId);
            if (path != null) Write(path, key, value);
        }

        /// <summary>What a game starts with: its own value, else every game's.</summary>
        public static string For(string gameId, string key) => GameValue(gameId, key) ?? Value(key);

        // ── the files ──

        public static string SettingsPath
        {
            get
            {
                if (PathOverride != null) return PathOverride;
                var dll = typeof(PunesSettings).Assembly.Location;
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
