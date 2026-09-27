// Installing a .vpk into the virtual Vita filesystem, ourselves.
//
// WHY OURSELVES, WHEN VITA3K CAN DO IT. Because it cannot do it WITHOUT ALSO PLAYING THE GAME.
// Measured in main.cpp:218-250: a .vpk, .zip or content folder handed to the emulator is installed,
// the first piece of content whose category is "gd" is found, and cfg.run_app_path is set to its
// title id - so it boots. There is no flag to stop after installing; even --console ends at
// MainWindow and app.exec().
//
// And the whole point of the disposable NAND is that the baseline walk happens BETWEEN the install
// and the session. A game that has already started has already written to its save directory, and
// whatever it wrote would be indistinguishable from the install.
//
// SO WE DO THE INSTALL, AND IT IS SMALL. A .vpk is a zip. Its sce_sys/param.sfo carries TITLE_ID and
// CATEGORY, and those two decide the destination - the table is interface.cpp:112-134:
//
//     gd (or anything else)   ux0/app/<TITLE_ID>            the game
//     gp                      ux0/patch/<TITLE_ID>          an update, and the app must exist first
//     ac                      ux0/addcont/<TITLE_ID>/<ID>   downloadable content
//
// That is also exactly where updates and DLC will plug in later, which is the second reason to own
// this step rather than borrow it.
//
// NOT HANDLED, and said rather than silently mishandled: themes (category ac with a theme.xml),
// .pkg archives, which need a zRIF key, and .vci. Each is a different shape of install.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LbIntegrations.Psf;
using SharpCompress.Archives;
using LbIntegrations.Snapshot;

namespace LbIntegrations.Vita3k
{
    /// <summary>What an archive says it is.</summary>
    internal sealed class VitaContent
    {
        public string TitleId;
        public string Category;
        public string ContentId;
        public string Title;

        /// <summary>TITLE - the full title, where Title prefers STITLE, the short one.</summary>
        public string FullTitle;

        /// <summary>APP_VER - the version an update brings the game to ("01.22"). VERSION is not it:
        /// that is the version of the param.sfo itself, 01.00 on every update measured.</summary>
        public string AppVer;

        /// <summary>The path inside the archive that the content starts at - "" when param.sfo is at
        /// sce_sys/param.sfo, or "foo/" when the archive wraps everything in a folder.</summary>
        public string Root = "";

        /// <summary>The folders the install wrote into, relative to the virtual filesystem - what the
        /// reference walk has to hash, everything else being the pristine base. See
        /// SnapWalk.WriteFrom.</summary>
        public List<string> Written = new List<string>();

        /// <summary>Every file the install wrote, keyed like a manifest (relative to the virtual
        /// filesystem, forward slashes), with the size and SHA-1 of the bytes AS THEY WERE WRITTEN -
        /// by the unzip, by the decrypt (native, hashed on the way to the disk) and for the licence.
        /// The reference walk takes these instead of reading the files back: reading a file just
        /// written is what real-time antivirus makes expensive. A file missing here is simply read.</summary>
        public Dictionary<string, SnapEntry> Hashed = new Dictionary<string, SnapEntry>(StringComparer.Ordinal);

        public bool IsGame => !IsPatch && !IsAddon;
        public bool IsPatch => (Category ?? "").Contains("gp", StringComparison.OrdinalIgnoreCase);
        public bool IsAddon => string.Equals(Category, "ac", StringComparison.OrdinalIgnoreCase);

        public override string ToString()
            => (TitleId ?? "?") + " [" + (Category ?? "?") + "]" + (Title != null ? " " + Title : "");
    }

    internal static class Vita3kContent
    {
        private const string SfoPath = "sce_sys/param.sfo";

