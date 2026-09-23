// How a DSiWare save survives the loss of the console it was made on.
//
// A SAVE IS A DELTA, so it is bound to a base image. That is the whole design - see MelonDsDelta -
// and it is what makes a library of DSiWare cost 480 MB instead of 240 MB a game. The price is that
// a save means nothing without the image it was measured against, and until now that image was one
// file on one machine. Lose it - a reinstall, a new PC, a console reconfigured, a file deleted - and
// every DSiWare save becomes unapplicable. Not broken, not reported: they simply stop meaning
// anything, silently.
//
// THREE THINGS MAKE UP A CONSOLE, and only one of them is irreplaceable:
//
//     the ORIGINAL   a pristine dump, kept as <nand>.lock        the user has it; it never changes
//     the RECIPE     original -> initial, a few dozen kilobytes  we can compute it
//     the INITIAL    the original plus its console setup         DERIVED: rebuildable from the two
//
// So a save carries the recipe and the original's identity, and can rebuild its own base from a dump
// anybody can re-obtain. What it cannot do is cross consoles: a NAND is encrypted with its console's
// own id, so "the same original" means literally the same file. That is said in the error message
// rather than worked around.
//
// THE RECIPE IS ITSELF A DELTA, in exactly the format a save uses - files.txt with its F and X rows,
// plus the flat files. MelonDsDelta.Capture and Apply are reused verbatim, with the original as the
// reference instead of a fresh install. Measured on a real pair: the console setup touches
// shared1/TWLCFG0.dat, TWLCFG1.dat and the launcher's private.sav. Tens of kilobytes.
//
// THE IDENTITY IS DERIVED, NEVER ASSIGNED. It is a hash of the entries the recipe names, READ OUT OF
// THE IMAGE - not out of the recipe. Three consequences, each of which killed an alternative:
// nothing is written inside the NAND, so there is no foreign directory whose failure mode would be
// "an image that no longer boots"; it cannot go stale, unlike a token dropped beside the file, which
// survives the file being swapped; and two byte-identical initials are correctly recognised as the
// same, where a random GUID would have forced a pointless recovery.
//
// AND THE RECIPE IS A ZIP, WHICH IS NOT A PREFERENCE. MelonDsSaves.TryBackupSave and
// MelonDsDsi.RestoreSave both copy Directory.GetFiles - top level only, no recursion. A recipe
// FOLDER inside a state folder would be lost at the first backup and absent at the restore. One file
// survives both without a line of code.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace LbIntegrations.MelonDs
{
    /// <summary>What a save records about the console it was made on.</summary>
    internal sealed class BaseRecord
    {
        /// <summary>The derived identity of the initial image. The authority.</summary>
        public string Identity;

        /// <summary>The initial image as it was then - the cheap check, free from a FileInfo.</summary>
        public string InitialName;
        public long InitialSize;
        public long InitialTicks;

        /// <summary>The original dump, so it can be found again among the user's files.</summary>
        public string OriginalName;
        public long OriginalSize;
        public string OriginalSha256;
    }

    internal static class MelonDsBase
    {
        /// <summary>Beside the image it describes, so moving or renaming a base takes its recipe
        /// with it.</summary>
        public const string RecipeSuffix = ".recipe.zip";

        /// <summary>Inside a save's state folder. Two files rather than one, so the identity can be
        /// read on every launch without opening a zip.</summary>
        public const string RecipeInState = "base.zip";
        public const string RecordInState = "base.txt";

        /// <summary>Rebuilt initials live here, under dsi\, because they are a CACHE and have no
        /// business in the user's dump folder - and because the NAND sweep only looks in the BIOS
        /// folders, so nothing here can ever be mistaken for an original.</summary>
        public const string BasesDirName = "bases";

        private const string KillSwitch = "no-dsi-base";

        /// <summary>The record kept BESIDE a base, so a capture copies two files instead of
        /// walking a 240 MB image to work out what it already knows.</summary>
        public const string RecordSuffix = ".recipe.txt";

        public static string RecipeFor(string imagePath)
            => imagePath == null ? null : imagePath + RecipeSuffix;

        public static string RecordFor(string imagePath)
            => imagePath == null ? null : imagePath + RecordSuffix;

        /// <summary>Does this base carry everything a save needs to describe it?</summary>
        public static bool Described(string imagePath)
        {
            try
            {
                return imagePath != null
                       && File.Exists(RecipeFor(imagePath)) && File.Exists(RecordFor(imagePath));
            }
            catch { return false; }
        }

        public static string BasesDir(MelonDsLayout layout)
        {
            var dir = MelonDsDsi.DsiDir(layout);
            return dir == null ? null : Path.Combine(dir, BasesDirName);
        }

        public static string ArchiveFor(MelonDsLayout layout, string identity)
        {
            var dir = BasesDir(layout);
            return dir == null || string.IsNullOrWhiteSpace(identity)
                ? null : Path.Combine(dir, identity + ".bin");
        }

        /// <summary>Is this one of our rebuilt bases? Such an image is configured BY CONSTRUCTION,
        /// so it must never be offered for first-use setup - see MelonDsNandSetup.Of.</summary>
        public static bool IsArchive(string imagePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imagePath)) return false;
                var parent = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(imagePath)));
                return string.Equals(parent, BasesDirName, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // ── making a recipe ──────────────────────────────────────────────────

        /// <summary>Write the delta that turns <paramref name="originalPath"/> into
        /// <paramref name="initialPath"/>, as a zip beside the initial.
        ///
        /// THE ONE MOMENT BOTH IMAGES EXIST is when the first-use flow renames the backup copy to
        /// .lock, so that is where this is called from. An installation locked before this existed
        /// still has both, so it can be caught up later - once.</summary>
        public static bool MakeRecipe(string originalPath, string initialPath, string bios7Path,
                                      out string error)
        {
            error = null;
            string scratch = null;
            try
            {
                if (Log.Disabled(KillSwitch)) { error = "switched off by the " + KillSwitch + " marker"; return false; }
                if (!File.Exists(originalPath)) { error = "no original to compare against"; return false; }
                if (!File.Exists(initialPath)) { error = "no configured image to describe"; return false; }
                if (!MelonDsNand.IsUsable(out var why)) { error = why; return false; }

                scratch = Path.Combine(Path.GetTempPath(), "lbip-recipe-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(scratch);

                // The original's walk, which is the reference the delta is measured against.
                var reference = Path.Combine(scratch, MelonDsDelta.ReferenceName);
                using (var pristine = MelonDsNand.Open(originalPath, bios7Path, out var openError))
                {
                    if (pristine == null) { error = openError; return false; }
                    if (pristine.Walk(reference, out var walkError) < 0) { error = walkError; return false; }
                }

                // Then the configured image, compared against it. Capture REPLACES its output folder
                // and deletes it recursively, so it is pointed at a folder of its own making.
                var built = Path.Combine(scratch, "built");
                int kept;
                using (var configured = MelonDsNand.Open(initialPath, bios7Path, out var openError))
                {
                    if (configured == null) { error = openError; return false; }
                    kept = MelonDsDelta.Capture(configured, reference, built, scratch, out var captureError);
                    if (kept < 0) { error = captureError; return false; }
                }

                var target = RecipeFor(initialPath);
                var partial = target + ".part";
                try { if (File.Exists(partial)) File.Delete(partial); } catch { }
                ZipFile.CreateFromDirectory(built, partial, CompressionLevel.Optimal, false);
                if (File.Exists(target)) File.Delete(target);
                File.Move(partial, target);

                // And the record beside it, computed once here rather than at every capture.
                var identity = IdentityOf(initialPath, bios7Path, PathsIn(target));
                if (identity == null) { error = "the configured image could not be identified"; return false; }
                WriteRecord(RecordFor(initialPath), Describe(initialPath, originalPath, identity));

                Log.Info("wrote the recipe for " + Path.GetFileName(initialPath) + ": " + kept
                         + " file(s) of console setup, " + new FileInfo(target).Length
                         + " bytes, console " + Short(identity) + ". A save made on this console can "
                         + "now rebuild it from " + Path.GetFileName(originalPath) + " alone.");
                return true;
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return false; }
            finally { try { if (scratch != null && Directory.Exists(scratch)) Directory.Delete(scratch, true); } catch { } }
        }

        /// <summary>The NAND paths a recipe names, F and X alike. This is the list the identity is
        /// computed over - the recipe says WHICH files matter, the image says what is in them.</summary>
        public static List<string> PathsIn(string recipeZip)
        {
            var paths = new List<string>();
            try
            {
                if (!File.Exists(recipeZip)) return paths;
                using var zip = ZipFile.OpenRead(recipeZip);
                var entry = zip.GetEntry(MelonDsDelta.IndexName);
                if (entry == null) return paths;

                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var parts = line.Split(new[] { '\t' }, 3);
                    if (parts.Length == 3 && parts[2].Length > 0) paths.Add(parts[2]);
                }
            }
            catch (Exception ex) { Log.Verbose("could not read " + recipeZip + " - " + ex.Message); }
            paths.Sort(StringComparer.Ordinal);
            return paths;
        }

        // ── the identity ─────────────────────────────────────────────────────

        /// <summary>What identifies this image as a base: a hash over the entries the recipe names,
        /// READ OUT OF THE IMAGE. Null when it cannot be computed, which is never an answer of
        /// "different" - see the callers.</summary>
        public static string IdentityOf(string imagePath, string bios7Path, List<string> paths)
        {
            string scratch = null;
            try
            {
                if (paths == null || paths.Count == 0) return null;
                if (!File.Exists(imagePath)) return null;
                if (!MelonDsNand.IsUsable(out _)) return null;

                scratch = Path.Combine(Path.GetTempPath(), "lbip-identity-" + Guid.NewGuid().ToString("N") + ".txt");
                using (var session = MelonDsNand.Open(imagePath, bios7Path, out var openError))
                {
                    if (session == null)
                    {
                        Log.Verbose("could not open " + Path.GetFileName(imagePath) + " - " + openError);
                        return null;
                    }
                    if (session.Walk(scratch, out var walkError) < 0)
                    {
                        Log.Verbose("could not walk " + Path.GetFileName(imagePath) + " - " + walkError);
                        return null;
                    }
                }

                var walk = MelonDsDelta.Read(scratch);
                var lines = new List<string>();
                foreach (var path in paths)
                {
                    // A path the recipe names and the image does not have is as much a part of the
                    // identity as one it does - that is exactly what an X row describes.
                    lines.Add(walk.TryGetValue(path, out var entry)
                        ? path + "\t" + entry.Size + "\t" + entry.Sha1
                        : path + "\t-\t-");
                }
                return Sha256Of(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
            }
            catch (Exception ex) { Log.Verbose("could not identify a base - " + ex.Message); return null; }
            finally { try { if (scratch != null && File.Exists(scratch)) File.Delete(scratch); } catch { } }
        }

        /// <summary>The same, through dsi\identities.txt.
        ///
        /// A SEPARATE FILE FROM nands.txt, deliberately. That one's parser takes everything after the
        /// stamp and hands it to Enum.TryParse, so a fourth field would fail to parse and force a
        /// full re-scan on every launch - silently. Same shape, own file, no interaction.</summary>
        public static string CachedIdentity(MelonDsLayout layout, string imagePath, string bios7Path,
                                            List<string> paths)
        {
            try
            {
                if (!File.Exists(imagePath)) return null;

                var info = new FileInfo(imagePath);
                var stamp = info.Length.ToString(CultureInfo.InvariantCulture) + "\t"
                          + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);

                var known = ReadIndex(layout);
                if (known.TryGetValue(info.FullName, out var remembered)
                    && remembered.StartsWith(stamp + "\t", StringComparison.Ordinal))
                    return remembered.Substring(stamp.Length + 1);

                var identity = IdentityOf(imagePath, bios7Path, paths);
                if (identity == null) return null;

                known[info.FullName] = stamp + "\t" + identity;
                WriteIndex(layout, known);
                return identity;
            }
            catch (Exception ex) { Log.Verbose("could not cache an identity - " + ex.Message); return null; }
        }

        // ── rebuilding ───────────────────────────────────────────────────────

        /// <summary>Copy the original and apply the recipe, then CHECK the result is what was asked
        /// for. The check costs the files the recipe names and turns "we think we rebuilt the right
        /// base" into "we rebuilt the right base".</summary>
        public static bool RebuildFrom(string originalPath, string recipeZip, string targetPath,
                                       string bios7Path, string expected, out string error)
        {
            error = null;
            string unpacked = null;
            try
            {
                if (!File.Exists(originalPath)) { error = "no original to rebuild from"; return false; }
                if (!File.Exists(recipeZip)) { error = "no recipe to apply"; return false; }
                if (!MelonDsNand.IsUsable(out var why)) { error = why; return false; }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

                unpacked = Path.Combine(Path.GetTempPath(), "lbip-apply-" + Guid.NewGuid().ToString("N"));
                ZipFile.ExtractToDirectory(recipeZip, unpacked);

                // Through a temporary name: a 240 MB copy interrupted halfway must not leave
                // something that looks like a finished base.
                var partial = targetPath + ".part";
                try { if (File.Exists(partial)) File.Delete(partial); } catch { }
                File.Copy(originalPath, partial, overwrite: true);

                using (var session = MelonDsNand.Open(partial, bios7Path, out var openError))
                {
                    if (session == null) { error = openError; return false; }
                    if (MelonDsDelta.Apply(session, unpacked, out var applyError) < 0)
                    { error = applyError; return false; }
                }

                var got = IdentityOf(partial, bios7Path, PathsIn(recipeZip));
                if (got == null) { error = "the rebuilt image could not be identified"; return false; }
                if (!string.Equals(got, expected, StringComparison.Ordinal))
                {
                    error = "the rebuilt image is not the one asked for - wanted " + Short(expected)
                          + ", got " + Short(got);
                    return false;
                }

                if (File.Exists(targetPath)) File.Delete(targetPath);
                File.Move(partial, targetPath);

                // Beside the base, so the capture path has no branch: the recipe and its record
                // are next to whatever base was used, always.
                try { File.Copy(recipeZip, RecipeFor(targetPath), overwrite: true); } catch { }
                try
                {
                    var record = Describe(targetPath, originalPath, expected);
                    WriteRecord(RecordFor(targetPath), record);
                }
                catch { }

                Log.Info("rebuilt the console " + Short(expected) + " from "
                         + Path.GetFileName(originalPath) + " and its recipe, and checked it");
                return true;
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return false; }
            finally
            {
                try { if (unpacked != null && Directory.Exists(unpacked)) Directory.Delete(unpacked, true); } catch { }
                try { var p = targetPath + ".part"; if (File.Exists(p)) File.Delete(p); } catch { }
            }
        }

        /// <summary>Find the original dump among the user's files: by name, then by size, then by
        /// hash. The hash reads 240 MB, so it is only ever reached on a recovery, and only for a
        /// candidate whose size already matched.</summary>
        public static string FindOriginal(IEnumerable<string> candidates, BaseRecord want)
        {
            try
            {
                if (want == null) return null;

                var sized = new List<string>();
                foreach (var path in candidates ?? Array.Empty<string>())
                {
                    try
                    {
                        var info = new FileInfo(path);
                        if (!info.Exists || info.Length != want.OriginalSize) continue;
                        sized.Add(path);
                    }
                    catch { }
                }
                if (sized.Count == 0) return null;

                // The one with the right name first: it is very probably the file, and checking it
                // first usually spares every other hash.
                sized.Sort((a, b) =>
                {
                    bool na = string.Equals(Path.GetFileName(a), want.OriginalName, StringComparison.OrdinalIgnoreCase);
                    bool nb = string.Equals(Path.GetFileName(b), want.OriginalName, StringComparison.OrdinalIgnoreCase);
                    if (na != nb) return na ? -1 : 1;
                    return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                });

                foreach (var path in sized)
                {
                    var hash = Sha256OfFile(path);
                    if (hash == null) continue;
                    if (string.Equals(hash, want.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Info("found the original dump this save was built on: " + Path.GetFileName(path));
                        return path;
                    }
                    Log.Verbose(Path.GetFileName(path) + " is the right size but not the right file");
                }
                return null;
            }
            catch (Exception ex) { Log.Warn("could not look for the original dump", ex); return null; }
        }

        // ── the record a save carries ────────────────────────────────────────

        public static BaseRecord Describe(string initialPath, string originalPath, string identity)
        {
            try
            {
                var initial = new FileInfo(initialPath);
                var original = originalPath == null ? null : new FileInfo(originalPath);
                return new BaseRecord
                {
                    Identity = identity,
                    InitialName = initial.Name,
                    InitialSize = initial.Length,
                    InitialTicks = initial.LastWriteTimeUtc.Ticks,
                    OriginalName = original?.Name,
                    OriginalSize = original?.Length ?? 0,
                    OriginalSha256 = original == null ? null : Sha256OfFile(original.FullName),
                };
            }
            catch (Exception ex) { Log.Verbose("could not describe a base - " + ex.Message); return null; }
        }

        public static void WriteRecord(string path, BaseRecord record)
        {
            try
            {
                if (path == null || record == null) return;
                var lines = new[]
                {
                    "identity\t" + (record.Identity ?? ""),
                    "initial.name\t" + (record.InitialName ?? ""),
                    "initial.size\t" + record.InitialSize.ToString(CultureInfo.InvariantCulture),
                    "initial.ticks\t" + record.InitialTicks.ToString(CultureInfo.InvariantCulture),
                    "original.name\t" + (record.OriginalName ?? ""),
                    "original.size\t" + record.OriginalSize.ToString(CultureInfo.InvariantCulture),
                    "original.sha256\t" + (record.OriginalSha256 ?? ""),
                };
                MelonDsToml.WriteAtomicBytes(path, Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n"));
            }
            catch (Exception ex) { Log.Verbose("could not write " + path + " - " + ex.Message); }
        }

        /// <summary>What a save says about its base, or null. NULL IS NOT AN ERROR: a save made
        /// before any of this existed has no record, and the only honest answer to "is this the right
        /// base" is then "no opinion" - the same rule work.sum follows.</summary>
        public static BaseRecord ReadRecord(string path)
        {
            try
            {
                if (path == null || !File.Exists(path)) return null;
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(path))
                {
                    var parts = line.Split(new[] { '\t' }, 2);
                    if (parts.Length == 2) map[parts[0].Trim()] = parts[1].Trim();
                }
                if (!map.TryGetValue("identity", out var identity) || identity.Length == 0) return null;

                return new BaseRecord
                {
                    Identity = identity,
                    InitialName = Get(map, "initial.name"),
                    InitialSize = Number(map, "initial.size"),
                    InitialTicks = Number(map, "initial.ticks"),
                    OriginalName = Get(map, "original.name"),
                    OriginalSize = Number(map, "original.size"),
                    OriginalSha256 = Get(map, "original.sha256"),
                };
            }
            catch (Exception ex) { Log.Verbose("could not read " + path + " - " + ex.Message); return null; }
        }

        private static string Get(Dictionary<string, string> map, string key)
            => map.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

        private static long Number(Dictionary<string, string> map, string key)
            => map.TryGetValue(key, out var value)
               && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

        // ── the cache ────────────────────────────────────────────────────────

        private const string IndexName = "identities.txt";

        private static string IndexPath(MelonDsLayout layout)
        {
            var dir = MelonDsDsi.DsiDir(layout);
            return dir == null ? null : Path.Combine(dir, IndexName);
        }

        private static Dictionary<string, string> ReadIndex(MelonDsLayout layout)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var path = IndexPath(layout);
                if (path == null || !File.Exists(path)) return map;
                foreach (var line in File.ReadAllLines(path))
                {
                    var parts = line.Split(new[] { '\t' }, 2);
                    if (parts.Length == 2) map[parts[0]] = parts[1];
                }
            }
            catch (Exception ex) { Log.Verbose("could not read " + IndexName + " - " + ex.Message); }
            return map;
        }

        private static void WriteIndex(MelonDsLayout layout, Dictionary<string, string> entries)
        {
            try
            {
                var path = IndexPath(layout);
                if (path == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                var lines = new List<string>();
                foreach (var pair in entries) lines.Add(pair.Key + "\t" + pair.Value);
                lines.Sort(StringComparer.OrdinalIgnoreCase);
                File.WriteAllLines(path, lines);
            }
            catch (Exception ex) { Log.Verbose("could not write " + IndexName + " - " + ex.Message); }
        }

        // ── hashing ──────────────────────────────────────────────────────────

        public static string Sha256OfFile(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var sha = SHA256.Create();
                return Hex(sha.ComputeHash(stream));
            }
            catch (Exception ex) { Log.Verbose("could not hash " + path + " - " + ex.Message); return null; }
        }

        private static string Sha256Of(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return Hex(sha.ComputeHash(bytes));
        }

        private static string Hex(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) text.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        /// <summary>The first eight characters, for a log line. Whole hashes make a log unreadable
        /// and nobody compares them by eye anyway.</summary>
        public static string Short(string identity)
            => identity == null ? "(none)" : identity.Length <= 8 ? identity : identity.Substring(0, 8);
    }
}
