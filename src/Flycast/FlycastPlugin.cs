// Integration plugin for Flycast: Dreamcast, Naomi, Naomi 2 and Atomiswave.
//
// Four LaunchBox platforms from one emulator, which is why it was worth doing. Flycast also runs
// Sega System SP, but LaunchBox's metadata database has no platform for it, so those 28 titles have
// nowhere to be filed and IsPlatformSupported cannot claim them.
//
// Everything here was read out of Flycast's own source rather than inferred from behaviour; the
// file-level comments say which file and why. The one thing NOT read from source is the Dreamcast
// BIOS MD5, which comes from LaunchBox's published table - see FlycastBios.cs.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Flycast
{
    /// <summary>The LaunchBox platform names Flycast covers, spelled exactly as the metadata database
    /// spells them - verified against LaunchBox.Metadata.db rather than typed from memory.</summary>
    internal static class FlycastPlatforms
    {
        public const string Dreamcast = "Sega Dreamcast";
        public const string Naomi = "Sega Naomi";
        public const string Naomi2 = "Sega Naomi 2";
        public const string Atomiswave = "Sammy Atomiswave";

        public static readonly string[] All = { Dreamcast, Naomi, Naomi2, Atomiswave };

        public static bool IsArcade(string platform)
            => !string.Equals(platform, Dreamcast, StringComparison.InvariantCultureIgnoreCase)
               && All.Any(p => string.Equals(p, platform, StringComparison.InvariantCultureIgnoreCase));
    }

    public partial class FlycastPlugin : EmulatorPlugin, ISystemEventsPlugin
    {
        private const string Repo = "flyinghead/flycast";

        /// <summary>Flycast has exactly ONE Windows asset (v2.7: flycast-win64-2.7.zip). The .appx is
        /// the Store package and cannot be extracted into an Emulators folder; everything else is
        /// another operating system.</summary>
        private static readonly string[] AssetRequired = { "flycast", "win64", ".zip" };
        private static readonly string[] AssetExcluded = { "macos", "appimage", "appx", "apk", "nro", "symbols", "pdb" };


        // Traced because a plugin is a black box inside LaunchBox: the only way to tell "the host
        // never asked us" from "we answered badly" is to see which members it actually calls.
        public FlycastPlugin()
        {
            Log.Info("plugin constructed, assembly " + typeof(FlycastPlugin).Assembly.Location);
        }

        public override string EmulatorName => "Flycast";

        // ── the host is up ────────────────────────────────────────────

        /// <summary>Complete the emulator entries as soon as the host has finished loading, rather
        /// than waiting to be asked. Three event names are accepted because the two hosts do not
        /// raise the same one: LaunchBox raises LaunchBoxStartupCompleted (and BigBox its own),
        /// LiteBox raises PluginInitialized when its window is shown. The work is idempotent, so
        /// hearing several of them is free.
        ///
        /// Anything else is ignored in silence - a plugin that logs every SelectionChanged would
        /// drown its own log.</summary>
        public void OnEventRaised(string eventType)
        {
            try
            {
                if (eventType != SystemEventTypes.PluginInitialized
                    && eventType != SystemEventTypes.LaunchBoxStartupCompleted
                    && eventType != SystemEventTypes.BigBoxStartupCompleted) return;

                Log.Info("host event \"" + eventType + "\"");
                FlycastAssociation.SweepAll();
            }
            catch (Exception ex) { Log.Warn("OnEventRaised", ex); }
        }

        // ── claiming ─────────────────────────────────────────────────────────

        /// <summary>Which of the user's emulators this plugin speaks for, matched on the executable
        /// file name - the same rule Unbroken's own plugins use for Dolphin and PCSX2.</summary>
        public override IEnumerable<IEmulator> GetApplicableEmulators(IEnumerable<IEmulator> emulators)
        {
            var claimed = new List<IEmulator>();
            if (emulators == null) return claimed;
            foreach (var emu in emulators)
            {
                if (emu == null) continue;
                string path;
                try { path = emu.ApplicationPath; } catch { continue; }
                if (!FlycastPaths.IsFlycastExecutable(path)) continue;

                claimed.Add(emu);
                // LaunchBox's metadata does not know Flycast, so an entry the user created by hand
                // arrives with no platforms at all and looks like it covers everything. We know the
                // four; fill them in. In memory only - see FlycastAssociation.
                FlycastAssociation.EnsurePlatforms(emu);
            }
            Log.Info("GetApplicableEmulators: claimed " + claimed.Count + " emulator(s)");
            return claimed;
        }

        /// <summary>Recommended for the Dreamcast, supported for the three arcade platforms.
        ///
        /// The distinction is deliberate. On the Dreamcast, Flycast is the reference emulator and
        /// nothing else standalone is maintained. On Naomi, Naomi 2 and Atomiswave, MAME covers the
        /// same hardware and a user may reasonably prefer it, so we say "yes, this works" without
        /// pushing it ahead of a choice they have already made.</summary>
        public override EmulatorSupportResponse IsPlatformSupported(string platform)
        {
            var name = (platform ?? "").Trim();
            bool supported = FlycastPlatforms.All
                .Any(p => string.Equals(p, name, StringComparison.InvariantCultureIgnoreCase));
            bool recommended = string.Equals(name, FlycastPlatforms.Dreamcast,
                                             StringComparison.InvariantCultureIgnoreCase);
            Log.Info("IsPlatformSupported(\"" + platform + "\") -> " + supported
                     + (supported && !recommended ? " (supported, not recommended)" : ""));
            return new EmulatorSupportResponse(supported, recommended);
        }

        // ── versions ─────────────────────────────────────────────────────────

        /// <summary>The installed version, from the executable's Win32 version resource.
        ///
        /// shell/windows/flycast.rc sets FILEVERSION to a fixed 1,0,0,0 - useless - but the STRING
        /// field carries GIT_VERSION, which CMake fills from `git describe`: "v2.7" on a release,
        /// "v2.7-31-gabc123456" on a build in between. So read the string, never the numeric.</summary>
        public override string GetCurrentVersion(string applicationPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(applicationPath) || !File.Exists(applicationPath)) return null;
                var raw = FileVersionInfo.GetVersionInfo(applicationPath).FileVersion;
                var version = NormalizeVersion(raw);
                if (version == null) Log.Info("version resource says \"" + raw + "\" - not a version we can read");
                return version;
            }
            catch (Exception ex) { Log.Warn("could not read the version of " + applicationPath, ex); return null; }
        }

        /// <summary>Strips the leading "v". A development build keeps its "-31-gabc123456" tail: it is
        /// part of the identity, and dropping it would make a build between releases look like the
        /// release itself and suppress the update it needs.</summary>
        internal static string NormalizeVersion(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var v = raw.Trim();
            if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase)) v = v.Substring(1);
            v = v.Trim();
            // A resource left at its placeholder tells us nothing.
            if (v.Length == 0 || v == "1.0.0.0" || v.StartsWith("0.0.0")) return null;
            return v;
        }

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

            var label = NormalizeVersion(release.Tag) ?? release.Tag;
            // The constructor takes (identifier, label, description) - IDENTIFIER FIRST. Getting that
            // order wrong puts the version string where the download URL belongs, and InstallEmulator
            // then tries to download "2.7"; the probe's output is what caught it.
            return assets
                .Select(a => new EmulatorControllerVersion(a.DownloadUrl, label, a.Name))
                .ToList();
        }

        /// <summary>Compare the installed version string with the latest tag. Both come from
        /// `git describe`, so equality is the right test and ordering is not needed: a build that is
        /// not the latest tag differs from it, whichever way it sorts.</summary>
        public override bool IsUpdateAvailable(string emulatorAppPath, out EmulatorControllerVersion version)
        {
            version = null;
            try
            {
                version = GetInstallableVersions()?.FirstOrDefault();
                if (version == null) return false;                       // couldn't ask; don't nag
                if (string.IsNullOrWhiteSpace(emulatorAppPath)) return true;   // nothing installed

                var current = GetCurrentVersion(emulatorAppPath);
                if (current == null) return false;                       // unreadable; don't nag
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
                string url = args?.Version;
                string label = null;
                if (string.IsNullOrWhiteSpace(url))
                {
                    var latest = GetInstallableVersions()?.FirstOrDefault();
                    if (latest == null)
                        return new EmulatorInstallResponse(
                            "Couldn't determine which build of Flycast to install. GitHub may be rate "
                            + "limiting this machine; try again in a few minutes.");
                    url = latest.Identifier;
                    label = latest.Label;
                }

                bool reinstall = args?.ExistingEmulator != null;
                string targetDir = reinstall
                    ? Path.GetDirectoryName(ResolveFullPath(args.ExistingEmulator.ApplicationPath))
                    : Path.Combine(LaunchBoxRoot(), "Emulators", "Flycast");
                if (string.IsNullOrWhiteSpace(targetDir))
                    return new EmulatorInstallResponse("Couldn't work out where to install Flycast.");

                Report(args, "Downloading Flycast...", 0);
                archive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                GitHubReleases.Download(url, archive,
                    p => Report(args, "Downloading Flycast...", p),
                    () => { try { return args?.ShouldCancelFunc?.Invoke() ?? false; } catch { return false; } });

                Report(args, "Extracting Flycast...", null);
                Directory.CreateDirectory(targetDir);
                // Extract OVER, never clear first: Flycast is portable, so emu.cfg and the whole data\
                // folder - every VMU, every save state, the arcade NVRAM - live inside this very
                // directory. Clearing it to "install cleanly" would destroy the user's saves.
                Archives.ExtractOver(archive, targetDir);

                string exe = FlycastPaths.FindExecutable(targetDir);
                if (exe == null)
                    return new EmulatorInstallResponse(
                        "Flycast was downloaded and extracted to " + targetDir
                        + " but no Flycast executable was found in it.");

                var layout = FlycastPaths.Resolve(exe);
                Log.Info("installed to " + targetDir + " - data: " + layout.DataDir + " (" + layout.Reason + ")");

                if (reinstall)
                {
                    try { args.ExistingEmulator.ApplicationPath = MakeRelativeToLaunchBox(exe); } catch { }
                    return new EmulatorInstallResponse(args.ExistingEmulator, "Flycast updated.");
                }
                if (args != null && !args.ShouldCreateEmulator)
                    return new EmulatorInstallResponse(
                        "Flycast was installed to " + targetDir + ", but no emulator entry was requested.");

                var created = EnsureEmulator(exe, label);
                if (created == null)
                    return new EmulatorInstallResponse(
                        "Flycast was installed to " + targetDir
                        + " but the emulator entry could not be created. Add it by hand.");

                return new EmulatorInstallResponse(created, "Flycast installed.");
            }
            catch (Exception ex)
            {
                Log.Warn("InstallEmulator failed", ex);
                return new EmulatorInstallResponse("Could not install Flycast: " + ex.Message);
            }
            finally
            {
                try { if (archive != null && File.Exists(archive)) File.Delete(archive); } catch { }
            }
        }

        /// <summary>The LaunchBox emulator entry for this installation, created if it is not there and
        /// REUSED if it is.
        ///
        /// Reuse is not a refinement, it is a bug fix. The first version called AddNewEmulator
        /// unconditionally, so pressing Download twice produced two Flycast entries pointing at the
        /// same executable, each with its own four platforms - measured in the plugin's own log,
        /// "created emulator entry" twice a minute apart. A second entry is worse than useless: the
        /// user's games are assigned to one of them and the other quietly shadows it in every list.
        ///
        /// Platforms are added only where they are MISSING, for the same reason. An entry that
        /// already carries Sega Naomi must not end up carrying it twice.</summary>
        private static IEmulator EnsureEmulator(string exePath, string versionLabel)
        {
            var dm = PluginHelper.DataManager;
            if (dm == null) return null;

            var full = Safe(() => Path.GetFullPath(exePath));
            var existing = FindByExecutable(dm, full);
            var emu = existing ?? dm.AddNewEmulator();
            if (emu == null) return null;

            if (existing == null)
            {
                emu.Title = "Flycast";
                emu.ApplicationPath = MakeRelativeToLaunchBox(exePath);
                emu.CommandLine = FlycastDefaults.CommandLine;
                emu.DefaultPlatform = FlycastPlatforms.Dreamcast;
            }

            // Only what is missing. A name is "present" whatever its case and whatever spacing the
            // grid left around it; a blank row belongs to no platform and is ignored here, because
            // this path runs when WE are installing, not while someone is typing.
            var have = new HashSet<string>(
                (emu.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>())
                    .Select(p => Safe(() => p.Platform) ?? "")
                    .Where(n => n.Trim().Length > 0)
                    .Select(n => n.Trim()),
                StringComparer.InvariantCultureIgnoreCase);

            bool hadAny = have.Count > 0;
            var added = new List<string>();
            foreach (var name in FlycastPlatforms.All)
            {
                if (have.Contains(name)) continue;
                var platform = emu.AddNewEmulatorPlatform();
                if (platform == null) continue;
                platform.Platform = name;
                // Only claim the default when nothing already held one.
                platform.IsDefault = !hadAny
                                     && string.Equals(name, FlycastPlatforms.Dreamcast, StringComparison.Ordinal);
                added.Add(name);
            }

            try { dm.Save(false); } catch (Exception ex) { Log.Warn("data manager save failed", ex); }

            Log.Info((existing != null ? "reused" : "created") + " emulator entry \"" + Safe(() => emu.Title) + "\""
                     + (versionLabel != null ? " (" + versionLabel + ")" : "")
                     + " -> " + Safe(() => emu.ApplicationPath)
                     + (added.Count > 0 ? "; added " + added.Count + " platform(s): " + string.Join(", ", added)
                                        : "; platforms already complete"));
            return emu;
        }

        /// <summary>An emulator already pointing at this executable, or null. Compared on the RESOLVED
        /// path: LaunchBox stores it relative to its own folder, and two entries can spell the same
        /// file differently.</summary>
        private static IEmulator FindByExecutable(IDataManager dm, string fullExePath)
        {
            if (string.IsNullOrWhiteSpace(fullExePath)) return null;
            try
            {
                foreach (var candidate in dm.GetAllEmulators() ?? Array.Empty<IEmulator>())
                {
                    var raw = Safe(() => candidate?.ApplicationPath);
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var resolved = Safe(() => Path.GetFullPath(ResolveFullPath(raw)));
                    if (string.Equals(resolved, fullExePath, StringComparison.OrdinalIgnoreCase))
                        return candidate;
                }
            }
            catch (Exception ex) { Log.Warn("could not look for an existing emulator entry", ex); }
            return null;
        }

        // ── BIOS ─────────────────────────────────────────────────────────────

        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string platform)
            => FlycastBios.For(platform);

        /// <summary>The three-argument overload adds nothing here: Flycast's BIOS folder is fixed at
        /// data\ unless the user redirects it in emu.cfg, and a redirect makes the declared Location
        /// wrong rather than better - the contract wants a path relative to the emulator root.</summary>
        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(
            string emulatorApplicationPath, string platform, string commandLine)
            => FlycastBios.For(platform);

        // ── RetroAchievements ────────────────────────────────────────────────

        private const string CheevoSection = "achievements";
        private const string KeyEnabled = "Enabled";
        private const string KeyHardcore = "HardcoreMode";
        private const string KeyUserName = "UserName";
        private const string KeyToken = "Token";

        public override RetroAchievementSupportResponse SupportsRetroAchievements(string emulatorApplicationPath)
        {
            try
            {
                var layout = FlycastPaths.Resolve(emulatorApplicationPath);
                var cfg = FlycastIni.Read(layout.ConfigFile, CheevoSection,
                                          KeyEnabled, KeyHardcore, KeyUserName, KeyToken);

                cfg.TryGetValue(KeyEnabled, out var enabled);
                cfg.TryGetValue(KeyHardcore, out var hardcore);
                cfg.TryGetValue(KeyUserName, out var user);
                bool hasToken = cfg.TryGetValue(KeyToken, out var token) && !string.IsNullOrEmpty(token);

                return new RetroAchievementSupportResponse(
                    isSupported: true,
                    isEnabled: FlycastIni.AsBool(enabled, false),
                    // Flycast defaults HardcoreMode to false (core/cfg/option.cpp:247), unlike PPSSPP.
                    isHardcore: FlycastIni.AsBool(hardcore, false),
                    username: user,
                    // Report only WHETHER a token exists, never its value.
                    token: hasToken ? "" : null);
            }
            catch (Exception ex)
            {
                Log.Warn("could not read the RetroAchievements configuration", ex);
                return new RetroAchievementSupportResponse(isSupported: true);
            }
        }

        /// <summary>Write the credentials into emu.cfg's [achievements] section.
        ///
        /// Flycast stores the token in the same file as everything else, unlike PPSSPP which keeps it
        /// in a sidecar. FlycastIni refuses to write while Flycast is running, because Flycast rewrites
        /// emu.cfg from memory on exit and would silently discard the edit.</summary>
        public override PluginResponse InjectRetroAchievementsCredentials(
            InjectRetroAchievementsCredentialsArgs args)
        {
            try
            {
                if (args == null) return new PluginResponse(false, "No credentials supplied.");
                var layout = FlycastPaths.Resolve(args.EmulatorApplicationPath);
                if (string.IsNullOrWhiteSpace(layout.ConfigFile))
                    return new PluginResponse(false, "Could not locate Flycast's emu.cfg.");

                // Only what the host actually gave us. Writing a blank over a value the user set is a
                // way to lose their login without ever reporting a failure.
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(args.Username)) values[KeyUserName] = args.Username;
                if (!string.IsNullOrEmpty(args.Token)) values[KeyToken] = args.Token;
                if (args.IsEnabled.HasValue) values[KeyEnabled] = FlycastIni.FromBool(args.IsEnabled.Value);
                if (args.HardcoreEnabled.HasValue) values[KeyHardcore] = FlycastIni.FromBool(args.HardcoreEnabled.Value);
                if (values.Count == 0) return new PluginResponse(true);

                var error = FlycastIni.Write(layout.ConfigFile, CheevoSection, values);
                if (error != null) return new PluginResponse(false, error);

                // Never log the token.
                Log.Info("RetroAchievements credentials written for user " + (args.Username ?? "(none)"));
                return new PluginResponse(true);
            }
            catch (Exception ex)
            {
                Log.Warn("could not write the RetroAchievements credentials", ex);
                return new PluginResponse(false, "Could not write Flycast's configuration: " + ex.Message);
            }
        }

        // ── launch ───────────────────────────────────────────────────────────

        /// <summary>Called right before the spawn. Its only job is carrying the host's
        /// RetroAchievements credentials into emu.cfg. Flycast reads that file at startup and is not
        /// running yet at this point, so the write lands.
        ///
        /// It must NEVER fail the launch: a user who cannot log in to RetroAchievements still wants to
        /// play the game. The command line is left alone - what we set at install is transient by
        /// construction and does not need rewriting.</summary>
        public override PrepareForLaunchResponse PrepareEmulatorForLaunch(PrepareForLaunchArgs args)
        {
            try
            {
                if (args?.RetroAchievementCredentials != null)
                {
                    var creds = args.RetroAchievementCredentials.Value;
                    bool hardcore = false;
                    try { hardcore = args.EmulatorBeingLaunched?.EnableHardcoreAchievements ?? false; } catch { }

                    var response = InjectRetroAchievementsCredentials(
                        new InjectRetroAchievementsCredentialsArgs(
                            args.EmulatorBeingLaunched?.ApplicationPath,
                            creds.Username, creds.Token, enable: true, hardcore: hardcore));

                    if (!response.WasSuccess)
                        Log.Warn("RetroAchievements credentials were not applied: "
                                 + (response.Message ?? "no reason given") + " - launching anyway");
                }
            }
            catch (Exception ex) { Log.Warn("PrepareEmulatorForLaunch", ex); }

            return new PrepareForLaunchResponse(success: true);
        }

        // ── paths ────────────────────────────────────────────────────────────

        /// <summary>The LaunchBox installation root. LaunchBox's own plugins read NamingHelper.RootFolder
        /// from the core; with the public SDK only, we walk up from this assembly - a plugin lives at
        /// &lt;LB&gt;\Plugins\&lt;Name&gt;\, &lt;LB&gt;\Local\Plugins\&lt;Name&gt;\ or
        /// &lt;LB&gt;\System\Plugins\&lt;Name&gt;\ - until we find a folder that looks like one.</summary>
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

        internal static T Safe<T>(Func<T> f)
        {
            try { return f(); } catch { return default; }
        }
    }
}
