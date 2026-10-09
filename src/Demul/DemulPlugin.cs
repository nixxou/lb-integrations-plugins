// Demul integration for LaunchBox / LiteBox: Dreamcast, Naomi, Naomi 2, Atomiswave, Hikaru - and System SP,
// CV1000 and Gaelco on a platform the user names for them (see DemulPlatforms).
//
// Claim the emulator, install the build the pack pins (DemulBuild), describe it to the host's catalogue,
// and make each launch work: Demul looks romsets up by name in the folders its Demul.ini lists, so a
// launch writes the game's folder there and hands Demul "-run=<system> -rom=<set>" (DemulLaunch).
//
// Demul is closed source. What is said here was read off its archives - the help text, the set names and
// the ini files in demul.exe and beside it - or off Batocera's launcher, which runs it for Hikaru, Gaelco
// and CV1000; the comments say which. Saves (nvram\, memsaves\) and the BIOS sets' checksums are not done
// yet: they are measured before they are written.
//
// Like every plugin here, this talks ONLY to the public SDK, and every entry point catches, logs and
// degrades - the host swallows exceptions silently.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;
using LbIntegrations.Catalog;
using LbIntegrations.Lbip;

namespace LbIntegrations.Demul
{
    public partial class DemulPlugin : EmulatorPlugin, ISystemEventsPlugin, ILbCatalogSource
    {
        /// <summary>The name this pack publishes under: the catalogue row, the Add Emulator entry, the title of
        /// an emulator this plugin creates, and the folder Demul is installed into.</summary>
        private const string PackName = "Nixx-Demul";

        private const string ExecutableName = "demul.exe";

        /// <summary>Empty: the whole line is written at launch (DemulLaunch.CommandLine), from the game.</summary>
        private const string DefaultCommandLine = "";

        /// <summary>Dreamcast discs: the two readers in plugins\ - gdrImage (.gdi, .cdi) and gdrCHD (.chd).</summary>
        private const string DiscExtensions = ".gdi; .cdi; .chd";

        /// <summary>Romsets. Demul opens zips and 7z itself - its loader's own messages are "Unzip error" and
        /// "Un7z error" - and needs them whole: an extracted set is a folder it cannot be told about.</summary>
        private const string ArcadeExtensions = ".zip; .7z";

        public DemulPlugin()
        {
            Log.Info("plugin constructed, assembly " + typeof(DemulPlugin).Assembly.Location);
            LbipLog.Use(Log.Info, Log.Warn, Log.Disabled, () => Log.Tracing);
            LbipRowInjection.Install("com.nixxou.lbip.demul", MetadataRows());

            // LaunchBox's Import ROM Files wizard: its game list sorted by board for an import to Demul - see
            // DemulLbImport. It does nothing at all outside LaunchBox.exe.
            DemulLbImport.Install();
        }

