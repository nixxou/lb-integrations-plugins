// Ymir integration for LaunchBox / LiteBox - StrikerX3's Sega Saturn emulator (github.com/ymir-emu/Ymir). docs\ymir.md
// holds the research and what was measured.
//
// Claim the emulator (YmirLaunch.IsYmirExe: ymir-sdl3.exe), install and update Ymir's latest STABLE release (YmirBuild -
// Mehdi 20/10: no nightly), describe it to the host's catalogue for the Sega Saturn, and at each launch:
//   - the disc Ymir is given (YmirLaunch.DiscFor): a .m3u's first disc, an archive unpacked, the formats Ymir opens itself;
//   - its BIOS (YmirBios): one in roms\ipl, else copied from RetroArch's system folder and the other emulators' - by content;
//   - Ymir.toml: its first lines when there is none, the pack's keys, and FOR THIS GAME ONLY its own backup RAM (YmirSession -
//     put back once Ymir has quit); the marker that keeps Ymir's update popup away (updates are LaunchBox's);
//   - the game's discs written down for the next-disc key (YmirAhk);
//   - its state in Ymir's compatibility list, shown as it starts (YmirCompat, YmirCompatToast);
//   - the line: -p <install> [-f] <disc>.
//
// THE PLATFORM ROW: one, Sega Saturn, made when the plugin creates the emulator; IsDefault only where no emulator of the library
// is yet the platform's. A row that is there is never removed nor unticked (never-touch-existing-platform-rows).
//
// NO RETROACHIEVEMENTS: Ymir has none (0.4.1).

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

namespace LbIntegrations.Ymir
{
    public partial class YmirPlugin : EmulatorPlugin, ISystemEventsPlugin, ILbCatalogSource
    {
        /// <summary>Nothing: the host appends the game's path; the switches are added at each launch.</summary>
        private const string DefaultCommandLine = "";
        internal const string PackName = "Nixx-Ymir";
        internal const string Platform = "Sega Saturn";
        private const string RowsId = "com.nixxou.lbip.ymir";

        /// <summary>What the game's file may be: what Ymir opens, a .m3u, an archive (docs\ymir.md §4).</summary>
        internal static readonly string[] Extensions = { ".chd", ".cue", ".ccd", ".mds", ".iso", ".m3u", ".zip", ".7z" };

        public YmirPlugin()
        {
            // First: the save front desk registers this plugin for the others (Shared.Saves SaveDesk).
            _desk = new SaveDesk(this, this, SaveEmu, SaveLog);
            Log.Info("plugin constructed, assembly " + typeof(YmirPlugin).Assembly.Location);
            LbipLog.Use(Log.Info, Log.Warn, Log.Disabled, () => Log.Tracing);
            LbipRowInjection.Install(RowsId, MetadataRows());
            // A session LaunchBox did not see the end of (it was closed or crashed while Ymir ran): Ymir.toml put back.
            try { YmirSession.Recover(); } catch (Exception ex) { Log.Warn("session recovery at start", ex); }
        }

        /// <summary>LaunchBox's metadata has no Ymir row (20/10). AutoExtract false: the plugin unpacks an archive itself, a
        /// .cue with its .bin files.</summary>
        private static IEnumerable<LbCatalogEmulator> MetadataRows()
        {
            var row = new LbCatalogEmulator
            {
                Name = PackName,
                CommandLine = DefaultCommandLine,
                ApplicableFileExtensions = string.Join("; ", Extensions),
                Url = YmirBuild.Home,
                BinaryFileName = YmirLaunch.ExecutableName,
                AutoExtract = false,
            };
            row.Platforms.Add(new LbCatalogPlatform { Platform = Platform, ApplicableFileExtensions = string.Join("; ", Extensions), Recommended = false, RequiredBiosFile = null });
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
                LbipRowInjection.Install(RowsId, MetadataRows());
                // The compatibility list, a week old or none: fetched in the background - only when a Ymir is in the library.
                if (eventType != SystemEventTypes.PluginInitialized && Settings.Emulators().Length > 0) YmirCompat.RefreshLater();
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
                if (!YmirLaunch.IsYmirExe(path)) continue;
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
                if (Fill(() => emu.AutoHotkeyScript, v => emu.AutoHotkeyScript = v, YmirAhk.Running)) set.Add("running");
                if (Fill(() => emu.ExitAutoHotkeyScript, v => emu.ExitAutoHotkeyScript = v, YmirAhk.Exit)) set.Add("exit");
                if (Fill(() => emu.SaveStateAutoHotkeyScript, v => emu.SaveStateAutoHotkeyScript = v, YmirAhk.SaveState)) set.Add("save");
                if (Fill(() => emu.LoadStateAutoHotkeyScript, v => emu.LoadStateAutoHotkeyScript = v, YmirAhk.LoadState)) set.Add("load");
                if (set.Count > 0) Log.Info("hotkey scripts set: " + string.Join(", ", set));
            }
            catch (Exception ex) { Log.Warn("could not describe the hotkeys on the emulator entry", ex); }
        }

