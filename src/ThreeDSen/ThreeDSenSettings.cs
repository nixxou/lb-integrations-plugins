// This plugin's settings: every game's in one key=value per line in
//
//     <plugin root>\.data\<PluginId>\settings.ini
//
// and a game's own beside it in games\<game id>.ini, the same keys - a key absent there follows every game's. The same
// files and rules as Bsnes's (BsnesSettings). Read at the moment a setting is used, never cached.
//
// THE OPTIONS: the screen, passed on 3dSen's command line at each launch (ThreeDSenLaunch.Switches) - nothing is written
// into 3dSen's own settings (setting.cfg stays the user's: speed, shadows, skybox...). And, a game's own only, its 3D
// PROFILE ("Profile": a profile id of 3dSen; absent = matched by its title, ThreeDSenProfiles).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.ThreeDSen
{
    internal sealed class ThreeDSenOption
    {
        public string Key, Label, Hint;
        public string[] Values, Texts;
        public string Default;

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
        public static ThreeDSenOption Option(string key) => All.First(o => o.Key == key);

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
