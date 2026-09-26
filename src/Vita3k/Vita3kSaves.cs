// Launching a game onto a disposable Vita, and what the host sees of the save that comes out.
//
// THE LAUNCH IS WHERE EVERYTHING HAPPENS. PrepareEmulatorForLaunch is called with the emulator and
// the game, and by the time it returns the virtual Vita exists, the firmware is on it, the game is
// installed onto it, its reference walk has been taken and the previous save is back in. Vita3K then
// starts and finds a console that looks exactly as it did when the last session ended.
//
// AND THE SESSION COMES OUT BECAUSE WE WATCH THE PROCESS, not because anybody tells us. Measured
// next door over a full session: LaunchBox 14 raised exactly one event, PluginInitialized, and called
// none of IGameLaunchingPlugin's three methods. The hook is implemented anyway - it costs nothing and
// another host may well raise it - but the thing that works is seeing Vita3K disappear.
//
// A save is ONE FILE, a .vitasav, and IsSaveContainer says so. The container path is where every
// save-management defect in this repository came from: backups that made empty folders, restores
// refused for not being a file, a delete that quietly did nothing.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;
using LbIntegrations.Snapshot;

namespace LbIntegrations.Vita3k
{
    public partial class Vita3kPlugin : IGameLaunchingPlugin
    {
        /// <summary>What a Vita save's group id starts with, so a title id survives the round trip
        /// through the host - AddSaveArgs carries neither the emulator nor the game's rom.</summary>
        private const string SavePrefix = "vita3k:title:";

        private const string SaveGroupName = "Vita save";
        private const string SaveChipText = "VITA";

        // ── the launch ───────────────────────────────────────────────────────

        /// <summary>The command line that runs the app WE installed, rather than installing it again.
        ///
        /// TWO THINGS MEASURED ON A REAL LAUNCH, and neither is guessable from the documentation.
        ///
        /// -r wants a TITLE ID, and the host appends the game's path after the command line. So the
        /// emulator received `-r "C:\...\GAME.zip"` - and -r is VALIDATED against the installed app
        /// list, so CLI11 rejected the value and the process was gone in 250 ms. The host said
        /// nothing beyond a failure dialog. Hence -r carries the title id the install just produced.
        ///
        /// AND THE APPENDED PATH STILL HAS TO GO SOMEWHERE. Left as a positional it means "install
        /// this and run it" - and it WINS over -r: measured, the log reads
        ///     input-content-path: C:\...\GAME.zip
        ///     input-installed-path: PCSE00965
        ///     Installing archive from CLI: C:\...\GAME.zip
        /// That would unpack the game a second time, over our own install, AFTER the reference walk
        /// was taken - so anything the second pass wrote differently would land in the save.
        ///
        /// So the line ends with -Z (--app-args), which takes one free TEXT and swallows the path
        /// the host is about to add. Measured: the emulator stays up and installs nothing.
        ///
        /// -Z is added ONLY when there is a game path to swallow, because a dangling -Z is a missing
        /// required value and CLI11 would refuse the whole line.</summary>
        internal static string CommandLineFor(string current, string titleId, bool hostWillAppendPath)
        {
            var kept = new List<string>();
            var words = (current ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                // Drop any -r/--installed-path already there, with its value when it has one: ours
                // is the one that names the app actually installed in this session.
                if (words[i] == "-r" || words[i] == "--installed-path")
                {
                    if (i + 1 < words.Length && !words[i + 1].StartsWith("-")) i++;
                    continue;
                }
                // A trailing -Z from an earlier build of this method would swallow our own -r.
                if (words[i] == "-Z" || words[i] == "--app-args")
                {
                    if (i + 1 < words.Length && !words[i + 1].StartsWith("-")) i++;
                    continue;
                }
                kept.Add(words[i]);
            }

            kept.Add("-r");
            kept.Add(titleId);
            if (hostWillAppendPath) kept.Add("-Z");
            return string.Join(" ", kept);
        }

