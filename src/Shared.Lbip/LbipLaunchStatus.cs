// What a launch is doing before its emulator starts, for the host to show (Mehdi, 14/10): LiteBox draws it on its
// loading screen as a yellow pre-bar - one fill per step, the step's text under it.
//
// THE CONTRACT is LiteBox's (LbApiHost Host\Gameplay\LaunchStatus.cs), by NAME in the AppDomain - this folder is
// compiled into each plugin, so no type can be shared - holding a delegate of BCL types only:
//     "LbIntegrations.LaunchStatus.v1" -> Action<string, double>
//     text: the step ("Extracting game.iso…"); null = the step already shown; a different text = a new step.
//     fraction: 0..1, NaN = unknown.
// LaunchBox sets nothing under that name: every call here is then nothing. Never throws, never blocks.
//
// THE COPIES (CopyStream, CopyFile): the plugins' long copies before a launch - an archive's entry, a disc image into
// the cache, a 240 MB NAND - report their bytes through here. Reports are thinned (a new percent, or 100 ms), so a
// copy costs the same with or without a host listening.

using System;
using System.Diagnostics;
using System.IO;

namespace LbIntegrations.Lbip
{
    internal static class LbipLaunchStatus
    {
        private const string DataName = "LbIntegrations.LaunchStatus.v1";

        /// <summary>A host shows the launch's steps now (LiteBox, during a launch's pre-phase) - a plugin may then leave
        /// out its own progress window, which would only fight the host's loading screen for the top.</summary>
        public static bool Listening
        {
            get { try { return AppDomain.CurrentDomain.GetData(DataName) is Action<string, double>; } catch { return false; } }
        }

        /// <summary>The step being done before the emulator starts, and its progress (0..1; NaN unknown).</summary>
        public static void Report(string text, double fraction = double.NaN)
        {
            try { (AppDomain.CurrentDomain.GetData(DataName) as Action<string, double>)?.Invoke(text, fraction); }
            catch { }
        }

        /// <summary>The progress of the step already shown.</summary>
        public static void Progress(double fraction) => Report(null, fraction);

        /// <summary><paramref name="source"/> into <paramref name="target"/>, <paramref name="total"/> bytes expected
        /// (0 or less: unknown - the step only), shown as <paramref name="step"/>. Returns the bytes copied.</summary>
        public static long CopyStream(Stream source, Stream target, long total, string step, int bufferSize = 1 << 20)
        {
            if (step != null) Report(step, total > 0 ? 0.0 : double.NaN);
            var buffer = new byte[Math.Max(81920, bufferSize)];
            long done = 0; int lastPercent = -1;
            var since = Stopwatch.StartNew();
            int n;
            while ((n = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                target.Write(buffer, 0, n);
                done += n;
                if (total > 0)
                {
                    int percent = (int)Math.Min(100, done * 100 / total);
                    if (percent != lastPercent || since.ElapsedMilliseconds >= 100)
                    {
                        lastPercent = percent; since.Restart();
                        Progress(Math.Min(1.0, (double)done / total));
                    }
                }
            }
            return done;
        }

        /// <summary>A file copied as File.Copy would, its bytes shown as <paramref name="step"/> - the timestamps and
        /// attributes File.Copy keeps are kept. A failure leaves no partial target behind and throws as File.Copy does.</summary>
        public static void CopyFile(string source, string target, bool overwrite, string step)
        {
            if (!overwrite && File.Exists(target)) throw new IOException("The file '" + target + "' already exists.");
            var info = new FileInfo(source);
            long total = info.Length;
            using var s = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
            bool opened = false;
            try
            {
                using (var t = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                {
                    opened = true;
                    t.SetLength(total);     // one allocation, not a growing file
                    t.Position = 0;
                    long done = CopyStream(s, t, total, step);
                    if (done != total) t.SetLength(done);
                }
                File.SetCreationTimeUtc(target, info.CreationTimeUtc);
                File.SetLastWriteTimeUtc(target, info.LastWriteTimeUtc);
                File.SetAttributes(target, info.Attributes);
            }
            catch
            {
                // Only a target this copy wrote: a source that could not be read never touched it.
                if (opened) try { File.Delete(target); } catch { }
                throw;
            }
        }
    }
}
