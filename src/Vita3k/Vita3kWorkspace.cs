// A Vita that is thrown away after every session.
//
// NOTHING IS EVER INSTALLED FOR GOOD. Each launch builds a virtual Vita from a pristine firmware,
// installs the game onto it, plays, and keeps only what changed. The rest goes. That is the DSiWare
// model of melonDS, and it is here for the same reason: a console that accumulates is a console
// nobody can put back the way it was.
//
//     <install>\portable\nand-initiale\      the firmware, as it came out of Sony's packages
//     <install>\portable\nand-initiale.manifest   its walk, taken once
//     <install>\portable\work\               the working tree when there is no RAM disk
//     <install>\portable\fs                  a JUNCTION to whichever of the two is in play
//     <install>\portable\work.title          which game the working tree currently holds
//     <install>\portable\work.reference      the walk of that tree BEFORE the session
//     <install>\portable\work.pending        a session that could not be taken out in time
//     <install>\portable\saves\<TITLE_ID>\state.vitasav
//
// THE JUNCTION IS HOW VITA3K IS POINTED AT IT. Measured: `portable\` beside the executable overrides
// everything else, including the pref-path setting (config.cpp:456-458), so the only way to move the
// filesystem is to make `portable\fs` be somewhere else. A junction and not a symbolic link, because
// a junction needs no administrator and no developer mode.
//
// THE ORDER OF A LAUNCH IS THE POINT, and it is melonDS's order:
//
//     copy the base -> install the game -> TAKE THE REFERENCE -> restore the save -> write work.title
//
// A reference taken after the save went back in would describe the save as part of the install, and
// the next capture would find nothing at all. work.title is written LAST because a marker naming a
// game whose save was never put back would make the next capture overwrite a good save with a blank
// one.
//
// AND THE CAPTURE IS NO LONGER OPTIONAL. For the DSi it could be lazy: the image sat on disk and
// whatever was in it could be read later. A RAM disk survives nothing - not a reboot, not a power
// cut - so the session comes out before the drive goes, or it is gone.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Threading;
using LbIntegrations.RamDisk;
using LbIntegrations.Snapshot;

namespace LbIntegrations.Vita3k
{
    internal static class Vita3kWorkspace
    {
        public const string BaseName = "nand-initiale";
        public const string BaseManifest = "nand-initiale.manifest";
        public const string WorkName = "work";
        public const string FsName = "fs";
        public const string TitleMarker = "work.title";
        public const string ReferenceName = "work.reference";
        public const string PendingName = "work.pending";
        public const string SavesName = "saves";
        public const string SaveFile = "state.vitasav";

        /// <summary>Headroom on top of the firmware and the game. A session writes saves, shader
        /// caches and logs, and a disk that fills up mid-game is worse than one that was refused.</summary>
        private const int MarginMb = 512;

        /// <summary>What the capture waits for, in the DSi's terms and for the DSi's reasons: a floor
        /// always paid, then the emulator gone and the tree still, then a ceiling because this runs
        /// while somebody is watching a frontend come back.</summary>
        private static readonly TimeSpan SettleFloor = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan SettleBudget = TimeSpan.FromSeconds(5);

        // ── paths ────────────────────────────────────────────────────────────

        private static string Portable(Vita3kLayout layout)
            => layout?.InstallDir == null ? null : Vita3kPaths.PortableDirOf(layout.InstallDir);

        public static string BaseDir(Vita3kLayout l) => Under(l, BaseName);
        public static string BaseManifestPath(Vita3kLayout l) => Under(l, BaseManifest);
        public static string WorkDir(Vita3kLayout l) => Under(l, WorkName);
        public static string FsLink(Vita3kLayout l) => Under(l, FsName);
        public static string MarkerPath(Vita3kLayout l) => Under(l, TitleMarker);
        public static string ReferencePath(Vita3kLayout l) => Under(l, ReferenceName);
        public static string PendingPath(Vita3kLayout l) => Under(l, PendingName);

        public static string SavePathFor(Vita3kLayout l, string titleId)
        {
            var dir = Under(l, SavesName);
            return dir == null || string.IsNullOrWhiteSpace(titleId)
                ? null : Path.Combine(dir, titleId, SaveFile);
        }

        private static string Under(Vita3kLayout layout, string name)
        {
            var portable = Portable(layout);
            return portable == null ? null : Path.Combine(portable, name);
        }

