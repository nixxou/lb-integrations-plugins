// The options this pack passes to Xenia at launch - for every game (the Nixx window's Xenia tab) and for one game (its
// right-click "Nixx-Xenia : Options..."), the game's own winning over every game's.
//
// ALL ON THE COMMAND LINE, NEVER IN THE TOML (Mehdi, 01/10). Measured in Xenia's code (config.cc): a cvar given on the
// command line takes the command line's value for the run, and only the CONFIG value is written back to the TOML at
// exit - so what is passed here is never saved, and the next game, or the emulator opened on its own, is untouched.
// Each one goes as --name=value: cxxopts takes a bool as --flag or --flag=value, and the space-separated form would
// leave a stray value for the positional argument, which is the game.
//
// What Xenia runs on when nothing is passed is read off its TOML (XeniaToml) and shown as "Xenia's own".
//
// Stored like SUPER ZSNES's: one name=value per line in <plugin root>\.data\<PluginId>\settings.ini (every game) and
// games\<LaunchBox game id>.ini beside it (one game). A key present is passed; absent, Xenia's own value stays.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Xenia
{
    internal enum XeniaOptionKind { Bool, Choice, Int, Float }

    internal sealed class XeniaOption
    {
        public string Key;                  // the cvar - and the key in the .ini files
        public string Label, Group, Help;
        public XeniaOptionKind Kind;
        public (string Value, string Label)[] Choices = new (string, string)[0];
        public decimal Min, Max;
        /// <summary>The cvars it sets - itself, unless it sets two (the resolution scale sets x and y).</summary>
        public string[] Sends;
        /// <summary>Xenia's default, for when the TOML does not say (no file yet).</summary>
        public string Default;

        public string LabelOf(string value)
        {
            if (Kind == XeniaOptionKind.Bool) return XeniaSettings.IsTrue(value) ? "on" : "off";
            var c = Choices.FirstOrDefault(x => string.Equals(x.Value, value, StringComparison.OrdinalIgnoreCase));
            return c.Label ?? value;
        }
    }

    internal static class XeniaOptions
    {
        public static readonly XeniaOption[] All =
        {
            new XeniaOption { Group = "Graphics", Key = "gpu", Label = "Graphics backend", Kind = XeniaOptionKind.Choice, Default = "any",
                Choices = new[] { ("any", "Automatic"), ("d3d12", "Direct3D 12"), ("vulkan", "Vulkan") },
                Help = "Some games only run right on one of the two." },
            new XeniaOption { Group = "Graphics", Key = "draw_resolution_scale", Label = "Rendering resolution", Kind = XeniaOptionKind.Choice, Default = "1",
                Choices = new[] { ("1", "1x (native, 720p)"), ("2", "2x (1440p)"), ("3", "3x (2160p)") },
                Sends = new[] { "draw_resolution_scale_x", "draw_resolution_scale_y" },
                Help = "Renders the game at a multiple of its own resolution. The heavier on the GPU, the sharper." },
            new XeniaOption { Group = "Graphics", Key = "vsync", Label = "VSync", Kind = XeniaOptionKind.Bool, Default = "true" },
            new XeniaOption { Group = "Graphics", Key = "framerate_limit", Label = "Frame rate limit", Kind = XeniaOptionKind.Int, Min = 0, Max = 1000, Default = "0",
                Help = "Frames per second at most. 0 = no limit (60 with VSync on)." },
            new XeniaOption { Group = "Graphics", Key = "kernel_display_gamma_type", Label = "Display gamma", Kind = XeniaOptionKind.Choice, Default = "2",
                Choices = new[] { ("0", "Linear"), ("1", "sRGB (CRT)"), ("2", "BT.709 (HDTV)"), ("3", "Power (kernel_display_gamma_power)") } },

            new XeniaOption { Group = "Post-processing", Key = "postprocess_antialiasing", Label = "Anti-aliasing", Kind = XeniaOptionKind.Choice, Default = "none",
                Choices = new[] { ("none", "None"), ("fxaa", "FXAA"), ("fxaa_extreme", "FXAA, extreme quality") } },
            new XeniaOption { Group = "Post-processing", Key = "postprocess_scaling_and_sharpening", Label = "Resampling and sharpening", Kind = XeniaOptionKind.Choice, Default = "bilinear",
                Choices = new[] { ("bilinear", "None / bilinear"), ("cas", "AMD FidelityFX CAS"), ("fsr", "AMD FidelityFX FSR 1.0") } },
            new XeniaOption { Group = "Post-processing", Key = "postprocess_ffx_cas_additional_sharpness", Label = "CAS additional sharpness", Kind = XeniaOptionKind.Float, Min = 0, Max = 1, Default = "0" },
            new XeniaOption { Group = "Post-processing", Key = "postprocess_ffx_fsr_sharpness_reduction", Label = "FSR sharpness reduction", Kind = XeniaOptionKind.Float, Min = 0, Max = 2, Default = "0.2",
                Help = "In stops: 0 is the sharpest." },
            new XeniaOption { Group = "Post-processing", Key = "postprocess_dither", Label = "Dithering", Kind = XeniaOptionKind.Bool, Default = "true",
                Help = "Dither the final output to 8 bits per channel so gradients are smoother." },
            new XeniaOption { Group = "Post-processing", Key = "present_letterbox", Label = "Keep the aspect ratio", Kind = XeniaOptionKind.Bool, Default = "true",
                Help = "Off stretches the picture to the window." },

            new XeniaOption { Group = "Input and audio", Key = "vibration", Label = "Controller vibration", Kind = XeniaOptionKind.Bool, Default = "true" },
            new XeniaOption { Group = "Input and audio", Key = "mute", Label = "Mute", Kind = XeniaOptionKind.Bool, Default = "false" },
            new XeniaOption { Group = "Input and audio", Key = "apu", Label = "Audio backend", Kind = XeniaOptionKind.Choice, Default = "any",
                Choices = new[] { ("any", "Automatic"), ("xaudio2", "XAudio2"), ("sdl", "SDL"), ("nop", "None (silent)") } },
        };

        public static XeniaOption ByKey(string key) => All.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase));

        /// <summary>What Xenia runs on for each option when nothing is passed: its TOML, else its default. Keyed by option.</summary>
        public static Dictionary<string, string> Own(string configFile)
        {
            var toml = XeniaToml.ReadAll(configFile);
            var own = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in All)
            {
                var cvar = (o.Sends ?? new[] { o.Key })[0];
                if (!toml.TryGetValue(cvar, out var v)) { own[o.Key] = o.Default; continue; }
                // The two post-processing strings are empty in a fresh TOML: Xenia reads anything unlisted as none / bilinear.
                if (v.Length == 0 || (o.Kind == XeniaOptionKind.Choice && o.Choices.All(c => !string.Equals(c.Value, v, StringComparison.OrdinalIgnoreCase)) && o.Key.StartsWith("postprocess_")))
                    v = o.Choices[0].Value;
                // A double as Xenia writes it (0.20000000298023224) shown as the number it is.
                if (o.Kind == XeniaOptionKind.Float && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    v = Math.Round(d, 4).ToString("0.####", CultureInfo.InvariantCulture);
                own[o.Key] = v;
            }
            return own;
        }
    }

    internal static class XeniaSettings
    {
        /// <summary>manifest.json's PluginId.</summary>
        public const string PluginId = "8d266d9f-aa30-4036-9cf0-cf8398840658";

#pragma warning disable CS0649
        /// <summary>For the probe: the settings somewhere else (a folder). Set by reflection.</summary>
        internal static string DirOverride;
#pragma warning restore CS0649

        public static string Dir
        {
            get
            {
                if (DirOverride != null) return DirOverride;
                var dll = typeof(XeniaSettings).Assembly.Location;
                var root = Path.GetDirectoryName(Path.GetDirectoryName(dll)) ?? Path.GetDirectoryName(dll);
                return Path.Combine(root, ".data", PluginId);
            }
        }

        public static string SettingsPath => Path.Combine(Dir, "settings.ini");

        public static string GamePath(string gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId)) return null;
            var safe = new string(gameId.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            return Path.Combine(Dir, "games", safe + ".ini");
        }

        public static Dictionary<string, string> Read() => Read(SettingsPath);

        public static Dictionary<string, string> ReadGame(string gameId)
        {
            var path = GamePath(gameId);
            return path == null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : Read(path);
        }

        public static void WriteAll(IDictionary<string, string> values)
            => WriteTo(SettingsPath, values, "# Xenia: what the pack passes on the command line for every game. Edited by the Nixx window.");

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
            WriteTo(path, values, "# Xenia: this game's own options, passed on its command line at launch. Edited by its options window.");
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
                    var key = line.Substring(0, at).Trim();
                    // A key no option has any more is dropped on read: never sent, gone at the next write.
                    if (XeniaOptions.ByKey(key) != null) values[key] = line.Substring(at + 1).Trim();
                }
            }
            catch (Exception ex) { Log.Warn("could not read " + path, ex); }
            return values;
        }

        private static void WriteTo(string path, IDictionary<string, string> values, string header)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var lines = new List<string> { header, "# A key present is passed; remove the line to leave Xenia's own value." };
            lines.AddRange(values.Where(kv => !string.IsNullOrEmpty(kv.Value))
                                 .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                                 .Select(kv => kv.Key + "=" + kv.Value));
            var tmp = path + ".tmp";
            File.WriteAllLines(tmp, lines);
            File.Move(tmp, path, overwrite: true);
            Log.Info("options written: " + (lines.Count - 2) + " set -> " + path);
        }

        // ── the command line ─────────────────────────────────────────────────

        /// <summary>Every game's values with this game's over them.</summary>
        public static Dictionary<string, string> ForGame(string gameId)
        {
            var values = Read();
            foreach (var kv in ReadGame(gameId)) values[kv.Key] = kv.Value;
            return values;
        }

        /// <summary>The flags these values ask for, in catalogue order.</summary>
        public static List<string> Flags(IDictionary<string, string> values)
        {
            var flags = new List<string>();
            foreach (var o in XeniaOptions.All)
            {
                if (!values.TryGetValue(o.Key, out var v) || string.IsNullOrWhiteSpace(v)) continue;
                var value = Normalise(o, v.Trim());
                foreach (var cvar in o.Sends ?? new[] { o.Key }) flags.Add("--" + cvar + "=" + value);
            }
            return flags;
        }

        /// <summary>The current command line, plus every flag whose cvar is not on it already - one the user typed on
        /// the emulator stays his.</summary>
        public static string Append(string current, IEnumerable<string> flags)
        {
            var sb = new StringBuilder((current ?? "").Trim());
            foreach (var f in flags)
            {
                var name = f.Substring(0, f.IndexOf('=') + 1);
                if (sb.ToString().IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0
                    || sb.ToString().IndexOf(name.TrimEnd('=') + " ", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(f);
            }
            return sb.ToString();
        }

        private static string Normalise(XeniaOption o, string v)
        {
            switch (o.Kind)
            {
                case XeniaOptionKind.Bool: return IsTrue(v) ? "true" : "false";
                case XeniaOptionKind.Int:
                    return long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i.ToString(CultureInfo.InvariantCulture) : v;
                case XeniaOptionKind.Float:
                    return double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d.ToString(CultureInfo.InvariantCulture) : v;
                default: return v;
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
    }
}
