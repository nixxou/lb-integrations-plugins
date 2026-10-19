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
        public override string ToString() => Set + ".zip: " + Items.Count + " files" + (Missing.Count > 0 ? ", " + Missing.Count + " missing" : "");
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

                switch (layout)
                {
                    case MameLayout.NonMerged:
                        foreach (var r in m.Roms) Put(m, r, null);
                        break;
                    case MameLayout.Split:
                        foreach (var r in m.Roms) if (r.Merge == null) Put(m, r, null);
                        break;
                    case MameLayout.Merged:
                        if (m.CloneOf != null && _index.Get(m.CloneOf) != null) continue;   // in its parent's zip
                        foreach (var r in m.Roms) if (r.Merge == null) Put(m, r, null);
                        foreach (var c in Clones(m))
                            foreach (var r in c.Roms) if (r.Merge == null) Put(c, r, c.Name);
                        break;
                }
                if (plan.Items.Count == 0 && plan.Missing.Count == 0) continue;
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
