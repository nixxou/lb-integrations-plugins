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

        public static readonly string[] Groups = { "Picture", "Emulation", "Console" };

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

        /// <summary>A property of one console's section, for the systems of that section ("ramPowerOnState" on NES, SNES...).</summary>
        private static Func<MesenSystem, string> In(string prop, params string[] sections)
            => s => Section(s) is string sec && sections.Contains(sec) ? sec + "." + prop : null;

        /// <summary>A property for the given systems alone (their keys), under one section.</summary>
        private static Func<MesenSystem, string> Only(string path, params string[] systems)
            => s => s != null && systems.Contains(s.Key) ? path : null;

        /// <summary>A property every console has (Mesen's Video / Emulation, for every console).</summary>
        private static Func<MesenSystem, string> Every(string path) => s => s != null && Section(s) != null ? path : null;

        private static readonly (string, string)[] OffOn = { ("false", "Off"), ("true", "On") };

        public static readonly MesenOption[] All =
        {
            new MesenOption { Key = "Fullscreen", Group = "Game", Label = "Screen", Default = "true", OnPage = true,
                Values = new[] { "true", "false" }, Texts = new[] { "Full screen", "Windowed" },
                Hint = "Full screen from the start (Mesen's --fullscreen, once the game is loaded). F11 switches in Mesen; Esc quits (bound by the plugin when free)." },

            // ── Picture ──
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
            Pick("Scanlines", "Picture", "Scanlines", Every("video.scanlineIntensity"), false,
                 new[] { ("0", "None"), ("20", "Light (20 %)"), ("40", "Medium (40 %)"), ("60", "Strong (60 %)"), ("80", "Very strong (80 %)") },
                 "Mesen's Video > Scanlines: dark lines between the picture's lines, as on a CRT."),
            Pick("Bilinear", "Picture", "Smoothing", Every("video.useBilinearInterpolation"), false, new[] { ("false", "Off - sharp pixels"), ("true", "Bilinear - smoothed") },
                 "Mesen's Video > Use bilinear interpolation: the picture smoothed when it is scaled up."),
            Pick("IntegerScale", "Picture", "Full screen scale", Every("video.fullscreenForceIntegerScale"), false,
                 new[] { ("false", "Fill the screen"), ("true", "Whole multiples only - every pixel the same size") },
                 "Mesen's Video > Fullscreen: use integer scale values - sharp, even pixels, with black borders."),
            Pick("Rotation", "Picture", "Rotation", Every("video.screenRotation"), false,
                 new[] { ("None", "None"), ("_90Degrees", "90 degrees"), ("_180Degrees", "180 degrees"), ("_270Degrees", "270 degrees") },
                 "Mesen's Video > Screen rotation: a vertical game on a turned screen (WonderSwan, homebrew)."),
            Pick("LcdColors", "Picture", "LCD colours", s => s == null ? null : s.Core == "Gameboy" && s.Key != "gb" ? "gameboy.gbcAdjustColors"
                                                               : s.Core == "Gba" ? "gba.gbaAdjustColors" : s.Core == "Ws" ? "ws.lcdAdjustColors" : null, false,
                 OffOn, "Mesen's LCD colour emulation (Game Boy Color, Game Boy Advance, WonderSwan): the paler colours of the console's own screen."),
            Pick("BlendFrames", "Picture", "Blend frames", s => s == null ? null : s.Key == "gg" ? "sms.ggBlendFrames"
                                                                  : s.Core is "Gameboy" or "Gba" or "Ws" ? Section(s) + ".blendFrames" : null, false,
                 OffOn, "Mesen's Blend frames: each picture mixed with the one before, as the handheld's slow LCD did - the flicker-based transparency some games use."),
            Pick("HighResBlend", "Picture", "High resolution blend", In("highResBlendMode", "snes"), false,
                 new[] { ("None", "None"), ("BlendAll", "Blend the whole screen"), ("BlendEvenOdd", "Blend even / odd columns - fake transparency") },
                 "Mesen's SNES high resolution blend: the transparency some games draw with the 512-pixel mode (Kirby's Dream Land 3...)."),
            Pick("PalBorders", "Picture", "PAL borders", In("enablePalBorders", "nes"), false, OffOn,
                 "Mesen's NES Enable PAL black borders: the black edges of the PAL console's picture, when the game runs as PAL or Dendy."),
            Pick("SgbBorders", "Picture", "Super Game Boy border", Only("gameboy.hideSgbBorders", "sgb"), false,
                 new[] { ("false", "Shown"), ("true", "Hidden - the game's screen alone") }, "Mesen's Hide Super Game Boy borders."),
            Pick("LcdIcons", "Picture", "LCD icons", In("lcdShowIcons", "ws"), false, OffOn, "Mesen's WonderSwan Show LCD icons: the icons beside the screen (battery, sound...)."),

            // ── Emulation ──
            Pick("Region", "Emulation", "Region (timing)", s => s == null ? null : s.Key == "gg" ? "sms.gameGearRegion"
                                                         : Section(s) is "nes" or "snes" or "sms" or "cv" ? Section(s) + ".region" : null, false,
                 new[] { ("Auto", "Auto - from the game"), ("Ntsc", "NTSC (60 Hz)"), ("Pal", "PAL (50 Hz)"), ("Dendy", "Dendy (the Russian clone, 50 Hz) - NES only"),
                         ("NtscJapan", "NTSC Japan - Game Gear only") },
                 "Mesen's region of the console (Settings > the console > Emulation): NTSC or PAL timing. Auto takes it from the game's header or name.",
                 s => s == null ? null : s.Core == "Nes" ? new[] { "auto", "ntsc", "pal", "dendy" } : s.Key == "gg" ? new[] { "auto", "ntsc", "ntscjapan", "pal" } : AnyRegion),
            Pick("SpriteLimit", "Emulation", "Sprite limit", In("removeSpriteLimit", "nes", "sms", "cv", "pcEngine"), false,
                 new[] { ("false", "The console's limit per line"), ("true", "No limit - less flicker") },
                 "Mesen's Remove sprite limit (Settings > the console > Video): kept, sprites flicker in busy scenes as on the console; a few games rely on the limit."),
            Pick("AdaptiveSpriteLimit", "Emulation", "Adaptive sprite limit", In("adaptiveSpriteLimit", "nes"), false, OffOn,
                 "Mesen's NES Automatically re-enable sprite limit: with no limit, the limit comes back where a game uses it to hide sprites (no glitch)."),
            Pick("RunAhead", "Emulation", "Run-ahead", Every("emulation.runAheadFrames"), false,
                 new[] { ("0", "Off"), ("1", "1 frame"), ("2", "2 frames"), ("3", "3 frames"), ("4", "4 frames") },
                 "Mesen's Emulation > Run-ahead: the game's answer to a button shown frames earlier - less input lag, more CPU."),
            Pick("Speed", "Emulation", "Speed", Every("emulation.emulationSpeed"), false,
                 new[] { ("50", "50 %"), ("75", "75 %"), ("100", "100 % - normal"), ("150", "150 %"), ("200", "200 %"), ("300", "300 %") },
                 "Mesen's Emulation > Speed: the game slower or faster than the console (the music too)."),
            Pick("Overclock", "Emulation", "Overclock", s => s == null ? null : s.Core is "Nes" or "Snes" && s.Key != "sgb" ? Section(s) + ".ppuExtraScanlinesBeforeNmi"
                                                           : s.Core == "Gba" ? "gba.overclockScanlineCount" : null, false,
                 new[] { ("0", "Off - the console's speed"), ("100", "Light (+100 lines)"), ("300", "Medium (+300 lines)"), ("600", "Strong (+600 lines)") },
                 "Mesen's Overclocking (extra scanlines before NMI on NES / SNES, extra scanlines on the GBA): fewer slowdowns in busy scenes. A few games break: lower it then."),
            Pick("SuperFx", "Emulation", "Super FX speed", In("gsuClockSpeed", "snes"), false,
                 new[] { ("100", "100 % - the chip's speed"), ("200", "200 %"), ("300", "300 %"), ("500", "500 %") },
                 "Mesen's SNES Super FX clock speed: Star Fox, Stunt Race FX, Yoshi's Island run smoother (other games: no effect)."),
            Pick("RamPowerOn", "Emulation", "RAM at power on", In("ramPowerOnState", "nes", "snes", "gameboy", "gba", "pcEngine", "sms", "cv"), false,
                 new[] { ("Random", "Random - as a real console"), ("AllZeros", "All zeros"), ("AllOnes", "All ones") },
                 "Mesen's RAM power-on state: what the console's memory holds when it is switched on. Some games and hacks behave differently (random numbers, glitches)."),

            // ── Console ──
            Pick("HdPacks", "Console", "HD packs", In("enableHdPacks", "nes"), false, OffOn, "Mesen's NES HD packs: the game's graphics replaced by an HD pack in Mesen's HdPacks folder."),
            Pick("FdsAutoInsert", "Console", "Disk sides", Only("nes.fdsAutoInsertDisk", "fds"), false,
                 new[] { ("false", "Changed by hand"), ("true", "Inserted automatically") }, "Mesen's FDS Automatically insert disk: the side the game asks for is put in by itself."),
            Pick("FdsFastLoad", "Console", "Disk loading", Only("nes.fdsFastForwardOnLoad", "fds"), false,
                 new[] { ("false", "At the drive's speed"), ("true", "Fast forward while loading") }, "Mesen's FDS Automatically fast forward when loading."),
            Pick("Sgb2", "Console", "Super Game Boy", Only("gameboy.useSgb2", "sgb"), false,
                 new[] { ("true", "Super Game Boy 2 (SGB2.sfc)"), ("false", "Super Game Boy (SGB1.sfc)") },
                 "Mesen's Use Super Game Boy 2: the SGB2 cartridge (its border, its exact speed), else the first one - its BIOS needed."),
            Pick("GbaSkipBoot", "Console", "Boot screen", In("skipBootScreen", "gba"), false, new[] { ("false", "Shown"), ("true", "Skipped") },
                 "Mesen's GBA Skip boot screen: the game starts at once, without the Game Boy Advance logo."),
            Pick("SmsFm", "Console", "FM sound chip", Only("sms.enableFmAudio", "sms"), false,
                 new[] { ("false", "Off - the console's own sound"), ("true", "On - the Japanese FM unit (YM2413)") },
                 "Mesen's Master System Enable FM audio: the music of games written for the Japanese FM sound unit."),
            Pick("SmsRevision", "Console", "Console model", Only("sms.revision", "sms"), false,
                 new[] { ("Compatibility", "Compatibility (Mesen's default)"), ("Sms1", "Master System 1"), ("Sms2", "Master System 2") },
                 "Mesen's Master System revision: a few games show differently on the first and second model."),
            Pick("WsAutoRotate", "Console", "Auto rotation", In("autoRotate", "ws"), false, OffOn,
                 "Mesen's WonderSwan Auto-rotate: the picture turned for a game played with the console held upright."),
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

        /// <summary>The game's options of its own, as switches for <paramref name="system"/> - those its console has, and (with
        /// <paramref name="json"/>, the install's settings.json) those THIS Mesen has: a setting a later Mesen renamed or dropped
        /// is left out (logged), never passed as a switch Mesen would refuse on its screen.</summary>
        public static List<string> SwitchesFor(string gameId, MesenSystem system, MesenJson json = null)
        {
            var list = new List<string>();
            foreach (var o in All.Where(o => !o.OnPage))
            {
                var v = GameValue(gameId, o.Key);
                if (v == null) continue;
                var mine = Switches(o, system, v);
                var missing = Missing(mine, json);
                if (missing != null) { Log.Info("launch: the game's option \"" + o.Label + "\" left out - this Mesen has no " + missing); continue; }
                list.AddRange(mine);
            }
            return list;
        }

        /// <summary>The first setting of <paramref name="switches"/> the install's settings.json lacks (every setting is in the file
        /// Mesen writes - measured 18/10); null when all are there, or when the file cannot tell.</summary>
        internal static string Missing(IEnumerable<string> switches, MesenJson json)
        {
            if (json == null || !json.Readable || !json.Exists || json.Get("Preferences") == null) return null;
            foreach (var s in switches)
            {
                var key = MesenSystems.SwitchParts(s)?.Key;
                if (key != null && json.Get(MesenSession.PathOfSwitch(key)) == null) return key;
            }
            return null;
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
