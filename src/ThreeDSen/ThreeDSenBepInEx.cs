// BepInEx and the pack's in-process plugin in a 3dSen folder (Mehdi 19/10: the model of SuperZsnesBepInEx).
//
// WHAT GOES THERE, and why each piece:
//
//   winhttp.dll, doorstop_config.ini, .doorstop_version, BepInEx\core\
//       BepInEx 5.4.23.5 for Unity MONO, x86 (3dSen 1.1.2 is Unity 2019.4, Mono, 32-bit: 3dSen_Data\Managed holds its IL),
//       ONE PINNED BUILD, downloaded from BepInEx's GitHub release at install time and verified by its SHA-256 (GitHub's own
//       "digest" of the asset, 37651c79...) before a byte of it is extracted. Downloaded rather than carried: it is LGPL,
//       the pack redistributes nothing of it. Nothing of 3dSen's is touched: Doorstop's winhttp.dll is a NEW file there
//       (3dSen ships none), so a Steam "verify files" keeps it, and taking it away disables everything.
//   BepInEx\config\BepInEx.cfg
//       SILENT: no console window, no disk log. Written only when absent, so a user who turned logging on keeps it.
//   BepInEx\plugins\ThreeDSen.BepInEx.dll
//       the pack's plugin (tools\3dsen-bepinex), embedded in THIS assembly at build time. Replaced when its bytes differ.
//
// MEASURED 19/10 in the lab (G:\LB1326\lbip-3dsen-lab\emu-bep, 3dSen 1.1.2 + this package + the plugin): BepInEx loads it,
// the data and states folders are redirected, Esc twice quits with exit code 0, a forced Volume is not kept, F2 saves with a
// picture and F4 loads (docs\3dsen.md section 15).
//
// NOTHING IS DOWNLOADED AT LAUNCH: a launch only puts back the config and the plugin from this assembly (PutBack) when BepInEx
// itself is there; a missing BepInEx is a log line, and the plugin's features are simply not asked for on the line.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;

namespace LbIntegrations.ThreeDSen
{
    internal static class ThreeDSenBepInEx
    {
        public const string Build = "5.4.23.5";
        public const string Url = "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x86_5.4.23.5.zip";
        public const string Sha256 = "37651c79e40d6f909572a4f461ac25350bb3ef8fe7fbd29f1aa8791a33b84c82";
        public const string PluginFileName = "ThreeDSen.BepInEx.dll";

        /// <summary>For the probe: a local archive instead of the download, a local plugin instead of the embedded one.</summary>
        internal static string ZipOverride, PluginOverride;

        public const string SilentConfig =
            "## Written by the LaunchBox integration plugin: BepInEx runs without a console and without a disk log.\n"
            + "## To diagnose, set both Enabled below to true, or tick the plugin's log in the Nixx window (<data>\\nixx.log).\n\n"
            + "[Logging.Console]\n\nEnabled = false\n\n"
            + "[Logging.Disk]\n\nEnabled = false\n\nWriteUnityLog = false\n";

        internal const string InstallingMarker = "lbip-bepinex-installing";

        // ── status ───────────────────────────────────────────────────────────

        public static bool HasBepInEx(string exeDir)
            => File.Exists(Path.Combine(exeDir, "winhttp.dll")) && File.Exists(Path.Combine(exeDir, "BepInEx", "core", "BepInEx.dll"))
               && !File.Exists(Path.Combine(exeDir, InstallingMarker));

        public static bool HasPlugin(string exeDir) => File.Exists(Path.Combine(exeDir, "BepInEx", "plugins", PluginFileName));

        /// <summary>BepInEx and the plugin there: the launch may ask for the plugin's features.</summary>
        public static bool IsDeployed(string exeDir) => HasBepInEx(exeDir) && HasPlugin(exeDir);

        public static string Describe(string exeDir)
        {
            if (!Directory.Exists(exeDir)) return "no such folder";
            if (File.Exists(Path.Combine(exeDir, InstallingMarker))) return "BepInEx install interrupted - Install puts it back";
            if (!HasBepInEx(exeDir)) return "BepInEx not installed";
            return "BepInEx " + Build + " installed, plugin " + (HasPlugin(exeDir) ? "present" : "MISSING");
        }

        // ── deploying ────────────────────────────────────────────────────────

