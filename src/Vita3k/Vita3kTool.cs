// Running native\vita3k-install.exe: the two installs Vita3K performs that a plain unzip cannot.
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
//                     morning, and crashed outright from a folder deeper than MAX_PATH. The tool
//                     does neither, and its output is byte-identical to the emulator's on all four
//                     partitions - 1825 files.
//
// Both are Vita3K's own code, compiled unmodified under tools\vita3k-install. It is GPL, run as its
// own process: nothing of it is loaded into a host.

using System;
using System.Diagnostics;
using System.IO;

namespace LbIntegrations.Vita3k
{
    internal static class Vita3kTool
    {
        public const string ToolName = "vita3k-install.exe";

        /// <summary>Set by the probe, which runs the plugin from its build folder where there is no
        /// native\ beside it. Nothing else should need it.</summary>
        public const string OverrideVariable = "LBIP_VITA3K_TOOL";

        /// <summary>Generous on purpose: measured 0.3 s to decrypt a 75 MB game and 6 s for the main
        /// firmware, and the largest Vita titles are around 4 GB. Past this the tool is assumed stuck,
        /// not slow.</summary>
        private const int TimeoutMs = 15 * 60 * 1000;

        public static string ToolPath
        {
            get
            {
                var forced = Environment.GetEnvironmentVariable(OverrideVariable);
                if (!string.IsNullOrWhiteSpace(forced)) return forced;
                var dir = Path.GetDirectoryName(typeof(Vita3kTool).Assembly.Location);
                return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "native", ToolName);
            }
        }

        public static bool Available => ToolPath is string p && File.Exists(p);

        /// <summary>Decrypt <paramref name="encryptedApp"/> into <paramref name="destination"/>, which
        /// the tool clears first.</summary>
        public static bool DecryptApp(string encryptedApp, string licence, string destination, out string error)
        {
            if (!Available)
            {
                error = "this dump is PFS-encrypted and needs " + ToolName + ", which is not installed beside the plugin"
                        + (ToolPath != null ? " (" + ToolPath + ")" : "");
                Log.Warn(error);
                return false;
            }
            return Run(out error, "decrypt", encryptedApp, licence, destination);
        }

        /// <summary>Install one firmware package into <paramref name="vitaFs"/>. The caller still checks
        /// that the partition it expected is populated: the tool's verdict covers the four together.</summary>
        public static bool InstallFirmware(string pup, string vitaFs, out string error)
            => Run(out error, "firmware", pup, vitaFs);

        /// <summary>Run the tool, true only when it reported success on its last line.</summary>
        private static bool Run(out string error, params string[] arguments)
        {
            error = null;
            var tool = ToolPath;
            try
            {
                var psi = new ProcessStartInfo(tool)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var a in arguments) psi.ArgumentList.Add(a);

                var watch = Stopwatch.StartNew();
                using var p = Process.Start(psi);
                // Both streams drained, or a chatty child fills a pipe and the wait never ends.
                var stderr = p.StandardError.ReadToEndAsync();
                var stdout = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(TimeoutMs))
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                    error = ToolName + " " + arguments[0] + " did not finish within " + (TimeoutMs / 60000) + " minutes";
                    Log.Warn(error);
                    return false;
                }

                var last = LastLine(stdout);
                if (p.ExitCode == 0 && last.StartsWith("OK", StringComparison.Ordinal))
                {
                    Log.Info(ToolName + " " + arguments[0] + " in " + watch.ElapsedMilliseconds + " ms - " + last);
                    return true;
                }

                error = ToolName + " " + arguments[0] + " failed (exit 0x" + p.ExitCode.ToString("X")
                        + "): " + (last.Length > 0 ? last : LastLine(stderr.Result));
                Log.Warn(error);
                return false;
            }
            catch (Exception ex)
            {
                error = "could not run " + ToolName + ": " + ex.GetType().Name + ": " + ex.Message;
                Log.Warn(error, ex);
                return false;
            }
        }

        private static string LastLine(string text)
        {
            var lines = (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length > 0 ? lines[lines.Length - 1].Trim() : "";
        }
    }
}
