// Making a console out of a dump, once, and never touching the dump to do it.
//
// A DSiWARE SAVE IS A DIFFERENCE against a base image, so that base has to exist and has to be
// configured: a NAND out of a real console carries that console's name, language, birthday and
// colour, and one from anywhere else carries a stranger's or an unfinished welcome sequence the DSi
// menu insists on completing. Completing it WRITES INTO THE IMAGE.
//
// SO THE IMAGE THAT GETS WRITTEN INTO IS OURS. The dump is copied into dsi\ under its own name and
// the emulator is pointed at the copy - see DsiBase.BuildConsole, which both plugins share. The
// user's folder keeps only what the user put there.
//
// AND HERE IS THE ONE PLACE no$gba COSTS MORE THAN melonDS. melonDS can be told where its NAND is,
// so it is simply pointed at the copy. no$gba reads a fixed name beside its executable and will not
// be told otherwise, so the copy has to BE that file for the length of the setup, and then be put
// back. Two 240 MB copies - once per console, ever, and never on a launch.
//
// no$gba ALSO STARTS NOTHING WITHOUT A CARTRIDGE. Measured: with no ROM it sits idle and never even
// opens the eMMC. So the setup hands it the ROM that triggered all this, purely to turn the machine
// on; the console is what is being configured, not the game.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using LbIntegrations.Dsi;

namespace LbIntegrations.NoGba
{
    internal static class NoGbaNandSetup
    {
        private const string KillSwitch = "no-nogba-dsi-setup";

        /// <summary>Offer to build a console for this dump. Answers TRUE when the launch that asked
        /// should be abandoned - no$gba was just opened on the DSi menu, and starting the game on
        /// top of that would be two emulators on one image.</summary>
        public static bool Run(NoGbaLayout layout, NandDump dump, string bios7Path)
        {
            try
            {
                if (layout?.IniFile == null || dump?.Path == null) return false;
                if (Log.Disabled(KillSwitch))
                {
                    Log.Verbose("the " + KillSwitch + " marker is there; no console will be built");
                    return false;
                }

                if (!DsiDialog.Available)
                {
                    Log.Info(Path.GetFileName(dump.Path) + " has no configured console, and windows "
                             + "are turned off; using the dump as it is");
                    return false;
                }

                DsiHost host = layout;
                var name = Path.GetFileName(dump.Path);
                var answer = DsiDialog.Ask("no$gba - no console for this NAND yet",
                                           Announcement(name, dump.Region),
                                           new[] { "Set one up now", "Close" });
                if (answer != 0)
                {
                    Log.Info("no console was built for " + name + ", so this launch is dropped. "
                             + "Nothing was written, and you will be asked again next time.");
                    return true;
                }

                var console = DsiBase.BuildConsole(host, dump.Path, out var error);
                if (console == null)
                {
                    Log.Warn("could not build a console from " + name + " - " + error);
                    DsiDialog.Ask("no$gba - the console could not be built",
                                  "A copy of " + name + " has to be made before it can be set up, "
                                  + "and it could not be:" + Environment.NewLine + Environment.NewLine
                                  + "    " + error + Environment.NewLine + Environment.NewLine
                                  + "Your own dump was not touched, and the game was not started.",
                                  new[] { "Close" });
                    return true;
                }

                if (!Configure(layout, console))
                {
                    DsiDialog.Ask("no$gba - the console could not be set up",
                                  "The copy could not be put where no$gba reads it, so nothing was "
                                  + "configured. Your own dump was not touched, and the game was "
                                  + "not started.",
                                  new[] { "Close" });
                    return true;
                }

                var verdict = DsiDialog.Ask("no$gba - is this console set up?", Confirmation(name),
                                            new[] { "Yes, keep it", "No, throw it away" });
                if (verdict == 0)
                {
                    Describe(layout, console, dump.Path, bios7Path, dump.Region);
                    Log.Info("the console for " + name + " is set up and described. It will not be "
                             + "asked about again.");
                }
                else
                {
                    try { File.Delete(console); } catch { }
                    try { File.Delete(DsiBase.RecipeFor(console)); } catch { }
                    try { File.Delete(DsiBase.RecordFor(console)); } catch { }
                    Log.Info("the console built from " + name + " was thrown away; your dump was "
                             + "never touched, so there is nothing to put back");
                }
                return true;
            }
            catch (Exception ex) { Log.Warn("could not build a console", ex); return false; }
        }

