// ares integration for LaunchBox / LiteBox - the accurate multi-system emulator (Near's, after higan).
//
// Claim the emulator, install and update its latest release (AresBuild), describe it to the host's catalogue for every
// platform it runs (AresSystems), and at each launch: the game's SYSTEM (AresLaunch.SystemFor - its own choice, its
// platform's line, the plugin's table), its BIOS found and given to ares (AresFirmware), its options in settings.bml,
// and the line. Saves and states: AresSaves.
//
// THE PLATFORM LINES, AS RETROARCH'S CORES (Mehdi, 06/10): each platform row of the emulator carries --system "<name>" in
// its command line - the plugin writes it on the rows of an emulator it creates, and on a row whose line is still empty
// when the host hands the emulator over; a line the user wrote is never rewritten.
//
// NO RETROACHIEVEMENTS: ares has none.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;
using LbIntegrations.Catalog;
using LbIntegrations.Lbip;

namespace LbIntegrations.Ares
{
    public partial class AresPlugin : EmulatorPlugin, ISystemEventsPlugin, ILbCatalogSource
    {
        private const string DefaultCommandLine = "";
        internal const string PackName = "Nixx-ares";

        public AresPlugin()
        {
            Log.Info("plugin constructed, assembly " + typeof(AresPlugin).Assembly.Location);
            LbipLog.Use(Log.Info, Log.Warn, Log.Disabled, () => Log.Tracing);
            LbipRowInjection.Install("com.nixxou.lbip.ares", MetadataRows());
        }

        /// <summary>LaunchBox's metadata has no ares row (06/10: higan only). AutoExtract is false: ares opens a zip itself,
        /// and the plugin unpacks a .7z when it has to.</summary>
        private static IEnumerable<LbCatalogEmulator> MetadataRows()
        {
            var row = new LbCatalogEmulator
            {
                Name = PackName,
                CommandLine = DefaultCommandLine,
                ApplicableFileExtensions = string.Join("; ", AresSystems.Platforms.SelectMany(p => AresSystems.AllExtensions(p).Split(new[] { "; " }, StringSplitOptions.None)).Distinct(StringComparer.OrdinalIgnoreCase)),
                Url = AresBuild.Home,
                BinaryFileName = AresLaunch.ExecutableName,
                AutoExtract = false,
            };
            foreach (var p in AresSystems.Platforms)
                row.Platforms.Add(new LbCatalogPlatform { Platform = p.Platform, ApplicableFileExtensions = AresSystems.AllExtensions(p), Recommended = true });
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
                LbipRowInjection.Install("com.nixxou.lbip.ares", MetadataRows());
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
                if (!AresLaunch.IsAresExe(path)) continue;
                claimed.Add(emu);
                EnsureHotkeyScripts(emu);
                FillPlatformLines(emu);
            }
            var signature = string.Join("|", claimed.Select(e => { try { return e.Title; } catch { return "?"; } }).OrderBy(t => t));
            if (signature != _lastClaimed)
            {
                _lastClaimed = signature;
                Log.Info("GetApplicableEmulators: claimed " + claimed.Count + " emulator(s)");
            }
            return claimed;
        }

        internal static string LineFor(string system) => "--system \"" + system + "\"";

        /// <summary>A platform row of ours whose command line is empty gets its --system. A line the user wrote is kept.</summary>
        private static void FillPlatformLines(IEmulator emu)
        {
            try
            {
                var set = new List<string>();
                foreach (var row in emu.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>())
                {
                    var p = AresSystems.Platform(Safe(() => row.Platform));
                    if (p == null || !string.IsNullOrWhiteSpace(Safe(() => row.CommandLine))) continue;
                    try { row.CommandLine = LineFor(p.Default); set.Add(p.Platform); } catch { }
                }
                if (set.Count > 0) Log.Info("platform lines set: " + string.Join(", ", set));
            }
            catch (Exception ex) { Log.Warn("could not set the platforms' lines", ex); }
        }

        private static void EnsureHotkeyScripts(IEmulator emu)
        {
            try
            {
                var set = new List<string>();
                if (Fill(() => emu.AutoHotkeyScript, v => emu.AutoHotkeyScript = v, AresAhk.Running)) set.Add("running");
                if (Fill(() => emu.ExitAutoHotkeyScript, v => emu.ExitAutoHotkeyScript = v, AresAhk.Exit)) set.Add("exit");
                if (Fill(() => emu.SaveStateAutoHotkeyScript, v => emu.SaveStateAutoHotkeyScript = v, AresAhk.SaveState)) set.Add("save");
                if (Fill(() => emu.LoadStateAutoHotkeyScript, v => emu.LoadStateAutoHotkeyScript = v, AresAhk.LoadState)) set.Add("load");
                if (set.Count > 0) Log.Info("hotkey scripts set: " + string.Join(", ", set));
            }
            catch (Exception ex) { Log.Warn("could not describe the hotkeys on the emulator entry", ex); }
        }

