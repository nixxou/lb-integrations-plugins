// FCEUX integration for LaunchBox / LiteBox - the NES, Famicom and Famicom Disk System emulator (TASEmulators/fceux).
//
// Claim the emulator (fceux64.exe, the build installed; fceux.exe, the 32-bit one LaunchBox's own FCEUX row names), install
// and update its latest release (FceuxBuild), describe it to the host's catalogue for every platform it runs
// (FceuxSystems), and at each launch: the Famicom Disk System's BIOS when the game is a disk (FceuxFirmware), the game's
// platform's save folders, the screen and the pack's hotkeys in fceux.cfg (FceuxLaunch, FceuxSavePaths, FceuxHotkeys).
//
// ONE EXECUTABLE, SEVERAL SYSTEMS - and no system to choose: FCEUX tells a cartridge from a disk by the file's content
// (fceu.cpp FCEUI_LoadGameVirtual), so no platform row needs a line. The system the plugin works out (FceuxSystems.SystemFor:
// the file's extension, else its platform's) decides the BIOS, the folders and later the save ids.
//
// NO RETROACHIEVEMENTS: FCEUX has none.
//
// Like every plugin here, this talks ONLY to the public SDK, and every entry point is defensive: the host swallows
// exceptions silently, so we catch, log and degrade instead.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using LbIntegrations.Saves;
using Unbroken.LaunchBox.Plugins.Data;
using LbIntegrations.Catalog;
using LbIntegrations.Lbip;

namespace LbIntegrations.Fceux
{
    public partial class FceuxPlugin : EmulatorPlugin, ISystemEventsPlugin, ILbCatalogSource
    {
        /// <summary>Nothing: the host appends the game's path ("fceux64.exe &lt;rom&gt;"); FCEUX needs no switch to run it.</summary>
        private const string DefaultCommandLine = "";

        internal const string PackName = "Nixx-FCEUX";

        public FceuxPlugin()
        {
            // First: the save front desk registers this plugin for the others (Shared.Saves SaveDesk).
            _desk = new SaveDesk(this, this, SaveEmu, SaveLog);
            Log.Info("plugin constructed, assembly " + typeof(FceuxPlugin).Assembly.Location);
            LbipLog.Use(Log.Info, Log.Warn, Log.Disabled, () => Log.Tracing);
            LbipRowInjection.Install("com.nixxou.lbip.fceux", MetadataRows());
        }

        /// <summary>LaunchBox's metadata has an FCEUX row (fceux.exe, the NES alone); this pack publishes its own beside it,
        /// with every platform. AutoExtract is false: FCEUX opens archives itself, and the ROM inside names its saves.</summary>
        private static IEnumerable<LbCatalogEmulator> MetadataRows()
        {
            var row = new LbCatalogEmulator
            {
                Name = PackName,
                CommandLine = DefaultCommandLine,
                ApplicableFileExtensions = FceuxSystems.EveryExtension(),
                Url = FceuxBuild.Home,
                BinaryFileName = FceuxLaunch.ExecutableName,
                AutoExtract = false,
            };
            foreach (var p in FceuxSystems.Platforms)
                row.Platforms.Add(new LbCatalogPlatform
                {
                    Platform = p.Platform, ApplicableFileExtensions = FceuxSystems.AllExtensions(p), Recommended = false,
                    RequiredBiosFile = p.System == FceuxSystems.Fds.Key ? FceuxSystems.Fds.Bios : null,
                });
            yield return row;
        }

        public override string EmulatorName => PackName;

        public IEnumerable<LbCatalogEmulator> EmulatorRows() => MetadataRows();

        public void OnEventRaised(string eventType)
        {
            try
            {
                if (eventType != SystemEventTypes.PluginInitialized
                    && eventType != SystemEventTypes.LaunchBoxStartupCompleted
                    && eventType != SystemEventTypes.BigBoxStartupCompleted) return;
                Log.Info("host event \"" + eventType + "\"");
                LbipRowInjection.Install("com.nixxou.lbip.fceux", MetadataRows());
            }
            catch (Exception ex) { Log.Warn("OnEventRaised", ex); }
        }

