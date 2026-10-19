// Kronos integration for LaunchBox / LiteBox - the Sega Saturn emulator of FCare, carried on by Benjamin Siskoo (its Windows
// builds are Tradu-France's). docs\kronos.md holds the research and what was measured.
//
// Claim the emulator (KronosLaunch.IsKronosExe: kronos.exe), install and update Tradu-France's latest 64-bit build
// (KronosBuild), describe it to the host's catalogue for the Sega Saturn, and at each launch:
//   - the disc Kronos is given (KronosLaunch.DiscFor): a .m3u's first disc, an archive unpacked, the formats Kronos opens itself;
//   - its BIOS (KronosBios): the one of the disc's area in <install>\bios, else copied from RetroArch's system folder and the
//     other emulators' - by content - and given on the line (--bios=, nothing written);
//   - kronos.ini: its first settings, the pack's slot keys, and FOR THIS GAME ONLY its backup RAM, its states' folder and its
//     backup cartridge (KronosSession - put back once Kronos has quit);
//   - the game's discs written down for the next-disc key (KronosAhk);
//   - the line: -a [-f] --bios=<file> -i <disc>.
//
// THE PLATFORM ROW: one, Sega Saturn, made when the plugin creates the emulator; IsDefault only where no emulator of the library
// is yet the platform's. A row that is there is never removed nor unticked (never-touch-existing-platform-rows).
//
// NO RETROACHIEVEMENTS: Kronos has none.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;
using LbIntegrations.Catalog;
using LbIntegrations.Lbip;
using LbIntegrations.Saves;

namespace LbIntegrations.Kronos
{
    public partial class KronosPlugin : EmulatorPlugin, ISystemEventsPlugin, ILbCatalogSource
    {
        /// <summary>Nothing: the host appends the game's path; the switches are added at each launch.</summary>
        private const string DefaultCommandLine = "";
        internal const string PackName = "Nixx-Kronos";
        internal const string Platform = "Sega Saturn";
        private const string RowsId = "com.nixxou.lbip.kronos";

        /// <summary>What the game's file may be: what Kronos opens, a .m3u, an archive (docs\kronos.md §4).</summary>
        internal static readonly string[] Extensions = { ".chd", ".cue", ".ccd", ".mds", ".iso", ".m3u", ".zip", ".7z" };

        public KronosPlugin()
        {
            // First: the save front desk registers this plugin for the others (Shared.Saves SaveDesk).
            _desk = new SaveDesk(this, this, SaveEmu, SaveLog);
            Log.Info("plugin constructed, assembly " + typeof(KronosPlugin).Assembly.Location);
            LbipLog.Use(Log.Info, Log.Warn, Log.Disabled, () => Log.Tracing);
            LbipRowInjection.Install(RowsId, MetadataRows());
            // A session LaunchBox did not see the end of (it was closed or crashed while Kronos ran): kronos.ini put back.
            try { KronosSession.Recover(); } catch (Exception ex) { Log.Warn("session recovery at start", ex); }
            // LaunchBox's import wizard, to Kronos for Sega ST-V or Arcade: the list sorted to Kronos's sets (KronosLbImport).
            KronosLbImport.Install();
        }

        /// <summary>The catalogue's row. AutoExtract false: the plugin unpacks an archive itself, a .cue with its .bin files.</summary>
        private static IEnumerable<LbCatalogEmulator> MetadataRows()
        {
            var row = new LbCatalogEmulator
            {
                Name = PackName,
                CommandLine = DefaultCommandLine,
                ApplicableFileExtensions = string.Join("; ", Extensions),
                Url = KronosBuild.Home,
                BinaryFileName = KronosLaunch.ExecutableName,
                AutoExtract = false,
            };
            row.Platforms.Add(new LbCatalogPlatform { Platform = Platform, ApplicableFileExtensions = string.Join("; ", Extensions), Recommended = false, RequiredBiosFile = null });
            // ST-V (KronosStv): MAME's zips; "Arcade" accepted too, its sets sorted at import and at launch.
            row.Platforms.Add(new LbCatalogPlatform { Platform = KronosStv.Platform, ApplicableFileExtensions = ".zip", Recommended = false, RequiredBiosFile = "stvbios.zip" });
            row.Platforms.Add(new LbCatalogPlatform { Platform = KronosStv.Arcade, ApplicableFileExtensions = ".zip", Recommended = false, RequiredBiosFile = null });
            yield return row;
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
                LbipRowInjection.Install(RowsId, MetadataRows());
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
                if (!KronosLaunch.IsKronosExe(path)) continue;
                claimed.Add(emu);
                EnsureHotkeyScripts(emu);
            }
            var signature = string.Join("|", claimed.Select(e => { try { return e.Title; } catch { return "?"; } }).OrderBy(t => t));
            if (signature != _lastClaimed)
            {
                _lastClaimed = signature;
                Log.Info("GetApplicableEmulators: claimed " + claimed.Count + " emulator(s)");
            }
            return claimed;
        }

