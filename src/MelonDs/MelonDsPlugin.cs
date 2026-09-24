// melonDS integration for LaunchBox / LiteBox.
//
// Claim the emulator, report and install versions, choose DS or DSi mode per ROM, and tell the host
// about keys melonDS already has. Save management lives next door in MelonDsSaves.cs.
//
// Like the other three, this talks ONLY to the public SDK - no reference to any LaunchBox core
// assembly - so it behaves identically under both hosts. Every entry point is defensive: the host
// swallows exceptions silently, which turns a bug into a feature that quietly does nothing, so we
// catch, log and degrade instead.
//
// THE INSTALL IS ONE FILE. melonDS-1.1-windows-x86_64.zip has a single entry, melonDS.exe, linked
// statically (BUILD_STATIC=ON in the release preset). There is no Qt DLL to place, no plugins\
// folder, nothing to keep in step - which is also why the extraction can be over the folder without
// leaving stale files behind.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;
using LbIntegrations.Dsi;
using LbIntegrations.Catalog;
using LbIntegrations.Lbip;

namespace LbIntegrations.MelonDs
{
    public partial class MelonDsPlugin : EmulatorPlugin, ISystemEventsPlugin, ILbCatalogSource
    {
        private const string Repo = "melonDS-emu/melonDS";

        /// <summary>Spelled as LaunchBox.Metadata.db spells it - verified against the database rather
        /// than typed from memory. There is no Nintendo DSi platform in it; DSi titles live under this
        /// one, and the console mode is decided from the ROM instead. See PrepareEmulatorForLaunch.</summary>
        private const string DsPlatform = "Nintendo DS";

        /// <summary>LaunchBox's metadata knows one DS platform and no DSi one, so anybody with a
        /// DSiWare library has invented a name for it - measured on a real install, "Nintendo DSiware",
        /// and the community also writes "Nintendo DSi" and "Nintendo DSiWare". Claiming them matters
        /// now that this plugin treats DSiWare as a case of its own rather than a curiosity.
        ///
        /// Compared without spaces or case, so the three spellings collapse into two names.</summary>
        private static readonly string[] DsiPlatforms = { "nintendodsi", "nintendodsiware" };

        private static bool IsDsiPlatform(string platform)
        {
            var name = (platform ?? "").Replace(" ", "").Replace("-", "");
            foreach (var candidate in DsiPlatforms)
                if (string.Equals(name, candidate, StringComparison.InvariantCultureIgnoreCase)) return true;
            return false;
        }

        private static bool IsDsPlatform(string platform)
            => string.Equals((platform ?? "").Trim(), DsPlatform, StringComparison.InvariantCultureIgnoreCase);

        /// <summary>-f is fullscreen (CLI.cpp:45). The ROM goes in as a positional argument, which the
        /// host appends, so nothing else is needed. There is deliberately no --boot: its default of
        /// "auto" already boots when a ROM is given (CLI.cpp:71-87).</summary>
        private const string DefaultCommandLine = "-f";

        /// <summary>The Windows x86_64 zip. Same filters Freegosy uses, which is where they were
        /// taken from; the aarch64 build is a separate asset and would match "windows" alone.</summary>
        private static readonly string[] AssetRequired = { "windows", "x86_64", ".zip" };
        private static readonly string[] AssetExcluded = { "source", "debug", "libretro", "appimage", "macos" };

        private static readonly string[] AssetRequiredArm = { "windows", "aarch64", ".zip" };

        // Traced because a plugin is a black box inside LaunchBox: the only way to tell "the host
        // never asked us" from "we answered badly" is to see which members it actually calls.
        public MelonDsPlugin()
        {
            Log.Info("plugin constructed, assembly " + typeof(MelonDsPlugin).Assembly.Location);

            // THE SHARED ENGINE LEARNS WHOSE PLUGIN IT IS IN, first of all: it is compiled into
            // two of them and cannot tell on its own which logger to write to, nor which process
            // name means "the emulator is running". See MelonDsHost.
            MelonDsHost.Announce();

            // THE SHARED ROW INJECTION LEARNS WHOSE PLUGIN IT IS IN. It is compiled into all
            // five plugins and cannot tell on its own which log file to write to, nor which kill
            // switches to read. See LbipLog.
            LbipLog.Use(Log.Info, Log.Warn, Log.Disabled, () => Log.Tracing);

            // As early as possible: the patch only sees connections opened AFTER it is installed, and
            // LaunchBox reads its metadata the moment a window asks for it.
            LbipRowInjection.Install("com.nixxou.lbip.melonds", MetadataRows());
        }

        /// <summary>The name this pack publishes under, in one place so its three uses cannot
        /// disagree: the row in LaunchBox's emulator catalogue, the entry Add Emulator offers, and
        /// the title given to an emulator this plugin creates. The prefix says whose integration it
        /// is - LaunchBox's catalogue knows no standalone DS
        /// emulator at all, so ours is the only one there.</summary>
        private const string PackName = "Nixx-melonDS";

        public override string EmulatorName => PackName;

        /// <summary>What this plugin brings to a host's emulator catalogue, for a host that asks
        /// rather than one whose database has to be patched. Same rows either way - see
        /// MetadataRows.</summary>
        public IEnumerable<LbCatalogEmulator> EmulatorRows() => MetadataRows();

        /// <summary>What LaunchBox's emulator metadata should say about melonDS, published through the
        /// row injection rather than written into their database - see LbipRowInjection.
        ///
        /// This is the plugin with the strongest case for it: LaunchBox.Metadata.db knows no
        /// standalone DS emulator at all, so without these rows the Add Emulator window offers
        /// RetroArch and nothing else for Nintendo DS.
        ///
        /// Every value is one this plugin already answers elsewhere: the platform is DsPlatform, the
        /// recommendation matches IsPlatformSupported, the command line is DefaultCommandLine, and the
        /// archive extensions are there because ARCHIVE_SUPPORT_ENABLED is defined unconditionally in
        /// melonDS's build (src/frontend/qt_sdl/CMakeLists.txt:92) with libarchive as a required
        /// dependency. No BIOS is declared required, because DS mode needs none.</summary>
        private static IEnumerable<LbCatalogEmulator> MetadataRows()
        {
            // The ROM extensions are melonDS's own (Window.cpp:95). The containers are NOT its full
            // list - libarchive accepts more than SharpCompress does, and a container this plugin
            // cannot open is one whose inner entry name it cannot read, which is what the save is
            // named after. These four were measured by handing the plugin one of each; see the
            // ArchiveFormats part of the probe.
            const string extensions = ".nds; .srl; .dsi; .ids; .zip; .7z; .rar; .tar; .tgz";

            yield return new LbCatalogEmulator
            {
                Name = PackName,
                CommandLine = DefaultCommandLine,
                ApplicableFileExtensions = extensions,
                Url = "https://melonds.kuribo64.net/",
                BinaryFileName = MelonDsPaths.ExecutableName,
                AutoExtract = false,
                Platforms =
                {
                    new LbCatalogPlatform
                    {
                        Platform = DsPlatform,
                        ApplicableFileExtensions = extensions,
                        Recommended = true,
                    },
                },
            };
        }

        // ── the host is up ───────────────────────────────────────────────────

        /// <summary>Retry the metadata patch once the host has finished loading. The constructor is
        /// the earliest moment, which is what we want, but it may be TOO early: the patch needs
        /// Microsoft.Data.Sqlite to be loaded already, and assemblies load on first use. Install is a
        /// no-op once it has succeeded, so retrying costs nothing.</summary>
        public void OnEventRaised(string eventType)
        {
            try
            {
                if (eventType != SystemEventTypes.PluginInitialized
                    && eventType != SystemEventTypes.LaunchBoxStartupCompleted
                    && eventType != SystemEventTypes.BigBoxStartupCompleted) return;

                Log.Info("host event \"" + eventType + "\"");
                LbipRowInjection.Install("com.nixxou.lbip.melonds", MetadataRows());
            }
            catch (Exception ex) { Log.Warn("OnEventRaised", ex); }
        }

        // ── claiming ─────────────────────────────────────────────────────────

        /// <summary>The last set we reported claiming, so the log says it once.</summary>
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
                if (!MelonDsPaths.IsMelonDsExecutable(path)) continue;
                claimed.Add(emu);

                // Undo what an earlier version might have written - see the note in CreateEmulator.
                try
                {
                    if (string.Equals(emu.DefaultPlatform, DsPlatform,
                                      StringComparison.InvariantCultureIgnoreCase))
                    {
                        emu.DefaultPlatform = "";
                        Log.Info("cleared DefaultPlatform - the edit window turns it into a duplicate row");
                    }
                }
                catch { }

