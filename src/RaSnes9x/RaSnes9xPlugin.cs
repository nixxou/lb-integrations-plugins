// RASnes9x integration for LaunchBox / LiteBox - RetroAchievements' own Snes9x (Super Nintendo).
//
// Claim the emulator, install the one pinned build (RaSnes9xBuild), describe it to the host's catalogue, write the
// game's options into snes9x.conf at every launch (RaSnes9xLaunch), carry LaunchBox's RetroAchievements login into the
// toolset's file (RaSnes9xCheevos), and list the cartridge saves and the save states (RaSnes9xSaves). Modelled on the
// SUPER ZSNES plugin, the pack's other Super Nintendo one.
//
// Like every plugin here, this talks ONLY to the public SDK - no reference to any LaunchBox core assembly - so it
// behaves identically under LaunchBox and under LiteBox. Every entry point is defensive: the host swallows exceptions
// silently, which turns a bug into a feature that quietly does nothing, so we catch, log and degrade instead.

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

namespace LbIntegrations.RaSnes9x
{
    public partial class RaSnes9xPlugin : EmulatorPlugin, ISystemEventsPlugin, ILbCatalogSource
    {
        internal const string SnesPlatform = "Super Nintendo Entertainment System";

        /// <summary>Nothing: the host appends the game's path, which is all Snes9x needs ("RASnes9x.exe &lt;rom&gt;"). Full
        /// screen is snes9x.conf's, written at each launch - not -fullscreen, which would stay set when Snes9x saves its file.</summary>
        private const string DefaultCommandLine = "";

        /// <summary>What Snes9x opens (memmap.cpp: .zip, .jma and .msu1 - an MSU-1 pack, a zip of the ROM, its .msu and
        /// .pcm and a patch.bps - itself) and the ROM types of its Open dialog, plus .7z, which the plugin unpacks for it
        /// (RaSnes9xLaunch.RomFor).</summary>
        private const string Extensions = ".sfc; .smc; .swc; .fig; .bs; .st; .zip; .jma; .msu1; .7z";

        /// <summary>The name this pack publishes under: the catalogue row, the Add Emulator entry, the title of an emulator
        /// this plugin creates, and the folder it is installed into.</summary>
        internal const string PackName = "Nixx-RASnes9x";

        public RaSnes9xPlugin()
        {
            // First: the save front desk registers this plugin for the others (Shared.Saves SaveDesk).
            _desk = new SaveDesk(this, this, SaveEmu, SaveLog);
            Log.Info("plugin constructed, assembly " + typeof(RaSnes9xPlugin).Assembly.Location);
            LbipLog.Use(Log.Info, Log.Warn, Log.Disabled, () => Log.Tracing);
            // As early as possible: the patch only sees connections opened AFTER it is installed.
            LbipRowInjection.Install("com.nixxou.lbip.rasnes9x", MetadataRows());
        }

        /// <summary>LaunchBox's metadata has a Snes9x row but none for RASnes9x, and this pack publishes its own beside it.
        /// AutoExtract is false: Snes9x opens a .zip itself, and the name of the zip is the name of its save.</summary>
        private static IEnumerable<LbCatalogEmulator> MetadataRows()
        {
            yield return new LbCatalogEmulator
            {
                Name = PackName,
                CommandLine = DefaultCommandLine,
                ApplicableFileExtensions = Extensions,
                Url = RaSnes9xBuild.Home,
                BinaryFileName = RaSnes9xLaunch.ExecutableName,
                AutoExtract = false,
                Platforms =
                {
                    new LbCatalogPlatform { Platform = SnesPlatform, ApplicableFileExtensions = Extensions, Recommended = true },
                },
            };
        }

        public override string EmulatorName => PackName;

        public IEnumerable<LbCatalogEmulator> EmulatorRows() => MetadataRows();

