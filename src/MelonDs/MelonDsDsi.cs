// One working NAND, rebuilt at every launch, and a few kilobytes of difference kept per title.
//
// WHY A NAND AT ALL. A DSiWare title does not run from its .nds the way a cartridge does: melonDS
// boots it out of the NAND, and its save lives inside the NAND too, at
// title/<category>/<id>/data/public.sav (DSi_NAND.cpp:1077-1092). So an image has to exist, be
// pointed at, and hold the title.
//
// WHAT THIS REPLACES. The first design gave each title its own copy of the user's dump. It worked,
// and it cost 240 MB per game for a few kilobytes of state. Measuring what actually differs - see
// MelonDsDelta, which is where the numbers are - showed a whole session to be 80 KB inside a 240 MB
// image. So now there is ONE image, it is scratch, and what is kept per title is the difference.
//
//     <install>\dsi\base.bin                    the user's own dump, copied here once
//     <install>\dsi\work.bin                    the working image, rebuilt at every launch
//     <install>\dsi\work.title                  which title work.bin currently holds
//     <install>\dsi\0003000412345678\
//         title.tmd                             the metadata that installed it
//         reference.txt                         the walk of a fresh install
//         state\                                the files that differ from that walk - the save
//         public.sav                            a copy of the game's own save, for the host to list
//
// Disk now: 480 MB whatever the size of the library, plus tens of kilobytes per title. Before:
// 240 MB times the number of DSiWare games.
//
// THE WORKING IMAGE IS KEPT, AS A CACHE OF ONE. Relaunching the same game is the common case, and
// rebuilding for it would mean copying 240 MB and reinstalling a title to arrive at what is already
// on disk - about a quarter of a second, measured, and 240 MB written for nothing. So the image
// stays, and a launch of the same title from the same ROM uses it as it is. Launching anything else
// rebuilds over it, which IS the eviction: there is only ever one.
//
// THE BASE IS THE USER'S OWN DUMP AND NOTHING ELSE. A DSi NAND cannot be fabricated: NANDImage opens
// an existing file and reads the ConsoleID out of it (DSi_NAND.h:52-64, :77), and the decryption
// depends on console-unique data plus the ES key held in dsi_bios7.bin at 0x8308. What this file does
// is copy, never create.
//
// Capturing the base is done ONCE, before anything is overwritten: the moment a DSiWare launch is
// about to repoint DSi.NANDPath, whatever the user had configured would otherwise be lost as a
// source. So the first pass copies it to dsi\base.bin and works from that copy afterwards.
//
// THE CAPTURE HAPPENS BEFORE THE REBUILD, NOT AFTER THE GAME. There is no "the emulator has quit"
// event to hang it on, and a lazy capture would be a data loss waiting to happen: launch game A,
// play, launch game B, and B's rebuild would wipe A's session before anything read it. So every
// launch - of anything, including a plain DS cartridge - first captures whatever work.bin is still
// holding. Reading the saves page captures too, so the host sees fresh state without waiting for a
// launch.
//
// ONLY WHAT IS INSIDE THE FILESYSTEM IS CAPTURED, and that is enough: measured, the region below
// 0x10EE00 - the MBR, the stage2 loader and the "DSi eMMC CID/CPU" footer carrying the console
// identity the decryption key is derived from - did not change in a single byte across a real
// session. It comes from base.bin and stays there, because every rebuild starts from base.bin.

using System;
using System.Globalization;
using System.IO;

namespace LbIntegrations.MelonDs
{
    /// <summary>What happened when a DSiWare title asked for a NAND to run on.</summary>
    internal sealed class DsiNand
    {
        /// <summary>The image melonDS should be pointed at, or null when there is none.</summary>
        public string Path;

        /// <summary>Why there is none, for the log. Null when there is one.</summary>
        public string Reason;
    }

    internal static class MelonDsDsi
    {
        public const string DirName = "dsi";
        public const string BaseName = "base.bin";
        public const string WorkName = "work.bin";
        public const string WorkTitleName = "work.title";
        public const string SaveName = "public.sav";

        /// <summary>The per-title NAND the first design made. Nothing writes one any more; it is
        /// still named here because one found on disk is migrated and then removed.</summary>
        public const string LegacyNandName = "nand.bin";

        /// <summary>Beside the log, the way the other switches in this repository work. Creating it
        /// stops the plugin touching NANDs at all, and DSiWare goes back to being merely reported.</summary>
        private const string KillSwitch = "no-dsi-nand";

        /// <summary>A NAND dump is about 240 MB; refuse to start a copy without room for it plus a
        /// margin, rather than filling the disk and failing halfway.</summary>
        private const long FreeSpaceMargin = 256L * 1024 * 1024;

        public static string DsiDir(MelonDsLayout layout)
            => layout?.InstallDir == null ? null : Path.Combine(layout.InstallDir, DirName);

