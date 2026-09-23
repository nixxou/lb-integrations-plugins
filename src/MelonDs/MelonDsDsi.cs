// One NAND per DSiWare title, under <install>\dsi\.
//
// WHY A NAND PER GAME RATHER THAN ONE SHARED. A DSiWare title does not run from its .nds the way a
// cartridge does: melonDS boots it out of the NAND, and its saves live inside the NAND too, at
// title/<category>/<id>/data/public.sav (DSi_NAND.cpp:1077-1092). So the NAND is not a side file - it
// IS the container of the game and of its progress. Giving each title its own means a save is one
// self-contained artefact that can be copied, backed up and restored without touching another game,
// and it removes the whole question of installing and uninstalling titles in a shared image.
//
//     <install>\dsi\base.bin                      the user's own dump, copied here once
//     <install>\dsi\0003000412345678\nand.bin      one per title id
//
// THE BASE IS THE USER'S OWN DUMP AND NOTHING ELSE. A DSi NAND cannot be fabricated: NANDImage opens
// an existing file and reads the ConsoleID out of it (DSi_NAND.h:52-64, :77), and the decryption
// depends on console-unique data plus the ES key held in dsi_bios7.bin at 0x8308. What this file
// does is copy, never create.
//
// Capturing the base is done ONCE, before anything is overwritten: the moment a DSiWare launch is
// about to repoint DSi.NANDPath, whatever the user had configured would otherwise be lost as a
// source. So the first pass copies it to dsi\base.bin and works from that copy afterwards.
//
// WHAT IS STILL MANUAL, said plainly rather than hidden. This file prepares the vessel and points
// melonDS at it; it does NOT install the title into it. That operation - NANDMount::ImportTitle -
// lives in melonDS's core library and has no entry point outside its Manage DSi titles dialog. So
// the first time a DSiWare game is launched the plugin creates its NAND and says, in the log, that
// the title has to be imported by hand once. Every launch after that is automatic. Automating the
// import needs either a small tool linked against melonDS's core, or a port of the NAND crypto and
// FAT writing to C#; see the README.
//
// SIZE. A DSi NAND dump is around 240 MB, so a per-title copy costs that much. Free space is checked
// before copying, because filling a disk silently is the one failure mode that would be this
// plugin's fault rather than the user's.

using System;
using System.IO;

namespace LbIntegrations.MelonDs
{
    /// <summary>What happened when a DSiWare title asked for its NAND.</summary>
    internal sealed class DsiNand
    {
        /// <summary>The per-title NAND melonDS should be pointed at, or null when there is none and
        /// none could be made.</summary>
        public string Path;

        /// <summary>True when this call created it, which is when the title still has to be imported
        /// by hand.</summary>
        public bool JustCreated;

        /// <summary>Why there is no NAND, for the log. Null when there is one.</summary>
        public string Reason;
    }

    internal static class MelonDsDsi
    {
        public const string DirName = "dsi";
        public const string BaseName = "base.bin";
        public const string NandName = "nand.bin";

        /// <summary>Beside the log, the way the other switches in this repository work. Creating it
        /// stops the plugin copying NANDs at all, and DSiWare goes back to being merely reported.</summary>
        private const string KillSwitch = "no-dsi-nand";

        /// <summary>A NAND dump is about 240 MB; refuse to start a copy without room for it plus a
        /// margin, rather than filling the disk and failing halfway.</summary>
        private const long FreeSpaceMargin = 256L * 1024 * 1024;

        public static string DsiDir(MelonDsLayout layout)
            => layout?.InstallDir == null ? null : System.IO.Path.Combine(layout.InstallDir, DirName);

        public static string BasePath(MelonDsLayout layout)
        {
            var dir = DsiDir(layout);
            return dir == null ? null : System.IO.Path.Combine(dir, BaseName);
        }

