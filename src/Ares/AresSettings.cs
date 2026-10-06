// This plugin's settings: every game's in one key=value per line in
//
//     <plugin root>\.data\<PluginId>\settings.ini
//
// and a game's own beside it in games\<game id>.ini, the same keys - a key absent there follows every game's. The same
// files and rules as the Demul plugin's (DemulSettings). Read at the moment a setting is used, never cached: the Nixx
// window and a game's window write the files, the next launch sees them.
//
// THE OPTIONS are a table (All): each is a choice among a few values, with its default, written into settings.bml at
// every launch by AresLaunch (full screen: ares's --fullscreen / --pseudofullscreen). A game's SYSTEM is its own key, kept as
// it is written - ares matches names exactly. The Nixx page and a game's window are built from the same table.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Ares
{
    internal sealed class AresOption
    {
        public string Key, Label, Hint;
        public string[] Values, Texts;    // the stored values, and what the lists show for them
        public string Default;

        public string TextOf(string value) { int i = Array.IndexOf(Values, value); return i < 0 ? value : Texts[i]; }
    }

    internal static class AresSettings
    {
        /// <summary>manifest.json's PluginId.</summary>
        public const string PluginId = "531ec195-8bd6-4df5-acc0-645dd8a71645";

        /// <summary>For the probe: a settings file somewhere else than beside the install.</summary>
        internal static string PathOverride;

        public static readonly AresOption[] All =
        {
            new AresOption { Key = "Screen", Label = "Screen", Default = "full",
                Values = new[] { "full", "pseudo", "windowed" }, Texts = new[] { "Full screen", "Borderless window over the screen", "Windowed" },
                Hint = "Full screen is ares's --fullscreen; the borderless window (--pseudofullscreen) covers the screen without changing its mode." },
            new AresOption { Key = "Output", Label = "Scaling", Default = "scale",
                Values = new[] { "scale", "integer", "stretch" }, Texts = new[] { "Fill the screen, keep the shape", "Integer only", "Stretch to the screen" },
                Hint = "ares's Video Output: Scale fills the screen keeping the aspect; Integer scales by whole numbers only, every pixel the same size; "
                     + "Stretch fills it all." },
            new AresOption { Key = "Aspect", Label = "Aspect", Default = "standard",
                Values = new[] { "standard", "none" }, Texts = new[] { "As on a TV (aspect correction)", "Square pixels" },
                Hint = "ares's Aspect Correction: Standard gives the picture the console gave on a television; None keeps its pixels square." },
            new AresOption { Key = "VSync", Label = "VSync", Default = "false",
                Values = new[] { "true", "false" }, Texts = new[] { "On - against tearing", "Off" },
                Hint = "ares's video Blocking: waits for the screen's refresh. Off is ares's default." },
            new AresOption { Key = "Driver", Label = "Video driver", Default = "opengl",
                Values = new[] { "opengl", "d3d" }, Texts = new[] { "OpenGL 3.2 (default)", "Direct3D 9.0" },
                Hint = "ares's video driver. OpenGL is its default on Windows and the one its shaders need; Direct3D 9 for a card OpenGL troubles." },
            new AresOption { Key = "Region", Label = "Preferred region", Default = "ntsc-u",
                Values = new[] { "ntsc-u", "ntsc-j", "pal" }, Texts = new[] { "NTSC-U (America)", "NTSC-J (Japan)", "PAL (Europe)" },
                Hint = "ares's Boot Prefer: the region a console starts as when the game runs on several - and the BIOS it then takes." },
            new AresOption { Key = "FastBoot", Label = "Fast boot", Default = "false",
                Values = new[] { "true", "false" }, Texts = new[] { "On - skip the BIOS intro", "Off" },
                Hint = "ares's Boot Fast: straight to the game, past the console's logo where ares can." },
        };

        /// <summary>A game's own system ("Mega CD 32X"), or null: its platform's. Kept as written: ares matches it exactly.</summary>
        public static string GameSystem(string gameId)
        {
            var path = GamePath(gameId);
            if (path == null) return null;
            return Read(path).TryGetValue("System", out var v) && AresSystems.System(v?.Trim()) != null ? v.Trim() : null;
        }

        public static void SetGameSystem(string gameId, string system)
        {
            var path = GamePath(gameId);
            if (path != null) Write(path, "System", string.IsNullOrWhiteSpace(system) ? null : system.Trim());
        }
        public static AresOption Option(string key) => All.First(o => o.Key == key);

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
                var dll = typeof(AresSettings).Assembly.Location;
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