        /// <summary>Read what an archive holds, WITHOUT unpacking it. Null when it is not something we
        /// know how to install.</summary>
        public static VitaContent Describe(string archivePath, out string error)
        {
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
                { error = "there is no archive at " + archivePath; return null; }

                using var archive = ArchiveFactory.Open(archivePath);
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;
                    var key = (entry.Key ?? "").Replace('\\', '/');
                    if (!key.EndsWith(SfoPath, StringComparison.OrdinalIgnoreCase)) continue;

                    // The content starts where sce_sys/ does, whatever wraps it.
                    var root = key.Substring(0, key.Length - SfoPath.Length);

                    using var stream = entry.OpenEntryStream();
                    using var memory = new MemoryStream();
                    stream.CopyTo(memory);
                    var sfo = ParamSfo.Parse(memory.ToArray());
                    if (sfo == null) { error = "the param.sfo in " + Path.GetFileName(archivePath) + " is malformed"; return null; }

                    var content = new VitaContent
                    {
                        Root = root,
                        TitleId = sfo.FirstString("TITLE_ID"),
                        Category = sfo.FirstString("CATEGORY"),
                        ContentId = sfo.FirstString("CONTENT_ID"),
                        Title = sfo.FirstString("STITLE", "TITLE"),
                        FullTitle = sfo.FirstString("TITLE", "STITLE"),
                        AppVer = sfo.FirstString("APP_VER"),
                    };
                    if (string.IsNullOrWhiteSpace(content.TitleId))
                    { error = "the param.sfo carries no TITLE_ID"; return null; }
                    return content;
                }

