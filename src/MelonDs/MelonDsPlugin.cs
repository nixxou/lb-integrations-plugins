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

namespace LbIntegrations.MelonDs
{
    public partial class MelonDsPlugin : EmulatorPlugin, ISystemEventsPlugin
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

            // As early as possible: the patch only sees connections opened AFTER it is installed, and
            // LaunchBox reads its metadata the moment a window asks for it.
            LbipRowInjection.Install("com.nixxou.lbip.melonds", MetadataRows());
        }

        public override string EmulatorName => "melonDS";

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
        private static IEnumerable<LbipEmulatorRow> MetadataRows()
        {
            const string extensions = ".nds; .srl; .dsi; .ids; .zip; .7z; .rar";

            yield return new LbipEmulatorRow
            {
                Name = "melonDS",
                CommandLine = DefaultCommandLine,
                ApplicableFileExtensions = extensions,
                Url = "https://melonds.kuribo64.net/",
                BinaryFileName = MelonDsPaths.ExecutableName,
                AutoExtract = false,
                Platforms =
                {
                    new LbipPlatformRow
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
                MelonDsDsi.PrepareFolder(layout);

                if (wanted.Count == 0)
                {
                    Log.Info("save paths already configured; leaving them alone");
                    return;
                }

                try { Directory.CreateDirectory(layout.DefaultSaveDir); } catch { }
                try { Directory.CreateDirectory(layout.DefaultStateDir); } catch { }
                MelonDsDsi.PrepareFolder(layout);

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
            emu.Title = "melonDS";
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
        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string platform)
        {
            // A DSi platform is the other regime entirely, and saying so here is what puts the answer
            // in front of the user: LaunchBox shows these in its own BIOS check, so "you need a NAND
            // dump" arrives before a game fails rather than afterwards in a log.
            if (IsDsiPlatform(platform))
                return new[]
                {
                    File_("dsi_bios7.bin", "DSi ARM7 BIOS - REQUIRED for DSi mode. melonDS has no "
                                         + "built-in replacement for it, unlike the DS ones.", md5: null),
                    File_("dsi_bios9.bin", "DSi ARM9 BIOS - REQUIRED for DSi mode.", md5: null),
                    File_("dsi_nand.bin", "A dump of YOUR OWN console's NAND - REQUIRED. DSiWare runs "
                                        + "from inside it and keeps its save there, so it cannot be "
                                        + "generated or downloaded. Put it at Emulators\\melonDS\\dsi\\"
                                        + "base.bin and this plugin gives each title a copy.",
                          md5: null),
                    File_("dsi_firmware.bin", "DSi firmware - optional; only read when external BIOS "
                                            + "is turned on.", md5: null),
                };

            if (!IsDsPlatform(platform)) return Array.Empty<EmulatorBiosFile>();

            return new[]
            {
                File_("bios7.bin", "DS ARM7 BIOS - optional; melonDS has a built-in replacement. Point "
                                 + "DS.BIOS7Path at it and turn on Config > Emu settings > external BIOS.",
                      md5: "df692a80a5b1bc90728bc3dfc76cd948"),
                File_("bios9.bin", "DS ARM9 BIOS - optional, same as bios7.bin.",
                      md5: "a392174eb3e572fed6447e956bde4b25"),
                File_("firmware.bin", "DS firmware - optional; without it melonDS generates one, which "
                                    + "boots straight to the game and carries no user settings.",
                      md5: null),
            };
        }

        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(
            string emulatorApplicationPath, string platform, string commandLine)
            => GetBiosFilesForPlatform(platform);

        /// <summary>Every property on EmulatorBiosFile is read-only, so the constructor is the only way
        /// in. The location is the emulator's own folder: melonDS takes absolute paths, and resolves a
        /// relative one against its directory (Platform.cpp:157-174), so that is where a dropped file
        /// can be pointed at with the least ceremony.</summary>
        private static EmulatorBiosFile File_(string fileName, string description, string md5)
            => new EmulatorBiosFile("", fileName, false, description, md5, null);

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
            try
            {
                var exe = Safe(() => args?.EmulatorBeingLaunched?.ApplicationPath);
                var rom = Safe(() => args?.GameBeingLaunched?.ApplicationPath);
                if (!string.IsNullOrWhiteSpace(exe))
                    ChooseConsoleMode(MelonDsPaths.Resolve(ResolveFullPath(exe)), ResolveFullPath(rom));
            }
            catch (Exception ex) { Log.Warn("PrepareEmulatorForLaunch", ex); }

            // The command line is left alone: the host already appends the ROM, and -f is on the
            // emulator entry.
            return new PrepareForLaunchResponse(success: true);
        }

        /// <summary>DS or DSi, decided and written. Never throws, and never fails a launch.</summary>
        internal static void ChooseConsoleMode(MelonDsLayout layout, string romPath)
        {
            if (layout?.ConfigFile == null) return;

            var rom = NdsHeader.Describe(romPath);
            if (!rom.Known)
            {
                Log.Verbose("could not read a DS header from " + romPath + "; leaving the console mode alone");
                return;
            }

            if (rom.IsDSiWare) { PrepareDsiWare(layout, rom, romPath); return; }

            int wanted = 0;
            if (rom.IsDSi)
            {
                RestoreBaseNand(layout, rom);
                var missing = MissingDsiFiles(layout);
                if (missing.Count == 0) wanted = 1;
                else
                    Log.Info("\"" + rom.AssetName + "\" is a DSi title but DSi mode needs "
                             + string.Join(", ", missing) + "; starting in DS mode instead");
            }

            // A cartridge boots straight in: it carries its own save and has no business on a menu.
            SetBootMode(layout, wanted, directBoot: true, rom);
        }

        /// <summary>Give a DSiWare title its own NAND and point melonDS at it.
        ///
        /// A DSiWare title is not a cartridge: melonDS boots it out of the NAND and keeps its saves
        /// there too, so the NAND is the game's container rather than a side file. One per title makes
        /// that container self-standing - see MelonDsDsi.
        ///
        /// What this does NOT do is install the title into it. That lives in melonDS's Manage DSi
        /// titles dialog and has no entry point outside it, so the first launch of a title prepares
        /// its NAND and says, once, that the import is owed. Every launch after that is automatic.</summary>
        private static void PrepareDsiWare(MelonDsLayout layout, NdsRom rom, string romPath)
        {
            var configured = MelonDsToml.Read(layout.ConfigFile, MelonDsPaths.DSiTable, "NANDPath");
            var current = configured.TryGetValue("NANDPath", out var p)
                ? AbsoluteTo(layout.ConfigDir, p) : null;
            var nand = MelonDsDsi.Ensure(layout, rom, current);

            if (nand.Path == null)
            {
                Log.Info(rom.AssetName + " is DSiWare (title " + rom.TitleId
                         + ") but it has no NAND to run from - " + nand.Reason
                         + ". Leaving the console mode alone.");
                return;
            }

            // DSi mode still needs the two BIOS: verifySetup checks them whatever else is configured.
            var missing = MissingDsiFiles(layout, nandPathIsOurs: true);
            if (missing.Count > 0)
            {
                Log.Info(rom.AssetName + " has its NAND but DSi mode also needs "
                         + string.Join(", ", missing) + "; leaving the console mode alone.");
                return;
            }

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
                if (error != null) { Log.Warn("DSi NAND not selected: " + error); return; }
            }

            // Through the menu, not straight into the cartridge - see SetBootMode.
            SetBootMode(layout, consoleType: 1, directBoot: false, rom);

            // The title has to be INSIDE that NAND for melonDS to boot it, and that is melonDS's own
            // code doing it - see MelonDsNand. Without the library the step is a click in Manage DSi
            // titles, and either way the launch goes ahead.
            InstallTitle(layout, rom, romPath, nand.Path);
        }

        /// <summary>Put the title inside its NAND, if it is not there already.</summary>
        private static void InstallTitle(MelonDsLayout layout, NdsRom rom, string romPath, string nandPath)
        {
            if (!MelonDsNand.IsUsable(out var why))
            {
                Log.Info("ONE MANUAL STEP for " + rom.AssetName + ": in melonDS, open Manage DSi titles "
                         + "and import this .nds into the NAND that is now selected. melonDS boots "
                         + "DSiWare from the NAND, and its saves live there. (" + why + ")");
                return;
            }

            // An archive cannot be imported: melonDS's importer reads a .nds, and so does ours.
            if (NdsHeader.IsArchive(romPath))
            {
                Log.Info(rom.AssetName + " is inside an archive; extract the .nds to have it installed "
                         + "into its NAND automatically.");
                return;
            }

            var bios7 = AbsoluteTo(layout.ConfigDir, ValueOf(layout, "BIOS7Path"));
            using var session = MelonDsNand.Open(nandPath, bios7, out var error);
            if (session == null)
            {
                Log.Warn("could not open the NAND of " + rom.AssetName + " - " + error);
                return;
            }

            if (session.TitleExists(rom.TitleId)) return;       // nothing to do, and nothing to say

            // THE REAL METADATA FIRST. A TMD built from the ROM carries everything melonDS reads,
            // but it is unsigned - and the DSi menu that launches an installed title checks. Measured
            // on a live install: a title imported with a built TMD ran when direct-booted as a
            // cartridge and failed from the menu. Nintendo's update server still answers, which is
            // where melonDS's own dialog gets it. See MelonDsNus.
            var tmd = MelonDsTmd.Resolve(layout, rom, romPath, out var source);
            if (tmd != null) Log.Verbose("metadata for " + rom.TitleId + " from " + source);

            if (session.ImportTitle(romPath, tmd, out var failure, out var generated))
                Log.Info(rom.AssetName + ": title " + rom.TitleId + " installed into its NAND"
                         + (generated ? " (its metadata was BUILT from the ROM, not signed - if the DSi "
                                      + "menu refuses it, that is why)" : ""));
            else
                Log.Warn("could not install " + rom.AssetName + " into its NAND - " + failure
                         + ". Import it through Manage DSi titles instead.");
        }

        /// <summary>Point a DSi CARTRIDGE back at the base NAND.
        ///
        /// The per-title NANDs exist for DSiWare, which lives inside them. A cartridge does not: it
        /// only needs a NAND for the system settings and firmware, and running it against the copy
        /// belonging to whichever DSiWare was launched last would write those settings into that
        /// copy instead of into the user's own dump.
        ///
        /// Only a path of OURS is moved - anything the user chose is his answer.</summary>
        private static void RestoreBaseNand(MelonDsLayout layout, NdsRom rom)
        {
            try
            {
                var current = AbsoluteTo(layout.ConfigDir, ValueOf(layout, "NANDPath"));
                if (!MelonDsDsi.IsOurs(layout, current)) return;

                var basePath = MelonDsDsi.BasePath(layout);
                if (basePath == null || !File.Exists(basePath)) return;
                if (string.Equals(current, basePath, StringComparison.OrdinalIgnoreCase)) return;

                var error = MelonDsToml.Write(layout.ConfigFile, MelonDsPaths.DSiTable,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["NANDPath"] = MelonDsToml.Text(basePath),
                    }, force: true);
                if (error != null) { Log.Warn("base NAND not restored: " + error); return; }
                Log.Info(rom.AssetName + " is a cartridge, not DSiWare - back to the base NAND");
            }
            catch (Exception ex) { Log.Warn("could not restore the base NAND", ex); }
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
        /// A DSiWARE TITLE NEEDS THE MENU, and that was measured the hard way: direct-booted from its
        /// .nds it runs as a cartridge, and its save never reaches the NAND - public.sav came back
        /// byte for byte unchanged after a session. DSiWare lives in the NAND and has to be started
        /// from there, which costs one click on the menu and nothing else.</summary>
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

        /// <summary>Which of the three files DSi mode needs are not there. verifySetup checks the two
        /// BIOS and the NAND whatever ExternalBIOSEnable says; the firmware is only checked when it is
        /// on, so it is not demanded here.</summary>
        private static List<string> MissingDsiFiles(MelonDsLayout layout, bool nandPathIsOurs = false)
        {
            var missing = new List<string>();
            try
            {
                var keys = MelonDsToml.Read(layout.ConfigFile, MelonDsPaths.DSiTable,
                                            "BIOS7Path", "BIOS9Path", "NANDPath");
                // The NAND is not asked for when we have just prepared one ourselves.
                var wanted = nandPathIsOurs
                    ? new[] { "BIOS7Path", "BIOS9Path" }
                    : new[] { "BIOS7Path", "BIOS9Path", "NANDPath" };
                foreach (var key in wanted)
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
