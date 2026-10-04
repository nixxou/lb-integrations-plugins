// The disc handed to xemu - which opens ONE kind: an XISO, the game partition alone (xemu.app/docs/disc-images: redump's
// full-disc images "not currently compatible", nothing about CSO, CCI, CHD).
//
// WHAT A FILE IS, by its content (Xdvdfs.List - the Cxbx plugin's reader, the same file):
//   an XISO                 the XDVDFS volume at the start of the image: handed over AS IT IS - nothing copied
//   a redump image          the volume further in (the game partition, after the video one): its partition is the XISO
//   a CSO, CCI, CHD         the disc inside read through its container (Shared.Disc), whichever of the two it holds
//   an .iso / .xiso in a zip or a 7z   unpacked first, then as above
//   a ZArchive, a game unpacked in a zip   no XISO to cut out: refused for now (to come: an XDVDFS built on the fly)
//   an Xbox 360 disc        default.xex: refused, it is Xenia's
//
// THE COPY, WHEN THERE HAS TO BE ONE (Mehdi, 04/10: "sans copie d'abord" - serving such a disc through AIM as a raw disk
// waits on a measurement, see the plan): the partition written to <install>\discs\<name>-<stamp>.iso, from the volume's
// start to the end of the image, through a .part file - and KEPT, a cache: the next launch of the same file opens it at
// once. The cache is held under a size (40 GB by default): past it, the copies used longest ago go, never the one in use.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LbIntegrations.Cxbx;
using SharpCompress.Archives;

namespace LbIntegrations.Xemu
{
    internal enum XemuDiscKind { Unknown, Xiso, Redump, Compressed, ImageInArchive, Zar, TreeInArchive, Xbox360 }

    internal sealed class XemuDiscInfo
    {
        public string Path;
        public XemuDiscKind Kind;
        public long PartitionBase;
        public XbeInfo Xbe;
        public string EntryKey;
        public long EntrySize;
        public string Problem;
        /// <summary>Set when the disc is served where it is (RamDrive.AttachXiso): the root to detach at the session's end.</summary>
        public string AttachedRoot;
    }

    internal static class XemuDisc
    {
        private static readonly string[] ImageExtensions = { ".iso", ".xiso" };
        public const long DefaultCacheBytes = 40L << 30;

#pragma warning disable CS0649
        /// <summary>For the probe: never attach a disc, always the copy. Set by reflection.</summary>
        internal static bool AttachOff;
#pragma warning restore CS0649

