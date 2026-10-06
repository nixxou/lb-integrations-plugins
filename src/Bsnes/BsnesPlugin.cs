// bsnes integration for LaunchBox / LiteBox - the accurate Super Nintendo emulator, its nightly builds.
//
// Claim the emulator, install and update the nightly (BsnesBuild), describe it to the host's catalogue, write the
// game's options into settings.bml and put --fullscreen on its line at every launch (BsnesLaunch), and list the
// cartridge saves and the save states (BsnesSaves). The same shape as the RASnes9x plugin.
//
// NO RETROACHIEVEMENTS: bsnes has none, and RetroAchievements lists its cores as untested.
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

namespace LbIntegrations.Bsnes
{
    public partial class BsnesPlugin : EmulatorPlugin, ISystemEventsPlugin, ILbCatalogSource
    {
        internal const string SnesPlatform = "Super Nintendo Entertainment System";

        /// <summary>Nothing: the host appends the game's path ("bsnes.exe &lt;rom&gt;"); --fullscreen is added at each launch.</summary>
        private const string DefaultCommandLine = "";

        /// <summary>What bsnes opens itself (game.cpp): the ROM types, .zip and .7z.</summary>
        private const string Extensions = ".sfc; .smc; .swc; .fig; .bs; .st; .zip; .7z";

        internal const string PackName = "Nixx-bsnes";

        public BsnesPlugin()
        {
            Log.Info("plugin constructed, assembly " + typeof(BsnesPlugin).Assembly.Location);
            LbipLog.Use(Log.Info, Log.Warn, Log.Disabled, () => Log.Tracing);
            LbipRowInjection.Install("com.nixxou.lbip.bsnes", MetadataRows());
        }

