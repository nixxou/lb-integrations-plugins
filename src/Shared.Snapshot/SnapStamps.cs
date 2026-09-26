// What every file of a tree looked like on the outside when its reference was taken - size, write
// time, creation time - so a later walk can tell the files nobody touched from the others without
// opening them.
//
// WHY: the capture at the end of a session hashed the whole tree again, firmware and game included,
// to find the few files a session writes. On the files nobody touched, the reference already holds
// the answer; the question is only which ones those are.
//
// THE RULE: same size, same write time AND same creation time as at the reference - to the tick -
// and the reference's hash is taken as it stands. Anything else is read and hashed.
//   - A write goes through Windows, which moves the write time: the emulator writes, never the game.
//   - A save written as "tmp, then rename over the old one" carries the tmp's times, not the old
//     file's: caught.
//   - Two old files swapped by renames keep their own times, so each now wears the other's: caught,
//     unless both were created and written at the same tick.
// What is NOT caught: something that rewrites a file keeping its size and then puts BOTH times back
// exactly. The Vita API can set a time (sceIoChstat); doing it to the tick, on both, at the same size,
// is not something a game does. That is the one assumption here, and the probe holds the fast walk to
// the full one on every other case.
//
// AND WHAT IS NAMED IS NEVER STAMPED. The caller hands over the paths it knows it wrote after the
// reference - a restored save - and those are left out, so they are always read. They differ from
// the reference by construction, and File.Copy keeps the source's write time: times are not the
// right question for them.
//
// KEPT BESIDE THE REFERENCE, NOT IN IT. The manifest format is shared with the pristine NAND's and is
// compared byte for byte; times would make two identical trees differ. And it is tied to ITS
// reference: the first line names the reference's length and write time, and a stamps file that does
// not match is ignored, never trusted - the walk is then simply the full one.
//
//     #reference <length> <write ticks>
//     <size> <write ticks> <creation ticks> <path>

#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LbIntegrations.Snapshot
{
    internal readonly struct SnapStamp
    {
        public readonly long Size, Written, Created;
        public SnapStamp(long size, long written, long created) { Size = size; Written = written; Created = created; }

        public static SnapStamp Of(FileInfo info)
            => new SnapStamp(info.Length, info.LastWriteTimeUtc.Ticks, info.CreationTimeUtc.Ticks);

        public bool Same(SnapStamp other) => Size == other.Size && Written == other.Written && Created == other.Created;
    }

    internal static class SnapStamps
    {
        /// <summary>The stamps file that goes with a reference manifest.</summary>
        public static string PathFor(string referencePath) => referencePath + ".stamps";

        /// <summary>Stamp every file of <paramref name="root"/> but the <paramref name="leaveOut"/>
        /// ones, tied to the reference already written. Lists folders and opens nothing. False on
        /// failure - which costs only speed later.</summary>
        public static bool Write(string root, string referencePath, IEnumerable<string> leaveOut, out string error)
        {
            var skip = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in leaveOut ?? new string[0])
                if (!string.IsNullOrEmpty(p)) skip.Add(p.Replace('\\', '/').Trim('/'));

            error = null;
            string partial = null;
            var target = PathFor(referencePath);
            try
            {
                var full = Path.GetFullPath(root);
                var reference = new FileInfo(referencePath);
                if (!reference.Exists) { error = "there is no reference to tie the stamps to"; return false; }

                var lines = new List<string>();
                foreach (var info in new DirectoryInfo(full).EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    var key = SnapWalk.Relative(full, info.FullName);
                    if (key == null || skip.Contains(key)) continue;
                    var s = SnapStamp.Of(info);
                    lines.Add(s.Size + "\t" + s.Written + "\t" + s.Created + "\t" + key);
                }
                lines.Sort(StringComparer.Ordinal);

                var text = new StringBuilder();
                text.Append("#reference\t").Append(reference.Length).Append('\t')
                    .Append(reference.LastWriteTimeUtc.Ticks).Append('\n');
                foreach (var line in lines) text.Append(line).Append('\n');

                partial = target + "." + Guid.NewGuid().ToString("N") + ".part";
                File.WriteAllText(partial, text.ToString(), new UTF8Encoding(false));
                if (File.Exists(target)) File.Delete(target);
                File.Move(partial, target);
                partial = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                SnapLog.Warn("could not stamp " + root, ex);
                return false;
            }
            finally { if (partial != null) try { File.Delete(partial); } catch { } }
        }

        /// <summary>The stamps of a reference, or null when there are none or they belong to another
        /// reference than the one on disk now.</summary>
        public static Dictionary<string, SnapStamp> Read(string referencePath)
        {
            try
            {
                var path = PathFor(referencePath);
                if (!File.Exists(path)) return null;
                var reference = new FileInfo(referencePath);
                if (!reference.Exists) return null;

                var lines = File.ReadAllLines(path);
                var expected = "#reference\t" + reference.Length + "\t" + reference.LastWriteTimeUtc.Ticks;
                if (lines.Length == 0 || lines[0] != expected)
                {
                    SnapLog.Info("the stamps do not belong to this reference - walking all of it");
                    return null;
                }

                var stamps = new Dictionary<string, SnapStamp>(StringComparer.Ordinal);
                for (int i = 1; i < lines.Length; i++)
                {
                    var parts = lines[i].Split('\t');
                    if (parts.Length < 4) continue;
                    if (!long.TryParse(parts[0], out var size) || !long.TryParse(parts[1], out var written)
                        || !long.TryParse(parts[2], out var created)) continue;
                    var key = parts.Length == 4 ? parts[3] : string.Join("\t", parts, 3, parts.Length - 3);
                    stamps[key] = new SnapStamp(size, written, created);
                }
                return stamps;
            }
            catch (Exception ex) { SnapLog.Warn("could not read the stamps of " + referencePath, ex); return null; }
        }

        /// <summary>Forget the stamps of a reference - wherever the reference itself is deleted.</summary>
        public static void Delete(string referencePath)
        {
            try { var p = PathFor(referencePath); if (File.Exists(p)) File.Delete(p); } catch { }
        }
    }
}
