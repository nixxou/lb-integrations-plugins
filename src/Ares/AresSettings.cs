// This plugin's settings: every game's in one key=value per line in
//
//     <plugin root>\.data\<PluginId>\settings.ini
//
// and a game's own beside it in games\<game id>.ini, the same keys - a key absent there follows every game's. The same
// files and rules as the Demul plugin's (DemulSettings). Read at the moment a setting is used, never cached: the Nixx
// window and a game's window write the files, the next launch sees them.
//
// THE OPTIONS are a table (All): each is a choice among a few values, with its default and the settings.bml node it
// sets, given on ares's command line as --setting "<node>=<value>" (SettingArgs; full screen: --fullscreen /
// --pseudofullscreen) - ares puts the file back as it was when it quits, so nothing a game chose stays for the next.
// Most offer "as set in ares": then nothing is passed and ares's own menus decide. Some only mean something for a few systems (Systems) - a game's
// window shows those of its system alone. A game's SYSTEM is its own key, kept as it is written - ares matches names
// exactly. The Nixx page and a game's window are built from the same table.

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
        public string Group;              // the card it shows in
        public string Bml;                // the settings.bml node it writes - null: AresLaunch writes it (Screen, Output)
        public string[] Writes;           // what goes into that node for each value - null: the value itself
        public string[] Systems;          // the systems it means anything for - null: every one
        public bool Free;                 // a value outside Values is kept (the shader: a file of the install, as written)
        public bool OnPage;               // set for every game on the Nixx page; the others are a game's alone, else ares's own

        public string TextOf(string value) { int i = Array.IndexOf(Values, value); return i < 0 ? ShaderText(value) : Texts[i]; }

        public bool AppliesTo(string system) => Systems == null || system == null || Systems.Contains(system);

        internal static string ShaderText(string value) => value != null && value.EndsWith(".slangp", StringComparison.OrdinalIgnoreCase) ? value.Substring(0, value.Length - 7) : value;
    }

    internal static class AresSettings
    {
        /// <summary>manifest.json's PluginId.</summary>
        public const string PluginId = "531ec195-8bd6-4df5-acc0-645dd8a71645";

        /// <summary>For the probe: a settings file somewhere else than beside the install.</summary>
        internal static string PathOverride;

        /// <summary>The value of an option that leaves ares's own: nothing is written, what is set in ares's menus stays.</summary>
        public const string Ares = "ares";

        // The groups, in the order the cards show.
        public static readonly string[] Groups = { "Picture", "Colour and effects", "Sound", "Start and emulation", "Nintendo 64", "Super Famicom", "Game Boy Advance", "Mega Drive" };

        private static readonly string[] N64 = { "Nintendo 64", "Nintendo 64DD" };
        private static readonly string[] MegaDrive = { "Mega Drive", "Mega 32X", "Mega CD", "Mega CD 32X", "LaserActive (SEGA PAC)" };

        // Every value below is what ares v148 itself writes (its menus, settings.bml, the strings of ares.exe - measured on
        // 06/10: OpenGL 3.2, OpenGL 4.6 and Direct3D 9.0 draw a game, GDI draws nothing, a name ares does not know stops it).
        // Every game's settings are ares's own, set in its menus (Mehdi, 06/10: "the general options are set on the emulator
        // itself"): the Nixx page sets the screen alone (OnPage); every other option is "as set in ares" until a game's window
        // chooses one for that game.
        public static readonly AresOption[] All =
        {
            // -- Picture --
            new AresOption { Key = "Screen", Group = "Picture", Label = "Screen", Default = "full", OnPage = true,
                Values = new[] { "full", "pseudo", "windowed" }, Texts = new[] { "Full screen", "Borderless window over the screen", "Windowed" },
                Hint = "Full screen is ares's --fullscreen; the borderless window (--pseudofullscreen) covers the screen without changing its mode." },
            Pick("Driver", "Picture", "Video driver", "Video/Driver", new[] { "opengl", "opengl46", "d3d" }, new[] { "OpenGL 3.2", "OpenGL 4.6", "Direct3D 9.0" },
                 new[] { "OpenGL 3.2", "OpenGL 4.6", "Direct3D 9.0" },
                 "ares's video driver (Settings > Drivers). OpenGL 3.2 is its default on Windows; the shaders run on OpenGL. Direct3D 9 for a card OpenGL troubles."),
            // The scaling has two nodes (Output, and FixedScale for a fixed integer): SettingArgs writes it, Bml stays null.
            new AresOption { Key = "Output", Group = "Picture", Label = "Scaling", Default = Ares,
                Values = new[] { Ares, "scale", "integer", "fixed1", "fixed2", "fixed3", "fixed4", "fixed5", "fixed6", "fixed7", "stretch" },
                Texts = new[] { "As set in ares", "Best fit, keep the shape", "Integer (auto)", "Integer, fixed 1x", "Integer, fixed 2x", "Integer, fixed 3x",
                                "Integer, fixed 4x", "Integer, fixed 5x", "Integer, fixed 6x", "Integer, fixed 7x", "Stretch to fill" },
                Hint = "ares's Settings > Output: Best Fit fills the screen keeping the aspect; Integer scales by whole numbers only, every pixel the same size "
                     + "(auto: the largest that fits; fixed: that one); Stretch fills it all." },
            Pick("Aspect", "Picture", "Aspect", "Video/AspectCorrectionMode", new[] { "standard", "none", "anamorphic" },
                 new[] { "Standard (as on a TV)", "No correction (square pixels)", "Anamorphic (16:9)" }, new[] { "Standard", "None", "Anamorphic" },
                 "ares's Aspect: Standard gives the picture the console gave on a television; No correction keeps its pixels square; Anamorphic stretches "
                 + "it to 16:9 - for the games made for a widescreen TV."),
            new AresOption { Key = "Shader", Group = "Picture", Label = "Shader", Default = Ares, Bml = "Video/Shader", Free = true,
                Values = new[] { Ares, "none" }, Texts = new[] { "As set in ares", "None" },
                Hint = "ares's Settings > Shader: the .slangp presets of its Shaders folder (CRT, scanlines, LCD, smoothing...), picked as in ares's menu. "
                     + "They run on the OpenGL drivers." },
            Bool("VSync", "Picture", "VSync", "Video/Blocking", "against tearing", "ares's Drivers > Synchronize (video): waits for the screen's refresh. Off is ares's default."),
            Bool("Exclusive", "Picture", "Exclusive full screen", "Video/Exclusive", "the screen is ares's alone",
                 "ares's Drivers > Exclusive mode (Direct3D): full screen takes the display for itself - less latency, a slower switch out of the game."),

            // -- Colour and effects --
            Pick("Luminance", "Colour and effects", "Luminance", "Video/Luminance", new[] { "0.5", "0.75", "0.9", "1.0" }, new[] { "50%", "75%", "90%", "100% (ares's default)" },
                 null, "ares's Video > Luminance: the picture's brightness, 100% at most."),
            Pick("Saturation", "Colour and effects", "Saturation", "Video/Saturation", new[] { "0.0", "0.5", "0.75", "1.0", "1.25", "1.5", "2.0" },
                 new[] { "0% (grey)", "50%", "75%", "100% (ares's default)", "125%", "150%", "200%" }, null, "ares's Video > Saturation."),
            Pick("Gamma", "Colour and effects", "Gamma", "Video/Gamma", new[] { "1.0", "1.1", "1.2", "1.5", "1.8", "2.0" },
                 new[] { "100% (ares's default)", "110%", "120%", "150%", "180%", "200%" }, null, "ares's Video > Gamma: above 100% darkens the mid-tones."),
            Bool("ColorEmulation", "Colour and effects", "Colour emulation", "Video/ColorEmulation", "the colours of the real screen",
                 "ares's Video > Color Emulation: matches the colours to how they looked on the console's own screen or a TV of the time. On is ares's default."),
            Bool("ColorBleed", "Colour and effects", "Colour bleed", "Video/ColorBleed", "neighbouring pixels blurred",
                 "ares's Video > Color Bleed: blurs adjacent pixels, for the translucency effects games drew on a composite TV."),
            Bool("InterframeBlending", "Colour and effects", "Interframe blending", "Video/InterframeBlending", "LCD ghosting",
                 "ares's Video > Interframe Blending: blends each frame with the one before, as the handhelds' LCDs did - the flicker effects show as meant, "
                 + "with a little motion blur. On is ares's default."),
            Bool("Overscan", "Colour and effects", "Overscan", "Video/Overscan", "the whole frame, borders included",
                 "ares's Video > Overscan: shows the full frame, the borders a TV cropped included. On is ares's default."),
            Bool("PixelAccuracy", "Colour and effects", "Pixel accuracy", "Video/PixelAccuracy", "slower, more exact",
                 "ares's Video > Pixel Accuracy Mode: pixel-accurate rendering where the system has it - slower; for the few games that need it."),

            // -- Sound --
            Pick("AudioDriver", "Sound", "Sound driver", "Audio/Driver", new[] { "wasapi", "xaudio", "dsound", "waveout", "sdl" },
                 new[] { "WASAPI", "XAudio 2.9", "DirectSound 7.0", "waveOut", "SDL" }, new[] { "WASAPI", "XAudio 2.9", "DirectSound 7.0", "waveOut", "SDL" },
                 "ares's Drivers > Audio driver. WASAPI has the lowest latency; SDL is the one ares starts with on a fresh install."),
            Pick("Volume", "Sound", "Volume", "Audio/Volume", new[] { "0.25", "0.5", "0.75", "1.0", "1.25", "1.5", "2.0" },
                 new[] { "25%", "50%", "75%", "100% (ares's default)", "125%", "150%", "200%" }, null, "ares's Audio > Volume."),
            Bool("Mute", "Sound", "Mute", "Audio/Mute", "no sound", "ares's Settings > Mute Audio."),
            Bool("DynamicRate", "Sound", "Dynamic rate", "Audio/Dynamic", "against crackles",
                 "ares's Drivers > Dynamic rate: bends the sound's rate a little to keep it in step with the picture - against crackles with VSync on."),

            // -- Start and emulation --
            Pick("Region", "Start and emulation", "Preferred region", "Boot/Prefer", new[] { "ntsc-u", "ntsc-u-pal", "ntsc-j", "ntsc-j-pal", "pal", "pal-ntsc-j" },
                 new[] { "NTSC-U, then NTSC-J, then PAL", "NTSC-U, then PAL, then NTSC-J", "NTSC-J, then NTSC-U, then PAL",
                         "NTSC-J, then PAL, then NTSC-U", "PAL, then NTSC-U, then NTSC-J", "PAL, then NTSC-J, then NTSC-U" },
                 new[] { "NTSC-U,NTSC-J,PAL", "NTSC-U,PAL,NTSC-J", "NTSC-J,NTSC-U,PAL", "NTSC-J,PAL,NTSC-U", "PAL,NTSC-U,NTSC-J", "PAL,NTSC-J,NTSC-U" },
                 "ares's Boot Options > Region Preference: the region a console starts as when the game runs on several - and the BIOS it then takes."),
            Bool("FastBoot", "Start and emulation", "Fast boot", "Boot/Fast", "skip the BIOS intro",
                 "ares's Boot Options > Fast Boot: straight to the game, past the console's logo where ares can."),
            Pick("Defocus", "Start and emulation", "When ares loses focus", "Input/Defocus", new[] { "pause", "block", "allow" },
                 new[] { "Pause the game (ares's default)", "Keep running, ignore the pads", "Keep running, pads still work" }, new[] { "Pause", "Block", "Allow" },
                 "ares's Drivers > When focus is lost. \"Pads still work\" for a game played while another window has the focus."),
            Bool("Rewind", "Start and emulation", "Rewind", "General/Rewind", "back in time with the rewind hotkey",
                 "ares's Options > Rewind: keeps the last moments so its Rewind hotkey (Settings > Hotkeys) can play them back. Costs some speed."),
            Bool("RunAhead", "Start and emulation", "Run-ahead", "General/RunAhead", "one frame less input lag",
                 "ares's Options > Run-Ahead: removes one frame of input lag, and doubles what the system asks of the PC."),
            Bool("AutoSaveMemory", "Start and emulation", "Save memory periodically", "General/AutoSaveMemory", "the game's save written as it goes",
                 "ares's Options > Auto-Save Memory Periodically: the cartridge's or memory card's save written to disk every so often, not only when ares "
                 + "quits - nothing lost if it is killed. On is ares's default."),
            Bool("ForceInterpreter", "Start and emulation", "Force the interpreter", "General/ForceInterpreter", "slow, for a game the recompiler breaks",
                 "ares's Options > Force Interpreter: the CPU interpreted instead of recompiled, on the systems that recompile (Nintendo 64, PlayStation). "
                 + "Much slower; for a game the recompiler troubles.", "Nintendo 64", "Nintendo 64DD", "PlayStation"),

            // -- the systems' own --
            Pick("N64Quality", "Nintendo 64", "Resolution", "Video/Quality", new[] { "sd", "hd", "uhd" }, new[] { "1x native (ares's default)", "2x native", "4x native" },
                 new[] { "SD", "HD", "UHD" }, "ares's Video > N64 Render Settings: the 3D drawn at 1x, 2x or 4x the console's resolution (Vulkan, parallel-RDP).", N64),
            Bool("N64Supersampling", "Nintendo 64", "Supersampling", "Video/Supersampling", "2x / 4x scaled back down",
                 "ares's Video > Supersampling: draws at 2x or 4x and scales back down to native - smoother edges at the native size.", N64),
            Bool("N64Weave", "Nintendo 64", "Weave deinterlacing", "Video/WeaveDeinterlacing", "double the lines",
                 "ares's Video > Weave Deinterlacing: doubles the perceived vertical resolution of interlaced games; not with supersampling. On is ares's default.", N64),
            Bool("N64NoVI", "Nintendo 64", "No video interface", "Video/DisableVideoInterfaceProcessing", "the frame straight from VRAM",
                 "ares's Video > Disable Video Interface Processing: the picture taken from VRAM, without the console's post-processing (its blur).", N64),
            Bool("N64ExpansionPak", "Nintendo 64", "4MB Expansion Pak", "Nintendo64/ExpansionPak", "plugged in",
                 "ares's Options > 4MB Expansion Pak - Donkey Kong 64, Majora's Mask and Perfect Dark need it. On is ares's default.", N64),
            Pick("N64ControllerPak", "Nintendo 64", "Controller Pak size", "Nintendo64/ControllerPakBankString", new[] { "32", "128", "512", "1984" },
                 new[] { "32 KiB (ares's default)", "128 KiB (Datel 1Meg)", "512 KiB (Datel 4Meg)", "1984 KiB (maximum)" },
                 new[] { "32KiB (Default)", "128KiB (Datel 1Meg)", "512KiB (Datel 4Meg)", "1984KiB (Maximum)" },
                 "ares's Options > Controller Pak Size: the memory of a Controller Pak ares creates - one already made keeps its size.", N64),
            Bool("SfcDeepBlack", "Super Famicom", "Deep black boost", "Video/DeepBlackBoost", "darker blacks",
                 "ares's Video > Deep Black Boost: a gamma ramp that crushes the Super Famicom's greyish blacks.", "Super Famicom"),
            Bool("GbaPlayer", "Game Boy Advance", "Game Boy Player", "GameBoyAdvance/Player", "its rumble",
                 "ares's Options > Game Boy Player: the games made for it rumble the pad.", "Game Boy Advance"),
            Bool("MdTmss", "Mega Drive", "TMSS boot ROM", "MegaDrive/TMSS", "the SEGA licence screen",
                 "ares's Options > TMSS Boot Rom: the console's \"Produced by or under license from SEGA\" screen at start.", MegaDrive),
        };

        private static AresOption Bool(string key, string group, string label, string bml, string on, string hint, params string[] systems)
            => new AresOption { Key = key, Group = group, Label = label, Bml = bml, Default = Ares, Hint = hint, Systems = systems.Length == 0 ? null : systems,
                                Values = new[] { Ares, "true", "false" }, Texts = new[] { "As set in ares", "On - " + on, "Off" } };

        private static AresOption Pick(string key, string group, string label, string bml, string[] values, string[] texts, string[] writes, string hint, params string[] systems)
            => new AresOption { Key = key, Group = group, Label = label, Bml = bml, Default = Ares, Hint = hint, Systems = systems.Length == 0 ? null : systems,
                                Values = new[] { Ares }.Concat(values).ToArray(), Texts = new[] { "As set in ares" }.Concat(texts).ToArray(),
                                Writes = writes == null ? null : new string[] { null }.Concat(writes).ToArray() };

        /// <summary>A game's options as ares's --setting "Key=Value" pairs, the scaling first (Output, and FixedScale for a
        /// fixed integer); nothing for an option left "as set in ares". desktop-ui.cpp (v148): each --setting sets the node
        /// after settings.bml is read, the last of a key wins, and at exit ares puts back what the file had - measured 06/10
        /// on all of them: the game ran with them, the file came back as it was.</summary>
        public static List<string> SettingArgs(string gameId) => SettingArgs(key => For(gameId, key));

        /// <summary>The same, from values given (a game's window, before it saves them).</summary>
        public static List<string> SettingArgs(Func<string, string> valueOf)
        {
            var list = new List<string>();
            var output = valueOf("Output") ?? Ares;
            if (output.StartsWith("fixed")) { list.Add("Video/Output=IntegerFixed"); list.Add("Video/FixedScale=" + output.Substring(5)); }
            else if (output != Ares) list.Add("Video/Output=" + (output == "integer" ? "Integer" : output == "stretch" ? "Stretch" : "Scale"));
            foreach (var o in All)
            {
                var value = valueOf(o.Key);
                if (o.Bml == null || value == null || value == Ares) continue;
                if (o.Key == "Shader") { list.Add(o.Bml + "=" + (value == "none" ? "None" : value.Replace('\\', '/'))); continue; }
                int i = Array.IndexOf(o.Values, value);
                if (i >= 0) list.Add(o.Bml + "=" + (o.Writes?[i] ?? value));
            }
            return list;
        }

        /// <summary>The .slangp presets of the install's Shaders folder, as ares names them ("crt/crt-geom.slangp"), sorted.</summary>
        public static List<string> Shaders(string exe)
        {
            var list = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(exe)) return list;
                var root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), "Shaders");
                if (!Directory.Exists(root)) return list;
                foreach (var f in Directory.GetFiles(root, "*.slangp", SearchOption.AllDirectories))
                    list.Add(f.Substring(root.Length + 1).Replace('\\', '/'));
                list.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) { Log.Warn("the shaders could not be listed", ex); }
            return list;
        }

        /// <summary>What a list offers for an option: its values, then - the shader - the presets of the install, and any
        /// value in <paramref name="keep"/> it does not have (a preset since removed, still chosen).</summary>
        public static (string[] Values, string[] Texts) Choices(AresOption o, string exe, params string[] keep)
        {
            var values = o.Values.ToList();
            var texts = o.Texts.ToList();
            if (o.Free)
            {
                foreach (var s in Shaders(exe).Concat(keep.Where(k => k != null && !o.Values.Contains(k))))
                    if (!values.Contains(s, StringComparer.OrdinalIgnoreCase)) { values.Add(s); texts.Add(AresOption.ShaderText(s)); }
            }
            return (values.ToArray(), texts.ToArray());
        }

        /// <summary>A value as stored: one of the option's, lower case - or a shader's path, as ares has it.</summary>
        private static string Stored(AresOption o, string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            var lower = s.ToLowerInvariant();
            if (o.Values.Contains(lower)) return lower;
            return o.Free && s.EndsWith(".slangp", StringComparison.OrdinalIgnoreCase) ? s.Replace('\\', '/') : null;
        }

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

        /// <summary>Every game's value of <paramref name="key"/>: what the Nixx window set (the options it shows), else the
        /// option's default - "as set in ares" for all but the screen. A value an older build kept for another option is
        /// not read: every game's settings are ares's own now.</summary>
        public static string Value(string key)
        {
            var o = Option(key);
            if (!o.OnPage) return o.Default;
            return (Read(SettingsPath).TryGetValue(key, out var s) ? Stored(o, s) : null) ?? o.Default;
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
            return Read(path).TryGetValue(key, out var s) ? Stored(Option(key), s) : null;
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
