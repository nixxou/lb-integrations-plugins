// Xenia integration for LaunchBox / LiteBox.
//
// Claim the emulator, report and install versions, and fix the two launch defaults that cost users
// the most. Save management lives next door in XeniaSaves.cs.
//
// Like the PPSSPP plugin, this talks ONLY to the public SDK - no reference to any LaunchBox core
// assembly - so it behaves identically under LaunchBox and under LiteBox. Every entry point is
// defensive: the host swallows exceptions silently, which turns a bug into a feature that quietly
// does nothing, so we catch, log and degrade instead.
//
// Two forks ship. Canary is the live one (master has been frozen since February 2026 and xenia.jp
// labels it inactive), and they differ in more than their name: different executable, different
// config file, different default for portable mode, and a different content tree. We CLAIM both and
// READ saves from both; we only INSTALL canary.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;
using LbIntegrations.Catalog;
using LbIntegrations.Lbip;

namespace LbIntegrations.Xenia
{
    public partial class XeniaPlugin : EmulatorPlugin, ISystemEventsPlugin, ILbCatalogSource
    {
        private const string CanaryRepo = "xenia-canary/xenia-canary";
        private const string Xbox360Platform = "Microsoft Xbox 360";

        private const string DefaultCommandLine = "--fullscreen";

        // The Windows asset has been renamed twice since 2026 (xenia_canary.zip ->
        // xenia_canary_windows.zip -> xenia_canary_windows.7z), so match on substrings rather than on a
        // template, and do not constrain the extension: the extractor picks its reader by content.
        private static readonly string[] AssetRequired = { "xenia_canary", "windows" };
        private static readonly string[] AssetExcluded = { "linux", "appimage", "symbols", "pdb" };

        // Traced because a plugin is a black box inside LaunchBox: the only way to tell "the host
        // never asked us" from "we answered badly" is to see which members it actually calls.
        public XeniaPlugin()
        {
            Log.Info("plugin constructed, assembly " + typeof(XeniaPlugin).Assembly.Location);

            // THE SHARED ROW INJECTION LEARNS WHOSE PLUGIN IT IS IN. It is compiled into all
            // five plugins and cannot tell on its own which log file to write to, nor which kill
            // switches to read. See LbipLog.
            LbipLog.Use(Log.Info, Log.Warn, Log.Disabled, () => Log.Tracing);

            // As early as possible: the patch only sees connections opened AFTER it is installed,
            // and LaunchBox reads its metadata the moment a window asks for it.
            LbipRowInjection.Install("com.nixxou.lbip.xenia", MetadataRows());
        }

        /// <summary>The name this pack publishes under, in one place so its three uses cannot
        /// disagree: the row in LaunchBox's emulator catalogue, the entry Add Emulator offers, and
        /// the title given to an emulator this plugin creates.
        ///
        /// UNLIKE Flycast, melonDS and no$gba, LaunchBox HAS a Xenia row of its own. Ours does not
        /// replace it and must not: the two sit side by side in the Add Emulator window, theirs
        /// plain and ours carrying this integration. The prefix is what tells them apart, and it is
        /// why the name is not simply "Xenia" - Emulators.Name is that table's primary key, so one
        /// name can only ever mean one row.</summary>
        private const string PackName = "Nixx-Xenia";

        /// <summary>What LaunchBox's emulator metadata should say about this pack's Xenia, published
        /// through the row injection rather than written into their database - see
        /// LbipRowInjection.
        ///
        /// Every value here is one this plugin already answers elsewhere: the platform is
        /// Xbox360Platform and the command line is DefaultCommandLine. That command line is the
        /// difference worth having - LaunchBox's own row carries the literal string "None", which is
        /// what a user then has to fix by hand. The extensions and AutoExtract are left exactly as
        /// theirs: this plugin has measured nothing that says otherwise, and inventing capability a
        /// user would discover was wrong is worse than repeating what is already there.</summary>
        private static IEnumerable<LbCatalogEmulator> MetadataRows()
        {
            const string extensions = ".iso";

            yield return new LbCatalogEmulator
            {
                Name = PackName,
                CommandLine = DefaultCommandLine,
                ApplicableFileExtensions = extensions,
                Url = "https://xenia.jp/",
                BinaryFileName = XeniaPaths.ExecutableNames[0],
                AutoExtract = true,
                Platforms =
                {
                    new LbCatalogPlatform { Platform = Xbox360Platform,
                                          ApplicableFileExtensions = extensions,
                                          Recommended = true },
                },
            };
        }

