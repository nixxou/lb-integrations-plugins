// What FCEUX runs, and which LaunchBox platform each is - read off FCEUX 2.7.0's own code (tag fceux-2.7.0, commit
// 8cdbeeb, 18/10/2026; docs\fceux.md §2):
//
//   src\fceu.cpp FCEUI_LoadGameVirtual   a file is tried BY ITS CONTENT, never by its extension, in this order: iNES /
//                                        NES 2.0 ("NES\x1A", ines.cpp), NSF ("NESM\x1A", nsf.cpp), UNIF (unif.cpp), FDS
//                                        (fwNES "FDS\x1A" header or a raw disk starting "\x01*NINTENDO-HVC*", fds.cpp).
//                                        No system switch exists, and none is needed: FCEUX tells by itself.
//   src\fceu.cpp:439 romextensions       inside an archive only the nes, fds and nsf entries are looked at - a .unf in a
//                                        zip is not found (FceuxLaunch.RomFor unpacks it).
//   drivers\win\window.cpp:1182          the Open dialog's filter: *.nes;*.nsf;*.fds;*.unf;*.zip;*.rar;*.7z;*.gz
//   drivers\win\archive.cpp, file.cpp    archives through 7z_64.dll (every format it knows) and gzip in the core.
//
// THE SYSTEMS:
//   NES / Famicom cartridge   .nes (iNES, NES 2.0), .unf / .unif (UNIF), and .nsf music (a player, no save). The region -
//                             NTSC, PAL, Dendy - is the NES 2.0 header's, else the file name's "(E)", "(Europe)", "(PAL)"...
//                             (ines.cpp:1114), or -pal / -dendy on the line; Vs. UniSystem games run from .nes files too
//                             (vsuni.cpp: told by the header or FCEUX's table). PlayChoice-10 is NOT emulated (only a field
//                             of the iNES header editor).
//   Famicom Disk System       .fds - needs the BIOS disksys.rom, 8 192 bytes (fds.cpp:869 checks the size alone), in
//                             "odfdsrom" or else beside the executable (file.cpp:751).
//
// THE PLATFORMS (names read in LaunchBox's own base, a copy of G:\LB1326\Metadata\LaunchBox.Metadata.db, 18/10):
// "Nintendo Entertainment System" (a .fds there is a disk game - LaunchBox's own FCEUX row lists .fds), "Nintendo Famicom
// Disk System" (LaunchBox's FCEUX row has none: ours adds it), and two names LaunchBox does not have but LiteBox uses:
// "Nes - Super Mario Bros. Hacks" (its metadata, Mehdi 17/10) and "Nintendo Famicom" (its RomM map,
// LbApiHost\Host\Romm\RommPlatformMap.cs) - claimed by name, as Nixx-ares claims "Pocket Challenge V2". No LaunchBox
// platform exists for Vs. games (MAME's, under Arcade), PlayChoice-10 or NSF music.
//
// This table is the plugin's one list: the catalogue rows, IsPlatformSupported, the entry's platform rows, the BIOS, the
// launch and the save folders all read it. The probe checks it against what the source says (FceuxCheck.Systems).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Fceux
{
    internal sealed class FceuxSystem
    {
        /// <summary>"nes" or "fds": what the save ids carry (SaveIdData.Sys), never changed once released.</summary>
        public string Key;
        public string Name;
        public string[] Extensions = Array.Empty<string>();
        /// <summary>The BIOS file the system needs beside the executable (or in "odfdsrom"), or null.</summary>
        public string Bios;
        public long BiosSize;
        public string BiosSha256;
        /// <summary>The names the same dump goes by in the other emulators' folders.</summary>
        public string[] BiosAliases = Array.Empty<string>();
    }

    internal sealed class FceuxPlatform
    {
        public string Platform;             // LaunchBox's name
        public string System;               // the default system's key
        public Dictionary<string, string> ByExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    internal static class FceuxSystems
    {
        public static readonly FceuxSystem Nes = new FceuxSystem
        {
            Key = "nes", Name = "NES / Famicom", Extensions = new[] { ".nes", ".unf", ".unif", ".nsf" },
        };

        public static readonly FceuxSystem Fds = new FceuxSystem
        {
            Key = "fds", Name = "Famicom Disk System", Extensions = new[] { ".fds" },
            Bios = "disksys.rom", BiosSize = 8192,
            // The dump ares's table carries for the same BIOS (src\Ares\AresSystems.cs) - FCEUX checks only the size.
            BiosSha256 = "fdc1a76e654feea993fcb38366e05ee5f4eb641f86fe6bebaeefd412e112dd72",
            BiosAliases = new[] { "fds_bios.bin" },
        };

        public static readonly FceuxSystem[] All = { Nes, Fds };

        /// <summary>The archives FCEUX opens itself (window.cpp's filter; 7z_64.dll and the core's gzip).</summary>
        public static readonly string[] Archives = { ".zip", ".7z", ".rar", ".gz" };

        /// <summary>The entries FCEUX looks at inside an archive (fceu.cpp:439, romextensions).</summary>
        public static readonly string[] ArchiveRomExtensions = { ".nes", ".fds", ".nsf" };

        public const string NesPlatform = "Nintendo Entertainment System";

        public static readonly FceuxPlatform[] Platforms =
        {
            // The main one first: it is the one the defaults go by.
            new FceuxPlatform { Platform = NesPlatform, System = "nes", ByExtension = { [".fds"] = "fds" } },
            new FceuxPlatform { Platform = "Nintendo Famicom Disk System", System = "fds" },
            // LiteBox's own names (see the header): not in LaunchBox's list, claimed by name all the same.
            new FceuxPlatform { Platform = "Nintendo Famicom", System = "nes", ByExtension = { [".fds"] = "fds" } },
            new FceuxPlatform { Platform = "Nes - Super Mario Bros. Hacks", System = "nes" },
        };

        public static FceuxSystem System(string key)
            => All.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

        public static FceuxPlatform Platform(string platform)
            => Platforms.FirstOrDefault(p => string.Equals(p.Platform, (platform ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>Every extension of a platform, for its catalogue row: its systems' and the archives.</summary>
        public static string AllExtensions(FceuxPlatform p)
        {
            var exts = (System(p.System)?.Extensions ?? Array.Empty<string>())
                       .Concat(p.ByExtension.Keys.SelectMany(k => new[] { k }))
                       .Concat(p.ByExtension.Values.Select(System).Where(s => s != null).SelectMany(s => s.Extensions))
                       .Concat(Archives)
                       .Distinct(StringComparer.OrdinalIgnoreCase);
            return string.Join("; ", exts);
        }

        /// <summary>The emulator row's: every platform's.</summary>
        public static string EveryExtension()
            => string.Join("; ", Platforms.SelectMany(p => AllExtensions(p).Split(new[] { "; " }, StringSplitOptions.None)).Distinct(StringComparer.OrdinalIgnoreCase));

        /// <summary>The system a game runs as. <paramref name="romInside"/>: the ROM's own name when the game is an archive
        /// (Archives.RomEntry), else null. FCEUX tells by the file's content; this only decides the BIOS and the save ids:
        /// the file's extension first (a .fds among NES games is a disk), else its platform's default, else the NES.</summary>
        public static FceuxSystem SystemFor(string platform, string rom, string romInside = null)
        {
            var ext = Path.GetExtension(romInside ?? rom ?? "").ToLowerInvariant();
            var byExt = All.FirstOrDefault(s => s.Extensions.Contains(ext));
            if (byExt != null) return byExt;
            var p = Platform(platform);
            return System(p?.System) ?? Nes;
        }

        /// <summary>The folder name of a platform under &lt;FCEUX&gt;\Saves\: its name, the characters Windows refuses in
        /// a file name replaced, no trailing dot or space. An empty platform is the NES's.</summary>
        public static string FolderOf(string platform)
        {
            var name = string.IsNullOrWhiteSpace(platform) ? NesPlatform : platform.Trim();
            var bad = Path.GetInvalidFileNameChars();
            name = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).TrimEnd('.', ' ');
            return name.Length == 0 ? NesPlatform : name;
        }
    }
}
