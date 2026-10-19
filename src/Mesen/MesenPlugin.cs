// Mesen integration for LaunchBox / LiteBox - Sour's accurate multi-system emulator, carried on as MesenCE.
//
// Claim the emulator (MesenLaunch.IsMesenExe: Mesen 2 or later, not the old NES-only 0.9.x of the same exe name), install
// and update MesenCE's latest release (MesenBuild), describe it to the host's catalogue for EVERY platform it runs
// (MesenSystems: NES, Famicom Disk System, Super Nintendo, Satellaview, Sufami Turbo, Game Boy / Color / Super Game Boy,
// Game Boy Advance, PC Engine / SuperGrafx / CD, Master System, Game Gear, SG-1000, ColecoVision, WonderSwan / Color, Pocket
// Challenge V2), and at each launch: the game's SYSTEM (MesenLaunch.SystemFor - its own choice, its platform's line, the
// table), its BIOS (MesenBios), the pack's hotkeys and the platform's save folders in settings.json, put back after the
// session with the line's switches (MesenSession), and the line.
//
// THE PLATFORM LINES, AS ARES'S: a platform row whose console has models carries the model the platform means
// (--gameboy.model=AutoFavorGb on the Game Boy's...) - written on the rows of an emulator the plugin creates, and on a row
// whose line is still empty when the host hands the emulator over; a line the user wrote is never rewritten.
//
// THE PLATFORM ROWS: made when the plugin creates the emulator, one per platform (LiteBox's other spellings only when the
// library has that platform); IsDefault only where no emulator of the library is yet. A row that is there is NEVER removed nor
// unticked, and none is added afterwards to an emulator entry - a platform the user took off is his choice
// (never-touch-existing-platform-rows).
//
// NO RETROACHIEVEMENTS: Mesen has none (no RetroAchievements code at either tag). NO SAVE LISTING YET (the next lot):
// LaunchBox's own save management sees nothing of Mesen's until then; the files themselves are in Saves\<platform>.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;
using LbIntegrations.Catalog;
using LbIntegrations.Lbip;
using LbIntegrations.Saves;

namespace LbIntegrations.Mesen
{
    public partial class MesenPlugin : EmulatorPlugin, ISystemEventsPlugin, ILbCatalogSource
    {
        /// <summary>Nothing: the host appends the game's path; the switches are added at each launch.</summary>
        private const string DefaultCommandLine = "";
        internal const string PackName = "Nixx-Mesen";
        private const string RowsId = "com.nixxou.lbip.mesen";

        public MesenPlugin()
        {
            // First: the save front desk registers this plugin for the others (Shared.Saves SaveDesk).
            _desk = new SaveDesk(this, this, SaveEmu, SaveLog);
            Log.Info("plugin constructed, assembly " + typeof(MesenPlugin).Assembly.Location);
            LbipLog.Use(Log.Info, Log.Warn, Log.Disabled, () => Log.Tracing);
            LbipRowInjection.Install(RowsId, MetadataRows());
            // A session LaunchBox did not see the end of (it was closed or crashed while Mesen ran): Mesen's settings put back.
            try { MesenSession.Recover(); } catch (Exception ex) { Log.Warn("session recovery at start", ex); }
        }

        /// <summary>LaunchBox's metadata has no Mesen row (18/10). AutoExtract is false: Mesen opens .zip and .7z itself and
        /// names the saves after the game inside.</summary>
        private static IEnumerable<LbCatalogEmulator> MetadataRows()
        {
            var row = new LbCatalogEmulator
            {
                Name = PackName,
                CommandLine = DefaultCommandLine,
                ApplicableFileExtensions = MesenSystems.EveryExtension(),
                Url = MesenBuild.Home,
                BinaryFileName = MesenLaunch.ExecutableName,
                AutoExtract = false,
            };
            foreach (var p in MesenSystems.Platforms)
                row.Platforms.Add(new LbCatalogPlatform { Platform = p.Platform, ApplicableFileExtensions = MesenSystems.AllExtensions(p), Recommended = false, RequiredBiosFile = RequiredBios(p) });
            yield return row;
        }

