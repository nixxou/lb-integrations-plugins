// A DSiWare session's save, as the difference between the NAND it ran on and a freshly built one.
//
// THE PROBLEM THIS REPLACES. A DSiWare title lives in the NAND, so the first design gave each title
// its own copy of the user's dump: 240 MB per game, and a 240 MB write before the first launch. It
// worked and it was honest, but it made a library of DSiWare cost gigabytes for a few kilobytes of
// actual state.
//
// WHAT IS ACTUALLY DIFFERENT. Measured, on a real session of a real title, by walking both images:
//
//   the user's base NAND   -> + one title installed     7 entries added, nothing else
//                                                       ticket, the title tree, the .app, its tmd,
//                                                       and an empty public.sav
//   that rebuilt reference -> the NAND actually played  4 files differ, 16 KB each:
//                                                         shared1/TWLCFG0.dat   } the DSi's settings
//                                                         shared1/TWLCFG1.dat   } (it keeps two)
//                                                         shared2/launcher/wrap.bin
//                                                         title/00030017/484e4145/data/private.sav
//
// and putting those four files back into the rebuilt reference made it identical to the played NAND,
// file for file. A whole session is 80 KB of difference inside 240 MB. So the difference is the save,
// and the image is scratch.
//
// THE COMPARISON IS BY FILE, NEVER BY BYTE, and that is not a detail. A FAT directory entry carries
// the wall clock (get_fattime, ffsystem.c:107) and the allocator places clusters in the order the
// operations happened. Two installs of the same title into the same base, a second apart, produce
// images that differ in raw bytes and manifests that are identical - measured, both halves of it.
// A byte delta would therefore be mostly noise, and worse, it would not apply to a reference
// regenerated later. A file delta has neither problem, and it survives a melonDS that changes how it
// lays a title out.
//
// NOTHING DECIDES WHICH FILES MATTER. The four above are what one measurement found; they are not a
// list this code carries. It walks, it compares, it keeps what differs. A title that writes somewhere
// nobody expected is captured because it differed, not because it was foreseen.
//
// THE REFERENCE IS ALWAYS A FRESH INSTALL, never the previous session. So a stored delta is complete
// on its own: restoring is base + install + apply, one step, no chain of increments to replay and
// none to corrupt. The cost is that the delta holds everything that ever changed rather than only
// what changed last time, which for 80 KB is not a cost.

using System;
using System.Collections.Generic;
using System.IO;

namespace LbIntegrations.MelonDs
{
    /// <summary>One file inside a NAND, as a walk reports it.</summary>
    internal struct NandEntry
    {
        public bool IsDirectory;
        public string Size;      // as written, so "?" for something unreadable survives the round trip
        public string Sha1;
    }

    internal static class MelonDsDelta
    {
        /// <summary>The walk of a fresh install, kept beside the title. Recomputable at any time -
        /// that is the whole point - but rebuilding it costs an install, so it is written down.</summary>
        public const string ReferenceName = "reference.txt";

        /// <summary>Where the differing files are kept. One file per NAND path, named by the path
        /// with its separators flattened, plus an index that maps back.</summary>
        public const string StateDirName = "state";

        public const string IndexName = "files.txt";

        /// <summary>Everything in <paramref name="manifestPath"/>, by NAND path.</summary>
        public static Dictionary<string, NandEntry> Read(string manifestPath)
        {
            var map = new Dictionary<string, NandEntry>(StringComparer.Ordinal);
            try
            {
                if (manifestPath == null || !File.Exists(manifestPath)) return map;
                foreach (var line in File.ReadAllLines(manifestPath))
                {
                    // "F <size>\t<sha1>\t<path>" or "D -\t-\t<path>"
                    if (line.Length < 5 || (line[0] != 'F' && line[0] != 'D')) continue;
                    var parts = line.Substring(2).Split(new[] { '\t' }, 3);
                    if (parts.Length != 3) continue;
                    map[parts[2]] = new NandEntry
                    {
                        IsDirectory = line[0] == 'D',
                        Size = parts[0],
                        Sha1 = parts[1],
                    };
                }
            }
            catch (Exception ex) { Log.Verbose("could not read " + manifestPath + " - " + ex.Message); }
            return map;
        }

