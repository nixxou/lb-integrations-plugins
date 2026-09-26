// What the snapshot engine has to get right, on a forged tree.
//
// Two properties, and the second is the one that breaks quietly. The difference has to find every
// kind of change - a file added, a file altered, a file deleted, and a directory created and left
// empty - and putting it back has to reproduce the tree exactly. Then the container has to be
// DETERMINISTIC: the same content packed twice, from folders written in different orders and with
// different timestamps, has to give the same sha256, because the host fingerprints a save by hashing
// its bytes and a save that rewrites itself would flicker for ever.
//
// EVERYTHING HERE IS FORGED, under the temp folder, and deleted afterwards. No emulator is involved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Security.Cryptography;
using LbIntegrations.Snapshot;

namespace LbIntegrations.Probe
{
    internal static class SnapshotCheck
    {
        private static int _bad;

        public static bool Run()
        {
            Console.WriteLine();
            Console.WriteLine("-- the snapshot engine  [WRITES, in the temp folder] " + new string('-', 11));

            _bad = 0;
            var root = Path.Combine(Path.GetTempPath(), "lbip-snap-" + Guid.NewGuid().ToString("N"));
            try
            {
                SnapLog.Use(m => Console.WriteLine("  [log] " + m),
                            (m, ex) => Console.WriteLine("  [log] " + m + (ex != null ? " - " + ex.Message : "")));

                Directory.CreateDirectory(root);
                Console.WriteLine("  under     " + root);

                TheDifference(root);
                PuttingItBack(root);
                Determinism(root);
                Stamps(root);

                Console.WriteLine();
                Console.WriteLine(_bad == 0 ? "  OK - the difference finds every kind of change, and the file is stable"
                                            : "  " + _bad + " FAILURE(S) - see above");
                return _bad == 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  EXCEPTION: " + ex);
                return false;
            }
            finally { Scrub(root); }
        }

        // ── the four kinds of change ─────────────────────────────────────────

        private static string _tree, _reference, _state;

        private static void TheDifference(string root)
        {
            Console.WriteLine();
            Console.WriteLine("  the difference");

            _tree = Path.Combine(root, "tree");
            _reference = Path.Combine(root, "reference.txt");
            _state = Path.Combine(root, "state");

            // A base tree: three files, one of them nested.
            Write(_tree, "ux0/app/PCSE00965/eboot.bin", "the game");
            Write(_tree, "ux0/app/PCSE00965/sce_sys/param.sfo", "metadata");
            Write(_tree, "vs0/data/font.pvf", "a font");
            Write(_tree, "doomed.txt", "this one goes away");
            Directory.CreateDirectory(Path.Combine(_tree, "ux0/user/00"));

            int count = SnapWalk.Write(_tree, _reference, out var error);
            Check("the reference walk succeeds", count > 0, error);
            Console.WriteLine("            " + count + " entries");

            // Now a session happens: one file changed, one added, one deleted, one directory created
            // and left EMPTY.
            Write(_tree, "ux0/app/PCSE00965/eboot.bin", "the game, patched");
            Write(_tree, "ux0/user/00/savedata/PCSE00965/data.bin", "a save");
            File.Delete(Path.Combine(_tree, "doomed.txt"));
            Directory.CreateDirectory(Path.Combine(_tree, "ux0/user/00/trophy"));

            int captured = SnapDelta.Capture(_tree, _reference, _state, out error);
            Check("the capture succeeds", captured >= 0, error);

            var index = File.ReadAllLines(Path.Combine(_state, SnapDelta.IndexName));
            var kinds = index.Select(l => l.Split('\t')[0]).ToList();
            var paths = index.Select(l => l.Split('\t').Last()).ToList();

            Check("the altered file is captured", paths.Contains("ux0/app/PCSE00965/eboot.bin"));
            Check("the new file is captured", paths.Contains("ux0/user/00/savedata/PCSE00965/data.bin"));
            Check("the deleted file is recorded", kinds.Contains("X") && paths.Contains("doomed.txt"));
            Check("the new empty directory is recorded", kinds.Contains("D") && paths.Contains("ux0/user/00/trophy"));
            Check("the untouched file is NOT captured", !paths.Contains("vs0/data/font.pvf"));
            Check("the directory that already existed is not recorded", !paths.Contains("ux0/user/00"));
            Console.WriteLine("            " + index.Length + " index lines, " + captured + " file(s) copied");
        }

        // ── putting it back ──────────────────────────────────────────────────

