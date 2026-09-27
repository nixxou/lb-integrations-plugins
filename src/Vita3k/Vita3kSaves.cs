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
using System.Text;
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

        /// <summary>OUR OWN FLAGS, read from the host's line and never passed on - Vita3K's CLI11
        /// rejects an option it does not know and the emulator would not start. Put on the emulator's
        /// command line in LaunchBox, or on one game's custom command line.
        ///
        ///     --no-ramdisk               this session is played on the disk (work@@), never on a RAM disk
        ///     --ramdisk-margin &lt;MB&gt;     room left free on the RAM disk beyond the firmware and the
        ///                                game, for saves, shader caches and logs - 512 when not given.
        ///                                Also written --ramdisk-margin=&lt;MB&gt;.
        ///     --vita3k-ram &lt;MB&gt;         RAM kept free for Vita3K itself beside the RAM disk, taken
        ///                                as it is - instead of the peak measured for this game plus
        ///                                15%, or 2048 before any measurement. Also =&lt;MB&gt;.</summary>
        internal const string NoRamDiskFlag = "--no-ramdisk";
        internal const string RamDiskMarginFlag = "--ramdisk-margin";
        internal const string Vita3kRamFlag = "--vita3k-ram";

        private static readonly string[] OurFlags = { NoRamDiskFlag };
        private static readonly string[] OurFlagsWithValue = { RamDiskMarginFlag, Vita3kRamFlag };

        /// <summary>The largest value taken at face value: past this it is a typo, not a wish.</summary>
        private const int MaxMb = 65536;

        /// <summary>Does the host's line carry one of our flags?</summary>
        internal static bool Carries(string line, string flag)
            => Tokenize(line ?? "").Exists(t => string.Equals(t, flag, StringComparison.OrdinalIgnoreCase));

        /// <summary>The margin the line asks for, in MB - see MbFrom.</summary>
        internal static int? MarginFrom(string line, out string problem) => MbFrom(line, RamDiskMarginFlag, out problem);

        /// <summary>The RAM the line asks to keep for Vita3K, in MB - see MbFrom.</summary>
        internal static int? Vita3kRamFrom(string line, out string problem) => MbFrom(line, Vita3kRamFlag, out problem);

        /// <summary>The MB one of our valued flags asks for - null when the line does not carry it, or
        /// carries something that is not a whole number of MB between 0 and 65536 (then
        /// <paramref name="problem"/> says what was found, and the default applies).</summary>
        internal static int? MbFrom(string line, string flag, out string problem)
        {
            problem = null;
            var tokens = Tokenize(line ?? "");
            for (int i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                string value;
                if (string.Equals(t, flag, StringComparison.OrdinalIgnoreCase))
                    value = i + 1 < tokens.Count ? tokens[i + 1] : null;
                else if (t.StartsWith(flag + "=", StringComparison.OrdinalIgnoreCase))
                    value = t.Substring(flag.Length + 1);
                else continue;

                if (int.TryParse(value, System.Globalization.NumberStyles.None,
                                 System.Globalization.CultureInfo.InvariantCulture, out int mb) && mb <= MaxMb)
                    return mb;
                problem = flag + " wants a whole number of MB up to " + MaxMb
                          + ", not " + (value == null ? "nothing" : "\"" + value + "\"");
                return null;
            }
            return null;
        }

        /// <summary>The command line that runs the app WE installed, rather than installing it again.
        ///
        /// MEASURED ON REAL LAUNCHES, three facts, and the third corrected an earlier reading of the
        /// first two.
        ///
        /// -r wants a TITLE ID. The line the host hands us is the FINAL one, game path included:
        /// `-F -r "C:\...\GAME.zip"` - and -r is VALIDATED against the installed app list, so CLI11
        /// rejected the path and the process was gone in 250 ms.
        ///
        /// A game path left as a positional means "install this and run it", and it WINS over -r:
        ///     input-content-path: C:\...\GAME.zip
        ///     input-installed-path: PCSE00965
        ///     Installing archive from CLI: C:\...\GAME.zip
        /// That would unpack the game a second time, after the reference walk was taken.
        ///
        /// AND NewCommandLine REPLACES THAT WHOLE LINE - nothing is appended after it. LiteBox's
        /// EmuPlugins.PrepareForLaunch says so in as many words, and LaunchBox showed it: an earlier
        /// version assumed the host would append the path and ended the line with -Z to swallow it.
        /// Split on spaces, the quoted path came apart, its first word became -r's value, the rest
        /// was left lying on the line - "-F [PCSE00965] [USA] [NoNpDRM].zip" -r PCSE00965 -Z" - and
        /// the emulator opened on its own window instead of the game.
        ///
        /// So the path is REMOVED, respecting quotes, and -r names the title id. With no title id -
        /// no console to build one on - the path is KEPT and only -r goes: Vita3K then installs and
        /// runs the game itself, which is the right fallback and what it would do without us.</summary>
        internal static string CommandLineFor(string current, string titleId, string romPath)
        {
            var tokens = Tokenize(current);
            var kept = new List<string>();

            for (int i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];

                if (Array.Exists(OurFlags, f => string.Equals(f, t, StringComparison.OrdinalIgnoreCase))) continue;
                if (Array.Exists(OurFlagsWithValue, f => t.StartsWith(f + "=", StringComparison.OrdinalIgnoreCase))) continue;
                if (Array.Exists(OurFlagsWithValue, f => string.Equals(f, t, StringComparison.OrdinalIgnoreCase)))
                {
                    // Its value goes with it - never the game path. A NEGATIVE number is a value too,
                    // refused by MarginFrom but still ours: left on the line, Vita3K would take "-5"
                    // for an option it does not know and not start.
                    if (i + 1 < tokens.Count && !IsTheGame(tokens[i + 1], romPath)
                        && (!tokens[i + 1].StartsWith("-") || long.TryParse(tokens[i + 1], out _))) i++;
                    continue;
                }

                if (t == "-r" || t == "--installed-path" || t == "-Z" || t == "--app-args")
                {
                    // Its value goes with it - unless that "value" is the game path the host put right
                    // after an emulator line ending in -r, which is the fallback's positional.
                    if (i + 1 < tokens.Count && !tokens[i + 1].StartsWith("-")
                        && (titleId != null || !IsTheGame(tokens[i + 1], romPath)))
                        i++;
                    continue;
                }

                if (titleId != null && IsTheGame(t, romPath)) continue;
                kept.Add(t);
            }

            if (titleId != null) { kept.Add("-r"); kept.Add(titleId); }
            return string.Join(" ", kept.ConvertAll(Quote));
        }

        /// <summary>Split a Windows command line the way the runtime will: spaces outside quotes
        /// separate, quotes group and are dropped. Backslash escapes are not honoured - a game path
        /// does not end in a quote.</summary>
        private static List<string> Tokenize(string line)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
            bool quoted = false, any = false;
            foreach (var c in line ?? "")
            {
                if (c == '"') { quoted = !quoted; any = true; continue; }
                if (!quoted && (c == ' ' || c == '\t'))
                {
                    if (any || current.Length > 0) tokens.Add(current.ToString());
                    current.Clear();
                    any = false;
                    continue;
                }
                current.Append(c);
            }
            if (any || current.Length > 0) tokens.Add(current.ToString());
            return tokens;
        }

        private static string Quote(string token)
            => token.Length == 0 || token.IndexOfAny(new[] { ' ', '\t' }) >= 0 ? "\"" + token + "\"" : token;

        /// <summary>Is this token the game? The full path, or the bare file name for an emulator entry
        /// set to pass the name without its folder or extension.</summary>
        private static bool IsTheGame(string token, string romPath)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(romPath)) return false;
            try
            {
                if (string.Equals(Path.GetFullPath(token), Path.GetFullPath(romPath), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }
            return string.Equals(token, Path.GetFileName(romPath), StringComparison.OrdinalIgnoreCase)
                   || string.Equals(token, Path.GetFileNameWithoutExtension(romPath), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The line the host is about to run. A host that passes none gets the emulator
        /// entry's own line, the least wrong thing to start from.</summary>
        private static string CurrentLine(PrepareForLaunchArgs args)
        {
            var line = Safe(() => args?.CurrentCommandLine);
            if (!string.IsNullOrWhiteSpace(line)) return line;
            return Safe(() => args?.EmulatorBeingLaunched?.CommandLine) ?? "";
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
                    // Vita3K installs and runs it itself, and says what is missing far better than we
                    // could from here - once -r is off the line, since -r would reject the path.
                    Log.Warn("no pristine firmware is put aside - launching without a disposable Vita");
                    var plain = CommandLineFor(CurrentLine(args), null, ResolveFullPath(rom));
                    Log.Info("command line: " + plain);
                    return new PrepareForLaunchResponse(success: true) { NewCommandLine = plain };
                }

                // NOT AN ARCHIVE WE INSTALL - a folder, an eboot, no game at all: then no console is
                // built, so there is no half-built one to protect, and refusing would only stop a game
                // Vita3K can very well start itself. Passed through, -r off the line. The probe's
                // generic launch check found this refusing a launch with no game at all.
                var romFull = ResolveFullPath(rom);
                if (string.IsNullOrWhiteSpace(romFull) || !Vita3kContent.Installable(romFull))
                {
                    Log.Info("not an archive this plugin installs (" + (romFull ?? "no game") + ") - handing it to Vita3K as it is");
                    var plain = CommandLineFor(CurrentLine(args), null, romFull);
                    return new PrepareForLaunchResponse(success: true) { NewCommandLine = plain };
                }

                // THE ONLY PROGRESS THE USER GETS AT LAUNCH: PrepareForLaunchArgs has no channel for
                // it. The window stays invisible for a quick relaunch - see Vita3kProgressWindow.
                string titleId, error;
                var gameTitle = Safe(() => args?.GameBeingLaunched?.Title);
                using (var window = Vita3kProgressWindow.Open("Vita3K - " + (string.IsNullOrWhiteSpace(gameTitle) ? "preparing the game" : gameTitle)))
                {
                    var current = CurrentLine(args);
                    bool noRamDisk = Carries(current, NoRamDiskFlag);
                    if (noRamDisk) Log.Info(NoRamDiskFlag + " is on the command line - this session stays on the disk");
                    int? margin = MarginFrom(current, out var marginProblem);
                    if (marginProblem != null) Log.Warn(marginProblem + " - keeping the default");
                    else if (margin != null) Log.Info(RamDiskMarginFlag + " " + margin + " is on the command line");
                    int? vitaRam = Vita3kRamFrom(current, out var ramProblem);
                    if (ramProblem != null) Log.Warn(ramProblem + " - keeping the measured reserve");
                    else if (vitaRam != null) Log.Info(Vita3kRamFlag + " " + vitaRam + " is on the command line");
                    titleId = Vita3kWorkspace.Prepare(layout, ResolveFullPath(rom), out error,
                                                      (step, fraction) => window?.Report(step, fraction), noRamDisk, margin, vitaRam);
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

                var line = CommandLineFor(CurrentLine(args), titleId, ResolveFullPath(rom));
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

        /// <summary>The host saying the game ended. LaunchBox 14 DOES call this - measured on a real
        /// Vita session, 60 ms apart from the watcher below, which is how two captures came to race
        /// for the same file. Both paths stay, since neither can be relied on alone, and the
        /// workspace serialises them.</summary>
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
                    long peak = 0;
                    while (Vita3kPaths.EmulatorRunning())
                    {
                        peak = Math.Max(peak, Vita3kPaths.EmulatorPeakBytes());
                        System.Threading.Thread.Sleep(500);
                    }
                    Log.Info("watcher: Vita3K is gone after " + (int)(DateTime.UtcNow - started).TotalSeconds
                             + "s - this is the end of the program, whatever the host does or does not say");

                    // WHAT THE EMULATOR ITSELF NEEDED, so the next launch of this game reserves it
                    // next to the RAM disk instead of guessing.
                    if (peak > 0) Vita3kWorkspace.RememberEmulatorPeak(layout, titleId, (int)(peak / (1024 * 1024)));

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