        // ── the pristine firmware, put aside once ────────────────────────────

        /// <summary>Turn a freshly installed firmware into the base every session is built from, and
        /// walk it. Called once, right after the three packages go in.
        ///
        /// A base that is already there is left alone: it is the thing every save was taken against,
        /// and rebuilding it would make every existing save describe a tree that no longer
        /// exists.</summary>
        public static bool AdoptFirmware(Vita3kLayout layout, out string error)
        {
            error = null;
            try
            {
                var baseDir = BaseDir(layout);
                var manifest = BaseManifestPath(layout);
                if (baseDir == null) { error = "the install layout is not known"; return false; }

                if (Directory.Exists(baseDir) && File.Exists(manifest))
                {
                    Log.Info("the pristine firmware is already put aside");
                    return true;
                }

                var fs = layout.VitaFs;
                if (fs == null || !Directory.Exists(fs))
                { error = "there is no firmware at " + fs; return false; }

                // The link, if there is one, has to go before anything moves: a junction pointing at
                // a drive that is gone is a path that throws rather than a path that is absent.
                DropLink(layout);

                if (!Directory.Exists(baseDir))
                {
                    Directory.Move(fs, baseDir);
                    Log.Info("put the firmware aside as " + BaseName);
                }

                int count = SnapWalk.Write(baseDir, manifest, out error);
                if (count < 0) return false;
                Log.Info("walked the pristine firmware: " + count + " entries");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                Log.Warn("could not put the firmware aside", ex);
                return false;
            }
        }

        /// <summary>Put the firmware aside if it is complete and has not been put aside yet.
        ///
        /// SEPARATE FROM AdoptFirmware BECAUSE THE FIRMWARE IS NOT ALWAYS OURS TO HAVE JUST
        /// INSTALLED. Measured on a real install: the three packages went in across two runs - one
        /// crashed and was redone later - so the install step never saw them all succeed at once and
        /// never adopted anything. Somebody can also install a firmware from Vita3K itself. Asking
        /// "is there a complete firmware sitting there" is the honest question, and it costs three
        /// Directory.Exists.</summary>
        public static bool EnsureBase(Vita3kLayout layout, out string error)
        {
            error = null;
            if (HasBase(layout)) return true;

            try
            {
                var fs = layout?.VitaFs;
                if (fs == null || !Directory.Exists(fs)) { error = "there is no filesystem yet"; return false; }

                foreach (var part in new[] { "vs0", "sa0", "pd0" })
                {
                    var dir = Path.Combine(fs, part);
                    if (!Directory.Exists(dir) || !Directory.EnumerateFileSystemEntries(dir).Any())
                    { error = part + " is missing - the firmware is not complete"; return false; }
                }

                Log.Info("a complete firmware is sitting in fs and has never been put aside - doing it now");
                QuietTheFirstRun(layout);
                return AdoptFirmware(layout, out error);
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                Log.Warn("could not check the firmware", ex);
                return false;
            }
        }

        /// <summary>Put down the one setting that stops a launch dead.
        ///
        /// MEASURED: with show-welcome left true, Vita3K opens on its "Welcome to Vita3K" dialog and
        /// waits for a click - before booting anything. From a front end that is simply a game that
        /// never starts. The flag lives in portable\config.yml, which is ours, and the emulator
        /// rewrites the file on exit keeping whatever we set.
        ///
        /// Only that one line is touched, and only when it says true: the rest of the file is the
        /// user's, including anything they changed in the emulator's own settings.</summary>
        public static void QuietTheFirstRun(Vita3kLayout layout)
        {
            try
            {
                var portable = Vita3kPaths.PortableDirOf(layout?.InstallDir);
                if (string.IsNullOrEmpty(portable) || !Directory.Exists(portable)) return;
                var config = Path.Combine(portable, "config.yml");

                // NO FILE YET IS THE USUAL CASE NOW, and it is the case that matters: the firmware is
                // installed by our own library, so the emulator has never run when the first game
                // starts, and it writes a fresh config.yml - with the welcome dialog on - on that very
                // run. A config.yml holding this one line is enough: Vita3K reads every key it finds
                // and takes its default for the rest (update_members, config.cpp), then writes the
                // whole file back on exit, keeping ours.
                if (!File.Exists(config))
                {
                    File.WriteAllText(config, "show-welcome: false\n");
                    Log.Info("wrote a config.yml with the welcome dialog off - the emulator has not run yet");
                    return;
                }

                var text = File.ReadAllText(config);
                if (text.IndexOf("show-welcome: true", StringComparison.Ordinal) < 0) return;

                File.WriteAllText(config, text.Replace("show-welcome: true", "show-welcome: false"));
                Log.Info("turned off the welcome dialog - it waits for a click before booting anything");
            }
            catch (Exception ex) { Log.Warn("could not quiet the welcome dialog", ex); }
        }

