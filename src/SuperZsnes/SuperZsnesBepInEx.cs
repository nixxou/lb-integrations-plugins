// Putting BepInEx, the pack's in-process plugin and its documentation into an emulator folder.
//
// WHAT GOES THERE, and why each piece:
//
//   winhttp.dll, doorstop_config.ini, .doorstop_version, dotnet\, BepInEx\core\
//       BepInEx 6 IL2CPP, ONE PINNED BUILD, downloaded from builds.bepinex.dev at install time and
//       verified by sha256 before a byte of it is extracted. Pinned rather than "latest" because a
//       chainloader is not something to let drift under a plugin compiled against it; downloaded
//       rather than carried because it is LGPL and 30 MB, and the pack redistributes nothing of it.
//   BepInEx\config\BepInEx.cfg
//       SILENT: no console window, no disk log. Written only when absent, so a user who turned
//       logging on to diagnose something keeps it. docs\ says how to turn it back on.
//   BepInEx\plugins\SuperZsnes.BepInEx.dll
//       the pack's plugin, embedded in THIS assembly at build time when tools\superzsnes-bepinex
//       has been built (it needs a game folder's interop, so it is not always there). Replaced when
//       its bytes differ, never when identical - a launch must not rewrite a file for nothing.
//   BepInEx\nixx-docs\*.md
//       the documentation of everything the plugin does, embedded the same way, so whoever opens
//       the emulator's folder after an update broke something finds the runbook beside the log.
//
// The architecture is read off SUPERZSNES.exe's PE header, because BepInEx's runtime must match the
// process: 0.310 is x86, and the x64 package is pinned too for the day that changes.
//
// NOTHING IS DOWNLOADED AT LAUNCH. PrepareEmulatorForLaunch only puts back the two things that can
// be put back from this assembly - the plugin DLL and the docs - when BepInEx itself is already
// there; a missing BepInEx is a log line and an install or the window's button, never a 30 MB
// download the host shows no progress for.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace LbIntegrations.SuperZsnes
{
    internal sealed class BepInExPackage
    {
        public string Arch;
        public string Url;
        public string Sha256;
        public string FileName => Uri.UnescapeDataString(Path.GetFileName(new Uri(Url).AbsolutePath));
    }

    internal sealed class DeployResult
    {
        public bool BepInExPresent, PluginPresent, DocsPresent;
        public bool Changed;
        public List<string> Steps = new List<string>();
        public string Problem;
        public bool Ok => Problem == null;
    }

    internal static class SuperZsnesBepInEx
    {
        /// <summary>The build this plugin was compiled and tested against, 1 September 2026.</summary>
        public const string Build = "6.0.0-be.788+5b766a3";

        public static readonly BepInExPackage[] Packages =
        {
            new BepInExPackage { Arch = "x86", Url = "https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x86-6.0.0-be.788%2B5b766a3.zip",
                                 Sha256 = "D5954A5993EC39CD1133603D85BFF93875D30B6411B712CC13DCF03C8E08A4D3" },
            new BepInExPackage { Arch = "x64", Url = "https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip",
                                 Sha256 = "F4CC496BD098A0DF4164B81E3737297707F13A47C2478DBA2F60EEFAB784817A" },
        };

        public const string PluginFileName = "SuperZsnes.BepInEx.dll";
        public const string DocsFolder = "nixx-docs";

        /// <summary>For the probe: a local archive instead of the download, and a local plugin
        /// instead of the embedded one. Set by reflection from outside, hence the pragma.</summary>
#pragma warning disable CS0649
        internal static string ZipOverride, PluginOverride, DocsOverride;
#pragma warning restore CS0649

        /// <summary>No console, no disk log. BepInEx fills in every other key on its first run.
        /// The runbook in nixx-docs says which two lines to flip to see what is happening.</summary>
        public const string SilentConfig =
            "## Written by the LaunchBox integration plugin: BepInEx runs without a console and without a\n"
            + "## disk log. To diagnose, set both Enabled below to true, or pass --nixx-log to the emulator\n"
            + "## for the integration plugin's own log in portable\\nixx.log. BepInEx fills in the other keys.\n\n"
            + "[Logging.Console]\n\nEnabled = false\n\n"
            + "[Logging.Disk]\n\nEnabled = false\n\nWriteUnityLog = false\n";

        // ── status ───────────────────────────────────────────────────────────

        public static bool HasBepInEx(string exeDir)
            => File.Exists(Path.Combine(exeDir, "winhttp.dll"))
               && File.Exists(Path.Combine(exeDir, "BepInEx", "core", "BepInEx.Unity.IL2CPP.dll"));

        public static bool HasPlugin(string exeDir) => File.Exists(Path.Combine(exeDir, "BepInEx", "plugins", PluginFileName));

        public static bool IsDeployed(string exeDir) => HasBepInEx(exeDir) && HasPlugin(exeDir);

        /// <summary>x86, x64, or null for an executable this cannot read.</summary>
        public static string ArchOf(string exePath)
        {
            try
            {
                using var f = File.OpenRead(exePath);
                var head = new byte[0x40];
                if (f.Read(head, 0, head.Length) < head.Length || head[0] != 'M' || head[1] != 'Z') return null;
                int pe = BitConverter.ToInt32(head, 0x3C);
                f.Seek(pe, SeekOrigin.Begin);
                var sig = new byte[6];
                if (f.Read(sig, 0, 6) < 6 || sig[0] != 'P' || sig[1] != 'E') return null;
                ushort machine = BitConverter.ToUInt16(sig, 4);
                return machine == 0x14C ? "x86" : machine == 0x8664 ? "x64" : null;
            }
            catch { return null; }
        }

        // ── the pieces this assembly carries ─────────────────────────────────

        /// <summary>The plugin's bytes, from the override or the embedded resource; null when this
        /// build of the pack was made without tools\superzsnes-bepinex built.</summary>
        internal static byte[] PluginBytes()
        {
            if (PluginOverride != null) return File.Exists(PluginOverride) ? File.ReadAllBytes(PluginOverride) : null;
            return Resource("bepinex/" + PluginFileName);
        }

        /// <summary>The documentation, file name to bytes.</summary>
        internal static Dictionary<string, byte[]> Docs()
        {
            var docs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            if (DocsOverride != null)
            {
                if (Directory.Exists(DocsOverride))
                    foreach (var f in Directory.GetFiles(DocsOverride, "*.md")) docs[Path.GetFileName(f)] = File.ReadAllBytes(f);
                return docs;
            }
            var asm = typeof(SuperZsnesBepInEx).Assembly;
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (!name.StartsWith("docs/", StringComparison.Ordinal)) continue;
                using var s = asm.GetManifestResourceStream(name);
                if (s == null) continue;
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                docs[name.Substring(5)] = ms.ToArray();
            }
            return docs;
        }

        private static byte[] Resource(string name)
        {
            try
            {
                using var s = typeof(SuperZsnesBepInEx).Assembly.GetManifestResourceStream(name);
                if (s == null) return null;
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
            catch { return null; }
        }

        // ── deploying ────────────────────────────────────────────────────────

        /// <summary>Everything, with the download. What InstallEmulator and the window's button call.</summary>
        public static DeployResult Deploy(string exePath, Action<string, double?> report, Func<bool> cancel)
        {
            var r = new DeployResult();
            string archive = null;
            try
            {
                var exeDir = Path.GetDirectoryName(Path.GetFullPath(exePath));
                var arch = ArchOf(exePath);
                if (arch == null) { r.Problem = "could not read the architecture of " + Path.GetFileName(exePath); return r; }
                var package = Packages.FirstOrDefault(p => p.Arch == arch);
                if (package == null) { r.Problem = "no BepInEx package pinned for " + arch; return r; }

                if (!HasBepInEx(exeDir))
                {
                    report?.Invoke("Downloading BepInEx " + Build + " (" + arch + ")...", 0);
                    if (ZipOverride != null) archive = ZipOverride;
                    else
                    {
                        archive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                        SuperZsnesSite.Fetch(package.Url, archive, p => report?.Invoke("Downloading BepInEx " + Build + "...", p), cancel);
                    }
                    var hash = Sha256Of(archive);
                    if (ZipOverride == null && !string.Equals(hash, package.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        r.Problem = "the BepInEx archive does not match its pinned sha256 (" + hash.Substring(0, 12) + "... instead of "
                                    + package.Sha256.Substring(0, 12) + "...); nothing was extracted";
                        return r;
                    }
                    report?.Invoke("Extracting BepInEx...", null);
                    Archives.ExtractOver(archive, exeDir);
                    r.Steps.Add("BepInEx " + Build + " (" + arch + ") extracted");
                    r.Changed = true;
                }
                r.BepInExPresent = HasBepInEx(exeDir);
                if (!r.BepInExPresent) { r.Problem = "BepInEx was extracted but winhttp.dll or BepInEx\\core is missing"; return r; }

                PutBack(exeDir, r);
                return r;
            }
            catch (OperationCanceledException) { r.Problem = "cancelled"; return r; }
            catch (Exception ex) { r.Problem = ex.GetType().Name + ": " + ex.Message; return r; }
            finally
            {
                try { if (archive != null && ZipOverride == null && File.Exists(archive)) File.Delete(archive); } catch { }
            }
        }

        /// <summary>The parts that come from this assembly - config, plugin, docs - written where
        /// missing or different. No network. What a launch is allowed to do.</summary>
        public static DeployResult PutBack(string exeDir, DeployResult r = null)
        {
            r ??= new DeployResult();
            try
            {
                r.BepInExPresent = HasBepInEx(exeDir);
                if (!r.BepInExPresent) { r.Problem = "BepInEx is not installed in " + exeDir; return r; }

                var configDir = Path.Combine(exeDir, "BepInEx", "config");
                var config = Path.Combine(configDir, "BepInEx.cfg");
                if (!File.Exists(config))
                {
                    Directory.CreateDirectory(configDir);
                    File.WriteAllText(config, SilentConfig);
                    r.Steps.Add("silent BepInEx.cfg written");
                    r.Changed = true;
                }

                var plugin = PluginBytes();
                var pluginsDir = Path.Combine(exeDir, "BepInEx", "plugins");
                var target = Path.Combine(pluginsDir, PluginFileName);
                if (plugin == null)
                {
                    r.PluginPresent = File.Exists(target);
                    r.Steps.Add(r.PluginPresent ? "plugin left as it is (this build of the pack carries none)"
                                                : "no plugin: this build of the pack carries none and the folder has none");
                }
                else if (!File.Exists(target) || !Same(File.ReadAllBytes(target), plugin))
                {
                    Directory.CreateDirectory(pluginsDir);
                    WriteAtomic(target, plugin);
                    r.PluginPresent = true;
                    r.Steps.Add(PluginFileName + " written (" + plugin.Length + " bytes)");
                    r.Changed = true;
                }
                else r.PluginPresent = true;

                var docs = Docs();
                var docsDir = Path.Combine(exeDir, "BepInEx", DocsFolder);
                int written = 0;
                foreach (var kv in docs)
                {
                    var path = Path.Combine(docsDir, kv.Key);
                    if (File.Exists(path) && Same(File.ReadAllBytes(path), kv.Value)) continue;
                    Directory.CreateDirectory(docsDir);
                    WriteAtomic(path, kv.Value);
                    written++;
                }
                r.DocsPresent = docs.Count > 0 && Directory.Exists(docsDir);
                if (written > 0) { r.Steps.Add(written + " documentation file(s) written to BepInEx\\" + DocsFolder); r.Changed = true; }
                return r;
            }
            catch (Exception ex) { r.Problem = ex.GetType().Name + ": " + ex.Message; return r; }
        }

        private static bool Same(byte[] a, byte[] b) => a.Length == b.Length && a.AsSpan().SequenceEqual(b);

        private static void WriteAtomic(string path, byte[] bytes)
        {
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, path, overwrite: true);
        }

        internal static string Sha256Of(string path)
        {
            using var sha = SHA256.Create();
            using var f = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "");
        }

        /// <summary>One line for a log or a label.</summary>
        public static string Describe(string exeDir)
        {
            if (!Directory.Exists(exeDir)) return "no such folder";
            if (!HasBepInEx(exeDir)) return "BepInEx not installed";
            var version = "?";
            try { version = File.ReadAllText(Path.Combine(exeDir, ".doorstop_version")).Trim(); } catch { }
            return "BepInEx installed (Doorstop " + version + "), plugin " + (HasPlugin(exeDir) ? "present" : "MISSING")
                   + ", docs " + (Directory.Exists(Path.Combine(exeDir, "BepInEx", DocsFolder)) ? "present" : "missing");
        }
    }
}
