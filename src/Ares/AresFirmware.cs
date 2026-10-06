// The BIOS files ares needs, found where the user already keeps them and given to ares by path.
//
// ARES ASKS FOR NO FILE NAME (desktop-ui\settings\settings.cpp:233-236): a firmware is a path in settings.bml,
// "<system node>/Firmware/<type>.<region>" ("PlayStation/Firmware/BIOS.US"), read as it is when the game starts - its
// SHA-256 is not checked then. Its own Firmware > Scan button (settings\firmware.cpp) hashes the files of one folder,
// skips those of 10 MiB and more, and cannot find Saturn's nor Neo Geo's, which carry no hash.
//
// SO THE PLUGIN, for a system (at install for all, at a launch for the game's):
//   1. a key already pointing at a file that is there is the user's choice and is kept;
//   2. else the file in <ares>\Firmware\ - by its usual names (AresSystems), then BY ITS SHA-256 whatever its name:
//      a file the user dropped there is found;
//   3. else it is COPIED there from RetroArch's system folder and the bios / system / firmware folders of the other
//      emulators of the library (LbipBiosImport: by name, only what is missing, never a move); neogeo.zip also from the
//      folders of the arcade games;
//   4. the key written with the file's full path.
// A file found by name whose SHA-256 is not ares's is still given (ares checks nothing) and said in the log.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using LbIntegrations.Lbip;

namespace LbIntegrations.Ares
{
    internal sealed class AresFirmwareState
    {
        public AresFirmwareSpec Spec;
        public string Path;       // the file ares is given, or null
        public bool HashOk;       // its SHA-256 is ares's (or ares has none for it)
    }

    internal static class AresFirmware
    {
        public const string Folder = "Firmware";

        private static readonly Dictionary<string, (DateTime When, string Sha)> Hashes = new Dictionary<string, (DateTime, string)>(StringComparer.OrdinalIgnoreCase);

        internal static string Sha256Of(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length >= 10L << 20) return null;
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

