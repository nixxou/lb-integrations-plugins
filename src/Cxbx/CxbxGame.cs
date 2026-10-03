// What a game file is, read from its CONTENT, never from its name (a redump set and a "Games" folder name nothing
// reliably):
//
//   an XBE                         launched where it is
//   a disc image (.iso, .xiso)     an XDVDFS volume - trimmed (XISO) or redump (game partition at 0x18300000);
//                                  unpacked, since Cxbx-Reloaded opens XBEs only - or attached through AIM (the helper)
//   (.cso, .cci, .chd)             the same, compressed - read through its container (src\Shared.Disc)
//   an archive (.zip, .7z...)      holding a disc image - unpacked from the stream, the image never written whole -
//                                  or holding a game already unpacked (a default.xbe and the files beside it)
//   an Xbox 360 disc               default.xex where default.xbe would be: refused, it is Xenia's
//
// THE TITLE ID is the certificate's (Xbe.cs), the name of the game's save folder. For a loose image it costs a few
// reads; for an archive it would cost a pass over the whole stream, so it is learnt when the game is unpacked and
// kept in titles.tsv, by path, size and date - the save of a zipped game is found from there.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SharpCompress.Archives;

namespace LbIntegrations.Cxbx
{
    /// <summary>Zar (Mehdi, 03/10): a ZArchive holding the game's files - read where it is, as a disc image is
    /// (src\Shared.Zar), attached through AIM like one (the helper 1.8), else unpacked.</summary>
    internal enum CxbxRomKind { Unknown, Xbe, Image, ImageInArchive, TreeInArchive, Xbox360, Zar }

    internal sealed class CxbxRom
    {
        public string Path;
        public CxbxRomKind Kind;
        /// <summary>The archive entry: the image, or the default.xbe of an unpacked game.</summary>
        public string EntryKey;
        public long EntrySize;
        /// <summary>The game's files, unpacked - known for an XBE's folder, a loose image, an unpacked game in an
        /// archive; for an image in an archive, once it has been unpacked (titles.tsv), else the image's size.</summary>
        public long Bytes;
        public XbeInfo Xbe;
        public string Problem;
        public string Stamp => StampOf(Path);

        public static string StampOf(string path)
        {
            try { var i = new FileInfo(path); return i.FullName + "|" + i.Length + "|" + i.LastWriteTimeUtc.Ticks; } catch { return path; }
        }
    }

    internal static class CxbxGame
    {
        private static readonly string[] ImageExtensions = { ".iso", ".xiso" };

        /// <summary>The file at <paramref name="rom"/>, described. <paramref name="openArchives"/> false: an archive is
        /// answered from titles.tsv alone - what GetSaves can afford, asked for every game of a page.</summary>
        public static CxbxRom Describe(string rom, bool openArchives = true)
        {
            var d = new CxbxRom { Path = rom };
            try
            {
                if (string.IsNullOrWhiteSpace(rom) || !File.Exists(rom)) { d.Problem = "the game's file is not there (" + rom + ")"; return d; }
                var ext = System.IO.Path.GetExtension(rom).ToLowerInvariant();

                if (ext == ".xbe")
                {
                    d.Kind = CxbxRomKind.Xbe;
                    d.Xbe = Xbe.Read(rom);
                    if (d.Xbe == null) d.Problem = "this file is not an Xbox executable";
                    return d;
                }

                // A ZArchive, by its footer: the game's files in it, read where they are - the shallowest default.xbe.
                if (Zar.ZArchive.IsZar(rom))
                {
                    using var z = Zar.ZArchive.Open(rom);
                    if (z == null) { d.Problem = "this ZArchive does not read"; return d; }
                    var zxbe = z.Entries.Where(e => !e.IsDirectory && Leaf(e.Path.Replace('\\', '/')).Equals("default.xbe", StringComparison.OrdinalIgnoreCase))
                                        .OrderBy(e => e.Path.Count(c => c == '\\')).FirstOrDefault();
                    if (zxbe == null)
                    {
                        if (z.Entries.Any(e => !e.IsDirectory && Leaf(e.Path.Replace('\\', '/')).Equals("default.xex", StringComparison.OrdinalIgnoreCase)))
                        { d.Kind = CxbxRomKind.Xbox360; d.Problem = "this is an Xbox 360 game (default.xex) - it is Xenia's, not Cxbx-Reloaded's"; return d; }
                        d.Problem = "this ZArchive holds no default.xbe";
                        return d;
                    }
                    d.Kind = CxbxRomKind.Zar;
                    d.EntryKey = zxbe.Path.Replace('\\', '/');
                    var zprefix = Folder(d.EntryKey);
                    d.Bytes = z.Entries.Where(e => !e.IsDirectory && e.Path.Replace('\\', '/').StartsWith(zprefix, StringComparison.OrdinalIgnoreCase)).Sum(e => e.Length);
                    d.Xbe = Xbe.Parse(z.ReadHead(zxbe, Xbe.HeadBytes));
                    if (d.Xbe == null) d.Problem = "the default.xbe in this ZArchive is not an Xbox executable";
                    return d;
                }

                // name.2.cso, name.3.cci: a part of a split image, read through its first part - no game of its own.
                if (Disc.SectorImage.IsLaterPart(rom)) { d.Problem = "this is a later part of a split image - the game is its .1 part"; return d; }

                // An image is told by its volume descriptor, whatever its extension - asked FIRST: an archive reader
                // sniffing a disc's first sectors (zeros, mostly) is not a question worth its answer.
                var list = Xdvdfs.List(rom);
                if (list.Found)
                    return FromListing(d, list, () => list.Root("default.xbe") is XdvdfsFile f ? Xdvdfs.ReadHead(rom, f, Xbe.HeadBytes) : null, CxbxRomKind.Image);
                if (!IsArchive(rom)) { d.Problem = "this file is neither an Xbox disc image nor an archive" + (list.Error != null ? " (" + list.Error + ")" : ""); return d; }

                var known = Cached(rom);
                if (!openArchives)
                {
                    if (known == null) { d.Problem = "not unpacked yet"; return d; }
                    d.Kind = known.Value.Kind; d.Bytes = known.Value.Bytes;
                    d.Xbe = new XbeInfo { TitleId = known.Value.TitleId, TitleName = known.Value.Name, Version = known.Value.Version };
                    return d;
                }

                using var archive = ArchiveFactory.Open(rom);
                var files = archive.Entries.Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key)).ToList();

