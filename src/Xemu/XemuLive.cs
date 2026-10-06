// The Live console: ONE console disk for the games played online on Insignia (Mehdi, 06/10: "une seule console live, mais une
// checkbox dans le per game pour passer en mode live. Pas de gestion save/savestate pour celles-là").
//
// WHY A DISK OF ITS OWN, IMPORTED: Insignia's Setup Assistant (insignia-live/setup-assistant-release, Insignia-Setup.iso for
// xemu) checks the console and registers it, but the Xbox Live ACCOUNT is made in the Xbox Live section of Microsoft's own
// dashboard (5960), and the DNS Insignia needs (46.101.64.175) is set in its network settings - xemu's NAT cannot hand the
// guest another DNS (slirp relays Windows'). xemu's dashboard has neither, and Microsoft's is not ours to ship: the user
// brings a console disk that has it (a qcow2, or a raw image of a real Xbox's disk), COPIED here once and kept CLEAN - the
// console works in a differential image over it (Mehdi, 06/10: "on fait une copie du hdd dans live, mais ensuite il bosse sur
// un différentiel de celui-ci"); the user's own file is never written to.
//
//   <xemu>\live\base.qcow2 (or base.img)   the imported disk, as it was imported - read-only, never written
//   <xemu>\live\live.qcow2                 the Live console: a differential image over base.* (Qcow2Overlay) - registration,
//                                          account, DNS, the games' updates; deleted, the console is the imported one again
//   <xemu>\live\eeprom.bin                 the console's EEPROM: a COPY of the one chosen (optional), else made once with your
//                                          console's identity - kept as it is from then on
//   <xemu>\live\sources.txt                the files chosen (disk=, eeprom=), for the Nixx window to show them
//   <xemu>\live\Insignia-Setup.iso         the Setup Assistant, downloaded from Insignia's release
//
// A game in Live mode (its option "live") boots on that console with xemu's network on (NAT), no save or savestate of
// its own handled (XemuPlugin.MakeSession, XemuSession.Watch, XemuSaves).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LbIntegrations.Cxbx;
using LbIntegrations.Xemu.Saves;

namespace LbIntegrations.Xemu
{
    internal sealed class XemuLiveDisk
    {
        public string Path, Format, Problem;
        public long VirtualSize;
        public bool HasC, HasE, HasDashboard;
        public string DashboardVersion;           // "1.00.5960.01" as found in xboxdash.xbe, or null
        public string DashboardTitle;
        public long DashboardBytes;
        public string[] CRoot = new string[0];
    }

    internal static class XemuLive
    {
        public const string SetupRepo = "insignia-live/setup-assistant-release";
        public const string SetupAsset = "Insignia-Setup.iso";
        public const string Dns = "46.101.64.175";

        /// <summary>The retail C: partition (system: the dashboard), at its fixed place on every Xbox disk.</summary>
        private const long COffset = 0x8CA80000, CSize = 0x1F400000;

        public static string Dir(string exe) => XemuPaths.Dir(exe) is string d ? System.IO.Path.Combine(d, "live") : null;

        /// <summary>The imported disk, clean (base.qcow2, else a raw base.img), or null.</summary>
        public static string Base(string exe)
        {
            var d = Dir(exe);
            if (d == null) return null;
            foreach (var n in new[] { "base.qcow2", "base.img" }) { var p = System.IO.Path.Combine(d, n); if (File.Exists(p)) return p; }
            return null;
        }

        /// <summary>The Live console's disk - the differential image over the imported one, made when it is not there - or null
        /// when nothing was imported. A Live console imported whole before the differential (06/10: live.qcow2 or live.img with no
        /// backing file) becomes the clean base first, the console then working over it.</summary>
        public static string Hdd(string exe)
        {
            var d = Dir(exe);
            if (d == null) return null;
            var overlay = System.IO.Path.Combine(d, "live.qcow2");
            if (Base(exe) == null)
            {
                foreach (var n in new[] { "live.qcow2", "live.img" })
                {
                    var whole = System.IO.Path.Combine(d, n);
                    if (!File.Exists(whole) || (n == "live.qcow2" && Qcow2Overlay.BackingName(whole) != null)) continue;
                    var b = System.IO.Path.Combine(d, n == "live.img" ? "base.img" : "base.qcow2");
                    File.Move(whole, b);
                    File.SetAttributes(b, File.GetAttributes(b) | FileAttributes.ReadOnly);
                    Log.Info("live: " + n + " (imported whole) is now the clean base " + System.IO.Path.GetFileName(b));
                    break;
                }
            }
            var base_ = Base(exe);
            if (base_ == null) return null;
            if (!File.Exists(overlay))
            {
                var error = Qcow2Overlay.Create(base_, overlay, rawAllowed: true);
                if (error != null) { Log.Warn("live: its differential image could not be made: " + error); return null; }
                Log.Info("live: a differential image made over " + System.IO.Path.GetFileName(base_));
            }
            return overlay;
        }

