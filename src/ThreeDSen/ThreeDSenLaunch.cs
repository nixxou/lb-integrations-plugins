// What a launch of 3dSen is (docs\3dsen.md section 3, measured on 3dSen PC 1.1.2):
//
//   3dSen.exe -id=<profile id> -screen-fullscreen 1 -screen-width <w> -screen-height <h>
//
// - THE GAME IS A PROFILE ID, never a file: VRUIController.ProcessArgs reads "-id=" and runs that profile, whose ROM is the
//   romPath of rom.json (ThreeDSenRomJson writes it first). [run] -id=73 with Zelda II bound: the game starts by itself, in
//   3D. The game path LaunchBox puts on the line is taken OFF it (NewCommandLine replaces the whole line - Vita3kSaves'
//   measurement), with our own switches from an earlier line; anything else the user put on the platform's line is kept.
// - THE SCREEN is Unity's switches, and Unity REMEMBERS them in 3dSen's registry key (HKCU\Software\Geod Studio\3dSen,
//   "Screenmanager Fullscreen mode", width, height): [run] after one windowed launch a plain start was windowed, and
//   "-screen-fullscreen 1" alone gave a full screen at the remembered size, not the monitor's. So the full set is always
//   passed: full screen at the primary monitor's real resolution (EnumDisplaySettings: physical pixels whatever the host's
//   DPI awareness), or a window of the size chosen. "As 3dSen last had it" passes nothing.
// - ARCHIVES: 3dSen opens a .zip or a .7z itself but plays only its FIRST entry (Engine.LoadRom: lzip.entry2Buffer(path,
//   ninfo[0]) / lzma.decode2Buffer(path, ninfo[0]); anything else must start "NES\x1A"). [run] a .zip whose first entry is
//   the ROM plays as is. A .7z was read in the source only - not measured - so a .7z, and a .zip whose first entry is not
//   a .nes, are unpacked by the plugin into <3dSen>\Unpacked\<name>-<hash>\ (as AresLaunch / RaSnes9xLaunch; a state of
//   3dSen embeds the whole ROM, docs\3dsen.md section 6, so where the ROM lies changes nothing for the saves).
// - 3dSen RUNNING: every copy of 3dSen of the Windows user shares one data folder (LocalLow\Geod Studio\3dSen) and rewrites
//   rom.json and input.cfg there when it quits - a launch while any 3dSen runs would lose its binding. Refused.
// - QUITTING: WM_CLOSE is clean ([run] five times: exit code 0 in 252-313 ms, its files written) - ThreeDSenAhk.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using SharpCompress.Archives;

namespace LbIntegrations.ThreeDSen
{
    internal static class ThreeDSenLaunch
    {
        public const string ExecutableName = "3dSen.exe";
        private const string ProcessName = "3dSen";

        /// <summary>For the probe: another user data folder.</summary>
        internal static string DataDirOverride;

        public static bool IsExe(string path)
        {
            try { return string.Equals(Path.GetFileName((path ?? "").Trim().Trim('"')), ExecutableName, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        /// <summary>Any 3dSen running, from whatever folder: they all share one data folder.</summary>
        public static bool AnyRunning()
        {
            try
            {
                var ps = Process.GetProcessesByName(ProcessName);
                foreach (var p in ps) p.Dispose();
                return ps.Length > 0;
            }
            catch { return false; }
        }

        // ── the user data folder ─────────────────────────────────────────────

        /// <summary>Unity's Application.persistentDataPath for 3dSen: &lt;LocalLow&gt;\Geod Studio\3dSen - the company and
        /// product of 3dSen_Data\app.info (Info.Awake: DataPath = persistentDataPath). No setting of 3dSen moves it; the pack's
        /// in-process plugin does (Portable).</summary>
        public static string DataDir()
        {
            if (DataDirOverride != null) return DataDirOverride;
            return Path.Combine(LocalLow(), "Geod Studio", "3dSen");
        }

        // ── the portable mode (the in-process plugin, Mehdi 19/10) ──────────

        public const string PortableFolder = "portable", SavesFolder = "Saves", StatesFolder = "States";

        /// <summary>For the probe: the in-process plugin taken as there.</summary>
        internal static bool? PortableOverride;

        /// <summary>This 3dSen runs with the pack's plugin (BepInEx and the plugin in its folder, and the Nixx page's switch on):
        /// its data is &lt;install&gt;\portable, its states &lt;install&gt;\Saves\&lt;platform&gt;\States (the plugin's
        /// --nixx-data / --nixx-states).</summary>
        public static bool Portable(string exe)
        {
            if (PortableOverride != null) return PortableOverride.Value;
            try { return !string.IsNullOrWhiteSpace(exe) && ThreeDSenSettings.On("BepInEx") && ThreeDSenBepInEx.IsDeployed(Path.GetDirectoryName(Path.GetFullPath(exe))); }
            catch { return false; }
        }

        /// <summary>The data folder of THIS 3dSen: &lt;install&gt;\portable with the plugin, else LocalLow's.</summary>
        public static string DataDir(string exe)
            => Portable(exe) ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), PortableFolder) : DataDir();

        /// <summary>Where the states of a game of <paramref name="platform"/> are, sav.cfg with them (a folder named "States" holds
        /// the slot files itself - ThreeDSenSavCfg.StateDir): &lt;install&gt;\Saves\&lt;platform&gt;\States with the plugin,
        /// else 3dSen's data folder (sav.cfg there, the slots in its "sav").</summary>
        public static string StatesRoot(string exe, string platform)
            => Portable(exe) ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), SavesFolder, PlatformFolder(platform), StatesFolder) : DataDir();

