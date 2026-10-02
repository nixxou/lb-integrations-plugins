// How fast each way of putting a disk in memory - or a VHDX on the disk - reads and writes:
//
//   --ramdisk-perf --lb <LaunchBox root> [--vhdx-dir <folder for a test .vhdx>] [--mb 512]
//
// RAM disks: ImDisk (virtual memory), AIM virtual memory, AIM AWE (physical memory), 1 GB each. VHDX: one fresh
// dynamic VHDX attached by Windows (diskpart, vhdmp.sys) and by AIM (aim_cli, DiscUtils), on the same disk.
// Every measure is UNBUFFERED (FILE_FLAG_NO_BUFFERING, sector-aligned buffers): without it the Windows file
// cache answers the reads and every line shows the same number. Sequential: a file of --mb MB in 1 MB blocks.
// Random: 4 KB at random offsets in that file, one at a time (queue depth 1), for 3 seconds.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using LbIntegrations.RamDisk;

namespace LbIntegrations.Probe
{
    internal static class RamDiskPerf
    {
        private const FileOptions NoBuffering = (FileOptions)0x20000000;

        public static bool Run(string lb, string vhdxDir, int mb)
        {
            RamDiskLog.Use(_ => { }, (m, ex) => Console.WriteLine("    [warn] " + m + (ex != null ? " - " + ex.Message : "")));
            RamDiskHost.UseRoot(lb);
            var ini = RamDiskOptions.FilePath;
            string saved = File.Exists(ini) ? File.ReadAllText(ini) : null;
            var rows = new List<string>();
            try
            {
                Console.WriteLine("  " + mb + " MB file, unbuffered; MB/s sequential, IOPS 4K random QD1");
                if (RamDrive.IsImDiskInstalled()) rows.Add(Ram("ImDisk, virtual memory", "imdisk", false, mb));
                if (RamDrive.IsAimInstalled())
                {
                    rows.Add(Ram("AIM, virtual memory", "aim", false, mb));
                    rows.Add(Ram("AIM, AWE (physical)", "aim", true, mb));
                }
                if (!string.IsNullOrEmpty(vhdxDir))
                {
                    rows.Add(Vhdx("VHDX, Windows (vhdmp)", "windows", vhdxDir, mb));
                    if (RamDrive.IsAimInstalled()) { rows.Add(Vhdx("VHDX, AIM (aim_cli)", "aim", vhdxDir, mb, false)); rows.Add(Vhdx("VHDX, AIM, removable", "aim", vhdxDir, mb, true)); }
                }
            }
            finally { try { if (saved != null) File.WriteAllText(ini, saved); else if (File.Exists(ini)) File.Delete(ini); } catch { } }
            Console.WriteLine();
            Console.WriteLine("  " + "".PadRight(26) + "seq write   seq read   rnd write   rnd read");
            foreach (var r in rows) Console.WriteLine("  " + r);
            return true;
        }

        private static string Ram(string name, string backend, bool awe, int mb)
        {
            new RamDiskOptions { Backend = backend, Awe = awe, AutoMemory = false, Removable = true }.Save();
            var root = RamDrive.MountFor("perf", mb + 512);
            if (root == null) return name.PadRight(26) + "did not mount";
            try { return name.PadRight(26) + Measure(root, mb); }
            finally { RamDrive.UnmountFor("perf"); }
        }