        /// <summary>The metadata patch tried again once the host is up - the constructor may run before Microsoft.Data.Sqlite
        /// is loaded. Install is a no-op once it has succeeded.</summary>
        public void OnEventRaised(string eventType)
        {
            try
            {
                if (eventType != SystemEventTypes.PluginInitialized
                    && eventType != SystemEventTypes.LaunchBoxStartupCompleted
                    && eventType != SystemEventTypes.BigBoxStartupCompleted) return;
                Log.Info("host event \"" + eventType + "\"");
                LbipRowInjection.Install("com.nixxou.lbip.rasnes9x", MetadataRows());
            }
            catch (Exception ex) { Log.Warn("OnEventRaised", ex); }
        }

        // ── claiming ─────────────────────────────────────────────────────────

        private static string _lastClaimed;

        public override IEnumerable<IEmulator> GetApplicableEmulators(IEnumerable<IEmulator> emulators)
        {
            var claimed = new List<IEmulator>();
            if (emulators == null) return claimed;
            foreach (var emu in emulators)
            {
                if (emu == null) continue;
                string path;
                try { path = emu.ApplicationPath; } catch { continue; }
                if (!RaSnes9xLaunch.IsRaSnes9xExe(path)) continue;
                claimed.Add(emu);
                EnsureHotkeyScripts(emu);
            }
            var signature = string.Join("|", claimed.Select(e => { try { return e.Title; } catch { return "?"; } }).OrderBy(t => t));
            if (signature != _lastClaimed)
            {
                _lastClaimed = signature;
                Log.Info("GetApplicableEmulators: claimed " + claimed.Count + " emulator(s)");
            }
            return claimed;
        }

        /// <summary>The emulator's keys in the AutoHotkey fields the pause screens send. Only a blank field, or one still
        /// holding an earlier build's script, is filled: a script the user wrote is his answer to the same question.</summary>
        private static void EnsureHotkeyScripts(IEmulator emu)
        {
            if (!LbIntegrations.Compat.Compat.On("emulatorFields")) return;   // the Compatibility page
            try
            {
                var set = new List<string>();
                if (Fill(() => emu.AutoHotkeyScript, v => emu.AutoHotkeyScript = v, RaSnes9xAhk.Running)) set.Add("running");
                if (Fill(() => emu.ExitAutoHotkeyScript, v => emu.ExitAutoHotkeyScript = v, RaSnes9xAhk.Exit)) set.Add("exit");
                if (Fill(() => emu.SaveStateAutoHotkeyScript, v => emu.SaveStateAutoHotkeyScript = v, RaSnes9xAhk.SaveState)) set.Add("save");
                if (Fill(() => emu.LoadStateAutoHotkeyScript, v => emu.LoadStateAutoHotkeyScript = v, RaSnes9xAhk.LoadState)) set.Add("load");
                if (set.Count > 0) Log.Info("hotkey scripts set: " + string.Join(", ", set));
            }
            catch (Exception ex) { Log.Warn("could not describe the hotkeys on the emulator entry", ex); }
        }

        /// <summary>A script set when empty - or when it is, word for word, one an earlier build of this plugin wrote
        /// (RaSnes9xAhk.Superseded: Snes9x's Shift+F1 / F1, before the pack's keys of 07/10). One the user changed is never
        /// touched.</summary>
        private static bool Fill(Func<string> get, Action<string> set, string value)
        {
            try
            {
                var now = get();
                string Norm(string s) => (s ?? "").Replace("\r\n", "\n").Trim();
                bool ours = RaSnes9xAhk.Superseded.Any(old => Norm(old) == Norm(now));
                if (!string.IsNullOrWhiteSpace(now) && !ours) return false;
                if (Norm(now) == Norm(value)) return false;
                set(value);
                return true;
            }
            catch { return false; }
        }

        public override EmulatorSupportResponse IsPlatformSupported(string platform)
        {
            bool supported = string.Equals((platform ?? "").Trim(), SnesPlatform, StringComparison.InvariantCultureIgnoreCase);
            Log.Verbose("IsPlatformSupported(\"" + platform + "\") -> " + supported);
            return new EmulatorSupportResponse(supported, supported);
        }

