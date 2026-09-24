// Making a console out of a dump, once, and never touching the dump to do it.
//
// A DSiWARE SAVE IS A DIFFERENCE against a base image - see DsiDelta - so that base has to exist
// and has to be configured: a NAND out of a real console carries that console's name, language,
// birthday and colour, and one from anywhere else carries a stranger's or an unfinished welcome
// sequence the DSi menu insists on completing. Completing it WRITES INTO THE IMAGE.
//
// SO THE IMAGE THAT GETS WRITTEN INTO IS OURS, NOT HIS. The dump is copied into dsi\ under its own
// name, and melonDS is pointed at the copy. This file used to do the opposite - configure the user's
// dump in place and keep a pristine copy as <dump>.lock to repair it afterwards - which meant the
// folder where somebody keeps pristine dumps held one that was not, under the name that said it was.
// The .lock, the .bak, the "never touch this file again" warning and the whole three-state machine
// existed to manage that damage. None of them survive the damage not being done.
//
// AND THE NAME IS THE LINK. dsi\<the dump's file name> is that dump's console. One File.Exists
// answers "has this been set up", and because a dump has exactly one console, "which console" is
// never asked. See DsiBase.ConsoleFor.
//
// THERE IS NO "PLAY WITHOUT A CONSOLE", and that was a deliberate reversal. This window used to
// offer "Not now", which started the game on the raw dump - the behaviour from before any of this
// existed, kept out of a reflex not to gate somebody's game. Measured against the save model, it
// was not the free option it looked like: an unconfigured NAND makes the DSi menu run its welcome
// sequence IN THE GAME, and those settings land in shared1/TWLCFG0.dat and TWLCFG1.dat, which the
// capture files under THAT ONE TITLE's state and RestoreState replays at every launch. The console
// setup then lives once per game, inside each save, instead of once in a console - and those saves
// carry no recipe, so nothing can rebuild them anywhere else. Closing this window abandons the
// launch instead.
//
// WITHOUT A WINDOW, NOTHING IS BUILT AND NOTHING IS REFUSED. With the dialog suppressed there is
// nobody to ask, and a silent refusal would make DSiWare unplayable with no way to find out why -
// so that one case still runs on the dump, and says so in the log. Turning the windows off is an
// explicit act; being asked nothing is what it means.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using LbIntegrations.Dsi;

namespace LbIntegrations.MelonDs
{
    internal static class MelonDsNandSetup
    {
        /// <summary>Beside the log, like every other switch here.</summary>
        private const string KillSwitch = "no-dsi-setup";

        /// <summary>Offer to build a console for this dump.
        ///
        /// Called only when the dump has none - DsiBase.ConsoleFor answered null. Answers TRUE
        /// when the launch that asked should be abandoned: melonDS was just opened on the DSi menu,
        /// and starting the game on top of that would be two emulators on one image.</summary>
        public static bool Run(MelonDsLayout layout, NandDump dump, string bios7Path)
        {
            try
            {
                if (layout?.ConfigFile == null || dump?.Path == null) return false;
                if (Log.Disabled(KillSwitch))
                {
                    Log.Verbose("the " + KillSwitch + " marker is there; no console will be built");
                    return false;
                }

                if (!DsiDialog.Available)
                {
                    // Said once, then dropped. Configuring a console behind somebody's back is not a
                    // lesser evil than leaving a dump unconfigured.
                    Log.Info(Path.GetFileName(dump.Path) + " has no configured console, and windows "
                             + "are turned off; using the dump as it is");
                    return false;
                }

                var name = Path.GetFileName(dump.Path);
                var answer = DsiDialog.Ask("melonDS - no console for this NAND yet",
                                               Announcement(name, dump.Region),
                                               new[] { "Set one up now", "Close" });
                if (answer != 0)
                {
                    // ABANDONED, not "run it anyway". See the head of this file: playing without a
                    // console is not a lesser option, it is the same setup done once per game and
                    // buried in a save that cannot be rebuilt.
                    Log.Info("no console was built for " + name + ", so this launch is dropped. "
                             + "Nothing was written, and you will be asked again next time.");
                    return true;
                }

                var console = DsiBase.BuildConsole(layout, dump.Path, out var error);
                if (console == null)
                {
                    Log.Warn("could not build a console from " + name + " - " + error);
                    DsiDialog.Ask("melonDS - the console could not be built",
                                      "A copy of " + name + " has to be made before it can be set up, "
                                      + "and it could not be:" + Environment.NewLine + Environment.NewLine
                                      + "    " + error + Environment.NewLine + Environment.NewLine
                                      + "Your own dump was not touched, and the game was not "
                                      + "started.",
                                      new[] { "Close" });
                    return true;
                }

                Configure(layout, console);

                var verdict = DsiDialog.Ask("melonDS - is this console set up?",
                                                Confirmation(name),
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
                    Log.Info("the console built from " + name + " was thrown away; your dump was never "
                             + "touched, so there is nothing to put back");
                }
                return true;
            }
            catch (Exception ex) { Log.Warn("could not build a console", ex); return false; }
        }

