// What the user chose in the pack's window, and the command line it becomes.
//
// One key=value per line in <plugin root>\.data\<PluginId>\settings.ini - LaunchBox 14's place for
// a plugin's data, the one the pack's uninstaller leaves alone, same rule as Vita3k. A key present
// means "override this"; absent means "leave the emulator's own value". Keys are the catalogue's
// IniKey: "setting.srmPath", "game.overclock", "unity.screen-fullscreen", "plugin.menu-key",
// "native.loadstate".
//
// Read at the moment it is used, never cached: the window writes the file and the next launch
// sees it, no restart.
//
// THE LINE. Each family renders its own way (SuperZsnesOptions has the table), and each flag knows
// its NAME apart from its value, so Append can refuse to add a flag whose name is already on the
// emulator's command line - a user who typed -screen-fullscreen 0 by hand keeps it. A value with
// a space is one quoted argument; Windows unquotes it before the process sees it, and the emulator
// ignores any argument starting with "-" that it does not know (read off MasterExecutor.Awake).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.SuperZsnes
{
    internal sealed class Flag
    {
        /// <summary>What identifies the flag on a line, so it is not sent twice: "--nixx-set:srmPath=",
        /// "-screen-fullscreen", "--loadstate".</summary>
        public string Name;
        /// <summary>The whole thing as it goes on the line, quoted when it has to be.</summary>
        public string Text;
        public override string ToString() => Text;
    }

    internal static class SuperZsnesSettings
    {
        /// <summary>manifest.json's PluginId.</summary>
        public const string PluginId = "3f6c1a2e-9b7d-4e58-a1c4-7d2f0b9e6a51";

        /// <summary>For the probe: a settings file somewhere else than beside the install. Set by
        /// reflection from outside this assembly, hence the pragma.</summary>
#pragma warning disable CS0649
        internal static string PathOverride;
#pragma warning restore CS0649

        public static string SettingsPath
        {
            get
            {
                if (PathOverride != null) return PathOverride;
                var dll = typeof(SuperZsnesSettings).Assembly.Location;
                var pluginDir = Path.GetDirectoryName(dll);
                var root = Path.GetDirectoryName(pluginDir) ?? pluginDir;
                return Path.Combine(root, ".data", PluginId, "settings.ini");
            }
        }

        /// <summary>Is the in-process plugin wanted at all? The one catalogue entry read on the
        /// LaunchBox side rather than sent: "plugin.bepinex", on unless the window turned it off.</summary>
        public static bool BepInExWanted
        {
            get
            {
                var v = Read().TryGetValue("plugin.bepinex", out var s) ? s : null;
                return v == null || IsTrue(v);
            }
        }

        public static Dictionary<string, string> Read() => Read(SettingsPath);

        /// <summary>A game's own options (its right-click window): <c>games\&lt;LaunchBox game id&gt;.ini</c> beside
        /// settings.ini, the same format. Null for no id.</summary>
        public static string GamePath(string gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId)) return null;
            var safe = new string(gameId.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            return Path.Combine(Path.GetDirectoryName(SettingsPath), "games", safe + ".ini");
        }

        public static Dictionary<string, string> ReadGame(string gameId)
        {
            var path = GamePath(gameId);
            return path == null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : Read(path);
        }

        /// <summary>A game's own options written - the file removed when there are none.</summary>
        public static void WriteGame(string gameId, IDictionary<string, string> values)
        {
            var path = GamePath(gameId);
            if (path == null) return;
            if (values == null || values.All(kv => string.IsNullOrEmpty(kv.Value)))
            {
                if (File.Exists(path)) { File.Delete(path); Log.Info("game options of " + gameId + ": none any more"); }
                return;
            }
            WriteTo(path, values, "# SUPER ZSNES: this game's own options, passed on its command line at launch. Edited by its options window.");
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
            catch (Exception ex) { Log.Warn("could not read the settings", ex); }
            return values;
        }

        /// <summary>Replace the whole file - through a temporary one, so a failure halfway leaves
        /// the previous settings. Null or empty values mean "not overridden" and are dropped.</summary>
        public static void WriteAll(IDictionary<string, string> values)
            => WriteTo(SettingsPath, values, "# SUPER ZSNES: what the pack passes on the emulator's command line for every game. Edited by the Nixx window.");

        private static void WriteTo(string path, IDictionary<string, string> values, string header)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var lines = new List<string> { header, "# A key present is passed; remove the line to leave the emulator's own value (or the pack's default)." };
            lines.AddRange(values.Where(kv => !string.IsNullOrEmpty(kv.Value))
                                 .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                                 .Select(kv => kv.Key + "=" + kv.Value));
            var tmp = path + ".tmp";
            File.WriteAllLines(tmp, lines);
            File.Move(tmp, path, overwrite: true);
            Log.Info("settings written: " + (lines.Count - 2) + " override(s) -> " + path);
        }

        public static void Set(string iniKey, string value)
        {
            var values = Read();
            if (string.IsNullOrEmpty(value)) values.Remove(iniKey); else values[iniKey] = value;
            WriteAll(values);
        }

        // ── the command line ─────────────────────────────────────────────────

        /// <summary>What a launch of this game passes: the pack's own settings (settings.ini, the Global options only)
        /// and the game's (its file, the Game options only), in catalogue order. A key of the wrong scope - left by an
        /// older version, or typed by hand - is not sent.</summary>
        public static List<Flag> ForLaunch(string gameId)
        {
            var flags = Flags(Read(), OptionScope.Global);
            flags.AddRange(Flags(ReadGame(gameId), OptionScope.Game));
            return flags;
        }

        /// <summary>Every flag these values ask for, whatever the scope - the renderer, as the probe tests it.</summary>
        public static List<Flag> Flags(IDictionary<string, string> values) => Flags(values, null);

        public static List<Flag> Flags(IDictionary<string, string> values, OptionScope? scope)
        {
            var flags = new List<Flag>();
            foreach (var o in SuperZsnesOptions.All)
            {
                if (scope != null && o.Scope != scope) continue;
                if (!values.TryGetValue(o.IniKey, out var value) || string.IsNullOrEmpty(value)) continue;
                var flag = Render(o, value);
                if (flag != null) flags.Add(flag);
            }
            return flags;
        }

        /// <summary>One option as it goes on the line, or null when its value says "nothing".</summary>
        internal static Flag Render(Option o, string value)
        {
            value = value.Trim();
            switch (o.Family)
            {
                case OptionFamily.Setting:
                    return new Flag { Name = "--nixx-set:" + o.Key + "=", Text = Quote("--nixx-set:" + o.Key + "=" + Normalise(o, value)) };
                case OptionFamily.Game:
                    return new Flag { Name = "--nixx-game:" + o.Key + "=", Text = Quote("--nixx-game:" + o.Key + "=" + Normalise(o, value)) };
                case OptionFamily.Plugin:
                    // "display" is a switch with one meaning: on is --nixx-display=primary, off is nothing.
                    if (o.Key == "display")
                        return IsTrue(value) ? new Flag { Name = "--nixx-display=", Text = "--nixx-display=primary" } : null;
                    // "bepinex" is read by the LaunchBox side (deploy or not) and never sent.
                    if (o.Key == "bepinex") return null;
                    // "log" is a bare switch: --nixx-log or nothing.
                    if (o.Key == "log")
                        return IsTrue(value) ? new Flag { Name = "--nixx-log", Text = "--nixx-log" } : null;
                    return new Flag { Name = "--nixx-" + o.Key + "=", Text = Quote("--nixx-" + o.Key + "=" + (o.Kind == OptionKind.Bool ? (IsTrue(value) ? "on" : "off") : value)) };
                case OptionFamily.Native:
                    // Presence is the value: "--loadstate" or nothing.
                    return IsTrue(value) ? new Flag { Name = "--" + o.Key, Text = "--" + o.Key } : null;
                case OptionFamily.Unity:
                    if (o.Kind == OptionKind.Bool)
                        return IsTrue(value) ? new Flag { Name = "-" + o.Key, Text = "-" + o.Key } : null;
                    if (o.Key == "screen-fullscreen")
                        return new Flag { Name = "-screen-fullscreen", Text = "-screen-fullscreen " + (value.Equals("windowed", StringComparison.OrdinalIgnoreCase) || value == "0" ? "0" : "1") };
                    return new Flag { Name = "-" + o.Key, Text = "-" + o.Key + " " + value };
            }
            return null;
        }

        /// <summary>Booleans as true/false, numbers with a dot, the rest as typed.</summary>
        private static string Normalise(Option o, string value)
        {
            switch (o.Kind)
            {
                case OptionKind.Bool: return IsTrue(value) ? "true" : "false";
                case OptionKind.Int:
                    return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i.ToString(CultureInfo.InvariantCulture) : value;
                case OptionKind.Float:
                    return double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d.ToString(CultureInfo.InvariantCulture) : value;
                default: return value;
            }
        }

        internal static bool IsTrue(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "1": case "on": case "true": case "yes": return true;
                default: return false;
            }
        }

        private static string Quote(string token)
            => token.IndexOf(' ') >= 0 || token.IndexOf('\t') >= 0 ? "\"" + token.Replace("\"", "\\\"") + "\"" : token;

        /// <summary>The flags joined, for the window to show.</summary>
        public static string CommandLine(IDictionary<string, string> values)
            => string.Join(" ", Flags(values).Select(f => f.Text));

        /// <summary>The emulator's current command line plus every flag whose NAME is not already on
        /// it. What PrepareEmulatorForLaunch hands back; the host appends the ROM after it.</summary>
        public static string Append(string current, IEnumerable<Flag> flags)
        {
            var sb = new StringBuilder((current ?? "").Trim());
            foreach (var f in flags)
            {
                if (sb.ToString().IndexOf(f.Name, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(f.Text);
            }
            return sb.ToString();
        }
    }
}
