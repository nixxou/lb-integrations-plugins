// Where an install of ours keeps what, and how an entry of ours is told from anybody else's.
//
//     <install>\xemu.exe                 xemu, PORTABLE: xemu.toml beside it (ui/xemu-settings.cc detects that file)
//     <install>\xemu.toml                its settings - [sys.files] set by this plugin (XemuToml)
//     <install>\lbip-xemu-build.txt      the release tag this plugin installed - and THE MARK of an install of ours
//     <install>\bios\                    the user's originals: mcpx_1.0.bin, a flash BIOS - never written by the pack
//     <install>\hdd\base.qcow2           the console as xemu's dashboard project ships it - NEVER the disk xemu writes to
//     <install>\hdd\games\<title>.qcow2  a game's own console: a qcow2 of differences over base.qcow2 (Qcow2Overlay)
//     <install>\hdd\standalone.qcow2     the one xemu.toml points at between games - xemu opened on its own writes there
//     <install>\discs\                   the XISO copies of discs xemu cannot open as they are (XemuDisc)
//
// OURS IS THE ONE WITH THE MARK. Unbroken's own Xemu plugin installs into Emulators\Xemu and claims any emulator whose
// executable is named like xemu: this one claims only an install carrying lbip-xemu-build.txt, so the two never fight over
// the same entry (from our side).

using System;
using System.Diagnostics;
using System.IO;

namespace LbIntegrations.Xemu
{
    internal static class XemuPaths
    {
        public const string Exe = "xemu.exe";
        public const string Toml = "xemu.toml";
        public const string VersionFile = "lbip-xemu-build.txt";

        public static bool IsXemuExe(string path)
        {
            try { return !string.IsNullOrWhiteSpace(path) && string.Equals(Path.GetFileName(path), Exe, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        /// <summary>An xemu installed by this plugin: xemu.exe with the mark beside it.</summary>
        public static bool IsOurs(string fullExe)
        {
            try { return IsXemuExe(fullExe) && File.Exists(Path.Combine(Path.GetDirectoryName(fullExe) ?? "", VersionFile)); }
            catch { return false; }
        }

        public static string Dir(string exe) => string.IsNullOrEmpty(exe) ? null : Path.GetDirectoryName(exe);
        public static string TomlOf(string exe) => Dir(exe) is string d ? Path.Combine(d, Toml) : null;
        public static string BiosDir(string exe) => Dir(exe) is string d ? Path.Combine(d, "bios") : null;
        public static string HddDir(string exe) => Dir(exe) is string d ? Path.Combine(d, "hdd") : null;
        public static string BaseHdd(string exe) => HddDir(exe) is string d ? Path.Combine(d, "base.qcow2") : null;
        public static string StandaloneHdd(string exe) => HddDir(exe) is string d ? Path.Combine(d, "standalone.qcow2") : null;
        public static string GameHdd(string exe, string titleId) => HddDir(exe) is string d ? Path.Combine(d, "games", titleId + ".qcow2") : null;
        public static string DiscCache(string exe) => Dir(exe) is string d ? Path.Combine(d, "discs") : null;
        public static string Eeprom(string exe) => Dir(exe) is string d ? Path.Combine(d, "eeprom.bin") : null;
        /// <summary>The console of a game's session, made from eeprom.bin at each launch (Eeprom\XemuEeprom).</summary>
        public static string SessionEeprom(string exe) => Dir(exe) is string d ? Path.Combine(d, "eeprom-session.bin") : null;

        public static string InstalledTag(string exe)
        {
            try { var f = Path.Combine(Dir(exe) ?? "", VersionFile); return File.Exists(f) ? File.ReadAllText(f).Trim() : null; }
            catch { return null; }
        }

        /// <summary>xemu.exe under <paramref name="dir"/> (the zip carries it at its root; one level down in case).</summary>
        public static string FindExe(string dir)
        {
            try
            {
                var direct = Path.Combine(dir, Exe);
                if (File.Exists(direct)) return direct;
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    var p = Path.Combine(sub, Exe);
                    if (File.Exists(p)) return p;
                }
            }
            catch { }
            return null;
        }

        /// <summary>Is an xemu.exe running from this install's folder?</summary>
        public static bool Running(string exe)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Exe)))
                    using (p)
                    {
                        string path = null;
                        try { path = p.MainModule?.FileName; } catch { }
                        if (path == null || exe == null || string.Equals(Path.GetFullPath(path), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase)) return true;
                    }
            }
            catch { }
            return false;
        }

        // ── the BIOS files (xemu.app/docs/required-files, and Unbroken's plugin's list and hashes) ──

        public const string Mcpx = "mcpx_1.0.bin";
        public const string McpxMd5 = "d49c52a4102f6df7bcf8d0617ac475ed";

        /// <summary>Flash BIOS names, best first, with their MD5 - the ones Unbroken's plugin knows. A retail BIOS that is NOT
        /// modified does not start games (xemu's docs); "COMPLEX 4627" is the one they recommend.</summary>
        public static readonly (string Name, string Md5)[] Flashes =
        {
            ("Complex_4627v1.03.bin", "21445c6f28fca7285b0f167ea770d1e5"),
            ("Complex_4627.bin",      "ec00e31e746de2473acfe7903c5a4cb7"),
            ("bios_debug_4627.bin",   "19b5c6d3d42a707bba620634fe6d4baf"),
            ("xbox-4627_debug.bin",   "e8dd61cc6abdbd06aac185e371312dc1"),
            ("bios_retail_4627.bin",  "39cee882148a87f93cb440b99dde3ceb"),
        };

        public static string McpxPath(string exe)
        {
            var f = BiosDir(exe) is string d ? Path.Combine(d, Mcpx) : null;
            return f != null && File.Exists(f) && new FileInfo(f).Length == 512 ? f : null;
        }

        /// <summary>The flash BIOS to use: a known name first, else any .bin of the right size (a multiple of 64 KB - xbox.c).</summary>
        public static string FlashPath(string exe)
        {
            try
            {
                var d = BiosDir(exe);
                if (d == null || !Directory.Exists(d)) return null;
                foreach (var (name, _) in Flashes) { var p = Path.Combine(d, name); if (File.Exists(p)) return p; }
                foreach (var p in Directory.GetFiles(d, "*.bin"))
                {
                    var n = Path.GetFileName(p);
                    if (n.Equals(Mcpx, StringComparison.OrdinalIgnoreCase) || n.Equals("eeprom.bin", StringComparison.OrdinalIgnoreCase)) continue;
                    var len = new FileInfo(p).Length;
                    if (len >= 256 * 1024 && len % (64 * 1024) == 0) return p;
                }
            }
            catch { }
            return null;
        }
    }
}