        /// <summary>The BIOS file a platform's games all need (its default system's), for the catalogue row; null for none.</summary>
        private static string RequiredBios(MesenPlatform p)
        {
            var s = MesenSystems.System(p.Default);
            return s?.Firmware.FirstOrDefault(f => !f.Optional && f != MesenSystems.Sgb1)?.File;
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
                LbipRowInjection.Install(RowsId, MetadataRows());
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
                if (!MesenLaunch.IsMesenExe(path)) continue;
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
                if (Fill(() => emu.AutoHotkeyScript, v => emu.AutoHotkeyScript = v, MesenAhk.Running)) set.Add("running");
                if (Fill(() => emu.ExitAutoHotkeyScript, v => emu.ExitAutoHotkeyScript = v, MesenAhk.Exit)) set.Add("exit");
                if (Fill(() => emu.SaveStateAutoHotkeyScript, v => emu.SaveStateAutoHotkeyScript = v, MesenAhk.SaveState)) set.Add("save");
                if (Fill(() => emu.LoadStateAutoHotkeyScript, v => emu.LoadStateAutoHotkeyScript = v, MesenAhk.LoadState)) set.Add("load");
                if (set.Count > 0) Log.Info("hotkey scripts set: " + string.Join(", ", set));
            }
            catch (Exception ex) { Log.Warn("could not describe the hotkeys on the emulator entry", ex); }
        }

        /// <summary>A script set when empty - or when it is, word for word, one an earlier build of this plugin wrote
        /// (MesenAhk.Superseded). One the user changed is never touched.</summary>
        private static bool Fill(Func<string> get, Action<string> set, string value)
        {
            try
            {
                var now = get();
                string Norm(string s) => (s ?? "").Replace("\r\n", "\n").Trim();
                bool ours = MesenAhk.Superseded.Any(old => Norm(old) == Norm(now));
                if (!string.IsNullOrWhiteSpace(now) && !ours) return false;
                if (Norm(now) == Norm(value)) return false;
                set(value);
                return true;
            }
            catch { return false; }
        }

        private static string Safe(Func<string> get) { try { return get(); } catch { return null; } }

        public override EmulatorSupportResponse IsPlatformSupported(string platform)
        {
            bool supported = MesenSystems.Platform(platform) != null;
            // Never recommended (Mehdi, 17/10): LaunchBox pushes a recommended emulator - in the import wizard too, not installed yet.
            return new EmulatorSupportResponse(supported, false);
        }

        // ── versions ─────────────────────────────────────────────────────────

        /// <summary>The version of an install of OURS (its stamp), else null - a Mesen the user installed is his to update.</summary>
        public override string GetCurrentVersion(string applicationPath) => MesenBuild.InstalledVersion(ResolveFullPath(applicationPath));

        public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions()
        {
            var r = MesenBuild.Latest();
            if (r == null) return null;
            return new List<EmulatorControllerVersion> { new EmulatorControllerVersion(r.Url, r.Version ?? "latest", r.FileName) };
        }

        /// <summary>THE DOWNLOAD BUTTON OF ADD EMULATOR IS THIS (DemulPlugin, measured 24/09): an empty path - nothing installed
        /// yet - true; installed but not by us - false, it is the user's; ours and older than the latest - true.</summary>
        public override bool IsUpdateAvailable(string emulatorAppPath, out EmulatorControllerVersion version)
        {
            version = null;
            try
            {
                version = GetInstallableVersions()?.FirstOrDefault();
                if (version == null) return string.IsNullOrWhiteSpace(emulatorAppPath);
                if (string.IsNullOrWhiteSpace(emulatorAppPath)) return true;
                var current = GetCurrentVersion(emulatorAppPath);
                return current != null && MesenBuild.IsNewer(version.Label, current);
            }
            catch (Exception ex) { Log.Warn("update check failed", ex); return false; }
        }

        // ── installation ─────────────────────────────────────────────────────

