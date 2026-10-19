// A game's backup RAM CARTRIDGE in Ymir - plugged for its session only when the game has one (Mehdi 20/10).
//
// WHY: Beetle Saturn always plugs a 4 Mbit backup cartridge (its <rom>.bcr, 512 KiB); a game may have saved there. Ymir plugs
// none by default ([Cartridge] Type = 'None'). Always plugging one would split a game's saves between the console and the
// cartridge, so the plugin plugs it only for a game that HAS one: <install>\Saves\<platform>\<game>.bcr, beside its backup RAM
// - made by an import from RetroArch whose cartridge holds saves, or restored from LaunchBox.
//
// THE SAME FORMAT (read 20/10, sys\backup_ram.cpp): Ymir's 4 Mbit cartridge is a 512 KiB image in blocks of 512 bytes, its
// first block "BackUpRam Format" over and over, the second empty - byte for byte the .bcr Beetle writes. Ymir maps the file and
// writes into it as the game runs.
//
// THE TRAP: an image whose size is not the capacity set is RESIZED AND FORMATTED by Ymir (BackupMemory::CreateFrom) - its saves
// gone. So the session forces Capacity = '4Mbit' with the path, and only a file of exactly 512 KiB with that header is plugged.
//
// AND AutoLoadGameCarts OFF for the session: Ymir plugs the cartridge a game NEEDS (RAM expansion, KoF '95 / Ultraman ROM) from
// its game database - and plugs NOTHING for a game of that database that needs none (rom_service.cpp LoadRecommendedCartridge:
// "case None: break"), whatever the settings say. The games that need another cartridge (db\game_db.cpp at 0.4.1) never get
// the backup cartridge: theirs comes first.

using System;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Ymir
{
    internal static class YmirCart
    {
        public const int Size = 512 * 1024;
        private const int Block = 512;

        /// <summary>db\game_db.cpp (0.4.1): the product codes whose game needs a DRAM or ROM cartridge.</summary>
        private static readonly string[] NeedsOther =
        {
            "MK-81088", "T-3101G", "T-13308G", "T-1521G", "T-9904G", "T-1217G", "GS-9107", "T-20109G", "T-14411G", "T-7032H-50", "T-1215G",
            "T-3111G", "T-22205G", "T-20114G", "T-3105G", "T-99901G", "T-3119G", "T-3116G", "T-3104G", "T-16509G", "T-16510G", "610636008",
            "T-3108G", "T-3121G", "T-1515G", "T-1245G", "T-1248G", "T-1238G", "T-1230G", "T-1246G", "T-1229G", "6106881", "T-1226G",
        };

        /// <summary>And by disc hash: Heart of Darkness (Japan) (Prototype), a 48 Mbit DRAM cartridge.</summary>
        private const string NeedsOtherHash = "CFA7E24F43C986F7051DAF831876C5FD";

        public static bool NeedsOtherCart(SaturnDisc disc)
            => disc != null && (NeedsOther.Contains((disc.Product ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                                || string.Equals(disc.Hash, NeedsOtherHash, StringComparison.OrdinalIgnoreCase));

        /// <summary>A 4 Mbit backup cartridge image: 512 KiB, its header first. Null when it is one, else why not.</summary>
        public static string WhyNot(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return Path.GetFileName(path) + " is not there.";
                if (info.Length != Size) return Path.GetFileName(path) + " is " + info.Length + " bytes, not a 4 Mbit backup cartridge (512 KB).";
                var head = new byte[16];
                using (var s = info.OpenRead()) s.Read(head, 0, 16);
                return Encoding.ASCII.GetString(head) == "BackUpRam Format" ? null : Path.GetFileName(path) + " does not start as a Saturn backup cartridge.";
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>The cartridge holds at least one save: a block starting a file (0x80000000), past the header's two.</summary>
        public static bool HasSaves(string path)
        {
            try
            {
                if (WhyNot(path) != null) return false;
                var d = File.ReadAllBytes(path);
                for (int off = 2 * Block; off + 4 <= d.Length; off += Block)
                    if (d[off] == 0x80 && d[off + 1] == 0 && d[off + 2] == 0 && d[off + 3] == 0) return true;
            }
            catch { }
            return false;
        }
    }
}
