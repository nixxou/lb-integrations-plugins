// The options a game is launched with (Mehdi, 04/10), CxbxOptions' shape: for every game in the Nixx window, for one game in
// its right-click menu - the game's own over every game's, over the plugin's default, over xemu's own setting.
//
// TWO KINDS. xemu's own settings, chosen from its config_spec.yml (v0.8.136: the list of every setting, its type, values and
// default) - the ones that make sense game by game: renderer, internal resolution, filtering, vsync, fit, aspect, memory, AV
// pack, FPU, shader cache, DSP, HRTF, the menu bar. They go into the session's xemu.toml (XemuSessionConfig), the user's file
// left as it is. And the plugin's: the console (region, video standard, language, time zone, HDD key - Eeprom\XemuEeprom) and
// the disc (the media patch - XboxMediaPatch). Left out on purpose: pads and keys (the user's, kept whole), paths, the network
// (one for every game), the debug windows.
//
// xemu's own settings are a game's only (Mehdi, 05/10): for every game, xemu's own window - the Nixx window shows the console
// and the disc alone.
// STORED as "opt.<key>" in settings.ini (every game) and games\<game id>.ini (one game), XemuSettings. An option unset is not
// written: xemu's own value - its window's - stays. "xemu" sets it back to that for an option the plugin sets by default.

using System;
using System.Collections.Generic;
using System.Linq;

namespace LbIntegrations.Xemu
{
    internal sealed class XemuOption
    {
        public string Key;                       // "display.renderer", "console.region"
        public string Group;
        public string Label;
        /// <summary>On or off ("on" / "off"): a three-state box. Otherwise a list of Choices.</summary>
        public bool Bool;
        public List<(string Value, string Label)> Choices = new List<(string, string)>();
        /// <summary>What applies when neither the game nor every game sets it - null: xemu's own.</summary>
        public string Default;
        public string Help;
        /// <summary>For xemu's settings: the table and key in xemu.toml, the value's TOML kind, xemu's own default (config_spec.yml).
        /// Null table: the plugin's own option.</summary>
        public string Table, TomlKey;
        public TomlKind Kind;
        public string XemuDefault;

        public bool IsXemuSetting => Table != null;

        public string LabelOf(string value)
            => Bool ? (value == "on" || value == "true" ? "on" : "off") : Choices.FirstOrDefault(c => c.Value == value).Label ?? value;

        /// <summary>A value of this option as TOML: true / false, 'TEXT', or a number.</summary>
        public string ToToml(string value)
            => Bool ? (value == "on" ? "true" : "false") : Kind == TomlKind.String ? XemuToml.Literal(value) : value;

        /// <summary>A raw TOML value read back as this option's value - "on" / "off", the text, the number.</summary>
        public string FromToml(string raw)
        {
            if (raw == null) return null;
            raw = raw.Trim();
            int hash = raw.IndexOf('#');
            if (hash > 0 && raw[0] != '\'' && raw[0] != '"') raw = raw.Substring(0, hash).Trim();
            if (Bool) return raw == "true" ? "on" : raw == "false" ? "off" : null;
            return XemuToml.Text(raw);
        }
    }

    internal enum TomlKind { Bool, String, Integer }

    internal static class XemuOptions
    {
        public const string Prefix = "opt.";
        /// <summary>For an option the plugin sets by default: the way back to xemu's own - written as nothing.</summary>
        public const string OwnValue = "xemu";
        private static readonly (string, string) Own = (OwnValue, "xemu's own");

        private static XemuOption X(string key, string group, string label, string table, string tomlKey, TomlKind kind, string xemuDefault, params (string, string)[] choices)
        {
            var o = new XemuOption { Key = key, Group = group, Label = label, Table = table, TomlKey = tomlKey, Kind = kind, XemuDefault = xemuDefault, Bool = kind == TomlKind.Bool };
            o.Choices.AddRange(choices);
            return o;
        }

        private static XemuOption WithDefault(XemuOption o, string value, string help) { o.Default = value; o.Help = help; return o; }

        /// <summary>The graphics card Vulkan renders on - the machine's, by the names Vulkan gives them (the drivers' own, as
        /// Windows lists its display adapters). Measured 04/10: on a machine with an RTX 3060 and an RTX 3050, xemu chose the 3050
        /// by itself. Virtual adapters (Parsec, a remote display) left out.</summary>
        private static XemuOption Gpu()
        {
            var o = X("display.gpu", "Picture", "Graphics card (Vulkan)", "display.vulkan", "preferred_physical_device", TomlKind.String, "");
            foreach (var name in DisplayAdapters()) o.Choices.Add((name, name));
            o.Help = "Vulkan only. xemu's choice is the first card it finds - on a machine with two, not always the faster one.";
            return o;
        }

