// ZiNc EX integration for LaunchBox / LiteBox - the launcher of ZiNc 1.1, the arcade emulator of Sony's ZN-1 / ZN-2 boards
// (Capcom, Taito FX-1, Raizing, Tecmo, Atlus, Konami GV, Namco System 11 / 12): 71 games.
//
// Claim ZiNc EX (ZiNc-EX.exe or ZiNc-EX-32bit.exe), install and update its GitHub release (ZincExBuild), describe it to
// the host's catalogue, and make each launch work (ZincExLaunch): tell which of ZiNc's games the romset is by its ROMs
// (ZincGames - MAME renamed most of them since ZiNc), give ZiNc EX a folder holding its zips under ZiNc's names
// (ZincRoms), point ZiNc EX's ROMs folder there for the launch (ZincExConfig) and put it back after (ZincExSession).
//
// Also: ZiNc EX's own per-game settings from the game's window (ZincExGameMenu, ZincExGameCfg), the import wizard kept to
// ZiNc's games (ZincExLbImport), and the high scores - cfg\<set>.cfg, measured on 16/10 (ZincExSaves).
//
// MEASURED: the ZiNc listings, the release's layout, ZiNc EX's sources (command line, settings, ROM check) and the CRCs of a
// MAME 0.288 folder on 15/10; launches, refusals and the cfg files on G:\LB1326 on 16/10. No RetroAchievements: ZiNc has
// none, and RetroAchievements does not cover these boards.
//
// Like every plugin here, this talks ONLY to the public SDK, and every entry point catches, logs and degrades - the host
// swallows exceptions silently.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;
using LbIntegrations.Catalog;
using LbIntegrations.Lbip;

namespace LbIntegrations.ZincEx
{
    public partial class ZincExPlugin : EmulatorPlugin, ISystemEventsPlugin, ILbCatalogSource
    {
        internal const string PackName = "Nixx-ZiNc-EX";

        /// <summary>LaunchBox's platform for these boards: there is none of ZN's own, they are imported as Arcade.</summary>
        internal const string ArcadePlatform = "Arcade";

        /// <summary>Empty: the whole line is written at launch (ZincExLaunch.CommandLine), from the game.</summary>
        private const string DefaultCommandLine = "";

        /// <summary>Romsets. ZiNc opens zips only (its unzip engine), and a whole one: AutoExtract stays off.</summary>
        private const string Extensions = ".zip";

        public ZincExPlugin()
        {
            // First: the save front desk registers this plugin for the others (Shared.Saves SaveDesk).
            _desk = new LbIntegrations.Saves.SaveDesk(this, this, SaveEmu, SaveLog);
            Log.Info("plugin constructed, assembly " + typeof(ZincExPlugin).Assembly.Location);
            LbipLog.Use(Log.Info, Log.Warn, Log.Disabled, () => Log.Tracing);
            LbipRowInjection.Install("com.nixxou.lbip.zincex", MetadataRows());

            // LaunchBox's Import ROM Files wizard: its game list kept to ZiNc's games for an import to ZiNc EX - see
            // ZincExLbImport. It does nothing at all outside LaunchBox.exe.
            ZincExLbImport.Install();
        }

        /// <summary>LaunchBox's metadata has no ZiNc EX row; this one is ours. AutoExtract is false: the romsets ARE the zips.</summary>
        private static IEnumerable<LbCatalogEmulator> MetadataRows()
        {
            yield return new LbCatalogEmulator
            {
                Name = PackName,
                CommandLine = DefaultCommandLine,
                ApplicableFileExtensions = Extensions,
                Url = ZincExBuild.Home,
                BinaryFileName = ZincExLaunch.ExecutableName,
                AutoExtract = false,
                // Not recommended for Arcade: 71 games of it - MAME stays the platform's emulator.
                Platforms = { new LbCatalogPlatform { Platform = ArcadePlatform, ApplicableFileExtensions = Extensions } },
            };
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
                LbipRowInjection.Install("com.nixxou.lbip.zincex", MetadataRows());
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
                if (!ZincExLaunch.IsZincEx(path)) continue;
                claimed.Add(emu);
                EnsureHotkeyScripts(emu);
                GiveBackFolder(path);
            }
            var signature = string.Join("|", claimed.Select(e => { try { return e.Title; } catch { return "?"; } }).OrderBy(t => t));
            if (signature != _lastClaimed)
            {
                _lastClaimed = signature;
                Log.Info("GetApplicableEmulators: claimed " + claimed.Count + " emulator(s)");
            }
            return claimed;
        }

