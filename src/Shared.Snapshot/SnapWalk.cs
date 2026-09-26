// What a directory tree holds, written down so two of them can be compared.
//
// THE COMPARISON IS BY FILE, NEVER BY BYTES, and that is borrowed rather than invented: the DSi
// engine next door learnt it the expensive way. There the reason was a FAT directory entry carrying
// the wall clock and an allocator placing clusters in the order operations happened, so two identical
// installs produced images differing in raw bytes and manifests that matched exactly. Here the
// surface is an ordinary NTFS tree and the reason is simpler - there is no image to compare - but the
// shape of the answer is the same: a path, a size and a hash, and nothing about when.
//
// DIRECTORIES ARE KEPT, which is where this parts company with DsiDelta. That one drops them
// (DsiDelta.cs:91-94) and says so: an import creates the path it needs, so a directory line would
// never be acted on, and a new empty one is simply missed. Here a game that lays out an empty tree
// and expects to find it again is an ordinary thing, so directories are walked, compared and
// restored like anything else.
//
// The manifest is a text file, one line per entry, sorted ORDINAL - not the filesystem's order, which
// varies by volume and by how the tree was written:
//
//     F <size> <sha1> <path>
//     D -      -      <path>
//
// Tab-separated, paths relative to the root with forward slashes. Size is carried as TEXT so that a
// "?" for something unreadable survives the round trip rather than becoming a zero that compares
// equal to an empty file.