        /// <summary>Write the recipe and the record beside a console, so a save made on it can
        /// rebuild it later from the user's dump alone. Shared with melonDS through DsiBase; the
        /// only thing this adds is the refusal to write beside a file that is not ours.</summary>
        public static bool Describe(NoGbaLayout layout, string consolePath, string dumpPath,
                                    string bios7Path, DsiRegion region)
        {
            try
            {
                if (layout == null || consolePath == null || dumpPath == null) return false;
                DsiHost host = layout;
                if (!DsiWorkspace.IsOurs(host, consolePath))
                {
                    Log.Verbose(Path.GetFileName(consolePath) + " is not ours, so no recipe is "
                                + "written beside it");
                    return false;
                }
                if (DsiBase.Described(consolePath)) return true;
                if (bios7Path == null) return false;

                if (DsiBase.MakeRecipe(dumpPath, consolePath, bios7Path, region, out var error))
                    return true;

                Log.Verbose("could not describe " + Path.GetFileName(consolePath) + " - " + error);
                return false;
            }
            catch (Exception ex) { Log.Warn("could not describe a console", ex); return false; }
        }

        /// <summary>Put the console where no$gba reads it, open no$gba on it, and put it back.
        ///
        /// THE TWO COPIES ARE THE PRICE OF A FIXED FILE NAME. no$gba has no setting that says where
        /// its eMMC is, so "run the emulator on THIS image" means "make this image be DSi-1.mmc".
        /// Once per console, ever.</summary>
        private static bool Configure(NoGbaLayout layout, string consolePath)
        {
            var exe = NoGbaPaths.FindExecutable(layout.InstallDir);
            if (exe == null) { Log.Warn("no no$gba executable in " + layout.InstallDir); return false; }

            var mmc = layout.MmcFile;
            if (mmc == null) return false;

            try
            {
                File.Copy(consolePath, mmc, overwrite: true);
                NoGbaDsi.SetMode(layout, dsi: true);

                Log.Info("opening no$gba on the DSi menu to set up "
                         + Path.GetFileName(consolePath));
                using (var process = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = layout.InstallDir,
                    UseShellExecute = false,
                }))
                {
                    // The instruction goes on screen ONCE no$gba is up, not before: said in the
                    // window that precedes it, it would be said to somebody about to look somewhere
                    // else. The same lesson the melonDS side learned.
                    try { process?.WaitForInputIdle(15000); } catch { }
                    DsiDialog.Ask("no$gba - what to do now", Instruction(), new[] { "OK" });
                    process?.WaitForExit();
                }
                Log.Info("no$gba closed");

                // Back into our folder. The console is ours; DSi-1.mmc is scratch.
                File.Copy(mmc, consolePath, overwrite: true);
                return true;
            }
            catch (Exception ex) { Log.Warn("could not set the console up", ex); return false; }
        }

        // ── what the windows say ─────────────────────────────────────────────

        private static string Announcement(string name, DsiRegion region)
            => string.Join(Environment.NewLine, new List<string>
            {
                "You have no console set up for " + name + " yet.",
                "",
                "A DSi NAND is a copy of a whole console, and a console has to be set up once:",
                "your name, language, date and favourite colour. Doing that writes into the",
                "image - so it is done on a COPY, kept in no$gba's own dsi folder.",
                "",
                "YOUR OWN DUMP IS NOT TOUCHED. It stays exactly as it is, and it is what every",
                "console is rebuilt from if one is ever lost.",
                "",
                "If you set one up now:",
                "",
                "    1. " + name + " is copied into no$gba's dsi folder",
                "    2. that copy becomes " + NoGbaHost.MmcName + ", which is the only name",
                "       no$gba will read a DSi NAND under",
                "    3. no$gba opens - set the console up, then quit it",
                "    4. this window comes back and asks whether it worked",
                "",
                "Either way, the game you launched does not start this time. Launch it again",
                "once the console is ready.",
                "",
                "This is a " + DsiRegions.Name(region) + " NAND, and you are asked once per NAND -",
                "every DSiWare of this region then runs on the same console.",
            });

        private static string Instruction()
            => string.Join(Environment.NewLine, new List<string>
            {
                "no$gba is open, on the copy that will become your console.",
                "",
                "It boots the DSi menu by itself: this plugin has already set",
                "    " + NoGbaDsi.ModeKey + "  to  " + NoGbaDsi.ModeDsi,
                "    " + NoGbaDsi.EntryKey + "  to  " + NoGbaDsi.EntryBios,
                "",
                "Go through the welcome sequence - name, language, date, colour - then quit",
                "no$gba, and you will be asked whether it worked.",
                "",
                "If nothing boots, check Options > Emulation Setup: a value no$gba does not",
                "recognise is ignored in silence, and those two are the ones that matter.",
            });

        private static string Confirmation(string name)
            => string.Join(Environment.NewLine, new List<string>
            {
                "no$gba has closed. Is the console you made from " + name + " set up the way",
                "you want it?",
                "",
                "Yes, keep it",
                "    it is kept in no$gba's dsi folder, and every DSiWare save you make from",
                "    now on records which console it belongs to - so it can be rebuilt from",
                "    your dump if it is ever lost. That record is the same one melonDS writes,",
                "    so a save can move between the two.",
                "",
                "No, throw it away",
                "    the copy is deleted. Your own dump was never touched, so there is nothing",
                "    to put back, and you will be asked again next time.",
                "",
                "If you are not sure, throw it away - going through this again costs nothing.",
            });
    }
}