        // ── versions ─────────────────────────────────────────────────────────

        public override string GetCurrentVersion(string applicationPath) => RaSnes9xBuild.InstalledVersion(ResolveFullPath(applicationPath));

        /// <summary>One build, the pinned one. The identifier is its file name, the label its version.</summary>
        public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions()
            => new List<EmulatorControllerVersion> { new EmulatorControllerVersion(RaSnes9xBuild.FileName, RaSnes9xBuild.Version, RaSnes9xBuild.FileName) };

        /// <summary>An empty path is the Add Emulator window's Download button: yes. Otherwise the pinned build against the
        /// one on disk; an unknown build answers no rather than nagging.</summary>
        public override bool IsUpdateAvailable(string emulatorAppPath, out EmulatorControllerVersion version)
        {
            version = GetInstallableVersions().First();
            try
            {
                if (string.IsNullOrWhiteSpace(emulatorAppPath)) return true;
                var current = GetCurrentVersion(emulatorAppPath);
                return current != null && RaSnes9xBuild.IsNewer(RaSnes9xBuild.Version, current);
            }
            catch (Exception ex) { Log.Warn("update check failed", ex); return false; }
        }

        // ── installation ─────────────────────────────────────────────────────

        public override EmulatorInstallResponse InstallEmulator(InstallEmulatorArgs args)
        {
            string archive = null;
            try
            {
                bool reinstall = args?.ExistingEmulator != null;
                string targetDir = reinstall
                    ? Path.GetDirectoryName(ResolveFullPath(args.ExistingEmulator.ApplicationPath))
                    : Path.Combine(LaunchBoxRoot(), "Emulators", PackName);
                if (string.IsNullOrWhiteSpace(targetDir)) return new EmulatorInstallResponse("Couldn't work out where to install RASnes9x.");
                if (RaSnes9xLaunch.Running(Path.Combine(targetDir, RaSnes9xLaunch.ExecutableName)))
                    return new EmulatorInstallResponse("RASnes9x is running from " + targetDir + ": close it, then update again.");

                Report(args, "Downloading RASnes9x " + RaSnes9xBuild.Version + "...", 0);
                archive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                RaSnes9xBuild.Fetch(archive, p => Report(args, "Downloading RASnes9x " + RaSnes9xBuild.Version + "...", p),
                    () => { try { return args?.ShouldCancelFunc?.Invoke() ?? false; } catch { return false; } });

                Report(args, "Extracting RASnes9x...", null);
                Archives.ExtractOver(archive, targetDir);
                var exe = Path.Combine(targetDir, RaSnes9xLaunch.ExecutableName);
                if (!File.Exists(exe))
                    return new EmulatorInstallResponse("RASnes9x was extracted to " + targetDir + " but " + RaSnes9xLaunch.ExecutableName
                                                       + " is not there - a virus scanner may have quarantined it.");
                RaSnes9xBuild.Stamp(targetDir);
                Log.Info("installed " + RaSnes9xBuild.Version + " to " + targetDir);

                // RetroAchievements' toolset, fetched as the emulator would at its first start - see RaSnes9xToolset.
                Report(args, "Downloading RetroAchievements' toolset...", null);
                var toolset = RaSnes9xToolset.Ensure(exe);
                string note = toolset == null ? "" : " RetroAchievements' toolset could not be fetched (" + toolset + "): RASnes9x offers to download it when it starts.";
                if (reinstall)
                {
                    try { args.ExistingEmulator.ApplicationPath = MakeRelativeToLaunchBox(exe); } catch { }
                    return new EmulatorInstallResponse(args.ExistingEmulator, "RASnes9x updated." + note);
                }
                if (args != null && !args.ShouldCreateEmulator)
                    return new EmulatorInstallResponse("RASnes9x was installed to " + targetDir + ", but no emulator entry was requested." + note);

                var created = CreateEmulator(exe);
                if (created == null)
                    return new EmulatorInstallResponse("RASnes9x was installed to " + targetDir + " but the emulator entry couldn't be created (no data manager available).");
                return new EmulatorInstallResponse(created, "RASnes9x installed." + note);
            }
            catch (OperationCanceledException) { return new EmulatorInstallResponse("Installation cancelled."); }
            catch (Exception ex)
            {
                Log.Warn("install failed", ex);
                return new EmulatorInstallResponse("Failed to install RASnes9x: " + ex.Message);
            }
            finally
            {
                try { if (archive != null && File.Exists(archive)) File.Delete(archive); } catch { }
            }
        }

