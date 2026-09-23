// no$gba integration for LaunchBox / LiteBox.
//
// Claim the emulator, install and update it, describe it to a host that has never heard of it, and
// make its saves readable by everything else. Save management lives next door in NoGbaSaves.cs.
//
// WHY THIS PLUGIN EXISTS, in one line: no$gba writes its cartridge saves COMPRESSED, in a container
// only it can read, and it does so by default. One line in NO$GBA.INI makes them plain battery
// images. Measured on Mario Kart DS - 25,487 bytes and "NocashGbaBackupMediaSavDataFile" before,
// 262,144 bytes and the game's own "MKDSSV10" after. See NoGbaConfig.
//
// It also brings the Game Boy Advance, which nothing in this repository covered.
//
// Like the other four, this talks ONLY to the public SDK - no reference to any LaunchBox core
// assembly - so it behaves identically under both hosts. Every entry point is defensive: the host
// swallows exceptions silently, which turns a bug into a feature that quietly does nothing, so we
// catch, log and degrade instead.
//
// THE INSTALL IS ONE FILE AND NO SETTINGS. The zip holds NO$GBA.EXE, a README and a blank DSi SD
// image; the emulator creates everything else in its own folder and touches no registry. There is
// no configuration file until somebody opens Options > Save Options, and there is no path setting
// of any kind anywhere in it - so this plugin cannot point the emulator at anything, it can only
// put files where the emulator already looks.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.NoGba
{
    public partial class NoGbaPlugin : EmulatorPlugin, ISystemEventsPlugin
    {
        /// <summary>Spelled as Platforms.xml spells them - read out of the file rather than typed
        /// from memory. There is no DSi platform in LaunchBox's metadata, which is a problem for a
        /// later instalment and not for this one.</summary>
        private const string GbaPlatform = "Nintendo Game Boy Advance";
        private const string DsPlatform = "Nintendo DS";

        /// <summary>Nothing. no$gba takes a ROM as its one positional argument and has no documented
        /// switches - the executable is packed, so there is nothing to read out of it either, and
        /// the community's word is that command-line options were lost when it was compressed.
        /// The host appends the ROM, so an empty command line is the whole story.</summary>
        private const string DefaultCommandLine = "";

        private const string PluginId = "com.nixxou.lbip.nogba";

        public NoGbaPlugin()
        {
            Log.Info("plugin constructed, assembly " + typeof(NoGbaPlugin).Assembly.Location);

            // As early as possible: the patch only sees connections opened AFTER it is installed,
            // and LaunchBox reads its metadata the moment a window asks for it.
            LbipRowInjection.Install(PluginId, MetadataRows());
        }

        public override string EmulatorName => "no$gba";

        /// <summary>What LaunchBox's emulator metadata should say about no$gba, published through the
        /// row injection rather than written into their database - see LbipRowInjection.
        ///
        /// It is needed here for the same reason it was needed for Flycast and melonDS: the Emulators
        /// table of LaunchBox.Metadata.db was queried and has no row for no$gba at all. Without these
        /// rows the Add Emulator window offers nothing for it.
        ///
        /// AUTOEXTRACT IS TRUE, and that is the load-bearing field. no$gba cannot open an archive -
        /// handed one it shows a modal "Cartridge not found" and waits - so the host has to unpack it
        /// first. See NoGbaRoms.</summary>
        private static IEnumerable<LbipEmulatorRow> MetadataRows()
        {
            var extensions = NoGbaRoms.DeclaredExtensions();

            yield return new LbipEmulatorRow
            {
                Name = "no$gba",
                CommandLine = DefaultCommandLine,
                ApplicableFileExtensions = extensions,
                Url = "https://problemkaputt.de/gba.htm",
                BinaryFileName = NoGbaPaths.ExecutableName,
                AutoExtract = true,
                Platforms =
                {
                    new LbipPlatformRow
                    {
                        Platform = GbaPlatform,
                        ApplicableFileExtensions = extensions,
                        // Not recommended: mGBA and the libretro cores are more accurate on GBA.
                        // no$gba is offered, not pushed.
                        Recommended = false,
                    },
                    new LbipPlatformRow
                    {
                        Platform = DsPlatform,
                        ApplicableFileExtensions = extensions,
                        Recommended = false,
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
                LbipRowInjection.Install(PluginId, MetadataRows());
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
                if (!NoGbaPaths.IsNoGbaExecutable(path)) continue;
                claimed.Add(emu);

                EnsureAutoExtract(emu);
                EnsureHotkeyScripts(emu);
            }

            // The host asks this constantly - twenty-three times in one second, measured on another
            // plugin - and the answer almost never changes. Said once, then only when it does.
            var signature = string.Join("|", claimed.Select(e => { try { return e.Title; } catch { return "?"; } })
                                                    .OrderBy(t => t));
            if (signature != _lastClaimed)
            {
                _lastClaimed = signature;
                Log.Info("GetApplicableEmulators: claimed " + claimed.Count + " emulator(s)");
            }
            return claimed;
        }

        /// <summary>Turn the host's archive extraction ON, overriding whatever is there.
        ///
        /// THIS OVERRIDES A USER SETTING ON PURPOSE, which is not something done lightly anywhere
        /// else in this repository. For most emulators AutoExtract is a preference. For no$gba it is
        /// the difference between a game launching and a modal "Cartridge not found" box sitting
        /// behind the emulator window with nobody to click it. There is no configuration in which
        /// leaving it off is what somebody wanted.</summary>
        private static void EnsureAutoExtract(IEmulator emu)
        {
            try
            {
                if (emu.AutoExtract) return;
                emu.AutoExtract = true;
                Log.Info("turned on Auto-Extract for \"" + Safe(() => emu.Title)
                         + "\" - no$gba cannot open an archive, and shows a modal box and waits when "
                         + "it is handed one. The host has to unpack first.");
            }
            catch { }
        }

        /// <summary>Describe no$gba's keys in the emulator's AutoHotkey fields - what the pause screen
        /// of LaunchBox and BigBox sends. Read off its own File menu; see NoGbaAhk.
        ///
        /// Only a blank field is filled: a script the user wrote is his answer to the same question.
        /// The idempotence is on the OBJECT, never on the executable - the host hands us the same
        /// emulator under a new object every time a window asks.</summary>
        private static void EnsureHotkeyScripts(IEmulator emu)
        {
            try
            {
                if (!IsBlank(() => emu.AutoHotkeyScript)
                    && !IsBlank(() => emu.ExitAutoHotkeyScript)
                    && !IsBlank(() => emu.SaveStateAutoHotkeyScript)
                    && !IsBlank(() => emu.LoadStateAutoHotkeyScript)) return;

                var set = new List<string>();
                if (Fill(() => emu.AutoHotkeyScript, v => emu.AutoHotkeyScript = v, NoGbaAhk.Running))
                    set.Add("running");
                if (Fill(() => emu.ExitAutoHotkeyScript, v => emu.ExitAutoHotkeyScript = v, NoGbaAhk.Exit))
                    set.Add("exit");
                if (Fill(() => emu.SaveStateAutoHotkeyScript,
                         v => emu.SaveStateAutoHotkeyScript = v, NoGbaAhk.SaveState))
                    set.Add("save");
                if (Fill(() => emu.LoadStateAutoHotkeyScript,
                         v => emu.LoadStateAutoHotkeyScript = v, NoGbaAhk.LoadState))
                    set.Add("load");

                if (set.Count > 0) Log.Info("hotkey scripts set: " + string.Join(", ", set));
            }
            catch (Exception ex) { Log.Warn("could not set the hotkey scripts", ex); }
        }

        private static bool IsBlank(Func<string> get)
        {
            try { return string.IsNullOrWhiteSpace(get()); } catch { return false; }
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

        // ── platforms ────────────────────────────────────────────────────────

        public override EmulatorSupportResponse IsPlatformSupported(string platform)
        {
            var name = (platform ?? "").Trim();
            bool ours = string.Equals(name, GbaPlatform, StringComparison.InvariantCultureIgnoreCase)
                        || string.Equals(name, DsPlatform, StringComparison.InvariantCultureIgnoreCase);
            // Supported but not recommended: on both platforms there are more accurate emulators,
            // and this one is here for what it can do that they cannot - be no$gba.
            return new EmulatorSupportResponse(ours, false);
        }

        // ── versions ─────────────────────────────────────────────────────────

        /// <summary>What is installed, as written down at install time.
        ///
        /// THERE IS NOTHING TO READ OFF THE FILE. The executable's version resource says, literally,
        /// FileVersion = "Windows version". So an installation this plugin did not make has no
        /// version - null, honestly, rather than a number invented from a file size.</summary>
        public override string GetCurrentVersion(string applicationPath)
        {
            try
            {
                if (!NoGbaPaths.IsNoGbaExecutable(applicationPath)) return null;
                var dir = Path.GetDirectoryName(ResolveFullPath(applicationPath));
                return NoGbaDownload.Installed(dir)?.Label;
            }
            catch (Exception ex) { Log.Warn("could not read the installed version", ex); return null; }
        }

        public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions()
        {
            var versions = new List<EmulatorControllerVersion>();
            try
            {
                var build = NoGbaDownload.Latest();
                if (build == null) return versions;
                versions.Add(new EmulatorControllerVersion(NoGbaDownload.Url, build.Label, "no$gba-w.zip"));
            }
            catch (Exception ex) { Log.Warn("could not list the installable versions", ex); }
            return versions;
        }

        /// <summary>Overridden because the SDK's default compares two strings and announces an update
        /// whenever GetCurrentVersion answers null - which, for an installation somebody made by
        /// hand, is always.</summary>
        public override bool IsUpdateAvailable(string emulatorAppPath, out EmulatorControllerVersion version)
        {
            version = null;
            try
            {
                var dir = Path.GetDirectoryName(ResolveFullPath(emulatorAppPath));
                var have = NoGbaDownload.Installed(dir);
                if (have == null) return false;        // we did not install it; not ours to update

                var latest = NoGbaDownload.Latest();
                if (latest == null) return false;      // the server did not answer; claim nothing

                if (string.Equals(have.Tag, latest.Tag, StringComparison.Ordinal)) return false;

                version = new EmulatorControllerVersion(NoGbaDownload.Url, latest.Label, "no$gba-w.zip");
                return true;
            }
            catch (Exception ex) { Log.Warn("could not check for an update", ex); return false; }
        }

        // ── install ──────────────────────────────────────────────────────────

        public override EmulatorInstallResponse InstallEmulator(InstallEmulatorArgs args)
        {
            string archive = null;
            try
            {
                bool reinstall = args?.ExistingEmulator != null;
                string targetDir = reinstall
                    ? Path.GetDirectoryName(ResolveFullPath(args.ExistingEmulator.ApplicationPath))
                    : Path.Combine(LaunchBoxRoot(), "Emulators", "no$gba");
                if (string.IsNullOrWhiteSpace(targetDir))
                    return new EmulatorInstallResponse("Couldn't work out where to install no$gba.");

                var build = NoGbaDownload.Latest();

                Report(args, "Downloading no$gba...", 0);
                archive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                NoGbaDownload.Fetch(archive,
                    p => Report(args, "Downloading no$gba...", p),
                    () => { try { return args?.ShouldCancelFunc?.Invoke() ?? false; } catch { return false; } });

                Report(args, "Extracting no$gba...", null);
                Directory.CreateDirectory(targetDir);
                // Extract OVER, never clear first: no$gba keeps its configuration, its BATTERY saves
                // and its snapshots inside this very folder.
                Archives.ExtractOver(archive, targetDir);

                string exe = NoGbaPaths.FindExecutable(targetDir);
                if (exe == null)
                    return new EmulatorInstallResponse(
                        "no$gba was downloaded and extracted to " + targetDir
                        + " but no executable was found in it.");

                var layout = NoGbaPaths.Resolve(exe);
                Log.Info("installed to " + targetDir + " (" + layout.Reason + ")");
                NoGbaDownload.Stamp(targetDir, build);

                // The point of the whole plugin, applied before anybody launches anything.
                NoGbaConfig.Apply(layout);
                NoGbaBios.Sync(layout);

                if (reinstall)
                {
                    try { args.ExistingEmulator.ApplicationPath = MakeRelativeToLaunchBox(exe); } catch { }
                    EnsureAutoExtract(args.ExistingEmulator);
                    return new EmulatorInstallResponse(args.ExistingEmulator, "no$gba updated.");
                }
                if (args != null && !args.ShouldCreateEmulator)
                    return new EmulatorInstallResponse(
                        "no$gba was installed to " + targetDir + ", but no emulator entry was requested.");

                var created = CreateEmulator(exe, build?.Label);
                if (created == null)
                    return new EmulatorInstallResponse(
                        "no$gba was installed to " + targetDir
                        + " but the emulator entry couldn't be created (no data manager available).");
                return new EmulatorInstallResponse(created, "no$gba installed.");
            }
            catch (OperationCanceledException) { return new EmulatorInstallResponse("Installation cancelled."); }
            catch (Exception ex)
            {
                Log.Warn("install failed", ex);
                return new EmulatorInstallResponse("Failed to install no$gba: " + ex.Message);
            }
            finally
            {
                try { if (archive != null && File.Exists(archive)) File.Delete(archive); } catch { }
            }
        }

        /// <summary>Create the emulator entry, with a platform row per platform.
        ///
        /// DefaultPlatform is deliberately NOT set: the Edit Emulator window turns it into a
        /// duplicate platform row. The lesson is another plugin's, paid for there.</summary>
        private static IEmulator CreateEmulator(string exePath, string label)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null) return null;

                var emu = dm.AddNewEmulator();
                if (emu == null) return null;

                emu.Title = label == null ? "no$gba" : "no$gba " + label;
                emu.ApplicationPath = MakeRelativeToLaunchBox(exePath);
                emu.CommandLine = DefaultCommandLine;
                emu.AutoExtract = true;          // see EnsureAutoExtract

                foreach (var platform in new[] { GbaPlatform, DsPlatform })
                {
                    var row = emu.AddNewEmulatorPlatform();
                    if (row == null) continue;
                    row.Platform = platform;
                    row.IsDefault = true;
                }

                EnsureHotkeyScripts(emu);
                dm.Save(false);
                Log.Info("created the emulator entry \"" + emu.Title + "\"");
                return emu;
            }
            catch (Exception ex) { Log.Warn("could not create the emulator entry", ex); return null; }
        }

        // ── BIOS ─────────────────────────────────────────────────────────────

        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string platform)
            => BiosFiles(platform);

        /// <summary>The overload the host actually calls. The command line is ignored: no$gba has no
        /// cores and no switches, so nothing in it changes which files apply.</summary>
        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(
            string emulatorApplicationPath, string platform, string commandLine)
            => BiosFiles(platform, emulatorApplicationPath);

        /// <summary>Declared OPTIONAL, every one of them.
        ///
        /// no$gba's default is to start a cartridge directly without running any boot code, and it
        /// runs games with none of these present. They buy accuracy - real BIOS call behaviour, the
        /// boot animation - not function. Marking them Required would put a red badge on a working
        /// installation and teach people to ignore the badge.</summary>
        private static IEnumerable<EmulatorBiosFile> BiosFiles(string platform, string appPath = null)
        {
            var files = new List<EmulatorBiosFile>();
            try
            {
                var name = (platform ?? "").Trim();
                bool gba = string.Equals(name, GbaPlatform, StringComparison.InvariantCultureIgnoreCase);
                bool ds = string.Equals(name, DsPlatform, StringComparison.InvariantCultureIgnoreCase);
                if (!gba && !ds) return files;

                NoGbaLayout layout = null;
                if (NoGbaPaths.IsNoGbaExecutable(appPath))
                    layout = NoGbaPaths.Resolve(ResolveFullPath(appPath));

                foreach (var file in NoGbaBios.Files)
                {
                    bool isGba = file.TheirName.StartsWith("BIOSGBA", StringComparison.OrdinalIgnoreCase);
                    if (gba != isGba) continue;

                    var complaint = layout == null ? null : NoGbaBios.SizeComplaint(layout, file);
                    var what = file.What + " - optional, no$gba runs without it"
                             + (complaint == null ? "" : " (the copy in RetroArch\\system " + complaint + ")");

                    files.Add(new EmulatorBiosFile(NoGbaBios.TargetDirName, file.TheirName,
                                                   false, what, null, null));
                }
            }
            catch (Exception ex) { Log.Warn("could not list the BIOS files", ex); }
            return files;
        }

        // ── launch ───────────────────────────────────────────────────────────

        /// <summary>Make sure the configuration still says what it has to before the game starts.
        ///
        /// NEVER FAILS A LAUNCH. Everything here is a convenience; none of it is a reason to refuse
        /// somebody their game.</summary>
        public override PrepareForLaunchResponse PrepareEmulatorForLaunch(PrepareForLaunchArgs args)
        {
            try
            {
                var exe = Safe(() => args?.EmulatorBeingLaunched?.ApplicationPath);
                if (!string.IsNullOrWhiteSpace(exe) && NoGbaPaths.IsNoGbaExecutable(exe))
                {
                    var layout = NoGbaPaths.Resolve(ResolveFullPath(exe));

                    // Checked every launch, not written every launch: Options > Save Options rewrites
                    // the whole INI from the running configuration, so a user who opens that dialog
                    // for an unrelated reason silently reverts this.
                    NoGbaConfig.Apply(layout);
                    NoGbaBios.Sync(layout);
                }

                // If an archive reaches us here, the host did not unpack it - and no$gba is about to
                // put up a modal box and wait. Said loudly, because the alternative is a launch that
                // hangs with no explanation anywhere.
                var rom = Safe(() => args?.GameBeingLaunched?.ApplicationPath);
                if (NoGbaRoms.IsArchive(rom))
                    Log.Warn("about to hand no$gba an archive (" + Path.GetFileName(rom ?? "")
                             + "). It cannot open one: expect a \"Cartridge not found\" box. "
                             + "Turn Auto-Extract on for this emulator in Edit Emulator.");
            }
            catch (Exception ex) { Log.Warn("PrepareEmulatorForLaunch", ex); }

            // The command line is left alone: the host appends the ROM, and no$gba takes no switches.
            return new PrepareForLaunchResponse(success: true);
        }

        // ── RetroAchievements ────────────────────────────────────────────────

        /// <summary>No. no$gba has no rcheevos, no RetroAchievements menu and no account setting -
        /// it predates the whole idea and is a single packed executable with no network features
        /// beyond its own link emulation.</summary>
        public override RetroAchievementSupportResponse SupportsRetroAchievements(string emulatorApplicationPath)
            => new RetroAchievementSupportResponse(false, false);

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
