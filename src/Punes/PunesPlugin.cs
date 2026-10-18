// puNES integration for LaunchBox / LiteBox - the accurate Nintendo Entertainment System, Famicom and Famicom Disk System
// emulator (Fabio Cavallo, "FHorse"; GPL-2.0-or-later).
//
// Claim the emulator, install the one pinned build (PunesBuild), describe it to the host's catalogue for every platform it
// runs (PunesSystems), and at each launch: the game's file as puNES must be given it (an archive unpacked when needed),
// the system it is, the Famicom Disk System's BIOS checked (PunesFirmware), the pack's free hotkeys bound, the game's
// options on the line (PunesSettings, PunesLaunch). Everything measured on punes64.exe 0.111 or read in its source at the
// tag is in docs\punes.md.
//
// THE SYSTEM is puNES's own choice from the file (no --system switch exists - PunesSystems): nothing is written on the
// platform rows' lines.
//
// SAVES: puNES keeps them in FIXED folders of its data folder - prb\ (cartridges), save\ (states), diff\ (disk writes),
// screenshot\ - with no setting for any of them (src\core\common.h:35-48; the whole puNES.cfg read). So NOTHING EVER
// LANDS BESIDE THE GAME (lot 9 holds by construction, nothing to bring back from there) - but per-platform folders cannot
// be had through a setting either: docs\punes.md section 14, question 1.
//
// NO RETROACHIEVEMENTS: there is no rcheevos code in puNES (grep of the source at v0.111).
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

namespace LbIntegrations.Punes
{
    public partial class PunesPlugin : EmulatorPlugin, ISystemEventsPlugin, ILbCatalogSource
    {
        /// <summary>Nothing: the host appends the game's path ("punes64.exe &lt;rom&gt;"); the options go in front at each launch.</summary>
        private const string DefaultCommandLine = "";

        /// <summary>The name this pack publishes under: the catalogue row, the Add Emulator entry, the title of an emulator
        /// this plugin creates, and the folder it is installed into.</summary>
        internal const string PackName = "Nixx-puNES";

        public PunesPlugin()
        {
            Log.Info("plugin constructed, assembly " + typeof(PunesPlugin).Assembly.Location);
            LbipLog.Use(Log.Info, Log.Warn, Log.Disabled, () => Log.Tracing);
            // As early as possible: the patch only sees connections opened AFTER it is installed.
            LbipRowInjection.Install("com.nixxou.lbip.punes", MetadataRows());
        }