        // ── claiming ─────────────────────────────────────────────────────────

        private static string _lastClaimed;

        /// <summary>By the file name of the entry's ApplicationPath: fceux64.exe or fceux.exe. No other emulator of the pack
        /// nor of LaunchBox's metadata ships either name (the Qt build is qfceux.exe; FCE Ultra was fceu.exe).</summary>
        public override IEnumerable<IEmulator> GetApplicableEmulators(IEnumerable<IEmulator> emulators)
        {
            var claimed = new List<IEmulator>();
            if (emulators == null) return claimed;
            foreach (var emu in emulators)
            {
                if (emu == null) continue;
                string path;
                try { path = emu.ApplicationPath; } catch { continue; }
                if (!FceuxLaunch.IsFceuxExe(path)) continue;
                claimed.Add(emu);
                EnsureHotkeyScripts(emu);
                AddMissingPlatforms(emu, onlyKnown: true);
            }
            var signature = string.Join("|", claimed.Select(e => { try { return e.Title; } catch { return "?"; } }).OrderBy(t => t));
            if (signature != _lastClaimed)
            {
                _lastClaimed = signature;
                Log.Info("GetApplicableEmulators: claimed " + claimed.Count + " emulator(s)");
            }
            return claimed;
        }

        private static void EnsureHotkeyScripts(IEmulator emu)
        {
            if (!LbIntegrations.Compat.Compat.On("emulatorFields")) return;   // the Compatibility page
            try
            {
                var exeName = Path.GetFileName((Safe(() => emu.ApplicationPath) ?? FceuxLaunch.ExecutableName).Trim().Trim('"'));
                if (!FceuxLaunch.IsFceuxExe(exeName)) exeName = FceuxLaunch.ExecutableName;
                var old = FceuxAhk.Superseded(exeName);
                var set = new List<string>();
                if (Fill(() => emu.AutoHotkeyScript, v => emu.AutoHotkeyScript = v, FceuxAhk.Running(exeName), old)) set.Add("running");
                if (Fill(() => emu.ExitAutoHotkeyScript, v => emu.ExitAutoHotkeyScript = v, FceuxAhk.Exit(exeName), old)) set.Add("exit");
                if (Fill(() => emu.SaveStateAutoHotkeyScript, v => emu.SaveStateAutoHotkeyScript = v, FceuxAhk.SaveState(exeName), old)) set.Add("save");
                if (Fill(() => emu.LoadStateAutoHotkeyScript, v => emu.LoadStateAutoHotkeyScript = v, FceuxAhk.LoadState(exeName), old)) set.Add("load");
                if (set.Count > 0) Log.Info("hotkey scripts set: " + string.Join(", ", set));
            }
            catch (Exception ex) { Log.Warn("could not describe the hotkeys on the emulator entry", ex); }
        }

        /// <summary>A script set when empty - or when it is, word for word, one an earlier build of this plugin wrote
        /// (FceuxAhk.Superseded). One the user changed is never touched.</summary>
        private static bool Fill(Func<string> get, Action<string> set, string value, string[] superseded)
        {
            try
            {
                var now = get();
                string Norm(string s) => (s ?? "").Replace("\r\n", "\n").Trim();
                bool ours = superseded.Any(old => Norm(old) == Norm(now));
                if (!string.IsNullOrWhiteSpace(now) && !ours) return false;
                if (Norm(now) == Norm(value)) return false;
                set(value);
                return true;
            }
            catch { return false; }
        }