        /// <summary>A platform's folder name: its name, the characters Windows refuses replaced; none: the NES's.</summary>
        public static string PlatformFolder(string platform)
        {
            var p = string.IsNullOrWhiteSpace(platform) ? ThreeDSenPlugin.NesPlatform : platform.Trim();
            var bad = Path.GetInvalidFileNameChars();
            var s = new string(p.Select(c => bad.Contains(c) ? '_' : c).ToArray()).TrimEnd('.', ' ');
            return s.Length == 0 ? ThreeDSenPlugin.NesPlatform : s;
        }

        /// <summary>The first launch in portable mode: what 3dSen has in LocalLow COPIED into the portable folder (its games'
        /// bindings, keys, settings, custom profiles, cover art) and its states into the NES's states folder - copied, never
        /// moved: LocalLow is every copy of 3dSen's, and stays as it was. Only when the portable folder has no rom.json yet.</summary>
        public static void FirstPortable(string exe)
        {
            try
            {
                var data = DataDir(exe);
                if (!Portable(exe) || File.Exists(Path.Combine(data, ThreeDSenRomJson.FileName))) return;
                var from = DataDir();
                Directory.CreateDirectory(data);
                if (!Directory.Exists(from)) { Log.Info("portable: 3dSen never ran under this Windows user - starting empty"); return; }
                int n = 0;
                foreach (var f in new[] { ThreeDSenRomJson.FileName, ThreeDSenInput.FileName, "setting.cfg" })
                    if (File.Exists(Path.Combine(from, f))) { File.Copy(Path.Combine(from, f), Path.Combine(data, f), false); n++; }
                foreach (var d in new[] { "CustomProfiles", "CoverArt" })
                    n += CopyTree(Path.Combine(from, d), Path.Combine(data, d));
                var states = StatesRoot(exe, ThreeDSenPlugin.NesPlatform);
                if (File.Exists(ThreeDSenSavCfg.PathIn(from)) && !File.Exists(ThreeDSenSavCfg.PathIn(states)))
                {
                    Directory.CreateDirectory(states);
                    File.Copy(ThreeDSenSavCfg.PathIn(from), ThreeDSenSavCfg.PathIn(states), false); n++;
                    var slots = ThreeDSenSavCfg.StateDir(from);
                    if (Directory.Exists(slots))
                        foreach (var f in Directory.GetFiles(slots)) { File.Copy(f, Path.Combine(ThreeDSenSavCfg.StateDir(states), Path.GetFileName(f)), false); n++; }
                }
                Log.Info("portable: " + n + " file(s) of LocalLow copied into " + data + " (LocalLow left as it was)");
            }
            catch (Exception ex) { Log.Warn("portable: LocalLow could not be copied", ex); }
        }

        private static int CopyTree(string from, string to)
        {
            if (!Directory.Exists(from)) return 0;
            int n = 0;
            foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(to, Path.GetRelativePath(from, f));
                if (File.Exists(target)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(f, target); n++;
            }
            return n;
        }

        /// <summary>The in-process plugin's switches for a launch: the folders, the keys, a game's 3dSen settings. None without it.</summary>
        public static List<string> PluginSwitches(string exe, string platform, Func<string, string> valueOf)
        {
            var list = new List<string>();
            if (!Portable(exe)) return list;
            list.Add("--nixx-data=" + DataDir(exe));
            list.Add("--nixx-states=" + StatesRoot(exe, platform));
            list.Add("--nixx-esc");
            list.Add("--nixx-quit-confirm=" + (ThreeDSenSettings.On("QuitConfirm") ? "on" : "off"));
            list.Add("--nixx-slots=" + (ThreeDSenSettings.On("Slots") ? "on" : "off"));
            if (ThreeDSenSettings.On("PluginLog")) list.Add("--nixx-log");
            list.AddRange(ThreeDSenSettings.SetArgs(valueOf));
            return list;
        }

