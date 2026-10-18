// What a launch writes into fceux.cfg, and what it hands FCEUX on its line.
//
// FCEUX READS fceux.cfg WHEN IT STARTS AND WRITES IT BACK WHOLE WHEN IT QUITS (FceuxCfg), so everything the plugin wants is
// written BEFORE the launch, in one pass, and only when FCEUX is not running from that folder:
//
//   the folders     the game's platform's (FceuxSavePaths) - what FCEUX had in its own folders for this game moved there
//   the screen      "fs" 1 / 0, the plugin's option (FceuxSettings "Fullscreen"). FCEUX's Windows build has NO full-screen
//                   switch on its line (drivers\win\args.cpp: -nofs is commented out). "fs" is applied at start only when a
//                   ROM is on the line (main.cpp:788), which is always the case here.
//   the mode        "vmcx" / "vmcy" / "vmcb" = the display's current mode when they are 0. MEASURED 18/10: on a first-run
//                   fceux.cfg, "fs" 1 fails at start with the modal "Horizontal resolution is too low." (the window stays
//                   blank). Why, read in drivers\win\video.cpp: SetVideoMode(1) calls RecalcCustom() before DirectDraw
//                   exists, and ResetCustomMode - which copies the current display mode into custom mode 0 - returns early
//                   while lpDD7 is null, so the mode stays 0x0. One Alt+Enter from a window (DirectDraw then up) wrote
//                   2560 / 1440 / 32, the primary display's mode: what is written here, read the same way
//                   (EnumDisplaySettings on the primary display, which DirectDrawCreateEx(NULL) is). A mode the user set
//                   in FCEUX (not 0) is kept.
//   the hotkeys     the pack's, where FCEUX lets them be (FceuxHotkeys)
//
// FULL SCREEN IS EXCLUSIVE DirectDraw (video.cpp:440-447, DDSCL_EXCLUSIVE | DDSCL_FULLSCREEN + SetDisplayMode). It could not
// be validated on the machine this was written on (a virtual-display VM - docs\fceux.md §5): it is Mehdi's test.
//
// THE LINE: the host's (the game's quoted path), nothing added - every per-game switch FCEUX has (-pal, -dendy, -gg,
// -no8lim, -clipsides) is the game's window's, later. Two things are checked before it goes:
//   - the path must be ANSI: FCEUX's Windows build takes char arguments (main(int argc, char *argv[])). MEASURED 18/10:
//     "Zelda é.nes" loads and saves, a Japanese name opens a modal "Error opening ...??????.nes!". Refused with a notice.
//   - an archive whose ROM is a .unf / .unif: FCEUX looks only at .nes, .fds and .nsf inside an archive (fceu.cpp:439) - the
//     ROM is unpacked under its own name into <FCEUX>\Unpacked\<archive>-<hash>\ (a stable folder of the install, never
//     %TEMP%) and the line pointed at it; its saves take the ROM's name as they would have from the archive.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace LbIntegrations.Fceux
{
    internal static class FceuxLaunch
    {
        /// <summary>The classic Windows build: 64-bit (what the plugin installs) and 32-bit (LaunchBox's own FCEUX row) - the same
        /// code, the same fceux.cfg (measured: first-run files identical but for RAMInitSeed). Not the Qt build's qfceux.exe.</summary>
        public static readonly string[] ExecutableNames = { "fceux64.exe", "fceux.exe" };
        public const string ExecutableName = "fceux64.exe";
        public const string UnpackedFolder = "Unpacked";

        public static bool IsFceuxExe(string path)
        {
            try
            {
                var name = Path.GetFileName((path ?? "").Trim().Trim('"'));
                return ExecutableNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        /// <summary>An FCEUX running from that folder (either build): an edit of fceux.cfg now would be lost when it quits.</summary>
        public static bool Running(string exe)
        {
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(exe));
                foreach (var name in ExecutableNames)
                    foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name)))
                        using (p)
                            try { if (string.Equals(Path.GetDirectoryName(p.MainModule?.FileName ?? ""), dir, StringComparison.OrdinalIgnoreCase)) return true; } catch { }
            }
            catch { }
            return false;
        }

        // ── fceux.cfg ────────────────────────────────────────────────────────

        /// <summary>The folders, the screen and the hotkeys written into fceux.cfg for this game, then its files in FCEUX's own
        /// folders moved into its platform's. Never throws: a launch with the file as it was beats none.</summary>
        public static void PrepareConfig(string exe, string gameId, string platform, string rom)
        {
            try
            {
                var cfg = FceuxCfg.Load(FceuxCfg.Of(exe));
                var fresh = !cfg.Existed;
                var emptied = FceuxSavePaths.Apply(cfg, exe, platform);

                bool full = FceuxSettings.BoolFor(gameId, "Fullscreen");
                cfg.SetInt(FceuxCfg.FullScreen, full ? 1 : 0);
                string mode = null;
                if (full && ((cfg.GetInt(FceuxCfg.ModeWidth) ?? 0) == 0 || (cfg.GetInt(FceuxCfg.ModeHeight) ?? 0) == 0))
                {
                    var m = DisplayMode();
                    if (m.Width > 0 && m.Height > 0)
                    {
                        cfg.SetInt(FceuxCfg.ModeWidth, m.Width);
                        cfg.SetInt(FceuxCfg.ModeHeight, m.Height);
                        cfg.SetInt(FceuxCfg.ModeBits, m.Bits > 0 ? m.Bits : 32);
                        mode = m.Width + "x" + m.Height + "x" + (m.Bits > 0 ? m.Bits : 32);
                    }
                    else Log.Warn("the display's mode could not be read - FCEUX's full screen has no mode and will refuse to start in it");
                }

                foreach (var line in FceuxHotkeys.Bind(cfg)) Log.Info(line);

                bool changed = cfg.Changed;
                cfg.SaveIfChanged();
                Log.Info("fceux.cfg: " + (changed ? (fresh ? "made" : "written") : "already as wanted") + " - " + (full ? "full screen" : "windowed")
                         + (mode != null ? ", full-screen mode " + mode + " (the display's)" : "")
                         + (emptied.Count > 0 ? ", " + string.Join(", ", emptied) + " were FCEUX's own folders" : "")
                         + ", folders of " + FceuxSystems.FolderOf(platform));
                // Only now that FCEUX will read the new folders from its file (as RASnes9x: moved before a save that failed,
                // they would be out of its sight).
                FceuxSavePaths.Migrate(exe, rom, cfg);
            }
            catch (Exception ex) { Log.Warn("fceux.cfg could not be written - the game starts with it as it was", ex); }
        }

        // ── the display's mode ──

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettingsW(string deviceName, int modeNum, ref DEVMODE devMode);

        private const int EnumCurrentSettings = -1;

        /// <summary>The primary display's current mode, in physical pixels (what DirectDraw's GetDisplayMode gives); 0s when
        /// it cannot be read.</summary>
        internal static (int Width, int Height, int Bits) DisplayMode()
        {
            try
            {
                var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                if (EnumDisplaySettingsW(null, EnumCurrentSettings, ref dm)) return (dm.dmPelsWidth, dm.dmPelsHeight, dm.dmBitsPerPel);
            }
            catch (Exception ex) { Log.Warn("EnumDisplaySettings", ex); }
            return (0, 0, 0);
        }

        // ── the line ─────────────────────────────────────────────────────────

        /// <summary>The file FCEUX is given for this game - the game itself, or its .unf unpacked from an archive (see the
        /// header) - or null with <paramref name="problem"/> said.</summary>
        public static string RomFor(string rom, string exe, out string problem)
        {
            problem = null;
            if (string.IsNullOrWhiteSpace(rom) || !Archives.IsArchive(rom)) return rom;
            var (seen, unif) = Archives.RomEntries(rom);
            if (seen == null || seen.Count > 0 || unif == null || unif.Count == 0) return rom;     // FCEUX's to open (or to say what is wrong)
            if (unif.Count > 1)
            {
                problem = Path.GetFileName(rom) + " holds " + unif.Count + " UNIF ROMs and FCEUX looks only at .nes, .fds and .nsf files inside an archive. "
                        + "Keep one ROM per archive.";
                return null;
            }
            try
            {
                var entry = unif[0];
                var target = Path.Combine(UnpackDir(exe, rom), Path.GetFileName(entry.Replace('\\', '/')));
                if (!Archives.Unpack(rom, entry, target)) { problem = Path.GetFileName(rom) + " could not be read."; return null; }
                Log.Info("unpacked " + entry + " from " + Path.GetFileName(rom) + " -> " + target + " (FCEUX does not look for UNIF ROMs inside an archive)");
                return target;
            }
            catch (Exception ex)
            {
                Log.Warn("could not unpack " + rom, ex);
                problem = Path.GetFileName(rom) + " could not be opened: " + ex.Message;
                return null;
            }
        }

        /// <summary>One folder per archive PATH under the install: two archives of the same name never share it.</summary>
        internal static string UnpackDir(string exe, string rom)
        {
            string hash;
            using (var md5 = System.Security.Cryptography.MD5.Create())
                hash = Convert.ToHexString(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(rom).ToLowerInvariant()))).Substring(0, 8).ToLowerInvariant();
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), UnpackedFolder, Path.GetFileNameWithoutExtension(rom) + "-" + hash);
        }

        /// <summary>The host's line with its game path swapped for <paramref name="newRom"/>.</summary>
        public static string SwapRom(string line, string oldRom, string newRom)
        {
            line = line ?? "";
            foreach (var form in new[] { "\"" + oldRom + "\"", oldRom })
            {
                int at = line.LastIndexOf(form, StringComparison.OrdinalIgnoreCase);
                if (at >= 0) return line.Substring(0, at) + "\"" + newRom + "\"" + line.Substring(at + form.Length);
            }
            var m = System.Text.RegularExpressions.Regex.Match(line, "\"[^\"]*\"\\s*$");
            if (m.Success) return line.Substring(0, m.Index) + "\"" + newRom + "\"";
            var cut = line.TrimEnd().LastIndexOf(' ');
            return cut < 0 ? "\"" + newRom + "\"" : line.Substring(0, cut) + " \"" + newRom + "\"";
        }
    }
}
