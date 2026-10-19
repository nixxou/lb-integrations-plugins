// The description of a virtual volume - what the RAM disk helper's view=volume serves, made beforehand by whoever knows what
// it holds (a MAME romset in another layout: Shared.Mame). The helper knows nothing of MAME: it reads this, checks the
// sources are still the ones described, and serves an exFAT volume (ExfatVolumeView) whose files are read through.
//
// A FILE OF THE VOLUME IS: a source served as it is (a CHD), or a virtual zip (VirtualZip) whose entries are ranges of source
// zips. Paths are relative, '\'-separated; a directory is listed before what it holds (Add makes the missing ones).
//
// THE FORMAT, gzip, UTF-8, tab-separated lines (a path never holds a tab nor a line break):
//   #lbip-volume 1
//   L <label>
//   S <size> <last write, UTC ticks> <path>                       a source, numbered from 0 in order
//   D <path>                                                      a directory
//   F <path> <source>                                             a source served as it is
//   Z <path> <entries>                                            a virtual zip, then its entries:
//   E <name> <source> <local offset> <data offset> <crc hex> <compressed> <size> <method> <flags> <time> <date>
//
// STALE: a source whose size or date is not the described one has changed (a ROM manager rewrote it) - its offsets are no
// longer true. Open leaves out every file that reads from it, and says which: never served wrong.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace LbIntegrations.Volume
{
    internal sealed class VolumeSpec
    {
        public sealed class Source { public string Path; public long Size; public long Ticks; }
        public sealed class Entry
        {
            public string Name;
            public int Source;
            public long LocalOffset, DataOffset, CompressedSize, Size;
            public uint Crc;
            public ushort Method, Flags, Time, Date;
        }
        public sealed class File
        {
            public string Path;
            /// <summary>A source served as it is, or -1 for a virtual zip (its Entries).</summary>
            public int Source = -1;
            public List<Entry> Entries;
        }

        public string Label = "VOLUME";
        public readonly List<Source> Sources = new();
        public readonly List<string> Dirs = new();
        public readonly List<File> Files = new();
        private readonly Dictionary<string, int> _sourceIndex = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>A source's number, added (with its size and date now) the first time.</summary>
        public int SourceOf(string path)
        {
            path = System.IO.Path.GetFullPath(path);
            if (_sourceIndex.TryGetValue(path, out var i)) return i;
            var fi = new FileInfo(path);
            Sources.Add(new Source { Path = path, Size = fi.Length, Ticks = fi.LastWriteTimeUtc.Ticks });
            return _sourceIndex[path] = Sources.Count - 1;
        }

        private void DirsFor(string path)
        {
            var parts = path.Split('\\');
            for (int i = 1; i < parts.Length; i++)
            {
                var d = string.Join("\\", parts, 0, i);
                if (_dirs.Add(d)) Dirs.Add(d);
            }
        }

        public void AddPassThrough(string path, string source)
        {
            path = path.Replace('/', '\\');
            DirsFor(path);
            Files.Add(new File { Path = path, Source = SourceOf(source) });
        }

        public void AddZip(string path, IEnumerable<VirtualZip.Item> items, SourcePool pool)
        {
            path = path.Replace('/', '\\');
            DirsFor(path);
            var f = new File { Path = path, Entries = new List<Entry>() };
            foreach (var it in items)
            {
                var e = it.Source;
                f.Entries.Add(new Entry
                {
                    Name = it.Name, Source = SourceOf(e.Archive), LocalOffset = e.LocalOffset, DataOffset = e.DataOffset(pool),
                    CompressedSize = e.CompressedSize, Size = e.Size, Crc = e.Crc, Method = e.Method, Flags = e.Flags, Time = e.Time, Date = e.Date,
                });
            }
            Files.Add(f);
        }

        public void Save(string path)
        {
            var tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
            using (var w = new StreamWriter(gz, new UTF8Encoding(false)))
            {
                var inv = CultureInfo.InvariantCulture;
                w.Write("#lbip-volume 1\n");
                w.Write("L\t" + Label + "\n");
                foreach (var s in Sources) w.Write("S\t" + s.Size.ToString(inv) + "\t" + s.Ticks.ToString(inv) + "\t" + s.Path + "\n");
                foreach (var d in Dirs) w.Write("D\t" + d + "\n");
                foreach (var f in Files)
                {
                    if (f.Entries == null) { w.Write("F\t" + f.Path + "\t" + f.Source.ToString(inv) + "\n"); continue; }
                    w.Write("Z\t" + f.Path + "\t" + f.Entries.Count.ToString(inv) + "\n");
                    foreach (var e in f.Entries)
                        w.Write("E\t" + e.Name + "\t" + e.Source.ToString(inv) + "\t" + e.LocalOffset.ToString(inv) + "\t" + e.DataOffset.ToString(inv)
                                + "\t" + e.Crc.ToString("x8") + "\t" + e.CompressedSize.ToString(inv) + "\t" + e.Size.ToString(inv)
                                + "\t" + e.Method.ToString(inv) + "\t" + e.Flags.ToString(inv) + "\t" + e.Time.ToString(inv) + "\t" + e.Date.ToString(inv) + "\n");
                }
            }
            System.IO.File.Move(tmp, path, true);
        }

        public static VolumeSpec Load(string path)
        {
            var spec = new VolumeSpec();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var r = new StreamReader(gz, Encoding.UTF8);
            var first = r.ReadLine();
            if (first != "#lbip-volume 1") throw new InvalidDataException(path + " is not a volume description (version 1)");
            var inv = CultureInfo.InvariantCulture;
            File zip = null;
            string line;
            while ((line = r.ReadLine()) != null)
            {
                var f = line.Split('\t');
                switch (f[0])
                {
                    case "L": spec.Label = f[1]; break;
                    case "S": spec.Sources.Add(new Source { Size = long.Parse(f[1], inv), Ticks = long.Parse(f[2], inv), Path = f[3] }); break;
                    case "D": spec.Dirs.Add(f[1]); break;
                    case "F": spec.Files.Add(new File { Path = f[1], Source = int.Parse(f[2], inv) }); zip = null; break;
                    case "Z": spec.Files.Add(zip = new File { Path = f[1], Entries = new List<Entry>(int.Parse(f[2], inv)) }); break;
                    case "E":
                        if (zip == null) throw new InvalidDataException(path + ": an entry outside a zip");
                        zip.Entries.Add(new Entry
                        {
                            Name = f[1], Source = int.Parse(f[2], inv), LocalOffset = long.Parse(f[3], inv), DataOffset = long.Parse(f[4], inv),
                            Crc = uint.Parse(f[5], NumberStyles.HexNumber, inv), CompressedSize = long.Parse(f[6], inv), Size = long.Parse(f[7], inv),
                            Method = ushort.Parse(f[8], inv), Flags = ushort.Parse(f[9], inv), Time = ushort.Parse(f[10], inv), Date = ushort.Parse(f[11], inv),
                        });
                        break;
                }
            }
            return spec;
        }

        /// <summary>Its files as served: each a source read through or a virtual zip - every file reading from a source that has
        /// changed (size or date) left out and named in <paramref name="stale"/>.</summary>
        public List<(string Path, IVolumeFile File)> Open(SourcePool pool, List<string> stale)
        {
            var changed = new bool[Sources.Count];
            for (int i = 0; i < Sources.Count; i++)
            {
                var s = Sources[i];
                var fi = new FileInfo(s.Path);
                changed[i] = !fi.Exists || fi.Length != s.Size || fi.LastWriteTimeUtc.Ticks != s.Ticks;
            }
            var list = new List<(string, IVolumeFile)>(Files.Count);
            foreach (var f in Files)
            {
                if (f.Entries == null)
                {
                    if (changed[f.Source]) { stale.Add(f.Path + " (" + Sources[f.Source].Path + ")"); continue; }
                    list.Add((f.Path, new PassThroughFile(pool, Sources[f.Source].Path, Sources[f.Source].Size)));
                    continue;
                }
                var bad = f.Entries.FirstOrDefault(e => changed[e.Source]);
                if (bad != null) { stale.Add(f.Path + " (" + Sources[bad.Source].Path + ")"); continue; }
                var items = f.Entries.Select(e => new VirtualZip.Item
                {
                    Name = e.Name,
                    Source = new ZipEntryRef
                    {
                        Name = e.Name, Archive = Sources[e.Source].Path, LocalOffset = e.LocalOffset, KnownDataOffset = e.DataOffset,
                        Crc = e.Crc, CompressedSize = e.CompressedSize, Size = e.Size, Method = e.Method, Flags = e.Flags, Time = e.Time, Date = e.Date,
                    },
                }).ToList();
                list.Add((f.Path, new VirtualZip(items, pool)));
            }
            return list;
        }
    }
}