#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LbIntegrations.Snapshot
{
    /// <summary>One line of a manifest.</summary>
    internal sealed class SnapEntry
    {
        public bool IsDirectory;
        public string Size;
        public string Sha1;

        public bool SameAs(SnapEntry other)
            => other != null
               && IsDirectory == other.IsDirectory
               && string.Equals(Size, other.Size, StringComparison.Ordinal)
               && string.Equals(Sha1, other.Sha1, StringComparison.Ordinal);
    }

    internal static class SnapWalk
    {
        /// <summary>Unreadable, as opposed to empty. A file we could not hash is not a file of size
        /// zero, and must not compare equal to one.</summary>
        private const string Unknown = "?";

        /// <summary>Walk <paramref name="root"/> into a dictionary keyed by relative path. Returns
        /// null on failure, never throws.</summary>
        public static Dictionary<string, SnapEntry> Of(string root, out string error)
            => Of(root, out error, null);

        /// <summary>The same walk, reporting how far it is - 0..1, by BYTES hashed rather than by files:
        /// on a game one file is often most of it, and a count would sit at 99% for most of the wait.</summary>
        public static Dictionary<string, SnapEntry> Of(string root, out string error, Action<double> progress)
        {
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                { error = "there is no tree at " + root; return null; }

                var full = Path.GetFullPath(root);
                var entries = new Dictionary<string, SnapEntry>(StringComparer.Ordinal);

                foreach (var dir in Directory.EnumerateDirectories(full, "*", SearchOption.AllDirectories))
                {
                    var key = Relative(full, dir);
                    if (key != null) entries[key] = new SnapEntry { IsDirectory = true, Size = "-", Sha1 = "-" };
                }

                // Listed first, so the total is known before the first byte is hashed.
                var files = new List<FileInfo>();
                long total = 0;
                foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                {
                    var info = new FileInfo(file);
                    files.Add(info);
                    try { total += info.Length; } catch { }
                }

                long done = 0;
                foreach (var info in files)
                {
                    var key = Relative(full, info.FullName);
                    if (key == null) continue;
                    entries[key] = Describe(info.FullName);
                    if (progress != null)
                    {
                        try { done += info.Length; } catch { }
                        try { progress(total > 0 ? (double)done / total : 1.0); } catch { }
                    }
                }

                return entries;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                SnapLog.Warn("could not walk " + root, ex);
                return null;
            }
        }

        /// <summary>Walk a tree and write the manifest. Written beside the target and moved into
        /// place, so an interrupted walk never leaves half a reference - which would read as a tree
        /// that lost most of its files. Returns the entry count, or -1.</summary>
        public static int Write(string root, string manifestPath, out string error)
            => Write(root, manifestPath, out error, null);

        public static int Write(string root, string manifestPath, out string error, Action<double> progress)
        {
            error = null;
            string partial = null;
            try
            {
                var entries = Of(root, out error, progress);
                if (entries == null) return -1;
                return WriteEntries(entries, manifestPath, out error);
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                SnapLog.Warn("could not write the manifest " + manifestPath, ex);
                return -1;
            }
        }

        /// <summary>The manifest text, one writer for Write and WriteFrom so the two cannot drift -
        /// the probe holds them to the same bytes.</summary>
        private static int WriteEntries(Dictionary<string, SnapEntry> entries, string manifestPath, out string error)
        {
            error = null;
            string partial = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(manifestPath)));
                partial = manifestPath + "." + Guid.NewGuid().ToString("N") + ".part";

                var keys = new List<string>(entries.Keys);
                keys.Sort(StringComparer.Ordinal);

                var text = new StringBuilder();
                foreach (var key in keys)
                {
                    var e = entries[key];
                    text.Append(e.IsDirectory ? 'D' : 'F').Append('\t')
                        .Append(e.Size).Append('\t')
                        .Append(e.Sha1).Append('\t')
                        .Append(key).Append('\n');
                }

                File.WriteAllText(partial, text.ToString(), new UTF8Encoding(false));
                if (File.Exists(manifestPath)) File.Delete(manifestPath);
                File.Move(partial, manifestPath);
                partial = null;
                return keys.Count;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                SnapLog.Warn("could not write the manifest " + manifestPath, ex);
                return -1;
            }
            finally { if (partial != null) try { File.Delete(partial); } catch { } }
        }

        /// <summary>The same manifest as Write, for a tree that is a KNOWN BASE plus a few fresh folders
        /// - hashing only the fresh ones.
        ///
        /// WHY: on the tree a session is built on, the base is a copy of a folder already walked once,
        /// and hashing it again cost most of a launch. Measured on a RAM disk, 2253 firmware files:
        ///     walk of files just copied    3985 ms
        ///     the same walk straight after  503 ms
        /// The difference is real-time antivirus scanning each new file on its first open. Not
        /// opening the copies is what saves the time, more than not hashing them.
        ///
        /// EXACT, OR IT FALLS BACK. Every entry of the tree is still ENUMERATED - listing a folder
        /// opens no file and costs nothing - and must be accounted for: under a fresh folder, it is
        /// hashed; elsewhere it must be in the base with the same kind and size, and every base entry
        /// must be there. One mismatch - a leftover file, a missing one, a size that changed - and the
        /// whole tree is walked the slow way instead. A file this skipped would otherwise come out of
        /// the next session as a "change", straight into somebody's save.
        ///
        /// Returns the entry count, or -1; <paramref name="hashed"/> says how many files were read,
        /// and -1 when it fell back.</summary>
        public static int WriteFrom(string root, string baseManifest, IEnumerable<string> freshFolders,
                                    string manifestPath, out string error, Action<double> progress, out int hashed)
        {
            error = null;
            hashed = -1;
            try
            {
                var based = Read(baseManifest);
                if (based.Count == 0) return Write(root, manifestPath, out error, progress);

                var full = Path.GetFullPath(root);
                var fresh = new List<string>();
                foreach (var f in freshFolders ?? new string[0])
                    if (!string.IsNullOrWhiteSpace(f)) fresh.Add(f.Replace('\\', '/').Trim('/'));

                bool IsFresh(string key)
                {
                    foreach (var f in fresh)
                        if (key == f || key.StartsWith(f + "/", StringComparison.Ordinal)) return true;
                    return false;
                }

                // A fresh folder's ancestors ("ux0/app" above "ux0/app/PCSE00965") may be new too.
                bool IsAncestorOfFresh(string key)
                {
                    foreach (var f in fresh)
                        if (f.StartsWith(key + "/", StringComparison.Ordinal)) return true;
                    return false;
                }

                var entries = new Dictionary<string, SnapEntry>(StringComparer.Ordinal);
                var toHash = new List<(string key, string file, long size)>();
                string why = null;

                foreach (var dir in Directory.EnumerateDirectories(full, "*", SearchOption.AllDirectories))
                {
                    var key = Relative(full, dir);
                    if (key == null) continue;
                    if (!IsFresh(key) && !IsAncestorOfFresh(key)
                        && !(based.TryGetValue(key, out var b) && b.IsDirectory))
                    { why = "a folder the base does not have: " + key; break; }
                    entries[key] = new SnapEntry { IsDirectory = true, Size = "-", Sha1 = "-" };
                }

                if (why == null)
                    foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                    {
                        var key = Relative(full, file);
                        if (key == null) continue;
                        long size = new FileInfo(file).Length;
                        if (IsFresh(key)) { toHash.Add((key, file, size)); continue; }

                        if (!based.TryGetValue(key, out var b) || b.IsDirectory)
                        { why = "a file the base does not have: " + key; break; }
                        if (b.Size != size.ToString())
                        { why = "a size that is not the base's: " + key; break; }
                        entries[key] = b;
                    }

                if (why == null)
                    foreach (var pair in based)
                        if (!entries.ContainsKey(pair.Key) && !IsFresh(pair.Key))
                        { why = "the base has it and the tree does not: " + pair.Key; break; }

                if (why != null)
                {
                    SnapLog.Info("the tree is not the base plus the fresh folders (" + why + ") - walking all of it");
                    return Write(root, manifestPath, out error, progress);
                }

                long total = 0, done = 0;
                foreach (var t in toHash) total += t.size;
                foreach (var t in toHash)
                {
                    entries[t.key] = Describe(t.file);
                    done += t.size;
                    if (progress != null) { try { progress(total > 0 ? (double)done / total : 1.0); } catch { } }
                }

                hashed = toHash.Count;
                return WriteEntries(entries, manifestPath, out error);
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                SnapLog.Warn("could not write the manifest from its base " + manifestPath, ex);
                return -1;
            }
        }

        /// <summary>Read a manifest back. An empty or missing one gives an EMPTY dictionary rather
        /// than null: the caller decides whether "nothing to compare against" is a failure, and for a
        /// reference it always is.</summary>
        public static Dictionary<string, SnapEntry> Read(string manifestPath)
        {
            var entries = new Dictionary<string, SnapEntry>(StringComparer.Ordinal);
            try
            {
                if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath)) return entries;
                foreach (var line in File.ReadAllLines(manifestPath))
                {
                    if (line.Length == 0) continue;
                    var parts = line.Split('\t');
                    if (parts.Length < 4) continue;
                    // The path may itself contain a tab; everything from field 4 on belongs to it.
                    var path = parts.Length == 4 ? parts[3] : string.Join("\t", parts, 3, parts.Length - 3);
                    entries[path] = new SnapEntry
                    {
                        IsDirectory = parts[0] == "D",
                        Size = parts[1],
                        Sha1 = parts[2],
                    };
                }
            }
            catch (Exception ex) { SnapLog.Warn("could not read the manifest " + manifestPath, ex); }
            return entries;
        }

        /// <summary>Size and SHA-1 of one file. Unreadable gives "?" for both rather than throwing:
        /// a tree with one locked file in it is still worth walking.</summary>
        private static SnapEntry Describe(string file)
        {
            try
            {
                var length = new FileInfo(file).Length.ToString();
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sha1 = SHA1.Create();
                return new SnapEntry
                {
                    IsDirectory = false,
                    Size = length,
                    Sha1 = Convert.ToHexString(sha1.ComputeHash(stream)),
                };
            }
            catch (Exception ex)
            {
                SnapLog.Warn("could not read " + file, ex);
                return new SnapEntry { IsDirectory = false, Size = Unknown, Sha1 = Unknown };
            }
        }

        /// <summary>A path relative to the root, with forward slashes. Null when it escapes the root,
        /// which a reparse point can arrange.</summary>
        internal static string Relative(string root, string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
                var rest = full.Substring(root.Length).TrimStart('\\', '/');
                return rest.Length == 0 ? null : rest.Replace('\\', '/');
            }
            catch { return null; }
        }
    }
}