        private static IEmulator CreateEmulator(string exePath)
        {
            var dm = PluginHelper.DataManager;
            if (dm == null) return null;

            // The default of the platform only when no emulator of the library is yet - an existing choice is the user's.
            bool taken = false;
            try
            {
                taken = (dm.GetAllEmulators() ?? Array.Empty<IEmulator>()).Any(e => (e.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>())
                            .Any(r => r.IsDefault && string.Equals(r.Platform, SnesPlatform, StringComparison.OrdinalIgnoreCase)));
            }
            catch { }
            var emu = dm.AddNewEmulator();
            emu.Title = PackName;
            emu.ApplicationPath = MakeRelativeToLaunchBox(exePath);
            emu.CommandLine = DefaultCommandLine;
            EnsureHotkeyScripts(emu);

            // DefaultPlatform IS NOT SET - see the note in XeniaPlugin.CreateEmulator: the Edit Emulator window grows a
            // duplicate platform row from it. IsDefault on the row says "the default for that platform".
            var platform = emu.AddNewEmulatorPlatform();
            platform.Platform = SnesPlatform;
            platform.IsDefault = !taken;

            try { dm.Save(false); } catch (Exception ex) { Log.Warn("data manager save failed", ex); }
            Log.Info("created emulator entry \"" + PackName + "\" -> " + emu.ApplicationPath);
            return emu;
        }

        // ── BIOS ─────────────────────────────────────────────────────────────

