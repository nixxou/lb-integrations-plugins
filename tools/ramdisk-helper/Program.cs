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
//   action = dismount              (1.4) unmount drive=<letter> CLEANLY, without imdisk.exe - below
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
//   -- added in 1.5, optional --------------------------------------------------------------------
//   removable = 1                  mount with removable media ("-o rem") - below
//
// removable = 1 (1.5): A DRIVE NO INDEXER HOLDS ON TO. Measured 30/09: Everything indexes every FIXED
// NTFS volume the moment it appears and keeps a handle on it for its change journal; ImDisk is not a
// Plug and Play driver, so nobody can tell Everything the drive is going, the lock is refused, and a
// device removed while that handle is open stays "\Device\ImDisk<n>: the device has been removed" until
// Everything lets go - 64 of them in six days, then NTFS failing to flush them ten times a second.
// Everything leaves removable volumes alone by default, and so does Windows Search. AN OLDER HELPER
// IGNORES THIS KEY and mounts a fixed drive, which is what every helper before it did - no worse, so a
// caller may send it without checking the version.
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
//   OK|FAIL dismount <drive> locked=<0|1> dismounted=<0|1> exit=<n>   a dismount run (1.4)
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
// action = dismount (1.4): AN UNMOUNT THAT LEAVES NOTHING BEHIND. Measured 28/09: a drive removed by
// force while its NTFS volume is still mounted - the unelevated direct removal, or "imdisk -D" when the
// lock is refused - stays listed by the driver as "\Device\ImDisk<n>: the device has been removed"
// until Windows restarts, one more at every session (56 of them in a day). The volume keeps the device
// alive. Dismounted first, then removed, the device is gone for good - measured 29/09, three sessions
// in a row, not one device left. Only an elevated process may open a volume to lock it (a standard
// user is refused, error 5), which is why this is here.
//   - the letter must point at \Device\ImDisk<n>, or nothing is done;
//   - the volume is flushed, then LOCKED if it can be, retried for a second - measured 29/09 on real
//     sessions it never was: the shell or a scanner keeps a handle on a live drive;
//   - then DISMOUNTED, locked or not: without the lock the dismount still takes the file system off
//     and invalidates the handles still open (what kept the lock refused - a shell or a scanner watching
//     the new drive; imdisk -D gets the lock only by broadcasting to every window first). The result
//     says locked= and dismounted= so the caller can tell;
//   - then the device is removed through imdisk.cpl, and the letter dropped quietly. No imdisk.exe, so
//     no broadcast to every window on the machine - which is what made an unmount take 88 s with one
//     hung window somewhere.
// Same caution as clean: an older helper reads "dismount" as a mount; check FileVersion >= 1.4.
//
// -- added in 1.6: ARSENAL IMAGE MOUNTER, FIRST WHEN THERE ---------------------------------------
//   backend  = auto | aim | imdisk  which driver a mount goes through. ABSENT MEANS imdisk - what every
//                                  helper before did, so a caller that never heard of AIM (LiteBox's
//                                  1.0 cfg) gets exactly what it always got. auto = AIM when its driver
//                                  (service phdskmnt) and aim_ll.exe are both there, else ImDisk.
//   compress = 1                   NTFS compression: "/c" added to the format parameters
//   mount    = <folder>            mount in an empty folder (an NTFS mount point) instead of drive=
//   type     = awe, no image       a blank disk in PHYSICAL memory (awealloc), size= MB. AIM always has
//                                  awealloc; ImDisk only when its service is there - else FAIL, never a
//                                  silent vm disk instead
//   action   = image-attach        a disk image attached as a disk: image=<path> [readonly=1]
//                                  [overlay=<diff file> [autodelete=1]] [drive=<L> | mount=<folder>].
//                                  AIM: raw and ISO through aim_ll, VHD/VHDX/AVHDX/VMDK/VDI (differencing
//                                  chains included) and any overlay through aim_cli (DiscUtils). Without
//                                  AIM: Windows' own (diskpart attach vdisk) for VHD/VHDX/ISO, an overlay
//                                  on a VHDX being a differencing child; ImDisk for raw images and ISO.
//   action   = image-detach        detach what image-attach attached: drive= | mount= | image=
// Every result gains " backend=aim|imdisk|windows" before " id=", and image-attach " at=<where>".
// The umount and dismount actions tell the backend from the drive itself. An older helper reads the
// image-* actions as a MOUNT: check FileVersion >= 1.6.
//
// -- added in 1.7 ------------------------------------------------------------------------------
//   view     = xbox                on image-attach: an Xbox disc image (redump ISO, XISO) as a FAT32 disk
//                                  whose files are the image's own sectors, AIM only - see XboxAttach. The
//                                  one time this exe takes arguments: "--xbox-serve <image>", started by
//                                  itself. An older helper ignores the key: check FileVersion >= 1.7.
//
// -- added in 1.10 -----------------------------------------------------------------------------
//   view     = xiso                on image-attach: an Xbox disc image (redump ISO, XISO, CSO, CCI, CHD) as a disk holding one
//                                  exFAT volume holding ONE file, game.iso - the disc's XISO, its game partition read where it
//                                  is (ExfatOneFileView, from the xemu plugin), AIM only. For xemu, which opens a disc image and
//                                  not a folder, and cannot open a raw disk unelevated. Self-started as "--xiso-serve <image>".
//                                  The result gains " file=<the file's path>". A ZArchive: from 1.11, below.
//                                  patch=media: extract-xiso's "media enable" patch served in every .xbe that has its pattern
//                                  (XboxMediaPatch, from the xemu plugin) - the image itself untouched.
//                                  An older helper reads view=xiso as no view and attaches the ISO as a CD: check >= 1.10.
//
// -- added in 1.11 -----------------------------------------------------------------------------
//   view     = xiso                a ZArchive (.zar) too: it holds the game's files, so the XDVDFS volume is BUILT around them
//                                  (ZarXiso, from the xemu plugin), read through the archive, nothing unpacked; patch=media
//                                  as for a disc. A helper 1.10 refuses a ZArchive: check >= 1.11.
//
// AIM, WHAT DIFFERS FROM IMDISK. aim_ll takes imdisk's arguments almost word for word, but its disks
// are real SCSI disks: Windows' mount manager gives a new volume a letter of its own on top of the one
// asked for, which is taken off again (only the mount point asked for is kept), and the disk goes the
// Plug and Play way - Windows announces its departure itself, which nothing here can stop. What dismount
// keeps from 1.4: flushed, locked if it can be, dismounted, and only then removed; no broadcast of ours.
// aim_ll only works elevated: unelevated it answers "Arsenal Image Mounter not installed".
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
using System.Linq;
using System.Runtime.InteropServices;