        /// <summary>The file chosen for "disk" or "eeprom" in the Nixx window (live\sources.txt), or null.</summary>
        public static string Source(string exe, string what)
        {
            try
            {
                var p = Dir(exe) is string d ? System.IO.Path.Combine(d, "sources.txt") : null;
                if (p == null || !File.Exists(p)) return null;
                return File.ReadAllLines(p).Where(l => l.StartsWith(what + "=", StringComparison.OrdinalIgnoreCase)).Select(l => l.Substring(what.Length + 1)).FirstOrDefault(v => v.Length > 0);
            }
            catch { return null; }
        }

        private static void SetSource(string exe, string what, string value)
        {
            var d = Dir(exe);
            Directory.CreateDirectory(d);
            var p = System.IO.Path.Combine(d, "sources.txt");
            var lines = File.Exists(p) ? File.ReadAllLines(p).Where(l => !l.StartsWith(what + "=", StringComparison.OrdinalIgnoreCase)).ToList() : new List<string>();
            if (!string.IsNullOrEmpty(value)) lines.Add(what + "=" + value);
            File.WriteAllLines(p, lines);
        }

        public static string Eeprom(string exe) => Dir(exe) is string d && File.Exists(System.IO.Path.Combine(d, "eeprom.bin")) ? System.IO.Path.Combine(d, "eeprom.bin") : null;
        public static string SetupIso(string exe) => Dir(exe) is string d ? System.IO.Path.Combine(d, SetupAsset) : null;

        /// <summary>What a console disk holds, read only: its format and size, its C and E partitions, Microsoft's dashboard
        /// (C:\xboxdash.xbe) and the version it carries. <see cref="XemuLiveDisk.Problem"/> says why it cannot be a Live console.</summary>
        public static XemuLiveDisk Inspect(string path)
        {
            var d = new XemuLiveDisk { Path = path };
            try
            {
                if (!File.Exists(path)) { d.Problem = "the file is not there"; return d; }
                d.Format = Qcow2Image.IsQcow2(path) ? "qcow2" : "raw";
                using var disk = Qcow2Image.Open(path);
                d.VirtualSize = disk.Length;
                if (disk.Length < 0xABE80000L + 0x1000) { d.Problem = "too small for an Xbox disk (" + (disk.Length >> 20) + " MB)"; return d; }
                FatxVolume c = null;
                try { c = FatxVolume.Open(disk, COffset, CSize); d.HasC = c != null; } catch { }
                try { d.HasE = FatxVolume.Open(disk) != null; } catch { }
                if (c == null) { d.Problem = "no Xbox system partition (C:) on it"; return d; }
                d.CRoot = c.List(c.RootCluster).Select(e => e.ToString()).ToArray();
                var dash = c.Find("xboxdash.xbe");
                if (dash == null || dash.IsDirectory) { d.Problem = "no dashboard (C:\\xboxdash.xbe) on it"; return d; }
                var bytes = c.ReadFile(dash);
                d.DashboardBytes = bytes.Length;
                d.HasDashboard = true;
                var xbe = Xbe.Parse(bytes.Take(Xbe.HeadBytes).ToArray());
                d.DashboardTitle = xbe?.TitleName;
                d.DashboardVersion = VersionIn(bytes);
                if (d.DashboardTitle != null && d.DashboardTitle.IndexOf("xemu", StringComparison.OrdinalIgnoreCase) >= 0)
                    d.Problem = "its dashboard is xemu's, not Microsoft's - it has no Xbox Live section";
            }
            catch (Exception ex) { d.Problem = ex.GetType().Name + ": " + ex.Message; }
            return d;
        }

