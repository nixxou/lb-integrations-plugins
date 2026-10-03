// Where a Cxbx-Reloaded install keeps things, read the way Cxbx-Reloaded reads it (src/common/Settings.cpp,
// FilePaths.cpp, master of April 2026).
//
//   settings.ini   beside the executable first, then %APPDATA%\Cxbx-Reloaded\settings.ini. NEITHER: the first start
//                  asks "Use Cxbx-Reloaded in Portable Mode?" - so an install of ours puts an empty one beside the
//                  executable, which answers "portable" before the question is asked.
//   the data       [gui] DataStorageToggle in that file: 0 %APPDATA%\Cxbx-Reloaded, 1 the executable's folder,
//                  2 [gui] DataCustomLocation. Absent, it is where the file was found.
//   EmuDisk        <data>\EmuDisk\Partition<N> - the Xbox hard disk, one folder per partition. Partition1 is E:,
//                  and a game's saves are E:\UDATA\<title id>.
//
// A GAME IS LAUNCHED WITH THE LOADER, cxbxr-ldr.exe, given /load "<xbe>" (Mehdi, 03/10) - the entry is kept on it. The
// GUI, cxbx.exe, is what LaunchBox's "Open" menu opens instead (CxbxOpenRedirect), for Cxbx-Reloaded's settings. An entry
// still on cxbx.exe launches too: "<xbe>" as its first word, its window closed with the game.
// cxbx.exe given /load, which is LaunchBox's own Cxbx-Reloaded row, answers "Emulation must be launched from
// cxbxr-ldr.exe!".

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace LbIntegrations.Cxbx
{
    internal static class CxbxPaths
    {
        public const string Loader = "cxbxr-ldr.exe";
        public const string Gui = "cxbx.exe";
        public const string SettingsFile = "settings.ini";
        public const string VersionFile = "lbip-version.txt";

        public static bool IsLoader(string path) => NameIs(path, Loader);

        /// <summary>The GUI (Mehdi, 03/10): a game launched through it runs inside its window, full screen at once, and
        /// Alt+Enter brings the window - and its menus - back. Measured on GTA 03/10: cxbx.exe "&lt;xbe&gt;" /df "&lt;rom&gt;"
        /// starts the game; when the game stops the GUI stays open (CxbxSession closes it).</summary>
        public static bool IsGui(string path) => NameIs(path, Gui);

        /// <summary>Either of the two this plugin launches.</summary>
        public static bool IsCxbx(string path) => IsLoader(path) || IsGui(path);

        /// <summary>The emulator entry's path, made to name the executable the "launch with the window" option wants -
        /// the same folder, the same spelling (relative or not), only the file name changed, and only when that file is
        /// there. LaunchBox runs the entry's executable and a plugin cannot choose another at launch, so the option IS
        /// the entry's path (Mehdi, 03/10). Null when nothing is to change.</summary>
        public static string PathForMode(string entryPath, string fullPath, bool gui)
        {
            try
            {
                if (!IsCxbx(entryPath) || string.IsNullOrWhiteSpace(fullPath)) return null;
                var wanted = gui ? Gui : Loader;
                if (string.Equals(Path.GetFileName(entryPath.Trim().Trim('"')), wanted, StringComparison.OrdinalIgnoreCase)) return null;
                if (!File.Exists(Path.Combine(Path.GetDirectoryName(fullPath) ?? "", wanted))) return null;
                var dir = Path.GetDirectoryName(entryPath.Trim().Trim('"'));
                return string.IsNullOrEmpty(dir) ? wanted : Path.Combine(dir, wanted);
            }
            catch { return null; }
        }

        private static bool NameIs(string path, string name)
        {
            try { return !string.IsNullOrWhiteSpace(path) && string.Equals(Path.GetFileName(path.Trim().Trim('"')), name, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        /// <summary>The loader in <paramref name="dir"/> or one level down (a zip with a folder in it), or null.</summary>
        public static string FindLoader(string dir)
        {
            try
            {
                var direct = Path.Combine(dir, Loader);
                if (File.Exists(direct)) return direct;
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    var p = Path.Combine(sub, Loader);
                    if (File.Exists(p)) return p;
                }
            }
            catch { }
            return null;
        }

        public static string AppDataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cxbx-Reloaded");

        /// <summary>The settings.ini Cxbx-Reloaded would read for this install, or null when it has none yet.</summary>
        public static string SettingsOf(string exe)
        {
            var dir = Path.GetDirectoryName(exe) ?? "";
            var beside = Path.Combine(dir, SettingsFile);
            if (File.Exists(beside)) return beside;
            var roaming = Path.Combine(AppDataDir, SettingsFile);
            return File.Exists(roaming) ? roaming : null;
        }

        /// <summary>Its data folder - where EmuDisk is. Null when Cxbx-Reloaded has never been set up.</summary>
        public static string DataDir(string exe)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(exe)) return null;
                var settings = SettingsOf(exe);
                if (settings == null) return null;
                bool beside = string.Equals(Path.GetDirectoryName(settings), Path.GetDirectoryName(exe), StringComparison.OrdinalIgnoreCase);
                var ini = ReadIni(settings);
                int toggle = beside ? 1 : 0;
                // Written in hex by Cxbx-Reloaded's SimpleIni ("0x1"); decimal read too.
                if (ini.TryGetValue("gui|DataStorageToggle", out var t))
                {
                    t = t.Trim();
                    if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                            ? int.TryParse(t.Substring(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var v)
                            : int.TryParse(t, out v))
                        toggle = v;
                }
                switch (toggle)
                {
                    case 1: return Path.GetDirectoryName(exe);
                    case 2: return ini.TryGetValue("gui|DataCustomLocation", out var c) && !string.IsNullOrWhiteSpace(c) ? c : null;
                    default: return AppDataDir;
                }
            }
            catch (Exception ex) { Log.Warn("could not read where Cxbx-Reloaded keeps its data", ex); return null; }
        }

        /// <summary>E:\UDATA - where every game's saves are.</summary>
        public static string UdataDir(string exe)
        {
            var data = DataDir(exe);
            return data == null ? null : Path.Combine(data, "EmuDisk", "Partition1", "UDATA");
        }

        /// <summary>The CI tag this plugin installed (CI-&lt;sha7&gt;) - neither executable carries a version a plugin can read.</summary>
        public static string InstalledTag(string exe)
        {
            try
            {
                var f = Path.Combine(Path.GetDirectoryName(exe) ?? "", VersionFile);
                return File.Exists(f) ? File.ReadAllText(f).Trim() : null;
            }
            catch { return null; }
        }

        /// <summary>Is a Cxbx-Reloaded loader running - any: one data folder takes one at a time.</summary>
        public static bool LoaderRunning()
        {
            try
            {
                var ps = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Loader));
                foreach (var p in ps) p.Dispose();
                return ps.Length > 0;
            }
            catch { return false; }
        }

        /// <summary>The Visual C++ 2015-2022 runtime, 32-bit - Cxbx-Reloaded is a 32-bit program.</summary>
        public static bool VcRuntimeX86()
        {
            try
            {
                using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                                          .OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X86"))
                    if (k?.GetValue("Installed") is int i && i == 1) return true;
            }
            catch { }
            try
            {
                var wow = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
                return File.Exists(Path.Combine(wow, "vcruntime140.dll")) && File.Exists(Path.Combine(wow, "msvcp140.dll"));
            }
            catch { return false; }
        }

        /// <summary>"section|key" -> value, as SimpleIni writes it ("Key = value").</summary>
        public static Dictionary<string, string> ReadIni(string path)
        {
            var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string section = "";
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[' && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2).Trim(); continue; }
                var at = line.IndexOf('=');
                if (at > 0) v[section + "|" + line.Substring(0, at).Trim()] = line.Substring(at + 1).Trim();
            }
            return v;
        }
    }
}
