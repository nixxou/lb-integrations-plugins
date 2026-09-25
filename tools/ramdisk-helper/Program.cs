// Elevated ImDisk mount/unmount helper.
//
// ONE HELPER, TWO PRODUCTS. LiteBox and the Nixx integration pack both deploy a build of this file
// to <LaunchBox>\ThirdParty\RomExtractor\ramdisk\ - the same folder, driven by the same scheduled
// task, over the same ImDisk driver - rather than each standing up a machine of its own beside the
// other. This source is therefore kept IDENTICAL in both repositories, and it is the source that is
// shared, not the binary: two builds of it differ in their embedded commit hash and agree on
// everything that matters.
//
// THE CONTRACT BETWEEN THEM IS ramdisk.cfg PLUS THE FILE VERSION. Two products write this file and
// neither can see the other's release schedule, so the rule on both sides is: deploy when the helper
// is ABSENT OR OLDER, never when it is newer. That is what makes an install by either one valid for
// the other, a refresh included. And before sending a key added after 1.0, a caller reads
// FileVersion off the deployed exe and falls back when it is too old - an older helper does not fail
// on a key it does not know, it quietly does something else, which is worse.
//
// Reads <exe-dir>\ramdisk.cfg (key=value lines), shells to System32\imdisk.exe, writes
// <exe-dir>\ramdisk.result with the outcome. No command-line args, so a single fixed scheduled-task
// command ("run this exe") handles every operation.
//
// ramdisk.cfg
//   action = mount | umount        (also accepts "unmount" and "remove")
//   drive  = R                     drive letter, no colon
//   size   = <MB>                  mount only
//   label  = RomExtractorRAM       read and then ignored - LiteBox writes it, nothing uses it
//
//   -- added in 1.1, all optional; leaving them all out reproduces 1.0 exactly ------------------
//   image  = <path>                a disk image to back the drive with, or to preload it from
//   type   = vm | file | awe       where the bytes live (below); absent means imdisk's own default
//   sparse = 1                     NTFS sparse attribute, type=file only
//   format = /fs:ntfs /q /y        format parameters. Defaulted by whether the image EXISTS, not
//                                  by whether one was named: an image being created has no
//                                  filesystem yet and must be formatted, one being loaded already
//                                  has and must not be.
//
// WHAT THE THREE TYPES MEAN, in imdisk's words and in ours:
//
//   vm    "allocated from virtual memory ... if a file is specified with -f that file is loaded
//         into the memory allocated for the disk image". A pristine image, a working copy in
//         memory, the file on disk untouched. The whole declared size is committed up front.
//
//   file  the image file IS the disk and changes land in it. With sparse=1 a large mostly-empty
//         image costs only what has been written.
//
//   awe   file type plus "-o awe": "the driver copies contents of image file to physical memory.
//         No changes are written to image file." The same untouched-original property as vm, but
//         the size comes from the image instead of having to be declared.
//
// ramdisk.result
//   OK|FAIL <action> <drive> exit=<n>     the imdisk run, whatever it did
//   ERROR <message>                       we never got as far as imdisk
//
// THIS IS NOT A PRIVILEGE BOUNDARY, and never was: the cfg is writable by the user and this runs
// elevated, which is inherent to every no-UAC elevation bridge - LiteBox says the same of its
// admin-launch helper. 1.1 widens what the cfg can ask for, so the values are held to the shapes
// imdisk expects: type is one of three words and nothing else, and every value is passed as a single
// argument rather than pasted into a command line.

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

                string image = Get(kv, "image", "");
                string type = Get(kv, "type", "").ToLowerInvariant();
                bool sparse = Get(kv, "sparse", "") == "1";
                // FORMAT WHEN THERE IS NOTHING TO PRESERVE, which is not the same question as
                // "was an image named". Measured, by getting it wrong: creating a new file-backed
                // image with the format left empty attaches a disk with no filesystem on it - imdisk
                // exits 0, the device appears, and the drive letter is not a directory. An image
                // that already exists carries its own filesystem and must NOT be formatted, or the
                // base would be wiped by the act of mounting it.
                string format = Get(kv, "format",
                    image.Length == 0 || !File.Exists(image) ? "/fs:ntfs /q /y" : "");

                // Only the three imdisk knows about. Anything else is treated as not given rather
                // than handed on, because this runs elevated.
                bool awe = type == "awe";
                if (awe) type = "file";
                if (type != "vm" && type != "file") type = "";

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
                    if (type.Length > 0) { psi.ArgumentList.Add("-t"); psi.ArgumentList.Add(type); }
                    if (image.Length > 0) { psi.ArgumentList.Add("-f"); psi.ArgumentList.Add(image); }

                    // A size is needed for a blank disk, and for a file that does not exist yet. It
                    // is meaningless when loading an existing image, whose size is the image's.
                    if (image.Length == 0 || !File.Exists(image))
                    { psi.ArgumentList.Add("-s"); psi.ArgumentList.Add(size + "M"); }

                    var options = new List<string>();
                    if (awe) options.Add("awe");
                    if (sparse) options.Add("sparse");
                    if (options.Count > 0) { psi.ArgumentList.Add("-o"); psi.ArgumentList.Add(string.Join(",", options)); }

                    psi.ArgumentList.Add("-m"); psi.ArgumentList.Add(drive + ":");
                    if (format.Length > 0) { psi.ArgumentList.Add("-p"); psi.ArgumentList.Add(format); }
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