        private static readonly Guid LocalAppDataLow = new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16");

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        private static string LocalLow()
        {
            try
            {
                if (SHGetKnownFolderPath(LocalAppDataLow, 0, IntPtr.Zero, out var p) == 0)
                    try { return Marshal.PtrToStringUni(p); } finally { Marshal.FreeCoTaskMem(p); }
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow");
        }

        // ── the screen ───────────────────────────────────────────────────────

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
        private static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE dm);

        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        private static readonly IntPtr PerMonitorAwareV2 = new IntPtr(-4);   // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2

        /// <summary>The primary monitor's resolution in real pixels, or null. Windows may answer a thread that is not DPI
        /// aware in scaled pixels (DPI virtualisation - not measured), so the call
        /// is made with the thread per-monitor aware, then put back. [run] 18/10: 1920 x 1080, as Win32_VideoController and
        /// Screen.PrimaryScreen say for that display.</summary>
        public static (int W, int H)? PrimaryResolution()
        {
            IntPtr previous = IntPtr.Zero;
            try
            {
                try { previous = SetThreadDpiAwarenessContext(PerMonitorAwareV2); } catch { }   // Windows 10 1607 and later
                var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                if (EnumDisplaySettings(null, -1 /* ENUM_CURRENT_SETTINGS */, ref dm) && dm.dmPelsWidth > 0 && dm.dmPelsHeight > 0)
                    return (dm.dmPelsWidth, dm.dmPelsHeight);
            }
            catch { }
            finally { try { if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous); } catch { } }
            return null;
        }

        /// <summary>What Unity remembers in 3dSen's registry key: "full screen 2560 x 1440", "window 1280 x 720"; null when
        /// 3dSen never ran. Value names carry a hash suffix ("Screenmanager Fullscreen mode_h3630240806", read 18/10).</summary>
        public static string Remembered()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Geod Studio\3dSen");
                if (key == null) return null;
                int? Get(string name)
                {
                    var n = key.GetValueNames().FirstOrDefault(v => v.StartsWith(name + "_h", StringComparison.Ordinal));
                    return n == null ? null : key.GetValue(n) is int i ? i : (int?)null;
                }
                var mode = Get("Screenmanager Fullscreen mode");
                var w = Get("Screenmanager Resolution Width");
                var h = Get("Screenmanager Resolution Height");
                if (mode == null) return null;
                // Unity's FullScreenMode: 0 exclusive, 1 full screen window, 2 maximized window, 3 windowed.
                var what = mode == 3 ? "window" : mode == 2 ? "maximized window" : "full screen";
                return what + (w != null && h != null ? " " + w + " x " + h : "");
            }
            catch { return null; }
        }

        // ── the command line ─────────────────────────────────────────────────

        private static readonly string[] OurValued = { "-screen-fullscreen", "-screen-width", "-screen-height" };

        /// <summary>The switches a launch passes, in order: -id=, then the screen's.</summary>
        public static List<string> Switches(string profileId, string screen, string windowSize)
        {
            var list = new List<string> { "-id=" + profileId };
            if (screen == "full")
            {
                var r = PrimaryResolution();
                list.Add("-screen-fullscreen"); list.Add("1");
                if (r != null) { list.Add("-screen-width"); list.Add(r.Value.W.ToString()); list.Add("-screen-height"); list.Add(r.Value.H.ToString()); }
            }
            else if (screen == "window")
            {
                var (w, h) = SizeOf(windowSize);
                list.Add("-screen-fullscreen"); list.Add("0");
                list.Add("-screen-width"); list.Add(w.ToString()); list.Add("-screen-height"); list.Add(h.ToString());
            }
            return list;
        }

        internal static (int W, int H) SizeOf(string size)
        {
            var parts = (size ?? "").Split('x');
            return parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h) && w > 0 && h > 0 ? (w, h) : (1280, 720);
        }

        /// <summary>The host's line made 3dSen's: the game path and our earlier switches out, ours first, the user's kept.</summary>
        public static string CommandLine(string current, string romPath, IEnumerable<string> switches)
        {
            var t = Tokenize(current);
            string Full(string a) { try { return Path.GetFullPath(a.Replace('/', '\\')); } catch { return a; } }
            var rom = string.IsNullOrWhiteSpace(romPath) ? null : Full(romPath);
            var kept = new List<string>();
            for (int i = 0; i < t.Count; i++)
            {
                var a = t[i];
                if (a.StartsWith("-id=", StringComparison.OrdinalIgnoreCase) || a.StartsWith("--nixx-", StringComparison.OrdinalIgnoreCase)) continue;
                if (OurValued.Any(o => string.Equals(o, a, StringComparison.OrdinalIgnoreCase))) { if (i + 1 < t.Count && !t[i + 1].StartsWith("-")) i++; continue; }
                if (rom != null && !a.StartsWith("-") && string.Equals(Full(a), rom, StringComparison.OrdinalIgnoreCase)) continue;
                kept.Add(a);
            }
            // Spelt otherwise by the host (an extracted copy, another form): the last token that is a path to an existing file.
            if (rom != null && kept.Count > 0 && !kept[kept.Count - 1].StartsWith("-") && LooksLikeFile(kept[kept.Count - 1])) kept.RemoveAt(kept.Count - 1);
            return string.Join(" ", (switches ?? Enumerable.Empty<string>()).Concat(kept).Select(Quote));
        }

        private static bool LooksLikeFile(string a)
        {
            try { return (a.Contains('\\') || a.Contains('/')) && File.Exists(a); } catch { return false; }
        }

        private static string Quote(string a) => a.Length == 0 || a.Any(char.IsWhiteSpace) || a.Contains('"') ? "\"" + a.Replace("\"", "\\\"") + "\"" : a;

        /// <summary>A command line cut as Windows does for a C program: spaces apart, quotes kept together.</summary>
        internal static List<string> Tokenize(string line)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(line)) return list;
            var cur = new StringBuilder();
            bool quoted = false, any = false;
            foreach (var c in line)
            {
                if (c == '"') { quoted = !quoted; any = true; continue; }
                if (char.IsWhiteSpace(c) && !quoted) { if (any || cur.Length > 0) list.Add(cur.ToString()); cur.Clear(); any = false; continue; }
                cur.Append(c);
            }
            if (any || cur.Length > 0) list.Add(cur.ToString());
            return list;
        }

        // ── the file 3dSen is given ──────────────────────────────────────────

        /// <summary>What rom.json should name for <paramref name="rom"/>: the file itself (a .nes, a .zip whose first entry is
        /// the .nes), else the .nes unpacked from the archive. Null with <paramref name="problem"/> said when it cannot be played.</summary>
        public static string RomFor(string exe, string rom, out string problem)
        {
            problem = null;
            var ext = Path.GetExtension(rom ?? "").ToLowerInvariant();
            if (ext != ".zip" && ext != ".7z") return rom;
            try
            {
                using var archive = ArchiveFactory.Open(rom);
                var entries = archive.Entries.ToList();
                bool IsNes(IArchiveEntry e) => !e.IsDirectory && string.Equals(Path.GetExtension(e.Key ?? ""), ".nes", StringComparison.OrdinalIgnoreCase);
                if (ext == ".zip" && entries.Count > 0 && IsNes(entries[0])) return rom;   // what 3dSen plays itself (measured)

                var roms = entries.Where(IsNes).ToList();
                var name = Path.GetFileNameWithoutExtension(rom);
                var pick = roms.Count == 1 ? roms[0] : roms.FirstOrDefault(e => string.Equals(Path.GetFileNameWithoutExtension(e.Key), name, StringComparison.OrdinalIgnoreCase));
                if (pick == null)
                {
                    problem = roms.Count == 0 ? "there is no .nes file in " + Path.GetFileName(rom)
                            : Path.GetFileName(rom) + " holds " + roms.Count + " .nes files and none is named after the archive - which one to play cannot be told. "
                              + "Keep one ROM per archive, or name the one to play \"" + name + ".nes\".";
                    return null;
                }
                var dir = UnpackDir(exe, rom);
                var target = Path.Combine(dir, name + ".nes");
                if (File.Exists(target) && new FileInfo(target).Length == pick.Size && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(rom))
                    return target;
                Directory.CreateDirectory(dir);
                foreach (var old in Directory.GetFiles(dir, "*.nes")) try { File.Delete(old); } catch { }
                using (var src = pick.OpenEntryStream())
                using (var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                    LbIntegrations.Lbip.LbipLaunchStatus.CopyStream(src, dst, pick.Size, "Extracting " + Path.GetFileName(pick.Key) + "…");
                Log.Info("unpacked " + pick.Key + " from " + Path.GetFileName(rom) + " -> " + target);
                return target;
            }
            catch (Exception ex)
            {
                Log.Warn("could not open " + rom, ex);
                problem = Path.GetFileName(rom) + " could not be opened: " + ex.Message;
                return null;
            }
        }

        /// <summary>One folder per archive path, under the install: two archives of one name never share it.</summary>
        public static string UnpackDir(string exe, string rom)
        {
            var name = Path.GetFileNameWithoutExtension(rom);
            using var md5 = System.Security.Cryptography.MD5.Create();
            var hash = Convert.ToHexString(md5.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(rom).ToLowerInvariant()))).Substring(0, 8).ToLowerInvariant();
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), "Unpacked", name + "-" + hash);
        }
    }
}
