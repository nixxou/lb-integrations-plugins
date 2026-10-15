// This plugin's own settings: one key=value per line in
//
//     <plugin root>\.data\<PluginId>\settings.ini           every game's
//     <plugin root>\.data\<PluginId>\games\<game id>.ini    a game's own, the same keys; a key absent = every game's
//
// LaunchBox 14's place for a plugin's data, and the one the pack's uninstaller leaves alone - the same files and rules as
// the Demul plugin's (DemulSettings). Read at the moment a setting is used, never cached: the Nixx window writes them, the
// next launch sees them. The page is ZincExSettingsPage.cs, a game's window ZincExGameMenu.cs.
//
// WHAT THERE IS, ON PURPOSE LITTLE (Mehdi, 06/10: the Nixx page simple, the emulator's own settings are the emulator's):
// ZiNc EX keeps every picture, sound and control setting itself - globally and per game, in its own window - and the
// launch takes them as they are. The plugin only says how the window opens (ZiNc EX's --fullscreen / --windowed, for that
// launch only) and, per game, which of ZiNc's games a romset is when telling it by its content is not what is wanted.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.ZincEx
{
    internal static class ZincExSettings
    {
        /// <summary>manifest.json's PluginId.</summary>
        public const string PluginId = "5fee1a57-c678-4374-a692-d7c573a0e6fe";

        /// <summary>For the probe: a settings file somewhere else than beside the install.</summary>
        internal static string PathOverride;

        /// <summary>"fullscreen" (the default: --fullscreen), "windowed" (--windowed) or "zincex" (neither: ZiNc EX's own
        /// setting, Video > Full Screen and the game's in its Game Settings).</summary>
        public static string Screen
        {
            get { var v = Text(SettingsPath, "Screen"); return v == "windowed" || v == "zincex" ? v : "fullscreen"; }
        }
        public static void SetScreen(string value) => Write(SettingsPath, "Screen", value == "windowed" || value == "zincex" ? value : "fullscreen");

        public static string ScreenFor(string gameId)
        {
            var v = GameValue(gameId, "Screen");
            return v == "fullscreen" || v == "windowed" || v == "zincex" ? v : Screen;
        }

        /// <summary>The ZiNc set a game is started as, chosen in its window - null: told by the romset's content (ZincGames).</summary>
        public static string SetFor(string gameId)
        {
            var v = GameValue(gameId, "Set");
            return v != null && ZincGames.BySet(v) != null ? ZincGames.BySet(v).Set : null;
        }

        public static string GamePath(string gameId)
        {
            var safe = string.Concat((gameId ?? "").Where(c => char.IsLetterOrDigit(c) || c == '-'));
            return safe.Length == 0 ? null : Path.Combine(Path.GetDirectoryName(SettingsPath), "games", safe + ".ini");
        }

        /// <summary>The game's own value of <paramref name="key"/>, or null: it follows every game's.</summary>
        public static string GameValue(string gameId, string key)
        {
            var path = GamePath(gameId);
            return path == null ? null : Text(path, key);
        }

        /// <summary>The game's own value set, or taken away with null.</summary>
        public static void SetGameValue(string gameId, string key, string value)
        {
            var path = GamePath(gameId);
            if (path != null) Write(path, key, value);
        }

        public static string SettingsPath
        {
            get
            {
                if (PathOverride != null) return PathOverride;
                var dll = typeof(ZincExSettings).Assembly.Location;
                var pluginDir = Path.GetDirectoryName(dll);
                var root = Path.GetDirectoryName(pluginDir) ?? pluginDir;
                return Path.Combine(root, ".data", PluginId, "settings.ini");
            }
        }

        private static string Text(string path, string key)
            => Read(path).TryGetValue(key, out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim().ToLowerInvariant() : null;

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
