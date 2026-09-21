// What Flycast needs before a game will start.
//
// The list is taken from Flycast's own BIOS[] table (core/hw/naomi/naomi_roms.cpp, 10 romsets) and
// from the load path in core/hw/flashrom/nvmem.cpp, NOT from MAME. That matters: Flycast's table is
// MAME's, filtered. It comments out the revisions it will not accept, one of them flagged BAD DUMP.
// Declaring straight from MAME would tell a user to find files this emulator refuses.
//
// Two regimes, measured:
//
//   Dreamcast   OPTIONAL. core/emulator.cpp: `if (config::UseReios || !nvmem::loadFiles())
//               loadHle()` - without a BIOS, Flycast runs its own HLE (reios). It warns
//               "This game requires a real BIOS" for the titles that genuinely need one.
//               The file is "%boot.bin;%bios.bin" with the platform prefix, so dc_boot.bin OR
//               dc_bios.bin - two names for one thing, which is what a BIOS GROUP is for.
//
//   Arcade      REQUIRED. Without it naomi_cart_LoadBios throws and nothing runs.
//
// THE CONTRACT DOES NOT FIT THE EMULATOR, and the honest thing is to say how we bend.
// GetBiosFilesForPlatform is handed a PLATFORM, but Flycast picks the BIOS per GAME: on Sega Naomi,
// 234 titles want naomi.zip while 4 want hod2bios.zip and 3 want f355bios.zip. So the majority BIOS
// is Required, the game-specific ones are not. A group with AllItemsRequired=false would be WRONG
// here - it means "these are equivalent", and someone holding only hod2bios.zip would satisfy the
// check while being unable to run 230 games. Unbroken's own table makes the same call for the
// flycast libretro core, which is reassuring but not why we do it.
//
// MD5: usable for a raw dump, meaningless for a zip - two archives with identical contents do not
// share an MD5. So dc_boot.bin carries one and the romsets do not. The value below is the one
// LaunchBox publishes for it in the RetroArch plugin's Bios_Requirements resource.
//
// CRC32 is what Flycast actually validates - it opens a romset member by CRC, not by name
// (naomi_cart.cpp: OpenFileByCrc). EmulatorBiosFile has no field for it, so it cannot be declared;
// it is noted here so the next person does not go looking for one.

using System;
using System.Collections.Generic;
using System.Linq;
using Unbroken.LaunchBox.Plugins;

namespace LbIntegrations.Flycast
{
    internal static class FlycastBios
    {
        /// <summary>Where Flycast looks by default, relative to the install folder. `Dreamcast.BiosPath`
        /// in emu.cfg can add folders ahead of it; this is the one that always works.</summary>
        public const string Location = "data";

        private const string DreamcastMd5 = "e10c53c2f8b90bab96ead2d368858623";

        public static IEnumerable<EmulatorBiosFile> For(string platform)
        {
            var name = (platform ?? "").Trim();

            if (Is(name, FlycastPlatforms.Dreamcast))
            {
                // One group, either name accepted, and the group itself is NOT required: reios covers
                // a missing BIOS for most games.
                var group = new EmulatorBiosGroup(
                    "flycast-dreamcast-bios",
                    "Dreamcast boot ROM - optional: without it Flycast uses its built-in HLE BIOS, "
                    + "which a few games refuse",
                    isRequired: false,
                    allItemsRequired: false);

                return new[]
                {
                    File_("dc_boot.bin", "Dreamcast boot ROM", required: false, md5: DreamcastMd5, group: group),
                    File_("dc_bios.bin", "Dreamcast boot ROM, alternative name Flycast also accepts",
                          required: false, md5: null, group: group),
                };
            }

            if (Is(name, FlycastPlatforms.Naomi))
                return new[]
                {
                    Zip("naomi.zip",     "Naomi BIOS - needed by 234 of the 244 Naomi titles Flycast knows", required: true),
                    Zip("hod2bios.zip",  "The House of the Dead 2 BIOS - 4 titles",                          required: false),
                    Zip("f355bios.zip",  "Ferrari F355 Challenge BIOS - 3 titles",                           required: false),
                    Zip("f355dlx.zip",   "Ferrari F355 Challenge Deluxe BIOS - 1 title",                     required: false),
                    Zip("airlbios.zip",  "Airline Pilots BIOS - 2 titles",                                   required: false),
                    Zip("naomigd.zip",   "Naomi GD-ROM BIOS",                                                required: false),
                    Zip("naomidev.zip",  "Naomi development BIOS",                                           required: false),
                };

            if (Is(name, FlycastPlatforms.Naomi2))
                return new[] { Zip("naomi2.zip", "Naomi 2 BIOS - needed by every Naomi 2 title", required: true) };

            if (Is(name, FlycastPlatforms.Atomiswave))
                return new[] { Zip("awbios.zip", "Atomiswave BIOS - needed by every Atomiswave title", required: true) };

            return Array.Empty<EmulatorBiosFile>();
        }

        private static bool Is(string candidate, string platform)
            => string.Equals(candidate, platform, StringComparison.InvariantCultureIgnoreCase);

        private static EmulatorBiosFile Zip(string fileName, string description, bool required)
            => File_(fileName, description + " (MAME romset)", required, md5: null, group: null);

        // Every property on EmulatorBiosFile is read-only, so the constructor is the only way in.
        private static EmulatorBiosFile File_(string fileName, string description, bool required,
                                              string md5, EmulatorBiosGroup group)
            => new EmulatorBiosFile(Location, fileName, required, description, md5, group);
    }
}
