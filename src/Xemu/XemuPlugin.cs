// xemu (original Xbox) for LaunchBox / LiteBox - the Cxbx plugin's shape (src\Cxbx\CxbxPlugin.cs), further than Unbroken's
// own Xemu plugin (decompiled 04/10: download, xemu.toml, the dashboard's disk, the BIOS list - and nothing for discs or saves).
//
// Claim an install of ours (XemuPaths.IsOurs), install and update it from xemu's stable releases, launch a game on a disc
// xemu can open (XemuDisc: an XISO as it is, a redump / CSO / CCI / CHD / zipped image cut to its XISO) and on ITS OWN
// CONSOLE (Mehdi, 04/10: "un disque par jeu"): hdd\games\<title id>.qcow2, a qcow2 of differences over the pristine
// hdd\base.qcow2 (Qcow2Overlay) - that file is the game's save (XemuSaves).
//
// THE LAUNCH LINE: xemu.exe -full-screen -dvd_path "<xiso>" -L
//   -dvd_path   xemu's own (system/vl.c), over [sys.files] dvd_path
//   -L          QEMU's firmware search folder - no use to xemu, put LAST to take the game's path the host appends after the
//               line (NoGbaRoms.cs: the host appends the ROM after NewCommandLine): a bare path would be taken by QEMU for a
//               hard disk. Not -name: QEMU splits its value at commas, and "(En,Fr,De)" is in half the names. TO MEASURE.
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
                // A session the host never saw end (killed mid-game): xemu.toml back on the stand-alone console.
                foreach (var emu in PluginHelper.DataManager?.GetAllEmulators() ?? new IEmulator[0])
                {
                    var path = ResolveFullPath(Safe(() => emu.ApplicationPath));
                    if (XemuPaths.IsOurs(path)) XemuSession.Standalone(path, "the host has started");
                }
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
                if (XemuPaths.Running(Path.Combine(targetDir, XemuPaths.Exe))) return new EmulatorInstallResponse("xemu is running - close it first, then try again.");

                Report(args, "Downloading xemu...", 0);
                archive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                GitHubReleases.Download(url, archive, p => Report(args, "Downloading xemu...", p), cancelled);
                Report(args, "Extracting xemu...", null);
                Directory.CreateDirectory(targetDir);
                // OVER, never cleared first: xemu.toml, eeprom.bin, the BIOS and every game's console live in this folder.
                Archives.ExtractOver(archive, targetDir);
                var exe = XemuPaths.FindExe(targetDir);
                if (exe == null) return new EmulatorInstallResponse("xemu was extracted to " + targetDir + " but no " + XemuPaths.Exe + " was found in it.");
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

                var dvd = XemuDisc.Present(rom, exe, out var problem, out var info);
                if (dvd == null) return Refuse(Path.GetFileName(rom) + " cannot be launched: " + problem + ".");
                XemuDisc.Remember(rom, info.Xbe);

                // ITS OWN CONSOLE: made over the pristine one at the game's first launch.
                var titleId = info.Xbe?.TitleId > 0 ? info.Xbe.TitleIdText : null;
                if (titleId == null) return Refuse(Path.GetFileName(rom) + " cannot be launched: its title id could not be read.");
                var hdd = XemuPaths.GameHdd(exe, titleId);
                if (!File.Exists(hdd))
                {
                    var error = Qcow2Overlay.Create(base_, hdd);
                    if (error != null) return Refuse("The console of " + Path.GetFileName(rom) + " could not be made: " + error + ".");
                }

                var toml = XemuPaths.TomlOf(exe);
                XemuToml.Set(toml, "general", "show_welcome", "false");
                XemuToml.Set(toml, "sys.files", "bootrom_path", XemuToml.Literal(mcpx));
                XemuToml.Set(toml, "sys.files", "flashrom_path", XemuToml.Literal(flash));
                XemuToml.Set(toml, "sys.files", "eeprom_path", XemuToml.Literal(XemuPaths.Eeprom(exe)));
                XemuToml.Set(toml, "sys.files", "hdd_path", XemuToml.Literal(hdd));
                Log.Info("launch: " + Path.GetFileName(rom) + " (" + titleId + " \"" + info.Xbe?.TitleName + "\", " + info.Kind + ") on " + Path.GetFileName(hdd));

                var line = CommandLineFor(Safe(() => args?.CurrentCommandLine) ?? "", dvd, rom);
                Log.Info("command line: " + line);
                XemuSession.Watch(exe, hdd);
                return new PrepareForLaunchResponse(success: true) { NewCommandLine = line };
            }
            catch (Exception ex)
            {
                Log.Warn("PrepareEmulatorForLaunch", ex);
                return Refuse("Something went wrong while preparing the game: " + ex.Message);
            }
        }

        /// <summary>The host's line without any -dvd_path (and its value) and -L, then -dvd_path "&lt;disc&gt;" -L last - to take
        /// the game's path the host appends. The user's other options kept with their values (-machine xbox,...); a loose
        /// word that is the game's path dropped - the host puts it back.</summary>
        internal static string CommandLineFor(string current, string dvd, string rom = null)
        {
            var tokens = Tokenize(current);
            var kept = new List<string>();
            for (int i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (t.Length == 0) continue;
                bool valued = i + 1 < tokens.Count && tokens[i + 1].Length > 0 && !tokens[i + 1].StartsWith("-");
                if (t.Equals("-dvd_path", StringComparison.OrdinalIgnoreCase) || t.Equals("-L", StringComparison.Ordinal))
                { if (valued) i++; continue; }
                if (!t.StartsWith("-")) { if (!IsTheGame(t, rom)) kept.Add(Quote(t)); continue; }
                kept.Add(Quote(t));
                if (valued) { if (!IsTheGame(tokens[i + 1], rom)) kept.Add(Quote(tokens[i + 1])); i++; }
            }
            if (!kept.Contains("-full-screen")) kept.Insert(0, "-full-screen");
            return string.Join(" ", kept) + " -dvd_path \"" + dvd + "\" -L";
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
