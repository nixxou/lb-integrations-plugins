// Getting no$gba a ROM it can actually open.
//
// IT CANNOT OPEN AN ARCHIVE. Not zip, not 7z, not anything. Measured: handed a .zip on the command
// line it puts up a modal box reading "Cartridge not found" and waits for somebody to click it. From
// a frontend that is worse than a refusal - the launch does not fail, it HANGS, behind a dialog
// nobody was expecting. And a LaunchBox library is full of zipped ROMs.
//
// SO LAUNCHBOX UNPACKS IT, not this plugin. The host has a purpose-built flag for exactly this
// case - AutoExtract on the emulator - which extracts the archive and hands the emulator the file
// inside. Doing it ourselves would mean rewriting the command line, and the host appends the ROM
// path AFTER whatever NewCommandLine returns: there is no way to substitute a different path from
// there. The flag is therefore set in three places, because an emulator can arrive by three routes:
// in the metadata row a new entry is seeded from, on the entry this plugin creates, and on any
// claimed emulator that does not have it - see NoGbaPlugin.
//
// FORCED, not merely suggested, and that is a deliberate override of a user setting. For most
// emulators AutoExtract is a preference; here it is the difference between launching and hanging.
//
// WHAT IS STILL OURS is the NAME. no$gba files a save as BATTERY\<rom file name without its last
// extension>.SAV, reading nothing out of the ROM - measured, "mk.nds" gave "BATTERY\mk.SAV". When
// the host unpacks an archive the emulator sees the INNER entry's name, so that is the name the
// save carries. But the library still knows the game by its archive, so listing its save means
// looking inside the archive for the entry name. That is all this file does now.

using System;
using System.IO;

namespace LbIntegrations.NoGba
{
    internal static class NoGbaRoms
    {
        /// <summary>What no$gba can be handed directly. DS first, then Game Boy Advance - the
        /// emulator does both, and this plugin claims both platforms.</summary>
        public static readonly string[] RomExtensions =
        {
            ".nds", ".srl", ".dsi", ".ids",      // Nintendo DS
            ".gba", ".agb", ".mb",               // Game Boy Advance (.mb is a multiboot image)
        };

        /// <summary>Containers a ROM may arrive in. LONGEST FIRST, because these are matched against
        /// the end of a file name rather than through Path.GetExtension - which answers ".gz" for
        /// "Game.tar.gz" and would have made every tarball look like something else. Taken from the
        /// melonDS plugin, where the list is what SharpCompress was measured to open rather than what
        /// it advertises: a container we cannot look into is one whose inner name we cannot read, and
        /// that name is what the save is called.</summary>
        private static readonly string[] ArchiveExtensions =
        {
            ".tar.gz", ".tar.bz2", ".tar.xz", ".tar.lz", ".tar.zst",
            ".tgz", ".tbz2", ".txz", ".tzst",
            ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz",
        };

        /// <summary>Every extension the emulator entry should claim: the ROMs plus the containers
        /// the host will unpack for it.</summary>
        public static string DeclaredExtensions()
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var ext in RomExtensions) parts.Add(ext);
            foreach (var ext in new[] { ".zip", ".7z", ".rar", ".tar", ".tgz" }) parts.Add(ext);
            return string.Join("; ", parts);
        }

        public static bool IsArchive(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            foreach (var ext in ArchiveExtensions)
                if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>The name a save will be filed under for this ROM: the inner entry's when it came
        /// out of an archive, the file's own otherwise.
        ///
        /// Reading an archive costs an open, so this is called only where the answer is needed - once
        /// per game in a save listing, never on the launch path.</summary>
        public static string AssetName(string romPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(romPath)) return null;
                if (!IsArchive(romPath)) return NoGbaPaths.SaveBaseName(romPath);

                if (Archives.TryReadFirstEntry(romPath, RomExtensions, 1, out var entry, out _)
                    && !string.IsNullOrEmpty(entry))
                    return NoGbaPaths.SaveBaseName(entry);

                // An archive we could not look into. Its own name is the best guess left, and it is
                // said out loud, because a save filed under it would be filed under the wrong name.
                Log.Verbose("could not read an entry name out of " + Path.GetFileName(romPath)
                            + "; its save will be looked for under the archive's own name");
                return NoGbaPaths.SaveBaseName(romPath);
            }
            catch { return NoGbaPaths.SaveBaseName(romPath); }
        }
    }
}
