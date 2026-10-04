// The BIOS and firmware files, and the one thing that makes them awkward here.
//
// NO$GBA HAS NO PATH SETTING. None - the whole 2541-byte configuration it writes for itself holds
// not one path. A BIOS is found by its FIXED NAME, in the emulator's own folder, or it is not found.
// So this plugin cannot do what the melonDS one does, which is point the emulator at files wherever
// the user keeps them. It has to put a copy where no$gba will look.
//
// SO IT COPIES, from no$gba's own bios\ (Mehdi, 04/10 - RetroArch's system folder until then): the user's
// originals, never written by the pack, as melonDS keeps its own in its bios\. That folder is filled at
// install (and at the first start-up check of an installation from before 04/10) from RetroArch's system
// folder and from melonDS's bios\, when they have the files - Import. The copy beside the executable
// costs about 600 KB and is refreshed when the original changes size.
//
// AND IT ASKS FOR NOTHING. On Game Boy Advance and DS these files are OPTIONAL: no$gba's default is
// to start the cartridge directly without running the boot code at all, and the emulator runs games
// perfectly well with none of them present. They buy accuracy - real SWI behaviour, the Nintendo
// boot animation - not function. Declaring them Required would put a red badge on a working
// installation, so they are declared optional and their absence is not an event.
//
// That changes for DSi, where the BIOS and the NAND are not optional at all. When that arrives it
// brings the missing-files window with it; it has no business here.

using System;
using System.Collections.Generic;
using System.IO;
using LbIntegrations.Dsi;

namespace LbIntegrations.NoGba
{
    /// <summary>One file no$gba may use: what we call it, what it must be called there, how big it
    /// is, and what it is for.</summary>
    internal sealed class BiosFile
    {
        public string OurName;
        public string TheirName;
        public long Size;
        public string What;
        /// <summary>Other names the same file circulates under, RetroArch's first.</summary>
        public string[] AlsoKnownAs;
    }

    internal static class NoGbaBios
    {
        /// <summary>Where the user's originals live, relative to the emulator: its own bios\ (04/10), the only one read.</summary>
        public const string SourceDirName = "bios";

        private const string SEP = "\\";

        /// <summary>Where they are copied from at install, relative to the emulator: RetroArch's system folder (where they
        /// were read until 04/10) and melonDS's bios\ beside it.</summary>
        private static readonly string[] ImportDirNames = { ".." + SEP + "RetroArch" + SEP + "system", ".." + SEP + "Nixx-melonDS" + SEP + "bios" };

        /// <summary>The user's files copied into bios\ - the BIOS and firmware under any of their names, and every NAND
        /// dump - from RetroArch's system folder, melonDS's bios\ beside it and that of every other melonDS of the library
        /// (<paramref name="melonDsDirs"/>), when bios\ does not have them yet. See DsiBiosImport.</summary>
        public static List<string> Import(NoGbaLayout layout, IEnumerable<string> melonDsDirs = null, Action<string> report = null, Func<bool> cancelled = null)
        {
            var dir = SourceDir(layout);
            if (dir == null) return new List<string>();
            var sources = new List<string>();
            foreach (var name in ImportDirNames)
                try { sources.Add(Path.GetFullPath(Path.Combine(layout.InstallDir, name))); } catch { }
            foreach (var m in melonDsDirs ?? Array.Empty<string>())
                if (!string.IsNullOrEmpty(m)) sources.Add(Path.Combine(m, "bios"));
            var wanted = new List<DsiBiosImport.Wanted>();
            foreach (var file in Files) wanted.Add(new DsiBiosImport.Wanted { Name = file.OurName, Aliases = file.AlsoKnownAs ?? Array.Empty<string>() });
            return DsiBiosImport.Import(dir, sources, wanted, nands: true, report, cancelled);
        }

        /// <summary>Where no$gba looks: its own folder. Declared as "." rather than the empty string
        /// so the dependency window shows something a person can read.</summary>
        public const string TargetDirName = ".";

