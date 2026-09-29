// Vita3K opened on a game WITHOUT running it, so its per-game settings can be edited in Vita3K itself
// (Mehdi, 29/09): right-click one game > "Nixx-Vita3K : Game settings in Vita3K...".
//
// WHERE THOSE SETTINGS LIVE, read in Vita3K's source: <config path>\config\config_<TITLE_ID>.xml
// (config/src/settings.cpp, get_custom_config_path), and the config path is portable\ when portable\
// exists (app_init.cpp). OUTSIDE the virtual filesystem - so a setting made here is the one every real
// session of that game loads, and nothing of the disposable console is involved in keeping it.
//
// WHAT VITA3K NEEDS TO LIST THE GAME, and nothing more: ux0/app/<TITLE_ID>/sce_sys/param.sfo, and
// icon0.png for its picture (app/src/apps_list.cpp). No eboot, no licence, no install: a FAKE
// console, in portable\settings-fs, which portable\fs is pointed at for the time Vita3K is open:
//   - os0, pd0, sa0, vs0 - the firmware - are JUNCTIONS into nand-initiale: nothing is copied, and a
//     Vita3K that runs no game has no reason to write to its system partitions;
//   - ux0/app/<TITLE_ID>/sce_sys holds the game's param.sfo and icon0.png, read out of its archive.
// Vita3K is started with no game (no -r, no -F: a window to click in), and when it quits the link is
// dropped and the fake console deleted - junctions first, never what they point at. The next launch of
// any game puts portable\fs back where a session wants it, as it always does.
//
// Refused while Vita3K runs: portable\fs is the running session's.
//
// A MACHINE THAT DIES WHILE IT IS OPEN leaves portable\fs pointing at the fake console, and the fake
// console itself. The first is nothing to fear: portable\fs is then a JUNCTION, which the next launch
// drops like any link of ours - never mistaken for a real folder and moved aside. The second is swept
// (Sweep): at the plugin's start-up check and before every launch, a settings-fs with no Vita3K
// running is ours and nobody's in use, and goes - its junctions first.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SharpCompress.Archives;

namespace LbIntegrations.Vita3k
{
    internal static class Vita3kSettingsSession
    {
        private const string FakeName = "settings-fs";
        private static readonly string[] FirmwareParts = { "os0", "pd0", "sa0", "vs0" };

        /// <summary>Open Vita3K on <paramref name="romPath"/>'s game, not started. Null when it opened,
        /// otherwise why not - for a message box.</summary>
        public static string Open(string exePath, string romPath)
        {
            try
            {
                if (Vita3kPaths.EmulatorRunning()) return "Vita3K is already running - close it first.";
                var layout = Vita3kPaths.Resolve(exePath);
                var portable = Vita3kPaths.PortableDirOf(layout?.InstallDir);
                if (portable == null || !Directory.Exists(portable)) return "This Vita3K has no portable folder.";
                var baseDir = Vita3kWorkspace.BaseDir(layout);
                if (baseDir == null || !Directory.Exists(baseDir)) return "This Vita3K has no firmware put aside yet - install it again.";

                var content = Vita3kContent.Describe(romPath, out var error);
                if (content == null) return "Could not read the game: " + error;
                if (!content.IsGame) return "This is not a game (" + content + ").";

                var fake = Path.Combine(portable, FakeName);
                Remove(fake);
                Directory.CreateDirectory(fake);
                foreach (var part in FirmwareParts)
                {
                    var target = Path.Combine(baseDir, part);
                    if (Directory.Exists(target) && !Junction(Path.Combine(fake, part), target, out error))
                    { Remove(fake); return "Could not prepare the console: " + error; }
                }
                var sys = Path.Combine(fake, "ux0", "app", content.TitleId, "sce_sys");
                Directory.CreateDirectory(sys);
                int written = WriteSysFiles(romPath, content, sys);
                if (!File.Exists(Path.Combine(sys, "param.sfo"))) { Remove(fake); return "The game's param.sfo could not be read."; }

                if (!Vita3kWorkspace.PointFsAt(layout, fake, out error)) { Remove(fake); return "Could not point Vita3K at the console: " + error; }
                Vita3kWorkspace.QuietTheFirstRun(layout);

                var psi = new ProcessStartInfo(layout.Executable) { UseShellExecute = false, WorkingDirectory = layout.InstallDir };
                Process process;
                try { process = Process.Start(psi); }
                catch (Exception ex)
                {
                    Vita3kWorkspace.DropFs(layout);
                    Remove(fake);
                    return "Vita3K could not be started: " + ex.Message;
                }
                Log.Info("settings: Vita3K opened on " + content + " without running it (" + written + " file(s) of sce_sys) - "
                         + "its per-game settings go to portable\\config\\config_" + content.TitleId + ".xml");

                Task.Run(() =>
                {
                    try
                    {
                        process?.WaitForExit();
                        while (Vita3kPaths.EmulatorRunning()) System.Threading.Thread.Sleep(500);
                    }
                    catch { }
                    Vita3kWorkspace.DropFs(layout);
                    Remove(fake);
                    Log.Info("settings: Vita3K closed - the settings console is gone"
                             + (File.Exists(Path.Combine(portable, "config", "config_" + content.TitleId + ".xml"))
                                ? ", " + content.TitleId + " has settings of its own" : ""));
                });
                return null;
            }
            catch (Exception ex)
            {
                Log.Warn("settings: could not open Vita3K", ex);
                return ex.GetType().Name + ": " + ex.Message;
            }
        }