        /// <summary>None for the Super Nintendo: Snes9x emulates its add-on chips (DSP, Super FX, SA-1...) without their ROMs.
        /// Only the Satellaview needs one (BS-X.bin), and that platform is not this plugin's.</summary>
        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string platform) => Array.Empty<EmulatorBiosFile>();

        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string emulatorApplicationPath, string platform, string commandLine)
            => GetBiosFilesForPlatform(platform);

        // ── RetroAchievements ────────────────────────────────────────────────

        public override RetroAchievementSupportResponse SupportsRetroAchievements(string emulatorApplicationPath)
        {
            try
            {
                var exe = ResolveFullPath(emulatorApplicationPath);
                var (user, hasToken, hardcore) = RaSnes9xCheevos.Read(exe);
                return new RetroAchievementSupportResponse(isSupported: true, isEnabled: RaSnes9xCheevos.HasToolset(exe), isHardcore: hardcore,
                                                           username: user, token: hasToken ? "" : null);   // whether a token is there, never its value
            }
            catch (Exception ex)
            {
                Log.Warn("could not read the RetroAchievements login", ex);
                return new RetroAchievementSupportResponse(isSupported: true);
            }
        }

        /// <summary>The login written into RAPrefs_RASnes9X.cfg - see RaSnes9xCheevos.</summary>
        public override PluginResponse InjectRetroAchievementsCredentials(InjectRetroAchievementsCredentialsArgs args)
        {
            try
            {
                if (args == null) return new PluginResponse(false, "No credentials supplied.");
                var exe = ResolveFullPath(args.EmulatorApplicationPath);
                if (!RaSnes9xLaunch.IsRaSnes9xExe(exe)) return new PluginResponse(false, "This emulator is not RASnes9x.");
                var why = RaSnes9xCheevos.Write(exe, args.Username, args.Token, args.HardcoreEnabled);
                return why == null ? new PluginResponse(true) : new PluginResponse(false, why);
            }
            catch (Exception ex)
            {
                Log.Warn("could not write the RetroAchievements login", ex);
                return new PluginResponse(false, "Could not write RASnes9x's RetroAchievements login: " + ex.Message);
            }
        }

        // ── launch ───────────────────────────────────────────────────────────

        /// <summary>One launch at a time for this plugin - see LbipLaunchGate.</summary>
        public override PrepareForLaunchResponse PrepareEmulatorForLaunch(PrepareForLaunchArgs args)
            => LbipLaunchGate.Run(PackName, args, PrepareCore);

        /// <summary>The login, the game's options, a .7z unpacked. Only a .7z that cannot be played fails the launch: the
        /// rest degrades to the files as they were.</summary>
        private PrepareForLaunchResponse PrepareCore(PrepareForLaunchArgs args)
        {
            string exe = null, gameId = null, rom = null, title = null;
            try { exe = ResolveFullPath(args?.EmulatorBeingLaunched?.ApplicationPath); } catch { }
            try { gameId = args?.GameBeingLaunched?.Id; } catch { }
            try { rom = ResolveFullPath(args?.GameBeingLaunched?.ApplicationPath); } catch { }
            try { title = args?.GameBeingLaunched?.Title; } catch { }
            if (!RaSnes9xLaunch.IsRaSnes9xExe(exe) || !File.Exists(exe)) return new PrepareForLaunchResponse(success: true);

            if (RaSnes9xLaunch.Running(exe))
                Log.Warn("RASnes9x is already running from " + Path.GetDirectoryName(exe) + " - what is written now is lost when it quits");

            // THE LOGIN LaunchBox has, and its hardcore switch on the emulator - as Flycast's.
            try
            {
                if (args?.RetroAchievementCredentials != null)
                {
                    var creds = args.RetroAchievementCredentials.Value;
                    bool hardcore = false;
                    try { hardcore = args.EmulatorBeingLaunched?.EnableHardcoreAchievements ?? false; } catch { }
                    var why = RaSnes9xCheevos.Write(exe, creds.Username, creds.Token, hardcore);
                    if (why != null) Log.Warn("RetroAchievements login not applied: " + why + " - launching anyway");
                }
            }
            catch (Exception ex) { Log.Warn("RetroAchievements login at launch", ex); }

            // RetroAchievements' toolset brought to the latest, as the emulator would ask to - see RaSnes9xToolset.
            var toolset = RaSnes9xToolset.Ensure(exe, always: false);
            if (toolset != null) Log.Info("toolset not updated before the launch: " + toolset + " - the emulator asks itself");

            RaSnes9xLaunch.PrepareConf(exe, gameId, rom);
            // A PNG beside each state (Zelda.000 -> Zelda.000.png), from the picture Snes9x puts in it, once it has quit.
            RaSnes9xStateThumbs.AfterSession(exe, rom);

            // A .7z: its ROM unpacked under the archive's name, the line pointed at it.
            if (!string.IsNullOrEmpty(rom) && string.Equals(Path.GetExtension(rom), ".7z", StringComparison.OrdinalIgnoreCase))
            {
                var played = RaSnes9xLaunch.RomFor(rom, out var problem);
                if (played == null)
                {
                    LbipNotice.Show(PackName + " - " + (title ?? Path.GetFileName(rom)), "This game cannot be started: " + problem);
                    return new PrepareForLaunchResponse(success: false);
                }
                var line = RaSnes9xLaunch.SwapRom(args?.CurrentCommandLine, rom, played);
                Log.Info("launch: " + line);
                return new PrepareForLaunchResponse(success: true) { NewCommandLine = line };
            }
            return new PrepareForLaunchResponse(success: true);
        }

        // ── paths ────────────────────────────────────────────────────────────

        /// <summary>Worked out once: it follows from this dll's folder, which does not move (10/10: walked up with Directory.Exists on

        /// every relative path, hundreds of times per Game Saves opening).</summary>

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
    }
}