        /// <summary>What LaunchBox's emulator metadata should say. Their database has a "Demul" row for the
        /// Dreamcast alone ("-run=dc -image=", .gdi and .cdi); this one is ours, beside it.
        /// AutoExtract is false: the romsets ARE the archives.</summary>
        private static IEnumerable<LbCatalogEmulator> MetadataRows()
        {
            yield return new LbCatalogEmulator
            {
                Name = PackName,
                CommandLine = DefaultCommandLine,
                ApplicableFileExtensions = DiscExtensions + "; " + ArcadeExtensions,
                Url = DemulBuild.Home,
                BinaryFileName = ExecutableName,
                AutoExtract = false,
                Platforms =
                {
                    new LbCatalogPlatform { Platform = DemulPlatforms.Dreamcast, ApplicableFileExtensions = DiscExtensions, RequiredBiosFile = "dc.zip" },
                    new LbCatalogPlatform { Platform = DemulPlatforms.Naomi, ApplicableFileExtensions = ArcadeExtensions, RequiredBiosFile = "naomi.zip" },
                    new LbCatalogPlatform { Platform = DemulPlatforms.Naomi2, ApplicableFileExtensions = ArcadeExtensions, RequiredBiosFile = "naomi2.zip" },
                    new LbCatalogPlatform { Platform = DemulPlatforms.Atomiswave, ApplicableFileExtensions = ArcadeExtensions, RequiredBiosFile = "awbios.zip" },
                    new LbCatalogPlatform { Platform = DemulPlatforms.Hikaru, ApplicableFileExtensions = ArcadeExtensions, RequiredBiosFile = "hikaru.zip", Recommended = true },
                },
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
                LbipRowInjection.Install("com.nixxou.lbip.demul", MetadataRows());
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
                if (!IsDemul(path)) continue;
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

        /// <summary>The AutoHotkey scripts of the pack's keys (Mehdi, 07/10) - see DemulAhk. Each one set when its field is
        /// blank, or still a script an earlier build of this plugin wrote (DemulAhk.Previous: none yet); one the user wrote
        /// is never touched. On the object the host hands over each time, as AresPlugin does.</summary>
        private static void EnsureHotkeyScripts(IEmulator emu)
        {
            if (!LbIntegrations.Compat.Compat.On("emulatorFields")) return;   // the Compatibility page
            try
            {
                var set = new List<string>();
                if (Fill(() => emu.AutoHotkeyScript, v => emu.AutoHotkeyScript = v, DemulAhk.Running)) set.Add("running");
                if (Fill(() => emu.ExitAutoHotkeyScript, v => emu.ExitAutoHotkeyScript = v, DemulAhk.Exit)) set.Add("exit");
                if (Fill(() => emu.SaveStateAutoHotkeyScript, v => emu.SaveStateAutoHotkeyScript = v, DemulAhk.SaveState)) set.Add("save");
                if (Fill(() => emu.LoadStateAutoHotkeyScript, v => emu.LoadStateAutoHotkeyScript = v, DemulAhk.LoadState)) set.Add("load");
                if (set.Count > 0) Log.Info("hotkey scripts set: " + string.Join(", ", set));
            }
            catch (Exception ex) { Log.Warn("could not describe the hotkeys on the emulator entry", ex); }
        }

        private static bool Fill(Func<string> get, Action<string> set, string value)
        {
            try
            {
                if (!LbipAhk.Ours(get(), value, DemulAhk.Previous)) return false;
                set(value);
                return true;
            }
            catch { return false; }
        }

        private static bool IsDemul(string applicationPath)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(applicationPath)
                    && string.Equals(Path.GetFileName(applicationPath.Trim().Trim('"')), ExecutableName, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public override EmulatorSupportResponse IsPlatformSupported(string platform)
        {
            bool supported = DemulPlatforms.IsOurs(platform);
            Log.Verbose("IsPlatformSupported(\"" + platform + "\") -> " + supported);
            return new EmulatorSupportResponse(supported, supported);
        }

        // ── versions ─────────────────────────────────────────────────────────

        public override string GetCurrentVersion(string applicationPath)
            => DemulBuild.InstalledVersion(ResolveFullPath(applicationPath));

        /// <summary>One build: the one the pack pins (DemulBuild). The identifier is the download URL.</summary>
        public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions()
        {
            Log.Info("GetInstallableVersions: asked");
            return new List<EmulatorControllerVersion> { new EmulatorControllerVersion(DemulBuild.Url, DemulBuild.Version, DemulBuild.FileName) };
        }

        /// <summary>THE DOWNLOAD BUTTON OF ADD EMULATOR IS THIS (measured 24/09, see README): the host asks with an
        /// EMPTY path and shows the button only on true. Three answers: nothing installed - yes; installed, but
        /// not by this plugin - no, it is the user's; installed by us and older than the pinned build - yes.</summary>
        public override bool IsUpdateAvailable(string emulatorAppPath, out EmulatorControllerVersion version)
        {
            version = null;
            try
            {
                version = GetInstallableVersions().FirstOrDefault();
                if (string.IsNullOrWhiteSpace(emulatorAppPath)) return true;
                var exe = ResolveFullPath(emulatorAppPath);
                if (!DemulBuild.IsOurs(exe)) return false;
                return DemulBuild.IsNewer(DemulBuild.Version, DemulBuild.InstalledVersion(exe));
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
                if (string.IsNullOrWhiteSpace(targetDir))
                    return new EmulatorInstallResponse("Couldn't work out where to install Demul.");

                Report(args, "Downloading Demul...", 0);
                archive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".7z");
                DemulBuild.Fetch(archive, p => Report(args, "Downloading Demul...", p),
                    () => { try { return args?.ShouldCancelFunc?.Invoke() ?? false; } catch { return false; } });

                Report(args, "Extracting Demul...", null);
                Directory.CreateDirectory(targetDir);
                Archives.ExtractOver(archive, targetDir);

                var exe = Path.Combine(targetDir, ExecutableName);
                if (!File.Exists(exe))
                    return new EmulatorInstallResponse("Demul was downloaded and extracted to " + targetDir + " but " + ExecutableName + " is not there.");
                DemulBuild.Stamp(targetDir);
                Log.Info("installed " + DemulBuild.Version + " to " + targetDir);
                // The state keys at once, so a Demul opened by itself has them too - and again at each launch.
                DemulHotkeys.Ensure(targetDir);

                // THE USER'S BIOS SETS into roms\, from where they already are - only what roms\ is missing. See DemulBios.
                DemulBios.Import(targetDir, null, m => Report(args, m, null),
                    () => { try { return args?.ShouldCancelFunc?.Invoke() ?? false; } catch { return false; } });

                if (reinstall)
                {
                    try { args.ExistingEmulator.ApplicationPath = MakeRelativeToLaunchBox(exe); } catch { }
                    return new EmulatorInstallResponse(args.ExistingEmulator, "Demul updated.");
                }
                if (args != null && !args.ShouldCreateEmulator)
                    return new EmulatorInstallResponse("Demul was installed to " + targetDir + ", but no emulator entry was requested.");

                var created = CreateEmulator(exe);
                if (created == null)
                    return new EmulatorInstallResponse("Demul was installed to " + targetDir + " but the emulator entry couldn't be created (no data manager available).");
                var missing = DemulBios.Sets.Where(n => !File.Exists(Path.Combine(targetDir, DemulLaunch.BiosFolder, n))).ToList();
                return new EmulatorInstallResponse(created, "Demul installed."
                    + (missing.Count == 0 ? "" : " BIOS sets not found on this computer, to put in " + Path.Combine(targetDir, DemulLaunch.BiosFolder)
                                                 + ": " + string.Join(", ", missing) + "."));
            }
            catch (OperationCanceledException) { return new EmulatorInstallResponse("Installation cancelled."); }
            catch (Exception ex)
            {
                Log.Warn("install failed", ex);
                return new EmulatorInstallResponse("Failed to install Demul: " + ex.Message);
            }
            finally
            {
                try { if (archive != null && File.Exists(archive)) File.Delete(archive); } catch { }
            }
        }

        /// <summary>A new entry with every platform of ours (DemulPlatforms.Wanted). Only on the entry this plugin
        /// creates: an existing emulator's platforms are never touched.</summary>
        private static IEmulator CreateEmulator(string exePath)
        {
            var dm = PluginHelper.DataManager;
            if (dm == null) return null;

            var emu = dm.AddNewEmulator();
            emu.Title = PackName;
            emu.ApplicationPath = MakeRelativeToLaunchBox(exePath);
            emu.CommandLine = DefaultCommandLine;

            // DefaultPlatform IS NOT SET - see XeniaPlugin.CreateEmulator: the Edit Emulator window grows a
            // duplicate platform row from it.
            var added = new List<string>();
            foreach (var name in DemulPlatforms.Wanted())
            {
                var platform = emu.AddNewEmulatorPlatform();
                platform.Platform = name;
                // Default for the boards Flycast does not run; the Flycast plugin already offers itself for the rest.
                platform.IsDefault = DemulPlatforms.SystemOf(name) is "hikaru" or "cave3rd" or "gaelco";
                added.Add(name);
            }

            try { dm.Save(false); } catch (Exception ex) { Log.Warn("data manager save failed", ex); }
            Log.Info("created emulator entry \"" + PackName + "\" -> " + emu.ApplicationPath + ", platforms: " + string.Join(", ", added));
            return emu;
        }

        // ── BIOS ─────────────────────────────────────────────────────────────

        /// <summary>The BIOS romsets, in the emulator's roms\ folder - the first folder DemulLaunch puts in
        /// Demul.ini. The set names are the ones in demul.exe ("dc", "naomi", "naomi2", "awbios", "hikaru",
        /// "segasp"); no checksum is declared for a zip, whose bytes vary with whoever packed it.
        /// CV1000 and Gaelco: none - their sets carry everything they need.</summary>
        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string platform)
        {
            var system = DemulPlatforms.SystemOf(platform);
            if (DemulPlatforms.IsDreamcast(platform)) return new[] { Zip("dc.zip", "Dreamcast BIOS - Demul has no built-in BIOS") };
            if (DemulPlatforms.IsSystemSp(platform)) return new[] { Zip("segasp.zip", "System SP BIOS"), Zip("naomi.zip", "Naomi BIOS") };
            if (string.Equals((platform ?? "").Trim(), DemulPlatforms.Naomi2, StringComparison.InvariantCultureIgnoreCase))
                return new[] { Zip("naomi2.zip", "Naomi 2 BIOS") };
            return system switch
            {
                "naomi" => new[] { Zip("naomi.zip", "Naomi BIOS"), Zip("naomigd.zip", "Naomi GD-ROM BIOS - for the GD-ROM games only", required: false) },
                "awave" => new[] { Zip("awbios.zip", "Atomiswave BIOS") },
                "hikaru" => new[] { Zip("hikaru.zip", "Hikaru BIOS") },
                _ => Array.Empty<EmulatorBiosFile>(),
            };
        }

        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string emulatorApplicationPath, string platform, string commandLine)
            => GetBiosFilesForPlatform(platform);

