// --mame-convert: MAME sets seen in another layout (src\Shared.Mame), checked by MAME itself.
//
//   Probe --mame-convert --index <listxml or .tsv.gz> --src <folder of sets> --layout nonmerged|split|merged
//         --out <folder> --sets a,b,c [--mame <mame.exe>]
//
// The sets asked and what they need (parents, BIOS, devices) are planned in the target layout; each zip is made as a
// VirtualZip and WRITTEN to <out> (the helper never writes: here it is how MAME gets to read them), then, with --mame,
// "mame -rompath <out> -verifyroms <set>" says whether each set is good. A listxml given is cached beside it as .tsv.gz.
//
//   ... --volume <description> [--lb <LaunchBox root>] [--keep] [--verify a,b,c]   (in place of --out)
//
// The plan saved as a volume description (Shared.Volume) and read back; with --lb, served by the RAM disk helper (view=volume,
// 1.17, through AIM: MOUNTS A DISK) and MAME verifying ON THE VOLUME. --sets * plans the whole romset; --verify then says which
// sets MAME and the CRC check read (all those planned by default).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LbIntegrations.Mame;
using LbIntegrations.Volume;

namespace LbIntegrations.Probe
{
    internal static class MameCheck
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string newFile, string existingFile, IntPtr security);

        private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(i =>
        {
            uint c = (uint)i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            return c;
        }).ToArray();

        private static uint Crc32(Stream s)
        {
            uint crc = 0xFFFFFFFFu;
            var buf = new byte[1 << 16];
            int n;
            while ((n = s.Read(buf, 0, buf.Length)) > 0)
                for (int i = 0; i < n; i++) crc = CrcTable[(crc ^ buf[i]) & 0xFF] ^ (crc >> 8);
            return ~crc;
        }

        public static bool Convert(string indexPath, string src, string layoutName, string outDir, string sets, string mame,
                                   string volume = null, string lb = null, bool keep = false, string verify = null)
        {
            var watch = Stopwatch.StartNew();
            MameIndex index;
            if (indexPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)) index = MameIndex.Load(indexPath);
            else
            {
                index = MameIndex.FromListXml(indexPath);
                var cache = Path.ChangeExtension(indexPath, ".tsv.gz");
                index.Save(cache);
                Console.WriteLine("  index cached: " + cache + " (" + new FileInfo(cache).Length + " bytes)");
            }
            Console.WriteLine("  MAME " + index.Build + ": " + index.Machines.Count + " machines, " + index.Machines.Values.Sum(m => m.Roms.Count) + " ROMs, read in " + watch.ElapsedMilliseconds + " ms");

            var layout = layoutName.ToLowerInvariant() switch
            {
                "nonmerged" or "non-merged" => MameLayout.NonMerged,
                "split" => MameLayout.Split,
                "merged" => MameLayout.Merged,
                _ => throw new ArgumentException("layout: nonmerged, split or merged"),
            };
            var conv = new MameConvert(index, src);
            // --sets *: every machine of MAME (the whole romset); then --verify says which ones MAME checks.
            var asked = sets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var machines = asked.Count == 1 && asked[0] == "*" ? new HashSet<string>(index.Machines.Keys, StringComparer.OrdinalIgnoreCase) : conv.Closure(asked);
            watch.Restart();
            var plans = conv.Plan(layout, machines);
            Console.WriteLine("  " + layout + ": " + plans.Count + " zips for " + machines.Count + " machines, planned in " + watch.ElapsedMilliseconds + " ms");
            foreach (var u in conv.Unreadable.Take(20)) Console.WriteLine("  UNREADABLE " + u);
            var toVerify = verify != null ? conv.Closure(verify.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) : machines;

            if (volume != null) return Volume(index, plans, toVerify, volume, lb, mame, keep);

            Directory.CreateDirectory(outDir);
            bool ok = true;
            using var pool = new SourcePool();
            foreach (var p in plans)
            {
                // Its disks: hard links to the source CHDs (the same bytes, as the volume serves them), copies across volumes.
                foreach (var (name, source) in p.Disks)
                {
                    var dir = Path.Combine(outDir, p.Set);
                    Directory.CreateDirectory(dir);
                    var to = Path.Combine(dir, name);
                    if (File.Exists(to)) File.Delete(to);
                    if (!CreateHardLink(to, source, IntPtr.Zero)) File.Copy(source, to);
                    Console.WriteLine("  " + p.Set + "\\" + name + "  <- " + source);
                }
                if (p.MissingDisks.Count > 0) { Console.WriteLine("  " + p.Set + "  MISSING DISKS: " + string.Join(", ", p.MissingDisks)); ok = false; }
                if (p.Items.Count == 0 && p.Missing.Count == 0) continue;
                var vz = new VirtualZip(p.Items, pool);
                var path = Path.Combine(outDir, p.Set + ".zip");
                vz.WriteTo(path);
                Console.WriteLine("  " + p.Set + ".zip  " + p.Items.Count + " files, " + vz.Length + " bytes" + (p.Missing.Count > 0 ? "  MISSING: " + string.Join(", ", p.Missing) : ""));
                if (p.Missing.Count > 0) ok = false;
                // Read back by .NET: every entry's CRC from its decompressed bytes.
                using var check = System.IO.Compression.ZipFile.OpenRead(path);
                foreach (var e in check.Entries)
                {
                    if (e.FullName.EndsWith("/")) continue;
                    using var s = e.Open();
                    var got = Crc32(s);
                    if (got != e.Crc32) { Console.WriteLine("    CRC MISMATCH " + e.FullName); ok = false; }
                }
            }

            if (mame != null && !VerifyWithMame(mame, outDir, index, toVerify)) ok = false;
            Console.WriteLine(ok ? "ALL OK" : "FAILED");
            return ok;
        }

        /// <summary>"mame -rompath &lt;rompath&gt; -verifyroms &lt;set&gt;" for each set with ROMs: good or best available, else what MAME says.</summary>
        private static bool VerifyWithMame(string mame, string rompath, MameIndex index, IEnumerable<string> sets)
        {
            bool ok = true;
            int checkedSets = 0;
            var watch = Stopwatch.StartNew();
            foreach (var set in sets.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            {
                var m = index.Get(set);
                if (m == null || m.IsDevice || m.Roms.Count == 0) continue;
                var psi = new ProcessStartInfo(mame, "-rompath \"" + rompath.TrimEnd('\\') + "\" -verifyroms " + set)
                {
                    WorkingDirectory = Path.GetDirectoryName(mame), UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                };
                using var proc = Process.Start(psi);
                var outText = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                checkedSets++;
                var verdict = outText.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("romset ", StringComparison.OrdinalIgnoreCase)) ?? outText.Trim();
                bool good = verdict.Contains(" is good") || verdict.Contains(" is best available");
                Console.WriteLine("  MAME: " + verdict);
                if (!good)
                {
                    ok = false;
                    foreach (var l in outText.Split('\n').Where(l => l.Contains("NOT FOUND") || l.Contains("INCORRECT") || l.Contains("WRONG")).Take(8))
                        Console.WriteLine("        " + l.Trim());
                }
            }
            Console.WriteLine("  MAME checked " + checkedSets + " sets in " + watch.ElapsedMilliseconds + " ms");
            return ok;
        }

        /// <summary>--volume: the plan saved as a volume description (Shared.Volume\VolumeSpec), read back; with --lb, attached by the
        /// RAM disk helper (view=volume, 1.17) and MAME verifying the sets ON THE MOUNTED VOLUME - the volume is the romset, nothing
        /// written. Detached after unless --keep. MOUNTS A DISK.</summary>
        private static bool Volume(MameIndex index, List<MameZipPlan> plans, ICollection<string> toVerify, string description, string lb, string mame, bool keep)
        {
            bool ok = true;
            var watch = Stopwatch.StartNew();
            var spec = new VolumeSpec { Label = "MAME" + index.Build.Split(' ')[0].Replace(".", "").Replace("(", "") };
            spec.Label = new string(spec.Label.Take(11).ToArray());
            using (var pool = new SourcePool())
                foreach (var p in plans)
                {
                    if (p.Missing.Count + p.MissingDisks.Count > 0)
                    {
                        Console.WriteLine("  " + p.Set + "  MISSING: " + string.Join(", ", p.Missing.Concat(p.MissingDisks).Take(6)));
                        ok = false;
                    }
                    if (p.Items.Count > 0) spec.AddZip(p.Set + ".zip", p.Items, pool);
                    foreach (var (name, source) in p.Disks) spec.AddPassThrough(p.Set + "\\" + name, source);
                }
            spec.Save(description);
            Console.WriteLine("  description: " + description + ", " + spec.Files.Count + " files, " + spec.Sources.Count + " sources, "
                              + new FileInfo(description).Length.ToString("N0") + " bytes, made in " + watch.ElapsedMilliseconds + " ms");

            watch.Restart();
            var back = VolumeSpec.Load(description);
            var stale = new List<string>();
            using (var pool = new SourcePool())
            {
                var files = back.Open(pool, stale);
                var view = new ExfatVolumeView(files, back.Dirs, back.Label);
                Console.WriteLine("  read back: " + files.Count + " files, " + stale.Count + " stale, volume " + (view.Length >> 20).ToString("N0") + " MB, "
                                  + view.Skipped.Count + " skipped, in " + watch.ElapsedMilliseconds + " ms");
                foreach (var s in stale.Take(10)) Console.WriteLine("    stale " + s);
                if (stale.Count > 0 || view.Skipped.Count > 0) ok = false;
            }
            if (lb == null) { Console.WriteLine(ok ? "ALL OK (not attached: no --lb)" : "FAILED"); return ok; }

            LbIntegrations.RamDisk.RamDiskHost.UseRoot(lb);
            LbIntegrations.RamDisk.RamDrive.ProxyOverride = Environment.GetEnvironmentVariable("LBIP_PROXY");
            Console.WriteLine("  helper " + (LbIntegrations.RamDisk.RamDrive.HelperVersion?.ToString() ?? "absent"));
            watch.Restart();
            var root = LbIntegrations.RamDisk.RamDrive.AttachVolume(Path.GetFullPath(description), out var error);
            Console.WriteLine("  attach: " + (root ?? "FAILED - " + error) + " in " + watch.ElapsedMilliseconds + " ms");
            if (root == null) return false;
            try
            {
                var drive = new DriveInfo(root);
                Console.WriteLine("  volume: " + drive.DriveFormat + ", label " + drive.VolumeLabel + ", " + drive.TotalSize.ToString("N0") + " bytes, "
                                  + Directory.GetFileSystemEntries(root).Length + " entries at its root");
                // Every zip on the volume read by .NET: each entry's CRC from its decompressed bytes (the zips of the sets verified).
                foreach (var p in plans.Where(p => p.Items.Count > 0 && toVerify.Contains(p.Set)))
                {
                    using var z = System.IO.Compression.ZipFile.OpenRead(Path.Combine(root, p.Set + ".zip"));
                    foreach (var e in z.Entries)
                    {
                        using var s = e.Open();
                        if (Crc32(s) != e.Crc32) { Console.WriteLine("    CRC MISMATCH " + p.Set + ".zip/" + e.FullName); ok = false; }
                    }
                }
                if (mame != null && !VerifyWithMame(mame, root, index, toVerify)) ok = false;
            }
            finally
            {
                if (keep) Console.WriteLine("  kept attached at " + root);
                else Console.WriteLine("  detach: " + (LbIntegrations.RamDisk.RamDrive.DetachImage(root, out var derr) ? "OK" : "FAILED - " + derr));
            }
            Console.WriteLine(ok ? "ALL OK" : "FAILED");
            return ok;
        }
    }
}
