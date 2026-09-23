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

        /// <summary>The copy of a title's own save that the host lists and restores.
        ///
        /// THE IMAGE IS NOT THE SAVE UNIT. A DSiWare save is a few kilobytes of public.sav inside a
        /// 240 MB image; handing the image to the host would mean hashing 240 MB to draw a freshness
        /// dot and a 240 MB vault copy per backup. So the save is written out beside the title's own
        /// folder, and THAT is what the host sees - a plain file, like every other save here.</summary>
        public static string SavePathFor(MelonDsLayout layout, string titleId)
        {
            var dir = TitleDir(layout, titleId);
            return dir == null ? null : Path.Combine(dir, SaveName);
        }

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

                // The host-facing copy comes out of the same session, so what the saves page shows
                // and what the state holds can never disagree.
                MirrorSave(session, layout, titleId);
                Log.Verbose("captured " + kept + " file(s) of state for " + titleId);
            }
            catch (Exception ex) { Log.Warn("could not capture the working NAND", ex); }
        }

        private static void MirrorSave(NandSession session, MelonDsLayout layout, string titleId)
        {
            var inNand = SaveInNand(titleId);
            var mirror = SavePathFor(layout, titleId);
            if (inNand == null || mirror == null) return;

            var tmp = mirror + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                if (!session.ExportFile(inNand, tmp, out _)) { Cleanup(tmp); return; }
                if (File.Exists(mirror)) File.Delete(mirror);
                File.Move(tmp, mirror);
            }
            catch { Cleanup(tmp); }
        }

        private static void Cleanup(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void Forget(MelonDsLayout layout)
        {
            try { var m = MarkerPath(layout); if (m != null && File.Exists(m)) File.Delete(m); } catch { }
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
                    MirrorSave(session, layout, titleId);
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
                var mirror = SavePathFor(layout, titleId);
                if (mirror == null) return null;

                var work = WorkPath(layout);
                if (string.Equals(WorkTitle(layout), titleId, StringComparison.OrdinalIgnoreCase)
                    && work != null && File.Exists(work)
                    && (!File.Exists(mirror)
                        || File.GetLastWriteTimeUtc(mirror) < File.GetLastWriteTimeUtc(work)))
                    CaptureWork(layout, bios7Path, onlyTitle: titleId);

                return File.Exists(mirror) ? mirror : null;
            }
            catch (Exception ex) { Log.Warn("could not extract a DSiWare save", ex); return null; }
        }

        /// <summary>Put an edited save back. THE STATE IS THE TRUTH between sessions, so that is what
        /// is written; work.bin is written too when it happens to be holding this title, so a launch
        /// that follows immediately sees the same thing.</summary>
        public static bool RestoreSave(MelonDsLayout layout, string titleId, string bios7Path,
                                       string sourceFile, out string error)
        {
            error = null;
            try
            {
                var inNand = SaveInNand(titleId);
                var stateDir = StateDirFor(layout, titleId);
                if (inNand == null || stateDir == null) { error = "this title has no state folder"; return false; }

                if (!MelonDsDelta.Put(stateDir, inNand, sourceFile, out error)) return false;

                var work = WorkPath(layout);
                if (string.Equals(WorkTitle(layout), titleId, StringComparison.OrdinalIgnoreCase)
                    && work != null && File.Exists(work)
                    && !string.IsNullOrWhiteSpace(bios7Path) && File.Exists(bios7Path))
                {
                    using var session = MelonDsNand.Open(work, bios7Path, out var why);
                    if (session != null) session.ImportFile(inNand, sourceFile, out _);
                    else Log.Verbose("the working NAND was not updated - " + why);
                }

                var mirror = SavePathFor(layout, titleId);
                if (mirror != null) File.Copy(sourceFile, mirror, overwrite: true);
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
