// Where a restore WOULD write a save or a state, for one install and one game (docs\saves-two-installs.md §5).
//
// Each plugin answers it with the very computation its restore uses: what the choice of an install shows is what is
// written. Read only - no folder created, no session captured, no file moved. Its sources, in order: what the pack
// forces (command line, keys it writes, "nothing beside the games"), the emulator's config key, the emulator's default.
//
// A path an emulator reads from its own config is handed to Windows as it is (ares Emulator::locate, bsnes
// Program::path, melonDS getAssetPath - read 12/10): a relative one is relative to the emulator's current directory,
// which LaunchBox sets to the folder of its exe (measured 12/10: ares launched by LaunchBox, cwd
// G:\LB1326\Emulators\Nixx-ares\, LaunchBox's own G:\LB1326\). Not LaunchBox's current directory: OfConfig.

#nullable enable

using System;
using System.IO;

namespace LbIntegrations.Saves
{
    internal enum ExpectedKind
    {
        /// <summary>Not known; <see cref="ExpectedDir.Reason"/> says why.</summary>
        Unknown,
        /// <summary>The folder the emulator reads the file from.</summary>
        Folder,
        /// <summary>A file of the plugin's own, put into the console at the next launch (Cxbx, xemu, Vita3K, DSiWare).</summary>
        Pack,
    }

    internal readonly struct ExpectedDir
    {
        public ExpectedKind Kind { get; }
        public string? Path { get; }
        public string? Reason { get; }

        private ExpectedDir(ExpectedKind kind, string? path, string? reason) { Kind = kind; Path = path; Reason = reason; }

        public static ExpectedDir Folder(string path) => new ExpectedDir(ExpectedKind.Folder, path, null);
        public static ExpectedDir Pack(string path) => new ExpectedDir(ExpectedKind.Pack, path, null);
        public static ExpectedDir Unknown(string reason) => new ExpectedDir(ExpectedKind.Unknown, null, reason);

        /// <summary>A folder when <paramref name="path"/> is one, else unknown with <paramref name="reason"/>.</summary>
        public static ExpectedDir FolderOr(string? path, string reason) => string.IsNullOrWhiteSpace(path) ? Unknown(reason) : Folder(path!);

        public bool Known => Kind != ExpectedKind.Unknown;

        public override string ToString() => Kind == ExpectedKind.Unknown ? "unknown (" + Reason + ")" : Kind.ToString().ToLowerInvariant() + " " + Path;

        /// <summary>A path read from an emulator's own config, as the emulator resolves it: rooted as it is, relative from
        /// the folder of its exe (its current directory under LaunchBox). Null when empty or unusable.</summary>
        public static string? OfConfig(string? value, string? exeDir)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            try
            {
                var v = value!.Trim().Replace('/', '\\');
                if (System.IO.Path.IsPathRooted(v)) return System.IO.Path.GetFullPath(v);
                if (string.IsNullOrEmpty(exeDir)) return null;
                return System.IO.Path.GetFullPath(System.IO.Path.Combine(exeDir!, v));
            }
            catch { return null; }
        }
    }
}
