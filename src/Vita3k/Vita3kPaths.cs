// Where a Vita3K install keeps its things, and which build it is.
//
// TWO LAYOUTS, and which one is in force is decided by the mere EXISTENCE of a directory. Measured
// in app_init.cpp: if `portable\` sits beside Vita3K.exe the emulator puts its virtual Vita
// filesystem, its config, its log, its cache and its patches inside it; otherwise it calls
// SDL_GetPrefPath and everything lands in %AppData%\Vita3K\Vita3K\. Nothing else - no flag, no
// setting, no config key - takes part in that decision.
//
// THAT IS ALSO WHY WE NEVER CREATE THAT DIRECTORY UNDER SOMEBODY ELSE'S INSTALL. Creating it beside
// an emulator that has been running for a year does not migrate anything: it switches the
// filesystem root, and the user's games, firmware and saves - still perfectly present in %AppData%
// - simply stop being visible. Only a fresh install of ours gets one. See InstallEmulator.

using System;
using System.Diagnostics;
using System.IO;

namespace LbIntegrations.Vita3k
{
    /// <summary>What a resolved install looks like.</summary>
    internal sealed class Vita3kLayout
    {
        /// <summary>Full path to Vita3K.exe.</summary>
        public string Executable;

        /// <summary>The folder holding the executable.</summary>
        public string InstallDir;

        /// <summary>True when `portable\` exists beside the executable, so everything stays local.</summary>
        public bool Portable;

        /// <summary>The virtual Vita filesystem - the folder holding ux0, vs0, sa0, pd0. This is the
        /// path Vita3K calls vita_fs_path, and what `--firmware` writes into.</summary>
        public string VitaFs;

        /// <summary>In one phrase, for the log.</summary>
        public string Reason;
    }

    internal static class Vita3kPaths
    {
        /// <summary>The executable, exactly as their CI publishes it. One name: the Windows asset has
        /// carried Vita3K.exe since the project moved to Qt, and the version resource inside it
        /// declares OriginalFilename "Vita3K.exe" - measured on build 4098.</summary>
        public static readonly string[] ExecutableNames = { "Vita3K.exe" };

        /// <summary>Is this path a Vita3K executable? Name only, case-insensitively.
        ///
        /// This is what claims an emulator, and it is deliberately the ONLY thing that does: a user's
        /// entry is claimed by where it points, never by what it is called. Renaming an emulator entry
        /// in LaunchBox must not cost it its plugin.</summary>
        public static bool IsVita3kExecutable(string applicationPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(applicationPath)) return false;
                var name = Path.GetFileName(applicationPath);
                foreach (var candidate in ExecutableNames)
                    if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            catch { return false; }
        }

