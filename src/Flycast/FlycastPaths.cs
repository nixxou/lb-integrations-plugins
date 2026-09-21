// Where Flycast keeps everything, resolved from the emulator's executable path.
//
// Flycast is FAR simpler than PPSSPP here, and that is measured rather than hoped. On Windows,
// core/windows/winmain.cpp's setupPath() does, unconditionally:
//
//     set_user_config_dir(<dir of exe>\)          -> emu.cfg lives beside the executable
//     add_system_data_dir(<dir of exe>\)
//     set_user_data_dir(<dir of exe>\data\)       -> everything written goes here
//
// There is no %APPDATA% branch, no Documents fallback, no installed.txt to interpret: a Windows
// Flycast is always portable. The only UWP branch is irrelevant to a LaunchBox install.
//
// Five settings in emu.cfg can redirect parts of that, all under the [config] section with the
// "Dreamcast." prefix (core/cfg/option.cpp:143-151):
//
//     Dreamcast.BiosPath        a LIST of folders searched for BIOS files, before data\
//     Dreamcast.VMUPath         one folder for VMU files
//     Dreamcast.SavestatePath   a LIST of folders for save states
//     Dreamcast.SavePath        one folder for arcade NVRAM/EEPROM/card files
//     Dreamcast.MappingsPath    a LIST of folders for input mappings - whose fallback is NOT
//                               data\ but <exe dir>\mappings, because Flycast resolves it
//                               with get_writable_config_path, not the user data dir
//
// Each is honoured here the way Flycast honours it: a redirect wins for reading AND writing when
// set, and data\ is the fallback. A list is searched in order.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Flycast
{
    /// <summary>Every folder that matters for one Flycast installation.</summary>
    internal sealed class FlycastLayout
    {
        /// <summary>The folder holding the executable. Empty when it could not be resolved.</summary>
        public string InstallDir = "";
        /// <summary>&lt;install&gt;\emu.cfg — the configuration, beside the executable, not under data\.</summary>
        public string ConfigFile = "";
        /// <summary>&lt;install&gt;\data — where Flycast writes saves, states, NVRAM and where it looks
        /// for BIOS by default.</summary>
        public string DataDir = "";

        /// <summary>Folders to search for a VMU file, most specific first.</summary>
        public List<string> VmuDirs = new List<string>();
        /// <summary>Folders to search for arcade NVRAM / EEPROM / card files, most specific first.</summary>
        public List<string> ArcadeDirs = new List<string>();
        /// <summary>Folders to search for save states, most specific first.</summary>
        public List<string> StateDirs = new List<string>();
        /// <summary>Folders to search for BIOS files, most specific first.</summary>
        public List<string> BiosDirs = new List<string>();
        /// <summary>Folders holding controller and keyboard mappings, most specific first. NOT under
        /// data\ like the others: Flycast resolves these with get_writable_config_path("mappings/"),
        /// which for a portable install is &lt;install&gt;\mappings.</summary>
        public List<string> MappingsDirs = new List<string>();

        /// <summary>Flycast's PerGameVmu, default TRUE (core/cfg/option.cpp:234). When false, even
        /// port A1 falls back to the shared vmu_save_A1.bin and no save can be attributed to a game.</summary>
        public bool PerGameVmu = true;

        /// <summary>Human-readable account of how the above was decided, for the log.</summary>
        public string Reason = "";

        /// <summary>The folder a NEW file of this kind should be written to.</summary>
        public string PrimaryVmuDir => VmuDirs.FirstOrDefault() ?? DataDir;
        public string PrimaryArcadeDir => ArcadeDirs.FirstOrDefault() ?? DataDir;
        public string PrimaryStateDir => StateDirs.FirstOrDefault() ?? DataDir;

        /// <summary>Where a mapping file we write must go. Falls back beside the executable, not into
        /// data\ - see MappingsDirs.</summary>
        public string PrimaryMappingsDir => MappingsDirs.FirstOrDefault()
                                            ?? System.IO.Path.Combine(InstallDir, MappingsDirName);

        public const string MappingsDirName = "mappings";
    }

    internal static class FlycastPaths
    {
        /// <summary>Executable names Flycast ships on Windows. Upstream's release zip carries exactly
        /// one, "flycast.exe"; the others are kept because forks and older builds used them and
        /// claiming an emulator must not depend on a rename.</summary>
        public static readonly string[] ExecutableNames =
        {
            "flycast.exe",
            "flycast-dbg.exe",
            "reicast.exe",
        };

        public const string ConfigFileName = "emu.cfg";
        public const string DataDirName = "data";

        private const string Section = "config";
        private const string KeyBiosPath = "Dreamcast.BiosPath";
        private const string KeyVmuPath = "Dreamcast.VMUPath";
        private const string KeyStatePath = "Dreamcast.SavestatePath";
        private const string KeySavePath = "Dreamcast.SavePath";
        private const string KeyMappingsPath = "Dreamcast.MappingsPath";
        private const string KeyPerGameVmu = "PerGameVmu";

        /// <summary>Is this path one of Flycast's executables? Matched on the file name, never on the
        /// folder: a user is free to install it anywhere, under any folder name.</summary>
        public static bool IsFlycastExecutable(string applicationPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(applicationPath)) return false;
                var name = Path.GetFileName(applicationPath);
                return name != null
                    && ExecutableNames.Any(e => string.Equals(e, name, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        /// <summary>Find Flycast's executable inside an install folder, most likely name first.</summary>
        public static string FindExecutable(string installDir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir)) return null;
                foreach (var name in ExecutableNames)
                {
                    var candidate = Path.Combine(installDir, name);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch { }
            return null;
        }

        /// <summary>Resolve every folder from the executable path. Never throws; a failure yields a
        /// layout whose folders are empty, which every caller already treats as "nothing here".</summary>
        public static FlycastLayout Resolve(string applicationPath)
        {
            var layout = new FlycastLayout();
            try { layout.InstallDir = Path.GetDirectoryName(Path.GetFullPath(applicationPath ?? "")) ?? ""; }
            catch { layout.InstallDir = ""; }

            if (layout.InstallDir.Length == 0)
            {
                layout.Reason = "no install directory could be derived from the executable path";
                return layout;
            }

            layout.ConfigFile = Path.Combine(layout.InstallDir, ConfigFileName);
            layout.DataDir = Path.Combine(layout.InstallDir, DataDirName);

            var cfg = FlycastIni.Read(layout.ConfigFile, Section,
                                      KeyBiosPath, KeyVmuPath, KeyStatePath, KeySavePath,
                                      KeyMappingsPath, KeyPerGameVmu);

            layout.PerGameVmu = cfg.TryGetValue(KeyPerGameVmu, out var pgv)
                ? FlycastIni.AsBool(pgv, true)
                : true;

            layout.VmuDirs = Redirected(cfg, KeyVmuPath, layout.DataDir);
            layout.ArcadeDirs = Redirected(cfg, KeySavePath, layout.DataDir);
            layout.StateDirs = Redirected(cfg, KeyStatePath, layout.DataDir);
            layout.BiosDirs = Redirected(cfg, KeyBiosPath, layout.DataDir);

            // The odd one out: Flycast writes mappings beside the executable, not under data\.
            layout.MappingsDirs = Redirected(cfg, KeyMappingsPath,
                                             Path.Combine(layout.InstallDir, FlycastLayout.MappingsDirName));

            layout.Reason = File.Exists(layout.ConfigFile)
                ? "portable install, emu.cfg read"
                : "portable install, no emu.cfg yet (Flycast writes one on first run)";
            return layout;
        }

        /// <summary>The folders for one setting: its configured value(s) first, then data\ — which is
        /// what Flycast falls back to. Duplicates and blanks are dropped, order preserved.</summary>
        private static List<string> Redirected(IDictionary<string, string> cfg, string key, string dataDir)
        {
            var dirs = new List<string>();
            if (cfg.TryGetValue(key, out var raw))
                foreach (var part in SplitList(raw))
                    Add(dirs, part);
            Add(dirs, dataDir);
            return dirs;
        }

        /// <summary>A list setting as Flycast stores it, parsed the way Flycast parses it
        /// (core/cfg/option.h, the std::vector&lt;std::string&gt; doLoad/doSave pair).
        ///
        /// Entries are joined with ';'. An entry that CONTAINS a ';', or starts with a '"', is written
        /// quoted with its inner quotes doubled - so a naive Split(';') would cut a legitimate path in
        /// half. Rare, but it is the format, and a half path is worse than no path.
        /// A single-value setting simply yields one entry.</summary>
        private static IEnumerable<string> SplitList(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) yield break;

            int i = 0;
            while (i < raw.Length)
            {
                string entry;
                if (raw[i] == '"')
                {
                    var sb = new System.Text.StringBuilder();
                    i++;                                   // past the opening quote
                    while (i < raw.Length)
                    {
                        if (raw[i] != '"') { sb.Append(raw[i++]); continue; }
                        if (i + 1 < raw.Length && raw[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                        i++;                               // closing quote
                        break;
                    }
                    entry = sb.ToString();
                    if (i < raw.Length && raw[i] == ';') i++;
                }
                else
                {
                    int semi = raw.IndexOf(';', i);
                    if (semi < 0) { entry = raw.Substring(i); i = raw.Length; }
                    else { entry = raw.Substring(i, semi - i); i = semi + 1; }
                }

                var t = entry.Trim();
                if (t.Length > 0) yield return t;
            }
        }

        private static void Add(List<string> dirs, string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            string full;
            try { full = Path.GetFullPath(dir); } catch { return; }
            if (!dirs.Any(d => string.Equals(d, full, StringComparison.OrdinalIgnoreCase)))
                dirs.Add(full);
        }

        /// <summary>The first existing file of this name across a list of folders, or null.</summary>
        public static string FirstExisting(IEnumerable<string> dirs, string fileName)
        {
            if (dirs == null || string.IsNullOrEmpty(fileName)) return null;
            foreach (var dir in dirs)
            {
                try
                {
                    var candidate = Path.Combine(dir, fileName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
            return null;
        }
    }
}
