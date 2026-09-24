// Launching a DSiWare title under no$gba.
//
// EVERYTHING HARD IS IN src\Shared.Dsi, and this file is what is left: which console to build on,
// where the working image goes, and the two settings no$gba needs before it will boot a DSi at all.
// The engine chooses the dump, builds the console, installs the title, takes the reference walk,
// puts the saved state back, captures the session afterwards and packs it as a .dsisave - all of
// that is the same code melonDS runs, because none of it is about the emulator.
//
// MEASURED, 2026-09-24, on a real installation: a console configured by the melonDS side booted
// under no$gba, melonds-nandtool installed Zelda into it, the DSi menu showed the title and ran it,
// and the walk afterwards found the SAME set of files a melonDS session produces - the title's
// public.sav, shared1/TWLCFG0.dat and TWLCFG1.dat, shared2/launcher/wrap.bin, two built-in apps'
// private.sav - plus sys/log/sysmenu.log, which is the menu's own journal and moves every boot.
//
// THREE THINGS ARE NOT LIKE melonDS.
//
// NO PATH SETTING. no$gba reads a fixed file name beside its executable, DSi-1.mmc, and its INI
// holds no path at all. So the working image IS that file - NoGbaHost.WorkImagePath says so - and
// a DSi-1.mmc that is not ours is set aside rather than overwritten.
//
// NO BOOT WITHOUT A CARTRIDGE. Measured: started with no ROM, no$gba sits idle - no logo, and the
// eMMC is never even opened. The entrypoint setting only decides how a ROM starts. So the title's
// own .nds goes in the cartridge slot to turn the machine on, and the title is then launched from
// the DSi menu, out of the NAND. melonDS is handed the ROM for the same reason.
//
// TWO SETTINGS, AND THEIR VALUES ARE THE MENU'S OWN LABELS. A wrong value is ignored in silence -
// see NoGbaIni - so both are named constants carrying the label they were read from.

using System;
using System.Collections.Generic;
using System.IO;
using LbIntegrations.Dsi;

namespace LbIntegrations.NoGba
{
    internal static class NoGbaDsi
    {
        /// <summary>Beside the log, the way every other switch here works.</summary>
        private const string KillSwitch = "no-nogba-dsi";

        // ── the two settings, with the labels no$gba answers to ──────────────

        /// <summary>Which machine no$gba emulates. Read off the drop-down in Options > Emulation
        /// Setup, which lists seven entries; this is the fifth.</summary>
        public const string ModeKey = "NDS Mode/Colors";
        public const string ModeDsi = "DSi (retail/16MB)";
        public const string ModeDs = "Nintendo DS (retail/4MB)";

        /// <summary>Where a reset begins. "Start Cartridge directly" skips the eMMC entirely, which
        /// is right for a DS game and useless for a DSiWare: the title lives in the NAND and only
        /// the menu can launch it.</summary>
        public const string EntryKey = "Reset/Startup Entrypoint";
        public const string EntryBios = "GBA/NDS BIOS (Nintendo logo)";
        public const string EntryCartridge = "Start Cartridge directly";

        /// <summary>Put no$gba into DSi mode, or back. Called on every launch, because these are
        /// GLOBAL settings and a DSiWare launch must not leave a GBA game booting through a BIOS it
        /// does not need.</summary>
        public static void SetMode(NoGbaLayout layout, bool dsi)
        {
            if (layout?.IniFile == null) return;

            var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ModeKey] = dsi ? ModeDsi : ModeDs,
                [EntryKey] = dsi ? EntryBios : EntryCartridge,
            };

            var current = NoGbaIni.Read(layout.IniFile, ModeKey, EntryKey);
            bool same = true;
            foreach (var pair in wanted)
                if (!current.TryGetValue(pair.Key, out var have)
                    || !string.Equals(have, pair.Value, StringComparison.OrdinalIgnoreCase))
                { same = false; break; }
            if (same) return;