        /// <summary>The Game Boy Advance and Nintendo DS files. The DSi ones are deliberately absent
        /// until the DSi support that needs them exists - see the header.
        ///
        /// Names on the right are no$gba's own, read from its installation notes (GBATEK,
        /// "File Locations and Names"); the sizes are the real chips'.</summary>
        public static readonly BiosFile[] Files =
        {
            new BiosFile
            {
                OurName = "biosgba.bin", TheirName = "BIOSGBA.ROM", Size = 16 * 1024,
                What = "Game Boy Advance BIOS",
                AlsoKnownAs = new[] { "gba_bios.bin", "GBA.ROM" },
            },
            new BiosFile
            {
                OurName = "biosnds7.bin", TheirName = "BIOSNDS7.ROM", Size = 16 * 1024,
                What = "DS ARM7 BIOS",
                AlsoKnownAs = new[] { "bios7.bin" },
            },
            new BiosFile
            {
                OurName = "biosnds9.bin", TheirName = "BIOSNDS9.ROM", Size = 4 * 1024,
                What = "DS ARM9 BIOS",
                AlsoKnownAs = new[] { "bios9.bin" },
            },
            new BiosFile
            {
                OurName = "dsfirmware.bin", TheirName = "FIRMWARE.BIN", Size = 256 * 1024,
                What = "DS firmware",
                AlsoKnownAs = new[] { "firmware.bin" },
            },

            // THE TWO DSi HALVES, and they are not optional the way the four above are. Without
            // them no$gba cannot enter DSi mode at all, so a DSiWare launch stops before it starts
            // rather than quietly running as a DS. Measured: with these two in place and
            // NDS Mode/Colors set, no$gba boots the DSi menu out of our own eMMC image.
            //
            // gbatek says a DSi BIOS dump is "only 66% dumpable" - the lower halves come from
            // exploits and the rest is zero-filled - so 64 KB each is what a good dump looks like,
            // not a full chip.
            new BiosFile
            {
                OurName = "biosdsi7.bin", TheirName = "BIOSDSI7.ROM", Size = 64 * 1024,
                What = "DSi ARM7 BIOS",
                AlsoKnownAs = new[] { "dsi_bios7.bin", "bios7i.bin" },
            },
            new BiosFile
            {
                OurName = "biosdsi9.bin", TheirName = "BIOSDSI9.ROM", Size = 64 * 1024,
                What = "DSi ARM9 BIOS",
                AlsoKnownAs = new[] { "dsi_bios9.bin", "bios9i.bin" },
            },
        };

        /// <summary>Where a NAND dump is looked for: no$gba's bios\, the only one read (04/10). Each emulator keeps its
        /// own consoles, built from its own copy of the originals.</summary>
        public static IEnumerable<string> SearchFolders(NoGbaLayout layout)
        {
            var dir = SourceDir(layout);
            if (dir != null) yield return dir;
        }

        /// <summary>The folder the user's originals are read from, absolute: &lt;no$gba&gt;\bios.</summary>
        public static string SourceDir(NoGbaLayout layout)
        {
            try
            {
                if (layout?.InstallDir == null) return null;
                return Path.GetFullPath(Path.Combine(layout.InstallDir, SourceDirName));
            }
            catch { return null; }
        }

        /// <summary>Find a file under any of its names, in the shared folder. Null when it is not
        /// there under any of them.</summary>
        public static string Find(NoGbaLayout layout, BiosFile file)
        {
            try
            {
                var dir = SourceDir(layout);
                if (dir == null || !Directory.Exists(dir)) return null;

                var names = new List<string> { file.OurName };
                names.AddRange(file.AlsoKnownAs ?? Array.Empty<string>());
                foreach (var name in names)
                {
                    var path = Path.Combine(dir, name);
                    if (File.Exists(path)) return path;
                }
                return null;
            }
            catch { return null; }
        }

        /// <summary>Put a copy of every file we can find where no$gba will look for it.
        ///
        /// Copies only what is missing or a different size, so the steady state is one FileInfo per
        /// file per launch and no writing at all. A file the user does not have is simply skipped:
        /// on GBA and DS the emulator does not need it.</summary>
        public static int Sync(NoGbaLayout layout)
        {
            int copied = 0;
            try
            {
                if (layout?.InstallDir == null) return 0;

                foreach (var file in Files)
                {
                    var source = Find(layout, file);
                    if (source == null) continue;

                    var target = Path.Combine(layout.InstallDir, file.TheirName);
                    try
                    {
                        var from = new FileInfo(source);
                        if (File.Exists(target) && new FileInfo(target).Length == from.Length) continue;

                        File.Copy(source, target, overwrite: true);
                        copied++;
                        Log.Info("copied " + Path.GetFileName(source) + " to " + file.TheirName
                                 + " - no$gba has no path setting, so it only reads that name, in its "
                                 + "own folder");
                    }
                    catch (Exception ex)
                    {
                        Log.Verbose("could not copy " + file.TheirName + " - " + ex.Message);
                    }
                }
            }
            catch (Exception ex) { Log.Warn("could not sync the BIOS files", ex); }
            return copied;
        }

        /// <summary>The size a file should be, for the amber badge. Answers null when it is the
        /// right size or not there at all.</summary>
        public static string SizeComplaint(NoGbaLayout layout, BiosFile file)
        {
            try
            {
                var source = Find(layout, file);
                if (source == null) return null;
                var length = new FileInfo(source).Length;
                if (length == file.Size) return null;
                return "is " + length + " bytes; " + file.What + " is " + file.Size;
            }
            catch { return null; }
        }
    }
}