        /// <summary>"1.00.5960.01"-like, ASCII or UTF-16, the highest build found; null when there is none.</summary>
        private static string VersionIn(byte[] b)
        {
            var rx = new Regex(@"\b1\.0\d\.(\d{4})\.\d{2}\b");
            var texts = new[] { System.Text.Encoding.ASCII.GetString(b), System.Text.Encoding.Unicode.GetString(b), System.Text.Encoding.Unicode.GetString(b, 1, b.Length - 1) };
            return texts.SelectMany(t => rx.Matches(t).Cast<Match>()).OrderByDescending(m => int.Parse(m.Groups[1].Value)).Select(m => m.Value).FirstOrDefault();
        }
        // ── its EEPROM: the console Insignia registered ──

        /// <summary>The Live console's EEPROM: the one imported with its disk, else made ONCE from eeprom.bin with your console's
        /// identity (PackIdentity - serial, MAC, online key, HDD key) and kept as it is from then on: the console Insignia has
        /// registered must stay that console, whatever happens to the pack's identity later. Null when it cannot be made.</summary>
        public static string EnsureEeprom(string exe, List<string> said)
        {
            var dir = Dir(exe);
            if (dir == null) return null;
            var path = System.IO.Path.Combine(dir, "eeprom.bin");
            if (File.Exists(path)) return path;
            Directory.CreateDirectory(dir);
            var keep = new Dictionary<string, string> { { "console.region", "xemu" }, { "console.video", "xemu" }, { "console.language", "xemu" }, { "console.timezone", "xemu" } };
            var made = LbIntegrations.Xemu.Eeprom.XemuEeprom.Prepare(XemuPaths.Eeprom(exe), path, null, keep, said);
            if (made == null || !File.Exists(path)) return null;
            said.Add("the Live console's EEPROM made from eeprom.bin with your console's identity");
            return path;
        }

        // ── import ──

        /// <summary>A console disk COPIED in as the Live console's clean base (base.qcow2, or base.img for a raw image), read-only;
        /// the console then works in a differential image over it (Hdd). A Live console already there - its base, its
        /// differential image (the registration, the account), its EEPROM - is put aside together (live\previous-<date>\),
        /// never deleted; the EEPROM goes too, so the new console is not given the old one's identity unasked. Null when done.</summary>
        public static string ImportDisk(string exe, string source, Action<double> progress = null)
        {
            var d = Inspect(source);
            if (d.Problem != null) return System.IO.Path.GetFileName(source) + ": " + d.Problem;
            var dir = Dir(exe);
            if (dir == null) return "the emulator's folder is not known";
            Directory.CreateDirectory(dir);
            var target = System.IO.Path.Combine(dir, d.Format == "qcow2" ? "base.qcow2" : "base.img");
            var part = target + ".part";
            using (var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20))
            using (var to = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                var buf = new byte[1 << 20]; long done = 0; int n;
                while ((n = from.Read(buf, 0, buf.Length)) > 0) { to.Write(buf, 0, n); done += n; progress?.Invoke(from.Length > 0 ? (double)done / from.Length : 0); }
            }
            PutAside(exe, "base.qcow2", "base.img", "live.qcow2", "live.img", "eeprom.bin");
            SetSource(exe, "eeprom", null);
            File.Move(part, target);
            File.SetAttributes(target, File.GetAttributes(target) | FileAttributes.ReadOnly);
            SetSource(exe, "disk", System.IO.Path.GetFullPath(source));
            if (Hdd(exe) == null) return "the disk was copied, but its differential image could not be made - see xemu.log";
            Log.Info("live: " + source + " imported as the Live console's base (" + d.Format + ", dashboard " + (d.DashboardVersion ?? d.DashboardTitle) + ")");
            return null;
        }

