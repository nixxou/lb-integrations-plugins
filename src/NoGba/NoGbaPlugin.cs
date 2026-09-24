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
using LbIntegrations.Dsi;
using LbIntegrations.Catalog;
using LbIntegrations.Lbip;

namespace LbIntegrations.NoGba
{
    public partial class NoGbaPlugin : EmulatorPlugin, ISystemEventsPlugin, ILbCatalogSource, IGameLaunchingPlugin
    {
        /// <summary>Spelled as Platforms.xml spells them - read out of the file rather than typed
        /// from memory. There is no DSi platform in LaunchBox's metadata, which is a problem for a
        /// later instalment and not for this one.</summary>
        private const string GbaPlatform = "Nintendo Game Boy Advance";
        private const string DsPlatform = "Nintendo DS";

        /// <summary>The name LaunchBox gives the platform, and the one melonDS's plugin already
        /// publishes - so a library set up for one emulator is set up for the other.</summary>
        private const string DsiWarePlatform = "Nintendo DSiware";

        /// <summary>Nothing. no$gba takes a ROM as its one positional argument and has no documented
        /// switches - the executable is packed, so there is nothing to read out of it either, and
        /// the community's word is that command-line options were lost when it was compressed.
        /// The host appends the ROM, so an empty command line is the whole story.</summary>
        private const string DefaultCommandLine = "";

        private const string PluginId = "com.nixxou.lbip.nogba";

        public NoGbaPlugin()
        {
            Log.Info("plugin constructed, assembly " + typeof(NoGbaPlugin).Assembly.Location);

            // THE SHARED DSi ENGINE LEARNS WHOSE PLUGIN IT IS IN, first of all: it is compiled into
            // two of them and cannot tell on its own which logger to write to, nor which process
            // name means "the emulator is running". See NoGbaHost.
            NoGbaHost.Announce();

            // THE SHARED ROW INJECTION LEARNS WHOSE PLUGIN IT IS IN. It is compiled into all
            // five plugins and cannot tell on its own which log file to write to, nor which kill
            // switches to read. See LbipLog.
            LbipLog.Use(Log.Info, Log.Warn, Log.Disabled, () => Log.Tracing);

            // As early as possible: the patch only sees connections opened AFTER it is installed,
            // and LaunchBox reads its metadata the moment a window asks for it.
            LbipRowInjection.Install(PluginId, MetadataRows());
        }

        /// <summary>The name this pack publishes under, in one place so its four uses cannot
        /// disagree: the row in LaunchBox's emulator catalogue, the entry Add Emulator offers, the
        /// title given to an emulator this plugin creates, and THE FOLDER THE EMULATOR IS INSTALLED
        /// INTO.
        ///
        /// That last one is why it matters beyond presentation. Unbroken's own integrations install
        /// to Emulators\&lt;name&gt; as well, so an official plugin for the same emulator - today's or
        /// tomorrow's - would download into the very folder this one manages, over a build we put
        /// there and around the dsi\ folder we keep inside it. Prefixing the folder is what keeps
        /// the two installs apart.
        ///
        /// AND IT IS A PREFIX, NOT A PARENT FOLDER. Emulators\Nixx\&lt;name&gt; would have been tidier
        /// and is wrong: the DSi BIOS and the user's NAND dumps are read at ..\RetroArch\system,
        /// one level up from the emulator, which resolves to Emulators\RetroArch\system today and
        /// would become Emulators\Nixx\RetroArch\system under a parent folder - a share with
        /// RetroArch that would quietly stop being a share.</summary>
        /// <summary>WITHOUT THE DOLLAR, where the emulator's own name has one. no$gba spells
        /// itself that way and this plugin says so everywhere it speaks to a person; the dollar is
        /// kept out of the one name that becomes a PATH, an INI key and a row in somebody else's
        /// database.
        ///
        /// THIS IS PRECAUTION, NOT A DIAGNOSIS, and the difference is worth stating because it was
        /// got wrong once. The Add Emulator Download button was missing for this emulator and the
        /// dollar was blamed on the strength of "LaunchBox never called GetInstallableVersions for
        /// it" - read off a log that did not carry that line at all, because this was the only one
        /// of the five plugins not tracing the call. The rename stands on its own merits: a dollar
        /// cost two traps while this pack was being built, a PowerShell string where $GBA expanded
        /// to nothing and an MSBuild LogicalName that would have done the same. It is NOT known to
        /// be what kept the button dark.
        ///
        /// The emulator is still recognised by its executable, which NoGbaPaths matches after
        /// stripping the dollar, so nothing here depends on the name a user sees.</summary>
        private const string PackName = "Nixx-nogba";