        private static readonly HashSet<string> Completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The platforms FCEUX runs that the entry lacks, ADDED - a row that is there is never removed nor unticked,
        /// whatever its name (Mehdi, 30/09: custom platforms exist). LaunchBox's own FCEUX row gives an entry the NES alone:
        /// a claimed entry gets the Famicom Disk System and the others the LIBRARY HAS (<paramref name="onlyKnown"/>: a row
        /// for a platform the library does not have would be noise on a user's entry), in memory only - nothing saved here,
        /// as Flycast does (FlycastAssociation). The default of a platform only where no emulator of the library is yet.
        /// Never beside a row being typed (a blank one). Once per entry. Returns the platforms added.</summary>
        internal static List<string> AddMissingPlatforms(IEmulator emu, bool onlyKnown)
        {
            var added = new List<string>();
            if (emu == null || !LbIntegrations.Compat.Compat.On("emulatorFields")) return added;
            try
            {
                var id = Safe(() => emu.Id) ?? "";
                lock (Completed) if (onlyKnown && id.Length > 0 && !Completed.Add(id)) return added;
                var rows = emu.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
                if (rows.Any(r => string.IsNullOrWhiteSpace(Safe(() => r.Platform))))
                {
                    lock (Completed) Completed.Remove(id);
                    return added;
                }
                var have = new HashSet<string>(rows.Select(r => (Safe(() => r.Platform) ?? "").Trim()), StringComparer.OrdinalIgnoreCase);
                var dm = PluginHelper.DataManager;
                HashSet<string> library = null;
                if (onlyKnown)
                {
                    library = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    try { foreach (var p in dm?.GetAllPlatforms() ?? Array.Empty<IPlatform>()) { var n = Safe(() => p.Name); if (!string.IsNullOrWhiteSpace(n)) library.Add(n.Trim()); } }
                    catch { }
                }
                var taken = DefaultsTaken(dm);
                foreach (var p in FceuxSystems.Platforms)
                {
                    if (have.Contains(p.Platform) || (library != null && !library.Contains(p.Platform))) continue;
                    var row = emu.AddNewEmulatorPlatform();
                    if (row == null) continue;
                    row.Platform = p.Platform;
                    row.IsDefault = !taken.Contains(p.Platform);
                    added.Add(p.Platform + (row.IsDefault ? " (default)" : ""));
                }
                if (added.Count > 0) Log.Info("\"" + Safe(() => emu.Title) + "\": platform(s) added" + (onlyKnown ? " in memory" : "") + " - " + string.Join(", ", added));
            }
            catch (Exception ex) { Log.Warn("could not complete the emulator's platforms", ex); }
            return added;
        }

