// Nixx-SaveProbeMain / Nixx-SaveProbeSecondary / Nixx-SaveProbeCatchAll - save-management plugins that do what a real one
// does, on files a fake emulator writes, and log every call LaunchBox makes on them (Mehdi, 07/10). What they answer:
//   - at the end of a game, is the plugin asked (GetSaves, TryComputeSaveSignature, TryBackupSave...)?
//   - does Game Saves ask it (GetSaves, IsSaveActive...)?
//   - does a Restore of a save it made come back to it (AddSaveFile), or go to the game's emulator?
//   - can a group id be a big JSON, with any character, and come back intact (IdMode)?
//   - can a plugin claim the emulators nobody handles (the CatchAll role, Claim=)?
//
// Main and Secondary: Emulators\Nixx-SaveProbe<Role>\SaveProbe<Role>.exe (tools\save-probe\emu), whose buttons write
// saves\<rom name><SaveExt> and saves\<rom name><StateExt><slot>; the entry is created at start-up when missing, with a
// SNES row that is NOT the platform's default. CatchAll: no emulator of its own - it claims the exe names of its ini
// (Claim=FakeEmulateur.exe) and lists Emulators\Nixx-SaveProbeCatchAll\saves.
//
// probe.ini in Emulators\Nixx-SaveProbe<Role>\, read at every call (no restart):
//   Supports=true          SupportsSaveManagement()
//   OwnGamesOnly=false     answer only for games whose emulator is this one (as Dolphin), else for any (as RetroArch)
//   Container=false        IsSaveContainer()
//   Signature=content      TryComputeSaveSignature: content (SHA-256), fixed, none (false), base (the SDK's)
//   SaveExt=.mainsave      StateExt=.mainstate
//   IdMode=classic         classic, json, big (json + IdPad awkward characters), raw (big with # and | unescaped)
//   IdPad=1024
//   Claim=FakeEmulateur.exe    (CatchAll only; ';' between names)
//   Slots=1..3             GetPotentialSaveSlots: "a..b" or "a,b,c", "empty" for none; -1 is "auto" (08/10, the slot study)
//   SlotLabel=Slot {n}     the label of key n: "{n}" the key, "{n+1}" one more, any text around it; -1 is always "Auto"
//   RestoreSlot=keep       a state's restore: keep = the backup's own name (our plugins before the fix), honor = the
//                          picked slot's file, as RetroArch and Dolphin
//   ReturnId=same          the restored row's id: same = the backup's, recompute = the written slot's, none = no id (Dolphin)
//   ReturnSlot=written     the restored row's Slot: written = the file's slot, picked = the one LaunchBox passed
//   GroupName=Probe <Role> save   the save's SaveGroupName - two plugins with the same name on one game (08/10)
//   OrigPrefix=            prefix written before OriginalFileName ("probemain:"), cut off at restore (09/10)
//   Companions=            extensions of files beside a save that go with it (".rtc"), for GetCompanionSaveFiles
//
// Log: %LOCALAPPDATA%\lb-integrations-plugins\saveprobe.log, every role in one file, in order.

#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

