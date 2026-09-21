// Where Xenia keeps its things, which depends on WHICH Xenia.
//
// Two forks ship, and they disagree about the default. Read from EmulatorApp::OnInitialize:
//
//     storage_root = --storage_root
//     if empty:  storage_root = <folder holding the exe>
//                if !portable AND no portable.txt beside the exe:
//                    storage_root = <Documents>\Xenia
//
// and the `portable` cvar defaults differently per fork:
//
//     Canary on Windows : true   -> portable, storage lives beside the exe. portable.txt is redundant.
//     Master            : false  -> Documents\Xenia unless portable.txt is there.
//
// Guessing Documents for a Canary install points at a folder that does not exist. Guessing
// beside-the-exe for Master points at an empty one.
//
// `--storage_root` on the command line beats everything, so the emulator's own command line is read
// before any of this is applied.

using System;
using System.IO;
using System.Text.RegularExpressions;

namespace LbIntegrations.Xenia
{
    internal enum XeniaFork { Canary, Master }

    internal sealed class XeniaLayout
    {
        public XeniaFork Fork;
        public string InstallDir;
        /// <summary>The root everything else hangs off.</summary>
        public string StorageRoot;
        /// <summary>&lt;storage root&gt;\content, unless content_root says otherwise.</summary>
        public string ContentRoot;
        public string ConfigFile;
        /// <summary>Why we concluded this, for the log and the Edit Emulator page.</summary>
        public string Reason;
    }

    internal static class XeniaPaths
    {
        /// <summary>Executables the two forks ship. Canary first: it is the one people run.</summary>
        public static readonly string[] ExecutableNames = { "xenia_canary.exe", "xenia.exe" };

        public static bool IsXeniaExecutable(string applicationPath)
        {
            if (string.IsNullOrWhiteSpace(applicationPath)) return false;
            string name;
            try { name = Path.GetFileName(applicationPath); } catch { return false; }
            return !string.IsNullOrEmpty(name)
                   && name.StartsWith("xenia", StringComparison.OrdinalIgnoreCase)
                   && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                   // xenia-vfs-dump.exe ships beside master and is a tool, not the emulator.
                   && name.IndexOf("vfs-dump", StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>Canary and master differ in more than their name; the executable is the only thing
        /// we can read without launching anything.</summary>
        public static XeniaFork ForkOf(string applicationPath)
        {
            var name = SafeName(applicationPath);
            return name.IndexOf("canary", StringComparison.OrdinalIgnoreCase) >= 0
                ? XeniaFork.Canary : XeniaFork.Master;
        }

        /// <summary>Resolve everything from the executable, honouring a --storage_root or --content_root
        /// on the emulator's own command line. Never throws.</summary>
        public static XeniaLayout Resolve(string applicationPath, string commandLine = null)
        {
            var layout = new XeniaLayout { Fork = ForkOf(applicationPath) };
            try { layout.InstallDir = Path.GetDirectoryName(Path.GetFullPath(applicationPath ?? "")) ?? ""; }
            catch { layout.InstallDir = ""; }

            string fromCommandLine = OptionValue(commandLine, "storage_root");
            if (!string.IsNullOrWhiteSpace(fromCommandLine))
            {
                layout.StorageRoot = fromCommandLine;
                layout.Reason = "--storage_root on the command line";
            }
            else if (layout.InstallDir.Length == 0)
            {
                layout.StorageRoot = DocumentsXenia();
                layout.Reason = "no install directory - Documents";
            }
            else
            {
                bool portableByDefault = layout.Fork == XeniaFork.Canary;   // canary: portable on Windows
                bool marker = SafeExists(Path.Combine(layout.InstallDir, "portable.txt"));
                if (portableByDefault || marker)
                {
                    layout.StorageRoot = layout.InstallDir;
                    layout.Reason = portableByDefault
                        ? "canary is portable by default on Windows"
                        : "portable.txt beside the executable";
                }
                else
                {
                    layout.StorageRoot = DocumentsXenia();
                    layout.Reason = "master without portable.txt - Documents";
                }
            }

            string contentOverride = OptionValue(commandLine, "content_root");
            layout.ContentRoot = !string.IsNullOrWhiteSpace(contentOverride)
                // Relative values resolve against the storage root, which is Xenia's own rule.
                ? (Path.IsPathRooted(contentOverride)
                    ? contentOverride
                    : Path.Combine(layout.StorageRoot, contentOverride))
                : Path.Combine(layout.StorageRoot, "content");

            layout.ConfigFile = Path.Combine(layout.StorageRoot,
                layout.Fork == XeniaFork.Canary ? "xenia-canary.config.toml" : "xenia.config.toml");
            return layout;
        }

        /// <summary>The Xenia executable in a folder, or null. Canary preferred.</summary>
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
            return null;
        }

        /// <summary>The build string Xenia embeds, e.g. "canary_experimental@74c4e4a on Sep 21 2026".
        ///
        /// There is no Win32 version resource and no --version flag: main_resources.rc holds nothing but
        /// an icon, and --help opens a MODAL DIALOG in a windowed app - never call it. The window title
        /// and the log line are compile-time literal concatenations, so the string sits contiguous in
        /// the executable and can be read without running anything.</summary>
        public static string BuildStringOf(string applicationPath)
        {
            try
            {
                if (!File.Exists(applicationPath)) return null;
                var bytes = File.ReadAllBytes(applicationPath);
                var ascii = System.Text.Encoding.ASCII.GetString(bytes);
                var m = Regex.Match(ascii,
                    @"(?<branch>[A-Za-z0-9_]{3,40})@(?<sha>[0-9a-f]{7})\s+on\s+(?<date>[A-Z][a-z]{2}\s+[ 0-9]\d\s+\d{4})");
                return m.Success ? m.Groups["branch"].Value + "@" + m.Groups["sha"].Value : null;
            }
            catch (Exception ex) { Log.Warn("could not scan " + applicationPath + " for a build string", ex); return null; }
        }

        /// <summary>Documents\Xenia, resolved through the shell. Xenia calls SHGetKnownFolderPath, so
        /// OneDrive redirection is honoured and a hardcoded %USERPROFILE%\Documents would miss.</summary>
        private static string DocumentsXenia()
        {
            try
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrEmpty(docs)) return Path.Combine(docs, "Xenia");
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? "", "Documents", "Xenia");
        }

        /// <summary>The value of --name=value or --name value on a command line, or null.</summary>
        internal static string OptionValue(string commandLine, string name)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) return null;
            var m = Regex.Match(commandLine, "--" + Regex.Escape(name) + "(?:=|\\s+)(\"[^\"]*\"|\\S+)");
            if (!m.Success) return null;
            return m.Groups[1].Value.Trim('"');
        }

        private static string SafeName(string p)
        {
            try { return Path.GetFileName(p) ?? ""; } catch { return ""; }
        }

        private static bool SafeExists(string p)
        {
            try { return File.Exists(p); } catch { return false; }
        }
    }
}
