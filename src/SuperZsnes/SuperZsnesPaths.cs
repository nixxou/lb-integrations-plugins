// What an installation of SUPER ZSNES looks like, read off the 0.310 Windows build.
//
// The zip is flat and the whole player sits at its root:
//
//     SUPERZSNES.exe                     632 KB, x86 (32-bit) - the Unity player stub
//     UnityPlayer.dll                    the engine
//     GameAssembly.dll                   the emulator, compiled by IL2CPP - no managed code to read
//     SUPERZSNES_Data\
//         level0                         the one scene, and where the About box text lives
//         il2cpp_data\Metadata\global-metadata.dat   every string literal and identifier
//         resources.assets, sharedassets0.assets     enhancement data, fonts, shaders
//         Plugins\x86\steam_api.dll      a Steam build; runs without Steam
//         smw.srm                        a 2 KB Super Mario World save, left in by the developers
//     D3D12\D3D12Core.dll
//
// ITS SETTINGS ARE IN UNITY'S PERSISTENT DATA FOLDER, measured on a run that reached the menu:
//
//     %USERPROFILE%\AppData\LocalLow\ZEMU Software Inc_\SUPERZSNES\szsnes_ui.data
//
// (the underscore is Unity's for the trailing dot of "ZEMU Software Inc."), written at exit - the
// log says "Save: .../szsnes_ui.data" - in BinaryFormatter's NRBF format, one object of class
// MainMenuManager+MainMenuSettings whose members are the settings named in the metadata: srmPath,
// chtPath, bpsPath, gameSavePath, rauserID, raencT and the rest. The options dialog says "Keep the
// path fields empty if you want those files to be in the same folder as the ROM", and on a fresh
// install they are null. Nothing here reads or writes that file yet; SettingsFile says where it is.
// Whether the emulator can be made portable (its extension table carries a bare ".portable"
// literal) is not measured - see README, "Notes on SUPER ZSNES".

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LbIntegrations.SuperZsnes
{
    internal static class SuperZsnesPaths
    {
        public const string ExecutableName = "SUPERZSNES.exe";

        /// <summary>Its process name, for "is it running".</summary>
        public const string ProcessName = "SUPERZSNES";
        public const string DataFolder = "SUPERZSNES_Data";

        /// <summary>The settings file, NRBF, written at exit.
        ///
        /// Two places, and the install decides. With the BepInEx plugin in the emulator's folder
        /// (tools\superzsnes-bepinex), Application.persistentDataPath answers &lt;exe&gt;\portable and
        /// the file is &lt;exe&gt;\portable\szsnes_ui.data - the game's own log says so. Without it,
        /// Unity's persistent data path for company "ZEMU Software Inc." and product "SUPERZSNES"
        /// (both read from app.info), with the dot the folder name cannot end in turned into an
        /// underscore. Both measured off the "Save:" line rather than derived.</summary>
        public static string SettingsFile(string applicationPath = null)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(applicationPath))
                {
                    var exeDir = Path.GetDirectoryName(Path.GetFullPath(applicationPath));
                    if (!string.IsNullOrEmpty(exeDir) && IsPortable(exeDir))
                        return Path.Combine(exeDir, "portable", "szsnes_ui.data");
                }
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var profile = Path.GetDirectoryName(Path.GetDirectoryName(local));   // ...\AppData
                if (string.IsNullOrEmpty(profile)) return null;
                return Path.Combine(profile, "LocalLow", "ZEMU Software Inc_", "SUPERZSNES", "szsnes_ui.data");
            }
            catch { return null; }
        }

        /// <summary>Is the in-process plugin installed beside this executable? BepInEx's relay and
        /// our plugin under its plugins folder - the two files that make persistentDataPath move.</summary>
        public static bool IsPortable(string exeDir)
        {
            try
            {
                return File.Exists(Path.Combine(exeDir, "winhttp.dll"))
                       && File.Exists(Path.Combine(exeDir, "BepInEx", "plugins", "SuperZsnes.BepInEx.dll"));
            }
            catch { return false; }
        }

        /// <summary>Unity's log of the last run, beside the settings. It prints "# ARGS: n" at
        /// start and "Save: &lt;path&gt;" at exit, which is how the open questions in the README get
        /// answered.</summary>
        public static string PlayerLog()
        {
            // Unity's native log never moves: it does not ask the managed getter the plugin patches.
            var settings = SettingsFile(null);
            return settings == null ? null : Path.Combine(Path.GetDirectoryName(settings), "Player.log");
        }

        /// <summary>Is this the emulator? The name, compared without case: the zip spells it
        /// SUPERZSNES.exe and the site SuperZSNES, and Windows does not care.</summary>
        public static bool IsSuperZsnesExecutable(string applicationPath)
        {
            if (string.IsNullOrWhiteSpace(applicationPath)) return false;
            string name;
            try { name = Path.GetFileName(applicationPath); } catch { return false; }
            return string.Equals(name, ExecutableName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The executable in a freshly extracted folder, or null. At the root today; one
        /// level down is looked at too, for the day the zip grows a top folder.</summary>
        public static string FindExecutable(string installDir)
        {
            if (string.IsNullOrWhiteSpace(installDir)) return null;
            try
            {
                var here = Path.Combine(installDir, ExecutableName);
                if (File.Exists(here)) return here;
                foreach (var sub in Directory.EnumerateDirectories(installDir))
                {
                    var below = Path.Combine(sub, ExecutableName);
                    if (File.Exists(below)) return below;
                }
            }
            catch { }
            return null;
        }

        /// <summary>The version the installed build says it is, digits only - "0.310" - or null.
        ///
        /// Read out of the scene files under SUPERZSNES_Data, where the About box's caption is
        /// serialised as text: measured on 0.310, "v0.310b" sits in level0 right before the list of
        /// enhanced games and the coprocessor ROM names. The trailing letter is the developers'
        /// hotfix mark - the site calls the same release 0.310 - so it is dropped, and version.txt
        /// and this answer then speak the same language.
        ///
        /// THERE ARE THREE SUCH STRINGS IN 0.310, and the highest is the right one. Measured:
        /// "v0.001" and "v0.100a" are the design-time texts of two TextMeshPro labels (each sits in a
        /// text component's serialised block, right after its kerning table), left over from when
        /// the dialog was laid out and overwritten at runtime; "v0.310b" is a script's own string
        /// field, followed by the list of enhanced games and the coprocessor ROM names. Stale labels
        /// stay behind while the real one moves up, so the highest wins - and the trace log names
        /// them all, in case a build ever ships a label that says more than its script.</summary>
        public static string InstalledVersion(string applicationPath)
        {
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(applicationPath ?? ""));
                if (string.IsNullOrEmpty(dir)) return null;
                var data = Path.Combine(dir, DataFolder);
                if (!Directory.Exists(data)) return null;

                var found = Directory.EnumerateFiles(data, "level*")
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .SelectMany(f => VersionsIn(f))
                    .Distinct()
                    .ToList();
                if (found.Count == 0) return null;
                if (found.Count > 1)
                    Log.Verbose("version strings in the scene files: " + string.Join(", ", found) + " - taking the highest");
                return found.OrderByDescending(v => Version.TryParse(v, out var p) ? p : new Version(0, 0)).First();
            }
            catch (Exception ex)
            {
                Log.Warn("could not read the installed version beside " + applicationPath, ex);
                return null;
            }
        }

        /// <summary>A Unity scene stores a string as its length, the bytes, then padding to four - so
        /// the caption is a WHOLE string, framed by control bytes on both sides. Requiring that frame
        /// is what keeps "shader@v17.200" or "package_v2.100.x" from being read as a version: those
        /// are the middle of a longer string, and the byte before their "v" is printable.</summary>
        private static readonly Regex AboutVersion =
            new Regex(@"(?<=[\x00-\x1F])v(?<v>\d+\.\d{3})(?<mark>[a-z])?(?=[\x00-\x1F])", RegexOptions.Compiled);

        private static string[] VersionsIn(string sceneFile)
        {
            try
            {
                var text = Encoding.ASCII.GetString(File.ReadAllBytes(sceneFile));
                return AboutVersion.Matches(text).Cast<Match>().Select(m => m.Groups["v"].Value).ToArray();
            }
            catch { return Array.Empty<string>(); }
        }
    }
}