        public override EmulatorInstallResponse InstallEmulator(InstallEmulatorArgs args)
        {
            string archive = null;
            try
            {
                var release = MesenBuild.Latest();
                if (release == null) return new EmulatorInstallResponse("Couldn't read MesenCE's latest release on GitHub. Try again later.");

                bool reinstall = args?.ExistingEmulator != null;
                string targetDir = reinstall
                    ? Path.GetDirectoryName(ResolveFullPath(args.ExistingEmulator.ApplicationPath))
                    : Path.Combine(LaunchBoxRoot(), "Emulators", PackName);
                if (string.IsNullOrWhiteSpace(targetDir)) return new EmulatorInstallResponse("Couldn't work out where to install Mesen.");
                var exe = Path.Combine(targetDir, MesenLaunch.ExecutableName);
                if (MesenLaunch.Running(exe)) return new EmulatorInstallResponse("Mesen is running from " + targetDir + ": close it, then update again.");

                Report(args, "Downloading MesenCE " + release.Version + "... " + MesenBuild.RuntimeNote, 0);
                archive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                MesenBuild.Fetch(release, archive, p => Report(args, "Downloading MesenCE " + release.Version + "...", p),
                    () => { try { return args?.ShouldCancelFunc?.Invoke() ?? false; } catch { return false; } });

                Report(args, "Extracting Mesen...", null);
                Archives.ExtractOver(archive, targetDir);
                if (!File.Exists(exe)) return new EmulatorInstallResponse("Mesen was extracted to " + targetDir + " but " + MesenLaunch.ExecutableName + " is not there.");
                MesenBuild.Stamp(targetDir, release.Version);
                Log.Info("installed MesenCE " + release.Version + " to " + targetDir);

                // Its first settings when it has none anywhere: else its setup wizard comes up and the first game is ignored.
                MesenSession.Recover();
                MesenLaunch.Bootstrap(exe);
                var json = MesenJson.Load(MesenLaunch.SettingsOf(exe));
                var bound = MesenLaunch.BindPackKeys(json);
                if (json.SaveIfChanged() && bound.Count > 0) Log.Info("settings.json: the pack's keys bound - " + string.Join(", ", bound));

                // Every BIOS the user already has, copied into Mesen's Firmware folder now.
                Report(args, "Looking for the BIOS files...", null);
                var bios = MesenBios.Ensure(exe, MesenSystems.All, m => Report(args, m, null));
                var needed = bios.Where(b => b.Required).GroupBy(b => b.Spec.File).Select(g => g.First()).ToList();
                var note = " " + needed.Count(b => b.Path != null && b.SizeOk) + " of " + needed.Count + " BIOS files found. " + MesenBuild.RuntimeNote;

                if (reinstall)
                {
                    try { args.ExistingEmulator.ApplicationPath = MakeRelativeToLaunchBox(exe); } catch { }
                    return new EmulatorInstallResponse(args.ExistingEmulator, "Mesen updated to MesenCE " + release.Version + "." + note);
                }
                if (args != null && !args.ShouldCreateEmulator)
                    return new EmulatorInstallResponse("Mesen was installed to " + targetDir + ", but no emulator entry was requested." + note);

                var created = CreateEmulator(exe);
                if (created == null)
                    return new EmulatorInstallResponse("Mesen was installed to " + targetDir + " but the emulator entry couldn't be created (no data manager available).");
                return new EmulatorInstallResponse(created, "MesenCE " + release.Version + " installed." + note);
            }
            catch (OperationCanceledException) { return new EmulatorInstallResponse("Installation cancelled."); }
            catch (Exception ex)
            {
                Log.Warn("install failed", ex);
                return new EmulatorInstallResponse("Failed to install Mesen: " + ex.Message);
            }
            finally
            {
                try { if (archive != null && File.Exists(archive)) File.Delete(archive); } catch { }
            }
        }