        /// <summary>BepInEx (downloaded and checked when missing), then the config and the plugin. Null when done, else why not.</summary>
        public static string Deploy(string exeDir, Action<string> report = null)
        {
            string archive = null;
            try
            {
                if (!HasBepInEx(exeDir))
                {
                    report?.Invoke("Downloading BepInEx " + Build + "...");
                    if (ZipOverride != null) archive = ZipOverride;
                    else
                    {
                        archive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
                        {
                            http.DefaultRequestHeaders.UserAgent.ParseAdd("Nixx-3DSen");
                            File.WriteAllBytes(archive, http.GetByteArrayAsync(Url).GetAwaiter().GetResult());
                        }
                    }
                    var hash = Sha256Of(archive);
                    if (!string.Equals(hash, Sha256, StringComparison.OrdinalIgnoreCase))
                        return "the BepInEx archive does not match its pinned SHA-256 (" + hash.Substring(0, 12) + "... instead of " + Sha256.Substring(0, 12) + "...); nothing was extracted";
                    report?.Invoke("Extracting BepInEx...");
                    File.WriteAllText(Path.Combine(exeDir, InstallingMarker), "BepInEx " + Build + " being extracted - Install again if this stays");
                    Extract(archive, exeDir);
                    File.Delete(Path.Combine(exeDir, InstallingMarker));
                    if (!HasBepInEx(exeDir)) return "BepInEx was extracted but winhttp.dll or BepInEx\\core is missing";
                    Log.Info("BepInEx " + Build + " extracted into " + exeDir);
                }
                return PutBack(exeDir);
            }
            catch (Exception ex) { Log.Warn("BepInEx: install failed", ex); return ex.GetType().Name + ": " + ex.Message; }
            finally
            {
                try { if (archive != null && ZipOverride == null && File.Exists(archive)) File.Delete(archive); } catch { }
            }
        }

        /// <summary>Every entry of the archive over the folder (never above it); a config already there is the user's.</summary>
        private static void Extract(string zip, string exeDir)
        {
            var root = Path.GetFullPath(exeDir).TrimEnd('\\') + "\\";
            using var z = ZipFile.OpenRead(zip);
            foreach (var e in z.Entries)
            {
                if (string.IsNullOrEmpty(e.Name)) continue;
                var target = Path.GetFullPath(Path.Combine(exeDir, e.FullName.Replace('/', '\\')));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("archive entry escapes the folder: " + e.FullName);
                if (e.FullName.StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase) && File.Exists(target)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                e.ExtractToFile(target, overwrite: true);
            }
        }

        /// <summary>The config (when absent) and the plugin (when different), from this assembly. No network: what a launch may do.
        /// Null when done, else why not.</summary>
        public static string PutBack(string exeDir)
        {
            try
            {
                if (!HasBepInEx(exeDir)) return "BepInEx is not installed in " + exeDir;
                var config = Path.Combine(exeDir, "BepInEx", "config", "BepInEx.cfg");
                if (!File.Exists(config))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(config));
                    File.WriteAllText(config, SilentConfig);
                }
                var plugin = PluginBytes();
                var target = Path.Combine(exeDir, "BepInEx", "plugins", PluginFileName);
                if (plugin == null) return File.Exists(target) ? null : "this build of the pack carries no 3dSen plugin";
                if (!File.Exists(target) || !File.ReadAllBytes(target).AsSpan().SequenceEqual(plugin))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    var tmp = target + ".tmp";
                    File.WriteAllBytes(tmp, plugin);
                    File.Move(tmp, target, overwrite: true);
                    Log.Info(PluginFileName + " written (" + plugin.Length + " bytes)");
                }
                return null;
            }
            catch (Exception ex) { Log.Warn("BepInEx: the plugin could not be put back", ex); return ex.Message; }
        }

        /// <summary>BepInEx's files taken away (winhttp.dll first: without it nothing loads). The folder's data stays.</summary>
        public static string Remove(string exeDir)
        {
            try
            {
                foreach (var f in new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt" })
                {
                    var p = Path.Combine(exeDir, f);
                    if (File.Exists(p)) File.Delete(p);
                }
                var dir = Path.Combine(exeDir, "BepInEx");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        internal static byte[] PluginBytes()
        {
            if (PluginOverride != null) return File.Exists(PluginOverride) ? File.ReadAllBytes(PluginOverride) : null;
            try
            {
                using var s = typeof(ThreeDSenBepInEx).Assembly.GetManifestResourceStream("bepinex/" + PluginFileName);
                if (s == null) return null;
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
            catch { return null; }
        }

        internal static string Sha256Of(string path)
        {
            using var sha = SHA256.Create();
            using var f = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
        }
    }
}
