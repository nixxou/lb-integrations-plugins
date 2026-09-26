// Calling native\vita3k-install.native: the two installs Vita3K performs that a plain unzip cannot.
//
//   DecryptApp        a NoNpDRM dump ships its files still encrypted, with the PFS descriptors
//                     (sce_pfs/) and the licence (sce_sys/package/work.bin) beside them. Vita3K's
//                     installer decrypts every file through that layer before the game can boot -
//                     measured against a reference install made by the emulator itself: same 34
//                     paths, 32 of them with different bytes, and an eboot.bin that starts "SCE\0"
//                     only once decrypted. Without it:
//                         decrypt_fself: Invalid SELF: file is either not a SELF or is still encrypted
//
//   InstallFirmware   a system update (.PUP) into the virtual filesystem - what `Vita3K.exe
//                     --firmware` does, without the emulator starting around it. Measured: the
//                     emulator's own run died with 0xC0000409 on the first attempt, twice in one
//                     morning, and crashed outright from a folder deeper than MAX_PATH. This does
//                     neither, and its output is byte-identical to the emulator's on all four
//                     partitions - 1825 files.
//
// Both are Vita3K's own code, compiled unmodified under tools\vita3k-install (but for two lines
// added to psvpfsparser's Utils.cpp, which hash each decrypted file as it is written), and LOADED INTO THIS
// PROCESS - which is why this plugin is GPL-2.0-or-later: see LICENSE.md beside it.
//
// ONE LOAD PER CALL. The library is loaded, called once and freed. Vita3K's pup.cpp numbers the
// pieces it extracts with a function-local static counter that nothing resets; kept loaded, the DLL
// would carry it from one install to the next, and past 99 the two-digit names sort out of order
// when the pieces are joined - a corrupt partition image with no error. Freeing the library
// re-initialises it. The cost is a LoadLibrary per install, next to seconds of decryption.
//
// NOT beside the plugin with a .dll extension: LaunchBox loads every .dll in a plugin folder as a
// .NET assembly and shows an error dialog for a native one - measured with melonDS. It sits in
// native\ as .native, and is loaded by path.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace LbIntegrations.Vita3k
{
    internal static class Vita3kNative
    {
        public const string FileName = "vita3k-install.native";

        /// <summary>The ABI the plugin was written against. A library that answers anything else is
        /// not called: the argument lists would not mean the same thing.</summary>
        private const int AbiVersion = 2;   // 2: v3k_decrypt_hashed

        /// <summary>Set by the probe, which runs the plugin from its build folder where there is no
        /// native\ beside it. Nothing else should need it.</summary>
        public const string OverrideVariable = "LBIP_VITA3K_NATIVE";

        public static string LibraryPath
        {
            get
            {
                var forced = Environment.GetEnvironmentVariable(OverrideVariable);
                if (!string.IsNullOrWhiteSpace(forced)) return forced;
                var dir = Path.GetDirectoryName(typeof(Vita3kNative).Assembly.Location);
                return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "native", FileName);
            }
        }

        public static bool Available => LibraryPath is string p && File.Exists(p);

        /// <summary>(fraction 0..1). Called on the thread that made the call.</summary>
        public delegate void Progress(double fraction);

        // ── the C door ───────────────────────────────────────────────────────

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ProgressFn(IntPtr user, ulong done, ulong total);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int AbiFn();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private delegate int DecryptFn(string app, string licence, string destination,
                                       ProgressFn progress, IntPtr user, byte[] message, int messageLen);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private delegate void FileFn(IntPtr user, [MarshalAs(UnmanagedType.LPWStr)] string relative, ulong size,
                                     [MarshalAs(UnmanagedType.LPStr)] string sha1);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private delegate int DecryptHashedFn(string app, string licence, string destination,
                                             ProgressFn progress, FileFn onFile, IntPtr user,
                                             byte[] message, int messageLen);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private delegate int FirmwareFn(string pup, string vitaFs,
                                        ProgressFn progress, IntPtr user, byte[] message, int messageLen);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SelftestFn(byte[] message, int messageLen);

        // ── the operations ───────────────────────────────────────────────────

        /// <summary>Decrypt <paramref name="encryptedApp"/> into <paramref name="destination"/>, which
        /// the library clears first.</summary>
        public static bool DecryptApp(string encryptedApp, string licence, string destination,
                                      Progress progress, out string error)
            => DecryptApp(encryptedApp, licence, destination, progress, null, out error);

        /// <summary>The same decrypt, telling <paramref name="onFile"/> about every file it wrote -
        /// (path relative to the destination with forward slashes, size, SHA-1 in upper-case hex),
        /// hashed from the bytes as they went to the disk, so nobody has to read them back. Called
        /// after the decrypt succeeded, on this thread.</summary>
        public static bool DecryptApp(string encryptedApp, string licence, string destination,
                                      Progress progress, Action<string, long, string> onFile, out string error)
        {
            if (!Available)
            {
                error = "this dump is PFS-encrypted and needs " + FileName + ", which is not installed beside the plugin"
                        + (LibraryPath != null ? " (" + LibraryPath + ")" : "");
                Log.Warn(error);
                return false;
            }
            if (onFile == null)
                return Call("decrypt", progress, out error, (lib, cb, buffer) =>
                    Export<DecryptFn>(lib, "v3k_decrypt")(encryptedApp, licence, destination, cb, IntPtr.Zero, buffer, buffer.Length));

            // Never throws back into native code: that would be a crash of the host.
            FileFn file = (user, relative, size, sha1) =>
            {
                try { onFile(relative, (long)size, sha1); } catch { }
            };
            bool ok = Call("decrypt", progress, out error, (lib, cb, buffer) =>
                Export<DecryptHashedFn>(lib, "v3k_decrypt_hashed")(encryptedApp, licence, destination, cb, file,
                                                                   IntPtr.Zero, buffer, buffer.Length));
            GC.KeepAlive(file);
            return ok;
        }

        /// <summary>Install one firmware package into <paramref name="vitaFs"/>. The caller still checks
        /// that the partition it expected is populated: the verdict here covers the four together.</summary>
        public static bool InstallFirmware(string pup, string vitaFs, Progress progress, out string error)
            => Call("firmware", progress, out error, (lib, cb, buffer) =>
                Export<FirmwareFn>(lib, "v3k_firmware")(pup, vitaFs, cb, IntPtr.Zero, buffer, buffer.Length));

        /// <summary>The library's own known-answer test of its cryptography.</summary>
        public static bool Selftest(out string report)
            => Call("selftest", null, out report, (lib, cb, buffer) =>
                Export<SelftestFn>(lib, "v3k_selftest")(buffer, buffer.Length), wantReport: true);

        // ── one load, one call, one free ─────────────────────────────────────

        private static bool Call(string what, Progress progress, out string error,
                                 Func<IntPtr, ProgressFn, byte[], int> invoke, bool wantReport = false)
        {
            error = null;
            IntPtr lib = IntPtr.Zero;
            try
            {
                var path = LibraryPath;
                if (!NativeLibrary.TryLoad(path, out lib))
                {
                    error = "could not load " + path;
                    Log.Warn(error);
                    return false;
                }

                int abi = Export<AbiFn>(lib, "v3k_abi_version")();
                if (abi != AbiVersion)
                {
                    error = FileName + " speaks ABI " + abi + ", this plugin " + AbiVersion + " - not calling it";
                    Log.Warn(error);
                    return false;
                }

                // The callback runs on this thread, inside the native call. It must never throw back
                // into native code - that is a crash of the host - so every failure in it is dropped.
                ProgressFn callback = null;
                if (progress != null)
                    callback = (user, done, total) =>
                    {
                        try { progress(total == 0 ? 0 : Math.Min(1.0, (double)done / total)); }
                        catch { }
                    };

                var buffer = new byte[wantReport ? 4096 : 1024];
                var watch = System.Diagnostics.Stopwatch.StartNew();
                int result = invoke(lib, callback, buffer);
                GC.KeepAlive(callback);   // alive until the native side can no longer call it

                var message = Encoding.UTF8.GetString(buffer, 0, Math.Max(0, Array.IndexOf(buffer, (byte)0))).TrimEnd();
                if (result == 0)
                {
                    if (wantReport) error = message;
                    Log.Info(FileName + " " + what + " in " + watch.ElapsedMilliseconds + " ms - " + LastLine(message));
                    return true;
                }

                error = FileName + " " + what + " failed (" + result + "): " + LastLine(message);
                Log.Warn(error);
                return false;
            }
            catch (Exception ex)
            {
                error = "could not call " + FileName + " " + what + ": " + ex.GetType().Name + ": " + ex.Message;
                Log.Warn(error, ex);
                return false;
            }
            finally
            {
                if (lib != IntPtr.Zero) { try { NativeLibrary.Free(lib); } catch { } }
            }
        }

        private static T Export<T>(IntPtr lib, string name) where T : Delegate
            => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, name));

        private static string LastLine(string text)
        {
            var lines = (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length > 0 ? lines[lines.Length - 1].Trim() : "";
        }
    }
}
