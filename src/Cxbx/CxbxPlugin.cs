// Cxbx-Reloaded (original Xbox) for LaunchBox / LiteBox - the Xenia plugin's shape (src\Xenia\XeniaPlugin.cs).
//
// Claim the loader, install and update it from the project's CI builds, launch a game the only way Cxbx-Reloaded can
// open one - an XBE, the disc unpacked first (CxbxPlace) - and keep its saves (CxbxSaves).
//
// THE LAUNCH LINE: cxbxr-ldr.exe /load "<folder>\default.xbe" /df
//   /load    the XBE. cxbx.exe /load, which is LaunchBox's own Cxbx-Reloaded row, answers "Emulation must be launched
//            from cxbxr-ldr.exe!".
//   /df      a key with no use here (the debug file, read only with /dm - absent, DM_NONE), put LAST to take the game's
//            path should the host append it: Cxbx-Reloaded's parser (cliConverter.cpp cliToMapPairs) throws the whole
//            line away at a bare word after the first, so a path left loose would stop the game from starting.
// Talks only to the public SDK; every entry point is defensive (the host swallows exceptions).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;
using LbIntegrations.Catalog;
using LbIntegrations.Lbip;

namespace LbIntegrations.Cxbx
{
    public partial class CxbxPlugin : EmulatorPlugin, ISystemEventsPlugin, ILbCatalogSource
    {
        private const string Repo = "Cxbx-Reloaded/Cxbx-Reloaded";
        private const string TagPrefix = "CI-";
        private const string XboxPlatform = "Microsoft Xbox";
        private const string DefaultCommandLine = "/df";

        private static readonly string[] AssetRequired = { "CxbxReloaded-Release" };
        private static readonly string[] AssetExcluded = { "debug" };

        /// <summary>The name this pack publishes under AND the folder it installs into (Emulators\Nixx-Cxbx) - see
        /// XeniaPlugin.PackName for why it is a prefix.</summary>
        private const string PackName = "Nixx-Cxbx";

        public CxbxPlugin()
        {
            Log.Info("plugin constructed, assembly " + typeof(CxbxPlugin).Assembly.Location);
            LbipLog.Use(Log.Info, Log.Warn, Log.Disabled, () => Log.Tracing);
            LbIntegrations.RamDisk.RamDiskLog.Use(Log.Info, (m, ex) => Log.Warn(m, ex));
            LbipRowInjection.Install("com.nixxou.lbip.cxbx", MetadataRows());

            // "Open Nixx-Cxbx..." in LaunchBox's menu opens Cxbx-Reloaded's window, even when the entry names the loader
            // (Mehdi, 03/10). One Process.Start patch for the whole pack - see LbipEmulatorOpened.
            try { ListenForOpening(); }
            catch (Exception ex) { Log.Info("an emulator opened without a game is not redirected here (" + ex.GetType().Name + ": " + ex.Message + ")"); }

            // LaunchBox's Import ROM Files wizard for the original Xbox: the list read by content, then titles and regions (CxbxImport).
            try { CxbxLbImport.Install(); }
            catch (Exception ex) { Log.Info("the import wizard is not watched (" + ex.GetType().Name + ": " + ex.Message + ")"); }
            try { ListenForImports(); }
            catch (Exception ex) { Log.Info("an import's end is not seen here (" + ex.GetType().Name + ": " + ex.Message + ")"); }
        }

        /// <summary>NOT INLINED, and called under a try: LbImportFinished is newer than a Catalog a host may carry.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void ListenForImports()
        {
            LbImportFinished.Register(new CxbxImportFinished());
            LbipImportWatch.Install();
        }

        /// <summary>NOT INLINED, and called under a try: LbEmulatorRedirect is newer than a Catalog a host may carry.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void ListenForOpening()
        {
            LbEmulatorRedirect.Register(new CxbxOpenRedirect());
            if (!LbCatalog.HostWillAsk) LbipEmulatorOpened.Install("com.nixxou.lbip.cxbx");
        }

