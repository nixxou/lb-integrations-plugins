// The BIOS files Mesen needs, found where the user already keeps them and put in Mesen's Firmware folder.
//
// MESEN READS ONE FOLDER, BY NAME AND SIZE (Core\Shared\FirmwareHelper.h, both tags): <home>\Firmware\<name>, or its
// alternate name (disksys.rom / FdsBios.bin, syscard3.pce...), loaded only when its size is the one expected - the content is
// never checked. A required one missing makes Mesen show a modal "firmware not found" box with a file picker
// (FirmwareHelper.RequestFirmwareFile - read, not measured: it would have waited on the screen); the plugin refuses the launch
// before that, with a message naming the file (MesenPlugin.PrepareCore).
//
// SO THE PLUGIN, for a system (at install for all, at a launch for the game's, from the Nixx page on demand):
//   1. Mesen's file there under one of its names with a size Mesen takes: kept, nothing done;
//   2. else a file of the Firmware folder whose SHA-256 is one Mesen knows (UI\Interop\FirmwareTypeExtensions.cs), under
//      another name: copied under Mesen's name;
//   3. else COPIED in from RetroArch's system folder and the bios / system / firmware folders of the other emulators of the
//      library (LbipBiosImport: by name, only what is missing, never a move).
// A file found whose size Mesen does not take is said (Mesen would not load it); one of the right size whose SHA-256 is not
// one Mesen knows is still Mesen's to load, and said in the log.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using LbIntegrations.Lbip;

namespace LbIntegrations.Mesen
{
    internal sealed class MesenBiosState
    {
        public MesenSystem System;
        public MesenFirmware Spec;
        public string Path;        // the file Mesen will load, or null
        public bool SizeOk, HashOk;
        /// <summary>Needed to start this system (Super Game Boy: only the cartridge Mesen's UseSgb2 picks).</summary>
        public bool Required;
    }

    internal static class MesenBios
    {
        public const string Folder = "Firmware";

        private static readonly Dictionary<string, (DateTime When, string Sha)> Hashes = new Dictionary<string, (DateTime, string)>(StringComparer.OrdinalIgnoreCase);

