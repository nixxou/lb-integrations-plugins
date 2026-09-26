// Decrypting an app through its PFS layer, by running native\vita3k-pfs.exe.
//
// WHY A SEPARATE PROGRAM. A NoNpDRM dump ships its files still encrypted, with the PFS descriptors
// (sce_pfs/) and the licence (sce_sys/package/work.bin) beside them. Vita3K's installer decrypts every
// file through that layer before the game can boot - measured against a reference install made by
// the emulator itself: same 34 paths, but 32 of them with different bytes, and an eboot.bin that
// starts "SCE\0" only once decrypted. A plain unzip therefore produces a game that does not start:
//
//     decrypt_fself: Invalid SELF: file is either not a SELF or is still encrypted (unsupported)
//
// The decryption is Vita3K's own library, psvpfsparser, built into a small tool under
// tools\vita3k-pfs. It is GPL code, run as its own process: nothing of it is loaded into a host.
// Measured on the reference game: the tool's output is byte-identical to Vita3K's install.

using System;
using System.Diagnostics;
using System.IO;

namespace LbIntegrations.Vita3k
{
    internal static class Vita3kPfs
    {
        public const string ToolName = "vita3k-pfs.exe";

        /// <summary>Set by the probe, which runs the plugin from its build folder where there is no
        /// native\ beside it. Nothing else should need it.</summary>
        public const string OverrideVariable = "LBIP_VITA3K_PFS";

        /// <summary>Generous on purpose: the measured cost is 0.4 s for a 75 MB game, and the largest
        /// Vita titles are around 4 GB. Past this the tool is assumed stuck, not slow.</summary>
        private const int TimeoutMs = 15 * 60 * 1000;

        public static string ToolPath
        {
            get
            {
                var forced = Environment.GetEnvironmentVariable(OverrideVariable);
                if (!string.IsNullOrWhiteSpace(forced)) return forced;
                var dir = Path.GetDirectoryName(typeof(Vita3kPfs).Assembly.Location);
                return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "native", ToolName);
            }
        }

        /// <summary>Decrypt <paramref name="encryptedApp"/> into <paramref name="destination"/>, which
        /// the tool clears first. True only when the tool reported success on its last line.</summary>
        public static bool Decrypt(string encryptedApp, string licence, string destination, out string error)
        {
            error = null;
            var tool = ToolPath;
            if (string.IsNullOrEmpty(tool) || !File.Exists(tool))
            {
                error = "this dump is PFS-encrypted and needs " + ToolName + ", which is not installed beside the plugin"
                        + (tool != null ? " (" + tool + ")" : "");
                Log.Warn(error);
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo(tool)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                psi.ArgumentList.Add("decrypt");
                psi.ArgumentList.Add(encryptedApp);
                psi.ArgumentList.Add(licence);
                psi.ArgumentList.Add(destination);

                var watch = Stopwatch.StartNew();
                using var p = Process.Start(psi);
                // Both streams drained, or a chatty child fills a pipe and the wait never ends.
                var stderr = p.StandardError.ReadToEndAsync();
                var stdout = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(TimeoutMs))
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                    error = ToolName + " did not finish within " + (TimeoutMs / 60000) + " minutes";
                    Log.Warn(error);
                    return false;
                }

                var last = LastLine(stdout);
                if (p.ExitCode == 0 && last.StartsWith("OK", StringComparison.Ordinal))
                {
                    Log.Info("decrypted the PFS layer in " + watch.ElapsedMilliseconds + " ms - " + last);
                    return true;
                }

                error = ToolName + " failed (exit " + p.ExitCode + "): " + (last.Length > 0 ? last : LastLine(stderr.Result));
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
