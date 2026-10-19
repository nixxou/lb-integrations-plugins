// --mame-convert: MAME sets seen in another layout (src\Shared.Mame), checked by MAME itself.
//
//   Probe --mame-convert --index <listxml or .tsv.gz> --src <folder of sets> --layout nonmerged|split|merged
//         --out <folder> --sets a,b,c [--mame <mame.exe>]
//
// The sets asked and what they need (parents, BIOS, devices) are planned in the target layout; each zip is made as a
// VirtualZip and WRITTEN to <out> (the helper never writes: here it is how MAME gets to read them), then, with --mame,
// "mame -rompath <out> -verifyroms <set>" says whether each set is good. A listxml given is cached beside it as .tsv.gz.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LbIntegrations.Mame;

namespace LbIntegrations.Probe
{
    internal static class MameCheck
    {
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

        public static bool Convert(string indexPath, string src, string layoutName, string outDir, string sets, string mame)
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
            var asked = sets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var machines = conv.Closure(asked);
            watch.Restart();
            var plans = conv.Plan(layout, machines);
            Console.WriteLine("  " + layout + ": " + plans.Count + " zips for " + machines.Count + " machines, planned in " + watch.ElapsedMilliseconds + " ms");
            foreach (var u in conv.Unreadable) Console.WriteLine("  UNREADABLE " + u);

            Directory.CreateDirectory(outDir);
            bool ok = true;
            foreach (var p in plans)
            {
                using var vz = new VirtualZip(p.Items);
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

            if (mame != null)
            {
                foreach (var set in machines.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                {
                    var m = index.Get(set);
                    if (m == null || m.IsDevice || m.Roms.Count == 0) continue;
                    var psi = new ProcessStartInfo(mame, "-rompath \"" + outDir + "\" -verifyroms " + set)
                    {
                        WorkingDirectory = Path.GetDirectoryName(mame), UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                    };
                    using var proc = Process.Start(psi);
                    var outText = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
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
            }
            Console.WriteLine(ok ? "ALL OK" : "FAILED");
            return ok;
        }
    }
}