        /// <summary>A script set when empty - or when it is, word for word, one an earlier build of this plugin wrote
        /// (YmirAhk.Superseded). One the user changed is never touched.</summary>
        private static bool Fill(Func<string> get, Action<string> set, string value)
        {
            try
            {
                var now = get();
                string Norm(string s) => (s ?? "").Replace("\r\n", "\n").Trim();
                bool ours = YmirAhk.Superseded.Any(old => Norm(old) == Norm(now));
                if (!string.IsNullOrWhiteSpace(now) && !ours) return false;
                if (Norm(now) == Norm(value)) return false;
                set(value);
                return true;
            }
            catch { return false; }
        }

        private static T Safe<T>(Func<T> get) { try { return get(); } catch { return default; } }

        public override EmulatorSupportResponse IsPlatformSupported(string platform)
        {
            bool supported = string.Equals((platform ?? "").Trim(), Platform, StringComparison.OrdinalIgnoreCase);
            // Never recommended (Mehdi, 17/10): LaunchBox pushes a recommended emulator - in the import wizard too, not installed yet.
            return new EmulatorSupportResponse(supported, false);
        }

        // ── versions ─────────────────────────────────────────────────────────

        /// <summary>The version of an install of OURS (its stamp), else null - a Ymir the user installed is his to update.</summary>
        public override string GetCurrentVersion(string applicationPath) => YmirBuild.InstalledVersion(ResolveFullPath(applicationPath));