        /// <summary>LaunchBox's metadata has no puNES row (18/10: FCEUX and Nestopia only); this pack publishes its own. One
        /// platform each with its own extensions. AutoExtract is false: puNES opens a .zip and a .7z itself, and the plugin
        /// unpacks what it should not open (PunesLaunch.RomFor) - LaunchBox's own extraction would name the save after a
        /// file in %TEMP%.</summary>
        private static IEnumerable<LbCatalogEmulator> MetadataRows()
        {
            var row = new LbCatalogEmulator
            {
                Name = PackName,
                CommandLine = DefaultCommandLine,
                ApplicableFileExtensions = PunesSystems.EveryExtension(),
                Url = PunesBuild.Home,
                BinaryFileName = PunesLaunch.ExecutableName,
                AutoExtract = false,
            };
            foreach (var p in PunesSystems.Platforms)
                row.Platforms.Add(new LbCatalogPlatform
                {
                    Platform = p.Platform, ApplicableFileExtensions = PunesSystems.AllExtensions(p), Recommended = false,
                    RequiredBiosFile = PunesSystems.System(p.Default)?.Bios.FirstOrDefault(b => b.Required)?.Name,
                });
            yield return row;
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
                LbipRowInjection.Install("com.nixxou.lbip.punes", MetadataRows());
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
                if (!PunesLaunch.IsPunesExe(path)) continue;
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

        /// <summary>puNES's keys in the AutoHotkey fields the pause screens send. Only a blank field, or one still holding an
        /// earlier build's script, is filled: a script the user wrote is his answer to the same question.</summary>
        private static void EnsureHotkeyScripts(IEmulator emu)
        {
            if (!LbIntegrations.Compat.Compat.On("emulatorFields")) return;   // the Compatibility page
            try
            {
                string exeName = null;
                try { exeName = Path.GetFileName((emu.ApplicationPath ?? "").Trim().Trim('"')); } catch { }
                var set = new List<string>();
                if (Fill(() => emu.AutoHotkeyScript, v => emu.AutoHotkeyScript = v, PunesAhk.Running)) set.Add("running");
                if (Fill(() => emu.ExitAutoHotkeyScript, v => emu.ExitAutoHotkeyScript = v, PunesAhk.Exit(exeName))) set.Add("exit");
                if (Fill(() => emu.SaveStateAutoHotkeyScript, v => emu.SaveStateAutoHotkeyScript = v, PunesAhk.SaveState)) set.Add("save");
                if (Fill(() => emu.LoadStateAutoHotkeyScript, v => emu.LoadStateAutoHotkeyScript = v, PunesAhk.LoadState)) set.Add("load");
                if (set.Count > 0) Log.Info("hotkey scripts set: " + string.Join(", ", set));
            }
            catch (Exception ex) { Log.Warn("could not describe the hotkeys on the emulator entry", ex); }
        }

        /// <summary>A script set when empty - or when it is, word for word, one an earlier build of this plugin wrote
        /// (PunesAhk.Superseded). One the user changed is never touched.</summary>
        private static bool Fill(Func<string> get, Action<string> set, string value)
        {
            try
            {
                var now = get();
                string Norm(string s) => (s ?? "").Replace("\r\n", "\n").Trim();
                bool ours = PunesAhk.Superseded.Any(old => Norm(old) == Norm(now));
                if (!string.IsNullOrWhiteSpace(now) && !ours) return false;
                if (Norm(now) == Norm(value)) return false;
                set(value);
                return true;
            }
            catch { return false; }
        }

        public override EmulatorSupportResponse IsPlatformSupported(string platform)
        {
            bool supported = PunesSystems.Platform(platform) != null;
            Log.Verbose("IsPlatformSupported(\"" + platform + "\") -> " + supported);
            // Never recommended (Mehdi, 17/10): LaunchBox pushes a recommended emulator - in the import wizard too, not installed yet.
            return new EmulatorSupportResponse(supported, false);
        }

        // ── versions ─────────────────────────────────────────────────────────

        public override string GetCurrentVersion(string applicationPath) => PunesBuild.InstalledVersion(ResolveFullPath(applicationPath));

        /// <summary>One build, the pinned one. The identifier is its download, the label its version.</summary>
        public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions()
            => new List<EmulatorControllerVersion> { new EmulatorControllerVersion(PunesBuild.Url, PunesBuild.Version, PunesBuild.FileName) };

        /// <summary>THE DOWNLOAD BUTTON OF ADD EMULATOR IS THIS (DemulPlugin.IsUpdateAvailable, measured 24/09): called with an
        /// EMPTY path, true shows the button. Installed but not by us (no version of ours on disk): false - it is the user's.
        /// Installed by us and older than the pinned build: true.</summary>
        public override bool IsUpdateAvailable(string emulatorAppPath, out EmulatorControllerVersion version)
        {
            version = GetInstallableVersions().First();
            try
            {
                if (string.IsNullOrWhiteSpace(emulatorAppPath)) return true;
                PunesBuild.NoticeLatest();
                var current = GetCurrentVersion(emulatorAppPath);
                return current != null && PunesBuild.IsNewer(PunesBuild.Version, current);
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
                if (string.IsNullOrWhiteSpace(targetDir)) return new EmulatorInstallResponse("Couldn't work out where to install puNES.");
                foreach (var name in PunesLaunch.ExecutableNames)
                    if (PunesLaunch.Running(Path.Combine(targetDir, name)))
                        return new EmulatorInstallResponse("puNES is running from " + targetDir + ": close it, then update again.");

                Report(args, "Downloading puNES " + PunesBuild.Version + "...", 0);
                archive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                PunesBuild.Fetch(archive, p => Report(args, "Downloading puNES " + PunesBuild.Version + "...", p),
                    () => { try { return args?.ShouldCancelFunc?.Invoke() ?? false; } catch { return false; } });

                Report(args, "Extracting puNES...", null);
                Archives.ExtractOver(archive, targetDir);
                var exe = Path.Combine(targetDir, PunesLaunch.ExecutableName);
                if (!File.Exists(exe))
                    return new EmulatorInstallResponse("puNES was extracted to " + targetDir + " but " + PunesLaunch.ExecutableName
                                                       + " is not there - a virus scanner may have quarantined it.");
                PunesBuild.Stamp(targetDir);
                // Portable, so its first start opens no wizard (PunesCfg) - an install of the user's with a configuration kept.
                PunesCfg.EnsurePortable(exe);
                Log.Info("installed puNES " + PunesBuild.Version + " to " + targetDir);

                // The Famicom Disk System's BIOS, when the user already has it somewhere - see PunesFirmware.
                Report(args, "Looking for the Famicom Disk System BIOS...", null);
                var bios = PunesFirmware.Ensure(exe, PunesSystems.All, m => Report(args, m, null));
                var note = bios.All(b => b.Path != null) ? " Famicom Disk System BIOS found."
                         : " No Famicom Disk System BIOS found: put disksys.rom in " + PunesFirmware.FolderOf(exe) + " to play disks.";

                if (reinstall)
                {
                    try { args.ExistingEmulator.ApplicationPath = MakeRelativeToLaunchBox(exe); } catch { }
                    return new EmulatorInstallResponse(args.ExistingEmulator, "puNES updated to " + PunesBuild.Version + "." + note);
                }
                if (args != null && !args.ShouldCreateEmulator)
                    return new EmulatorInstallResponse("puNES was installed to " + targetDir + ", but no emulator entry was requested." + note);

                var created = CreateEmulator(exe);
                if (created == null)
                    return new EmulatorInstallResponse("puNES was installed to " + targetDir + " but the emulator entry couldn't be created (no data manager available).");
                return new EmulatorInstallResponse(created, "puNES " + PunesBuild.Version + " installed." + note);
            }
            catch (OperationCanceledException) { return new EmulatorInstallResponse("Installation cancelled."); }
            catch (Exception ex)
            {
                Log.Warn("install failed", ex);
                return new EmulatorInstallResponse("Failed to install puNES: " + ex.Message);
            }
            finally
            {
                try { if (archive != null && File.Exists(archive)) File.Delete(archive); } catch { }
            }
        }

        /// <summary>The emulator with a row for each platform. The default of a platform only when no emulator of the library
        /// is yet - an existing choice is the user's (never-touch-existing-platform-rows: a new entry, nothing of anyone's
        /// changed).</summary>
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
            // DefaultPlatform IS NOT SET - see XeniaPlugin.CreateEmulator: the Edit Emulator window grows a duplicate platform
            // row from it. IsDefault on the row says "the default for that platform".
            foreach (var p in PunesSystems.Platforms)
            {
                var row = emu.AddNewEmulatorPlatform();
                row.Platform = p.Platform;
                row.IsDefault = !taken.Contains(p.Platform);
                if (row.IsDefault) defaults.Add(p.Platform);
            }
            try { dm.Save(false); } catch (Exception ex) { Log.Warn("data manager save failed", ex); }
            Log.Info("created emulator entry \"" + PackName + "\" -> " + emu.ApplicationPath + ", " + PunesSystems.Platforms.Length + " platforms, default for "
                     + (defaults.Count == 0 ? "none" : string.Join(", ", defaults)));
            return emu;
        }

