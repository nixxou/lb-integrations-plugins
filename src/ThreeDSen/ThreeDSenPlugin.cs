// 3dSen PC integration for LaunchBox / LiteBox - Geod Studio's NES emulator that turns games into 3D dioramas.
//
// Claim the emulator (by its exe, 3dSen.exe), set it up from the user's Steam install or the folder of their own copy -
// never a download, 3dSen is sold on Steam (ThreeDSenInstall, ThreeDSenSteam) - describe it to the host's catalogue, and at
// every launch bind the game to its 3D profile in rom.json and start "3dSen.exe -id=<profile>" with the screen's switches
// (ThreeDSenProfiles, ThreeDSenRomJson, ThreeDSenLaunch). Everything measured is in docs\3dsen.md.
//
// ONE SYSTEM, THE NES (docs\3dsen.md section 2, read in 3dSen 1.1.2's Assembly-CSharp): one C# core, iNES files only
// (Cartridge.LoadCart refuses anything not starting "NES\x1A"), no Famicom Disk System (no loader, no BIOS), no Vs.
// System nor PlayChoice nor Dendy. Its platforms: Nintendo Entertainment System - the Famicom is one of its alternate names
// in LaunchBox's base, not a platform - and LiteBox's "Nes - Super Mario Bros. Hacks" (iNES files too). No BIOS, no
// RetroAchievements (no RA code in 3dSen).
//
// SAVES: none in this first lot. 3dSen has no battery saves at all (the cartridge RAM is never written to a file,
// docs\3dsen.md section 5) and keeps its states in its own data folder, shared by every copy, outside the install
// (LocalLow\Geod Studio\3dSen, sections 4 and 6). Nothing is ever written beside the game - measured - so there is
// nothing to move there. The states' listing and restore come with the next lot.
//
// Like every plugin here, this talks ONLY to the public SDK, and every entry point is defensive: the host swallows
// exceptions silently, so we catch, log and degrade instead.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;
using LbIntegrations.Catalog;
using LbIntegrations.Lbip;

namespace LbIntegrations.ThreeDSen
{
    public class ThreeDSenPlugin : EmulatorPlugin, ISystemEventsPlugin, ILbCatalogSource
    {
        internal const string NesPlatform = "Nintendo Entertainment System";

        /// <summary>The platforms 3dSen runs (the header): the main one first, the one the defaults go by.</summary>
        internal static readonly string[] NesPlatforms = { NesPlatform, "Nes - Super Mario Bros. Hacks" };