        /// <summary>The Vita3K executable inside <paramref name="dir"/>, or null. The release archive
        /// is FLAT - measured on windows-latest.zip build 4098, 135 entries and Vita3K.exe at the root -
        /// so one level is looked at and one level below it, no more.</summary>
        public static string FindExecutable(string dir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return null;
                foreach (var name in ExecutableNames)
                {
                    var direct = Path.Combine(dir, name);
                    if (File.Exists(direct)) return direct;
                }
                foreach (var sub in Directory.GetDirectories(dir))
                    foreach (var name in ExecutableNames)
                    {
                        var nested = Path.Combine(sub, name);
                        if (File.Exists(nested)) return nested;
                    }
                return null;
            }
            catch (Exception ex) { Log.Warn("could not look for a Vita3K executable in " + dir, ex); return null; }
        }

        /// <summary>The directory whose mere existence turns portable mode on.</summary>
        public static string PortableDirOf(string installDir)
            => string.IsNullOrWhiteSpace(installDir) ? null : Path.Combine(installDir, "portable");

        /// <summary>Resolve an install from the path of its executable. Never throws; never creates
        /// anything.</summary>
        public static Vita3kLayout Resolve(string executablePath)
        {
            var layout = new Vita3kLayout { Executable = executablePath };
            try
            {
                layout.InstallDir = Path.GetDirectoryName(Path.GetFullPath(executablePath));
                var portable = PortableDirOf(layout.InstallDir);

                if (portable != null && Directory.Exists(portable))
                {
                    layout.Portable = true;
                    layout.VitaFs = Path.Combine(portable, "fs");
                    layout.Reason = "portable\\ is present, so storage is local to the install";
                    return layout;
                }

                // SDL_GetPrefPath(org_name, app_name) with both set to "Vita3K" in their CMakeLists,
                // which on Windows is the roaming application data folder.
                layout.Portable = false;
                layout.VitaFs = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vita3K", "Vita3K");
                layout.Reason = "no portable\\ directory, so storage is under %AppData%";
                return layout;
            }
            catch (Exception ex)
            {
                Log.Warn("could not resolve the layout of " + executablePath, ex);
                layout.Reason = "unresolved";
                return layout;
            }
        }

        /// <summary>Is Vita3K running right now?
        ///
        /// Prefix and not equality, because a build can be published under a decorated name, and
        /// because this is the only signal that a session is over - measured next door, LaunchBox 14
        /// never calls OnGameExited.</summary>
        /// <summary>The largest peak working set among the running Vita3K processes, in bytes - 0
        /// when none runs. Windows keeps the peak itself, so a sample every few hundred milliseconds
        /// misses nothing.</summary>
        public static long EmulatorPeakBytes()
        {
            long peak = 0;
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcesses())
                {
                    using (p)
                    {
                        try
                        {
                            if (p.ProcessName != null && p.ProcessName.StartsWith("Vita3K", StringComparison.OrdinalIgnoreCase))
                                peak = Math.Max(peak, p.PeakWorkingSet64);
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return peak;
        }

        /// <summary>End the Vita3K processes of this install - the executable under its folder; a Vita3K
        /// whose path cannot be read is taken by name, as EmulatorRunning takes it. The count ended.</summary>
        public static int EndEmulator(Vita3kLayout layout)
        {
            int ended = 0;
            var dir = layout?.InstallDir;
            string full = null;
            try { if (!string.IsNullOrEmpty(dir)) full = Path.GetFullPath(dir).TrimEnd('\\') + "\\"; } catch { }
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcesses())
                {
                    using (p)
                    {
                        try
                        {
                            if (p.ProcessName == null || !p.ProcessName.StartsWith("Vita3K", StringComparison.OrdinalIgnoreCase)) continue;
                            string exe = null;
                            try { exe = p.MainModule?.FileName; } catch { }
                            if (exe != null && full != null && !exe.StartsWith(full, StringComparison.OrdinalIgnoreCase)) continue;   // another install's
                            p.Kill();
                            p.WaitForExit(5000);
                            ended++;
                        }
                        catch (Exception ex) { Log.Warn("could not end Vita3K (" + p.Id + ")", ex); }
                    }
                }
            }
            catch (Exception ex) { Log.Warn("could not look at the process list", ex); }
            return ended;
        }

        /// <summary>Is a visible window of a Vita3K process titled for <paramref name="titleId"/>'s game -
        /// "(PCSA00017)" in its title, as Vita3K's game window has it?</summary>
        public static bool GameWindowOpen(string titleId)
        {
            if (string.IsNullOrWhiteSpace(titleId)) return false;
            var pids = new System.Collections.Generic.HashSet<int>();
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcesses())
                    using (p)
                    {
                        try { if (p.ProcessName != null && p.ProcessName.StartsWith("Vita3K", StringComparison.OrdinalIgnoreCase)) pids.Add(p.Id); }
                        catch { }
                    }
            }
            catch { return false; }
            if (pids.Count == 0) return false;
            bool found = false;
            var needle = "(" + titleId + ")";
            EnumWindows((h, _) =>
            {
                GetWindowThreadProcessId(h, out var pid);
                if (!pids.Contains((int)pid) || !IsWindowVisible(h)) return true;
                var text = new System.Text.StringBuilder(512);
                GetWindowText(h, text, text.Capacity);
                if (text.ToString().IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) { found = true; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int max);

        public static bool EmulatorRunning()
        {
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcesses())
                {
                    string name;
                    try { name = p.ProcessName; } catch { continue; }
                    if (name != null && name.StartsWith("Vita3K", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }
            catch (Exception ex) { Log.Warn("could not look at the process list", ex); return false; }
        }

        /// <summary>The LaunchBox root an install sits under - the folder holding Core\ and Data\.
        /// Walked up from the emulator rather than from our own assembly, because the shared RAM disk
        /// code needs the root of the install this emulator belongs to.</summary>
        public static string LaunchBoxRootOf(Vita3kLayout layout)
        {
            try
            {
                var dir = layout?.InstallDir;
                for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
                {
                    if (Directory.Exists(Path.Combine(dir, "Core")) && Directory.Exists(Path.Combine(dir, "Data")))
                        return dir;
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch (Exception ex) { Log.Warn("could not locate the LaunchBox root", ex); }
            return null;
        }

        /// <summary>The build number of an installed Vita3K, or null.
        ///
        /// It is the FOURTH FIELD of the Win32 file version, and that is not a coincidence to be
        /// relied on loosely: their resource.h defines FILE_VERSION as
        /// APP_VER_HI, APP_VER_MID, APP_VER_LO, APP_NUMBER, and APP_NUMBER is `git rev-list HEAD
        /// --count` - the very number their CI then writes into the release notes as "Vita3K Build:".
        /// Measured on build 4098: the executable reports 0.2.1.4098 and the release body says 4098.
        ///
        /// So the installed build and the published build are the same quantity read from two places,
        /// which is what makes an update check possible at all - the git tag is the constant
        /// "continuous" and says nothing.</summary>
        public static int? BuildNumberOf(string executablePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath)) return null;
                var info = FileVersionInfo.GetVersionInfo(executablePath);
                // A build of 0 is what CMake falls back to when git is unavailable - present, but not
                // a number anything can be compared against.
                return info.FilePrivatePart > 0 ? info.FilePrivatePart : (int?)null;
            }
            catch (Exception ex) { Log.Warn("could not read the version of " + executablePath, ex); return null; }
        }
    }
}
