// A disc's listing, kept (Mehdi, 03/10): the partition, every folder, every file with its offset and length - a few KB
// per game, <plugin data>\listings\<hash>.tsv, for the game file at this path, size and date.
//
// What it buys: listing an image inside a zip costs a read of the stream as far as its last table; kept, the next
// launches go straight to the one pass that unpacks (Xdvdfs.ExtractListed), and the RAM disk is sized exactly before
// anything is read. A file changed (size or date) is another key, so another listing. And a kept listing is checked
// against the disc before it is used - its volume descriptor where the listing says - so it cannot unpack the wrong one.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LbIntegrations.Cxbx
{
    internal static class CxbxListing
    {
        private static string PathOf(string rom)
        {
            using var sha = SHA1.Create();
            var key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(CxbxRom.StampOf(rom).ToLowerInvariant()))).Replace("-", "").Substring(0, 20);
            return Path.Combine(CxbxSettings.Dir, "listings", key + ".tsv");
        }

        public static XdvdfsResult Load(string rom)
        {
            var path = PathOf(rom);
            try
            {
                if (!File.Exists(path)) return null;
                var lines = File.ReadAllLines(path);
                if (lines.Length < 2 || lines[0] != CxbxRom.StampOf(rom) || !lines[1].StartsWith("base=")) return null;
                var r = new XdvdfsResult { PartitionBase = long.Parse(lines[1].Substring(5), NumberStyles.HexNumber, CultureInfo.InvariantCulture) };
                foreach (var line in lines.Skip(2))
                {
                    var c = line.Split('\t');
                    if (c[0] == "D" && c.Length == 2) r.Dirs.Add(c[1]);
                    else if (c[0] == "F" && c.Length == 4)
                        r.Files.Add(new XdvdfsFile { Offset = long.Parse(c[1], CultureInfo.InvariantCulture), Length = long.Parse(c[2], CultureInfo.InvariantCulture), Path = c[3] });
                }
                return r.Files.Count > 0 ? r : null;
            }
            catch (Exception ex) { Log.Warn("could not read the listing " + path, ex); return null; }
        }

        public static void Save(string rom, XdvdfsResult listing)
        {
            if (listing == null || !listing.Found || listing.Error != null) return;
            var path = PathOf(rom);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var lines = new List<string> { CxbxRom.StampOf(rom), "base=" + listing.PartitionBase.ToString("X", CultureInfo.InvariantCulture) };
                lines.AddRange(listing.Dirs.Select(d => "D\t" + d));
                lines.AddRange(listing.Files.Select(f => "F\t" + f.Offset.ToString(CultureInfo.InvariantCulture) + "\t" + f.Length.ToString(CultureInfo.InvariantCulture) + "\t" + f.Path));
                File.WriteAllLines(path + ".part", lines);
                File.Move(path + ".part", path, overwrite: true);
            }
            catch (Exception ex) { Log.Warn("could not keep the listing " + path, ex); }
        }

        public static void Forget(string rom)
        {
            try { File.Delete(PathOf(rom)); } catch { }
        }
    }
}
