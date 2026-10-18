// What puNES runs, and which LaunchBox platform each is - read off puNES v0.111's own code (18/10, tag v0.111, 613b7b4;
// docs\punes.md section 2):
//
//   src\core\emu.c, emu_load_rom    the loaders by file: .fds / .qd -> fds_load_rom (FDS_FORMAT / QD_FORMAT), .nsf /
//                                   .nsfe -> the music player, everything else -> iNES (.nes, NES 2.0), then UNIF (.unf)
//   src\gui\mainWindow.cpp:1377-1405 the Open dialog's filters: the same list, plus .zip / .7z (and .rar when 7z.dll has it)
//   src\core\ines.c:160-195          NES 2.0 console types: 1 Vs. System (emulated), 2 PlayChoice-10 (only logged, runs as a
//                                   plain NES game), 4-12 "[unsupported]"
//   src\core\common.h:55             the region is a TIMING MODE (pal, ntsc, dendy, auto), not a system nor a file type
//
// TWO SYSTEMS, and puNES PICKS THE ONE ITSELF FROM THE FILE: there is no --system switch (src\gui\cmd_line.cpp, its whole
// option table read). So the ares order (a game's own choice > the platform row's line > the table and the extension)
// is, for puNES, the table and the extension alone (SystemFor): what the plugin needs to know - the BIOS to check, the
// system in the save ids - is what puNES itself will do with the file. A platform this table does not name still
// launches when its file says what it is (a .nes is a cartridge whatever its platform is called - never-touch-existing-
// platform-rows: custom platforms exist); a file that says nothing on a platform the table does not name is refused.
//
// THE PLATFORMS (names read 18/10 in a copy of G:\LB1326\Metadata\LaunchBox.Metadata.db, and in LiteBox's own maps,
// LbApiHost Host\Romm\RommPlatformMap.cs and Host\Ra\RaPlatformMap.cs): LaunchBox's two, then LiteBox's own names for the
// same consoles - claimed by name too, the main one first (it is the one the defaults go by):
//   Nintendo Entertainment System    the cartridges; Vs. System and PlayChoice-10 dumps (NES 2.0 .nes) belong here -
//                                    LaunchBox has no Vs. System platform, MAME's Vs. sets are Arcade and puNES reads none
//   Nintendo Famicom Disk System     the disks, disksys.rom required (src\core\fds.c:255-330)
//   Nes - Super Mario Bros. Hacks    LiteBox's (RommPlatformMap, RaPlatformMap): NES cartridges
//   Nintendo Famicom                 LiteBox's (RommPlatformMap "famicom"): NES cartridges - NOT a LaunchBox platform
//                                    ("Famicom" is only an alternate name of the NES there)
// NOT CLAIMED: Arcade (MAME's Vs. sets, which puNES cannot load), Dendy (no platform anywhere: the per-game region),
// NSF players (no platform: the music files ride on the NES row, as LaunchBox's own FCEUX and Nestopia rows carry .nsf).
//
// THE SYSTEM GOES INTO THE SAVE IDS (SaveIdData.Sys, brief section 0): a .nes and a .fds of one base name write the same
// save\<name>.p00 (src\core\save_slot.c:759-795) - "nes" and "fds", fixed now, before the first release.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Punes
{
    internal sealed class PunesBios
    {
        public string Name;               // the name puNES looks for
        public string[] Aliases = Array.Empty<string>();   // the names the dump goes by in the other emulators' folders
        public bool Required;
        public long Size;                 // what puNES reads of it
        public string Sha256;             // the known dump, for the log only: puNES checks nothing (fds.c:322-328)
        public string Description;
        public IEnumerable<string> Names => new[] { Name }.Concat(Aliases);
    }

    internal sealed class PunesSystem
    {
        public string Id;                 // the "sys" of the save ids: "nes", "fds"
        public string Name;
        public string[] Extensions = Array.Empty<string>();
        public PunesBios[] Bios = Array.Empty<PunesBios>();
    }

    internal sealed class PunesPlatform
    {
        public string Platform;           // LaunchBox's (or LiteBox's) name
        public string[] Systems;          // the default first
        public Dictionary<string, string> ByExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string Default => Systems[0];
    }

    internal static class PunesSystems
    {
        public const string Nes = "nes", Fds = "fds";

        /// <summary>What puNES opens itself (mainWindow.cpp's filter, measured 18/10 with Outer Zip.zip / Outer 7z.7z); .rar
        /// only when 7z.dll has it - not measured, so the plugin unpacks a .rar itself (PunesLaunch.RomFor).</summary>
        public static readonly string[] Archives = { ".zip", ".7z", ".rar" };

        public static readonly PunesSystem[] All =
        {
            new PunesSystem { Id = Nes, Name = "NES / Famicom cartridge", Extensions = new[] { ".nes", ".unf", ".unif", ".nsf", ".nsfe" } },
            new PunesSystem { Id = Fds, Name = "Famicom Disk System", Extensions = new[] { ".fds", ".qd" },
                Bios = new[]
                {
                    new PunesBios { Name = "disksys.rom", Aliases = new[] { "FdsBios.bin", "fds_bios.bin" }, Required = true, Size = 8192,
                                    Sha256 = "fdc1a76e654feea993fcb38366e05ee5f4eb641f86fe6bebaeefd412e112dd72",
                                    Description = "Famicom Disk System BIOS (8 KB) - puNES starts no disk without it" },
                } },
        };

        private static PunesPlatform P(string platform, params string[] systems)
            => new PunesPlatform { Platform = platform, Systems = systems };

        private static PunesPlatform Cartridges(string platform)
            => new PunesPlatform { Platform = platform, Systems = new[] { Nes }, ByExtension = { [".fds"] = Fds, [".qd"] = Fds } };

        public static readonly PunesPlatform[] Platforms =
        {
            Cartridges("Nintendo Entertainment System"),
            // Its row lists the disks alone: a cartridge filed under it still runs as one (SystemFor goes by the file first).
            P("Nintendo Famicom Disk System", Fds),
            // LiteBox's own names (its metadata, Mehdi 17/10; RommPlatformMap.cs): not in LaunchBox's list, claimed by name.
            Cartridges("Nes - Super Mario Bros. Hacks"),
            Cartridges("Nintendo Famicom"),
        };

        public static PunesSystem System(string id) => All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));

        public static PunesPlatform Platform(string platform)
            => Platforms.FirstOrDefault(p => string.Equals(p.Platform, (platform ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

        public static bool IsArchive(string path) => Archives.Contains(Path.GetExtension(path ?? "").ToLowerInvariant());

        /// <summary>The system a file is, by its extension, as puNES's loader picks it (emu.c) - null for a file it says
        /// nothing about (an archive, an unknown extension).</summary>
        public static PunesSystem OfExtension(string extension)
            => All.FirstOrDefault(s => s.Extensions.Contains((extension ?? "").ToLowerInvariant()));

        /// <summary>The system a game runs as, and where that came from: the file's extension (the one INSIDE an archive,
        /// <paramref name="innerExtension"/>, when it is one), else its platform's default; null when neither says.</summary>
        public static (PunesSystem System, string From) SystemFor(string platform, string rom, string innerExtension = null)
        {
            var ext = innerExtension ?? Path.GetExtension(rom ?? "");
            var byFile = OfExtension(ext);
            if (byFile != null) return (byFile, innerExtension != null ? "the file in the archive" : "the file's extension");
            var p = Platform(platform);
            if (p != null) return (System(p.Default), "the platform");
            return (null, null);
        }

        /// <summary>Every extension of a platform, for its catalogue row: ".nes; .unf; ...; .zip; .7z; .rar".</summary>
        public static string AllExtensions(PunesPlatform p)
        {
            var exts = p.Systems.Select(System).Where(s => s != null).SelectMany(s => s.Extensions).Concat(p.ByExtension.Keys).Concat(Archives)
                        .Distinct(StringComparer.OrdinalIgnoreCase);
            return string.Join("; ", exts);
        }

        /// <summary>The emulator row's: the union of the platforms'.</summary>
        public static string EveryExtension()
            => string.Join("; ", Platforms.SelectMany(p => AllExtensions(p).Split(new[] { "; " }, StringSplitOptions.None)).Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