                EnsureHotkeyScripts(emu);
            }
            // The host asks this constantly - twenty-three times in one second, measured - and the
            // answer almost never changes. Said once, then only when it does.
            var signature = string.Join("|", claimed.Select(e => { try { return e.Title; } catch { return "?"; } })
                                                    .OrderBy(t => t));
            if (signature != _lastClaimed)
            {
                _lastClaimed = signature;
                Log.Info("GetApplicableEmulators: claimed " + claimed.Count + " emulator(s)");
            }
            return claimed;
        }

        /// <summary>Describe melonDS's keys in the emulator's AutoHotkey fields - what the pause screen
        /// of LaunchBox and BigBox sends.
        ///
        /// Nothing is read from disk here, unlike the Flycast and PPSSPP plugins: melonDS's save-state
        /// keys are Qt menu shortcuts set in code, not configuration. See MelonDsAhk.
        ///
        /// Only a blank field is filled: a script the user wrote is his answer to the same question.
        /// The idempotence is on the OBJECT, never on the executable - the host hands us the same
        /// emulator under a new object every time a window asks, and remembering "this executable is
        /// done" fills the first and leaves every later one empty, which is exactly the shape that
        /// cost an evening on Flycast.</summary>
        private static void EnsureHotkeyScripts(IEmulator emu)
        {
            try
            {
                if (!IsBlank(() => emu.AutoHotkeyScript)
                    && !IsBlank(() => emu.ExitAutoHotkeyScript)
                    && !IsBlank(() => emu.SaveStateAutoHotkeyScript)
                    && !IsBlank(() => emu.LoadStateAutoHotkeyScript)) return;

                var set = new List<string>();
                if (Fill(() => emu.AutoHotkeyScript, v => emu.AutoHotkeyScript = v, MelonDsAhk.Running))
                    set.Add("running");
                if (Fill(() => emu.ExitAutoHotkeyScript, v => emu.ExitAutoHotkeyScript = v, MelonDsAhk.Exit))
                    set.Add("exit");
                if (Fill(() => emu.SaveStateAutoHotkeyScript,
                         v => emu.SaveStateAutoHotkeyScript = v, MelonDsAhk.SaveState))
                    set.Add("save");
                if (Fill(() => emu.LoadStateAutoHotkeyScript,
                         v => emu.LoadStateAutoHotkeyScript = v, MelonDsAhk.LoadState))
                    set.Add("load");

                if (set.Count > 0)
                    Log.Info("hotkey scripts set: " + string.Join(", ", set));
            }
            catch (Exception ex) { Log.Warn("could not describe the hotkeys on the emulator entry", ex); }
        }

        private static bool IsBlank(Func<string> get)
        {
            try { return string.IsNullOrWhiteSpace(get()); } catch { return false; }
        }

        /// <summary>Write the script only into a field the user left blank. True when it landed.</summary>
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
            bool supported = IsDsPlatform(platform) || IsDsiPlatform(platform);
            Log.Verbose("IsPlatformSupported(\"" + platform + "\") -> " + supported);
            return new EmulatorSupportResponse(supported, supported);
        }

        // ── versions ─────────────────────────────────────────────────────────

        /// <summary>The installed build, read from the executable's Win32 version resource. melonDS
        /// tags its releases "1.1", and that is what the resource carries too, so the two compare
        /// directly - no SHA gymnastics like Xenia's.</summary>
        public override string GetCurrentVersion(string applicationPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(applicationPath) || !File.Exists(applicationPath)) return null;
                var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(applicationPath);
                var raw = info?.ProductVersion ?? info?.FileVersion;
                var version = NormalizeVersion(raw);
                if (version == null) Log.Info("version resource says \"" + raw + "\" - not a version we can read");
                return version;
            }
            catch (Exception ex) { Log.Warn("could not read the version of " + applicationPath, ex); return null; }
        }

        /// <summary>"1.1.0.0" and "1.1" are the same release. Trailing zero components are dropped so a
        /// resource written four-part compares equal to a two-part tag.</summary>
        private static string NormalizeVersion(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var text = raw.Trim().TrimStart('v', 'V');
            var parts = text.Split('.');
            int last = parts.Length - 1;
            while (last > 0 && int.TryParse(parts[last], out var n) && n == 0) last--;
            var kept = string.Join(".", parts.Take(last + 1));
            return kept.Length == 0 ? null : kept;
        }

        public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions()
        {
            Log.Info("GetInstallableVersions: asked");
            var release = GitHubReleases.GetLatest(Repo);
            if (release == null) { Log.Warn("no release information for " + Repo); return null; }

            var assets = GitHubReleases.SelectAssets(release, AssetRequired, AssetExcluded);
            if (assets.Count == 0)
            {
                assets = GitHubReleases.SelectAssets(release, AssetRequiredArm, AssetExcluded);
                if (assets.Count > 0) Log.Info("no x86_64 asset in " + release.Tag + "; offering the ARM64 build");
            }
            if (assets.Count == 0)
            {
                Log.Warn("release " + release.Tag + " has no Windows asset matching our filters; saw: "
                         + string.Join(", ", release.Assets.Select(a => a.Name)));
                return null;
            }
            if (assets.Count > 1)
                Log.Warn("release " + release.Tag + " matched " + assets.Count + " assets: "
                         + string.Join(", ", assets.Select(a => a.Name)));

            string label = NormalizeVersion(release.Tag) ?? release.Tag ?? "";
            return assets.Select(a => new EmulatorControllerVersion(a.DownloadUrl, label, a.Name)).ToList();
        }

        /// <summary>The SDK's default compares the two strings and calls anything unequal - including a
        /// null current version - an update, which is how an install with no readable version ends up
        /// wearing a permanent update badge. Compare the release parts instead.</summary>
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
                return !string.Equals(current, NormalizeVersion(version.Label) ?? version.Label,
                                      StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) { Log.Warn("update check failed", ex); return false; }
        }

        // ── installation ─────────────────────────────────────────────────────

        public override EmulatorInstallResponse InstallEmulator(InstallEmulatorArgs args)
        {
            string archive = null;
            try
            {
                string url = args?.Version;
                string label = null;
                if (string.IsNullOrWhiteSpace(url))
                {
                    var latest = GetInstallableVersions()?.FirstOrDefault();
                    if (latest == null)
                        return new EmulatorInstallResponse(
                            "Couldn't determine which build of melonDS to install. GitHub may be rate "
                            + "limiting this machine; try again in a few minutes.");
                    url = latest.Identifier;
                    label = latest.Label;
                }

                bool reinstall = args?.ExistingEmulator != null;
                string targetDir = reinstall
                    ? Path.GetDirectoryName(ResolveFullPath(args.ExistingEmulator.ApplicationPath))
                    : Path.Combine(LaunchBoxRoot(), "Emulators", "melonDS");
                if (string.IsNullOrWhiteSpace(targetDir))
                    return new EmulatorInstallResponse("Couldn't work out where to install melonDS.");

                Report(args, "Downloading melonDS...", 0);
                archive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + SafeExtension(url));
                GitHubReleases.Download(url, archive,
                    p => Report(args, "Downloading melonDS...", p),
                    () => { try { return args?.ShouldCancelFunc?.Invoke() ?? false; } catch { return false; } });

                Report(args, "Extracting melonDS...", null);
                Directory.CreateDirectory(targetDir);
                // Extract OVER, never clear first: a portable melonDS keeps melonDS.toml, its saves and
                // its savestates inside this very folder.
                Archives.ExtractOver(archive, targetDir);

                string exe = MelonDsPaths.FindExecutable(targetDir);
                if (exe == null)
                    return new EmulatorInstallResponse(
                        "melonDS was downloaded and extracted to " + targetDir
                        + " but no melonDS executable was found in it.");

                var layout = MelonDsPaths.Resolve(exe);
                Log.Info("installed to " + targetDir + " - configuration: " + layout.ConfigFile
                         + " (" + layout.Reason + ")");
                RedirectSavePaths(layout);

                if (reinstall)
                {
                    try { args.ExistingEmulator.ApplicationPath = MakeRelativeToLaunchBox(exe); } catch { }
                    return new EmulatorInstallResponse(args.ExistingEmulator, "melonDS updated.");
                }
                if (args != null && !args.ShouldCreateEmulator)
                    return new EmulatorInstallResponse(
                        "melonDS was installed to " + targetDir + ", but no emulator entry was requested.");

                var created = CreateEmulator(exe, label);
                if (created == null)
                    return new EmulatorInstallResponse(
                        "melonDS was installed to " + targetDir
                        + " but the emulator entry couldn't be created (no data manager available).");
                return new EmulatorInstallResponse(created, "melonDS installed.");
            }
            catch (OperationCanceledException) { return new EmulatorInstallResponse("Installation cancelled."); }
            catch (Exception ex)
            {
                Log.Warn("install failed", ex);
                return new EmulatorInstallResponse("Failed to install melonDS: " + ex.Message);
            }
            finally
            {
                try { if (archive != null && File.Exists(archive)) File.Delete(archive); } catch { }
            }
        }

        /// <summary>Give an install of OUR making a saves\ and a savestates\ folder, and point melonDS
        /// at them.
        ///
        /// Out of the box melonDS writes the .sav next to the ROM (EmuInstance.cpp:445-484, an empty
        /// SaveFilePath means "the ROM's directory"), which scatters save files through a user's ROM
        /// library and fails outright when the ROM sits on read-only media. One folder is also what
        /// makes save management tractable, the way PPSSPP's memstick does.
        ///
        /// ONLY ON AN INSTALL WE MADE, and only into a key that is absent or empty. A melonDS the user
        /// set up himself keeps whatever he chose - including the default - and MelonDsSaves reads both
        /// dispositions for exactly that reason.
        ///
        /// The paths are written ABSOLUTE: getAssetPath uses the configured value as a directory
        /// directly, so a relative one would resolve against the process's working directory, which
        /// belongs to whoever launched the emulator.</summary>
        private static void RedirectSavePaths(MelonDsLayout layout)
        {
            try
            {
                if (layout?.ConfigFile == null) return;

                var wanted = new Dictionary<string, string>(StringComparer.Ordinal);
                if (!layout.HasRedirectedSaves)
                    wanted[MelonDsPaths.KeySaveFilePath] = MelonDsToml.Text(layout.DefaultSaveDir);
                if (!layout.HasRedirectedStates)
                    wanted[MelonDsPaths.KeySavestatePath] = MelonDsToml.Text(layout.DefaultStateDir);
                // Both folders, and before the early return below: somebody whose save paths were
                // already configured still needs somewhere to put a NAND.
                DsiWorkspace.PrepareFolder(layout);
                MelonDsBios.Prepare(layout);

                // AND A KEYBOARD, because melonDS ships without one. Its default table gives every
                // key -1 and there is no table of defaults anywhere else, so an installation this
                // plugin just made cannot be played at all until somebody opens Config > Input and
                // clicks twelve times. See MelonDsInput - it writes only what is unbound.
                MelonDsInput.EnsureDefaults(layout, out _);

                if (wanted.Count == 0)
                {
                    Log.Info("save paths already configured; leaving them alone");
                    return;
                }

                try { Directory.CreateDirectory(layout.DefaultSaveDir); } catch { }
                try { Directory.CreateDirectory(layout.DefaultStateDir); } catch { }

                var error = MelonDsToml.Write(layout.ConfigFile, MelonDsPaths.InstanceTable, wanted);
                if (error != null) { Log.Warn("save paths not written: " + error); return; }

                if (wanted.ContainsKey(MelonDsPaths.KeySaveFilePath)) layout.SaveDir = layout.DefaultSaveDir;
                if (wanted.ContainsKey(MelonDsPaths.KeySavestatePath)) layout.StateDir = layout.DefaultStateDir;
                Log.Info("save paths set in " + layout.ConfigFile + ": " + string.Join(", ", wanted.Keys));
            }
            catch (Exception ex) { Log.Warn("could not set the save paths", ex); }
        }

        private static string SafeExtension(string url)
        {
            try
            {
                var ext = Path.GetExtension(new Uri(url).AbsolutePath);
                return string.IsNullOrEmpty(ext) ? ".bin" : ext;
            }
            catch { return ".bin"; }
        }

        private static IEmulator CreateEmulator(string exePath, string versionLabel)
        {
            var dm = PluginHelper.DataManager;
            if (dm == null) return null;

            var emu = dm.AddNewEmulator();
            emu.Title = PackName;
            emu.ApplicationPath = MakeRelativeToLaunchBox(exePath);
            emu.CommandLine = DefaultCommandLine;
            EnsureHotkeyScripts(emu);

            // DefaultPlatform IS NOT SET, and that is the point.
            //
            // Measured on a real library: the Edit Emulator window adds a platform row for whatever
            // this field names, ON TOP of the association that already exists - so an emulator whose
            // DefaultPlatform is set grows a duplicate row every time its window is opened, and the
            // duplicate is saved if the user clicks OK. None of Unbroken's own plugins set it either.
            // What actually matters is IsDefault on each platform row, which says "this emulator is
            // the default FOR that platform", and we do set that.

            var platform = emu.AddNewEmulatorPlatform();
            platform.Platform = DsPlatform;
            platform.IsDefault = true;

            try { dm.Save(false); } catch (Exception ex) { Log.Warn("data manager save failed", ex); }
            Log.Info("created emulator entry \"melonDS\""
                     + (versionLabel != null ? " (" + versionLabel + ")" : "") + " -> " + emu.ApplicationPath);
            return emu;
        }

        // ── BIOS ─────────────────────────────────────────────────────────────

        /// <summary>DS mode needs nothing, and that is measured rather than assumed.
        /// Emu.ExternalBIOSEnable is absent from melonDS's default table (Config.cpp:96-114) so it is
        /// false, and with it false loadARM9BIOS / loadARM7BIOS return the built-in FreeBIOS
        /// (EmuInstance.cpp:868-896) while loadFirmware GENERATES a firmware (EmuInstance.cpp:1013-1025).
        ///
        /// The three files are therefore declared OPTIONAL: a user who has real dumps gets better
        /// compatibility and the DS boot animation, and a user who has none is not told he is missing
        /// something he does not need.
        ///
        /// DSi mode is the other regime entirely - verifySetup calls verifyDSiBIOS and verifyDSiNAND
        /// unconditionally (EmuInstance.cpp:633-659), and there is no generated DSi firmware. It is not
        /// declared here because LaunchBox has no Nintendo DSi platform to declare it against; see
        /// PrepareEmulatorForLaunch, which refuses DSi mode when those files are missing and says so.
        ///
        /// The MD5s come from Freegosy's BIOS registry (MIT); melonDS itself validates by size
        /// (EmuInstance.cpp:492-506), not by hash.</summary>
        /// <summary>What LaunchBox should tell the user to go and find.
        ///
        /// THE LOCATION IS bios\, AND IT USED TO BE WRONG. This passed an empty location, which means
        /// the emulator's own folder, while the files actually live in a subfolder - so the check
        /// reported every file missing on an installation where everything worked. An empty location
        /// is only right for somebody who drops BIOS files loose beside the executable, which is not
        /// what this plugin sets up.
        ///
        /// REQUIRED MEANS REQUIRED. This also used to declare every file optional while describing
        /// some of them as REQUIRED in the very next sentence. The four a DSiWare title needs are
        /// declared required; the DS ones are genuinely optional, because melonDS has a built-in
        /// BIOS and generates a firmware, and they matter only with external BIOS turned on.</summary>
        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string platform)
            => BiosFiles(platform, null);

        /// <summary>The same question asked with the installation in hand - and THAT CHANGES THE
        /// ANSWER, which is why this no longer forwards to the overload above.
        ///
        /// A DSi NAND dump has no canonical file name. The ones in circulation carry a firmware
        /// version (DSi_Nand_USA_1.4.5.bin), this plugin reads their region out of their contents
        /// rather than their name, and asking anyone to rename one would be inventing a requirement.
        /// But a name is exactly what this contract is made of. With the installation path we can
        /// look: a dump that is there is declared UNDER THE NAME IT HAS, and only the regions with
        /// no dump at all fall back to a suggested name. So the check describes the folder instead
        /// of describing a convention nobody follows.</summary>
        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(
            string emulatorApplicationPath, string platform, string commandLine)
        {
            MelonDsLayout layout = null;
            try
            {
                var exe = ResolveFullPath(emulatorApplicationPath);
                if (!string.IsNullOrWhiteSpace(exe)) layout = MelonDsPaths.Resolve(exe);
            }
            catch { }
            return BiosFiles(platform, layout);
        }

        private static IEnumerable<EmulatorBiosFile> BiosFiles(string platform, MelonDsLayout layout)
        {
            // A DSi platform is the other regime entirely, and saying so here is what puts the answer
            // in front of the user: LaunchBox shows these in its own BIOS check, so "you need a NAND
            // dump" arrives before a game fails rather than afterwards in a log.
            if (IsDsiPlatform(platform))
            {
                // SHORT, because this is a list in a dialog and not a place to explain anything.
                // The reasoning lives in the README and in MelonDsBios; here a name and a role are
                // all anybody needs to go and find a file.
                var files = new List<EmulatorBiosFile>
                {
                    File_(Named(layout, MelonDsBios.DsiBios7), "DSi ARM7 BIOS", required: true),
                    File_(Named(layout, MelonDsBios.DsiBios9), "DSi ARM9 BIOS", required: true),
                    File_(Named(layout, MelonDsBios.DsiFirmware), "DSi firmware", required: true),
                };
                files.AddRange(NandFiles(layout));
                return files;
            }

            if (!IsDsPlatform(platform)) return Array.Empty<EmulatorBiosFile>();

            return new[]
            {
                File_(Named(layout, MelonDsBios.DsBios7), "DS ARM7 BIOS - optional",
                      required: false, md5: "df692a80a5b1bc90728bc3dfc76cd948"),
                File_(Named(layout, MelonDsBios.DsBios9), "DS ARM9 BIOS - optional",
                      required: false, md5: "a392174eb3e572fed6447e956bde4b25"),
                File_(Named(layout, MelonDsBios.DsFirmware), "DS firmware - optional", required: false),
            };
        }

        /// <summary>What to call a file in the list. With no installation in hand there is nothing
        /// to look at, so the plugin's own name stands.</summary>
        private static string Named(MelonDsLayout layout, string ourName)
            => layout == null ? ourName : MelonDsBios.PreferredName(layout, ourName);

        /// <summary>One entry per DSi region, named after the dump that is there when there is one.
        ///
        /// NONE OF THEM IS REQUIRED AND NONE CARRIES A HASH. Only the region of the game being
        /// launched is needed, so demanding six would be a lie; and the dumps vary by firmware
        /// revision and by console, so a hash would reject perfectly good files. What this plugin
        /// checks instead is that a dump decrypts and says which region it came from - which is a
        /// stronger statement than any checksum, because it is about THIS console's files.</summary>
        private static IEnumerable<EmulatorBiosFile> NandFiles(MelonDsLayout layout)
        {
            var have = new Dictionary<DsiRegion, string>();
            try
            {
                if (layout != null)
                {
                    var bios7 = MelonDsBios.Find(layout, MelonDsBios.DsiBios7)
                                ?? AbsoluteTo(layout.ConfigDir, ValueOf(layout, "BIOS7Path"));
                    var declared = MelonDsBios.Dir(layout);
                    foreach (var dump in DsiDumps.Nands(layout, bios7))
                    {
                        // ONLY A DUMP IN THE DECLARED FOLDER IS NAMED HERE. One sitting elsewhere
                        // still works - every search folder is looked in at launch - but naming it
                        // would point the host's own check at a folder that does not hold it.
                        var where = System.IO.Path.GetDirectoryName(dump.Path);
                        if (declared == null || !string.Equals(System.IO.Path.GetFullPath(where),
                                                               declared, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!have.ContainsKey(dump.Region))
                            have[dump.Region] = System.IO.Path.GetFileName(dump.Path);
                    }
                }
            }
            catch { }

            var files = new List<EmulatorBiosFile>();
            foreach (DsiRegion region in Enum.GetValues(typeof(DsiRegion)))
            {
                string name = have.TryGetValue(region, out var actual)
                    ? actual : DsiRegions.SuggestedFileName(region);
                files.Add(File_(name, "DSi NAND, " + DsiRegions.Name(region) + " - any file name",
                                required: false));
            }
            return files;
        }

        /// <summary>Every property on EmulatorBiosFile is read-only, so the constructor is the only
        /// way in. The location is the bios folder this plugin creates beside the executable; melonDS
        /// itself takes absolute paths and this plugin writes them, so the location is what the
        /// user's own check looks at rather than what the emulator reads.</summary>
        private static EmulatorBiosFile File_(string fileName, string description,
                                              bool required, string md5 = null)
            => new EmulatorBiosFile(MelonDsBios.DirName, fileName, required, description, md5, null);

        // ── RetroAchievements ────────────────────────────────────────────────

        /// <summary>Standalone melonDS has no RetroAchievements support of any kind: there is no
        /// rcheevos submodule, no vendored rc_* source, no menu entry and no configuration key
        /// anywhere in the tree. RA support for melonDS exists only in the libretro core, which is a
        /// different project. Answering "not supported" is honest; claiming support and silently doing
        /// nothing would be worse.</summary>
        public override RetroAchievementSupportResponse SupportsRetroAchievements(string emulatorApplicationPath)
            => new RetroAchievementSupportResponse(isSupported: false);

        // ── launch ───────────────────────────────────────────────────────────

        /// <summary>Choose the console mode from the ROM itself, and write it before the launch.
        ///
        /// melonDS has NO command-line option for this - CLI.cpp accepts a ROM, --boot, --fullscreen
        /// and --archive-file, and nothing else, on 1.1 and on master alike. Emu.ConsoleType in
        /// melonDS.toml is the only lever, read at startup (EmuInstance.cpp:72, :638, :1243).
        ///
        /// The ROM says which mode it wants, using melonDS's own predicates (NDS_Header.h:206, :219):
        /// UnitCode &amp; 0x02 means the title is DSi-capable, and a DSiTitleIDHigh of 0x00030004 means it
        /// is DSiWare.
        ///
        ///   plain DS ROM                  DS mode
        ///   DSi cartridge or enhanced     DSi mode, but ONLY when the DSi files are configured and
        ///                                 present - verifySetup refuses otherwise and the user gets a
        ///                                 dialog instead of a game
        ///   DSiWare                       nothing is written, and the log says why: melonDS boots
        ///                                 DSiWare from the NAND, and installing a title there is a
        ///                                 GUI-only operation (TitleManagerDialog)
        ///
        /// THIS CHANGES A GLOBAL EMULATOR SETTING per game, which is unusual enough to say out loud -
        /// the README does. It is written only when the value actually differs, and never while
        /// melonDS is running, because melonDS rewrites the whole file when it exits.</summary>
        public override PrepareForLaunchResponse PrepareEmulatorForLaunch(PrepareForLaunchArgs args)
        {
            // THE ONE THING THAT STOPS A LAUNCH. A dump being set up for the first time has just
            // had melonDS opened on it, and the game must not start on top of that. Everything else
            // here answers true: a plugin that cannot prepare something is not a reason to refuse
            // somebody their game.
            bool go = true;
            try
            {
                var exe = Safe(() => args?.EmulatorBeingLaunched?.ApplicationPath);
                var rom = Safe(() => args?.GameBeingLaunched?.ApplicationPath);
                if (!string.IsNullOrWhiteSpace(exe))
                    go = ChooseConsoleMode(MelonDsPaths.Resolve(ResolveFullPath(exe)), ResolveFullPath(rom));
            }
            catch (Exception ex) { Log.Warn("PrepareEmulatorForLaunch", ex); }

            // The command line is left alone: the host already appends the ROM, and -f is on the
            // emulator entry.
            return new PrepareForLaunchResponse(success: go);
        }

        /// <summary>DS or DSi, decided and written. Never throws.
        ///
        /// Answers whether the launch should go ahead. FALSE happens in exactly one place - a NAND
        /// dump being set up for the first time, which opens melonDS itself and must not be raced by
        /// the game. Every other outcome, including every failure, answers true: not being able to
        /// prepare something is not a reason to refuse somebody their game.</summary>
        internal static bool ChooseConsoleMode(MelonDsLayout layout, string romPath)
        {
            if (layout?.ConfigFile == null) return true;

            var rom = NdsHeader.Describe(romPath);
            if (!rom.Known)
            {
                Log.Verbose("could not read a DS header from " + romPath + "; leaving the console mode alone");
                return true;
            }

            // A DSiWARE LAUNCH CAPTURES ITS OWN PREDECESSOR, inside PrepareDsiWare and only once
            // it knows whether it is about to rebuild: the image it would be walking is the very one
            // it is about to hand straight back, and doing that costs a quarter of a second per
            // launch to write down something already on disk.
            if (rom.IsDSiWare) return PrepareDsiWare(layout, rom, romPath);

            // ANY OTHER LAUNCH CAPTURES UNCONDITIONALLY. Whatever the working NAND still holds
            // belongs to the title that ran last, and there is no "the emulator quit" event to hang
            // this on - so launching a plain cartridge must not silently discard the DSiWare session
            // that came before it. With nothing in flight it costs one File.Exists.
            //
            // AND IT WAITS FIRST, because a cartridge takes the working image over: going ahead while
            // melonDS still has it would discard exactly the session this paragraph exists to keep.
            if (!DsiWorkspace.WaitForTheImage(layout, rom.AssetName)) return false;
            DsiWorkspace.CaptureWork(layout, AbsoluteTo(layout.ConfigDir, ValueOf(layout, "BIOS7Path")));

            // A CONFIGURATION NOBODY CAN PLAY is the one case where this touches an installation it
            // did not make: every DS button unbound means melonDS was never set up, and a launch is
            // about to show a game that answers to nothing. One bound key and this does not fire.
            if (MelonDsInput.NothingIsBound(layout)) MelonDsInput.EnsureDefaults(layout, out _);

            // THE FILES ARE THE PLUGIN'S JOB. Drop a BIOS in the folder and it gets configured;
            // drop nothing and melonDS boots on its built-in one. What nobody should have to do is
            // type three paths into Config > Emu settings to make a game start.
            ConfigureDsFiles(layout, rom);

            int wanted = 0;
            if (rom.IsDSi)
            {
                PointCartridgeAtScratch(layout, rom);
                var missing = MissingDsiFiles(layout);
                if (missing.Count == 0) wanted = 1;
                else
                    Log.Info("\"" + rom.AssetName + "\" is a DSi title but DSi mode needs "
                             + string.Join(", ", missing) + "; starting in DS mode instead");
            }

            // A cartridge boots straight in: it carries its own save and has no business on a menu.
            SetBootMode(layout, wanted, directBoot: true, rom);
            return true;
        }

        /// <summary>Rebuild the working NAND for this title and point melonDS at it.
        ///
        /// A DSiWare title is not a cartridge: melonDS boots it out of the NAND and keeps its save
        /// there too. So the image is rebuilt from the user's base dump, the title is installed into
        /// it, and the few kilobytes that title had accumulated go back in - see DsiWorkspace and
        /// DsiDelta. One image for the whole library instead of one per game.
        ///
        /// WITHOUT THE NATIVE LIBRARY none of that is possible - nothing can be installed, walked or
        /// captured - and a scratch image rebuilt every launch would then be worse than useless,
        /// because it would wipe the manual import that is the only thing left to do. So that case
        /// keeps a NAND of its own per title, which is where a manual import survives.</summary>
        private static bool PrepareDsiWare(MelonDsLayout layout, NdsRom rom, string romPath)
        {
            // WHICH CONSOLE CAN RUN IT. A DSi NAND is region locked, so this decides which dump
            // the image is built on - see DsiRegions for the three sources and their order.
            var regions = DsiRegions.RegionsFor(rom, romPath, out var how);
            if (regions.Count > 0)
                Log.Verbose(rom.AssetName + " runs on " + DsiRegions.Names(regions)
                            + " hardware, according to " + how);

            // The DS side first, and for a DSiWare too: verifySetup demands the DS BIOS whenever
            // external BIOS is on, WHATEVER the console type (EmuInstance.cpp:640-643). A DSi launch
            // with that on and no DS BIOS would be refused for a reason that has nothing to do with
            // DSi, so that switch is settled from what is actually there before anything else.
            ConfigureDsFiles(layout, rom);

            // Then the DSi files, from the user's folder when they are not already configured.
            // Writing them is what lets somebody drop files in and launch.
            var missing = EnsureDsiFiles(layout);

            var bios7 = AbsoluteTo(layout.ConfigDir, ValueOf(layout, "BIOS7Path"));
            var dump = DsiDumps.NandFor(layout, regions, bios7, out var whyNoNand);


            // A base.bin from the previous layout still counts, so an install that worked yesterday
            // keeps working while its owner moves dumps into bios\.
            string source = dump?.Path;
            if (source == null)
            {
                source = DsiWorkspace.LegacyBase(layout);
                if (source != null)
                    Log.Info("no NAND for " + DsiRegions.Names(regions) + " in "
                             + MelonDsBios.Dir(layout) + "; falling back to the old dsi\\base.bin. "
                             + "Put your region dumps in the bios folder to have the right one chosen.");
            }

            if (missing.Count > 0 || source == null)
            {
                ReportMissing(layout, rom, regions, missing, source == null ? whyNoNand : null);
                return true;
            }

            // ── WHICH CONSOLE DOES THIS GAME RUN ON ────────────────────────────────
            //
            // Two questions, and the first one settles most launches on its own.
            //
            // Q0  Does this title's save name a console?  It is a delta against ONE console, so
            //     neither the region nor any file name gets a say - the save is the authority.
            //     Rebuilt from the user's dump when it is not on disk.
            // Q1..Q3  Otherwise: which regions, which dumps, and has one of them a console already?
            //     dsi\<the dump's name> exists, and that is the whole test.
            // Q4  None anywhere: offer to build one.

            var wanted = BaseForSave(layout, rom, bios7, out var noBase);
            if (noBase != null)
            {
                // The console is gone AND its dump with it. Refusing outright would be correct and
                // closed: somebody who lost a dump and just wants to play again has no way out. So
                // the choice is put to them - with the file named, so they can go and find it.
                var instead = FreshStartOn(layout, noBase);
                if (!OfferFreshStart(layout, rom, noBase, instead)) return false;

                DsiWorkspace.ParkState(layout, rom.TitleId, noBase.Identity);
                source = instead;
            }

            if (wanted != null)
            {
                source = wanted;                      // Q0 answered; nothing else has a say
            }
            else if (dump != null)
            {
                var console = DsiBase.ConsoleFor(layout, dump.Path);
                if (console == null)
                {
                    // Q4. THE WINDOW ANSWERS FOR THIS LAUNCH, whichever way it is answered:
                    // setting a console up opens melonDS on the DSi menu, and closing the window
                    // means there is still no console to run on. Only the suppressed-window and
                    // kill-switch cases fall through, and those run on the dump - read and copied,
                    // never written to, since the working image is always a copy.
                    if (MelonDsNandSetup.Run(layout, dump, bios7))
                    {
                        Log.Info(rom.AssetName + " is not started this time; " + MelonDsBios.Dir(layout)
                                 + "\\" + System.IO.Path.GetFileName(dump.Path) + " needs a console "
                                 + "first. Launch it again once there is one.");
                        return false;
                    }
                    console = DsiBase.ConsoleFor(layout, dump.Path);
                }
                else if (!DsiBase.Described(console))
                {
                    // A console whose setup was interrupted between the copy and the recipe. The dump
                    // it came from is right here, so describe it now rather than leave every save it
                    // ever carries without a way home.
                    MelonDsNandSetup.Describe(layout, console, dump.Path, bios7, dump.Region);
                }
                if (console != null) source = console;
            }

            var configured = MelonDsToml.Read(layout.ConfigFile, MelonDsPaths.DSiTable, "NANDPath");
            var current = configured.TryGetValue("NANDPath", out var p)
                ? AbsoluteTo(layout.ConfigDir, p) : null;

            // NOTHING TOUCHES THE IMAGE UNTIL MELONDS HAS LET IT GO. Everything below reads it,
            // captures it or builds over it, and all three are wrong while a session is still in
            // flight. See DsiWorkspace.WaitForTheImage for why a launch waits where a capture does not.
            if (!DsiWorkspace.WaitForTheImage(layout, rom.AssetName))
            {
                Log.Info(rom.AssetName + " is not started this time; melonDS still had the working "
                         + "image. Launch it again once melonDS has closed.");
                return false;
            }

            // THE RECEIPT, BEFORE ANY DECISION IS TAKEN. If this title's save has been written by
            // something other than melonDS since the image was last agreed with it - a RomM sync, a
            // restore from another machine, a file dropped in by hand - the image describes a save
            // that no longer exists. It is thrown away here rather than merely refused below,
            // because a launch that does not reuse goes on to CAPTURE the image first, and that
            // capture would put the old session back over the new save. See DsiWorkSum.
            DsiWorkspace.DropWorkIfSaveMoved(layout, rom.TitleId);

            bool automatic = DsiNand.IsUsable(out var missingLibrary);

            // THE SAME GAME AGAIN, ON THE SAME NAND. The image on disk already holds this title,
            // built from this ROM on this dump, with its state in it - rebuilding would copy 240 MB
            // to arrive back where we are. Every condition is checked rather than assumed.
            bool reused = automatic && DsiWorkspace.CanReuseWork(layout, rom, romPath, source);

            // Now that the answer is known: a rebuild is about to overwrite whatever the image
            // holds, so whoever ran last has to be written down first.
            if (!reused) DsiWorkspace.CaptureWork(layout, bios7);

            var nand = !automatic ? DsiWorkspace.EnsureLegacy(layout, rom, source)
                     : reused     ? DsiWorkspace.ExistingWork(layout)
                                  : DsiWorkspace.Rebuild(layout, rom, source);

            if (nand.Path == null)
            {
                Log.Info(rom.AssetName + " is DSiWare (title " + rom.TitleId
                         + ") but it has no NAND to run from - " + nand.Reason
                         + ". Leaving the console mode alone.");
                return true;
            }

            // NAME THE IMAGE ACTUALLY USED. This used to name dump.Path, which is right only
            // when the console came from the dump chosen by region - on a recovery it named the file
            // that was NOT used, which is the one launch where somebody reads the log.
            if (!reused)
                Log.Info(rom.AssetName + ": built on " + System.IO.Path.GetFileName(source)
                         + (wanted != null ? ", the console its save was made on"
                          : dump != null ? ", the " + DsiRegions.Name(dump.Region) + " console"
                          : ""));

            // Only when it differs, like SetConsoleType. A relaunch of the same title should write
            // nothing at all - and a write attempted while melonDS is still closing would be refused
            // and logged as a warning, which would be noise about a change that was not needed.
            if (!string.Equals(current, nand.Path, StringComparison.OrdinalIgnoreCase))
            {
                var error = MelonDsToml.Write(layout.ConfigFile, MelonDsPaths.DSiTable,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["NANDPath"] = MelonDsToml.Text(nand.Path),
                    }, force: true);
                if (error != null) { Log.Warn("DSi NAND not selected: " + error); return true; }
            }

            // Through the menu, not straight into the cartridge - see SetBootMode. The marker
            // turns that around, and says so in the log so a session can be told from the other.
            bool direct = Log.Marker(DirectBootMarker);
            if (direct)
                Log.Info("the " + DirectBootMarker + " marker is there: booting " + rom.AssetName
                         + " straight from its .nds instead of through the DSi menu. MEASURED TO LOSE "
                         + "SAVES - the game reports its data corrupt and nothing reaches the NAND. "
                         + "Delete the marker to go back to the menu, which works.");
            SetBootMode(layout, consoleType: 1, directBoot: direct, rom);

            if (!automatic)
            {
                Log.Info("ONE MANUAL STEP for " + rom.AssetName + ": in melonDS, open Manage DSi titles "
                         + "and import this .nds into the NAND that is now selected. melonDS boots "
                         + "DSiWare from the NAND, and its saves live there. (" + missingLibrary + ")");
                return true;
            }

            if (reused)
            {
                Log.Verbose(rom.AssetName + ": the working NAND already holds it, reusing it as it is");
                return true;
            }

            // Install, then walk what that produced. The walk is the reference every later comparison
            // is made against, so it has to describe the install AND NOTHING ELSE - taken after the
            // saved state went back in, it would describe the state as part of the install and the
            // next capture would find no difference at all.
            if (!InstallTitle(layout, rom, romPath, nand.Path)) return true;

            // A NAND from the previous layout, if there is one: read its state out and drop the
            // 240 MB image. It needs the reference, so it cannot happen any earlier than this.
            MigrateLegacyNand(layout, rom, bios7);

            DsiWorkspace.RestoreState(layout, rom.TitleId, bios7);

            // Last, once everything above has worked: a marker naming a title whose state never went
            // back in would make the next capture overwrite a good state with a blank one.
            DsiWorkspace.RememberWork(layout, rom.TitleId, romPath, source);
            return true;
        }

        /// <summary>Point melonDS at the DS BIOS and firmware, and turn external BIOS on or off to
        /// match what is actually there.
        ///
        /// SETTING THE PATHS IS THIS PLUGIN'S JOB, not the user's. Dropping a file in a folder is
        /// something anybody can do; going into Config > Emu settings and typing three paths is
        /// something nobody should have to do to make a game start.
        ///
        /// AND THE SWITCH FOLLOWS THE FILES. Emu.ExternalBIOSEnable is what makes melonDS demand all
        /// three (verifySetup, EmuInstance.cpp:640-643), so it goes ON when all three are there -
        /// which gets you your own console's boot animation and settings - and OFF when they are
        /// not, which boots on the built-in BIOS and a generated firmware. Either way the game runs.
        /// Left on without the files, melonDS refuses to start at all.
        ///
        /// A PATH ALREADY SET AND POINTING AT A FILE IS LEFT ALONE. Somebody who configured these by
        /// hand, wherever they like, has answered the question; only what is absent or broken is
        /// filled in from the folder.</summary>
        private static void ConfigureDsFiles(MelonDsLayout layout, NdsRom rom)
        {
            try
            {
                var wanted = new Dictionary<string, string>(StringComparer.Ordinal);
                var keys = MelonDsToml.Read(layout.ConfigFile, MelonDsPaths.DsTable,
                                            "BIOS7Path", "BIOS9Path", "FirmwarePath");

                bool complete = true;
                foreach (var (key, name) in new[]
                {
                    ("BIOS7Path", MelonDsBios.DsBios7),
                    ("BIOS9Path", MelonDsBios.DsBios9),
                    ("FirmwarePath", MelonDsBios.DsFirmware),
                })
                {
                    var set = keys.TryGetValue(key, out var value) ? value : null;
                    if (!string.IsNullOrWhiteSpace(set) && File.Exists(AbsoluteTo(layout.ConfigDir, set)))
                        continue;                                  // already answered, and it is there

                    var path = MelonDsBios.FindChecked(layout, name, out var doubt);
                    if (doubt != null) Log.Info(doubt);
                    if (path != null) wanted[key] = MelonDsToml.Text(path);
                    else complete = false;
                }

                if (wanted.Count > 0)
                {
                    var error = MelonDsToml.Write(layout.ConfigFile, MelonDsPaths.DsTable, wanted, force: true);
                    if (error != null) { Log.Warn("DS files not selected: " + error); return; }
                    Log.Info("pointed melonDS at DS." + string.Join(", DS.", wanted.Keys) + " in "
                             + MelonDsBios.Dir(layout));
                }

                SetExternalBios(layout, complete, rom);
            }
            catch (Exception ex) { Log.Warn("could not set the DS files", ex); }
        }

        /// <summary>Turn Emu.ExternalBIOSEnable on or off, and only when it differs.</summary>
        private static void SetExternalBios(MelonDsLayout layout, bool on, NdsRom rom)
        {
            try
            {
                var emu = MelonDsToml.Read(layout.ConfigFile, MelonDsPaths.EmuTable,
                                           MelonDsPaths.KeyExternalBios);
                var value = emu.TryGetValue(MelonDsPaths.KeyExternalBios, out var v) ? v : null;
                bool already = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                if (already == on) return;

                var error = MelonDsToml.Write(layout.ConfigFile, MelonDsPaths.EmuTable,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [MelonDsPaths.KeyExternalBios] = on ? "true" : "false",
                    }, force: true);
                if (error != null) { Log.Warn("external BIOS not changed: " + error); return; }

                Log.Info(on
                    ? "turned external BIOS on: your own DS BIOS and firmware are all there"
                    : "turned external BIOS off for " + rom.AssetName + ": " + MelonDsBios.DsBios7
                      + ", " + MelonDsBios.DsBios9 + " and " + MelonDsBios.DsFirmware
                      + " are not all in " + MelonDsBios.Dir(layout) + ", so melonDS boots on its "
                      + "built-in BIOS and a generated firmware instead. Put them there to use "
                      + "your own console's.");
            }
            catch (Exception ex) { Log.Warn("could not set external BIOS", ex); }
        }

        /// <summary>Point melonDS at the DSi BIOS and firmware, and answer with what is missing.
        ///
        /// A path already configured and pointing at a file that exists is LEFT ALONE - somebody who
        /// set these up by hand, in whatever folder they like, has answered the question already.
        /// Only what is absent or broken is looked up in the bios folder and written.
        ///
        /// All three are demanded, firmware included. verifySetup only checks the firmware when
        /// Emu.ExternalBIOSEnable is on, which makes it look optional; loadFirmware opens it either
        /// way, because its built-in branch for DSi mode is an empty TODO that falls through
        /// (EmuInstance.cpp:1016-1019). Believing the verify step would have meant declaring a file
        /// unnecessary that melonDS then fails without.</summary>
        private static List<string> EnsureDsiFiles(MelonDsLayout layout)
        {
            var missing = new List<string>();
            var wanted = new Dictionary<string, string>(StringComparer.Ordinal);
            var pairs = new[]
            {
                ("BIOS7Path", MelonDsBios.DsiBios7),
                ("BIOS9Path", MelonDsBios.DsiBios9),
                ("FirmwarePath", MelonDsBios.DsiFirmware),
            };

            try
            {
                var keys = MelonDsToml.Read(layout.ConfigFile, MelonDsPaths.DSiTable,
                                            "BIOS7Path", "BIOS9Path", "FirmwarePath");
                foreach (var (key, name) in pairs)
                {
                    var set = keys.TryGetValue(key, out var value) ? value : null;
                    if (!string.IsNullOrWhiteSpace(set)
                        && File.Exists(AbsoluteTo(layout.ConfigDir, set))) continue;

                    var found = MelonDsBios.FindChecked(layout, name, out var doubt);
                    if (doubt != null) Log.Info(doubt);
                    if (found == null) { missing.Add(name); continue; }
                    wanted[key] = MelonDsToml.Text(found);
                }

                if (wanted.Count > 0)
                {
                    var error = MelonDsToml.Write(layout.ConfigFile, MelonDsPaths.DSiTable, wanted, force: true);
                    if (error != null) Log.Warn("DSi files not selected: " + error);
                    else Log.Info("pointed melonDS at " + string.Join(", ", wanted.Keys)
                                  + " in " + MelonDsBios.Dir(layout));
                }
            }
            catch (Exception ex) { Log.Warn("could not check the DSi files", ex); }
            return missing;
        }

        /// <summary>Say, in the log AND on screen, that a DSiWare title cannot run and what is
        /// missing. See DsiDialog for why this plugin brings its own windows at all.</summary>
        private static void ReportMissing(MelonDsLayout layout, NdsRom rom, List<DsiRegion> regions,
                                          List<string> missing, string whyNoNand)
        {
            // Make the folder before naming it. The message below, and the window after it, both
            // point at a path - and a path that does not exist is a spelling to guess at.
            MelonDsBios.Prepare(layout);

            var wanted = whyNoNand == null ? null : DsiRegions.Names(regions);
            var parts = new List<string>();
            if (missing.Count > 0) parts.Add(string.Join(", ", missing));
            if (wanted != null) parts.Add("a NAND dump for " + wanted);

            Log.Info(rom.AssetName + " is DSiWare and cannot start: it needs " + string.Join(" and ", parts)
                     + " in " + MelonDsBios.Dir(layout)
                     + (whyNoNand == null ? "" : " - " + whyNoNand)
                     + ". Leaving the console mode alone.");

            DsiDialog.MissingFiles(rom.AssetName, MelonDsBios.Dir(layout), missing, wanted,
                regions.Count == 0
                    ? "Nothing in this ROM or its file name says which region it is for, so any NAND "
                      + "you have will be tried."
                    : null);
        }

        /// <summary>Which image this title's save should actually be rebuilt on.
        ///
        /// Answers <paramref name="chosen"/> unchanged in the ordinary case, the path of a rebuilt
        /// base when the save was made on a different console, or null when that console cannot be
        /// reconstructed - in which case the launch is refused rather than started on the wrong base.
        ///
        /// NO RECORD MEANS NO OPINION. A save made before any of this existed carries nothing, and
        /// guessing that the current base is wrong would break something that works. The same rule
        /// work.sum follows.</summary>
        private static string BaseForSave(MelonDsLayout layout, NdsRom rom, string bios7,
                                          out BaseRecord lost)
        {
            lost = null;
            try
            {
                var save = DsiWorkspace.SavePathFor(layout, rom.TitleId);
                if (save == null || !File.Exists(save)) return null;

                // READ OUT OF THE SAVE FILE, IN MEMORY. This runs on every DSiWare launch, and what
                // it costs is one 400-byte entry and one 30 KB entry out of a 70 KB archive - a zip
                // is random-access through its central directory, so neither the rest of the save
                // nor the disk is touched. Nothing is unpacked unless the console turns out to be
                // missing, which is the rare path.
                var record = DsiBase.ReadRecord(
                    DsiSaveFile.Bytes(save, DsiBase.RecordInState));
                if (record == null) return null;            // no record, no opinion

                var recipe = DsiSaveFile.Bytes(save, DsiBase.RecipeInState);

                // Already on disk, under the name it was built from or as a rebuild: use it.
                var here = ConsoleWithIdentity(layout, record.Identity, bios7, recipe);
                if (here != null) return here;

                Log.Info(rom.AssetName + "'s save was made on console "
                         + DsiBase.Short(record.Identity) + ", which is not here. Looking for the "
                         + "dump it was built from.");

                var original = DsiBase.FindOriginal(Dumps(layout), record);
                if (original == null) { lost = record; return null; }

                // THE ONE PLACE A SAVE IS WRITTEN OUT TO READ IT. Rebuilding lays the recipe down as
                // a folder to apply it, so it wants a file rather than bytes. This happens when a
                // console has gone missing, not on an ordinary launch.
                var onDisk = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                                    "lbip-recipe-" + Guid.NewGuid().ToString("N") + ".zip");
                try
                {
                    if (!DsiSaveFile.Extract(save, DsiBase.RecipeInState, onDisk, out var why))
                    {
                        Log.Warn("this save carries no recipe, so its console cannot be rebuilt - " + why);
                        lost = record;
                        return null;
                    }

                    var archive = DsiBase.ArchiveFor(layout, record.Identity);
                    if (DsiBase.RebuildFrom(original, onDisk, archive, bios7, record.Identity,
                                                record.Region, out var error))
                        return archive;

                    Log.Warn("could not rebuild the console this save belongs to - " + error);
                    lost = record;
                    return null;
                }
                finally { try { File.Delete(onDisk); } catch { } }
            }
            catch (Exception ex)
            {
                Log.Warn("could not work out which console this save belongs to", ex);
                return null;                                // never fail a launch over this
            }
        }

        /// <summary>An image on disk whose identity is the one asked for: a rebuild kept in bases\,
        /// or a console named after its dump. Null when none of them is.</summary>
        private static string ConsoleWithIdentity(MelonDsLayout layout, string identity, string bios7,
                                                  byte[] recipe)
        {
            try
            {
                var archive = DsiBase.ArchiveFor(layout, identity);
                if (archive != null && File.Exists(archive))
                {
                    Log.Verbose("reusing the rebuilt console " + DsiBase.Short(identity));
                    return archive;
                }

                var paths = DsiBase.PathsIn(recipe);
                if (paths.Count == 0) return null;

                var dir = DsiWorkspace.DsiDir(layout);
                if (dir == null || !Directory.Exists(dir)) return null;

                foreach (var image in Directory.GetFiles(dir))
                {
                    if (!File.Exists(DsiBase.RecordFor(image))) continue;
                    var have = DsiBase.CachedIdentity(layout, image, bios7, paths);
                    if (string.Equals(have, identity, StringComparison.Ordinal)) return image;
                }
                return null;
            }
            catch (Exception ex) { Log.Verbose("could not look for a console - " + ex.Message); return null; }
        }

        /// <summary>Every file that could be the pristine dump: whatever the NAND sweep already
        /// found, plus the locks beside them - a lock IS a pristine dump, and on the machine the
        /// save came from it is the likeliest match of all.</summary>
        private static IEnumerable<string> Dumps(MelonDsLayout layout)
        {
            var seen = new List<string>();
            try
            {
                foreach (var dir in MelonDsBios.SearchFolders(layout))
                {
                    if (dir == null || !Directory.Exists(dir)) continue;
                    foreach (var path in Directory.EnumerateFiles(dir)) seen.Add(path);
                }
            }
            catch (Exception ex) { Log.Verbose("could not list the dumps - " + ex.Message); }
            return seen;
        }

        /// <summary>A console of the same region to start a fresh game on, or null when there is
        /// not even that. Same region because a DSi menu refuses a title from another one - offering
        /// a console that cannot launch the game would be no offer at all.</summary>
        private static string FreshStartOn(MelonDsLayout layout, BaseRecord lost)
        {
            try
            {
                var dir = DsiWorkspace.DsiDir(layout);
                if (dir == null || !Directory.Exists(dir) || lost?.Region == null) return null;

                foreach (var image in Directory.GetFiles(dir))
                {
                    var record = DsiBase.ReadRecord(DsiBase.RecordFor(image));
                    if (record == null) continue;
                    if (string.Equals(record.Region, lost.Region, StringComparison.OrdinalIgnoreCase))
                        return image;
                }
                return null;
            }
            catch (Exception ex) { Log.Verbose("could not look for a substitute console - " + ex.Message); return null; }
        }

        /// <summary>Put the choice to the user. Answers true when they chose to start again on
        /// <paramref name="instead"/>, false when the launch should be abandoned.</summary>
        private static bool OfferFreshStart(MelonDsLayout layout, NdsRom rom, BaseRecord lost,
                                            string instead)
        {
            ReportLostBase(layout, rom, lost);
            if (instead == null) return false;

            var lines = new List<string>
            {
                (rom.AssetName ?? "This game") + " has a save, but the console it was made on is",
                "gone, and so is the dump it was built from.",
                "",
                "    wanted: " + (lost?.OriginalName ?? "(unknown)"),
                "    " + (lost?.OriginalSize ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " bytes, sha256 " + DsiBase.Short(lost?.OriginalSha256),
                "",
                "Put that file back in your BIOS folder and the save returns by itself - it is",
                "found by its contents, so its name does not matter.",
                "",
                "Or start a new game on " + System.IO.Path.GetFileName(instead) + ", which is a",
                "console of the same region. YOUR OLD SAVE IS NOT DELETED: it is set aside, and",
                "it comes back the day that dump does.",
            };

            var answer = DsiDialog.Ask("melonDS - this save's console is missing",
                                           string.Join(Environment.NewLine, lines),
                                           new[] { "Don't start - I'll find the file",
                                                   "Start a new game" });
            if (answer != 1) return false;

            Log.Info(rom.AssetName + ": starting fresh on " + System.IO.Path.GetFileName(instead)
                     + " at the user's request; the old save is set aside, not deleted");
            return true;
        }

        /// <summary>Say, in the log AND on screen, that a save cannot be applied because the console
        /// it was made on is missing - and name exactly the file to go and find.</summary>
        private static void ReportLostBase(MelonDsLayout layout, NdsRom rom, BaseRecord wanted)
        {
            var folder = MelonDsBios.Dir(layout);
            MelonDsBios.Prepare(layout);

            Log.Warn(rom.AssetName + " cannot start: its save was made on a console built from "
                     + (wanted?.OriginalName ?? "a dump") + " (" + (wanted?.OriginalSize ?? 0)
                     + " bytes, sha256 " + DsiBase.Short(wanted?.OriginalSha256)
                     + "), and that file is not in " + folder + ". Refusing rather than starting a "
                     + "fresh game over an existing save.");

            var lines = new List<string>
            {
                (rom.AssetName ?? "This game") + " has a save, but the console it was made on is gone.",
                "",
                "A DSiWare save is stored as the difference between your console and a fresh",
                "install of the game, so it can only be applied to the console it came from.",
                "That console can be rebuilt - the save carries the recipe - but it needs the",
                "original dump it was built from:",
                "",
                "    " + (wanted?.OriginalName ?? "(unknown)"),
                "    " + (wanted?.OriginalSize ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " bytes",
                "    sha256 " + (wanted?.OriginalSha256 ?? "(unknown)"),
                "",
                "Put that file in:",
                "    " + folder,
                "",
                "The name does not matter - it is found by its contents. What does matter is that",
                "it is that exact file: a DSi NAND is encrypted with its own console's id, so a",
                "dump from another console cannot stand in for it.",
                "",
                "The game was not started, because starting it would begin a new game on top of",
                "the save you already have.",
            };

            DsiDialog.Ask("melonDS - this save's console is missing",
                              string.Join(Environment.NewLine, lines),
                              new[] { "Close" });
        }

        /// <summary>A NAND left over from the first design, turned into a saved state and removed.
        /// Nothing is deleted until its state has been written down.</summary>
        private static void MigrateLegacyNand(MelonDsLayout layout, NdsRom rom, string bios7)
        {
            if (DsiWorkspace.LegacyNandFor(layout, rom.TitleId) == null) return;
            Log.Info(rom.AssetName + " still has a NAND of its own from the previous layout; its state "
                     + "is being read out so the image can go.");
            DsiWorkspace.Migrate(layout, rom.TitleId, bios7);
        }

        /// <summary>Beside the log, like the other switches here. Present, a DSiWare title is
        /// direct-booted from its .nds rather than started from the DSi menu.
        ///
        /// IT HAS BEEN RUN, AND DIRECT BOOTING LOST THE SAVE - see SetBootMode for the numbers.
        /// It is kept anyway: the question was worth reopening once, the answer came from one launch,
        /// and a future melonDS may well change it. Nothing here depends on it being absent.</summary>
        private const string DirectBootMarker = "dsi-direct-boot";

        /// <summary>Put the title into the working NAND and write down the walk of the result.
        /// False when the title is not in there afterwards, which means the launch goes ahead but
        /// nothing downstream - migration, state, marker - has any footing.</summary>
        private static bool InstallTitle(MelonDsLayout layout, NdsRom rom, string romPath, string nandPath)
        {
            // AN ARCHIVE IS UNPACKED HERE, not refused. melonDS opens archives itself, so a DS game
            // is handed straight over and none of this applies - but a DSiWare title has to be
            // INSTALLED into a NAND first, and both melonDS's importer and ours read a .nds from
            // disk. Refusing meant preparing a NAND, installing nothing into it, and leaving the DSi
            // menu showing no game: a dead end whose only explanation was a line in a log.
            string unpacked = null;
            string appPath = romPath;
            try
            {
                if (NdsHeader.IsArchive(romPath))
                {
                    unpacked = Path.Combine(Path.GetTempPath(),
                                            "lbip-melonds-" + Guid.NewGuid().ToString("N") + ".nds");
                    if (!Archives.ExtractFirstEntry(romPath, NdsHeader.RomExtensions, unpacked, out var inner))
                    {
                        Log.Warn("could not find a .nds inside " + rom.AssetName
                                 + "; it cannot be installed into a NAND.");
                        return false;
                    }
                    appPath = unpacked;
                    Log.Verbose("unpacked " + inner + " out of the archive to install it");
                }

                return InstallFrom(layout, rom, appPath, romPath, nandPath);
            }
            finally
            {
                try { if (unpacked != null && File.Exists(unpacked)) File.Delete(unpacked); } catch { }
            }
        }

        /// <summary>The install itself, given a .nds ON DISK. <paramref name="besidePath"/> is what
        /// the user actually launched - the archive, when there was one - so a .tmd they put next to
        /// it is still found.</summary>
        private static bool InstallFrom(MelonDsLayout layout, NdsRom rom, string appPath,
                                        string besidePath, string nandPath)
        {

            var bios7 = AbsoluteTo(layout.ConfigDir, ValueOf(layout, "BIOS7Path"));
            using var session = DsiNand.Open(nandPath, bios7, out var error);
            if (session == null)
            {
                Log.Warn("could not open the NAND of " + rom.AssetName + " - " + error);
                return false;
            }

            // THE REAL METADATA FIRST. A TMD built from the ROM carries everything melonDS reads,
            // but it is unsigned - and the DSi menu that launches an installed title checks. Measured
            // on a live install: a title imported with a built TMD ran when direct-booted as a
            // cartridge and failed from the menu. Nintendo's update server still answers, which is
            // where melonDS's own dialog gets it. See DsiNus.
            var tmd = DsiTmd.Resolve(layout, rom, appPath, besidePath, out var source);
            if (tmd != null) Log.Verbose("metadata for " + rom.TitleId + " from " + source);

            if (!session.ImportTitle(appPath, tmd, out var failure, out var generated))
            {
                Log.Warn("could not install " + rom.AssetName + " into the working NAND - " + failure
                         + ". Import it through Manage DSi titles instead.");
                return false;
            }
            Log.Verbose(rom.AssetName + ": title " + rom.TitleId + " installed into the working NAND"
                        + (generated ? " (its metadata was BUILT from the ROM, not signed - if the DSi "
                                     + "menu refuses it, that is why)" : ""));

            return DsiWorkspace.TakeReference(layout, session, rom.TitleId);
        }

        /// <summary>Put a DSi CARTRIDGE on the scratch image, never on a console.
        ///
        /// A cartridge does not live in a NAND the way DSiWare does - it carries its own save and
        /// only needs a console for the settings and the firmware. But it WRITES those settings,
        /// and since this folder started holding configured consoles, "whatever NAND is currently
        /// selected" can be one: a cartridge session on it would change its identity and orphan
        /// every DSiWare save made on it. So the cartridge is handed a copy instead.
        ///
        /// Only a path of OURS is moved - anything the user chose himself is his answer.</summary>
        private static void PointCartridgeAtScratch(MelonDsLayout layout, NdsRom rom)
        {
            try
            {
                var current = AbsoluteTo(layout.ConfigDir, ValueOf(layout, "NANDPath"));
                if (!DsiWorkspace.IsOurs(layout, current)) return;

                var work = DsiWorkspace.HandToCartridge(layout, current, out var why);
                if (work == null)
                {
                    Log.Verbose("left " + rom.AssetName + " on the NAND it had - " + why);
                    return;
                }
                if (string.Equals(current, work, StringComparison.OrdinalIgnoreCase)) return;

                var error = MelonDsToml.Write(layout.ConfigFile, MelonDsPaths.DSiTable,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["NANDPath"] = MelonDsToml.Text(work),
                    }, force: true);
                if (error != null) { Log.Warn("the cartridge's NAND was not set: " + error); return; }
                Log.Info(rom.AssetName + " is a cartridge, not DSiWare - it runs on the working "
                         + "image, so the console it was copied from is left exactly as it is");
            }
            catch (Exception ex) { Log.Warn("could not give the cartridge a NAND of its own", ex); }
        }

        /// <summary>One key of the [DSi] table, as the configuration spells it.</summary>
        private static string ValueOf(MelonDsLayout layout, string key)
        {
            var map = MelonDsToml.Read(layout.ConfigFile, MelonDsPaths.DSiTable, key);
            return map.TryGetValue(key, out var v) ? v : null;
        }

        /// <summary>Write Emu.ConsoleType and Emu.DirectBoot together, and only what differs.
        ///
        /// DIRECTBOOT IS WHAT DECIDES BETWEEN A GAME AND A MENU. With it true - melonDS's default -
        /// EmuInstance calls SetupDirectBoot and the ROM starts immediately, which is what anybody
        /// wants from a frontend. With it false the console boots through its firmware instead, and
        /// in DSi mode that firmware is the DSi menu held in the NAND (EmuInstance.cpp:1469-1473,
        /// :1951-1954).
        ///
        /// A DSiWARE TITLE IS STARTED FROM THE MENU, and that is settled. Direct-booted from its
        /// .nds it runs as a cartridge and its save never reaches the title's data folder. Measured
        /// twice: once on a title installed with a fabricated TMD, which left the result open because
        /// that TMD turned out to be broken in its own right, and then again on a title installed
        /// from real signed metadata. The second run is the one that decides it:
        ///
        ///   nand.bin     changed         - melonDS did write to the image
        ///   public.sav   byte-identical  - nothing of the game's reached its save
        ///
        /// and the game said so itself, on screen: "data was corrupted and has been deleted". Even
        /// that deletion did not land. The extraction ran afterwards and returned the same bytes, so
        /// the failure is upstream of it - there was nothing new to extract.
        ///
        /// Two separate bugs produced the same message on screen, which is what made this take two
        /// passes. The dsi-direct-boot marker is kept for anyone who wants to re-run it.</summary>
        private static void SetBootMode(MelonDsLayout layout, int consoleType, bool directBoot, NdsRom rom)
        {
            var current = MelonDsToml.Read(layout.ConfigFile, MelonDsPaths.EmuTable,
                                           "ConsoleType", "DirectBoot");
            int haveType = MelonDsToml.AsInt(current.TryGetValue("ConsoleType", out var v) ? v : null, 0);
            bool haveBoot = !string.Equals(current.TryGetValue("DirectBoot", out var b) ? b : "true",
                                           "false", StringComparison.OrdinalIgnoreCase);

            var wanted = new Dictionary<string, string>(StringComparer.Ordinal);
            if (haveType != consoleType)
                wanted["ConsoleType"] = consoleType.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (haveBoot != directBoot)
                wanted["DirectBoot"] = directBoot ? "true" : "false";
            if (wanted.Count == 0) return;

            var error = MelonDsToml.Write(layout.ConfigFile, MelonDsPaths.EmuTable, wanted, force: true);
            if (error != null) { Log.Warn("boot mode not changed: " + error); return; }

            Log.Info((consoleType == 1 ? "DSi" : "DS") + " mode, "
                     + (directBoot ? "booting the game directly" : "booting the DSi menu")
                     + ", for " + rom.AssetName);
        }

        /// <summary>Which of the files DSi mode needs are not configured or not there.
        ///
        /// This is the CARTRIDGE path's question - a DSi cart or a DSi-enhanced game, which runs off
        /// whatever NAND is selected and needs no title installed. DSiWare asks a different and
        /// harder question, by region, in PrepareDsiWare.</summary>
        private static List<string> MissingDsiFiles(MelonDsLayout layout)
        {
            var missing = new List<string>();
            try
            {
                var keys = MelonDsToml.Read(layout.ConfigFile, MelonDsPaths.DSiTable,
                                            "BIOS7Path", "BIOS9Path", "NANDPath");
                foreach (var key in new[] { "BIOS7Path", "BIOS9Path", "NANDPath" })
                {
                    var path = keys.TryGetValue(key, out var p) ? p : null;
                    if (string.IsNullOrWhiteSpace(path)) { missing.Add("DSi." + key); continue; }
                    if (!File.Exists(AbsoluteTo(layout.ConfigDir, path))) missing.Add(path);
                }
            }
            catch (Exception ex) { Log.Warn("could not check the DSi files", ex); }
            return missing;
        }

        /// <summary>melonDS resolves a relative configured path against its own directory
        /// (Platform.cpp:157-174, GetLocalFilePath), so do the same before asking whether a file is
        /// there.</summary>
        private static string AbsoluteTo(string baseDir, string path)
        {
            try
            {
                return Path.IsPathRooted(path) || string.IsNullOrEmpty(baseDir)
                    ? path
                    : Path.GetFullPath(Path.Combine(baseDir, path));
            }
            catch { return path; }
        }

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
                if (!string.IsNullOrEmpty(root)
                    && full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
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
                return Path.IsPathRooted(maybeRelative)
                    ? maybeRelative
                    : Path.GetFullPath(Path.Combine(LaunchBoxRoot(), maybeRelative));
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