        private static void PuttingItBack(string root)
        {
            Console.WriteLine();
            Console.WriteLine("  putting it back");

            // A fresh copy of the ORIGINAL tree, as a launch would rebuild it from the base.
            var fresh = Path.Combine(root, "fresh");
            Write(fresh, "ux0/app/PCSE00965/eboot.bin", "the game");
            Write(fresh, "ux0/app/PCSE00965/sce_sys/param.sfo", "metadata");
            Write(fresh, "vs0/data/font.pvf", "a font");
            Write(fresh, "doomed.txt", "this one goes away");
            Directory.CreateDirectory(Path.Combine(fresh, "ux0/user/00"));

            int written = SnapDelta.Apply(fresh, _state, out var error);
            Check("the state applies", written >= 0, error);

            Check("the altered file came back altered",
                  Read(fresh, "ux0/app/PCSE00965/eboot.bin") == "the game, patched");
            Check("the new file is there", Read(fresh, "ux0/user/00/savedata/PCSE00965/data.bin") == "a save");
            Check("the deleted file is gone again", !File.Exists(Path.Combine(fresh, "doomed.txt")));
            Check("the empty directory is back", Directory.Exists(Path.Combine(fresh, "ux0/user/00/trophy")));
            Check("the untouched file is untouched", Read(fresh, "vs0/data/font.pvf") == "a font");

            // AND THE WHOLE TREE MATCHES. Individual checks can all pass while something else moved.
            var a = SnapWalk.Of(_tree, out _);
            var b = SnapWalk.Of(fresh, out _);
            Check("every entry of the played tree is reproduced", Same(a, b));
        }

        // ── the same bytes every time ────────────────────────────────────────

        private static void Determinism(string root)
        {
            Console.WriteLine();
            Console.WriteLine("  the packed file");

            // The SAME content in two folders, written in opposite orders and with different
            // timestamps - the two things a careless writer would let through.
            var one = Path.Combine(root, "pack-one");
            var two = Path.Combine(root, "pack-two");
            var names = new[] { "alpha.bin", "beta.bin", "files.txt" };

            Directory.CreateDirectory(one);
            Directory.CreateDirectory(two);
            foreach (var n in names) File.WriteAllText(Path.Combine(one, n), "content of " + n);
            // Enumerable.Reverse and not names.Reverse(): on an array the latter binds to
            // MemoryExtensions.Reverse, which reverses IN PLACE and returns void.
            foreach (var n in Enumerable.Reverse(names))
            {
                var p = Path.Combine(two, n);
                File.WriteAllText(p, "content of " + n);
                File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddDays(-400));
            }

            var fileOne = Path.Combine(root, "one.vitasav");
            var fileTwo = Path.Combine(root, "two.vitasav");
            Check("the first packs", SnapFile.Pack(one, fileOne, out var e1), e1);
            Check("the second packs", SnapFile.Pack(two, fileTwo, out var e2), e2);

            var h1 = Sha256(fileOne);
            var h2 = Sha256(fileTwo);
            Console.WriteLine("            " + h1.Substring(0, 24) + "…");
            Check("both give the SAME sha256", h1 == h2);

            Check("it is recognised as ours", SnapFile.Holds(fileOne));
            Check("a file that is not ours is refused", !SnapFile.Holds(Path.Combine(one, "alpha.bin")));

            var back = Path.Combine(root, "unpacked");
            Check("it unpacks", SnapFile.Unpack(fileOne, back, out var e3), e3);
            Check("with every entry", Directory.GetFiles(back).Length == names.Length);
            Check("and the content survives", Read(back, "alpha.bin") == "content of alpha.bin");
        }

        // ── stamps: the fast walk must be the full walk ──────────────────────

