// Vita3K integration for LaunchBox / LiteBox.
//
// THIS ONE ONLY INSTALLS, for now. It claims a Vita3K, publishes a row, reports versions, downloads
// the emulator and gets its firmware in - and stops there. No save management, no launch handling:
// the way a PS Vita title's data should be captured is a design that has not been settled, and
// shipping half of it would leave users with saves in a shape we then have to migrate.
//
// Like the other four, this talks ONLY to the public SDK - no reference to any LaunchBox core
// assembly - so it behaves identically under both hosts. Every entry point is defensive: the host
// swallows exceptions silently, which turns a bug into a feature that quietly does nothing.
//
// TWO THINGS MAKE VITA3K DIFFERENT from its neighbours here.
//
// Its release tag is the constant "continuous", re-pointed by CI at every build, so the tag cannot
// say which build a release is. The build number lives in the release notes, and the SAME number is
// the fourth field of the executable's Win32 version - which is what makes an update check possible.
// See Vita3kPaths.BuildNumberOf.
//
// And it needs firmware: three packages from Sony's own update servers, without which the emulator
// runs nothing at all. Getting them is part of installing, not a thing to leave to the user. See
// Vita3kFirmware.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;
using LbIntegrations.Catalog;
using LbIntegrations.Lbip;

namespace LbIntegrations.Vita3k
{
    public partial class Vita3kPlugin : EmulatorPlugin, ISystemEventsPlugin, ILbCatalogSource
    {
        private const string Repo = "Vita3K/Vita3K";
        private const string VitaPlatform = "Sony Playstation Vita";

        // Their own defaults, and LaunchBox's row carries the same pair: -F is --fullscreen and -r is
        // --installed-path, the title id of an app under ux0/app. Measured in config.cpp, where -r is
        // even validated against the installed list.
        private const string DefaultCommandLine = "-F -r";

        // "windows-latest.zip", and NOT "windows-arm64-latest.zip" - which contains every substring
        // the first one does, so the arm64 build has to be excluded by name rather than merely
        // out-matched.
        private static readonly string[] AssetRequired = { "windows", "latest", ".zip" };
        private static readonly string[] AssetExcluded = { "arm64" };

        public Vita3kPlugin()
        {
            Log.Info("plugin constructed, assembly " + typeof(Vita3kPlugin).Assembly.Location);

            // THE SHARED ROW INJECTION LEARNS WHOSE PLUGIN IT IS IN. It is compiled into every
            // plugin of the pack and cannot tell on its own which log file to write to, nor which
            // kill switches to read. See LbipLog.
            LbipLog.Use(Log.Info, Log.Warn, Log.Disabled, () => Log.Tracing);

            // As early as possible: the patch only sees connections opened AFTER it is installed,
            // and LaunchBox reads its metadata the moment a window asks for it.
            LbipRowInjection.Install("com.nixxou.lbip.vita3k", MetadataRows());
        }

        /// <summary>The name this pack publishes under, in one place so its four uses cannot disagree:
        /// the row in LaunchBox's emulator catalogue, the entry Add Emulator offers, the title given
        /// to an emulator this plugin creates, and THE FOLDER THE EMULATOR IS INSTALLED INTO.
        ///
        /// It also keeps us clear of LaunchBox's own row, which is spelled "Vita3k" with a small k -
        /// measured in their metadata database, where Emulators.Name is the PRIMARY KEY. Two rows
        /// that differ only in case would be a collision waiting for a host that compares
        /// insensitively; the Nixx- prefix means the question never comes up.</summary>
        private const string PackName = "Nixx-Vita3K";

        /// <summary>What a host's emulator catalogue should say about this pack's Vita3K, published
        /// through the row injection rather than written into their database - see LbipRowInjection.
        ///
        /// The command line and the binary name are LaunchBox's own values, kept deliberately: this
        /// plugin has measured nothing that says otherwise, and -F -r is what the emulator documents.
        /// The extensions are left EMPTY, as theirs are, and that is a real statement rather than a
        /// gap - a Vita "rom" is a title id of an app already installed under ux0\app, not a file
        /// with an extension. Declaring .vpk here would claim a launch model this plugin does not
        /// yet implement.</summary>
        private static IEnumerable<LbCatalogEmulator> MetadataRows()
        {
            yield return new LbCatalogEmulator
            {
                Name = PackName,
                CommandLine = DefaultCommandLine,
                ApplicableFileExtensions = null,
                Url = "https://vita3k.org/",
                BinaryFileName = Vita3kPaths.ExecutableNames[0],
                AutoExtract = false,
                Platforms =
                {
                    new LbCatalogPlatform { Platform = VitaPlatform, Recommended = true },
                },
            };
        }