        public static XemuDiscInfo Describe(string rom)
        {
            var d = new XemuDiscInfo { Path = rom };
            try
            {
                if (string.IsNullOrWhiteSpace(rom) || !File.Exists(rom)) { d.Problem = "the game's file is not there (" + rom + ")"; return d; }
                if (Zar.ZArchive.IsZar(rom)) { d.Kind = XemuDiscKind.Zar; d.Problem = "a ZArchive (.zar) holds the game's files, not a disc xemu can open - not supported yet"; return d; }
                if (Disc.SectorImage.IsLaterPart(rom)) { d.Problem = "this is a later part of a split image - the game is its .1 part"; return d; }

                var list = Xdvdfs.List(rom);
                if (list.Found)
                {
                    d.PartitionBase = list.PartitionBase;
                    d.Kind = Disc.DiscImages.IsCompressed(rom) ? XemuDiscKind.Compressed : list.PartitionBase > 0 ? XemuDiscKind.Redump : XemuDiscKind.Xiso;
                    return FromListing(d, list, () => list.Root("default.xbe") is XdvdfsFile f ? Xdvdfs.ReadHead(rom, f, Xbe.HeadBytes) : null);
                }

                IArchive archive;
                try { archive = ArchiveFactory.Open(rom); }
                catch { d.Problem = "this file is neither an Xbox disc image nor an archive" + (list.Error != null ? " (" + list.Error + ")" : ""); return d; }
                using (archive)
                {
                    var files = archive.Entries.Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key)).ToList();
                    if (files.Any(e => Leaf(e.Key).Equals("default.xbe", StringComparison.OrdinalIgnoreCase)))
                    { d.Kind = XemuDiscKind.TreeInArchive; d.Problem = "this archive holds the game's files, not a disc image xemu can open - not supported yet"; return d; }
                    if (files.Any(e => Leaf(e.Key).Equals("default.xex", StringComparison.OrdinalIgnoreCase)))
                    { d.Kind = XemuDiscKind.Xbox360; d.Problem = "this is an Xbox 360 game (default.xex) - it is Xenia's"; return d; }
                    var image = files.Where(e => ImageExtensions.Contains(System.IO.Path.GetExtension(Leaf(e.Key)).ToLowerInvariant()))
                                     .OrderByDescending(e => e.Size).FirstOrDefault();
                    if (image == null) { d.Problem = "this archive holds no Xbox disc image"; return d; }
                    d.Kind = XemuDiscKind.ImageInArchive;
                    d.EntryKey = image.Key;
                    d.EntrySize = image.Size;
                    return d;
                }
            }
            catch (Exception ex) { d.Problem = ex.GetType().Name + ": " + ex.Message; return d; }
        }

        private static XemuDiscInfo FromListing(XemuDiscInfo d, XdvdfsResult list, Func<byte[]> head)
        {
            if (list.Root("default.xbe") == null)
            {
                if (list.Root("default.xex") != null) { d.Kind = XemuDiscKind.Xbox360; d.Problem = "this is an Xbox 360 disc (default.xex) - it is Xenia's"; }
                else d.Problem = "this disc has no default.xbe at its root";
                return d;
            }
            d.Xbe = Xbe.Parse(head());
            if (d.Xbe == null) d.Problem = "the disc's default.xbe is not an Xbox executable";
            return d;
        }

        /// <summary>The path to give xemu's -dvd_path for <paramref name="rom"/> - the file itself for an XISO that needs nothing,
        /// else the disc served where it is, else a copy in the cache, made now when it is not there. Null with
        /// <paramref name="problem"/> when the game cannot be launched.</summary>
        public static string Present(string rom, string exe, out string problem, out XemuDiscInfo info, Action<string> report = null, bool? mediaPatch = null)
        {
            problem = null;
            info = Describe(rom);
            if (info.Problem != null) { problem = info.Problem; return null; }
            var name = System.IO.Path.GetFileName(rom);

            // THE MEDIA PATCH (XboxMediaPatch, the option disc.media_patch - on by default): where the disc's .xbe files hold extract-xiso's pattern.
            bool patch = mediaPatch ?? XemuSettings.MediaPatch();
            long[] patches = patch && info.Kind != XemuDiscKind.ImageInArchive ? PatchesOf(rom) : Array.Empty<long>();
            if (patches.Length > 0) Log.Info("disc: the media patch goes in " + patches.Length + " place(s) of " + name);
            if (info.Kind == XemuDiscKind.Xiso && patches.Length == 0) { Log.Info("disc: " + name + " is an XISO" + (patch ? ", no media patch to make" : "") + " - opened as it is"); return rom; }

            // WHERE IT IS FIRST (Mehdi, 04/10: "sans copie d'abord"): a redump, a compressed image - or an XISO to patch - served by
            // the RAM disk helper as the one file of an exFAT volume, its XISO read in place, the patch laid over it. Else (no AIM,
            // an older helper, a failure) the copy.
            if (!AttachOff && (info.Kind == XemuDiscKind.Redump || info.Kind == XemuDiscKind.Compressed || info.Kind == XemuDiscKind.Xiso))
            {
                if (RamDisk.RamDrive.CanAttachXiso(out var why, rom))
                {
                    var file = RamDisk.RamDrive.AttachXiso(rom, out var root, out var error, patches.Length > 0);
                    if (file != null && File.Exists(file)) { info.AttachedRoot = root; Log.Info("disc: " + name + " served where it is" + (patches.Length > 0 ? ", media patched" : "") + " - " + file); return file; }
                    if (file != null) { info.AttachedRoot = root; Release(info); }
                    Log.Warn("disc: " + name + " could not be served where it is (" + (error ?? "the file did not appear") + ") - copied instead");
                }
                else Log.Info("disc: not served where it is (" + why + ") - copied");
            }

            var cache = XemuPaths.DiscCache(exe);
            if (cache == null) { problem = "the emulator's folder is not known"; return null; }
            Directory.CreateDirectory(cache);
            // A patched copy is named apart: the option turned off never gets one.
            var target = System.IO.Path.Combine(cache, Safe(System.IO.Path.GetFileNameWithoutExtension(rom)) + "-" + StampHash(rom) + (patch ? "-mp" : "") + ".iso");
            if (File.Exists(target))
            {
                try { File.SetLastAccessTimeUtc(target, DateTime.UtcNow); } catch { }
                Log.Info("disc: the XISO of " + name + " is in the cache - " + target);
                return target;
            }

            var part = target + ".part";
            try
            {
                Purge(cache, target, NeededBytes(info));
                report?.Invoke("Preparing " + name + " for xemu...");
                var watch = System.Diagnostics.Stopwatch.StartNew();
                if (info.Kind == XemuDiscKind.ImageInArchive)
                {
                    // Unpacked whole first - an archive's entry reads forward only - then cut like a file of its own.
                    var unpacked = target + ".unpacked";
                    try
                    {
                        var key = info.EntryKey;
                        using (var archive = ArchiveFactory.Open(rom))
                        {
                            var entry = archive.Entries.FirstOrDefault(e => e.Key == key);
                            if (entry == null) { problem = "the disc image is no longer in the archive"; return null; }
                            using var s = entry.OpenEntryStream();
                            using var o = File.Create(unpacked);
                            s.CopyTo(o, 1 << 20);
                        }
                        var inner = Describe(unpacked);
                        if (inner.Problem != null) { problem = "the image in the archive: " + inner.Problem; return null; }
                        info.Xbe = inner.Xbe; info.PartitionBase = inner.PartitionBase;
                        patches = patch ? PatchesOf(unpacked) : Array.Empty<long>();
                        if (inner.Kind == XemuDiscKind.Xiso) File.Move(unpacked, part);
                        else Cut(unpacked, inner.PartitionBase, part);
                    }
                    finally { try { if (File.Exists(unpacked)) File.Delete(unpacked); } catch { } }
                }
                else Cut(rom, info.PartitionBase, part);
                XboxMediaPatch.Write(part, patches);
                File.Move(part, target);
                if (patches.Length > 0) Log.Info("disc: media patched in " + patches.Length + " place(s)");
                return Done(rom, target, watch);
            }
            catch (Exception ex)
            {
                problem = "its XISO could not be made (" + ex.GetType().Name + ": " + ex.Message + ")";
                Log.Warn("disc: " + problem);
                return null;
            }
            finally { try { if (File.Exists(part)) File.Delete(part); } catch { } }
        }

        /// <summary>Where the media patch goes in <paramref name="image"/>'s XISO (XboxMediaPatch.Find); none when it cannot be read.</summary>
        internal static long[] PatchesOf(string image)
        {
            try
            {
                var listing = Xdvdfs.List(image);
                if (!listing.Found) return Array.Empty<long>();
                using var disc = Disc.DiscImages.Open(image);
                return XboxMediaPatch.Find(disc, listing);
            }
            catch (Exception ex) { Log.Warn("disc: the .xbe files could not be read for the media patch", ex); return Array.Empty<long>(); }
        }
        /// <summary>The disc served where it is, detached - at the session's end, or when the launch goes no further.</summary>
        public static void Release(XemuDiscInfo info)
        {
            var root = info?.AttachedRoot;
            if (root == null) return;
            info.AttachedRoot = null;
            try
            {
                if (RamDisk.RamDrive.DetachImage(root, out var error)) Log.Info("disc: " + root + " detached");
                else Log.Warn("disc: " + root + " could not be detached - " + error);
            }
            catch (Exception ex) { Log.Warn("disc: detaching " + root, ex); }
        }

        private static string Done(string rom, string target, System.Diagnostics.Stopwatch watch)
        {
            Log.Info("disc: the XISO of " + System.IO.Path.GetFileName(rom) + " made in " + watch.Elapsed.TotalSeconds.ToString("0") + " s - " + target
                     + " (" + (new FileInfo(target).Length >> 20) + " MB)");
            return target;
        }

        /// <summary>The image from <paramref name="from"/> on - its game partition - into <paramref name="to"/>; through its
        /// container when it is one.</summary>
        private static void Cut(string image, long from, string to)
        {
            using var src = Disc.DiscImages.Open(image);
            src.Seek(from, SeekOrigin.Begin);
            using var dst = new FileStream(to, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
            src.CopyTo(dst, 1 << 20);
        }

        private static long NeededBytes(XemuDiscInfo info)
        {
            try { return info.Kind == XemuDiscKind.ImageInArchive ? info.EntrySize * 2 : Disc.DiscImages.Open(info.Path).Length - info.PartitionBase; }
            catch { return 0; }
        }

        /// <summary>The cache held under its size: the copies used longest ago go first - never <paramref name="keep"/>.</summary>
        private static void Purge(string cache, string keep, long needed)
        {
            try
            {
                long limit = XemuSettings.CacheBytes();
                var files = new DirectoryInfo(cache).GetFiles("*.iso").Where(f => !string.Equals(f.FullName, keep, StringComparison.OrdinalIgnoreCase))
                                                    .OrderBy(f => f.LastAccessTimeUtc).ToList();
                long total = files.Sum(f => f.Length);
                foreach (var f in files)
                {
                    if (total + needed <= limit) break;
                    try { total -= f.Length; f.Delete(); Log.Info("disc: " + f.Name + " taken out of the cache (over " + (limit >> 30) + " GB)"); } catch { }
                }
            }
            catch (Exception ex) { Log.Warn("disc: the cache could not be purged", ex); }
        }

        /// <summary>A name for the file, its game told by its path, size and date - a changed file is a new copy.</summary>
        private static string StampHash(string rom)
        {
            var fi = new FileInfo(rom);
            var key = System.IO.Path.GetFullPath(rom).ToLowerInvariant() + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            using var sha = System.Security.Cryptography.SHA1.Create();
            return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key)), 0, 4).Replace("-", "").ToLowerInvariant();
        }

        private static string Safe(string name)
        {
            var bad = System.IO.Path.GetInvalidFileNameChars();
            var s = new string((name ?? "disc").Select(c => bad.Contains(c) || c == '\'' ? '_' : c).ToArray());
            return s.Length > 80 ? s.Substring(0, 80) : s;
        }

        private static string Leaf(string key) => (key ?? "").Replace('\\', '/').TrimStart('/').Split('/').Last();

        // ── the title id, for a game's console and its save ──

        private static readonly Dictionary<string, string> _ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The game's title id (8 hex digits), or null. Remembered per file and size and date; an archive answers from
        /// the copy it was unpacked to, once it has been launched.</summary>
        public static string TitleIdOf(string rom, string exe = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rom) || !File.Exists(rom)) return null;
                var fi = new FileInfo(rom);
                var key = fi.FullName + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
                lock (_ids) if (_ids.TryGetValue(key, out var known)) return known;
                var d = Describe(rom);
                string id = d.Xbe?.TitleId > 0 ? d.Xbe.TitleIdText : null;
                if (id == null && d.Kind == XemuDiscKind.ImageInArchive && exe != null)
                {
                    var copy = System.IO.Path.Combine(XemuPaths.DiscCache(exe) ?? "", Safe(System.IO.Path.GetFileNameWithoutExtension(rom)) + "-" + StampHash(rom) + ".iso");
                    if (File.Exists(copy)) { var c = Describe(copy); id = c.Xbe?.TitleId > 0 ? c.Xbe.TitleIdText : null; }
                }
                if (id != null) lock (_ids) _ids[key] = id;
                return id;
            }
            catch { return null; }
        }

        public static void Remember(string rom, XbeInfo xbe)
        {
            try
            {
                if (xbe == null || xbe.TitleId == 0) return;
                var fi = new FileInfo(rom);
                lock (_ids) _ids[fi.FullName + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks] = xbe.TitleIdText;
            }
            catch { }
        }
    }
}