        public static string BasePath(MelonDsLayout layout)
        {
            var dir = DsiDir(layout);
            return dir == null ? null : Path.Combine(dir, BaseName);
        }

        /// <summary>The one working image. Scratch: rebuilt from base.bin at every launch, and
        /// nothing is expected to survive in it past a capture.</summary>
        public static string WorkPath(MelonDsLayout layout)
        {
            var dir = DsiDir(layout);
            return dir == null ? null : Path.Combine(dir, WorkName);
        }

        /// <summary>Everything kept for one title.</summary>
        public static string TitleDir(MelonDsLayout layout, string titleId)
        {
            var dir = DsiDir(layout);
            return dir == null || string.IsNullOrWhiteSpace(titleId) ? null : Path.Combine(dir, titleId);
        }

        public static string ReferencePathFor(MelonDsLayout layout, string titleId)
        {
            var dir = TitleDir(layout, titleId);
            return dir == null ? null : Path.Combine(dir, MelonDsDelta.ReferenceName);
        }

        public static string StateDirFor(MelonDsLayout layout, string titleId)
        {
            var dir = TitleDir(layout, titleId);
            return dir == null ? null : Path.Combine(dir, MelonDsDelta.StateDirName);
        }

        /// <summary>What the host lists, backs up and hands back: the STATE FOLDER.
        ///
        /// NOT THE IMAGE, and not one file out of it either. The image is 240 MB, so handing that
        /// over would mean hashing 240 MB to draw a freshness dot and a 240 MB vault copy per
        /// backup. But handing over only the game's own public.sav - which is what this used to do,
        /// and it looked tidy - was worse than clumsy, it was WRONG: measured on one real session,
        /// the game's own save was 16 KB out of 4.2 MB across eleven files. The console settings,
        /// the menu's data and the built-in apps' saves had all moved too. A backup would have
        /// captured a fifth of a save.
        ///
        /// So the unit is the whole difference, and it is a DIRECTORY. The host contract has a shape
        /// for that - Xenia in this same repository uses it, because an Xbox 360 save is a folder
        /// too - and it beats packing the folder into an archive: nothing to pack, nothing to unpack,
        /// and no archive quietly changing its own bytes from one write to the next.</summary>
        public static string SavePathFor(MelonDsLayout layout, string titleId)
            => StateDirFor(layout, titleId);

        /// <summary>Where a title's save sits inside the NAND's own filesystem.</summary>
        public static string SaveInNand(string titleId)
        {
            if (string.IsNullOrWhiteSpace(titleId) || titleId.Length != 16) return null;
            return "0:/title/" + titleId.Substring(0, 8) + "/" + titleId.Substring(8) + "/data/" + SaveName;
        }

        /// <summary>Make the dsi\ folder at install time, with a note saying what belongs in it.
        ///
        /// It used to appear only when a NAND was first copied, which meant the message telling a
        /// user to "put your dump at dsi\base.bin" named a folder that was not there. Asking someone
        /// to create a directory from a log line is asking them to guess at a spelling.</summary>
        public static void PrepareFolder(MelonDsLayout layout)
        {
            try
            {
                var dir = DsiDir(layout);
                if (dir == null) return;
                Directory.CreateDirectory(dir);

                var note = Path.Combine(dir, "PUT-YOUR-NAND-HERE.txt");
                if (File.Exists(note) || File.Exists(BasePath(layout))) return;

                File.WriteAllText(note, string.Join("\r\n", new[]
                {
                    "DSiWare needs a DSi NAND. Put yours here, named exactly:",
                    "",
                    "    " + BasePath(layout),
                    "",
                    "It has to be a full dump of YOUR OWN console - the kind that ends with the",
                    "\"DSi eMMC CID/CPU\" footer. A NAND cannot be generated or downloaded: it is",
                    "encrypted with data unique to the console it came from.",
                    "",
                    "You also need dsi_bios7.bin and dsi_bios9.bin, set in melonDS under",
                    "Config > Emu settings > DSi mode. Without those, DSi mode cannot start at all.",
                    "",
                    "Once they are in place, launching a DSiWare game rebuilds a working copy of this",
                    "NAND, installs the title into it, puts that title's saved state back, and points",
                    "melonDS at it. Only the difference is kept per game - tens of kilobytes, not a",
                    "copy of this file.",
                    "",
                    "This file is only a note. You can delete it.",
                }) + "\r\n");
                Log.Info("made " + dir + " - drop a DSi NAND dump in it to enable DSiWare");
            }
            catch (Exception ex) { Log.Verbose("could not prepare the dsi folder - " + ex.Message); }
        }