        public override string EmulatorName => PackName;

        /// <summary>What this plugin brings to a host's emulator catalogue, for a host that asks
        /// rather than one whose database has to be patched. Same rows either way - see
        /// MetadataRows.</summary>
        public IEnumerable<LbCatalogEmulator> EmulatorRows() => MetadataRows();

        // -- the host is up ------------------------------------------------

        /// <summary>Try again to install the metadata patch. The constructor is the earliest
        /// moment, which is what we want, but it may be TOO early: the patch needs
        /// Microsoft.Data.Sqlite to be loaded already, and assemblies load on first use - measured,
        /// the probe constructs a plugin before anything has touched SQLite and the patch declines.
        /// Install is a no-op once it has succeeded, so retrying here costs nothing and removes the
        /// dependency on load order.
        ///
        /// Three event names are accepted because the two hosts do not raise the same one:
        /// LaunchBox raises LaunchBoxStartupCompleted (and BigBox its own), LiteBox raises
        /// PluginInitialized when its window is shown. Anything else is ignored in silence - a
        /// plugin that logs every SelectionChanged would drown its own log.</summary>
        public void OnEventRaised(string eventType)
        {
            try
            {
                if (eventType != SystemEventTypes.PluginInitialized
                    && eventType != SystemEventTypes.LaunchBoxStartupCompleted
                    && eventType != SystemEventTypes.BigBoxStartupCompleted) return;

                Log.Info("host event " + Q(eventType));
                LbipRowInjection.Install("com.nixxou.lbip.xenia", MetadataRows());
            }
            catch (Exception ex) { Log.Warn("OnEventRaised", ex); }
        }

        private static string Q(string text) { return "\"" + text + "\""; }

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
                if (!XeniaPaths.IsXeniaExecutable(path)) continue;
                claimed.Add(emu);

                // Undo what earlier versions wrote - see the note in InstallEmulator.
                try
                {
                    if (string.Equals(emu.DefaultPlatform, Xbox360Platform,
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

        /// <summary>Describe Xenia's keys in the emulator's AutoHotkey fields - what the pause screen
        /// of LaunchBox and BigBox sends.
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
                if (Fill(() => emu.AutoHotkeyScript, v => emu.AutoHotkeyScript = v, XeniaAhk.Running))
                    set.Add("running");
                if (Fill(() => emu.ExitAutoHotkeyScript, v => emu.ExitAutoHotkeyScript = v, XeniaAhk.Exit))
                    set.Add("exit");
                if (Fill(() => emu.SaveStateAutoHotkeyScript,
                         v => emu.SaveStateAutoHotkeyScript = v, XeniaAhk.SaveState))
                    set.Add("save");
                if (Fill(() => emu.LoadStateAutoHotkeyScript,
                         v => emu.LoadStateAutoHotkeyScript = v, XeniaAhk.LoadState))
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
            bool supported = string.Equals((platform ?? "").Trim(), Xbox360Platform,
                                           StringComparison.InvariantCultureIgnoreCase);
            Log.Verbose("IsPlatformSupported(\"" + platform + "\") -> " + supported);
            return new EmulatorSupportResponse(supported, supported);
        }

        // ── versions ─────────────────────────────────────────────────────────

        /// <summary>The installed build, or null - and null is an ordinary answer here, not a failure.
        ///
        /// Xenia has NO Win32 version resource (main_resources.rc holds an icon and nothing else) and
        /// NO --version flag; --help opens a MODAL DIALOG in a windowed app and must never be invoked
        /// from a plugin. What exists is a build string compiled into the binary, "branch@sha on date",
        /// which we read straight out of the file. For canary the "version" is a 7-character git SHA.</summary>
        public override string GetCurrentVersion(string applicationPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(applicationPath) || !File.Exists(applicationPath)) return null;
                var build = XeniaPaths.BuildStringOf(applicationPath);
                if (build == null) { Log.Info("no build string in " + Path.GetFileName(applicationPath)); return null; }
                // "canary_experimental@74c4e4a" -> "74c4e4a", which is what the release tag holds.
                int at = build.IndexOf('@');
                return at >= 0 && at + 1 < build.Length ? build.Substring(at + 1) : build;
            }
            catch (Exception ex) { Log.Warn("could not read the version of " + applicationPath, ex); return null; }
        }