        private static IEnumerable<LbCatalogEmulator> MetadataRows()
        {
            // Disc images, the archives they come in, and an XBE for a game already unpacked (CxbxGame).
            const string extensions = ".iso; .xiso; .cso; .cci; .chd; .xbe; .zar; .zip; .7z";
            yield return new LbCatalogEmulator
            {
                Name = PackName,
                CommandLine = DefaultCommandLine,
                ApplicableFileExtensions = extensions,
                Url = "https://cxbx-reloaded.co.uk/",
                BinaryFileName = CxbxPaths.Loader,
                AutoExtract = false,
                Platforms =
                {
                    new LbCatalogPlatform { Platform = XboxPlatform, ApplicableFileExtensions = extensions, Recommended = true },
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
                CxbxRamSession.Release("the host has started");
                // A session the host never saw end (killed mid-game): Cxbx-Reloaded's own settings and EEPROM put back.
                try
                {
                    foreach (var emu in PluginHelper.DataManager?.GetAllEmulators() ?? new IEmulator[0])
                    {
                        var path = ResolveFullPath(Safe(() => emu.ApplicationPath));
                        if (CxbxPaths.IsCxbx(path)) CxbxOptions.Restore(path, "the host has started");
                    }
                }
                catch (Exception ex) { Log.Warn("options at start", ex); }
                LbipRowInjection.Install("com.nixxou.lbip.cxbx", MetadataRows());
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
                if (!CxbxPaths.IsCxbx(path)) continue;
                claimed.Add(emu);
                EnsureAutoExtract(emu);
                // Always the loader (Mehdi, 03/10): an entry pointed at cxbx.exe is brought back to cxbxr-ldr.exe. The window
                // is opened from LaunchBox's "Open" menu instead (CxbxOpenRedirect).
                EnsureLaunchMode(emu, path, gui: false);
                EnsureHotkeyScripts(emu);
            }
            var signature = string.Join("|", claimed.Select(e => { try { return e.Title; } catch { return "?"; } }).OrderBy(t => t));
            if (signature != _lastClaimed) { _lastClaimed = signature; Log.Info("GetApplicableEmulators: claimed " + claimed.Count + " emulator(s)"); }
            return claimed;
        }

        /// <summary>AutoExtract OFF: this plugin unpacks the game itself - the disc inside the archive, read as a stream,
        /// never written whole - and to a RAM disk when it can. LaunchBox unpacking it first would write the image to the
        /// disk every time, then this plugin would unpack it a second time.</summary>
        private static void EnsureAutoExtract(IEmulator emu)
        {
            try
            {
                if (!emu.AutoExtract) return;
                emu.AutoExtract = false;
                Log.Info("AutoExtract turned off - this plugin unpacks Xbox games itself");
            }
            catch { }
        }

        /// <summary>The entry pointed at cxbx.exe or cxbxr-ldr.exe. True when it changed.</summary>
        internal static bool EnsureLaunchMode(IEmulator emu, string path, bool gui)
        {
            try
            {
                var to = CxbxPaths.PathForMode(path, ResolveFullPath(path), gui);
                if (to == null) return false;
                emu.ApplicationPath = to;
                Log.Info("emulator \"" + Safe(() => emu.Title) + "\" pointed at " + to + (gui ? " - launched with its window" : " - launched without its window"));
                return true;
            }
            catch (Exception ex) { Log.Warn("could not point the emulator at the executable the options want", ex); return false; }
        }

        public override EmulatorSupportResponse IsPlatformSupported(string platform)
        {
            bool supported = string.Equals((platform ?? "").Trim(), XboxPlatform, StringComparison.InvariantCultureIgnoreCase);
            Log.Verbose("IsPlatformSupported(\"" + platform + "\") -> " + supported);
            return new EmulatorSupportResponse(supported, supported);
        }

        // ── versions ─────────────────────────────────────────────────────────

        /// <summary>The CI tag we installed. Cxbx-Reloaded's executables carry no version a plugin can read - it is
        /// compiled into a string shown in the GUI's title - so the tag is written beside them at each install.</summary>
        public override string GetCurrentVersion(string applicationPath)
        {
            try
            {
                var full = ResolveFullPath(applicationPath);
                return string.IsNullOrWhiteSpace(full) || !File.Exists(full) ? null : CxbxPaths.InstalledTag(full);
            }
            catch (Exception ex) { Log.Warn("could not read the version of " + applicationPath, ex); return null; }
        }

        public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions()
        {
            Log.Info("GetInstallableVersions: asked");
            var release = GitHubReleases.GetNewest(Repo, TagPrefix);
            if (release == null) { Log.Warn("no release information for " + Repo); return null; }
            var assets = GitHubReleases.SelectAssets(release, AssetRequired, AssetExcluded);
            if (assets.Count == 0)
            {
                Log.Warn("release " + release.Tag + " has no asset matching our filters; saw: " + string.Join(", ", release.Assets.Select(a => a.Name)));
                return null;
            }
            if (assets.Count > 1) Log.Warn("release " + release.Tag + " matched " + assets.Count + " assets: " + string.Join(", ", assets.Select(a => a.Name)));
            return assets.Select(a => new EmulatorControllerVersion(a.DownloadUrl, release.Tag ?? "", a.Name)).ToList();
        }

        /// <summary>A tag says nothing about order: "is there an update" is "is the newest tag another one". An install
        /// whose tag we do not know (not installed by us) answers no rather than nagging.</summary>
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
            try
            {
                string url = args?.Version, label = null;
                var latest = GetInstallableVersions()?.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(url))
                {
                    if (latest == null)
                        return new EmulatorInstallResponse("Couldn't determine which build of Cxbx-Reloaded to install. GitHub may be rate limiting this machine; try again in a few minutes.");
                    url = latest.Identifier;
                }
                // The tag of the build being installed - the version this plugin reads back.
                if (latest != null && string.Equals(latest.Identifier, url, StringComparison.OrdinalIgnoreCase)) label = latest.Label;
                label ??= TagFromUrl(url);

                if (CxbxPaths.LoaderRunning())
                    return new EmulatorInstallResponse("Cxbx-Reloaded is running - close it first, then try again.");

                bool reinstall = args?.ExistingEmulator != null;
                string targetDir = reinstall
                    ? Path.GetDirectoryName(ResolveFullPath(args.ExistingEmulator.ApplicationPath))
                    : Path.Combine(LaunchBoxRoot(), "Emulators", PackName);
                if (string.IsNullOrWhiteSpace(targetDir)) return new EmulatorInstallResponse("Couldn't work out where to install Cxbx-Reloaded.");

                Report(args, "Downloading Cxbx-Reloaded...", 0);
                archive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                GitHubReleases.Download(url, archive, p => Report(args, "Downloading Cxbx-Reloaded...", p),
                    () => { try { return args?.ShouldCancelFunc?.Invoke() ?? false; } catch { return false; } });

                Report(args, "Extracting Cxbx-Reloaded...", null);
                Directory.CreateDirectory(targetDir);
                // OVER, never cleared first: a portable install keeps settings.ini and EmuDisk (the saves) in this folder.
                Archives.ExtractOver(archive, targetDir);

                var loader = CxbxPaths.FindLoader(targetDir);
                if (loader == null) return new EmulatorInstallResponse("Cxbx-Reloaded was extracted to " + targetDir + " but no " + CxbxPaths.Loader + " was found in it.");
                var dir = Path.GetDirectoryName(loader);
                // WHICH EXECUTABLE THE ENTRY POINTS AT: the one the Nixx window's "launch with Cxbx-Reloaded's window" wants
                // (Mehdi, 03/10) - the GUI by default - falling back to the loader if a build lacks it.
                var exe = Path.Combine(dir, CxbxPaths.Loader);
                if (!File.Exists(exe)) exe = loader;
                if (label != null) File.WriteAllText(Path.Combine(dir, CxbxPaths.VersionFile), label);

                // PORTABLE, AND NO QUESTION AT THE FIRST START: an empty settings.ini beside the executables is what
                // "Yes" to "Use Cxbx-Reloaded in Portable Mode?" writes (Settings.cpp SetupFile) - Cxbx-Reloaded fills
                // in its defaults the first time it reads it. Never written over one already there.
                var settings = Path.Combine(dir, CxbxPaths.SettingsFile);
                if (!File.Exists(settings)) { File.WriteAllText(settings, ""); Log.Info("portable: " + settings + " created"); }

                if (!CxbxPaths.VcRuntimeX86())
                    Log.Warn("the Visual C++ 2015-2022 runtime (x86) is not installed - Cxbx-Reloaded will not start without it");

                // The whole compatibility list read again, in the background - its failure is a line in the log.
                CxbxCompat.RefreshAll();

                if (reinstall)
                {
                    try { args.ExistingEmulator.ApplicationPath = MakeRelativeToLaunchBox(exe); } catch { }
                    return new EmulatorInstallResponse(args.ExistingEmulator, "Cxbx-Reloaded updated.");
                }
                if (args != null && !args.ShouldCreateEmulator)
                    return new EmulatorInstallResponse("Cxbx-Reloaded was installed to " + targetDir + ", but no emulator entry was requested.");

                var created = CreateEmulator(exe, label);
                if (created == null) return new EmulatorInstallResponse("Cxbx-Reloaded was installed to " + targetDir + " but the emulator entry couldn't be created.");
                return new EmulatorInstallResponse(created, "Cxbx-Reloaded installed.");
            }
            catch (OperationCanceledException) { return new EmulatorInstallResponse("Installation cancelled."); }
            catch (Exception ex)
            {
                Log.Warn("install failed", ex);
                return new EmulatorInstallResponse("Failed to install Cxbx-Reloaded: " + ex.Message);
            }
            finally { try { if (archive != null && File.Exists(archive)) File.Delete(archive); } catch { } }
        }