        public override PrepareForLaunchResponse PrepareEmulatorForLaunch(PrepareForLaunchArgs args)
        {
            bool go = true;
            try
            {
                var exe = Safe(() => args?.EmulatorBeingLaunched?.ApplicationPath);
                var rom = Safe(() => args?.GameBeingLaunched?.ApplicationPath);
                if (string.IsNullOrWhiteSpace(exe)) return new PrepareForLaunchResponse(success: true);

                var layout = Vita3kPaths.Resolve(ResolveFullPath(exe));
                NotPlaying();

                if (!Vita3kWorkspace.HasBase(layout))
                {
                    // Nothing to build a console from. Not a reason to refuse somebody their game:
                    // Vita3K will open on whatever it has and say what is missing far better than we
                    // could from here.
                    Log.Warn("no pristine firmware is put aside - launching without a disposable Vita");
                    return new PrepareForLaunchResponse(success: true);
                }

                // THE ONLY PROGRESS THE USER GETS AT LAUNCH: PrepareForLaunchArgs has no channel for
                // it. The window stays invisible for a quick relaunch - see Vita3kProgressWindow.
                string titleId, error;
                var gameTitle = Safe(() => args?.GameBeingLaunched?.Title);
                using (var window = Vita3kProgressWindow.Open("Vita3K - " + (string.IsNullOrWhiteSpace(gameTitle) ? "preparing the game" : gameTitle)))
                {
                    titleId = Vita3kWorkspace.Prepare(layout, ResolveFullPath(rom), out error,
                                                      (step, fraction) => window?.Report(step, fraction));
                }
                if (titleId == null)
                {
                    // REFUSED, and this is the one place we do refuse. A game that starts on a
                    // half-built console writes into a tree whose reference was never taken, and the
                    // session would be lost with nothing to say so.
                    Log.Warn("not launching: " + error);
                    return new PrepareForLaunchResponse(success: false);
                }

                Playing(layout, titleId);
                go = true;

                var line = CommandLineFor(Safe(() => args?.CurrentCommandLine), titleId,
                                          !string.IsNullOrWhiteSpace(rom));
                Log.Info("command line: " + line);
                return new PrepareForLaunchResponse(success: true) { NewCommandLine = line };
            }
            catch (Exception ex) { Log.Warn("PrepareEmulatorForLaunch", ex); }

            return new PrepareForLaunchResponse(success: go);
        }

        // ── watching it end ──────────────────────────────────────────────────

        private static Vita3kLayout _playingLayout;
        private static string _playingTitleId;

        internal static void Playing(Vita3kLayout layout, string titleId)
        {
            _playingLayout = layout;
            _playingTitleId = titleId;
            WatchTheEmulator(layout, titleId);
        }

        internal static void NotPlaying() { _playingLayout = null; _playingTitleId = null; }

        public void OnBeforeGameLaunching(IGame game, IAdditionalApplication app, IEmulator emulator) { }

        public void OnAfterGameLaunched(IGame game, IAdditionalApplication app, IEmulator emulator) { }

