// The front desk (lot 3, docs\saves-lb14-routing.md §4b): when a game's emulator is one of ours, its
// plugin answers for every emulator the game can run on.
//
// WHY. LaunchBox lists a game's saves through the plugin of the game's OWN emulator only, and sends every
// call on a row to that same plugin (measured 07/10). A game played once through "Launch With" on another
// emulator - ares for a SNES game set on SUPER ZSNES, RetroArch, PCSX2 - has its saves listed by nobody,
// backed up by nobody. So the game's plugin lists them too, and hands each call on a row to its owner.
//
// WHO OWNS A ROW is read from its id (SaveOwner): our JSON names it, LaunchBox's integrations write forms
// of their own. An id nobody recognises (RetroArch's bare GUIDs) falls back on the row's EmulatorFileName
// and EmulatorCore - which this desk writes itself on the rows it lists for others, since a row's
// EmulatorFileName holds an exe NAME only (measured 08/10). The owner's emulator is the one of the library
// that plugin drives, the row's exe name and core preferred.
//
// THE OTHER PLUGINS: ours register in LbIntegrations.Catalog's LbSaveOwners (one copy for the process);
// LaunchBox's own integrations (System\Plugins) are found among the loaded assemblies and instantiated here,
// the way LiteBox does (IPluginPaths for the v14 ones). Their emu is "lb." + their name ("lb.retroarch").
//
// OFF UNDER LITEBOX (LbCatalog.HostWillAsk): it asks every plugin itself, a desk would list rows twice.
// NOT NESTED: a plugin asked by a desk lists its own saves only (a thread-static guard).

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Saves
{
    /// <summary>Where a row goes: its owner's plugin and the emulator of the library it runs on.</summary>
    internal sealed class SaveOwnerRoute
    {
        public SaveOwnerRoute(EmulatorPlugin plugin, string emu, IEmulator? emulator) { Plugin = plugin; Emu = emu; Emulator = emulator; }
        public EmulatorPlugin Plugin { get; }
        public string Emu { get; }
        public IEmulator? Emulator { get; }
        public bool Stock => Emu.StartsWith(SaveOwner.LaunchBoxPrefix, StringComparison.Ordinal);
        /// <summary>The emulator's path as LaunchBox stores it - what it would hand the owner itself.</summary>
        public string? EmulatorPath => Emulator?.ApplicationPath;
    }

    internal static class SaveRouter
    {
        /// <summary>Where the router writes what it did; set by the plugins (NixxSavePlugin).</summary>
        public static Action<string>? Log;

        /// <summary>The emu of Nixx-Saves, the catch-all (lot 6): no saves of its own, never asked for any.</summary>
        internal const string CatchAllEmu = "catchall";

        // NOT NESTED, ACROSS THE PACK: every plugin carries its own copy of this class, so a [ThreadStatic] would only
        // stop a plugin from nesting into itself - asked by SUPER ZSNES's desk, RASnes9x's desk listed the others again
        // (ares's files came back labelled RASnes9x.exe, measured 08/10). A named thread slot is one per process.
        private static readonly LocalDataStoreSlot Depth = Thread.GetNamedDataSlot("LbIntegrations.Saves.DeskDepth");
        private static int Inner
        {
            get { try { return Thread.GetData(Depth) is int n ? n : 0; } catch { return 0; } }
            set { try { Thread.SetData(Depth, value); } catch { } }
        }

        /// <summary>True under LaunchBox with the "desk" patch on (Compatibility page); false under a host that asks every
        /// plugin itself (LiteBox).</summary>
        public static bool Active
        {
            get
            {
                if (!LbIntegrations.Compat.Compat.On("desk")) return false;
                try { return !HostWillAsk(); }
                catch { return true; }   // an old Catalog without the flag: LaunchBox's behaviour
            }
        }

        /// <summary>True under LaunchBox (whatever the patches), false under LiteBox.</summary>
        public static bool LaunchBoxHost
        {
            get { try { return !HostWillAsk(); } catch { return true; } }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool HostWillAsk() => LbIntegrations.Catalog.LbCatalog.HostWillAsk;

        // ── the owners ───────────────────────────────────────────────────────

        /// <summary>Register one of ours. NOT INLINED, under a try: a Catalog older than LbSaveOwners costs the desk,
        /// never the plugin.</summary>
        public static void Register(string emu, EmulatorPlugin plugin)
        {
            // RetroArch's integration reads its retroarch.cfg again for every game: answered from a cache (RaConfigCache).
            RaConfigCache.Install();
            try { RegisterCore(emu, plugin); }
            catch (Exception ex) { Say("save owners unavailable (an old LbIntegrations.Catalog): " + ex.Message); }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void RegisterCore(string emu, EmulatorPlugin plugin) => LbIntegrations.Catalog.LbSaveOwners.Register(emu, plugin);

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static List<KeyValuePair<string, object>> OursCore() => LbIntegrations.Catalog.LbSaveOwners.All();

        private static List<(string Emu, EmulatorPlugin Plugin)> Ours()
        {
            try { return OursCore().Where(kv => kv.Value is EmulatorPlugin).Select(kv => (kv.Key, (EmulatorPlugin)kv.Value)).ToList(); }
            catch { return new List<(string, EmulatorPlugin)>(); }
        }

        private static List<(string Emu, EmulatorPlugin Plugin)>? _stock;
        private static readonly object StockGate = new object();

        /// <summary>LaunchBox's own integrations, instantiated once, by their "lb." emu.</summary>
        internal static List<(string Emu, EmulatorPlugin Plugin)> Stock()
        {
            lock (StockGate)
            {
                if (_stock != null) return _stock;
                var found = new List<(string, EmulatorPlugin)>();
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string? location = null;
                    try { location = asm.IsDynamic ? null : asm.Location; } catch { }
                    if (string.IsNullOrEmpty(location) || location!.IndexOf(@"\System\Plugins\", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
                    catch { continue; }
                    foreach (var t in types)
                    {
                        if (t == null || t.IsAbstract || !typeof(EmulatorPlugin).IsAssignableFrom(t)) continue;
                        var p = Create(t, location);
                        if (p == null) continue;
                        string name = "";
                        try { name = p.EmulatorName ?? ""; } catch { }
                        var emu = SaveOwner.LaunchBoxPrefix + Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]", "");
                        if (emu.Length > SaveOwner.LaunchBoxPrefix.Length && !found.Any(f => f.Item1 == emu)) found.Add((emu, p));
                    }
                }
                if (found.Count > 0) RaConfigCache.Install();
                Say("LaunchBox's integrations seen: " + (found.Count == 0 ? "none" : string.Join(", ", found.Select(f => f.Item1))));
                return _stock = found;
            }
        }

        /// <summary>LaunchBox's integrations listed again at the next call (one loaded since), with who drives which emulator
        /// and the routes found.</summary>
        internal static void ForgetStock()
        {
            lock (StockGate) _stock = null;
            _driver.Clear();
            _routes.Clear();
            _covered.Clear();
            _ownPaths.Clear();
        }

        private static readonly ConcurrentDictionary<string, (long At, bool Yes)> _covered = new(StringComparer.Ordinal);

        /// <summary>Lot 5, D6: true when a plugin WITH saves (one of ours, or LaunchBox's RetroArch/Dolphin/PCSX2) drives an
        /// emulator of the library that shares a platform with one of the emulators <paramref name="emu"/> drives. The others
        /// without saves (<paramref name="noSave"/>) never count - and are never asked, their answer being this very rule.
        /// Kept a minute: LaunchBox asks it at every game shown, and the library's emulators rarely change. <paramref name="mine"/>:
        /// the emulators <paramref name="emu"/> drives, when the plugin tells them itself (lot 6, the catch-all: it takes what nobody else
        /// takes, decided by its own rule).</summary>
        internal static bool OthersCoverPlatformsOf(string emu, Func<string?, bool> noSave, Func<IEmulator, bool>? mine = null)
        {
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_covered.TryGetValue(emu, out var known) && now - known.At < System.Diagnostics.Stopwatch.Frequency * 60) return known.Yes;
            bool yes = false;
            string? by = null;
            var platforms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var lib = Library();
                foreach (var e in lib)
                    if (mine != null ? mine(e) : DriverOf(e)?.Emu == emu)
                        foreach (var p in Safe(() => e.GetAllEmulatorPlatforms()) ?? Array.Empty<IEmulatorPlatform>())
                            if (!string.IsNullOrEmpty(Safe(() => p?.Platform))) platforms.Add(p!.Platform);
                if (platforms.Count > 0)
                    foreach (var e in lib)
                    {
                        var d = DriverOf(e);
                        // The catch-all (lot 6) has no saves of its own: it never counts as a plugin with saves.
                        if (d == null || d.Value.Emu == emu || d.Value.Emu == CatchAllEmu || noSave(d.Value.Emu)) continue;
                        bool manages;
                        try { manages = d.Value.Plugin.SupportsSaveManagement(); } catch { manages = false; }
                        if (!manages) continue;
                        if ((Safe(() => e.GetAllEmulatorPlatforms()) ?? Array.Empty<IEmulatorPlatform>()).Any(p => platforms.Contains(Safe(() => p?.Platform) ?? ""))) { yes = true; by = d.Value.Emu + " (" + Safe(() => e.Title) + ")"; break; }
                    }
            }
            catch { yes = false; }
            if (!_covered.TryGetValue(emu, out var was) || was.Yes != yes)
                Say(emu + ": its emulators' platforms [" + string.Join(", ", platforms) + "] " + (yes ? "covered by " + by : "covered by no plugin with saves"));
            _covered[emu] = (now, yes);
            return yes;
        }

        /// <summary>A parameterless constructor, else LaunchBox 14's one taking IPluginPaths (RetroArch's).</summary>
        private static EmulatorPlugin? Create(Type t, string dllPath)
        {
            try
            {
                if (t.GetConstructor(Type.EmptyTypes) != null) return (EmulatorPlugin)Activator.CreateInstance(t)!;
                var ctor = t.GetConstructors().FirstOrDefault(c => c.GetParameters() is { Length: 1 } ps && ps[0].ParameterType == typeof(IPluginPaths));
                if (ctor != null) return (EmulatorPlugin)ctor.Invoke(new object[] { new PluginPaths(dllPath) });
            }
            catch (Exception ex) { Say("could not create " + t.FullName + ": " + (ex.InnerException?.Message ?? ex.Message)); }
            return null;
        }

        /// <summary>LaunchBox's own convention: the folder beside the dll, and System\Plugins\.data\&lt;PluginId&gt;.</summary>
        private sealed class PluginPaths : IPluginPaths
        {
            public PluginPaths(string dll)
            {
                InstallDirectory = Path.GetDirectoryName(Path.GetFullPath(dll)) ?? "";
                string id = Path.GetFileName(InstallDirectory);
                try
                {
                    var manifest = Path.Combine(InstallDirectory, "manifest.json");
                    if (File.Exists(manifest))
                    {
                        var m = Regex.Match(File.ReadAllText(manifest), "\"PluginId\"\\s*:\\s*\"([^\"]+)\"");
                        if (m.Success) id = m.Groups[1].Value;
                    }
                }
                catch { }
                DataDirectory = Path.Combine(Path.GetDirectoryName(InstallDirectory) ?? InstallDirectory, ".data", id);
            }
            public string DataDirectory { get; }
            public string InstallDirectory { get; }
        }

        private static IEnumerable<(string Emu, EmulatorPlugin Plugin)> Everyone() => Ours().Concat(Stock());

        // ── the emulators ────────────────────────────────────────────────────

        private static readonly ConcurrentDictionary<string, (string Emu, EmulatorPlugin Plugin)?> _driver = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The plugin that drives <paramref name="emulator"/>: ours first, then LaunchBox's. Null for none.</summary>
        internal static (string Emu, EmulatorPlugin Plugin)? DriverOf(IEmulator? emulator)
        {
            if (emulator == null) return null;
            var key = (Safe(() => emulator.Id) ?? "") + "|" + (Safe(() => emulator.ApplicationPath) ?? "");
            return _driver.GetOrAdd(key, _ =>
            {
                foreach (var o in Everyone())
                {
                    try { if (o.Plugin.GetApplicableEmulators(new[] { emulator })?.Any(x => ReferenceEquals(x, emulator) || (x != null && string.Equals(Safe(() => x.Id), Safe(() => emulator.Id), StringComparison.OrdinalIgnoreCase))) == true) return o; } catch { }
                }
                return null;
            });
        }

        /// <summary>The library's emulators (lot 4: who drives an exe, for the migration of the persisted cards).</summary>
        internal static IReadOnlyCollection<IEmulator> LibraryEmulators() => Library();

        private static IReadOnlyCollection<IEmulator> Library()
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null) { Once("library: no DataManager"); return new List<IEmulator>(); }
                return dm.GetAllEmulators()?.Where(e => e != null).ToList() ?? new List<IEmulator>();
            }
            catch (Exception ex) { Once("library unreadable: " + ex.GetType().Name + ": " + ex.Message); return new List<IEmulator>(); }
        }

        private static readonly Regex Core = new Regex(@"-L\s+""?(?:[^""\s]*[\\/])?(?<core>[^""\\/\s]+?)(?:\.dll)?""?(?:\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>The RetroArch core of <paramref name="emulator"/>'s line for <paramref name="platform"/>, or null.</summary>
        internal static string? CoreOf(IEmulator? emulator, string? platform)
        {
            try
            {
                var line = emulator?.GetAllEmulatorPlatforms()?.FirstOrDefault(p => string.Equals(p?.Platform, platform, StringComparison.OrdinalIgnoreCase))?.CommandLine
                           ?? emulator?.CommandLine;
                return CoreIn(line);
            }
            catch { return null; }
        }

        /// <summary>The core a RetroArch command line loads ("-L cores\snes9x_libretro.dll" → "snes9x_libretro"), or null.</summary>
        internal static string? CoreIn(string? line)
        {
            var m = string.IsNullOrEmpty(line) ? null : Core.Match(line);
            return m != null && m.Success ? m.Groups["core"].Value : null;
        }

        private static bool Covers(IEmulator emulator, string? platform)
        {
            if (string.IsNullOrEmpty(platform)) return false;
            // A big listing: the emulator's platforms once per listing, not once per game (SaveScan; nothing kept after it).
            if (SaveScan.Active)
                return SaveScan.Memo("covers|" + (Safe(() => emulator.Id) ?? "") + "|" + RuntimeHelpersId(emulator), () =>
                {
                    try { return new HashSet<string>(emulator.GetAllEmulatorPlatforms()?.Select(p => p?.Platform).Where(p => p != null).Select(p => p!) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase); }
                    catch { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
                }).Contains(platform!);
            try { return emulator.GetAllEmulatorPlatforms()?.Any(p => string.Equals(p?.Platform, platform, StringComparison.OrdinalIgnoreCase)) == true; }
            catch { return false; }
        }

        /// <summary>The exe a row of the plugin's OWN is asked about with (IsSaveActive, the signature, the backup): the one
        /// LaunchBox passes - the game's emulator - unless the plugin drives several installs of the library, then the one the
        /// row belongs to (EmulatorFor's tiebreak: the folder holding the file, the card's hint). Kept per row: asked hundreds
        /// of times per Game Saves opening.</summary>
        internal static string? OwnInstallPath(EmulatorPlugin plugin, GameSaveBase? row, string? given)
        {
            try
            {
                if (row == null || !Active || !LbIntegrations.Compat.Compat.On("twoInstalls")) return given;
                var key = plugin.GetType().FullName + "\u0001" + row.SaveGroupId + "\u0001" + row.FileLocation + "\u0001" + row.GameId;
                if (_ownPaths.TryGetValue(key, out var known)) return known ?? given;
                string? found = null;
                if (Library().Count(e => ReferenceEquals(DriverOf(e)?.Plugin, plugin)) > 1)
                    found = Safe(() => EmulatorFor(plugin, row)?.ApplicationPath);
                if (_ownPaths.Count > 20000) _ownPaths.Clear();
                _ownPaths[key] = found;
                return found ?? given;
            }
            catch { return given; }
        }
        private static readonly ConcurrentDictionary<string, string?> _ownPaths = new(StringComparer.Ordinal);

        /// <summary>The emulator of the library <paramref name="plugin"/> drives that suits <paramref name="row"/>: its exe
        /// name, its core, the game's platform - the best match, else any of that plugin's.</summary>
        private static IEmulator? EmulatorFor(EmulatorPlugin plugin, GameSaveBase row)
        {
            string? platform = null;
            try { platform = PluginHelper.DataManager?.GetGameById(row.GameId)?.Platform; } catch { }
            IEmulator? best = null; int bestScore = -1;
            // Between two installs of one exe name (two ares, measured 12/10): the one that listed this very file, else the
            // one whose folder holds it, else the one whose hint the card carries - else the first, as before. Without it
            // the second install's live file was asked about as the first's: "not active".
            var file = FullOf(Safe(() => row.FileLocation));
            var listedBy = file.Length > 0 ? ListedFrom(file) : null;
            var hint = InstallIds.HintOf(row.SaveGroupId);
            foreach (var e in Library())
            {
                if (!ReferenceEquals(DriverOf(e)?.Plugin, plugin)) continue;
                int score = 0;
                if (!string.IsNullOrEmpty(row.EmulatorFileName)
                    && string.Equals(Path.GetFileName(Safe(() => e.ApplicationPath) ?? ""), row.EmulatorFileName, StringComparison.OrdinalIgnoreCase)) score += 4;
                if (!string.IsNullOrEmpty(row.EmulatorCore) && string.Equals(CoreOf(e, platform), row.EmulatorCore, StringComparison.OrdinalIgnoreCase)) score += 2;
                if (Covers(e, platform)) score += 1;
                score *= 8;
                if (listedBy != null && ReferenceEquals(listedBy, e)) score += 4;
                else if (file.Length > 0 && EmulatorDir(e) is string dir && dir.Length > 0
                         && file.StartsWith(dir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) score += 2;
                if (hint != null && string.Equals(InstallIds.Hint(Safe(() => e.ApplicationPath)), hint, StringComparison.OrdinalIgnoreCase)) score += 1;
                if (score > bestScore) { best = e; bestScore = score; }
            }
            return best;
        }

        /// <summary>The install a restore of <paramref name="row"/> writes into, for a LaunchBox integration that writes where
        /// its emulator says (StockRestore). EmulatorFor keeps the first of a tie; two installs of the same exe name, core
        /// and platform in DIFFERENT folders tie (Codex review, 09/10), so then the backup's card says which: its live
        /// file, as this desk listed it (ListedFrom) or the folder it is in. Still tied: <paramref name="refused"/> - never
        /// written to a guessed install.</summary>
        internal static IEmulator? RestoreEmulator(EmulatorPlugin plugin, GameSaveBase row, IEmulator? best, out string? refused)
        {
            refused = null;
            string? platform = null;
            try { platform = PluginHelper.DataManager?.GetGameById(row.GameId)?.Platform; } catch { }
            var cands = new List<(IEmulator E, string Dir, int Score)>();
            foreach (var e in Library())
            {
                if (!ReferenceEquals(DriverOf(e)?.Plugin, plugin)) continue;
                int score = 0;
                if (!string.IsNullOrEmpty(row.EmulatorFileName)
                    && string.Equals(Path.GetFileName(Safe(() => e.ApplicationPath) ?? ""), row.EmulatorFileName, StringComparison.OrdinalIgnoreCase)) score += 4;
                if (!string.IsNullOrEmpty(row.EmulatorCore) && string.Equals(CoreOf(e, platform), row.EmulatorCore, StringComparison.OrdinalIgnoreCase)) score += 2;
                if (Covers(e, platform)) score += 1;
                cands.Add((e, EmulatorDir(e) ?? "", score));
            }
            var lives = SavePersist.LivePathsOf(row.GameId, row.SaveGroupId);
            // Where each would write it (the plugin's OwnExpectedDir): two entries writing the same place are one; and the
            // card's own hint of the install it was made on (InstallIds).
            var full = cands.Select(c => new InstallCandidate
            {
                Dir = c.Dir, Score = c.Score,
                Dest = Expected(plugin, c.E, row),
                Hint = InstallIds.Hint(Safe(() => c.E.ApplicationPath)),
            }).ToList();
            int pick = PickInstall(full, lives,
                                   p => { var from = ListedFrom(p); return from == null ? -1 : cands.FindIndex(c => ReferenceEquals(c.E, from)); },
                                   InstallIds.HintOf(row.SaveGroupId), out var why, out var tiedAt);
            if (pick >= 0) return cands[pick].E;
            if (why == null) return best;
            // Still more than one: the user picks the folder it goes to (Mehdi 12/10) - in LaunchBox, the switch on.
            if (LbIntegrations.Compat.Compat.On("twoInstalls") && SlotRestore.HostName() == "launchbox")
            {
                string? gameEmu = null;
                try { gameEmu = PluginHelper.DataManager?.GetGameById(row.GameId)?.EmulatorId; } catch { }
                var choices = tiedAt.Select(i => (Label: Safe(() => cands[i].E.Title) ?? "?", Folder: full[i].Dest ?? full[i].Dir)).ToList();
                int pre = Math.Max(0, tiedAt.FindIndex(i => string.Equals(Safe(() => cands[i].E.Id), gameEmu, StringComparison.OrdinalIgnoreCase)));
                var chosen = InstallChoice.Ask(Path.GetFileName(row.FileLocation ?? ""), choices, pre);
                Say("restore of " + Path.GetFileName(row.FileLocation ?? "") + ": " + (chosen is int k ? "the user chose " + choices[k].Label + " (" + choices[k].Folder + ")" : "no install chosen"));
                if (chosen is int c) return cands[tiedAt[c]].E;
                refused = "Restore cancelled: no install was chosen.";
                return null;
            }
            refused = why + ": " + string.Join(", ", tiedAt.Select(i => (Safe(() => cands[i].E.Title) ?? "?") + " (" + (full[i].Dest ?? full[i].Dir) + ")"))
                      + ". Restore it with the game set to the right one.";
            return null;
        }

        /// <summary>One install a restore could go to: its folder, its score, where its plugin would write the row (null:
        /// unknown), its hint (InstallIds.Hint).</summary>
        internal sealed class InstallCandidate
        {
            public string Dir = "";
            public int Score;
            public string? Dest;
            public string? Hint;
        }

        /// <summary>Where <paramref name="plugin"/> would write <paramref name="row"/> on <paramref name="e"/>'s install - its
        /// IOwnSaves.OwnExpectedDir, found by name (each plugin compiles its own copy of the interface); null when it does not
        /// say (a LaunchBox integration, an older plugin, unknown).</summary>
        internal static string? Expected(EmulatorPlugin plugin, IEmulator e, GameSaveBase row)
        {
            try
            {
                var m = _expected.GetOrAdd(plugin.GetType(), t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                                                   .FirstOrDefault(x => x.Name.EndsWith("IOwnSaves.OwnExpectedDir", StringComparison.Ordinal) && x.GetParameters().Length == 2));
                if (m == null) return null;
                var r = m.Invoke(plugin, new object[] { e, row });
                if (r == null || r.GetType().GetProperty("Kind")?.GetValue(r)?.ToString() == "Unknown") return null;
                var p = r.GetType().GetProperty("Path")?.GetValue(r) as string;
                return string.IsNullOrWhiteSpace(p) ? null : Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar);
            }
            catch { return null; }
        }
        private static readonly ConcurrentDictionary<Type, MethodInfo?> _expected = new();

        /// <summary>The rule of <see cref="RestoreEmulator"/> as it was (no destinations, no hints) - kept for the probe.</summary>
        internal static int PickInstall(IReadOnlyList<(string Dir, int Score)> c, IReadOnlyList<string> lives, Func<string, int> listedIndex, out string? why)
            => PickInstall(c.Select(x => new InstallCandidate { Dir = x.Dir, Score = x.Score }).ToList(), lives, listedIndex, null, out why, out _);

        /// <summary>The rule of <see cref="RestoreEmulator"/>, apart for the probe. The best score's installs, one per place they
        /// write to (Dest, else their folder): one place → the first. Else (docs\saves-two-installs.md §4):
        ///   a. the installs holding a live file of the card - listed by it (<paramref name="listedIndex"/>), in its
        ///      destination, or under its folder: exactly one → it (Codex review 12/10: never the first one found);
        ///   b. else the one whose hint is the card's (<paramref name="rowHint"/>): exactly one → it;
        ///   c. else -1 with <paramref name="why"/> and the tied ones in <paramref name="tied"/> (one per place).
        /// -1 and no reason: no candidate.</summary>
        internal static int PickInstall(IReadOnlyList<InstallCandidate> c, IReadOnlyList<string> lives, Func<string, int> listedIndex,
                                        string? rowHint, out string? why, out List<int> tied)
        {
            why = null; tied = new List<int>();
            if (c.Count == 0) return -1;
            int top = c.Max(x => x.Score);
            string Key(int i) => (c[i].Dest ?? c[i].Dir).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
            var groups = Enumerable.Range(0, c.Count).Where(i => c[i].Score == top).GroupBy(Key).Select(g => g.ToList()).ToList();
            tied = groups.Select(g => g[0]).ToList();
            if (groups.Count <= 1) return tied[0];
            int GroupOf(int i) => groups.FindIndex(g => g.Contains(i));
            bool Holds(InstallCandidate x, string p)
            {
                string Under(string d) => d.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (x.Dest != null && (string.Equals(p.TrimEnd(Path.DirectorySeparatorChar), x.Dest, StringComparison.OrdinalIgnoreCase)
                                       || p.StartsWith(Under(x.Dest), StringComparison.OrdinalIgnoreCase))) return true;
                return x.Dir.Length > 0 && p.StartsWith(Under(x.Dir), StringComparison.OrdinalIgnoreCase);
            }
            var holders = new HashSet<int>();
            foreach (var p in lives)
            {
                int i = listedIndex(p);
                if (i >= 0 && GroupOf(i) is int g && g >= 0) { holders.Add(g); continue; }
                for (int k = 0; k < groups.Count; k++) if (Holds(c[groups[k][0]], p)) holders.Add(k);
            }
            if (holders.Count == 1) return tied[holders.First()];
            if (!string.IsNullOrEmpty(rowHint))
            {
                var hinted = Enumerable.Range(0, groups.Count).Where(k => groups[k].Any(i => string.Equals(c[i].Hint, rowHint, StringComparison.OrdinalIgnoreCase))).ToList();
                if (hinted.Count == 1) return tied[hinted[0]];
            }
            why = holders.Count > 1 ? "This save's card holds a file in " + holders.Count + " installs of the same emulator"
                                    : "This save could belong to " + groups.Count + " installs of the same emulator, and nothing says which";
            return -1;
        }
        // ── a row's owner ────────────────────────────────────────────────────

        /// <summary>Where <paramref name="row"/> must go, or null when it is <paramref name="self"/>'s own (or the desk is off).</summary>
        public static SaveOwnerRoute? OwnerOf(GameSaveBase? row, EmulatorPlugin self, string selfEmu)
        {
            if (row == null || !Active) return null;
            // Kept once found: IsSaveActive asks for the same rows hundreds of times.
            var key = selfEmu + "\u0001" + row.SaveGroupId + "\u0001" + row.EmulatorFileName + "\u0001" + row.EmulatorCore + "\u0001" + row.GameId;
            if (_routes.TryGetValue(key, out var known)) return known.Route;
            var found = OwnerOfCore(row, self, selfEmu, out var cacheable);
            if (cacheable)
            {
                if (_routes.Count > 20000) _routes.Clear();
                _routes[key] = new RouteBox(found);
            }
            return found;
        }

        private sealed class RouteBox { public RouteBox(SaveOwnerRoute? r) { Route = r; } public SaveOwnerRoute? Route { get; } }

        /// <summary>Every live file the desk listed for another plugin, by full path: who listed it and from which
        /// emulator of the library (OthersFor). Read by OwnerOfPath and by a restore that must find its install.</summary>
        private static readonly ConcurrentDictionary<string, (string Emu, EmulatorPlugin Plugin, IEmulator Emulator)> _listed = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The plugin that listed the live file at <paramref name="path"/> through a desk this session, when it is
        /// not <paramref name="selfEmu"/>.</summary>
        internal static SaveOwnerRoute? ListedOwner(string? path, string selfEmu)
            => !string.IsNullOrWhiteSpace(path) && _listed.TryGetValue(FullOf(path), out var w) && w.Emu != selfEmu
                ? new SaveOwnerRoute(w.Plugin, w.Emu, w.Emulator) : null;

        /// <summary>A live file listed for <paramref name="emu"/> through a desk (OthersFor; the probe).</summary>
        internal static void NoteListed(string path, string emu, EmulatorPlugin plugin, IEmulator emulator)
        {
            // It decides routing (OwnerForStock, OwnerOfPath): emptied only past a size no library reaches, and said.
            if (_listed.Count > 200000) { _listed.Clear(); Say("the table of listed files was full (200000) and was emptied - rows of other emulators route by their ids until listed again"); }
            _listed[FullOf(path)] = (emu, plugin, emulator);
        }

        /// <summary>The live files of a game's card (SavePersist.LivePathsOf); the probe puts its own.</summary>
        internal static Func<string?, string?, IReadOnlyList<string>> CardLivePaths = SavePersist.LivePathsOf;

        /// <summary>True for a file in LaunchBox's vault of backups (&lt;root&gt;\Saves\).</summary>
        internal static bool IsVault(string? path)
        {
            var full = FullOf(path);
            if (full.Length == 0) return false;
            var root = SaveScan.Memo("root", () => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")));
            return full.StartsWith(Path.Combine(root, "Saves") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The emulator that listed the live file at <paramref name="path"/> this session, if any.</summary>
        internal static IEmulator? ListedFrom(string? path)
            => !string.IsNullOrWhiteSpace(path) && _listed.TryGetValue(FullOf(path), out var w) ? w.Emulator : null;
        private static readonly ConcurrentDictionary<string, RouteBox> _routes = new(StringComparer.Ordinal);

        private static SaveOwnerRoute? OwnerOfCore(GameSaveBase row, EmulatorPlugin self, string selfEmu, out bool cacheable)
        {
            cacheable = true;
            var owner = SaveOwner.Of(row.SaveGroupId);
            (string Emu, EmulatorPlugin Plugin)? who = null;
            switch (owner.Kind)
            {
                case SaveOwnerKind.Nixx:
                    if (owner.Name == selfEmu) return null;
                    foreach (var o in Ours()) if (o.Emu == owner.Name) { who = o; break; }
                    if (who == null) cacheable = false;   // not registered yet (or not installed): ask again later
                    break;
                case SaveOwnerKind.LaunchBox:
                    // The desk of a LaunchBox integration (lot 4, StockDesk) asking about its own rows: they are its own,
                    // never its twin's (Stock() makes instances of its own, never the one LaunchBox holds).
                    if (owner.Name == selfEmu) return null;
                    foreach (var o in Stock()) if (o.Emu == owner.Name) { who = o; break; }
                    break;
                default:
                    // Unknown (a bare GUID): the exe name the desk wrote on the row, else it is the desk's own.
                    if (string.IsNullOrEmpty(row.EmulatorFileName)) return null;
                    foreach (var e in Library())
                        if (string.Equals(Path.GetFileName(Safe(() => e.ApplicationPath) ?? ""), row.EmulatorFileName, StringComparison.OrdinalIgnoreCase)
                            && DriverOf(e) is { } d) { who = d; break; }
                    break;
            }
            if (who == null || ReferenceEquals(who.Value.Plugin, self)) return null;
            return new SaveOwnerRoute(who.Value.Plugin, who.Value.Emu, EmulatorFor(who.Value.Plugin, row));
        }

        /// <summary>Where a row a LaunchBox integration's desk is asked about must go (lot 4, StockDesk), or null when it is
        /// that integration's own: its emu in our JSON, or any id that is not ours (empty, native, a GUID - LaunchBox's own
        /// cards) on a file no other plugin listed through a desk. No last resort by exe name, no instance compared: the
        /// instance LaunchBox holds is never the router's.</summary>
        public static SaveOwnerRoute? OwnerForStock(GameSaveBase? row, string selfEmu)
        {
            if (row == null || !Active) return null;
            var owner = SaveOwner.Of(row.SaveGroupId);
            // No owner in the id (empty, a GUID): the plugin that LISTED this file through a desk this session (review
            // 10/10: RetroArch's rows on a Dolphin game, listed with their own ids when RetroArch is not in our form or
            // `stockIds` is off, were restored, removed and backed up by Dolphin's code).
            if (owner.Kind != SaveOwnerKind.Nixx && owner.Kind != SaveOwnerKind.LaunchBox)
            {
                if (ListedOwner(row.FileLocation, selfEmu) is { } listed) return listed;
                // A backup (LaunchBox's vault, Saves\ - a restore): its path was never listed; its card's live files were
                // (second review 10/10). Only for the vault: the row methods ask about live rows hundreds of times.
                if (!IsVault(row.FileLocation) || string.IsNullOrEmpty(row.SaveGroupId)) return null;
                foreach (var live in CardLivePaths(row.GameId, row.SaveGroupId))
                    if (ListedOwner(live, selfEmu) is { } byCard) return byCard;
                return null;
            }
            if (owner.Name == selfEmu) return null;
            var key = "stock\u0001" + selfEmu + "\u0001" + row.SaveGroupId + "\u0001" + row.EmulatorFileName + "\u0001" + row.EmulatorCore + "\u0001" + row.GameId;
            if (_routes.TryGetValue(key, out var known)) return known.Route;
            (string Emu, EmulatorPlugin Plugin)? who = null;
            foreach (var o in owner.Kind == SaveOwnerKind.Nixx ? Ours() : Stock()) if (o.Emu == owner.Name) { who = o; break; }
            if (who == null)
            {
                // Not registered yet (or not installed): the integration's own code answers, as LaunchBox would; asked again later.
                if (owner.Kind == SaveOwnerKind.LaunchBox) { if (_routes.Count > 20000) _routes.Clear(); _routes[key] = new RouteBox(null); }
                return null;
            }
            var found = new SaveOwnerRoute(who.Value.Plugin, who.Value.Emu, EmulatorFor(who.Value.Plugin, row));
            if (_routes.Count > 20000) _routes.Clear();
            _routes[key] = new RouteBox(found);
            return found;
        }

        /// <summary>The owner of a FILE (companions, secondary files: no row) - the plugin whose emulator's folder holds
        /// it; null when none other than <paramref name="self"/> (of <paramref name="selfEmu"/>, for a LaunchBox
        /// integration's desk whose instance is not the router's) does.</summary>
        public static SaveOwnerRoute? OwnerOfPath(string? path, EmulatorPlugin self, string? selfEmu = null)
        {
            if (string.IsNullOrWhiteSpace(path) || !Active) return null;
            string full;
            try { full = Path.GetFullPath(path); } catch { return null; }
            // A file another plugin listed through this desk: that plugin, whatever folder it is in (Codex review, 09/10:
            // a save beside the rom or in a save folder set elsewhere had its companions answered by the desk's own
            // plugin - measured, a RASnes9x .srm backed up without its .rtc).
            var listed = FullOf(full);
            if (_listed.TryGetValue(listed, out var who))
                return ReferenceEquals(who.Plugin, self) || who.Emu == selfEmu ? null : new SaveOwnerRoute(who.Plugin, who.Emu, who.Emulator);
            foreach (var e in Library())
            {
                var dir = EmulatorDir(e);
                if (dir == null || !full.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                var d = DriverOf(e);
                if (d == null || ReferenceEquals(d.Value.Plugin, self) || d.Value.Emu == selfEmu) return null;
                return new SaveOwnerRoute(d.Value.Plugin, d.Value.Emu, e);
            }
            return null;
        }

        private static string? EmulatorDir(IEmulator e)
        {
            try
            {
                var p = e.ApplicationPath;
                if (string.IsNullOrWhiteSpace(p)) return null;
                var root = SaveScan.Memo("root", () => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")));
                return Path.GetDirectoryName(Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(root, p)))?.TrimEnd(Path.DirectorySeparatorChar);
            }
            catch { return null; }
        }

        // ── listing the others ───────────────────────────────────────────────

        /// <summary>The saves the OTHER emulators of the library keep for the games of <paramref name="args"/> - each one
        /// that covers the game's platform and whose plugin manages saves - their exe name and core written on them.</summary>
        public static List<GameSaveBase> OthersFor(EmulatorPlugin self, GetSavesArgs args, IReadOnlyCollection<GameSaveBase> own)
        {
            var found = new List<GameSaveBase>();
            if (!Active || Inner > 0 || args == null) return found;
            var games = (args.Games ?? Array.Empty<IGame>()).Where(g => g != null).ToList();
            var apps = (args.AdditionalApplications ?? Array.Empty<IAdditionalApplication?>()).Where(a => a != null && !SaveRows.IsLink(Safe(() => a.ApplicationPath))).Select(a => a!).ToList();
            Once("first listing: " + games.Count + " game(s), " + apps.Count + " version(s), " + Library().Count + " emulator(s) in the library");
            if (games.Count == 0 && apps.Count == 0) return found;
            var selfId = Safe(() => args.Emulator?.Id);
            var mine = new HashSet<string>(own.Select(r => FullOf(Safe(() => r.FileLocation))).Where(p => p.Length > 0), StringComparer.OrdinalIgnoreCase);
            var gameOf = new Dictionary<string, IGame?>(StringComparer.OrdinalIgnoreCase);
            // A big listing: the games by id once (the first of an id, as FirstOrDefault found it), not a walk per version.
            Dictionary<string, IGame>? byId = null;
            if (SaveScan.Active)
            {
                byId = new Dictionary<string, IGame>(StringComparer.Ordinal);
                foreach (var g in games) { var gid = Safe(() => g.Id); if (gid != null && !byId.ContainsKey(gid)) byId[gid] = g; }
            }
            IGame? GameOf(string? id)
            {
                if (string.IsNullOrEmpty(id)) return null;
                if (!gameOf.TryGetValue(id!, out var g)) { g = (byId != null ? (byId.TryGetValue(id!, out var hit) ? hit : null) : games.FirstOrDefault(x => Safe(() => x.Id) == id)) ?? Safe(() => PluginHelper.DataManager?.GetGameById(id)); gameOf[id!] = g; }
                return g;
            }

            Inner = Inner + 1;
            try
            {
                foreach (var e in Library())
                {
                    if (string.Equals(Safe(() => e.Id), selfId, StringComparison.OrdinalIgnoreCase)) continue;
                    var d = DriverOf(e);
                    if (d == null || ReferenceEquals(d.Value.Plugin, self) || d.Value.Plugin.GetType() == self.GetType() || d.Value.Emu == CatchAllEmu) continue;
                    bool manages;
                    try { manages = d.Value.Plugin.SupportsSaveManagement(); } catch { manages = false; }
                    if (!manages) continue;

                    var theirGames = games.Where(g => Covers(e, Safe(() => g.Platform))).ToList();
                    var theirApps = apps.Where(a => Covers(e, Safe(() => GameOf(a.GameId)?.Platform))).ToList();
                    if (theirGames.Count == 0 && theirApps.Count == 0) continue;

                    // LaunchBox's integrations read some folders off the GAME's command line, which names ours: they are
                    // shown the game as if this emulator ran it (SaveGameView - RetroArch's states, 09/10).
                    if (d.Value.Emu.StartsWith(SaveOwner.LaunchBoxPrefix, StringComparison.Ordinal) && LbIntegrations.Compat.Compat.On("retroarchView"))
                    {
                        theirGames = theirGames.Select(g => SaveGameView.As(g, e)).ToList();
                        theirApps = theirApps.Select(a => SaveGameView.As(a, e, Safe(() => GameOf(a.GameId)?.Platform))).ToList();
                    }
                    GetSavesResponse? r = null;
                    try { r = d.Value.Plugin.GetSaves(new GetSavesArgs { Emulator = e, Games = theirGames, AdditionalApplications = theirApps }); }
                    catch (Exception ex) { Say(d.Value.Emu + " could not list for " + Safe(() => e.Title) + ": " + ex.Message); }
                    if (r == null || !r.WasSuccess || r.FoundSaves == null) continue;

                    var exe = Path.GetFileName(Safe(() => e.ApplicationPath) ?? "");
                    int n = 0;
                    foreach (var row in r.FoundSaves)
                    {
                        // One row per file: two emulators on one exe (RetroArch with two cores) list the same .srm.
                        var at = FullOf(Safe(() => row?.FileLocation));
                        if (row == null || (at.Length > 0 && !mine.Add(at))) continue;
                        var core = string.IsNullOrEmpty(row.EmulatorCore) ? CoreOf(e, Safe(() => GameOf(row.GameId)?.Platform)) : row.EmulatorCore;
                        found.Add(SaveRows.With(row, exe, core));
                        if (at.Length > 0) NoteListed(at, d.Value.Emu, d.Value.Plugin, e);
                        n++;
                    }
                    if (n > 0) Say("listed " + n + " save(s) of " + d.Value.Emu + " (" + Safe(() => e.Title) + ") with ours");
                }
            }
            finally { Inner = Inner - 1; }
            return found;
        }

        /// <summary>A row's file as one string whatever its form (LaunchBox stores paths relative to its root).</summary>
        private static string FullOf(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try
            {
                var root = SaveScan.Memo("root", () => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")));
                return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path)).TrimEnd(Path.DirectorySeparatorChar);
            }
            catch { return path!; }
        }

        internal static void Say(string message) { try { Log?.Invoke("[saves] " + message); } catch { } }

        private static readonly ConcurrentDictionary<string, bool> _said = new(StringComparer.Ordinal);
        /// <summary>A diagnostic said once per process (the listing runs over the whole library).</summary>
        private static void Once(string message) { if (_said.TryAdd(message.Split(':')[0], true)) Say(message); }

        private static T? Safe<T>(Func<T> f) { try { return f(); } catch { return default; } }

        /// <summary>The object's identity hash, for a memo key beside its id (two objects of one id, a copy: told apart unless their hashes collide).</summary>
        private static int RuntimeHelpersId(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
    }
}