        /// <summary>Where the user's dumps may be: RetroArch's system folder and the bios, system and firmware folders of
        /// the other emulators beside this one (Emulators\*\), with their sub-folders.</summary>
        private static List<LbipBiosImport.Source> Sources(string aresDir, HashSet<string> names, bool neoGeo)
        {
            var sources = new List<LbipBiosImport.Source>();
            try
            {
                var emulators = Path.GetDirectoryName(Path.GetFullPath(aresDir));
                foreach (var emu in Directory.EnumerateDirectories(emulators))
                {
                    if (string.Equals(Path.GetFullPath(emu).TrimEnd('\\'), Path.GetFullPath(aresDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var sub in new[] { "system", "bios", "BIOS", "Bios", "firmware", "Firmware", "data" })
                    {
                        var dir = Path.Combine(emu, sub);
                        if (Directory.Exists(dir)) sources.Add(new LbipBiosImport.Source { Dir = dir, Recursive = true, Only = names });
                    }
                }
            }
            catch (Exception ex) { Log.Warn("firmware: could not list the other emulators", ex); }
            if (neoGeo)
                foreach (var dir in LbipBiosImport.ArcadeRomDirs())
                    sources.Add(new LbipBiosImport.Source { Dir = dir, Recursive = false, Only = new HashSet<string>(new[] { "neogeo.zip" }, StringComparer.OrdinalIgnoreCase) });
            return sources;
        }

        /// <summary>The firmware of <paramref name="systems"/> found, copied when it has to be, and written into settings.bml.
        /// Never throws. One state per firmware entry, in the systems' order.</summary>
        /// <param name="optional">Copy the optional ones too (at install and from the Nixx page; a launch leaves them out - a
        /// Master System game must not search every emulator's folders each time for a BIOS it does not need).</param>
        /// <param name="readOnly">Only say what is there: nothing copied, nothing written (a game's window).</param>
        public static List<AresFirmwareState> Ensure(string exe, IEnumerable<AresSystem> systems, Action<string> report = null, bool optional = true, bool readOnly = false)
        {
            var states = new List<AresFirmwareState>();
            try
            {
                var aresDir = Path.GetDirectoryName(Path.GetFullPath(exe));
                var target = Path.Combine(aresDir, Folder);
                var owners = systems.Where(s => s != null).Select(s => AresSystems.FirmwareFor(s).Owner).Where(o => o != null && o.Firmware.Length > 0)
                                    .Distinct().ToList();
                if (owners.Count == 0) return states;
                // ares writes settings.bml back when it quits: while it runs nothing is written (nor copied - said only).
                if (!readOnly && AresLaunch.Running(exe)) readOnly = true;
                if (!readOnly) Directory.CreateDirectory(target);

                // What is missing from the Firmware folder, copied there first - one pass for all.
                var bml = AresBml.Load(AresLaunch.SettingsOf(exe));
                var wanted = new List<LbipBiosImport.Wanted>();
                foreach (var o in owners)
                    foreach (var spec in o.Firmware)
                        if (!readOnly && (optional || !spec.Optional) && Given(bml, o, spec) == null && Local(target, spec) == null && spec.Names.Length > 0)
                            wanted.Add(new LbipBiosImport.Wanted { Name = spec.Names[0], Aliases = spec.Names.Skip(1).ToArray() });
                if (wanted.Count > 0)
                {
                    var names = new HashSet<string>(wanted.SelectMany(w => w.Names), StringComparer.OrdinalIgnoreCase);
                    var copied = LbipBiosImport.Import(target, Sources(aresDir, names, owners.Any(o => o.Name.StartsWith("Neo Geo") && !o.Name.Contains("Pocket"))),
                                                      wanted.GroupBy(w => w.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()), report);
                    if (copied.Count > 0) Log.Info("firmware: copied " + string.Join(", ", copied));
                }

                foreach (var o in owners)
                    foreach (var spec in o.Firmware)
                    {
                        var path = Given(bml, o, spec) ?? Local(target, spec);
                        bool borrowed = false;
                        if (path == null && spec.FallbackKey != null)
                        {
                            var other = o.Firmware.FirstOrDefault(s => s.Key == spec.FallbackKey);
                            path = other == null ? null : Given(bml, o, other) ?? Local(target, other);
                            borrowed = path != null;
                        }
                        var state = new AresFirmwareState { Spec = spec, Path = path };
                        if (path != null)
                        {
                            var sha = spec.Sha256 == null ? null : Sha256Of(path);
                            state.HashOk = !borrowed && (spec.Sha256 == null || string.Equals(sha, spec.Sha256, StringComparison.OrdinalIgnoreCase)
                                           || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                            if (!state.HashOk) Log.Info("firmware: " + o.Name + " " + spec.Label + " is " + path + ", whose SHA-256 " + sha + " is not the dump ares knows - given anyway");
                            if (!readOnly) bml.Set(o.Node + "/Firmware/" + spec.Key, path.Replace('\\', '/'));
                        }
                        states.Add(state);
                    }
                if (!readOnly) bml.SaveIfChanged();
            }
            catch (Exception ex) { Log.Warn("firmware", ex); }
            return states;
        }

        /// <summary>What settings.bml already gives, when that file is there.</summary>
        private static string Given(AresBml bml, AresSystem owner, AresFirmwareSpec spec)
        {
            var v = bml.Get(owner.Node + "/Firmware/" + spec.Key);
            if (string.IsNullOrWhiteSpace(v)) return null;
            try { var p = Path.GetFullPath(v.Replace('/', Path.DirectorySeparatorChar)); return File.Exists(p) ? p : null; }
            catch { return null; }
        }

        /// <summary>The spec's file in the Firmware folder: by one of its names, else by its SHA-256.</summary>
        private static string Local(string target, AresFirmwareSpec spec)
        {
            if (!Directory.Exists(target)) return null;
            foreach (var n in spec.Names)
            {
                var p = Path.Combine(target, n);
                if (File.Exists(p)) return p;
            }
            if (spec.Sha256 == null) return null;
            foreach (var file in LbipBiosImport.Listing(target, recursive: true))
                if (string.Equals(Sha256Of(file), spec.Sha256, StringComparison.OrdinalIgnoreCase)) return file;
            return null;
        }

        /// <summary>A system starts when one of its firmware entries that is not optional is given (the game's region picks
        /// which - ares says so itself when it is not the one there).</summary>
        public static bool Enough(IEnumerable<AresFirmwareState> states)
        {
            var needed = states.Where(s => !s.Spec.Optional).ToList();
            return needed.Count == 0 || needed.Any(s => s.Path != null);
        }
    }
}
