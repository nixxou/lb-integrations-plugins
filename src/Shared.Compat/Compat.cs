// The pack's patches to LaunchBox, each one switchable (Mehdi, 09/10 - docs\compat-page-plan.md): what LaunchBox does not
// do, the bugs routed around, the safeguards, what the pack changes in LaunchBox's data and where it hooks in. Many rest on
// LaunchBox's internals found by reflection; a LaunchBox update can break one, and the day it fixes a bug the patch must
// be dropped - so every patch can be turned off from the "Compatibility" page of the Nixx settings.
//
// READ ONCE, AT START-UP. The page writes Local\Plugins\.data\nixx-compat.ini ([patches] id=on|off, a missing key = on)
// on Apply and asks for a restart; each plugin (its own copy of this code) reads the file the first time it asks, which
// is at its start, and keeps that answer for the whole session - no patch changes state in the middle of one.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Compat
{
    internal enum CompatGroup { Missing, Bug, Safeguard, Data, Hook }

    internal sealed class CompatPatch
    {
        public string Id = "";
        public CompatGroup Group;
        public string Title = "";
        public string Problem = "", Example = "", Does = "", Without = "";
        /// <summary>Another patch this one needs (greyed out when that one is off).</summary>
        public string? Needs;
        /// <summary>Asked before turning it off: what can be lost. Null: no confirmation.</summary>
        public string? OffWarning;
        /// <summary>False: shown for information, no switch (always on).</summary>
        public bool Switchable = true;
        /// <summary>The reflection checks of <see cref="CompatChecks"/> it relies on.</summary>
        public string[] Checks = Array.Empty<string>();
        /// <summary>The bug test of <see cref="CompatChecks"/>, if it can be tested without touching anything.</summary>
        public string? BugTest;
        /// <summary>Where a plugin already has its own switch for it (shown, not duplicated).</summary>
        public string? OwnSwitches;
    }

    internal static class Compat
    {
        public const string FileName = "nixx-compat.ini";

        private static Dictionary<string, bool>? _boot;
        private static readonly object Gate = new object();

        /// <summary>True when the patch <paramref name="id"/> is on for this session - as the file said at start-up.</summary>
        public static bool On(string id)
        {
            var boot = _boot;
            if (boot == null) lock (Gate) boot = _boot ??= Load(PathOf(null));
            if (boot.TryGetValue(id, out var on) && !on) return false;
            var p = CompatRegistry.Find(id);
            return p?.Needs == null || On(p.Needs);
        }

        /// <summary>The file, under the LaunchBox root (this runs inside &lt;root&gt;\Core).</summary>
        public static string PathOf(string? root)
        {
            root ??= Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
            return Path.Combine(root, "Local", "Plugins", ".data", FileName);
        }

        /// <summary>The switches in <paramref name="path"/> (only those written; a missing one is on).</summary>
        public static Dictionary<string, bool> Load(string path) => LoadSection(path, "patches");

        /// <summary>The page's own settings: [page] (view = simple | advanced) and [beforeSafe] (the switches as they were when the
        /// safe mode was turned on - empty when it is not). Never read by a patch.</summary>
        public static (string View, Dictionary<string, bool> BeforeSafe) LoadPage(string path)
        {
            string view = "simple";
            try
            {
                if (File.Exists(path))
                {
                    bool inPage = false;
                    foreach (var raw in File.ReadAllLines(path))
                    {
                        var line = raw.Trim();
                        if (line.StartsWith("[")) { inPage = line.Equals("[page]", StringComparison.OrdinalIgnoreCase); continue; }
                        if (inPage && line.StartsWith("view=", StringComparison.OrdinalIgnoreCase)) view = line.Substring(5).Trim().ToLowerInvariant() == "advanced" ? "advanced" : "simple";
                    }
                }
            }
            catch { }
            return (view, LoadSection(path, "beforeSafe"));
        }

        private static Dictionary<string, bool> LoadSection(string path, string section)
        {
            var d = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(path)) return d;
                bool inPatches = false;
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                    if (line.StartsWith("[")) { inPatches = line.Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase); continue; }
                    if (!inPatches) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var v = line.Substring(eq + 1).Trim();
                    d[line.Substring(0, eq).Trim()] = !(v.Equals("off", StringComparison.OrdinalIgnoreCase) || v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase));
                }
            }
            catch { }
            return d;
        }

        /// <summary>Writes every switchable patch's state (the page's Apply).</summary>
        public static void Save(string path, IReadOnlyDictionary<string, bool> state, string? view = null, IReadOnlyDictionary<string, bool>? beforeSafe = null)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var lines = new List<string>
            {
                "; Nixx integration plugins - the Compatibility page of the Nixx settings writes this file.",
                "; Read by every plugin when LaunchBox starts: restart LaunchBox (or BigBox) after a change.",
                "[patches]",
            };
            foreach (var p in CompatRegistry.All.Where(p => p.Switchable))
                lines.Add(p.Id + "=" + (state.TryGetValue(p.Id, out var on) && !on ? "off" : "on"));
            if (view != null) { lines.Add("[page]"); lines.Add("view=" + view); }
            if (beforeSafe != null && beforeSafe.Count > 0)
            {
                lines.Add("[beforeSafe]");
                foreach (var kv in beforeSafe) lines.Add(kv.Key + "=" + (kv.Value ? "on" : "off"));
            }
            var tmp = path + ".tmp";
            File.WriteAllLines(tmp, lines);
            File.Move(tmp, path, overwrite: true);
        }

        /// <summary>For the probe: the switches as if read at start-up.</summary>
        internal static void UseForTests(Dictionary<string, bool>? state) { lock (Gate) _boot = state; }
    }
}