        /// <summary>The EEPROM of the console the disk came from (optional): 256 bytes an Xbox key opens, COPIED to live\eeprom.bin -
        /// the console works on the copy, the chosen file is never written. The one there before is put aside. Null when done.</summary>
        public static string ImportEeprom(string exe, string source)
        {
            var bytes = File.ReadAllBytes(source);
            if (bytes.Length != 256 || LbIntegrations.Xemu.Eeprom.XemuEeprom.Open(bytes) == null) return System.IO.Path.GetFileName(source) + " is not an Xbox EEPROM (256 bytes an Xbox key opens)";
            var dir = Dir(exe);
            if (dir == null) return "the emulator's folder is not known";
            Directory.CreateDirectory(dir);
            PutAside(exe, "eeprom.bin");
            File.WriteAllBytes(System.IO.Path.Combine(dir, "eeprom.bin"), bytes);
            SetSource(exe, "eeprom", System.IO.Path.GetFullPath(source));
            Log.Info("live: " + source + " copied as the Live console's EEPROM");
            return null;
        }

        /// <summary>Back to an EEPROM made with your console's identity (at the next start): the copy of the chosen one put aside.</summary>
        public static string ClearEeprom(string exe)
        {
            if (Dir(exe) == null) return "the emulator's folder is not known";
            PutAside(exe, "eeprom.bin");
            SetSource(exe, "eeprom", null);
            Log.Info("live: the chosen EEPROM put aside - one made with your console's identity at the next start");
            return null;
        }

