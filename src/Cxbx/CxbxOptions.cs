// The options a game is launched with (Mehdi, 03/10): video, the emulated console (EEPROM), audio, hacks and the
// experimental LLE parts - for every game in the Nixx window, and for one game in its right-click menu, the game's own
// over every game's, over the plugin's default, over Cxbx-Reloaded's own setting.
//
// WHERE THEY GO. Cxbx-Reloaded has no command line for any of them: they are read from its settings.ini and its
// EEPROM.bin when the loader starts (a game that restarts itself reads them again - still ours). So for the time of a
// session the plugin WRITES them there, and puts them back once the session is over: a copy of each in
// <data>\lbip-session\, taken before anything is written. Left behind (the host killed mid-game), they are put back at
// the next launch or the host's next start.
//
// PUT BACK WHAT WE CHANGED, NOT THE WHOLE FILE (Mehdi, 04/10). A session left behind, then cxbx.exe opened on its own and
// a setting changed in it: copying the old settings.ini back would wipe that change without a word. So keys.tsv notes
// each line written - its value before, the value written - and only a line still holding what was written goes back;
// a line changed since is the user's (or Cxbx-Reloaded's) and stays, as every line not touched. EEPROM.bin, a few bytes
// under a checksum, goes back whole - but only while it is still exactly what the session wrote (eeprom.sha): changed
// since, it is kept. A settings.ini nobody touched since the session wrote it (settings.sha) goes back whole, as before;
// a line is "still what was written" also when Cxbx-Reloaded wrote the same value its own way (0x8 for 8, True for
// true). A session from before keys.tsv is put back whole, as it was. An option left unset is not written: Cxbx-Reloaded's own value - the
// one its window (right-click a game, "Open Nixx-Cxbx...") sets - stays.
//
// What each one is, read in Cxbx-Reloaded's source (master of April 2026):
//   settings.ini  [video]  adapter (Direct3D's numbering: 0 is the main screen), VideoResolution ("W x H 32bit x8r8g8b8
//                          (Hz hz)" - the size of the image drawn; unparsable, "Automatic (Xbox Default)" - the Xbox's
//                          own 640x480, stretched to the screen), RenderResolution (the upscale factor), VSync,
//                          MaintainAspect
//                 [audio]  adapter (a DirectSound device GUID - all zeros is the device Windows uses by default), PCM,
//                          XADPCM, UnknownCodec, MuteOnUnfocus
//                 [hack]   DisablePixelShaders, UseAllCores, SkipRdtscPatching
//                 [core]   FlagsLLE: APU 1, GPU 2, JIT 4, USB 8
//   EEPROM.bin    0x2C game region, 0x58 AV region (the video standard: NTSC-M | 60 Hz, NTSC-J | 60 Hz, PAL-I | 50 Hz),
//                 0x90 language, 0x94 video flags (widescreen 0x10000, letterbox 0x100000, PAL 60 Hz 0x400000, 480p
//                 0x80000, 720p 0x20000, 1080i 0x40000), 0x98 audio flags (mono 1, surround 2), 0x9C / 0xA4 parental
//                 controls - see CxbxEeprom for the checksum.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace LbIntegrations.Cxbx
{
    internal sealed class CxbxOption
    {
        public string Key;                 // "video.resolution" - the key in the plugin's ini files ("opt." + it)
        public string Group;
        public string Label;
        /// <summary>On or off ("on" / "off"): a three-state box. Otherwise a list of Choices.</summary>
        public bool Bool;
        public List<(string Value, string Label)> Choices = new List<(string, string)>();
        /// <summary>What applies when neither the game nor every game sets it - null: Cxbx-Reloaded's own.</summary>
        public string Default;
        public string Help;
        /// <summary>What a window shows under the option (Mehdi, 05/10: short sentences, the rest on hover) - null: Help itself.</summary>
        public string Short;

        /// <summary>Set game by game only (Mehdi, 04/10): audio, the hacks and the experimental LLE parts are not in the Nixx
        /// window's tab - a value it once saved for every game is no longer used.</summary>
        public bool PerGameOnly => Group == "Audio" || Group == "Hacks" || Group == "Experimental (LLE)";

        public string LabelOf(string value)
            => Bool ? (value == "on" ? "on" : "off") : Choices.FirstOrDefault(c => c.Value == value).Label ?? value;
    }

    internal static class CxbxOptions
    {
        public const string Prefix = "opt.";

        /// <summary>For an option the plugin sets by default: the way back to what Cxbx-Reloaded's window sets - written as nothing.</summary>
        public const string OwnValue = "cxbx";
        private static readonly (string, string) Own = (OwnValue, "Cxbx-Reloaded's own");

        // UNSET IS CXBX-RELOADED'S OWN (Mehdi, 03/10) - read from its files and shown - but for the two defaults agreed on:
        // the console in Windows' language, and its video standard following the game's region.
        public static readonly List<CxbxOption> All = new List<CxbxOption>
        {
            // ── video ──
            new CxbxOption { Key = "video.adapter", Group = "Video", Label = "Screen", Choices = { ("main", "The main screen") } },
            new CxbxOption { Key = "video.resolution", Group = "Video", Label = "Display resolution",
                             Choices = { ("screen", "The screen's current resolution"), ("xbox", "Automatic (Xbox default)") },
                             Help = "The size of the image drawn. The screen's current: read at each launch, sharp. Automatic: the Xbox's 640x480, stretched." },
            new CxbxOption { Key = "video.render", Group = "Video", Label = "Render resolution",
                             Choices = { ("1", "1x (native)"), ("2", "2x"), ("3", "3x"), ("4", "4x") } },
            new CxbxOption { Key = "video.vsync", Group = "Video", Label = "VSync", Bool = true },
            new CxbxOption { Key = "video.aspect", Group = "Video", Label = "Maintain aspect ratio", Bool = true },

            // ── the console ──
            new CxbxOption { Key = "console.region", Group = "Console", Label = "Region", Default = "follow",
                             Choices = { ("follow", "The game's region"), ("1", "North America (NTSC)"), ("2", "Japan"), ("4", "Europe / rest of the world (PAL)"), Own },
                             Help = "The game's: the console takes a region the game accepts - no question at start. Another one: Cxbx-Reloaded "
                                    + "warns before starting a game that does not accept it." },
            new CxbxOption { Key = "console.language", Group = "Console", Label = "Language", Default = "windows",
                             Choices = { ("windows", "Your console's language (else Windows')"), ("1", "English"), ("4", "French"), ("3", "German"), ("5", "Spanish"),
                                         ("6", "Italian"), ("9", "Portuguese"), ("2", "Japanese"), ("7", "Korean"), ("8", "Chinese"), Own },
                             Help = "A game with several languages starts in the console's. Your console's: the one set in the Nixx window's "
                                    + "\"Your console\" tab, else Windows' display language." },
            new CxbxOption { Key = "console.video", Group = "Console", Label = "Video standard", Default = "follow",
                             Choices = { ("follow", "Follow the game's region"), ("pal50", "PAL 50 Hz"), ("pal60", "PAL 60 Hz"),
                                         ("ntsc", "NTSC"), ("ntsc-hd", "NTSC + 480p/720p/1080i"), Own },
                             Help = "Follow: a European game gets PAL with 60 Hz allowed, an American or Japanese one NTSC with its HD modes. "
                                    + "A European game's HD modes, when it has any, only show on NTSC." },
            new CxbxOption { Key = "console.hddkey", Group = "Console", Label = "Console identity", Default = "pack",
                             Choices = { ("pack", "Your console's (from its seed)"), Own },
                             Help = "The serial number, MAC address, HDD key and online key - made from the seed of \"Your console\", the same "
                                    + "on Cxbx-Reloaded and xemu (with no seed yet: the HDD key alone, the pack's). A save carries the HDD key it "
                                    + "was made with, and its game is launched with that one. Cxbx-Reloaded's own: the values of its EEPROM window." },
            new CxbxOption { Key = "console.screen", Group = "Console", Label = "Picture", Choices = { ("normal", "Normal (4:3)"), ("widescreen", "Widescreen (16:9)"), ("letterbox", "Letterbox") } },
            new CxbxOption { Key = "console.audio", Group = "Console", Label = "Sound", Choices = { ("stereo", "Stereo"), ("mono", "Mono"), ("surround", "Surround") } },

            // ── audio ──
            new CxbxOption { Key = "audio.device", Group = "Audio", Label = "Output device", Choices = { ("windows", "The one Windows uses, at launch") } },
            new CxbxOption { Key = "audio.pcm", Group = "Audio", Label = "PCM", Bool = true },
            new CxbxOption { Key = "audio.xadpcm", Group = "Audio", Label = "XADPCM", Bool = true },
            new CxbxOption { Key = "audio.unknown", Group = "Audio", Label = "Unknown codec", Bool = true },
            new CxbxOption { Key = "audio.mute", Group = "Audio", Label = "Mute when not in front", Bool = true },

            // ── hacks ──
            new CxbxOption { Key = "hack.pixelshaders", Group = "Hacks", Label = "Disable pixel shaders", Bool = true },
            new CxbxOption { Key = "hack.allcores", Group = "Hacks", Label = "Use all cores", Bool = true },
            new CxbxOption { Key = "hack.rdtsc", Group = "Hacks", Label = "Skip RDTSC patching", Bool = true },

            // ── experimental ──
            new CxbxOption { Key = "lle.apu", Group = "Experimental (LLE)", Label = "LLE APU (audio)", Bool = true },
            new CxbxOption { Key = "lle.gpu", Group = "Experimental (LLE)", Label = "LLE GPU (video)", Bool = true },
            new CxbxOption { Key = "lle.jit", Group = "Experimental (LLE)", Label = "LLE JIT (CPU)", Bool = true },
            new CxbxOption { Key = "lle.usb", Group = "Experimental (LLE)", Label = "LLE USB (controllers)", Bool = true },
        };

        /// <summary>The short sentences a window shows (Mehdi, 05/10), the option's whole Help in its tooltip.</summary>
        private static readonly Dictionary<string, string> Shorts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["video.resolution"] = "The screen's current is sharp; Automatic stretches the Xbox's 640x480.",
            ["console.region"] = "The game's own starts it with no warning; another one makes Cxbx-Reloaded warn first.",
            ["console.language"] = "A game with several languages starts in this one.",
            ["console.video"] = "Follow: PAL with 60 Hz for a European game, NTSC with HD modes for the others.",
            ["console.hddkey"] = "Serial, MAC and keys made from \"Your console\" - the same on xemu.",
        };

        static CxbxOptions() { foreach (var o in All) if (Shorts.TryGetValue(o.Key, out var s)) o.Short = s; }
        /// <summary>What a launch uses, option by option: the game's, else every game's, else the default; absent: Cxbx-Reloaded's own.</summary>
        public static Dictionary<string, string> Effective(string gameId)
        {
            var every = CxbxSettings.Read();
            var game = CxbxSettings.ReadGame(gameId);
            var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in All)
            {
                var value = Get(game, o.Key) ?? (o.PerGameOnly ? null : Get(every, o.Key)) ?? o.Default;
                if (value != null && value != OwnValue) v[o.Key] = value;
            }
            return v;
        }

        public static string Get(IDictionary<string, string> s, string key)
            => s.TryGetValue(Prefix + key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        // ── the session's copies ─────────────────────────────────────────────

        private static string SessionDir(string data) => Path.Combine(data, "lbip-session");

        /// <summary>A session left behind put right: settings.ini and EEPROM.bin back as they were before it. Never while
        /// Cxbx-Reloaded runs.</summary>
        public static void Restore(string exe, string why)
        {
            try
            {
                var data = CxbxPaths.DataDir(exe);
                if (data == null) return;
                var dir = SessionDir(data);
                if (!Directory.Exists(dir)) return;
                if (CxbxPaths.LoaderRunning()) { Log.Info("options: Cxbx-Reloaded is running - its files are put back once it has quit"); return; }
                // paths.tsv: each copy and where it came from - written with the copies, so a restore never guesses.
                var list = Path.Combine(dir, "paths.tsv");
                var keys = Path.Combine(dir, KeysFile);
                var sha = Path.Combine(dir, EepromHash);
                var done = new List<string>();
                if (File.Exists(list))
                    foreach (var line in File.ReadAllLines(list))
                    {
                        var c = line.Split('\t');
                        // keys.bin (the save's certificate key, XboxSaveKeys): back as it was - removed when there was none.
                        if (c.Length >= 2 && c[0].Equals(KeysBin, StringComparison.OrdinalIgnoreCase))
                        {
                            if (c.Length == 3 && c[2] == "absent") { if (File.Exists(c[1])) { File.Delete(c[1]); done.Add("keys.bin removed"); } }
                            else if (File.Exists(Path.Combine(dir, KeysBin))
                                     && !(File.Exists(c[1]) && File.ReadAllBytes(c[1]).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(dir, KeysBin)))))
                            { File.Copy(Path.Combine(dir, KeysBin), c[1], overwrite: true); done.Add("keys.bin put back"); }
                            continue;
                        }
                        var copy = c.Length == 2 ? Path.Combine(dir, c[0]) : null;
                        if (copy == null || !File.Exists(copy)) continue;
                        bool isSettings = c[0].Equals(CxbxPaths.SettingsFile, StringComparison.OrdinalIgnoreCase);
                        var settingsSha = Path.Combine(dir, SettingsHash);
                        bool untouched = File.Exists(settingsSha) && File.Exists(c[1]) && Sha(c[1]) == File.ReadAllText(settingsSha).Trim();
                        if (isSettings && !untouched && File.Exists(keys) && File.Exists(c[1])) done.Add(PutKeysBack(c[1], keys));
                        else if (!isSettings && File.Exists(sha) && File.Exists(c[1]) && Sha(c[1]) != File.ReadAllText(sha).Trim())
                            done.Add("EEPROM.bin changed since the session - kept as it is");
                        else { File.Copy(copy, c[1], overwrite: true); done.Add(c[0] + " put back"); }
                    }
                Directory.Delete(dir, recursive: true);
                Log.Info("options: " + (done.Count == 0 ? "nothing to put back" : string.Join("; ", done)) + " (" + why + ")");
            }
            catch (Exception ex) { Log.Warn("options: could not put Cxbx-Reloaded's files back", ex); }
        }

        /// <summary>The game's options written into settings.ini and EEPROM.bin for its session, the two files copied
        /// first. <paramref name="xbe"/>: the game, for its region. <paramref name="keys"/>: its save's (XboxSaveKeys) - the
        /// HDD key into EEPROM.bin, the certificate key into keys.bin (its EEPROM key kept: the one EEPROM.bin is signed with).</summary>
        public static void Apply(string exe, string gameId, XbeInfo xbe, LbIntegrations.Xbox.SaveKeys keys = null)
        {
            try
            {
                var data = CxbxPaths.DataDir(exe);
                var settings = CxbxPaths.SettingsOf(exe);
                if (data == null || settings == null) { Log.Info("options: Cxbx-Reloaded is not set up yet - nothing written"); return; }
                Restore(exe, "a session left behind");

                // EEPROM.bin made first when there is none: the copy is then of a real one, and a restore never deletes the
                // console's identity (its serial, its keys).
                var eeprom = Path.Combine(data, "EEPROM.bin");
                if (!File.Exists(eeprom)) CxbxEeprom.MatchRegion(exe, xbe, createOnly: true);

                var dir = SessionDir(data);
                Directory.CreateDirectory(dir);
                var paths = new List<string>();
                File.Copy(settings, Path.Combine(dir, CxbxPaths.SettingsFile), overwrite: true);
                paths.Add(CxbxPaths.SettingsFile + "\t" + settings);
                if (File.Exists(eeprom)) { File.Copy(eeprom, Path.Combine(dir, "EEPROM.bin"), overwrite: true); paths.Add("EEPROM.bin\t" + eeprom); }
                var keysBin = Path.Combine(data, KeysBin);
                if (File.Exists(keysBin)) { File.Copy(keysBin, Path.Combine(dir, KeysBin), overwrite: true); paths.Add(KeysBin + "\t" + keysBin); }
                else paths.Add(KeysBin + "\t" + keysBin + "\tabsent");
                File.WriteAllLines(Path.Combine(dir, "paths.tsv"), paths);

                var v = Effective(gameId);
                var said = new List<string>();
                // THE SAVE'S KEYS (XboxSaveKeys): its HDD key (CxbxEeprom.ApplyOptions), its certificate key in keys.bin.
                if (keys != null) said.Add("keys " + keys.Origin);
                if (keys?.Hdd != null) v["console.hddkey.bytes"] = LbIntegrations.Xbox.XboxKeys.Hex(keys.Hdd);
                if (keys?.Cert != null && !LbIntegrations.Xbox.XboxKeys.Same(CertificateKey(data), keys.Cert))
                {
                    var now = File.Exists(keysBin) ? File.ReadAllBytes(keysBin) : null;
                    var b = new byte[32];
                    if (now != null && now.Length == 32) Array.Copy(now, b, 16); else LbIntegrations.Xbox.XboxKeys.RetailEeprom.CopyTo(b, 0);
                    keys.Cert.CopyTo(b, 16);
                    File.WriteAllBytes(keysBin, b);
                    said.Add("certificate key " + (LbIntegrations.Xbox.XboxKeys.Same(keys.Cert, LbIntegrations.Xbox.XboxKeys.Zero) ? "zero" : "the save's") + " (keys.bin)");
                }
                var written = WriteSettings(settings, v, said);
                File.WriteAllLines(Path.Combine(dir, KeysFile), written.Select(w => string.Join("\t", w.Section, w.Key, w.Before ?? Absent, w.After)), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(dir, SettingsHash), Sha(settings));
                // The region first: the video standard that follows it reads it (CxbxEeprom.ApplyOptions).
                if (v.TryGetValue("console.region", out var region) && region == "follow") CxbxEeprom.MatchRegion(exe, xbe);
                if (File.Exists(eeprom)) CxbxEeprom.ApplyOptions(eeprom, data, v, said);
                if (File.Exists(eeprom)) File.WriteAllText(Path.Combine(dir, EepromHash), Sha(eeprom));
                Log.Info("options: " + (said.Count == 0 ? "nothing to write" : string.Join(", ", said)));
            }
            catch (Exception ex) { Log.Warn("options: could not be written - Cxbx-Reloaded's own settings apply", ex); }
        }

        private const string KeysFile = "keys.tsv", EepromHash = "eeprom.sha", SettingsHash = "settings.sha", Absent = "\u0001absent";
        private const string KeysBin = "keys.bin";

        /// <summary>The certificate key Cxbx-Reloaded runs with: keys.bin's second half (LoadXboxKeys reads a file of 32 bytes
        /// exactly), else zero.</summary>
        public static byte[] CertificateKey(string data)
        {
            try
            {
                var f = data == null ? null : Path.Combine(data, KeysBin);
                var b = f != null && File.Exists(f) ? File.ReadAllBytes(f) : null;
                return b != null && b.Length == 32 ? b.Skip(16).ToArray() : LbIntegrations.Xbox.XboxKeys.Zero;
            }
            catch { return LbIntegrations.Xbox.XboxKeys.Zero; }
        }

        /// <summary>The same value, however written: true / True, 8 / 0x8.</summary>
        private static bool SameValue(string a, string b)
            => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
               || (CxbxOwn.Long(a) is long x && CxbxOwn.Long(b) is long y && x == y);

        /// <summary>The lines a session wrote put back to what they held - only those still holding what was written.</summary>
        private static string PutKeysBack(string settings, string keys)
        {
            var ini = new IniLines(settings);
            int back = 0, kept = 0;
            foreach (var line in File.ReadAllLines(keys))
            {
                var c = line.Split('\t');
                if (c.Length < 4) continue;
                var now = ini.Get(c[0], c[1]);
                if (!SameValue(now, c[3])) { kept++; continue; }       // changed since: the user's
                if (c[2] == Absent) ini.Remove(c[0], c[1]); else ini.Set(c[0], c[1], c[2]);
                back++;
            }
            ini.Save();
            return "settings.ini: " + back + " line(s) put back" + (kept > 0 ? ", " + kept + " changed since the session kept" : "");
        }

        private static string Sha(string path)
        {
            using var s = File.OpenRead(path);
            using var h = System.Security.Cryptography.SHA256.Create();
            return Convert.ToHexString(h.ComputeHash(s));
        }

        // ── settings.ini ─────────────────────────────────────────────────────

        private static List<(string Section, string Key, string Before, string After)> WriteSettings(string path, Dictionary<string, string> v, List<string> said)
        {
            var ini = new IniLines(path);
            var written = new List<(string, string, string, string)>();
            void Set(string section, string key, string value, string what)
            {
                if (!written.Any(w => w.Item1 == section && w.Item2 == key)) written.Add((section, key, ini.Get(section, key), value));
                ini.Set(section, key, value);
                said.Add(what);
            }
            string Bool(string x) => x == "on" ? "true" : "false";

            if (v.TryGetValue("video.adapter", out var adapter) && adapter == "main") Set("video", "adapter", "0", "main screen");
            if (v.TryGetValue("video.resolution", out var res))
            {
                if (res == "xbox") Set("video", "VideoResolution", "Automatic (Xbox Default)", "Xbox resolution");
                else if (res == "screen" && MainScreenMode() is string mode) Set("video", "VideoResolution", mode, mode);
            }
            if (v.TryGetValue("video.render", out var render) && int.TryParse(render, out var r) && r >= 1 && r <= 10) Set("video", "RenderResolution", r.ToString(CultureInfo.InvariantCulture), "render " + r + "x");
            if (v.TryGetValue("video.vsync", out var vs)) Set("video", "VSync", Bool(vs), "VSync " + vs);
            if (v.TryGetValue("video.aspect", out var ar)) Set("video", "MaintainAspect", Bool(ar), "aspect " + ar);

            if (v.TryGetValue("audio.device", out var dev) && dev == "windows") Set("audio", "adapter", "00000000 0000 0000 0000 000000000000", "Windows' audio device");
            if (v.TryGetValue("audio.pcm", out var pcm)) Set("audio", "PCM", Bool(pcm), "PCM " + pcm);
            if (v.TryGetValue("audio.xadpcm", out var xa)) Set("audio", "XADPCM", Bool(xa), "XADPCM " + xa);
            if (v.TryGetValue("audio.unknown", out var un)) Set("audio", "UnknownCodec", Bool(un), "unknown codec " + un);
            if (v.TryGetValue("audio.mute", out var mu)) Set("audio", "MuteOnUnfocus", Bool(mu), "mute " + mu);

            if (v.TryGetValue("hack.pixelshaders", out var ps)) Set("hack", "DisablePixelShaders", Bool(ps), "no pixel shaders " + ps);
            if (v.TryGetValue("hack.allcores", out var ac)) Set("hack", "UseAllCores", Bool(ac), "all cores " + ac);
            if (v.TryGetValue("hack.rdtsc", out var rd)) Set("hack", "SkipRdtscPatching", Bool(rd), "skip RDTSC " + rd);

            // FlagsLLE: a bit per part - the ones set here changed, the others kept.
            long lle = CxbxOwn.Long(ini.Get("core", "FlagsLLE")) ?? 0, was = lle;     // "0x8": SimpleIni writes hex
            foreach (var (key, bit) in new[] { ("lle.apu", 1L), ("lle.gpu", 2L), ("lle.jit", 4L), ("lle.usb", 8L) })
                if (v.TryGetValue(key, out var on)) lle = on == "on" ? lle | bit : lle & ~bit;
            if (lle != was || v.Keys.Any(k => k.StartsWith("lle."))) Set("core", "FlagsLLE", lle.ToString(CultureInfo.InvariantCulture), "LLE " + lle);

            ini.Save();
            return written;
        }

        /// <summary>"2560 x 1440 32bit x8r8g8b8 (144 hz)": the main screen's mode now, as Cxbx-Reloaded's window writes one.</summary>
        public static string MainScreenMode()
        {
            var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(null, -1, ref dm) || dm.dmPelsWidth <= 0) return null;   // ENUM_CURRENT_SETTINGS
            return dm.dmPelsWidth + " x " + dm.dmPelsHeight + " 32bit x8r8g8b8 (" + dm.dmDisplayFrequency + " hz)";
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        /// <summary>An ini file edited line by line: every line not touched is kept as it is, comments and order included.
        /// SimpleIni's spelling, "Key = value"; a key or section missing is added.</summary>
        internal sealed class IniLines
        {
            private readonly string _path;
            private readonly List<string> _lines;

            public IniLines(string path)
            {
                _path = path;
                _lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
            }

            private int SectionAt(string section)
                => _lines.FindIndex(l => l.Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase));

            private int End(int sectionAt)
            {
                int i = sectionAt + 1;
                while (i < _lines.Count && !_lines[i].TrimStart().StartsWith("[")) i++;
                return i;
            }

            private int KeyAt(int sectionAt, string key)
            {
                for (int i = sectionAt + 1; i < End(sectionAt); i++)
                {
                    var at = _lines[i].IndexOf('=');
                    if (at > 0 && _lines[i].Substring(0, at).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return i;
                }
                return -1;
            }

            public string Get(string section, string key)
            {
                int s = SectionAt(section);
                if (s < 0) return null;
                int k = KeyAt(s, key);
                return k < 0 ? null : _lines[k].Substring(_lines[k].IndexOf('=') + 1).Trim();
            }

            public void Set(string section, string key, string value)
            {
                int s = SectionAt(section);
                if (s < 0)
                {
                    if (_lines.Count > 0 && _lines[_lines.Count - 1].Trim().Length > 0) _lines.Add("");
                    _lines.Add("[" + section + "]");
                    _lines.Add(key + " = " + value);
                    return;
                }
                int k = KeyAt(s, key);
                if (k >= 0) _lines[k] = key + " = " + value;
                else
                {
                    int end = End(s);
                    while (end > s + 1 && _lines[end - 1].Trim().Length == 0) end--;
                    _lines.Insert(end, key + " = " + value);
                }
            }

            public void Remove(string section, string key)
            {
                int s = SectionAt(section);
                if (s < 0) return;
                int k = KeyAt(s, key);
                if (k >= 0) _lines.RemoveAt(k);
            }

            public void Save()
            {
                File.WriteAllLines(_path + ".lbip", _lines, new UTF8Encoding(false));
                File.Move(_path + ".lbip", _path, overwrite: true);
            }
        }
    }
}
