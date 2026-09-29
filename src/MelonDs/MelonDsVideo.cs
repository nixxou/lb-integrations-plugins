// A game's OWN video settings for melonDS (Mehdi, 29/09) - the ones of melonDS's Video settings window.
//
// melonDS HAS NO PER-GAME SETTING, AND NO WAY TO BE GIVEN ANOTHER CONFIG FILE (CLI.cpp: -b, -f, -a, -A,
// nothing else). These are global keys of melonDS.toml (Config.cpp):
//     [3D]       Renderer          0 software, 1 OpenGL (classic), 2 OpenGL (compute shader)
//     [3D.Soft]  Threaded
//     [3D.GL]    ScaleFactor 1-16, BetterPolygons, HiresCoordinates
//     [Screen]   UseGL, VSync, VSyncInterval 1-20
// So a game's own values are written into it for the length of its session, and the user's own values
// put back afterwards:
//   - a game's values live in <install>\lbip-video.tsv, one line per LaunchBox game id - only the games
//     that have their own; the others run on melonDS's settings as they are;
//   - AT LAUNCH, the values about to be replaced are written down FIRST (<install>\lbip-video.restore),
//     then the game's are written - melonDS reads its file when it starts;
//   - WHEN melonDS HAS QUIT - it rewrites its whole file on exit, the game's values with it - the values
//     written down go back, and the note is deleted. A change made to the video settings DURING such a
//     game is therefore not kept (the log says the session ended; the user's settings come back);
//   - A NOTE STILL THERE - the host or the machine went mid-session - is put back by the next launch and
//     by the plugin's start-up check, before anything else. Opening melonDS on its own in between shows
//     the game's values; changed there, they are put back over later (Mehdi: "pas un drame").

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LbIntegrations.Dsi;

namespace LbIntegrations.MelonDs
{
    internal static class MelonDsVideo
    {
        internal sealed class Setting
        {
            public string Id, Table, Key;
            public bool IsBool;
            public int Min, Max;
            public string Default;
        }

        internal static readonly Setting[] Settings =
        {
            new Setting { Id = "Renderer",         Table = "3D",      Key = "Renderer",         Min = 0, Max = 2,  Default = "0" },
            new Setting { Id = "Threaded",         Table = "3D.Soft", Key = "Threaded",         IsBool = true,     Default = "true" },
            new Setting { Id = "ScaleFactor",      Table = "3D.GL",   Key = "ScaleFactor",      Min = 1, Max = 16, Default = "1" },
            new Setting { Id = "BetterPolygons",   Table = "3D.GL",   Key = "BetterPolygons",   IsBool = true,     Default = "false" },
            new Setting { Id = "HiresCoordinates", Table = "3D.GL",   Key = "HiresCoordinates", IsBool = true,     Default = "true" },
            new Setting { Id = "UseGL",            Table = "Screen",  Key = "UseGL",            IsBool = true,     Default = "false" },
            new Setting { Id = "VSync",            Table = "Screen",  Key = "VSync",            IsBool = true,     Default = "false" },
            new Setting { Id = "VSyncInterval",    Table = "Screen",  Key = "VSyncInterval",    Min = 1, Max = 20, Default = "1" },
        };

        private const string StoreName = "lbip-video.tsv";
        private const string RestoreName = "lbip-video.restore";

        private static string StorePath(string installDir) => installDir == null ? null : Path.Combine(installDir, StoreName);
        private static string RestorePath(string installDir) => installDir == null ? null : Path.Combine(installDir, RestoreName);

        /// <summary>A value as melonDS.toml wants it: true/false, or a number in range. Null when it is neither.</summary>
        internal static string Normal(Setting s, string value)
        {
            value = (value ?? "").Trim();
            if (s.IsBool)
                return value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true"
                     : value.Equals("false", StringComparison.OrdinalIgnoreCase) ? "false" : null;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= s.Min && n <= s.Max
                ? n.ToString(CultureInfo.InvariantCulture) : null;
        }

        // ── a game's own values ──────────────────────────────────────────────

        /// <summary>The game's own values, or null when it runs on melonDS's settings.</summary>
        public static Dictionary<string, string> Load(string installDir, string gameId)
        {
            try
            {
                var path = StorePath(installDir);
                if (path == null || string.IsNullOrWhiteSpace(gameId) || !File.Exists(path)) return null;
                foreach (var line in File.ReadAllLines(path))
                {
                    var f = line.Split('\t');
                    if (f.Length < 2 || !string.Equals(f[0], gameId, StringComparison.OrdinalIgnoreCase)) continue;
                    var values = Parse(f[1]);
                    return values.Count > 0 ? values : null;
                }
            }
            catch (Exception ex) { Log.Warn("video: could not read the games' own settings", ex); }
            return null;
        }

        /// <summary>Give a game its own values - or take them away, with null.</summary>
        public static void Save(string installDir, string gameId, Dictionary<string, string> values)
        {
            try
            {
                var path = StorePath(installDir);
                if (path == null || string.IsNullOrWhiteSpace(gameId)) return;
                var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
                lines.RemoveAll(l => l.Split('\t')[0].Equals(gameId, StringComparison.OrdinalIgnoreCase));
                if (values != null && values.Count > 0) lines.Add(gameId + "\t" + Format(values));
                MelonDsToml.WriteAtomicBytes(path, Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + (lines.Count > 0 ? "\r\n" : "")));
            }
            catch (Exception ex) { Log.Warn("video: could not save the game's own settings", ex); }
        }