        /// <summary>These files of live\, those there, moved together into live\previous-<date>\ - never deleted.</summary>
        private static void PutAside(string exe, params string[] names)
        {
            var dir = Dir(exe);
            var there = names.Select(n => System.IO.Path.Combine(dir, n)).Where(File.Exists).ToList();
            if (there.Count == 0) return;
            var aside = System.IO.Path.Combine(dir, "previous-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss"));
            Directory.CreateDirectory(aside);
            foreach (var p in there)
            {
                File.SetAttributes(p, File.GetAttributes(p) & ~FileAttributes.ReadOnly);
                File.Move(p, System.IO.Path.Combine(aside, System.IO.Path.GetFileName(p)));
            }
            Log.Info("live: " + string.Join(", ", there.Select(System.IO.Path.GetFileName)) + " put aside in " + System.IO.Path.GetFileName(aside));
        }
        // ── "Run with Insignia" (06/10): a game of Insignia's list launched online, whatever its own option says ──

        public const string RunCaption = "Nixx-Xemu : Run with Insignia";
        private static readonly Dictionary<string, DateTime> Forced = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Insignia set up: a Live console chosen.</summary>
        public static bool Configured(string exe)
            => exe != null && (Base(exe) != null || (Dir(exe) is string d && File.Exists(System.IO.Path.Combine(d, "live.qcow2"))));

        /// <summary>The menu's entry for this game - when it is one of ours, in Insignia's list, and a Live console is there - or null.</summary>
        public static string RunEntry(Unbroken.LaunchBox.Plugins.Data.IGame game)
        {
            try
            {
                if (game == null || !XemuGameMenu.IsOurs(game)) return null;
                var exe = XemuLibrary.For(game);
                if (!Configured(exe)) return null;
                var rom = XemuPlugin.ResolveFullPath(XemuPlugin.Safe(() => game.ApplicationPath));
                return rom != null && XemuInsignia.Of(XemuDisc.TitleIdOf(rom, exe)) != null ? RunCaption : null;
            }
            catch { return null; }
        }

        /// <summary>The game launched now - by LaunchBox or BigBox, on our xemu - its next launch in Live mode (TakeForced).</summary>
        public static void Run(Unbroken.LaunchBox.Plugins.Data.IGame game)
        {
            var id = XemuPlugin.Safe(() => game.Id);
            if (string.IsNullOrEmpty(id)) return;
            lock (Forced) Forced[id] = DateTime.UtcNow;
            var dm = Unbroken.LaunchBox.Plugins.PluginHelper.DataManager;
            Unbroken.LaunchBox.Plugins.Data.IEmulator emulator = null;
            try
            {
                var own = string.IsNullOrWhiteSpace(game.EmulatorId) ? null : dm?.GetEmulatorById(game.EmulatorId);
                emulator = own != null && XemuPaths.IsOurs(XemuPlugin.ResolveFullPath(own.ApplicationPath)) ? own
                         : (dm?.GetAllEmulators() ?? new Unbroken.LaunchBox.Plugins.Data.IEmulator[0]).FirstOrDefault(e => e != null && XemuPaths.IsOurs(XemuPlugin.ResolveFullPath(e.ApplicationPath)));
            }
            catch { }
            Log.Info("live: \"" + XemuPlugin.Safe(() => game.Title) + "\" run with Insignia");
            var bigBox = Unbroken.LaunchBox.Plugins.PluginHelper.BigBoxMainViewModel;
            if (bigBox != null) bigBox.PlayGame(game, null, emulator, null);
            else Unbroken.LaunchBox.Plugins.PluginHelper.LaunchBoxMainViewModel?.PlayGame(game, null, emulator, null);
        }

        /// <summary>Was this game's launch asked by "Run with Insignia" (in the last two minutes)? Taken once.</summary>
        public static bool TakeForced(string gameId)
        {
            if (string.IsNullOrEmpty(gameId)) return false;
            lock (Forced)
            {
                if (!Forced.TryGetValue(gameId, out var at)) return false;
                Forced.Remove(gameId);
                return DateTime.UtcNow - at < TimeSpan.FromMinutes(2);
            }
        }

        // ── Insignia's Setup Assistant ──

        /// <summary>Insignia-Setup.iso from Insignia's latest release - checked to be an Xbox disc. Null when it is there, else why not.</summary>
        public static string DownloadSetup(string exe, Action<double> progress = null)
        {
            var iso = SetupIso(exe);
            if (iso == null) return "the emulator's folder is not known";
            var release = GitHubReleases.GetLatest(SetupRepo);
            var asset = release == null ? null : GitHubReleases.SelectAssets(release, new[] { SetupAsset }, null).FirstOrDefault();
            if (asset == null) return "Insignia's release (github.com/" + SetupRepo + ") has no " + SetupAsset;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(iso));
            var part = iso + ".part";
            GitHubReleases.Download(asset.DownloadUrl, part, progress, () => false);
            var head = new byte[20];
            using (var f = File.OpenRead(part)) { f.Seek(0x10000, SeekOrigin.Begin); f.Read(head, 0, head.Length); }
            if (System.Text.Encoding.ASCII.GetString(head) != "MICROSOFT*XBOX*MEDIA") { File.Delete(part); return "what was downloaded is not an Xbox disc"; }
            File.Move(part, iso, true);
            Log.Info("live: " + SetupAsset + " " + release.Tag + " downloaded");
            return null;
        }

        /// <summary>xemu started on the Live console - its dashboard, or Insignia's Setup Assistant in its drive. Null when started.</summary>
        public static string Boot(string exe, bool setup)
        {
            try
            {
                if (exe == null) return "there is no xemu of this plugin in the library";
                if (XemuPaths.Running(exe)) return "xemu is already running - close it first";
                if (XemuPaths.McpxPath(exe) == null || XemuPaths.FlashPath(exe) == null) return "the Xbox's BIOS is not in " + XemuPaths.BiosDir(exe);
                if (Hdd(exe) == null) return "there is no Live console yet - choose a console disk first";
                string dvd = null;
                if (setup)
                {
                    if (!File.Exists(SetupIso(exe)) && DownloadSetup(exe) is string why) return why;
                    dvd = SetupIso(exe);
                }
                XemuConsoleBoot.RestoreOwn(exe, "before the Live console");
                try { XemuSessionConfig.MergeBack(XemuPaths.TomlOf(exe), XemuSessionConfig.SessionPath(exe)); } catch { }
                var problem = XemuPlugin.MakeSession(exe, "live", null, XemuOptions.Effective(null), dvd, out var hdd, out _, live: true);
                if (problem != null) return problem;
                Log.Info("live: the Live console started" + (setup ? " with Insignia's Setup Assistant" : ", its dashboard"));
                var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "-config_path \"" + XemuSessionConfig.SessionPath(exe) + "\"") { UseShellExecute = false, WorkingDirectory = XemuPaths.Dir(exe) });
                if (p == null) return "xemu did not start";
                XemuSession.Watch(exe, hdd, null, live: true);
                return null;
            }
            catch (Exception ex) { Log.Warn("live: boot", ex); return ex.Message; }
        }
    }
}