        private static EmulatorBiosFile Zip(string fileName, string description, bool required = true)
            => new EmulatorBiosFile(DemulLaunch.BiosFolder, fileName, required, description + " (MAME romset)", null, null);

        public override RetroAchievementSupportResponse SupportsRetroAchievements(string emulatorApplicationPath)
            => new RetroAchievementSupportResponse(isSupported: false);

        // ── launch ───────────────────────────────────────────────────────────

        /// <summary>One launch at a time - LbipLaunchGate.</summary>
        public override PrepareForLaunchResponse PrepareEmulatorForLaunch(PrepareForLaunchArgs args)
            => LbipLaunchGate.Run(PackName, args, PrepareCore, null);

        private PrepareForLaunchResponse PrepareCore(PrepareForLaunchArgs args)
        {
            try
            {
                var exe = ResolveFullPath(Safe(() => args?.EmulatorBeingLaunched?.ApplicationPath));
                if (string.IsNullOrWhiteSpace(exe) || !IsDemul(exe)) return new PrepareForLaunchResponse(success: true);
                var rom = ResolveFullPath(Safe(() => args?.GameBeingLaunched?.ApplicationPath));
                var platform = Safe(() => args?.GameBeingLaunched?.Platform);
                var system = DemulPlatforms.SystemOf(platform);
                if (system == null)
                    return Refuse("Demul doesn't know which system to start for the platform \"" + platform + "\".\n\n"
                                  + "Name the platform with \"System SP\", \"CV1000\" or \"Gaelco\" in it for those boards.");
                if (string.IsNullOrWhiteSpace(rom)) return Refuse("No game was given to Demul.");

                // Its BIOS set, looked for once more when roms\ does not have it: one added to the library since the install.
                var bios = DemulBios.For(system, DemulPlatforms.BoardOf(platform));
                if (bios != null) DemulBios.Import(Path.GetDirectoryName(exe), new[] { bios });

                // A Dreamcast CHD Demul cannot read (a zstd one: its reader crashes) handed over uncompressed - served where it
                // is, else copied (DemulChd); a CHD it reads, and every other file, as it is.
                string disc = rom, served = null;
                if (system == "dc")
                {
                    disc = DemulChd.Present(rom, exe, out served, out var problem);
                    if (disc == null) return Refuse(problem);
                }
                try
                {
                    DemulLaunch.PrepareIni(exe, system, disc, Safe(() => args?.GameBeingLaunched?.Id));
                    // The state keys in padDemul.ini, when Demul has none (Mehdi, 07/10) - see DemulHotkeys. Never throws.
                    DemulHotkeys.Ensure(Path.GetDirectoryName(exe));
                    // A PNG of the screen beside each state Demul writes (Mehdi, 07/10) - see WatchStateThumbs.
                    WatchStateThumbs(exe);
                    var line = DemulLaunch.CommandLine(Safe(() => args?.CurrentCommandLine) ?? "", system, disc);
                    Log.Info("command line: " + line);
                    DemulSession.Watch(exe, served);
                    served = null;
                    return new PrepareForLaunchResponse(success: true) { NewCommandLine = line };
                }
                finally { DemulChd.Release(served); }
            }
            catch (Exception ex)
            {
                Log.Warn("PrepareEmulatorForLaunch", ex);
                return Refuse("Something went wrong while preparing the game for Demul: " + ex.Message);
            }
        }

