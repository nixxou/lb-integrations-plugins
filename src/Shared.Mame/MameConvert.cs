// A folder of MAME sets seen in another layout: for each zip of the target layout, which files it holds and where each one
// is found in the source folder - whatever the source's layout. Served as virtual zips (VirtualZip): nothing copied.
//
// THE THREE LAYOUTS (MAME's -listxml: cloneof, romof, merge=):
//   non-merged  every machine's zip holds all its ROMs - its parent's and its BIOS's included;
//   split       a machine's zip holds the ROMs without merge= (its own): a clone's shared ROMs are in its parent's zip, a
//               parent's BIOS ROMs in the BIOS set's;
//   merged      a parent's zip holds its own ROMs and its clones' own ones - one under the clone's folder ("clone/name") only
//               when the parent already has another file of that name; clones have no zip. BIOS sets stay apart.
// Device sets (device_ref) are their own zips in every layout.
//
// DISKS (CHDs, <set>\<disk>.chd): placed by the same rules - non-merged: every disk of the set in its folder; split: the
// ones without merge=; merged: the parent's folder holds its clones' own ones too. Each is found by the SHA-1 in its CHD
// header (ChdSha1), in the folders of the same sets a ROM is looked for in, and served as it is: the volume's file is the
// original's bytes, read through.
//
// FINDING A FILE: by CRC and size, never by name, in the zips it can be in whatever the source layout - the machine's own,
// its parent's (cloneof), then the chain of romof (the BIOS). So a merged, split or non-merged folder (or a mix) serves any
// layout. A file in none of them is missing: told, never guessed. A redumped file (another CRC) is missing too - the
// layout does not change a dump.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Mame
{
    internal enum MameLayout { NonMerged, Split, Merged }

    internal sealed class MameZipPlan
    {
        /// <summary>The zip's set name (its file is &lt;Set&gt;.zip).</summary>
        public string Set;
        public List<VirtualZip.Item> Items = new();
        /// <summary>"name (crc)" of each file found in no source zip.</summary>
        public List<string> Missing = new();
        /// <summary>Its folder's CHDs: their file name ("cap-33s-2.chd") and the source CHD they are read from.</summary>
        public List<(string Name, string Source)> Disks = new();
        /// <summary>"name (sha1)" of each disk found in no source folder.</summary>
        public List<string> MissingDisks = new();
        public override string ToString() => Set + ": " + Items.Count + " files, " + Disks.Count + " disks"
                                             + (Missing.Count + MissingDisks.Count > 0 ? ", " + (Missing.Count + MissingDisks.Count) + " missing" : "");
    }

    internal sealed class MameConvert
    {
        private readonly MameIndex _index;
        private readonly string _source;
        private readonly Dictionary<string, Dictionary<(uint, long), ZipEntryRef>> _zips = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Source zips that could not be read, with why.</summary>
        public readonly List<string> Unreadable = new();

        public MameConvert(MameIndex index, string sourceFolder)
        {
            _index = index;
            _source = sourceFolder;
        }

        /// <summary>A source zip's files by CRC and size (null when it has no zip). Read once.</summary>
        private Dictionary<(uint, long), ZipEntryRef> ZipOf(string set)
        {
            if (set == null) return null;
            if (_zips.TryGetValue(set, out var known)) return known;
            Dictionary<(uint, long), ZipEntryRef> map = null;
            var path = Path.Combine(_source, set + ".zip");
            if (File.Exists(path))
            {
                try
                {
                    map = new Dictionary<(uint, long), ZipEntryRef>();
                    foreach (var e in ZipCentral.Read(path))
                        if (!e.Name.EndsWith("/", StringComparison.Ordinal)) map.TryAdd((e.Crc, e.Size), e);
                }
                catch (Exception ex) { Unreadable.Add(path + ": " + ex.Message); map = null; }
            }
            return _zips[set] = map;
        }

        /// <summary>The zips a machine's file can be in: its own, its parent's, the romof chain.</summary>
        private IEnumerable<string> Candidates(MameMachine m)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var walked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var x = m; x != null && walked.Add(x.Name); x = _index.Get(x.RomOf ?? x.CloneOf))
            {
                if (seen.Add(x.Name)) yield return x.Name;
                if (x.CloneOf != null && seen.Add(x.CloneOf)) yield return x.CloneOf;
            }
        }

        private readonly Dictionary<string, Dictionary<string, string>> _chds = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>A source set folder's CHDs by their header SHA-1 (lower-case hex). Read once.</summary>
        private Dictionary<string, string> ChdsOf(string set)
        {
            if (_chds.TryGetValue(set, out var known)) return known;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var dir = Path.Combine(_source, set);
            if (Directory.Exists(dir))
                foreach (var f in Directory.EnumerateFiles(dir, "*.chd"))
                {
                    var sha = ChdSha1(f);
                    if (sha != null) map.TryAdd(sha, f);
                    else Unreadable.Add(f + ": not a CHD (v3-v5) header");
                }
            return _chds[set] = map;
        }

        /// <summary>The SHA-1 a CHD's header gives (the data and metadata one, MAME's disk sha1): v5 at 84, v4 at 48, v3 at
        /// 80. Null when it is not a CHD.</summary>
        public static string ChdSha1(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                var h = new byte[124];
                if (fs.Read(h, 0, h.Length) < 108 || System.Text.Encoding.ASCII.GetString(h, 0, 8) != "MComprHD") return null;
                int version = (h[12] << 24) | (h[13] << 16) | (h[14] << 8) | h[15];
                int at = version switch { 5 => 84, 4 => 48, 3 => 80, _ => -1 };
                return at < 0 ? null : Convert.ToHexString(h, at, 20).ToLowerInvariant();
            }
            catch { return null; }
        }

        private string FindDisk(MameMachine m, MameDisk d)
        {
            if (d.Sha1 == null) return null;
            foreach (var set in Candidates(m))
                if (ChdsOf(set).TryGetValue(d.Sha1, out var path)) return path;
            // Loose beside the zips (not where MAME looks: its SHA-1 says which disk it is).
            return ChdsOf("").TryGetValue(d.Sha1, out var loose) ? loose : null;
        }

        private ZipEntryRef Find(MameMachine m, MameRom r)
        {
            foreach (var set in Candidates(m))
            {
                var zip = ZipOf(set);
                if (zip != null && zip.TryGetValue((r.Crc, r.Size), out var e)) return e;
            }
            return null;
        }

        /// <summary>The machines a list of sets needs: themselves, their parents, their romof chain, their devices with ROMs.</summary>
        public HashSet<string> Closure(IEnumerable<string> sets)
        {
            var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var todo = new Stack<string>(sets);
            while (todo.Count > 0)
            {
                var m = _index.Get(todo.Pop());
                if (m == null || !all.Add(m.Name)) continue;
                if (m.CloneOf != null) todo.Push(m.CloneOf);
                if (m.RomOf != null) todo.Push(m.RomOf);
                foreach (var d in m.DeviceRefs) todo.Push(d);
            }
            return all;
        }

        /// <summary>The zips of the target layout for these machines (all of MAME's when null), each with its files found.</summary>
        public List<MameZipPlan> Plan(MameLayout layout, ICollection<string> machines = null)
        {
            var names = machines ?? _index.Machines.Keys;
            var plans = new List<MameZipPlan>();
            foreach (var name in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                var m = _index.Get(name);
                if (m == null) continue;
                var plan = new MameZipPlan { Set = m.Name };
                var byName = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
                void Put(MameMachine owner, MameRom r, string folder)
                {
                    var file = r.Name;
                    if (byName.TryGetValue(file, out var crc))
                    {
                        if (crc == r.Crc) return;                       // the same file, once
                        if (folder == null) return;                     // a machine's own duplicate name: MAME's own sets keep one
                        file = folder + "/" + r.Name;
                        if (byName.ContainsKey(file)) return;
                    }
                    var e = Find(owner, r);
                    if (e == null) { plan.Missing.Add(r.Name + " (" + r.Crc.ToString("x8") + ", for " + owner.Name + ")"); return; }
                    byName[file] = r.Crc;
                    plan.Items.Add(new VirtualZip.Item { Name = file, Source = e });
                }

                var diskNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                void PutDisk(MameMachine owner, MameDisk d)
                {
                    var file = d.Name + ".chd";
                    if (diskNames.TryGetValue(file, out var sha))
                    {
                        if (!string.Equals(sha, d.Sha1, StringComparison.OrdinalIgnoreCase))
                            plan.MissingDisks.Add(d.Name + " (" + d.Sha1 + ", for " + owner.Name + "): another disk of that name is already in " + m.Name);
                        return;
                    }
                    var source = FindDisk(owner, d);
                    if (source == null) { plan.MissingDisks.Add(d.Name + " (" + d.Sha1 + ", for " + owner.Name + ")"); return; }
                    diskNames[file] = d.Sha1;
                    plan.Disks.Add((file, source));
                }

                switch (layout)
                {
                    case MameLayout.NonMerged:
                        foreach (var r in m.Roms) Put(m, r, null);
                        foreach (var d in m.Disks) PutDisk(m, d);
                        break;
                    case MameLayout.Split:
                        foreach (var r in m.Roms) if (r.Merge == null) Put(m, r, null);
                        foreach (var d in m.Disks) if (d.Merge == null) PutDisk(m, d);
                        break;
                    case MameLayout.Merged:
                        if (m.CloneOf != null && _index.Get(m.CloneOf) != null) continue;   // in its parent's zip
                        foreach (var r in m.Roms) if (r.Merge == null) Put(m, r, null);
                        foreach (var d in m.Disks) if (d.Merge == null) PutDisk(m, d);
                        foreach (var c in Clones(m))
                        {
                            foreach (var r in c.Roms) if (r.Merge == null) Put(c, r, c.Name);
                            foreach (var d in c.Disks) if (d.Merge == null) PutDisk(c, d);
                        }
                        break;
                }
                if (plan.Items.Count == 0 && plan.Missing.Count == 0 && plan.Disks.Count == 0 && plan.MissingDisks.Count == 0) continue;
                plan.Disks.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                plan.Items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                plans.Add(plan);
            }
            return plans;
        }

        private Dictionary<string, List<MameMachine>> _clones;

        private List<MameMachine> Clones(MameMachine parent)
        {
            if (_clones == null)
            {
                _clones = new Dictionary<string, List<MameMachine>>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in _index.Machines.Values)
                    if (m.CloneOf != null)
                    {
                        if (!_clones.TryGetValue(m.CloneOf, out var l)) _clones[m.CloneOf] = l = new List<MameMachine>();
                        l.Add(m);
                    }
                foreach (var l in _clones.Values) l.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }
            return _clones.TryGetValue(parent.Name, out var list) ? list : new List<MameMachine>();
        }
    }
}
