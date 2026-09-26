// What changed between a reference walk and a tree, and how to put it back.
//
// The state folder is FLAT by construction: every captured file is stored under a name with its
// separators replaced, and an index says where each one came from. That is not tidiness - it is what
// makes the container a plain zip with no subdirectories in it, and what stops a hand-made one from
// writing outside the destination when it is applied.
//
//     files.txt      F <flat name> <path>     a file to write back
//                    X -           <path>     a file to delete
//                    D -           <path>     a directory to create, even if it stays empty
//
// THE REFERENCE IS ALWAYS A FRESH INSTALL, never the previous session. A stored delta is therefore
// complete in itself - base plus install plus apply - with no chain of increments to replay, and no
// way for one bad capture to poison every later one.
//
// AND A CAPTURE THAT LOSES ENTRIES IS REFUSED. When the walk finds things missing, it walks a second
// time and compares the two: a tree still being written gives two different answers, and the capture
// is abandoned rather than recorded. The DSi engine paid for this lesson on a real evening - a
// capture racing melonDS's shutdown came back missing 56 of about 60 entries, and 27 milliseconds
// later was missing 45. It tests the READ, not the session: refusing a capture that deletes "too
// much" would instead punish somebody who deliberately deleted things.

#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LbIntegrations.Snapshot
{
    internal static class SnapDelta
    {
        /// <summary>The index inside a state folder, and inside the packed save.</summary>
        public const string IndexName = "files.txt";

        // ── comparing ────────────────────────────────────────────────────────

        /// <summary>What <paramref name="actual"/> has that <paramref name="reference"/> did not, and
        /// what it has lost. Both lists come back sorted ordinal.</summary>
        public static void Compare(Dictionary<string, SnapEntry> reference,
                                   Dictionary<string, SnapEntry> actual,
                                   out List<string> differing, out List<string> removed,
                                   out List<string> directories)
        {
            differing = new List<string>();
            removed = new List<string>();
            directories = new List<string>();

            foreach (var pair in actual)
            {
                SnapEntry was;
                reference.TryGetValue(pair.Key, out was);

                if (pair.Value.IsDirectory)
                {
                    // A directory is only worth recording when it is NEW. One that already existed
                    // will be recreated by whatever file goes into it, or by the base itself.
                    if (was == null) directories.Add(pair.Key);
                    continue;
                }
                if (was == null || !was.SameAs(pair.Value)) differing.Add(pair.Key);
            }

            foreach (var pair in reference)
            {
                if (pair.Value.IsDirectory) continue;   // a lost directory follows its files
                if (!actual.ContainsKey(pair.Key)) removed.Add(pair.Key);
            }

            differing.Sort(StringComparer.Ordinal);
            removed.Sort(StringComparer.Ordinal);
            directories.Sort(StringComparer.Ordinal);
        }

        /// <summary>A path turned into a single flat file name. "ux0/user/00/savedata/x.bin" becomes
        /// "ux0_user_00_savedata_x.bin" - unique because the separator is the only thing replaced,
        /// and legible because nothing else is.</summary>
        public static string FlatName(string path)
        {
            var flat = (path ?? "").Replace('/', '_').Replace('\\', '_').Replace(':', '_');
            foreach (var bad in Path.GetInvalidFileNameChars()) flat = flat.Replace(bad, '_');
            return flat;
        }

        // ── capturing ────────────────────────────────────────────────────────

        /// <summary>Build a state folder from what <paramref name="root"/> has that the reference did
        /// not. Returns the number of captured files, or -1.
        ///
        /// The folder is built under a temporary name and swapped in, so a capture interrupted half
        /// way never leaves a state that is neither the old one nor the new one.</summary>
        public static int Capture(string root, string referencePath, string stateDir, out string error)
            => Capture(root, referencePath, stateDir, out error, null);

        /// <summary>The same capture, saying how far its walk of the tree has got - 0..1, by bytes.</summary>
        public static int Capture(string root, string referencePath, string stateDir, out string error,
                                  Action<double> progress)
            => Capture(root, referencePath, stateDir, out error, progress, out _);

        /// <summary>The same capture; <paramref name="hashed"/> says how many files it had to read.
        /// When the reference has stamps (SnapStamps), only files touched since are read.</summary>
        public static int Capture(string root, string referencePath, string stateDir, out string error,
                                  Action<double> progress, out int hashed)
        {
            error = null;
            hashed = -1;
            string building = null;
            try
            {
                var reference = SnapWalk.Read(referencePath);
                if (reference.Count == 0)
                { error = "there is no reference to compare against"; return -1; }

                var stamps = SnapStamps.Read(referencePath);
                var actual = SnapWalk.Of(root, out error, progress, reference, stamps, out hashed);
                if (actual == null) return -1;

                List<string> differing, removed, directories;
                Compare(reference, actual, out differing, out removed, out directories);

                // A SECOND LOOK WHEN SOMETHING IS MISSING. See the header: this asks whether the tree
                // is still, not whether the user deleted too much.
                if (removed.Count > 0)
                {
                    var again = SnapWalk.Of(root, out error, null, reference, stamps, out _);
                    if (again == null) return -1;

                    List<string> differing2, removed2, directories2;
                    Compare(reference, again, out differing2, out removed2, out directories2);

                    if (removed2.Count != removed.Count || differing2.Count != differing.Count)
                    {
                        error = "the tree changed between two walks (" + removed.Count + " then "
                                + removed2.Count + " missing) - it is still being written, nothing captured";
                        SnapLog.Warn(error);
                        return -1;
                    }
                }

                building = stateDir + "." + Guid.NewGuid().ToString("N") + ".part";
                Directory.CreateDirectory(building);

                var index = new StringBuilder();
                foreach (var path in differing)
                {
                    var flat = FlatName(path);
                    File.Copy(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)),
                              Path.Combine(building, flat), overwrite: true);
                    index.Append("F\t").Append(flat).Append('\t').Append(path).Append('\n');
                }
                foreach (var path in removed) index.Append("X\t-\t").Append(path).Append('\n');
                foreach (var path in directories) index.Append("D\t-\t").Append(path).Append('\n');

                File.WriteAllText(Path.Combine(building, IndexName), index.ToString(), new UTF8Encoding(false));

                Swap(building, stateDir);
                building = null;

                SnapLog.Info("captured " + differing.Count + " file(s), " + removed.Count
                             + " removal(s), " + directories.Count + " new director(ies)");
                return differing.Count;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                SnapLog.Warn("could not capture from " + root, ex);
                return -1;
            }
            finally { if (building != null) Scrub(building); }
        }

        // ── putting it back ──────────────────────────────────────────────────

        /// <summary>The paths a state folder writes as files ("F" lines), in index order. Empty when
        /// there is no index.</summary>
        public static List<string> FilesIn(string stateDir)
        {
            var paths = new List<string>();
            try
            {
                var index = Path.Combine(stateDir ?? "", IndexName);
                if (!File.Exists(index)) return paths;
                foreach (var line in File.ReadAllLines(index))
                {
                    var parts = line.Split('\t');
                    if (parts.Length < 3 || parts[0] != "F") continue;
                    paths.Add(parts.Length == 3 ? parts[2] : string.Join("\t", parts, 2, parts.Length - 2));
                }
            }
            catch (Exception ex) { SnapLog.Warn("could not read the index of " + stateDir, ex); }
            return paths;
        }

        /// <summary>Apply a state folder onto <paramref name="root"/>. Returns the number of files
        /// written, or -1. A missing index is 0 and not a failure: a session that changed nothing is
        /// a perfectly ordinary session.</summary>
        public static int Apply(string root, string stateDir, out string error)
        {
            error = null;
            try
            {
                var index = Path.Combine(stateDir ?? "", IndexName);
                if (!File.Exists(index)) return 0;

                var full = Path.GetFullPath(root);
                int written = 0;

                foreach (var line in File.ReadAllLines(index))
                {
                    if (line.Length == 0) continue;
                    var parts = line.Split('\t');
                    if (parts.Length < 3) continue;
                    var path = parts.Length == 3 ? parts[2] : string.Join("\t", parts, 2, parts.Length - 2);

                    // NOTHING WRITES OUTSIDE THE ROOT. The index is inside a file a user can edit,
                    // and this runs over their disk.
                    var target = Path.GetFullPath(Path.Combine(full, path.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(full, StringComparison.OrdinalIgnoreCase))
                    { SnapLog.Warn("refused an entry that escapes the tree: " + path); continue; }

                    switch (parts[0])
                    {
                        case "F":
                            var source = Path.Combine(stateDir, Path.GetFileName(parts[1]));
                            if (!File.Exists(source)) { SnapLog.Warn("missing from the save: " + parts[1]); break; }
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            File.Copy(source, target, overwrite: true);
                            written++;
                            break;

                        case "X":
                            try { if (File.Exists(target)) File.Delete(target); } catch { }
                            break;

                        case "D":
                            Directory.CreateDirectory(target);
                            break;
                    }
                }

                SnapLog.Info("restored " + written + " file(s) into " + root);
                return written;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                SnapLog.Warn("could not apply " + stateDir, ex);
                return -1;
            }
        }

        // ── helpers ──────────────────────────────────────────────────────────

        /// <summary>Put the freshly built folder where the old one was, in two moves rather than a
        /// copy: at no point is there a folder holding half of each.</summary>
        private static void Swap(string building, string stateDir)
        {
            string retired = null;
            if (Directory.Exists(stateDir))
            {
                retired = stateDir + "." + Guid.NewGuid().ToString("N") + ".old";
                Move(stateDir, retired);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(stateDir)));
            Move(building, stateDir);
            if (retired != null) Scrub(retired);
        }

        /// <summary>A folder rename that outlasts a brief lock. The files in it were copied a moment
        /// ago, and real-time antivirus opens each new one to scan it; while it does, renaming the
        /// folder is refused ("access denied", measured once in three runs of the probe). Two seconds
        /// at most, then the error is the caller's.</summary>
        private static void Move(string from, string to)
        {
            for (int attempt = 1; ; attempt++)
            {
                try { Directory.Move(from, to); return; }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 20)
                {
                    System.Threading.Thread.Sleep(100);
                }
            }
        }

        private static void Scrub(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch (Exception ex) { SnapLog.Warn("could not remove " + dir, ex); }
        }
    }
}
