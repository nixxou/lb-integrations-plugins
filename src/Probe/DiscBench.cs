// --disc-bench <image>: what reading a compressed disc costs, without AIM or the helper (06/10, before any read-ahead): the
// container file's raw speed on its disk (a region never read, cold), the disc decoded through Shared.Disc (another region,
// cold - disk and codec - then the same again, the file now in Windows' cache - the codec alone), and what CHDSharp's stream
// is made of (to see whether a hunk can be read on its own). Reads only.

using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LbIntegrations.Probe
{
    internal static class DiscBench
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static bool Run(Assembly asm, string image)
        {
            var open = asm.GetType("LbIntegrations.Disc.DiscImages", true).GetMethod("Open", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            using var disc = (Stream)open.Invoke(null, new object[] { image });
            long fileLength = new FileInfo(image).Length;
            Console.WriteLine("  " + Path.GetFileName(image) + ": file " + fileLength.ToString("N0") + " bytes, disc " + disc.Length.ToString("N0") + " bytes, stream " + disc.GetType().FullName);

            // What the stream holds: its fields, and the methods that look like a hunk's read.
            var t = disc.GetType();
            foreach (var f in t.GetFields(Any)) Console.WriteLine("    field " + f.FieldType.Name + " " + f.Name + " = " + Short(f.GetValue(f.IsStatic ? null : disc)));
            foreach (var m in t.GetMethods(Any).Where(m => m.DeclaringType == t)) Console.WriteLine("    method " + m.ReturnType.Name + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
            foreach (var f in t.GetFields(Any))
            {
                var v = f.GetValue(f.IsStatic ? null : disc);
                if (v == null || v.GetType().IsPrimitive || v is string) continue;
                var vt = v.GetType();
                if (vt.Namespace == null || !vt.Namespace.StartsWith("CHD")) continue;
                Console.WriteLine("    " + f.Name + ": " + vt.FullName);
                foreach (var pr in vt.GetProperties(Any)) { object pv; try { pv = pr.GetValue(v); } catch { pv = "?"; } Console.WriteLine("      prop " + pr.PropertyType.Name + " " + pr.Name + " = " + Short(pv)); }
                var hdr = vt.GetField("_chd", Any)?.GetValue(v);
                if (hdr != null) foreach (var pr in hdr.GetType().GetProperties(Any).Cast<MemberInfo>().Concat(hdr.GetType().GetFields(Any))) { object pv; try { pv = pr is PropertyInfo pi ? pi.GetValue(hdr) : ((FieldInfo)pr).GetValue(hdr); } catch { pv = "?"; } Console.WriteLine("      header " + pr.Name + " = " + Short(pv)); }
                foreach (var g in vt.GetFields(Any)) Console.WriteLine("      field " + g.FieldType.Name + " " + g.Name + " = " + Short(g.GetValue(g.IsStatic ? null : v)));
                foreach (var m in vt.GetMethods(Any).Where(m => m.DeclaringType == vt)) Console.WriteLine("      method " + m.ReturnType.Name + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
            }

            const long Chunk = 512L << 20;
            var buf = new byte[4 << 20];
            // 1. the file itself, a region at 70 % of it: never read by the decodes below.
            using (var raw = new FileStream(image, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan))
            {
                raw.Seek(fileLength * 7 / 10, SeekOrigin.Begin);
                Console.WriteLine("  raw file, cold:         " + Speed(raw, Chunk, buf));
            }
            // 2. the disc at 30 % of it: cold, then again warm.
            long at = disc.Length * 3 / 10 / 2048 * 2048;
            disc.Seek(at, SeekOrigin.Begin);
            Console.WriteLine("  decoded, cold:          " + Speed(disc, Chunk, buf));
            disc.Seek(at, SeekOrigin.Begin);
            Console.WriteLine("  decoded, file in cache: " + Speed(disc, Chunk, buf));
            if (!image.EndsWith(".chd", StringComparison.OrdinalIgnoreCase)) return true;

            // 3. the CHD on several cores, the same region (the file in cache): speed, then its bytes against the plain stream's.
            var par = asm.GetType("LbIntegrations.Disc.DiscImages", true).GetMethod("OpenChdParallel", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            bool allSame = true;
            foreach (int threads in new[] { 1, 2, 4, 8, 16 })
            {
                using var p = (Stream)par.Invoke(null, new object[] { image, threads });
                p.Seek(at, SeekOrigin.Begin);
                long mem0 = GC.GetTotalMemory(true);
                Console.WriteLine(("  " + threads + " thread(s):").PadRight(26) + Speed(p, Chunk, buf) + ", managed memory +" + ((GC.GetTotalMemory(false) - mem0) >> 20) + " MB");
                // Bytes: 64 MB in the region, then 300 random 2 KB reads - each against the plain stream.
                var x = new byte[1 << 20]; var y = new byte[1 << 20];
                p.Seek(at, SeekOrigin.Begin); disc.Seek(at, SeekOrigin.Begin);
                for (int i = 0; i < 64 && allSame; i++) { Fill(p, x); Fill(disc, y); if (!x.AsSpan().SequenceEqual(y)) { allSame = false; Console.WriteLine("    DIFFERS at MB " + i); } }
                var rng = new Random(threads); var a2 = new byte[2048]; var b2 = new byte[2048]; var times = new System.Collections.Generic.List<double>();
                for (int i = 0; i < 300 && allSame; i++)
                {
                    long q = (long)(rng.NextDouble() * (disc.Length - 4096)) / 512 * 512;
                    p.Seek(q, SeekOrigin.Begin); var t0 = System.Diagnostics.Stopwatch.GetTimestamp(); Fill(p, a2); times.Add((System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                    disc.Seek(q, SeekOrigin.Begin); Fill(disc, b2);
                    if (!a2.AsSpan().SequenceEqual(b2)) { allSame = false; Console.WriteLine("    DIFFERS at " + q); }
                }
                times.Sort();
                Console.WriteLine("    same bytes: " + allSame + "; random 2 KB reads: median " + times[times.Count / 2].ToString("0.000") + " ms, p95 " + times[times.Count * 95 / 100].ToString("0.000") + " ms");
            }

            // 4. CHDSharp alone: N threads, each its own ChdFile, each ReadHunk over its own slice in a tight loop - no cache, no task.
            var chdType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("CHDSharp.ChdFile", false)).FirstOrDefault(x => x != null);
            var openFile = chdType.GetMethods(BindingFlags.Static | BindingFlags.Public).First(m => m.Name == "Open" && m.GetParameters().Length == 3 && m.GetParameters()[0].ParameterType == typeof(string));
            var readHunk = chdType.GetMethods(BindingFlags.Instance | BindingFlags.Public).First(m => m.Name == "ReadHunk" && m.GetParameters()[1].ParameterType == typeof(byte[]));
            Console.WriteLine("  GC: server " + System.Runtime.GCSettings.IsServerGC + ", " + System.Runtime.GCSettings.LatencyMode);
            {
                long m0 = GC.GetTotalMemory(true);
                var a = new object[] { image, null, System.Threading.CancellationToken.None }; openFile.Invoke(null, a);
                var hb = new byte[4096]; readHunk.Invoke(a[1], new object[] { 0u, hb, System.Threading.CancellationToken.None });
                long m1 = GC.GetTotalMemory(true);
                Console.WriteLine("  one ChdFile open, one hunk read: " + ((m1 - m0) >> 20) + " MB kept");
                ((IDisposable)a[1]).Dispose();
            }
            foreach (int threads in new[] { 1, 2, 4, 8, 12, 16 })
            {
                var files = new object[threads];
                for (int i = 0; i < threads; i++) { var a = new object[] { image, null, System.Threading.CancellationToken.None }; openFile.Invoke(null, a); files[i] = a[1]; }
                long firstHunk = at / 4096; int per = 131072 / threads;      // 512 MB of 4 KB hunks in all
                var w = System.Diagnostics.Stopwatch.StartNew(); var cpu = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime;
                int gc0 = GC.CollectionCount(0);
                System.Threading.Tasks.Parallel.For(0, threads, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = threads }, i =>
                {
                    var hb = new byte[4096]; var args = new object[] { 0u, hb, System.Threading.CancellationToken.None };
                    for (long h = firstHunk + (long)i * per; h < firstHunk + (long)(i + 1) * per; h++) { args[0] = (uint)h; readHunk.Invoke(files[i], args); }
                });
                var used = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime - cpu;
                Console.WriteLine(("  raw ReadHunk x" + threads + ":").PadRight(26) + (512.0 / w.Elapsed.TotalSeconds).ToString("0") + " MB/s (CPU " + (used.TotalMilliseconds / w.Elapsed.TotalMilliseconds * 100).ToString("0") + " % of one core), " + (GC.CollectionCount(0) - gc0) + " gen0 GCs");
                foreach (var fl in files) ((IDisposable)fl).Dispose();
            }
            return allSame;
        }

        private static string Speed(Stream s, long bytes, byte[] buf)
        {
            var w = System.Diagnostics.Stopwatch.StartNew();
            var cpu = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime;
            long done = 0; int n;
            while (done < bytes && (n = s.Read(buf, 0, (int)Math.Min(buf.Length, bytes - done))) > 0) done += n;
            var used = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime - cpu;
            return (done >> 20) + " MB in " + w.ElapsedMilliseconds + " ms = " + (done / 1048576.0 / w.Elapsed.TotalSeconds).ToString("0") + " MB/s (CPU " + (used.TotalMilliseconds / w.Elapsed.TotalMilliseconds * 100).ToString("0") + " % of one core)";
        }

        private static void Fill(Stream s, byte[] b) { int got = 0, n; while (got < b.Length && (n = s.Read(b, got, b.Length - got)) > 0) got += n; }

        private static string Short(object v)
        {
            if (v == null) return "null";
            if (v is Array a) return v.GetType().GetElementType().Name + "[" + a.Length + "]";
            var s = v.ToString();
            return s.Length > 80 ? s.Substring(0, 80) + "..." : s;
        }
    }
}
