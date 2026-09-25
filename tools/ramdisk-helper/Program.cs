// Elevated ImDisk mount/unmount helper.
//
// THE SAME HELPER LITEBOX SHIPS. Its original lives in the ExtendDB plugin's own tree and LiteBox
// deploys a build of it to <LaunchBox>\ThirdParty\RomExtractor\ramdisk\; this pack deploys a build
// of it to the very same folder, so the two share one helper, one scheduled task and one ImDisk
// driver rather than standing up a second machine beside the first.
//
// THE CONTRACT BETWEEN THEM IS ramdisk.cfg, NOT THE BINARY. Whoever installs first owns the file -
// neither side ever overwrites it - so the two builds may differ in every way that does not change
// how the cfg is read or the result written. That is what makes vendoring the source here safe
// rather than a second copy waiting to drift.
//
// Reads <exe-dir>\ramdisk.cfg (key=value lines), shells to System32\imdisk.exe, writes
// <exe-dir>\ramdisk.result with the outcome. No command-line args, so a single fixed scheduled-task
// command ("run this exe") handles every operation.
//
// ramdisk.cfg:
//   action = mount | umount        (also accepts "unmount" and "remove")
//   size   = <MB>                  (mount only)
//   drive  = R                     (drive letter, no colon)
//   label  = RomExtractorRAM       (read and then ignored - LiteBox writes it, nothing uses it)
//
// ramdisk.result:
//   OK|FAIL <action> <drive> exit=<n>     the imdisk run, whatever it did
//   ERROR <message>                       we never got as far as imdisk

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace RamDiskHelper
{
    internal static class Program
    {
        private static int Main()
        {
            string dir = AppContext.BaseDirectory;
            string cfgPath = Path.Combine(dir, "ramdisk.cfg");
            string resultPath = Path.Combine(dir, "ramdisk.result");
            try
            {
                var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(cfgPath))
                {
                    var l = line.Trim();
                    if (l.Length == 0 || l.StartsWith("#")) continue;
                    int i = l.IndexOf('=');
                    if (i <= 0) continue;
                    kv[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim();
                }

                string action = Get(kv, "action", "mount");
                string drive = Get(kv, "drive", "R").TrimEnd(':');
                string size = Get(kv, "size", "1024");
                bool umount = action.Equals("umount", StringComparison.OrdinalIgnoreCase)
                           || action.Equals("unmount", StringComparison.OrdinalIgnoreCase)
                           || action.Equals("remove", StringComparison.OrdinalIgnoreCase);

                string imdisk = Path.Combine(Environment.SystemDirectory ?? @"C:\Windows\System32", "imdisk.exe");
                var psi = new ProcessStartInfo(imdisk) { UseShellExecute = false, CreateNoWindow = true };
                if (umount)
                {
                    psi.ArgumentList.Add("-D");
                    psi.ArgumentList.Add("-m"); psi.ArgumentList.Add(drive + ":");
                }
                else
                {
                    psi.ArgumentList.Add("-a");
                    psi.ArgumentList.Add("-s"); psi.ArgumentList.Add(size + "M");
                    psi.ArgumentList.Add("-m"); psi.ArgumentList.Add(drive + ":");
                    psi.ArgumentList.Add("-p"); psi.ArgumentList.Add("/fs:ntfs /q /y");
                }

                using var p = Process.Start(psi);
                p.WaitForExit();
                File.WriteAllText(resultPath, (p.ExitCode == 0 ? "OK" : "FAIL") + " " + (umount ? "umount" : "mount") + " " + drive + " exit=" + p.ExitCode);
                return p.ExitCode;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(resultPath, "ERROR " + ex.Message); } catch { }
                return 1;
            }
        }

        private static string Get(Dictionary<string, string> kv, string key, string def)
            => kv.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : def;
    }
}