                // A game already unpacked: the shallowest default.xbe.
                var xbe = files.Where(e => Leaf(e.Key).Equals("default.xbe", StringComparison.OrdinalIgnoreCase))
                               .OrderBy(e => Norm(e.Key).Count(c => c == '/')).FirstOrDefault();
                if (xbe != null)
                {
                    d.Kind = CxbxRomKind.TreeInArchive;
                    d.EntryKey = Norm(xbe.Key);
                    var prefix = Folder(d.EntryKey);
                    d.Bytes = files.Where(e => Norm(e.Key).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Sum(e => e.Size);
                    using (var s = xbe.OpenEntryStream()) d.Xbe = Xbe.Parse(Head(s, Xbe.HeadBytes));
                    if (d.Xbe == null) d.Problem = "the default.xbe in this archive is not an Xbox executable";
                    return d;
                }
                if (files.Any(e => Leaf(e.Key).Equals("default.xex", StringComparison.OrdinalIgnoreCase)))
                { d.Kind = CxbxRomKind.Xbox360; d.Problem = "this is an Xbox 360 game (default.xex) - it is Xenia's, not Cxbx-Reloaded's"; return d; }

                // A disc image: the biggest .iso/.xiso entry - then confirmed by what it holds, when it is unpacked.
                var image = files.Where(e => ImageExtensions.Contains(System.IO.Path.GetExtension(Leaf(e.Key)).ToLowerInvariant()))
                                 .OrderByDescending(e => e.Size).FirstOrDefault();
                if (image == null) { d.Problem = "this archive holds no Xbox disc image and no default.xbe"; return d; }
                d.Kind = CxbxRomKind.ImageInArchive;
                d.EntryKey = Norm(image.Key);
                d.EntrySize = image.Size;
                // Exact once the disc has been listed (CxbxListing) or unpacked (titles.tsv); the image's size until then.
                d.Bytes = CxbxListing.Load(rom)?.Bytes ?? (known?.Bytes > 0 ? known.Value.Bytes : image.Size);
                if (known != null) d.Xbe = new XbeInfo { TitleId = known.Value.TitleId, TitleName = known.Value.Name, Version = known.Value.Version };
                return d;
            }
            catch (Exception ex) { d.Problem = ex.GetType().Name + ": " + ex.Message; return d; }
        }

        private static CxbxRom FromListing(CxbxRom d, XdvdfsResult list, Func<byte[]> head, CxbxRomKind kind)
        {
            d.Kind = kind;
            d.Bytes = list.Bytes;
            if (list.Root("default.xbe") == null)
            {
                if (list.Root("default.xex") != null) { d.Kind = CxbxRomKind.Xbox360; d.Problem = "this is an Xbox 360 disc (default.xex) - it is Xenia's, not Cxbx-Reloaded's"; }
                else d.Problem = "this disc has no default.xbe at its root";
                return d;
            }
            d.Xbe = Xbe.Parse(head());
            if (d.Xbe == null) d.Problem = "the disc's default.xbe is not an Xbox executable";
            return d;
        }