#if ROLE_MAIN
namespace LbIntegrations.SaveProbeMain
#elif ROLE_CATCHALL
namespace LbIntegrations.SaveProbeCatchAll
#else
namespace LbIntegrations.SaveProbeSecondary
#endif
{
    internal static class Probe
    {
#if ROLE_MAIN
        public const string Role = "Main";
#elif ROLE_CATCHALL
        public const string Role = "CatchAll";
#else
        public const string Role = "Secondary";
#endif
        public const string Name = "Nixx-SaveProbe" + Role;
        public const string Exe = "SaveProbe" + Role + ".exe";
        public static readonly string Prefix = "probe" + Role.ToLowerInvariant() + ":";

        private static readonly object Gate = new object();
        public static readonly string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lb-integrations-plugins", "saveprobe.log");

        public static void Log(string line)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                    File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  [" + Role.PadRight(9) + "] t" + Environment.CurrentManagedThreadId.ToString().PadRight(3) + " " + line + Environment.NewLine);
                }
            }
            catch { }
        }

        public static T Safe<T>(Func<T> f) { try { return f(); } catch { return default; } }

        public static string LaunchBoxRoot()
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(Assembly.GetExecutingAssembly().Location));
            for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
            {
                if (Directory.Exists(Path.Combine(dir, "Core")) && Directory.Exists(Path.Combine(dir, "Data"))) return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return @"G:\LB1326";
        }

        public static string Full(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try { return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(LaunchBoxRoot(), path)); } catch { return path; }
        }

        public static string EmuDir => Path.Combine(LaunchBoxRoot(), "Emulators", Name);
        public static string SavesDir => Path.Combine(EmuDir, Ini("SavesDir", "saves"));   // SavesDir: the same saves elsewhere (test 12, 08/10)

        // ── probe.ini ────────────────────────────────────────────────────────

        public static string Ini(string key, string fallback)
        {
            try
            {
                var path = Path.Combine(EmuDir, "probe.ini");
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(EmuDir);
                    File.WriteAllLines(path, new[]
                    {
                        "; read at every call - see tools\\save-probe\\README.md",
                        "Supports=true", "OwnGamesOnly=false", "Container=false", "Signature=content",
                        "SaveExt=." + Role.ToLowerInvariant() + "save", "StateExt=." + Role.ToLowerInvariant() + "state",
                        "IdMode=classic", "IdPad=1024",
#if ROLE_CATCHALL
                        "Claim=FakeEmulateur.exe",
#endif
                    });
                }
                foreach (var l in File.ReadAllLines(path))
                {
                    var i = l.IndexOf('=');
                    if (i > 0 && !l.TrimStart().StartsWith(";") && string.Equals(l.Substring(0, i).Trim(), key, StringComparison.OrdinalIgnoreCase)) return l.Substring(i + 1).Trim();
                }
            }
            catch { }
            return fallback;
        }

        public static bool IniBool(string key, bool fallback) => bool.TryParse(Ini(key, fallback.ToString()), out var b) ? b : fallback;
        public static string SaveExt => Ini("SaveExt", "." + Role.ToLowerInvariant() + "save");
        public static string StateExt => Ini("StateExt", "." + Role.ToLowerInvariant() + "state");

        // ── the group ids ────────────────────────────────────────────────────

        /// <summary>classic: probe&lt;role&gt;:save:&lt;stem&gt;. json: canonical {"emu","kind","by","id"[,"slot"]}, # and | escaped.
        /// big: json + "pad" of IdPad awkward characters. raw: big with # and | left as they are.
        /// IdSuffix (classic only) is appended to every id - the same file listed under another id (test 10, 08/10).</summary>
        public static string GroupId(string kind, string stem, int? slot, bool suffix = true)
        {
            var mode = Ini("IdMode", "classic").ToLowerInvariant();
            if (mode == "classic") return Prefix + kind + ":" + stem + (slot == null ? "" : ":" + slot) + (suffix ? Ini("IdSuffix", "") : "");
            bool raw = mode == "raw";
            var sb = new StringBuilder();
            sb.Append("{\"emu\":").Append(J("probe" + Role.ToLowerInvariant(), raw)).Append(",\"kind\":").Append(J(kind, raw)).Append(",\"by\":\"name\",\"id\":").Append(J(stem, raw));
            if (slot != null) sb.Append(",\"slot\":").Append(slot.Value);
            if (mode == "big" || raw)
            {
                int n = int.TryParse(Ini("IdPad", "1024"), out var v) ? v : 1024;
                const string awkward = "\"<>&'éàüß漢字🙂\t\\#|:;/ ";
                var pad = new StringBuilder(n + awkward.Length);
                while (pad.Length < n) pad.Append(awkward);
                sb.Append(",\"pad\":").Append(J(pad.ToString(0, n), raw));
            }
            return sb.Append('}').ToString();
        }

        public static string J(string s, bool raw = false)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s ?? "")
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '#' && !raw) sb.Append("\\u0023");
                else if (c == '|' && !raw) sb.Append("\\u007c");
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        public static bool IsOursId(string id) => id != null && (id.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("{\"emu\":\"probe" + Role.ToLowerInvariant() + "\"", StringComparison.OrdinalIgnoreCase));

        /// <summary>An id as the log shows it: length, SHA-256 prefix, and its start - to see it come back intact.</summary>
        public static string IdSummary(string id)
        {
            if (id == null) return "null";
            var h = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).Substring(0, 12);
            return "[len " + id.Length + " sha " + h + " \"" + (id.Length > 70 ? id.Substring(0, 70) + "…" : id) + "\"]";
        }

        // ── what is logged ───────────────────────────────────────────────────

        public static string Game(string id)
        {
            if (string.IsNullOrEmpty(id)) return "(no game)";
            var g = Safe(() => PluginHelper.DataManager?.GetGameById(id));
            return g == null ? "game " + id : "\"" + Safe(() => g.Title) + "\" (emu " + Emu(Safe(() => g.EmulatorId)) + ")";
        }

        public static string Emu(string id)
        {
            if (string.IsNullOrEmpty(id)) return "none";
            var e = Safe(() => PluginHelper.DataManager?.GetEmulatorById(id));
            return e == null ? id : Safe(() => e.Title) ?? id;
        }

        public static string Emu(IEmulator e) => e == null ? "null" : Safe(() => e.Title) + " [" + Safe(() => Path.GetFileName(e.ApplicationPath)) + "]";

        public static string Save(GameSaveBase s)
        {
            if (s == null) return "null";
            var gid = Safe(() => s.SaveGroupId);
            var lin = Safe(() => s.MatchLineageId);
            return (s is GameSaveState st ? "STATE slot " + Safe(() => st.Slot) : s is GameSaveGame ? "SAVE" : s.GetType().Name)
                 + " file=\"" + Safe(() => s.FileLocation) + "\" orig=\"" + Safe(() => s.OriginalFileName) + "\" group=" + IdSummary(gid)
                 + " lineage=" + (lin == null ? "null" : lin == gid ? "=group" : IdSummary(lin)) + " emuFile=" + Safe(() => s.EmulatorFileName) + " core=" + Safe(() => s.EmulatorCore)
                 + " game=" + Game(Safe(() => s.GameId)) + (string.IsNullOrEmpty(Safe(() => s.AdditionalApplicationId)) ? "" : " app=" + s.AdditionalApplicationId)
                 + " title=\"" + Safe(() => s.Title) + "\" dir=" + Safe(() => s.IsDirectory);
        }

        public static string Caller()
        {
            // Who asked - the first frames outside this file (LaunchBox's are obfuscated, but the type tells the screen).
            try
            {
                var st = new System.Diagnostics.StackTrace(2, false);
                return string.Join(" < ", st.GetFrames().Select(f => f.GetMethod()).Where(m => m != null && m.DeclaringType?.Namespace?.StartsWith("LbIntegrations.SaveProbe") != true)
                                     .Take(4).Select(m => (m.DeclaringType?.Name ?? "?") + "." + m.Name));
            }
            catch { return "?"; }
        }
    }

