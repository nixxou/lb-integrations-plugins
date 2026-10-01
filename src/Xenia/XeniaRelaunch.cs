// A game that is only a launcher for another - Minecraft's disc (Mehdi, 01/10): its default.xex asks the console to start
// a package the disc carries (XamContentLaunchImage) or another executable (XamLoaderLaunchTitle). Xenia cannot switch
// title in flight: it unpacks the package into its content folder, writes the request into launch_data.bin beside its
// executable, says "Please close Xenia and launch it again", and reads that file at its next start (xam_module.cc,
// xenia_main.cc). From LaunchBox that next start is handed the disc again - the launcher runs again, and asks again.
//
// SO THE REQUEST IS REMEMBERED, per game: once Xenia has written launch_data.bin during a game's session, the plugin reads
// it (host_path, launch_path), deletes it, and from then on hands Xenia that path as --target - the launcher is skipped,
// the real game starts. A file found at a launch (the host went before the watcher saw it) is the last launched game's.
//
//   launch_data.bin: u16 length + host path, u16 length + launch path, u32 flags, u16 length + launch data (all
//   little-endian, as SaveLoaderData fwrites them). Launch data a launcher hands its game is lost - Xenia keeps it only
//   in that file; none was seen on Minecraft's.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LbIntegrations.Xenia
{
    internal static class XeniaRelaunch
    {
        private const string FileName = "launch_data.bin";
        private static readonly object Gate = new object();

        private static string MapPath => Path.Combine(XeniaSettings.Dir, "relaunch.tsv");
        private static string LastPath => Path.Combine(XeniaSettings.Dir, "relaunch-last.txt");

        /// <summary>At a launch: a request left behind is adopted, then the game's own redirection, if any - the path to hand
        /// Xenia as --target and the module to start in it (null for default.xex). Null when the game is not a launcher.</summary>
        /// <summary>First thing at a launch: a request a previous session left behind is adopted - for the game last launched,
        /// or this one when none is known - so that everything after it sees the game as what it really starts.</summary>
        public static void AdoptPending(string exe, string rom)
        {
            try
            {
                var file = Path.Combine(Path.GetDirectoryName(exe), FileName);
                if (File.Exists(file))
                {
                    var last = File.Exists(LastPath) ? File.ReadAllText(LastPath).Trim() : null;
                    Adopt(file, string.IsNullOrEmpty(last) ? rom : last);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(LastPath));
                File.WriteAllText(LastPath, rom);
            }
            catch (Exception ex) { Log.Warn("relaunch: pending request", ex); }
        }

        public static (string Target, string Module)? Before(string exe, string rom)
        {
            try
            {

                var map = Load();
                if (!map.TryGetValue(rom, out var r)) return null;
                if (!File.Exists(r.Host) && !Directory.Exists(r.Host))
                {
                    Log.Info("relaunch: " + rom + " was redirected to " + r.Host + ", which is gone - the launcher runs again");
                    map.Remove(rom);
                    Save(map);
                    return null;
                }
                Log.Info("relaunch: " + Path.GetFileName(rom) + " is a launcher - Xenia is handed " + r.Host + (r.Module != null ? " (" + r.Module + ")" : ""));
                return (r.Host, r.Module);
            }
            catch (Exception ex) { Log.Warn("relaunch", ex); return null; }
        }

        /// <summary>The game a launcher starts, when it has been seen to start one - null otherwise. What identifies such a game
        /// (its title id, the executable its updates name) is THAT package, not the launcher.</summary>
        public static string TargetOf(string rom)
        {
            try
            {
                if (string.IsNullOrEmpty(rom)) return null;
                var map = Load();
                return map.TryGetValue(rom, out var r) && (File.Exists(r.Host) || Directory.Exists(r.Host)) ? r.Host : null;
            }
            catch { return null; }
        }

        /// <summary>Once Xenia has come and gone: a request it wrote in this session is this game's.</summary>
        public static void WatchAfter(string exe, string rom)
        {
            var name = Path.GetFileNameWithoutExtension(exe ?? "");
            var file = Path.Combine(Path.GetDirectoryName(exe ?? "") ?? "", FileName);
            if (name.Length == 0) return;
            Task.Run(() =>
            {
                try
                {
                    bool Running() { try { return Process.GetProcessesByName(name).Length > 0; } catch { return false; } }
                    var armed = DateTime.UtcNow;
                    while (!Running() && (DateTime.UtcNow - armed).TotalSeconds < 120) Thread.Sleep(1000);
                    while (Running()) Thread.Sleep(1500);
                    if (File.Exists(file) && File.GetLastWriteTimeUtc(file) >= armed.AddSeconds(-5) && Adopt(file, rom))
                        XeniaNotify.Info(Path.GetFileNameWithoutExtension(rom) + " starts through a launcher: it is set up now. Launch it again - "
                                         + "from now on the game itself starts directly.", 10);
                }
                catch (Exception ex) { Log.Warn("relaunch watch", ex); }
            });
        }

        /// <summary>The request read, remembered for <paramref name="rom"/>, the file deleted. False when it does not read.</summary>
        private static bool Adopt(string file, string rom)
        {
            lock (Gate)
            {
                try
                {
                    var b = File.ReadAllBytes(file);
                    int at = 0;
                    string Str()
                    {
                        int n = BitConverter.ToUInt16(b, at); at += 2;
                        var s = Encoding.UTF8.GetString(b, at, n); at += n;
                        return s;
                    }
                    var host = Str();
                    var module = Str();
                    at += 4;
                    int data = at + 2 <= b.Length ? BitConverter.ToUInt16(b, at) : 0;
                    File.Delete(file);
                    if (string.IsNullOrWhiteSpace(host)) { Log.Info("relaunch: " + file + " held no path - deleted"); return false; }
                    if (string.IsNullOrWhiteSpace(module) || string.Equals(module, "default.xex", StringComparison.OrdinalIgnoreCase)) module = null;
                    var map = Load();
                    map[rom] = (host, module);
                    Save(map);
                    Log.Info("relaunch: " + Path.GetFileName(rom) + " asked Xenia to start " + host + (module != null ? " (" + module + ")" : "")
                             + " - remembered" + (data > 0 ? "; its " + data + " bytes of launch data are lost" : ""));
                    return true;
                }
                catch (Exception ex) { Log.Warn("relaunch: " + file + " could not be read", ex); return false; }
            }
        }

        private static Dictionary<string, (string Host, string Module)> Load()
        {
            var map = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(MapPath))
                    foreach (var line in File.ReadAllLines(MapPath, Encoding.UTF8))
                    {
                        var c = line.Split('\t');
                        if (c.Length >= 2 && c[0].Length > 0) map[c[0]] = (c[1], c.Length > 2 && c[2].Length > 0 ? c[2] : null);
                    }
            }
            catch (Exception ex) { Log.Warn("relaunch: " + MapPath, ex); }
            return map;
        }

        private static void Save(Dictionary<string, (string Host, string Module)> map)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MapPath));
            File.WriteAllLines(MapPath, map.OrderBy(kv => kv.Key).Select(kv => kv.Key + "\t" + kv.Value.Host + "\t" + (kv.Value.Module ?? "")), new UTF8Encoding(false));
        }
    }
}