        /// <summary>Canary's tags are git SHAs - NOT sortable as strings, so the newest release is the
        /// one published last, never the one that sorts highest. GitHubReleases asks for /latest, which
        /// GitHub answers by publication date.</summary>
        public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions()
        {
            Log.Info("GetInstallableVersions: asked");
            var release = GitHubReleases.GetLatest(CanaryRepo);
            if (release == null) { Log.Warn("no release information for " + CanaryRepo); return null; }

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

            string label = release.Tag ?? "";
            return assets.Select(a => new EmulatorControllerVersion(a.DownloadUrl, label, a.Name)).ToList();
        }

        /// <summary>A SHA says nothing about order, so "is there an update" becomes "is the latest tag a
        /// different SHA from the installed one". An unreadable install answers no rather than nagging.</summary>
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
                string url = args?.Version;
                string label = null;
                if (string.IsNullOrWhiteSpace(url))
                {
                    var latest = GetInstallableVersions()?.FirstOrDefault();
                    if (latest == null)
                        return new EmulatorInstallResponse(
                            "Couldn't determine which build of Xenia to install. GitHub may be rate "
                            + "limiting this machine; try again in a few minutes.");
                    url = latest.Identifier;
                    label = latest.Label;
                }

                bool reinstall = args?.ExistingEmulator != null;
                string targetDir = reinstall
                    ? Path.GetDirectoryName(ResolveFullPath(args.ExistingEmulator.ApplicationPath))
                    : Path.Combine(LaunchBoxRoot(), "Emulators", "Xenia");
                if (string.IsNullOrWhiteSpace(targetDir))
                    return new EmulatorInstallResponse("Couldn't work out where to install Xenia.");

                Report(args, "Downloading Xenia...", 0);
                archive = Path.Combine(Path.GetTempPath(),
                                       Guid.NewGuid().ToString("N") + SafeExtension(url));
                GitHubReleases.Download(url, archive,
                    p => Report(args, "Downloading Xenia...", p),
                    () => { try { return args?.ShouldCancelFunc?.Invoke() ?? false; } catch { return false; } });

                Report(args, "Extracting Xenia...", null);
                Directory.CreateDirectory(targetDir);
                // Extract OVER, never clear first: a portable canary keeps content\, its config and the
                // user's saves inside this very folder.
                Archives.ExtractOver(archive, targetDir);

                string exe = XeniaPaths.FindExecutable(targetDir);
                if (exe == null)
                    return new EmulatorInstallResponse(
                        "Xenia was downloaded and extracted to " + targetDir
                        + " but no Xenia executable was found in it.");

                var layout = XeniaPaths.Resolve(exe);
                Log.Info("installed to " + targetDir + " - storage root: " + layout.StorageRoot
                         + " (" + layout.Reason + ")");

                if (reinstall)
                {
                    try { args.ExistingEmulator.ApplicationPath = MakeRelativeToLaunchBox(exe); } catch { }
                    return new EmulatorInstallResponse(args.ExistingEmulator, "Xenia updated.");
                }
                if (args != null && !args.ShouldCreateEmulator)
                    return new EmulatorInstallResponse(
                        "Xenia was installed to " + targetDir + ", but no emulator entry was requested.");