        /// <summary>The files that differ, and the ones that are gone. Directories are not carried:
        /// a directory holds no content, and importing a file into the NAND makes the path it needs.
        /// A directory that is genuinely empty and genuinely new would be missed - no DSiWare title
        /// has been seen to make one, and saying so here is better than pretending otherwise.</summary>
        public static void Compare(Dictionary<string, NandEntry> reference,
                                   Dictionary<string, NandEntry> actual,
                                   out List<string> differing, out List<string> removed)
        {
            differing = new List<string>();
            removed = new List<string>();

            foreach (var pair in actual)
            {
                if (pair.Value.IsDirectory) continue;
                if (!reference.TryGetValue(pair.Key, out var was)
                    || was.Sha1 != pair.Value.Sha1 || was.Size != pair.Value.Size)
                    differing.Add(pair.Key);
            }
            foreach (var pair in reference)
            {
                if (pair.Value.IsDirectory) continue;
                if (!actual.ContainsKey(pair.Key)) removed.Add(pair.Key);
            }

            differing.Sort(StringComparer.Ordinal);
            removed.Sort(StringComparer.Ordinal);
        }

        /// <summary>A NAND path as a file name, reversibly enough for a human to recognise it. The
        /// index is what maps back; this only has to be unique and legible.</summary>
        public static string FlatName(string nandPath)
        {
            var name = nandPath ?? "";
            foreach (var bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
            return name.Replace('/', '_').Replace(':', '_').Trim('_');
        }

        /// <summary>Walk <paramref name="session"/>, compare it with the reference, and write what
        /// differs into <paramref name="stateDir"/>. Returns how many files were kept, or -1.
        ///
        /// The state directory is rebuilt rather than merged: it describes ONE moment, and a file
        /// that stopped differing has to stop being restored.</summary>
        public static int Capture(NandSession session, string referencePath, string stateDir,
                                  string scratchDir, out string error)
        {
            error = null;
            try
            {
                var walk = Path.Combine(scratchDir, "walk-" + Guid.NewGuid().ToString("N") + ".txt");
                try
                {
                    if (session.Walk(walk, out error) < 0) return -1;

                    var reference = Read(referencePath);
                    if (reference.Count == 0)
                    {
                        error = "there is no reference to compare against";
                        return -1;
                    }
                    Compare(reference, Read(walk), out var differing, out var removed);

                    // Into a new folder, then swapped in: a capture interrupted halfway would
                    // otherwise leave a state that is neither the old one nor the new one.
                    var building = stateDir + "." + Guid.NewGuid().ToString("N") + ".part";
                    Directory.CreateDirectory(building);

                    var index = new List<string>();
                    foreach (var path in differing)
                    {
                        var flat = FlatName(path);
                        if (!session.ExportFile(path, Path.Combine(building, flat), out var why))
                        {
                            Log.Verbose("could not take " + path + " out of the NAND - " + why);
                            continue;
                        }
                        index.Add("F\t" + flat + "\t" + path);
                    }
                    foreach (var path in removed) index.Add("X\t-\t" + path);

                    File.WriteAllLines(Path.Combine(building, IndexName), index);

                    // Said here rather than left to the caller's count, which is the number of files
                    // WRITTEN. A session that only deleted things writes none, and would otherwise be
                    // logged as having captured nothing at all - the one shape where "captured 0"
                    // and "captured nothing" mean opposite things.
                    if (removed.Count > 0)
                        Log.Info(removed.Count + " file(s) the fresh install has were deleted in this "
                                 + "session and are recorded as removals");

                    var old = stateDir + "." + Guid.NewGuid().ToString("N") + ".old";
                    if (Directory.Exists(stateDir)) Directory.Move(stateDir, old);
                    Directory.Move(building, stateDir);
                    try { if (Directory.Exists(old)) Directory.Delete(old, recursive: true); } catch { }

                    return differing.Count;
                }
                finally { try { if (File.Exists(walk)) File.Delete(walk); } catch { } }
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return -1; }
        }

        /// <summary>Put a captured state back into a freshly built NAND. Returns how many files were
        /// written. Missing state is not a failure: a title that has never been played has none.</summary>
        public static int Apply(NandSession session, string stateDir, out string error)
        {
            error = null;
            try
            {
                var index = Path.Combine(stateDir ?? "", IndexName);
                if (!File.Exists(index)) return 0;

                int written = 0;
                foreach (var line in File.ReadAllLines(index))
                {
                    var parts = line.Split(new[] { '\t' }, 3);
                    if (parts.Length != 3) continue;

                    if (parts[0] == "X")
                    {
                        // Gone in the saved state, so it has to go here too - otherwise the fresh
                        // install's copy would come back as if the game had never deleted it.
                        //
                        // AND A FAILURE HERE IS SAID OUT LOUD. It used to be swallowed, which made
                        // the two halves of this loop disagree: a file that could not be put back
                        // was reported, a file that could not be taken away was not - and the second
                        // is the one the player notices, because something they deleted reappears.
                        if (!session.RemoveFile(parts[2], out var whyNot))
                            Log.Verbose("could not take " + parts[2] + " back out of the NAND - "
                                        + whyNot + "; it was deleted in the saved state and will "
                                        + "come back");
                        continue;
                    }

                    var file = Path.Combine(stateDir, parts[1]);
                    if (!File.Exists(file)) continue;
                    if (session.ImportFile(parts[2], file, out var why)) written++;
                    else Log.Verbose("could not put " + parts[2] + " back into the NAND - " + why);
                }
                return written;
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return -1; }
        }

        /// <summary>Put one file into a saved state, adding it if the state did not carry it.
        ///
        /// This is how a save the HOST edited gets in: the state is what a rebuild puts back, so
        /// writing anywhere else would be writing somewhere the next launch does not read.</summary>
        public static bool Put(string stateDir, string nandPath, string sourceFile, out string error)
        {
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(stateDir) || string.IsNullOrWhiteSpace(nandPath)
                    || sourceFile == null || !File.Exists(sourceFile))
                { error = "nothing to put, or nowhere to put it"; return false; }

                Directory.CreateDirectory(stateDir);
                var flat = FlatName(nandPath);
                File.Copy(sourceFile, Path.Combine(stateDir, flat), overwrite: true);

                // The index is rewritten with this path present exactly once, whatever it held
                // before - including an "X" saying the file had been deleted, which it no longer is.
                var index = Path.Combine(stateDir, IndexName);
                var lines = new List<string>();
                if (File.Exists(index))
                    foreach (var line in File.ReadAllLines(index))
                    {
                        var parts = line.Split(new[] { '\t' }, 3);
                        if (parts.Length == 3 && string.Equals(parts[2], nandPath, StringComparison.Ordinal))
                            continue;
                        lines.Add(line);
                    }
                lines.Add("F\t" + flat + "\t" + nandPath);
                lines.Sort(StringComparer.Ordinal);
                File.WriteAllLines(index, lines);
                return true;
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return false; }
        }

        /// <summary>The state file holding one NAND path, or null. Used to hand the host a save it can
        /// list and restore without knowing any of the above.</summary>
        public static string FileFor(string stateDir, string nandPath)
        {
            try
            {
                var index = Path.Combine(stateDir ?? "", IndexName);
                if (!File.Exists(index)) return null;
                foreach (var line in File.ReadAllLines(index))
                {
                    var parts = line.Split(new[] { '\t' }, 3);
                    if (parts.Length == 3 && parts[0] == "F"
                        && string.Equals(parts[2], nandPath, StringComparison.Ordinal))
                    {
                        var file = Path.Combine(stateDir, parts[1]);
                        return File.Exists(file) ? file : null;
                    }
                }
                return null;
            }
            catch { return null; }
        }
    }
}