        public static bool HasBase(Vita3kLayout layout)
        {
            try
            {
                var dir = BaseDir(layout);
                return dir != null && Directory.Exists(dir) && File.Exists(BaseManifestPath(layout));
            }
            catch { return false; }
        }

        /// <summary>Total size of the pristine firmware, in MB, read from its manifest rather than by
        /// walking it again.</summary>
        public static int BaseSizeMb(Vita3kLayout layout)
        {
            try
            {
                long total = 0;
                foreach (var pair in SnapWalk.Read(BaseManifestPath(layout)))
                {
                    if (pair.Value.IsDirectory) continue;
                    if (long.TryParse(pair.Value.Size, out var size)) total += size;
                }
                return (int)(total / (1024 * 1024));
            }
            catch (Exception ex) { Log.Warn("could not size the pristine firmware", ex); return 0; }
        }

        // ── the marker ───────────────────────────────────────────────────────

        /// <summary>Five tab-separated fields: title id, the game's path, its length, its write time
        /// and where the tree lives. The fingerprint is not a hash - this runs at every launch and a
        /// .vpk is several hundred megabytes.</summary>
        private static string[] MarkerParts(Vita3kLayout layout)
        {
            try
            {
                var path = MarkerPath(layout);
                if (path == null || !File.Exists(path)) return null;
                var parts = File.ReadAllText(path).Split('\t');
                return parts.Length >= 5 ? parts : null;
            }
            catch { return null; }
        }

        public static string WorkTitle(Vita3kLayout layout) => MarkerParts(layout)?[0];

        /// <summary>Where the working tree currently is - a drive root, or the work folder. Null when
        /// nothing is set up.</summary>
        public static string WorkRoot(Vita3kLayout layout) => MarkerParts(layout)?[4];

        private static string Fingerprint(string romPath)
        {
            try
            {
                var info = new FileInfo(romPath);
                return info.FullName + "\t" + info.Length + "\t" + info.LastWriteTimeUtc.Ticks;
            }
            catch { return romPath + "\t0\t0"; }
        }

        private static void Remember(Vita3kLayout layout, string titleId, string romPath, string root)
        {
            try
            {
                File.WriteAllText(MarkerPath(layout), titleId + "\t" + Fingerprint(romPath) + "\t" + root);
            }
            catch (Exception ex) { Log.Warn("could not write " + TitleMarker, ex); }
        }

        private static void Forget(Vita3kLayout layout)
        {
            try { var p = MarkerPath(layout); if (p != null && File.Exists(p)) File.Delete(p); }
            catch (Exception ex) { Log.Warn("could not clear " + TitleMarker, ex); }
        }

        /// <summary>Is the working tree already this exact game, still there, and still walked?
        ///
        /// When it is, a launch does nothing at all: no mount, no copy, no reinstall. That is what
        /// makes a second run of the same game cheap, and it is also the rule you asked for - the
        /// working tree is NOT cleared when a game ends, only when a DIFFERENT one starts.</summary>
        public static bool CanReuse(Vita3kLayout layout, string titleId, string romPath)
        {
            try
            {
                var parts = MarkerParts(layout);
                if (parts == null) return false;
                if (!string.Equals(parts[0], titleId, StringComparison.Ordinal)) return false;
                if (!string.Equals(parts[1] + "\t" + parts[2] + "\t" + parts[3], Fingerprint(romPath),
                                   StringComparison.Ordinal)) return false;
                if (!File.Exists(ReferencePath(layout))) return false;

                var root = parts[4];
                return !string.IsNullOrEmpty(root) && Directory.Exists(root);
            }
            catch { return false; }
        }

        // ── the junction ─────────────────────────────────────────────────────