        /// <summary>A ROMs folder a launch never gave back (LaunchBox gone mid-game) given back, when no game of ours is on
        /// and no ZiNc EX of that folder runs. Cheap: a File.Exists, when there is nothing to do.</summary>
        private static void GiveBackFolder(string path)
        {
            try
            {
                var exe = ResolveFullPath(path);
                var dir = string.IsNullOrWhiteSpace(exe) ? null : Path.GetDirectoryName(exe);
                if (!ZincExConfig.Pointed(dir) || _sessionOn || ZincExBuild.Running(dir)) return;
                Log.Info("a launch left ZiNc EX's ROMs folder pointed elsewhere - giving it back");
                ZincExConfig.Restore(dir);
            }
            catch { }
        }

        private static void EnsureHotkeyScripts(IEmulator emu)
        {
            if (!LbIntegrations.Compat.Compat.On("emulatorFields")) return;   // the Compatibility page
            try
            {
                var set = new List<string>();
                if (Fill(() => emu.AutoHotkeyScript, v => emu.AutoHotkeyScript = v, ZincExAhk.Running)) set.Add("running");
                if (Fill(() => emu.ExitAutoHotkeyScript, v => emu.ExitAutoHotkeyScript = v, ZincExAhk.Exit)) set.Add("exit");
                if (set.Count > 0) Log.Info("hotkey scripts set: " + string.Join(", ", set));
            }
            catch (Exception ex) { Log.Warn("could not describe the hotkeys on the emulator entry", ex); }
        }

        private static bool Fill(Func<string> get, Action<string> set, string value)
        {
            try
            {
                if (!LbipAhk.Ours(get(), value, ZincExAhk.Previous)) return false;
                set(value);
                return true;
            }
            catch { return false; }
        }

        public override EmulatorSupportResponse IsPlatformSupported(string platform)
        {
            bool supported = string.Equals((platform ?? "").Trim(), ArcadePlatform, StringComparison.InvariantCultureIgnoreCase);
            return new EmulatorSupportResponse(supported, false);
        }

        // ── versions ─────────────────────────────────────────────────────────

        public override string GetCurrentVersion(string applicationPath) => ZincExBuild.InstalledVersion(ResolveFullPath(applicationPath));

        /// <summary>The latest release: its download URL is the identifier, its version the label.</summary>
        public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions()
        {
            var r = ZincExBuild.Latest();
            if (r == null) return null;
            return new List<EmulatorControllerVersion> { new EmulatorControllerVersion(r.Url, r.Version ?? "latest", r.FileName) };
        }

        /// <summary>THE DOWNLOAD BUTTON OF ADD EMULATOR IS THIS (see DemulPlugin.IsUpdateAvailable): asked with an EMPTY path,
        /// shown on true. Installed, but not by this plugin - no, it is the user's; installed by us and older (or the same
        /// version uploaded again) - yes.</summary>
        public override bool IsUpdateAvailable(string emulatorAppPath, out EmulatorControllerVersion version)
        {
            version = null;
            try
            {
                var latest = ZincExBuild.Latest();
                version = latest == null ? null : new EmulatorControllerVersion(latest.Url, latest.Version ?? "latest", latest.FileName);
                if (string.IsNullOrWhiteSpace(emulatorAppPath)) return true;
                var exe = ResolveFullPath(emulatorAppPath);
                if (latest == null || !ZincExBuild.IsOurs(exe)) return false;
                return ZincExBuild.IsUpdate(latest, exe);
            }
            catch (Exception ex) { Log.Warn("update check failed", ex); return false; }
        }

        // ── installation ─────────────────────────────────────────────────────

