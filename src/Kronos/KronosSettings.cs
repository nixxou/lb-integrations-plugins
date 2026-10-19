// This plugin's settings: every game's in one key=value per line in
//
//     <plugin root>\.data\<PluginId>\settings.ini
//
// and a game's own beside it in games\<game id>.ini, the same keys - a key absent there follows every game's. The same files
// and rules as YmirSettings. Read at the moment a setting is used, never cached.
//
// KEPT SIMPLE (memory "nixx-page-simple-per-game-rich"): the Nixx page holds the screen; a game's window, its own screen and
// its own options (KronosGameOptions). Every other setting is Kronos's own, in its Settings window.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Kronos
{
    internal sealed class KronosOption
    {
        public string Key, Label, Hint, Default;
        public string[] Values, Texts;
        public bool PerGame;
        public string TextOf(string value) { int i = Array.IndexOf(Values, value); return i < 0 ? value : Texts[i]; }
    }

    internal static class KronosSettings
    {
        /// <summary>manifest.json's PluginId.</summary>
        public const string PluginId = "33fb087c-e747-4f08-ad4a-c9777827d7e9";

        /// <summary>For the probe: a settings file somewhere else than beside the install.</summary>
        internal static string PathOverride;

        public const string Fullscreen = "Fullscreen";

        public static readonly KronosOption[] All =
        {
            new KronosOption
            {
                Key = Fullscreen, Label = "Screen", Default = "true", PerGame = true,
                Values = new[] { "false", "true" }, Texts = new[] { "In a window", "Full screen" },
                Hint = "Full screen: Kronos is started with -f (2.7.5 and later). F11 (or Alt+Enter) switches while the game runs.",
            },
            new KronosOption
            {
                Key = ImportSort, Label = "ST-V import", Default = "true",
                Values = new[] { "true", "false" }, Texts = new[] { "Kronos's sets only", "As LaunchBox lists it" },
                Hint = "LaunchBox's Import ROM Files to Nixx-Kronos for Sega ST-V or Arcade: the last list keeps only the ST-V sets Kronos runs - "
                       + "a whole MAME folder holds thousands of others (KronosLbImport).",
            },
            new KronosOption
            {
                Key = ImportUnknownSets, Label = "ST-V sets Kronos does not run", Default = "ask",
                Values = new[] { "ask", "keep", "drop" }, Texts = new[] { "Asked at each import", "Kept", "Left out" },
                Hint = "Imported to Sega ST-V, the ST-V sets that are not in Kronos's list (in 2.7.5: wwshin) - another emulator of the platform "
                       + "may run them. Imported to Arcade, they are always left out.",
            },
        };

        public const string ImportSort = "ImportSort", ImportUnknownSets = "ImportUnknownSets";

        public static KronosOption Option(string key) => All.First(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase));

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

        /// <summary>A value of the game's file as it is (KronosGameOptions: raw TOML, case kept), null when absent.</summary>
        public static string GameRaw(string gameId, string key)
        {
            var path = GamePath(gameId);
            if (path == null) return null;
            return Read(path).TryGetValue(key, out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
        }

        /// <summary>The same written; null takes it out.</summary>
        public static void SetGameRaw(string gameId, string key, string value)
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
                var dll = typeof(KronosSettings).Assembly.Location;
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