        /// <summary>The save folder's name for this game file - null when it cannot be known without unpacking it.</summary>
        public static string TitleIdOf(string rom)
        {
            var stamp = CxbxRom.StampOf(rom);
            lock (_ids) if (_ids.TryGetValue(stamp, out var known)) return known;
            var d = Describe(rom, openArchives: false);
            var id = d.Xbe?.TitleId > 0 ? d.Xbe.TitleIdText : null;
            // "Not unpacked yet" is not remembered: it changes at the game's first launch.
            if (id != null || d.Kind != CxbxRomKind.Unknown) lock (_ids) _ids[stamp] = id;
            return id;
        }

        private static readonly Dictionary<string, string> _ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A game just unpacked: its title id from now on, without reading titles.tsv again.</summary>
        private static void Learn(CxbxRom d) { if (d?.Xbe?.TitleId > 0) lock (_ids) _ids[d.Stamp] = d.Xbe.TitleIdText; }

        // ── titles.tsv ───────────────────────────────────────────────────────

        private static string CachePath => System.IO.Path.Combine(CxbxSettings.Dir, "titles.tsv");
        private static readonly object Gate = new object();

        public static (CxbxRomKind Kind, uint TitleId, string Name, long Bytes, uint Version)? Cached(string rom)
        {
            var stamp = CxbxRom.StampOf(rom);
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(CachePath)) return null;
                    foreach (var line in File.ReadAllLines(CachePath))
                    {
                        var c = line.Split('\t');
                        if (c.Length < 5 || !string.Equals(c[0], stamp, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!Enum.TryParse<CxbxRomKind>(c[1], out var kind)) continue;
                        uint.TryParse(c[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id);
                        long.TryParse(c[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes);
                        uint version = 0;
                        if (c.Length > 5) uint.TryParse(c[5], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out version);
                        return (kind, id, c[3], bytes, version);
                    }
                }
                catch (Exception ex) { Log.Warn("could not read " + CachePath, ex); }
            }
            return null;
        }

        /// <summary>Learnt once the game is unpacked: what its certificate says and how big it is.</summary>
        public static void Remember(CxbxRom d)
        {
            Learn(d);
            if (d?.Xbe == null || (d.Kind != CxbxRomKind.ImageInArchive && d.Kind != CxbxRomKind.TreeInArchive)) return;
            lock (Gate)
            {
                try
                {
                    var stamp = d.Stamp;
                    var path = d.Path;
                    var lines = File.Exists(CachePath) ? File.ReadAllLines(CachePath).ToList() : new List<string>();
                    // This file at any other size or date is a file that changed: its old line goes too.
                    lines.RemoveAll(l => { var k = l.Split('\t')[0]; return k.Equals(stamp, StringComparison.OrdinalIgnoreCase) || k.StartsWith(System.IO.Path.GetFullPath(path) + "|", StringComparison.OrdinalIgnoreCase); });
                    lines.Add(string.Join("\t", stamp, d.Kind, d.Xbe.TitleId.ToString("x8"), (d.Xbe.TitleName ?? "").Replace('\t', ' '), d.Bytes.ToString(CultureInfo.InvariantCulture), d.Xbe.Version.ToString("x8")));
                    Directory.CreateDirectory(CxbxSettings.Dir);
                    File.WriteAllLines(CachePath, lines);
                }
                catch (Exception ex) { Log.Warn("could not write " + CachePath, ex); }
            }
        }

        // ── plumbing ─────────────────────────────────────────────────────────

        public static bool IsArchive(string path)
        {
            try { using var a = ArchiveFactory.Open(path); return a.Entries.Any(); }
            catch { return false; }
        }

        public static string Norm(string key) => (key ?? "").Replace('\\', '/').TrimStart('/');
        public static string Leaf(string key) => Norm(key).Split('/').Last();
        /// <summary>"a/b/default.xbe" -> "a/b/", "default.xbe" -> "".</summary>
        public static string Folder(string key) { var k = Norm(key); var at = k.LastIndexOf('/'); return at < 0 ? "" : k.Substring(0, at + 1); }

        private static byte[] Head(Stream s, int max)
        {
            var b = new byte[max];
            int got = 0, n;
            while (got < max && (n = s.Read(b, got, max - got)) > 0) got += n;
            return got == max ? b : b.Take(got).ToArray();
        }
    }
}
