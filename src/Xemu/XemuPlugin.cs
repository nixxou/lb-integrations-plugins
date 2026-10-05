// xemu (original Xbox) for LaunchBox / LiteBox - the Cxbx plugin's shape (src\Cxbx\CxbxPlugin.cs), further than Unbroken's
// own Xemu plugin (decompiled 04/10: download, xemu.toml, the dashboard's disk, the BIOS list - and nothing for discs or saves).
//
// Claim an install of ours (XemuPaths.IsOurs), install and update it from xemu's stable releases, launch a game on a disc
// xemu can open (XemuDisc: an XISO as it is, a redump / CSO / CCI / CHD / zipped image cut to its XISO) and on ITS OWN
// CONSOLE (Mehdi, 04/10: "un disque par jeu"): hdd\games\<title id>.qcow2, a qcow2 of differences over the pristine
// hdd\base.qcow2 (Qcow2Overlay) - that file is the game's save (XemuSaves).
//
// THE LAUNCH LINE: x.emu.exe (xemu renamed - XemuPaths) -full-screen -config_path "<session toml>" -dvd_path "<xiso>"
//   -dvd_path   xemu's own (system/vl.c), over [sys.files] dvd_path
// NOTHING AFTER IT: NewCommandLine REPLACES the host's whole line, the game's path included (Vita3kSaves.cs). Measured in
// LaunchBox 14 on 04/10: a line ending in -L, to swallow a path the host was thought to append, stopped xemu at once -
// "-L: requires an argument" in xemu.log. The game's own path is dropped from the line: a bare path is a hard disk to QEMU.
// Talks only to the public SDK; every entry point is defensive (the host swallows exceptions).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;
using LbIntegrations.Catalog;
using LbIntegrations.Cxbx;
using LbIntegrations.Lbip;

namespace LbIntegrations.Xemu
{
    public partial class XemuPlugin : EmulatorPlugin, ISystemEventsPlugin, ILbCatalogSource
    {
        private const string Repo = "xemu-project/xemu";
        private const string TagPrefix = "v";                      // the stable releases - not the rolling "pre-release"
        private const string XboxPlatform = "Microsoft Xbox";
        private const string DefaultCommandLine = "-full-screen -dvd_path";
        private const string DashboardHdd = "https://github.com/xemu-project/xemu-dashboard/releases/latest/download/xbox_hdd.qcow2";

        private static readonly string[] AssetRequired = { "windows-x86_64" };
        private static readonly string[] AssetExcluded = { "dbg", "pdb" };

        /// <summary>The name this pack publishes under AND the folder it installs into (Emulators\Nixx-Xemu).</summary>
        private const string PackName = "Nixx-Xemu";

        public XemuPlugin()
        {
            Log.Info("plugin constructed, assembly " + typeof(XemuPlugin).Assembly.Location);
            LbipLog.Use(Log.Info, Log.Warn, Log.Disabled, () => Log.Tracing);
            LbIntegrations.RamDisk.RamDiskLog.Use(Log.Info, (m, ex) => Log.Warn(m, ex));
            // Where LaunchBox is, for the RAM disk helper's folder - unset, the helper reads as absent and every disc is copied
            // (measured 04/10: "the RAM disk helper is absent, 1.10.0.0 is needed" with 1.10 in place). The probe sets its own.
            if (LbIntegrations.RamDisk.RamDiskHost.LaunchBoxRoot == null)
            {
                var forced = Environment.GetEnvironmentVariable("LBIP_RAMDISK_ROOT");
                if (!string.IsNullOrWhiteSpace(forced)) LbIntegrations.RamDisk.RamDiskHost.UseRoot(forced);
                else LbIntegrations.RamDisk.RamDiskHost.LaunchBoxRoot = LaunchBoxRoot;
            }
            LbipRowInjection.Install("com.nixxou.lbip.xemu", MetadataRows());
        }

