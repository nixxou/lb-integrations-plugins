// Nixx-Menus: the right-click entries of the pack's plugins on games, relayed.
//
// WHY A DLL OF ITS OWN, IN Plugins\. Measured on LaunchBox 14 (27/09): a plugin installed in the
// managed root, Local\Plugins, gets its EMULATOR role and nothing else. Its IGameMenuItemPlugin was
// never asked a single question - as a class of its own, then on the EmulatorPlugin object itself -
// while the same entry in a bare DLL under the classic Plugins\ root showed at once. It is also why
// that plugin never receives OnGameExited. So the menus live here, and this file holds no feature:
// each click goes back to the plugin it belongs to.
//
// THE CONTRACT IS A NAME, not a type. A pack plugin whose assembly is <X> exposes
//
//     public static class LbIntegrations.<X>.GameMenu
//     {
//         public static string[] Entries(IGame[] games);            // what it offers this selection
//         public static void Selected(string entry, IGame[] games); // one of them was clicked
//         public static Image Icon { get; }                         // optional
//     }
//
// and is found among the assemblies the host has already loaded. Nothing is referenced and nothing
// is shared but the SDK's own IGame, which the host provides to both sides - so there is no second
// copy of a contract type to keep in step between two plugin roots. A plugin returns no entry for a
// selection that holds none of its games; which games are its own is for it to say.
//
// It never throws into the host: a plugin that fails is left out of the menu, and logged.
//
// NOTHING IN BIGBOX (Mehdi, 29/09): these entries open settings windows, for a mouse and a desk -
// not for a TV and a controller.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Menus
{
    public sealed class NixxGameMenus : IGameMultiMenuItemPlugin
    {
        public IEnumerable<IGameMenuItem> GetMenuItems(IGame[] selectedGames)
        {
            var games = selectedGames ?? new IGame[0];
            var items = new List<IGameMenuItem>();
            if (games.Length == 0 || InBigBox) return items;

            foreach (var provider in Providers.All())
            {
                string[] entries;
                try { entries = provider.Entries(games) ?? new string[0]; }
                catch (Exception ex) { RelayLog.Warn(provider.Name + ".Entries", ex); continue; }
                foreach (var entry in entries.Where(e => !string.IsNullOrWhiteSpace(e)))
                    items.Add(new Item(provider, entry));
            }
            return items;
        }

        private static readonly bool InBigBox = IsBigBox();

        private static bool IsBigBox()
        {
            try { return string.Equals(System.Diagnostics.Process.GetCurrentProcess().ProcessName, "BigBox", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private sealed class Item : IGameMenuItem
        {
            private readonly Provider _provider;
            public Item(Provider provider, string caption) { _provider = provider; Caption = caption; }

            public string Caption { get; }
            public Image Icon => _provider.Icon;
            public bool Enabled => true;
            public IEnumerable<IGameMenuItem> Children => null;

            public void OnSelect(IGame[] games)
            {
                try { _provider.Selected(Caption, games ?? new IGame[0]); }
                catch (Exception ex) { RelayLog.Warn(_provider.Name + ".Selected", ex); }
            }
        }
    }

    /// <summary>One plugin's GameMenu class, bound once.</summary>
    internal sealed class Provider
    {
        public string Name;
        public MethodInfo EntriesMethod, SelectedMethod;
        public PropertyInfo IconProperty;
        private Image _icon;

        public string[] Entries(IGame[] games) => (string[])EntriesMethod.Invoke(null, new object[] { games });
        public void Selected(string entry, IGame[] games) => SelectedMethod.Invoke(null, new object[] { entry, games });

        /// <summary>The plugin's own icon, or the application one. NEVER NULL: a host turns it into
        /// its own menu image.</summary>
        public Image Icon
        {
            get
            {
                if (_icon != null) return _icon;
                try { _icon = IconProperty?.GetValue(null) as Image; } catch { }
                return _icon ??= SystemIcons.Application.ToBitmap();
            }
        }
    }

    internal static class Providers
    {
        /// <summary>Assemblies already looked at, with what they gave - null for "none". An assembly
        /// is only ever looked at once: a right-click costs a dictionary lookup per loaded assembly.</summary>
        private static readonly Dictionary<Assembly, Provider> Seen = new Dictionary<Assembly, Provider>();

        public static List<Provider> All()
        {
            var found = new List<Provider>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Provider provider;
                lock (Seen)
                {
                    if (!Seen.TryGetValue(asm, out provider))
                        Seen[asm] = provider = Bind(asm);
                }
                if (provider != null) found.Add(provider);
            }
            return found.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static Provider Bind(Assembly asm)
        {
            try
            {
                if (asm.IsDynamic) return null;
                var name = asm.GetName().Name;
                if (string.IsNullOrEmpty(name)) return null;
                var type = asm.GetType("LbIntegrations." + name + ".GameMenu", throwOnError: false);
                if (type == null) return null;

                var flags = BindingFlags.Public | BindingFlags.Static;
                var entries = type.GetMethod("Entries", flags, null, new[] { typeof(IGame[]) }, null);
                var selected = type.GetMethod("Selected", flags, null, new[] { typeof(string), typeof(IGame[]) }, null);
                if (entries == null || entries.ReturnType != typeof(string[]) || selected == null)
                {
                    // THE ONE WAY A NAME CONTRACT GOES QUIETLY WRONG: the class is there and its
                    // methods do not match - or match against ANOTHER copy of IGame. Said, once.
                    RelayLog.Warn(type.FullName + " is there but not as the relay expects it (Entries(IGame[]) -> string[], Selected(string, IGame[]))");
                    return null;
                }
                RelayLog.Info("relaying the game menu of " + name);
                return new Provider
                {
                    Name = name, EntriesMethod = entries, SelectedMethod = selected,
                    IconProperty = type.GetProperty("Icon", flags),
                };
            }
            catch (Exception ex) { RelayLog.Warn("looking at " + asm.FullName, ex); return null; }
        }
    }

    /// <summary>A log of its own, beside the plugins' - %LOCALAPPDATA%\lb-integrations-plugins\menus.log -
    /// truncated at each start of the host.</summary>
    internal static class RelayLog
    {
        private static readonly object Gate = new object();
        private static string _path;
        private static bool _started;

        public static void Info(string message) => Write(message);
        public static void Warn(string message, Exception ex = null)
            => Write("WARN " + message + (ex != null ? " - " + (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message : ""));

        private static void Write(string line)
        {
            try
            {
                lock (Gate)
                {
                    if (_path == null)
                    {
                        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lb-integrations-plugins");
                        Directory.CreateDirectory(dir);
                        _path = Path.Combine(dir, "menus.log");
                    }
                    var text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + line + Environment.NewLine;
                    if (!_started) { File.WriteAllText(_path, "=== " + Environment.ProcessPath + Environment.NewLine + text); _started = true; }
                    else File.AppendAllText(_path, text);
                }
            }
            catch { }
        }
    }
}