namespace RamDiskHelper
{
    internal static class Program
    {
        private static int Main(string[] argv)
        {
            // 1.8: ZstdSharp (a .zar's zstd) is inside this exe, not beside it - loaded the first time a ZArchive needs it.
            // 1.9: so are CHDSharp and what it needs (a CHD) - and a library beside the exe, should one ever be.
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
            {
                var name = new System.Reflection.AssemblyName(e.Name).Name + ".dll";
                using (var s = typeof(Program).Assembly.GetManifestResourceStream(name))
                    if (s != null)
                    {
                        using var m = new MemoryStream();
                        s.CopyTo(m);
                        return System.Reflection.Assembly.Load(m.ToArray());
                    }
                var beside = Path.Combine(AppContext.BaseDirectory, name);
                return File.Exists(beside) ? System.Reflection.Assembly.LoadFrom(beside) : null;
            };
            // 1.7: the one command-line use - this exe serving an Xbox disc's view, started by itself (below).
            if (argv.Length == 2 && argv[0] == "--xbox-serve") return XboxServe(argv[1]);
            // 1.10: the same, the disc's XISO as one file on an exFAT volume.
            if ((argv.Length == 2 || argv.Length == 3) && argv[0] == "--xiso-serve") return XisoServe(argv[1], argv.Length == 3 && argv[2] == "media");
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
                if (action.Equals("list", StringComparison.OrdinalIgnoreCase))
                {
                    // 1.6: what the drivers have mounted, for a diagnosis - aim_ll -l, which only answers elevated.
                    File.WriteAllText(resultPath, "OK list exit=0 - aim: " + (Aim.Available ? Flat(Run(Aim.LowLevel, "-l").output) : "not installed")
                                                  + " - imdisk: " + (File.Exists(ImDiskExe) ? Flat(Run(ImDiskExe, "-l").output) : "not installed") + id);
                    return 0;
                }
                if (action.StartsWith("image-", StringComparison.OrdinalIgnoreCase))
                {
                    var said = action.Equals("image-attach", StringComparison.OrdinalIgnoreCase) ? ImageAttach(kv, dir)
                             : action.Equals("image-detach", StringComparison.OrdinalIgnoreCase) ? ImageDetach(kv, dir)
                             : "FAIL " + action + " exit=-1 - no such action";
                    File.WriteAllText(resultPath, said + id);
                    return said.StartsWith("OK", StringComparison.Ordinal) ? 0 : 1;
                }

                // WHERE: a letter (drive=, the 1.0 key) or, 1.6, an empty folder (mount=).
                if (!Point(kv, out string point, out string token, out string pointError))
                {
                    File.WriteAllText(resultPath, "FAIL " + action + " ? exit=-1 - " + pointError + id);
                    return 1;
                }
                if (action.Equals("dismount", StringComparison.OrdinalIgnoreCase))
                {
                    var said = Dismount(point, token);
                    File.WriteAllText(resultPath, said + id);
                    return said.StartsWith("OK", StringComparison.Ordinal) ? 0 : 1;
                }
                string size = Get(kv, "size", "1024");
                bool umount = action.Equals("umount", StringComparison.OrdinalIgnoreCase)
                           || action.Equals("unmount", StringComparison.OrdinalIgnoreCase)
                           || action.Equals("remove", StringComparison.OrdinalIgnoreCase);

                if (umount)
                {
                    // The drive says which driver it is on; nothing in the cfg needs to.
                    bool onAim = !IsImDiskPoint(point) && Aim.IsAimPoint(point);
                    var unpsi = new ProcessStartInfo(onAim ? Aim.LowLevel : ImDiskExe) { UseShellExecute = false, CreateNoWindow = true };
                    unpsi.ArgumentList.Add("-D");
                    unpsi.ArgumentList.Add("-m"); unpsi.ArgumentList.Add(point);
                    using var up = Process.Start(unpsi);
                    up.WaitForExit();
                    if (token == "folder") RemoveFolderMount(point);
                    File.WriteAllText(resultPath, (up.ExitCode == 0 ? "OK" : "FAIL") + " umount " + token + " exit=" + up.ExitCode
                                                  + " backend=" + (onAim ? "aim" : "imdisk") + id);
                    return up.ExitCode;
                }

                string image = Get(kv, "image", "");
                string type = Get(kv, "type", "").ToLowerInvariant();
                bool sparse = Get(kv, "sparse", "") == "1";
                bool removable = Get(kv, "removable", "") == "1";
                bool compress = Get(kv, "compress", "") == "1";
                // FORMAT WHEN THERE IS NOTHING TO PRESERVE, which is not the same question as
                // "was an image named". Measured, by getting it wrong: creating a new file-backed
                // image with the format left empty attaches a disk with no filesystem on it - imdisk
                // exits 0, the device appears, and the drive letter is not a directory. An image
                // that already exists carries its own filesystem and must NOT be formatted, or the
                // base would be wiped by the act of mounting it.
                string format = Get(kv, "format",
                    image.Length == 0 || !File.Exists(image) ? "/fs:ntfs /q /y" : "");
                if (compress && format.Length > 0 && format.IndexOf("/c", StringComparison.OrdinalIgnoreCase) < 0) format += " /c";

                // Only the three imdisk knows about. Anything else is treated as not given rather
                // than handed on, because this runs elevated.
                bool awe = type == "awe";
                if (awe) type = "file";
                if (type != "vm" && type != "file") type = "";

                // 1.6: which driver. Absent = ImDisk, as before AIM existed here.
                string backend = Get(kv, "backend", "imdisk").ToLowerInvariant();
                bool useAim = backend == "aim" || (backend == "auto" && Aim.Available);
                if (backend == "aim" && !Aim.Available)
                {
                    File.WriteAllText(resultPath, "FAIL mount " + token + " exit=-1 backend=aim - Arsenal Image Mounter is not installed" + id);
                    return 1;
                }
                if (!useAim && !File.Exists(ImDiskExe))
                {
                    File.WriteAllText(resultPath, "FAIL mount " + token + " exit=-1 backend=imdisk - ImDisk is not installed" + id);
                    return 1;
                }
                if (awe && !useAim && !AweAllocInstalled())
                {
                    File.WriteAllText(resultPath, "FAIL mount " + token + " exit=-1 backend=imdisk - awe unavailable: the AWEAlloc driver is not installed" + id);
                    return 1;
                }
                if (useAim && type.Length == 0) type = "vm";     // aim_ll wants a type, imdisk defaults one
                // NEVER A LETTER THAT IS ALREADY SOMETHING (1.7): a mount formats what it mounts, and the format is run on
                // the LETTER (imdisk and aim_ll hand "format.com <letter>" the mount point). The callers pick a free one;
                // this makes sure of it here too, so that no mistake upstream can ever point a format at a disk that
                // was there before.
                if (token != "folder" && (DeviceOf(point) != null || Directory.Exists(point + "\\")))
                {
                    File.WriteAllText(resultPath, "FAIL mount " + token + " exit=-1 - " + point + " is already in use: nothing mounted, nothing formatted" + id);
                    return 1;
                }
                if (token == "folder")
                {
                    Directory.CreateDirectory(point);
                    if (Directory.EnumerateFileSystemEntries(point).GetEnumerator().MoveNext())
                    {
                        File.WriteAllText(resultPath, "FAIL mount folder exit=-1 - the folder is not empty" + id);
                        return 1;
                    }
                }

                // COMPRESSION IS AIM ONLY, AND COMES WITH MEMORY ALLOCATED AS IT IS USED. Not used by the plugins any
                // more (Mehdi, 02/10: compression made Vita3K crash - see the sizing note below). A fixed-size
                // disk commits its whole size up front: compressing what is on it would save nothing - the callers
                // size it from the uncompressed content. So compress=1 through AIM is a DYNAMIC disk: the Toolkit's
                // RamDyn.exe, a proxy that allocates memory as blocks are written and gives it back on TRIM, under a
                // compressed NTFS - memory used ~ the compressed size. Virtual memory: RamDyn's AWE needs the "lock
                // pages in memory" right, which administrators do not have by default. Through ImDisk, compress=1
                // is not applied.
                if (compress && !useAim && format.Length > 0) format = format.Replace(" /c", "");
                // dynamic=1: the same dynamic disk without the compression - for telling the two apart.
                bool dynamicOnly = Get(kv, "dynamic", "") == "1";
                if ((compress || dynamicOnly) && useAim && image.Length == 0)
                {
                    var ramDyn = Path.Combine(Aim.Folder ?? "", "RamDyn.exe");
                    if (!File.Exists(ramDyn))
                    {
                        File.WriteAllText(resultPath, "FAIL mount " + token + " exit=-1 backend=aim - RamDyn.exe is not in the AIM Toolkit's folder" + id);
                        return 1;
                    }
                    if (!long.TryParse(size, out long sizeMb) || sizeMb <= 0) sizeMb = 1024;
                    // NO MARGIN IS ADDED FOR COMPRESSION, and a caller using it must know: NTFS compression holds space
                    // by 64 KB unit and reserves the uncompressed size of what is being written - measured 02/10, a
                    // volume sized to its content said "not enough space" half full (1.3 GB in 2.9 GB), and Vita3K died
                    // at its first write. The plugins no longer ask for it (Mehdi, 02/10); the keys stay for later use.
                    // RamDyn.exe MountPoint SizeKB TRIM MemoryType BlockSize FormatParam Label SectorSize Removable
                    var dyn = new ProcessStartInfo(ramDyn) { UseShellExecute = false, CreateNoWindow = true };
                    foreach (var a in new[] { point, (sizeMb * 1024).ToString(), "-1", "0", "20", compress ? "/fs:ntfs /c" : "/fs:ntfs", "RamDisk", "512", removable ? "1" : "0" })
                        dyn.ArgumentList.Add(a);
                    var started = Process.Start(dyn);
                    // It stays, serving the disk: waited for until the volume is there, or it gives up.
                    bool up = false;
                    for (int i = 0; i < 120 && !up; i++)
                    {
                        up = VolumeOf(point.TrimEnd('\\') + "\\") != null && (point.Length > 2 || Directory.Exists(point + "\\"));
                        if (!up && started.HasExited) break;
                        if (!up) System.Threading.Thread.Sleep(250);
                    }
                    if (up) KeepOnly(point);
                    File.WriteAllText(resultPath, (up ? "OK" : "FAIL") + " mount " + token + " exit=" + (up ? 0 : started.HasExited ? started.ExitCode : -2)
                                                  + " backend=aim dynamic=1" + (up ? "" : " - RamDyn did not bring the disk up") + id);
                    return up ? 0 : 1;
                }

                var psi = new ProcessStartInfo(useAim ? Aim.LowLevel : ImDiskExe) { UseShellExecute = false, CreateNoWindow = true };
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
                if (removable) options.Add("rem");
                if (options.Count > 0) { psi.ArgumentList.Add("-o"); psi.ArgumentList.Add(string.Join(",", options)); }

                psi.ArgumentList.Add("-m"); psi.ArgumentList.Add(point);
                if (format.Length > 0) { psi.ArgumentList.Add("-p"); psi.ArgumentList.Add(format); }

                using var p = Process.Start(psi);
                p.WaitForExit();
                // AIM's disk is a real disk: the mount manager may have given its volume a letter of its
                // own besides the one asked for. Only the mount point asked for stays.
                if (p.ExitCode == 0 && useAim) KeepOnly(point);
                File.WriteAllText(resultPath, (p.ExitCode == 0 ? "OK" : "FAIL") + " mount " + token + " exit=" + p.ExitCode
                                              + " backend=" + (useAim ? "aim" : "imdisk") + id);
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

        private static string ImDiskExe => Path.Combine(Environment.SystemDirectory ?? @"C:\Windows\System32", "imdisk.exe");

        /// <summary>Where: mount= (1.6, a folder) when given, else drive= (a letter, R by default as in
        /// 1.0). The point is what goes to -m: "R:" or the folder; the token is what the result line
        /// says: the letter, or "folder".</summary>
        private static bool Point(Dictionary<string, string> kv, out string point, out string token, out string error)
        {
            error = null;
            string folder = Get(kv, "mount", "");
            if (folder.Length > 0)
            {
                point = folder.TrimEnd('\\');
                token = "folder";
                if (!SafeFolder(point)) { error = "mount must be an absolute folder on a local drive, not a drive root, of a plain shape"; return false; }
                return true;
            }
            string drive = Get(kv, "drive", "R").TrimEnd(':').ToUpperInvariant();
            point = drive + ":";
            token = drive;
            if (drive.Length != 1 || drive[0] < 'A' || drive[0] > 'Z') { error = "drive must be one letter"; return false; }
            return true;
        }

        /// <summary>A folder this elevated process may make a mount point of: absolute, local (X:\...),
        /// not a drive root, no quote and no control character.</summary>
        private static bool SafeFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 240 || path.Length < 4) return false;
            if (!(char.IsLetter(path[0]) && path[1] == ':' && path[2] == '\\')) return false;
            foreach (var c in path) if (c < ' ' || c == '"' || c == '*' || c == '?' || c == '<' || c == '>' || c == '|') return false;
            try { return Path.IsPathFullyQualified(path) && Path.GetFullPath(path).TrimEnd('\\') == path; } catch { return false; }
        }

