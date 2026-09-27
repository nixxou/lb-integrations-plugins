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
//   action = clean                 (1.2) free physical memory, mount nothing - see below
//   action = vhdx-create           (1.3) a new dynamic VHDX, formatted NTFS, left detached:
//                                    image=<path.vhdx> size=<MB> [label=<name>]
//   action = vhdx-child            (1.3) a new DIFFERENCING VHDX over a parent, left detached:
//                                    image=<child.vhdx> parent=<parent.vhdx>
//   action = vhdx-attach           (1.3) attach and give its volume a letter:
//                                    image=<path.vhdx> drive=<letter> [readonly=1]
//   action = vhdx-detach           (1.3) detach it:  image=<path.vhdx>
//   id     = <letters, digits, ->  (1.2) echoed at the end of ramdisk.result as " id=<id>", so the
//                                  caller can tell ITS answer from the answer of a run somebody
//                                  else started: the task ignores a second instance while one is
//                                  going, and the file it then reads back belongs to that one
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
//   OK clean trimmed=<n> flushed=<0|1>    a clean run (1.2)
//   ERROR <message>                       we never got as far as imdisk
//
// action = clean (1.2): WHAT FREES MEMORY BEFORE A RAM DISK IS ASKED FOR, and only that. Two steps,
// both needing the privileges only this elevated run has:
//   - EmptyWorkingSet on every process holding more than 100 MB: their pages leave RAM, the clean
//     ones straight to the standby list (which counts as available), the dirty ones to the
//     modified list
//   - flush the modified list, which writes those dirty pages to the page file so they become
//     available too. THAT IS A WRITE TO DISK, of other programs' memory, once - the price of
//     keeping a session in RAM when RAM is short, instead of playing it on the disk entirely
// What it deliberately does NOT do: purge the standby list. The standby list already counts as
// available memory, so purging it gains nothing on the number a caller checks, and it throws away
// the file cache - the front end would then reload every image from disk.
//
// AN OLDER HELPER READS "clean" AS A MOUNT: it only knows the unmount words and takes anything else
// for the default. A caller therefore checks FileVersion >= 1.2 before ever sending it.
//
// THE vhdx-* ACTIONS (1.3), for a pristine image kept clean under a differencing disk per session.
// Through diskpart: it is on every edition of Windows (the Hyper-V cmdlets are not), it creates
// differencing disks ("create vdisk ... parent="), and this runs elevated already. And because this
// runs elevated and the values go into a diskpart SCRIPT, every one is held to its shape first - an
// absolute path ending .vhdx with no quote and no line break in it, one letter, a number, a plain
// label - or nothing runs at all: a line break in a path would otherwise be a diskpart command.
// Same caution as clean: an older helper reads these as a mount; check FileVersion >= 1.3.
//
//   OK|FAIL vhdx-<what> exit=<n>[ - <diskpart's last words>]
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
using System.Runtime.InteropServices;