        /// <summary>Demul keeps its states in sstates\ beside demul.exe (the folder its archive ships, and the "sstates\"
        /// in demul.exe), and draws no picture of them: the window is captured as a state is written there
        /// (LbipStateThumbs.Watch), &lt;state&gt;.png beside it. Every file written there is taken for a state - their
        /// names are not measured yet - but our PNGs and their .part.</summary>
        private static void WatchStateThumbs(string exe)
        {
            try
            {
                var dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)) ?? "", StatesFolder);
                LbipStateThumbs.Watch(exe, new[] { dir }, IsStateFile, Log.Info);
            }
            catch (Exception ex) { Log.Warn("state thumbnails", ex); }
        }

        private const string StatesFolder = "sstates";

        /// <summary>A file in sstates\ that is a state: not one of our thumbnails, nor the one being written.</summary>
        internal static bool IsStateFile(string path)
        {
            var name = Path.GetFileName(path ?? "");
            return name.Length > 0 && !LbipStateThumbs.IsThumb(name)
                   && !name.EndsWith(LbipStateThumbs.Suffix + ".part", StringComparison.OrdinalIgnoreCase);
        }

        private static PrepareForLaunchResponse Refuse(string message)
        {
            Log.Info("launch refused: " + message.Replace("\n", " "));
            LbipNotice.Show(PackName, message);
            return new PrepareForLaunchResponse(success: false);
        }

        private static T Safe<T>(Func<T> get) { try { return get(); } catch { return default; } }

        // ── paths ────────────────────────────────────────────────────────────

        private static string LaunchBoxRoot()
        {
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(Assembly.GetExecutingAssembly().Location));
                for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
                {
                    if (Directory.Exists(Path.Combine(dir, "Core")) && Directory.Exists(Path.Combine(dir, "Data")))
                        return dir;
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