#if ROLE_MAIN
    public sealed class SaveProbeMainPlugin : EmulatorPlugin, ISystemEventsPlugin
#elif ROLE_CATCHALL
    public sealed class SaveProbeCatchAllPlugin : EmulatorPlugin, ISystemEventsPlugin
#else
    public sealed class SaveProbeSecondaryPlugin : EmulatorPlugin, ISystemEventsPlugin
#endif
    {
        private static int _ensured;

        public override string EmulatorName => Probe.Name;

        // ── the entry ────────────────────────────────────────────────────────

        public void OnEventRaised(string eventType)
        {
            Probe.Log("event " + eventType);
#if ROLE_MAIN
            SaveProbeObserver.Install();
#endif
#if !ROLE_CATCHALL
            if (Interlocked.CompareExchange(ref _ensured, 1, 0) == 0 && !EnsureEmulator()) _ensured = 0;
#endif
        }

        private static bool EnsureEmulator()
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null) return false;
                if ((dm.GetAllEmulators() ?? Array.Empty<IEmulator>()).Any(e => string.Equals(Path.GetFileName(Probe.Safe(() => e.ApplicationPath)), Probe.Exe, StringComparison.OrdinalIgnoreCase)))
                    return true;
                var emu = dm.AddNewEmulator();
                emu.Title = Probe.Name;
                emu.ApplicationPath = Path.Combine("Emulators", Probe.Name, Probe.Exe);
                var row = emu.AddNewEmulatorPlatform();
                row.Platform = "Super Nintendo Entertainment System";
                row.IsDefault = false;
                dm.Save(false);
                Probe.Log("created the emulator entry " + Probe.Name + " -> " + emu.ApplicationPath + " (SNES row, not default)");
                return true;
            }
            catch (Exception ex) { Probe.Log("could not create the emulator entry: " + ex.Message); return false; }
        }

        public override IEnumerable<IEmulator> GetApplicableEmulators(IEnumerable<IEmulator> emulators)
        {
            var all = (emulators ?? Enumerable.Empty<IEmulator>()).ToList();
#if ROLE_CATCHALL
            // The catch-all of the test: the exe names of its ini only - it must not take the user's emulators.
            var claim = new HashSet<string>(Probe.Ini("Claim", "FakeEmulateur.exe").Split(';').Select(x => x.Trim()).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
            var mine = all.Where(e => claim.Contains(Path.GetFileName(Probe.Safe(() => e.ApplicationPath)) ?? "")).ToList();
            Probe.Log("GetApplicableEmulators: offered " + all.Count + ", claimed " + (mine.Count == 0 ? "none" : string.Join(", ", mine.Select(Probe.Emu))) + "   by " + Probe.Caller());
            return mine;
#else
            return all.Where(e => string.Equals(Path.GetFileName(Probe.Safe(() => e.ApplicationPath)), Probe.Exe, StringComparison.OrdinalIgnoreCase)).ToList();
#endif
        }

        public override EmulatorSupportResponse IsPlatformSupported(string platform) => new EmulatorSupportResponse(true, false);
        public override string GetCurrentVersion(string applicationPath) => "probe";
        public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions() => new[] { new EmulatorControllerVersion("probe", "probe", "Save probe") };

        // What a claimed emulator could show in LaunchBox's screens: every answer logged, nothing offered.
        public override bool IsUpdateAvailable(IEmulator emulator, out EmulatorControllerVersion version)
        {
            version = null;
            Probe.Log("IsUpdateAvailable(" + Probe.Emu(emulator) + ") -> false   by " + Probe.Caller());
            return false;
        }

        public override IEnumerable<EmulatorBiosFile> GetBiosFilesForPlatform(string platform)
        {
            Probe.Log("GetBiosFilesForPlatform(" + platform + ") -> none   by " + Probe.Caller());
            return Array.Empty<EmulatorBiosFile>();
        }

        public override RetroAchievementSupportResponse SupportsRetroAchievements(string emulatorApplicationPath)
        {
            Probe.Log("SupportsRetroAchievements(" + emulatorApplicationPath + ") -> base   by " + Probe.Caller());
            return base.SupportsRetroAchievements(emulatorApplicationPath);
        }

        public override EmulatorInstallResponse InstallEmulator(InstallEmulatorArgs args)
        {
            Probe.Log("InstallEmulator");
#if ROLE_CATCHALL
            return new EmulatorInstallResponse("The catch-all installs nothing.");
#else
            EnsureEmulator();
            var emu = Probe.Safe(() => PluginHelper.DataManager.GetAllEmulators().FirstOrDefault(e => string.Equals(Path.GetFileName(e.ApplicationPath), Probe.Exe, StringComparison.OrdinalIgnoreCase)));
            return emu == null ? new EmulatorInstallResponse("No emulator entry.") : new EmulatorInstallResponse(emu, "Probe ready.");
#endif
        }

        public override PrepareForLaunchResponse PrepareEmulatorForLaunch(PrepareForLaunchArgs args)
        {
            Probe.Log("PrepareEmulatorForLaunch emu=" + Probe.Emu(args?.EmulatorBeingLaunched) + " game=" + Probe.Game(Probe.Safe(() => args.GameBeingLaunched.Id))
                      + " app=" + Probe.Safe(() => args.AppBeingLaunched?.Name) + " line=" + args?.CurrentCommandLine + " -> unchanged");
            return new PrepareForLaunchResponse(true, null) { NewCommandLine = args?.CurrentCommandLine };
        }

        // ── the saves ────────────────────────────────────────────────────────

        public override bool SupportsSaveManagement()
        {
            var v = Probe.IniBool("Supports", true);
            Probe.Log("SupportsSaveManagement -> " + v + "   by " + Probe.Caller());
            return v;
        }

        public override GetSavesResponse GetSaves(GetSavesArgs args)
        {
            var found = new List<GameSaveBase>();
            var own = Probe.IniBool("OwnGamesOnly", false);
            var games = args?.Games ?? (IReadOnlyCollection<IGame>)Array.Empty<IGame>();
            var apps = args?.AdditionalApplications ?? (IReadOnlyCollection<IAdditionalApplication>)Array.Empty<IAdditionalApplication>();
            Probe.Log("GetSaves emu=" + Probe.Emu(args?.Emulator) + " games=" + games.Count + " apps=" + apps.Count + " idMode=" + Probe.Ini("IdMode", "classic") + "   by " + Probe.Caller());
            var ourId = Probe.Safe(() => args.Emulator.Id);
            foreach (var g in games)
            {
                var mine = string.Equals(Probe.Safe(() => g.EmulatorId), ourId, StringComparison.OrdinalIgnoreCase);
                int before = found.Count;
                if (!own || mine) Collect(found, Probe.Safe(() => g.Id), null, Probe.Safe(() => g.ApplicationPath));
                Probe.Log("   game " + Probe.Game(Probe.Safe(() => g.Id)) + " path=" + Probe.Safe(() => g.ApplicationPath) + (own && !mine ? " SKIPPED (not ours)" : "") + " -> " + (found.Count - before));
            }
            foreach (var a in apps)
            {
                int before = found.Count;
                Collect(found, Probe.Safe(() => a.GameId), Probe.Safe(() => a.Id), Probe.Safe(() => a.ApplicationPath));
                Probe.Log("   app \"" + Probe.Safe(() => a.Name) + "\" emu=" + Probe.Emu(Probe.Safe(() => a.EmulatorId)) + " of " + Probe.Game(Probe.Safe(() => a.GameId)) + " path=" + Probe.Safe(() => a.ApplicationPath) + " -> " + (found.Count - before));
            }
            foreach (var s in found) Probe.Log("   -> " + Probe.Save(s));
            return new GetSavesResponse(found);
        }

        private static void Collect(List<GameSaveBase> into, string gameId, string appId, string romPath)
        {
            var rom = Probe.Full(romPath);
            if (gameId == null || rom == null) return;
            var stem = Path.GetFileNameWithoutExtension(rom);
            var save = Path.Combine(Probe.SavesDir, stem + Probe.SaveExt);
            if (File.Exists(save)) into.Add(Row(null, save, gameId, appId, Probe.GroupId("save", stem, null), Probe.Ini("GroupName", "Probe " + Probe.Role + " save"), Lineage("save", stem, null)));
            // Twin=<folder>: the same file name in another folder under another id - two emulators' "<rom>.srm" (08/10, the
            // RASnes9x row LaunchBox re-tied to SUPER ZSNES's card). TwinOrig=same|none, TwinLineage=none|self.
            var twinDir = Probe.Ini("Twin", "");
            if (twinDir.Length > 0)
            {
                var twin = Path.Combine(Probe.EmuDir, twinDir, stem + Probe.SaveExt);
                if (File.Exists(twin))
                {
                    // TwinEmu=<name>: the twin's id is the canonical JSON of the card's, its "emu" alone changed (08/10).
                    var temu = Probe.Ini("TwinEmu", "");
                    var tid = temu.Length > 0
                        ? "{\"emu\":" + Probe.J(temu) + ",\"kind\":\"save\",\"by\":\"name\",\"id\":" + Probe.J(stem) + "}"
                        : Probe.GroupId("save", stem, null) + ":twin:" + twinDir;
                    var fi = new FileInfo(twin);
                    into.Add(new GameSaveGame
                    {
                        GameId = gameId, AdditionalApplicationId = appId, FileLocation = twin, IsDirectory = false,
                        OriginalFileName = string.Equals(Probe.Ini("TwinOrig", "same"), "none", StringComparison.OrdinalIgnoreCase) ? null : Path.GetFileName(twin),
                        SaveGroupId = tid, SaveGroupName = Probe.Ini("TwinName", "Probe twin save"), DisplayChipText = Probe.Ini("TwinChip", "TWIN"), EmulatorFileName = Probe.Ini("TwinExe", "") is { Length: > 0 } x ? x : null,
                        MatchLineageId = string.Equals(Probe.Ini("TwinLineage", "none"), "self", StringComparison.OrdinalIgnoreCase) ? tid : null,
                        ReportedFileSizeBytes = fi.Length, ReportedLastModifiedUtc = fi.LastWriteTimeUtc,
                    });
                }
            }
            for (int slot = -1; slot <= 9; slot++)
            {
                var state = Path.Combine(Probe.SavesDir, stem + Probe.StateExt + SlotSuffix(slot));
                if (File.Exists(state)) into.Add(Row(slot, state, gameId, appId, Probe.GroupId("state", stem, slot), "Probe " + Probe.Role + " states", Lineage("state", stem, slot)));
            }
        }

        /// <summary>A row - the LB14 SDK's properties are init-only: built in one initializer, a state when slot is given.</summary>
        /// <summary>Lineage=base: MatchLineageId is the id WITHOUT IdSuffix (test 11, 08/10); unset leaves it to LaunchBox.</summary>
        private static string Lineage(string kind, string stem, int? slot)
            => string.Equals(Probe.Ini("Lineage", ""), "base", StringComparison.OrdinalIgnoreCase) ? Probe.GroupId(kind, stem, slot, suffix: false) : null;

        private static GameSaveBase Row(int? slot, string path, string gameId, string appId, string group, string groupName, string lineage = null)
        {
            var i = new FileInfo(path);
            if (slot != null)
                return new GameSaveState
                {
                    Slot = slot, GameId = gameId, AdditionalApplicationId = appId, FileLocation = path, OriginalFileName = Probe.Ini("OrigPrefix", "") + Path.GetFileName(path), IsDirectory = false,
                    SaveGroupId = group, SaveGroupName = groupName, DisplayChipText = Probe.Role.ToUpperInvariant(), ReportedFileSizeBytes = i.Length, ReportedLastModifiedUtc = i.LastWriteTimeUtc,
                    MatchLineageId = lineage,
                };
            return new GameSaveGame
            {
                GameId = gameId, AdditionalApplicationId = appId, FileLocation = path, OriginalFileName = Probe.Ini("OrigPrefix", "") + Path.GetFileName(path), IsDirectory = false,
                SaveGroupId = group, SaveGroupName = groupName, DisplayChipText = Probe.Role.ToUpperInvariant(), ReportedFileSizeBytes = i.Length, ReportedLastModifiedUtc = i.LastWriteTimeUtc,
                MatchLineageId = lineage,
            };
        }

        private static bool Ours(GameSaveBase s) => Probe.IsOursId(s?.SaveGroupId);

        public override bool IsSecondarySaveFile(string filePath)
        {
            Probe.Log("IsSecondarySaveFile \"" + filePath + "\" -> false");
            return false;
        }

        /// <summary>Companions=.rtc (';' between extensions): the files beside a SAVE that go with it, as bsnes's .rtc
        /// (08/10: does LaunchBox's own signature see a companion change?). Empty: none.</summary>
        public override IReadOnlyList<string> GetCompanionSaveFiles(string primaryFilePath)
        {
            var exts = Probe.Ini("Companions", "").Split(';').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            var found = new List<string>();
            if (exts.Count > 0 && !string.IsNullOrWhiteSpace(primaryFilePath) && primaryFilePath.EndsWith(Probe.SaveExt, StringComparison.OrdinalIgnoreCase))
                foreach (var e in exts) { var c = Path.ChangeExtension(primaryFilePath, e); if (File.Exists(c)) found.Add(c); }
            Probe.Log("GetCompanionSaveFiles \"" + primaryFilePath + "\" -> " + (found.Count == 0 ? "none" : string.Join(", ", found)) + "   by " + Probe.Caller());
            return found;
        }

        public override bool IsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            var v = Probe.Safe(() => File.Exists(save.FileLocation));
            Probe.Log("IsSaveActive emuPath=" + emulatorApplicationPath + " " + Probe.Save(save) + " -> " + v);
            return v;
        }

        public override bool IsSaveContainer(GameSaveBase save)
        {
            var v = Probe.IniBool("Container", false);
            Probe.Log("IsSaveContainer " + Probe.Save(save) + " -> " + v);
            return v;
        }

        public override bool UseSaveGroupIdForPersistedMatch(GameSaveBase save)
        {
            // TwinMatchById=true: the twin answers true too, as a Nixx plugin for its own JSON id (08/10).
            var twin = (save?.FileLocation ?? "").IndexOf("\\twin", StringComparison.OrdinalIgnoreCase) >= 0;
            var v = (Ours(save) || (twin && Probe.IniBool("TwinMatchById", false))) && Probe.IniBool("MatchById", true);
            Probe.Log("UseSaveGroupIdForPersistedMatch " + Probe.Save(save) + " -> " + v);
            return v;
        }

        public override bool TryBackupSave(GameSaveBase save, string emulatorApplicationPath, string destinationFolder, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(destinationFolder);
                var to = Path.Combine(destinationFolder, Path.GetFileName(save.FileLocation));
                File.Copy(save.FileLocation, to, true);
                Probe.Log("TryBackupSave emuPath=" + emulatorApplicationPath + " dest=" + destinationFolder + " " + Probe.Save(save) + " -> true (" + to + ")   by " + Probe.Caller());
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Probe.Log("TryBackupSave emuPath=" + emulatorApplicationPath + " dest=" + destinationFolder + " " + Probe.Save(save) + " -> false: " + ex.Message);
                return false;
            }
        }

        public override bool TryComputeSaveSignature(GameSaveBase save, string emulatorApplicationPath, out string signature)
        {
            var mode = Probe.Ini("Signature", "content").ToLowerInvariant();
            bool ok;
            if (mode == "base") ok = base.TryComputeSaveSignature(save, emulatorApplicationPath, out signature);
            else if (mode == "none") { signature = null; ok = false; }
            else if (mode == "fixed") { signature = "fixed"; ok = true; }
            else
            {
                signature = null; ok = false;
                try { using var f = File.OpenRead(save.FileLocation); signature = Convert.ToHexString(SHA256.HashData(f)); ok = true; } catch { }
            }
            Probe.Log("TryComputeSaveSignature (" + mode + ") emuPath=" + emulatorApplicationPath + " " + Probe.Save(save) + " -> " + ok + " " + signature + "   by " + Probe.Caller());
            return ok;
        }

        public override bool TryGetSaveGroupInfo(GameSaveBase save, string primaryFilePath, out string saveGroupId, out string saveGroupName)
        {
            var ok = base.TryGetSaveGroupInfo(save, primaryFilePath, out saveGroupId, out saveGroupName);
            Probe.Log("TryGetSaveGroupInfo primary=\"" + primaryFilePath + "\" " + Probe.Save(save) + " -> base: " + ok + " " + saveGroupId + " / " + saveGroupName);
            return ok;
        }

        /// <summary>A state file's slot part: the number, "auto" for -1.</summary>
        private static string SlotSuffix(int slot) => slot < 0 ? "auto" : slot.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public override IReadOnlyDictionary<int, string> GetPotentialSaveSlots()
        {
            var spec = Probe.Ini("Slots", "1..3").Trim();
            var label = Probe.Ini("SlotLabel", "Slot {n}");
            var keys = new List<int>();
            if (!string.Equals(spec, "empty", StringComparison.OrdinalIgnoreCase))
            {
                var range = spec.Split(new[] { ".." }, StringSplitOptions.None);
                if (range.Length == 2 && int.TryParse(range[0], out var a) && int.TryParse(range[1], out var b)) { for (int i = a; i <= b; i++) keys.Add(i); }
                else foreach (var k in spec.Split(',')) if (int.TryParse(k.Trim(), out var v)) keys.Add(v);
            }
            var d = new Dictionary<int, string>();
            foreach (var k in keys) d[k] = k < 0 ? "Auto" : label.Replace("{n+1}", (k + 1).ToString()).Replace("{n}", k.ToString());
            Probe.Log("GetPotentialSaveSlots (" + spec + ") -> " + (d.Count == 0 ? "empty" : string.Join(", ", d.Select(kv => kv.Key + "=\"" + kv.Value + "\""))) + "   by " + Probe.Caller());
            return d;
        }

        public override AddSaveResponse AddSaveFile(AddSaveArgs args)
        {
            var save = args?.SaveToAdd;
            Probe.Log("AddSaveFile " + Probe.Save(save) + "   by " + Probe.Caller());
            try
            {
                if (save == null || !File.Exists(save.FileLocation)) return Fail("no file to add");
                // OrigPrefix (09/10): the original name may carry "<emu>:" - logged raw, then cut off, as our plugins would.
                Probe.Log("   OriginalFileName received: \"" + save.OriginalFileName + "\"");
                var rawOrig = save.OriginalFileName;
                if (!string.IsNullOrEmpty(rawOrig) && rawOrig.IndexOf(':') > 0) rawOrig = rawOrig.Substring(rawOrig.LastIndexOf(':') + 1);
                var name = !string.IsNullOrWhiteSpace(rawOrig) ? rawOrig : Path.GetFileName(save.FileLocation);
                var restoreSlot = Probe.Ini("RestoreSlot", "keep").ToLowerInvariant();
                var returnId = Probe.Ini("ReturnId", "same").ToLowerInvariant();
                int? backupSlot = null;
                if (save is GameSaveState bs)
                {
                    var at = name.IndexOf(Probe.StateExt, StringComparison.OrdinalIgnoreCase);
                    var tail = at > 0 ? name.Substring(at + Probe.StateExt.Length) : null;
                    backupSlot = tail == "auto" ? -1 : int.TryParse(tail, out var t) ? t : (int?)null;
                    Probe.Log("   STATE: picked slot " + Probe.Safe(() => bs.Slot) + ", the backup's own slot " + backupSlot + ", RestoreSlot=" + restoreSlot + " ReturnId=" + returnId
                              + " ReturnSlot=" + Probe.Ini("ReturnSlot", "written"));
                    if (restoreSlot == "honor" && bs.Slot != null && at > 0) name = name.Substring(0, at) + Probe.StateExt + SlotSuffix(bs.Slot.Value);
                }
                var target = Path.Combine(Probe.SavesDir, name);
                if (File.Exists(target))
                {
                    var overwrite = Probe.Safe(() => args.ShouldOverwriteFunc?.Invoke() ?? true);
                    Probe.Log("   target exists, ShouldOverwriteFunc -> " + overwrite);
                    if (!overwrite) return Fail("cancelled");
                }
                Directory.CreateDirectory(Probe.SavesDir);
                File.Copy(save.FileLocation, target, true);
                var stem = name;
                foreach (var ext in new[] { Probe.SaveExt, Probe.StateExt }) { var at = stem.IndexOf(ext, StringComparison.OrdinalIgnoreCase); if (at > 0) { stem = stem.Substring(0, at); break; } }
                // Did the id survive the trip (XML, vault, restart)? Compared with what the listing gives now, byte for byte.
                var expected = save is GameSaveState es ? Probe.GroupId("state", stem, es.Slot) : Probe.GroupId("save", stem, null);
                Probe.Log("   id from LaunchBox " + (string.Equals(save.SaveGroupId, expected, StringComparison.Ordinal) ? "IDENTICAL to the listing's" : "DIFFERS from the listing's " + Probe.IdSummary(expected)));
                GameSaveBase row;
                if (save is GameSaveState st)
                {
                    // The slot the file now is: the picked one (honor) or the backup's own (keep).
                    var written = restoreSlot == "honor" ? st.Slot : backupSlot ?? st.Slot;
                    var id = returnId == "none" ? null : returnId == "recompute" ? Probe.GroupId("state", stem, written) : save.SaveGroupId ?? Probe.GroupId("state", stem, written);
                    var rowSlot = string.Equals(Probe.Ini("ReturnSlot", "written"), "picked", StringComparison.OrdinalIgnoreCase) ? st.Slot : written;
                    row = Row(rowSlot ?? 1, target, save.GameId, save.AdditionalApplicationId, id, "Probe " + Probe.Role + " states");
                }
                else row = Row(null, target, save.GameId, save.AdditionalApplicationId, save.SaveGroupId ?? Probe.GroupId("save", stem, null), "Probe " + Probe.Role + " save");
                Probe.Log("   restored -> " + Probe.Save(row));
                return new AddSaveResponse(row);
            }
            catch (Exception ex) { return Fail(ex.Message); }

            static AddSaveResponse Fail(string why) { Probe.Log("   AddSaveFile failed: " + why); return new AddSaveResponse(why); }
        }

        public override PluginResponse RemoveSave(GameSaveBase save)
        {
            Probe.Log("RemoveSave " + Probe.Save(save) + "   by " + Probe.Caller());
            try { if (save?.FileLocation != null && File.Exists(save.FileLocation)) File.Delete(save.FileLocation); return new PluginResponse(true, null); }
            catch (Exception ex) { return new PluginResponse(false, ex.Message); }
        }
    }
}