        /// <summary>The display adapters in the machine now (EnumDisplayDevices - not the registry, which keeps every card the machine
        /// ever had), virtual ones left out.</summary>
        internal static List<string> DisplayAdapters()
        {
            var names = new List<string>();
            try
            {
                for (uint i = 0; i < 32; i++)
                {
                    var d = new DisplayDevice { cb = System.Runtime.InteropServices.Marshal.SizeOf<DisplayDevice>() };
                    if (!EnumDisplayDevices(null, i, ref d, 0)) break;
                    var desc = (d.DeviceString ?? "").Trim();
                    var low = desc.ToLowerInvariant();
                    if (desc.Length == 0 || low.Contains("virtual") || low.Contains("parsec") || low.Contains("remote") || low.Contains("basic display") || low.Contains("basic render")) continue;
                    if (!names.Contains(desc, StringComparer.OrdinalIgnoreCase)) names.Add(desc);
                }
            }
            catch { }
            return names;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private struct DisplayDevice
        {
            public int cb;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "EnumDisplayDevicesW")]
        private static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice info, uint flags);

        public static readonly List<XemuOption> All = new List<XemuOption>
        {
            // ── picture ──
            X("display.renderer", "Picture", "Renderer", "display", "renderer", TomlKind.String, "OPENGL", ("OPENGL", "OpenGL"), ("VULKAN", "Vulkan")),
            X("display.surface_scale", "Picture", "Internal resolution", "display.quality", "surface_scale", TomlKind.Integer, "1",
              ("1", "1x (native, 640x480)"), ("2", "2x"), ("3", "3x"), ("4", "4x"), ("6", "6x"), ("8", "8x")),
            Gpu(),
            X("display.filtering", "Picture", "Filtering", "display", "filtering", TomlKind.String, "linear", ("linear", "Linear (smooth)"), ("nearest", "Nearest (sharp pixels)")),
            X("display.vsync", "Picture", "VSync", "display.window", "vsync", TomlKind.Bool, "true"),
            X("display.fit", "Picture", "Fit to the window", "display.ui", "fit", TomlKind.String, "scale", ("center", "Center"), ("scale", "Scale"), ("stretch", "Stretch")),
            X("display.aspect", "Picture", "Aspect ratio", "display.ui", "aspect_ratio", TomlKind.String, "auto", ("native", "Native"), ("auto", "Auto"), ("4x3", "4:3"), ("16x9", "16:9")),
            WithDefault(X("display.menubar", "Picture", "Menu bar", "display.ui", "show_menubar", TomlKind.Bool, "true"), "off",
                        "Hidden by default: a game launched from LaunchBox shows no Machine / View / Debug / Help bar over its picture."),

            // ── system ──
            X("sys.mem_limit", "System", "Memory", "sys", "mem_limit", TomlKind.String, "64", ("64", "64 MB (a retail Xbox)"), ("128", "128 MB (a debug kit)")),
            X("sys.avpack", "System", "AV cable", "sys", "avpack", TomlKind.String, "hdtv",
              ("hdtv", "HDTV (component: 480p, 720p, 1080i)"), ("composite", "Composite"), ("svideo", "S-Video"), ("scart", "SCART"), ("vga", "VGA"), ("rfu", "RF"), ("none", "None")),

            // ── performance ──
            X("perf.hard_fpu", "Performance", "Hardware FPU", "perf", "hard_fpu", TomlKind.Bool, "true"),
            X("perf.cache_shaders", "Performance", "Cache shaders", "perf", "cache_shaders", TomlKind.Bool, "true"),

            // ── sound ──
            X("audio.use_dsp", "Sound", "DSP emulation", "audio", "use_dsp", TomlKind.Bool, "false"),
            X("audio.hrtf", "Sound", "HRTF (3D sound)", "audio", "hrtf", TomlKind.Bool, "true"),

            // ── the console (Eeprom\XemuEeprom) ──
            new XemuOption { Key = "console.region", Group = "Console", Label = "Region", Default = "follow",
                             Choices = { ("follow", "The game's region"), ("1", "North America (NTSC)"), ("2", "Japan"), ("4", "Europe / rest of the world (PAL)"), Own },
                             Help = "The game's: the console takes a region the game accepts - an Xbox refuses a game of another region (a black screen)." },
            new XemuOption { Key = "console.video", Group = "Console", Label = "Video standard", Default = "follow",
                             Choices = { ("follow", "Follow the console's region"), ("pal50", "PAL 50 Hz"), ("pal60", "PAL 60 Hz"), ("ntsc", "NTSC"), ("ntsc-hd", "NTSC + 480p/720p/1080i"), Own },
                             Help = "Follow: a European console gets PAL with 60 Hz allowed, an American or Japanese one NTSC with its HD modes." },
            new XemuOption { Key = "console.language", Group = "Console", Label = "Language", Default = "windows",
                             Choices = { ("windows", "Your console's language (else Windows')"), ("1", "English"), ("4", "French"), ("3", "German"), ("5", "Spanish"),
                                         ("6", "Italian"), ("9", "Portuguese"), ("2", "Japanese"), ("7", "Korean"), ("8", "Chinese"), Own } },
            new XemuOption { Key = "console.timezone", Group = "Console", Label = "Time zone", Default = "windows", Choices = { ("windows", "Windows' time zone"), Own } },
            new XemuOption { Key = "console.hddkey", Group = "Console", Label = "Console identity", Default = "pack",
                             Choices = { ("pack", "Your console's (from its seed)"), Own },
                             Help = "The serial number, MAC address, HDD key and online key - made from the seed of \"Your console\", the same "
                                    + "on xemu and Cxbx-Reloaded (with no seed yet: the HDD key alone, the pack's). A save carries the HDD key it "
                                    + "was made with, and its game is launched with that one. xemu's own: its EEPROM as it is." },
            new XemuOption { Key = "console.certkey", Group = "Console", Label = "Certificate key", Default = "retail",
                             Choices = { ("retail", "A real Xbox's"), ("zero", "Zero - Cxbx-Reloaded's without keys.bin") },
                             Help = "For a game with no save yet: a save carries the keys it was made with, and its game is launched with them "
                                    + "whatever is set here. The key a game signs its saves with comes from it; zero is Cxbx-Reloaded's. Made on "
                                    + "a copy of your flash BIOS for the session - your file is never written." },

            // ── the disc (XboxMediaPatch) ──
            new XemuOption { Key = "disc.media_patch", Group = "Disc", Label = "Media patch", Bool = true, Default = "on",
                             Help = "extract-xiso's patch, made on the fly in the disc's .xbe files: some games stay on a black screen without it "
                                    + "(Batman: Rise of Sin Tzu). Your files are never written." },
        };

