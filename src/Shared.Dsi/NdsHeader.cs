// The two things a DS ROM tells us about itself, read where melonDS reads them.
//
// UnitCode at 0x12 and DSiTitleIDHigh at 0x234, with the predicates copied from melonDS rather than
// paraphrased (src/NDS_Header.h:206 and :219):
//
//     IsDSi()     = (UnitCode & 0x02) != 0
//     IsDSiWare() = IsDSi() && DSiTitleIDHigh == 0x00030004
//
// The offsets follow from the struct at NDS_Header.h:44-52 - GameTitle[12], GameCode[4],
// MakerCode[2], then UnitCode - and the DSiWare constant is named there too, DSiWareTitleIDHigh.
//
// THE SAME READ ANSWERS A SECOND QUESTION, which is why this file also opens archives. melonDS names
// a save after the ROM it loaded, and when the ROM came out of a zip that is the name of the entry
// INSIDE the archive, not the archive's own (EmuInstance.cpp, loadROMData: basepath is the archive's
// directory while romname is the inner file). So the plugin has to look inside a .zip or .7z to know
// what a save will be called - and once it is in there, the header is free.

using System;
using System.IO;

namespace LbIntegrations.Dsi
{
    /// <summary>What a ROM turned out to be. Every field is best-effort: a ROM we could not read
    /// reports <see cref="Known"/> false and the caller carries on in DS mode, which is what melonDS
    /// would have done anyway.</summary>
    internal sealed class NdsRom
    {
        /// <summary>The name melonDS will use for the save and the savestates: the ROM's file name
        /// with its last extension removed. For an archive, the INNER entry's name.</summary>
        public string AssetName;

        /// <summary>The directory melonDS falls back to when no save path is configured: the folder
        /// holding the ROM - or, for an archive, the folder holding the archive.</summary>
        public string RomDir;

        public bool Known;
        public bool IsDSi;
        public bool IsDSiWare;

        /// <summary>The DSiWare title id, which names the per-game NAND. Written the way the DSi
        /// itself spells it in the NAND tree, high word first: title/&lt;high&gt;/&lt;low&gt;.</summary>
        public uint DSiTitleIdLow;
        public uint DSiTitleIdHigh;

        /// <summary>DSiRegionMask: which console regions accept this title, as a bitmask
        /// (NDS_Header.h:29-39). Zero when the field was never filled in, which is not the same as
        /// "no region" - see DsiRegions, which treats it as "no answer" and asks elsewhere.</summary>
        public uint DSiRegionMask;

        public string TitleId => DSiTitleIdHigh.ToString("x8") + DSiTitleIdLow.ToString("x8");
    }

    internal static class NdsHeader
    {
        private const int UnitCodeOffset = 0x12;
        private const int DSiTitleIdOffset = 0x230;      // low word; the high word follows at 0x234
        private const uint DSiWareTitleIdHigh = 0x00030004;

        /// <summary>How much of the ROM has to be read. 0x238 would do; a round 1 KiB costs nothing
        /// and survives a struct that grows.</summary>
        private const int HeadBytes = 1024;

        /// <summary>What melonDS calls a DS ROM (Window.cpp:95). Public because the DSiWare path
        /// has to pull one of these out of an archive before it can install it.</summary>
        public static readonly string[] RomExtensions = { ".nds", ".srl", ".dsi", ".ids" };
        /// <summary>Containers a ROM may arrive in. LONGEST FIRST, because these are matched against
        /// the end of a file name rather than against Path.GetExtension - which answers ".gz" for
        /// "Game.tar.gz" and would have made every tarball look like something else.
        ///
        /// This is NOT melonDS's list. melonDS opens archives itself through libarchive and accepts
        /// a longer one, but a container this plugin cannot open is a container whose inner entry
        /// name it cannot read - and that name is what the save is called. Declaring one would mean
        /// silently misfiling saves. What is here is what SharpCompress was measured to open; see
        /// the ArchiveFormats part of the probe, which hands it one of each.</summary>
        private static readonly string[] ArchiveExtensions =
        {
            ".tar.gz", ".tar.bz2", ".tar.xz", ".tar.lz", ".tar.zst",
            ".tgz", ".tbz2", ".txz", ".tzst",
            ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz",
        };

        /// <summary>Everything the plugin needs to know about the file the host is about to launch, or
        /// about a game it is listing saves for. Never throws.</summary>
        public static NdsRom Describe(string romPath)
        {
            var rom = new NdsRom();
            try
            {
                if (string.IsNullOrWhiteSpace(romPath)) return rom;

                rom.RomDir = Path.GetDirectoryName(romPath);
                rom.AssetName = StripLastExtension(Path.GetFileName(romPath));

                byte[] head = null;
                if (IsArchive(romPath))
                {
                    // The archive's own name is NOT the save's name; the entry's is.
                    if (File.Exists(romPath)
                        && Archives.TryReadFirstEntry(romPath, RomExtensions, HeadBytes, out var entry, out head))
                        rom.AssetName = StripLastExtension(entry);
                }
                else if (File.Exists(romPath))
                {
                    head = ReadHead(romPath);
                }

                if (head == null || head.Length < DSiTitleIdOffset + 8) return rom;

                rom.Known = true;
                rom.IsDSi = (head[UnitCodeOffset] & 0x02) != 0;
                rom.DSiTitleIdLow = ReadU32(head, DSiTitleIdOffset);
                rom.DSiTitleIdHigh = ReadU32(head, DSiTitleIdOffset + 4);
                if (head.Length >= DsiRegions.MaskOffset + 4)
                    rom.DSiRegionMask = ReadU32(head, DsiRegions.MaskOffset);
                rom.IsDSiWare = rom.IsDSi && rom.DSiTitleIdHigh == DSiWareTitleIdHigh;
            }
            catch (Exception ex) { DsiLog.Verbose("could not read the header of " + romPath + " - " + ex.Message); }
            return rom;
        }

        public static bool IsArchive(string path)
        {
            try
            {
                var name = Path.GetFileName(path) ?? "";
                foreach (var candidate in ArchiveExtensions)
                    if (name.EndsWith(candidate, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            return false;
        }

        /// <summary>melonDS cuts at the LAST dot: baseAssetName = romname.substr(0, romname.rfind('.')).
        /// A name with no dot is kept whole, which is what substr does when rfind returns npos... and
        /// what the standard library would NOT do, so this is written out rather than assumed.</summary>
        private static string StripLastExtension(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return fileName;
            int dot = fileName.LastIndexOf('.');
            return dot <= 0 ? fileName : fileName.Substring(0, dot);
        }

        private static byte[] ReadHead(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[HeadBytes];
            int total = 0, read;
            while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0)
                total += read;
            if (total == buffer.Length) return buffer;
            var exact = new byte[total];
            Array.Copy(buffer, exact, total);
            return exact;
        }

        private static uint ReadU32(byte[] data, int offset)
            => (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
    }
}
