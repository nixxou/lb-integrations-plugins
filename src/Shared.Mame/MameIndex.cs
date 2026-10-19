// MAME's sets, from its -listxml: for each machine its parent (cloneof), the set its shared ROMs come from (romof: the parent,
// or a BIOS set), whether it is a BIOS or a device, its ROMs - name, size, CRC, SHA-1, the name it has in the set it is merged
// into (merge=) - and the devices it uses (device_ref: their ROMs are their own sets, in every layout).
//
// A ROM without a CRC is undumped (status="nodump"): in no set. A disk (<disk>: a CHD, <set>\<name>.chd) has its SHA-1 -
// the one in its CHD header - and merge= when its parent has it. Read as a stream (the 0.288 list is 324 MB); the compact
// cache (Save/Load: one line per machine and ROM) is what is kept beside the helper.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace LbIntegrations.Mame
{
    internal sealed class MameRom
    {
        public string Name, Sha1, Merge;
        public long Size;
        public uint Crc;
        public override string ToString() => Name + " " + Crc.ToString("x8") + (Merge != null ? " merge=" + Merge : "");
    }

    internal sealed class MameDisk
    {
        /// <summary>Its file is &lt;Name&gt;.chd in its set's folder.</summary>
        public string Name, Sha1, Merge;
        public override string ToString() => Name + " " + Sha1 + (Merge != null ? " merge=" + Merge : "");
    }

    internal sealed class MameMachine
    {
        public string Name, CloneOf, RomOf;
        public bool IsBios, IsDevice;
        public List<MameRom> Roms = new();
        public List<MameDisk> Disks = new();
        public List<string> DeviceRefs = new();
        public override string ToString() => Name + (CloneOf != null ? " (clone of " + CloneOf + ")" : "") + " " + Roms.Count + " ROMs";
    }

    internal sealed class MameIndex
    {
        public string Build;
        public Dictionary<string, MameMachine> Machines = new(StringComparer.OrdinalIgnoreCase);

        public MameMachine Get(string name) => name != null && Machines.TryGetValue(name, out var m) ? m : null;

        public static MameIndex FromListXml(string path)
        {
            var idx = new MameIndex();
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreWhitespace = true, IgnoreComments = true };
            using var r = XmlReader.Create(path, settings);
            MameMachine m = null;
            while (r.Read())
            {
                if (r.NodeType != XmlNodeType.Element) continue;
                switch (r.Name)
                {
                    case "mame":
                        idx.Build = r.GetAttribute("build");
                        break;
                    case "machine":
                        m = new MameMachine
                        {
                            Name = r.GetAttribute("name"),
                            CloneOf = r.GetAttribute("cloneof"),
                            RomOf = r.GetAttribute("romof"),
                            IsBios = r.GetAttribute("isbios") == "yes",
                            IsDevice = r.GetAttribute("isdevice") == "yes",
                        };
                        idx.Machines[m.Name] = m;
                        break;
                    case "rom":
                        if (m == null) break;
                        var crc = r.GetAttribute("crc");
                        if (string.IsNullOrEmpty(crc) || r.GetAttribute("status") == "nodump") break;
                        m.Roms.Add(new MameRom
                        {
                            Name = r.GetAttribute("name"),
                            Size = long.Parse(r.GetAttribute("size") ?? "0", CultureInfo.InvariantCulture),
                            Crc = uint.Parse(crc, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                            Sha1 = r.GetAttribute("sha1"),
                            Merge = r.GetAttribute("merge"),
                        });
                        break;
                    case "disk":
                        if (m != null && !string.IsNullOrEmpty(r.GetAttribute("sha1")) && r.GetAttribute("status") != "nodump")
                            m.Disks.Add(new MameDisk { Name = r.GetAttribute("name"), Sha1 = r.GetAttribute("sha1"), Merge = r.GetAttribute("merge") });
                        break;
                    case "device_ref":
                        if (m != null) { var d = r.GetAttribute("name"); if (!m.DeviceRefs.Contains(d)) m.DeviceRefs.Add(d); }
                        break;
                }
            }
            return idx;
        }

        // ── the compact cache ────────────────────────────────────────────────
        // gzip, UTF-8 lines: "#build <build>"; "M\t<name>\t<cloneof>\t<romof>\t<flags b/d>\t<device refs ,>";
        // then its ROMs "R\t<name>\t<size>\t<crc>\t<sha1>\t<merge>" and disks "D\t<name>\t<sha1>\t<merge>".

        public void Save(string path)
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var gz = new GZipStream(fs, CompressionLevel.Optimal);
            using var w = new StreamWriter(gz, new UTF8Encoding(false));
            w.Write("#build "); w.Write(Build); w.Write('\n');
            foreach (var m in Machines.Values)
            {
                w.Write("M\t" + m.Name + "\t" + m.CloneOf + "\t" + m.RomOf + "\t" + (m.IsBios ? "b" : "") + (m.IsDevice ? "d" : "")
                        + "\t" + string.Join(",", m.DeviceRefs) + "\n");
                foreach (var r in m.Roms)
                    w.Write("R\t" + r.Name + "\t" + r.Size.ToString(CultureInfo.InvariantCulture) + "\t" + r.Crc.ToString("x8") + "\t" + r.Sha1 + "\t" + r.Merge + "\n");
                foreach (var d in m.Disks) w.Write("D\t" + d.Name + "\t" + d.Sha1 + "\t" + d.Merge + "\n");
            }
        }

        public static MameIndex Load(string path)
        {
            var idx = new MameIndex();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var rd = new StreamReader(gz, Encoding.UTF8);
            MameMachine m = null;
            string line;
            static string N(string s) => s.Length == 0 ? null : s;
            while ((line = rd.ReadLine()) != null)
            {
                if (line.StartsWith("#build ", StringComparison.Ordinal)) { idx.Build = line.Substring(7); continue; }
                var f = line.Split('\t');
                if (f[0] == "M" && f.Length >= 6)
                {
                    m = new MameMachine
                    {
                        Name = f[1], CloneOf = N(f[2]), RomOf = N(f[3]), IsBios = f[4].Contains('b'), IsDevice = f[4].Contains('d'),
                        DeviceRefs = new List<string>(f[5].Split(',', StringSplitOptions.RemoveEmptyEntries)),
                    };
                    idx.Machines[m.Name] = m;
                }
                else if (f[0] == "D" && f.Length >= 4 && m != null)
                    m.Disks.Add(new MameDisk { Name = f[1], Sha1 = N(f[2]), Merge = N(f[3]) });
                else if (f[0] == "R" && f.Length >= 6 && m != null)
                    m.Roms.Add(new MameRom
                    {
                        Name = f[1], Size = long.Parse(f[2], CultureInfo.InvariantCulture),
                        Crc = uint.Parse(f[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture), Sha1 = N(f[4]), Merge = N(f[5]),
                    });
            }
            return idx;
        }
    }
}