        /// <summary>melonDS's own values now - its default for a key the file does not hold.</summary>
        public static Dictionary<string, string> Current(string configFile) => TryCurrent(configFile, out var v) ? v : v;

        /// <summary>The same, FALSE when the file could not be read: then the values are only defaults,
        /// and must never be written down as somebody's own.</summary>
        public static bool TryCurrent(string configFile, out Dictionary<string, string> values)
        {
            bool known = true;
            values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var group in Settings.GroupBy(s => s.Table))
            {
                if (!MelonDsToml.TryRead(configFile, group.Key, out var read, group.Select(s => s.Key).ToArray())) known = false;
                foreach (var s in group)
                    values[s.Id] = (read.TryGetValue(s.Key, out var v) ? Normal(s, v) : null) ?? s.Default;
            }
            return known;
        }

        // ── the session ──────────────────────────────────────────────────────

        /// <summary>Write the game's own values for its session, having written down the ones they
        /// replace. True when there were any - the caller then watches for melonDS to quit.</summary>
        public static bool Apply(MelonDsLayout layout, string gameId)
        {
            try
            {
                var own = Load(layout?.InstallDir, gameId);
                if (own == null || layout.ConfigFile == null) return false;
                // melonDS's own, to be put back - and so they must be REALLY its own, never defaults
                // standing in for a file that could not be read.
                if (!TryCurrent(layout.ConfigFile, out var before))
                {
                    Log.Warn("video: melonDS's configuration could not be read - this game runs on melonDS's settings this time");
                    return false;
                }
                var replaced = own.Keys.Where(before.ContainsKey).ToDictionary(k => k, k => before[k]);
                if (own.All(kv => replaced.TryGetValue(kv.Key, out var was) && was == kv.Value))
                {
                    Log.Info("video: this game's own settings are melonDS's already - nothing to write");
                    return false;
                }
                // THE NOTE FIRST: written down before a byte of melonDS.toml changes.
                MelonDsToml.WriteAtomicBytes(RestorePath(layout.InstallDir), Encoding.UTF8.GetBytes(Format(replaced)));
                var error = WriteValues(layout.ConfigFile, own, force: true);
                if (error != null) { Log.Warn("video: the game's own settings were not written - " + error); Restore(layout, "the write failed"); return false; }
                Log.Info("video: this game's own settings for its session - " + Format(own));
                return true;
            }
            catch (Exception ex) { Log.Warn("video: could not apply the game's own settings", ex); return false; }
        }

        /// <summary>Put back the values a session replaced, if a note says there are any. Never while
        /// melonDS runs - it would write its own over them when it quits.</summary>
        public static void Restore(MelonDsLayout layout, string why)
        {
            try
            {
                var note = RestorePath(layout?.InstallDir);
                if (note == null || !File.Exists(note) || layout.ConfigFile == null) return;
                if (DsiNand.EmulatorRunning()) { Log.Info("video: melonDS is running - its settings go back once it has quit"); return; }
                var values = Parse(File.ReadAllText(note));
                var error = WriteValues(layout.ConfigFile, values, force: false);
                if (error != null) { Log.Warn("video: melonDS's own settings could not go back yet - " + error); return; }
                File.Delete(note);
                Log.Info("video: melonDS's own settings are back (" + why + ")");
            }
            catch (Exception ex) { Log.Warn("video: could not put melonDS's own settings back", ex); }
        }

        /// <summary>Wait for the melonDS of this launch to come and go, then put its settings back.</summary>
        public static void RestoreWhenDone(MelonDsLayout layout)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var armed = DateTime.UtcNow;
                    bool appeared = false;
                    while ((DateTime.UtcNow - armed).TotalSeconds < 120)
                    {
                        if (DsiNand.EmulatorRunning()) { appeared = true; break; }
                        System.Threading.Thread.Sleep(500);
                    }
                    if (appeared)
                        while (DsiNand.EmulatorRunning()) System.Threading.Thread.Sleep(500);
                    // melonDS writes its file as it quits: a moment for that to land.
                    System.Threading.Thread.Sleep(1000);
                    Restore(layout, appeared ? "the session is over" : "melonDS never started");
                }
                catch (Exception ex) { Log.Warn("video: watching for the end of the session", ex); }
            });
        }

        private static string WriteValues(string configFile, Dictionary<string, string> values, bool force)
        {
            foreach (var group in Settings.Where(s => values.ContainsKey(s.Id)).GroupBy(s => s.Table))
            {
                var wanted = group.ToDictionary(s => s.Key, s => values[s.Id], StringComparer.Ordinal);
                var error = MelonDsToml.Write(configFile, group.Key, wanted, force);
                if (error != null) return error;
            }
            return null;
        }

        private static string Format(Dictionary<string, string> values)
            => string.Join(";", Settings.Where(s => values.ContainsKey(s.Id)).Select(s => s.Id + "=" + values[s.Id]));

        private static Dictionary<string, string> Parse(string text)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var part in (text ?? "").Trim().Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                var s = Settings.FirstOrDefault(x => x.Id == part.Substring(0, eq).Trim());
                var v = s == null ? null : Normal(s, part.Substring(eq + 1));
                if (v != null) values[s.Id] = v;
            }
            return values;
        }
    }
}
