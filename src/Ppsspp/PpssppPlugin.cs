// PPSSPP integration for LaunchBox / LiteBox.
//
// Claim the emulator, report and install versions, report BIOS requirements, and carry
// RetroAchievements credentials into the emulator's configuration before a launch.
//
// Save management lives next door in PpssppSaves.cs, which is the other half of this class.
//
// The one structural decision worth stating up front: this plugin talks ONLY to the public SDK.
// LaunchBox's own integration plugins build their emulator with `new Emulator{...}` and rewrite the
// library through `Root.DataManager`, both of which live in the obfuscated core; that is why a host
// like LiteBox has to mirror its library to run them. IDataManager.AddNewEmulator() is public and
// does the same job, so this plugin runs identically under LaunchBox and under LiteBox, unmodified
// and unshimmed. Nothing here may reference a core assembly.
//
// Every public entry point is defensive. The host wraps our calls in a bare catch, so an exception
// here does not crash anything — it silently turns the feature off, which is far harder to diagnose
// than a logged failure. We log and degrade instead.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Ppsspp
{
    public partial class PpssppPlugin : EmulatorPlugin
    {
        private const string Repo = "hrydgard/ppsspp";
        private const string PspPlatform = "Sony PSP";       // exactly as LaunchBox's metadata db spells it

        /// <summary>The LaunchBox default for this emulator, straight out of the metadata database.
        /// --pause-menu-exit keeps the pause menu reachable while still letting the frontend regain
        /// control, which is why it is preferred to --escape-exit.</summary>
        private const string DefaultCommandLine = "--fullscreen --pause-menu-exit";

        // Asset selection, declarative. ARM64 names contain "ARM64" but not "x64", so the two sets
        // stay disjoint. The exclusions are defensive: upstream does not currently publish debug or
        // VR builds under these names, but it has in the past.
        private static readonly string[] AssetRequiredX64 = { "Windows", "x64", ".zip" };
        private static readonly string[] AssetRequiredArm64 = { "Windows", "ARM64", ".zip" };
        private static readonly string[] AssetExcluded = { "debug", "symbols", "VR" };

        public override string EmulatorName => "PPSSPP";

        // ── claiming the emulator ────────────────────────────────────────────

        /// <summary>Which of the user's emulators this plugin speaks for. Matched on the executable
        /// file name, the same way LaunchBox's metadata database identifies PPSSPP ("PPSSPP*.exe").</summary>
        public override IEnumerable<IEmulator> GetApplicableEmulators(IEnumerable<IEmulator> emulators)
        {
            var claimed = new List<IEmulator>();
            if (emulators == null) return claimed;
            foreach (var emu in emulators)
            {
                if (emu == null) continue;
                string path;
                try { path = emu.ApplicationPath; } catch { continue; }
                if (PpssppPaths.IsPpssppExecutable(path)) claimed.Add(emu);
            }
            return claimed;
        }

        public override EmulatorSupportResponse IsPlatformSupported(string platform)
        {
            bool supported = string.Equals((platform ?? "").Trim(), PspPlatform,
                                           StringComparison.InvariantCultureIgnoreCase);
            // Supported implies recommended here: PPSSPP is the reference PSP emulator, and the only
            // standalone one LaunchBox knows about.
            return new EmulatorSupportResponse(supported, supported);
        }

        // ── versions ─────────────────────────────────────────────────────────

        /// <summary>Version of the installed emulator, read from the executable's Win32 version
        /// resource: "v1.20.4" for a release, "v1.20.4-1874-ga312749f8320" for a development build,
        /// "unknown" when it was built without git. Returns the bare number, or null.
        ///
        /// We deliberately do NOT run the executable to ask it. PPSSPP's -v means *verbose*, not
        /// version, so probing that way launches the emulator; --version only exists from 1.21, and
        /// in a /SUBSYSTEM:WINDOWS binary whose printf has no console to write to.</summary>
        public override string GetCurrentVersion(string applicationPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(applicationPath) || !File.Exists(applicationPath)) return null;
                var raw = FileVersionInfo.GetVersionInfo(applicationPath).FileVersion;
                var v = NormalizeVersion(raw);
                if (v == null) Log.Info("version resource says \"" + raw + "\" — not a version we can read");
                return v;
            }
            catch (Exception ex) { Log.Warn("could not read the version of " + applicationPath, ex); return null; }
        }

        /// <summary>Strips the leading "v" and any "-&lt;commits&gt;-g&lt;sha&gt;" development suffix.
        /// "unknown" (a build made outside git) becomes null.</summary>
        internal static string NormalizeVersion(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var v = raw.Trim();
            if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase)) v = v.Substring(1);
            int dash = v.IndexOf('-');
            if (dash > 0) v = v.Substring(0, dash);
            v = v.Trim();
            if (v.Length == 0 || v.Equals("unknown", StringComparison.OrdinalIgnoreCase)) return null;
            // A locally built PPSSPP reports 1.0.0.1; it means "not a CI build", not version 1.0.
            if (v == "1.0.0.1") return null;
            return v;
        }

        public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions()
        {
            var release = GitHubReleases.GetLatest(Repo);
            if (release == null) { Log.Warn("no release information for " + Repo); return null; }

            var assets = GitHubReleases.SelectAssets(release, AssetRequiredX64, AssetExcluded);
            if (assets.Count == 0)
                assets = GitHubReleases.SelectAssets(release, AssetRequiredArm64, AssetExcluded);

            if (assets.Count == 0)
            {
                Log.Warn("release " + release.Tag + " has no Windows asset matching our filters; saw: "
                         + string.Join(", ", release.Assets.Select(a => a.Name)));
                return null;
            }
            if (assets.Count > 1)
            {
                // Don't guess which build the user wants — say so and offer them all.
                Log.Warn("release " + release.Tag + " matched " + assets.Count + " assets: "
                         + string.Join(", ", assets.Select(a => a.Name)));
            }

            string label = (release.Tag ?? "").TrimStart('v', 'V');
            return assets
                .Select(a => new EmulatorControllerVersion(a.DownloadUrl, label, a.Name))
                .ToList();
        }

        /// <summary>Compare installed against latest semantically. The SDK's default compares the two
        /// strings and calls anything unequal — including a null current version — an update, which
        /// nags the user forever on a build we cannot read.</summary>
        public override bool IsUpdateAvailable(string emulatorAppPath, out EmulatorControllerVersion version)
        {
            version = null;
            try
            {
                version = GetInstallableVersions()?.FirstOrDefault();
                if (version == null) return false;                      // couldn't ask; don't nag
                if (string.IsNullOrWhiteSpace(emulatorAppPath)) return true;   // nothing installed

                var current = GetCurrentVersion(emulatorAppPath);
                if (current == null) return false;                      // unreadable; don't nag
                return CompareVersions(current, version.Label) < 0;
            }
            catch (Exception ex) { Log.Warn("update check failed", ex); return false; }
        }

        /// <summary>Numeric, component-wise, tolerant of a missing third component: PPSSPP tags both
        /// "1.20" and "1.20.1". Returns &lt;0 when <paramref name="a"/> is older.</summary>
        internal static int CompareVersions(string a, string b)
        {
            int[] pa = Parts(a), pb = Parts(b);
            for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
            {
                int va = i < pa.Length ? pa[i] : 0;
                int vb = i < pb.Length ? pb[i] : 0;
                if (va != vb) return va.CompareTo(vb);
            }
            return 0;

            static int[] Parts(string s) => (s ?? "")
                .Split('.')
                .Select(p => int.TryParse(p, out var n) ? n : 0)
                .ToArray();
        }

        // ── installation ─────────────────────────────────────────────────────

        public override EmulatorInstallResponse InstallEmulator(InstallEmulatorArgs args)
        {
            string archive = null;
            try
            {
                // args.Version carries the Identifier we handed out in GetInstallableVersions: the
                // asset's download URL, not a version number.
                string url = args?.Version;
                string label = null;
                if (string.IsNullOrWhiteSpace(url))
                {
                    var latest = GetInstallableVersions()?.FirstOrDefault();
                    if (latest == null)
                        return new EmulatorInstallResponse(
                            "Couldn't determine which version of PPSSPP to install. GitHub may be rate "
                            + "limiting this machine; try again in a few minutes.");
                    url = latest.Identifier;
                    label = latest.Label;
                }

                bool reinstall = args?.ExistingEmulator != null;
                string targetDir = reinstall
                    ? Path.GetDirectoryName(ResolveFullPath(args.ExistingEmulator.ApplicationPath))
                    : Path.Combine(LaunchBoxRoot(), "Emulators", "PPSSPP");

                if (string.IsNullOrWhiteSpace(targetDir))
                    return new EmulatorInstallResponse("Couldn't work out where to install PPSSPP.");

                Report(args, "Downloading PPSSPP...", 0);
                archive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                GitHubReleases.Download(url, archive,
                    p => Report(args, "Downloading PPSSPP...", p),
                    () => { try { return args?.ShouldCancelFunc?.Invoke() ?? false; } catch { return false; } });

                Report(args, "Extracting PPSSPP...", null);
                Directory.CreateDirectory(targetDir);

                // Extract OVER the folder; never clear it first. In a portable install — which is what
                // this produces — memstick\ lives inside targetDir, so wiping it would destroy the
                // user's saves, save states and configuration. The cost is that files dropped between
                // releases linger; that is the right trade.
                ExtractOver(archive, targetDir);

                string exe = PpssppPaths.FindExecutable(targetDir);
                if (exe == null)
                    return new EmulatorInstallResponse(
                        "PPSSPP was downloaded and extracted to " + targetDir
                        + " but no PPSSPP executable was found in it.");

                // No post-install step: PPSSPP is portable as long as installed.txt is absent, and the
                // release zip does not contain one. (DuckStation, PCSX2 and Dolphin all need a marker
                // file written here; PPSSPP does not.)
                var layout = PpssppPaths.Resolve(exe);
                Log.Info("installed to " + targetDir + " — memstick: " + layout.MemStickDir
                         + " (" + layout.Reason + ")");

                if (reinstall)
                {
                    try { args.ExistingEmulator.ApplicationPath = MakeRelativeToLaunchBox(exe); } catch { }
                    return new EmulatorInstallResponse(args.ExistingEmulator, "PPSSPP updated.");
                }

                if (args != null && !args.ShouldCreateEmulator)
                    return new EmulatorInstallResponse(
                        "PPSSPP was installed to " + targetDir + ", but no emulator entry was requested.");

                var created = CreateEmulator(exe, label);
                if (created == null)
                    return new EmulatorInstallResponse(
                        "PPSSPP was installed to " + targetDir
                        + " but the emulator entry couldn't be created (no data manager available).");

                return new EmulatorInstallResponse(created, "PPSSPP installed.");
            }
            catch (OperationCanceledException)
            {
                return new EmulatorInstallResponse("Installation cancelled.");
            }
            catch (Exception ex)
            {
                Log.Warn("install failed", ex);
                return new EmulatorInstallResponse("Failed to install PPSSPP: " + ex.Message);
            }
            finally
            {
                try { if (archive != null && File.Exists(archive)) File.Delete(archive); } catch { }
            }
        }

        /// <summary>Unpack every entry over the destination, overwriting. Rejects entries whose path
        /// escapes the destination (zip slip) rather than trusting the archive.</summary>
        private static void ExtractOver(string archivePath, string destinationDir)
        {
            string root = Path.GetFullPath(destinationDir);
            using (var zip = ZipFile.OpenRead(archivePath))
            {
                foreach (var entry in zip.Entries)
                {
                    string target = Path.GetFullPath(Path.Combine(root, entry.FullName));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Archive entry escapes the destination: " + entry.FullName);

                    if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\") || entry.Name.Length == 0)
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, overwrite: true);
                }
            }
        }

        /// <summary>Create the LaunchBox emulator entry through the PUBLIC data manager, and give it the
        /// PSP platform. Returns null when no data manager is available (a probe harness, say).</summary>
        private static IEmulator CreateEmulator(string exePath, string versionLabel)
        {
            var dm = PluginHelper.DataManager;
            if (dm == null) return null;

            var emu = dm.AddNewEmulator();
            emu.Title = "PPSSPP";
            emu.ApplicationPath = MakeRelativeToLaunchBox(exePath);
            emu.CommandLine = DefaultCommandLine;
            emu.DefaultPlatform = PspPlatform;

            var platform = emu.AddNewEmulatorPlatform();
            platform.Platform = PspPlatform;
            platform.IsDefault = true;

            try { dm.Save(false); } catch (Exception ex) { Log.Warn("data manager save failed", ex); }
            Log.Info("created emulator entry \"PPSSPP\""
                     + (versionLabel != null ? " (" + versionLabel + ")" : "")
                     + " -> " + emu.ApplicationPath);
            return emu;
        }

        // ── BIOS ─────────────────────────────────────────────────────────────

        // PPSSPP needs NO BIOS or firmware, ever. It high-level-emulates the BIOS and the PSP's whole
        // operating system; its own FAQ says so, and LaunchBox's metadata database agrees
        // (RequiredBiosFile is null for the PPSSPP/Sony PSP row). The assets\flash0\font folder in the
        // install is not firmware — it holds 18 freely redistributable replacement .pgf fonts, shipped
        // with the emulator, and a user who dumped their own PSP fonts may optionally drop them in
        // <memstick>\PSP\flash0\font for fidelity. That is cosmetic. Do not "fix" this to return files.
        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string platform)
            => Array.Empty<EmulatorBiosFile>();

        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(
            string emulatorApplicationPath, string platform, string commandLine)
            => Array.Empty<EmulatorBiosFile>();

        // ── RetroAchievements ────────────────────────────────────────────────

        private const string CheevoSection = "Achievements";
        private const string KeyEnable = "AchievementsEnable";
        private const string KeyHardcore = "AchievementsChallengeMode";   // yes: "ChallengeMode" IS hardcore
        private const string KeyUser = "AchievementsUserName";
        private const string KeyToken = "AchievementsToken";

        public override RetroAchievementSupportResponse SupportsRetroAchievements(string emulatorApplicationPath)
        {
            try
            {
                var layout = PpssppPaths.Resolve(emulatorApplicationPath);
                var ini = PpssppIni.Read(layout.ConfigFile, CheevoSection,
                                         KeyEnable, KeyHardcore, KeyUser, KeyToken);

                ini.TryGetValue(KeyEnable, out var enable);
                ini.TryGetValue(KeyHardcore, out var hardcore);
                ini.TryGetValue(KeyUser, out var user);

                // The token is NOT normally in the ini (see InjectRetroAchievementsCredentials); the
                // authoritative copy is a separate file. Report whether one exists, never its content.
                bool hasToken = SafeExists(layout.RetroAchievementsTokenFile)
                                || (ini.TryGetValue(KeyToken, out var t) && !string.IsNullOrEmpty(t));

                return new RetroAchievementSupportResponse(
                    isSupported: true,
                    isEnabled: PpssppIni.AsBool(enable, false),
                    // PPSSPP defaults hardcore ON when achievements are enabled.
                    isHardcore: PpssppIni.AsBool(hardcore, true),
                    username: user,
                    token: hasToken ? "" : null);
            }
            catch (Exception ex)
            {
                Log.Warn("could not read the RetroAchievements state", ex);
                return new RetroAchievementSupportResponse(isSupported: true);
            }
        }

        /// <summary>Write RetroAchievements credentials where PPSSPP will actually read them.
        ///
        /// This needs TWO destinations, and getting it wrong looks like it works. PPSSPP marks
        /// AchievementsToken as DONT_SAVE: it reads the key from ppsspp.ini if it is there, but never
        /// writes it back — deliberately, so a user can post their ini for debugging without leaking a
        /// credential. The copy PPSSPP maintains lives in PSP\SYSTEM\ppsspp_retroachievements.dat as
        /// raw bytes. So a token written only to the ini survives exactly until PPSSPP next exits and
        /// rewrites the file.</summary>
        public override PluginResponse InjectRetroAchievementsCredentials(InjectRetroAchievementsCredentialsArgs args)
        {
            try
            {
                if (args == null) return new PluginResponse(false, "No credentials supplied.");
                var layout = PpssppPaths.Resolve(args.EmulatorApplicationPath);

                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(args.Username)) values[KeyUser] = args.Username;
                if (args.IsEnabled.HasValue) values[KeyEnable] = PpssppIni.FromBool(args.IsEnabled.Value);
                if (args.HardcoreEnabled.HasValue) values[KeyHardcore] = PpssppIni.FromBool(args.HardcoreEnabled.Value);

                string error = PpssppIni.Write(layout.ConfigFile, CheevoSection, values);
                if (error != null) return new PluginResponse(false, error);

                if (!string.IsNullOrEmpty(args.Token))
                {
                    // Raw bytes, no encoding, no newline — this is exactly what PPSSPP's NativeSaveSecret
                    // writes and what it reads back.
                    PpssppIni.WriteAtomicBytes(layout.RetroAchievementsTokenFile,
                                               System.Text.Encoding.UTF8.GetBytes(args.Token));
                }

                // Never log the token, and never log the .dat path's contents.
                Log.Info("RetroAchievements credentials written for user \"" + (args.Username ?? "") + "\"");
                return new PluginResponse(true);
            }
            catch (Exception ex)
            {
                Log.Warn("could not write the RetroAchievements credentials", ex);
                return new PluginResponse(false, ex.Message);
            }
        }

        // ── launch ───────────────────────────────────────────────────────────

        /// <summary>Called right before the spawn. Its only job is carrying the host's
        /// RetroAchievements credentials into PPSSPP's configuration. It must NEVER fail the launch:
        /// a user who cannot log in to RetroAchievements still wants to play the game.</summary>
        public override PrepareForLaunchResponse PrepareEmulatorForLaunch(PrepareForLaunchArgs args)
        {
            try
            {
                if (args?.RetroAchievementCredentials != null)
                {
                    var creds = args.RetroAchievementCredentials.Value;
                    bool hardcore = true;
                    try { hardcore = args.EmulatorBeingLaunched?.EnableHardcoreAchievements ?? true; } catch { }

                    var response = InjectRetroAchievementsCredentials(
                        new InjectRetroAchievementsCredentialsArgs(
                            args.EmulatorBeingLaunched?.ApplicationPath,
                            creds.Username, creds.Token, enable: true, hardcore: hardcore));

                    if (!response.WasSuccess)
                        Log.Warn("RetroAchievements credentials were not applied: "
                                 + (response.Message ?? "no reason given") + " — launching anyway");
                }
            }
            catch (Exception ex) { Log.Warn("PrepareEmulatorForLaunch", ex); }

            // Command line untouched: LaunchBox's own default for PPSSPP is right, and it survives the
            // command-line rewrite upstream has queued for 1.21.
            return new PrepareForLaunchResponse(success: true);
        }

        // ── save management: not yet ─────────────────────────────────────────

        // PSP saves are directories under <memstick>\PSP\SAVEDATA\<DISC_ID><suffix>\, titled by
        // PARAM.SFO and illustrated by ICON0.PNG; save states are
        // <memstick>\PSP\PPSSPP_STATE\<DISC_ID>_<DISC_VERSION>_<slot>.ppst with a sibling .jpg. All of
        // that is well understood, but none of it is implemented, and reporting support without the
        // implementation would show the user an empty saves page.

        // ── paths ────────────────────────────────────────────────────────────

        /// <summary>The LaunchBox installation root. LaunchBox's own plugins read NamingHelper.RootFolder
        /// from the core; with the public SDK only, we walk up from this assembly — a plugin lives at
        /// &lt;LB&gt;\Plugins\&lt;Name&gt;\ or &lt;LB&gt;\System\Plugins\&lt;Name&gt;\ — until we find a
        /// folder that looks like a LaunchBox root.</summary>
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

            // Fall back to the current directory: under both hosts the process runs from Core.
            try { return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..")); }
            catch { return Environment.CurrentDirectory; }
        }

        /// <summary>LaunchBox stores ApplicationPath relative to its own root when the file lives under
        /// it, which is what keeps a library portable. Match that.</summary>
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

        private static bool SafeExists(string p)
        {
            try { return File.Exists(p); } catch { return false; }
        }
    }
}