        private static void EnsureHotkeyScripts(IEmulator emu)
        {
            if (!LbIntegrations.Compat.Compat.On("emulatorFields")) return;   // the Compatibility page
            try
            {
                var set = new List<string>();
                if (Fill(() => emu.AutoHotkeyScript, v => emu.AutoHotkeyScript = v, KronosAhk.Running)) set.Add("running");
                if (Fill(() => emu.ExitAutoHotkeyScript, v => emu.ExitAutoHotkeyScript = v, KronosAhk.Exit)) set.Add("exit");
                if (Fill(() => emu.SaveStateAutoHotkeyScript, v => emu.SaveStateAutoHotkeyScript = v, KronosAhk.SaveState)) set.Add("save");
                if (Fill(() => emu.LoadStateAutoHotkeyScript, v => emu.LoadStateAutoHotkeyScript = v, KronosAhk.LoadState)) set.Add("load");
                if (set.Count > 0) Log.Info("hotkey scripts set: " + string.Join(", ", set));
            }
            catch (Exception ex) { Log.Warn("could not describe the hotkeys on the emulator entry", ex); }
        }

        /// <summary>A script set when empty - or when it is, word for word, one an earlier build of this plugin wrote
        /// (KronosAhk.Superseded). One the user changed is never touched.</summary>
        private static bool Fill(Func<string> get, Action<string> set, string value)
        {
            try
            {
                var now = get();
                string Norm(string s) => (s ?? "").Replace("\r\n", "\n").Trim();
                bool ours = KronosAhk.Superseded.Any(old => Norm(old) == Norm(now));
                if (!string.IsNullOrWhiteSpace(now) && !ours) return false;
                if (Norm(now) == Norm(value)) return false;
                set(value);
                return true;
            }
            catch { return false; }
        }

        private static T Safe<T>(Func<T> get) { try { return get(); } catch { return default; } }

        public override EmulatorSupportResponse IsPlatformSupported(string platform)
        {
            bool supported = string.Equals((platform ?? "").Trim(), Platform, StringComparison.OrdinalIgnoreCase) || KronosStv.IsOurs(platform);
            // Never recommended (Mehdi, 17/10): LaunchBox pushes a recommended emulator - in the import wizard too, not installed yet.
            return new EmulatorSupportResponse(supported, false);
        }

        // ── versions ─────────────────────────────────────────────────────────

        /// <summary>The version of an install of OURS (its stamp), else null - a Kronos the user installed is his to update.</summary>
        public override string GetCurrentVersion(string applicationPath) => KronosBuild.InstalledVersion(ResolveFullPath(applicationPath));

        public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions()
        {
            var r = KronosBuild.Latest();
            if (r == null) return null;
            return new List<EmulatorControllerVersion> { new EmulatorControllerVersion(r.Url, r.Version ?? "latest", r.FileName) };
        }

        /// <summary>THE DOWNLOAD BUTTON OF ADD EMULATOR IS THIS (DemulPlugin, measured 24/09): an empty path - nothing installed
        /// yet - true; installed but not by us - false, it is the user's; ours and older than the latest - true.</summary>
        public override bool IsUpdateAvailable(string emulatorAppPath, out EmulatorControllerVersion version)
        {
            version = null;
            try
            {
                version = GetInstallableVersions()?.FirstOrDefault();
                if (version == null) return string.IsNullOrWhiteSpace(emulatorAppPath);
                if (string.IsNullOrWhiteSpace(emulatorAppPath)) return true;
                var current = GetCurrentVersion(emulatorAppPath);
                return current != null && KronosBuild.IsNewer(version.Label, current);
            }
            catch (Exception ex) { Log.Warn("update check failed", ex); return false; }
        }