                error = Path.GetFileName(archivePath) + " has no " + SfoPath + " in it - not a .vpk";
                return null;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                Log.Warn("could not read " + archivePath, ex);
                return null;
            }
        }

        /// <summary>Where a piece of content belongs under the virtual filesystem, relative to it.
        /// Null when we do not install this kind.</summary>
        public static string DestinationFor(VitaContent content, out string why)
        {
            why = null;
            if (content == null) { why = "nothing to place"; return null; }

            if (content.IsPatch) return "ux0/patch/" + content.TitleId;

            if (content.IsAddon)
            {
                // Vita3K keeps only the tail of CONTENT_ID, from index 20 - the part before it
                // repeats the title id and the region.
                var id = content.ContentId ?? "";
                if (id.Length <= 20) { why = "this add-on carries no usable CONTENT_ID"; return null; }
                return "ux0/addcont/" + content.TitleId + "/" + id.Substring(20);
            }

            return "ux0/app/" + content.TitleId;
        }

        /// <summary>Unpack an archive into the virtual filesystem. Returns what was installed, or
        /// null with a reason.
        ///
        /// A PATCH REFUSES TO GO IN WITHOUT ITS APP, which is Vita3K's own rule ("Install app before
        /// patch"): a patch tree on its own is not a game, and installing it would leave something
        /// that looks playable and is not.</summary>
        public static VitaContent Install(string archivePath, string vitaFs, out string error)
            => Install(archivePath, vitaFs, out error, null);

        /// <summary>The same install, saying what it is doing - see Vita3kProgressWindow.</summary>
        public static VitaContent Install(string archivePath, string vitaFs, out string error,
                                          Action<string, double?> report)
        {
            error = null;
            try
            {
                var content = Describe(archivePath, out error);
                if (content == null) return null;

                var relative = DestinationFor(content, out var why);
                if (relative == null) { error = why; return null; }

                var destination = Path.Combine(vitaFs, relative.Replace('/', Path.DirectorySeparatorChar));

                if (content.IsPatch)
                {
                    var app = Path.Combine(vitaFs, "ux0", "app", content.TitleId);
                    if (!Directory.Exists(app) || !Directory.EnumerateFileSystemEntries(app).Any())
                    { error = "install the game before its update (" + content.TitleId + ")"; return null; }
                }

                int files;
                if (HasPfs(archivePath, content.Root))
                {
                    // ENCRYPTED: unpacked whole into a staging copy - sce_pfs/ and the licence are
                    // the decryptor's input - then decrypted INTO the destination, and the staging
                    // copy dropped. Vita3K does the same with an "_dec" folder it renames; decrypting
                    // straight into place is the same result without the rename.
                    //
                    // THE STAGING COPY LIVES BESIDE THE TREE, ON THE SAME VOLUME - on the RAM disk when
                    // the session is on one. It used to go to %TEMP%, which meant writing the whole
                    // game to the system disk at every launch of an encrypted one: exactly what the
                    // RAM disk is there to avoid. The reason it had to was psvpfsparser mapping each
                    // source file to its destination with std::filesystem::relative, which resolves
                    // real paths - and an ImDisk volume answers that with ERROR_INVALID_FUNCTION:
                    //     weakly_canonical: Fonction incorrecte.: "Z:/fs/ux0/app/PCSE00965.pfs/sce_sys"
                    // Our copy of that code now computes the path as a string (tools\vita3k-install\
                    // pfs\Utils.cpp), so the source can sit on the RAM disk like everything else.
                    //
                    // AND IT IS SPENT AS IT GOES: each encrypted file is deleted the moment its
                    // decrypted copy is finished, so the two never both hold the whole game - the peak
                    // is the game plus its largest file. See WorkingSizeBytes.
                    //
                    // AND BEFORE ALL THAT, NO COPY AT ALL. The first attempt reads the encrypted app
                    // straight from its zip: the stage holds the index and empty sparse placeholders,
                    // and the native side serves every read from the archive (tools\vita3k-install\
                    // zipsource.h). The RAM disk then holds the decrypted game and nothing else. The
                    // unpack below is the fallback, for an archive that cannot be read that way.
                    var stagingRoot = Path.Combine(StagingParent(vitaFs), StagingPrefix + Guid.NewGuid().ToString("N"));
                    var staging = Path.Combine(stagingRoot, content.TitleId);
                    var prefix = relative.Replace('\\', '/').TrimEnd('/') + "/";
                    Action<string, long, string> onFile = (path, size, sha1) => content.Hashed[prefix + path] = Entry(size, sha1);
                    Directory.CreateDirectory(staging);
                    try
                    {
                        bool done = false;
                        // An update carries NO licence of its own (measured: no work.bin in it) - it
                        // runs under the app's, installed just before it.
                        if (IsZip(archivePath)
                            && (ArchiveHas(archivePath, content.Root + "sce_sys/package/work.bin") || InstalledLicence(content, vitaFs) != null)
                            && Environment.GetEnvironmentVariable(NoZipVariable) != "1")
                        {
                            // The licence the stage WILL hold: the native side extracts the package
                            // folder for real before it decrypts anything.
                            var licence = InstalledLicence(content, vitaFs)
                                          ?? Path.Combine(staging, "sce_sys", "package", "work.bin");
                            report?.Invoke("Decrypting " + Name(content) + "...", 0);
                            done = Vita3kNative.DecryptFromZip(archivePath, content.Root, staging, licence, destination,
                                                               f => report?.Invoke(null, f), onFile, out error);
                            if (!done)
                            {
                                Log.Warn("could not decrypt " + Name(content) + " from its zip (" + error
                                         + ") - unpacking it first instead");
                                content.Hashed.Clear();
                                TryDelete(staging);
                                Directory.CreateDirectory(staging);
                            }
                        }

                        if (!done)
                        {
                            long need = Math.Max(0, UncompressedSize(archivePath));
                            long room = FreeBytes(stagingRoot);
                            if (room >= 0 && room < need + 64L * 1024 * 1024)
                            {
                                error = "not enough room on the working disk to unpack " + Name(content) + " before decrypting it ("
                                        + (need / (1024 * 1024)) + " MB needed, " + (room / (1024 * 1024)) + " MB free)";
                                return null;
                            }
                            report?.Invoke("Unpacking " + Name(content) + "...", 0);
                            Unpack(archivePath, content.Root, staging, everything: true,
                                   progress: f => report?.Invoke(null, f));

                            var licence = LicenceFor(content, staging, vitaFs);
                            if (licence == null)
                            { error = content + " is PFS-encrypted and carries no licence to decrypt it with"; return null; }

                            report?.Invoke("Decrypting " + Name(content) + "...", 0);
                            if (!Vita3kNative.DecryptApp(staging, licence, destination,
                                                         f => report?.Invoke(null, f), onFile,
                                                         consumeSource: true, out error)) return null;
                        }
                    }
                    finally { TryDelete(stagingRoot); }

                    files = Directory.Exists(destination)
                        ? Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Length : 0;
                }
                else
                {
                    Directory.CreateDirectory(destination);
                    report?.Invoke("Unpacking " + Name(content) + "...", 0);
                    files = Unpack(archivePath, content.Root, destination,
                                   progress: f => report?.Invoke(null, f),
                                   hashed: content.Hashed, prefix: relative.Replace('\\', '/').TrimEnd('/') + "/");
                }

                // Vita3K copies the licence for an app or an add-on, never for a patch: a patch runs
                // under the licence of the app it patches.
                if (!content.IsPatch && !PlaceLicence(archivePath, content, vitaFs, out error)) return null;

                content.Written.Add(relative.Replace('\\', '/'));
                Log.Info("installed " + content + " - " + files + " file(s) into " + relative);
                return content;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                Log.Warn("could not install " + archivePath, ex);
                return null;
            }
        }

        /// <summary>The two folders an install LEAVES BEHIND, and the reason a game would not boot.
        ///
        /// MEASURED against a reference pair - the same emulator before and after installing this
        /// game through its own installer. Installing added exactly 35 files: 34 under
        /// ux0/app/&lt;TITLE_ID&gt; and one licence. The archive holds 43. The nine it did NOT copy are
        /// these two folders, and leaving them in place is not harmless:
        ///
        ///   sce_pfs/        files.db, pflist, unicv.db - the descriptors of the PFS encryption
        ///                   layer. With them present the emulator reads the app THROUGH that layer
        ///                   and gives up on the executable:
        ///                       decrypt_fself: Invalid SELF: file is either not a SELF or is still
        ///                       encrypted (unsupported)
        ///                       load_module: Failed to decrypt module file app0:eboot.bin
        ///                   The bytes of eboot.bin are identical either way - we checked - so
        ///                   nothing about the file says what is wrong. Only their absence does.
        ///
        ///   sce_sys/package/  the NoNpDRM package: head, body, tail, stat, temp and work.bin.
        ///                   work.bin is the licence and belongs elsewhere - see PlaceLicence. The
        ///                   rest is install-time material and stays out.
        ///
        /// For a PFS dump these two folders are unpacked after all - into a staging copy, as the
        /// decryptor's input - and it is the decryption that leaves them behind. See Install.</summary>
        private static readonly string[] NotInstalled = { "sce_pfs/", "sce_sys/package/" };

        /// <summary>Put the NoNpDRM licence where the emulator looks for it.
        ///
        /// ux0/license/&lt;TITLE_ID&gt;/&lt;CONTENT_ID&gt;.rif, and it is sce_sys/package/work.bin copied
        /// VERBATIM - byte for byte, measured against the reference install, same 512 bytes and same
        /// sha1. Both names come from the param.sfo we already read.
        ///
        /// Without it the executable stays encrypted and nothing runs. An archive that carries no
        /// work.bin is not an error: a homebrew .vpk has no licence and needs none.
        ///
        /// AND IT HAS TO BE DONE AT EVERY LAUNCH, unlike a real console. The licence lives in ux0,
        /// which this model rebuilds from the pristine firmware each session - so a licence
        /// installed yesterday is gone today, by design.</summary>
        private static bool PlaceLicence(string archivePath, VitaContent content, string vitaFs,
                                         out string error)
        {
            error = null;
            try
            {
                var wanted = content.Root + "sce_sys/package/work.bin";

                using var archive = ArchiveFactory.Open(archivePath);
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;
                    var key = (entry.Key ?? "").Replace('\\', '/');
                    if (!string.Equals(key, wanted, StringComparison.OrdinalIgnoreCase)) continue;

                    byte[] licence;
                    using (var source = entry.OpenEntryStream())
                    using (var memory = new MemoryStream())
                    {
                        source.CopyTo(memory);
                        licence = memory.ToArray();
                    }

                    // NAMED FROM THE LICENCE ITSELF, as Vita3K's copy_license does (license.cpp): the
                    // content id is the licence's own field at 0x10, and the title id is its nine
                    // characters from the seventh - "UP4235-PCSE00965_00-..." gives PCSE00965. For a
                    // game the param.sfo says the same thing; the licence is what the emulator reads
                    // back at boot (get_license), so it is the one that decides the path. The
                    // param.sfo is the fallback for a licence too short or too blank to name itself.
                    var (titleId, contentId) = NamesIn(licence);
                    titleId ??= content.TitleId;
                    contentId ??= content.ContentId;
                    if (string.IsNullOrWhiteSpace(titleId) || string.IsNullOrWhiteSpace(contentId))
                    { Log.Warn("the licence names nothing and neither does the param.sfo - not placed"); return true; }

                    var dir = Path.Combine(vitaFs, "ux0", "license", titleId);
                    Directory.CreateDirectory(dir);
                    File.WriteAllBytes(Path.Combine(dir, contentId + ".rif"), licence);
                    content.Hashed["ux0/license/" + titleId + "/" + contentId + ".rif"] =
                        Entry(licence.Length, Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(licence)));

                    content.Written.Add("ux0/license/" + titleId);
                    Log.Info("licence: ux0/license/" + titleId + "/" + contentId + ".rif");
                    return true;
                }
                return true;   // no work.bin - homebrew, and it needs none
            }
            catch (Exception ex)
            {
                error = "could not place the licence: " + ex.GetType().Name + ": " + ex.Message;
                Log.Warn(error, ex);
                return false;
            }
        }

        /// <summary>The title id and content id a NoNpDRM licence names for itself - SceNpDrmLicense
        /// (zRIF/rif.h): content_id is 0x30 bytes at 0x10, NUL-padded. Nulls when it does not hold a
        /// usable one.</summary>
        internal static (string titleId, string contentId) NamesIn(byte[] licence)
        {
            if (licence == null || licence.Length < 0x40) return (null, null);
            int end = 0x10;
            while (end < 0x40 && licence[end] != 0) end++;
            var contentId = System.Text.Encoding.ASCII.GetString(licence, 0x10, end - 0x10).Trim();
            if (contentId.Length < 16) return (null, null);
            return (contentId.Substring(7, 9), contentId);
        }

        /// <summary>Extract everything under <paramref name="root"/> into the destination, MINUS the
        /// folders an install does not copy - see NotInstalled. Entries whose path escapes it are
        /// refused rather than trusted - an archive is somebody else's file.</summary>
        private static string Name(VitaContent content)
            => string.IsNullOrWhiteSpace(content?.Title) ? (content?.TitleId ?? "the game") : content.Title;

        private static SnapEntry Entry(long size, string sha1)
            => new SnapEntry { IsDirectory = false, Size = size.ToString(), Sha1 = sha1 };

        /// <summary>... and, given <paramref name="hashed"/>, the SHA-1 of every file as it is written,
        /// under <paramref name="prefix"/> + its path.</summary>
        private static int Unpack(string archivePath, string root, string destination, bool everything = false,
                                  Action<double> progress = null,
                                  Dictionary<string, SnapEntry> hashed = null, string prefix = null)
        {
            var full = Path.GetFullPath(destination);
            int files = 0;

            using var archive = ArchiveFactory.Open(archivePath);

            // By bytes, from the archive's own directory - known before anything is unpacked.
            long total = 0, done = 0;
            if (progress != null)
                foreach (var e in archive.Entries)
                    if (!e.IsDirectory) total += Math.Max(0, e.Size);

            foreach (var entry in archive.Entries)
            {
                var key = (entry.Key ?? "").Replace('\\', '/');
                if (root.Length > 0)
                {
                    if (!key.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                    key = key.Substring(root.Length);
                }
                if (key.Length == 0) continue;

                // Everything, when this is the decryptor's input: the layer it removes is the layer
                // it needs to read.
                if (!everything)
                {
                    bool skip = false;
                    foreach (var folder in NotInstalled)
                        if (key.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) { skip = true; break; }
                    if (skip) continue;
                }

                var target = Path.GetFullPath(Path.Combine(full, key.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(full, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("archive entry escapes the destination: " + entry.Key);

                // EMPTY FOLDERS ARE CONTENT. The PFS index names them: this game's files.db lists an
                // empty dlc/, and without it the decryptor stops at "Directory ...\dlc does not
                // exist". The reference install has it too.
                if (entry.IsDirectory) { Directory.CreateDirectory(target); continue; }

                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (var source = entry.OpenEntryStream())
                using (var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    if (hashed == null) source.CopyTo(file);
                    else
                    {
                        using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(
                            System.Security.Cryptography.HashAlgorithmName.SHA1);
                        var buffer = new byte[1 << 20];
                        long size = 0;
                        int n;
                        while ((n = source.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            file.Write(buffer, 0, n);
                            sha.AppendData(buffer, 0, n);
                            size += n;
                        }
                        hashed[(prefix ?? "") + key.TrimEnd('/')] = Entry(size, Convert.ToHexString(sha.GetHashAndReset()));
                    }
                }
                files++;

                if (progress != null)
                {
                    done += Math.Max(0, entry.Size);
                    try { progress(total > 0 ? (double)done / total : 1.0); } catch { }
                }
            }
            return files;
        }

        /// <summary>Does the content carry a PFS layer, i.e. must it be decrypted before it runs?</summary>
        public static bool HasPfs(string archivePath, string root)
        {
            try
            {
                var marker = (root ?? "") + "sce_pfs/";
                using var archive = ArchiveFactory.Open(archivePath);
                foreach (var entry in archive.Entries)
                {
                    var key = (entry.Key ?? "").Replace('\\', '/');
                    if (key.StartsWith(marker, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
            catch (Exception ex) { Log.Warn("could not look into " + archivePath, ex); return false; }
        }

        /// <summary>The licence to decrypt with - Vita3K's own rule (is_nonpdrm, interface.cpp): a
        /// patch whose app licence is already installed uses that one; anything else uses the
        /// work.bin it carries.</summary>
        private static string LicenceFor(VitaContent content, string staging, string vitaFs)
        {
            var installed = InstalledLicence(content, vitaFs);
            if (installed != null) return installed;
            var work = Path.Combine(staging, "sce_sys", "package", "work.bin");
            return File.Exists(work) ? work : null;
        }

        /// <summary>The app's own licence, when this is a patch and the app is installed.</summary>
        private static string InstalledLicence(VitaContent content, string vitaFs)
        {
            if (!content.IsPatch || string.IsNullOrWhiteSpace(content.ContentId)) return null;
            var app = Path.Combine(vitaFs, "ux0", "license", content.TitleId, content.ContentId + ".rif");
            return File.Exists(app) ? app : null;
        }

        /// <summary>A zip by its signature, not its name: a .vpk is one too, and a renamed 7z is not.</summary>
        private static bool IsZip(string archivePath)
        {
            try
            {
                using var f = File.OpenRead(archivePath);
                var head = new byte[4];
                return f.Read(head, 0, 4) == 4 && head[0] == 0x50 && head[1] == 0x4B && head[2] == 0x03 && head[3] == 0x04;
            }
            catch { return false; }
        }

        private static bool ArchiveHas(string archivePath, string key)
        {
            try
            {
                using var archive = ArchiveFactory.Open(archivePath);
                foreach (var entry in archive.Entries)
                    if (string.Equals((entry.Key ?? "").Replace('\\', '/'), key, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            return false;
        }

        /// <summary>Free bytes on the volume holding <paramref name="path"/>, or -1 when that cannot be
        /// told - in which case the unpack is simply attempted.</summary>
        private static long FreeBytes(string path)
        {
            try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))).AvailableFreeSpace; }
            catch { return -1; }
        }

        private static void TryDelete(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch (Exception ex) { Log.Warn("could not remove " + dir, ex); }
        }

        /// <summary>How much room installing this archive needs ON THE WORKING VOLUME: the game, once,
        /// for a PFS dump as for anything else - it is decrypted straight from its zip, and the stage
        /// holds only placeholders that take no room. An ImDisk drive holds its whole declared size in
        /// memory for as long as it is mounted, so this is RAM held for the session: not a byte more.
        ///
        /// The unpacking fallback needs the game plus its largest file; it checks the room it has and
        /// says so plainly when it is short, rather than every launch paying for a case that should not
        /// happen.</summary>
        public static long WorkingSizeBytes(string archivePath) => UncompressedSize(archivePath);

        /// <summary>Set to 1 by the probe to exercise the unpacking fallback. Nothing else sets it.</summary>
        public const string NoZipVariable = "LBIP_VITA3K_NO_ZIP";

        /// <summary>The prefix of a staging folder - what a cleanup looks for.</summary>
        public const string StagingPrefix = "lbip-staging-";

        /// <summary>Where the staging copy goes: the folder that holds the tree ("Z:\" for "Z:\fs"),
        /// so it is on the same volume and never inside the tree the reference walks.</summary>
        public static string StagingParent(string vitaFs)
        {
            var full = Path.GetFullPath(vitaFs).TrimEnd('\\', '/');
            return Path.GetDirectoryName(full) ?? full;
        }


        /// <summary>The uncompressed size of an archive, read from its directory WITHOUT unpacking
        /// it. This is what the working disk has to be big enough for.</summary>
        public static long UncompressedSize(string archivePath)
        {
            try
            {
                using var archive = ArchiveFactory.Open(archivePath);
                long total = 0;
                foreach (var entry in archive.Entries)
                    if (!entry.IsDirectory) total += Math.Max(0, entry.Size);
                return total;
            }
            catch (Exception ex) { Log.Warn("could not size " + archivePath, ex); return -1; }
        }

        /// <summary>The extensions this plugin will install. Kept in one place because the catalogue
        /// row publishes them and the launch path checks them.</summary>
        public static readonly string[] Extensions = { ".vpk", ".zip" };

        public static bool Installable(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return false;
                if (Directory.Exists(path)) return true;      // a content folder, as Vita3K accepts
                var ext = Path.GetExtension(path);
                return Extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }
    }
}