        public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions()
        {
            var r = YmirBuild.Latest();
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
                return current != null && YmirBuild.IsNewer(version.Label, current);
            }
            catch (Exception ex) { Log.Warn("update check failed", ex); return false; }
        }

        // ── installation ─────────────────────────────────────────────────────

        public override EmulatorInstallResponse InstallEmulator(InstallEmulatorArgs args)
        {
            string archive = null;
            try
            {
                var release = YmirBuild.Latest();
                if (release == null) return new EmulatorInstallResponse("Couldn't read Ymir's latest release on GitHub. Try again later.");

                bool reinstall = args?.ExistingEmulator != null;
                string targetDir = reinstall
                    ? Path.GetDirectoryName(ResolveFullPath(args.ExistingEmulator.ApplicationPath))
                    : Path.Combine(LaunchBoxRoot(), "Emulators", PackName);
                if (string.IsNullOrWhiteSpace(targetDir)) return new EmulatorInstallResponse("Couldn't work out where to install Ymir.");
                var exe = Path.Combine(targetDir, YmirLaunch.ExecutableName);
                if (YmirLaunch.Running(exe)) return new EmulatorInstallResponse("Ymir is running from " + targetDir + ": close it, then update again.");

                Report(args, "Downloading Ymir " + release.Version + "... " + YmirBuild.RuntimeNote, 0);
                archive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                YmirBuild.Fetch(release, archive, p => Report(args, "Downloading Ymir " + release.Version + "...", p), () => Cancelled(args));

                Report(args, "Extracting Ymir...", null);
                Archives.ExtractOver(archive, targetDir);
                if (!File.Exists(exe)) return new EmulatorInstallResponse("Ymir was extracted to " + targetDir + " but " + YmirLaunch.ExecutableName + " is not there.");
                YmirBuild.Stamp(targetDir, release.Version);
                Log.Info("installed Ymir " + release.Version + " (" + YmirBuild.Flavour() + ") to " + targetDir);

                // Its first settings, the pack's keys, no update popup.
                YmirSession.Recover();
                YmirLaunch.Bootstrap(exe);
                YmirLaunch.Onboard(exe);
                var settings = YmirLaunch.SettingsOf(exe);
                var doc = YmirTomlDoc.Load(settings);
                var bound = YmirLaunch.BindPackKeys(doc);
                if (bound.Count > 0) { doc.Save(settings); Log.Info("Ymir.toml: the pack's keys bound - " + string.Join(", ", bound)); }

                // Every Saturn BIOS the user already has, copied into roms\ipl now.
                Report(args, "Looking for the Saturn BIOS...", null);
                var bios = YmirBios.Ensure(exe, m => Report(args, m, null));
                var known = bios.Where(b => b.Known != null).Select(b => b.Known.Label).Distinct().ToList();
                var note = " " + (known.Count == 0 ? "No Saturn BIOS found: put one in " + YmirLaunch.IplDirOf(exe) + "." : "BIOS: " + string.Join(", ", known) + ".");

                // Ymir's compatibility list, for the games' windows and their launches.
                Report(args, "Downloading Ymir's compatibility list...", null);
                YmirCompat.Download(() => Cancelled(args));

                if (reinstall)
                {
                    try { args.ExistingEmulator.ApplicationPath = MakeRelativeToLaunchBox(exe); } catch { }
                    return new EmulatorInstallResponse(args.ExistingEmulator, "Ymir updated to " + release.Version + "." + note);
                }
                if (args != null && !args.ShouldCreateEmulator)
                    return new EmulatorInstallResponse("Ymir was installed to " + targetDir + ", but no emulator entry was requested." + note);

                var created = CreateEmulator(exe);
                if (created == null)
                    return new EmulatorInstallResponse("Ymir was installed to " + targetDir + " but the emulator entry couldn't be created (no data manager available).");
                return new EmulatorInstallResponse(created, "Ymir " + release.Version + " installed." + note);
            }
            catch (OperationCanceledException) { return new EmulatorInstallResponse("Installation cancelled."); }
            catch (Exception ex)
            {
                Log.Warn("install failed", ex);
                return new EmulatorInstallResponse("Failed to install Ymir: " + ex.Message);
            }
            finally
            {
                try { if (archive != null && File.Exists(archive)) File.Delete(archive); } catch { }
            }
        }

        private static bool Cancelled(InstallEmulatorArgs args) { try { return args?.ShouldCancelFunc?.Invoke() ?? false; } catch { return false; } }

        /// <summary>The emulator with its Sega Saturn row - the platform's default only when no emulator of the library is yet.</summary>
        private static IEmulator CreateEmulator(string exePath)
        {
            var dm = PluginHelper.DataManager;
            if (dm == null) return null;
            bool taken = false;
            try
            {
                foreach (var e in dm.GetAllEmulators() ?? Array.Empty<IEmulator>())
                    foreach (var r in e.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>())
                        try { if (r.IsDefault && string.Equals(r.Platform, Platform, StringComparison.OrdinalIgnoreCase)) taken = true; } catch { }
            }
            catch { }

            var emu = dm.AddNewEmulator();
            emu.Title = PackName;
            emu.ApplicationPath = MakeRelativeToLaunchBox(exePath);
            emu.CommandLine = DefaultCommandLine;
            EnsureHotkeyScripts(emu);
            // DefaultPlatform IS NOT SET - see XeniaPlugin.CreateEmulator. IsDefault on the row is what counts.
            var row = emu.AddNewEmulatorPlatform();
            row.Platform = Platform;
            row.CommandLine = "";
            row.IsDefault = !taken;
            try { dm.Save(false); } catch (Exception ex) { Log.Warn("data manager save failed", ex); }
            Log.Info("created emulator entry \"" + PackName + "\" -> " + emu.ApplicationPath + ", " + Platform + (row.IsDefault ? " (its default)" : ""));
            return emu;
        }

        // ── BIOS ─────────────────────────────────────────────────────────────

        /// <summary>For LaunchBox's BIOS list: the usual names, none required - Ymir knows a BIOS by its content, whatever its
        /// name (YmirBios), so a name LaunchBox looks for may be missing while Ymir has what it needs.</summary>
        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string platform)
        {
            if (!string.Equals((platform ?? "").Trim(), Platform, StringComparison.OrdinalIgnoreCase)) return Array.Empty<EmulatorBiosFile>();
            const string folder = "roms\\ipl";
            return new[]
            {
                new EmulatorBiosFile(folder, "mpr-17933.bin", false, "Saturn BIOS US / Europe 1.01a - Ymir reads any Saturn BIOS in roms\\ipl, by its content", null, null),
                new EmulatorBiosFile(folder, "sega_101.bin", false, "Saturn BIOS Japan 1.01 - for Japanese discs", null, null),
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
            // The file launched: a version's (another disc, another release) or the game's.
            try { rom = ResolveFullPath(args?.AppBeingLaunched?.ApplicationPath); } catch { }
            try { if (string.IsNullOrWhiteSpace(rom)) rom = ResolveFullPath(args?.GameBeingLaunched?.ApplicationPath); } catch { }
            try { platform = args?.GameBeingLaunched?.Platform; } catch { }
            try { title = args?.GameBeingLaunched?.Title; } catch { }
            if (!YmirLaunch.IsYmirExe(exe) || !File.Exists(exe)) return new PrepareForLaunchResponse(success: true);
            string Caption() => PackName + " - " + (title ?? Path.GetFileName(rom ?? ""));
            PrepareForLaunchResponse Refuse(string text) { LbipNotice.Show(Caption(), text); return new PrepareForLaunchResponse(success: false); }

            // Two Ymirs on one profile would write over each other's settings and saves.
            if (YmirLaunch.Running(exe)) return Refuse("Ymir is already running from " + Path.GetDirectoryName(exe) + ": close it, then start the game again.");
            // A session LaunchBox did not see the end of: put back first.
            YmirSession.Recover();

            // The disc.
            var plan = YmirLaunch.DiscFor(rom, exe);
            if (plan.Disc == null) return Refuse("This game cannot be started: " + plan.Problem);
            var disc = YmirDisc.Read(plan.Disc);
            Log.Info("launch: " + Path.GetFileName(plan.Disc) + (plan.Discs.Count > 1 ? " (disc " + (plan.Discs.IndexOf(plan.Disc) + 1) + " of " + plan.Discs.Count + ")" : "")
                     + (disc.Hash != null ? " - " + disc : " - " + disc.Problem));

            // Its BIOS: one in roms\ipl, else copied in.
            var region = YmirDisc.BiosRegion(disc);
            var bios = YmirBios.InFolder(exe);
            if (bios.Count == 0 || (region != null && !YmirBios.HasFor(bios, region))) bios = YmirBios.Ensure(exe);
            if (bios.Count == 0)
                return Refuse("Ymir needs a Sega Saturn BIOS, and none was found.\n\nPut one in " + YmirLaunch.IplDirOf(exe)
                              + " (any name - Ymir knows it by its content: mpr-17933.bin for US / European discs, sega_101.bin for Japanese ones), "
                              + "or in RetroArch's system folder - it is copied at the next launch.");
            if (region != null && !YmirBios.HasFor(bios, region))
                Log.Warn("launch: no " + YmirBios.RegionWord(region) + " BIOS in roms\\ipl for this disc (area " + disc.Area + ") - Ymir decides with the one there");

            // Ymir.toml: its first lines, the pack's keys, the game's own backup RAM for the session.
            var settings = YmirLaunch.SettingsOf(exe);
            YmirLaunch.Bootstrap(exe);
            YmirLaunch.Onboard(exe);
            var doc = YmirTomlDoc.Load(settings);
            var bound = YmirLaunch.BindPackKeys(doc);
            if (bound.Count > 0) Log.Info("Ymir.toml: the pack's keys bound - " + string.Join(", ", bound));
            if (!YmirLaunch.SlotKeysAreDefault(doc)) Log.Info("Ymir.toml: the slot keys 1-0 were rebound in Ymir - F6 / F7 send them all the same");
            var ram = RamPath(exe, platform, rom);
            try { Directory.CreateDirectory(Path.GetDirectoryName(ram)); } catch { }
            var keys = new List<(string, string, string, bool)>();
            // Only keys this Ymir reads (YmirLaunch.Knows): a newer Ymir that renamed them would keep its own single RAM.
            if (YmirLaunch.Knows(doc, "System", "InternalBackupRAMImagePath") && YmirLaunch.Knows(doc, "System", "InternalBackupRAMPerGame"))
            {
                keys.Add(("System", "InternalBackupRAMImagePath", YmirToml.Literal(ram), true));
                keys.Add(("System", "InternalBackupRAMPerGame", "false", false));
            }
            else Log.Warn("launch: Ymir.toml (ConfigVersion " + YmirLaunch.ConfigVersionOf(doc) + ") has no backup RAM setting known here - the game uses Ymir's own RAM");
            // Its own options, from its window (YmirGameOptions) - those this Ymir has.
            var options = YmirGameOptions.KeysFor(gameId, exe, doc);
            if (options.LeftOut.Count > 0) Log.Warn("launch: not in this Ymir (ConfigVersion " + YmirLaunch.ConfigVersionOf(doc) + "), left out - " + string.Join(", ", options.LeftOut));
            var expansion = YmirGameOptions.GameValue(gameId, "Expansion");
            // Its backup cartridge, when it has one (YmirCart): 4 Mbit, its file - never for a game that needs another cartridge,
            // nor when its window forces an expansion cartridge.
            var cart = CartPath(exe, platform, rom);
            if (File.Exists(cart) && expansion != null && expansion != "auto")
                Log.Info("launch: the game's cartridge is not plugged - its window forces an expansion cartridge");
            else if (File.Exists(cart))
            {
                var bad = YmirCart.WhyNot(cart);
                if (bad != null) Log.Warn("launch: the game's cartridge is not plugged - " + bad);
                else if (YmirCart.NeedsOtherCart(disc)) Log.Info("launch: the game's cartridge is not plugged - Ymir plugs the cartridge this game needs");
                else if (!new[] { ("Cartridge", "Type"), ("Cartridge", "AutoLoadGameCarts"), ("Cartridge.BackupRAM", "Capacity"), ("Cartridge.BackupRAM", "ImagePath") }
                             .All(k => YmirLaunch.Knows(doc, k.Item1, k.Item2)))
                    Log.Warn("launch: the game's cartridge is not plugged - Ymir.toml (ConfigVersion " + YmirLaunch.ConfigVersionOf(doc)
                             + ") lacks a cartridge setting known here, and a capacity Ymir did not read would format the cartridge");
                else
                {
                    keys.Add(("Cartridge", "Type", "'BackupRAM'", false));
                    keys.Add(("Cartridge", "AutoLoadGameCarts", "false", false));
                    keys.Add(("Cartridge.BackupRAM", "Capacity", "'4Mbit'", false));
                    keys.Add(("Cartridge.BackupRAM", "ImagePath", YmirToml.Literal(cart), true));
                    Log.Info("launch: backup cartridge " + cart);
                    options.Keys.RemoveAll(k => k.Option == "Expansion");
                }
            }
            var forced = YmirSession.Force(doc, keys);
            forced.AddRange(YmirSession.Force(doc, options.Keys));
            try { doc.Save(settings); }
            catch (Exception ex) { return Refuse("Ymir's settings could not be written (" + ex.Message + "): the game would use another game's backup RAM."); }
            Log.Info("launch: backup RAM " + ram + (File.Exists(ram) ? "" : " (new - Ymir formats it)"));
            YmirSession.Begin(exe, settings, forced, gameId: gameId);

            // The discs, for the next-disc key.
            YmirLaunch.WriteDiscs(exe, plan);

            // Its state in the compatibility list.
            try
            {
                var compat = YmirCompat.Lookup(disc);
                Log.Info("launch: compatibility - " + YmirCompat.Line(compat));
                YmirCompatToast.ForLaunch(title ?? Path.GetFileNameWithoutExtension(rom), compat);
                YmirCompat.RefreshLater();
            }
            catch (Exception ex) { Log.Warn("compatibility at launch", ex); }

            var current = args?.CurrentCommandLine ?? "";
            var line = YmirLaunch.CommandLine(current, YmirLaunch.ProfileOf(exe), YmirSettings.BoolFor(gameId, YmirSettings.Fullscreen), rom, plan.Disc);
            Log.Info("launch: " + line);
            return new PrepareForLaunchResponse(success: true) { NewCommandLine = line };
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