        /// <summary>Write the recipe and the record beside a console. Safe to call on one that
        /// already has them - it says so and does nothing.
        ///
        /// REFUSES ANYTHING OUTSIDE OUR FOLDER, and that guard is the point of the whole file: the
        /// recipe is written BESIDE the image it describes, so calling this on a user's dump would
        /// drop two files in their folder. The catch-up path in PrepareDsiWare can reach here with
        /// whatever base a launch settled on, and that base is the dump itself when somebody
        /// declined to build a console.</summary>
        public static bool Describe(MelonDsLayout layout, string consolePath, string dumpPath,
                                    string bios7Path, DsiRegion region)
        {
            try
            {
                if (layout == null || consolePath == null || dumpPath == null) return false;
                if (!DsiWorkspace.IsOurs(layout, consolePath))
                {
                    Log.Verbose(Path.GetFileName(consolePath) + " is not ours, so no recipe is written "
                                + "beside it");
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

        /// <summary>Open melonDS on this image, tell the user the one click to make, and wait
        /// for it to be closed.
        ///
        /// ConsoleType = 1 and DirectBoot = false is what makes the firmware - in DSi mode, the menu
        /// held in the NAND - the thing that boots. None of it needs undoing: the next launch
        /// rewrites all three keys before melonDS sees them, and melonDS rewrites the file itself on
        /// the way out.
        ///
        /// BUT THOSE TWO KEYS ONLY DECIDE HOW A ROM STARTS, and there is no ROM here. Started with
        /// nothing to run, melonDS sits on its splash screen saying "File->Open ROM... to get
        /// started"; the menu item File > Boot firmware is the only way in. Measured on 1.1: the
        /// command line has -b/--boot auto|always|never, but it is handed to preloadROMs and does
        /// nothing without a ROM - launched with "-b always" and no ROM, melonDS stayed on the
        /// splash, no window opened and the NAND was not written.
        ///
        /// So the instruction is put on screen, ON TOP OF melonDS, once melonDS is up. Saying it in
        /// the window before would be saying it to somebody who is about to look at something else -
        /// which is exactly what happened: the old text promised "melonDS opens on the DSi menu",
        /// and it does not.</summary>
        private static void Configure(MelonDsLayout layout, string imagePath)
        {
            var exe = MelonDsPaths.FindExecutable(layout.InstallDir);
            if (exe == null) { Log.Warn("no melonDS executable in " + layout.InstallDir); return; }

            var error = MelonDsToml.Write(layout.ConfigFile, MelonDsPaths.DSiTable,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["NANDPath"] = MelonDsToml.Text(imagePath),
                }, force: true);
            if (error != null) { Log.Warn("image not selected for setup: " + error); return; }

            error = MelonDsToml.Write(layout.ConfigFile, MelonDsPaths.EmuTable,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ConsoleType"] = "1",
                    ["DirectBoot"] = "false",
                }, force: true);
            if (error != null) { Log.Warn("boot mode not set for setup: " + error); return; }

            try
            {
                Log.Info("opening melonDS on the DSi menu to set up " + Path.GetFileName(imagePath));
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = layout.InstallDir,
                    UseShellExecute = false,
                });
                // Wait for its window before covering it: our own is TopMost, so showing it
                // first would just mean melonDS appearing over an instruction nobody read.
                try { process?.WaitForInputIdle(15000); } catch { }

                DsiDialog.Ask("melonDS - one menu item to click", Instruction(),
                                  new[] { "OK" });

                process?.WaitForExit();
                Log.Info("melonDS closed");
            }
            catch (Exception ex) { Log.Warn("could not start melonDS for the setup", ex); }
        }

        // ── what the windows say ─────────────────────────────────────────────

        private static string Announcement(string name, DsiRegion region)
        {
            var lines = new List<string>
            {
                "You have no console set up for " + name + " yet.",
                "",
                "A DSi NAND is a copy of a whole console, and a console has to be set up once:",
                "your name, language, date and favourite colour. Doing that writes into the",
                "image - so it is done on a COPY, kept in melonDS's own folder.",
                "",
                "YOUR OWN DUMP IS NOT TOUCHED. It stays exactly as it is, and it is what every",
                "console is rebuilt from if one is ever lost.",
                "",
                "If you set one up now:",
                "",
                "    1. " + name + " is copied into melonDS's dsi folder",
                "    2. melonDS opens on that copy - you click File > Boot firmware,",
                "       which starts the DSi menu. It will say so again on screen.",
                "    3. set the console up, then quit melonDS",
                "    4. this window comes back and asks whether it worked",
                "",
                "Either way, the game you launched does not start this time. Launch it again",
                "once the console is ready.",
                "",
                "This is a " + DsiRegions.Name(region) + " NAND, and you are asked once per NAND -",
                "every DSiWare of this region then runs on the same console.",
                "",
                "There is no \"play without a console\". An unconfigured NAND makes the DSi",
                "menu run its welcome sequence INSIDE THE GAME, and those settings are then",
                "saved as part of that one game - the same again for the next one, in a save",
                "nothing can rebuild if you lose it.",
            };
            return string.Join(Environment.NewLine, lines);
        }

        private static string Instruction()
        {
            var lines = new List<string>
            {
                "melonDS is open, on the copy that will become your console.",
                "",
                "    In melonDS:  File  >  Boot firmware",
                "",
                "That starts the DSi menu. Set the console up - name, language, date, colour -",
                "then quit melonDS, and you will be asked whether it worked.",
                "",
                "melonDS lands on its splash screen rather than the menu because it has no",
                "command line for booting the firmware: that menu item is the only way in.",
            };
            return string.Join(Environment.NewLine, lines);
        }

        private static string Confirmation(string name)
        {
            var lines = new List<string>
            {
                "melonDS has closed. Is the console you made from " + name + " set up the way",
                "you want it?",
                "",
                "Yes, keep it",
                "    it is kept in melonDS's dsi folder, and every DSiWare save you make from",
                "    now on records which console it belongs to - so it can be rebuilt from",
                "    your dump if it is ever lost.",
                "",
                "No, throw it away",
                "    the copy is deleted. Your own dump was never touched, so there is nothing",
                "    to put back, and you will be asked again next time.",
                "",
                "If you are not sure, throw it away - going through this again costs nothing.",
            };
            return string.Join(Environment.NewLine, lines);
        }
    }
}
