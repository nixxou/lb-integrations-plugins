// SUPER ZSNES integration for LaunchBox / LiteBox.
//
// Claim the emulator, report and install versions, and describe it to the host's catalogue. That
// is the whole of it for now, deliberately. SUPER ZSNES is closed source - a Unity player compiled
// by IL2CPP - so what the other plugins of this pack read out of an emulator's code had to be
// measured off its build here: the string table of its metadata, the text of its scene, the site
// it updates itself from. What could not be measured that way (where it keeps its settings, whether
// it takes a ROM on the command line, which keys it binds by default, where a save goes once the
// user has set a path) is left OUT rather than guessed. README.md, "Notes on SUPER ZSNES", says
// what was measured, how, and what a run on a real machine still has to answer.
//
// Like every plugin here, this talks ONLY to the public SDK - no reference to any LaunchBox core
// assembly - so it behaves identically under LaunchBox and under LiteBox. Every entry point is
// defensive: the host swallows exceptions silently, which turns a bug into a feature that quietly
// does nothing, so we catch, log and degrade instead.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;
using LbIntegrations.Catalog;
using LbIntegrations.Lbip;

namespace LbIntegrations.SuperZsnes
{
    public partial class SuperZsnesPlugin : EmulatorPlugin, ISystemEventsPlugin, ILbCatalogSource
    {
        private const string SnesPlatform = "Super Nintendo Entertainment System";

        /// <summary>Nothing. Whether SUPER ZSNES takes a ROM path as its first argument is exactly
        /// what a run on a real machine has to measure (see README); the host appends the ROM path
        /// to this line either way, and adding a flag nobody has seen work would be inventing one.</summary>
        private const string DefaultCommandLine = "";

        /// <summary>Read off the emulator's own extension table in global-metadata.dat, where the
        /// ROM types sit in one sorted run: ".sav .sfc .smc .spc .srm .swc ... .zip". The SNES images
        /// among them are these four; .spc is music, .srm and .sav are saves, and .fig is absent.
        /// .7z is not in the table and is not declared.</summary>
        private const string Extensions = ".sfc; .smc; .swc; .zip";

        /// <summary>The name this pack publishes under, in one place so its four uses cannot
        /// disagree: the catalogue row, the Add Emulator entry, the title of an emulator this plugin
        /// creates, and THE FOLDER THE EMULATOR IS INSTALLED INTO. Prefixed like the rest of the pack
        /// - README, "While developing" - and spelt the way the download is (SuperZSNES_v0.310.zip)
        /// rather than the way the executable is (SUPERZSNES.exe).</summary>
        private const string PackName = "Nixx-SuperZSNES";

        public SuperZsnesPlugin()
        {
            Log.Info("plugin constructed, assembly " + typeof(SuperZsnesPlugin).Assembly.Location);

            // THE SHARED ROW INJECTION LEARNS WHOSE PLUGIN IT IS IN - see LbipLog.
            LbipLog.Use(Log.Info, Log.Warn, Log.Disabled, () => Log.Tracing);

            // As early as possible: the patch only sees connections opened AFTER it is installed.
            LbipRowInjection.Install("com.nixxou.lbip.superzsnes", MetadataRows());

            // When an import's games are in the library: told - and one watcher for the whole pack, see
            // LbipImportWatch. Under a try: LbImportFinished is newer than a Catalog a host may carry.
            try { ListenForImports(); }
            catch (Exception ex) { Log.Info("an import's end is not seen here (" + ex.GetType().Name + ": " + ex.Message + ")"); }
        }

        /// <summary>NOT INLINED, and called under a try: named in the constructor, a type a host's older
        /// Catalog lacks would fail the constructor itself; named here, it fails this call alone.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void ListenForImports()
        {
            LbIntegrations.Catalog.LbImportFinished.Register(new SuperZsnesImportFinished());
            LbipImportWatch.Install();
        }

        /// <summary>What LaunchBox's emulator metadata should say about SUPER ZSNES. Their database
        /// has a row for the ZSNES of 2007 and nothing for this one - a different program under a
        /// related name, closed source, Unity, no shared file - so there is nothing to sit beside.
        ///
        /// AutoExtract is false because the emulator opens .zip itself: .zip is in its own ROM
        /// extension table and its file browser offers it as a game. A host that unpacked the archive
        /// first would hand it a folder for no gain.</summary>
        private static IEnumerable<LbCatalogEmulator> MetadataRows()
        {
            yield return new LbCatalogEmulator
            {
                Name = PackName,
                CommandLine = DefaultCommandLine,
                ApplicableFileExtensions = Extensions,
                Url = SuperZsnesSite.Home,
                BinaryFileName = SuperZsnesPaths.ExecutableName,
                AutoExtract = false,
                Platforms =
                {
                    new LbCatalogPlatform { Platform = SnesPlatform,
                                          ApplicableFileExtensions = Extensions,
                                          Recommended = true },
                },
            };
        }

