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
// WITH --use-vhdx the tree is none of those: it is a DIFFERENCING DISK over the game's own disk,
// attached for the session, captured like any other tree, then detached and deleted. The marker's
// seventh field names it. See Vita3kVhdx for the disks, PrepareOnVhdx for the session.
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
    /// <summary>What one launch asks for, beyond the game: our flags on the host's command line (see
    /// Vita3kPlugin.CommandLineFor) and the title the host shows, which the search for the game's
    /// update and DLC uses as one more name for its folder.</summary>
    /// <summary>What a console was built with, as far as a save cares: the version the game runs at
    /// (its update's APP_VER, or its own) and the DLC installed, by CONTENT_ID. Written beside the
    /// session while it runs, and packed INTO the save it produces, so the save knows what it was
    /// made with. Text, one key per line, sorted - the save stays deterministic.</summary>
    internal sealed class SaveContext
    {
        public string AppVer;
        public SortedSet<string> Dlc = new SortedSet<string>(StringComparer.Ordinal);

        public string Text()
        {
            var text = new System.Text.StringBuilder();
            text.Append("app_ver=").Append(AppVer ?? "").Append('\n');
            foreach (var d in Dlc) text.Append("dlc=").Append(d).Append('\n');
            return text.ToString();
        }

        public static SaveContext Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var c = new SaveContext();
            foreach (var line in text.Split('\n'))
            {
                var l = line.Trim();
                if (l.StartsWith("app_ver=", StringComparison.Ordinal)) c.AppVer = l.Substring(8);
                else if (l.StartsWith("dlc=", StringComparison.Ordinal) && l.Length > 4) c.Dlc.Add(l.Substring(4));
            }
            return c;
        }

        public bool SameAs(SaveContext other)
            => other != null && string.Equals(AppVer ?? "", other.AppVer ?? "", StringComparison.Ordinal) && Dlc.SetEquals(other.Dlc);
    }

    internal sealed class Vita3kLaunch
    {
        public bool NoRamDisk;      // --no-ramdisk
        public int? MarginMb;       // --ramdisk-margin=
        public int? Vita3kRamMb;    // --vita3k-ram=
        public string HostTitle;    // the game's title in LaunchBox
        public bool UseVhdx;        // --use-vhdx
        public string VhdxDir;      // --use-vhdx=<dir>, null for the emulator's own vhdx folder
        public string GameId;       // the game's LaunchBox ID - its choice of update and DLC
    }

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

        /// <summary>The running session's SaveContext, beside the marker; and its name inside a save.
        /// The latter cannot be a flattened path: FlatName never produces a dash-led name.</summary>
        public const string ContextName = "work.context";
        public const string SaveContextName = "-lbip-context.txt";

        /// <summary>Who answers the question a risky save raises. The dialog by default; the probe
        /// puts its own answer here.</summary>
        internal static Func<string, string, bool> Ask = (title, text) => Vita3kQuestion.Ask(title, text, fallback: true);
        public const string SaveFile = "state.vitasav";

        /// <summary>Headroom on top of the firmware and the game. A session writes saves, shader
        /// caches and logs, and a disk that fills up mid-game is worse than one that was refused.
        /// --ramdisk-margin on the command line replaces it.</summary>
        public const int MarginMb = 512;

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
            // And the exit confirmation, at every launch: a Vita3K run on its own, or reinstalled,
            // must not bring it back between two sessions.
            Vita3kConfig.QuietExitConfirm(layout);
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

        /// <summary>Tab-separated fields: title id, the game's path, its length, its write time, where
        /// the tree lives - and, sixth, the key of the update and DLC installed with it (VitaExtras.Key;
        /// absent from a marker written before they were, which reads as "none"). The fingerprint is
        /// not a hash - this runs at every launch and a .vpk is several hundred megabytes.</summary>
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

        /// <summary>The session disk the tree lives on - the seventh field, written by a --use-vhdx
        /// session only. Null for a RAM disk or the work folder.</summary>
        public static string WorkVhdx(Vita3kLayout layout)
        {
            var parts = MarkerParts(layout);
            return parts != null && parts.Length > 6 && parts[6].Trim().Length > 0 ? parts[6].Trim() : null;
        }

        private static bool OnVhdx(Vita3kLayout layout) => WorkVhdx(layout) != null;

        private static string Fingerprint(string romPath)
        {
            try
            {
                var info = new FileInfo(romPath);
                return info.FullName + "\t" + info.Length + "\t" + info.LastWriteTimeUtc.Ticks;
            }
            catch { return romPath + "\t0\t0"; }
        }

        private static void Remember(Vita3kLayout layout, string titleId, string romPath, string root, string extrasKey = "",
                                     string vhdx = null)
        {
            try
            {
                File.WriteAllText(MarkerPath(layout), titleId + "\t" + Fingerprint(romPath) + "\t" + root + "\t" + (extrasKey ?? "")
                                                      + (vhdx != null ? "\t" + vhdx : ""));
            }
            catch (Exception ex) { Log.Warn("could not write " + TitleMarker, ex); }
        }

        private static void Forget(Vita3kLayout layout)
        {
            try { var p = MarkerPath(layout); if (p != null && File.Exists(p)) File.Delete(p); }
            catch (Exception ex) { Log.Warn("could not clear " + TitleMarker, ex); }
            try { var c = Under(layout, ContextName); if (c != null && File.Exists(c)) File.Delete(c); } catch { }
        }

        /// <summary>Is the working tree already this exact game, still there, and still walked?
        ///
        /// When it is, a launch does nothing at all: no mount, no copy, no reinstall. That is what
        /// makes a second run of the same game cheap, and it is also the rule you asked for - the
        /// working tree is NOT cleared when a game ends, only when a DIFFERENT one starts.</summary>
        public static bool CanReuse(Vita3kLayout layout, string titleId, string romPath) => CanReuse(layout, titleId, romPath, "");

        /// <summary>... and holding exactly the update and DLC found now: one added, removed or newer
        /// since means a rebuild, not a console without it.</summary>
        public static bool CanReuse(Vita3kLayout layout, string titleId, string romPath, string extrasKey)
        {
            try
            {
                var parts = MarkerParts(layout);
                if (parts == null) return false;
                // A SESSION DISK IS NEVER REUSED: it is thrown away once captured, and one still
                // there is a session that did not end - saved first, like a different game.
                if (parts.Length > 6 && parts[6].Trim().Length > 0) return false;
                var had = parts.Length > 5 ? parts[5] : "";
                if (!string.Equals(had, extrasKey ?? "", StringComparison.Ordinal))
                {
                    Log.Info("the updates and DLC are not the ones the working tree was built with - rebuilding");
                    return false;
                }
                if (!string.Equals(parts[0], titleId, StringComparison.Ordinal)) return false;
                if (!string.Equals(parts[1] + "\t" + parts[2] + "\t" + parts[3], Fingerprint(romPath),
                                   StringComparison.Ordinal)) return false;
                if (!File.Exists(ReferencePath(layout))) return false;

                var root = parts[4];
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return false;

                // A LETTER READ FROM A FILE MAY BE ANYTHING BY NOW - after a restart Z: can be somebody
                // else's drive. On a RAM disk the tree is only ours if the drive says so.
                return !OnRamDisk(layout) || Owns(layout, root);
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
        /// <summary>Point portable\fs at <paramref name="target"/> - for a console that is not a session's
        /// (Vita3kSettingsSession). The same link, the same checks.</summary>
        internal static bool PointFsAt(Vita3kLayout layout, string target, out string error) => Link(layout, target, out error);

        /// <summary>Drop portable\fs's link - see PointFsAt.</summary>
        internal static void DropFs(Vita3kLayout layout) => DropLink(layout);

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

                // A LINK, OR NOTHING. "A folder is there" is not the question: measured 28/09, a real
                // portable\fs made by Vita3K run on its own kept the name, mklink was refused, and this
                // said success - the game was installed on the RAM disk and Vita3K opened the empty
                // folder instead, then quit at once.
                bool linked = false;
                try { linked = File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint); } catch { }
                if (linked) { Log.Info("portable\\fs -> " + target); return true; }
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
                    // A REAL folder, not a link. Without a pristine console put aside, it may be the
                    // firmware itself, not yet adopted: not ours to touch.
                    if (!HasBase(layout))
                    {
                        Log.Warn("portable\\fs is a real folder, not a junction - leaving it alone");
                        return;
                    }
                    // With one, portable\fs is only ever our link - a real folder there is what Vita3K
                    // makes when it runs WITHOUT us (its first-run tree: empty devices and a user.xml,
                    // measured 28/09), and it would take the link's place at every launch.
                    //
                    // NOTHING BUT THAT TREE - empty folders, at most the default profile Vita3K writes
                    // for itself (ux0/user/00/user.xml, which the pristine console has its own of): it
                    // holds nothing of anybody's, and it goes (Mehdi's point). Anything more is MOVED
                    // ASIDE, never deleted: whatever it holds is still there for anyone who wants it.
                    List<string> files;
                    try { files = Directory.EnumerateFiles(link, "*", SearchOption.AllDirectories).ToList(); }
                    catch { files = null; }
                    var profile = Path.Combine(link, "ux0", "user", "00", "user.xml");
                    if (files != null && files.All(f => string.Equals(Path.GetFullPath(f), Path.GetFullPath(profile), StringComparison.OrdinalIgnoreCase)))
                    {
                        Directory.Delete(link, recursive: true);
                        Log.Warn("portable\\fs was Vita3K's own first-run tree (empty folders" + (files.Count > 0 ? " and its default user.xml" : "")
                                 + ") - removed, so the session's link can take its place");
                        return;
                    }
                    var aside = link + ".stray-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    Directory.Move(link, aside);
                    Log.Warn("portable\\fs was a real folder (Vita3K run on its own?) - moved aside to "
                             + Path.GetFileName(aside) + " so the session's link can take its place");
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
                                     Action<string, double?> report) => Prepare(layout, romPath, out error, report, new Vita3kLaunch());

        /// <summary>... with what this launch asked for - see Vita3kLaunch.</summary>
        public static string Prepare(Vita3kLayout layout, string romPath, out string error,
                                     Action<string, double?> report, Vita3kLaunch launch)
        {
            launch ??= new Vita3kLaunch();
            // Waits for an end-of-session release still in progress rather than racing it.
            if (!Monitor.TryEnter(SessionGate))
            {
                report?.Invoke("Waiting for the previous session to be put away...", null);
                Monitor.Enter(SessionGate);
            }
            try { return PrepareLocked(layout, romPath, out error, report, launch); }
            finally { Monitor.Exit(SessionGate); }
        }

        private static string PrepareLocked(Vita3kLayout layout, string romPath, out string error,
                                            Action<string, double?> report, Vita3kLaunch launch)
        {
            error = null;
            // A fake settings console a crash left behind goes before anything is built - see Vita3kSettingsSession.
            Vita3kSettingsSession.Sweep(layout);
            bool noRamDisk = launch.NoRamDisk;
            int marginMb = launch.MarginMb ?? MarginMb;
            int? vita3kRamMb = launch.Vita3kRamMb;
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

                // ITS UPDATE AND DLC, found before anything else: they decide whether the tree there
                // is still the right one, and how big a new one has to be. See Vita3kExtras.
                report?.Invoke("Looking for updates and DLC...", null);
                // A .pkg's licence is looked for in this emulator's zrif folder - see Vita3kLicences.
                Vita3kLicences.InstallDir = layout.InstallDir;
                var extras = Vita3kExtras.For(romPath, content, launch.HostTitle, layout.InstallDir,
                                              Vita3kExtrasChoice.Load(layout.InstallDir, launch.GameId));
                var extrasKey = extras.Key();

                // --use-vhdx SUPPLANTS THE RAM DISK - when it can. Whatever stops it is logged and
                // the session takes the usual path: a launch is never refused over it.
                if (launch.UseVhdx)
                {
                    var vhdxDir = Vita3kVhdx.DirFor(layout, launch.VhdxDir);
                    var whyNot = Vita3kVhdx.WhyNot(layout, vhdxDir);
                    if (whyNot != null)
                        Log.Warn(Vita3kPlugin.UseVhdxFlag + ": " + whyNot + " - this session takes the usual path");
                    else
                    {
                        if (noRamDisk || launch.MarginMb != null || vita3kRamMb != null)
                            Log.Info("the RAM disk flags are not used: this session is on a VHDX");
                        var onDisk = PrepareOnVhdx(layout, vhdxDir, romPath, content, extras, extrasKey, report, out var vhdxError);
                        if (onDisk != null) return onDisk;
                        Log.Warn(Vita3kPlugin.UseVhdxFlag + ": " + vhdxError + " - this session takes the usual path");
                    }
                }

                // Reused as it is - unless it is on a RAM disk and this launch asks for the disk: then
                // it is saved and rebuilt where it was asked to be, like a different game.
                if (CanReuse(layout, content.TitleId, romPath, extrasKey) && !(noRamDisk && OnRamDisk(layout)))
                {
                    Log.Info("the working tree already holds " + content.TitleId + " - reusing it");
                    // The link is remade every time: a release interrupted after it dropped the
                    // junction and before it forgot the tree would otherwise leave a console the
                    // emulator cannot see.
                    if (!Link(layout, WorkRoot(layout), out error)) return null;
                    return content.TitleId;
                }

                // A DIFFERENT GAME. This is the only moment anything is cleared - so it is also the
                // last chance to save a session that never ended properly.
                report?.Invoke("Putting the previous game away...", null);
                var previous = WorkTitle(layout);
                if (previous != null) SaveBeforeClearing(layout, previous);
                Teardown(layout);

                // EVERYTHING THE DISK WILL HOLD: the firmware, the game, its update, its DLC, the save
                // that goes back in - and the margin for what the session writes. What an install needs
                // only WHILE it runs (a .pkg's largest item) is freed before the save and the session,
                // so it shares their room: the larger of the two, not both.
                int baseMb = BaseSizeMb(layout);
                int gameMb = CeilMb(Vita3kContent.WorkingSizeBytes(romPath));
                int updateMb = CeilMb(extras.Update?.Bytes ?? 0);
                int dlcMb = CeilMb(extras.Addons.Sum(a => Math.Max(0, a.Bytes)));
                int saveMb = CeilMb(SaveBytes(layout, content.TitleId));
                int transientMb = CeilMb(new[] { romPath }.Concat(extras.All.Select(e => e.Path)).Max(p => Vita3kContent.TransientBytes(p)));
                int sizeMb = baseMb + gameMb + updateMb + dlcMb + Math.Max(saveMb + marginMb, transientMb);
                Log.Info("the console needs " + sizeMb + " MB: firmware " + baseMb + " + game " + gameMb + " + update " + updateMb
                         + " + DLC " + dlcMb + " + the larger of save " + saveMb + " + margin " + marginMb
                         + " and what an install needs while it runs (" + transientMb + ")");

                report?.Invoke("Preparing a fresh console...", null);
                var root = OpenWorkingTree(layout, content.TitleId, sizeMb, report, noRamDisk, vita3kRamMb);
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

                    // THE UPDATE, THEN THE DLC - after the game (an update needs the app and its
                    // licence) and BEFORE the reference, or they would come out of the session as a
                    // change, into the save. One that fails is left out, said so, and the game runs
                    // without it: better than no game.
                    InstallExtras(extras, root, installed, report);

                    // THE REFERENCE, between the install and everything else. See the header.
                    // From the base's own manifest, hashing only what the install wrote - see
                    // SnapWalk.WriteFrom, which falls back to a full walk the moment the tree is not
                    // exactly that.
                    report?.Invoke("Taking the console's fingerprint...", 0);
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    int walked = SnapWalk.WriteFrom(root, BaseManifestPath(layout), installed.Written, installed.Hashed,
                                                    ReferencePath(layout), out error, f => report?.Invoke(null, f),
                                                    out int hashed, out int reused);
                    if (walked < 0) return null;
                    Log.Info("reference walk: " + walked + " entries in " + watch.ElapsedMilliseconds + " ms"
                             + (hashed >= 0 ? " - " + hashed + " file(s) read, " + reused
                                              + " hashed as the install wrote them, the rest from the base"
                                            : " - full walk"));


                    var context = ContextFor(content, extras);
                    var restored = RestoreSave(layout, content, root, context);
                    try { File.WriteAllText(Under(layout, ContextName), context.Text()); }
                    catch (Exception ex) { Log.Warn("could not write " + ContextName + " - the next save will not know what it was made with", ex); }

                    // And what every file looks like from outside, so the capture at the end reads only
                    // what the session touched. AFTER the restore and WITHOUT what it wrote: a restored
                    // file differs from the reference and has to be captured again every time, and
                    // File.Copy keeps the old write time - so it is not left to times at all, it is
                    // simply never trusted. Failing costs only speed: the capture reads everything.
                    watch.Restart();
                    if (SnapStamps.Write(root, ReferencePath(layout), restored, out var stampError))
                        Log.Info("stamped the tree in " + watch.ElapsedMilliseconds + " ms, "
                                 + restored.Count + " restored file(s) left to be read");
                    else
                        Log.Warn("no stamps (" + stampError + ") - the capture will read the whole tree");

                    Remember(layout, content.TitleId, romPath, root, extrasKey);
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

        /// <summary>What a console built with this game, update and DLC gives a save to remember.</summary>
        private static SaveContext ContextFor(VitaContent content, VitaExtras extras)
        {
            var context = new SaveContext { AppVer = extras?.Update?.Content.AppVer ?? content.AppVer };
            foreach (var a in extras?.Addons ?? new List<VitaExtra>())
                if (!string.IsNullOrEmpty(a.Content.ContentId)) context.Dlc.Add(a.Content.ContentId);
            return context;
        }

        // ── a session on a VHDX ──────────────────────────────────────────────

        /// <summary>--use-vhdx: the previous session put away, any orphaned session disk in the folder
        /// saved, the game's disk built or reused, and a fresh differencing disk over it for this
        /// session - the title id, or null with <paramref name="error"/> (the usual path then runs, on
        /// a console already cleared: nothing half-built is left for it to trip on).
        ///
        /// NOTHING IS INSTALLED HERE. The firmware, the game, its update and its DLC are in the disks
        /// under this one; what a launch does is create the session's disk (well under a second),
        /// attach it, put the save back, and stamp the tree.</summary>
        private static string PrepareOnVhdx(Vita3kLayout layout, string dir, string romPath, VitaContent content,
                                            VitaExtras extras, string extrasKey, Action<string, double?> report, out string error)
        {
            error = null;
            var watch = Stopwatch.StartNew();

            // The previous session first, whatever it was on - a RAM disk, work\, a session disk -
            // and saved if it never ended: a console is only ever cleared by the next launch.
            report?.Invoke("Putting the previous game away...", null);
            var previous = WorkTitle(layout);
            if (previous != null) SaveBeforeClearing(layout, previous);
            Teardown(layout);

            RecoverOrphanSessions(layout, dir, report);

            var gameDisk = Vita3kVhdx.EnsureGameBase(layout, dir, romPath, content, extras, report, out error);
            if (gameDisk == null) return null;

            var titleId = content.TitleId;
            var session = Vita3kVhdx.SessionVhdx(dir, titleId);
            report?.Invoke("Preparing the console...", null);
            if (File.Exists(session)) DropSessionDisk(session);
            if (!RamDrive.CreateDifferencingVhdx(session, gameDisk, out error)) { DropSessionDisk(session); return null; }

            string drive = null;
            bool ready = false;
            try
            {
                drive = RamDrive.AttachVhdx(session, false, out error);
                if (drive == null) return null;
                var root = Path.Combine(drive, Vita3kVhdx.FsName);
                if (!Directory.Exists(root)) { error = "the session disk holds no console at " + root; return null; }
                Claim(layout, drive);

                // THE GAME'S REFERENCE, taken once when its disk was built - the tree under the
                // session is exactly that, by construction.
                File.Copy(Vita3kVhdx.GameReference(dir, titleId), ReferencePath(layout), overwrite: true);
                SnapStamps.Delete(ReferencePath(layout));
                if (!Link(layout, root, out error)) return null;

                var context = ContextFor(content, extras);
                var restored = RestoreSave(layout, content, root, context);
                try { File.WriteAllText(Under(layout, ContextName), context.Text()); }
                catch (Exception ex) { Log.Warn("could not write " + ContextName + " - the next save will not know what it was made with", ex); }

                var stamps = Stopwatch.StartNew();
                if (SnapStamps.Write(root, ReferencePath(layout), restored, out var stampError))
                    Log.Info("stamped the tree in " + stamps.ElapsedMilliseconds + " ms, " + restored.Count + " restored file(s) left to be read");
                else
                    Log.Warn("no stamps (" + stampError + ") - the capture will read the whole tree");

                Remember(layout, titleId, romPath, root, extrasKey, session);
                ready = true;
                Log.Info("working on the session disk " + Path.GetFileName(session) + " at " + root
                         + ", over " + Path.GetFileName(gameDisk) + " - ready in " + watch.ElapsedMilliseconds + " ms");
                return titleId;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                Log.Warn("could not prepare the session disk", ex);
                return null;
            }
            finally
            {
                if (!ready)
                {
                    report?.Invoke("That did not work - cleaning up...", null);
                    DropLink(layout);
                    DropSessionDisk(session);
                    try { var r = ReferencePath(layout); if (File.Exists(r)) File.Delete(r); SnapStamps.Delete(r); } catch { }
                    Forget(layout);
                }
            }
        }

        /// <summary>A session disk gone: detached if it is attached, then deleted.</summary>
        private static void DropSessionDisk(string session)
        {
            if (string.IsNullOrEmpty(session) || !File.Exists(session)) return;
            RamDrive.DetachVhdx(session, out _);   // refused when it is not attached: nothing to do then
            try { File.Delete(session); Log.Info("deleted the session disk " + Path.GetFileName(session)); }
            catch (Exception ex) { Log.Warn("could not delete the session disk " + session, ex); }
        }

        /// <summary>The session disk the marker names, attached again READ-ONLY when nothing has it -
        /// the machine restarted, or its drive was unplugged and is back - and the marker pointed at
        /// its new letter. True when the tree can be read.</summary>
        private static bool Reattach(Vita3kLayout layout)
        {
            var session = WorkVhdx(layout);
            if (session == null) return false;
            var root = WorkRoot(layout);
            if (root != null && Directory.Exists(root) && Owns(layout, root)) return true;
            if (!File.Exists(session)) return false;

            RamDiskHost.LaunchBoxRoot = () => Vita3kPaths.LaunchBoxRootOf(layout);
            var drive = RamDrive.AttachVhdx(session, true, out var error);
            if (drive == null) { Log.Warn("could not attach the session disk " + session + " again: " + error); return false; }
            try
            {
                var parts = MarkerParts(layout);
                parts[4] = Path.Combine(drive, Vita3kVhdx.FsName);
                File.WriteAllText(MarkerPath(layout), string.Join("\t", parts));
                Log.Info("attached the session disk " + Path.GetFileName(session) + " again, read-only, at " + parts[4]);
                return true;
            }
            catch (Exception ex) { Log.Warn("could not point the marker at the reattached session disk", ex); return false; }
        }

        /// <summary>Session disks in the folder that no marker of this console names - its drive was
        /// unplugged while the session ran and another game has been played since, say. Saved when
        /// they are newer than the game's save, then deleted. THEIR OWN PROOF DECIDES: a session disk
        /// whose owner file names another install is that install's, left alone; one attached by
        /// somebody (in use) is left alone too.
        ///
        /// Without its marker the session has no stamps and no context file: the capture reads the
        /// whole tree, and the context is the one the game's identity gives - the disk it sits on.</summary>
        private static void RecoverOrphanSessions(Vita3kLayout layout, string dir, Action<string, double?> report)
        {
            string[] sessions;
            try { sessions = Directory.GetFiles(dir, "*.session.vhdx"); }
            catch (Exception ex) { Log.Warn("could not look for session disks in " + dir, ex); return; }

            foreach (var session in sessions)
            {
                var titleId = Path.GetFileName(session);
                titleId = titleId.Substring(0, titleId.Length - ".session.vhdx".Length);
                string drive = null;
                try
                {
                    drive = RamDrive.AttachVhdx(session, true, out var error);
                    if (drive == null) { Log.Info("the session disk " + Path.GetFileName(session) + " cannot be attached (" + error + ") - in use elsewhere? left alone"); continue; }
                    if (!Owns(layout, drive))
                    {
                        Log.Info("the session disk " + Path.GetFileName(session) + " is another install's - left alone");
                        RamDrive.DetachVhdx(session, out _); drive = null;
                        continue;
                    }

                    var save = SavePathFor(layout, titleId);
                    var reference = Vita3kVhdx.GameReference(dir, titleId);
                    bool newer = save != null && (!File.Exists(save) || File.GetLastWriteTimeUtc(session) > File.GetLastWriteTimeUtc(save));
                    if (newer && File.Exists(reference))
                    {
                        Log.Info("the session disk " + Path.GetFileName(session) + " was never saved - saving it before it goes");
                        report?.Invoke("Saving a session that never ended...", null);
                        var context = Vita3kVhdx.ContextFrom(Vita3kVhdx.ReadIdentity(Vita3kVhdx.GameIdentity(dir, titleId)));
                        if (!CaptureFrom(layout, titleId, Path.Combine(drive, Vita3kVhdx.FsName), reference, context?.Text(), report))
                        {
                            Log.Warn("the orphaned session of " + titleId + " could not be saved - its disk is kept");
                            RamDrive.DetachVhdx(session, out _); drive = null;
                            continue;
                        }
                    }
                    else
                        Log.Info("the session disk " + Path.GetFileName(session) + " is older than the save of " + titleId + " - nothing to take from it");

                    drive = null;
                    DropSessionDisk(session);   // detaches it, then deletes it
                }
                catch (Exception ex) { Log.Warn("could not recover the session disk " + session, ex); }
                finally { if (drive != null) RamDrive.DetachVhdx(session, out _); }
            }
        }

        /// <summary>A RAM disk when the machine can hold one, a folder otherwise.
        ///
        /// THE CHECK IS AGAINST FREE PHYSICAL MEMORY, and that matters. An ImDisk drive with no image
        /// behind it is a `vm` disk, backed by virtual memory - so one bigger than the RAM actually
        /// free does not fail, it PAGES to the system drive. We would be writing the SSD twice over
        /// while believing we were sparing it, and more slowly than a plain folder.</summary>
        private static string OpenWorkingTree(Vita3kLayout layout, string titleId, int sizeMb,
                                              Action<string, double?> report = null, bool noRamDisk = false,
                                              int? vita3kRamMb = null)
        {
            RamDiskHost.LaunchBoxRoot = () => Vita3kPaths.LaunchBoxRootOf(layout);

            // THE RAM THE EMULATOR NEEDS COMES FIRST. A RAM disk that fits only by leaving Vita3K
            // itself short would just move the paging from the disk image to the emulator.
            // --vita3k-ram, when given, is taken as it is: whoever wrote it knows the game.
            int reserve = vita3kRamMb ?? EmulatorReserveMb(layout, titleId);
            int need = sizeMb + reserve;
            int free = RamDrive.GetFreeRamMb();

            bool ready = RamDrive.IsReady();
            if (noRamDisk)
            {
                Log.Info("working on disk: " + Vita3kPlugin.NoRamDiskFlag + " is on the command line");
                ready = false;
            }
            else if (ready && free > 0 && free < need && RamDrive.CanCleanMemory)
            {
                // SHORT, BUT MAYBE NOT FOR LONG: other programs' idle memory can be handed back.
                // It costs a write of their dirty pages to the page file, once - against a whole
                // session played on the disk.
                report?.Invoke("Freeing memory for the console...", null);
                var said = RamDrive.CleanMemory();
                int after = RamDrive.GetFreeRamMb();
                Log.Info("freeing memory: " + free + " MB free before, " + after + " MB after, " + need
                         + " MB needed (" + sizeMb + " for the disk, " + reserve + " for Vita3K) - " + (said ?? "no answer"));
                free = after;
            }

            if (ready && free > 0 && need < free)
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
                    Claim(layout, drive);
                    Log.Info("working on a RAM disk at " + root + " (" + sizeMb + " MB, and " + reserve
                             + " MB kept for Vita3K, of " + free + " free)");
                    return root;
                }
                Log.Warn("the RAM disk did not mount - falling back to a folder");
            }
            else
            {
                Log.Info("working on disk: " + sizeMb + " MB for the disk and " + reserve + " MB for Vita3K needed, "
                         + free + " MB of physical RAM free"
                         + (RamDrive.IsReady() ? "" : ", and no RAM disk is available"));
            }

            var work = WorkDir(layout);
            Directory.CreateDirectory(work);
            return work;
        }

        /// <summary>Install the chosen update and DLC onto the tree the game was just installed on,
        /// folding what each wrote into <paramref name="installed"/> - the reference walk hashes those
        /// folders, or takes the hashes the install already has.</summary>
        internal static void InstallExtras(VitaExtras extras, string root, VitaContent installed, Action<string, double?> report)
        {
            // THE WINDOW SAYS WHICH (Mehdi's wording): the update by its version, with its own bar; the
            // DLC under ONE bar for all of them, "1/3", "2/3"... The install's own step text ("Decrypting
            // <game>...") would name the game for an update - the label here replaces it, the fraction
            // is kept.
            int dlcCount = extras.Addons.Count, dlcIndex = 0;
            foreach (var extra in extras.All)
            {
                var c = extra.Content;
                string step;
                Action<string, double?> progress;
                if (c.IsPatch)
                {
                    step = "Installing update " + (c.AppVer ?? "?") + "...";
                    progress = (_, f) => report?.Invoke(step, f);
                }
                else
                {
                    int i = dlcIndex++;
                    step = "Installing DLC " + (i + 1) + "/" + dlcCount + ": " + (c.Title ?? c.ContentId) + "...";
                    progress = (_, f) => report?.Invoke(step, ((double)i + (f ?? 0)) / dlcCount);
                }
                var label = c.IsPatch ? "update " + (c.AppVer ?? "?") : "DLC " + (c.Title ?? c.ContentId);
                progress(null, 0);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var done = Vita3kContent.Install(extra.Path, c, root, out var why, progress);
                if (done == null)
                {
                    Log.Warn("the " + label + " was not installed (" + why + ") - the game runs without it");
                    var dest = Vita3kContent.DestinationFor(c, out _);
                    if (dest != null)
                    {
                        var half = Path.Combine(root, dest.Replace('/', Path.DirectorySeparatorChar));
                        try { if (Directory.Exists(half)) Directory.Delete(half, recursive: true); }
                        catch (Exception ex) { Log.Warn("could not clear the half-installed " + label, ex); }
                    }
                    continue;
                }
                installed.Written.AddRange(done.Written);
                foreach (var kv in done.Hashed) installed.Hashed[kv.Key] = kv.Value;
                Log.Info("installed the " + label + " (" + extra.Name + ") in " + watch.ElapsedMilliseconds + " ms");
            }
        }

        private static int CeilMb(long bytes) => bytes <= 0 ? 0 : (int)((bytes + 1024L * 1024 - 1) / (1024L * 1024));

        /// <summary>The size of the save that will go back in - it is stored uncompressed.</summary>
        private static long SaveBytes(Vita3kLayout layout, string titleId)
        {
            try { var save = SavePathFor(layout, titleId); return save != null && File.Exists(save) ? new FileInfo(save).Length : 0; }
            catch { return 0; }
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

                try { var r = ReferencePath(layout); if (r != null && File.Exists(r)) File.Delete(r); SnapStamps.Delete(r); } catch { }
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
                    report?.Invoke(OnVhdx(layout) ? "Releasing the session disk..." : "Releasing the RAM disk...", null);
                if (previous != null) ReleaseDrive(layout, previous);
                report?.Invoke("Clearing the console...", null);

                var work = WorkDir(layout);
                if (work != null && Directory.Exists(work))
                {
                    try { Directory.Delete(work, recursive: true); }
                    catch (Exception ex) { Log.Warn("could not clear " + work, ex); }
                }

                try { var r = ReferencePath(layout); if (r != null && File.Exists(r)) File.Delete(r); SnapStamps.Delete(r); } catch { }
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

        private static string _closingShownFor;
        private static DateTime _closingShownAt;

        private static bool CaptureOnExitLocked(Vita3kLayout layout, string titleId)
        {
            try
            {
                var root = WorkRoot(layout);
                if (root == null || !Directory.Exists(root)) return false;
                if (!string.Equals(WorkTitle(layout), titleId, StringComparison.Ordinal)) return false;

                // THE CLOSING WINDOW, as the launch has one - ALWAYS shown, and modal to the host (see
                // Vita3kProgressWindow). Once per session end: the second of the two end signals, a few
                // seconds behind the first, finds the work done and shows nothing unless it takes time.
                bool first = !(string.Equals(_closingShownFor, titleId, StringComparison.Ordinal)
                               && (DateTime.UtcNow - _closingShownAt).TotalSeconds < 30);
                _closingShownFor = titleId; _closingShownAt = DateTime.UtcNow;
                using var window = Vita3kProgressWindow.Open("Vita3K - saving " + NameOf(titleId), always: first);
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
                    var settle = System.Diagnostics.Stopwatch.StartNew();
                    var deadline = DateTime.UtcNow + SettleBudget;

                    // THE FLOOR IS FOR A SIGNAL THAT COMES BEFORE THE PROCESS HAS GONE - the host's
                    // OnGameExited may. When Vita3K is already out of the process list nothing is left
                    // to write, and the stillness check below is the only wait worth paying.
                    if (Vita3kPaths.EmulatorRunning()) Thread.Sleep(SettleFloor);

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
                    Log.Info("the tree was still after " + settle.ElapsedMilliseconds + " ms");

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
                        Log.Info("releasing the " + (OnVhdx(layout) ? "session disk" : "RAM disk") + " of " + titleId + " - the session is saved");
                        var release = System.Diagnostics.Stopwatch.StartNew();
                        Teardown(layout, report);
                        Log.Info("released in " + release.ElapsedMilliseconds + " ms");
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

        /// <summary>Give back the session's drive.
        ///
        /// By key when this process mounted it. By the marker when it did not - LaunchBox restarted
        /// between the launch and now, and the drive would otherwise stay mounted until a reboot. A
        /// letter read from a file may be anything by now, so it is unmounted only while it is still
        /// an ImDisk drive.</summary>
        private static void ReleaseDrive(Vita3kLayout layout, string titleId)
        {
            // A SESSION DISK is not a drive to unmount: it is detached, and deleted - the session in
            // it has been saved, or is being given up by whoever called this.
            var session = WorkVhdx(layout);
            if (session != null) { DropSessionDisk(session); return; }
            if (RamDrive.UnmountFor(titleId) || !OnRamDisk(layout)) return;
            var drive = Path.GetPathRoot(WorkRoot(layout) ?? "");
            if (string.IsNullOrEmpty(drive) || !RamDrive.IsImDiskDrive(drive)) return;
            if (!Owns(layout, drive))
            {
                // Measured risk, not a guess: LiteBox mounts its own RAM disks from the same helper
                // and picks letters from Z down, exactly as we do.
                Log.Info(drive + " is an ImDisk drive but not this console's - left mounted");
                return;
            }
            Log.Info("the RAM disk " + drive + " was not mounted by this process - releasing it from the marker");
            RamDrive.Unmount(drive);
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
            string context = null;
            try { var c = Under(layout, ContextName); if (c != null && File.Exists(c)) context = File.ReadAllText(c); } catch { }
            return CaptureFrom(layout, titleId, WorkRoot(layout), ReferencePath(layout), context, report);
        }

        /// <summary>Compare a tree with a reference and pack the difference into the game's save, with
        /// <paramref name="context"/> (SaveContext.Text) inside it - the session's tree by default,
        /// an orphaned session disk's for RecoverOrphanSessions.</summary>
        private static bool CaptureFrom(Vita3kLayout layout, string titleId, string root, string reference, string context,
                                        Action<string, double?> report)
        {
            string building = null;
            try
            {
                var save = SavePathFor(layout, titleId);
                if (root == null || save == null || !File.Exists(reference)) return false;

                building = save + "." + Guid.NewGuid().ToString("N") + ".state";
                report?.Invoke("Looking for what the game changed...", 0);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                int files = SnapDelta.Capture(root, reference, building, out var error, f => report?.Invoke(null, f),
                                              out int hashed);
                if (files < 0) { Log.Warn("nothing captured: " + error); return false; }
                Log.Info("capture walk in " + watch.ElapsedMilliseconds + " ms - " + hashed + " file(s) read"
                         + (SnapStamps.Read(reference) == null ? " (no stamps: the whole tree)" : ", the rest untouched since the reference"));

                // WHAT IT WAS MADE WITH, into the save itself - see SaveContext.
                if (context != null) File.WriteAllText(Path.Combine(building, SaveContextName), context);

                report?.Invoke("Packing the save...", null);
                watch.Restart();
                if (!SnapFile.Pack(building, save, out error))
                { Log.Warn("could not pack the save: " + error); return false; }
                Log.Info("packed in " + watch.ElapsedMilliseconds + " ms");

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
        /// ordinary case of a game nobody has played yet.
        ///
        /// Returns the paths the save names as files, written or not - never null.</summary>
        ///
        /// A SAVE STAYS VALID WHATEVER THE UPDATE AND DLC (Mehdi's rule): it holds what a session wrote,
        /// and goes back onto any console of the same game. Two cases can still trouble the GAME, and
        /// only those ask first - a save made at a HIGHER version than the game will now run (its
        /// update is gone), and a save made with a DLC that is no longer here. Answered no, the game
        /// starts without it, and the save is set aside as a copy the session's capture cannot
        /// overwrite. A save made before saves knew their context asks nothing.
        ///
        /// And when the update or DLC changed at all, an entry of the save inside the game's, the
        /// update's or a DLC's folder is left alone: written back, it would put the old version's file
        /// over the new one's. Those folders are read-only to a game, so this is rare - and logged.</summary>
        private static List<string> RestoreSave(Vita3kLayout layout, VitaContent game, string root, SaveContext current)
        {
            string folder = null;
            var named = new List<string>();
            var titleId = game.TitleId;
            try
            {
                var save = SavePathFor(layout, titleId);
                if (save == null || !File.Exists(save)) return named;

                folder = save + "." + Guid.NewGuid().ToString("N") + ".open";
                if (!SnapFile.Unpack(save, folder, out var error))
                { Log.Warn("could not open the save: " + error); return named; }

                SaveContext made = null;
                var contextFile = Path.Combine(folder, SaveContextName);
                if (File.Exists(contextFile)) made = SaveContext.Parse(File.ReadAllText(contextFile));
                Log.Info("the save of " + titleId + " was made " + (made == null ? "before saves recorded their context"
                         : "at version " + (made.AppVer ?? "?") + " with " + made.Dlc.Count + " DLC")
                         + "; the game now runs " + (current.AppVer ?? "?") + " with " + current.Dlc.Count + " DLC");

                if (made != null)
                {
                    var worries = new List<string>();
                    if (Vita3kExtras.VersionOf(made.AppVer) > Vita3kExtras.VersionOf(current.AppVer))
                        worries.Add("It was made with the game at version " + made.AppVer + ", and the game will now run at "
                                    + (current.AppVer ?? "?") + " - the update " + made.AppVer + " was not found.");
                    var missing = made.Dlc.Where(d => !current.Dlc.Contains(d)).ToList();
                    if (missing.Count > 0)
                        worries.Add("It was made with " + (missing.Count == 1 ? "a DLC that is" : missing.Count + " DLC that are")
                                    + " no longer here: " + string.Join(", ", missing.Select(ShortId)) + ".");
                    if (worries.Count > 0)
                    {
                        var name = game.FullTitle ?? game.Title ?? titleId;
                        var text = "The save of " + name + " may not load in the game as it is now.\n\n" + string.Join("\n\n", worries)
                                   + "\n\nLoad the save anyway?\n\nNo: the game starts without it, and the save is kept aside as a copy.";
                        Log.Info("asking before restoring: " + string.Join(" ", worries));
                        bool load = Ask("Vita3K - " + name, text);
                        if (!load)
                        {
                            var aside = SetAside(save, made.AppVer);
                            Log.Info("not restored, as answered - the save is kept aside as " + (aside != null ? Path.GetFileName(aside) : "(could not)"));
                            return named;
                        }
                        Log.Info("restoring anyway, as answered");
                    }
                }

                // Update or DLC changed: their folders are not the save's to write.
                Func<string, bool> leaveAlone = null;
                if (made != null && !made.SameAs(current))
                    leaveAlone = path => path.StartsWith("ux0/app/", StringComparison.OrdinalIgnoreCase)
                                         || path.StartsWith("ux0/patch/", StringComparison.OrdinalIgnoreCase)
                                         || path.StartsWith("ux0/addcont/", StringComparison.OrdinalIgnoreCase);

                int written = SnapDelta.Apply(root, folder, out error, leaveAlone, out var left);
                named = SnapDelta.FilesIn(folder).Where(p => !left.Contains(p)).ToList();
                if (left.Count > 0)
                    Log.Warn("left " + left.Count + " entry(ies) of the save alone - inside the game's, the update's or a DLC's folder,"
                             + " which changed since: " + string.Join(", ", left.Take(5)) + (left.Count > 5 ? ", ..." : ""));
                Log.Info("restored " + written + " file(s) of " + titleId + "'s save");
            }
            catch (Exception ex) { Log.Warn("could not restore the save of " + titleId, ex); }
            finally
            {
                try { if (folder != null && Directory.Exists(folder)) Directory.Delete(folder, true); }
                catch { }
            }
            return named;
        }

        /// <summary>Move a save out of the session's way, as state.v&lt;version&gt;.vitasav beside it -
        /// dated when that name is taken. Null when it could not be moved.</summary>
        private static string SetAside(string save, string version)
        {
            try
            {
                var dir = Path.GetDirectoryName(save);
                var tag = string.IsNullOrWhiteSpace(version) ? "old" : "v" + version.Trim();
                var aside = Path.Combine(dir, "state." + tag + ".vitasav");
                if (File.Exists(aside)) aside = Path.Combine(dir, "state." + tag + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".vitasav");
                File.Move(save, aside);
                return aside;
            }
            catch (Exception ex) { Log.Warn("could not set the save aside", ex); return null; }
        }

        private static string ShortId(string contentId) => contentId != null && contentId.Length > 20 ? contentId.Substring(20) : contentId;

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

        internal static void CopyTree(string from, string to, Action<double> progress = null)
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

        // ── what a session that never ended leaves behind ────────────────────
        //
        // A SESSION CAN STOP WITHOUT ENDING. The machine loses power, the host is killed, the watcher
        // dies with it - and nothing captures, nothing releases. What that leaves:
        //   - a RAM disk still mounted, holding progress nobody saved (host killed, machine up)
        //   - a junction pointing at a drive that no longer exists (machine restarted)
        //   - a work\ tree newer than its save (either, on the disk fallback)
        //   - staging copies and half-built temporaries nothing will ever delete
        // Three things answer it: a proof of ownership on the drive, a capture before anything is
        // cleared, and a look around when the plugin starts.

        // ── what the emulator itself needs ───────────────────────────────────
        //
        // The RAM disk is not the only thing a session holds in memory: Vita3K does too - the guest's
        // memory, textures, shaders - and how much depends on the game. So it is MEASURED: the watcher
        // records the emulator's peak working set at the end of every session, per game, and the
        // next launch keeps that much free beside the disk, with a margin. A game never measured
        // gets the largest peak seen so far, or a default until there is one. And NEVER LESS THAN
        // 1.5 GB (Mehdi): a peak taken on a short session - a menu, a crash - says little about the
        // next one. --vita3k-ram= is taken as it is, floor or not: whoever wrote it knows the game.

        public const string MemoryName = "lbip-vita3k.memory";
        private const int DefaultReserveMb = 2048;
        public const int MinReserveMb = 1536;
        private const int ReserveMarginPercent = 115;   // integers: 3000 * 1.15 is 3449.99... in floating point

        /// <summary>What to keep free for Vita3K itself when it runs <paramref name="titleId"/>.</summary>
        public static int EmulatorReserveMb(Vita3kLayout layout, string titleId)
        {
            var peaks = ReadPeaks(layout);
            int measured;
            if (titleId != null && peaks.TryGetValue(titleId, out measured)) return Math.Max(MinReserveMb, measured * ReserveMarginPercent / 100);
            if (peaks.Count > 0) return Math.Max(MinReserveMb, peaks.Values.Max() * ReserveMarginPercent / 100);
            return DefaultReserveMb;
        }

        /// <summary>Record what Vita3K peaked at running <paramref name="titleId"/>. The latest session
        /// is the one kept: a game's needs are what its current build and settings ask for.</summary>
        public static void RememberEmulatorPeak(Vita3kLayout layout, string titleId, int peakMb)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(titleId) || peakMb <= 0) return;
                var path = Under(layout, MemoryName);
                if (path == null) return;
                var peaks = ReadPeaks(layout);
                peaks[titleId] = peakMb;
                File.WriteAllLines(path, peaks.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "\t" + p.Value));
                Log.Info("Vita3K peaked at " + peakMb + " MB running " + titleId + " - the next launch keeps "
                         + Math.Max(MinReserveMb, peakMb * ReserveMarginPercent / 100) + " MB free for it"
                         + (peakMb * ReserveMarginPercent / 100 < MinReserveMb ? " (the floor; its peak + 15% is " + peakMb * ReserveMarginPercent / 100 + ")" : ""));
            }
            catch (Exception ex) { Log.Warn("could not record the emulator's memory", ex); }
        }

        private static Dictionary<string, int> ReadPeaks(Vita3kLayout layout)
        {
            var peaks = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                var path = Under(layout, MemoryName);
                if (path == null || !File.Exists(path)) return peaks;
                foreach (var line in File.ReadAllLines(path))
                {
                    var parts = line.Split('\t');
                    if (parts.Length == 2 && int.TryParse(parts[1], out var mb) && mb > 0) peaks[parts[0]] = mb;
                }
            }
            catch { }
            return peaks;
        }

        /// <summary>The file at the root of a session's RAM disk that says whose it is.</summary>
        public const string OwnerName = "lbip-vita3k.owner";

        private static string Owner(Vita3kLayout layout)
        {
            var portable = Portable(layout);
            return portable == null ? null : Path.GetFullPath(portable).TrimEnd(Path.DirectorySeparatorChar);
        }

        /// <summary>Write the drive's owner: this install's portable folder. A second install of the
        /// plugin, LiteBox's own RAM disks, a USB stick given the letter after a restart - none of
        /// them carries it.</summary>
        private static void Claim(Vita3kLayout layout, string drive)
        {
            try { File.WriteAllText(Path.Combine(Path.GetPathRoot(drive), OwnerName), Owner(layout)); }
            catch (Exception ex) { Log.Warn("could not mark the RAM disk as this console's", ex); }
        }

        /// <summary>Does the drive holding <paramref name="path"/> say it is this console's?</summary>
        private static bool Owns(Vita3kLayout layout, string path)
        {
            try
            {
                var owner = Owner(layout);
                var file = Path.Combine(Path.GetPathRoot(path) ?? "", OwnerName);
                return owner != null && File.Exists(file)
                       && string.Equals(File.ReadAllText(file).Trim(), owner, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>Capture the session of <paramref name="titleId"/> if its tree is newer than its
        /// save - the case of a session that stopped without ending. True when it is safe to clear
        /// the tree now: captured, or nothing to capture.</summary>
        private static bool SaveBeforeClearing(Vita3kLayout layout, string titleId)
        {
            try
            {
                if (OnVhdx(layout)) Reattach(layout);                                    // a reboot detached it
                var root = WorkRoot(layout);
                if (root == null || !Directory.Exists(root)) return true;              // nothing left to read
                if (OnRamDisk(layout) && !Owns(layout, root)) return true;               // not ours to read
                if (!File.Exists(ReferencePath(layout))) return true;                    // never finished building

                var save = SavePathFor(layout, titleId);
                if (save != null && File.Exists(save) && Newest(root) <= File.GetLastWriteTimeUtc(save)) return true;

                if (Vita3kPaths.EmulatorRunning())
                {
                    Log.Warn("the session of " + titleId + " was never saved, and Vita3K is still running -"
                             + " not reading a tree it may still be writing");
                    return false;
                }

                Log.Info("the session of " + titleId + " never ended (the host or the machine stopped first)"
                         + " - saving it before its tree is cleared");
                return CaptureLocked(layout, titleId);
            }
            catch (Exception ex) { Log.Warn("could not save the session of " + titleId + " before clearing it", ex); return false; }
        }

        /// <summary>The look around when the plugin starts. Never while Vita3K runs, never waiting for
        /// a session in progress, never throwing.</summary>
        public static void CleanUpAtStart(Vita3kLayout layout)
        {
            try
            {
                var portable = Portable(layout);
                if (portable == null || !Directory.Exists(portable)) return;
                if (Vita3kPaths.EmulatorRunning()) { Log.Info("start-up check skipped - Vita3K is running"); return; }
                if (!Monitor.TryEnter(SessionGate)) return;
                try
                {
                    RamDiskHost.LaunchBoxRoot = () => Vita3kPaths.LaunchBoxRootOf(layout);

                    var title = WorkTitle(layout);
                    var root = WorkRoot(layout);
                    bool there = root != null && Directory.Exists(root);

                    if (title != null && OnVhdx(layout))
                    {
                        var session = WorkVhdx(layout);
                        var holder = Path.GetPathRoot(session);
                        if (string.IsNullOrEmpty(holder) || !Directory.Exists(holder))
                            // AN EXTERNAL DRIVE UNPLUGGED: the session is on it, maybe unsaved. Kept, marker
                            // and all, for when it comes back - forgetting it here would lose it.
                            Log.Info("start-up: the drive holding the session disk of " + title + " is not there - the session is kept for when it is");
                        else if (!File.Exists(session))
                        {
                            Log.Info("start-up: the session disk of " + title + " is gone - forgetting the session");
                            Teardown(layout);
                        }
                        else if (SaveBeforeClearing(layout, title))
                        {
                            Log.Info("start-up: the session disk of " + title + " outlived its session - saved, and thrown away");
                            Teardown(layout);
                        }
                    }
                    else if (title != null && OnRamDisk(layout))
                    {
                        if (there && Owns(layout, root))
                        {
                            // The host went, the machine did not: the drive holds the session, maybe
                            // unsaved. Saved first, then given back - kept, it holds its memory until
                            // a reboot. Relaunching the game only costs a rebuild.
                            if (SaveBeforeClearing(layout, title))
                            {
                                Log.Info("start-up: the RAM disk of " + title + " outlived its session - releasing it");
                                Teardown(layout);
                            }
                        }
                        else if (!there)
                        {
                            // The machine restarted: the drive and whatever it held are gone. What is
                            // left is a junction into nothing and a marker for a tree that does not exist.
                            Log.Info("start-up: the RAM disk of " + title + " is gone - forgetting the session");
                            Teardown(layout);
                        }
                        // There but not ours: somebody else's drive under our old letter. Left alone;
                        // the next launch will not reuse it either.
                    }
                    else if (title != null && there)
                    {
                        // The disk fallback: saved if it needs to be, and KEPT - work\ is only ever
                        // cleared by another game.
                        SaveBeforeClearing(layout, title);
                    }
                    else if (title == null)
                    {
                        DropLink(layout);   // a junction with no session behind it
                    }

                    if (WorkTitle(layout) == null || WorkRoot(layout) is string r && !Directory.Exists(r))
                        ClearPending(layout);

                    SweepLeftovers(layout);
                    Vita3kSettingsSession.Sweep(layout);
                }
                finally { Monitor.Exit(SessionGate); }
            }
            catch (Exception ex) { Log.Warn("start-up check", ex); }
        }

        /// <summary>Older than this and still there, a temporary is an orphan: every one of them lives
        /// for the few seconds of the operation that made it.</summary>
        private static readonly TimeSpan OrphanAge = TimeSpan.FromHours(1);

        /// <summary>Staging copies and half-built temporaries whose operation never finished.</summary>
        private static void SweepLeftovers(Vita3kLayout layout)
        {
            int removed = 0;
            var portable = Portable(layout);

            void Remove(FileSystemInfo item)
            {
                try
                {
                    if (DateTime.UtcNow - item.LastWriteTimeUtc < OrphanAge) return;
                    if (item is DirectoryInfo d) d.Delete(recursive: true); else item.Delete();
                    removed++;
                    Log.Info("start-up: removed the leftover " + item.FullName);
                }
                catch (Exception ex) { Log.Warn("could not remove the leftover " + item.FullName, ex); }
            }

            try
            {
                // Staging beside the disk fallback's tree, and the %TEMP% ones of older versions.
                foreach (var d in new DirectoryInfo(portable).EnumerateDirectories(Vita3kContent.StagingPrefix + "*")) Remove(d);
                foreach (var d in new DirectoryInfo(Path.GetTempPath()).EnumerateDirectories("lbip-vita3k-staging-*")) Remove(d);

                // Half-written manifests and stamps.
                foreach (var f in new DirectoryInfo(portable).EnumerateFiles("*.part")) Remove(f);

                // A capture, a restore or a swap that never finished, beside the saves.
                var saves = new DirectoryInfo(Path.Combine(portable, SavesName));
                if (saves.Exists)
                    foreach (var item in saves.EnumerateFileSystemInfos("*", SearchOption.AllDirectories).ToList())
                    {
                        var n = item.Name;
                        if (n.EndsWith(".state", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".open", StringComparison.OrdinalIgnoreCase)
                            || n.EndsWith(".part", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".old", StringComparison.OrdinalIgnoreCase))
                            if (item.Exists) Remove(item);
                    }
            }
            catch (Exception ex) { Log.Warn("could not look for leftovers", ex); }
            if (removed > 0) Log.Info("start-up: " + removed + " leftover(s) removed");
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