        public override EmulatorInstallResponse InstallEmulator(InstallEmulatorArgs args)
        {
            string archive = null;
            try
            {
                var release = ZincExBuild.Latest();
                if (release == null) return new EmulatorInstallResponse("Couldn't read ZiNc EX's latest release on GitHub. Try again later.");

                bool reinstall = args?.ExistingEmulator != null;
                string targetDir = reinstall
                    ? Path.GetDirectoryName(ResolveFullPath(args.ExistingEmulator.ApplicationPath))
                    : Path.Combine(LaunchBoxRoot(), "Emulators", PackName);
                if (string.IsNullOrWhiteSpace(targetDir)) return new EmulatorInstallResponse("Couldn't work out where to install ZiNc EX.");
                if (ZincExBuild.Running(targetDir)) return new EmulatorInstallResponse("ZiNc EX or ZiNc is running from " + targetDir + ": close it, then update again.");

                Report(args, "Downloading ZiNc EX " + release.Version + "...", 0);
                archive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                ZincExBuild.Fetch(release.Url, archive, p => Report(args, "Downloading ZiNc EX " + release.Version + "...", p),
                    () => { try { return args?.ShouldCancelFunc?.Invoke() ?? false; } catch { return false; } });

                Report(args, "Extracting ZiNc EX...", null);
                Archives.ExtractOver(archive, targetDir);
                // An update keeps the launcher the entry was on (64-bit, or the 32-bit one the user chose).
                var exe = reinstall ? ResolveFullPath(args.ExistingEmulator.ApplicationPath) : Path.Combine(targetDir, ZincExLaunch.ExecutableName);
                if (!File.Exists(exe) || !File.Exists(Path.Combine(targetDir, ZincExLaunch.ZincName)))
                    return new EmulatorInstallResponse("ZiNc EX was extracted to " + targetDir + " but " + Path.GetFileName(exe) + " or " + ZincExLaunch.ZincName + " is not there.");
                ZincExBuild.Stamp(targetDir, release);
                // ZiNc wants a cfg\ folder ("Make sure a cfg/ dir exists", its readme): the zip has one, kept if it goes.
                Directory.CreateDirectory(Path.Combine(targetDir, "cfg"));
                Log.Info("installed " + release.FileName + " to " + targetDir);

                if (reinstall)
                {
                    try { args.ExistingEmulator.ApplicationPath = MakeRelativeToLaunchBox(exe); } catch { }
                    return new EmulatorInstallResponse(args.ExistingEmulator, "ZiNc EX updated to " + release.Version + ".");
                }
                if (args != null && !args.ShouldCreateEmulator)
                    return new EmulatorInstallResponse("ZiNc EX was installed to " + targetDir + ", but no emulator entry was requested.");

                var created = CreateEmulator(exe);
                if (created == null)
                    return new EmulatorInstallResponse("ZiNc EX was installed to " + targetDir + " but the emulator entry couldn't be created (no data manager available).");
                return new EmulatorInstallResponse(created, "ZiNc EX " + release.Version + " installed. Assign it to your ZN games (Edit > Emulation); "
                                                            + "the BIOS sets (cpzn1, cpzn2... or coh1000c, coh3002c... as MAME names them) go beside the games or in "
                                                            + Path.Combine(targetDir, ZincExConfig.DefaultFolder) + ".");
            }
            catch (OperationCanceledException) { return new EmulatorInstallResponse("Installation cancelled."); }
            catch (Exception ex)
            {
                Log.Warn("install failed", ex);
                return new EmulatorInstallResponse("Failed to install ZiNc EX: " + ex.Message);
            }
            finally
            {
                try { if (archive != null && File.Exists(archive)) File.Delete(archive); } catch { }
            }
        }

        /// <summary>A new entry for Arcade, NOT its default: MAME runs the platform, ZiNc EX 71 games of it - assigned per game.
        /// Only on the entry this plugin creates: an existing emulator's platforms are never touched.</summary>
        private static IEmulator CreateEmulator(string exePath)
        {
            var dm = PluginHelper.DataManager;
            if (dm == null) return null;
            var emu = dm.AddNewEmulator();
            emu.Title = PackName;
            emu.ApplicationPath = MakeRelativeToLaunchBox(exePath);
            emu.CommandLine = DefaultCommandLine;
            EnsureHotkeyScripts(emu);
            // DefaultPlatform IS NOT SET - see XeniaPlugin.CreateEmulator.
            var platform = emu.AddNewEmulatorPlatform();
            platform.Platform = ArcadePlatform;
            platform.IsDefault = false;
            try { dm.Save(false); } catch (Exception ex) { Log.Warn("data manager save failed", ex); }
            Log.Info("created emulator entry \"" + PackName + "\" -> " + emu.ApplicationPath);
            return emu;
        }

        // ── BIOS ─────────────────────────────────────────────────────────────