        // ── BIOS ─────────────────────────────────────────────────────────────

        /// <summary>The BIOS of the platform's systems, in puNES's bios folder - where the plugin copies them. Required only
        /// for the platform whose own system needs it (the Famicom Disk System); a disk among NES games lists it as optional.</summary>
        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string platform)
        {
            var p = PunesSystems.Platform(platform);
            if (p == null) return Array.Empty<EmulatorBiosFile>();
            var list = new List<EmulatorBiosFile>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in p.Systems.Concat(p.ByExtension.Values).Distinct())
            {
                var s = PunesSystems.System(id);
                if (s == null) continue;
                foreach (var b in s.Bios)
                    if (seen.Add(b.Name))
                    {
                        bool required = b.Required && id == p.Default;
                        list.Add(new EmulatorBiosFile(PunesFirmware.Folder, b.Name, required,
                            b.Description + (required ? "" : " - for the disks among these games") + (b.Aliases.Length > 0 ? " (also found as " + string.Join(", ", b.Aliases) + ")" : ""), null, null));
                    }
            }
            return list;
        }

        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string emulatorApplicationPath, string platform, string commandLine)
            => GetBiosFilesForPlatform(platform);

        public override RetroAchievementSupportResponse SupportsRetroAchievements(string emulatorApplicationPath)
            => new RetroAchievementSupportResponse(isSupported: false);

        // ── launch ───────────────────────────────────────────────────────────

        /// <summary>One launch at a time for this plugin - see LbipLaunchGate. It also refuses a launch into a puNES already
        /// open: puNES hands a second instance's game to the first one and quits at once (measured 18/10, "allow multiple
        /// instances of the emulator=no", its default) - LaunchBox would see the game end as it starts.</summary>
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
            if (!PunesLaunch.IsPunesExe(exe) || !File.Exists(exe)) return new PrepareForLaunchResponse(success: true);
            var current = args?.CurrentCommandLine ?? "";
            bool running = PunesLaunch.Running(exe);
            if (running) Log.Warn("puNES is already running from " + Path.GetDirectoryName(exe) + " - what is written now is lost when it quits");
            string Who() => PackName + " - " + (title ?? Path.GetFileName(rom));

            // The file puNES is given: the game, or an archive's (as it is, or unpacked into the install).
            var played = PunesLaunch.RomFor(exe, rom, out var inner, out var problem);
            if (played == null)
            {
                LbipNotice.Show(Who(), "This game cannot be started: " + problem);
                return new PrepareForLaunchResponse(success: false);
            }
            var (system, from) = PunesSystems.SystemFor(platform, rom, inner);
            if (system == null)
            {
                LbipNotice.Show(Who(), "puNES does not know what this file is: neither its name (" + Path.GetFileName(rom) + ") nor its platform (" + platform
                                + ") says a NES cartridge or a Famicom Disk System disk. puNES opens .nes, .unf, .unif, .fds and .qd files, and .zip and .7z archives of them.");
                return new PrepareForLaunchResponse(success: false);
            }
            if (!PunesLaunch.Givable(played))
            {
                LbipNotice.Show(Who(), "puNES cannot open a file whose path holds an '=' sign: it cuts every word of its command line at the first '=' - "
                                + "it would start on an empty screen.\n\n" + played + "\n\nRename the file or its folder without '=', then launch the game again.");
                return new PrepareForLaunchResponse(success: false);
            }
            Log.Info("launch: " + Path.GetFileName(rom) + " as " + system.Name + " (from " + from + ")" + (played != rom ? ", given as " + played : ""));

            // Its BIOS: a disk does not start without disksys.rom - and puNES says nothing, it shows an empty screen.
            if (system.Bios.Any(b => b.Required))
            {
                var bios = PunesFirmware.Ensure(exe, new[] { system }, rom: played);
                var missing = bios.Where(b => b.Bios.Required && b.Path == null).ToList();
                if (missing.Count > 0)
                {
                    LbipNotice.Show(Who(), system.Name + " needs its BIOS, and none was found.\n\nPut "
                                    + string.Join(" and ", missing.Select(m => m.Bios.Name + " (" + m.Bios.Size / 1024 + " KB" + (m.Bios.Aliases.Length > 0 ? ", also known as " + string.Join(" or ", m.Bios.Aliases) : "") + ")"))
                                    + " in " + PunesFirmware.FolderOf(exe) + " - or in RetroArch's system folder: it is copied at the next launch.");
                    return new PrepareForLaunchResponse(success: false);
                }
                if (system.Id == PunesSystems.Fds) WarnWriteMode(exe);
            }

            if (!running)
            {
                // Portable, so a first start opens no wizard; the pack's free hotkeys.
                if (!PunesLaunch.PortableOnLine(current)) PunesCfg.EnsurePortable(exe);
                PunesLaunch.BindHotkeys(exe);
            }

            var options = PunesSettings.Args(gameId, system.Id);
            var line = PunesLaunch.CommandLine(current, options, rom, played);
            if (line != current.Trim())
            {
                Log.Info("launch: " + line);
                return new PrepareForLaunchResponse(success: true) { NewCommandLine = line };
            }
            return new PrepareForLaunchResponse(success: true);
        }

        /// <summary>"write mode=original file" ([fds] of puNES.cfg) makes puNES write a disk's changes INTO its image, beside
        /// the game (fds.c:640-662, read) - the default, "diff file", writes them to diff\. Said, not changed: docs\punes.md
        /// section 14, question 5.</summary>
        private static void WarnWriteMode(string exe)
        {
            try
            {
                var mode = PunesCfg.Load(PunesCfg.MainOf(exe)).Get("fds", "write mode");
                if (string.Equals(mode, "original file", StringComparison.OrdinalIgnoreCase))
                    Log.Warn("puNES's [fds] write mode is \"original file\": the disk's changes are written into the game's own image, not into diff\\");
            }
            catch { }
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

        /// <summary>A library path made full - against LaunchBox's root, never the current directory (memory "lb-cwd-is-root":
        /// the probe and LiteBox do not set it there).</summary>
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