        /// <summary>Is this path one of ours? True only for something under dsi\, which is the folder
        /// this plugin owns.</summary>
        public static bool IsOurs(MelonDsLayout layout, string path)
        {
            try
            {
                var dir = DsiDir(layout);
                return !string.IsNullOrWhiteSpace(dir) && !string.IsNullOrWhiteSpace(path)
                       && Path.GetFullPath(path)
                              .StartsWith(Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // ── the working image ────────────────────────────────────────────────

        /// <summary>Which title work.bin is currently holding, or null.</summary>
        public static string WorkTitle(MelonDsLayout layout) => MarkerParts(layout)?[0];

        /// <summary>The marker, split. The title id, then the fingerprint of the ROM that was
        /// installed - path, length and write time - then the NAND it was built on. Older markers
        /// are shorter and read back as a title with no fingerprint, which simply means the image
        /// cannot be reused: a rebuild costs a quarter of a second and is always correct.</summary>
        private static string[] MarkerParts(MelonDsLayout layout)
        {
            try
            {
                var marker = MarkerPath(layout);
                if (marker == null || !File.Exists(marker)) return null;
                var parts = File.ReadAllText(marker).Trim().Split('\t');
                return parts.Length >= 1 && parts[0].Length == 16 ? parts : null;
            }
            catch { return null; }
        }

        /// <summary>What identifies the ROM an image was built from, cheaply. Not a hash: this runs
        /// on every launch, and a DSiWare .nds is several megabytes. Path, length and write time are
        /// enough to notice that the file is not the one that went in.</summary>
        private static string Fingerprint(string romPath)
        {
            try
            {
                var info = new FileInfo(romPath);
                return info.FullName + "\t" + info.Length.ToString(CultureInfo.InvariantCulture)
                       + "\t" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
            }
            catch { return null; }
        }

        /// <summary>Can the image on disk simply be used again for this launch?
        ///
        /// A CACHE OF ONE. Relaunching the same game is the common case, and rebuilding for it means
        /// copying 240 MB and reinstalling the title to arrive at what is already there. Reuse is
        /// allowed only when nothing could have made the image stale: it exists, a marker says it
        /// holds THIS title built from THIS ROM, the walk of that install is still on disk, and
        /// melonDS is not in the middle of using it.
        ///
        /// Being wrong here costs nothing that cannot be rebuilt - the state on disk is the save, and
        /// the image is scratch - but it would cost a session, so every condition is checked rather
        /// than assumed.</summary>
        public static bool CanReuseWork(MelonDsLayout layout, NdsRom rom, string romPath, string sourceNand)
        {
            try
            {
                if (Log.Disabled(KillSwitch)) return false;

                var work = WorkPath(layout);
                if (work == null || !File.Exists(work)) return false;

                var parts = MarkerParts(layout);
                if (parts == null || parts.Length != 5) return false;
                if (!string.Equals(parts[0], rom?.TitleId, StringComparison.OrdinalIgnoreCase)) return false;

                var fingerprint = Fingerprint(romPath);
                if (fingerprint == null
                    || !string.Equals(string.Join("\t", parts, 1, 3), fingerprint, StringComparison.OrdinalIgnoreCase))
                    return false;

                // A different NAND means a different console and a different region, so the image on
                // disk is not the one this launch wants however well it matches otherwise.
                if (!string.Equals(parts[4], sourceNand, StringComparison.OrdinalIgnoreCase)) return false;

                var reference = ReferencePathFor(layout, rom.TitleId);
                if (reference == null || !File.Exists(reference)) return false;

                // And nothing may have written the save since. DropWorkIfSaveMoved has normally
                // taken the image away before this is even asked; this is the same question put
                // again at the moment of deciding, so that a reordering upstream can only ever cost
                // a rebuild, never hand the player the session a sync just superseded.
                if (MelonDsWorkSum.Moved(layout, rom.TitleId, out _)) return false;

                return !MelonDsNand.EmulatorRunning();
            }
            catch { return false; }
        }

        private static string MarkerPath(MelonDsLayout layout)
        {
            var dir = DsiDir(layout);
            return dir == null ? null : Path.Combine(dir, WorkTitleName);
        }

        /// <summary>Take whatever work.bin is holding and write it down as that title's state. Safe
        /// to call at any time and on every launch: with no marker it does nothing.
        ///
        /// This is what stands in for an "emulator has quit" event, which does not exist. It runs
        /// BEFORE anything rebuilds work.bin, so a session can never be thrown away unread.</summary>
        public static void CaptureWork(MelonDsLayout layout, string bios7Path, string onlyTitle = null)
        {
            try
            {
                var titleId = WorkTitle(layout);
                if (titleId == null) return;
                if (onlyTitle != null && !string.Equals(onlyTitle, titleId, StringComparison.OrdinalIgnoreCase))
                    return;

                var work = WorkPath(layout);
                if (work == null || !File.Exists(work)) { Forget(layout); return; }

                // AND THE SAME CHECK HERE, for the title that is NOT being launched. A launch of
                // game B captures whatever game A left in the image, and A's save may have been
                // synced in the meantime - a case the check before the reuse decision cannot see,
                // because it asks about the title being launched. Capturing then would write A's old
                // session over A's new save, silently. The image is always rebuildable; the save is
                // not.
                if (MelonDsWorkSum.Moved(layout, titleId, out var how))
                {
                    Log.Info("the save of " + titleId + " changed outside melonDS (" + how
                             + "), so its session is not captured over it; the image is dropped");
                    DropWorkIfItHolds(layout, titleId, "because its save changed elsewhere");
                    return;
                }

                var reference = ReferencePathFor(layout, titleId);
                if (reference == null || !File.Exists(reference))
                {
                    Log.Verbose("no reference for " + titleId + ", so its session cannot be captured");
                    return;
                }
                if (string.IsNullOrWhiteSpace(bios7Path) || !File.Exists(bios7Path)) return;

                using var session = MelonDsNand.Open(work, bios7Path, out var error);
                if (session == null)
                {
                    Log.Verbose("could not open the working NAND to capture " + titleId + " - " + error);
                    return;
                }

                int kept = MelonDsDelta.Capture(session, reference, StateDirFor(layout, titleId),
                                                TitleDir(layout, titleId), out var why);
                if (kept < 0) { Log.Verbose("could not capture " + titleId + " - " + why); return; }

                // The save takes a copy of the recipe for the base it was made on, so it can rebuild
                // that base later from the user's pristine dump. Two small files; the base they
                // describe is named in the marker, fifth field.
                //
                // BEFORE THE RECEIPT, AND THAT ORDER IS LOAD-BEARING. The receipt is a fingerprint of
                // the whole state folder, and Capture replaces that folder wholesale - so these two
                // files are written fresh after every capture. Writing the receipt first described a
                // folder that no longer existed a millisecond later, and the next launch read that as
                // "the save changed outside melonDS (2 added)" and threw the working image away.
                // Measured on a real session, on the first launch after this was added.
                CarryBase(layout, titleId);

                // The two are in step again, so the receipt is rewritten to say so.
                MelonDsWorkSum.Write(layout, titleId);

                Log.Verbose("captured " + kept + " file(s) of state for " + titleId);
            }
            catch (Exception ex) { Log.Warn("could not capture the working NAND", ex); }
        }

        /// <summary>Copy the recipe and record of whatever base this image was built on into the
        /// title's state folder, so the save carries them wherever it goes.
        ///
        /// SILENT WHEN THERE IS NOTHING TO CARRY. A base locked before any of this existed has no
        /// recipe yet; the save is then exactly what it used to be, and the catch-up at the next
        /// launch fixes it.</summary>
        private static void CarryBase(MelonDsLayout layout, string titleId)
        {
            try
            {
                var parts = MarkerParts(layout);
                if (parts == null || parts.Length != 5) return;

                var baseImage = parts[4];
                if (string.IsNullOrWhiteSpace(baseImage) || !MelonDsBase.Described(baseImage)) return;

                var stateDir = StateDirFor(layout, titleId);
                if (stateDir == null || !Directory.Exists(stateDir)) return;

                File.Copy(MelonDsBase.RecipeFor(baseImage),
                          Path.Combine(stateDir, MelonDsBase.RecipeInState), overwrite: true);
                File.Copy(MelonDsBase.RecordFor(baseImage),
                          Path.Combine(stateDir, MelonDsBase.RecordInState), overwrite: true);
            }
            catch (Exception ex) { Log.Verbose("could not carry the base into the save - " + ex.Message); }
        }

        private static void Cleanup(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        /// <summary>Forget which title the image holds - and the receipt with it, which describes
        /// an agreement that no longer has two parties.</summary>
        private static void Forget(MelonDsLayout layout)
        {
            try { var m = MarkerPath(layout); if (m != null && File.Exists(m)) File.Delete(m); } catch { }
            MelonDsWorkSum.Forget(layout);
        }

        /// <summary>The first design's per-title NAND, kept for ONE case: no native library.
        ///
        /// Without it nothing can be installed, walked or captured, so the scheme above has no
        /// footing - and rebuilding a scratch image every launch would be actively worse than what it
        /// replaced, because the one thing a user can still do by hand, importing the title through
        /// Manage DSi titles, would be wiped on the next launch. So in that case the title keeps an
        /// image of its own, which is where a manual import survives.</summary>
        public static DsiNand EnsureLegacy(MelonDsLayout layout, NdsRom rom, string sourceNand)
        {
            var result = new DsiNand();
            try
            {
                if (Log.Disabled(KillSwitch))
                {
                    result.Reason = "switched off by the " + KillSwitch + " marker";
                    return result;
                }

                var titleDir = TitleDir(layout, rom?.TitleId);
                if (titleDir == null) { result.Reason = "no install directory to put it in"; return result; }
                var target = Path.Combine(titleDir, LegacyNandName);
                if (File.Exists(target)) { result.Path = target; return result; }

                var source = sourceNand;
                if (source == null || !File.Exists(source))
                {
                    result.Reason = "no DSi NAND to copy from";
                    return result;
                }

                var length = new FileInfo(source).Length;
                var room = FreeSpaceOn(target);
                if (room >= 0 && room < length + FreeSpaceMargin)
                {
                    result.Reason = "not enough free space for a " + Megabytes(length)
                                  + " NAND (" + Megabytes(room) + " free)";
                    return result;
                }

                Directory.CreateDirectory(titleDir);
                CopyIntoPlace(source, target);
                result.Path = target;
                Log.Info("created a NAND for title " + rom.TitleId + " (" + Megabytes(length) + "): " + target);
                return result;
            }
            catch (Exception ex)
            {
                Log.Warn("could not prepare a DSi NAND", ex);
                result.Reason = ex.GetType().Name + ": " + ex.Message;
                return result;
            }
        }

        /// <summary>The image as it stands, for a launch that reuses it. Answers in the same shape
        /// as Rebuild so the caller does not have to care which of the two it got.</summary>
        public static DsiNand ExistingWork(MelonDsLayout layout)
        {
            var work = WorkPath(layout);
            return work != null && File.Exists(work)
                ? new DsiNand { Path = work }
                : new DsiNand { Reason = "the working NAND is not there after all" };
        }

        /// <summary>Rebuild work.bin from the base. The caller then installs the title into it and
        /// calls TakeReferenceAndRestore with the same session.</summary>
        public static DsiNand Rebuild(MelonDsLayout layout, NdsRom rom, string sourceNand)
        {
            var result = new DsiNand();
            try
            {
                if (Log.Disabled(KillSwitch))
                {
                    result.Reason = "switched off by the " + KillSwitch + " marker";
                    return result;
                }

                var work = WorkPath(layout);
                var titleDir = TitleDir(layout, rom?.TitleId);
                if (work == null || titleDir == null)
                { result.Reason = "no install directory to work in"; return result; }

                var source = sourceNand;
                if (source == null || !File.Exists(source))
                {
                    result.Reason = "no DSi NAND to copy from";
                    return result;
                }

                var length = new FileInfo(source).Length;
                var room = FreeSpaceOn(work);
                if (room >= 0 && !File.Exists(work) && room < length + FreeSpaceMargin)
                {
                    result.Reason = "not enough free space for a " + Megabytes(length)
                                  + " working NAND (" + Megabytes(room) + " free)";
                    return result;
                }

                Directory.CreateDirectory(titleDir);
                Forget(layout);                       // its old contents are gone as of the next line
                CopyIntoPlace(source, work);

                result.Path = work;
                return result;
            }
            catch (Exception ex)
            {
                Log.Warn("could not prepare the working NAND", ex);
                result.Reason = ex.GetType().Name + ": " + ex.Message;
                return result;
            }
        }

        /// <summary>Write down the walk of the freshly installed image, then put the saved state back.
        /// Called with the session that just installed the title, so this costs no extra open.
        ///
        /// ORDER IS THE POINT. The reference has to be the walk of a fresh install and NOTHING else,
        /// so it is taken before the state goes back in. Taken afterwards it would describe the state
        /// as part of the install, and the next capture would find no difference at all.</summary>
        public static bool TakeReference(MelonDsLayout layout, NandSession session, string titleId)
        {
            try
            {
                var reference = ReferencePathFor(layout, titleId);
                if (reference == null) return false;

                var tmp = reference + "." + Guid.NewGuid().ToString("N") + ".part";
                if (session.Walk(tmp, out var error) < 0)
                {
                    Cleanup(tmp);
                    Log.Verbose("could not walk the fresh image for " + titleId + " - " + error);
                    return false;
                }
                if (File.Exists(reference)) File.Delete(reference);
                File.Move(tmp, reference);
                return true;
            }
            catch (Exception ex) { Log.Warn("could not walk a fresh DSiWare image", ex); return false; }
        }

        /// <summary>Put this title's saved state back into the working image. Called after the
        /// reference has been taken, and after a migration has had its chance to produce one.</summary>
        public static void RestoreState(MelonDsLayout layout, string titleId, string bios7Path)
        {
            try
            {
                var stateDir = StateDirFor(layout, titleId);
                var index = stateDir == null ? null : Path.Combine(stateDir, MelonDsDelta.IndexName);
                if (index == null || !File.Exists(index)) return;     // never played, nothing to put back

                var work = WorkPath(layout);
                if (work == null || !File.Exists(work)) return;
                if (string.IsNullOrWhiteSpace(bios7Path) || !File.Exists(bios7Path)) return;

                using var session = MelonDsNand.Open(work, bios7Path, out var error);
                if (session == null)
                {
                    Log.Warn("could not open the working NAND to put back the state of " + titleId
                             + " - " + error);
                    return;
                }

                int written = MelonDsDelta.Apply(session, stateDir, out var why);
                if (written < 0) { Log.Warn("could not put back the state of " + titleId + " - " + why); return; }
                if (written > 0) Log.Verbose("put back " + written + " file(s) of state for " + titleId);
            }
            catch (Exception ex) { Log.Warn("could not restore a DSiWare state", ex); }
        }

        /// <summary>Remember that work.bin now holds this title. Written last, once everything else
        /// has succeeded: a marker naming a title whose state was never put back would make the next
        /// capture overwrite a good state with a blank one.</summary>
        public static void RememberWork(MelonDsLayout layout, string titleId, string romPath,
                                        string sourceNand)
        {
            try
            {
                var marker = MarkerPath(layout);
                if (marker == null) return;
                var fingerprint = Fingerprint(romPath);
                File.WriteAllText(marker, fingerprint == null
                    ? titleId
                    : titleId + "\t" + fingerprint + "\t" + (sourceNand ?? ""));

                // The image has just been built around this state, so the two are in step: written
                // down here, and checked before the next capture is allowed to overwrite anything.
                MelonDsWorkSum.Write(layout, titleId);
            }
            catch (Exception ex) { Log.Verbose("could not write the work marker - " + ex.Message); }
        }

        /// <summary>A per-title NAND left by the first design, if there is one.</summary>
        public static string LegacyNandFor(MelonDsLayout layout, string titleId)
        {
            var dir = TitleDir(layout, titleId);
            if (dir == null) return null;
            var path = Path.Combine(dir, LegacyNandName);
            return File.Exists(path) ? path : null;
        }

        /// <summary>Take a per-title NAND from the first design, write its difference down as state,
        /// and delete the 240 MB image. Nothing is removed until the state has been written - a
        /// migration that cannot read the old image leaves it exactly where it is.</summary>
        public static bool Migrate(MelonDsLayout layout, string titleId, string bios7Path)
        {
            try
            {
                var legacy = LegacyNandFor(layout, titleId);
                var reference = ReferencePathFor(layout, titleId);
                if (legacy == null || reference == null || !File.Exists(reference)) return false;
                if (string.IsNullOrWhiteSpace(bios7Path) || !File.Exists(bios7Path)) return false;

                int kept;
                using (var session = MelonDsNand.Open(legacy, bios7Path, out var error))
                {
                    if (session == null)
                    {
                        Log.Warn("could not open the old NAND of " + titleId + " to migrate it - " + error
                                 + "; it is left where it is");
                        return false;
                    }
                    kept = MelonDsDelta.Capture(session, reference, StateDirFor(layout, titleId),
                                                TitleDir(layout, titleId), out var why);
                    if (kept < 0)
                    {
                        Log.Warn("could not read the state out of the old NAND of " + titleId + " - " + why
                                 + "; it is left where it is");
                        return false;
                    }
                }

                var size = new FileInfo(legacy).Length;
                File.Delete(legacy);
                Log.Info("migrated " + titleId + ": its state is now " + kept + " file(s) instead of a "
                         + Megabytes(size) + " image, which has been removed");
                return true;
            }
            catch (Exception ex) { Log.Warn("could not migrate an old per-title NAND", ex); return false; }
        }

        // ── the host's view of the save ──────────────────────────────────────

        /// <summary>The extracted save for this title, as fresh as it can cheaply be made.
        ///
        /// WORK.BIN'S TIMESTAMP IS THE TRIGGER. Opening an image means decrypting it and mounting a
        /// filesystem, far too much to do on every page render; so a capture happens only when this
        /// title is the one work.bin holds AND melonDS has written to it since the last one. In the
        /// steady state this is two calls to File.GetLastWriteTimeUtc and nothing else.</summary>
        public static string RefreshSave(MelonDsLayout layout, string titleId, string bios7Path)
        {
            try
            {
                var stateDir = StateDirFor(layout, titleId);
                if (stateDir == null) return null;
                var index = Path.Combine(stateDir, MelonDsDelta.IndexName);

                var work = WorkPath(layout);
                if (string.Equals(WorkTitle(layout), titleId, StringComparison.OrdinalIgnoreCase)
                    && work != null && File.Exists(work)
                    && (!File.Exists(index)
                        || File.GetLastWriteTimeUtc(index) < File.GetLastWriteTimeUtc(work)))
                    CaptureWork(layout, bios7Path, onlyTitle: titleId);

                return File.Exists(index) ? stateDir : null;
            }
            catch (Exception ex) { Log.Warn("could not extract a DSiWare save", ex); return null; }
        }

        /// <summary>Throw the working image away when this title's save has been written by
        /// something other than melonDS since the image was last agreed with it.
        ///
        /// CALLED BEFORE THE REUSE DECISION, and that ordering is the whole point. Merely declining
        /// to reuse a stale image is not enough: a launch that does not reuse goes on to CAPTURE the
        /// image first, which would write the session it holds over the save that has just arrived -
        /// the exact thing being guarded against, performed by the guard's own fallback. So the
        /// image is DROPPED, not just refused, and the capture that follows finds nothing to do.
        ///
        /// Answers whether anything was thrown away.</summary>
        public static bool DropWorkIfSaveMoved(MelonDsLayout layout, string titleId)
        {
            try
            {
                if (!string.Equals(WorkTitle(layout), titleId, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!MelonDsWorkSum.Moved(layout, titleId, out var how)) return false;

                Log.Info("the save of " + titleId + " changed outside melonDS (" + how
                         + "), so the working image no longer describes it");
                DropWorkIfItHolds(layout, titleId, "because its save changed elsewhere");
                return true;
            }
            catch (Exception ex) { Log.Warn("could not check the working image's receipt", ex); return false; }
        }

        /// <summary>Drop the working image when it is holding this title, and forget its marker.
        ///
        /// APPLYING A STATE ONTO A PLAYED IMAGE IS NOT A RESTORE. MelonDsDelta.Apply only touches the
        /// files the state names - it puts them back, it does not take away what the current session
        /// added. Restore a backup that has A and B onto an image that has A, B and C and C stays,
        /// the image is reused on the next launch as if it were sound, and the capture after that
        /// writes C back into the state folder: the restored save silently grows back the file it
        /// was restored to be rid of.
        ///
        /// A state is defined against a FRESH INSTALL and nothing else, so that is the only surface
        /// it may be applied to. Dropping the image costs a rebuild on the next launch - a quarter of
        /// a second - and makes the restore mean what it says.</summary>
        private static void DropWorkIfItHolds(MelonDsLayout layout, string titleId, string why)
        {
            try
            {
                if (!string.Equals(WorkTitle(layout), titleId, StringComparison.OrdinalIgnoreCase))
                    return;

                Forget(layout);
                var work = WorkPath(layout);
                if (work != null && File.Exists(work)) File.Delete(work);
                Log.Info("the working image held " + titleId + "; dropped it " + why
                         + ", so the next launch builds a fresh one");
            }
            catch (Exception ex) { Log.Warn("could not drop the working image", ex); }
        }

        /// <summary>Throw a title's save away, so it starts again as if it had never been played.
        ///
        /// THREE THINGS HOLD IT, and deleting one of them is not a deletion. The state folder is the
        /// truth between sessions. The working image may still be holding the same title, with the
        /// save inside it - and RefreshSave would then extract it straight back the next time the
        /// host asks what saves exist, which would look exactly like a delete button that does
        /// nothing. And the legacy per-title image, on an installation without the native library,
        /// IS the save and nothing else holds it.
        ///
        /// SO THE WHOLE TITLE FOLDER GOES, not just the state inside it. The reference walk and the
        /// cached .tmd are not saves and keeping them would cost nothing - but a folder named after
        /// the game, still sitting there after somebody deleted that game's save, reads as a delete
        /// that did not work. Both are recovered on the next launch: the walk from the install
        /// itself, the metadata from beside the ROM or the carried index, which is in this assembly
        /// and needs no network.
        ///
        /// AND THE WORKING IMAGE GOES WITH IT when it was this title's. Once its marker is forgotten
        /// nothing will ever read those 240 MB again - the next launch rebuilds over them - so what
        /// is left is a quarter of a gigabyte that still physically holds the save just deleted.
        ///
        /// REFUSED WHILE melonDS IS RUNNING. The emulator has the image open and will write it back
        /// on the way out, so anything deleted now would return within the minute.</summary>
        public static bool DropState(MelonDsLayout layout, string titleId, out string error)
        {
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(titleId)) { error = "no title id"; return false; }
                if (MelonDsNand.EmulatorRunning())
                {
                    error = "melonDS is running, and it will write its session back when it closes. "
                          + "Close it and delete this again.";
                    return false;
                }

                var titleDir = TitleDir(layout, titleId);
                if (titleDir == null) { error = "this title has no folder"; return false; }

                // Said before it is gone: on an installation without the native library this image
                // is the only place the title exists, so a hand import through Manage DSi titles
                // goes with it and has to be done again.
                if (LegacyNandFor(layout, titleId) != null)
                    Log.Info("the per-title NAND for " + titleId + " goes too. If this title was "
                             + "imported by hand through Manage DSi titles, that import is gone with "
                             + "it and has to be done again.");

                int removed = 0;
                if (Directory.Exists(titleDir))
                {
                    Directory.Delete(titleDir, recursive: true);
                    removed++;
                }

                // THE IMAGE IS LEFT WHERE IT IS, and this used to throw it away. Two independent
                // things now stop it putting the save back, and neither of them is this method's to
                // remember: the reference walk went with the folder, and nothing can be captured
                // without one; and the receipt no longer matches a state folder that is not there,
                // so the launch that follows drops the image before deciding anything. See
                // MelonDsWorkSum. An image nothing can read is scratch, and the next launch of any
                // DSiWare rebuilds over it.
                Log.Info("deleted the DSiWare save for " + titleId
                         + (removed == 0 ? " - there was nothing left to delete" : ""));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                Log.Warn("could not delete the DSiWare save for " + titleId, ex);
                return false;
            }
        }

        /// <summary>Put an edited save back. THE STATE IS THE TRUTH between sessions, so that is
        /// what is written - and the working image is DROPPED rather than written, because applying a
        /// state onto an image that has been played is not a restore. See DropWorkIfItHolds.
        ///
        /// <paramref name="bios7Path"/> is no longer needed and is kept: nothing here opens a NAND
        /// any more, and changing the signature would only move the question to every caller.</summary>
        public static bool RestoreSave(MelonDsLayout layout, string titleId, string bios7Path,
                                       string sourceDir, out string error)
        {
            error = null;
            try
            {
                var stateDir = StateDirFor(layout, titleId);
                if (stateDir == null) { error = "this title has no state folder"; return false; }
                if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
                { error = "a DSiWare save is a folder, and this is not one"; return false; }
                if (!File.Exists(Path.Combine(sourceDir, MelonDsDelta.IndexName)))
                { error = "this folder is not a melonDS state - it has no " + MelonDsDelta.IndexName; return false; }

                // REPLACED, NOT MERGED. A state describes one moment: a file that stopped differing
                // has to stop being restored, and merging would keep it forever.
                var building = stateDir + "." + Guid.NewGuid().ToString("N") + ".part";
                Directory.CreateDirectory(building);
                try
                {
                    foreach (var file in Directory.GetFiles(sourceDir))
                        File.Copy(file, Path.Combine(building, Path.GetFileName(file)), overwrite: true);

                    var old = stateDir + "." + Guid.NewGuid().ToString("N") + ".old";
                    if (Directory.Exists(stateDir)) Directory.Move(stateDir, old);
                    Directory.Move(building, stateDir);
                    try { if (Directory.Exists(old)) Directory.Delete(old, recursive: true); } catch { }
                }
                finally { try { if (Directory.Exists(building)) Directory.Delete(building, true); } catch { } }

                // AND THE WORKING IMAGE GOES, when it is this title's.
                //
                // KEPT DELIBERATELY, although the receipt would normally catch this too. The receipt
                // exists to notice writers who do not know about us; a restore is OUR OWN doing, and
                // laundering something we know first-hand through a heuristic is a worse answer than
                // acting on it. It also covers the one case the receipt cannot: an installation
                // upgraded from before receipts existed has none yet, so "has the save moved" has no
                // answer until the next capture writes one - and a restore performed inside that
                // window would otherwise be captured over and lost.
                //
                // Unlike a delete, nothing else would stop it: the reference walk survives a
                // restore, so the capture that precedes a rebuild would run and put the played
                // session back over what was just restored.
                DropWorkIfItHolds(layout, titleId, "rather than restore onto a played image");
                return true;
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return false; }
        }

        // ── the base ─────────────────────────────────────────────────────────

        /// <summary>The dump the FIRST design copied once and used for every title. Nothing
        /// captures one any more - the NAND to build on is now chosen per game, by region, out of
        /// the user's bios folder - but one found on disk is still accepted as a last resort so an
        /// install that worked yesterday does not stop working today. See MelonDsBios.</summary>
        public static string LegacyBase(MelonDsLayout layout)
        {
            var path = BasePath(layout);
            return path != null && File.Exists(path) ? path : null;
        }

        /// <summary>Copy through a temporary name in the destination folder, then move. A 240 MB copy
        /// interrupted halfway must not leave something that looks like a NAND.</summary>
        private static void CopyIntoPlace(string source, string target)
        {
            var tmp = target + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                File.Copy(source, tmp, overwrite: true);
                if (File.Exists(target)) File.Delete(target);
                File.Move(tmp, target);
            }
            catch
            {
                Cleanup(tmp);
                throw;
            }
        }

        private static long FreeSpaceOn(string path)
        {
            try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))).AvailableFreeSpace; }
            catch { return -1; }
        }

        private static string Megabytes(long bytes)
            => (bytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture) + " MB";
    }
}