        /// <summary>".../releases/download/CI-1a2b3c4/CxbxReloaded-Release.zip" -> "CI-1a2b3c4".</summary>
        private static string TagFromUrl(string url)
        {
            try
            {
                var parts = new Uri(url).AbsolutePath.Split('/');
                var at = Array.IndexOf(parts, "download");
                return at >= 0 && at + 1 < parts.Length ? Uri.UnescapeDataString(parts[at + 1]) : null;
            }
            catch { return null; }
        }

        /// <summary>Fill the emulator's AutoHotkey fields - see CxbxAhk. Only a blank field: a script the user wrote is
        /// their answer. On the OBJECT each time - the host hands the same emulator under a new object every time a
        /// window asks (the Flycast lesson).</summary>
        private static void EnsureHotkeyScripts(IEmulator emu)
        {
            try
            {
                var set = new List<string>();
                if (Ours(emu.AutoHotkeyScript, CxbxAhk.Running, CxbxAhk.PreviousRunning)) { emu.AutoHotkeyScript = CxbxAhk.Running; set.Add("running"); }
                if (Ours(emu.ExitAutoHotkeyScript, CxbxAhk.Exit, CxbxAhk.PreviousExit)) { emu.ExitAutoHotkeyScript = CxbxAhk.Exit; set.Add("exit"); }
                if (set.Count > 0) Log.Info("hotkey scripts set: " + string.Join(", ", set));
            }
            catch (Exception ex) { Log.Warn("could not describe the hotkeys on the emulator entry", ex); }
        }

