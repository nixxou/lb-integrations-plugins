// Where melonDS keeps its things, worked out from the executable's path alone.
//
// PORTABLE BY CONSTRUCTION, which makes this the simplest layout of the four plugins. pathInit()
// (src/frontend/qt_sdl/main.cpp:180-214) picks, in order:
//
//   1. a DIRECTORY named "portable" beside the executable, if one exists - config goes inside it;
//   2. otherwise the executable's own directory, because WIN32_PORTABLE is defined;
//   3. otherwise a per-user config directory - a branch that is #else-d out of the Windows build.
//
// WIN32_PORTABLE comes from option(PORTABLE ... ON) at src/frontend/qt_sdl/CMakeLists.txt:196-201,
// which the release preset does not override (CMakePresets.json:24-35 sets only BUILD_STATIC). So
// the third case cannot happen with the official build and is not modelled here - but the "portable"
// directory IS, because it costs one File.Exists and it is the shape a future non-portable build
// would need.
//
// SAVE AND STATE DIRECTORIES ARE READ, NOT ASSUMED. They come from [Instance0] SaveFilePath and
// SavestatePath, which are empty on a stock install - and an empty value means "beside the ROM"
// (EmuInstance.cpp:445-484, getAssetPath). This plugin fills them on installs it makes itself, and
// leaves them alone everywhere else, so both dispositions have to be readable.

using System;
using System.IO;

namespace LbIntegrations.MelonDs
{
    internal sealed class MelonDsLayout
    {
        /// <summary>The folder holding melonDS.exe.</summary>
        public string InstallDir;

        /// <summary>Where melonDS reads and writes melonDS.toml - InstallDir, or the portable\
        /// subdirectory when one exists.</summary>
        public string ConfigDir;

        public string ConfigFile;

        /// <summary>[Instance0] SaveFilePath, as the file says it. Empty means melonDS puts the .sav
        /// beside the ROM, which is a disposition and not a failure.</summary>
        public string SaveDir;

        /// <summary>[Instance0] SavestatePath, same rule.</summary>
        public string StateDir;

        /// <summary>Where this plugin would put them on an install of its own making.</summary>
        public string DefaultSaveDir;
        public string DefaultStateDir;

        /// <summary>For the log, so a surprising layout explains itself.</summary>
        public string Reason;

        public bool HasRedirectedSaves => !string.IsNullOrWhiteSpace(SaveDir);
        public bool HasRedirectedStates => !string.IsNullOrWhiteSpace(StateDir);
    }

    internal static class MelonDsPaths
    {
        public const string InstanceTable = "Instance0";
        public const string EmuTable = "Emu";
        public const string DSiTable = "DSi";

        public const string KeySaveFilePath = "SaveFilePath";
        public const string KeySavestatePath = "SavestatePath";
        public const string KeyConsoleType = "Emu.ConsoleType";      // written into [Emu] as ConsoleType
        public const string KeyExternalBios = "ExternalBIOSEnable";

        public const string SaveDirName = "saves";
        public const string StateDirName = "savestates";

        /// <summary>The only name melonDS ships on Windows - the release zip holds exactly one entry,
        /// melonDS.exe, statically linked.</summary>
        public const string ExecutableName = "melonDS.exe";

        public static bool IsMelonDsExecutable(string applicationPath)
        {
            if (string.IsNullOrWhiteSpace(applicationPath)) return false;
            string name;
            try { name = Path.GetFileName(applicationPath); } catch { return false; }
            return !string.IsNullOrEmpty(name)
                   && name.StartsWith("melonDS", StringComparison.OrdinalIgnoreCase)
                   && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The melonDS executable inside <paramref name="installDir"/>, or null.</summary>
        public static string FindExecutable(string installDir)
        {
            if (string.IsNullOrWhiteSpace(installDir)) return null;
            try
            {
                var exact = Path.Combine(installDir, ExecutableName);
                if (File.Exists(exact)) return exact;
            }
            catch { }
            try
            {
                foreach (var f in Directory.EnumerateFiles(installDir, "melonDS*.exe", SearchOption.TopDirectoryOnly))
                    return f;
            }
            catch { }
            return null;
        }

        /// <summary>Resolve everything from the emulator's executable path.
        ///
        /// ONE PARAMETER, deliberately: the probe finds this method by name and invokes it
        /// reflectively with a single argument. An optional second parameter would compile and then
        /// fail at run time, which is exactly what happened to XeniaPaths.Resolve.</summary>
        public static MelonDsLayout Resolve(string applicationPath)
        {
            var layout = new MelonDsLayout();
            try { layout.InstallDir = Path.GetDirectoryName(Path.GetFullPath(applicationPath ?? "")) ?? ""; }
            catch { layout.InstallDir = ""; }

            if (layout.InstallDir.Length == 0)
            {
                layout.Reason = "no install directory could be resolved from the application path";
                return layout;
            }

            var portable = Path.Combine(layout.InstallDir, "portable");
            if (SafeDirExists(portable))
            {
                layout.ConfigDir = portable;
                layout.Reason = "portable\\ directory beside the executable";
            }
            else
            {
                layout.ConfigDir = layout.InstallDir;
                layout.Reason = "portable build, configuration beside the executable";
            }

            layout.ConfigFile = Path.Combine(layout.ConfigDir, "melonDS.toml");
            layout.DefaultSaveDir = Path.Combine(layout.InstallDir, SaveDirName);
            layout.DefaultStateDir = Path.Combine(layout.InstallDir, StateDirName);

            var configured = MelonDsToml.Read(layout.ConfigFile, InstanceTable, KeySaveFilePath, KeySavestatePath);
            layout.SaveDir = Value(configured, KeySaveFilePath);
            layout.StateDir = Value(configured, KeySavestatePath);

            return layout;
        }

        private static string Value(System.Collections.Generic.IDictionary<string, string> map, string key)
            => map != null && map.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

        private static bool SafeDirExists(string path)
        {
            try { return Directory.Exists(path); } catch { return false; }
        }
    }
}