        /// <summary>LaunchBox's metadata has a bsnes row; this pack publishes its own beside it. AutoExtract is false: bsnes
        /// opens archives itself, and the archive's name is the name of its save.</summary>
        private static IEnumerable<LbCatalogEmulator> MetadataRows()
        {
            yield return new LbCatalogEmulator
            {
                Name = PackName,
                CommandLine = DefaultCommandLine,
                ApplicableFileExtensions = Extensions,
                Url = BsnesBuild.Home,
                BinaryFileName = BsnesLaunch.ExecutableName,
                AutoExtract = false,
                Platforms = { new LbCatalogPlatform { Platform = SnesPlatform, ApplicableFileExtensions = Extensions, Recommended = true } },
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
                LbipRowInjection.Install("com.nixxou.lbip.bsnes", MetadataRows());
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
                if (!BsnesLaunch.IsBsnesExe(path)) continue;
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
            try
            {
                var set = new List<string>();
                if (Fill(() => emu.AutoHotkeyScript, v => emu.AutoHotkeyScript = v, BsnesAhk.Running)) set.Add("running");
                if (Fill(() => emu.ExitAutoHotkeyScript, v => emu.ExitAutoHotkeyScript = v, BsnesAhk.Exit)) set.Add("exit");
                if (Fill(() => emu.SaveStateAutoHotkeyScript, v => emu.SaveStateAutoHotkeyScript = v, BsnesAhk.SaveState)) set.Add("save");
                if (Fill(() => emu.LoadStateAutoHotkeyScript, v => emu.LoadStateAutoHotkeyScript = v, BsnesAhk.LoadState)) set.Add("load");
                if (set.Count > 0) Log.Info("hotkey scripts set: " + string.Join(", ", set));
            }
            catch (Exception ex) { Log.Warn("could not describe the hotkeys on the emulator entry", ex); }
        }

        /// <summary>A script set when empty - or when it is, word for word, one an earlier build of this plugin wrote
        /// (BsnesAhk.Superseded: the F5/F7 keys, the F2/F4 note). One the user changed is never touched.</summary>
        private static bool Fill(Func<string> get, Action<string> set, string value)
        {
            try
            {
                var now = get();
                string Norm(string s) => (s ?? "").Replace("\r\n", "\n").Trim();
                bool ours = BsnesAhk.Superseded.Any(old => Norm(old) == Norm(now));
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
            return new EmulatorSupportResponse(supported, supported);
        }

        // ── versions ─────────────────────────────────────────────────────────

        public override string GetCurrentVersion(string applicationPath) => BsnesBuild.InstalledVersion(ResolveFullPath(applicationPath));

        /// <summary>The nightly: its download URL is the identifier, its date the label.</summary>
        public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions()
        {
            var n = BsnesBuild.Latest();
            if (n == null) return null;
            return new List<EmulatorControllerVersion> { new EmulatorControllerVersion(n.Url, n.Version ?? "nightly", n.FileName) };
        }

        public override bool IsUpdateAvailable(string emulatorAppPath, out EmulatorControllerVersion version)
        {
            version = null;
            try
            {
                version = GetInstallableVersions()?.FirstOrDefault();
                if (version == null) return string.IsNullOrWhiteSpace(emulatorAppPath);
                if (string.IsNullOrWhiteSpace(emulatorAppPath)) return true;
                var current = GetCurrentVersion(emulatorAppPath);
                return current != null && BsnesBuild.IsNewer(version.Label, current);
            }
            catch (Exception ex) { Log.Warn("update check failed", ex); return false; }
        }

        // ── installation ─────────────────────────────────────────────────────

        public override EmulatorInstallResponse InstallEmulator(InstallEmulatorArgs args)
        {
            string archive = null;
            try
            {
                var nightly = BsnesBuild.Latest();
                if (nightly == null) return new EmulatorInstallResponse("Couldn't read bsnes's nightly release on GitHub. Try again later.");

                bool reinstall = args?.ExistingEmulator != null;
                string targetDir = reinstall
                    ? Path.GetDirectoryName(ResolveFullPath(args.ExistingEmulator.ApplicationPath))
                    : Path.Combine(LaunchBoxRoot(), "Emulators", PackName);
                if (string.IsNullOrWhiteSpace(targetDir)) return new EmulatorInstallResponse("Couldn't work out where to install bsnes.");
                var exe = Path.Combine(targetDir, BsnesLaunch.ExecutableName);
                if (BsnesLaunch.Running(exe)) return new EmulatorInstallResponse("bsnes is running from " + targetDir + ": close it, then update again.");

                Report(args, "Downloading bsnes " + nightly.Version + "...", 0);
                archive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                BsnesBuild.Fetch(nightly.Url, archive, p => Report(args, "Downloading bsnes " + nightly.Version + "...", p),
                    () => { try { return args?.ShouldCancelFunc?.Invoke() ?? false; } catch { return false; } });

                Report(args, "Extracting bsnes...", null);
                Archives.ExtractOver(archive, targetDir);
                if (!File.Exists(exe))
                    return new EmulatorInstallResponse("bsnes was extracted to " + targetDir + " but " + BsnesLaunch.ExecutableName + " is not there.");
                BsnesBuild.Stamp(targetDir, nightly.Version);
                Log.Info("installed the nightly " + nightly.Version + " to " + targetDir);

                if (reinstall)
                {
                    try { args.ExistingEmulator.ApplicationPath = MakeRelativeToLaunchBox(exe); } catch { }
                    return new EmulatorInstallResponse(args.ExistingEmulator, "bsnes updated to the nightly of " + nightly.Version + ".");
                }
                if (args != null && !args.ShouldCreateEmulator)
                    return new EmulatorInstallResponse("bsnes was installed to " + targetDir + ", but no emulator entry was requested.");

                var created = CreateEmulator(exe);
                if (created == null)
                    return new EmulatorInstallResponse("bsnes was installed to " + targetDir + " but the emulator entry couldn't be created (no data manager available).");
                return new EmulatorInstallResponse(created, "bsnes (nightly of " + nightly.Version + ") installed.");
            }
            catch (OperationCanceledException) { return new EmulatorInstallResponse("Installation cancelled."); }
            catch (Exception ex)
            {
                Log.Warn("install failed", ex);
                return new EmulatorInstallResponse("Failed to install bsnes: " + ex.Message);
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
            // DefaultPlatform IS NOT SET - see XeniaPlugin.CreateEmulator. IsDefault on the row is what counts.
            var platform = emu.AddNewEmulatorPlatform();
            platform.Platform = SnesPlatform;
            platform.IsDefault = !taken;
            try { dm.Save(false); } catch (Exception ex) { Log.Warn("data manager save failed", ex); }
            Log.Info("created emulator entry \"" + PackName + "\" -> " + emu.ApplicationPath);
            return emu;
        }

        // ── BIOS ─────────────────────────────────────────────────────────────

        /// <summary>None asked: bsnes emulates the DSP and the other chips in high level when their firmware is not there.</summary>
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
            string exe = null, gameId = null;
            try { exe = ResolveFullPath(args?.EmulatorBeingLaunched?.ApplicationPath); } catch { }
            try { gameId = args?.GameBeingLaunched?.Id; } catch { }
            if (!BsnesLaunch.IsBsnesExe(exe) || !File.Exists(exe)) return new PrepareForLaunchResponse(success: true);
            if (BsnesLaunch.Running(exe)) Log.Warn("bsnes is already running from " + Path.GetDirectoryName(exe) + " - what is written now is lost when it quits");

            BsnesLaunch.PrepareSettings(exe, gameId);
            // The states' thumbnail (<name>.bsz.png), from bsnes's own preview once it has quit - BsnesStateThumbs.
            try { BsnesStateThumbs.AfterSession(exe, ResolveFullPath(args?.GameBeingLaunched?.ApplicationPath)); } catch { }
            var current = args?.CurrentCommandLine ?? "";
            var line = BsnesLaunch.CommandLine(current, BsnesSettings.BoolFor(gameId, "Fullscreen"));
            if (line != current.Trim())
            {
                Log.Info("launch: " + line);
                return new PrepareForLaunchResponse(success: true) { NewCommandLine = line };
            }
            return new PrepareForLaunchResponse(success: true);
        }

        // ── paths ────────────────────────────────────────────────────────────

        private static string LaunchBoxRoot()
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
