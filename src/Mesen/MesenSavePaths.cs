// Nothing beside the game, and a folder per platform (Mehdi 07/10: "par plateforme si possible, dans le répertoire de
// l'émulateur").
//
// MESEN NEVER WRITES BESIDE THE GAME (read at both tags: BatteryManager and SaveStateManager write into the save / state
// folders; measured 18/10: every .sav, .srm, .rtc and .mss in <home>\Saves\ and <home>\SaveStates\). Its folders are ONE
// setting for every system - Preferences.OverrideSaveDataFolder / SaveDataFolder, OverrideSaveStateFolder / SaveStateFolder,
// OverrideScreenshotFolder / ScreenshotFolder (PreferencesConfig.cs) - and two games of two systems with one name share a
// .sav there. So before each launch the three are set to the game's PLATFORM:
//
//     <home>\Saves\<platform>\               the battery files
//     <home>\Saves\<platform>\States\        the states (<rom>_<n>.mss)
//     <home>\Saves\<platform>\Screenshots\
//
// written RELATIVE ("Saves\Nintendo Entertainment System"), as Mesen resolves them from its home, its current folder
// (measured 18/10: the .sav landed in inst1\Saves\Nintendo Entertainment System\; Mesen made the folder), so a moved
// install keeps working. They are put back after the session (MesenSession): Mesen opened by hand writes in its own
// <home>\Saves\ and \SaveStates\ as before, and what it wrote there for a game is MOVED into the game's platform folder at
// that game's next launch (Migrate - Mesen's own files, never over one already there). A folder the user set himself in
// Mesen is his: kept, nothing set, nothing moved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using LbIntegrations.Lbip;

namespace LbIntegrations.Mesen
{
    internal static class MesenSavePaths
    {
        /// <summary>The three folder settings: the switch, the folder, and the sub-folder of the platform's (null: itself).</summary>
        internal static readonly (string Override, string Folder, string Sub)[] Settings =
        {
            ("Preferences/OverrideSaveDataFolder", "Preferences/SaveDataFolder", null),
            ("Preferences/OverrideSaveStateFolder", "Preferences/SaveStateFolder", "States"),
            ("Preferences/OverrideScreenshotFolder", "Preferences/ScreenshotFolder", "Screenshots"),
        };

        /// <summary>A platform's name as a folder: what Windows does not take, out.</summary>
        internal static string FolderName(string platform)
        {
            var bad = Path.GetInvalidFileNameChars();
            var s = new string((platform ?? "").Trim().Select(c => bad.Contains(c) ? '_' : c).ToArray()).TrimEnd('.', ' ');
            return s.Length == 0 ? "Other" : s;
        }

        /// <summary>"Saves\&lt;platform&gt;" (+ "\States" ...), relative to Mesen's home.</summary>
        internal static string Relative(string platform, string sub) => "Saves\\" + FolderName(platform) + (sub == null ? "" : "\\" + sub);

        /// <summary>A folder value the plugin wrote: Saves\&lt;something&gt;[\States|\Screenshots], relative.</summary>
        internal static bool IsOurs(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var v = value.Trim().Replace('/', '\\').TrimEnd('\\');
            if (Path.IsPathRooted(v) || !v.StartsWith("Saves\\", StringComparison.OrdinalIgnoreCase)) return false;
            var parts = v.Split('\\');
            return parts.Length == 2 || (parts.Length == 3 && (parts[2].Equals("States", StringComparison.OrdinalIgnoreCase) || parts[2].Equals("Screenshots", StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>The game's platform folders set in <paramref name="json"/> (not saved here), where the user set none of his
        /// own. What was set is added to <paramref name="forced"/>, with what was there, for the session to put back. False
        /// when the user's own save folder is kept (nothing is moved then).</summary>
        public static bool Ensure(MesenJson json, string platform, List<MesenSession.Forced> forced)
        {
            bool ours = true;
            try
            {
                if (!json.Readable || string.IsNullOrWhiteSpace(platform)) return false;
                foreach (var (ov, folder, sub) in Settings)
                {
                    var on = json.GetBool(ov) ?? false;
                    var value = json.GetString(folder);
                    if (on && !IsOurs(value)) { if (sub == null) ours = false; continue; }   // the user's own
                    var want = Relative(platform, sub);
                    // Ours already (a session that could not be put back): what goes back after this one is Mesen's own default.
                    bool stale = on && IsOurs(value);
                    forced.Add(new MesenSession.Forced { Path = ov, Before = stale ? "false" : json.Get(ov)?.ToJsonString(), Value = "true" });
                    forced.Add(new MesenSession.Forced { Path = folder, Before = stale ? "\"\"" : json.Get(folder)?.ToJsonString(), Value = want });
                    json.Set(ov, true);
                    json.Set(folder, want);
                }
            }
            catch (Exception ex) { Log.Warn("could not set Mesen's folders for the platform - it writes in its own Saves folders", ex); return false; }
            return ours;
        }

        /// <summary>What Mesen wrote for this game in its own flat folders - &lt;home&gt;\Saves\&lt;name&gt;&lt;battery ext&gt; and
        /// &lt;home&gt;\SaveStates\&lt;name&gt;_&lt;n&gt;.mss (+ its thumbnail) - moved into the platform's folders. Never over a
        /// file there. <paramref name="name"/>: the rom's name as Mesen names its files (the file inside an archive).</summary>
        public static int Migrate(string exe, string platform, MesenSystem system, string name)
        {
            int moved = 0;
            try
            {
                if (string.IsNullOrWhiteSpace(name) || system == null) return 0;
                var home = MesenLaunch.HomeOf(exe);
                void Say(string m) => Log.Info(m);
                var saves = Path.Combine(home, Relative(platform, null));
                foreach (var ext in system.Battery.Distinct(StringComparer.OrdinalIgnoreCase))
                    if (LbipBesideGame.Bring(Path.Combine(home, "Saves", name + ext), saves, copy: false, Say)) moved++;
                var flatStates = Path.Combine(home, "SaveStates");
                var states = Path.Combine(home, Relative(platform, "States"));
                if (Directory.Exists(flatStates))
                    foreach (var f in Directory.EnumerateFiles(flatStates, name + "_*.mss*"))
                    {
                        var rest = Path.GetFileName(f).Substring(name.Length + 1);
                        var slot = rest.Split('.')[0];
                        if (!int.TryParse(slot, out _)) continue;   // another game whose name begins as this one's
                        if (LbipBesideGame.Bring(f, states, copy: false, Say)) moved++;
                    }
                if (moved > 0) Log.Info("saves: " + moved + " file(s) of " + name + " moved from Mesen's own folders into " + Relative(platform, null));
            }
            catch (Exception ex) { Log.Warn("saves: could not bring the game's files into its platform's folder", ex); }
            return moved;
        }
    }
}
