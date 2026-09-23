// Where no$gba keeps its things, which is the shortest such file in this repository.
//
// EVERYTHING IS BESIDE THE EXECUTABLE, UNDER A FIXED NAME. Not "by default" - there is no other
// option. The full configuration file no$gba writes for itself was read (2541 bytes, produced by
// Options > Save Options) and it holds fifty-odd keys and NOT ONE PATH: no save folder, no BIOS
// folder, no NAND, no snapshot directory. So this layout is derived entirely from the executable's
// location, and there is nothing to read out of a config file to find the rest.
//
//     NO$GBA.EXE
//     NO$GBA.INI        the configuration, written only by Options > Save Options
//     NO$GBA.INP        an input blob written on exit; NOT the key mapping, which is in the INI
//     BATTERY\          cartridge saves, "<rom file name without extension>.SAV"
//     SNAP\             snapshots (F8 writes, F7 loads) - created on demand
//     BIOSNDS7.ROM …    the BIOS and firmware files, by name
//     DSI-1.MMC         the DSi NAND, for a later instalment
//
// THE EXECUTABLE'S NAME CONTAINS A DOLLAR SIGN. `NO$GBA.EXE` is fine in C#, but it expands in an
// unquoted PowerShell string and it is a metacharacter in some globs, so it is never interpolated
// into a shell command anywhere in this plugin.

using System;
using System.IO;

namespace LbIntegrations.NoGba
{
    /// <summary>One no$gba installation. Every field is a pure function of the executable path -
    /// there is no configuration to consult, because no$gba has no path settings at all.</summary>
    internal sealed class NoGbaLayout
    {
        /// <summary>The folder holding NO$GBA.EXE, and therefore holding everything else.</summary>
        public string InstallDir;

        /// <summary>&lt;install&gt;\NO$GBA.INI. May not exist: no$gba never creates it by itself,
        /// not at first run and not on exit - measured. Only Options > Save Options writes one.</summary>
        public string IniFile;

        /// <summary>&lt;install&gt;\BATTERY - cartridge saves. Created by no$gba at first launch.</summary>
        public string BatteryDir;

        /// <summary>&lt;install&gt;\SNAP - snapshots. Created the first time F8 is pressed.</summary>
        public string SnapDir;

        /// <summary>For the log, so a surprising layout explains itself.</summary>
        public string Reason;
    }

    internal static class NoGbaPaths
    {
        /// <summary>The only name the release ships. The zip holds three entries - NO$GBA.EXE,
        /// README.TXT and DSI-SD.ZIP - and the executable is not renamed by anything.</summary>
        public const string ExecutableName = "NO$GBA.EXE";

        public const string IniName = "NO$GBA.INI";
        public const string BatteryDirName = "BATTERY";
        public const string SnapDirName = "SNAP";

        /// <summary>The extension no$gba gives a cartridge save. Upper case as it writes it, though
        /// every comparison here is case-insensitive because Windows is.</summary>
        public const string SaveExtension = ".SAV";

        /// <summary>Is this the emulator this plugin speaks for?
        ///
        /// Matched on the name rather than the contents, like every other plugin here. "no$gba" and
        /// "nogba" are both accepted because the dollar is awkward enough that people rename it.</summary>
        public static bool IsNoGbaExecutable(string applicationPath)
        {
            if (string.IsNullOrWhiteSpace(applicationPath)) return false;
            string name;
            try { name = Path.GetFileNameWithoutExtension(applicationPath); } catch { return false; }
            if (string.IsNullOrEmpty(name)) return false;
            if (!applicationPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;

            var plain = name.Replace("$", "").Replace("-", "").Replace("_", "").Replace(" ", "");
            return plain.StartsWith("nogba", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The no$gba executable inside <paramref name="installDir"/>, or null.</summary>
        public static string FindExecutable(string installDir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir)) return null;

                // The exact name first, so a folder holding both it and something renamed is not a
                // coin toss. EnumerateFiles with a "*.exe" filter rather than a pattern containing
                // the dollar, which some providers treat specially.
                var exact = Path.Combine(installDir, ExecutableName);
                if (File.Exists(exact)) return exact;

                foreach (var path in Directory.EnumerateFiles(installDir, "*.exe"))
                    if (IsNoGbaExecutable(path)) return path;
                return null;
            }
            catch { return null; }
        }

        /// <summary>Everything, from the executable path alone.
        ///
        /// ONE PARAMETER, deliberately: the probe invokes this reflectively with a single argument,
        /// the way it does for every other plugin's Resolve.</summary>
        public static NoGbaLayout Resolve(string applicationPath)
        {
            var layout = new NoGbaLayout();
            try
            {
                if (string.IsNullOrWhiteSpace(applicationPath))
                {
                    layout.Reason = "no application path was supplied";
                    return layout;
                }

                layout.InstallDir = Path.GetDirectoryName(Path.GetFullPath(applicationPath));
                if (string.IsNullOrEmpty(layout.InstallDir))
                {
                    layout.Reason = "the application path has no directory";
                    return layout;
                }

                layout.IniFile = Path.Combine(layout.InstallDir, IniName);
                layout.BatteryDir = Path.Combine(layout.InstallDir, BatteryDirName);
                layout.SnapDir = Path.Combine(layout.InstallDir, SnapDirName);
                layout.Reason = "beside the executable, which is the only layout no$gba has";
                return layout;
            }
            catch (Exception ex)
            {
                layout.Reason = "could not be resolved - " + ex.Message;
                return layout;
            }
        }

        /// <summary>The name no$gba gives the save of a ROM: the file name without its LAST
        /// extension, and nothing read out of the ROM.
        ///
        /// MEASURED, not assumed: "mk.nds" produced "BATTERY\mk.SAV". So a ROM that came out of an
        /// archive must be handed over under the INNER entry's name, or its save is filed under the
        /// name of a temporary file - see NoGbaRoms.</summary>
        public static string SaveBaseName(string romPath)
        {
            try
            {
                var name = Path.GetFileName(romPath);
                if (string.IsNullOrEmpty(name)) return null;
                var dot = name.LastIndexOf('.');
                return dot > 0 ? name.Substring(0, dot) : name;
            }
            catch { return null; }
        }
    }
}