        private static bool AweAllocInstalled()
        {
            try { using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\AWEAlloc"); return k != null; }
            catch { return false; }
        }

        // ── Arsenal Image Mounter ────────────────────────────────────────────

        private static class Aim
        {
            /// <summary>The AIM Toolkit's folder: its uninstall entry names config.exe there; Program Files\AIM
            /// Toolkit otherwise. Null when neither holds aim_ll.exe.</summary>
            public static string Folder
            {
                get
                {
                    var dirs = new List<string>();
                    try
                    {
                        using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AIM-tk");
                        foreach (var name in new[] { "InstallLocation", "DisplayIcon", "UninstallString" })
                        {
                            var v = (k?.GetValue(name) as string ?? "").Trim();
                            if (v.StartsWith("\"")) { int end = v.IndexOf('"', 1); v = end > 0 ? v.Substring(1, end - 1) : v.Trim('"'); }
                            int exe = v.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                            if (exe > 0) v = Path.GetDirectoryName(v.Substring(0, exe + 4));
                            if (v.Length > 0) dirs.Add(v);
                        }
                    }
                    catch { }
                    dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AIM Toolkit"));
                    foreach (var d in dirs) if (File.Exists(Path.Combine(d, "aim_ll.exe"))) return d;
                    return null;
                }
            }

            public static string LowLevel => Path.Combine(Folder ?? "", "aim_ll.exe");
            public static string Cli => Path.Combine(Folder ?? "", "aim_cli.exe");

            public static bool DriverInstalled
            {
                get
                {
                    try { using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\phdskmnt"); return k != null; }
                    catch { return false; }
                }
            }

            public static bool Available => DriverInstalled && Folder != null;
            public static bool CliAvailable => Available && File.Exists(Cli);

            /// <summary>Is this mount point an AIM disk's? Asked of aim_ll itself.</summary>
            public static bool IsAimPoint(string point) => Available && UnitOf(point) != null;

            /// <summary>The six-digit device numbers aim_ll knows about.</summary>
            public static HashSet<string> Units()
            {
                var units = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!Available) return units;
                var (_, output) = Run(LowLevel, "-l");
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(output, @"Device number ([0-9A-Fa-f]{6})"))
                    units.Add(m.Groups[1].Value.ToUpperInvariant());
                return units;
            }

            /// <summary>The six-digit device number behind a mount point. MEASURED 02/10: "aim_ll -l -m W:"
            /// does not answer for a letter, so the whole list is read - one block per device, "Device
            /// number 000000", then its volume ("Contains volume \\?\Volume{...}\") and where it is
            /// mounted ("Mounted at W:\") - and the block that names this point or its volume is the one.</summary>
            public static string UnitOf(string point)
            {
                if (!Available) return null;
                var (_, output) = Run(LowLevel, "-l");
                string at = point.TrimEnd('\\') + "\\";
                string volume = VolumeOf(at);
                foreach (var block in output.Split(new[] { "Device number " }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (block.Length < 6) continue;
                    string unit = block.Substring(0, 6);
                    if (!System.Text.RegularExpressions.Regex.IsMatch(unit, "^[0-9A-Fa-f]{6}$")) continue;
                    if (block.IndexOf("Mounted at " + at, StringComparison.OrdinalIgnoreCase) >= 0
                        || (volume != null && block.IndexOf(volume, StringComparison.OrdinalIgnoreCase) >= 0))
                        return unit.ToUpperInvariant();
                }
                return null;
            }
        }
        /// <summary>Run a tool and read what it says. For tools that exit - never aim_cli --background, whose
        /// child would hold the pipe open.</summary>
        private static (int code, string output) Run(string exe, params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                return (p.ExitCode, output);
            }
            catch (Exception ex) { return (-1, ex.Message); }
        }

        // ── volumes and mount points ─────────────────────────────────────────

        /// <summary>The device a mount point leads to: \Device\ImDisk3, \Device\HarddiskVolume12...</summary>
        private static string DeviceOf(string point)
        {
            var target = new System.Text.StringBuilder(1024);
            if (point.Length == 2 && point[1] == ':')
                return QueryDosDevice(point, target, target.Capacity) == 0 ? null : target.ToString();
            var junction = JunctionDevice(point);
            if (junction != null) return junction;
            var volume = VolumeOf(point);
            if (volume == null) return null;
            // \\?\Volume{guid}\ -> Volume{guid}
            return QueryDosDevice(volume.Substring(4).TrimEnd('\\'), target, target.Capacity) == 0 ? null : target.ToString();
        }