        public override string EmulatorName => PackName;

        /// <summary>For a host that asks rather than one whose database has to be patched.</summary>
        public IEnumerable<LbCatalogEmulator> EmulatorRows() => MetadataRows();

        // -- the host is up ------------------------------------------------

        /// <summary>Try again to install the metadata patch once the host is up - the constructor
        /// may run before Microsoft.Data.Sqlite is loaded. Install is a no-op once it has succeeded.
        /// See the same member in XeniaPlugin for the three event names.</summary>
        public void OnEventRaised(string eventType)
        {
            try
            {
                if (eventType != SystemEventTypes.PluginInitialized
                    && eventType != SystemEventTypes.LaunchBoxStartupCompleted
                    && eventType != SystemEventTypes.BigBoxStartupCompleted) return;

                Log.Info("host event \"" + eventType + "\"");
                LbipRowInjection.Install("com.nixxou.lbip.superzsnes", MetadataRows());
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
                if (!SuperZsnesPaths.IsSuperZsnesExecutable(path)) continue;
                claimed.Add(emu);
                EnsureHotkeyScripts(emu);
            }
            // The host asks this constantly and the answer almost never changes. Said once.
            var signature = string.Join("|", claimed.Select(e => { try { return e.Title; } catch { return "?"; } })
                                                    .OrderBy(t => t));
            if (signature != _lastClaimed)
            {
                _lastClaimed = signature;
                Log.Info("GetApplicableEmulators: claimed " + claimed.Count + " emulator(s)");
            }
            return claimed;
        }

        /// <summary>Describe the emulator's keys in the AutoHotkey fields the pause screen of
        /// LaunchBox and BigBox sends. Only a blank field is filled: a script the user wrote is his
        /// answer to the same question. Idempotent on the OBJECT, never on the executable - the host
        /// hands us the same emulator under a new object every time a window asks.</summary>
        private static void EnsureHotkeyScripts(IEmulator emu)
        {
            try
            {
                if (!IsBlank(() => emu.AutoHotkeyScript)
                    && !IsBlank(() => emu.ExitAutoHotkeyScript)
                    && !IsBlank(() => emu.SaveStateAutoHotkeyScript)
                    && !IsBlank(() => emu.LoadStateAutoHotkeyScript)) return;

                var set = new List<string>();
                if (Fill(() => emu.AutoHotkeyScript, v => emu.AutoHotkeyScript = v, SuperZsnesAhk.Running))
                    set.Add("running");
                if (Fill(() => emu.ExitAutoHotkeyScript, v => emu.ExitAutoHotkeyScript = v, SuperZsnesAhk.Exit))
                    set.Add("exit");
                if (Fill(() => emu.SaveStateAutoHotkeyScript,
                         v => emu.SaveStateAutoHotkeyScript = v, SuperZsnesAhk.SaveState))
                    set.Add("save");
                if (Fill(() => emu.LoadStateAutoHotkeyScript,
                         v => emu.LoadStateAutoHotkeyScript = v, SuperZsnesAhk.LoadState))
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
            bool supported = string.Equals((platform ?? "").Trim(), SnesPlatform,
                                           StringComparison.InvariantCultureIgnoreCase);
            Log.Verbose("IsPlatformSupported(\"" + platform + "\") -> " + supported);
            return new EmulatorSupportResponse(supported, supported);
        }

        // ── versions ─────────────────────────────────────────────────────────

        /// <summary>The installed build, digits only ("0.310"), or null - which is an ordinary
        /// answer, not a failure. The executable carries Unity's version and not the emulator's;
        /// the About text in the scene file does, and the stamp an install writes is the fallback.
        /// See SuperZsnesSite and SuperZsnesPaths.</summary>
        public override string GetCurrentVersion(string applicationPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(applicationPath) || !File.Exists(applicationPath)) return null;
                var fromScene = SuperZsnesPaths.InstalledVersion(applicationPath);
                if (fromScene != null) return fromScene;

                var stamped = SuperZsnesSite.Stamped(Path.GetDirectoryName(Path.GetFullPath(applicationPath)));
                Log.Info(stamped != null
                    ? "no About version in the scene files; the install stamp says " + stamped
                    : "no About version in the scene files and no install stamp beside " + Path.GetFileName(applicationPath));
                return stamped;
            }
            catch (Exception ex) { Log.Warn("could not read the version of " + applicationPath, ex); return null; }
        }

