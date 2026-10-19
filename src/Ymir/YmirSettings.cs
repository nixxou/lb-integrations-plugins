// This plugin's settings: every game's in one key=value per line in
//
//     <plugin root>\.data\<PluginId>\settings.ini
//
// and a game's own beside it in games\<game id>.ini, the same keys - a key absent there follows every game's. The same files
// and rules as MesenSettings. Read at the moment a setting is used, never cached.
//
// KEPT SIMPLE (memory "nixx-page-simple-per-game-rich"): the Nixx page holds the screen and when the compatibility list is
// shown; a game's window, its own screen. Every other setting is Ymir's own, in its Settings window (F10).
//
// The compatibility list's copies are beside the settings, in compat\ (YmirCompat).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Ymir
{
    internal sealed class YmirOption
    {
        public string Key, Label, Hint, Default;
        public string[] Values, Texts;
        public bool PerGame;
        public string TextOf(string value) { int i = Array.IndexOf(Values, value); return i < 0 ? value : Texts[i]; }
    }

    internal static class YmirSettings
    {
        /// <summary>manifest.json's PluginId.</summary>
        public const string PluginId = "97e147ad-dd14-4a6a-9de0-1abc034aea63";

        /// <summary>For the probe: a settings file somewhere else than beside the install.</summary>
        internal static string PathOverride;

        public const string Fullscreen = "Fullscreen";
        public const string CompatAtLaunch = "CompatAtLaunch";

        public static readonly YmirOption[] All =
        {
            new YmirOption
            {
                Key = Fullscreen, Label = "Screen", Default = "true", PerGame = true,
                Values = new[] { "false", "true" }, Texts = new[] { "In a window", "Full screen" },
                Hint = "Full screen: Ymir is started with -f. F11 (or Alt+Enter) switches while the game runs.",
            },
            new YmirOption
            {
                Key = CompatAtLaunch, Label = "Compatibility at launch", Default = "always",
                Values = new[] { "always", "problems", "never" },
                Texts = new[] { "Always shown", "Only when the game may not play", "Never shown" },
                Hint = "The game's state in Ymir's compatibility list, shown for a few seconds as it starts (on LiteBox's loading bar in LiteBox). "
                       + "\"Only when the game may not play\": not for Perfect and Playable games.",
            },
        };

        public static YmirOption Option(string key) => All.First(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase));

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
            return safe.Length == 0 ? null : Path.Combine(DataDir, "games", safe + ".ini");
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

        // ── the files ──

        public static string SettingsPath
        {
            get
            {
                if (PathOverride != null) return PathOverride;
                var dll = typeof(YmirSettings).Assembly.Location;
                var pluginDir = Path.GetDirectoryName(dll);
                var root = Path.GetDirectoryName(pluginDir) ?? pluginDir;
                return Path.Combine(root, ".data", PluginId, "settings.ini");
            }
        }

        public static string DataDir => Path.GetDirectoryName(SettingsPath);

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