        // ── installation ─────────────────────────────────────────────────────

        public override EmulatorInstallResponse InstallEmulator(InstallEmulatorArgs args)
        {
            string archive = null;
            try
            {
                var release = KronosBuild.Latest();
                if (release == null) return new EmulatorInstallResponse("Couldn't read Kronos's latest build on Tradu-France. Try again later.");

                bool reinstall = args?.ExistingEmulator != null;
                string targetDir = reinstall
                    ? Path.GetDirectoryName(ResolveFullPath(args.ExistingEmulator.ApplicationPath))
                    : Path.Combine(LaunchBoxRoot(), "Emulators", PackName);
                if (string.IsNullOrWhiteSpace(targetDir)) return new EmulatorInstallResponse("Couldn't work out where to install Kronos.");
                var exe = Path.Combine(targetDir, KronosLaunch.ExecutableName);
                if (KronosLaunch.Running(exe)) return new EmulatorInstallResponse("Kronos is running from " + targetDir + ": close it, then update again.");

                Report(args, "Downloading Kronos " + release.Version + "... " + KronosBuild.RuntimeNote, 0);
                archive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                KronosBuild.Fetch(release, archive, p => Report(args, "Downloading Kronos " + release.Version + "...", p), () => Cancelled(args));

                Report(args, "Extracting Kronos...", null);
                Archives.ExtractOver(archive, targetDir);
                if (!File.Exists(exe)) return new EmulatorInstallResponse("Kronos was extracted to " + targetDir + " but " + KronosLaunch.ExecutableName + " is not there.");
                KronosBuild.Stamp(targetDir, release.Version);
                Log.Info("installed Kronos " + release.Version + " to " + targetDir);

                // Its first settings and the pack's keys.
                KronosSession.Recover();
                var settings = KronosLaunch.SettingsOf(exe);
                var doc = KronosIniDoc.Load(settings);
                bool changed = KronosLaunch.Bootstrap(doc);
                var bound = KronosLaunch.BindPackKeys(doc);
                if (changed || bound.Count > 0) { doc.Save(settings); Log.Info("kronos.ini: first settings" + (bound.Count > 0 ? ", the pack's keys bound - " + string.Join(", ", bound) : "")); }

                // Every Saturn BIOS the user already has, copied into bios\ now.
                Report(args, "Looking for the Saturn BIOS...", null);
                var bios = KronosBios.Ensure(exe, m => Report(args, m, null));
                var known = bios.Where(b => b.Known != null).Select(b => b.Known.Label).Distinct().ToList();
                var note = " " + (known.Count == 0 ? "No Saturn BIOS found: put one in " + KronosLaunch.BiosDirOf(exe) + "." : "BIOS: " + string.Join(", ", known) + ".");

                if (reinstall)
                {
                    try { args.ExistingEmulator.ApplicationPath = MakeRelativeToLaunchBox(exe); } catch { }
                    return new EmulatorInstallResponse(args.ExistingEmulator, "Kronos updated to " + release.Version + "." + note);
                }
                if (args != null && !args.ShouldCreateEmulator)
                    return new EmulatorInstallResponse("Kronos was installed to " + targetDir + ", but no emulator entry was requested." + note);

                var created = CreateEmulator(exe);
                if (created == null)
                    return new EmulatorInstallResponse("Kronos was installed to " + targetDir + " but the emulator entry couldn't be created (no data manager available).");
                return new EmulatorInstallResponse(created, "Kronos " + release.Version + " installed." + note);
            }
            catch (OperationCanceledException) { return new EmulatorInstallResponse("Installation cancelled."); }
            catch (Exception ex)
            {
                Log.Warn("install failed", ex);
                return new EmulatorInstallResponse("Failed to install Kronos: " + ex.Message);
            }
            finally
            {
                try { if (archive != null && File.Exists(archive)) File.Delete(archive); } catch { }
            }
        }