        /// <summary>One build: the Windows card on zsnes.com, or version.txt when the page cannot be
        /// read. The identifier is the download URL, the label the version, the third the file name.</summary>
        public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions()
        {
            Log.Info("GetInstallableVersions: asked");
            var build = SuperZsnesSite.Latest();
            if (build == null)
            {
                Log.Warn("zsnes.com gave no Windows download - unreachable, or the page and version.txt both changed shape");
                return null;
            }
            Log.Info("offering " + (build.Version ?? "latest") + " from " + build.Source + ": " + build.Url);
            return new List<EmulatorControllerVersion>
            {
                new EmulatorControllerVersion(build.Url, build.Version ?? "latest", build.FileName ?? "SuperZSNES.zip"),
            };
        }

        /// <summary>Newer on the site than on disk. An unreadable install answers no rather than
        /// nagging, and so does an unreachable site.</summary>
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
                return IsNewer(version.Label, current);
            }
            catch (Exception ex) { Log.Warn("update check failed", ex); return false; }
        }

        /// <summary>Is <paramref name="candidate"/> a later version than <paramref name="installed"/>?
        /// Numeric when both parse ("0.310" against "0.230"), so "1.0" after "0.999" is an update
        /// and "0.310" after "0.310" is not. A candidate that is not a version at all ("latest")
        /// answers NO: an update prompt that fires at every check because a file name changed shape
        /// is worse than one missed, and the log says so. Nothing installed is always an update.</summary>
        internal static bool IsNewer(string candidate, string installed)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            if (string.IsNullOrWhiteSpace(installed)) return true;
            if (!Version.TryParse(candidate.Trim(), out var c))
            {
                Log.Warn("the site's version \"" + candidate + "\" is not a number - not treating it as an update");
                return false;
            }
            if (!Version.TryParse(installed.Trim(), out var i))
                return !string.Equals(candidate.Trim(), installed.Trim(), StringComparison.OrdinalIgnoreCase);
            return c > i;
        }

        // ── installation ─────────────────────────────────────────────────────

        public override EmulatorInstallResponse InstallEmulator(InstallEmulatorArgs args)
        {
            string archive = null;
            try
            {
                string url = args?.Version;
                SuperZsnesBuild build = null;
                if (string.IsNullOrWhiteSpace(url))
                {
                    build = SuperZsnesSite.Latest();
                    if (build == null)
                        return new EmulatorInstallResponse(
                            "Couldn't find the SUPER ZSNES download on zsnes.com. The site may be down, "
                            + "or its Downloads section may have changed; try again later.");
                    url = build.Url;
                }
                else
                {
                    // The host hands back the identifier we gave it; the version is in the file name.
                    var m = Regex.Match(url, @"_v(?<v>\d+(?:\.\d+)+)", RegexOptions.IgnoreCase);
                    build = new SuperZsnesBuild
                    {
                        Url = url,
                        Version = m.Success ? m.Groups["v"].Value : null,
                        FileName = SafeFileName(url),
                        Source = "the version the host chose",
                    };
                }

                bool reinstall = args?.ExistingEmulator != null;
                string targetDir = reinstall
                    ? Path.GetDirectoryName(ResolveFullPath(args.ExistingEmulator.ApplicationPath))
                    : Path.Combine(LaunchBoxRoot(), "Emulators", PackName);
                if (string.IsNullOrWhiteSpace(targetDir))
                    return new EmulatorInstallResponse("Couldn't work out where to install SUPER ZSNES.");

                Report(args, "Downloading SUPER ZSNES...", 0);
                archive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + SafeExtension(url));
                SuperZsnesSite.Fetch(url, archive,
                    p => Report(args, "Downloading SUPER ZSNES...", p),
                    () => { try { return args?.ShouldCancelFunc?.Invoke() ?? false; } catch { return false; } });

                Report(args, "Extracting SUPER ZSNES...", null);
                Directory.CreateDirectory(targetDir);
                // Extract OVER, never clear first - see Archives.cs.
                Archives.ExtractOver(archive, targetDir);

                string exe = SuperZsnesPaths.FindExecutable(targetDir);
                if (exe == null)
                {
                    // The site itself warns that "the PC version may trigger a false trojan warning
                    // in certain virus scanners". A Data folder without its executable is what a
                    // scanner leaves behind, and saying so beats "no executable was found".
                    bool dataThere = Directory.Exists(Path.Combine(targetDir, SuperZsnesPaths.DataFolder));
                    return new EmulatorInstallResponse(
                        "SUPER ZSNES was downloaded and extracted to " + targetDir
                        + " but " + SuperZsnesPaths.ExecutableName + " is not there."
                        + (dataThere
                            ? " Its data folder is, so a virus scanner has most likely quarantined the "
                              + "executable - zsnes.com warns that some scanners flag it as a false positive. "
                              + "Restore it from the scanner's quarantine, or exclude the folder and install again."
                            : ""));
                }

                SuperZsnesSite.Stamp(Path.GetDirectoryName(exe), build);
                Log.Info("installed " + (build.Version ?? "?") + " to " + targetDir);

                // THE IN-PROCESS PLUGIN, unless the window said no. Its failure is a note in the
                // result and never a failed install: the emulator runs without it, as it ships.
                string note = "";
                if (SuperZsnesSettings.BepInExWanted)
                {
                    var deployed = SuperZsnesBepInEx.Deploy(exe, (m, p) => Report(args, m, p),
                        () => { try { return args?.ShouldCancelFunc?.Invoke() ?? false; } catch { return false; } });
                    foreach (var step in deployed.Steps) Log.Info("bepinex: " + step);
                    if (deployed.Ok) note = " BepInEx and the integration plugin are in place; the first start generates its interop assemblies and takes about a minute, silently.";
                    else { Log.Warn("bepinex: " + deployed.Problem); note = " BepInEx could not be installed (" + deployed.Problem + "); the emulator works without it, and the Nixx window can retry."; }
                }

                if (reinstall)
                {
                    try { args.ExistingEmulator.ApplicationPath = MakeRelativeToLaunchBox(exe); } catch { }
                    return new EmulatorInstallResponse(args.ExistingEmulator, "SUPER ZSNES updated." + note);
                }
                if (args != null && !args.ShouldCreateEmulator)
                    return new EmulatorInstallResponse(
                        "SUPER ZSNES was installed to " + targetDir + ", but no emulator entry was requested." + note);

                var created = CreateEmulator(exe, build.Version);
                if (created == null)
                    return new EmulatorInstallResponse(
                        "SUPER ZSNES was installed to " + targetDir
                        + " but the emulator entry couldn't be created (no data manager available)." + note);
                return new EmulatorInstallResponse(created, "SUPER ZSNES installed." + note);
            }
            catch (OperationCanceledException) { return new EmulatorInstallResponse("Installation cancelled."); }
            catch (Exception ex)
            {
                Log.Warn("install failed", ex);
                return new EmulatorInstallResponse("Failed to install SUPER ZSNES: " + ex.Message);
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

        private static string SafeFileName(string url)
        {
            try { return Uri.UnescapeDataString(Path.GetFileName(new Uri(url).AbsolutePath)); }
            catch { return null; }
        }

        private static IEmulator CreateEmulator(string exePath, string versionLabel)
        {
            var dm = PluginHelper.DataManager;
            if (dm == null) return null;

            var emu = dm.AddNewEmulator();
            emu.Title = PackName;
            emu.ApplicationPath = MakeRelativeToLaunchBox(exePath);
            emu.CommandLine = DefaultCommandLine;
            // THE MOUSE STAYS (Mehdi, 01/10): the emulator's menus, its file browser and its options are driven by the
            // mouse, and LaunchBox's "Hide mouse cursor during games" would take it away. Set on the entry this plugin
            // creates only; an entry the user made keeps his choice.
            try { emu.HideMouseCursorInGame = false; } catch (Exception ex) { Log.Info("could not leave the mouse cursor shown (" + ex.Message + ")"); }
            EnsureHotkeyScripts(emu);

            // DefaultPlatform IS NOT SET - see the note in XeniaPlugin.CreateEmulator: the Edit
            // Emulator window grows a duplicate platform row from it. IsDefault on the row is what
            // says "this emulator is the default for that platform", and that is set.
            var platform = emu.AddNewEmulatorPlatform();
            platform.Platform = SnesPlatform;
            platform.IsDefault = true;

            try { dm.Save(false); } catch (Exception ex) { Log.Warn("data manager save failed", ex); }
            Log.Info("created emulator entry \"" + PackName + "\""
                     + (versionLabel != null ? " (" + versionLabel + ")" : "") + " -> " + emu.ApplicationPath);
            return emu;
        }

        // ── BIOS ─────────────────────────────────────────────────────────────

        /// <summary>None declared, and not because there is nothing. The About box lists six
        /// coprocessor firmware files a user can provide - dsp2.rom, dsp3.rom, dsp4.rom, st010.rom,
        /// st011.rom, st018.rom, for the handful of games built around those chips - but WHERE the
        /// emulator looks for them has not been measured, and a declared BIOS is a folder the host
        /// then checks. Pointing that check at a folder we cannot name is the defect the other
        /// plugins went to some trouble to avoid (README, melonDS). Optional in any case: the SNES
        /// itself needs none.</summary>
        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string platform)
            => Array.Empty<EmulatorBiosFile>();

        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(
            string emulatorApplicationPath, string platform, string commandLine)
            => Array.Empty<EmulatorBiosFile>();

        // ── RetroAchievements ────────────────────────────────────────────────

        /// <summary>Not through this plugin. The EMULATOR supports RetroAchievements natively - its
        /// build carries rcheevos (rc_client_begin_login_with_password, RC_CONSOLE_SUPER_NINTENDO,
        /// a hardcore switch) and a Config > Retroachievements page with a Web API key field, even
        /// though the site still lists the feature under "coming". But it keeps the credentials in a
        /// settings store this pack has not located, as a user id and an ENCRYPTED token (its fields
        /// are named rauserID and raencT), so there is nothing to write them into. Claiming support
        /// and silently doing nothing would be worse than saying so; the user logs in inside the
        /// emulator, once.</summary>
        public override RetroAchievementSupportResponse SupportsRetroAchievements(string emulatorApplicationPath)
            => new RetroAchievementSupportResponse(isSupported: false);

        // ── launch ───────────────────────────────────────────────────────────

        /// <summary>Put what the user chose in the pack's window on the command line.
        ///
        /// Measured off MasterExecutor.Awake: the emulator takes as its ROM the first argument
        /// ending in .smc/.sfc/.zip/.swc/.ufo, honours nine "--" flags of its own (--loadstate among
        /// them), and IGNORES every other argument that starts with "-". So the pack's --nixx-*
        /// options and Unity's -screen-* switches can go in front of the ROM the host appends, and
        /// the BepInEx plugin inside the emulator reads the --nixx-* ones. A flag whose name the
        /// user already typed on the emulator's own line is not added twice.
        ///
        /// Never fails a launch: with no settings, the line is handed back as it came.
        ///
        /// One launch at a time for this plugin - see LbipLaunchGate: a launch while the last one is on is refused,
        /// silently in its first 5 seconds (a double click), and so is one while SUPERZSNES.exe is already open.</summary>
        public override PrepareForLaunchResponse PrepareEmulatorForLaunch(PrepareForLaunchArgs args)
            => LbipLaunchGate.Run(PackName, args, PrepareCore, null);

        private PrepareForLaunchResponse PrepareCore(PrepareForLaunchArgs args)
        {
            try
            {
                // The plugin and its docs, put back when BepInEx is there and they are not - after a
                // manual update, or a pack update that carries a newer plugin. No download here.
                try
                {
                    string launched = null;
                    try { launched = args?.EmulatorBeingLaunched?.ApplicationPath; } catch { }
                    var exe = ResolveFullPath(launched);
                    if (SuperZsnesSettings.BepInExWanted && !string.IsNullOrEmpty(exe) && File.Exists(exe))
                    {
                        var exeDir = Path.GetDirectoryName(exe);
                        if (SuperZsnesBepInEx.HasBepInEx(exeDir))
                        {
                            var put = SuperZsnesBepInEx.PutBack(exeDir);
                            foreach (var step in put.Steps) if (put.Changed) Log.Info("bepinex at launch: " + step);
                            if (!put.Ok) Log.Warn("bepinex at launch: " + put.Problem);
                        }
                        else Log.Info("bepinex is not installed beside " + Path.GetFileName(exe) + " - the --nixx-* options will be ignored; install it from the Nixx window");
                    }
                }
                catch (Exception ex) { Log.Warn("bepinex at launch", ex); }

                var flags = SuperZsnesSettings.Flags();
                if (flags.Count == 0) return new PrepareForLaunchResponse(success: true);
                var current = args?.CurrentCommandLine ?? "";
                var rewritten = SuperZsnesSettings.Append(current, flags);
                if (rewritten != current.Trim())
                {
                    Log.Info("launch: +" + flags.Count + " option(s): " + rewritten);
                    return new PrepareForLaunchResponse(success: true) { NewCommandLine = rewritten };
                }
            }
            catch (Exception ex) { Log.Warn("PrepareEmulatorForLaunch", ex); }
            return new PrepareForLaunchResponse(success: true);
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

        /// <summary>For the window, which lives in another class: an emulator path as the host
        /// stores it, made absolute.</summary>
        internal static string ResolveFullPathForUi(string maybeRelative) => ResolveFullPath(maybeRelative);

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
