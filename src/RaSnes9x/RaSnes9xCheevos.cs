// RetroAchievements in RASnes9x: the login LaunchBox hands over, written where RetroAchievements' toolset reads it.
//
// THE TOOLSET, RA_Integration.dll, is not in RASnes9x's zip: the emulator offers to download it at its first start
// ("Install RetroAchievements toolset?" - measured 06/10; declined, it plays on and asks again next time). The plugin
// fetches it before, exactly as the emulator would - see RaSnes9xToolset.
//
// THE LOGIN is the toolset's (RAIntegration, src\services\Initialization.cpp + JsonFileConfiguration.cpp, read 06/10):
// a JSON file beside the executable, RAPrefs_<client name>.cfg - "RASnes9X" for Snes9x (EmulatorContext.cpp) - with
// "Username", "Token" and "Hardcore Active" (true when absent) among its other keys. The toolset reads it when the
// emulator starts and writes it back when it quits, so it is written before the launch and every other key is kept.
// The token is never logged.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LbIntegrations.RaSnes9x
{
    internal static class RaSnes9xCheevos
    {
        public const string PrefsName = "RAPrefs_RASnes9X.cfg";
        public const string ToolsetName = RaSnes9xToolset.FileName;

        public static string PrefsOf(string exe) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), PrefsName);

        /// <summary>Has the emulator fetched RetroAchievements' toolset yet?</summary>
        public static bool HasToolset(string exe)
        {
            try { return File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), ToolsetName)); }
            catch { return false; }
        }

        /// <summary>What the toolset has: the user name, whether a token is there, hardcore. Nulls when nothing is.</summary>
        public static (string User, bool HasToken, bool Hardcore) Read(string exe)
        {
            try
            {
                var path = PrefsOf(exe);
                if (!File.Exists(path)) return (null, false, true);
                var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
                string Str(string k) { try { return node?[k]?.GetValue<string>(); } catch { return null; } }
                bool hard = true;
                try { if (node?["Hardcore Active"] != null) hard = node["Hardcore Active"].GetValue<bool>(); } catch { }
                return (Str("Username"), !string.IsNullOrEmpty(Str("Token")), hard);
            }
            catch (Exception ex) { Log.Warn("could not read " + PrefsName, ex); return (null, false, true); }
        }

        /// <summary>The values the host gave written in, everything else kept. Null when done, else why not.</summary>
        public static string Write(string exe, string user, string token, bool? hardcore)
        {
            try
            {
                if (RaSnes9xLaunch.Running(exe)) return "RASnes9x is running - it would write its own login back when it quits.";
                var path = PrefsOf(exe);
                JsonObject node = null;
                if (File.Exists(path))
                    try { node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject; }
                    catch (Exception ex) { Log.Warn(PrefsName + " does not read as JSON - written anew", ex); }
                node ??= new JsonObject();
                bool changed = false;
                void Put(string key, JsonNode value)
                {
                    if (JsonNode.DeepEquals(node[key], value)) return;
                    node[key] = value;
                    changed = true;
                }
                // Only what the host actually gave: writing a blank over the user's login loses it without a word.
                if (!string.IsNullOrEmpty(user)) Put("Username", JsonValue.Create(user));
                if (!string.IsNullOrEmpty(token)) Put("Token", JsonValue.Create(token));
                if (hardcore.HasValue) Put("Hardcore Active", JsonValue.Create(hardcore.Value));
                if (!changed) return null;
                var tmp = path + ".lbip-tmp";
                File.WriteAllText(tmp, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tmp, path, overwrite: true);
                Log.Info("RetroAchievements login written for " + (user ?? "(the same user)") + (hardcore.HasValue ? ", hardcore " + (hardcore.Value ? "on" : "off") : ""));
                return null;
            }
            catch (Exception ex) { Log.Warn("could not write " + PrefsName, ex); return "Could not write RASnes9x's RetroAchievements login: " + ex.Message; }
        }
    }
}
