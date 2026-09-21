// Where PPSSPP keeps its things on Windows.
//
// Everything here reproduces InitMemstickDirectory() in PPSSPP's Windows/main.cpp (read at tag
// v1.20.4). PPSSPP has no registry keys and no fixed AppData folder: one marker file beside the
// executable decides between a self-contained install and a Documents-based one, and every other
// path hangs off that decision.
//
//   installed.txt absent            -> <exe dir>\memstick          (portable; what our installs are)
//   installed.txt present, empty    -> <Documents>\PPSSPP
//   installed.txt present, a path   -> that path
//   ... then, in both of the last two cases, fall back to <Documents>\PPSSPP if the directory
//       cannot be created or cannot be written to.
//
// The trap that motivates most of the validation below: the OFFICIAL Inno installer does NOT ship
// an empty installed.txt. ppsspp.iss renames "notinstalled.txt" to it, and that file contains two
// lines of English prose. PPSSPP assigns that prose as the memstick path, fails to create it (it
// contains quote characters, illegal in Windows paths) and lands on Documents via its error path.
// So a non-empty installed.txt means "not portable", NOT "the memstick is at <contents>".

using System;
using System.IO;
using System.Text;

namespace LbIntegrations.Ppsspp
{
    /// <summary>How a PPSSPP install stores its data — the answer to "portable or not", plus every
    /// path that follows from it.</summary>
    internal sealed class PpssppLayout
    {
        /// <summary>Folder holding the executable.</summary>
        public string InstallDir;
        /// <summary>The Memory Stick root: the folder that contains PSP\.</summary>
        public string MemStickDir;
        /// <summary>True when the memstick lives inside <see cref="InstallDir"/> — i.e. the install is
        /// self-contained and moving or deleting its folder takes the saves with it.</summary>
        public bool IsPortable;
        /// <summary>Why we concluded that, for the log and for the Edit Emulator page.</summary>
        public string Reason;

        /// <summary>&lt;memstick&gt;\PSP — note PPSSPP does NOT nest another PSP when the memstick folder
        /// is itself named "PSP".</summary>
        public string PspDir;
        /// <summary>&lt;memstick&gt;\PSP\SYSTEM — ppsspp.ini, controls.ini, the RetroAchievements token.</summary>
        public string SystemDir;
        public string ConfigFile;          // ppsspp.ini
        public string RetroAchievementsTokenFile;   // ppsspp_retroachievements.dat
        public string SaveDataDir;         // PSP\SAVEDATA   (v2)
        public string SaveStateDir;        // PSP\PPSSPP_STATE (v2)
    }

    internal static class PpssppPaths
    {
        /// <summary>Executable names PPSSPP actually ships, most likely first. The order matters:
        /// ppsspp.org's zip carries BOTH the 32- and 64-bit builds, and we want the 64-bit one.</summary>
        public static readonly string[] ExecutableNames =
        {
            "PPSSPPWindows64.exe",
            "PPSSPPWindowsARM64.exe",
            "PPSSPPWindows.exe",
            "PPSSPPGold64.exe",
            "PPSSPPGold.exe",
        };