        internal static bool IsNes(string platform)
            => NesPlatforms.Any(p => string.Equals(p, (platform ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>Nothing: the line is the plugin's at each launch (ThreeDSenLaunch.CommandLine) - the game is a profile id.</summary>
        private const string DefaultCommandLine = "";

        /// <summary>What 3dSen's own ROM browser offers (VRUIController.FlashAndRunGame: nes|zip|7z).</summary>
        private const string Extensions = ".nes; .zip; .7z";

        internal const string PackName = "Nixx-3DSen";

        private const string InjectionId = "com.nixxou.lbip.3dsen";

        public ThreeDSenPlugin()
        {
            Log.Info("plugin constructed, assembly " + typeof(ThreeDSenPlugin).Assembly.Location);
            LbipLog.Use(Log.Info, Log.Warn, Log.Disabled, () => Log.Tracing);
            LbipRowInjection.Install(InjectionId, MetadataRows());
        }

        /// <summary>The pack's row, beside LaunchBox's own (it has no 3dSen). AutoExtract false: 3dSen opens a .zip itself,
        /// and the plugin unpacks what it cannot play (ThreeDSenLaunch.RomFor).</summary>
        private static IEnumerable<LbCatalogEmulator> MetadataRows()
        {
            var row = new LbCatalogEmulator
            {
                Name = PackName,
                CommandLine = DefaultCommandLine,
                ApplicableFileExtensions = Extensions,
                Url = "https://store.steampowered.com/app/" + ThreeDSenSteam.AppId + "/",
                BinaryFileName = ThreeDSenLaunch.ExecutableName,
                AutoExtract = false,
            };
            foreach (var p in NesPlatforms) row.Platforms.Add(new LbCatalogPlatform { Platform = p, ApplicableFileExtensions = Extensions, Recommended = false });
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
                LbipRowInjection.Install(InjectionId, MetadataRows());
            }
            catch (Exception ex) { Log.Warn("OnEventRaised", ex); }
        }

        // ── claiming ─────────────────────────────────────────────────────────

        private static string _lastClaimed;

        /// <summary>By the exe's name: 3dSen.exe is no other emulator's (the VR edition is 3dSenVR.exe, not claimed).</summary>
        public override IEnumerable<IEmulator> GetApplicableEmulators(IEnumerable<IEmulator> emulators)
        {
            var claimed = new List<IEmulator>();
            if (emulators == null) return claimed;
            foreach (var emu in emulators)
            {
                if (emu == null) continue;
                string path;
                try { path = emu.ApplicationPath; } catch { continue; }
                if (!ThreeDSenLaunch.IsExe(path)) continue;
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

        private static void EnsureHotkeyScripts(IEmulator emu)
        {
            if (!LbIntegrations.Compat.Compat.On("emulatorFields")) return;   // the Compatibility page
            try
            {
                var set = new List<string>();
                if (Fill(() => emu.AutoHotkeyScript, v => emu.AutoHotkeyScript = v, ThreeDSenAhk.Running)) set.Add("running");
                if (Fill(() => emu.ExitAutoHotkeyScript, v => emu.ExitAutoHotkeyScript = v, ThreeDSenAhk.Exit)) set.Add("exit");
                if (Fill(() => emu.SaveStateAutoHotkeyScript, v => emu.SaveStateAutoHotkeyScript = v, ThreeDSenAhk.SaveState)) set.Add("save");
                if (Fill(() => emu.LoadStateAutoHotkeyScript, v => emu.LoadStateAutoHotkeyScript = v, ThreeDSenAhk.LoadState)) set.Add("load");
                if (set.Count > 0) Log.Info("hotkey scripts set: " + string.Join(", ", set));
            }
            catch (Exception ex) { Log.Warn("could not describe the hotkeys on the emulator entry", ex); }
        }

        /// <summary>A script set when empty - or when it is, word for word, one an earlier build wrote (ThreeDSenAhk.Superseded).
        /// One the user changed is never touched.</summary>
        private static bool Fill(Func<string> get, Action<string> set, string value)
        {
            try
            {
                var now = get();
                string Norm(string s) => (s ?? "").Replace("\r\n", "\n").Trim();
                bool ours = ThreeDSenAhk.Superseded.Any(old => Norm(old) == Norm(now));
                if (!string.IsNullOrWhiteSpace(now) && !ours) return false;
                if (Norm(now) == Norm(value)) return false;
                set(value);
                return true;
            }
            catch { return false; }
        }

        public override EmulatorSupportResponse IsPlatformSupported(string platform)
            // Never recommended (Mehdi, 17/10): LaunchBox pushes a recommended emulator - in the import wizard too, not installed yet.
            => new EmulatorSupportResponse(IsNes(platform), false);

        // ── versions ─────────────────────────────────────────────────────────

        /// <summary>3dSen's own version (its Nes3D.Info.Version), whoever installed it: nothing is ever installed BY the plugin.</summary>
        public override string GetCurrentVersion(string applicationPath) => ThreeDSenInstall.VersionOf(ResolveFullPath(applicationPath));

        private const string VersionId = "steam-or-folder";

        /// <summary>One entry: no build is offered, the Download button asks where 3dSen is (ThreeDSenInstall).</summary>
        public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions()
            => new List<EmulatorControllerVersion> { new EmulatorControllerVersion(VersionId, "3dSen (Steam or your copy)", ThreeDSenLaunch.ExecutableName) };

        /// <summary>THE DOWNLOAD BUTTON OF ADD EMULATOR IS THIS (DemulPlugin, measured 24/09): called with an EMPTY path, true
        /// shows it - that is where the question is asked. An install is never "outdated" for the plugin: Steam updates its
        /// own, the user's copy is the user's.</summary>
        public override bool IsUpdateAvailable(string emulatorAppPath, out EmulatorControllerVersion version)
        {
            version = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(emulatorAppPath)) return false;
                version = GetInstallableVersions().First();
                return true;
            }
            catch (Exception ex) { Log.Warn("update check failed", ex); return false; }
        }

        // ── installation: the question ───────────────────────────────────────

        public override EmulatorInstallResponse InstallEmulator(InstallEmulatorArgs args)
        {
            try
            {
                Report(args, "Looking for 3dSen...", null);
                var steam = ThreeDSenSteam.Read();
                Log.Info("install: Steam " + (steam.SteamRoot == null ? "not found" : "found, " + (steam.LoggedIn ? "logged in" : "not logged in")
                         + ", 3dSen " + (steam.Exe != null ? "installed" : steam.InstallDir != null ? "being installed (state " + steam.StateFlags + ")" : "not installed")));
                var (pick, folder) = ThreeDSenInstall.Ask(steam);
                string exe;
                switch (pick)
                {
                    case ThreeDSenInstall.Pick.UseSteam:
                        exe = steam.Exe;
                        if (exe == null) return new EmulatorInstallResponse("3dSen is not installed by Steam on this computer.");
                        break;
                    case ThreeDSenInstall.Pick.InstallWithSteam:
                        if (!ThreeDSenSteam.AskToInstall())
                            return new EmulatorInstallResponse("Steam could not be asked to install 3dSen. Install it from Steam, then click Download again.");
                        return new EmulatorInstallResponse("Steam was asked to install 3dSen" + (steam.LoggedIn ? "" : " (log in to Steam first)")
                                                           + ". When Steam has finished, click Download again and choose \"Use the Steam install\".");
                    case ThreeDSenInstall.Pick.Folder:
                        exe = ThreeDSenInstall.ExeIn(folder, out var problem);
                        if (exe == null) return new EmulatorInstallResponse("Not this folder: " + problem);
                        break;
                    default:
                        return new EmulatorInstallResponse("Cancelled: 3dSen was not set up.");
                }

                var version = ThreeDSenInstall.VersionOf(exe);
                Log.Info("install: " + (pick == ThreeDSenInstall.Pick.UseSteam ? "Steam's" : "the user's copy") + " " + exe + " (" + version + ")");
                if (args?.ExistingEmulator != null)
                {
                    try { args.ExistingEmulator.ApplicationPath = MakeRelativeToLaunchBox(exe); } catch { }
                    return new EmulatorInstallResponse(args.ExistingEmulator, "3dSen " + version + " - " + exe + ".");
                }
                if (args != null && !args.ShouldCreateEmulator)
                    return new EmulatorInstallResponse("3dSen " + version + " found at " + exe + ", but no emulator entry was requested.");
                var created = CreateEmulator(exe);
                if (created == null)
                    return new EmulatorInstallResponse("3dSen was found at " + exe + " but the emulator entry couldn't be created (no data manager available).");
                return new EmulatorInstallResponse(created, "3dSen " + version + " set up from " + exe + ".");
            }
            catch (Exception ex)
            {
                Log.Warn("install failed", ex);
                return new EmulatorInstallResponse("3dSen could not be set up: " + ex.Message);
            }
        }

        private static IEmulator CreateEmulator(string exePath)
        {
            var dm = PluginHelper.DataManager;
            if (dm == null) return null;
            // The default of the platform only when no emulator of the library is yet - an existing choice is the user's.
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var e in dm.GetAllEmulators() ?? Array.Empty<IEmulator>())
                    foreach (var r in e.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>())
                        try { if (r.IsDefault && !string.IsNullOrEmpty(r.Platform)) taken.Add(r.Platform); } catch { }
            }
            catch { }
            var emu = dm.AddNewEmulator();
            emu.Title = PackName;
            emu.ApplicationPath = MakeRelativeToLaunchBox(exePath);
            emu.CommandLine = DefaultCommandLine;
            EnsureHotkeyScripts(emu);
            // DefaultPlatform IS NOT SET - see XeniaPlugin.CreateEmulator. IsDefault on the row is what counts.
            foreach (var name in NesPlatforms)
            {
                var platform = emu.AddNewEmulatorPlatform();
                platform.Platform = name;
                platform.IsDefault = !taken.Contains(name);
            }
            try { dm.Save(false); } catch (Exception ex) { Log.Warn("data manager save failed", ex); }
            Log.Info("created emulator entry \"" + PackName + "\" -> " + emu.ApplicationPath);
            return emu;
        }