        private static bool Cancelled(InstallEmulatorArgs args) { try { return args?.ShouldCancelFunc?.Invoke() ?? false; } catch { return false; } }

        /// <summary>The emulator with its Sega Saturn row - the platform's default only when no emulator of the library is yet.</summary>
        private static IEmulator CreateEmulator(string exePath)
        {
            var dm = PluginHelper.DataManager;
            if (dm == null) return null;
            bool taken = false;
            try
            {
                foreach (var e in dm.GetAllEmulators() ?? Array.Empty<IEmulator>())
                    foreach (var r in e.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>())
                        try { if (r.IsDefault && string.Equals(r.Platform, Platform, StringComparison.OrdinalIgnoreCase)) taken = true; } catch { }
            }
            catch { }

            var emu = dm.AddNewEmulator();
            emu.Title = PackName;
            emu.ApplicationPath = MakeRelativeToLaunchBox(exePath);
            emu.CommandLine = DefaultCommandLine;
            EnsureHotkeyScripts(emu);
            // DefaultPlatform IS NOT SET - see XeniaPlugin.CreateEmulator. IsDefault on the row is what counts.
            var row = emu.AddNewEmulatorPlatform();
            row.Platform = Platform;
            row.CommandLine = "";
            row.IsDefault = !taken;
            // Sega ST-V too (KronosStv) - never the default: MAME is the arcade emulator, and Kronos runs its own sets only.
            // "Arcade" is never added: the user adds it, and the import keeps only Kronos's sets there.
            var stv = emu.AddNewEmulatorPlatform();
            stv.Platform = KronosStv.Platform;
            stv.CommandLine = "";
            stv.IsDefault = false;
            try { dm.Save(false); } catch (Exception ex) { Log.Warn("data manager save failed", ex); }
            Log.Info("created emulator entry \"" + PackName + "\" -> " + emu.ApplicationPath + ", " + Platform + (row.IsDefault ? " (its default)" : ""));
            return emu;
        }

        // ── BIOS ─────────────────────────────────────────────────────────────

