// The PS Vita firmware: finding it, fetching it, and getting Vita3K to install it.
//
// Three packages, and Vita3K wants all three. Each one unpacks into its own directory under the
// virtual Vita filesystem, which is also how we tell what is already installed - app.cpp asks
// nothing more than "does this directory exist and is it non-empty":
//
//     vs0   the main firmware        without it Vita3K refuses to run anything
//     sa0   the font package         without it text is missing
//     pd0   the preinstalled package the bundled applications
//
// WHERE THE URLS COME FROM. Vita3K's own welcome window opens a BROWSER at three destinations: two
// bit.ly links and, for the main firmware, Sony's support page - where the download is written in by
// JavaScript. That page is the reason this looked like it needed a browser engine. It does not.
//
// The console does not read a web page either. It asks an update list, in plain XML, and that list
// carries all three with their sizes:
//
//     http://<host>/update/psp2/list/<region>/psp2-updatelist.xml
//
// Measured: its preinst entry is the byte-identical URL that bit.ly/4hlePsX redirects to, hash and
// all. So the list is the same source, just without the redirector in the way.
//
// THE TWO FIXED ONES ARE WRITTEN OUT HERE rather than read from the list, deliberately. Their URLs
// carry a content hash, so the file behind one cannot change - which is what makes a pinned size a
// real check rather than a guess. And for the font package it is not merely simpler, it is the only
// honest option: the list serves a 2022 build of systemdata while Vita3K documents the 2019 one, a
// difference of 10 240 bytes that nothing here has measured the meaning of. Taking the list's
// silently would install something other than what Vita3K's own users run.
//
// Only the main firmware is looked up, because it is the only one that moves.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Xml.Linq;

namespace LbIntegrations.Vita3k
{
    /// <summary>Which of the three a package is. The name is also the directory it creates under the
    /// virtual filesystem, which is how its presence is detected.</summary>
    internal enum FirmwarePart
    {
        /// <summary>vs0 - the main firmware.</summary>
        Main,
        /// <summary>sa0 - the font package.</summary>
        Font,
        /// <summary>pd0 - the preinstalled applications.</summary>
        Preinstalled,
    }

    internal sealed class FirmwarePackage
    {
        public FirmwarePart Part;
        public string Url;
        /// <summary>For the log and for the emulator entry: "3.74" for the main firmware, a date for
        /// the fixed ones.</summary>
        public string Label;
        /// <summary>The size the source declares, in bytes, or 0. Checked after the download: a proxy
        /// or a captive portal answering 200 with an HTML page is otherwise indistinguishable from a
        /// firmware, right up to the point where Vita3K quietly fails to install it.</summary>
        public long ExpectedSize;

        /// <summary>The directory this package creates under the virtual filesystem.</summary>
        public string Directory
        {
            get
            {
                switch (Part)
                {
                    case FirmwarePart.Main: return "vs0";
                    case FirmwarePart.Font: return "sa0";
                    default: return "pd0";
                }
            }
        }

        public override string ToString() => Part + " (" + Label + ")";
    }

    internal static class Vita3kFirmware
    {
        /// <summary>The update list, region "us".
        ///
        /// The region is a CDN choice and nothing else - measured: us, eu and jp all name the same
        /// image hash for the same firmware, and differ only in the host name and the dest= parameter.
        /// So there is no region for a user to get wrong, and none is offered.</summary>
        private const string UpdateListUrl =
            "http://dus01.psp2.update.playstation.net/update/psp2/list/us/psp2-updatelist.xml";

        /// <summary>The preinstalled package. Vita3K's welcome window sends a browser to
        /// bit.ly/4hlePsX, which redirects here; the update list names the same file.</summary>
        private const string PreinstalledUrl =
            "http://dus01.psp2.update.playstation.net/update/psp2/image/2022_0209/"
            + "pre_efd1ef6c1cc2fe92e72e9e783e421237/PSP2UPDAT.PUP?dest=us";
        private const long PreinstalledSize = 128798720;