        private static IEnumerable<LbCatalogEmulator> MetadataRows()
        {
            const string extensions = ".iso; .xiso; .cso; .cci; .chd; .zip; .7z";
            yield return new LbCatalogEmulator
            {
                Name = PackName,
                CommandLine = DefaultCommandLine,
                ApplicableFileExtensions = extensions,
                Url = "https://xemu.app/",
                BinaryFileName = XemuPaths.Exe,
                AutoExtract = false,
                Platforms = { new LbCatalogPlatform { Platform = XboxPlatform, ApplicableFileExtensions = extensions, Recommended = true } },
            };
        }

        public override string EmulatorName => PackName;

        public IEnumerable<LbCatalogEmulator> EmulatorRows() => MetadataRows();

        public void OnEventRaised(string eventType)
        {
            try
            {
                if (eventType != SystemEventTypes.PluginInitialized && eventType != SystemEventTypes.LaunchBoxStartupCompleted
                    && eventType != SystemEventTypes.BigBoxStartupCompleted) return;
                var dm = PluginHelper.DataManager;
                bool renamed = false;
                foreach (var emu in dm?.GetAllEmulators() ?? new IEmulator[0])
                {
                    var path = ResolveFullPath(Safe(() => emu.ApplicationPath));
                    // An install of ours from before x.emu.exe (XemuPaths): renamed, and the entry pointed at it - else
                    // Unbroken's plugin keeps claiming it.
                    if (XemuPaths.MigrateOldName(path) is string moved)
                    {
                        try { emu.ApplicationPath = MakeRelativeToLaunchBox(moved); renamed = true; Log.Info("renamed " + path + " -> " + moved + ", entry \"" + Safe(() => emu.Title) + "\" updated"); }
                        catch (Exception ex) { Log.Warn("renamed " + path + " but the entry could not be updated", ex); }
                        path = moved;
                    }
                    // A session the host never saw end (killed mid-game): xemu.toml back on the stand-alone console.
                    if (XemuPaths.IsOurs(path)) XemuSession.Standalone(path, "the host has started");
                }
                if (renamed) try { dm.Save(false); } catch (Exception ex) { Log.Warn("data manager save failed", ex); }
                LbipRowInjection.Install("com.nixxou.lbip.xemu", MetadataRows());
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
                if (!XemuPaths.IsOurs(ResolveFullPath(path))) continue;
                claimed.Add(emu);
                try { if (emu.AutoExtract) { emu.AutoExtract = false; Log.Info("AutoExtract turned off - this plugin prepares Xbox discs itself"); } } catch { }
                EnsureHotkeyScripts(emu);
            }
            var signature = string.Join("|", claimed.Select(e => { try { return e.Title; } catch { return "?"; } }).OrderBy(t => t));
            if (signature != _lastClaimed) { _lastClaimed = signature; Log.Info("GetApplicableEmulators: claimed " + claimed.Count + " emulator(s)"); }
            return claimed;
        }

        public override EmulatorSupportResponse IsPlatformSupported(string platform)
        {
            bool supported = string.Equals((platform ?? "").Trim(), XboxPlatform, StringComparison.InvariantCultureIgnoreCase);
            return new EmulatorSupportResponse(supported, supported);
        }

        private static void EnsureHotkeyScripts(IEmulator emu)
        {
            try
            {
                var set = new List<string>();
                if (LbipAhk.Ours(emu.AutoHotkeyScript, XemuAhk.Running, Array.Empty<string>())) { emu.AutoHotkeyScript = XemuAhk.Running; set.Add("running"); }
                if (string.IsNullOrWhiteSpace(emu.ExitAutoHotkeyScript)) { emu.ExitAutoHotkeyScript = XemuAhk.Exit; set.Add("exit"); }
                if (set.Count > 0) Log.Info("hotkey scripts set: " + string.Join(", ", set));
            }
            catch (Exception ex) { Log.Warn("could not describe the hotkeys on the emulator entry", ex); }
        }

        // ── versions ─────────────────────────────────────────────────────────