        public override string EmulatorName => PackName;

        /// <summary>What this plugin brings to a host's emulator catalogue, for a host that asks
        /// rather than one whose database has to be patched. Same rows either way.</summary>
        public IEnumerable<LbCatalogEmulator> EmulatorRows() => MetadataRows();

        // -- the host is up ------------------------------------------------

        /// <summary>Try again to install the metadata patch. The constructor is the earliest moment,
        /// which is what we want, but it may be TOO early: the patch needs Microsoft.Data.Sqlite to be
        /// loaded already, and assemblies load on first use. Install is a no-op once it has succeeded.
        ///
        /// Three event names because the two hosts do not raise the same one.</summary>
        public void OnEventRaised(string eventType)
        {
            try
            {
                if (eventType != SystemEventTypes.PluginInitialized
                    && eventType != SystemEventTypes.LaunchBoxStartupCompleted
                    && eventType != SystemEventTypes.BigBoxStartupCompleted) return;

                Log.Info("host event " + Q(eventType));
                LbipRowInjection.Install("com.nixxou.lbip.vita3k", MetadataRows());
            }
            catch (Exception ex) { Log.Warn("OnEventRaised", ex); }
        }

        private static string Q(string text) { return "\"" + text + "\""; }

        // -- claiming -------------------------------------------------------

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
                if (!Vita3kPaths.IsVita3kExecutable(path)) continue;
                claimed.Add(emu);
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

        public override EmulatorSupportResponse IsPlatformSupported(string platform)
        {
            bool supported = string.Equals((platform ?? "").Trim(), VitaPlatform,
                                           StringComparison.InvariantCultureIgnoreCase);
            Log.Verbose("IsPlatformSupported(\"" + platform + "\") -> " + supported);
            return new EmulatorSupportResponse(supported, supported);
        }

        // -- versions -------------------------------------------------------

        /// <summary>The installed build number as a string, or null.
        ///
        /// A bare number rather than "0.2.1.4098", because it is the same quantity the release notes
        /// publish and comparing the two is the whole point. See Vita3kPaths.BuildNumberOf.</summary>
        public override string GetCurrentVersion(string applicationPath)
        {
            var build = Vita3kPaths.BuildNumberOf(applicationPath);
            return build?.ToString();
        }

        /// <summary>The build behind the rolling "continuous" tag.
        ///
        /// The tag itself is useless as a label - it is the same string for every build ever
        /// published - so the label is the build number, taken out of the release notes with the very
        /// regex Vita3K's own updater uses. When the notes cannot be read (the API refused us and the
        /// Atom fallback carries no body) the asset is still perfectly downloadable, so we offer it
        /// with an honest label rather than refusing.</summary>
        public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions()
        {
            Log.Info("GetInstallableVersions: asked");
            var release = GitHubReleases.GetLatest(Repo);
            if (release == null) { Log.Warn("no release information for " + Repo); return null; }

            var assets = GitHubReleases.SelectAssets(release, AssetRequired, AssetExcluded);
            if (assets.Count == 0)
            {
                Log.Warn("release " + release.Tag + " has no Windows asset matching our filters; saw: "
                         + string.Join(", ", release.Assets.Select(a => a.Name)));
                return null;
            }
            if (assets.Count > 1)
                Log.Warn("release " + release.Tag + " matched " + assets.Count + " assets: "
                         + string.Join(", ", assets.Select(a => a.Name)));

            var label = BuildNumberIn(release.Body) ?? "unknown";
            return assets.Select(a => new EmulatorControllerVersion(a.DownloadUrl, label, a.Name)).ToList();
        }