        /// <summary>Does this application path look like PPSSPP? Matches the BinaryFileName pattern
        /// LaunchBox's own metadata database uses for this emulator, "PPSSPP*.exe", which covers the
        /// Windows/ARM64/Gold variants without us having to enumerate them.</summary>
        public static bool IsPpssppExecutable(string applicationPath)
        {
            if (string.IsNullOrWhiteSpace(applicationPath)) return false;
            string name;
            try { name = Path.GetFileName(applicationPath); } catch { return false; }
            return !string.IsNullOrEmpty(name)
                   && name.StartsWith("PPSSPP", StringComparison.OrdinalIgnoreCase)
                   && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The PPSSPP executable inside <paramref name="installDir"/>, or null. Looks for the
        /// names PPSSPP ships, in preference order, and never falls back to "the first .exe in the
        /// folder": the ARM64 release zip also carries AtlasTool.exe and ZimTool.exe, build-tool
        /// leftovers that would be picked by such a heuristic.</summary>
        public static string FindExecutable(string installDir)
        {
            if (string.IsNullOrWhiteSpace(installDir)) return null;
            foreach (var name in ExecutableNames)
            {
                try
                {
                    var p = Path.Combine(installDir, name);
                    if (File.Exists(p)) return p;
                }
                catch { }
            }
            // Last resort: any PPSSPP*.exe at the top level, still refusing the build tools.
            try
            {
                foreach (var f in Directory.EnumerateFiles(installDir, "PPSSPP*.exe", SearchOption.TopDirectoryOnly))
                    return f;
            }
            catch { }
            return null;
        }

        /// <summary>Resolve everything from the emulator's executable path. Never throws; on any
        /// failure it returns a layout pointing at the Documents default, which is what PPSSPP itself
        /// falls back to.</summary>
        public static PpssppLayout Resolve(string applicationPath)
        {
            var layout = new PpssppLayout();
            try { layout.InstallDir = Path.GetDirectoryName(Path.GetFullPath(applicationPath ?? "")) ?? ""; }
            catch { layout.InstallDir = ""; }

            string documents = DocumentsPpsspp();
            string installedTxt = layout.InstallDir.Length > 0
                ? Path.Combine(layout.InstallDir, "installed.txt")
                : null;

            if (installedTxt == null || !SafeFileExists(installedTxt))
            {
                // No marker: portable. This is what an install produced by this plugin looks like —
                // the release zip contains no installed.txt, and PPSSPP creates memstick\ on first run.
                layout.MemStickDir = layout.InstallDir.Length > 0
                    ? Path.Combine(layout.InstallDir, "memstick")
                    : documents;
                layout.IsPortable = layout.InstallDir.Length > 0;
                layout.Reason = layout.IsPortable
                    ? "portable (no installed.txt beside the executable)"
                    : "no install directory — falling back to Documents";
            }
            else
            {
                string configured = ReadInstalledTxt(installedTxt);
                if (string.IsNullOrEmpty(configured))
                {
                    layout.MemStickDir = documents;
                    layout.Reason = "installed.txt is empty — Documents";
                }
                else if (!LooksLikeUsablePath(configured))
                {
                    // The installer's prose, a relative path, or anything else we cannot trust.
                    // PPSSPP reaches Documents here too, via its create-then-fail path.
                    layout.MemStickDir = documents;
                    layout.Reason = "installed.txt does not hold a usable path — Documents";
                }
                else
                {
                    layout.MemStickDir = configured;
                    layout.Reason = "installed.txt points at " + configured;
                }
                layout.IsPortable = false;
            }

            // PPSSPP appends "PSP" unless the memstick folder is already called that.
            string leaf = "";
            try { leaf = Path.GetFileName(layout.MemStickDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? ""; }
            catch { }
            layout.PspDir = leaf.Equals("PSP", StringComparison.OrdinalIgnoreCase)
                ? layout.MemStickDir
                : Path.Combine(layout.MemStickDir, "PSP");

            layout.SystemDir = Path.Combine(layout.PspDir, "SYSTEM");
            layout.ConfigFile = Path.Combine(layout.SystemDir, "ppsspp.ini");
            layout.RetroAchievementsTokenFile = Path.Combine(layout.SystemDir, "ppsspp_retroachievements.dat");
            layout.SaveDataDir = Path.Combine(layout.PspDir, "SAVEDATA");
            layout.SaveStateDir = Path.Combine(layout.PspDir, "PPSSPP_STATE");
            return layout;
        }

        /// <summary>&lt;Documents&gt;\PPSSPP. Resolved through the shell, never through %USERPROFILE%:
        /// PPSSPP calls SHGetKnownFolderPath, so it honours OneDrive folder redirection and so must we,
        /// or we would read a different ppsspp.ini than the one PPSSPP writes.</summary>
        private static string DocumentsPpsspp()
        {
            try
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrEmpty(docs)) return Path.Combine(docs, "PPSSPP");
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? "", "Documents", "PPSSPP");
        }

        /// <summary>First line of installed.txt, BOM and line ending stripped. PPSSPP reads at most
        /// 2047 bytes and honours a UTF-8 BOM (which it writes itself when the user picks a folder).</summary>
        private static string ReadInstalledTxt(string path)
        {
            try
            {
                string first;
                using (var r = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                    first = r.ReadLine();
                return (first ?? "").Trim().Trim('﻿');
            }
            catch (Exception ex) { Log.Warn("could not read " + path, ex); return ""; }
        }

        /// <summary>Would PPSSPP be able to use this string as a directory? Rooted, no invalid
        /// characters, and either present or creatable. Deliberately strict — see the header note on
        /// the installer's prose-filled installed.txt.</summary>
        private static bool LooksLikeUsablePath(string candidate)
        {
            try
            {
                if (candidate.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
                if (!Path.IsPathRooted(candidate)) return false;
                if (Directory.Exists(candidate)) return true;
                // Don't create it: that would be a side effect of a read. Settle for "the parent exists".
                var parent = Path.GetDirectoryName(candidate);
                return !string.IsNullOrEmpty(parent) && Directory.Exists(parent);
            }
            catch { return false; }
        }

        private static bool SafeFileExists(string p)
        {
            try { return File.Exists(p); } catch { return false; }
        }
    }
}
