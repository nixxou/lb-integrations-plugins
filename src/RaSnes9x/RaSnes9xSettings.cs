// This plugin's settings: every game's in one key=value per line in
//
//     <plugin root>\.data\<PluginId>\settings.ini
//
// and a game's own beside it in games\<game id>.ini, the same keys - a key absent there follows every game's. The same
// files and rules as the Demul plugin's (DemulSettings). Read at the moment a setting is used, never cached: the Nixx
// window and a game's window write the files, the next launch sees them.
//
// THE OPTIONS are a table (All): each is a choice among a few values, with its default, written into snes9x.conf at
// every launch by RaSnes9xLaunch. The Nixx page and a game's window are built from the same table.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.RaSnes9x
{
    internal sealed class RaSnes9xOption
    {
        public string Key, Label, Hint;
        public string[] Values, Texts;    // the stored values, and what the lists show for them
        public string Default;

        public string TextOf(string value) { int i = Array.IndexOf(Values, value); return i < 0 ? value : Texts[i]; }
    }

    internal static class RaSnes9xSettings
    {
        /// <summary>manifest.json's PluginId.</summary>
        public const string PluginId = "d1494f7d-fb7a-4a0c-8f8a-9e05929527b1";

        /// <summary>For the probe: a settings file somewhere else than beside the install.</summary>
        internal static string PathOverride;

        public static readonly RaSnes9xOption[] All =
        {
            new RaSnes9xOption { Key = "Fullscreen", Label = "Screen", Default = "true",
                Values = new[] { "true", "false" }, Texts = new[] { "Full screen", "Windowed" },
                Hint = "Full screen as a borderless window over the whole screen (Snes9x's \"emulate fullscreen\") - quick to leave, "
                     + "and the RetroAchievements pop-ups draw over it." },
            new RaSnes9xOption { Key = "Aspect", Label = "Aspect", Default = "4:3",
                Values = new[] { "4:3", "8:7", "stretch" }, Texts = new[] { "4:3, as on a TV", "8:7, square pixels", "Stretched to the screen" },
                Hint = "4:3 is the picture a Super Nintendo gave on a television; 8:7 keeps the console's pixels square (256 x 224)." },
            new RaSnes9xOption { Key = "IntegerScaling", Label = "Integer scaling", Default = "false",
                Values = new[] { "true", "false" }, Texts = new[] { "On - whole multiples only", "Off" },
                Hint = "The picture's height scaled by a whole number only: every pixel the same size, with borders." },
            new RaSnes9xOption { Key = "Smooth", Label = "Smoothing", Default = "true",
                Values = new[] { "true", "false" }, Texts = new[] { "On (bilinear)", "Off - sharp pixels" },
                Hint = "Bilinear filtering of the stretched picture - Snes9x's default. Off gives sharp pixels, best with integer scaling." },
            new RaSnes9xOption { Key = "VSync", Label = "VSync", Default = "false",
                Values = new[] { "true", "false" }, Texts = new[] { "On - against tearing", "Off" },
                Hint = "Waits for the screen's refresh: no tearing, a little more latency. Off is Snes9x's default." },
            new RaSnes9xOption { Key = "Output", Label = "Video output", Default = "d3d",
                Values = new[] { "d3d", "opengl", "ddraw" }, Texts = new[] { "Direct3D (default)", "OpenGL", "DirectDraw" },
                Hint = "Snes9x's output method. Direct3D is its default and the one RetroAchievements' overlay is drawn with." },
        };

        public static RaSnes9xOption Option(string key) => All.First(o => o.Key == key);

        // ── every game's ──

        /// <summary>Every game's value of <paramref name="key"/>: what the Nixx window set, else the option's default.</summary>
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
                var dll = typeof(RaSnes9xSettings).Assembly.Location;
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
