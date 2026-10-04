// What Cxbx-Reloaded itself is set to, option by option (CxbxOptions) - read from its settings.ini and EEPROM.bin, with
// its own defaults for what they do not say (Settings.cpp LoadConfig, EmuEEPROMReset; master of April 2026). Shown as the
// "unset" of a row (Mehdi, 03/10): "<Cxbx-Reloaded's own: 2560 x 1440 (144 Hz)>" rather than a promise with no value.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LbIntegrations.Cxbx
{
    internal sealed class CxbxOwn
    {
        private readonly Dictionary<string, string> _ini;
        private readonly byte[] _eeprom;

        private CxbxOwn(Dictionary<string, string> ini, byte[] eeprom) { _ini = ini; _eeprom = eeprom; }

        public static CxbxOwn Read(string exe)
        {
            Dictionary<string, string> ini = null;
            byte[] eeprom = null;
            // During a game the files hold its options: Cxbx-Reloaded's own are the session's copies (CxbxOptions).
            var data = exe == null ? null : CxbxPaths.DataDir(exe);
            var session = data == null ? null : Path.Combine(data, "lbip-session");
            string Pick(string live, string name) => session != null && File.Exists(Path.Combine(session, name)) ? Path.Combine(session, name) : live;
            try { var s = exe == null ? null : CxbxPaths.SettingsOf(exe); if (s != null) ini = CxbxPaths.ReadIni(Pick(s, CxbxPaths.SettingsFile)); } catch { }
            try
            {
                var e = data == null ? null : Pick(Path.Combine(data, "EEPROM.bin"), "EEPROM.bin");
                if (e != null && File.Exists(e)) { var b = File.ReadAllBytes(e); if (b.Length == 256) eeprom = b; }
            }
            catch { }
            return new CxbxOwn(ini ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), eeprom);
        }

        /// <summary>"0x1" or "1": SimpleIni writes its numbers in hex.</summary>
        public static long? Long(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            v = v.Trim();
            if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.TryParse(v.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h) ? h : (long?)null;
            return long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : (long?)null;
        }

        private string Raw(string section, string key) => _ini.TryGetValue(section + "|" + key, out var v) ? v : null;

        private bool Bool(string section, string key, bool byDefault)
        {
            var v = Raw(section, key);
            if (string.IsNullOrWhiteSpace(v)) return byDefault;
            v = v.Trim().ToLowerInvariant();
            return v == "true" || v == "yes" || v == "on" || v == "1" || v == "t" || v == "y";
        }

        private uint E(int at) => BitConverter.ToUInt32(_eeprom, at);

        /// <summary>The setting in words - "2560 x 1440 (144 Hz)", "on", "PAL 50 Hz".</summary>
        public string Label(CxbxOption o)
        {
            string OnOff(bool b) => b ? "on" : "off";
            long Lle() => Long(Raw("core", "FlagsLLE")) ?? 0;
            switch (o.Key)
            {
                case "video.adapter": { var n = Long(Raw("video", "adapter")) ?? 0; return n == 0 ? "the main screen" : "screen " + (n + 1); }
                case "video.resolution":
                {
                    var m = Regex.Match(Raw("video", "VideoResolution") ?? "", @"^(\d+) x (\d+) \S+ \S+(?: \((\d+) hz\))?");
                    return m.Success ? m.Groups[1].Value + " x " + m.Groups[2].Value + (m.Groups[3].Success ? " (" + m.Groups[3].Value + " Hz)" : "") : "Automatic (Xbox default)";
                }
                case "video.render": return (Long(Raw("video", "RenderResolution")) ?? 1) + "x";
                case "video.vsync": return OnOff(Bool("video", "VSync", false));
                case "video.aspect": return OnOff(Bool("video", "MaintainAspect", true));
                case "audio.device":
                    return (Raw("audio", "adapter") ?? "").Replace(" ", "").Replace("0", "").Length == 0 ? "the device Windows uses" : "a device chosen in its window";
                case "audio.pcm": return OnOff(Bool("audio", "PCM", true));
                case "audio.xadpcm": return OnOff(Bool("audio", "XADPCM", true));
                case "audio.unknown": return OnOff(Bool("audio", "UnknownCodec", true));
                case "audio.mute": return OnOff(Bool("audio", "MuteOnUnfocus", true));
                case "hack.pixelshaders": return OnOff(Bool("hack", "DisablePixelShaders", false));
                case "hack.allcores": return OnOff(Bool("hack", "UseAllCores", false));
                case "hack.rdtsc": return OnOff(Bool("hack", "SkipRdtscPatching", false));
                case "lle.apu": return OnOff((Lle() & 1) != 0);
                case "lle.gpu": return OnOff((Lle() & 2) != 0);
                case "lle.jit": return OnOff((Lle() & 4) != 0);
                case "lle.usb": return OnOff((Lle() & 8) != 0);
            }
            if (_eeprom == null) return "made at its first start";
            switch (o.Key)
            {
                case "console.region": { uint r = E(0x2C) & 7; return r == 1 ? "North America (NTSC)" : r == 2 ? "Japan" : r == 4 ? "Europe / rest of the world (PAL)" : "region " + r; }
                case "console.language": return o.LabelOf(E(0x90).ToString(CultureInfo.InvariantCulture));
                case "console.video":
                {
                    uint av = E(0x58) & 0xF00, f = E(0x94);
                    if (av == 0x300) return (f & 0x400000) != 0 ? "PAL 60 Hz" : "PAL 50 Hz";
                    return (av == 0x200 ? "NTSC-J" : "NTSC") + ((f & 0xE0000) != 0 ? " + HD modes" : "");
                }
                case "console.screen": { uint f = E(0x94); return (f & 0x10000) != 0 ? "Widescreen (16:9)" : (f & 0x100000) != 0 ? "Letterbox" : "Normal (4:3)"; }
                case "console.audio": { uint a = E(0x98) & 3; return a == 1 ? "Mono" : a == 2 ? "Surround" : "Stereo"; }
                case "console.hddkey":
                {
                    var k = new byte[16]; Array.Copy(_eeprom, 0x1C, k, 0, 16);
                    return k.SequenceEqual(LbIntegrations.Identity.PackIdentity.XboxHddKey()) ? "the pack's" : "its own (" + BitConverter.ToString(k, 0, 4).Replace("-", "") + "...)";
                }
            }
            return "?";
        }
    }
}