        private static void Stamps(string root)
        {
            Console.WriteLine();
            Console.WriteLine("  stamps");

            var tree = Path.Combine(root, "stamped");
            var reference = Path.Combine(root, "stamped.txt");

            Write(tree, "vs0/keep.bin", "never touched");
            Write(tree, "same-size.bin", "AAAA");
            Write(tree, "ux0/save.dat", "old save");
            Write(tree, "swap-a.bin", "content A");
            Thread.Sleep(50);                       // the two to be swapped get different times
            Write(tree, "swap-b.bin", "content B");
            Write(tree, "restored.bin", "pristine");
            Write(tree, "chstat.bin", "1234");
            Write(tree, "doomed.bin", "goes away");

            Check("the reference walk succeeds", SnapWalk.Write(tree, reference, out var error) > 0, error);

            // THE RESTORE, at its worst: a save file written over a reference file at the same size,
            // with both times put back to what they were. Only being NAMED keeps it honest.
            var restored = Path.Combine(tree, "restored.bin");
            var (w0, c0) = (File.GetLastWriteTimeUtc(restored), File.GetCreationTimeUtc(restored));
            File.WriteAllText(restored, "fromsave");
            File.SetLastWriteTimeUtc(restored, w0);
            File.SetCreationTimeUtc(restored, c0);

            Check("the tree is stamped", SnapStamps.Write(tree, reference, new[] { "restored.bin" }, out error), error);

            // A session, a moment later.
            Thread.Sleep(100);
            Write(tree, "same-size.bin", "BBBB");
            Write(tree, "ux0/save.dat.tmp", "new save");
            File.Delete(Path.Combine(tree, "ux0/save.dat"));
            File.Move(Path.Combine(tree, "ux0/save.dat.tmp"), Path.Combine(tree, "ux0/save.dat"));
            File.Move(Path.Combine(tree, "swap-a.bin"), Path.Combine(tree, "swap.tmp"));
            File.Move(Path.Combine(tree, "swap-b.bin"), Path.Combine(tree, "swap-a.bin"));
            File.Move(Path.Combine(tree, "swap.tmp"), Path.Combine(tree, "swap-b.bin"));
            Write(tree, "ux0/new.bin", "brand new");
            File.Delete(Path.Combine(tree, "doomed.bin"));

            // The one case the rule does not see, done on purpose: same size, both times put back.
            var chstat = Path.Combine(tree, "chstat.bin");
            var (w1, c1) = (File.GetLastWriteTimeUtc(chstat), File.GetCreationTimeUtc(chstat));
            File.WriteAllText(chstat, "5678");
            File.SetLastWriteTimeUtc(chstat, w1);
            File.SetCreationTimeUtc(chstat, c1);

            var stamps = SnapStamps.Read(reference);
            Check("the stamps belong to their reference", stamps != null);
            var full = SnapWalk.Of(tree, out error);
            var fast = SnapWalk.Of(tree, out error, null, SnapWalk.Read(reference), stamps, out int hashed);
            Check("both walks succeed", full != null && fast != null, error);
            if (full == null || fast == null) return;

            var differ = full.Keys.Union(fast.Keys)
                             .Where(k => !full.TryGetValue(k, out var a) || !fast.TryGetValue(k, out var b) || !a.SameAs(b))
                             .OrderBy(k => k, StringComparer.Ordinal).ToList();
            Check("the fast walk equals the full walk, but for the one known case",
                  differ.Count == 1 && differ[0] == "chstat.bin", string.Join(", ", differ));
            Console.WriteLine("            known, by design: a same-size rewrite with BOTH times put back is not seen"
                              + (differ.Contains("chstat.bin") ? " - and it was not" : " - yet it was"));
            Check("only the touched files were read (6: same size, save, the swapped two, restored, new)",
                  hashed == 6, hashed + " read");

            var state = Path.Combine(root, "stamped-state");
            int captured = SnapDelta.Capture(tree, reference, state, out error, null, out int read);
            Check("the capture with stamps succeeds", captured >= 0, error);
            var index = File.ReadAllText(Path.Combine(state, SnapDelta.IndexName));
            Check("the restored file is captured again", index.Contains("\trestored.bin\n"));
            Check("the renamed-over save is captured", index.Contains("\tux0/save.dat\n"));
            Check("both swapped files are captured", index.Contains("\tswap-a.bin\n") && index.Contains("\tswap-b.bin\n"));
            Check("the same-size rewrite is captured", index.Contains("\tsame-size.bin\n"));
            Check("the untouched file is not", !index.Contains("keep.bin"));
            Check("the deletion is recorded", index.Contains("X\t-\tdoomed.bin"));

            // A reference written again: its old stamps must not be believed.
            Thread.Sleep(20);
            SnapWalk.Write(tree, reference, out error);
            Check("stamps of another reference are refused", SnapStamps.Read(reference) == null);
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static bool Same(Dictionary<string, SnapEntry> a, Dictionary<string, SnapEntry> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            foreach (var pair in a)
            {
                if (!b.TryGetValue(pair.Key, out var other)) return false;
                if (!pair.Value.SameAs(other)) return false;
            }
            return true;
        }

        private static void Write(string root, string relative, string content)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
        }

        private static string Read(string root, string relative)
        {
            try { return File.ReadAllText(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))); }
            catch { return null; }
        }

        private static string Sha256(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream));
        }

        private static void Check(string what, bool ok, string error = null)
        {
            Console.WriteLine("    " + (ok ? "ok  " : "BAD ") + what
                              + (!ok && !string.IsNullOrEmpty(error) ? "  (" + error + ")" : ""));
            if (!ok) _bad++;
        }

        private static void Scrub(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
