// The first time a NAND dump is used, and why that moment needs a window.
//
// A DSiWARE SAVE IS A DIFFERENCE, not a file. The working image is rebuilt from the user's base dump
// on every launch, the title is installed into it, and what is kept between sessions is the set of
// files that differ from that fresh install - see MelonDsDelta. Every one of those differences was
// computed against a particular base image, and is replayed onto a rebuild of that same image.
//
// SO THE BASE IMAGE MUST NOT MOVE. Change it and every difference already on disk describes a console
// that no longer exists: the paths still resolve, the bytes still apply, and the result is a console
// whose saves came from somewhere else. Nothing crashes. It just quietly stops meaning anything.
//
// AND A FRESH DUMP HAS TO BE CONFIGURED ONCE. A NAND out of a real console carries that console's
// setup - name, language, birthday, colour - and one pulled off the internet carries a stranger's, or
// an unfinished welcome sequence that the DSi menu will insist on completing. Completing it WRITES
// INTO THE NAND. There is no way to have both a configured console and an untouched base image unless
// the configuring happens first, deliberately, before a single save exists.
//
// Hence: say so, offer to do it now, keep a copy while it happens, ask afterwards whether it worked,
// and leave a marker so it is asked exactly once per dump.
//
// THE MARKER IS THE COPY. X.bak is made before melonDS opens; on success it is RENAMED to X.lock
// rather than deleted. One file move instead of a delete plus a create, and what is left behind is
// not an empty flag but the image as it was before anybody touched it - so the one irreversible thing
// in this flow becomes reversible. It costs 240 MB per dump. Making it empty is a one-line change if
// that trade stops being worth it.
//
// WITHOUT A WINDOW, NOTHING HAPPENS. Every path here is gated on MelonDsDialog.Available. Configuring
// somebody's console silently, or copying 240 MB and starting an emulator they did not ask for, would
// be far worse than leaving a dump unconfigured.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace LbIntegrations.MelonDs
{
    /// <summary>Where a NAND dump stands with respect to its one-time setup.</summary>
    internal enum NandSetup
    {
        /// <summary>No marker beside it: it has never been through this.</summary>
        NeverUsed,

        /// <summary>A .lock beside it: set up, and to be left alone from now on.</summary>
        Locked,

        /// <summary>A .bak beside it and no .lock: a previous attempt started and never got its
        /// answer. The copy is still there, so nothing is lost - but nothing is settled either.</summary>
        Interrupted,
    }

    internal static class MelonDsNandSetup
    {
        /// <summary>Appended to the NAND's own name, so `dsinand.bin` gets `dsinand.bin.lock`. Beside
        /// the file rather than in a folder of ours: whoever moves or renames a dump takes its
        /// markers with it, and a dump that arrives without them is - correctly - new.</summary>
        public const string LockSuffix = ".lock";
        public const string BakSuffix = ".bak";

        public static string LockFor(string nandPath) => nandPath + LockSuffix;
        public static string BakFor(string nandPath) => nandPath + BakSuffix;

        /// <summary>Which of the three states a dump is in. An error answers Locked: being unable to
        /// look at the folder is not a reason to start copying 240 MB and opening windows.</summary>
        public static NandSetup Of(string nandPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(nandPath)) return NandSetup.Locked;

                // A BASE WE REBUILT IS CONFIGURED BY CONSTRUCTION. It has no .lock beside it, so the
                // test below would call it NeverUsed and open the first-use window - asking somebody
                // to configure a console that is already configured. Worse, doing so would change
                // its identity and break the very save that was being recovered.
                if (MelonDsBase.IsArchive(nandPath)) return NandSetup.Locked;

                if (File.Exists(BakFor(nandPath))) return NandSetup.Interrupted;
                return File.Exists(LockFor(nandPath)) ? NandSetup.Locked : NandSetup.NeverUsed;
            }
            catch { return NandSetup.Locked; }
        }

        /// <summary>Put a dump through its first use, if it needs it.
        ///
        /// Answers TRUE when the launch that asked should be abandoned - melonDS was just opened on
        /// the DSi menu, and starting the game on top of that would be two emulators on one NAND.
        /// The user relaunches once the console is set up.</summary>
        public static bool Run(MelonDsLayout layout, NandDump nand)
        {
            try
            {
                if (layout?.ConfigFile == null || nand?.Path == null) return false;

                var state = Of(nand.Path);          // re-read: the cached one may be a launch old
                if (state == NandSetup.Locked) return false;

                if (!MelonDsDialog.Available)
                {
                    // Said once, in the log, and then dropped. Configuring a console behind somebody's
                    // back is not a lesser evil than leaving it unconfigured.
                    Log.Info(Path.GetFileName(nand.Path) + " has never been set up, and windows are "
                             + "turned off; using it as it is");
                    return false;
                }

                return state == NandSetup.Interrupted
                    ? Resume(layout, nand)
                    : Begin(layout, nand);
            }
            catch (Exception ex) { Log.Warn("could not run the NAND setup", ex); return false; }
        }

        // ── never used ───────────────────────────────────────────────────────

        private static bool Begin(MelonDsLayout layout, NandDump nand)
        {
            var name = Path.GetFileName(nand.Path);
            var answer = MelonDsDialog.Ask("melonDS - first time using this NAND",
                                           Announcement(name, nand.Region),
                                           new[] { "Set it up now", "Not now" });
            if (answer != 0)
            {
                Log.Info(name + " was not set up; using it as it is. This will be asked again next "
                         + "time, until it is set up or a " + name + LockSuffix + " is put beside it.");
                return false;
            }

            if (!KeepACopy(nand.Path)) return false;

            Configure(layout, nand.Path);

            var verdict = MelonDsDialog.Ask("melonDS - is this NAND set up?",
                                            Confirmation(name),
                                            new[] { "Yes, lock it", "No, put it back" });
            Settle(layout, nand.Path, verdict);
            return true;
        }

        // ── a previous attempt that never got its answer ─────────────────────

        private static bool Resume(MelonDsLayout layout, NandDump nand)
        {
            var name = Path.GetFileName(nand.Path);
            var answer = MelonDsDialog.Ask("melonDS - this NAND was being set up",
                                           Interrupted(name),
                                           new[] { "It is set up, lock it",
                                                   "Open melonDS again",
                                                   "Put the copy back" });

            if (answer == 0) { Settle(layout, nand.Path, 0); return false; }

            if (answer == 1)
            {
                Configure(layout, nand.Path);
                var verdict = MelonDsDialog.Ask("melonDS - is this NAND set up?",
                                                Confirmation(name),
                                                new[] { "Yes, lock it", "No, put it back" });
                Settle(layout, nand.Path, verdict);
                return true;
            }

            if (answer == 2) { Settle(layout, nand.Path, 1); return true; }

            Log.Info(name + " is still half set up; leaving " + name + BakSuffix + " where it is");
            return false;
        }

        // ── the three things this actually does to files ─────────────────────

        /// <summary>X -> X.bak, with the room for it checked first. A half-written copy would be
        /// worse than no copy: it looks exactly like a good one.</summary>
        private static bool KeepACopy(string nandPath)
        {
            var bak = BakFor(nandPath);
            try
            {
                long size = new FileInfo(nandPath).Length;
                long free = FreeSpaceOn(bak);
                if (free >= 0 && free < size + (16L * 1024 * 1024))
                {
                    Log.Warn("not enough room beside " + Path.GetFileName(nandPath) + " for a copy: "
                             + Megabytes(free) + " free, " + Megabytes(size) + " needed");
                    MelonDsDialog.Ask("melonDS - not enough room",
                                      "A copy of " + Path.GetFileName(nandPath) + " is kept while the "
                                      + "console is set up, so nothing can be lost." + Environment.NewLine
                                      + Environment.NewLine
                                      + "There is not enough free space for it: " + Megabytes(size)
                                      + " is needed and " + Megabytes(free) + " is free." + Environment.NewLine
                                      + Environment.NewLine
                                      + "Free some space and launch the game again.",
                                      new[] { "Close" });
                    return false;
                }

                // Copied to a scratch name and moved into place, so an interrupted copy never ends up
                // wearing the name that means "a good copy is here".
                var partial = bak + ".part";
                try { if (File.Exists(partial)) File.Delete(partial); } catch { }
                File.Copy(nandPath, partial, overwrite: true);
                if (File.Exists(bak)) File.Delete(bak);
                File.Move(partial, bak);

                Log.Info("kept a copy of " + Path.GetFileName(nandPath) + " as "
                         + Path.GetFileName(bak) + " before setting it up");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("could not copy " + Path.GetFileName(nandPath), ex);
                try { if (File.Exists(bak)) File.Delete(bak); } catch { }
                return false;
            }
        }

        /// <summary>Open melonDS on the DSi menu, on this NAND, and wait for it to be closed.
        ///
        /// NO ROM ARGUMENT, ConsoleType = 1 and DirectBoot = false: that combination boots the
        /// firmware, and in DSi mode the firmware IS the menu held in the NAND. NANDPath is pointed
        /// at the raw dump rather than the working image, because the whole point is to write into
        /// the dump. None of it needs undoing - the next launch rewrites all three keys before
        /// melonDS sees them, and melonDS rewrites the file itself on the way out.</summary>
        private static void Configure(MelonDsLayout layout, string nandPath)
        {
            var exe = MelonDsPaths.FindExecutable(layout.InstallDir);
            if (exe == null) { Log.Warn("no melonDS executable in " + layout.InstallDir); return; }

            var error = MelonDsToml.Write(layout.ConfigFile, MelonDsPaths.DSiTable,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["NANDPath"] = MelonDsToml.Text(nandPath),
                }, force: true);
            if (error != null) { Log.Warn("NAND not selected for setup: " + error); return; }

            error = MelonDsToml.Write(layout.ConfigFile, MelonDsPaths.EmuTable,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ConsoleType"] = "1",
                    ["DirectBoot"] = "false",
                }, force: true);
            if (error != null) { Log.Warn("boot mode not set for setup: " + error); return; }

            try
            {
                Log.Info("opening melonDS on the DSi menu to set up " + Path.GetFileName(nandPath));
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = layout.InstallDir,
                    UseShellExecute = false,
                });
                process?.WaitForExit();
                Log.Info("melonDS closed");
            }
            catch (Exception ex) { Log.Warn("could not start melonDS for the setup", ex); }
        }

        /// <summary>The copy becomes the lock, or the copy goes back. <paramref name="verdict"/> is
        /// the button index: 0 yes, 1 no, anything else no answer at all.</summary>
        private static void Settle(MelonDsLayout layout, string nandPath, int verdict)
        {
            var bak = BakFor(nandPath);
            var lockFile = LockFor(nandPath);
            var name = Path.GetFileName(nandPath);

            try
            {
                if (verdict == 0)
                {
                    if (File.Exists(lockFile)) File.Delete(lockFile);
                    if (File.Exists(bak)) File.Move(bak, lockFile);
                    else File.WriteAllText(lockFile, "");   // no copy to keep; the marker still matters
                    Log.Info(name + " is set up and locked. It will not be asked about again.");

                    // THE ONE MOMENT BOTH IMAGES EXIST. The pristine dump is now the .lock and the
                    // configured one is the NAND itself, so the difference between them - everything
                    // that makes this console THIS console - can be written down. From here on a save
                    // made on it can rebuild it from the dump alone. See MelonDsBase.
                    Describe(layout, nandPath);
                    return;
                }

                if (verdict == 1)
                {
                    if (File.Exists(bak))
                    {
                        File.Copy(bak, nandPath, overwrite: true);
                        File.Delete(bak);
                        Log.Info(name + " was put back as it was; nothing was kept");
                    }
                    return;
                }

                Log.Info("no answer about " + name + "; leaving " + Path.GetFileName(bak)
                         + " in place, and this will be picked up again next launch");
            }
            catch (Exception ex)
            {
                Log.Warn("could not finish the setup of " + name + "; " + Path.GetFileName(bak)
                         + " is still there and holds the image as it was", ex);
            }
        }

        /// <summary>Write the recipe and the record beside a locked NAND. Safe to call on one that
        /// already has them - it says so and does nothing.</summary>
        public static bool Describe(MelonDsLayout layout, string nandPath)
        {
            try
            {
                if (layout == null || nandPath == null) return false;
                if (MelonDsBase.Described(nandPath)) return true;

                var original = LockFor(nandPath);
                if (!File.Exists(original))
                {
                    Log.Verbose(Path.GetFileName(nandPath) + " has no .lock beside it, so there is no "
                                + "pristine dump to measure its console setup against");
                    return false;
                }

                var bios7 = MelonDsBios.Find(layout, MelonDsBios.DsiBios7);
                if (bios7 == null) return false;

                if (MelonDsBase.MakeRecipe(original, nandPath, bios7, out var error)) return true;
                Log.Verbose("could not describe " + Path.GetFileName(nandPath) + " - " + error);
                return false;
            }
            catch (Exception ex) { Log.Warn("could not describe a NAND", ex); return false; }
        }

        // ── what the windows say ─────────────────────────────────────────────

        private static string Announcement(string name, DsiRegion region)
        {
            var lines = new List<string>
            {
                "This is the first time " + name + " is used.",
                "",
                "A DSi NAND is a copy of a whole console, and a console has to be set up once:",
                "your name, language, date and favourite colour. Doing that writes into the NAND,",
                "so it has to happen now, before any game saves anything.",
                "",
                "AFTER THAT, LEAVE THIS FILE ALONE. Every DSiWare save is kept as the difference",
                "between this NAND and the one your game ran on. Change this file later and those",
                "differences describe a console that no longer exists - nothing will crash, your",
                "saves will simply stop making sense.",
                "",
                "If you choose to set it up now:",
                "",
                "    1. a copy of " + name + " is kept, so nothing can be lost",
                "    2. melonDS opens on the DSi menu, on this NAND",
                "    3. set the console up, then quit melonDS",
                "    4. this window comes back and asks whether it worked",
                "",
                "The game you launched will not start this time - launch it again once the",
                "console is ready.",
                "",
                "This is a " + MelonDsRegion.Name(region) + " NAND, and you are asked once per NAND.",
            };
            return string.Join(Environment.NewLine, lines);
        }

        private static string Confirmation(string name)
        {
            var lines = new List<string>
            {
                "melonDS has closed. Is " + name + " set up the way you want it?",
                "",
                "Yes, lock it",
                "    " + name + " is taken as final. The copy that was kept becomes",
                "    " + name + LockSuffix + ", so you can always go back to how it was,",
                "    and this is never asked about again.",
                "",
                "No, put it back",
                "    " + name + " goes back exactly as it was, and you will be asked again",
                "    next time you launch a DSiWare game.",
                "",
                "If you are not sure, put it back - going through this again costs nothing.",
            };
            return string.Join(Environment.NewLine, lines);
        }

        private static string Interrupted(string name)
        {
            var lines = new List<string>
            {
                name + " was being set up and the question never got answered.",
                "",
                "A copy from before that setup is still here, as " + name + BakSuffix + ",",
                "so nothing has been lost either way.",
                "",
                "It is set up, lock it",
                "    keep " + name + " as it is now; the copy becomes " + name + LockSuffix + ".",
                "",
                "Open melonDS again",
                "    go back to the DSi menu and carry on setting the console up.",
                "",
                "Put the copy back",
                "    undo everything: " + name + " goes back to how it was before.",
            };
            return string.Join(Environment.NewLine, lines);
        }

        // ── small things ─────────────────────────────────────────────────────

        private static long FreeSpaceOn(string path)
        {
            try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))).AvailableFreeSpace; }
            catch { return -1; }
        }

        private static string Megabytes(long bytes)
            => (bytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture) + " MB";
    }
}