        // ── BIOS, RetroAchievements: none ────────────────────────────────────

        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string platform) => Array.Empty<EmulatorBiosFile>();

        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string emulatorApplicationPath, string platform, string commandLine)
            => GetBiosFilesForPlatform(platform);

        public override RetroAchievementSupportResponse SupportsRetroAchievements(string emulatorApplicationPath)
            => new RetroAchievementSupportResponse(isSupported: false);

        // ── launch ───────────────────────────────────────────────────────────

        public override PrepareForLaunchResponse PrepareEmulatorForLaunch(PrepareForLaunchArgs args)
            => LbipLaunchGate.Run(PackName, args, PrepareCore);

        private PrepareForLaunchResponse PrepareCore(PrepareForLaunchArgs args)
        {
            string exe = null, gameId = null, rom = null, title = null;
            try { exe = ResolveFullPath(args?.EmulatorBeingLaunched?.ApplicationPath); } catch { }
            try { gameId = args?.GameBeingLaunched?.Id; } catch { }
            try { rom = ResolveFullPath(args?.GameBeingLaunched?.ApplicationPath); } catch { }
            try { title = args?.GameBeingLaunched?.Title; } catch { }
            if (!ThreeDSenLaunch.IsExe(exe) || !File.Exists(exe)) return new PrepareForLaunchResponse(success: true);
            var refusal = Prepare(exe, gameId, title, rom, args?.CurrentCommandLine ?? "", out var line);
            if (refusal != null)
            {
                Log.Info("launch refused: " + refusal);
                LbipNotice.Show(PackName, refusal);
                return new PrepareForLaunchResponse(success: false);
            }
            Log.Info("launch: " + line);
            return new PrepareForLaunchResponse(success: true) { NewCommandLine = line };
        }

        /// <summary>The launch's work, host-free (the probe calls it): null and the line, or why it cannot be launched.</summary>
        internal static string Prepare(string exe, string gameId, string title, string rom, string current, out string line)
        {
            line = null;
            if (ThreeDSenLaunch.AnyRunning())
                return "3dSen is already running. Close it first: it writes its list of games when it quits, which would undo this launch.";
            if (string.IsNullOrWhiteSpace(rom) || !File.Exists(rom))
                return "The game's file is not there: " + (rom ?? "(none)") + ".";

            var data = ThreeDSenLaunch.DataDir();
            var profiles = ThreeDSenProfiles.All(data);
            ThreeDSenProfile profile;
            var chosen = ThreeDSenSettings.GameValue(gameId, ThreeDSenSettings.ProfileKey);
            if (chosen != null)
            {
                profile = profiles.FirstOrDefault(p => p.Id == chosen);
                if (profile == null)
                    return "The 3D profile chosen for " + (title ?? "this game") + " (id " + chosen + ") is not in 3dSen any more. "
                           + "Choose another: right-click the game > Nixx-3DSen : Options...";
            }
            else
            {
                profile = ThreeDSenProfiles.Match(profiles, title, rom);
                if (profile == null)
                    return "3dSen has no 3D profile for \"" + (title ?? Path.GetFileNameWithoutExtension(rom)) + "\" - it draws in 3D only the games it has a profile for. "
                           + "If it has one under another name, choose it: right-click the game > Nixx-3DSen : Options... (\"2D Mode\" plays any game flat).";
            }
            Log.Info("profile: " + profile + (chosen != null ? ", the game's own choice" : ", matched by its title"));

            var file = ThreeDSenLaunch.RomFor(exe, rom, out var problem);
            if (file == null) return problem;
            var written = ThreeDSenRomJson.SetRom(Path.Combine(data, ThreeDSenRomJson.FileName), profile.Id, profile.Title, file);
            if (written != null) return written;
            ThreeDSenInput.BindStateKeys(data);

            var switches = ThreeDSenLaunch.Switches(profile.Id, ThreeDSenSettings.For(gameId, "Screen"), ThreeDSenSettings.For(gameId, "WindowSize"));
            line = ThreeDSenLaunch.CommandLine(current, rom, switches);
            return null;
        }

        // ── paths ────────────────────────────────────────────────────────────

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

        /// <summary>Relative when under LaunchBox's folder; a Steam library elsewhere stays a full path.</summary>
        private static string MakeRelativeToLaunchBox(string fullPath)
        {
            try
            {
                var root = LaunchBoxRoot();
                var full = Path.GetFullPath(fullPath);
                if (!string.IsNullOrEmpty(root) && full.StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
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
