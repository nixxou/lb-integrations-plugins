// Calling melonds-nand.dll, which is melonDS's own NAND code behind a C door.
//
// Installing a DSiWare title, and moving its save in and out, means AES-CTR over the whole image,
// the FAT filesystem inside it, and a ticket. melonDS already does all of that correctly in
// DSi_NAND.cpp. A second implementation of a format where being subtly different is the same as
// being wrong - on a file the user cannot easily replace - would be the wrong kind of ambition.
//
// THIS IS WHY THE PLUGIN IS GPL-3.0. Loading that library into this process makes the two one work,
// and melonDS is GPL-3.0. The choice was made deliberately: the alternative was an executable at
// arm's length, which would have kept the plugin MIT at the cost of a process per question. See
// THIRD-PARTY.md.
//
// ONE NAND AT A TIME, and that is melonDS's constraint rather than ours: NANDMount has its move
// constructor deleted because fatfs keeps a global pointer to the mounted filesystem
// (DSi_NAND.h:134-137). The library refuses a second open and says so; this file makes that easy to
// respect by handing out the handle inside a using-block and never storing one.
//
// THE LIBRARY IS OPTIONAL. Without it a DSiWare title still gets its own NAND and melonDS is still
// pointed at it; only the import and the save extraction stop. Every failure here is reported and
// then shrugged off - a plugin that broke a launch because a helper was missing would be worse than
// one that asks for a click.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace LbIntegrations.MelonDs
{
    /// <summary>The save files a DSiWare title keeps inside the NAND, as melonDS numbers them
    /// (DSi_NAND.h:37-42).</summary>
    internal enum NandSaveKind
    {
        Public = 0,
        Private = 1,
        Banner = 2,
    }

    /// <summary>An open NAND. Dispose closes it, and nothing else may be open meanwhile.</summary>
    internal sealed class NandSession : IDisposable
    {
        private IntPtr _handle;

        internal NandSession(IntPtr handle) { _handle = handle; }

        public ulong ConsoleId => _handle == IntPtr.Zero ? 0 : MelonDsNand.ConsoleId(_handle);

        public bool TitleExists(string titleId)
            => MelonDsNand.Call(_handle, titleId, (h, c, i) => MelonDsNand.TitleExists(h, c, i)) == MelonDsNand.Ok;

        /// <summary>Install the title from its .nds. True when it is in there afterwards, which
        /// includes the case where it already was.
        ///
        /// <paramref name="generatedTmd"/> says whether the metadata file had to be built from the
        /// ROM - worth repeating to the user, because a generated TMD is unsigned and a real DSi
        /// would notice even though melonDS does not.</summary>
        public bool ImportTitle(string appPath, string tmdPath, out string error, out bool generatedTmd)
        {
            var code = MelonDsNand.ImportTitle(_handle, appPath, tmdPath, 0);
            generatedTmd = false;
            try { generatedTmd = MelonDsNand.LastTmdWasGenerated() != 0; } catch { }
            error = code == MelonDsNand.Ok ? null : MelonDsNand.LastError(_handle);
            return code == MelonDsNand.Ok;
        }

        /// <summary>Copy one of the title's save files out. False with a null error means the title
        /// is simply not installed, which is not a failure.</summary>
        public bool ExportSave(string titleId, NandSaveKind kind, string outPath, out string error)
        {
            var code = MelonDsNand.Call(_handle, titleId,
                (h, c, i) => MelonDsNand.ExportSave(h, c, i, (int)kind, outPath));
            error = code < 0 ? MelonDsNand.LastError(_handle) : null;
            return code == MelonDsNand.Ok;
        }

        public bool ImportSave(string titleId, NandSaveKind kind, string inPath, out string error)
        {
            var code = MelonDsNand.Call(_handle, titleId,
                (h, c, i) => MelonDsNand.ImportSave(h, c, i, (int)kind, inPath));
            error = code < 0 ? MelonDsNand.LastError(_handle) : null;
            return code == MelonDsNand.Ok;
        }

        /// <summary>Copy one file out of the NAND's filesystem, by its path inside the image.</summary>
        public bool ExportFile(string nandPath, string outPath, out string error)
        {
            var code = MelonDsNand.ExportFile(_handle, nandPath, outPath);
            error = code == MelonDsNand.Ok ? null : MelonDsNand.LastError(_handle);
            return code == MelonDsNand.Ok;
        }

        /// <summary>Write a file back INTO the NAND's filesystem. Only ever used to put back a copy
        /// the emulator itself produced: the settings blocks carry their own hash and the ticket is
        /// ES-encrypted, so a file assembled rather than restored would not be accepted.</summary>
        public bool ImportFile(string nandPath, string inPath, out string error)
        {
            var code = MelonDsNand.ImportFile(_handle, nandPath, inPath);
            error = code == MelonDsNand.Ok ? null : MelonDsNand.LastError(_handle);
            return code == MelonDsNand.Ok;
        }

        public bool RemoveFile(string nandPath, out string error)
        {
            var code = MelonDsNand.RemoveFile(_handle, nandPath);
            error = code == MelonDsNand.Ok ? null : MelonDsNand.LastError(_handle);
            return code == MelonDsNand.Ok;
        }

        /// <summary>Write out every file in the image with its size and hash, sorted by path.
        /// Returns how many entries, or -1. See MelonDsDelta for what two of these are for.</summary>
        public int Walk(string manifestPath, out string error)
        {
            var count = MelonDsNand.Walk(_handle, "0:", manifestPath);
            error = count < 0 ? MelonDsNand.LastError(_handle) : null;
            return count;
        }

        public void Dispose()
        {
            if (_handle == IntPtr.Zero) return;
            try { MelonDsNand.Close(_handle); } catch { }
            _handle = IntPtr.Zero;
        }
    }

    internal static class MelonDsNand
    {
        /// <summary>The name the DllImport attributes use. It is never a file on disk: a resolver
        /// maps it to the real one, which lives out of the way for a reason - see NativeDir.</summary>
        public const string LibraryName = "melonds-nand";

        /// <summary>Where the native library actually sits, and why it is not beside the plugin.
        ///
        /// LAUNCHBOX LOADS EVERY .dll IN A PLUGIN FOLDER AS A .NET ASSEMBLY. Measured, with a dialog
        /// to prove it: "System.BadImageFormatException: Bad IL format. The format of the file
        /// melonds-nand.dll is invalid ... failed to load during PluginLoader.LoadAssembly". A native
        /// library in that folder is not a plugin, but nothing tells the loader so.
        ///
        /// So it goes into a subfolder AND loses the .dll extension - either would probably do, and
        /// both cost nothing. Windows loads a library by path whatever it is called.</summary>
        private const string NativeDir = "native";
        private const string NativeFile = "melonds-nand.native";

        /// <summary>The ABI this plugin was written against. A library that answers anything else is
        /// refused rather than called - a signature that moved underneath us would not fail politely.</summary>
        private const int ExpectedAbi = 4;

        internal const int Ok = 0;
        internal const int No = 1;

        private static bool _probed;
        private static bool _usable;
        private static string _why;

        /// <summary>Teach the runtime where our library is, once, before any DllImport fires. A
        /// resolver is the supported way to load a native library from a path of our choosing; the
        /// alternative would be putting it somewhere the host's loader would trip over.</summary>
        static MelonDsNand()
        {
            try
            {
                NativeLibrary.SetDllImportResolver(typeof(MelonDsNand).Assembly, (name, assembly, path) =>
                {
                    if (!string.Equals(name, LibraryName, StringComparison.OrdinalIgnoreCase))
                        return IntPtr.Zero;
                    var file = NativePath();
                    return file != null && NativeLibrary.TryLoad(file, out var handle) ? handle : IntPtr.Zero;
                });
            }
            catch (Exception ex) { _why = "could not install the library resolver - " + ex.Message; }
        }

        /// <summary>The library file, in the subfolder first and beside the assembly second - the
        /// latter for a working copy that has not been through deploy.</summary>
        private static string NativePath()
        {
            var dir = AssemblyDir();
            if (dir == null) return null;
            foreach (var candidate in new[]
                     {
                         Path.Combine(dir, NativeDir, NativeFile),
                         Path.Combine(dir, NativeFile),
                         Path.Combine(dir, NativeDir, "melonds-nand.dll"),
                         Path.Combine(dir, "melonds-nand.dll"),
                     })
            {
                try { if (File.Exists(candidate)) return candidate; } catch { }
            }
            return null;
        }

        /// <summary>Is the library there, loadable, and the version we expect? Answered once, with
        /// the reason kept for the log.</summary>
        public static bool IsUsable(out string why)
        {
            if (!_probed)
            {
                _probed = true;
                try
                {
                    if (NativePath() == null)
                    {
                        _why = NativeFile + " was not found under " + (AssemblyDir() ?? "?");
                    }
                    else
                    {
                        var abi = AbiVersion();
                        _usable = abi == ExpectedAbi;
                        _why = _usable ? null
                             : NativeFile + " speaks ABI " + abi + ", this plugin expects " + ExpectedAbi;
                    }
                }
                catch (Exception ex) { _why = NativeFile + " could not be loaded - " + ex.Message; }
            }
            why = _why;
            return _usable;
        }

        /// <summary>Open a NAND, or null with the reason. The caller disposes.</summary>
        public static NandSession Open(string nandPath, string bios7Path, out string error)
        {
            error = null;
            if (!IsUsable(out var why)) { error = why; return null; }

            // NEVER WHILE melonDS IS RUNNING. The library opens the image read/write, and so does the
            // emulator: the C runtime allows both, so nothing stops two writers from working on the
            // same 240 MB file at once. melonDS holds the NAND for the whole session, writes a save
            // into it when the game does, and flushes on exit - an import or a save round-trip
            // underneath that is a corrupted NAND, and the NAND is the game AND its progress.
            //
            // This is not hypothetical: GetSaves runs whenever the page is drawn, including while a
            // game is being played and the user has alt-tabbed back. Found by doing it by hand during
            // a session, which is exactly how it would have happened to somebody else.
            var running = RunningEmulatorProcess();
            if (running != null)
            {
                error = "melonDS is running (" + running + ") and holds this NAND open. Close it first; "
                      + "writing to the image underneath the emulator would corrupt it.";
                return null;
            }

            // LOOK BEFORE CROSSING. Everything past this line runs native code in the host's own
            // process, where an access violation would take LaunchBox down with it - that is the one
            // real cost of calling a library rather than a separate program. melonDS checks the
            // footer itself and declines politely, but checking here first means a file that is not
            // a NAND at all never reaches it, and the user gets a sentence that says what is wrong
            // instead of a generic refusal.
            if (!LooksLikeNand(nandPath, out var verdict)) { error = verdict; return null; }

            try
            {
                var code = Open(nandPath, bios7Path, out IntPtr handle);
                if (code == Ok) return new NandSession(handle);
                error = LastError(IntPtr.Zero);
                return null;
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return null; }
        }

        /// <summary>Is melonDS running right now? Asked wherever one of its files is about to be
        /// touched - a NAND it holds is not ours to open, and certainly not ours to delete.</summary>
        public static bool EmulatorRunning() => RunningEmulatorProcess() != null;

        /// <summary>The name of a running melonDS process, or null. The same test MelonDsToml uses,
        /// and for a related reason: melonDS owns its files for the length of a session.</summary>
        private static string RunningEmulatorProcess()
        {
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcesses())
                {
                    using (p)
                    {
                        string n;
                        try { n = p.ProcessName; } catch { continue; }
                        if (n != null && n.StartsWith("melonDS", StringComparison.OrdinalIgnoreCase))
                            return n;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>The 16 bytes every DSi NAND dump carries, at 0x40 from the end - and again at
        /// 0x000FF800 when a tool cut the image short (DSi_NAND.cpp:42-69, and GBATEK on DSi SDMMC
        /// images). A file without either is not a NAND, whatever its size.</summary>
        private static readonly byte[] Footer = System.Text.Encoding.ASCII.GetBytes("DSi eMMC CID/CPU");

        private static bool LooksLikeNand(string path, out string why)
        {
            why = null;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) { why = path + " is not there"; return false; }
                if (info.Length < 0x100000) { why = path + " is far too small to be a NAND dump"; return false; }

                using var stream = File.OpenRead(path);
                if (HasFooterAt(stream, info.Length - 0x40) || HasFooterAt(stream, 0x000FF800)) return true;

                why = path + " carries no \"DSi eMMC CID/CPU\" footer, so it is not a NAND dump "
                    + "melonDS can read - a full dump of your own console is what is needed";
                return false;
            }
            catch (Exception ex) { why = "could not read " + path + " - " + ex.Message; return false; }
        }

        private static bool HasFooterAt(FileStream stream, long offset)
        {
            try
            {
                if (offset < 0 || offset + Footer.Length > stream.Length) return false;
                stream.Seek(offset, SeekOrigin.Begin);
                var buffer = new byte[Footer.Length];
                int total = 0, read;
                while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0)
                    total += read;
                if (total != buffer.Length) return false;
                for (int i = 0; i < buffer.Length; i++) if (buffer[i] != Footer[i]) return false;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Split a 16-hex-digit title id and hand the two halves to a native call. The id is
        /// written high word first, the way the NAND tree spells it.</summary>
        internal static int Call(IntPtr handle, string titleId, Func<IntPtr, uint, uint, int> f)
        {
            if (handle == IntPtr.Zero || titleId == null || titleId.Length != 16) return -1;
            try
            {
                var category = Convert.ToUInt32(titleId.Substring(0, 8), 16);
                var id = Convert.ToUInt32(titleId.Substring(8, 8), 16);
                return f(handle, category, id);
            }
            catch { return -1; }
        }

        internal static string LastError(IntPtr handle)
        {
            try
            {
                var p = LastErrorRaw(handle);
                return p == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(p) ?? "";
            }
            catch { return ""; }
        }

        private static string AssemblyDir()
        {
            try { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }
            catch { return null; }
        }

        // ── the imports ──────────────────────────────────────────────────────
        //
        // Cdecl and UTF-8 throughout, matching nand_api.h. The library is resolved from the plugin's
        // own folder, which is where deploy puts it and where the host loads the plugin from.

        [DllImport(LibraryName, EntryPoint = "mdsnand_abi_version", CallingConvention = CallingConvention.Cdecl)]
        private static extern int AbiVersion();

        [DllImport(LibraryName, EntryPoint = "mdsnand_open", CallingConvention = CallingConvention.Cdecl,
                   CharSet = CharSet.Ansi, BestFitMapping = false)]
        private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string nandPath,
                                       [MarshalAs(UnmanagedType.LPUTF8Str)] string bios7Path,
                                       out IntPtr handle);

        [DllImport(LibraryName, EntryPoint = "mdsnand_last_tmd_was_generated",
                   CallingConvention = CallingConvention.Cdecl)]
        internal static extern int LastTmdWasGenerated();

        [DllImport(LibraryName, EntryPoint = "mdsnand_close", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Close(IntPtr handle);

        [DllImport(LibraryName, EntryPoint = "mdsnand_console_id", CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong ConsoleId(IntPtr handle);

        [DllImport(LibraryName, EntryPoint = "mdsnand_title_exists", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int TitleExists(IntPtr handle, uint category, uint titleId);

        [DllImport(LibraryName, EntryPoint = "mdsnand_import_title", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ImportTitle(IntPtr handle,
                                               [MarshalAs(UnmanagedType.LPUTF8Str)] string appPath,
                                               [MarshalAs(UnmanagedType.LPUTF8Str)] string tmdPath,
                                               int readOnly);

        [DllImport(LibraryName, EntryPoint = "mdsnand_export_save", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ExportSave(IntPtr handle, uint category, uint titleId, int kind,
                                              [MarshalAs(UnmanagedType.LPUTF8Str)] string outPath);

        [DllImport(LibraryName, EntryPoint = "mdsnand_import_save", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ImportSave(IntPtr handle, uint category, uint titleId, int kind,
                                              [MarshalAs(UnmanagedType.LPUTF8Str)] string inPath);

        [DllImport(LibraryName, EntryPoint = "mdsnand_export_file", CallingConvention = CallingConvention.Cdecl,
                   CharSet = CharSet.Ansi, BestFitMapping = false)]
        internal static extern int ExportFile(IntPtr handle, string nandPath, string outPath);

        [DllImport(LibraryName, EntryPoint = "mdsnand_import_file", CallingConvention = CallingConvention.Cdecl,
                   CharSet = CharSet.Ansi, BestFitMapping = false)]
        internal static extern int ImportFile(IntPtr handle, string nandPath, string inPath);

        [DllImport(LibraryName, EntryPoint = "mdsnand_remove_file", CallingConvention = CallingConvention.Cdecl,
                   CharSet = CharSet.Ansi, BestFitMapping = false)]
        internal static extern int RemoveFile(IntPtr handle, string nandPath);

        [DllImport(LibraryName, EntryPoint = "mdsnand_walk", CallingConvention = CallingConvention.Cdecl,
                   CharSet = CharSet.Ansi, BestFitMapping = false)]
        internal static extern int Walk(IntPtr handle, string root, string manifestPath);

        [DllImport(LibraryName, EntryPoint = "mdsnand_last_error", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr LastErrorRaw(IntPtr handle);
    }
}