        public override string GetCurrentVersion(string applicationPath)
        {
            var full = ResolveFullPath(applicationPath);
            return string.IsNullOrWhiteSpace(full) || !File.Exists(full) ? null : XemuPaths.InstalledTag(full);
        }

        public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions()
        {
            var release = GitHubReleases.GetNewest(Repo, TagPrefix);
            if (release == null) { Log.Warn("no release information for " + Repo); return null; }
            var assets = GitHubReleases.SelectAssets(release, AssetRequired, AssetExcluded).Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                                                                                                   && !a.Name.Contains("arm64")).ToList();
            if (assets.Count == 0)
            {
                Log.Warn("release " + release.Tag + " has no asset matching our filters; saw: " + string.Join(", ", release.Assets.Select(a => a.Name)));
                return null;
            }
            return assets.Take(1).Select(a => new EmulatorControllerVersion(a.DownloadUrl, release.Tag ?? "", a.Name)).ToList();
        }

        public override bool IsUpdateAvailable(string emulatorAppPath, out EmulatorControllerVersion version)
        {
            version = null;
            try
            {
                version = GetInstallableVersions()?.FirstOrDefault();
                if (version == null) return false;
                if (string.IsNullOrWhiteSpace(emulatorAppPath)) return true;
                var current = GetCurrentVersion(emulatorAppPath);
                if (current == null) return false;
                return !string.Equals(current, version.Label, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) { Log.Warn("update check failed", ex); return false; }
        }

        // ── installation ─────────────────────────────────────────────────────

        public override EmulatorInstallResponse InstallEmulator(InstallEmulatorArgs args)
        {
            string archive = null;
            Func<bool> cancelled = () => { try { return args?.ShouldCancelFunc?.Invoke() ?? false; } catch { return false; } };
            try
            {
                string url = args?.Version, label = null;
                var latest = GetInstallableVersions()?.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(url))
                {
                    if (latest == null) return new EmulatorInstallResponse("Couldn't determine which build of xemu to install. GitHub may be rate limiting this machine; try again in a few minutes.");
                    url = latest.Identifier;
                }
                if (latest != null && string.Equals(latest.Identifier, url, StringComparison.OrdinalIgnoreCase)) label = latest.Label;

                bool reinstall = args?.ExistingEmulator != null;
                string targetDir = reinstall
                    ? Path.GetDirectoryName(ResolveFullPath(args.ExistingEmulator.ApplicationPath))
                    : Path.Combine(LaunchBoxRoot(), "Emulators", PackName);
                if (string.IsNullOrWhiteSpace(targetDir)) return new EmulatorInstallResponse("Couldn't work out where to install xemu.");
                if (XemuPaths.Running(Path.Combine(targetDir, XemuPaths.Exe)) || XemuPaths.Running(Path.Combine(targetDir, XemuPaths.ReleaseExe))) return new EmulatorInstallResponse("xemu is running - close it first, then try again.");

                Report(args, "Downloading xemu...", 0);
                archive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                GitHubReleases.Download(url, archive, p => Report(args, "Downloading xemu...", p), cancelled);
                Report(args, "Extracting xemu...", null);
                Directory.CreateDirectory(targetDir);
                // OVER, never cleared first: xemu.toml, eeprom.bin, the BIOS and every game's console live in this folder.
                Archives.ExtractOver(archive, targetDir);
                // RENAMED x.emu.exe: Unbroken's Xemu plugin claims every executable named like xemu, and LaunchBox gives the entry
                // to the first plugin that claims it (XemuPaths).
                var exe = XemuPaths.RenameReleaseExe(targetDir);
                if (exe == null) return new EmulatorInstallResponse("xemu was extracted to " + targetDir + " but no " + XemuPaths.ReleaseExe + " was found in it.");
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(exe), XemuPaths.VersionFile), label ?? "unknown");

                var note = SetUp(exe, args, cancelled);