        /// <summary>None declared: which BIOS set a game wants depends on its board (cpzn1, cpzn2, taitofx1, psarc95, tps,
        /// atluspsx, konamigv - none for Namco's), it is looked for beside the game, in ZiNc EX's roms\ and its ROMs folder,
        /// under ZiNc's name or MAME's - and a MAME non-merged set carries it inside. A missing one is said at launch.</summary>
        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string platform) => Array.Empty<EmulatorBiosFile>();

        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string emulatorApplicationPath, string platform, string commandLine)
            => GetBiosFilesForPlatform(platform);

        public override RetroAchievementSupportResponse SupportsRetroAchievements(string emulatorApplicationPath)
            => new RetroAchievementSupportResponse(isSupported: false);

        // ── launch ───────────────────────────────────────────────────────────

        /// <summary>A session of ours holds ZiNc EX's ROMs folder: none given back from under it (GiveBackFolder).</summary>
        private static volatile bool _sessionOn;

        /// <summary>One launch at a time - LbipLaunchGate.</summary>
        public override PrepareForLaunchResponse PrepareEmulatorForLaunch(PrepareForLaunchArgs args)
            => LbipLaunchGate.Run(PackName, args, PrepareCore, () => !_sessionOn);

        private PrepareForLaunchResponse PrepareCore(PrepareForLaunchArgs args)
        {
            string exeDir = null;
            bool pointed = false;
            try
            {
                var exe = ResolveFullPath(Safe(() => args?.EmulatorBeingLaunched?.ApplicationPath));
                if (string.IsNullOrWhiteSpace(exe) || !ZincExLaunch.IsZincEx(exe) || !File.Exists(exe)) return new PrepareForLaunchResponse(success: true);
                exeDir = Path.GetDirectoryName(exe);
                if (!File.Exists(Path.Combine(exeDir, ZincExLaunch.ZincName)))
                    return Refuse("ZiNc.exe is not beside " + Path.GetFileName(exe) + " (" + exeDir + "): ZiNc EX starts the games with it.\n\n"
                                  + "Update the emulator from LaunchBox, or put ZiNc 1.1 there.");
                var rom = ResolveFullPath(Safe(() => args?.GameBeingLaunched?.ApplicationPath));
                var gameId = Safe(() => args?.GameBeingLaunched?.Id);

                var match = ZincExLaunch.Identify(exe, rom, gameId, out var problem);
                if (match == null) return Refuse(problem);
                var folder = ZincRoms.Prepare(exeDir, match, out problem);
                if (folder == null) return Refuse(problem);

                pointed = ZincExConfig.Point(exeDir, folder);
                var line = ZincExLaunch.CommandLine(Safe(() => args?.CurrentCommandLine) ?? "", rom, match.Game.Set, ScreenOf(exeDir, match.Game));
                Log.Info("command line: " + line);
                _sessionOn = true;
                ZincExSession.Watch(exe, PackName, () => _sessionOn = false);
                pointed = false;   // the session gives it back now
                return new PrepareForLaunchResponse(success: true) { NewCommandLine = line };
            }
            catch (Exception ex)
            {
                Log.Warn("PrepareEmulatorForLaunch", ex);
                return Refuse("Something went wrong while preparing the game for ZiNc EX: " + ex.Message);
            }
            finally
            {
                if (pointed) ZincExConfig.Restore(exeDir);
            }
        }

        /// <summary>The Nixx page's window flag - none for a game that has its own Window in zinc-games.cfg: ZiNc EX's
        /// --fullscreen / --windowed would win over the game's own setting (settings.c zinc_args_ex: the play options last).</summary>
        internal static string ScreenOf(string exeDir, ZincGame game)
        {
            try { if (ZincExGameCfg.Of(exeDir, game.Id).ContainsKey("fullscreen")) return "zincex"; }
            catch (Exception ex) { Log.Warn("could not read zinc-games.cfg", ex); }
            return ZincExSettings.Screen;
        }

        private static PrepareForLaunchResponse Refuse(string message)
        {
            Log.Info("launch refused: " + (message ?? "").Replace("\n", " "));
            LbipNotice.Show(PackName, message);
            return new PrepareForLaunchResponse(success: false);
        }

        private static T Safe<T>(Func<T> get) { try { return get(); } catch { return default; } }

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
                maybeRelative = maybeRelative.Trim().Trim('"');
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