        /// <summary>IMDISK IN A FOLDER IS NOT A VOLUME MOUNT POINT - measured 02/10: ImDisk is not known to the
        /// mount manager, so "imdisk -m C:\x" makes a mount-point reparse point (a junction) to \Device\ImDisk3\.
        /// Read from the reparse data itself - .NET's LinkTarget answers null for it. Its device, or null.</summary>
        private static string JunctionDevice(string folder)
        {
            IntPtr h = CreateFile(folder.TrimEnd('\\'), 0x80 /* FILE_READ_ATTRIBUTES */, 7, IntPtr.Zero, 3,
                                  0x02000000 | 0x00200000 /* BACKUP_SEMANTICS | OPEN_REPARSE_POINT */, IntPtr.Zero);
            if (h == new IntPtr(-1)) return null;
            try
            {
                var buffer = new byte[16384];
                if (!DeviceIoControlBytes(h, 0x000900A8 /* FSCTL_GET_REPARSE_POINT */, null, 0, buffer, (uint)buffer.Length, out _, IntPtr.Zero)) return null;
                if (BitConverter.ToUInt32(buffer, 0) != 0xA0000003) return null;          // IO_REPARSE_TAG_MOUNT_POINT
                int offset = BitConverter.ToUInt16(buffer, 8), length = BitConverter.ToUInt16(buffer, 10);
                var target = System.Text.Encoding.Unicode.GetString(buffer, 16 + offset, length);
                return target.StartsWith(@"\Device\ImDisk", StringComparison.OrdinalIgnoreCase) ? target.TrimEnd('\\') : null;
            }
            catch { return null; }
            finally { CloseHandle(h); }
        }

        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "DeviceIoControl")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControlBytes(IntPtr device, uint code, byte[] inBuf, uint inSize, byte[] outBuf, uint outSize, out uint returned, IntPtr overlapped);

        /// <summary>\\?\Volume{guid}\ of what is mounted at this point, or null.</summary>
        private static string VolumeOf(string point)
        {
            var name = new System.Text.StringBuilder(64);
            return GetVolumeNameForVolumeMountPoint(point.TrimEnd('\\') + "\\", name, (uint)name.Capacity) ? name.ToString() : null;
        }

        private static bool IsImDiskPoint(string point)
        {
            var device = DeviceOf(point);
            return device != null && device.StartsWith(@"\Device\ImDisk", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Every mount point of a volume: "R:\", "C:\x\y\".</summary>
        private static List<string> PathsOf(string volume)
        {
            var list = new List<string>();
            var buffer = new char[4096];
            if (!GetVolumePathNamesForVolumeName(volume, buffer, (uint)buffer.Length, out uint length)) return list;
            int start = 0;
            for (int i = 0; i < length; i++)
            {
                if (buffer[i] != '\0') continue;
                if (i > start) list.Add(new string(buffer, start, i - start));
                start = i + 1;
            }
            return list;
        }

        /// <summary>The mount point asked for is the only one the volume keeps - waiting a little for it,
        /// since the mount manager works after aim_ll has returned.</summary>
        private static void KeepOnly(string point)
        {
            string wanted = point.TrimEnd('\\') + "\\";
            for (int i = 0; i < 20; i++)
            {
                var volume = VolumeOf(wanted);
                if (volume != null)
                {
                    System.Threading.Thread.Sleep(500);     // the automatic letter lands a beat later
                    foreach (var other in PathsOf(volume))
                        if (!string.Equals(other, wanted, StringComparison.OrdinalIgnoreCase)) DeleteVolumeMountPoint(other);
                    return;
                }
                System.Threading.Thread.Sleep(250);
            }
        }

        private static void RemoveFolderMount(string folder)
        {
            try
            {
                if (VolumeOf(folder) != null) DeleteVolumeMountPoint(folder.TrimEnd('\\') + "\\");
                else if (JunctionDevice(folder) != null) Directory.Delete(folder);      // the link, not what it led to
            }
            catch { }
        }

        /// <summary>Every volume on the machine, by name.</summary>
        private static HashSet<string> Volumes()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var name = new System.Text.StringBuilder(64);
            IntPtr find = FindFirstVolume(name, (uint)name.Capacity);
            if (find == InvalidHandle) return set;
            try
            {
                do { set.Add(name.ToString()); name.Clear(); name.EnsureCapacity(64); }
                while (FindNextVolume(find, name, (uint)name.Capacity));
            }
            finally { FindVolumeClose(find); }
            return set;
        }

        /// <summary>Wait for a volume that was not there before.</summary>
        private static string NewVolume(HashSet<string> before, int seconds)
        {
            for (int i = 0; i < seconds * 4; i++)
            {
                foreach (var v in Volumes()) if (!before.Contains(v)) return v;
                System.Threading.Thread.Sleep(250);
            }
            return null;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVolumeNameForVolumeMountPoint(string mountPoint, System.Text.StringBuilder volumeName, uint length);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVolumePathNamesForVolumeName(string volumeName, [Out] char[] paths, uint length, out uint returned);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteVolumeMountPoint(string mountPoint);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetVolumeMountPoint(string mountPoint, string volumeName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstVolume(System.Text.StringBuilder name, uint length);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FindNextVolume(IntPtr find, System.Text.StringBuilder name, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FindVolumeClose(IntPtr find);

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

        // ── action = image-attach / image-detach (1.6) ───────────────────────
        //
        // ONE WAY TO ANY IMAGE, three drivers behind it. Whatever attaches it, the volume that appears is
        // found by comparing the machine's volumes before and after, its automatic mount points are taken
        // off, and the one asked for is given - so a letter or a folder behaves the same on all three.
        // What was attached, how, and what to delete afterwards is written to attached.tsv beside this
        // exe: image-detach needs only the mount point.

        private static readonly string[] RawImages = { ".img", ".ima", ".raw", ".dd", ".bin", ".001" };
        private static readonly string[] VirtualDisks = { ".vhd", ".vhdx", ".avhdx", ".vmdk", ".vdi", ".dmg", ".xva" };

        private static string ImageAttach(Dictionary<string, string> kv, string dir)
        {
            const string head = "image-attach";
            string image = Get(kv, "image", "");
            if (!SafeImage(image) || !File.Exists(image)) return "FAIL " + head + " exit=-1 - image is not an existing file with an absolute path of a plain shape";
            if (Get(kv, "view", "").Equals("xbox", StringComparison.OrdinalIgnoreCase)) return XboxAttach(kv, dir, image, "xbox");
            if (Get(kv, "view", "").Equals("xiso", StringComparison.OrdinalIgnoreCase)) return XboxAttach(kv, dir, image, "xiso");
            string ext = Path.GetExtension(image).ToLowerInvariant();
            bool iso = ext == ".iso";
            bool raw = Array.IndexOf(RawImages, ext) >= 0;
            bool virt = Array.IndexOf(VirtualDisks, ext) >= 0;
            if (!iso && !raw && !virt) return "FAIL " + head + " exit=-1 - not an image this knows: " + ext;
            bool readOnly = Get(kv, "readonly", "") == "1" || iso;
            string overlay = Get(kv, "overlay", "");
            bool autodelete = Get(kv, "autodelete", "") == "1";
            if (overlay.Length > 0 && (!SafeImage(overlay) || iso)) return "FAIL " + head + " exit=-1 - " + (iso ? "an ISO takes no write overlay" : "overlay is not an absolute path of a plain shape");
            string drive = Get(kv, "drive", "");
            if (drive.Length == 0 && Get(kv, "mount", "").Length == 0)
            {
                char free = FreeLetter();
                if (free == '\0') return "FAIL " + head + " exit=-1 - no free drive letter";
                kv["drive"] = free.ToString();
            }
            if (!Point(kv, out string point, out string token, out string pointError)) return "FAIL " + head + " exit=-1 - " + pointError;
            if (token == "folder")
            {
                Directory.CreateDirectory(point);
                if (Directory.EnumerateFileSystemEntries(point).GetEnumerator().MoveNext()) return "FAIL " + head + " exit=-1 - the folder is not empty";
            }
            else if (Directory.Exists(point + "\\")) return "FAIL " + head + " exit=-1 - " + point + " is in use";

            string backend = Get(kv, "backend", "auto").ToLowerInvariant();
            bool aim = backend != "imdisk" && backend != "windows" && Aim.CliAvailable;
            // WRITING INTO THE IMAGE ITSELF through AIM CLI is refused without --ignorerisks - measured 02/10:
            // "a system-wide write-cache deadlock could occur while mounting in write-original mode on
            // Windows 11 prior to 22H2". That is builds 22000 to 22620; anywhere else the flag only answers a
            // warning that does not apply. On those builds a VHD/VHDX goes to Windows' own support instead,
            // and anything else is refused.
            bool writeOriginal = !readOnly && overlay.Length == 0;
            int build = Environment.OSVersion.Version.Build;
            bool riskyBuild = build >= 22000 && build < 22621;
            if (aim && writeOriginal && riskyBuild)
            {
                if (ext == ".vhd" || ext == ".vhdx" || ext == ".avhdx") aim = false;
                else return "FAIL " + head + " exit=-1 backend=aim - writing into the image is unsafe on this Windows 11 build: attach it read-only or with an overlay";
            }
            var before = Volumes();
            var unitsBefore = aim ? Aim.Units() : new HashSet<string>();
            string how, detail;
            int code;
            if (aim)
            {
                how = "aim";
                // AIM CLI serves the image through the DevIO driver, which the Toolkit installs as a MANUAL
                // service - measured 02/10: "Cannot open \\?\DevIoDrv\..., the system cannot find the path".
                // Started here; already running is not a failure.
                var (sc, scSaid) = Run(Path.Combine(Environment.SystemDirectory ?? @"C:\Windows\System32", "sc.exe"), "start", "deviodrv");
                // removable=1 (1.6, AIM only): the image as REMOVABLE media, like the RAM disks - indexers leave it
                // alone. Windows' own attach has no such choice; an ISO is a CD-ROM either way.
                bool asRemovable = Get(kv, "removable", "") == "1";
                var args = new List<string> { iso ? "--mount=cdrom" : asRemovable ? "--mount=removable" : "--mount", readOnly ? "--readonly" : "--writable", "--online",
                                              "--filename=" + image, "--provider=" + (virt ? "DiscUtils" : "None") };
                if (overlay.Length > 0) { args.Add("--writeoverlay=" + overlay); if (autodelete) args.Add("--autodelete"); }
                if (writeOriginal) args.Add("--ignorerisks");
                args.Add("--background");
                (code, detail) = RunDetached(Aim.Cli, args, 120);
            }
            else if (raw)
            {
                if (!File.Exists(ImDiskExe)) return "FAIL " + head + " exit=-1 - a raw image needs AIM or ImDisk";
                if (overlay.Length > 0) return "FAIL " + head + " exit=-1 - a write overlay on a raw image needs AIM";
                how = "imdisk";
                var opts = new List<string> { readOnly ? "ro" : "rw" };
                var (c, said) = Run(ImDiskExe, "-a", "-t", "file", "-f", image, "-o", string.Join(",", opts), "-m", point);
                code = c; detail = said;
                if (code == 0)
                {
                    WriteAttached(dir, point, how, image, "");
                    return "OK " + head + " " + token + " exit=0 backend=imdisk at=" + point;
                }
                return "FAIL " + head + " " + token + " exit=" + code + " backend=imdisk - " + LastWords(said);
            }
            else
            {
                // Windows' own virtual disk support: VHD, VHDX (differencing chains included), ISO.
                how = "windows";
                if (ext != ".vhd" && ext != ".vhdx" && ext != ".avhdx" && !iso) return "FAIL " + head + " exit=-1 - " + ext + " needs the AIM Toolkit";
                if (iso)
                {
                    // diskpart cannot select an ISO ("There is no virtual disk selected", measured 02/10):
                    // the virtual disk API itself, which mounts it as a CD-ROM, read-only, for good -
                    // PERMANENT_LIFETIME, or it would go with this process.
                    int isoError = VirtualDisk.AttachIso(image);
                    if (isoError != 0) return "FAIL " + head + " " + token + " exit=" + isoError + " backend=windows - the ISO could not be attached";
                    code = 0; detail = "";
                    WriteAttached(dir, point, "windows-iso", image, "");
                    goto attached;
                }
                string target = image;
                if (overlay.Length > 0)
                {
                    if (!overlay.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase) && !overlay.EndsWith(".avhdx", StringComparison.OrdinalIgnoreCase))
                        return "FAIL " + head + " exit=-1 - without AIM the overlay is a differencing VHDX: name it .vhdx";
                    if (!File.Exists(overlay))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(overlay));
                        var (cc, cs) = DiskPart(dir, "create vdisk file=\"" + overlay + "\" parent=\"" + image + "\"\r\n");
                        if (cc != 0) return "FAIL " + head + " exit=" + cc + " backend=windows - the overlay was not created: " + LastWords(cs);
                    }
                    target = overlay;
                    readOnly = false;
                }
                (code, detail) = DiskPart(dir, "select vdisk file=\"" + target + "\"\r\nattach vdisk" + (readOnly ? " readonly" : "") + "\r\n");
                if (code != 0) return "FAIL " + head + " exit=" + code + " backend=windows - " + LastWords(detail);
                WriteAttached(dir, point, how, target, overlay.Length > 0 && autodelete ? overlay : "");
            }
            // AIM CLI's exit code is not a verdict - measured: the PID of the process it leaves serving the
            // image (58604 for a mount that worked). The volume that comes is.
            if (code != 0 && how != "aim") return "FAIL " + head + " " + token + " exit=" + code + " backend=" + how + " - " + LastWords(detail);
            attached:

            // The volume that came, and only the mount point asked for.
            var volume = NewVolume(before, 30);
            if (volume == null)
            {
                if (how == "aim") DropNewAimDevices(unitsBefore);
                return "FAIL " + head + " " + token + " exit=" + code + " backend=" + how + " - no volume appeared (no file system Windows reads?) - " + Flat(detail);
            }
            System.Threading.Thread.Sleep(500);
            foreach (var other in PathsOf(volume)) DeleteVolumeMountPoint(other);
            string wanted = point.TrimEnd('\\') + "\\";
            if (!SetVolumeMountPoint(wanted, volume))
            {
                int error = Marshal.GetLastWin32Error();
                if (how == "aim") DropNewAimDevices(unitsBefore);
                return "FAIL " + head + " " + token + " exit=" + error + " backend=" + how + " - attached, but " + point + " could not be given to it";
            }
            if (how == "aim") WriteAttached(dir, point, how, image, "");
            return "OK " + head + " " + token + " exit=0 backend=" + how + " at=" + point;
        }

        // ── an Xbox disc, read where it is (1.7) ─────────────────────────────
        //
        // view=xbox on image-attach: an Xbox disc image (redump ISO or XISO) attached as a disk holding one FAT32
        // volume whose files ARE the image's sectors (XisoFatView, from the Cxbx plugin) - nothing copied, nothing
        // in memory but the directories. Windows has no XDVDFS driver; it reads FAT32. Through AIM only, measured
        // 03/10: Cxbx-Reloaded resolves its XBE with GetFinalPathNameByHandleW, which fails on every ImDisk volume
        // (not the mount manager's), and then runs the game from an empty path.
        //
        // HOW: this exe again, "--xbox-serve <image>", started from here and so elevated too, lists the disc,
        // listens on a loopback port it picks and says "PORT <n>" (or "ERROR <why>") on its output, then serves
        // AIM's proxy protocol (the ImDisk one: INFO, READ, CLOSE) to the one client it takes - AIM's service,
        // connecting when "aim_ll -a -t proxy -o ip,ro" asks it to. image-detach removes the disk the usual way:
        // the connection closes and the server exits with it. A server nobody connects to within a minute exits.
        //   OK image-attach <L> exit=0 backend=aim view=xbox at=<L:>
        // An older helper ignores view= and attaches the ISO as a CD, which shows the video partition, not the
        // game: check FileVersion >= 1.7.

        private static string XboxAttach(Dictionary<string, string> kv, string dir, string image, string viewName)
        {
            const string head = "image-attach";
            if (!Aim.Available) return "FAIL " + head + " exit=-1 backend=aim view=" + viewName + " - an Xbox disc view needs the Arsenal Image Mounter";
            if (Get(kv, "drive", "").Length == 0 && Get(kv, "mount", "").Length == 0)
            {
                char free = FreeLetter();
                if (free == '\0') return "FAIL " + head + " exit=-1 - no free drive letter";
                kv["drive"] = free.ToString();
            }
            if (!Point(kv, out string point, out string token, out string pointError)) return "FAIL " + head + " exit=-1 - " + pointError;
            if (token == "folder")
            {
                Directory.CreateDirectory(point);
                if (Directory.EnumerateFileSystemEntries(point).GetEnumerator().MoveNext()) return "FAIL " + head + " exit=-1 - the folder is not empty";
            }
            else if (Directory.Exists(point + "\\")) return "FAIL " + head + " exit=-1 - " + point + " is in use";

            var unitsBefore = Aim.Units();
            // 1.12: HOW AIM REACHES THE SERVER - proxy=shm, shared memory (named section and events, no network at all), proxy=tcp,
            // a loopback port (up to 1.11), proxy=auto (the default) shared memory first, then the port. Measured 05/10: after a
            // reboot every loopback connection of AIM's driver was refused (STATUS_CONNECTION_REFUSED) while the server listened.
            var proxy = Get(kv, "proxy", "auto").ToLowerInvariant();
            Process server = null;
            string Fail(string why)
            {
                try { if (server != null && !server.HasExited) server.Kill(); } catch { }
                DropNewAimDevices(unitsBefore);
                return "FAIL " + head + " " + token + " exit=-1 backend=aim view=" + viewName + " - " + why;
            }
            string StartServer(string shmName)
            {
                var psi = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                psi.ArgumentList.Add("--" + viewName + "-serve");
                psi.ArgumentList.Add(image);
                if (viewName == "xiso" && Get(kv, "patch", "").Equals("media", StringComparison.OrdinalIgnoreCase)) psi.ArgumentList.Add("media");
                if (shmName != null) psi.Environment["LBIP_PROXY_SHM"] = shmName;
                // 1.13: a CHD decoded on that many cores while it is read in sequence (Shared.Disc\ChdParallel).
                if (int.TryParse(Get(kv, "chd_threads", ""), out int chdThreads) && chdThreads >= 0 && chdThreads <= 16) psi.Environment["LBIP_CHD_THREADS"] = chdThreads.ToString();
                server = Process.Start(psi);
                // Listing a redump reads its tables across the image: seconds, a minute on a slow disk.
                var first = server.StandardOutput.ReadLineAsync();
                if (!first.Wait(120000)) return null;
                return first.Result ?? "";
            }
            string key = null, said = "", why1 = null;
            int code = -1;
            if (proxy != "tcp")
            {
                var name = "lbip-devio-" + Guid.NewGuid().ToString("N");
                var line = StartServer(name);
                if (line == "SHM " + name)
                {
                    (code, said) = Run(Aim.LowLevel, "-a", "-t", "proxy", "-o", "shm,ro", "-f", name);
                    if (code == 0) { key = name; proxy = "shm"; }
                    else why1 = "shared memory: aim_ll exit=" + code + " - " + LastWords(said);
                }
                else why1 = "shared memory: " + (line == null ? "the disc was not listed within two minutes" : line.Length > 0 ? line : "the server stopped without a word");
                if (key == null)
                {
                    try { if (!server.HasExited) server.Kill(); } catch { }
                    DropNewAimDevices(unitsBefore);
                    if (proxy == "shm") return Fail(why1);
                }
            }
            if (key == null)
            {
                var line = StartServer(null);
                if (line == null) return Fail("the disc was not listed within two minutes");
                if (!line.StartsWith("PORT ", StringComparison.Ordinal) || !int.TryParse(line.Substring(5), out int port))
                    return Fail((line.Length > 0 ? line : "the server stopped without a word") + (why1 != null ? "; " + why1 : ""));
                (code, said) = Run(Aim.LowLevel, "-a", "-t", "proxy", "-o", "ip,ro", "-f", "127.0.0.1:" + port);
                if (code != 0) return Fail("aim_ll exit=" + code + " - " + LastWords(said) + (why1 != null ? "; " + why1 : ""));
                key = "127.0.0.1:" + port; proxy = "tcp";
            }
            // THE VOLUME OF THE DISK JUST MADE, by AIM's own word ("Contains volume ...") - never "the first volume that
            // appeared": a USB stick plugged in meanwhile would lose its letters to us.
            string unit = null, volume = null;
            for (int i = 0; i < 120 && volume == null; i++)
            {
                var (_, list) = Run(Aim.LowLevel, "-l");
                foreach (var block in list.Split(new[] { "Device number " }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (block.Length < 6 || unitsBefore.Contains(block.Substring(0, 6).ToUpperInvariant())) continue;
                    if (block.IndexOf(key, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    unit = block.Substring(0, 6);
                    var m = System.Text.RegularExpressions.Regex.Match(block, @"Contains volume (\\\\\?\\Volume\{[0-9a-fA-F-]+\}\\)");
                    if (m.Success) volume = m.Groups[1].Value;
                }
                if (volume == null) System.Threading.Thread.Sleep(250);
            }
            if (volume == null) return Fail((unit == null ? "AIM shows no disk for the server" : "the disk " + unit + " shows no volume") + " - " + Flat(said));
            System.Threading.Thread.Sleep(500);
            foreach (var other in PathsOf(volume)) DeleteVolumeMountPoint(other);
            if (!SetVolumeMountPoint(point.TrimEnd('\\') + "\\", volume)) return Fail("attached, but " + point + " could not be given to it (error " + Marshal.GetLastWin32Error() + ")");
            WriteAttached(dir, point, "aim", image, "");
            return "OK " + head + " " + token + " exit=0 backend=aim view=" + viewName + " proxy=" + proxy + " at=" + point
                   + (viewName == "xiso" ? " file=" + point.TrimEnd('\\') + "\\" + XisoFileName : "");
        }

        /// <summary>--xbox-serve &lt;image&gt;: the disc's FAT32 view served to the one proxy client that connects.</summary>
        private static int XboxServe(string image)
        {
            ChdThreadsFromEnvironment();
            try
            {
                // 1.8: a ZArchive (.zar) is the disc it holds - its files laid out as an image (ZArchiveImage), read through
                // the archive, nothing unpacked. Else a disc image, listed.
                LbIntegrations.Cxbx.XdvdfsResult listing;
                Stream source;
                long sourceLength;
                if (LbIntegrations.Zar.ZArchive.IsZar(image))
                {
                    var zar = LbIntegrations.Zar.ZArchive.Open(image);
                    if (zar == null) { Console.WriteLine("ERROR the ZArchive does not read"); return 1; }
                    var flat = new LbIntegrations.Zar.ZArchiveImage(zar);
                    listing = new LbIntegrations.Cxbx.XdvdfsResult { PartitionBase = 0 };
                    listing.Dirs.AddRange(flat.Dirs);
                    listing.Files.AddRange(flat.Files.Select(f => new LbIntegrations.Cxbx.XdvdfsFile { Path = f.Entry.Path, Offset = f.Offset, Length = f.Entry.Length }));
                    source = flat;
                    sourceLength = flat.Length;
                }
                else
                {
                    // 1.9: plain, CSO, CCI or CHD - the disc's bytes through its container (src\Shared.Disc), opened once.
                    source = LbIntegrations.Disc.DiscImages.Open(image);
                    var disc = source;
                    listing = LbIntegrations.Cxbx.Xdvdfs.List(() => LbIntegrations.Disc.DiscImages.Shared(disc), true, disc.Length, null);
                    if (listing.Error != null || !listing.Found) { Console.WriteLine("ERROR the disc could not be listed: " + (listing.Error ?? "no Xbox volume")); return 1; }
                    sourceLength = source.Length;
                }
                var view = new LbIntegrations.Cxbx.XisoFatView(listing, sourceLength, "XBOXGAME");
                return Serve(source, view.Length, view.Read);
            }
            catch (Exception ex)
            {
                try { Console.WriteLine("ERROR " + ex.GetType().Name + ": " + ex.Message); } catch { }
                return 1;
            }
        }

        /// <summary>The name of the one file on view=xiso's volume.</summary>
        private const string XisoFileName = "game.iso";

        /// <summary>--xiso-serve &lt;image&gt; (1.10): the disc's XISO - from its game partition on, through its container - as the
        /// one file of an exFAT volume, served to the one proxy client that connects.</summary>
        private static int XisoServe(string image, bool mediaPatch)
        {
            ChdThreadsFromEnvironment();
            try
            {
                Stream source;
                LbIntegrations.Cxbx.XdvdfsResult listing;
                if (LbIntegrations.Zar.ZArchive.IsZar(image))
                {
                    // 1.11: a ZArchive holds the game's files - the XDVDFS volume built around them (ZarXiso), read through the archive.
                    var built = LbIntegrations.Xemu.ZarXiso.Open(image);
                    source = built;
                    listing = built.Listing;
                }
                else
                {
                    source = LbIntegrations.Disc.DiscImages.Open(image);
                    var disc = source;
                    listing = LbIntegrations.Cxbx.Xdvdfs.List(() => LbIntegrations.Disc.DiscImages.Shared(disc), true, disc.Length, null);
                    if (listing.Error != null || !listing.Found) { source.Dispose(); Console.WriteLine("ERROR the disc could not be listed: " + (listing.Error ?? "no Xbox volume")); return 1; }
                }
                // patch=media: extract-xiso's media enable patch, served - one byte per .xbe that has its pattern (XboxMediaPatch).
                var patches = mediaPatch ? LbIntegrations.Xemu.XboxMediaPatch.Find(source, listing) : null;
                var view = new LbIntegrations.Xemu.ExfatOneFileView(listing.PartitionBase, source.Length - listing.PartitionBase, XisoFileName, "XBOXDISC", patches);
                return Serve(source, view.Length, view.Read);
            }
            catch (Exception ex)
            {
                try { Console.WriteLine("ERROR " + ex.GetType().Name + ": " + ex.Message); } catch { }
                return 1;
            }
        }

        /// <summary>1.12: the same disk through SHARED MEMORY - the ImDisk proxy protocol AIM speaks over a named section (no network):
        /// "Global\&lt;name&gt;" of 8 MB + 4 KB, its requests and answers at its start, the data 4 KB in; "&lt;name&gt;_Request" set by
        /// the driver, "&lt;name&gt;_Response" by us, "&lt;name&gt;_Server" held while we serve. "SHM &lt;name&gt;" said once all exist.
        /// Requests as on the socket: INFO (1), READ (2), CLOSE (5) - nothing else on a read-only disk.</summary>
        private static int ServeShared(Stream source, long length, Func<Stream, long, byte[], int, int> read, string name)
        {
            const int Header = 4096;
            const long Size = (8L << 20) + Header;
            try
            {
                using var request = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, @"Global\" + name + "_Request");
                using var response = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, @"Global\" + name + "_Response");
                using var held = new System.Threading.Mutex(false, @"Global\" + name + "_Server");
                if (!held.WaitOne(0)) { Console.WriteLine("ERROR the shared memory name is in use"); return 1; }
                using var map = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateNew(@"Global\" + name, Size, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.ReadWrite);
                using var view = map.CreateViewAccessor();
                Console.WriteLine("SHM " + name);
                Console.Out.Flush();
                Console.SetOut(TextWriter.Null);
                if (!request.WaitOne(60000)) return 2;     // nobody came within a minute
                using var img = source;
                var buffer = new byte[Size - Header];
                unsafe
                {
                    byte* at = null;
                    view.SafeMemoryMappedViewHandle.AcquirePointer(ref at);
                    try
                    {
                        while (true)
                        {
                            ulong code = *(ulong*)at;
                            if (code == 1)                  // INFO: size, alignment, flags (read-only)
                            {
                                *(ulong*)at = (ulong)length;
                                *(ulong*)(at + 8) = 1;
                                *(ulong*)(at + 16) = 1;
                            }
                            else if (code == 2)             // READ: offset, length -> errno, length, data at 4 KB
                            {
                                long offset = *(long*)(at + 8);
                                int n = (int)Math.Min(*(ulong*)(at + 16), (ulong)buffer.Length);
                                int got = offset < 0 || offset >= length ? 0 : read(img, offset, buffer, (int)Math.Min(n, length - offset));
                                if (got < n) Array.Clear(buffer, got, n - got);
                                Marshal.Copy(buffer, 0, (IntPtr)(at + Header), n);
                                *(ulong*)at = 0;
                                *(ulong*)(at + 8) = (ulong)n;
                            }
                            else break;                     // CLOSE (5), or anything a read-only disk does not do
                            System.Threading.WaitHandle.SignalAndWait(response, request);
                        }
                    }
                    finally { view.SafeMemoryMappedViewHandle.ReleasePointer(); }
                }
                return 0;
            }
            catch (Exception ex)
            {
                try { Console.WriteLine("ERROR " + ex.GetType().Name + ": " + ex.Message); } catch { }
                return 1;
            }
        }

        /// <summary>A read-only disk of <paramref name="length"/> bytes that <paramref name="read"/> gives from
        /// <paramref name="source"/>: "PORT n" said, then AIM's proxy protocol to the one client that connects (a minute at
        /// most for it to come). The source closed at the end.</summary>
        /// <summary>The cores a CHD is decoded on in this server (LBIP_CHD_THREADS, from chd_threads= - 1.13).</summary>
        private static void ChdThreadsFromEnvironment()
        {
            if (int.TryParse(Environment.GetEnvironmentVariable("LBIP_CHD_THREADS"), out int n) && n >= 0 && n <= 16) LbIntegrations.Disc.DiscImages.ChdThreads = n;
        }

        private static int Serve(Stream source, long length, Func<Stream, long, byte[], int, int> read)
        {
            var shm = Environment.GetEnvironmentVariable("LBIP_PROXY_SHM");
            if (!string.IsNullOrEmpty(shm)) return ServeShared(source, length, read, shm);
            try
            {
                var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                listener.Start();
                Console.WriteLine("PORT " + ((System.Net.IPEndPoint)listener.LocalEndpoint).Port);
                Console.Out.Flush();
                Console.SetOut(TextWriter.Null);       // the attaching run is gone soon: nothing more is said
                var accept = listener.AcceptSocketAsync();
                if (!accept.Wait(60000)) return 2;
                using var socket = accept.Result;
                listener.Stop();
                socket.NoDelay = true;
                using var net = new System.Net.Sockets.NetworkStream(socket, true);
                using var reader = new BinaryReader(net);
                using var img = source;
                var buffer = new byte[4 << 20];
                var head = new byte[16];
                while (true)
                {
                    ulong request;
                    try { request = reader.ReadUInt64(); } catch (EndOfStreamException) { break; } catch (IOException) { break; }
                    if (request == 1)                   // INFO: size, alignment, flags (read-only)
                    {
                        var o = new byte[24];
                        BitConverter.GetBytes((ulong)length).CopyTo(o, 0);
                        BitConverter.GetBytes(1UL).CopyTo(o, 8);
                        BitConverter.GetBytes(1UL).CopyTo(o, 16);
                        net.Write(o, 0, o.Length);
                    }
                    else if (request == 2)              // READ: offset, length -> errno, length, data
                    {
                        long offset = reader.ReadInt64();
                        int n = (int)Math.Min(reader.ReadUInt64(), 64UL << 20);
                        if (n > buffer.Length) buffer = new byte[n];
                        // Always the length asked for, zeros past the end: a short answer is a disk error.
                        int got = offset < 0 || offset >= length ? 0 : read(img, offset, buffer, (int)Math.Min(n, length - offset));
                        if (got < n) Array.Clear(buffer, got, n - got);
                        BitConverter.GetBytes(0UL).CopyTo(head, 0);
                        BitConverter.GetBytes((ulong)n).CopyTo(head, 8);
                        net.Write(head, 0, 16);
                        net.Write(buffer, 0, n);
                    }
                    else break;                         // CLOSE (5), or anything a read-only disk does not do
                }
                return 0;
            }
            catch (Exception ex)
            {
                try { Console.WriteLine("ERROR " + ex.GetType().Name + ": " + ex.Message); } catch { }
                return 1;
            }
        }

        /// <summary>ISO through virtdisk.dll - the API Windows' own "Mount" uses. Win32 error codes back.</summary>
        private static class VirtualDisk
        {
            [StructLayout(LayoutKind.Sequential)]
            private struct VIRTUAL_STORAGE_TYPE { public uint DeviceId; public Guid VendorId; }

            [StructLayout(LayoutKind.Sequential)]
            private struct OPEN_VIRTUAL_DISK_PARAMETERS { public uint Version; public uint RWDepth; }

            [StructLayout(LayoutKind.Sequential)]
            private struct ATTACH_VIRTUAL_DISK_PARAMETERS { public uint Version; public uint Reserved; }

            private const uint VIRTUAL_STORAGE_TYPE_DEVICE_ISO = 1;
            private static readonly Guid VendorMicrosoft = new Guid("EC984AEC-A0F9-47e9-901F-71415A66345B");
            private const uint VIRTUAL_DISK_ACCESS_READ = 0x000D0000, VIRTUAL_DISK_ACCESS_DETACH = 0x00040000;
            private const uint ATTACH_READ_ONLY = 0x1, ATTACH_PERMANENT_LIFETIME = 0x4;

            [DllImport("virtdisk.dll", CharSet = CharSet.Unicode)]
            private static extern int OpenVirtualDisk(ref VIRTUAL_STORAGE_TYPE type, string path, uint access, uint flags, ref OPEN_VIRTUAL_DISK_PARAMETERS parameters, out IntPtr handle);

            [DllImport("virtdisk.dll")]
            private static extern int AttachVirtualDisk(IntPtr handle, IntPtr securityDescriptor, uint flags, uint providerFlags, ref ATTACH_VIRTUAL_DISK_PARAMETERS parameters, IntPtr overlapped);

            [DllImport("virtdisk.dll")]
            private static extern int DetachVirtualDisk(IntPtr handle, uint flags, uint providerFlags);

            private static int Open(string path, uint access, out IntPtr handle)
            {
                var type = new VIRTUAL_STORAGE_TYPE { DeviceId = VIRTUAL_STORAGE_TYPE_DEVICE_ISO, VendorId = VendorMicrosoft };
                var p = new OPEN_VIRTUAL_DISK_PARAMETERS { Version = 1, RWDepth = 0 };
                return OpenVirtualDisk(ref type, path, access, 0, ref p, out handle);
            }

            public static int AttachIso(string path)
            {
                int e = Open(path, VIRTUAL_DISK_ACCESS_READ, out var h);
                if (e != 0) return e;
                try
                {
                    var p = new ATTACH_VIRTUAL_DISK_PARAMETERS { Version = 1 };
                    return AttachVirtualDisk(h, IntPtr.Zero, ATTACH_READ_ONLY | ATTACH_PERMANENT_LIFETIME, 0, ref p, IntPtr.Zero);
                }
                finally { CloseHandle(h); }
            }

            public static int DetachIso(string path)
            {
                int e = Open(path, VIRTUAL_DISK_ACCESS_DETACH, out var h);
                if (e != 0) return e;
                try { return DetachVirtualDisk(h, 0, 0); }
                finally { CloseHandle(h); }
            }
        }

        /// <summary>An attach that went wrong leaves nothing behind: the AIM devices that were not there
        /// before are removed.</summary>
        private static void DropNewAimDevices(HashSet<string> before)
        {
            foreach (var unit in Aim.Units())
                if (!before.Contains(unit)) Run(Aim.LowLevel, "-D", "-u", unit);
        }

        private static string ImageDetach(Dictionary<string, string> kv, string dir)
        {
            const string head = "image-detach";
            // By the image alone (no drive=, no mount=): wherever attached.tsv says it was attached - what a caller
            // that knows only the file asks, after a restart of its own.
            string byImage = Get(kv, "image", "");
            if (byImage.Length > 0 && Get(kv, "drive", "").Length == 0 && Get(kv, "mount", "").Length == 0)
            {
                var found = ReadAttachedByFile(dir, byImage);
                if (found == null) return "FAIL " + head + " exit=-1 - " + Path.GetFileName(byImage) + " is not attached by this helper";
                if (found[0].Length == 2) kv["drive"] = found[0].Substring(0, 1); else kv["mount"] = found[0];
            }
            if (!Point(kv, out string point, out string token, out string pointError)) return "FAIL " + head + " exit=-1 - " + pointError;
            var record = ReadAttached(dir, point);
            string how = record?[1] ?? (IsImDiskPoint(point) ? "imdisk" : Aim.Available && Aim.UnitOf(point) != null ? "aim" : "windows");
            string result;
            if (how == "windows-iso")
            {
                FlushAndDismount(point);
                if (point.Length > 2) DeleteVolumeMountPoint(point.TrimEnd('\\') + "\\");
                int error = VirtualDisk.DetachIso(record[2]);
                result = error == 0 ? "OK " + head + " " + token + " exit=0 backend=windows" : "FAIL " + head + " " + token + " exit=" + error + " backend=windows - the ISO could not be detached";
            }
            else if (how == "windows")
            {
                string file = record?[2] ?? Get(kv, "image", "");
                if (!SafeImage(file)) return "FAIL " + head + " " + token + " exit=-1 - which image is attached there is not known";
                FlushAndDismount(point);
                if (point.Length > 2) DeleteVolumeMountPoint(point.TrimEnd('\\') + "\\");
                var (code, said) = DiskPart(dir, "select vdisk file=\"" + file + "\"\r\ndetach vdisk\r\n");
                result = code == 0 ? "OK " + head + " " + token + " exit=0 backend=windows" : "FAIL " + head + " " + token + " exit=" + code + " backend=windows - " + LastWords(said);
                string delete = record?[3] ?? "";
                if (code == 0 && delete.Length > 0) { try { File.Delete(delete); } catch { } }
            }
            else
            {
                // ImDisk and AIM: the clean dismount, which knows both.
                result = Dismount(point, token).Replace("dismount " + token, head + " " + token);
            }
            if (result.StartsWith("OK", StringComparison.Ordinal)) ForgetAttached(dir, point);
            return result;
        }

        private static void FlushAndDismount(string point)
        {
            string volumeName = VolumeOf(point);
            if (volumeName == null) return;
            IntPtr volume = CreateFile(volumeName.TrimEnd('\\'), GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (volume == InvalidHandle) return;
            try
            {
                FlushFileBuffers(volume);
                DeviceIoControl(volume, FSCTL_LOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
                DeviceIoControl(volume, FSCTL_DISMOUNT_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
            }
            finally { CloseHandle(volume); }
        }

        /// <summary>An absolute path to an existing-or-to-be file, nothing a diskpart script or a command line
        /// could read as more.</summary>
        private static bool SafeImage(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 400) return false;
            foreach (var c in path) if (c < ' ' || c == '"') return false;
            try { return Path.IsPathFullyQualified(path) && Path.GetFullPath(path) == path; } catch { return false; }
        }

        private static (int code, string output) DiskPart(string dir, string script)
        {
            var scriptPath = Path.Combine(dir, "image-" + Guid.NewGuid().ToString("N") + ".diskpart");
            try
            {
                File.WriteAllText(scriptPath, script);
                return Run(Path.Combine(Environment.SystemDirectory ?? @"C:\Windows\System32", "diskpart.exe"), "/s", scriptPath);
            }
            finally { try { File.Delete(scriptPath); } catch { } }
        }

        /// <summary>Run a tool that may leave a child behind (aim_cli --background serves the image from a
        /// process of its own): its words are read as they come, never to the end of the pipe - the child
        /// holds it open - and only the parent is waited for, with a ceiling. AIM CLI's exit code is not a
        /// verdict (measured: 31252 for a mount that worked), so the caller judges by the volume that comes.</summary>
        private static (int code, string output) RunDetached(string exe, List<string> args, int seconds)
        {
            try
            {
                var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var a in args) psi.ArgumentList.Add(a);
                var said = new System.Text.StringBuilder();
                using var p = new Process { StartInfo = psi };
                p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (said) said.AppendLine(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (said) said.AppendLine(e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                bool exited = p.WaitForExit(seconds * 1000);
                System.Threading.Thread.Sleep(300);
                lock (said) return (exited ? p.ExitCode : -2, said.ToString() + (exited ? "" : " (still running after " + seconds + " s)"));
            }
            catch (Exception ex) { return (-1, ex.Message); }
        }
        private static char FreeLetter()
        {
            var used = new HashSet<char>();
            foreach (var d in DriveInfo.GetDrives()) used.Add(char.ToUpperInvariant(d.Name[0]));
            for (char c = 'Z'; c >= 'D'; c--) if (!used.Contains(c)) return c;
            return '\0';
        }

        private static string AttachedPath(string dir) => Path.Combine(dir, "attached.tsv");

        private static void WriteAttached(string dir, string point, string how, string file, string deleteAfter)
        {
            try
            {
                ForgetAttached(dir, point);
                File.AppendAllText(AttachedPath(dir), point + "\t" + how + "\t" + file + "\t" + deleteAfter + "\r\n");
            }
            catch { }
        }

        private static string[] ReadAttached(string dir, string point)
        {
            try
            {
                if (!File.Exists(AttachedPath(dir))) return null;
                foreach (var line in File.ReadAllLines(AttachedPath(dir)))
                {
                    var parts = line.Split('\t');
                    if (parts.Length >= 4 && string.Equals(parts[0], point, StringComparison.OrdinalIgnoreCase)) return parts;
                }
            }
            catch { }
            return null;
        }

        private static string[] ReadAttachedByFile(string dir, string file)
        {
            try
            {
                if (!File.Exists(AttachedPath(dir))) return null;
                foreach (var line in File.ReadAllLines(AttachedPath(dir)))
                {
                    var parts = line.Split('\t');
                    if (parts.Length >= 4 && string.Equals(parts[2], file, StringComparison.OrdinalIgnoreCase)) return parts;
                }
            }
            catch { }
            return null;
        }

        private static void ForgetAttached(string dir, string point)
        {
            try
            {
                var path = AttachedPath(dir);
                if (!File.Exists(path)) return;
                var keep = new List<string>();
                foreach (var line in File.ReadAllLines(path))
                    if (!line.StartsWith(point + "\t", StringComparison.OrdinalIgnoreCase)) keep.Add(line);
                File.WriteAllLines(path, keep);
            }
            catch { }
        }

        // ── action = dismount ────────────────────────────────────────────────

        private static string Dismount(string point, string token)
        {
            string head = "dismount " + token;
            string device = DeviceOf(point);
            if (device == null) return "FAIL " + head + " locked=0 dismounted=0 exit=-1 - no such drive";
            const string prefix = @"\Device\ImDisk";
            bool imdisk = device.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            uint number = 0;
            if (imdisk && !uint.TryParse(device.Substring(prefix.Length), out number))
                return "FAIL " + head + " locked=0 dismounted=0 exit=-1 - " + point + " is " + device + ", not an ImDisk drive";
            string unit = null;
            if (!imdisk)
            {
                unit = Aim.Available ? Aim.UnitOf(point) : null;
                if (unit == null) return "FAIL " + head + " locked=0 dismounted=0 exit=-1 - " + point + " is " + device + ", not an ImDisk or AIM drive"
                                         + (Aim.Available ? " [aim_ll -l: " + Flat(Run(Aim.LowLevel, "-l").output) + "]" : " [no AIM]");
            }
            // The volume, opened by its GUID path: works for a letter and for a folder alike.
            string volumeName = VolumeOf(point);
            string open = volumeName != null ? volumeName.TrimEnd('\\') : point.Length == 2 ? @"\\.\" + point : @"\\?\GLOBALROOT" + device;

            bool locked = false, dismounted = false;
            IntPtr volume = CreateFile(open, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                                       IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            int openError = volume == InvalidHandle ? Marshal.GetLastWin32Error() : 0;
            int dismountError = 0;
            try
            {
                if (volume != InvalidHandle)
                {
                    FlushFileBuffers(volume);
                    for (int i = 0; i < 4 && !locked; i++)
                    {
                        locked = DeviceIoControl(volume, FSCTL_LOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
                        if (!locked) System.Threading.Thread.Sleep(250);
                    }
                    // DISMOUNTED LOCKED OR NOT. Without the lock, FSCTL_DISMOUNT_VOLUME still takes the file
                    // system off the volume and invalidates whatever handles are open on it - measured
                    // 29/09: a shell or scanner watching the new drive kept the lock refused for five
                    // seconds, and imdisk -D only gets it by first broadcasting to every window, which
                    // is the slowness this action exists to avoid. The session is saved before anything
                    // asks for this; nothing on the drive is still wanted.
                    dismounted = DeviceIoControl(volume, FSCTL_DISMOUNT_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
                    if (!dismounted) dismountError = Marshal.GetLastWin32Error();
                }

                if (!imdisk)
                {
                    // AIM: the mount point goes first, quietly, then the device by its number. aim_ll locks
                    // the volume itself, so ours is let go first - dismounted already, nothing on it.
                    // -D only when -d is refused.
                    if (point.Length > 2) DeleteVolumeMountPoint(point.TrimEnd('\\') + "\\");
                    if (volume != InvalidHandle) { CloseHandle(volume); volume = InvalidHandle; }
                    var (code, said) = Run(Aim.LowLevel, "-d", "-u", unit);
                    if (code != 0) (code, said) = Run(Aim.LowLevel, "-D", "-u", unit);
                    if (code != 0)
                        return "FAIL " + head + " locked=" + B(locked) + " dismounted=" + B(dismounted) + " exit=" + code + " backend=aim - " + LastWords(said);
                    if (point.Length == 2) SHChangeNotify(SHCNE_DRIVEREMOVED, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, point + "\\", IntPtr.Zero);
                    return "OK " + head + " locked=" + B(locked) + " dismounted=" + B(dismounted) + " exit=0 backend=aim"
                           + (openError != 0 ? " - the volume could not be opened (error " + openError + ")" : "");
                }

                // The removal, with the volume still held locked: nothing can remount it in between.
                IntPtr handle = ImDiskOpenDeviceByNumber(number, 0);
                if (handle == IntPtr.Zero || handle == InvalidHandle)
                    return "FAIL " + head + " locked=" + B(locked) + " dismounted=" + B(dismounted) + " exit=" + Marshal.GetLastWin32Error() + " - could not open the device";
                bool removed;
                try { removed = ImDiskForceRemoveDevice(handle, 0); }
                finally { CloseHandle(handle); }
                if (!removed)
                    return "FAIL " + head + " locked=" + B(locked) + " dismounted=" + B(dismounted) + " exit=" + Marshal.GetLastWin32Error() + " - the device was not removed";
            }
            finally { if (volume != InvalidHandle) CloseHandle(volume); }

            // The driver takes the letter with the device; when it is still there and still this device,
            // it goes quietly - no broadcast.
            if (point.Length == 2)
            {
                var now = new System.Text.StringBuilder(1024);
                if (QueryDosDevice(point, now, now.Capacity) != 0 && string.Equals(now.ToString(), device, StringComparison.OrdinalIgnoreCase))
                    DefineDosDevice(DDD_REMOVE_DEFINITION | DDD_NO_BROADCAST_SYSTEM, point, null);
                SHChangeNotify(SHCNE_DRIVEREMOVED, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, point + "\\", IntPtr.Zero);
            }
            else RemoveFolderMount(point);
            return "OK " + head + " locked=" + B(locked) + " dismounted=" + B(dismounted) + " exit=0 backend=imdisk"
                   + (openError != 0 ? " - the volume could not be opened (error " + openError + ")" : "")
                   + (dismountError != 0 ? " - the dismount was refused (error " + dismountError + ")" : "");
        }

        private static string B(bool value) => value ? "1" : "0";

        private static string Flat(string s) { s = (s ?? "").Replace("\r", " ").Replace("\n", " | "); return s.Length > 600 ? s.Substring(0, 600) : s; }

        private static readonly IntPtr InvalidHandle = new IntPtr(-1);
        private const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
        private const uint FSCTL_LOCK_VOLUME = 0x00090018, FSCTL_DISMOUNT_VOLUME = 0x00090020;
        private const uint DDD_REMOVE_DEFINITION = 0x2, DDD_NO_BROADCAST_SYSTEM = 0x8;
        private const int SHCNE_DRIVEREMOVED = 0x80;
        private const uint SHCNF_PATHW = 0x5, SHCNF_FLUSHNOWAIT = 0x3000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(IntPtr device, uint code, IntPtr inBuf, uint inSize, IntPtr outBuf, uint outSize, out uint returned, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlushFileBuffers(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint QueryDosDevice(string deviceName, System.Text.StringBuilder targetPath, int max);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DefineDosDevice(uint flags, string deviceName, string targetPath);

        [DllImport("imdisk.cpl", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr ImDiskOpenDeviceByNumber(uint deviceNumber, uint accessMode);

        [DllImport("imdisk.cpl", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ImDiskForceRemoveDevice(IntPtr device, uint deviceNumber);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(int eventId, uint flags, string item1, IntPtr item2);

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