        /// <summary>For LaunchBox's BIOS list: the usual names, none required - Kronos knows a BIOS by its content, whatever its
        /// name (KronosBios), so a name LaunchBox looks for may be missing while Kronos has what it needs.</summary>
        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string platform)
        {
            if (KronosStv.IsOurs(platform))
                return new[] { new EmulatorBiosFile(KronosLaunch.BiosFolder, KronosStv.BiosSet + ".zip", false,
                    "ST-V BIOS (MAME's) - beside the games, or in Kronos's bios folder", null, null) };
            if (!string.Equals((platform ?? "").Trim(), Platform, StringComparison.OrdinalIgnoreCase)) return Array.Empty<EmulatorBiosFile>();
            const string folder = KronosLaunch.BiosFolder;
            return new[]
            {
                new EmulatorBiosFile(folder, "mpr-17933.bin", false, "Saturn BIOS US / Europe 1.01a - the plugin knows any Saturn BIOS in bios\\ by its content and gives Kronos the disc's", null, null),
                new EmulatorBiosFile(folder, "sega_101.bin", false, "Saturn BIOS Japan 1.01 - for Japanese discs", null, null),
            };
        }

        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string emulatorApplicationPath, string platform, string commandLine)
            => GetBiosFilesForPlatform(platform);

        public override RetroAchievementSupportResponse SupportsRetroAchievements(string emulatorApplicationPath)
            => new RetroAchievementSupportResponse(isSupported: false);

        // ── launch ───────────────────────────────────────────────────────────

        public override PrepareForLaunchResponse PrepareEmulatorForLaunch(PrepareForLaunchArgs args)
            => LbipLaunchGate.Run(PackName, args, PrepareCore);

        private PrepareForLaunchResponse PrepareCore(PrepareForLaunchArgs args)
        {
            string exe = null, gameId = null, rom = null, platform = null, title = null;
            try { exe = ResolveFullPath(args?.EmulatorBeingLaunched?.ApplicationPath); } catch { }
            try { gameId = args?.GameBeingLaunched?.Id; } catch { }
            // The file launched: a version's (another disc, another release) or the game's.
            try { rom = ResolveFullPath(args?.AppBeingLaunched?.ApplicationPath); } catch { }
            try { if (string.IsNullOrWhiteSpace(rom)) rom = ResolveFullPath(args?.GameBeingLaunched?.ApplicationPath); } catch { }
            try { platform = args?.GameBeingLaunched?.Platform; } catch { }
            try { title = args?.GameBeingLaunched?.Title; } catch { }
            if (!KronosLaunch.IsKronosExe(exe) || !File.Exists(exe)) return new PrepareForLaunchResponse(success: true);
            string Caption() => PackName + " - " + (title ?? Path.GetFileName(rom ?? ""));
            PrepareForLaunchResponse Refuse(string text) { LbipNotice.Show(Caption(), text); return new PrepareForLaunchResponse(success: false); }

            // Two Kronos on one install would write over each other's settings and saves.
            if (KronosLaunch.Running(exe)) return Refuse("Kronos is already running from " + Path.GetDirectoryName(exe) + ": close it, then start the game again.");
            // A session LaunchBox did not see the end of: put back first.
            KronosSession.Recover();

            // An ST-V game (KronosStv): its own path.
            if (KronosStv.IsOurs(platform)) return PrepareStv(args, exe, gameId, rom, platform, Refuse);

            // The disc.
            var plan = KronosLaunch.DiscFor(rom, exe);
            if (plan.Disc == null) return Refuse("This game cannot be started: " + plan.Problem);
            var disc = KronosDisc.Read(plan.Disc);
            Log.Info("launch: " + Path.GetFileName(plan.Disc) + (plan.Discs.Count > 1 ? " (disc " + (plan.Discs.IndexOf(plan.Disc) + 1) + " of " + plan.Discs.Count + ")" : "")
                     + (disc.Hash != null ? " - " + disc : " - " + disc.Problem));

            // Its BIOS: the disc's area's in bios\, else copied in.
            var region = KronosDisc.BiosRegion(disc);
            var bios = KronosBios.InFolder(exe);
            if (bios.Count == 0 || (region != null && !KronosBios.HasFor(bios, region))) bios = KronosBios.Ensure(exe);
            if (bios.Count == 0)
                return Refuse("Kronos needs a Sega Saturn BIOS, and none was found.\n\nPut one in " + KronosLaunch.BiosDirOf(exe)
                              + " (any name - the plugin knows it by its content: mpr-17933.bin for US / European discs, sega_101.bin for Japanese ones), "
                              + "or in RetroArch's system folder - it is copied at the next launch.");
            if (region != null && !KronosBios.HasFor(bios, region))
                Log.Warn("launch: no " + KronosBios.RegionWord(region) + " BIOS in bios\\ for this disc (area " + disc.Area + ") - Kronos is given the one there");
            var biosFile = KronosBios.PickFor(bios, region ?? "us-eu");
            Log.Info("launch: BIOS " + biosFile);

            // A CHD Kronos cannot decode (a Zstandard one: an empty window) handed over uncompressed - served where it is, else
            // copied (KronosChd) - every disc of the game, the next-disc key's too; every other disc as it is.
            var served = new List<string>();
            var given = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            void ReleaseServed() { foreach (var r in served) KronosChd.Release(r); served.Clear(); }
            foreach (var d in plan.Discs.Prepend(plan.Disc).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var shown = KronosChd.Present(d, exe, out var root, out var problem);
                if (root != null) served.Add(root);
                if (shown == null) { ReleaseServed(); return Refuse(problem); }
                given[d] = shown;
            }
            var toKronos = new KronosLaunch.Plan { Disc = given[plan.Disc], Discs = plan.Discs.Select(d => given[d]).ToList() };

            // kronos.ini: its first settings, the pack's keys, and for this game only its RAM, its states' folder, its cartridge.
            var settings = KronosLaunch.SettingsOf(exe);
            var doc = KronosIniDoc.Load(settings);
            KronosLaunch.Bootstrap(doc);
            var bound = KronosLaunch.BindPackKeys(doc);
            if (bound.Count > 0) Log.Info("kronos.ini: the pack's keys bound - " + string.Join(", ", bound));
            if (!KronosLaunch.SlotKeysAreOurs(doc)) Log.Info("kronos.ini: some slot keys were rebound in Kronos - F2 / F4 send the pack's all the same");
            var ram = RamPath(exe, platform, rom);
            var states = StatesDir(exe, platform, rom);
            try { Directory.CreateDirectory(Path.GetDirectoryName(ram)); Directory.CreateDirectory(states); } catch { }
            bool newRam = false;
            try { newRam = KronosCart.FormatRam(ram); } catch (Exception ex) { Log.Warn("launch: could not make the game's backup RAM " + ram, ex); }
            var keys = new List<(string, string, bool, string)>
            {
                (@"Memory\Path", KronosIniDoc.PathText(ram), true, null),
                // 32 KB, the console's: a new file is made of that size (an 8 MB one would be Kronos's own format only).
                (@"Memory\ExtendMemory", "false", false, null),
                (@"General\SaveStates", KronosIniDoc.PathText(states), true, null),
                // Its screenshots beside its saves, never in Kronos's default (%LocalAppData%\kronos\screenshots).
                (@"General\ScreenshotsDirectory", KronosIniDoc.PathText(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(states))), "Screenshots")), true, null),
            };
            // Its own options, from its window (KronosGameOptions).
            var options = KronosGameOptions.KeysFor(gameId, exe, doc);
            if (options.LeftOut.Count > 0) Log.Warn("launch: left out - " + string.Join(", ", options.LeftOut));
            var expansion = options.Keys.Any(k => k.Option == "Expansion");
            // Its backup cartridge, when it has one (KronosCart): 4 Mbit, its file - never for a game that needs another cartridge,
            // nor when its window forces an expansion cartridge.
            var cart = CartPath(exe, platform, rom);
            if (File.Exists(cart) && expansion)
                Log.Info("launch: the game's cartridge is not plugged - its window forces an expansion cartridge");
            else if (File.Exists(cart))
            {
                var bad = KronosCart.WhyNot(cart);
                if (bad != null) Log.Warn("launch: the game's cartridge is not plugged - " + bad);
                else if (KronosCart.NeedsOtherCart(disc)) Log.Info("launch: the game's cartridge is not plugged - this game needs another cartridge (set in Kronos)");
                else
                {
                    keys.Add((@"Cartridge\Type", "2", false, null));
                    keys.Add((@"Cartridge\Path", KronosIniDoc.PathText(cart), true, null));
                    Log.Info("launch: backup cartridge " + cart);
                }
            }
            var forced = KronosSession.Force(doc, keys);
            forced.AddRange(KronosSession.Force(doc, options.Keys));
            try { doc.Save(settings); }
            catch (Exception ex) { ReleaseServed(); return Refuse("Kronos's settings could not be written (" + ex.Message + "): the game would use another game's backup RAM."); }
            Log.Info("launch: backup RAM " + ram + (newRam ? " (new, formatted)" : "") + ", states in " + states);
            // The CHDs served uncompressed detached once Kronos has gone.
            var detach = served.ToList();
            KronosSession.Begin(exe, settings, forced, detach.Count == 0 ? null : () => { foreach (var r in detach) KronosChd.Release(r); }, gameId);

            // The discs, for the next-disc key.
            KronosLaunch.WriteDiscs(exe, toKronos, plan.Discs.Select(Path.GetFileName).ToList());

            var current = args?.CurrentCommandLine ?? "";
            var line = KronosLaunch.CommandLine(current, KronosSettings.BoolFor(gameId, KronosSettings.Fullscreen), biosFile, rom, toKronos.Disc);
            Log.Info("launch: " + line);
            return new PrepareForLaunchResponse(success: true) { NewCommandLine = line };
        }

        /// <summary>An ST-V game (KronosStv): the set checked as Kronos would, a folder of its own made, Kronos pointed at it in
        /// kronos.ini for this session - its EEPROM and coins brought in, taken back after.</summary>
        private PrepareForLaunchResponse PrepareStv(PrepareForLaunchArgs args, string exe, string gameId, string rom, string platform, Func<string, PrepareForLaunchResponse> refuse)
        {
            var why = KronosStv.WhyNotByName(rom, platform);
            if (why != null) { Log.Warn("launch refused: " + why); return refuse(why); }
            var set = KronosStv.SetOf(rom);
            var zips = KronosStv.ZipsFor(rom, set, exe, out var problem);
            if (zips == null) { Log.Warn("launch refused: " + problem.Replace("\n", " ")); return refuse(problem); }
            var saves = RamDir(exe, platform);
            var run = KronosStv.Prepare(exe, set, zips, saves, out problem);
            if (run == null) return refuse(problem);
            Log.Info("launch: ST-V " + set.Title + " (" + set.Name + ")");

            var settings = KronosLaunch.SettingsOf(exe);
            var doc = KronosIniDoc.Load(settings);
            KronosLaunch.Bootstrap(doc);
            var bound = KronosLaunch.BindPackKeys(doc);
            if (bound.Count > 0) Log.Info("kronos.ini: the pack's keys bound - " + string.Join(", ", bound));
            var states = Path.Combine(saves, "States", set.Name);
            try { Directory.CreateDirectory(states); } catch { }
            var keys = new List<(string, string, bool, string)>
            {
                (@"Cartridge\Type", KronosStv.CartType, false, null),
                (@"Cartridge\Path", KronosIniDoc.PathText(run), true, null),
                // The folder its settings window reads for the ST-V cartridge (2.7.5: "Cartridge/Path/" + the cartridge's name).
                (@"Cartridge\Path\STVRomgame", KronosIniDoc.PathText(run), true, null),
                (@"Cartridge\STVGame", set.Name, false, null),
                (@"General\SaveStates", KronosIniDoc.PathText(states), true, null),
                // Its screenshots beside its saves, never in Kronos's default (%LocalAppData%\kronos\screenshots).
                (@"General\ScreenshotsDirectory", KronosIniDoc.PathText(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(states))), "Screenshots")), true, null),
                // The board has no backup RAM; Kronos makes one all the same - kept apart from the Saturn games' own.
                (@"Memory\Path", KronosIniDoc.PathText(Path.Combine(run, "bkram.bin")), true, null),
                (@"Memory\ExtendMemory", "false", false, null),
            };
            // Its own options, from its window - an ST-V game's (no expansion cartridge: the ST-V cartridge is the game).
            var options = KronosGameOptions.KeysFor(gameId, exe, doc, stv: true);
            var forced = KronosSession.Force(doc, keys);
            forced.AddRange(KronosSession.Force(doc, options.Keys));
            try { doc.Save(settings); }
            catch (Exception ex) { return refuse("Kronos's settings could not be written (" + ex.Message + ")."); }
            KronosSession.Begin(exe, settings, forced, () => KronosStv.TakeBack(exe), gameId);

            // No other disc: the next-disc key has nothing to do.
            KronosLaunch.WriteDiscs(exe, new KronosLaunch.Plan { Disc = rom, Discs = new List<string>() });

            // Neither -a nor -f: the game is started once Kronos's window is there (KronosStvStart), then put in full screen.
            var line = KronosLaunch.CommandLine(args?.CurrentCommandLine ?? "", false, null, rom, null, autostart: false);
            KronosStvStart.Begin(exe, set.Name, KronosSettings.BoolFor(gameId, KronosSettings.Fullscreen));
            Log.Info("launch: " + (line.Length == 0 ? "(nothing on the line)" : line));
            return new PrepareForLaunchResponse(success: true) { NewCommandLine = line };
        }

        // ── paths ────────────────────────────────────────────────────────────

        /// <summary>Worked out once: it follows from this dll's folder, which does not move.</summary>
        private static string LaunchBoxRoot() => _launchBoxRoot ??= LaunchBoxRootCore();

        private static string _launchBoxRoot;

        private static string LaunchBoxRootCore()
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
                var p = maybeRelative.Trim().Trim('"');
                return Path.IsPathRooted(p) ? p : Path.GetFullPath(Path.Combine(LaunchBoxRoot(), p));
            }
            catch { return maybeRelative; }
        }

        private static void Report(InstallEmulatorArgs args, string message, double? progress)
        {
            try { args?.ReportProgressAction?.Invoke(message, null, progress); } catch { }
        }
    }
}