        /// <summary>The build number out of a release body, or null. The line reads
        /// "Vita3K Build: 4098"; the pattern is Vita3K's own, from update_manager.cpp.</summary>
        private static string BuildNumberIn(string body)
        {
            try
            {
                if (string.IsNullOrEmpty(body)) return null;
                var match = System.Text.RegularExpressions.Regex.Match(
                    body, @"Vita3K Build:\s*(\d+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                return match.Success ? match.Groups[1].Value : null;
            }
            catch { return null; }
        }

        /// <summary>Three answers, not two, and the first one is what draws the Download button.
        ///
        /// MEASURED, on no$gba, at some cost: Add Emulator calls this with an EMPTY path - there is
        /// no emulator yet, that is the point of the window - and shows its button only when the
        /// answer is true. It is not GetInstallableVersions, whose name suggests otherwise and which
        /// is never called in that case. So "nothing is installed" must answer TRUE; treating it as
        /// "up to date" is what leaves an emulator with no way to be downloaded.
        ///
        /// With something installed, the comparison is NUMERIC. These are git commit counts, so 4098
        /// and 998 order correctly as numbers and exactly backwards as strings.</summary>
        public override bool IsUpdateAvailable(string emulatorAppPath, out EmulatorControllerVersion version)
        {
            version = null;
            try
            {
                version = GetInstallableVersions()?.FirstOrDefault();
                if (version == null) return false;

                // Nothing installed is not "up to date". This is the Add Emulator case.
                if (string.IsNullOrWhiteSpace(emulatorAppPath)) return true;

                var installed = Vita3kPaths.BuildNumberOf(ResolveFullPath(emulatorAppPath));
                if (installed == null) { version = null; return false; }

                int latest;
                if (!int.TryParse(version.Label, out latest)) { version = null; return false; }
                if (latest <= installed.Value) { version = null; return false; }
                return true;
            }
            catch (Exception ex) { Log.Warn("update check failed", ex); return false; }
        }

        // -- installation ---------------------------------------------------

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
                            "Couldn't determine which build of Vita3K to install. GitHub may be rate "
                            + "limiting this machine; try again in a few minutes.");
                    url = latest.Identifier;
                    label = latest.Label;
                }

                bool reinstall = args?.ExistingEmulator != null;
                string targetDir = reinstall
                    ? Path.GetDirectoryName(ResolveFullPath(args.ExistingEmulator.ApplicationPath))
                    : Path.Combine(LaunchBoxRoot(), "Emulators", PackName);
                if (string.IsNullOrWhiteSpace(targetDir))
                    return new EmulatorInstallResponse("Couldn't work out where to install Vita3K.");

                Report(args, "Downloading Vita3K...", 0);
                archive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                GitHubReleases.Download(url, archive,
                    p => Report(args, "Downloading Vita3K...", p),
                    () => Cancelled(args));

                Report(args, "Extracting Vita3K...", null);
                Directory.CreateDirectory(targetDir);
                // Extract OVER, never clear first: portable\ lives inside this very folder and holds
                // the firmware we are about to install, plus whatever the user has put in it since.
                Archives.ExtractOver(archive, targetDir);

                string exe = Vita3kPaths.FindExecutable(targetDir);
                if (exe == null)
                    return new EmulatorInstallResponse(
                        "Vita3K was downloaded and extracted to " + targetDir
                        + " but no Vita3K executable was found in it.");

                if (!reinstall) MakePortable(exe);

                var layout = Vita3kPaths.Resolve(exe);
                Log.Info("installed to " + targetDir + " - storage root: " + layout.VitaFs
                         + " (" + layout.Reason + ")");

                var firmware = InstallFirmware(args, layout);

                if (reinstall)
                {
                    try { args.ExistingEmulator.ApplicationPath = MakeRelativeToLaunchBox(exe); } catch { }
                    return new EmulatorInstallResponse(args.ExistingEmulator, "Vita3K updated." + firmware);
                }
                if (args != null && !args.ShouldCreateEmulator)
                    return new EmulatorInstallResponse(
                        "Vita3K was installed to " + targetDir + ", but no emulator entry was requested."
                        + firmware);

