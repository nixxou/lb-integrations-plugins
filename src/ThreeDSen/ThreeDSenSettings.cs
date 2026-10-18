// This plugin's settings: every game's in one key=value per line in
//
//     <plugin root>\.data\<PluginId>\settings.ini
//
// and a game's own beside it in games\<game id>.ini, the same keys - a key absent there follows every game's. The same
// files and rules as Bsnes's (BsnesSettings). Read at the moment a setting is used, never cached.
//
// THE OPTIONS:
//   - the screen (All), passed on 3dSen's command line at each launch (ThreeDSenLaunch.Switches); and, a game's own only,
//     its 3D PROFILE ("Profile": a profile id of 3dSen; absent = matched by its title, ThreeDSenProfiles);
//   - the pack's in-process plugin (Plugin, the Nixx page only): BepInEx in 3dSen's folder or not, and what the plugin does
//     with the pack's keys (Esc confirmation, the slots on F6 / F7, its log) - ThreeDSenBepInEx, tools\3dsen-bepinex;
//   - 3dSen's own settings for ONE game (Sets, the game's window only, "As set in 3dSen" until chosen): given to the plugin as
//     --nixx-set:<SettingManager property>=<value>, applied in memory for that session, never written into setting.cfg
//     (Overrides of the plugin). The values and their labels are those of 3dSen's own menu, MEASURED 19/10 with
//     --nixx-dump-options (docs\3dsen.md section 15): its dropdowns, its sliders and what their handlers set
//     (VRSettingController: Fov = 70 + 5 x slider, Volume = slider / 100, Skybox = item - 1, FrameRate = 60 / 90 / 120).
//   They need BepInEx: without it a game's 3dSen settings are not passed (the window says so).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LbIntegrations.ThreeDSen
{
    internal sealed class ThreeDSenOption
    {
        public string Key, Label, Hint, Group;
        public string[] Values, Texts;
        public string Default;
        /// <summary>The SettingManager property it sets through the plugin (Sets), else null.</summary>
        public string Set;

        public string TextOf(string value) { int i = Array.IndexOf(Values, value); return i < 0 ? value : Texts[i]; }
    }

    internal static class ThreeDSenSettings
    {
        /// <summary>manifest.json's PluginId.</summary>
        public const string PluginId = "c466f77e-dde3-46ad-942b-636b904dbe66";

        public const string ProfileKey = "Profile";

        /// <summary>For the probe: a settings file somewhere else.</summary>
        internal static string PathOverride;

        public static readonly ThreeDSenOption[] All =
        {
            new ThreeDSenOption { Key = "Screen", Label = "Screen", Default = "full",
                Values = new[] { "full", "window", "asis" }, Texts = new[] { "Full screen", "Window", "As 3dSen last had it" },
                Hint = "Full screen: at the main monitor's resolution. Window: of the size below. Unity remembers what 3dSen was last given, "
                     + "so the plugin always passes the whole set (-screen-fullscreen, -screen-width, -screen-height); \"As 3dSen last had it\" passes nothing." },
            new ThreeDSenOption { Key = "WindowSize", Label = "Window size", Default = "1280x720",
                Values = new[] { "1280x720", "1600x900", "1920x1080", "2560x1440" }, Texts = new[] { "1280 x 720", "1600 x 900", "1920 x 1080", "2560 x 1440" },
                Hint = "The size of 3dSen's window when the screen is Window." },
        };
        /// <summary>The in-process plugin, every game's (the Nixx page).</summary>
        public static readonly ThreeDSenOption[] Plugin =
        {
            OnOff("BepInEx", "In-process plugin", "on",
                  "BepInEx " + ThreeDSenBepInEx.Build + " and the pack's plugin in 3dSen's folder: Esc quits, F6 / F7 pick a save slot, "
                  + "a game's own 3dSen settings, 3dSen's files in its own folder (portable). Off: 3dSen as it comes, nothing of it in the folder."),
            OnOff("QuitConfirm", "Esc asks first", "on", "Esc shows \"Press Esc again to quit\" and quits on the second press."),
            OnOff("Slots", "Save slots on F6 / F7", "on",
                  "F6 / F7 pick 3dSen's slot (Quick, 1 to 36), F2 / F4 save and load it, each said on screen. 3dSen's camera keys move to the keypad."),
            OnOff("PluginLog", "Plugin's log", "off", "The plugin writes what it does into nixx.log, in 3dSen's data folder."),
        };

        public const string AsIn3dSen = "3dsen";   // "As set in 3dSen": nothing passed

        /// <summary>3dSen's own settings, a game's own (its window).</summary>
        public static readonly ThreeDSenOption[] Sets =
        {
            Pick("TvMode", "Game", "TV system", "TVMode", new[] { "ntsc", "pal", "dendy" }, new[] { "NTSC (60 Hz)", "PAL (50 Hz)", "Dendy" },
                 "3dSen's TV Mode: the console's timing."),
            Pick("GameSpeed", "Game", "Speed", "GameSpeed", Range(-8, 8, 1), Range(-8, 8, 1).Select(v => v == "0" ? "0 (normal)" : (v.StartsWith("-") ? v : "+" + v)).ToArray(),
                 "3dSen's Speed slider (-8 slower ... +8 faster)."),
            Pick("Volume", "Sound", "Volume", "Volume", Enumerable.Range(0, 11).Select(i => (i / 10.0).ToString(CultureInfo.InvariantCulture)).ToArray(),
                 Enumerable.Range(0, 11).Select(i => (i * 10) + " %").ToArray(), "3dSen's volume."),
            Pick("BgMusic", "Sound", "Menu music", "BGMusic", new[] { "true", "false" }, new[] { "On", "Off" }, "3dSen's BG Music: the music of its menu."),
            Pick("Fov", "Picture", "Field of view", "Fov", Range(45, 95, 5), Range(45, 95, 5).Select(v => v + "°" + (v == "70" ? " (3dSen's default)" : "")).ToArray(),
                 "3dSen's FOV slider: 70 + 5 per step."),
            Pick("Quality", "Picture", "Graphics", "GraphicQuality", new[] { "0", "1", "2" }, new[] { "Low", "Medium", "High" }, "3dSen's Graphic quality."),
            Pick("AntiAlias", "Picture", "Anti-aliasing", "AntiAlias", new[] { "0", "1", "2" }, new[] { "None", "FFAA", "SMAA" }, "3dSen's Anti-alias."),
            Pick("FrameRate", "Picture", "Frame rate", "FrameRate", new[] { "60", "90", "120" }, new[] { "60 fps", "90 fps", "120 fps" }, "3dSen's Framerate."),
            Pick("Render", "Picture", "Rendering", "RenderMethod", new[] { "0", "1" }, new[] { "Direct", "Render texture" }, "3dSen's Render: Render texture allows the texture resolution below."),
            Pick("TexRes", "Picture", "Texture resolution", "ResScaleIndex", new[] { "0", "1", "2", "3", "4" }, new[] { "Native", "2/3", "1/2", "1/3", "1/4" },
                 "3dSen's TexRes, with Rendering = Render texture."),
            Pick("Skybox", "Picture", "Background", "Skybox", new[] { "-1", "0", "1", "2" }, new[] { "Default", "Color", "Gradient", "Fantasy" }, "3dSen's Skybox."),
            Pick("TvBox", "Picture", "TV frame", "TVBox", new[] { "0", "1", "2", "3" }, new[] { "None", "TV 1", "TV 2", "TV 3" }, "3dSen's TV Box: the game inside a TV set."),
            Pick("Shadow", "Picture", "Shadows", "Shadow", new[] { "true", "false" }, new[] { "On", "Off" }, "3dSen's Shadow."),
            Pick("FastRender", "Picture", "Fast render", "SkipFrame", new[] { "true", "false" }, new[] { "On", "Off" }, "3dSen's Fast Render."),
            Pick("Mirror", "Picture", "Mirror", "Mirroring", new[] { "true", "false" }, new[] { "On", "Off" }, "3dSen's Mirror switch."),
        };

        private static ThreeDSenOption OnOff(string key, string label, string dflt, string hint)
            => new ThreeDSenOption { Key = key, Group = "Plugin", Label = label, Default = dflt, Values = new[] { "on", "off" }, Texts = new[] { "On", "Off" }, Hint = hint };

        private static ThreeDSenOption Pick(string key, string group, string label, string set, string[] values, string[] texts, string hint)
            => new ThreeDSenOption { Key = key, Group = group, Label = label, Set = set, Default = AsIn3dSen, Hint = hint + " Needs the in-process plugin.",
                                     Values = new[] { AsIn3dSen }.Concat(values).ToArray(), Texts = new[] { "As set in 3dSen" }.Concat(texts).ToArray() };

        private static string[] Range(int from, int to, int step)
        {
            var list = new List<string>();
            for (int v = from; v <= to; v += step) list.Add(v.ToString(CultureInfo.InvariantCulture));
            return list.ToArray();
        }

        public static ThreeDSenOption Option(string key) => All.Concat(Plugin).Concat(Sets).First(o => o.Key == key);

        /// <summary>The plugin's switches for a game: its 3dSen settings as --nixx-set (the ones it chose).</summary>
        public static List<string> SetArgs(Func<string, string> valueOf)
        {
            var list = new List<string>();
            foreach (var o in Sets)
            {
                var v = valueOf(o.Key);
                if (v == null || v == AsIn3dSen || !o.Values.Contains(v)) continue;
                list.Add("--nixx-set:" + o.Set + "=" + v);
            }
            return list;
        }

        // ── every game's ──

        public static string Value(string key)
        {
            var o = Option(key);
            if (o.Set != null) return o.Default;   // a game's own only
            var v = Read(SettingsPath).TryGetValue(key, out var s) ? (s ?? "").Trim().ToLowerInvariant() : null;
            return v != null && o.Values.Contains(v) ? v : o.Default;
        }

        public static bool On(string key) => Value(key) == "on";

        public static void SetValue(string key, string value) => Write(SettingsPath, key, value);

        // ── a game's own ──

        public static string GamePath(string gameId)
        {
            var safe = string.Concat((gameId ?? "").Where(c => char.IsLetterOrDigit(c) || c == '-'));
            return safe.Length == 0 ? null : Path.Combine(Path.GetDirectoryName(SettingsPath), "games", safe + ".ini");
        }

        /// <summary>The game's own value of <paramref name="key"/>, or null: it follows every game's. The profile is any id.</summary>
        public static string GameValue(string gameId, string key)
        {
            var path = GamePath(gameId);
            if (path == null) return null;
            if (!Read(path).TryGetValue(key, out var s) || string.IsNullOrWhiteSpace(s)) return null;
            if (key == ProfileKey) return s.Trim();
            var v = s.Trim().ToLowerInvariant();
            return Option(key).Values.Contains(v) ? v : null;
        }

        public static void SetGameValue(string gameId, string key, string value)
        {
            var path = GamePath(gameId);
            if (value != null && (value.Contains('\n') || value.Contains('\r'))) throw new ArgumentException("one line only");
            if (path != null) Write(path, key, value);
        }

        public static string For(string gameId, string key) => key == ProfileKey ? GameValue(gameId, key) : GameValue(gameId, key) ?? Value(key);

        // ── the files ──

        public static string SettingsPath
        {
            get
            {
                if (PathOverride != null) return PathOverride;
                var dll = typeof(ThreeDSenSettings).Assembly.Location;
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
