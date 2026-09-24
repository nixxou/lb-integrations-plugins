// Writing a small file so that a crash leaves either the old one or the new one, never half of each.
//
// This used to live in MelonDsToml, which is where it was first needed and where it did not belong:
// nothing about it is TOML, and the DSi engine calls it for receipts, records and recipes. It moved
// here when the engine became shared, unchanged.
//
// THE RETRIES ARE NOT DECORATION. File.Replace fails while something else has the target open for a
// moment - an antivirus, a backup agent, the host reading the same file to draw a card - and the
// honest answer to that is to try again rather than to lose the write.

using System;
using System.IO;
using System.Threading;

namespace LbIntegrations.Dsi
{
    internal static class Atomic
    {
        /// <summary>Write bytes through a temporary file beside the target, then swap. Answers
        /// nothing: every caller here treats a failed write as "say so in the log and carry on",
        /// because none of these files is worth failing a launch over.</summary>
        public static void WriteBytes(string path, byte[] content)
        {
            if (path == null || content == null) return;

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, content);

                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        if (File.Exists(path)) File.Replace(temporary, path, null);
                        else File.Move(temporary, path);
                        return;
                    }
                    catch (IOException) when (attempt < 2)
                    {
                        Thread.Sleep(60);
                    }
                }
            }
            catch (Exception ex)
            {
                DsiLog.Verbose("could not write " + path + " - " + ex.Message);
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
        }
    }
}