        /// <summary>To be written: blank, or an older script of this plugin's (line endings aside) - never the current one
        /// again, never anyone else's.</summary>
        private static bool Ours(string field, string current, string[] previous)
        {
            if (string.IsNullOrWhiteSpace(field)) return true;
            string Flat(string s) => s.Replace("\r\n", "\n").Trim();
            var f = Flat(field);
            return f != Flat(current) && previous.Any(p => Flat(p) == f);
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
            // DefaultPlatform is NOT set - see XeniaPlugin.CreateEmulator: the edit window turns it into a duplicate row.
            var platform = emu.AddNewEmulatorPlatform();
            platform.Platform = XboxPlatform;
            platform.IsDefault = true;
            try { dm.Save(false); } catch (Exception ex) { Log.Warn("data manager save failed", ex); }
            Log.Info("created emulator entry \"" + PackName + "\"" + (versionLabel != null ? " (" + versionLabel + ")" : "") + " -> " + emu.ApplicationPath);
            return emu;
        }

        // Cxbx-Reloaded is an HLE emulator: no BIOS, no firmware.
        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string platform) => Array.Empty<EmulatorBiosFile>();
        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string emulatorApplicationPath, string platform, string commandLine)
            => Array.Empty<EmulatorBiosFile>();

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
                var rom = ResolveFullPath(Safe(() => args?.GameBeingLaunched?.ApplicationPath));
                if (string.IsNullOrWhiteSpace(exe) || !CxbxPaths.IsCxbx(exe)) return new PrepareForLaunchResponse(success: true);

                if (!CxbxPaths.VcRuntimeX86())
                    return Refuse("Cxbx-Reloaded needs the Microsoft Visual C++ 2015-2022 runtime, 32-bit (x86), and this computer does not have it.\n\n"
                                  + "Install vc_redist.x86.exe from Microsoft, then launch the game again.");
                if (string.IsNullOrWhiteSpace(rom)) return Refuse("No game was given to Cxbx-Reloaded.");
                // Its window open: it writes settings.ini when it closes - over the game's options, or over their putting back.
                if (GuiOpen(exe))
                    return Refuse("Cxbx-Reloaded's window is open. Close it first - its settings are saved as it closes - then launch the game again.");

                var xbe = CxbxPlace.Prepare(rom, Safe(() => args?.GameBeingLaunched?.Id), exe, out var problem, out var described);
                if (xbe == null) return Refuse(Path.GetFileName(rom) + " cannot be launched: " + problem + ".");

                bool gui = CxbxPaths.IsGui(exe);
                var line = CommandLineFor(Safe(() => args?.CurrentCommandLine) ?? "", xbe, rom, gui);
                Log.Info("command line: " + line);
                var settings = CxbxSettings.Read();
                // The game's state in the compatibility list, in the log - and asked again in the background (CxbxCompat).
                try
                {
                    var info = described?.Xbe ?? Xbe.Read(xbe);
                    Log.Info("compatibility: " + CxbxCompat.Describe(info));
                    CxbxCompat.RefreshGame(info);
                }
                catch (Exception ex) { Log.Info("compatibility: " + ex.Message); }
                // The game's options (CxbxOptions) written for its session into settings.ini and EEPROM.bin - put back once it
                // is over - and the console's region made the game's, or Cxbx-Reloaded stops on a question first (CxbxEeprom).
                CxbxOptions.Apply(exe, Safe(() => args?.GameBeingLaunched?.Id), described?.Xbe ?? Xbe.Read(xbe));
                // Its save file is the active save: put in lbip-saves\ since the last session, it is laid out now (CxbxSaves.SyncIn).
                var launchTitle = (described?.Xbe ?? Xbe.Read(xbe))?.TitleId > 0 ? (described?.Xbe ?? Xbe.Read(xbe)).TitleIdText : null;
                if (launchTitle != null)
                {
                    try { if (CxbxSaves.SyncIn(exe, launchTitle) is string synced) Log.Info("saves: " + launchTitle + " - " + synced); }
                    catch (Exception ex) { return Refuse("The save of " + Path.GetFileName(rom) + " could not be laid out: " + ex.Message + "\n\nNothing was changed. Move or remove its file in " + Path.GetDirectoryName(CxbxSaves.PackPath(exe, launchTitle)) + " to start without it."); }
                }
                // Full screen: a sub-option of the window's (Mehdi, 03/10); the loader alone always starts full screen.
                bool borderless = CxbxSettings.On(settings, "borderless", true) && !CxbxSession.ExclusiveFullScreen(exe);
                CxbxSession.Watch(exe, described?.Xbe?.TitleId > 0 ? described.Xbe.TitleIdText : null, borderless,
                                  closeGui: gui);     // an entry left on cxbx.exe: its window closed with the game
                return new PrepareForLaunchResponse(success: true) { NewCommandLine = line };
            }
            catch (Exception ex)
            {
                Log.Warn("PrepareEmulatorForLaunch", ex);
                return Refuse("Something went wrong while preparing the game: " + ex.Message);
            }
        }

        /// <summary>Is cxbx.exe running from this install's folder?</summary>
        private static bool GuiOpen(string exe)
        {
            try
            {
                var gui = Path.Combine(Path.GetDirectoryName(exe) ?? "", CxbxPaths.Gui);
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(CxbxPaths.Gui)))
                    using (p)
                    {
                        string path = null;
                        try { path = p.MainModule?.FileName; } catch { }
                        if (path == null || string.Equals(Path.GetFullPath(path), Path.GetFullPath(gui), StringComparison.OrdinalIgnoreCase)) return true;
                    }
            }
            catch { }
            return false;
        }

        private static PrepareForLaunchResponse Refuse(string why)
        {
            Log.Info("launch refused: " + why.Replace("\n", " "));
            LbipNotice.Show(PackName, why);
            return new PrepareForLaunchResponse(success: false);
        }

        /// <summary>The host's line with the game's path and any /load, /df and /dm taken out, then the XBE - /load "&lt;xbe&gt;"
        /// for the loader, "&lt;xbe&gt;" as the very first word for the GUI (WinMain.cpp reads it as arg1, and only there) -
        /// the user's other keys with their values, and /df last. Measured 03/10 on both.</summary>
        internal static string CommandLineFor(string current, string xbe, string rom, bool gui = false)
        {
            var tokens = Tokenize(current);
            var kept = new List<string>();
            for (int i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (t.Length == 0) continue;
                if (t[0] != '/')
                {
                    if (!IsTheGame(t, rom)) Log.Info("command line: \"" + t + "\" dropped - Cxbx-Reloaded refuses a line with a word that is not a /key's value");
                    continue;
                }
                bool valued = i + 1 < tokens.Count && tokens[i + 1].Length > 0 && tokens[i + 1][0] != '/';
                var key = t.Substring(1);
                if (key.Equals("load", StringComparison.OrdinalIgnoreCase) || key.Equals("df", StringComparison.OrdinalIgnoreCase) || key.Equals("dm", StringComparison.OrdinalIgnoreCase))
                { if (valued) i++; continue; }
                kept.Add(t);
                if (valued) { if (!IsTheGame(tokens[i + 1], rom)) kept.Add(Quote(tokens[i + 1])); i++; }
            }
            return ((gui ? "" : "/load ") + Quote(xbe, force: true) + " " + string.Join(" ", kept) + " /df").Replace("  ", " ");
        }

        private static bool IsTheGame(string token, string rom)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(rom)) return false;
            try { if (string.Equals(Path.GetFullPath(token), Path.GetFullPath(rom), StringComparison.OrdinalIgnoreCase)) return true; } catch { }
            return string.Equals(token, Path.GetFileName(rom), StringComparison.OrdinalIgnoreCase)
                   || string.Equals(token, Path.GetFileNameWithoutExtension(rom), StringComparison.OrdinalIgnoreCase);
        }

        private static string Quote(string token, bool force = false)
            => force || token.IndexOfAny(new[] { ' ', '\t' }) >= 0 ? "\"" + token + "\"" : token;

        /// <summary>Spaces outside quotes separate, quotes group and are dropped.</summary>
        internal static List<string> Tokenize(string line)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
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

        internal static T Safe<T>(Func<T> f)
        {
            try { return f(); } catch { return default; }
        }
    }
}