        private static string Vhdx(string name, string backend, string dir, int mb, bool removable = false)
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "lbip-perf-" + backend + ".vhdx");
            if (File.Exists(path)) File.Delete(path);
            if (!RamDrive.CreateVhdx(path, mb * 2 + 512, "PERF", out var err)) return name.PadRight(26) + "not created: " + err;
            try
            {
                new RamDiskOptions { Backend = backend == "aim" ? "aim" : "imdisk" }.Save();   // imdisk = no AIM: Windows' own
                var root = RamDrive.AttachImage(path, false, out err);
                if (root == null) return name.PadRight(26) + "not attached: " + err;
                try { return name.PadRight(26) + Measure(root, mb); }
                finally { RamDrive.DetachImage(root, out _); }
            }
            finally { try { File.Delete(path); } catch { } }
        }

        private static string Measure(string root, int mb)
        {
            var file = Path.Combine(root, "perf.bin");
            const int block = 1 << 20;
            var (buffer, handle) = Aligned(block);
            try
            {
                new Random(1).NextBytes(buffer.AsSpan(Offset(buffer, handle), block));
                var span = new ArraySegment<byte>(buffer, Offset(buffer, handle), block);

                var sw = Stopwatch.StartNew();
                using (var h = File.OpenHandle(file, FileMode.Create, FileAccess.ReadWrite, FileShare.None, NoBuffering | FileOptions.WriteThrough, (long)mb * block))
                    for (long i = 0; i < mb; i++) RandomAccess.Write(h, span, i * block);
                double seqW = mb / sw.Elapsed.TotalSeconds;

                sw.Restart();
                using (var h = File.OpenHandle(file, FileMode.Open, FileAccess.Read, FileShare.None, NoBuffering))
                    for (long i = 0; i < mb; i++) RandomAccess.Read(h, span, i * block);
                double seqR = mb / sw.Elapsed.TotalSeconds;

                var small = new ArraySegment<byte>(buffer, Offset(buffer, handle), 4096);
                var rnd = new Random(2);
                long pages = (long)mb * block / 4096;
                double Iops(bool write)
                {
                    using var h = File.OpenHandle(file, FileMode.Open, write ? FileAccess.ReadWrite : FileAccess.Read, FileShare.None, NoBuffering | (write ? FileOptions.WriteThrough : 0));
                    long n = 0;
                    var t = Stopwatch.StartNew();
                    while (t.ElapsedMilliseconds < 3000)
                    {
                        long at = (long)(rnd.NextDouble() * pages) * 4096;
                        if (write) RandomAccess.Write(h, small, at); else RandomAccess.Read(h, small, at);
                        n++;
                    }
                    return n / t.Elapsed.TotalSeconds;
                }
                double rw = Iops(true), rr = Iops(false);
                return (seqW.ToString("0") + " MB/s").PadLeft(10) + (seqR.ToString("0") + " MB/s").PadLeft(11)
                       + rw.ToString("0").PadLeft(12) + rr.ToString("0").PadLeft(11);
            }
            catch (Exception ex) { return "failed: " + ex.Message; }
            finally { handle.Free(); try { File.Delete(file); } catch { } }
        }

        /// <summary>--ramdisk-perf --detail: why one driver is slower - each RAM disk fixed and removable, writes with
        /// and without write-through, random reads and writes one at a time and eight at a time.</summary>
        public static bool Detail(string lb, int mb)
        {
            RamDiskLog.Use(_ => { }, (m, ex) => Console.WriteLine("    [warn] " + m + (ex != null ? " - " + ex.Message : "")));
            RamDiskHost.UseRoot(lb);
            var ini = RamDiskOptions.FilePath;
            string saved = File.Exists(ini) ? File.ReadAllText(ini) : null;
            var rows = new List<string>();
            try
            {
                foreach (var (backend, awe) in new[] { ("imdisk", false), ("aim", false), ("aim", true) })
                    foreach (var removable in new[] { true, false })
                    {
                        if (backend == "imdisk" ? !RamDrive.IsImDiskInstalled() : !RamDrive.IsAimInstalled()) continue;
                        var name = (backend == "aim" ? "AIM" : "ImDisk") + (awe ? " AWE" : " vm") + (removable ? ", removable" : ", fixed");
                        new RamDiskOptions { Backend = backend, Awe = awe, AutoMemory = false, Removable = removable }.Save();
                        var root = RamDrive.MountFor("perf", mb + 512);
                        if (root == null) { rows.Add(name.PadRight(24) + "did not mount"); continue; }
                        try { rows.Add(name.PadRight(24) + MeasureDetail(root, mb)); }
                        finally { RamDrive.UnmountFor("perf"); }
                    }
            }
            finally { try { if (saved != null) File.WriteAllText(ini, saved); else if (File.Exists(ini)) File.Delete(ini); } catch { } }
            Console.WriteLine();
            Console.WriteLine("  " + mb + " MB, unbuffered. MB/s for sequential (1 MB), IOPS for random (4 KB).");
            Console.WriteLine("  " + "".PadRight(24) + " seqW WT  seqW     seqR  rndR q1  rndR q8  rndW q1  rndW q8");
            foreach (var r in rows) Console.WriteLine("  " + r);
            return true;
        }

        private static string MeasureDetail(string root, int mb)
        {
            var file = Path.Combine(root, "perf.bin");
            const int block = 1 << 20;
            var (buffer, handle) = Aligned(block);
            try
            {
                int off = Offset(buffer, handle);
                new Random(1).NextBytes(buffer.AsSpan(off, block));
                var big = new ArraySegment<byte>(buffer, off, block);
                double Seq(bool write, bool through)
                {
                    var sw = Stopwatch.StartNew();
                    using (var h = File.OpenHandle(file, write ? FileMode.Create : FileMode.Open, write ? FileAccess.ReadWrite : FileAccess.Read, FileShare.None,
                                                   NoBuffering | (through ? FileOptions.WriteThrough : 0), write ? (long)mb * block : 0))
                        for (long i = 0; i < mb; i++) { if (write) RandomAccess.Write(h, big, i * block); else RandomAccess.Read(h, big, i * block); }
                    return mb / sw.Elapsed.TotalSeconds;
                }
                double wt = Seq(true, true), w = Seq(true, false), r = Seq(false, false);
                long pages = (long)mb * block / 4096;
                double Rnd(bool write, int threads)
                {
                    using var h = File.OpenHandle(file, FileMode.Open, write ? FileAccess.ReadWrite : FileAccess.Read, FileShare.ReadWrite, NoBuffering);
                    long n = 0;
                    var t = Stopwatch.StartNew();
                    var workers = new System.Threading.Thread[threads];
                    for (int k = 0; k < threads; k++)
                    {
                        int id = k;
                        workers[k] = new System.Threading.Thread(() =>
                        {
                            var (b, g) = Aligned(4096);
                            try
                            {
                                var s = new ArraySegment<byte>(b, Offset(b, g), 4096);
                                var rnd = new Random(10 + id);
                                long mine = 0;
                                while (t.ElapsedMilliseconds < 3000)
                                {
                                    long at = (long)(rnd.NextDouble() * pages) * 4096;
                                    if (write) RandomAccess.Write(h, s, at); else RandomAccess.Read(h, s, at);
                                    mine++;
                                }
                                System.Threading.Interlocked.Add(ref n, mine);
                            }
                            finally { g.Free(); }
                        });
                        workers[k].Start();
                    }
                    foreach (var x in workers) x.Join();
                    return n / t.Elapsed.TotalSeconds;
                }
                return wt.ToString("0").PadLeft(8) + w.ToString("0").PadLeft(6) + r.ToString("0").PadLeft(9)
                       + Rnd(false, 1).ToString("0").PadLeft(9) + Rnd(false, 8).ToString("0").PadLeft(9)
                       + Rnd(true, 1).ToString("0").PadLeft(9) + Rnd(true, 8).ToString("0").PadLeft(9);
            }
            catch (Exception ex) { return "failed: " + ex.Message; }
            finally { handle.Free(); try { File.Delete(file); } catch { } }
        }

        /// <summary>A pinned buffer with a 4 KB-aligned window of <paramref name="size"/> bytes in it -
        /// what FILE_FLAG_NO_BUFFERING asks of the memory it reads into.</summary>
        private static (byte[] buffer, GCHandle handle) Aligned(int size)
        {
            var buffer = new byte[size + 4096];
            return (buffer, GCHandle.Alloc(buffer, GCHandleType.Pinned));
        }

        private static int Offset(byte[] buffer, GCHandle handle)
        {
            long address = handle.AddrOfPinnedObject().ToInt64();
            return (int)((4096 - (address % 4096)) % 4096);
        }
    }
}