                var created = CreateEmulator(exe, label);
                if (created == null)
                    return new EmulatorInstallResponse(
                        "Xenia was installed to " + targetDir
                        + " but the emulator entry couldn't be created (no data manager available).");
                return new EmulatorInstallResponse(created, "Xenia installed.");
            }
            catch (OperationCanceledException) { return new EmulatorInstallResponse("Installation cancelled."); }
            catch (Exception ex)
            {
                Log.Warn("install failed", ex);
                return new EmulatorInstallResponse("Failed to install Xenia: " + ex.Message);
            }
            finally
            {
                try { if (archive != null && File.Exists(archive)) File.Delete(archive); } catch { }
            }
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
            // duplicate is saved if the user clicks OK. In a real library the only three emulators with
            // the field set were the two of ours that wrote it and a hand-made RetroArch copy, and
            // that copy already carried its duplicate on disk. Every emulator with the field empty
            // was clean.
            //
            // None of Unbroken's own plugins set it either - Xemu, the one native plugin that assigns
            // its properties by hand rather than reading them from the metadata, sets fifteen fields
            // and not this one. What actually matters is IsDefault on each platform row, which says
            // "this emulator is the default FOR that platform", and we do set that.

            var platform = emu.AddNewEmulatorPlatform();
            platform.Platform = Xbox360Platform;
            platform.IsDefault = true;

            try { dm.Save(false); } catch (Exception ex) { Log.Warn("data manager save failed", ex); }
            Log.Info("created emulator entry \"Xenia\""
                     + (versionLabel != null ? " (" + versionLabel + ")" : "") + " -> " + emu.ApplicationPath);
            return emu;
        }

        // ── BIOS ─────────────────────────────────────────────────────────────

        // Xenia needs no BIOS or firmware of any kind.
        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string platform)
            => Array.Empty<EmulatorBiosFile>();

        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(
            string emulatorApplicationPath, string platform, string commandLine)
            => Array.Empty<EmulatorBiosFile>();

        // ── RetroAchievements ────────────────────────────────────────────────

        /// <summary>Neither fork supports RetroAchievements. Canary has NATIVE Xbox 360 achievements,
        /// written into the profile's .gpd files by a GPD-only backend - unrelated machinery that this
        /// plugin cannot carry credentials into. Answering "not supported" is honest; claiming support
        /// and silently doing nothing would be worse.</summary>
        public override RetroAchievementSupportResponse SupportsRetroAchievements(string emulatorApplicationPath)
            => new RetroAchievementSupportResponse(isSupported: false);

        // ── launch ───────────────────────────────────────────────────────────

        /// <summary>Correct the two defaults that cost users the most, on the command line.
        ///
        /// Writing them into the TOML would be pointless: Xenia regenerates that file from its cvar
        /// registry on startup AND on exit, so anything we put there is erased. The command line wins
        /// at runtime and is the only durable route.
        ///
        ///   license_mask=1   the default 0 means "no licenses", which boots many XBLA titles in trial
        ///                    mode and makes owned DLC look absent - the single most common Xenia
        ///                    support question.
        ///   discord=false    a frontend launching games should not broadcast them.
        ///
        /// Both are written as --flag=value. cxxopts bools must be --flag or --flag=value: the
        /// space-separated form would leave a stray "true" to be swallowed by the positional argument,
        /// which is the ROM path, and the launch would fail in a way that looks like a bad ROM.
        ///
        /// Anything the user already set is left alone.</summary>
        public override PrepareForLaunchResponse PrepareEmulatorForLaunch(PrepareForLaunchArgs args)
        {
            try
            {
                var current = args?.CurrentCommandLine ?? "";
                var added = new List<string>();
                if (!HasOption(current, "license_mask")) added.Add("--license_mask=1");
                if (!HasOption(current, "discord")) added.Add("--discord=false");

                if (added.Count > 0)
                {
                    var rewritten = (current.Trim() + " " + string.Join(" ", added)).Trim();
                    return new PrepareForLaunchResponse(success: true) { NewCommandLine = rewritten };
                }
            }
            catch (Exception ex) { Log.Warn("PrepareEmulatorForLaunch", ex); }
            return new PrepareForLaunchResponse(success: true);
        }

        private static bool HasOption(string commandLine, string name)
            => !string.IsNullOrEmpty(commandLine)
               && commandLine.IndexOf("--" + name, StringComparison.OrdinalIgnoreCase) >= 0;

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