        public override string EmulatorName => PackName;

        /// <summary>What this plugin brings to a host's emulator catalogue, for a host that asks
        /// rather than one whose database has to be patched. Same rows either way - see
        /// MetadataRows.</summary>
        public IEnumerable<LbCatalogEmulator> EmulatorRows() => MetadataRows();

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
        private static IEnumerable<LbCatalogEmulator> MetadataRows()
        {
            var extensions = NoGbaRoms.DeclaredExtensions();

            yield return new LbCatalogEmulator
            {
                Name = PackName,
                CommandLine = DefaultCommandLine,
                ApplicableFileExtensions = extensions,
                Url = "https://problemkaputt.de/gba.htm",
                BinaryFileName = NoGbaPaths.ExecutableName,
                AutoExtract = true,
                Platforms =
                {
                    new LbCatalogPlatform
                    {
                        Platform = GbaPlatform,
                        ApplicableFileExtensions = extensions,
                        // Not recommended: mGBA and the libretro cores are more accurate on GBA.
                        // no$gba is offered, not pushed.
                        Recommended = false,
                    },
                    new LbCatalogPlatform
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
                // EVERY EVENT TYPE, ONCE. The three below are the ones acted on; the rest used to
                // be dropped in silence, so if the host announces a game ending anywhere it is
                // here that it would have gone unnoticed. Once per type, because some of these
                // arrive on every selection change.
                if (_saidEvent.Add(eventType ?? "(null)"))
                    Log.Info("host event seen: " + eventType);

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
                        || string.Equals(name, DsPlatform, StringComparison.InvariantCultureIgnoreCase)
                        || string.Equals(name, DsiWarePlatform, StringComparison.InvariantCultureIgnoreCase);
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

        /// <summary>Traced like its four siblings, and the line is not decoration: its absence
        /// cost a wrong diagnosis. "LaunchBox never asked no$gba for versions" was read off a log
        /// that simply never carried the line, and a rename followed from it. A call every other
        /// plugin records and this one did not is a hole shaped exactly like a false
        /// conclusion.</summary>
        public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions()
        {
            Log.Info("GetInstallableVersions: asked");
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

        /// <summary>Is there something to fetch? Overridden because the SDK's default compares two
        /// strings and announces an update whenever GetCurrentVersion answers null - which, for an
        /// installation somebody made by hand, is always.
        ///
        /// AND IT IS WHAT DRAWS THE DOWNLOAD BUTTON, which is not what the name suggests and cost a
        /// long evening to find out. LaunchBox's Add Emulator window calls this with an EMPTY path -
        /// there is no emulator yet, that is the point of the window - and shows its Download button
        /// only when the answer is true. An earlier version of this method looked for an install of
        /// ours, found none, and answered false, so no$gba was the one emulator of this pack that
        /// could not be downloaded from that window. Measured, by moving this single method onto a
        /// plugin whose button worked and watching it go dark.
        ///
        /// So there are three answers, not two:
        ///   * nothing installed          -> yes, and here is what you would get
        ///   * installed, but not by us   -> no; somebody's own build is not ours to replace
        ///   * installed by us, and old   -> yes
        /// The middle one is the restraint the original override existed for, and it is kept.</summary>
        public override bool IsUpdateAvailable(string emulatorAppPath, out EmulatorControllerVersion version)
        {
            version = null;
            try
            {
                var latest = NoGbaDownload.Latest();
                if (latest == null) return false;      // the server did not answer; claim nothing

                version = new EmulatorControllerVersion(NoGbaDownload.Url, latest.Label, "no$gba-w.zip");

                // NOTHING INSTALLED IS NOT "UP TO DATE". This is the Add Emulator case.
                if (string.IsNullOrWhiteSpace(emulatorAppPath)) return true;

                var dir = Path.GetDirectoryName(ResolveFullPath(emulatorAppPath));
                var have = NoGbaDownload.Installed(dir);
                if (have == null) { version = null; return false; }   // not ours to update

                if (string.Equals(have.Tag, latest.Tag, StringComparison.Ordinal))
                {
                    version = null;
                    return false;
                }
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
                    : Path.Combine(LaunchBoxRoot(), "Emulators", PackName);
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

                // THE TITLE IS THE NAME, NOT THE NAME AND A DATE. This used to append the release
                // label - "Nixx-nogba 2025-04-14" - which made the emulator's name disagree with
                // the catalogue row and with its own install folder, and went stale the first time
                // it was updated, since nothing rewrites a title afterwards. The four sibling
                // plugins all set the bare name. The version is not lost: GetCurrentVersion reports
                // it and the Edit Emulator window shows it.
                emu.Title = PackName;
                emu.ApplicationPath = MakeRelativeToLaunchBox(exePath);
                emu.CommandLine = DefaultCommandLine;
                emu.AutoExtract = true;          // see EnsureAutoExtract

                foreach (var platform in new[] { GbaPlatform, DsPlatform, DsiWarePlatform })
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

                    // WHICH MACHINE THIS GAME NEEDS. The DSi mode and the boot entrypoint are
                    // GLOBAL settings in no$gba - there is no command line to carry them - so they
                    // are written per launch and written back. A GBA game must not be left booting
                    // through a BIOS it does not need.
                    var romPath = ResolveFullPath(Safe(() => args?.GameBeingLaunched?.ApplicationPath));
                    var header = NdsHeader.Describe(romPath);
                    if (header.Known && header.IsDSiWare)
                    {
                        if (!NoGbaDsi.Prepare(layout, header, romPath))
                        {
                            NotPlaying();
                            return new PrepareForLaunchResponse(success: false);
                        }
                        // Arms the watcher, and remembers what ran for a host that bothers to say
                // the game has ended. LaunchBox does not - measured.
                        Playing(NoGbaHost.For(layout), header.TitleId, DsiBios7(layout));
                    }
                    else
                    {
                        NoGbaDsi.SetMode(layout, dsi: false);
                        NotPlaying();
                    }
                }

                // An archive the host is NOT going to unpack, which is a launch that hangs with no
                // explanation anywhere: no$gba cannot open one and answers with a modal box.
                //
                // AND ONLY THEN. The path seen here is the game's, in the library, so it is an
                // archive whenever the library holds one - which says nothing about what the host
                // will actually hand over. Warning on that alone sent somebody to turn on a setting
                // that was already on, over a launch where the host had unpacked correctly. The
                // emulator's own AutoExtract is the thing that decides, so it is what is asked.
                var rom = Safe(() => args?.GameBeingLaunched?.ApplicationPath);
                if (NoGbaRoms.IsArchive(rom) && !Safe(() => args.EmulatorBeingLaunched.AutoExtract))
                    Log.Warn("about to hand no$gba an archive (" + Path.GetFileName(rom ?? "")
                             + ") and Auto-Extract is off for this emulator. It cannot open one: "
                             + "expect a \"Cartridge not found\" box. Turn Auto-Extract on in Edit "
                             + "Emulator.");
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

        // -- the game has closed ---------------------------------------------

        /// <summary>What was playing, remembered at launch because OnGameExited is told nothing.
        /// Static: the host constructs a plugin more than once - measured - and only one game runs
        /// at a time.
        ///
        /// KEPT FOR A HOST THAT SAYS SOMETHING. LaunchBox 14 is not one: measured over a full
        /// session, it raised exactly one event - PluginInitialized - and called none of
        /// IGameLaunchingPlugin's three methods. The session comes out of the image because
        /// WatchTheEmulator sees the process go, not because anybody told us.</summary>
        private static readonly HashSet<string> _saidEvent =
            new HashSet<string>(StringComparer.Ordinal);

        private static string _playingTitleId;
        private static DsiHost _playingHost;
        private static string _playingBios7;

        internal static void Playing(DsiHost host, string titleId, string bios7)
        {
            _playingHost = host;
            _playingTitleId = titleId;
            _playingBios7 = bios7;
            WatchTheEmulator(host, titleId, bios7);
        }

        internal static void NotPlaying() { _playingHost = null; _playingTitleId = null; _playingBios7 = null; }

        // Traced, all three: the capture on exit never fired once and nothing said whether the
        // host was calling this interface at all, calling it and stopping before the exit, or not
        // reaching the plugin. Three lines answer that; guessing did not.
        public void OnBeforeGameLaunching(IGame game, IAdditionalApplication app, IEmulator emulator)
            => Log.Info("OnBeforeGameLaunching");

        public void OnAfterGameLaunched(IGame game, IAdditionalApplication app, IEmulator emulator)
            => Log.Info("OnAfterGameLaunched");

        /// <summary>Take the session out of the working image now that the game has closed.
        ///
        /// NEVER CALLED BY LAUNCHBOX, measured. It is left in because it costs nothing and another
        /// host may well raise it; what actually works is WatchTheEmulator below.
        ///
        /// It waits - a floor first, then for the emulator to be gone and the image to fall silent
        /// - and gives up quickly, because this runs while somebody is watching a frontend come
        /// back. Giving up costs nothing: the lazy path still has the session and a marker beside
        /// the image says so. See DsiWorkspace.CaptureOnExit.
        ///
        /// ON A BACKGROUND THREAD, because the host is calling us and waiting: even five seconds of
        /// a frozen window is five seconds too many for something nobody asked to watch.</summary>
        public void OnGameExited()
        {
            Log.Info("OnGameExited");
            var host = _playingHost;
            var titleId = _playingTitleId;
            var bios7 = _playingBios7;
            NotPlaying();
            if (host == null || titleId == null) return;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    if (DsiWorkspace.CaptureOnExit(host, titleId, bios7))
                        Log.Info("the session of " + titleId + " came out of the image as the game closed");
                }
                catch (Exception ex) { Log.Warn("capture on exit", ex); }
            });
        }


        /// <summary>Watch the emulator ourselves, and say in the log when it appears and when it
        /// goes. THIS IS THE SIGNAL THAT DEPENDS ON NOBODY: the host's OnGameExited has never once
        /// been seen firing, and until it is, the only thing that certainly knows the program has
        /// ended is us looking at the process list.
        ///
        /// It is a trace first and a capture second. If it turns out the host never tells us
        /// anything, this becomes the way the session comes out of the image.</summary>
        private static void WatchTheEmulator(DsiHost host, string titleId, string bios7)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var armed = DateTime.UtcNow;
                    var appeared = false;

                    // Up to two minutes for it to show up. A launch the host refuses, or a game
                    // somebody cancels, must not leave a thread polling for ever.
                    while ((DateTime.UtcNow - armed).TotalSeconds < 120)
                    {
                        if (host.Running()) { appeared = true; break; }
                        System.Threading.Thread.Sleep(500);
                    }
                    if (!appeared)
                    {
                        Log.Info("watcher: the emulator never appeared within two minutes of the launch");
                        return;
                    }
                    Log.Info("watcher: the emulator is running");

                    var started = DateTime.UtcNow;
                    while (host.Running()) System.Threading.Thread.Sleep(500);
                    Log.Info("watcher: the emulator is gone after "
                             + (int)(DateTime.UtcNow - started).TotalSeconds + "s - this is the end of"
                             + " the program, whatever the host does or does not say");

                    if (DsiWorkspace.CaptureOnExit(host, titleId, bios7))
                        Log.Info("watcher: the session of " + titleId + " came out of the image");
                }
                catch (Exception ex) { Log.Warn("watcher", ex); }
            });
        }

    }
}