        /// <summary>The platforms a new emulator entry gets: every one of the table, LiteBox's other spellings only when the
        /// library has them.</summary>
        internal static List<MesenPlatform> PlatformsForNewEntry(IEnumerable<string> libraryPlatforms)
        {
            var have = new HashSet<string>((libraryPlatforms ?? Enumerable.Empty<string>()).Where(n => n != null).Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
            return MesenSystems.Platforms.Where(p => !p.OnlyIfInLibrary || have.Contains(p.Platform)).ToList();
        }

        /// <summary>The emulator with a row for each platform, the model on the line where the platform means one. The default
        /// of a platform only when no emulator of the library is yet - an existing choice is the user's.</summary>
        private static IEmulator CreateEmulator(string exePath)
        {
            var dm = PluginHelper.DataManager;
            if (dm == null) return null;
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var library = new List<string>();
            try
            {
                foreach (var e in dm.GetAllEmulators() ?? Array.Empty<IEmulator>())
                    foreach (var r in e.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>())
                        try { if (r.IsDefault && !string.IsNullOrEmpty(r.Platform)) taken.Add(r.Platform); } catch { }
            }
            catch { }
            try { foreach (var p in dm.GetAllPlatforms() ?? Array.Empty<IPlatform>()) try { library.Add(p.Name); } catch { } } catch { }

            var emu = dm.AddNewEmulator();
            emu.Title = PackName;
            emu.ApplicationPath = MakeRelativeToLaunchBox(exePath);
            emu.CommandLine = DefaultCommandLine;
            EnsureHotkeyScripts(emu);
            var defaults = new List<string>();
            var platforms = PlatformsForNewEntry(library);
            foreach (var p in platforms)
            {
                // DefaultPlatform IS NOT SET - see XeniaPlugin.CreateEmulator. IsDefault on the row is what counts.
                var row = emu.AddNewEmulatorPlatform();
                row.Platform = p.Platform;
                row.CommandLine = "";   // no model switch: Mesen's own detection (Mehdi 19/10)
                row.IsDefault = !taken.Contains(p.Platform);
                if (row.IsDefault) defaults.Add(p.Platform);
            }
            try { dm.Save(false); } catch (Exception ex) { Log.Warn("data manager save failed", ex); }
            Log.Info("created emulator entry \"" + PackName + "\" -> " + emu.ApplicationPath + ", " + platforms.Count + " platforms, default for "
                     + (defaults.Count == 0 ? "none" : string.Join(", ", defaults)));
            return emu;
        }

        // ── BIOS ─────────────────────────────────────────────────────────────

        /// <summary>The firmware of the platform's systems, in Mesen's Firmware folder - what the plugin copies there. Required:
        /// what the platform's DEFAULT system cannot start without (a Famicom Disk System game, not every NES game).</summary>
        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string platform)
        {
            var p = MesenSystems.Platform(platform);
            if (p == null) return Array.Empty<EmulatorBiosFile>();
            var list = new List<EmulatorBiosFile>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in p.Systems.Select(MesenSystems.System).Where(s => s != null))
                foreach (var f in s.Firmware)
                {
                    if (!seen.Add(f.File)) continue;
                    bool required = s.Key == p.Default && !f.Optional && f != MesenSystems.Sgb1;
                    var also = f.Names.Skip(1).ToList();
                    list.Add(new EmulatorBiosFile(MesenBios.Folder, f.File, required,
                        f.Label + " - " + (f.Optional ? "optional" : "needed by " + s.Name + " games") + (also.Count > 0 ? " (also found as " + string.Join(", ", also) + ")" : ""), null, null));
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
            if (!MesenLaunch.IsMesenExe(exe) || !File.Exists(exe)) return new PrepareForLaunchResponse(success: true);
            bool running = MesenLaunch.Running(exe);
            if (running) Log.Warn("Mesen is already running from " + Path.GetDirectoryName(exe) + " - the game goes to it, nothing is written");
            string Caption() => PackName + " - " + (title ?? Path.GetFileName(rom ?? ""));
            PrepareForLaunchResponse Refuse(string text) { LbipNotice.Show(Caption(), text); return new PrepareForLaunchResponse(success: false); }

            // A session LaunchBox did not see the end of: put back first.
            if (!running) MesenSession.Recover();

            // What Mesen loads: the game, or the one of a several-game archive.
            string played = rom;
            if (!string.IsNullOrEmpty(rom))
            {
                played = MesenLaunch.RomFor(rom, exe, out var problem);
                if (played == null) return Refuse("This game cannot be started: " + problem);
            }
            var inner = string.IsNullOrEmpty(played) ? rom : MesenLaunch.Inner(played);
            var ext = Path.GetExtension(inner ?? "").ToLowerInvariant();

            var current = args?.CurrentCommandLine ?? "";
            var choice = MesenLaunch.SystemFor(gameId, platform, ext, current);
            var system = choice.System;
            if (system == null)
                return Refuse("Mesen does not know which console this game is for: its file (" + Path.GetFileName(inner ?? "") + ") is none Mesen runs, and its platform ("
                              + platform + ") is not one this plugin maps.");
            Log.Info("launch: " + Path.GetFileName(inner ?? "") + " as " + system.Name + " (from " + choice.From + ")");
            if (system.CeOnly && !MesenLaunch.IsCe(exe))
                return Refuse(system.Name + " games need MesenCE (2.2 or later); this Mesen is " + MesenLaunch.VersionOf(exe) + ". Update it from LaunchBox's emulator window.");
            if (system.Disc && (MesenLaunch.IsArchive(rom) || !system.Extensions.Contains(ext)))
                return Refuse("Mesen opens a " + system.Name + " disc only through its .cue (not a .chd, an .iso alone, a .m3u nor an archive): point the game at the disc's .cue.");

            // Its BIOS.
            var bios = MesenBios.Ensure(exe, new[] { system }, optional: false);
            if (!MesenBios.Enough(bios))
            {
                var names = string.Join("\n", MesenBios.Missing(bios).Select(b => "  " + b.Spec.Label + ": " + b.Spec.File + (b.Spec.Alt != null ? " (or " + b.Spec.Alt + ")" : "")
                                                                         + (b.Path != null ? " - the one there is not a size Mesen reads" : "")));
                return Refuse(system.Name + " needs its BIOS, and it was not found.\n\nPut it in " + MesenBios.FolderOf(exe) + ":\n\n" + names
                              + "\n\n(under that name), or in RetroArch's system folder - it is copied at the next launch.");
            }

            // settings.json: its first one if there is none, the pack's keys, the platform's folders - nothing while Mesen runs
            // (it rewrites the file when it quits). What the session forces is put back after it (MesenSession).
            bool ourFolders = false;
            var forced = new List<MesenSession.Forced>();
            var json = (MesenJson)null;
            if (!running)
            {
                MesenLaunch.Bootstrap(exe);
                json = MesenJson.Load(MesenLaunch.SettingsOf(exe));
                var bound = MesenLaunch.BindPackKeys(json);
                if (bound.Count > 0) Log.Info("settings.json: the pack's keys bound - " + string.Join(", ", bound));
                ourFolders = MesenSavePaths.Ensure(json, platform, forced);
            }

            var line = MesenLaunch.CommandLine(current, MesenSettings.BoolFor(gameId, "Fullscreen"), choice.Add, choice.Remove, rom, played == rom ? null : played);
            if (json != null)
            {
                forced.AddRange(MesenSession.FromLine(json, line));
                try { json.SaveIfChanged(); }
                catch (Exception ex) { Log.Warn("settings.json could not be written - the game starts with it as it was", ex); }
                // Once Mesen has quit: the settings put back, the game's states given their picture.
                var (romNow, platformNow) = (rom, platform);
                MesenSession.Begin(exe, json.Path, forced, () => MesenStateThumbs.RefreshAll(exe, StatesOf(exe, romNow, platformNow)));
                // What Mesen wrote for this game in its own flat folders, into the platform's.
                if (ourFolders) MesenSavePaths.Migrate(exe, platform, system, Path.GetFileNameWithoutExtension(inner ?? ""));
            }

            if (line != current.Trim())
            {
                Log.Info("launch: " + line);
                return new PrepareForLaunchResponse(success: true) { NewCommandLine = line };
            }
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
                var p = maybeRelative.Trim().Trim('"');
                return Path.IsPathRooted(p) ? p : Path.GetFullPath(Path.Combine(LaunchBoxRoot(), p));
            }
            catch { return maybeRelative; }
        }

        private static void Report(InstallEmulatorArgs args, string message, double? progress)
        {
            try { args?.ReportProgressAction?.Invoke(message, null, progress); } catch { }
        }
    }
}