        /// <summary>Point portable\fs at a folder - never at a volume root, which mklink /J
        /// refuses. See OpenWorkingTree.
        ///
        /// mklink /J and not Directory.CreateSymbolicLink: a junction needs no administrator and no
        /// developer mode, a symbolic link needs one or the other. This runs on somebody's ordinary
        /// account, at every launch.</summary>
        private static bool Link(Vita3kLayout layout, string target, out string error)
        {
            error = null;
            try
            {
                var link = FsLink(layout);
                DropLink(layout);

                var psi = new ProcessStartInfo("cmd.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                psi.ArgumentList.Add("/c");
                psi.ArgumentList.Add("mklink");
                psi.ArgumentList.Add("/J");
                psi.ArgumentList.Add(link);
                psi.ArgumentList.Add(target.TrimEnd('\\', '/'));

                using var p = Process.Start(psi);
                var said = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(30000);

                if (Directory.Exists(link)) { Log.Info("portable\\fs -> " + target); return true; }
                error = "could not link portable\\fs to " + target + ": " + said.Trim();
                Log.Warn(error);
                return false;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                Log.Warn("could not create the junction", ex);
                return false;
            }
        }

        /// <summary>Remove the junction, never what it points at. Directory.Delete on a junction
        /// removes the link itself, which is what we want - but only once we are sure it IS a
        /// link.</summary>
        private static void DropLink(Vita3kLayout layout)
        {
            try
            {
                var link = FsLink(layout);
                if (link == null) return;

                // THE LINK'S OWN ATTRIBUTES, NOT ITS TARGET'S. The question is "is a link sitting here",
                // and it has to be answered for an ORPHANED junction too: after a reboot the RAM disk is
                // gone but portable\fs still points at it, and the next mklink fails because the name
                // is taken. An Exists check asks about the target and can say no while the link is
                // there. GetAttributes reads the entry itself and never follows the reparse point.
                FileAttributes attributes;
                try { attributes = File.GetAttributes(link); }
                catch (FileNotFoundException) { return; }
                catch (DirectoryNotFoundException) { return; }

                if (!attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    // A REAL folder, not a link. That is somebody's filesystem, or a firmware that was
                    // never put aside. It is not ours to delete.
                    Log.Warn("portable\\fs is a real folder, not a junction - leaving it alone");
                    return;
                }

                // Removes the link, never what it points at - and needs no target to do it.
                Directory.Delete(link);
                Log.Info("removed the junction portable\\fs");
            }
            catch (Exception ex) { Log.Warn("could not remove the junction", ex); }
        }

        // ── setting a session up ─────────────────────────────────────────────

        /// <summary>Build the virtual Vita a session will be played on. Returns the title id, or null
        /// when the launch should be abandoned.</summary>
        public static string Prepare(Vita3kLayout layout, string romPath, out string error)
            => Prepare(layout, romPath, out error, null);

        /// <summary>The same preparation, saying what it is doing at each step - see
        /// Vita3kProgressWindow, which is what listens at launch.</summary>
        public static string Prepare(Vita3kLayout layout, string romPath, out string error,
                                     Action<string, double?> report)
        {
            // Waits for an end-of-session release still in progress rather than racing it.
            if (!Monitor.TryEnter(SessionGate))
            {
                report?.Invoke("Waiting for the previous session to be put away...", null);
                Monitor.Enter(SessionGate);
            }
            try { return PrepareLocked(layout, romPath, out error, report); }
            finally { Monitor.Exit(SessionGate); }
        }

        private static string PrepareLocked(Vita3kLayout layout, string romPath, out string error,
                                            Action<string, double?> report)
        {
            error = null;
            try
            {
                // Cheap, and it costs one read of a small file: somebody can turn the dialog back
                // on from the emulator's own settings between two launches.
                QuietTheFirstRun(layout);

                // Self-healing: a complete firmware that was never put aside becomes the base
                // here rather than requiring the install step to be run again.
                if (!EnsureBase(layout, out error))
                { error = "no pristine firmware to build from - " + error; return null; }
                if (!Vita3kContent.Installable(romPath))
                { error = "this is not something we install: " + romPath; return null; }

                var content = Vita3kContent.Describe(romPath, out error);
                if (content == null) return null;
                lock (Names) Names[content.TitleId] = content.Title;
                if (!content.IsGame)
                { error = content + " is not a game - updates and add-ons are not launched"; return null; }

                if (CanReuse(layout, content.TitleId, romPath))
                {
                    Log.Info("the working tree already holds " + content.TitleId + " - reusing it");
                    // The link is remade every time: a release interrupted after it dropped the
                    // junction and before it forgot the tree would otherwise leave a console the
                    // emulator cannot see.
                    if (!Link(layout, WorkRoot(layout), out error)) return null;
                    return content.TitleId;
                }

                // A DIFFERENT GAME. This is the only moment anything is cleared.
                report?.Invoke("Putting the previous game away...", null);
                Teardown(layout);

                int sizeMb = BaseSizeMb(layout)
                             + (int)(Math.Max(0, Vita3kContent.WorkingSizeBytes(romPath)) / (1024 * 1024))
                             + MarginMb;

                report?.Invoke("Preparing a fresh console...", null);
                var root = OpenWorkingTree(layout, content.TitleId, sizeMb);
                if (root == null) { error = "could not make a working tree"; return null; }

                // FROM HERE, A FAILURE OWES WHAT IT TOOK BACK. Measured: the junction was refused,
                // we reported the error honestly and left a 902 MB RAM disk mounted holding nothing.
                // The user saw one dialog and the memory stayed gone. Every exit below this line
                // goes through Release.
                bool ready = false;
                try
                {
                    if (!Link(layout, root, out error)) return null;

                    Log.Info("copying the pristine firmware into " + root);
                    report?.Invoke("Copying the console firmware...", 0);
                    CopyTree(BaseDir(layout), root, f => report?.Invoke(null, f));

                    var installed = Vita3kContent.Install(romPath, root, out error, report);
                    if (installed == null) return null;

                    // THE REFERENCE, between the install and everything else. See the header.
                    // From the base's own manifest, hashing only what the install wrote - see
                    // SnapWalk.WriteFrom, which falls back to a full walk the moment the tree is not
                    // exactly that.
                    report?.Invoke("Taking the console's fingerprint...", 0);
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    int walked = SnapWalk.WriteFrom(root, BaseManifestPath(layout), installed.Written,
                                                    ReferencePath(layout), out error, f => report?.Invoke(null, f),
                                                    out int hashed);
                    if (walked < 0) return null;
                    Log.Info("reference walk: " + walked + " entries in " + watch.ElapsedMilliseconds + " ms"
                             + (hashed >= 0 ? " - " + hashed + " file(s) hashed, the rest from the base"
                                            : " - full walk"));

                    RestoreSave(layout, content.TitleId, root);

                    Remember(layout, content.TitleId, romPath, root);
                    ready = true;
                    return content.TitleId;
                }
                finally
                {
                    if (!ready)
                    {
                        // Said, because it can take a while: unmounting waits for the helper's mount
                        // run to finish, measured at a minute and a half on a slow desktop. A window
                        // still reading "Decrypting..." for that long looks exactly like a hang.
                        report?.Invoke("That did not work - cleaning up...", null);
                        Release(layout, content.TitleId);
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                Log.Warn("could not prepare the session", ex);
                return null;
            }
        }

        /// <summary>A RAM disk when the machine can hold one, a folder otherwise.
        ///
        /// THE CHECK IS AGAINST FREE PHYSICAL MEMORY, and that matters. An ImDisk drive with no image
        /// behind it is a `vm` disk, backed by virtual memory - so one bigger than the RAM actually
        /// free does not fail, it PAGES to the system drive. We would be writing the SSD twice over
        /// while believing we were sparing it, and more slowly than a plain folder.</summary>
        private static string OpenWorkingTree(Vita3kLayout layout, string titleId, int sizeMb)
        {
            RamDiskHost.LaunchBoxRoot = () => Vita3kPaths.LaunchBoxRootOf(layout);

            int free = RamDrive.GetFreeRamMb();
            if (RamDrive.IsReady() && free > 0 && sizeMb < free)
            {
                var drive = RamDrive.MountFor(titleId, sizeMb);
                if (drive != null)
                {
                    // ONE FOLDER DOWN, NEVER THE DRIVE ROOT. A junction cannot point at a volume
                    // root: mklink /J answers "Local volumes are required to complete the operation"
                    // and creates nothing. Measured on a mounted Z:\ - the same call against Z:\fs
                    // succeeds. It is a Windows rule, not an ImDisk quirk, and it cost a launch:
                    // the drive mounted, the link was refused, and the host said only "Failed to
                    // prepare emulator to launch this game!".
                    //
                    // It also makes the two branches symmetric - the fallback was already a folder -
                    // and costs nothing, since the drive holds this session and nothing else.
                    var root = Path.Combine(drive, FsName);
                    Directory.CreateDirectory(root);
                    Log.Info("working on a RAM disk at " + root + " (" + sizeMb + " MB of " + free + " free)");
                    return root;
                }
                Log.Warn("the RAM disk did not mount - falling back to a folder");
            }
            else
            {
                Log.Info("working on disk: " + sizeMb + " MB needed, " + free + " MB of physical RAM free"
                         + (RamDrive.IsReady() ? "" : ", and no RAM disk is available"));
            }

            var work = WorkDir(layout);
            Directory.CreateDirectory(work);
            return work;
        }

        /// <summary>Give back everything a HALF-BUILT session took.
        ///
        /// Teardown cannot do this job: it reads the title marker to learn what to unmount, and a
        /// session that failed before Remember never wrote one. So this one is told the title.</summary>
        private static void Release(Vita3kLayout layout, string titleId)
        {
            try
            {
                DropLink(layout);
                if (titleId != null) RamDrive.UnmountFor(titleId);

                var work = WorkDir(layout);
                if (work != null && Directory.Exists(work))
                {
                    try { Directory.Delete(work, recursive: true); }
                    catch (Exception ex) { Log.Warn("could not clear " + work, ex); }
                }

                try { var r = ReferencePath(layout); if (r != null && File.Exists(r)) File.Delete(r); } catch { }
                Log.Info("the half-built session of " + titleId + " was given back");
            }
            catch (Exception ex) { Log.Warn("could not give back the half-built session", ex); }
        }

        /// <summary>Let go of the previous session's tree. Called when a DIFFERENT game starts, never
        /// when one ends.</summary>
        public static void Teardown(Vita3kLayout layout) => Teardown(layout, null);

        public static void Teardown(Vita3kLayout layout, Action<string, double?> report)
        {
            try
            {
                var previous = WorkTitle(layout);
                DropLink(layout);
                if (previous != null && OnRamDisk(layout))
                    report?.Invoke("Releasing the RAM disk - waiting for its helper to finish...", null);
                if (previous != null) RamDrive.UnmountFor(previous);
                report?.Invoke("Clearing the console...", null);

                var work = WorkDir(layout);
                if (work != null && Directory.Exists(work))
                {
                    try { Directory.Delete(work, recursive: true); }
                    catch (Exception ex) { Log.Warn("could not clear " + work, ex); }
                }

                try { var r = ReferencePath(layout); if (r != null && File.Exists(r)) File.Delete(r); } catch { }
                Forget(layout);
                if (previous != null) Log.Info("cleared the working tree that held " + previous);
            }
            catch (Exception ex) { Log.Warn("could not clear the working tree", ex); }
        }

        // ── the session comes out ────────────────────────────────────────────

        /// <summary>Take the session out of the working tree, and only then let the tree go.
        ///
        /// Waits in the DSi's three steps: a floor of one second always paid, then the emulator gone
        /// and the tree still, then a ceiling of five. Past the ceiling NOTHING IS READ - a marker
        /// says which game is still in there, and the lazy path finishes the job. Reading a tree
        /// somebody is still writing is what produced destructive saves next door.</summary>
        /// <summary>ONE SESSION OPERATION AT A TIME. Measured: the host's OnGameExited and our own
        /// watcher both fired at the end of the same session, 60 ms apart, and the second capture
        /// failed on the first one's half-written file. And the RAM disk is released at the end of a
        /// session, which waits for the helper - a relaunch during that wait must not reuse a console
        /// that is being taken away. Capture, release and Prepare all hold this.</summary>
        private static readonly object SessionGate = new object();

        /// <summary>A game's own name, by title id, as its param.sfo gave it when the console was built -
        /// for the window that says the session is being saved.</summary>
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal);

        private static string NameOf(string titleId)
        {
            lock (Names) return Names.TryGetValue(titleId ?? "", out var n) && !string.IsNullOrWhiteSpace(n) ? n : titleId;
        }

        /// <summary>The last session taken out: title and when. The host's OnGameExited and our own
        /// watcher both end the same session; the second one, arriving after the first, finds the job
        /// done and says nothing - no second window, no second capture.</summary>
        private static string _lastCaptured;
        private static DateTime _lastCapturedAt;

        public static bool CaptureOnExit(Vita3kLayout layout, string titleId)
        {
            lock (SessionGate) return CaptureOnExitLocked(layout, titleId);
        }

        private static bool CaptureOnExitLocked(Vita3kLayout layout, string titleId)
        {
            try
            {
                var root = WorkRoot(layout);
                if (root == null || !Directory.Exists(root)) return false;
                if (!string.Equals(WorkTitle(layout), titleId, StringComparison.Ordinal)) return false;

                // THE CLOSING WINDOW, as the launch has one. It stays invisible for a quick save -
                // see Vita3kProgressWindow - and shows for the RAM disk release, which waits for the
                // helper and has been measured at a minute and a half.
                using var window = Vita3kProgressWindow.Open("Vita3K - saving " + NameOf(titleId));
                Action<string, double?> report = (step, fraction) => window?.Report(step, fraction);

                // ALREADY CAPTURED - by the other end-of-session signal, or by the lazy path GetSaves
                // runs the moment the host lists saves after a game - and nothing written since. That
                // spares the CAPTURE, and nothing else. Measured: an earlier version returned here
                // outright, GetSaves had captured four seconds before the watcher arrived, and the RAM
                // disk was never released.
                bool taken = string.Equals(_lastCaptured, titleId, StringComparison.Ordinal)
                             && (DateTime.UtcNow - _lastCapturedAt).TotalSeconds < 60
                             && Newest(root) <= _lastCapturedAt;

                if (!taken)
                {
                    report("Waiting for Vita3K to finish writing...", null);
                    var deadline = DateTime.UtcNow + SettleBudget;
                    Thread.Sleep(SettleFloor);

                    while (true)
                    {
                        if (!Vita3kPaths.EmulatorRunning() && Quiet(root)) break;
                        if (DateTime.UtcNow >= deadline)
                        {
                            MarkPending(layout, titleId);
                            Log.Info("the working tree was still busy after " + (int)SettleBudget.TotalSeconds
                                     + "s - nothing read, " + PendingName + " says so");
                            return false;
                        }
                        Thread.Sleep(250);
                    }

                    taken = CaptureLocked(layout, titleId, report);
                }

                if (taken)
                {
                    ClearPending(layout);

                    // A RAM DISK IS GIVEN BACK ONCE THE SESSION IS SAFE. Kept mounted it holds its
                    // memory until another game or a reboot - past the host's own exit. So it goes,
                    // AFTER the capture and never before: what is in RAM survives nothing. The
                    // folder fallback stays, as asked: work\ is only cleared by another game.
                    if (OnRamDisk(layout))
                    {
                        Log.Info("releasing the RAM disk of " + titleId + " - the session is saved");
                        Teardown(layout, report);
                    }
                }
                return taken;
            }
            catch (Exception ex)
            {
                Log.Warn("capture on exit", ex);
                MarkPending(layout, titleId);
                return false;
            }
        }

        /// <summary>Is the current working tree on a RAM disk, rather than the work\ folder?</summary>
        private static bool OnRamDisk(Vita3kLayout layout)
        {
            var root = WorkRoot(layout);
            var work = WorkDir(layout);
            if (string.IsNullOrEmpty(root)) return false;
            return work == null || !string.Equals(Path.GetFullPath(root).TrimEnd('\\'),
                                                  Path.GetFullPath(work).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Compare the tree with its reference and pack the difference.</summary>
        public static bool Capture(Vita3kLayout layout, string titleId)
        {
            lock (SessionGate) return CaptureLocked(layout, titleId);
        }

        private static bool CaptureLocked(Vita3kLayout layout, string titleId) => CaptureLocked(layout, titleId, null);

        private static bool CaptureLocked(Vita3kLayout layout, string titleId, Action<string, double?> report)
        {
            string building = null;
            try
            {
                var root = WorkRoot(layout);
                var reference = ReferencePath(layout);
                var save = SavePathFor(layout, titleId);
                if (root == null || save == null || !File.Exists(reference)) return false;

                building = save + "." + Guid.NewGuid().ToString("N") + ".state";
                report?.Invoke("Looking for what the game changed...", 0);
                int files = SnapDelta.Capture(root, reference, building, out var error, f => report?.Invoke(null, f));
                if (files < 0) { Log.Warn("nothing captured: " + error); return false; }

                report?.Invoke("Packing the save...", null);
                if (!SnapFile.Pack(building, save, out error))
                { Log.Warn("could not pack the save: " + error); return false; }

                _lastCaptured = titleId;
                _lastCapturedAt = DateTime.UtcNow;

                Log.Info("the session of " + titleId + " came out of the tree - " + files
                         + " file(s) into " + Path.GetFileName(save));
                return true;
            }
            catch (Exception ex) { Log.Warn("could not capture " + titleId, ex); return false; }
            finally
            {
                try { if (building != null && Directory.Exists(building)) Directory.Delete(building, true); }
                catch { }
            }
        }

        /// <summary>Put a previous session back into a freshly built tree. A missing save is the
        /// ordinary case of a game nobody has played yet.</summary>
        private static void RestoreSave(Vita3kLayout layout, string titleId, string root)
        {
            string folder = null;
            try
            {
                var save = SavePathFor(layout, titleId);
                if (save == null || !File.Exists(save)) return;

                folder = save + "." + Guid.NewGuid().ToString("N") + ".open";
                if (!SnapFile.Unpack(save, folder, out var error))
                { Log.Warn("could not open the save: " + error); return; }

                int written = SnapDelta.Apply(root, folder, out error);
                Log.Info("restored " + written + " file(s) of " + titleId + "'s save");
            }
            catch (Exception ex) { Log.Warn("could not restore the save of " + titleId, ex); }
            finally
            {
                try { if (folder != null && Directory.Exists(folder)) Directory.Delete(folder, true); }
                catch { }
            }
        }

        /// <summary>The lazy net, for whatever the watcher missed: capture only when this game is the
        /// one in the tree and the tree is newer than its save. In the settled case that is two
        /// timestamps and nothing else.</summary>
        public static string RefreshSave(Vita3kLayout layout, string titleId)
        {
            try
            {
                var save = SavePathFor(layout, titleId);
                if (save == null) return null;
                if (!string.Equals(WorkTitle(layout), titleId, StringComparison.Ordinal)) return save;

                var root = WorkRoot(layout);
                if (root == null || !Directory.Exists(root)) return save;
                if (Vita3kPaths.EmulatorRunning()) return save;

                var newest = Newest(root);
                if (File.Exists(save) && newest <= File.GetLastWriteTimeUtc(save)) return save;

                // NEVER WAITS. This runs when the host asks for saves - possibly on its UI thread - and
                // a session operation holding the gate may be releasing a RAM disk, which takes a
                // minute and a half. That operation captures the session itself; this one steps aside.
                if (!Monitor.TryEnter(SessionGate)) return save;
                try { CaptureLocked(layout, titleId); }
                finally { Monitor.Exit(SessionGate); }
                return save;
            }
            catch (Exception ex) { Log.Warn("could not refresh the save of " + titleId, ex); return null; }
        }

        // ── helpers ──────────────────────────────────────────────────────────

        /// <summary>Is the tree still? Same file count and same newest write time, twice, a quarter of
        /// a second apart. Crude, and enough: an emulator flushing a save changes one or the
        /// other.</summary>
        private static bool Quiet(string root)
        {
            try
            {
                var a = Describe(root);
                Thread.Sleep(250);
                return a == Describe(root);
            }
            catch { return false; }
        }

        private static string Describe(string root)
        {
            int count = 0;
            var newest = DateTime.MinValue;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                count++;
                var when = File.GetLastWriteTimeUtc(file);
                if (when > newest) newest = when;
            }
            return count + "|" + newest.Ticks;
        }

        private static DateTime Newest(string root)
        {
            var newest = DateTime.MinValue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    var when = File.GetLastWriteTimeUtc(file);
                    if (when > newest) newest = when;
                }
            }
            catch { }
            return newest;
        }

        private static void CopyTree(string from, string to, Action<double> progress = null)
        {
            foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, dir.Substring(from.Length).TrimStart('\\', '/')));

            var files = new List<string>(Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories));
            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                var target = Path.Combine(to, file.Substring(from.Length).TrimStart('\\', '/'));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, overwrite: true);
                if (progress != null) { try { progress((double)(i + 1) / files.Count); } catch { } }
            }
        }

        private static void MarkPending(Vita3kLayout layout, string titleId)
        {
            try
            {
                File.WriteAllText(PendingPath(layout),
                    titleId + "\t" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch { }
        }

        private static void ClearPending(Vita3kLayout layout)
        {
            try { var p = PendingPath(layout); if (p != null && File.Exists(p)) File.Delete(p); }
            catch { }
        }
    }
}