        /// <summary>The extracted copy of a title's save, beside its NAND.
        ///
        /// THE NAND IS NOT THE SAVE UNIT, and this is the whole reason this file exists. A DSiWare
        /// save is a few kilobytes of public.sav living inside a 240 MB image; presenting the image
        /// to the host would mean hashing 240 MB to draw a freshness dot, and a vault copy of 240 MB
        /// for every backup. So the save is exported next to the NAND and THAT is what the host
        /// sees - a plain file, like every other save this repository handles.</summary>
        public static string SavePathFor(MelonDsLayout layout, string titleId)
        {
            var nand = NandPathFor(layout, titleId);
            return nand == null ? null : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(nand), SaveName);
        }

        public const string SaveName = "public.sav";

        public static string NandPathFor(MelonDsLayout layout, string titleId)
        {
            var dir = DsiDir(layout);
            return dir == null || string.IsNullOrWhiteSpace(titleId)
                ? null
                : System.IO.Path.Combine(dir, titleId, NandName);
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

                var note = System.IO.Path.Combine(dir, "PUT-YOUR-NAND-HERE.txt");
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
                    "Once they are in place, launching a DSiWare game makes a copy of this NAND for",
                    "that title, in a folder named after its title id, installs the title into it,",
                    "and points melonDS at it. Each title then owns its NAND and its save.",
                    "",
                    "A DSiWare .nds also needs its .tmd file beside it to be installed - that is the",
                    "metadata file, and melonDS's own Manage DSi titles dialog asks for it too.",
                    "",
                    "This file is only a note. You can delete it.",
                }) + "\r\n");
                Log.Info("made " + dir + " - drop a DSi NAND dump in it to enable DSiWare");
            }
            catch (Exception ex) { Log.Verbose("could not prepare the dsi folder - " + ex.Message); }
        }

        /// <summary>Is this path one of the per-title NANDs we made? True only for something under
        /// dsi\, which is the folder this plugin owns.</summary>
        public static bool IsOurs(MelonDsLayout layout, string path)
        {
            try
            {
                var dir = DsiDir(layout);
                return !string.IsNullOrWhiteSpace(dir) && !string.IsNullOrWhiteSpace(path)
                       && System.IO.Path.GetFullPath(path)
                              .StartsWith(System.IO.Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>The NAND this title should run from, making it if it is not there yet.
        ///
        /// Never throws, and never a half-written file: the copy goes to a temporary name in the same
        /// folder and is moved into place once it is complete.</summary>
        public static DsiNand Ensure(MelonDsLayout layout, NdsRom rom, string configuredNand)
        {
            var result = new DsiNand();
            try
            {
                if (Log.Disabled(KillSwitch))
                {
                    result.Reason = "switched off by the " + KillSwitch + " marker";
                    return result;
                }
                var target = NandPathFor(layout, rom?.TitleId);
                if (target == null) { result.Reason = "no install directory to put it in"; return result; }

                if (File.Exists(target)) { result.Path = target; return result; }

                var source = CaptureBase(layout, configuredNand);
                if (source == null)
                {
                    // Make the folder NOW, not at install time. The message below names a path, and a
                    // path that does not exist is a spelling to guess at - which is exactly what
                    // happened: the folder was only created when a NAND was first copied, so the one
                    // user who most needed it never saw it.
                    PrepareFolder(layout);
                    result.Reason = "no DSi NAND to copy from. Put your own dump at "
                                  + BasePath(layout) + ", or set DSi.NANDPath in melonDS once";
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

                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                CopyIntoPlace(source, target);

                result.Path = target;
                result.JustCreated = true;
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

        /// <summary>Bring the extracted save up to date with what is inside the NAND, and answer with
        /// its path - or null when there is nothing to show.
        ///
        /// THE NAND'S OWN TIMESTAMP IS THE TRIGGER. Opening an image means decrypting it and mounting
        /// a filesystem, which is far too much to do on every page render; so it happens only when
        /// melonDS has written to the NAND since the last extraction. In the steady state this is two
        /// calls to File.GetLastWriteTimeUtc and nothing else.</summary>
        public static string RefreshSave(MelonDsLayout layout, string titleId, string bios7Path)
        {
            try
            {
                var nand = NandPathFor(layout, titleId);
                var mirror = SavePathFor(layout, titleId);
                if (nand == null || mirror == null || !File.Exists(nand)) return null;

                if (File.Exists(mirror)
                    && File.GetLastWriteTimeUtc(mirror) >= File.GetLastWriteTimeUtc(nand))
                    return mirror;                       // nothing has happened since

                if (string.IsNullOrWhiteSpace(bios7Path) || !File.Exists(bios7Path))
                    return File.Exists(mirror) ? mirror : null;   // stale beats absent

                using var session = MelonDsNand.Open(nand, bios7Path, out var error);
                if (session == null)
                {
                    Log.Verbose("could not open " + nand + " to extract its save - " + error);
                    return File.Exists(mirror) ? mirror : null;
                }

                // Through a temporary name: a half-written mirror that looked newer than the NAND
                // would never be refreshed again.
                var tmp = mirror + "." + Guid.NewGuid().ToString("N") + ".part";
                if (!session.ExportSave(titleId, NandSaveKind.Public, tmp, out var why))
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    if (why != null) Log.Verbose("no save extracted for " + titleId + " - " + why);
                    return File.Exists(mirror) ? mirror : null;
                }

                if (File.Exists(mirror)) File.Delete(mirror);
                File.Move(tmp, mirror);
                Log.Verbose("extracted the save of " + titleId + " from its NAND");
                return mirror;
            }
            catch (Exception ex) { Log.Warn("could not extract a DSiWare save", ex); return null; }
        }

        /// <summary>Put an edited save back inside the NAND, which is the only copy melonDS reads.
        /// The mirror is refreshed from the NAND afterwards rather than trusted, so what the host
        /// shows is what the emulator will actually load.</summary>
        public static bool RestoreSave(MelonDsLayout layout, string titleId, string bios7Path,
                                       string sourceFile, out string error)
        {
            error = null;
            try
            {
                var nand = NandPathFor(layout, titleId);
                if (nand == null || !File.Exists(nand)) { error = "this title has no NAND"; return false; }
                if (string.IsNullOrWhiteSpace(bios7Path) || !File.Exists(bios7Path))
                { error = "DSi.BIOS7Path is not set, so the NAND cannot be opened"; return false; }

                using (var session = MelonDsNand.Open(nand, bios7Path, out error))
                {
                    if (session == null) return false;
                    if (!session.ImportSave(titleId, NandSaveKind.Public, sourceFile, out error))
                        return false;
                }

                // The mirror is now older than the NAND, so the next listing re-extracts it.
                try { File.SetLastWriteTimeUtc(nand, DateTime.UtcNow); } catch { }
                return true;
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return false; }
        }

        /// <summary>The dump every per-title NAND is copied from, captured once.
        ///
        /// Order matters: dsi\base.bin wins if it is there, because after the first DSiWare launch
        /// DSi.NANDPath points at a per-title copy and using THAT as a source would spread one game's
        /// state to the next. A configured path inside dsi\ is therefore refused as a base.</summary>
        private static string CaptureBase(MelonDsLayout layout, string configuredNand)
        {
            var basePath = BasePath(layout);
            if (basePath == null) return null;
            if (File.Exists(basePath)) return basePath;

            if (string.IsNullOrWhiteSpace(configuredNand) || !File.Exists(configuredNand)) return null;

            var dsiDir = DsiDir(layout);
            try
            {
                if (System.IO.Path.GetFullPath(configuredNand)
                        .StartsWith(System.IO.Path.GetFullPath(dsiDir), StringComparison.OrdinalIgnoreCase))
                    return null;                 // already one of ours, not a base
            }
            catch { }

            Directory.CreateDirectory(dsiDir);
            CopyIntoPlace(configuredNand, basePath);
            Log.Info("kept your NAND as the base for every DSiWare title: " + basePath);
            return basePath;
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
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
        }

        private static long FreeSpaceOn(string path)
        {
            try { return new DriveInfo(System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(path))).AvailableFreeSpace; }
            catch { return -1; }
        }

        private static string Megabytes(long bytes)
            => (bytes / (1024 * 1024)).ToString(System.Globalization.CultureInfo.InvariantCulture) + " MB";
    }
}