                if (reinstall)
                {
                    try { args.ExistingEmulator.ApplicationPath = MakeRelativeToLaunchBox(exe); } catch { }
                    return new EmulatorInstallResponse(args.ExistingEmulator, "xemu updated." + note);
                }
                if (args != null && !args.ShouldCreateEmulator) return new EmulatorInstallResponse("xemu was installed to " + targetDir + ", but no emulator entry was requested." + note);
                var created = CreateEmulator(exe, label);
                if (created == null) return new EmulatorInstallResponse("xemu was installed to " + targetDir + " but the emulator entry couldn't be created." + note);
                return new EmulatorInstallResponse(created, "xemu installed." + note);
            }
            catch (OperationCanceledException) { return new EmulatorInstallResponse("Installation cancelled."); }
            catch (Exception ex)
            {
                Log.Warn("install failed", ex);
                return new EmulatorInstallResponse("Failed to install xemu: " + ex.Message);
            }
            finally { try { if (archive != null && File.Exists(archive)) File.Delete(archive); } catch { } }
        }

        /// <summary>Everything around the executable, at install and update: portable xemu.toml, the BIOS copied in, the
        /// pristine console downloaded, the stand-alone console made. What to add to the install's message.</summary>
        private static string SetUp(string exe, InstallEmulatorArgs args, Func<bool> cancelled)
        {
            var notes = new List<string>();
            var dir = XemuPaths.Dir(exe);
            // PORTABLE: xemu.toml beside the executable (ui/xemu-settings.cc). Never cleared.
            var toml = XemuPaths.TomlOf(exe);
            if (!File.Exists(toml)) { File.WriteAllText(toml, ""); Log.Info("portable: " + toml + " created"); }
            XemuToml.Set(toml, "general", "show_welcome", "false");
            XemuToml.Set(toml, "general", "skip_boot_anim", "true", onlyIfAbsent: true);
            XemuToml.Set(toml, "general.updates", "check", "false");

            // THE BIOS: the user's, copied in from where they are already - Unbroken's plugin's folder, RetroArch's (recursive).
            Report(args, "Looking for the Xbox BIOS...", null);
            var bios = XemuPaths.BiosDir(exe);
            Directory.CreateDirectory(bios);
            var wanted = new List<LbipBiosImport.Wanted> { new LbipBiosImport.Wanted { Name = XemuPaths.Mcpx } };
            wanted.AddRange(XemuPaths.Flashes.Select(f => new LbipBiosImport.Wanted { Name = f.Name }));
            var emulators = Path.GetDirectoryName(dir) ?? dir;
            LbipBiosImport.Import(bios, new[]
            {
                new LbipBiosImport.Source { Dir = Path.Combine(emulators, "Xemu", "bios"), Recursive = false },
                new LbipBiosImport.Source { Dir = Path.Combine(emulators, "RetroArch", "system"), Recursive = true },
            }, wanted, m => Report(args, m, null), cancelled);
            if (XemuPaths.McpxPath(exe) == null) notes.Add("Put the MCPX boot ROM (mcpx_1.0.bin) in " + bios + ".");
            if (XemuPaths.FlashPath(exe) == null) notes.Add("Put a flash BIOS (a modified one, e.g. Complex_4627.bin) in " + bios + ".");

            // THE PRISTINE CONSOLE: xemu's own dashboard, downloaded once, read-only - every game's console is made over it.
            var base_ = XemuPaths.BaseHdd(exe);
            Directory.CreateDirectory(Path.GetDirectoryName(base_));
            if (!File.Exists(base_))
            {
                var part = base_ + ".part";
                try
                {
                    GitHubReleases.Download(DashboardHdd, part, p => Report(args, "Downloading xemu's dashboard disk...", p), cancelled);
                    if (Qcow2Overlay.VirtualSize(part) <= 0) { notes.Add("The dashboard's disk did not download as a qcow2 - reinstall to try again."); try { File.Delete(part); } catch { } }
                    else
                    {
                        File.Move(part, base_);
                        File.SetAttributes(base_, File.GetAttributes(base_) | FileAttributes.ReadOnly);
                        Log.Info("console: the dashboard's disk downloaded -> " + base_);
                    }
                }
                catch (Exception ex) { Log.Warn("the dashboard's disk could not be downloaded", ex); notes.Add("The dashboard's disk could not be downloaded (" + ex.Message + ") - reinstall to try again."); }
            }

            // [sys.files]: the BIOS found, the EEPROM xemu makes itself, the stand-alone console.
            if (XemuPaths.McpxPath(exe) is string mcpx) XemuToml.Set(toml, "sys.files", "bootrom_path", XemuToml.Literal(mcpx));
            if (XemuPaths.FlashPath(exe) is string flash) XemuToml.Set(toml, "sys.files", "flashrom_path", XemuToml.Literal(flash));
            XemuToml.Set(toml, "sys.files", "eeprom_path", XemuToml.Literal(XemuPaths.Eeprom(exe)));
            // YOUR CONSOLE, for good: eeprom.bin with the seed's serial number, MAC and keys (Eeprom\XemuEeprom.ApplyIdentity).
            try
            {
                if (!XemuOptions.Effective(null).ContainsKey("console.hddkey")) Log.Info("console: xemu's own identity kept (the Console identity option)");
                else if (Eeprom.XemuEeprom.ApplyIdentity(XemuPaths.Eeprom(exe)) is string done) Log.Info("console: " + done);
            }
            catch (Exception ex) { Log.Warn("console: could not be set up as yours", ex); }
            XemuSession.Standalone(exe, "installed");
            return notes.Count == 0 ? "" : " " + string.Join(" ", notes);
        }

        private static IEmulator CreateEmulator(string exePath, string versionLabel)
        {
            var dm = PluginHelper.DataManager;
            if (dm == null) return null;
            var emu = dm.AddNewEmulator();
            emu.Title = PackName;
            emu.ApplicationPath = MakeRelativeToLaunchBox(exePath);
            emu.CommandLine = DefaultCommandLine;
            emu.AutoExtract = false;
            EnsureHotkeyScripts(emu);
            var platform = emu.AddNewEmulatorPlatform();
            platform.Platform = XboxPlatform;
            platform.IsDefault = true;
            try { dm.Save(false); } catch (Exception ex) { Log.Warn("data manager save failed", ex); }
            Log.Info("created emulator entry \"" + PackName + "\"" + (versionLabel != null ? " (" + versionLabel + ")" : "") + " -> " + emu.ApplicationPath);
            return emu;
        }

        // ── BIOS ─────────────────────────────────────────────────────────────

        /// <summary>In bios\ beside xemu: the MCPX boot ROM, required (512 bytes, MD5 from xemu's docs), and ONE flash BIOS -
        /// a required group, any of its files (Unbroken's list and hashes).</summary>
        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string platform)
        {
            if (!string.Equals((platform ?? "").Trim(), XboxPlatform, StringComparison.InvariantCultureIgnoreCase)) return Array.Empty<EmulatorBiosFile>();
            var files = new List<EmulatorBiosFile> { new EmulatorBiosFile("bios", XemuPaths.Mcpx, true, "MCPX boot ROM - required", XemuPaths.McpxMd5, null) };
            var flash = new EmulatorBiosGroup("xemu-flash", "Flash BIOS - required, one of these (a modified retail BIOS, or a debug one)", isRequired: true, allItemsRequired: false);
            files.AddRange(XemuPaths.Flashes.Select(f => new EmulatorBiosFile("bios", f.Name, false, "Flash BIOS", f.Md5, flash)));
            return files;
        }

        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string emulatorApplicationPath, string platform, string commandLine)
            => GetBiosFilesForPlatform(platform);

        public override RetroAchievementSupportResponse SupportsRetroAchievements(string emulatorApplicationPath)
            => new RetroAchievementSupportResponse(isSupported: false);

        // ── launch ───────────────────────────────────────────────────────────

        public override PrepareForLaunchResponse PrepareEmulatorForLaunch(PrepareForLaunchArgs args)
            => LbipLaunchGate.Run(PackName, args, PrepareCore, null);

        private PrepareForLaunchResponse PrepareCore(PrepareForLaunchArgs args)
        {
            try
            {
                var exe = ResolveFullPath(Safe(() => args?.EmulatorBeingLaunched?.ApplicationPath));
                var rom = ResolveFullPath(Safe(() => args?.GameBeingLaunched?.ApplicationPath));
                if (string.IsNullOrWhiteSpace(exe) || !XemuPaths.IsOurs(exe)) return new PrepareForLaunchResponse(success: true);
                if (string.IsNullOrWhiteSpace(rom)) return Refuse("No game was given to xemu.");
                if (XemuPaths.Running(exe)) return Refuse("xemu is already running - close it first, then launch the game again.");

                var mcpx = XemuPaths.McpxPath(exe);
                var flash = XemuPaths.FlashPath(exe);
                if (mcpx == null || flash == null)
                    return Refuse("xemu needs the Xbox's BIOS to start a game, and " + (mcpx == null && flash == null ? "neither is" : mcpx == null ? "the MCPX boot ROM is not" : "no flash BIOS is")
                                  + " in " + XemuPaths.BiosDir(exe) + ":\n\n- mcpx_1.0.bin, the MCPX boot ROM (512 bytes)\n- a flash BIOS, a modified one (e.g. Complex_4627.bin)\n\nPut them there, then launch the game again.");
                var base_ = XemuPaths.BaseHdd(exe);
                if (!File.Exists(base_)) return Refuse("xemu's console disk (" + base_ + ") is missing. Update xemu from LaunchBox to download it again.");

                // A session left behind (the host killed mid-game): the user's xemu.toml put back first (XemuSessionConfig).
                var toml = XemuPaths.TomlOf(exe);
                var sessionToml = XemuSessionConfig.SessionPath(exe);
                try { if (XemuSessionConfig.MergeBack(toml, sessionToml)) Log.Info("xemu.toml: a session left behind merged back"); }
                catch (Exception ex) { Log.Warn("xemu.toml: a session left behind could not be merged back", ex); }

                // THE GAME'S OPTIONS: its own over every game's over the plugin's defaults (XemuOptions).
                var gameId = Safe(() => args?.GameBeingLaunched?.Id);
                var options = XemuOptions.Effective(gameId);
                Log.Info("options: " + string.Join(", ", options.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value)));

                var dvd = XemuDisc.Present(rom, exe, out var problem, out var info, null, options.TryGetValue("disc.media_patch", out var mp) ? mp != "off" : (bool?)null);
                if (dvd == null) return Refuse(Path.GetFileName(rom) + " cannot be launched: " + problem + ".");
                XemuDisc.Remember(rom, info.Xbe);

                // ITS OWN CONSOLE: made over the pristine one at the game's first launch.
                var titleId = info.Xbe?.TitleId > 0 ? info.Xbe.TitleIdText : null;
                if (titleId == null) { XemuDisc.Release(info); return Refuse(Path.GetFileName(rom) + " cannot be launched: its title id could not be read."); }
                var hdd = XemuPaths.GameHdd(exe, titleId);
                // ITS SAVE FILE FIRST: lbip-saves\<title id>.cxbxsave is what counts - put there since the last session, it goes into
                // the console now (XemuSaveFiles.Sync, Shared.Xbox\XboxSaveSync).
                try { XemuSaveFiles.Sync(exe, titleId, LbIntegrations.Xbox.XboxSyncMode.Launch); }
                catch (Exception ex) { XemuDisc.Release(info); return Refuse("The save of " + Path.GetFileName(rom) + " could not be put into its console: " + ex.Message + "\n\nNothing was changed. Move or remove its file in " + Path.GetDirectoryName(XemuSaveFiles.PackPath(exe, titleId)) + " to start without it."); }
                if (!File.Exists(hdd))
                {
                    var error = Qcow2Overlay.Create(base_, hdd);
                    if (error != null) { XemuDisc.Release(info); return Refuse("The console of " + Path.GetFileName(rom) + " could not be made: " + error + "."); }
                }

                // ITS CONSOLE'S SETTINGS: region and video following the game, the pack's language, Windows' time zone, the pack's
                // HDD key - on a copy of eeprom.bin for the session (Eeprom\XemuEeprom).
                // THE SAVE'S KEYS (Shared.Xbox\XboxSaveKeys): the HDD key and the certificate key it was made with - a save from
                // before they were noted is Cxbx-Reloaded's (certificate key zero). No save: the option's certificate key.
                var pack = XemuSaveFiles.PackPath(exe, titleId);
                var keys = LbIntegrations.Xbox.XboxSaveKeys.ForLaunch(pack, LbIntegrations.Xbox.XboxKeys.Zero);
                if (keys?.Hdd != null) options["console.hddkey.bytes"] = LbIntegrations.Xbox.XboxKeys.Hex(keys.Hdd);
                var cert = keys?.Cert ?? (options.TryGetValue("console.certkey", out var ck) && ck == "zero" ? LbIntegrations.Xbox.XboxKeys.Zero : LbIntegrations.Xbox.XboxKeys.Retail);
                var said = new List<string>();
                if (keys != null) said.Add("keys " + keys.Origin);
                string eeprom = null;
                try { eeprom = Eeprom.XemuEeprom.Prepare(XemuPaths.Eeprom(exe), XemuPaths.SessionEeprom(exe), info.Xbe, options, said); }
                catch (Exception ex) { Log.Warn("console: its settings could not be made", ex); }
                // ITS CERTIFICATE KEY: on a copy of the flash BIOS when it is not the BIOS's own (Shared.Xbox\XboxKeys).
                var biosFlash = flash;
                flash = LbIntegrations.Xbox.XboxKeys.SessionFlash(mcpx, flash, XemuPaths.SessionFlash(exe), cert, said) ?? flash;
                Log.Info("console: " + (said.Count == 0 ? "as it is" : string.Join(", ", said)));
                // The keys this session runs with, for the save it writes (XemuSaveFiles.Capture).
                try
                {
                    var hddUsed = Eeprom.XemuEeprom.Open(File.ReadAllBytes(eeprom ?? XemuPaths.Eeprom(exe)))?.HddKey;
                    var certUsed = flash != biosFlash ? cert : LbIntegrations.Xbox.XboxKeys.CertificateKeyOf(mcpx, biosFlash);
                    if (hddUsed != null && certUsed != null) LbIntegrations.Xbox.XboxSaveSync.NoteSessionKeys(XemuSaveFiles.Side(exe, titleId), hddUsed, certUsed);
                }
                catch (Exception ex) { Log.Warn("console: the session's keys could not be noted", ex); }

                // ITS xemu.toml: the user's with the game's options and the plugin's keys over it, for the session only
                // (XemuSessionConfig) - the user's file merged back from it when xemu has gone.
                var set = new List<(string, string, string)>
                {
                    ("general", "show_welcome", "false"),
                    ("sys.files", "bootrom_path", XemuToml.Literal(mcpx)),
                    ("sys.files", "flashrom_path", XemuToml.Literal(flash)),
                    ("sys.files", "eeprom_path", XemuToml.Literal(eeprom ?? XemuPaths.Eeprom(exe))),
                    ("sys.files", "hdd_path", XemuToml.Literal(hdd)),
                    ("sys.files", "dvd_path", XemuToml.Literal(dvd)),
                };
                set.AddRange(XemuOptions.TomlOf(options));
                XemuSessionConfig.Make(toml, sessionToml, set);
                Log.Info("launch: " + Path.GetFileName(rom) + " (" + titleId + " \"" + info.Xbe?.TitleName + "\", " + info.Kind + ") on " + Path.GetFileName(hdd)
                         + ", " + Path.GetFileName(sessionToml) + " with " + string.Join(", ", set.Skip(6).Select(s => s.Item1 + "." + s.Item2 + "=" + s.Item3)));

                var line = CommandLineFor(Safe(() => args?.CurrentCommandLine) ?? "", dvd, rom, sessionToml);
                Log.Info("command line: " + line);
                XemuSession.Watch(exe, hdd, info);
                return new PrepareForLaunchResponse(success: true) { NewCommandLine = line };
            }
            catch (Exception ex)
            {
                Log.Warn("PrepareEmulatorForLaunch", ex);
                return Refuse("Something went wrong while preparing the game: " + ex.Message);
            }
        }

        /// <summary>The host's line without any -dvd_path (and its value) and -L - and -config_path when one is given - then
        /// [-config_path "&lt;session toml&gt;"] -dvd_path "&lt;disc&gt;" - nothing after: the line replaces the host's whole line. The user's other
        /// options kept with their values (-machine xbox,...); a loose word that is the game's path dropped.</summary>
        internal static string CommandLineFor(string current, string dvd, string rom = null, string configPath = null)
        {
            var tokens = Tokenize(current);
            var kept = new List<string>();
            for (int i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (t.Length == 0) continue;
                bool valued = i + 1 < tokens.Count && tokens[i + 1].Length > 0 && !tokens[i + 1].StartsWith("-");
                if (t.Equals("-dvd_path", StringComparison.OrdinalIgnoreCase) || t.Equals("-L", StringComparison.Ordinal) || (configPath != null && t.Equals("-config_path", StringComparison.OrdinalIgnoreCase)))
                { if (valued) i++; continue; }
                if (!t.StartsWith("-")) { if (!IsTheGame(t, rom)) kept.Add(Quote(t)); continue; }
                kept.Add(Quote(t));
                if (valued) { if (!IsTheGame(tokens[i + 1], rom)) kept.Add(Quote(tokens[i + 1])); i++; }
            }
            if (!kept.Contains("-full-screen")) kept.Insert(0, "-full-screen");
            return string.Join(" ", kept) + (configPath != null ? " -config_path \"" + configPath + "\"" : "") + " -dvd_path \"" + dvd + "\"";
        }

        private static string Quote(string token) => token.IndexOfAny(new[] { ' ', '\t' }) >= 0 ? "\"" + token + "\"" : token;

        private static bool IsTheGame(string token, string rom)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(rom)) return false;
            try { if (string.Equals(Path.GetFullPath(token), Path.GetFullPath(rom), StringComparison.OrdinalIgnoreCase)) return true; } catch { }
            return string.Equals(token, Path.GetFileName(rom), StringComparison.OrdinalIgnoreCase);
        }

        internal static List<string> Tokenize(string line)
        {
            var tokens = new List<string>();
            var current = new System.Text.StringBuilder();
            bool quoted = false, any = false;
            foreach (var c in line ?? "")
            {
                if (!quoted && (c == ' ' || c == '\t'))
                {
                    if (any || current.Length > 0) tokens.Add(current.ToString());
                    current.Clear(); any = false;
                    continue;
                }
                if (c == '"') { quoted = !quoted; any = true; continue; }
                current.Append(c);
            }
            if (any || current.Length > 0) tokens.Add(current.ToString());
            return tokens;
        }

        private static PrepareForLaunchResponse Refuse(string why)
        {
            Log.Info("launch refused: " + why.Replace("\n", " "));
            LbipNotice.Show(PackName, why);
            return new PrepareForLaunchResponse(success: false);
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

        internal static string ResolveFullPath(string maybeRelative)
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

        internal static T Safe<T>(Func<T> f)
        {
            try { return f(); } catch { return default; }
        }
    }
}