        /// <summary>KEPT FOR A HOST THAT SAYS SOMETHING. LaunchBox 14 is not one - measured - so the
        /// watcher below is what actually takes the session out.</summary>
        public void OnGameExited()
        {
            var layout = _playingLayout;
            var titleId = _playingTitleId;
            NotPlaying();
            if (layout == null || titleId == null) return;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    if (Vita3kWorkspace.CaptureOnExit(layout, titleId))
                        Log.Info("the session of " + titleId + " came out as the game closed");
                }
                catch (Exception ex) { Log.Warn("capture on exit", ex); }
            });
        }

        /// <summary>Watch the emulator ourselves. THIS IS THE SIGNAL THAT DEPENDS ON NOBODY.
        ///
        /// Two minutes for it to appear, so a launch the host refuses does not leave a thread polling
        /// for ever; then no ceiling at all, because a session lasts as long as it lasts.</summary>
        private static void WatchTheEmulator(Vita3kLayout layout, string titleId)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var armed = DateTime.UtcNow;
                    bool appeared = false;
                    while ((DateTime.UtcNow - armed).TotalSeconds < 120)
                    {
                        if (Vita3kPaths.EmulatorRunning()) { appeared = true; break; }
                        System.Threading.Thread.Sleep(500);
                    }
                    if (!appeared)
                    {
                        Log.Info("watcher: Vita3K never appeared within two minutes of the launch");
                        return;
                    }
                    Log.Info("watcher: Vita3K is running");

                    var started = DateTime.UtcNow;
                    while (Vita3kPaths.EmulatorRunning()) System.Threading.Thread.Sleep(500);
                    Log.Info("watcher: Vita3K is gone after " + (int)(DateTime.UtcNow - started).TotalSeconds
                             + "s - this is the end of the program, whatever the host does or does not say");

                    if (Vita3kWorkspace.CaptureOnExit(layout, titleId))
                        Log.Info("watcher: the session of " + titleId + " came out of the tree");
                }
                catch (Exception ex) { Log.Warn("watcher", ex); }
            });
        }

        // ── what the host sees ───────────────────────────────────────────────

        public override bool SupportsSaveManagement() => true;

        public override GetSavesResponse GetSaves(GetSavesArgs args)
        {
            try
            {
                if (args?.Emulator == null) return new GetSavesResponse("No emulator was supplied.");

                var appPath = Safe(() => args.Emulator.ApplicationPath);
                if (!Vita3kPaths.IsVita3kExecutable(appPath))
                    return new GetSavesResponse("This emulator is not Vita3K.");

                var layout = Vita3kPaths.Resolve(ResolveFullPath(appPath));
                if (layout?.InstallDir == null) return new GetSavesResponse(new List<GameSaveBase>());

                var found = new List<GameSaveBase>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var app in args.AdditionalApplications
                                    ?? (IReadOnlyCollection<IAdditionalApplication>)Array.Empty<IAdditionalApplication>())
                {
                    var rom = Safe(() => app.ApplicationPath);
                    Collect(layout, rom, Safe(() => app.GameId), Safe(() => app.Id), found, seen);
                }
                foreach (var game in args.Games ?? (IReadOnlyCollection<IGame>)Array.Empty<IGame>())
                {
                    var rom = Safe(() => game.ApplicationPath);
                    Collect(layout, rom, Safe(() => game.Id), null, found, seen);
                }

                return new GetSavesResponse(found);
            }
            catch (Exception ex)
            {
                Log.Warn("GetSaves", ex);
                return new GetSavesResponse("Could not read Vita3K saves: " + ex.Message);
            }
        }

        private static void Collect(Vita3kLayout layout, string romPath, string gameId, string appId,
                                    List<GameSaveBase> into, HashSet<string> seen)
        {
            try
            {
                var titleId = TitleIdOf(romPath);
                if (titleId == null) return;

                // THE LAZY NET. If the watcher missed the end of a session, this is where it is
                // noticed - and in the settled case it is two timestamps and nothing else.
                var save = Vita3kWorkspace.RefreshSave(layout, titleId);
                if (save == null || !File.Exists(save)) return;
                if (!seen.Add(save + "|" + gameId + "|" + appId)) return;

                long size = 0; DateTime when = default;
                try { var i = new FileInfo(save); size = i.Length; when = i.LastWriteTimeUtc; } catch { }

                into.Add(new GameSaveGame
                {
                    GameId = gameId,
                    AdditionalApplicationId = appId,
                    FileLocation = save,
                    IsDirectory = false,
                    OriginalFileName = Path.GetFileName(save),
                    SaveGroupId = SavePrefix + titleId,
                    SaveGroupName = SaveGroupName,
                    DisplayChipText = SaveChipText,
                    ReportedFileSizeBytes = size > 0 ? size : (long?)null,
                    ReportedLastModifiedUtc = when == default ? (DateTime?)null : when,
                });
            }
            catch (Exception ex) { Log.Warn("could not collect the save of " + romPath, ex); }
        }

        /// <summary>The title id a game's archive declares, cached by path and stamp: this is asked
        /// once per game every time the host lists saves, and reading a param.sfo means opening a
        /// zip.</summary>
        private static readonly Dictionary<string, string> _titleIds =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static string TitleIdOf(string romPath)
        {
            try
            {
                var full = ResolveFullPath(romPath);
                if (!Vita3kContent.Installable(full) || !File.Exists(full)) return null;

                var info = new FileInfo(full);
                var key = info.FullName + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
                lock (_titleIds)
                {
                    if (_titleIds.TryGetValue(key, out var cached)) return cached;
                    var content = Vita3kContent.Describe(full, out _);
                    var titleId = content?.IsGame == true ? content.TitleId : null;
                    _titleIds[key] = titleId;
                    return titleId;
                }
            }
            catch { return null; }
        }

        private static string TitleIdFrom(GameSaveBase save)
        {
            try
            {
                var group = Safe(() => save?.SaveGroupId);
                return group != null && group.StartsWith(SavePrefix, StringComparison.Ordinal)
                    ? group.Substring(SavePrefix.Length) : null;
            }
            catch { return null; }
        }

        /// <summary>A .vitasav is one file. See the header, and the eight defects the container path
        /// produced next door.</summary>
        public override bool IsSaveContainer(GameSaveBase save) => false;

        public override bool IsSecondarySaveFile(string filePath) => false;

        public override IReadOnlyList<string> GetCompanionSaveFiles(string primaryFilePath)
            => Array.Empty<string>();

        public override bool IsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            try
            {
                var titleId = TitleIdFrom(save);
                if (titleId == null) return false;
                var layout = Vita3kPaths.Resolve(ResolveFullPath(emulatorApplicationPath));
                var expected = Vita3kWorkspace.SavePathFor(layout, titleId);
                var actual = Safe(() => save?.FileLocation);
                return expected != null && actual != null
                       && string.Equals(Path.GetFullPath(expected), Path.GetFullPath(actual),
                                        StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public override AddSaveResponse AddSaveFile(AddSaveArgs args)
        {
            try
            {
                var save = args?.SaveToAdd;
                if (save == null) return new AddSaveResponse("No save was supplied.");

                var source = Safe(() => save.FileLocation);
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                    return new AddSaveResponse("This Vita backup is not a file: " + source);

                var titleId = TitleIdFrom(save);
                if (titleId == null)
                    return new AddSaveResponse("This backup does not say which Vita title it belongs to.");

                if (!SnapFile.Holds(source))
                    return new AddSaveResponse("That file is not a Vita save: it carries no "
                                               + SnapDelta.IndexName + ".");

                // AddSaveArgs carries neither the emulator nor the game's path, so the install is
                // found through the data manager - the same detour melonDS makes, for the same
                // reason.
                var layout = LayoutFor(save);
                if (layout == null)
                    return new AddSaveResponse("Could not locate the Vita3K installation.");

                var target = Vita3kWorkspace.SavePathFor(layout, titleId);
                if (target == null)
                    return new AddSaveResponse("Could not work out where this save belongs.");

                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(source, target, overwrite: true);

                // WORKS ON NOTHING AT ALL, which is what deleting a save leaves behind. The next
                // launch rebuilds the console from the pristine firmware, reinstalls the game, takes
                // a fresh reference walk and applies this file onto it - in that order. Nothing here
                // depends on a working tree existing now.
                Log.Info("restored the save of " + titleId + "; the next launch rebuilds its Vita around it");

                long size = 0; DateTime when = default;
                try { var i = new FileInfo(target); size = i.Length; when = i.LastWriteTimeUtc; } catch { }

                // The row that comes back has to describe the same thing the listing does, or the
                // host looks for the save in the wrong shape and decides it is not there.
                return new AddSaveResponse(new GameSaveGame
                {
                    GameId = Safe(() => save.GameId),
                    AdditionalApplicationId = Safe(() => save.AdditionalApplicationId),
                    FileLocation = target,
                    IsDirectory = false,
                    OriginalFileName = Path.GetFileName(target),
                    SaveGroupId = SavePrefix + titleId,
                    SaveGroupName = Safe(() => save.SaveGroupName) ?? SaveGroupName,
                    DisplayChipText = SaveChipText,
                    ReportedFileSizeBytes = size > 0 ? size : (long?)null,
                    ReportedLastModifiedUtc = when == default ? (DateTime?)null : when,
                });
            }
            catch (Exception ex)
            {
                Log.Warn("AddSaveFile", ex);
                return new AddSaveResponse("Could not restore the save: " + ex.Message);
            }
        }

        public override PluginResponse RemoveSave(GameSaveBase save)
        {
            try
            {
                if (TitleIdFrom(save) == null) return base.RemoveSave(save);

                var path = Safe(() => save?.FileLocation);
                if (string.IsNullOrWhiteSpace(path)) return new PluginResponse(false, "This save has no location.");
                if (File.Exists(path)) File.Delete(path);
                Log.Info("removed " + path);
                return new PluginResponse(true);
            }
            catch (Exception ex)
            {
                Log.Warn("RemoveSave", ex);
                return new PluginResponse(false, "Could not remove the save: " + ex.Message);
            }
        }

        /// <summary>The install a save belongs to. Its own path answers when it is still in place -
        /// a save sits at &lt;install&gt;\portable\saves\&lt;title&gt;\state.vitasav - and a vault copy is
        /// somewhere else entirely, so that case goes through the data manager instead.</summary>
        private static Vita3kLayout LayoutFor(GameSaveBase save)
        {
            var own = LayoutFromSavePath(Safe(() => save?.FileLocation));
            if (own != null) return own;

            try
            {
                var dm = PluginHelper.DataManager;
                var game = dm?.GetGameById(Safe(() => save?.GameId));
                var emuId = Safe(() => game?.EmulatorId);
                var emu = string.IsNullOrEmpty(emuId) ? null : dm?.GetEmulatorById(emuId);
                var appPath = Safe(() => emu?.ApplicationPath);
                if (Vita3kPaths.IsVita3kExecutable(appPath))
                    return Vita3kPaths.Resolve(ResolveFullPath(appPath));

                var any = dm?.GetAllEmulators()?
                    .FirstOrDefault(e => Vita3kPaths.IsVita3kExecutable(Safe(() => e.ApplicationPath)));
                var anyPath = Safe(() => any?.ApplicationPath);
                return anyPath == null ? null : Vita3kPaths.Resolve(ResolveFullPath(anyPath));
            }
            catch { return null; }
        }

        /// <summary>Walk up from a save file to the install that owns it: the save sits at
        /// &lt;install&gt;\portable\saves\&lt;title&gt;\state.vitasav, so the install is four levels up.</summary>
        private static Vita3kLayout LayoutFromSavePath(string savePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(savePath)) return null;
                var dir = Path.GetDirectoryName(Path.GetFullPath(savePath));      // <title>
                dir = Path.GetDirectoryName(dir);                                  // saves
                dir = Path.GetDirectoryName(dir);                                  // portable
                var install = Path.GetDirectoryName(dir);                          // the install
                if (install == null) return null;
                var exe = Vita3kPaths.FindExecutable(install);
                return exe == null ? null : Vita3kPaths.Resolve(exe);
            }
            catch { return null; }
        }
    }
}
