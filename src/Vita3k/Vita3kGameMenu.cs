// The right-click entry on games, for the games whose emulator is ours - one game or a selection.
//
// NOT AN IGameMenuItemPlugin. Measured on LaunchBox 14: a plugin in Local\Plugins gets its emulator
// role and nothing else - its menu was never asked a question, as a class of its own or on the
// EmulatorPlugin itself. The entry is shown by Nixx-Menus, a classic plugin in Plugins\ that relays
// to GameMenu below; src\Menus\Menus.cs has the contract. And none may be declared here: LiteBox
// loads both roots and would show the entry twice.
//
// WHOSE GAME IS IT: any game one of our Vita3K can run (Mehdi, 29/09) - its OWN emulator when that is
// ours, else an emulator of ours whose platforms name the game's (EmulatorFor): "Launch With" can pick
// it. The Session options live in the game's command line, which is its own emulator's: they are only
// set for games whose own emulator is ours.
//
// A SELECTION THAT MIXES: the entry shows when at least one selected game is ours, and acts on those
// only - the others are counted, not silently dropped.
//
// It opens the options window (Vita3kOptionsForm) on the games of ours in the selection.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Vita3k
{
    internal static class Vita3kGameMenu
    {
        public const string Caption = "Nixx-Vita3K : Options...";


        /// <summary>Vita3K opened on these games, not started, to edit their own Custom Config in Vita3K
        /// itself - from the Options window (Mehdi, 29/09: out of the right-click menu). One Vita3K: the
        /// games of another install are left out, and said.</summary>
        internal static void OpenSettings(IWin32Window owner, Vita3kLayout layout, List<(string Title, string Rom)> games)
        {
            try
            {
                if (layout?.Executable == null || games.Count == 0) { MessageBox.Show(owner, "This game's Vita3K was not found.", "Nixx-Vita3K"); return; }
                bool one = games.Count == 1;
                var name = one ? (string.IsNullOrWhiteSpace(games[0].Title) ? "this game" : "\"" + games[0].Title + "\"") : games.Count + " games";

                // SAID FIRST, AND ASKED (Mehdi, 29/09): what opens is not the game, and it plays nothing.
                var answer = MessageBox.Show(owner,
                    "Vita3K will open on a FAKE installation of " + name + ".\n\n"
                    + "It is there only so you can save " + (one ? "this game's" : "each game's") + " OWN custom settings in Vita3K: right-click "
                    + (one ? "the game" : "a game") + " in Vita3K's list, open its \"Custom Config\" menu, change what you want and save.\n\n"
                    + "The game" + (one ? " is" : "s are") + " NOT installed and cannot be played from there - starting one would fail. Nothing else "
                    + "is touched: no save, no console - only each game's own settings file (portable\\config\\config_<TITLE_ID>.xml), "
                    + "which every real launch of the game then uses.\n\n"
                    + "What the options window sets is NOT written there: it is this plugin's own, laid over that file only while the game runs.\n\n"
                    + "Close Vita3K when you are done: the fake installation is then removed.",
                    "Nixx-Vita3K - Vita3K's own custom settings", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                if (answer != DialogResult.OK) { Log.Info("options: Vita3K's own settings - cancelled at the explanation"); return; }

                var why = Vita3kSettingsSession.Open(layout.Executable, games.Select(g => g.Rom));
                if (why != null)
                    MessageBox.Show(owner, why, "Nixx-Vita3K", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex) { Log.Warn("options: Vita3K's own settings", ex); }
        }

        /// <summary>NEVER NULL: the host turns it into its own menu image, and a null there can cost the
        /// whole entry without a word - ExtendDB's entry, which shows, always returns one.</summary>
        public static Image Icon => _icon ??= SystemIcons.Application.ToBitmap();
        private static Image _icon;

        /// <summary>Does this game run through our emulator? Asked at every right-click, for every
        /// selected game: one lookup by id and a file-name comparison, nothing read from disk.</summary>
        internal static bool IsOurs(IGame game)
        {
            string why;
            bool ours = false;
            try
            {
                var emulator = EmulatorFor(game);
                ours = emulator != null;
                why = game == null ? "no game"
                    : emulator == null ? "no emulator of ours runs " + (game.Platform ?? "its platform")
                    : "emulator " + emulator.Title + " (" + emulator.ApplicationPath + ")" + (IsOwnEmulator(game, emulator) ? "" : ", not its own");
            }
            catch (Exception ex) { why = ex.GetType().Name + ": " + ex.Message; }

            // Asked at every right-click: said once per game and answer, not every time.
            var key = (game == null ? "" : SafeId(game)) + "|" + ours + "|" + why;
            lock (Said) if (Said.Add(key) && Said.Count < 500)
                Log.Info("game menu: " + (game == null ? "(none)" : SafeTitle(game)) + " -> " + (ours ? "ours" : "not ours") + " - " + why);
            return ours;
        }

        private static readonly HashSet<string> Said = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The emulator of ours this game can run on (Mehdi, 29/09): its OWN emulator when that one
        /// is ours - else any emulator of ours whose platforms name the game's. Null when none: the entry is
        /// not offered. Asked at every right-click: lookups in memory, nothing read from disk.</summary>
        internal static IEmulator EmulatorFor(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null || game == null) return null;
                var own = string.IsNullOrWhiteSpace(game.EmulatorId) ? null : dm.GetEmulatorById(game.EmulatorId);
                if (own != null && Vita3kPaths.IsVita3kExecutable(own.ApplicationPath)) return own;
                var platform = game.Platform ?? "";
                foreach (var e in dm.GetAllEmulators() ?? new IEmulator[0])
                {
                    if (e == null || !Vita3kPaths.IsVita3kExecutable(e.ApplicationPath)) continue;
                    if ((e.GetAllEmulatorPlatforms() ?? new IEmulatorPlatform[0]).Any(p => string.Equals(p?.Platform, platform, StringComparison.OrdinalIgnoreCase)))
                        return e;
                }
            }
            catch { }
            return null;
        }

        /// <summary>Is <paramref name="emulator"/> the game's OWN emulator - the one its command line is for?</summary>
        internal static bool IsOwnEmulator(IGame game, IEmulator emulator)
        {
            try { return emulator != null && string.Equals(game?.EmulatorId, emulator.Id, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private static string SafeId(IGame g) { try { return g.Id; } catch { return "?"; } }
        private static string SafeTitle(IGame g) { try { return g.Title; } catch { return "?"; } }

        internal static void Open(IGame[] games)
        {
            try
            {
                var ours = games.Where(IsOurs).ToList();
                int others = games.Length - ours.Count;
                Log.Info("game menu: " + ours.Count + " game(s) of ours selected" + (others > 0 ? ", " + others + " other(s) left out" : ""));
                if (ours.Count == 0) return;

                var entries = ours.Select(g =>
                {
                    var own = Safe(() => g.CommandLine);
                    var rom = Safe(() => g.ApplicationPath);
                    var emulator = EmulatorFor(g);
                    var inherited = InheritedLine(g, emulator);
                    Vita3kLayout layout = null;
                    try
                    {
                        var exe = Vita3kPlugin.ResolveFullPath(emulator?.ApplicationPath);
                        layout = string.IsNullOrEmpty(exe) ? null : Vita3kPaths.Resolve(exe);
                    }
                    catch { }
                    var romFull = Vita3kPlugin.ResolveFullPath(rom);
                    // The title id names the game's own settings file: read from the archive's param.sfo.
                    string titleId = null;
                    try { var content = Vita3kContent.Describe(romFull, out _); if (content != null && content.IsGame) titleId = content.TitleId; }
                    catch { }
                    return new Vita3kOptionsForm.Entry
                    {
                        Game = g, Title = Safe(() => g.Title), Rom = rom, Own = own, Inherited = inherited,
                        RomFull = romFull, GameId = Safe(() => g.Id), InstallDir = layout?.InstallDir,
                        // What the game runs with: the options kept for it, else its line's flags.
                        Options = Vita3kOptions.From(Vita3kSessionStore.Load(layout?.InstallDir, Safe(() => g.Id)) ?? (string.IsNullOrWhiteSpace(own) ? inherited : own), rom),
                        Layout = layout, TitleId = titleId,
                        LineIsOurs = IsOwnEmulator(g, emulator), EmulatorTitle = Safe(() => PluginHelper.DataManager?.GetEmulatorById(g.EmulatorId)?.Title),
                        System = Vita3kGameConfig.Load(layout, Safe(() => g.Id)),
                        Graphics = Vita3kGameConfig.LoadSection(layout, Safe(() => g.Id), Vita3kGameConfig.GpuSection),
                        GraphicsBase = Vita3kGameConfig.DefaultsOf(layout, titleId, Vita3kGameConfig.GpuSection),
                        Compat = CompatOf(layout, Safe(() => g.Id)),
                        CompatBase = CompatBaseOf(layout, titleId),
                        CompatState = layout == null ? null : Vita3kCompat.Lookup(layout, titleId),
                        Advanced = Vita3kGameConfig.LoadAdvanced(layout, Safe(() => g.Id), out var handOn),
                        AdvancedOn = handOn,
                        Base = layout == null ? new VitaSystemSettings()
                             : titleId == null ? Vita3kConfig.Read(layout) : Vita3kGameConfig.WithoutOurs(layout, titleId),
                    };
                }).ToList();

                using var form = new Vita3kOptionsForm(entries);
                form.ShowDialog(OwnerWindow());
            }
            catch (Exception ex) { Log.Warn("game menu", ex); }
        }

        private static readonly string[] CompatSections = { Vita3kGameConfig.CpuSection, Vita3kGameConfig.AudioSection, Vita3kGameConfig.EmulatorSection };

        /// <summary>The Compatibility tab's own values of a game, section/attribute; null for none.</summary>
        private static Dictionary<string, string> CompatOf(Vita3kLayout layout, string gameId)
        {
            if (layout == null) return null;
            var all = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var s in CompatSections)
                foreach (var kv in Vita3kGameConfig.LoadSection(layout, gameId, s) ?? new Dictionary<string, string>())
                    all[s + "/" + kv.Key] = kv.Value;
            return all.Count > 0 ? all : null;
        }

        /// <summary>What the game runs on for those without them, section/attribute.</summary>
        private static Dictionary<string, string> CompatBaseOf(Vita3kLayout layout, string titleId)
        {
            var all = new Dictionary<string, string>(StringComparer.Ordinal);
            if (layout == null) return all;
            foreach (var s in CompatSections)
                foreach (var kv in Vita3kGameConfig.DefaultsOf(layout, titleId, s))
                    all[s + "/" + kv.Key] = kv.Value;
            return all;
        }

        /// <summary>The line a game runs with when it has none of its own: its emulator's line for the
        /// game's platform, else the emulator's own - LaunchBox's GetEffectiveCommandLine without the
        /// game's part, which is exactly the part the window edits.</summary>
        internal static string InheritedLine(IGame game, IEmulator emulator)
        {
            try
            {
                if (emulator == null) return "";
                var platform = Safe(() => game.Platform);
                var forPlatform = (emulator.GetAllEmulatorPlatforms() ?? new IEmulatorPlatform[0])
                    .FirstOrDefault(p => string.Equals(Safe(() => p.Platform), platform, StringComparison.OrdinalIgnoreCase));
                var line = Safe(() => forPlatform?.CommandLine);
                return string.IsNullOrWhiteSpace(line) ? Safe(() => emulator.CommandLine) : line;
            }
            catch (Exception ex) { Log.Warn("could not read the emulator's command line", ex); return ""; }
        }

        private static string Safe(Func<string> read)
        {
            try { return read() ?? ""; } catch { return ""; }
        }

        /// <summary>The host's active window, so the dialog is modal to it and comes up in front.</summary>
        private static IWin32Window OwnerWindow()
        {
            var handle = GetForegroundWindow();
            return handle == IntPtr.Zero ? null : new Owner(handle);
        }

        private sealed class Owner : IWin32Window
        {
            public Owner(IntPtr handle) { Handle = handle; }
            public IntPtr Handle { get; }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }

    /// <summary>What Nixx-Menus calls, by name - see src\Menus\Menus.cs. Public, and its signatures
    /// fixed: the relay binds to exactly these.</summary>
    public static class GameMenu
    {
        /// <summary>The entries offered for this selection: ours when at least one game is.</summary>
        public static string[] Entries(IGame[] games)
        {
            if (games == null || !games.Any(Vita3kGameMenu.IsOurs)) return new string[0];
            // One entry: Vita3K's own per-game settings are opened from the Options window (Mehdi, 29/09).
            return new[] { Vita3kGameMenu.Caption };
        }

        public static void Selected(string entry, IGame[] games)
        {
            if (entry == Vita3kGameMenu.Caption) Vita3kGameMenu.Open(games ?? new IGame[0]);
        }

        public static Image Icon => Vita3kGameMenu.Icon;
    }
}