                var created = CreateEmulator(exe, label);
                if (created == null)
                    return new EmulatorInstallResponse(
                        "Vita3K was installed to " + targetDir
                        + " but the emulator entry couldn't be created (no data manager available)."
                        + firmware);
                return new EmulatorInstallResponse(created, "Vita3K installed." + firmware);
            }
            catch (OperationCanceledException) { return new EmulatorInstallResponse("Installation cancelled."); }
            catch (Exception ex)
            {
                Log.Warn("install failed", ex);
                return new EmulatorInstallResponse("Failed to install Vita3K: " + ex.Message);
            }
            finally
            {
                try { if (archive != null && File.Exists(archive)) File.Delete(archive); } catch { }
            }
        }

        /// <summary>Create the directory whose existence keeps everything local.
        ///
        /// ONLY ON A FRESH INSTALL OF OURS, and the restraint is the whole point. Vita3K decides where
        /// its games, firmware and saves live by looking for portable\ beside the executable -
        /// measured in app_init.cpp - and creating it under an install that has been running for a
        /// year does not move anything: it repoints the filesystem root, and the user's library, still
        /// sitting in %AppData%\Vita3K\Vita3K, simply vanishes from the emulator's view. A reinstall
        /// therefore touches nothing: if we put it there, it is already there.</summary>
        private static void MakePortable(string exePath)
        {
            try
            {
                var dir = Vita3kPaths.PortableDirOf(Path.GetDirectoryName(Path.GetFullPath(exePath)));
                if (dir == null) return;
                if (Directory.Exists(dir)) { Log.Info("portable\\ was already there"); return; }
                Directory.CreateDirectory(dir);
                Log.Info("created " + dir + " - storage stays inside the emulator folder rather than %AppData%");
            }
            catch (Exception ex) { Log.Warn("could not create the portable directory", ex); }
        }

        /// <summary>Get whatever firmware is missing in, and say so in one phrase for the install
        /// message. Never throws, and never fails the install: an emulator on disk with no firmware is
        /// still a better outcome than no emulator, and the user can run the install again.</summary>
        private static string InstallFirmware(InstallEmulatorArgs args, Vita3kLayout layout)
        {
            try
            {
                if (layout?.VitaFs == null) return "";

                // BEFORE ANYTHING IS DOWNLOADED. Three hundred megabytes fetched to be refused at the
                // last step is three hundred megabytes of somebody's connection, and the answer does
                // not depend on any of it.
                string tooDeep;
                if (!Vita3kFirmware.RoomToUnpack(layout.VitaFs, out tooDeep))
                    return " No firmware was installed: " + tooDeep;

                var available = Vita3kFirmware.Available();
                if (available == null)
                    return " The firmware couldn't be looked up, so none was installed - Vita3K will"
                           + " not run a game until it has some.";

                var missing = Vita3kFirmware.Missing(layout.VitaFs, available);
                if (missing.Count == 0) { Log.Info("every firmware package is already installed"); return ""; }

                Log.Info("missing firmware: " + string.Join(", ", missing.Select(m => m.ToString())));

                var done = new List<string>();
                var failed = new List<string>();
                foreach (var package in missing)
                {
                    if (Cancelled(args)) throw new OperationCanceledException();
                    if (Vita3kFirmware.Install(layout.Executable, layout.VitaFs, package,
                                               (message, progress) => Report(args, message, progress),
                                               () => Cancelled(args)))
                        done.Add(package.Directory);
                    else
                        failed.Add(package.Directory);
                }

                if (failed.Count == 0) return " Firmware installed (" + string.Join(", ", done) + ").";
                if (done.Count == 0)
                    return " No firmware could be installed - Vita3K will not run a game until it has"
                           + " some. The log says which download failed.";
                return " Firmware partly installed: " + string.Join(", ", done) + " went in, "
                       + string.Join(", ", failed) + " did not.";
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Warn("firmware installation failed", ex);
                return " The firmware installation failed; see the log.";
            }
        }

        private static IEmulator CreateEmulator(string exePath, string versionLabel)
        {
            var dm = PluginHelper.DataManager;
            if (dm == null) return null;

            var emu = dm.AddNewEmulator();
            emu.Title = PackName;
            emu.ApplicationPath = MakeRelativeToLaunchBox(exePath);
            emu.CommandLine = DefaultCommandLine;

            // DefaultPlatform IS NOT SET, and that is the point. Measured on a real library: the Edit
            // Emulator window adds a platform row for whatever this field names ON TOP of the
            // association that already exists, so an emulator with it set grows a duplicate row every
            // time its window is opened. What matters is IsDefault on the platform row, below.

            var platform = emu.AddNewEmulatorPlatform();
            platform.Platform = VitaPlatform;
            platform.IsDefault = true;

            try { dm.Save(false); } catch (Exception ex) { Log.Warn("data manager save failed", ex); }
            Log.Info("created emulator entry " + Q(PackName)
                     + (versionLabel != null ? " (build " + versionLabel + ")" : "")
                     + " -> " + emu.ApplicationPath);
            return emu;
        }

        // -- BIOS -----------------------------------------------------------

        /// <summary>Nothing, and deliberately so although Vita3K does need firmware.
        ///
        /// This contract is about FILES A USER HAS TO FIND AND PUT SOMEWHERE, which LaunchBox then
        /// checks for by name. Vita3K's firmware is none of those things: it is fetched from Sony's
        /// update servers and unpacked into the emulator's own filesystem by the emulator itself,
        /// during our install. Listing vs0 or sa0 here would put a missing-file warning in front of
        /// somebody with nothing to go and look for. When the install cannot get the firmware it says
        /// so in its own result, which is where that belongs.</summary>
        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string platform)
            => Array.Empty<EmulatorBiosFile>();

        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(
            string emulatorApplicationPath, string platform, string commandLine)
            => Array.Empty<EmulatorBiosFile>();

        // -- paths ----------------------------------------------------------

        private static bool Cancelled(InstallEmulatorArgs args)
        {
            try { return args?.ShouldCancelFunc?.Invoke() ?? false; } catch { return false; }
        }

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

        private static string ResolveFullPath(string maybeRelative)
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
    }
}
