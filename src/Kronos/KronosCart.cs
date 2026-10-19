// A game's backup RAM CARTRIDGE in Kronos - plugged for its session only when the game has one (Ymir's model, YmirCart).
//
// WHY: Beetle Saturn always plugs a 4 Mbit backup cartridge (its <rom>.bcr, 512 KiB); a game may have saved there. Kronos plugs
// what its settings say (Cartridge\Type, none by default). Always plugging one would split a game's saves between the console and
// the cartridge, so the plugin plugs it only for a game that HAS one: <install>\Saves\<platform>\<game>.bcr, beside its backup
// RAM - made by an import from RetroArch whose cartridge holds saves, or restored from LaunchBox.
//
// THE SAME FORMAT (docs\kronos.md §4): Kronos's 4 Mbit cartridge (Cartridge\Type 2) is a 512 KiB file of the cartridge's odd
// bytes, the .bcr Beetle writes - MEASURED 19/10: Beetle's .bcr with a mark in it read and left byte-identical.
//
// The games that need another cartridge (RAM expansion, KoF '95 / Ultraman ROM - Ymir's game database, db\game_db.cpp) never
// get the backup cartridge: theirs is the one set in Kronos.

using System;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Kronos
{
    internal static class KronosCart
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
        /// <summary>A new internal backup RAM made as Kronos formats one (memory.c FormatBackupRam): "BackUpRam Format" four times,
        /// zeros to 32 KB. Made by the plugin so a new game's RAM is the console's size from the start, and there - Kronos writes
        /// a new file only when it quits, and a quit that fails would leave none (docs\kronos.md §4). True when made.</summary>
        public static bool FormatRam(string path)
        {
            if (File.Exists(path)) return false;
            var bytes = new byte[32 * 1024];
            var header = System.Text.Encoding.ASCII.GetBytes("BackUpRam Format");
            for (int i = 0; i < 4; i++) Array.Copy(header, 0, bytes, i * 16, 16);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, bytes);
            return true;
        }

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
