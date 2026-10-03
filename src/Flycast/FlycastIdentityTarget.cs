// "Apply to my emulators" of the Nixx window's "Your console" tab, for Flycast: the identity's language as the Dreamcast's,
// [config] Dreamcast.Language of emu.cfg (FlycastPlugin.ApplyIdentity writes it at a first install). A game's own Language
// option still goes over it for its session. Found by the relay by its name, LbIntegrations.Flycast.IdentityTarget.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using LbIntegrations.Identity;

namespace LbIntegrations.Flycast
{
    internal static class IdentityTarget
    {
        private static readonly string[] Languages = { "Japanese", "English", "German", "French", "Spanish", "Italian" };

        private static FlycastLayout Layout()
        {
            var root = PackIdentity.LaunchBoxRoot();
            var exe = root == null ? null : FlycastPaths.FindExecutable(Path.Combine(root, "Emulators", "Nixx-Flycast"));
            return exe == null ? null : FlycastPaths.Resolve(exe);
        }

        private static bool Running()
        {
            try { return FlycastPaths.ExecutableNames.Any(n => Process.GetProcessesByName(Path.GetFileNameWithoutExtension(n)).Length > 0); }
            catch { return false; }
        }

        private static int? Current(FlycastLayout layout)
        {
            if (!File.Exists(layout.ConfigFile)) return null;
            bool inConfig = false;
            foreach (var raw in File.ReadAllLines(layout.ConfigFile))
            {
                var l = raw.Trim();
                if (l.StartsWith("[")) { inConfig = l.Equals("[config]", StringComparison.OrdinalIgnoreCase); continue; }
                if (!inConfig) continue;
                var c = l.Split('=');
                if (c.Length == 2 && c[0].Trim().Equals("Dreamcast.Language", StringComparison.OrdinalIgnoreCase) && int.TryParse(c[1].Trim(), out var v)) return v;
            }
            return null;
        }

        private static string Say(int? v) => v == null ? "Flycast's default (English)" : v >= 0 && v < Languages.Length ? Languages[v.Value] : "the BIOS's own language";

        public static string[][] Plan()
        {
            var id = PackIdentity.Load();
            var layout = id == null ? null : Layout();
            if (layout?.ConfigFile == null) return new string[0][];
            return new[] { PackIdentity.Row("flycast", "Flycast - the Dreamcast's language", Say(Current(layout)), Say(id.DreamcastLanguage()),
                                            Running() ? "Flycast is running - close it first" : "") };
        }

        public static string Apply(string key)
        {
            try
            {
                var id = PackIdentity.Load();
                var layout = id == null ? null : Layout();
                if (layout?.ConfigFile == null) return "no Flycast";
                if (Running()) return "Flycast is running - close it first";
                var lines = File.Exists(layout.ConfigFile) ? File.ReadAllLines(layout.ConfigFile).ToList() : new System.Collections.Generic.List<string>();
                var line = "Dreamcast.Language = " + id.DreamcastLanguage().ToString(CultureInfo.InvariantCulture);
                int section = lines.FindIndex(l => l.Trim().Equals("[config]", StringComparison.OrdinalIgnoreCase));
                if (section < 0) { if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add(""); lines.Add("[config]"); lines.Add(line); }
                else
                {
                    int end = lines.FindIndex(section + 1, l => l.TrimStart().StartsWith("["));
                    if (end < 0) end = lines.Count;
                    int at = lines.FindIndex(section + 1, end - section - 1, l => l.Split('=')[0].Trim().Equals("Dreamcast.Language", StringComparison.OrdinalIgnoreCase));
                    if (at >= 0) lines[at] = line; else lines.Insert(section + 1, line);
                }
                File.WriteAllText(layout.ConfigFile, string.Join("\r\n", lines) + "\r\n");
                Log.Info("console identity applied: " + line);
                return null;
            }
            catch (Exception ex) { Log.Warn("console identity: could not apply", ex); return ex.Message; }
        }
    }
}
