// The right-click entry on games, for the games whose emulator is ours - one game or a selection.
//
// NOT AN IGameMenuItemPlugin. Measured on LaunchBox 14: a plugin in Local\Plugins gets its emulator
// role and nothing else - its menu was never asked a question, as a class of its own or on the
// EmulatorPlugin itself. The entry is shown by Nixx-Menus, a classic plugin in Plugins\ that relays
// to GameMenu below; src\Menus\Menus.cs has the contract. And none may be declared here: LiteBox
// loads both roots and would show the entry twice.
//
// WHOSE GAME IS IT: the game's OWN emulator (IGame.EmulatorId), the one Play uses - not whatever
// emulator its platform happens to have. Ours when that emulator's executable is Vita3K's, the same
// test GetApplicableEmulators claims an emulator by.
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
                var id = game?.EmulatorId;
                if (game == null) why = "no game";
                else if (string.IsNullOrWhiteSpace(id)) why = "no emulator of its own";
                else
                {
                    var emulator = PluginHelper.DataManager?.GetEmulatorById(id);
                    if (emulator == null) why = "emulator " + id + " not found" + (PluginHelper.DataManager == null ? " (no data manager)" : "");
                    else
                    {
                        ours = Vita3kPaths.IsVita3kExecutable(emulator.ApplicationPath);
                        why = "emulator " + emulator.Title + " (" + emulator.ApplicationPath + ")";
                    }
                }
            }
            catch (Exception ex) { why = ex.GetType().Name + ": " + ex.Message; }

            // Asked at every right-click: said once per game and answer, not every time.
            var key = (game == null ? "" : SafeId(game)) + "|" + ours + "|" + why;
            lock (Said) if (Said.Add(key) && Said.Count < 500)
                Log.Info("game menu: " + (game == null ? "(none)" : SafeTitle(game)) + " -> " + (ours ? "ours" : "not ours") + " - " + why);
            return ours;
        }

        private static readonly HashSet<string> Said = new HashSet<string>(StringComparer.Ordinal);
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
                    var inherited = InheritedLine(g);
                    return new Vita3kOptionsForm.Entry
                    {
                        Game = g, Title = Safe(() => g.Title), Rom = rom, Own = own, Inherited = inherited,
                        Options = Vita3kOptions.From(string.IsNullOrWhiteSpace(own) ? inherited : own, rom),
                    };
                }).ToList();

                using var form = new Vita3kOptionsForm(entries);
                form.ShowDialog(OwnerWindow());
            }
            catch (Exception ex) { Log.Warn("game menu", ex); }
        }

        /// <summary>The line a game runs with when it has none of its own: its emulator's line for the
        /// game's platform, else the emulator's own - LaunchBox's GetEffectiveCommandLine without the
        /// game's part, which is exactly the part the window edits.</summary>
        internal static string InheritedLine(IGame game)
        {
            try
            {
                var emulator = PluginHelper.DataManager?.GetEmulatorById(game?.EmulatorId);
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
            => games != null && games.Any(Vita3kGameMenu.IsOurs) ? new[] { Vita3kGameMenu.Caption } : new string[0];

        public static void Selected(string entry, IGame[] games)
        {
            if (entry == Vita3kGameMenu.Caption) Vita3kGameMenu.Open(games ?? new IGame[0]);
        }

        public static Image Icon => Vita3kGameMenu.Icon;
    }
}