        public static XemuOption Find(string key) => All.FirstOrDefault(o => o.Key == key);

        /// <summary>The value saved for <paramref name="key"/> at one level, or null.</summary>
        public static string Get(IDictionary<string, string> s, string key)
            => s != null && s.TryGetValue(Prefix + key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        /// <summary>What a game is launched with, key by key - the game's, else every game's, else the plugin's default; an option
        /// with none of them (xemu's own) is absent, and so is one set to "xemu".</summary>
        public static Dictionary<string, string> Effective(string gameId)
        {
            var every = XemuSettings.Read();
            var game = gameId == null ? null : XemuSettings.ReadGame(gameId);
            var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in All)
            {
                // xemu's own settings have no every-game level (Mehdi, 05/10): for every game, xemu's own window sets them - an
                // "opt." written for every game before that is left in the file, and not used.
                var value = Get(game, o.Key) ?? (o.IsXemuSetting ? null : Get(every, o.Key)) ?? o.Default;
                if (value != null && value != OwnValue) v[o.Key] = value;
            }
            return v;
        }

        /// <summary>The xemu settings of <paramref name="effective"/>, as (table, key, TOML value) for the session's file.</summary>
        public static IEnumerable<(string Table, string Key, string Value)> TomlOf(IDictionary<string, string> effective)
        {
            foreach (var o in All.Where(o => o.IsXemuSetting))
                if (effective.TryGetValue(o.Key, out var v) && (o.Bool ? (v == "on" || v == "off") : o.Choices.Any(c => c.Value == v)))
                    yield return (o.Table, o.TomlKey, o.ToToml(v));
        }

        /// <summary>What xemu itself is set to for <paramref name="o"/>: its xemu.toml's value, else its default (config_spec.yml).</summary>
        public static string OwnOf(XemuOption o, XemuTomlDoc userToml)
            => !o.IsXemuSetting ? null : o.FromToml(userToml?.Get(o.Table, o.TomlKey)) ?? o.FromToml(o.Bool ? o.XemuDefault : "'" + o.XemuDefault + "'");
    }
}