namespace RamDiskHelper
{
    internal static class Program
    {
        private static int Main()
        {
            string dir = AppContext.BaseDirectory;
            string cfgPath = Path.Combine(dir, "ramdisk.cfg");
            string resultPath = Path.Combine(dir, "ramdisk.result");
            string id = "";
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

                id = Id(Get(kv, "id", ""));
                string action = Get(kv, "action", "mount");
                if (action.StartsWith("vhdx-", StringComparison.OrdinalIgnoreCase))
                {
                    File.WriteAllText(resultPath, Vhdx(action.ToLowerInvariant(), kv, dir) + id);
                    return 0;
                }
                if (action.Equals("clean", StringComparison.OrdinalIgnoreCase))
                {
                    File.WriteAllText(resultPath, Clean() + id);
                    return 0;
                }
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
                File.WriteAllText(resultPath, (p.ExitCode == 0 ? "OK" : "FAIL") + " " + (umount ? "umount" : "mount") + " " + drive + " exit=" + p.ExitCode + id);
                return p.ExitCode;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(resultPath, "ERROR " + ex.Message + id); } catch { }
                return 1;
            }
        }

        /// <summary>" id=&lt;id&gt;" for an id made of letters, digits and dashes - "" for anything
        /// else, since this is written into a file somebody else parses.</summary>
        private static string Id(string raw)
        {
            if (raw.Length == 0 || raw.Length > 64) return "";
            foreach (var c in raw) if (!(char.IsLetterOrDigit(c) || c == '-')) return "";
            return " id=" + raw;
        }

        private static string Get(Dictionary<string, string> kv, string key, string def)
            => kv.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : def;

        // ── action = vhdx-* ──────────────────────────────────────────────────

        private static string Vhdx(string action, Dictionary<string, string> kv, string dir)
        {
            string image = Get(kv, "image", "");
            if (!SafeVhdx(image)) return "FAIL " + action + " exit=-1 - image is not an absolute .vhdx path of a plain shape";
            var script = new System.Text.StringBuilder();
            switch (action)
            {
                case "vhdx-create":
                {
                    if (File.Exists(image)) return "FAIL " + action + " exit=-1 - the image already exists";
                    if (!int.TryParse(Get(kv, "size", ""), System.Globalization.NumberStyles.None,
                                      System.Globalization.CultureInfo.InvariantCulture, out int mb) || mb < 16 || mb > 4 * 1024 * 1024)
                        return "FAIL " + action + " exit=-1 - size is not a number of MB between 16 and 4194304";
                    string label = Get(kv, "label", "VHDX");
                    if (!SafeLabel(label)) return "FAIL " + action + " exit=-1 - label must be 1-32 letters, digits, - or _";
                    Directory.CreateDirectory(Path.GetDirectoryName(image));
                    script.Append("create vdisk file=\"").Append(image).Append("\" maximum=").Append(mb).Append(" type=expandable\r\n");
                    script.Append("select vdisk file=\"").Append(image).Append("\"\r\n");
                    script.Append("attach vdisk\r\n");
                    script.Append("convert mbr\r\n");
                    script.Append("create partition primary\r\n");
                    script.Append("format fs=ntfs quick label=\"").Append(label).Append("\"\r\n");
                    script.Append("detach vdisk\r\n");
                    break;
                }
                case "vhdx-child":
                {
                    string parent = Get(kv, "parent", "");
                    if (!SafeVhdx(parent) || !File.Exists(parent)) return "FAIL " + action + " exit=-1 - parent is not an existing .vhdx";
                    if (File.Exists(image)) return "FAIL " + action + " exit=-1 - the image already exists";
                    Directory.CreateDirectory(Path.GetDirectoryName(image));
                    script.Append("create vdisk file=\"").Append(image).Append("\" parent=\"").Append(parent).Append("\"\r\n");
                    break;
                }
                case "vhdx-attach":
                {
                    if (!File.Exists(image)) return "FAIL " + action + " exit=-1 - there is no such image";
                    string drive = Get(kv, "drive", "").TrimEnd(':').ToUpperInvariant();
                    if (drive.Length != 1 || drive[0] < 'D' || drive[0] > 'Z') return "FAIL " + action + " exit=-1 - drive must be one letter D-Z";
                    bool readOnly = Get(kv, "readonly", "") == "1";
                    script.Append("select vdisk file=\"").Append(image).Append("\"\r\n");
                    script.Append("attach vdisk").Append(readOnly ? " readonly" : "").Append("\r\n");
                    script.Append("select partition 1\r\n");
                    script.Append("remove all noerr\r\n");        // whatever letter automount gave it
                    script.Append("assign letter=").Append(drive).Append("\r\n");
                    break;
                }
                case "vhdx-detach":
                {
                    if (!File.Exists(image)) return "FAIL " + action + " exit=-1 - there is no such image";
                    script.Append("select vdisk file=\"").Append(image).Append("\"\r\n");
                    script.Append("detach vdisk\r\n");
                    break;
                }
                default:
                    return "FAIL " + action + " exit=-1 - no such action";
            }

            var scriptPath = Path.Combine(dir, "vhdx-" + Guid.NewGuid().ToString("N") + ".diskpart");
            try
            {
                File.WriteAllText(scriptPath, script.ToString());
                var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory ?? @"C:\Windows\System32", "diskpart.exe"))
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                };
                psi.ArgumentList.Add("/s");
                psi.ArgumentList.Add(scriptPath);
                using var p = Process.Start(psi);
                string said = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode == 0) return "OK " + action + " exit=0";
                return "FAIL " + action + " exit=" + p.ExitCode + " - " + LastWords(said);
            }
            finally { try { File.Delete(scriptPath); } catch { } }
        }

        /// <summary>An absolute path to a .vhdx, and nothing a diskpart script could read as more.</summary>
        private static bool SafeVhdx(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 400) return false;
            if (!path.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (var c in path) if (c < ' ' || c == '"') return false;
            try { return Path.IsPathFullyQualified(path) && Path.GetFullPath(path) == path; } catch { return false; }
        }

        private static bool SafeLabel(string label)
        {
            if (label.Length < 1 || label.Length > 32) return false;
            foreach (var c in label) if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_')) return false;
            return true;
        }

        /// <summary>Diskpart's last non-empty line, cut to fit a result line.</summary>
        private static string LastWords(string output)
        {
            var lines = (output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var last = lines.Length > 0 ? lines[lines.Length - 1].Trim() : "no output";
            return last.Length > 200 ? last.Substring(0, 200) : last;
        }

        // ── action = clean ───────────────────────────────────────────────────

        private const long TrimAbove = 100L * 1024 * 1024;

        private static string Clean()
        {
            // SeDebug to open other users' processes, SeProfileSingleProcess for the memory lists.
            Enable("SeDebugPrivilege");
            Enable("SeProfileSingleProcessPrivilege");

            int trimmed = 0;
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (process.Id == Environment.ProcessId || process.WorkingSet64 <= TrimAbove) continue;
                        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SET_QUOTA, false, process.Id);
                        if (h == IntPtr.Zero) continue;
                        try { if (K32EmptyWorkingSet(h)) trimmed++; }
                        finally { CloseHandle(h); }
                    }
                    catch { }   // gone, or protected: the others still count
                }
            }

            int command = MemoryFlushModifiedList;
            int status = NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));
            return "OK clean trimmed=" + trimmed + " flushed=" + (status == 0 ? "1" : "0 status=0x" + status.ToString("X8"));
        }

        private static void Enable(string privilege)
        {
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token)) return;
            try
            {
                if (!LookupPrivilegeValue(null, privilege, out var luid)) return;
                var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
                AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally { CloseHandle(token); }
        }

        private const int SystemMemoryListInformation = 80;
        private const int MemoryFlushModifiedList = 3;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, PROCESS_SET_QUOTA = 0x0100;
        private const uint TOKEN_ADJUST_PRIVILEGES = 0x20, TOKEN_QUERY = 0x8, SE_PRIVILEGE_ENABLED = 0x2;

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID { public uint Low; public int High; }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID Luid; public uint Attributes; }

        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(int infoClass, ref int info, int length);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool K32EmptyWorkingSet(IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LookupPrivilegeValue(string system, string name, out LUID luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES state,
                                                         uint bufferLength, IntPtr previous, IntPtr returnLength);
    }
}