        internal static string Sha256Of(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length >= 4L << 20) return null;
                lock (Hashes)
                    if (Hashes.TryGetValue(info.FullName, out var h) && h.When == info.LastWriteTimeUtc) return h.Sha;
                using var sha = SHA256.Create();
                using var s = File.OpenRead(info.FullName);
                var hex = Convert.ToHexString(sha.ComputeHash(s)).ToLowerInvariant();
                lock (Hashes) Hashes[info.FullName] = (info.LastWriteTimeUtc, hex);
                return hex;
            }
            catch { return null; }
        }

        public static string FolderOf(string exe) => System.IO.Path.Combine(MesenLaunch.HomeOf(exe), Folder);

        /// <summary>Is <paramref name="spec"/> needed to start <paramref name="system"/>? The Super Game Boy needs the cartridge
        /// Gameboy.UseSgb2 names (true, Mesen's default: SGB2.sfc).</summary>
        internal static bool IsRequired(MesenSystem system, MesenFirmware spec, string exe)
        {
            if (spec.Optional) return false;
            if (spec == MesenSystems.Sgb1 || spec == MesenSystems.Sgb2)
            {
                bool sgb2 = true;
                try { sgb2 = MesenJson.Load(MesenLaunch.SettingsOf(exe)).GetBool("Gameboy/UseSgb2") ?? true; } catch { }
                return spec == (sgb2 ? MesenSystems.Sgb2 : MesenSystems.Sgb1);
            }
            return true;
        }

        private static List<LbipBiosImport.Source> Sources(string exe, HashSet<string> names)
        {
            var sources = new List<LbipBiosImport.Source>();
            try
            {
                var own = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(exe)).TrimEnd('\\');
                var emulators = System.IO.Path.GetDirectoryName(own);
                foreach (var emu in Directory.EnumerateDirectories(emulators))
                {
                    if (string.Equals(System.IO.Path.GetFullPath(emu).TrimEnd('\\'), own, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var sub in new[] { "system", "bios", "BIOS", "Bios", "firmware", "Firmware", "data" })
                    {
                        var dir = System.IO.Path.Combine(emu, sub);
                        if (Directory.Exists(dir)) sources.Add(new LbipBiosImport.Source { Dir = dir, Recursive = true, Only = names });
                    }
                }
            }
            catch (Exception ex) { Log.Warn("bios: could not list the other emulators", ex); }
            return sources;
        }

        /// <summary>The firmware of <paramref name="systems"/> found, copied when it has to be. One state per firmware of each
        /// system, in order. Never throws.</summary>
        /// <param name="optional">Copy the optional ones too (install, the Nixx page); a launch leaves them out.</param>
        /// <param name="readOnly">Only say what is there (a window): nothing copied.</param>
        public static List<MesenBiosState> Ensure(string exe, IEnumerable<MesenSystem> systems, Action<string> report = null, bool optional = true, bool readOnly = false)
        {
            var states = new List<MesenBiosState>();
            try
            {
                var list = systems.Where(s => s != null).Distinct().ToList();
                var target = FolderOf(exe);
                var specs = list.SelectMany(s => s.Firmware).Distinct().ToList();
                if (specs.Count == 0) return states;

                if (!readOnly)
                {
                    // 2. By content, under another name in the Firmware folder.
                    var copyable = specs.Where(f => (optional || !f.Optional) && Find(target, f).Path == null).ToList();
                    if (copyable.Count > 0 && Directory.Exists(target))
                    {
                        var files = LbipBiosImport.Listing(target, recursive: true);
                        foreach (var f in copyable)
                        {
                            var hit = files.FirstOrDefault(p => f.Sha256.Contains(Sha256Of(p) ?? "", StringComparer.OrdinalIgnoreCase));
                            if (hit == null) continue;
                            var to = System.IO.Path.Combine(target, f.File);
                            if (LbipBiosImport.Copy(hit, to, report)) Log.Info("bios: " + hit + " is Mesen's " + f.Label + " - copied as " + f.File);
                        }
                    }
                    // 3. From the other emulators' folders.
                    var wanted = specs.Where(f => (optional || !f.Optional) && Find(target, f).Path == null)
                                      .Select(f => new LbipBiosImport.Wanted { Name = f.File, Aliases = f.Names.Skip(1).ToArray() }).ToList();
                    if (wanted.Count > 0)
                    {
                        Directory.CreateDirectory(target);
                        var names = new HashSet<string>(wanted.SelectMany(w => w.Names), StringComparer.OrdinalIgnoreCase);
                        var copied = LbipBiosImport.Import(target, Sources(exe, names), wanted, report);
                        if (copied.Count > 0) Log.Info("bios: copied " + string.Join(", ", copied));
                    }
                }

                foreach (var s in list)
                    foreach (var f in s.Firmware)
                    {
                        var (path, sizeOk) = Find(target, f);
                        var st = new MesenBiosState { System = s, Spec = f, Path = path, SizeOk = sizeOk, Required = IsRequired(s, f, exe) };
                        if (path != null && sizeOk)
                        {
                            st.HashOk = f.Sha256.Length == 0 || f.Sha256.Contains(Sha256Of(path) ?? "", StringComparer.OrdinalIgnoreCase);
                            if (!st.HashOk) Log.Verbose("bios: " + path + " has the size Mesen takes but not a SHA-256 it knows - Mesen loads it all the same");
                        }
                        else if (path != null) Log.Info("bios: " + path + " is not of a size Mesen loads for " + f.Label + " (" + new FileInfo(path).Length + " bytes)");
                        states.Add(st);
                    }
            }
            catch (Exception ex) { Log.Warn("bios", ex); }
            return states;
        }

        /// <summary>The file Mesen loads for <paramref name="f"/> (its name, then its alternate name - FirmwareHelper's order):
        /// the path of the first there, and whether its size is one Mesen takes.</summary>
        internal static (string Path, bool SizeOk) Find(string target, MesenFirmware f)
        {
            string first = null;
            foreach (var n in new[] { f.File, f.Alt }.Where(x => !string.IsNullOrEmpty(x)))
            {
                var p = System.IO.Path.Combine(target, n);
                if (!File.Exists(p)) continue;
                long size; try { size = new FileInfo(p).Length; } catch { continue; }
                if (f.SizeFits(size)) return (p, true);
                first ??= p;
            }
            return (first, false);
        }

        /// <summary>A system starts when each firmware it needs is there with a size Mesen takes.</summary>
        public static bool Enough(IEnumerable<MesenBiosState> states) => states.Where(s => s.Required).All(s => s.Path != null && s.SizeOk);

        public static IEnumerable<MesenBiosState> Missing(IEnumerable<MesenBiosState> states) => states.Where(s => s.Required && (s.Path == null || !s.SizeOk));
    }
}