        /// <summary>A fake console left behind - see the header. Nothing while Vita3K runs.</summary>
        internal static void Sweep(Vita3kLayout layout)
        {
            try
            {
                var portable = Vita3kPaths.PortableDirOf(layout?.InstallDir);
                if (portable == null) return;
                var fake = Path.Combine(portable, FakeName);
                if (!Directory.Exists(fake) || Vita3kPaths.EmulatorRunning()) return;
                Remove(fake);
                Log.Info("settings: a fake settings console was left behind (the host or the machine went while it was open) - "
                         + (Directory.Exists(fake) ? "could not remove it" : "removed"));
            }
            catch (Exception ex) { Log.Warn("settings: sweeping the fake console", ex); }
        }

        /// <summary>param.sfo and icon0.png (and pic0.png when there) out of the archive - a .pkg gives
        /// its param.sfo, which it holds in the clear, and no picture.</summary>
        private static int WriteSysFiles(string romPath, VitaContent content, string sys)
        {
            if (Vita3kContent.IsPkg(romPath))
            {
                var pkg = VitaPkg.Open(romPath, out _);
                if (pkg?.Sfo == null) return 0;
                File.WriteAllBytes(Path.Combine(sys, "param.sfo"), pkg.Sfo);
                return 1;
            }
            int n = 0;
            var wanted = new[] { "param.sfo", "icon0.png", "pic0.png" };
            using var archive = ArchiveFactory.Open(romPath);
            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory) continue;
                var key = (entry.Key ?? "").Replace('\\', '/');
                foreach (var name in wanted)
                {
                    if (!string.Equals(key, content.Root + "sce_sys/" + name, StringComparison.OrdinalIgnoreCase)) continue;
                    using var source = entry.OpenEntryStream();
                    using var file = File.Create(Path.Combine(sys, name));
                    source.CopyTo(file);
                    n++;
                }
            }
            return n;
        }

        private static bool Junction(string link, string target, out string error)
        {
            error = null;
            var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("/c"); psi.ArgumentList.Add("mklink"); psi.ArgumentList.Add("/J"); psi.ArgumentList.Add(link); psi.ArgumentList.Add(target);
            using var p = Process.Start(psi);
            var said = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(30000);
            bool ok = false;
            try { ok = File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint); } catch { }
            if (!ok) error = said.Trim();
            return ok;
        }

        /// <summary>Delete the fake console: its junctions as links first - never what they point at, the
        /// pristine firmware - then the rest.</summary>
        private static void Remove(string fake)
        {
            try
            {
                if (!Directory.Exists(fake)) return;
                // Every link at its top, whatever it is called - the firmware parts, and any a version of
                // this or a crash left there - removed as links, never followed.
                foreach (var link in Directory.EnumerateDirectories(fake).ToList())
                {
                    try { if (File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint)) Directory.Delete(link); } catch { }
                }
                // Anything still a link below is left alone rather than followed.
                if (Directory.EnumerateDirectories(fake, "*", SearchOption.TopDirectoryOnly)
                             .Any(d => { try { return File.GetAttributes(d).HasFlag(FileAttributes.ReparsePoint); } catch { return true; } }))
                { Log.Warn("settings: " + fake + " still holds a link - not deleted"); return; }
                Directory.Delete(fake, recursive: true);
            }
            catch (Exception ex) { Log.Warn("settings: could not delete " + fake, ex); }
        }
    }
}