        /// <summary>The font package - a systemdata recovery package. This is the 2019 build that
        /// Vita3K's own window points at (bit.ly/2P2rb0r), NOT the 2022 one the update list serves.
        /// See the header: the two differ and the difference has not been measured.</summary>
        private const string FontUrl =
            "http://dus01.psp2.update.playstation.net/update/psp2/image/2019_0924/"
            + "sd_8b5f60b56c3da8365b973dba570c53a5/PSP2UPDAT.PUP?dest=us";
        private const long FontSize = 56768512;

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // ── what is already there ────────────────────────────────────────────

        /// <summary>Is this package installed? The same question app.cpp asks, in the same terms:
        /// has_installed_firmware_content is exists + is_directory + not empty, and nothing more.
        /// Deliberately not stricter - a check of our own invention would disagree with the emulator
        /// about its own state, and the emulator is the one that decides.</summary>
        public static bool IsInstalled(string vitaFs, FirmwarePackage package)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(vitaFs) || package == null) return false;
                var dir = Path.Combine(vitaFs, package.Directory);
                return Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any();
            }
            catch (Exception ex) { Log.Warn("could not look at " + package + " under " + vitaFs, ex); return false; }
        }

        /// <summary>The parts still missing from <paramref name="vitaFs"/>, in install order.</summary>
        public static List<FirmwarePackage> Missing(string vitaFs, IEnumerable<FirmwarePackage> packages)
            => packages.Where(p => !IsInstalled(vitaFs, p)).ToList();

        // ── what is available ────────────────────────────────────────────────

        /// <summary>The three packages, main firmware first. Null when the update list could not be
        /// read - the two fixed ones are still known, but a firmware set with no vs0 in it is not
        /// something to start downloading.</summary>
        public static List<FirmwarePackage> Available()
        {
            var main = LatestMainFirmware();
            if (main == null) return null;

            return new List<FirmwarePackage>
            {
                main,
                new FirmwarePackage { Part = FirmwarePart.Font, Url = FontUrl,
                                      Label = "2019-09-24", ExpectedSize = FontSize },
                new FirmwarePackage { Part = FirmwarePart.Preinstalled, Url = PreinstalledUrl,
                                      Label = "2022-02-09", ExpectedSize = PreinstalledSize },
            };
        }

        /// <summary>The newest main firmware, from the console's own update list. Never throws.</summary>
        private static FirmwarePackage LatestMainFirmware()
        {
            try
            {
                var xml = Http.GetStringAsync(UpdateListUrl).GetAwaiter().GetResult();
                var doc = XDocument.Parse(xml);

                var version = doc.Descendants("version").FirstOrDefault();
                if (version == null) { Log.Warn("the update list carries no <version>"); return null; }

                // <update_data update_type="full"><image size="...">URL</image></update_data>
                var image = version.Descendants("update_data")
                                   .Where(d => (string)d.Attribute("update_type") == "full")
                                   .Descendants("image")
                                   .FirstOrDefault();
                if (image == null) { Log.Warn("the update list carries no full update image"); return null; }

                var url = (image.Value ?? "").Trim();
                if (url.Length == 0) { Log.Warn("the full update image has no URL"); return null; }

                long size = 0;
                long.TryParse((string)image.Attribute("size"), out size);

                var label = (string)version.Attribute("label");
                if (string.IsNullOrWhiteSpace(label))
                    label = (string)version.Attribute("system_version") ?? "unknown";

                Log.Info("the update list offers firmware " + label + " (" + size + " bytes)");
                return new FirmwarePackage { Part = FirmwarePart.Main, Url = url,
                                             Label = label, ExpectedSize = size };
            }
            catch (Exception ex) { Log.Warn("could not read the PS Vita update list", ex); return null; }
        }

        // ── getting it in ────────────────────────────────────────────────────

        /// <summary>Download a package to a temporary file and hand it to Vita3K. True when the
        /// package is installed afterwards.
        ///
        /// The temporary file is always deleted, including on failure: these are 130 MB each and a
        /// failed install must not leave a third of a gigabyte behind in the temp folder.</summary>
        public static bool Install(string executablePath, string vitaFs, FirmwarePackage package,
                                   Action<string, double?> report, Func<bool> shouldCancel)
        {
            string pup = null;
            try
            {
                pup = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".PUP");

                report?.Invoke("Downloading the " + Describe(package) + "...", 0);
                // The same downloader the release archive uses: it reports progress from
                // Content-Length and reads the body as a stream, so a 130 MB file is not held in
                // memory and the 30 second client timeout applies only to the headers.
                GitHubReleases.Download(package.Url, pup,
                    p => report?.Invoke("Downloading the " + Describe(package) + "...", p),
                    shouldCancel);

                var got = new FileInfo(pup).Length;
                if (package.ExpectedSize > 0 && got != package.ExpectedSize)
                {
                    Log.Warn("the " + Describe(package) + " is " + got + " bytes where "
                             + package.ExpectedSize + " was declared - not installing it. URL: " + package.Url);
                    return false;
                }

                report?.Invoke("Installing the " + Describe(package) + "...", null);

                // OURS FIRST: Vita3K's install_pup, compiled on its own - see Vita3kNative. The
                // emulator's --firmware below is the fallback for a checkout that did not build it,
                // not a second opinion: the two produce the same 1825 files, byte for byte.
                if (Vita3kNative.Available)
                {
                    if (RunOurs(pup, vitaFs, package, report)) return true;
                    Log.Info("our installer did not get the " + Describe(package) + " in - trying the emulator's own");
                }

                if (Run(executablePath, pup, vitaFs, package)) return true;

                // ONE RETRY, AND ONLY AFTER A CRASH WE COULD NOT EXPLAIN. Measured on a real install:
                // the main firmware died with 0xC0000409 less than a second into an install that
                // takes six, on a first run of an emulator that had never started before - and the
                // very same package into the very same folder went in cleanly minutes later. Whatever
                // that is, it is not the download and not the path length: both were checked.
                //
                // A second attempt costs seconds and no bytes, since the .pup is already here. It is
                // not a loop: twice, then the failure is reported honestly.
                Log.Info("the " + Describe(package) + " did not go in - trying once more");
                return Run(executablePath, pup, vitaFs, package);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Warn("could not install the " + Describe(package), ex);
                return false;
            }
            finally
            {
                try { if (pup != null && File.Exists(pup)) File.Delete(pup); } catch { }
            }
        }

        /// <summary>Install a .pup with vita3k-install.exe and say whether the package arrived - the
        /// same double answer as Run: the tool's verdict, and the partition this package fills.</summary>
        private static bool RunOurs(string pupPath, string vitaFs, FirmwarePackage package,
                                    Action<string, double?> report)
        {
            // Progress goes to the host's own install window - Add Emulator shows it. install_pup's
            // steps are coarse (10, 20, 30, 70, 100) but they are real, which --firmware never gave.
            var what = "Installing the " + Describe(package) + "...";
            bool ran = Vita3kNative.InstallFirmware(pupPath, vitaFs, f => report?.Invoke(what, f), out _);
            Scrub(vitaFs);
            bool installed = ran && IsInstalled(vitaFs, package);
            Log.Info((installed ? "installed " : "FAILED to install ") + Describe(package)
                     + " with " + Vita3kNative.FileName + " - " + Path.Combine(vitaFs, package.Directory)
                     + (installed ? " is populated" : " is missing or empty"));
            return installed;
        }

        /// <summary>Run the emulator on a .pup and say whether the package arrived.
        ///
        /// BOTH ANSWERS ARE NEEDED, and one of them was learnt the hard way. main.cpp calls
        /// install_pup and DISCARDS its return value, so a package that fails to install can still
        /// exit 0 - which is why the directory is looked at. But a crash shows up in the exit code
        /// and NOWHERE ELSE: measured, the main firmware dying half way through leaves a vs0 with 932
        /// of its 1473 files in it, which "exists and is not empty" calls installed. So a non-zero
        /// exit is a failure outright, and the directory check catches the quiet kind.
        ///
        /// No window opens: --firmware is handled on the quit path, before SDL or any GUI is
        /// created - measured in config.cpp, which returns QuitRequested for it.</summary>
        private static bool Run(string executablePath, string pupPath, string vitaFs,
                                FirmwarePackage package)
        {
            var start = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? "",
            };
            start.ArgumentList.Add("--firmware");
            start.ArgumentList.Add(pupPath);

            using (var process = Process.Start(start))
            {
                if (process == null) { Log.Warn("could not start " + executablePath); return false; }

                // Generous: unpacking 130 MB of firmware on a slow disk is minutes, not seconds. The
                // ceiling exists so a hung emulator cannot hold an install open for ever.
                if (!process.WaitForExit((int)TimeSpan.FromMinutes(15).TotalMilliseconds))
                {
                    Log.Warn("the emulator was still installing the " + Describe(package)
                             + " after fifteen minutes; giving up on it");
                    try { process.Kill(true); } catch { }
                    Scrub(vitaFs);
                    return false;
                }

                if (process.ExitCode != 0)
                {
                    Log.Warn("the emulator DIED installing the " + Describe(package) + ": exit 0x"
                             + process.ExitCode.ToString("X8") + ". Whatever it had written is partial.");
                    Scrub(vitaFs);
                    return false;
                }
                Log.Info("firmware install of the " + Describe(package) + " exited cleanly");
            }

            Scrub(vitaFs);
            bool installed = IsInstalled(vitaFs, package);
            Log.Info((installed ? "installed " : "FAILED to install ") + Describe(package)
                     + " - " + Path.Combine(vitaFs, package.Directory)
                     + (installed ? " is populated" : " is missing or empty"));
            return installed;
        }

        /// <summary>Remove the emulator's working directory, which it leaves behind when it dies.
        ///
        /// install_pup unpacks the whole .pup into PUP_DEC under the virtual filesystem and deletes
        /// it on its way out - so a crash leaves a QUARTER OF A GIGABYTE sitting there (measured:
        /// 251 MB, 54 files). It reuses the folder on the next attempt, so this is tidiness rather
        /// than correctness, and it runs after a success too because that costs one Exists.</summary>
        private static void Scrub(string vitaFs)
        {
            try
            {
                var work = Path.Combine(vitaFs, "PUP_DEC");
                if (!Directory.Exists(work)) return;
                Directory.Delete(work, true);
                Log.Info("removed the firmware working directory the emulator left behind");
            }
            catch (Exception ex) { Log.Warn("could not remove " + Path.Combine(vitaFs, "PUP_DEC"), ex); }
        }

        /// <summary>The longest path inside the firmware, relative to the virtual filesystem - measured
        /// on 3.74: 124 characters, and it is a remote web inspector image buried in vs0\data.</summary>
        private const int LongestPathInFirmware = 124;

        /// <summary>Is there room to unpack the firmware under this install, within MAX_PATH?
        ///
        /// THIS IS NOT A PRECAUTION, IT IS A MEASURED FAILURE. Unpacking 3.74 into a path 174
        /// characters long crashed the emulator outright - boost::filesystem::create_directories
        /// threw EINVAL, nothing caught it, and the process died with 0xC0000409 leaving 932 of vs0's
        /// 1473 files behind. Four attempts, four identical crashes. The same firmware into a 39
        /// character path installed in full and exited 0.
        ///
        /// Vita3K is not built with long paths enabled, so the ceiling is the old 260. A normal
        /// install - LaunchBox's Emulators folder - is around 33 characters and has a hundred to
        /// spare; this exists for the person whose LaunchBox lives somewhere deep, who would
        /// otherwise get a firmware that looks installed and a console that does not work.</summary>
        public static bool RoomToUnpack(string vitaFs, out string problem)
        {
            problem = null;
            try
            {
                // vitaFs already includes "\portable\fs"; +1 for the separator before the entry.
                int needed = vitaFs.Length + 1 + LongestPathInFirmware;
                if (needed <= 259) return true;

                problem = "the firmware cannot be unpacked here: its deepest file would need "
                          + needed + " characters and Windows stops at 259. Install Vita3K somewhere "
                          + "shorter - about " + (needed - 259) + " characters shorter than "
                          + vitaFs + ".";
                Log.Warn(problem);
                return false;
            }
            catch (Exception ex) { Log.Warn("could not check the path length", ex); return true; }
        }

        private static string Describe(FirmwarePackage package)
        {
            switch (package.Part)
            {
                case FirmwarePart.Main: return "firmware " + package.Label;
                case FirmwarePart.Font: return "font package";
                default: return "preinstalled package";
            }
        }
    }
}
