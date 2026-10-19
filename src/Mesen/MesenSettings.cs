// This plugin's settings: every game's in one key=value per line in
//
//     <plugin root>\.data\<PluginId>\settings.ini
//
// and a game's own beside it in games\<game id>.ini, the same keys - a key absent there follows every game's. The same files
// and rules as BsnesSettings. Read at the moment a setting is used, never cached: the Nixx window and a game's window write the
// files, the next launch sees them.
//
// KEPT SIMPLE (Mehdi 06/10, "nixx-page-simple-per-game-rich"): the Nixx page holds the screen only - every other setting is
// Mesen's own, in its menus. A game's own choices (its system, and the options of its window) are the per-game window's.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Mesen
{
    internal sealed class MesenOption
    {
        public string Key, Label, Hint;
        public string[] Values, Texts;    // the stored values, and what the lists show for them
        public string Default;

        public string TextOf(string value) { int i = Array.IndexOf(Values, value); return i < 0 ? value : Texts[i]; }
    }

    internal static class MesenSettings
    {
        /// <summary>manifest.json's PluginId.</summary>
        public const string PluginId = "6823227e-f902-452b-a1b0-7591a7e55917";

        /// <summary>For the probe: a settings file somewhere else than beside the install.</summary>
        internal static string PathOverride;

        public static readonly MesenOption[] All =
        {
            new MesenOption { Key = "Fullscreen", Label = "Screen", Default = "true",
                Values = new[] { "true", "false" }, Texts = new[] { "Full screen", "Windowed" },
                Hint = "Full screen from the start (Mesen's --fullscreen, once the game is loaded). F11 switches in Mesen; Esc quits (bound by the plugin when free)." },
        };

        public static MesenOption Option(string key) => All.First(o => o.Key == key);

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

        /// <summary>The system the game's window chose for it (a MesenSystems key), or null: its platform decides.</summary>
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