        /// <summary>The platforms some emulator of the library is already the default for.</summary>
        private static HashSet<string> DefaultsTaken(IDataManager dm)
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var e in dm?.GetAllEmulators() ?? Array.Empty<IEmulator>())
                    foreach (var r in e.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>())
                        try { if (r.IsDefault && !string.IsNullOrEmpty(r.Platform)) taken.Add(r.Platform.Trim()); } catch { }
            }
            catch { }
            return taken;
        }

        public override EmulatorSupportResponse IsPlatformSupported(string platform)
        {
            bool supported = FceuxSystems.Platform(platform) != null;
            // Never recommended (Mehdi, 17/10): LaunchBox pushes a recommended emulator - in the import wizard too, not installed yet.
            return new EmulatorSupportResponse(supported, false);
        }

        // ── versions ─────────────────────────────────────────────────────────

        public override string GetCurrentVersion(string applicationPath) => FceuxBuild.InstalledVersion(ResolveFullPath(applicationPath));

        public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions()
        {
            var r = FceuxBuild.Latest();
            if (r == null) return null;
            return new List<EmulatorControllerVersion> { new EmulatorControllerVersion(r.Url, r.Version, r.FileName) };
        }

        /// <summary>THE DOWNLOAD BUTTON OF ADD EMULATOR IS THIS (DemulPlugin, measured 24/09), asked with an EMPTY path: true
        /// (the button shows only then). An install that is not ours (no stamp): false - it is the user's. Ours and older: true.</summary>
        public override bool IsUpdateAvailable(string emulatorAppPath, out EmulatorControllerVersion version)
        {
            version = null;
            try
            {
                if (string.IsNullOrWhiteSpace(emulatorAppPath))
                {
                    version = GetInstallableVersions()?.FirstOrDefault();
                    return true;
                }
                var current = GetCurrentVersion(emulatorAppPath);
                if (current == null) return false;
                version = GetInstallableVersions()?.FirstOrDefault();
                return version != null && FceuxBuild.IsNewer(version.Label, current);
            }
            catch (Exception ex) { Log.Warn("update check failed", ex); return false; }
        }

        // ── installation ─────────────────────────────────────────────────────

        public override EmulatorInstallResponse InstallEmulator(InstallEmulatorArgs args)
        {
            string archive = null;
            try
            {
                var release = FceuxBuild.Latest();
                if (release == null) return new EmulatorInstallResponse("Couldn't read FCEUX's latest release on GitHub. Try again later.");

                bool reinstall = args?.ExistingEmulator != null;
                string targetDir = reinstall
                    ? Path.GetDirectoryName(ResolveFullPath(args.ExistingEmulator.ApplicationPath))
                    : Path.Combine(LaunchBoxRoot(), "Emulators", PackName);
                if (string.IsNullOrWhiteSpace(targetDir)) return new EmulatorInstallResponse("Couldn't work out where to install FCEUX.");
                var exe = Path.Combine(targetDir, FceuxLaunch.ExecutableName);
                if (FceuxLaunch.Running(exe)) return new EmulatorInstallResponse("FCEUX is running from " + targetDir + ": close it, then update again.");
                if (!FceuxCfg.Representable(targetDir))
                    return new EmulatorInstallResponse("FCEUX cannot run from " + targetDir + ": its folder names hold characters outside Windows' ANSI code page, "
                                                       + "which FCEUX cannot read. Install LaunchBox in a folder with plain letters.");

                Report(args, "Downloading FCEUX " + release.Version + "...", 0);
                archive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                FceuxBuild.Fetch(release, archive, p => Report(args, "Downloading FCEUX " + release.Version + "...", p),
                    () => { try { return args?.ShouldCancelFunc?.Invoke() ?? false; } catch { return false; } });

                Report(args, "Extracting FCEUX...", null);
                Archives.ExtractOver(archive, targetDir);
                if (!File.Exists(exe)) return new EmulatorInstallResponse("FCEUX was extracted to " + targetDir + " but " + FceuxLaunch.ExecutableName + " is not there.");
                FceuxBuild.Stamp(targetDir, release);
                Log.Info("installed FCEUX " + release.Version + " to " + targetDir);
                // Its saves, states, screenshots and cheats in folders of its own, per platform (FceuxSavePaths).
                FceuxSavePaths.Ensure(exe);

                // The disk system's BIOS, when the user already has it - see FceuxFirmware.
                Report(args, "Looking for the Famicom Disk System BIOS...", null);
                var bios = FceuxFirmware.Ensure(exe, m => Report(args, m, null));
                var note = bios.RightSize ? " The Famicom Disk System BIOS is there." : " The Famicom Disk System BIOS (disksys.rom) was not found: disk games need it beside fceux64.exe.";

                if (reinstall)
                {
                    try { args.ExistingEmulator.ApplicationPath = MakeRelativeToLaunchBox(exe); } catch { }
                    AddMissingPlatforms(args.ExistingEmulator, onlyKnown: false);
                    try { PluginHelper.DataManager?.Save(false); } catch (Exception ex) { Log.Warn("data manager save failed", ex); }
                    return new EmulatorInstallResponse(args.ExistingEmulator, "FCEUX updated to " + release.Version + "." + note);
                }
                if (args != null && !args.ShouldCreateEmulator)
                    return new EmulatorInstallResponse("FCEUX was installed to " + targetDir + ", but no emulator entry was requested." + note);

                var created = CreateEmulator(exe);
                if (created == null)
                    return new EmulatorInstallResponse("FCEUX was installed to " + targetDir + " but the emulator entry couldn't be created (no data manager available).");
                return new EmulatorInstallResponse(created, "FCEUX " + release.Version + " installed." + note);
            }
            catch (OperationCanceledException) { return new EmulatorInstallResponse("Installation cancelled."); }
            catch (Exception ex)
            {
                Log.Warn("install failed", ex);
                return new EmulatorInstallResponse("Failed to install FCEUX: " + ex.Message);
            }
            finally
            {
                try { if (archive != null && File.Exists(archive)) File.Delete(archive); } catch { }
            }
        }

        /// <summary>The emulator with a row for each platform FCEUX runs. The default of a platform only when no emulator of
        /// the library is yet - an existing choice is the user's (never-touch-existing-platform-rows).</summary>
        private static IEmulator CreateEmulator(string exePath)
        {
            var dm = PluginHelper.DataManager;
            if (dm == null) return null;
            var emu = dm.AddNewEmulator();
            emu.Title = PackName;
            emu.ApplicationPath = MakeRelativeToLaunchBox(exePath);
            emu.CommandLine = DefaultCommandLine;
            EnsureHotkeyScripts(emu);
            // DefaultPlatform IS NOT SET - see XeniaPlugin.CreateEmulator. IsDefault on the row is what counts.
            var added = AddMissingPlatforms(emu, onlyKnown: false);
            try { dm.Save(false); } catch (Exception ex) { Log.Warn("data manager save failed", ex); }
            Log.Info("created emulator entry \"" + PackName + "\" -> " + emu.ApplicationPath + ", platforms " + string.Join(", ", added));
            return emu;
        }

        // ── BIOS ─────────────────────────────────────────────────────────────

        /// <summary>The disk system's disksys.rom, beside fceux64.exe: required on the Famicom Disk System, optional where a
        /// .fds can be among the cartridges (the NES, the Famicom).</summary>
        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string platform)
        {
            var p = FceuxSystems.Platform(platform);
            if (p == null) return Array.Empty<EmulatorBiosFile>();
            bool disk = p.System == FceuxSystems.Fds.Key;
            if (!disk && !p.ByExtension.Values.Contains(FceuxSystems.Fds.Key)) return Array.Empty<EmulatorBiosFile>();
            var aliases = FceuxSystems.Fds.BiosAliases.Length > 0 ? " (also found as " + string.Join(", ", FceuxSystems.Fds.BiosAliases) + ")" : "";
            return new[]
            {
                new EmulatorBiosFile(".", FceuxSystems.Fds.Bios, disk,
                    "Famicom Disk System BIOS, 8 192 bytes - " + (disk ? "required" : "only for the .fds disk games") + aliases, null, null),
            };
        }

        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string emulatorApplicationPath, string platform, string commandLine)
            => GetBiosFilesForPlatform(platform);

        public override RetroAchievementSupportResponse SupportsRetroAchievements(string emulatorApplicationPath)
            => new RetroAchievementSupportResponse(isSupported: false);

        // ── launch ───────────────────────────────────────────────────────────

        public override PrepareForLaunchResponse PrepareEmulatorForLaunch(PrepareForLaunchArgs args)
            => LbipLaunchGate.Run(PackName, args, PrepareCore);

        private PrepareForLaunchResponse PrepareCore(PrepareForLaunchArgs args)
        {
            string exe = null, gameId = null, rom = null, platform = null, title = null;
            try { exe = ResolveFullPath(args?.EmulatorBeingLaunched?.ApplicationPath); } catch { }
            try { gameId = args?.GameBeingLaunched?.Id; } catch { }
            try { rom = ResolveFullPath(args?.GameBeingLaunched?.ApplicationPath); } catch { }
            try { platform = args?.GameBeingLaunched?.Platform; } catch { }
            try { title = args?.GameBeingLaunched?.Title; } catch { }
            if (!FceuxLaunch.IsFceuxExe(exe) || !File.Exists(exe)) return new PrepareForLaunchResponse(success: true);
            bool running = FceuxLaunch.Running(exe);
            if (running) Log.Warn("FCEUX is already running from " + Path.GetDirectoryName(exe) + " - what is written now is lost when it quits");
            var caption = PackName + " - " + (title ?? Path.GetFileName(rom ?? ""));

            // The file FCEUX is given: the game, or the .unf of an archive unpacked for it.
            string played = rom;
            if (!string.IsNullOrEmpty(rom))
            {
                played = FceuxLaunch.RomFor(rom, exe, out var problem);
                if (played == null)
                {
                    Log.Info("launch refused: " + problem);
                    LbipNotice.Show(caption, "This game cannot be started: " + problem);
                    return new PrepareForLaunchResponse(success: false);
                }
                if (!FceuxCfg.Representable(played))
                {
                    Log.Info("launch refused: " + played + " is not in the ANSI code page");
                    LbipNotice.Show(caption, "FCEUX cannot open this file: its path holds characters outside Windows' ANSI code page, and FCEUX "
                                    + "reads only those (it would stop on an \"Error opening\" box).\n\n" + played
                                    + "\n\nRename the file or its folders with plain letters, then update the game's path in LaunchBox.");
                    return new PrepareForLaunchResponse(success: false);
                }
            }

            var system = FceuxSystems.SystemFor(platform, rom, Archives.IsArchive(rom) ? Archives.RomInside(rom) : null);
            Log.Info("launch: " + Path.GetFileName(rom ?? "") + " as " + system.Name + " (platform " + (platform ?? "none") + ")");

            // A disk needs its BIOS: without it FCEUX stops on a modal box WM_CLOSE cannot close (measured 18/10).
            if (system == FceuxSystems.Fds && !running)
            {
                var bios = FceuxFirmware.Ensure(exe);
                if (!bios.RightSize)
                {
                    Log.Info("launch refused: no " + FceuxSystems.Fds.Bios + " of 8 192 bytes in " + bios.Folder);
                    LbipNotice.Show(caption, "The Famicom Disk System needs its BIOS, " + FceuxSystems.Fds.Bios + " (8 192 bytes), and "
                                    + (bios.Present ? "the one there is not 8 192 bytes" : "none was found") + ".\n\nPut it in " + bios.Folder
                                    + "\n\n(or in RetroArch's system folder, also as " + string.Join(", ", FceuxSystems.Fds.BiosAliases) + " - it is copied at the next launch).");
                    return new PrepareForLaunchResponse(success: false);
                }
            }

            // fceux.cfg: this platform's folders, the screen, the hotkeys - never while FCEUX runs (it writes the file back).
            if (!running) FceuxLaunch.PrepareConfig(exe, gameId, platform, rom);

            var current = args?.CurrentCommandLine ?? "";
            var line = played == rom ? current.Trim() : FceuxLaunch.SwapRom(current, rom, played);
            Log.Info("launch: " + line);
            if (line != current.Trim()) return new PrepareForLaunchResponse(success: true) { NewCommandLine = line };
            return new PrepareForLaunchResponse(success: true);
        }

        // ── paths ────────────────────────────────────────────────────────────

        /// <summary>Worked out once: it follows from this dll's folder, which does not move.</summary>
        private static string LaunchBoxRoot() => _launchBoxRoot ??= LaunchBoxRootCore();

        private static string _launchBoxRoot;

        private static string LaunchBoxRootCore()
        {
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(Assembly.GetExecutingAssembly().Location));
                for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
                {
                    if (Directory.Exists(Path.Combine(dir, "Core")) && Directory.Exists(Path.Combine(dir, "Data"))) return dir;
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch (Exception ex) { Log.Warn("could not locate the LaunchBox root", ex); }
            try { return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..")); }
            catch { return Environment.CurrentDirectory; }
        }

        private static string MakeRelativeToLaunchBox(string fullPath)
        {
            try
            {
                var root = LaunchBoxRoot();
                var full = Path.GetFullPath(fullPath);
                if (!string.IsNullOrEmpty(root) && full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
                    return Path.GetRelativePath(root, full);
                return full;
            }
            catch { return fullPath; }
        }

        internal static string ResolveFullPathForUi(string maybeRelative) => ResolveFullPath(maybeRelative);

        private static string ResolveFullPath(string maybeRelative)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(maybeRelative)) return maybeRelative;
                return Path.IsPathRooted(maybeRelative) ? maybeRelative : Path.GetFullPath(Path.Combine(LaunchBoxRoot(), maybeRelative));
            }
            catch { return maybeRelative; }
        }

        private static void Report(InstallEmulatorArgs args, string message, double? progress)
        {
            try { args?.ReportProgressAction?.Invoke(message, null, progress); } catch { }
        }

        private static string Safe(Func<string> read)
        {
            try { return read(); } catch { return null; }
        }
    }
}
