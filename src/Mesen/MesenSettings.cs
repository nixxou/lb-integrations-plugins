// This plugin's settings: every game's in one key=value per line in
//
//     <plugin root>\.data\<PluginId>\settings.ini
//
// and a game's own beside it in games\<game id>.ini, the same keys - a key absent there follows every game's. The same files
// and rules as BsnesSettings. Read at the moment a setting is used, never cached: the Nixx window and a game's window write the
// files, the next launch sees them.
//
// KEPT SIMPLE (Mehdi 06/10, "nixx-page-simple-per-game-rich"): the Nixx page holds the screen only - every other setting is
// Mesen's own, in its menus. A game's own choices are the per-game window's (MesenGameMenu): its system, and a few options
// of Mesen's - "As set in Mesen" until the game chooses.
//
// A GAME'S OPTION IS A SWITCH (docs\mesen.md §5: --section.prop=value, nested properties too, enum names without regard to
// case - UI\Utilities\CommandLineHelper.cs): put on the line at the launch for the game's console, and - because Mesen keeps
// a switch in settings.json - put back after the session by MesenSession (FromLine). The picture options go through Mesen's
// own per-console overrides (ConsoleOverrideConfig: <console>.configOverrides.overrideVideoFilter + .videoFilter; the Game
// Gear's is sms.ggConfigOverrides), the console Mesen applies them to itself (ConsoleOverrideConfig.GetActiveOverride), so a
// NES game's filter never reaches a Game Boy game. Names read in UI\Config\*.cs at CE 2.2.1. The Super Game Boy is taken as
// the SNES console's (it runs in the SNES core) - not measured.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Mesen
{
    internal sealed class MesenOption
    {
        public string Key, Label, Hint, Group;
        public string[] Values, Texts;    // the stored values (lower case), and what the lists show for them
        public string Default;
        public bool OnPage;               // set for every game on the Nixx page; the others a game's alone

        /// <summary>Mesen's property behind the option for a system ("nes.region"), null where the console has none.</summary>
        public Func<MesenSystem, string> Property = _ => null;
        /// <summary>The values a system takes (Mesen's ValidValues); null: all.</summary>
        public Func<MesenSystem, string[]> Valid = _ => null;
        /// <summary>A picture option: Mesen's per-console override, its switch "override&lt;Prop&gt;" set with it.</summary>
        public bool Override;
        /// <summary>The value as Mesen names it on a switch.</summary>
        public Dictionary<string, string> MesenName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string TextOf(string value) { int i = Array.IndexOf(Values, value); return i < 0 ? value : Texts[i]; }
        public bool AppliesTo(MesenSystem s) => s != null && Property(s) != null;
        public bool Takes(MesenSystem s, string value) => AppliesTo(s) && Values.Contains(value) && (Valid(s)?.Contains(value) ?? true);
    }

    internal static class MesenSettings
    {
        /// <summary>manifest.json's PluginId.</summary>
        public const string PluginId = "6823227e-f902-452b-a1b0-7591a7e55917";

        /// <summary>For the probe: a settings file somewhere else than beside the install.</summary>
        internal static string PathOverride;

        public static readonly string[] Groups = { "Picture", "Emulation" };

        /// <summary>The section of Mesen's settings a system's console reads (UI\Config: Nes, Snes, Gameboy, Gba, PcEngine, Sms,
        /// Cv, Ws). The Super Game Boy runs in the SNES core.</summary>
        internal static string Section(MesenSystem s)
        {
            if (s == null) return null;
            if (s.Key == "cv") return "cv";
            if (s.Key == "sgb") return "snes";
            switch (s.Core)
            {
                case "Nes": return "nes";
                case "Snes": return "snes";
                case "Gameboy": return "gameboy";
                case "Gba": return "gba";
                case "PcEngine": return "pcEngine";
                case "Sms": return "sms";
                case "Ws": return "ws";
                default: return null;
            }
        }

        /// <summary>The console's picture overrides ("nes.configOverrides"; the Game Gear's "sms.ggConfigOverrides").</summary>
        internal static string Overrides(MesenSystem s)
        {
            var sec = Section(s);
            return sec == null ? null : s.Key == "gg" ? "sms.ggConfigOverrides" : sec + ".configOverrides";
        }

        private static readonly string[] AnyRegion = { "auto", "ntsc", "pal" };

        public static readonly MesenOption[] All =
        {
            new MesenOption { Key = "Fullscreen", Group = "Game", Label = "Screen", Default = "true", OnPage = true,
                Values = new[] { "true", "false" }, Texts = new[] { "Full screen", "Windowed" },
                Hint = "Full screen from the start (Mesen's --fullscreen, once the game is loaded). F11 switches in Mesen; Esc quits (bound by the plugin when free)." },
            Pick("VideoFilter", "Picture", "Filter", s => Overrides(s) == null ? null : Overrides(s) + ".videoFilter", true,
                 new[] { ("None", "None - the console's pixels"), ("NtscBlargg", "NTSC (blargg) - a TV's signal"), ("NtscBisqwit", "NTSC (Bisqwit) - NES only"),
                         ("LcdGrid", "LCD grid - a handheld's screen"), ("HQ2x", "HQ2x"), ("HQ3x", "HQ3x"), ("HQ4x", "HQ4x"), ("xBRZ2x", "xBRZ 2x"), ("xBRZ3x", "xBRZ 3x"),
                         ("xBRZ4x", "xBRZ 4x"), ("xBRZ6x", "xBRZ 6x"), ("Scale2x", "Scale2x"), ("Scale3x", "Scale3x"), ("SuperEagle", "Super Eagle"), ("Super2xSai", "Super 2xSaI"),
                         ("Prescale2x", "Prescale 2x - sharp, smoothed edges"), ("Prescale4x", "Prescale 4x") },
                 "Mesen's Video > Filter, for this game's console only (its own override, Settings > the console > Video). Without it, the filter "
                 + "set in Mesen for every console is used.",
                 s => s?.Core == "Nes" ? null : new[] { "none", "ntscblargg", "lcdgrid", "hq2x", "hq3x", "hq4x", "xbrz2x", "xbrz3x", "xbrz4x", "xbrz6x", "scale2x", "scale3x",
                                                       "supereagle", "super2xsai", "prescale2x", "prescale4x" }),
            Pick("AspectRatio", "Picture", "Aspect ratio", s => Overrides(s) == null ? null : Overrides(s) + ".aspectRatio", true,
                 new[] { ("NoStretching", "No stretching - square pixels"), ("Auto", "Auto - the console's own"), ("NTSC", "NTSC (8:7)"), ("PAL", "PAL (11:8)"),
                         ("Standard", "4:3"), ("Widescreen", "16:9 - stretched") },
                 "Mesen's Video > Aspect Ratio, for this game's console only (its own override). Mesen's default is no stretching: square pixels."),
            Pick("Region", "Emulation", "Region (timing)", s => s == null ? null : s.Key == "gg" ? "sms.gameGearRegion"
                                                         : Section(s) is "nes" or "snes" or "sms" or "cv" ? Section(s) + ".region" : null, false,
                 new[] { ("Auto", "Auto - from the game"), ("Ntsc", "NTSC (60 Hz)"), ("Pal", "PAL (50 Hz)"), ("Dendy", "Dendy (the Russian clone, 50 Hz) - NES only"),
                         ("NtscJapan", "NTSC Japan - Game Gear only") },
                 "Mesen's region of the console (Settings > the console > Emulation): NTSC or PAL timing. Auto takes it from the game's header or name.",
                 s => s == null ? null : s.Core == "Nes" ? new[] { "auto", "ntsc", "pal", "dendy" } : s.Key == "gg" ? new[] { "auto", "ntsc", "ntscjapan", "pal" } : AnyRegion),
            new MesenOption { Key = "SpriteLimit", Group = "Emulation", Label = "Sprite limit", Default = null,
                Values = new[] { "keep", "remove" }, Texts = new[] { "The console's limit per line", "No limit - less flicker" },
                Property = s => Section(s) is "nes" or "sms" or "cv" or "pcEngine" ? Section(s) + ".removeSpriteLimit" : null,
                MesenName = { ["keep"] = "false", ["remove"] = "true" },
                Hint = "Mesen's Remove sprite limit (Settings > the console > Video): kept, sprites flicker in busy scenes as on the console; a few games rely on the limit." },
        };

        private static MesenOption Pick(string key, string group, string label, Func<MesenSystem, string> property, bool over, (string Mesen, string Text)[] values, string hint,
                                        Func<MesenSystem, string[]> valid = null)
        {
            var o = new MesenOption { Key = key, Group = group, Label = label, Hint = hint, Property = property, Override = over, Default = null,
                                      Values = values.Select(v => v.Mesen.ToLowerInvariant()).ToArray(), Texts = values.Select(v => v.Text).ToArray() };
            if (valid != null) o.Valid = valid;
            foreach (var (mesen, _) in values) o.MesenName[mesen.ToLowerInvariant()] = mesen;
            return o;
        }

        public static MesenOption Option(string key) => All.First(o => o.Key == key);

        /// <summary>The game's options of its own, as switches for <paramref name="system"/> - those its console has.</summary>
        public static List<string> SwitchesFor(string gameId, MesenSystem system)
        {
            var list = new List<string>();
            foreach (var o in All.Where(o => !o.OnPage))
            {
                var v = GameValue(gameId, o.Key);
                if (v != null) list.AddRange(Switches(o, system, v));
            }
            return list;
        }

        /// <summary>An option's switches for a system and a value; none where that console has no such option.</summary>
        public static List<string> Switches(MesenOption o, MesenSystem system, string value)
        {
            var list = new List<string>();
            if (!o.Takes(system, value)) return list;
            var prop = o.Property(system);
            var name = o.MesenName.TryGetValue(value, out var n) ? n : value;
            if (o.Override) list.Add("--" + OverrideFlag(prop) + "=true");
            list.Add("--" + prop + "=" + name);
            return list;
        }

        /// <summary>"nes.configOverrides.videoFilter" -> "nes.configOverrides.overrideVideoFilter".</summary>
        private static string OverrideFlag(string prop)
        {
            int dot = prop.LastIndexOf('.');
            var last = prop.Substring(dot + 1);
            return prop.Substring(0, dot) + ".override" + char.ToUpperInvariant(last[0]) + last.Substring(1);
        }

        /// <summary>What Mesen's settings.json has for an option on a system, as its list says it - null when it has nothing
        /// to tell (no such option for that console, a value the list does not know).</summary>
        public static string InMesen(MesenOption o, MesenSystem system, MesenJson json)
        {
            try
            {
                if (json == null || !json.Readable || !json.Exists || !o.AppliesTo(system)) return null;
                var prop = o.Property(system);
                string Text(string path) => MesenJson.TextOf(json.Get(MesenSession.PathOfSwitch(path)));
                // A picture option not overridden for the console: Mesen's setting for every console (Video).
                var raw = !o.Override || Text(OverrideFlag(prop)) == "true" ? Text(prop) : Text("video." + prop.Substring(prop.LastIndexOf('.') + 1));
                if (raw == null) return null;
                var value = o.MesenName.FirstOrDefault(kv => string.Equals(kv.Value, raw, StringComparison.OrdinalIgnoreCase)).Key;
                return value == null ? null : o.TextOf(value);
            }
            catch { return null; }
        }

        // ── every game's ──

        public static string Value(string key)
        {
            var o = Option(key);
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

        public static void SetGameValue(string gameId, string key, string value)
        {
            var path = GamePath(gameId);
            if (path != null) Write(path, key, value);
        }

        public static string For(string gameId, string key) => GameValue(gameId, key) ?? Value(key);

        public static bool BoolFor(string gameId, string key) => For(gameId, key) == "true";

        /// <summary>The system the game's window chose for it (a MesenSystems key), or null: Mesen detects it.</summary>
        public static string GameSystem(string gameId)
        {
            var path = GamePath(gameId);
            if (path == null) return null;
            var v = Read(path).TryGetValue("System", out var s) ? (s ?? "").Trim() : null;
            return MesenSystems.System(v)?.Key;
        }

        public static void SetGameSystem(string gameId, string key)
        {
            var path = GamePath(gameId);
            if (path != null) Write(path, "System", MesenSystems.System(key)?.Key);
        }

        // ── the files ──

        public static string SettingsPath
        {
            get
            {
                if (PathOverride != null) return PathOverride;
                var dll = typeof(MesenSettings).Assembly.Location;
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