            var error = NoGbaIni.Write(layout.IniFile, wanted);
            if (error != null) { Log.Warn("could not set the DSi mode: " + error); return; }
            Log.Info(dsi
                ? "no$gba set to DSi mode, booting through the BIOS so the DSi menu comes up"
                : "no$gba set back to DS mode, booting the cartridge directly");
        }

        // ── the launch ───────────────────────────────────────────────────────

        /// <summary>Prepare a DSiWare launch. Answers FALSE when the game must not start - the only
        /// two reasons being that the emulator still holds the image, and that somebody asked for a
        /// console to be built and has to relaunch afterwards.</summary>
        public static bool Prepare(NoGbaLayout layout, NdsRom rom, string romPath)
        {
            try
            {
                if (Log.Disabled(KillSwitch))
                {
                    Log.Verbose("the " + KillSwitch + " marker is there; DSiWare is left alone");
                    return true;
                }

                DsiHost host = layout;
                DsiWorkspace.PrepareFolder(host);

                // NOTHING TOUCHES THE IMAGE UNTIL no$gba HAS LET IT GO. Everything below reads it,
                // captures it or builds over it, and all three are wrong while a session is in
                // flight. Measured: no$gba does NOT hold the file open for the session, so the
                // process name is the only honest test here - see NoGbaHost.Announce.
                if (!DsiWorkspace.WaitForTheImage(host, rom.AssetName))
                {
                    Log.Info(rom.AssetName + " is not started this time; no$gba still had the "
                             + "working image. Launch it again once it has closed.");
                    return false;
                }

                if (!ProtectTheirImage(layout)) return false;

                var bios7 = DsiBios7(layout);
                if (bios7 == null)
                {
                    Log.Info(rom.AssetName + " is DSiWare and needs a DSi BIOS: put biosdsi7.bin and "
                             + "biosdsi9.bin in " + NoGbaBios.SourceDir(layout)
                             + ". Leaving the emulator in DS mode.");
                    return true;
                }

                // WHICH CONSOLE. A save is a difference against ONE console, so the save decides
                // when it has an opinion; otherwise the region picks a dump and the dump has at
                // most one console. Identical to the melonDS side, which is the point.
                var regions = DsiRegions.RegionsFor(rom, romPath, out var how);
                if (regions.Count > 0)
                    Log.Verbose(rom.AssetName + " runs on " + DsiRegions.Names(regions)
                                + " hardware, according to " + how);

                var dump = DsiDumps.NandFor(host, regions, bios7, out var whyNoNand);
                if (dump == null)
                {
                    Log.Info(rom.AssetName + " is DSiWare and cannot start: " + whyNoNand
                             + ". Leaving the emulator in DS mode.");
                    return true;
                }

                var console = DsiBase.ConsoleFor(host, dump.Path);
                if (console == null)
                {
                    if (NoGbaNandSetup.Run(layout, dump, bios7, romPath))
                    {
                        Log.Info("no$gba was opened to set a console up for "
                                 + Path.GetFileName(dump.Path) + ", so this launch of "
                                 + rom.AssetName + " is dropped. Launch it again once it is ready.");
                        return false;
                    }
                    console = DsiBase.ConsoleFor(host, dump.Path);
                }
                else if (!DsiBase.Described(console))
                    NoGbaNandSetup.Describe(layout, console, dump.Path, bios7, dump.Region);

                var source = console ?? dump.Path;

                // The receipt, before any decision: a save written by something else since the image
                // was last agreed with it means the image describes a save that no longer exists.
                DsiWorkspace.DropWorkIfSaveMoved(host, rom.TitleId);

                bool reused = DsiNand.IsUsable(out var missingLibrary)
                              && DsiWorkspace.CanReuseWork(host, rom, romPath, source);
                if (!reused) DsiWorkspace.CaptureWork(host, bios7);

                if (!DsiNand.IsUsable(out _))
                {
                    Log.Info(rom.AssetName + " is DSiWare but the NAND library is not usable - "
                             + missingLibrary + ". Leaving the emulator in DS mode.");
                    return true;
                }

                var image = reused ? DsiWorkspace.ExistingWork(host)
                                   : DsiWorkspace.Rebuild(host, rom, source);
                if (image.Path == null)
                {
                    Log.Info(rom.AssetName + " is DSiWare (title " + rom.TitleId
                             + ") but it has no NAND to run from - " + image.Reason
                             + ". Leaving the emulator in DS mode.");
                    return true;
                }

                if (!reused)
                {
                    Log.Info(rom.AssetName + ": built on " + Path.GetFileName(source)
                             + ", the " + DsiRegions.Name(dump.Region) + " console");
                    if (!Install(layout, rom, romPath, image.Path, bios7)) return true;
                    DsiWorkspace.RestoreState(host, rom.TitleId, bios7);
                    DsiWorkspace.RememberWork(host, rom.TitleId, romPath, source);
                }

                SetMode(layout, dsi: true);
                Log.Info(rom.AssetName + " is DSiWare: no$gba boots the DSi menu on "
                         + NoGbaHost.MmcName + ", and the title is launched from there. The "
                         + "cartridge is only there to turn the machine on - no$gba starts nothing "
                         + "without one.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("could not prepare a DSiWare launch", ex);
                return true;                       // never refuse a game over our own failure
            }
        }

        /// <summary>The DSi ARM7 BIOS, which every call into the NAND library needs: the image is
        /// encrypted with console-unique data plus the ES key that lives in this file.</summary>
        private static string DsiBios7(NoGbaLayout layout)
        {
            foreach (var file in NoGbaBios.Files)
                if (string.Equals(file.OurName, "biosdsi7.bin", StringComparison.OrdinalIgnoreCase))
                    return NoGbaBios.Find(layout, file);
            return null;
        }

        /// <summary>Install the title into the working image and write down the walk of that
        /// install. The ROM may be inside an archive - no$gba cannot open one, so the host has
        /// already unpacked it, but a save listing can reach here with the archive itself.</summary>
        private static bool Install(NoGbaLayout layout, NdsRom rom, string romPath, string nandPath,
                                    string bios7)
        {
            string unpacked = null;
            try
            {
                var appPath = romPath;
                if (NoGbaRoms.IsArchive(romPath))
                {
                    unpacked = Path.Combine(Path.GetTempPath(),
                                            "lbip-nogba-" + Guid.NewGuid().ToString("N") + ".nds");
                    if (!Archives.ExtractFirstEntry(romPath, NdsHeader.RomExtensions, unpacked, out _))
                    {
                        Log.Warn("could not find a .nds inside " + rom.AssetName
                                 + "; it cannot be installed into a NAND.");
                        return false;
                    }
                    appPath = unpacked;
                }

                using var session = DsiNand.Open(nandPath, bios7, out var error);
                if (session == null)
                {
                    Log.Warn("could not open the NAND of " + rom.AssetName + " - " + error);
                    return false;
                }

                // THE REAL METADATA FIRST. A TMD built from the ROM carries everything the importer
                // reads, but it is unsigned - and the DSi menu that launches an installed title
                // checks. See DsiTmd and DsiNus.
                var tmd = DsiTmd.Resolve(layout, rom, appPath, romPath, out var from);
                if (tmd != null) Log.Verbose("metadata for " + rom.TitleId + " from " + from);

                if (!session.ImportTitle(appPath, tmd, out var failure, out var generated))
                {
                    Log.Warn("could not install " + rom.AssetName + " into the working NAND - "
                             + failure);
                    return false;
                }
                Log.Verbose(rom.AssetName + ": title " + rom.TitleId + " installed"
                            + (generated ? " (its metadata was BUILT from the ROM, not signed - if "
                                         + "the DSi menu refuses it, that is why)" : ""));

                return DsiWorkspace.TakeReference(layout, session, rom.TitleId);
            }
            catch (Exception ex) { Log.Warn("could not install a DSiWare title", ex); return false; }
            finally { try { if (unpacked != null && File.Exists(unpacked)) File.Delete(unpacked); } catch { } }
        }

        /// <summary>Set aside a DSi-1.mmc that is not ours, once, instead of building over it.
        ///
        /// melonDS never has this problem: its working image lives in a folder we own, so the PATH
        /// answers "is this mine". Here the name is fixed and beside the emulator, and somebody who
        /// set a DSi up by hand has their own file sitting exactly there. The marker beside the
        /// working image answers it instead - work.title and work.sum are written whenever WE build
        /// one, so an image with no marker was not built by us.</summary>
        private static bool ProtectTheirImage(NoGbaLayout layout)
        {
            try
            {
                DsiHost host = layout;
                var mmc = layout.MmcFile;
                if (mmc == null || !File.Exists(mmc)) return true;
                if (DsiWorkspace.WorkTitle(host) != null) return true;    // ours, and it says so

                var parked = mmc + NoGbaHost.TheirsSuffix;
                if (File.Exists(parked))
                    parked += "-" + DateTime.UtcNow.Ticks.ToString(
                                  System.Globalization.CultureInfo.InvariantCulture);

                File.Move(mmc, parked);
                Log.Info("there was already a " + NoGbaHost.MmcName + " here that this plugin did "
                         + "not make, so it was set aside as " + Path.GetFileName(parked)
                         + " rather than built over. It is not deleted.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("could not set aside an existing " + NoGbaHost.MmcName + "; the launch is "
                         + "dropped rather than risk writing over it", ex);
                return false;
            }
        }
    }
}