        private static bool Fill(Func<string> get, Action<string> set, string value)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(get())) return false;
                set(value);
                return true;
            }
            catch { return false; }
        }

        public override EmulatorSupportResponse IsPlatformSupported(string platform)
        {
            bool supported = AresSystems.Platform(platform) != null;
            return new EmulatorSupportResponse(supported, supported);
        }

        // ── versions ─────────────────────────────────────────────────────────

        public override string GetCurrentVersion(string applicationPath) => AresBuild.InstalledVersion(ResolveFullPath(applicationPath));

        public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions()
        {
            var r = AresBuild.Latest();
            if (r == null) return null;
            return new List<EmulatorControllerVersion> { new EmulatorControllerVersion(r.Url, r.Version ?? "latest", r.FileName) };
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
                return current != null && AresBuild.IsNewer(version.Label, current);
            }
            catch (Exception ex) { Log.Warn("update check failed", ex); return false; }
        }

        // ── installation ─────────────────────────────────────────────────────

        public override EmulatorInstallResponse InstallEmulator(InstallEmulatorArgs args)
        {
            string archive = null;
            try
            {
                var release = AresBuild.Latest();
                if (release == null) return new EmulatorInstallResponse("Couldn't read ares's latest release on GitHub. Try again later.");

                bool reinstall = args?.ExistingEmulator != null;
                string targetDir = reinstall
                    ? Path.GetDirectoryName(ResolveFullPath(args.ExistingEmulator.ApplicationPath))
                    : Path.Combine(LaunchBoxRoot(), "Emulators", PackName);
                if (string.IsNullOrWhiteSpace(targetDir)) return new EmulatorInstallResponse("Couldn't work out where to install ares.");
                var exe = Path.Combine(targetDir, AresLaunch.ExecutableName);
                if (AresLaunch.Running(exe)) return new EmulatorInstallResponse("ares is running from " + targetDir + ": close it, then update again.");

                Report(args, "Downloading ares " + release.Version + "...", 0);
                archive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                AresBuild.Fetch(release, archive, p => Report(args, "Downloading ares " + release.Version + "...", p),
                    () => { try { return args?.ShouldCancelFunc?.Invoke() ?? false; } catch { return false; } });

                Report(args, "Extracting ares...", null);
                Archives.ExtractOver(archive, targetDir);
                if (!File.Exists(exe)) return new EmulatorInstallResponse("ares was extracted to " + targetDir + " but " + AresLaunch.ExecutableName + " is not there.");
                AresBuild.Stamp(targetDir, release.Version);
                Log.Info("installed ares " + release.Version + " to " + targetDir);

                // Every BIOS the user already has, given to ares now - see AresFirmware.
                Report(args, "Looking for the BIOS files...", null);
                var firmware = AresFirmware.Ensure(exe, AresSystems.All, m => Report(args, m, null));
                var given = firmware.Count(f => f.Path != null);
                var note = " " + given + " of " + firmware.Count + " BIOS entries found.";

                if (reinstall)
                {
                    try { args.ExistingEmulator.ApplicationPath = MakeRelativeToLaunchBox(exe); } catch { }
                    return new EmulatorInstallResponse(args.ExistingEmulator, "ares updated to " + release.Version + "." + note);
                }
                if (args != null && !args.ShouldCreateEmulator)
                    return new EmulatorInstallResponse("ares was installed to " + targetDir + ", but no emulator entry was requested." + note);

                var created = CreateEmulator(exe);
                if (created == null)
                    return new EmulatorInstallResponse("ares was installed to " + targetDir + " but the emulator entry couldn't be created (no data manager available).");
                return new EmulatorInstallResponse(created, "ares " + release.Version + " installed." + note);
            }
            catch (OperationCanceledException) { return new EmulatorInstallResponse("Installation cancelled."); }
            catch (Exception ex)
            {
                Log.Warn("install failed", ex);
                return new EmulatorInstallResponse("Failed to install ares: " + ex.Message);
            }
            finally
            {
                try { if (archive != null && File.Exists(archive)) File.Delete(archive); } catch { }
            }
        }

        /// <summary>The emulator with a row for each platform, its --system on its line. The default of a platform only when
        /// no emulator of the library is yet - an existing choice is the user's (never-touch-existing-platform-rows).</summary>
        private static IEmulator CreateEmulator(string exePath)
        {
            var dm = PluginHelper.DataManager;
            if (dm == null) return null;
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
            var defaults = new List<string>();
            foreach (var p in AresSystems.Platforms)
            {
                // DefaultPlatform IS NOT SET - see XeniaPlugin.CreateEmulator. IsDefault on the row is what counts.
                var row = emu.AddNewEmulatorPlatform();
                row.Platform = p.Platform;
                row.CommandLine = LineFor(p.Default);
                row.IsDefault = !taken.Contains(p.Platform);
                if (row.IsDefault) defaults.Add(p.Platform);
            }
            try { dm.Save(false); } catch (Exception ex) { Log.Warn("data manager save failed", ex); }
            Log.Info("created emulator entry \"" + PackName + "\" -> " + emu.ApplicationPath + ", " + AresSystems.Platforms.Length + " platforms, default for "
                     + (defaults.Count == 0 ? "none" : string.Join(", ", defaults)));
            return emu;
        }

        // ── BIOS ─────────────────────────────────────────────────────────────

        /// <summary>The firmware of the platform's systems, in ares's Firmware folder - what the plugin copies there.</summary>
        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string platform)
        {
            var p = AresSystems.Platform(platform);
            if (p == null) return Array.Empty<EmulatorBiosFile>();
            var list = new List<EmulatorBiosFile>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in p.Systems.Select(AresSystems.System).Where(s => s != null))
            {
                var (owner, specs) = AresSystems.FirmwareFor(s);
                foreach (var spec in specs)
                    if (spec.Names.Length > 0 && seen.Add(spec.Names[0]))
                        list.Add(new EmulatorBiosFile(AresFirmware.Folder, spec.Names[0], false,
                            owner.Name + " " + spec.Label + (spec.Optional ? " - optional" : "") + (spec.Names.Length > 1 ? " (also found as " + string.Join(", ", spec.Names.Skip(1)) + ")" : ""), null, null));
            }
            return list;
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
            if (!AresLaunch.IsAresExe(exe) || !File.Exists(exe)) return new PrepareForLaunchResponse(success: true);
            if (AresLaunch.Running(exe)) Log.Warn("ares is already running from " + Path.GetDirectoryName(exe) + " - what is written now is lost when it quits");

            var current = args?.CurrentCommandLine ?? "";
            var (systemName, from) = AresLaunch.SystemFor(gameId, platform, rom, current);
            var system = AresSystems.System(systemName);
            if (systemName == null)
            {
                LbipNotice.Show(PackName + " - " + (title ?? Path.GetFileName(rom)), "ares does not know which system to run this game as: its platform (" + platform
                                + ") is not one this plugin maps. Choose the system in the game's window (Nixx-ares : Options...), or put --system \"<name>\" on the platform's line.");
                return new PrepareForLaunchResponse(success: false);
            }
            Log.Info("launch: " + Path.GetFileName(rom) + " as " + systemName + " (from " + from + ")");

            // Its BIOS.
            if (system != null)
            {
                var firmware = AresFirmware.Ensure(exe, new[] { system }, optional: false);
                if (!AresFirmware.Enough(firmware))
                {
                    var (owner, specs) = AresSystems.FirmwareFor(system);
                    var names = string.Join("\n", specs.Where(s => !s.Optional).Select(s => "  " + s.Label + ": " + string.Join(" or ", s.Names)));
                    LbipNotice.Show(PackName + " - " + (title ?? Path.GetFileName(rom)),
                        system.Name + " needs its BIOS, and none was found.\n\nPut one of these in " + Path.Combine(Path.GetDirectoryName(exe), AresFirmware.Folder) + ":\n\n" + names
                        + "\n\n(any file name works there when its content is the right dump), or in RetroArch's system folder - it is copied at the next launch.");
                    return new PrepareForLaunchResponse(success: false);
                }
            }

            // A .7z or a .m3u.
            string played = rom;
            if (!string.IsNullOrEmpty(rom))
            {
                played = AresLaunch.RomFor(rom, system, exe, out var problem);
                if (played == null)
                {
                    LbipNotice.Show(PackName + " - " + (title ?? Path.GetFileName(rom)), "This game cannot be started: " + problem);
                    return new PrepareForLaunchResponse(success: false);
                }
            }

            AresLaunch.PrepareSettings(exe, gameId);
            var line = AresLaunch.CommandLine(current, exe, systemName, AresSettings.For(gameId, "Screen"), rom, played == rom ? null : played);
            Log.Info("launch: " + line);
            return new PrepareForLaunchResponse(success: true) { NewCommandLine = line };
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
