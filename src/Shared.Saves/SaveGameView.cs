// A game (or version) seen as if another emulator ran it - for LaunchBox's own integrations, asked by a front desk.
//
// WHY (measured and read 09/10, docs\saves-lot3.md §6.6c): RetroArch's integration finds its states folder from the core
// on the GAME's command line (GetSaveStateDirectory: game.GetEffectiveCommandLine(), its "-L cores\x_libretro.dll", then
// info\x_libretro.info's corename) whenever sort_savestates_enable is on - the default. A game whose emulator is ours
// has no "-L": no folder, so RetroArch listed none of its states on such a game, and our restore of one (StockRestore,
// the same function) found no folder either. Its saves go another way and were listed.
//
// The view answers GetEffectiveCommandLine with the line of the emulator being asked, for the game's platform (the
// line LaunchBox would build had the game run on it), and forwards every other member to the real object - so the
// rows it produces carry the real game's id, path, platform. Used only for LaunchBox's integrations (lb.*): ours read
// their own emulator.

#nullable enable

using System;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Saves
{
    internal static class SaveGameView
    {
        /// <summary><paramref name="game"/> as if <paramref name="emulator"/> ran it; the game itself when there is no line.</summary>
        public static IGame As(IGame game, IEmulator emulator)
        {
            var line = LineFor(emulator, Safe(() => game.Platform));
            return line == null ? game : Make<IGame>(game, line);
        }

        /// <summary><paramref name="app"/> (a version of <paramref name="platform"/>'s game) as if <paramref name="emulator"/> ran it.</summary>
        public static IAdditionalApplication As(IAdditionalApplication app, IEmulator emulator, string? platform)
        {
            var line = LineFor(emulator, platform);
            return line == null ? app : Make<IAdditionalApplication>(app, line);
        }

        /// <summary>The emulator's command line for the platform, else its own; null when it has none.</summary>
        internal static string? LineFor(IEmulator? emulator, string? platform)
        {
            try
            {
                var line = emulator?.GetAllEmulatorPlatforms()?.FirstOrDefault(p => string.Equals(p?.Platform, platform, StringComparison.OrdinalIgnoreCase))?.CommandLine;
                if (string.IsNullOrWhiteSpace(line)) line = emulator?.CommandLine;
                return string.IsNullOrWhiteSpace(line) ? null : line;
            }
            catch { return null; }
        }

        private static T Make<T>(T real, string line) where T : class
        {
            var proxy = DispatchProxy.Create<T, Forward>();
            var f = (Forward)(object)proxy;
            f.Real = real; f.Line = line;
            return proxy;
        }

        /// <summary>The real object behind a view, else <paramref name="o"/> itself. A view has the SDK's members only - not
        /// LaunchBox's own (Saves, review 10/10: the persisted cards of a game seen through a view read as none).</summary>
        internal static T Real<T>(T o) where T : class => o is Forward f && f.Real is T r ? r : o;

        private static T? Safe<T>(Func<T> f) { try { return f(); } catch { return default; } }

        /// <summary>Forwards every call to the real object, except GetEffectiveCommandLine.</summary>
        public class Forward : DispatchProxy
        {
            internal object? Real;
            internal string? Line;

            protected override object? Invoke(MethodInfo? method, object?[]? args)
            {
                if (method == null) return null;
                if (method.Name == "GetEffectiveCommandLine" && (args == null || args.Length == 0)) return Line;
                try { return method.Invoke(Real, args); }
                catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
            }
        }
    }
}
