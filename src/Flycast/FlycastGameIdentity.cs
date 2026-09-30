// The id Flycast gives a game - the name of its per-game section in emu.cfg, [<ID>] - for the games whose id
// this plugin cannot read by itself: the arcade ones (Mehdi, 29/09).
//
// A DREAMCAST DISC's id is its IP.BIN product number; AN ARCADE GAME's is the title in its cartridge's boot
// header, known only once Flycast has built and decrypted the cartridge (hw/naomi/naomi_cart.cpp) - a Naomi
// GD-ROM game's in the .chd beside its .zip. Both asked, in this order:
//
//   1. flycast-id.exe (github.com/nixxou/flycast-id): Flycast's own disc and cartridge code, one process, one line out -
//      a Dreamcast disc read here (FlycastGameId) only when it has no answer. It is
//      carried in the plugin's native\ folder and put BESIDE THE EMULATOR (Mehdi, 29/09), refreshed when its
//      bytes differ. A PROCESS OF ITS OWN, never loaded here: a bad rom crashes it, not LaunchBox; a GD-ROM
//      image loaded whole is its memory, given back when it ends; and it is GPL-2.0 as Flycast.
//      ONLY A WELL-FORMED ANSWER IS AN ID: exit code 0, "id<TAB>" then the id then one "\n", nothing else, an
//      id with something other than spaces in it - its leading spaces KEPT, they are part of it for Flycast
//      ("  18WHEELER"). Anything else - an error, a time-out, an empty or garbled line, no tool - is no id.
//   2. When it has none (a GD-ROM image it cannot open, a set it does not know): LEARNED FROM FLYCAST'S LOG,
//      for that launch only (Mehdi, 29/09). -config log:LogToFile=yes is added - a transient value, never
//      saved - and "Game ID is [<ID>]" is read out of flycast.log once Flycast has quit. The log cannot be
//      made smaller from the command line (measured: Verbosity and the per-family switches are read before
//      it - a few kilobytes a session). A log of the user's own (LogToFile in emu.cfg) is read from where it
//      stood and left alone; one this plugin asked for is deleted once read, and before a launch that asks
//      again.
//
// KEPT: <install>\lbip-game-ids.tsv, one line per rom (its full path, length and write time) -> id. A failure
// is never kept as an id: it is remembered for this session only, so a broken set is not asked again at
// every right-click.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LbIntegrations.Flycast
{
    internal static class FlycastGameIdentity
    {
        public const string ToolName = "flycast-id.exe";
        public const string CacheName = "lbip-game-ids.tsv";
        public const string LogName = "flycast.log";

        /// <summary>Set by the probe, which runs the plugin from its build folder: where the tool is.</summary>
        public const string ToolOverride = "LBIP_FLYCAST_ID_TOOL";

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, string> Failed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // ── the answer of the tool ───────────────────────────────────────────

        /// <summary>The id in the tool's answer, or null - with why. See the header: only a well-formed answer
        /// is an id.</summary>
        public static string Parse(int exitCode, byte[] stdout, out string why)
        {
            why = null;
            if (exitCode != 0) { why = "the tool answered with exit code " + exitCode; return null; }
            var text = Encoding.Latin1.GetString(stdout ?? new byte[0]);
            if (!text.StartsWith("id\t", StringComparison.Ordinal)) { why = "the tool's answer is not an id line"; return null; }
            if (!text.EndsWith("\n", StringComparison.Ordinal) || text.IndexOf('\n') != text.Length - 1 || text.IndexOf('\r') >= 0)
            { why = "the tool's answer is not one line"; return null; }
            var id = text.Substring(3, text.Length - 4);
            if (id.Trim().Length == 0) { why = "the tool's id is empty"; return null; }
            if (id.Any(c => c < 0x20 || c > 0x7E)) { why = "the tool's id holds characters emu.cfg would not keep as they are"; return null; }
            return id;
        }

        // ── the tool, beside the emulator ────────────────────────────────────

        /// <summary>The tool beside the emulator, put there - or refreshed - from the plugin's native\ folder.
        /// Null, with why, when there is none to run.</summary>
        private static string Tool(FlycastLayout layout, out string why)
        {
            why = null;
            var forced = Environment.GetEnvironmentVariable(ToolOverride);
            if (!string.IsNullOrWhiteSpace(forced)) return File.Exists(forced) ? forced : null;
            string carried = null;
            try
            {
                var dir = Path.GetDirectoryName(typeof(FlycastGameIdentity).Assembly.Location);
                carried = string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "native", ToolName);
                if (carried != null && !File.Exists(carried)) carried = null;
            }
            catch { carried = null; }
            // Beside the emulator when there is one installed - its folder there, never made for it.
            var beside = string.IsNullOrEmpty(layout?.InstallDir) || !Directory.Exists(layout.InstallDir) ? null : Path.Combine(layout.InstallDir, ToolName);
            if (beside != null && carried != null)
            {
                try
                {
                    if (!SameBytes(carried, beside))
                    {
                        File.Copy(carried, beside, overwrite: true);
                        Log.Info("game id: " + ToolName + " put beside Flycast (" + beside + ")");
                    }
                }
                catch (Exception ex) { Log.Warn("game id: could not put " + ToolName + " beside Flycast", ex); }
            }
            // The one beside the emulator when it is the plugin's own; else the plugin's own, run from its folder
            // (Mehdi, 30/09: an emulator entry of ours whose Flycast is not installed yet reads ids all the same);
            // else whatever is beside the emulator.
            if (beside != null && File.Exists(beside) && (carried == null || SameBytes(carried, beside))) return beside;
            if (carried != null) return carried;
            if (beside != null && File.Exists(beside)) return beside;
            why = ToolName + " is not there (neither in the plugin's native folder nor beside Flycast)";
            return null;
        }

        private static readonly Dictionary<string, string> Hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static bool SameBytes(string a, string b)
        {
            if (!File.Exists(b) || new FileInfo(a).Length != new FileInfo(b).Length) return false;
            return Hash(a) == Hash(b);
        }

        private static string Hash(string path)
        {
            var info = new FileInfo(path);
            var key = info.FullName + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
            lock (Hashes)
            {
                if (Hashes.TryGetValue(key, out var h)) return h;
                using var s = File.OpenRead(path);
                h = Convert.ToHexString(SHA256.HashData(s));
                Hashes[key] = h;
                return h;
            }
        }

        // ── the cache ────────────────────────────────────────────────────────

        /// <summary>What a rom is known by: its full path, length and write time - a rom replaced is asked again.</summary>
        internal static string KeyOf(string romPath)
        {
            try
            {
                var info = new FileInfo(romPath);
                return info.Exists ? info.FullName + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks : null;
            }
            catch { return null; }
        }

        public static string Cached(FlycastLayout layout, string romPath)
        {
            var key = KeyOf(romPath);
            if (key == null || string.IsNullOrEmpty(layout?.InstallDir)) return null;
            lock (Gate)
            {
                var path = Path.Combine(layout.InstallDir, CacheName);
                try
                {
                    if (!File.Exists(path)) return null;
                    foreach (var line in File.ReadAllLines(path))
                    {
                        var f = line.Split('\t');
                        if (f.Length == 2 && string.Equals(Uri.UnescapeDataString(f[0]), key, StringComparison.OrdinalIgnoreCase))
                            return Uri.UnescapeDataString(f[1]);
                    }
                }
                catch (Exception ex) { Log.Warn("game id: could not read " + CacheName, ex); }
                return null;
            }
        }

        public static void Keep(FlycastLayout layout, string romPath, string id, string how)
        {
            var key = KeyOf(romPath);
            if (key == null || string.IsNullOrEmpty(layout?.InstallDir) || id == null || id.Trim().Length == 0) return;
            lock (Gate)
            {
                var path = Path.Combine(layout.InstallDir, CacheName);
                try
                {
                    var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
                    var mine = Uri.EscapeDataString(key);
                    lines.RemoveAll(l => l.Split('\t')[0].Equals(mine, StringComparison.OrdinalIgnoreCase));
                    lines.Add(mine + "\t" + Uri.EscapeDataString(id));
                    FlycastIni.WriteAtomicBytes(path, new UTF8Encoding(false).GetBytes(string.Join("\r\n", lines) + "\r\n"));
                    Failed.Remove(key);
                    Log.Info("game id of " + Path.GetFileName(romPath) + ": [" + id + "] (" + how + ")");
                }
                catch (Exception ex) { Log.Warn("game id: could not write " + CacheName, ex); }
            }
        }

        // ── asking ───────────────────────────────────────────────────────────

        /// <summary>An arcade game's id: kept, else asked of the tool (which may take a while for a GD-ROM
        /// image - <paramref name="timeoutMs"/>). Null, with why, when there is none.</summary>
        public static string Of(FlycastLayout layout, string romPath, int timeoutMs, out string why)
        {
            why = null;
            var cached = Cached(layout, romPath);
            if (cached != null) return cached;
            var key = KeyOf(romPath);
            if (key == null) { why = "no such rom"; return null; }
            lock (Gate) if (Failed.TryGetValue(key, out var was)) { why = was; return null; }

            var id = Ask(layout, romPath, timeoutMs, out why);
            if (id != null) Keep(layout, romPath, id, "read by " + ToolName);
            else lock (Gate) Failed[key] = why;
            return id;
        }

        private static string Ask(FlycastLayout layout, string romPath, int timeoutMs, out string why)
        {
            var tool = Tool(layout, out why);
            if (tool == null) return null;
            try
            {
                var psi = new ProcessStartInfo(tool)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(tool) ?? "",
                };
                psi.ArgumentList.Add(romPath);
                using var p = Process.Start(psi);
                if (p == null) { why = "the tool did not start"; return null; }
                var output = new MemoryStream();
                var readOut = p.StandardOutput.BaseStream.CopyToAsync(output);
                var readErr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                    why = "the tool took longer than " + timeoutMs / 1000 + " s";
                    return null;
                }
                readOut.Wait(2000);
                var id = Parse(p.ExitCode, output.ToArray(), out why);
                if (id == null)
                {
                    var err = readErr.Wait(1000) ? readErr.Result.Trim() : "";
                    if (err.Length > 0) why += ": " + err;
                }
                return id;
            }
            catch (Exception ex) { why = "the tool could not be run (" + ex.Message + ")"; return null; }
        }

        // ── the arcade sets Flycast knows ────────────────────────────────────

        /// <summary>The "system" of a BIOS set in Sets: not a game - Flycast wants it in its own data folder.</summary>
        public const string BiosSystem = "BIOS";

        /// <summary>One arcade set of Flycast's table, as flycast-id --sets gives it.</summary>
        internal sealed class ArcadeSet
        {
            public string Name, System, Parent, GdRom, Description;
        }

        private static readonly Dictionary<string, Dictionary<string, ArcadeSet>> SetLists = new Dictionary<string, Dictionary<string, ArcadeSet>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Every arcade set the tool's Flycast knows, by name - asked once per tool (its bytes), so the
        /// list is always the one of the Flycast the ids are read with. Null, with why, when there is no tool.</summary>
        public static Dictionary<string, ArcadeSet> Sets(FlycastLayout layout, out string why)
        {
            var tool = Tool(layout, out why);
            if (tool == null) return null;
            string key;
            try { key = Hash(tool); } catch (Exception ex) { why = ex.Message; return null; }
            lock (SetLists) if (SetLists.TryGetValue(key, out var known)) return known;
            try
            {
                var psi = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                psi.ArgumentList.Add("--sets");
                using var p = Process.Start(psi);
                var output = new MemoryStream();
                var read = p.StandardOutput.BaseStream.CopyToAsync(output);
                if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } why = "the tool took too long to list its sets"; return null; }
                read.Wait(2000);
                if (p.ExitCode != 0) { why = "the tool could not list its sets (exit code " + p.ExitCode + ", an older flycast-id?)"; return null; }
                var sets = new Dictionary<string, ArcadeSet>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in Encoding.Latin1.GetString(output.ToArray()).Split('\n'))
                {
                    var f = line.TrimEnd('\r').Split('\t');
                    if (f.Length >= 6 && f[0] == "set")
                        sets[f[1]] = new ArcadeSet { Name = f[1], System = f[2], Parent = f[3].Length > 0 ? f[3] : null, GdRom = f[4].Length > 0 ? f[4] : null, Description = f[5] };
                    else if (f.Length >= 2 && f[0] == "bios" && !sets.ContainsKey(f[1]))
                        sets[f[1]] = new ArcadeSet { Name = f[1], System = BiosSystem };
                }
                if (sets.Count == 0) { why = "the tool listed no set"; return null; }
                lock (SetLists) SetLists[key] = sets;
                Log.Info("arcade sets: " + sets.Count + " known to " + ToolName);
                return sets;
            }
            catch (Exception ex) { why = "the tool could not be run (" + ex.Message + ")"; return null; }
        }

        /// <summary>What a zip's CONTENT is, by its files' CRCs (flycast-id --identify): the set it matches best
        /// (Best, all of its files there when BestWhole) and the set it is NAMED as (Own, all of its files there -
        /// its parent's zip counted in - when OwnWhole).</summary>
        internal sealed class Identified
        {
            public string Best, Own;
            public bool BestWhole, OwnWhole;
        }

        /// <summary>Many zips at once, one tool: path -> what it is. <paramref name="progress"/> is told each one
        /// done; <paramref name="cancelled"/> stops it. Null, with why, when the tool cannot be asked.</summary>
        public static Dictionary<string, Identified> Identify(FlycastLayout layout, IList<string> zips, Action<int> progress, Func<bool> cancelled, out string why)
        {
            var tool = Tool(layout, out why);
            if (tool == null) return null;
            var result = new Dictionary<string, Identified>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var psi = new ProcessStartInfo(tool)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false),
                };
                psi.ArgumentList.Add("--identify");
                psi.ArgumentList.Add("-");
                using var p = Process.Start(psi);
                var writer = System.Threading.Tasks.Task.Run(() =>
                {
                    try { foreach (var z in zips) p.StandardInput.WriteLine(z); p.StandardInput.Close(); } catch { }
                });
                p.StandardError.ReadToEndAsync();
                int done = 0;
                string line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    var f = line.Split('\t');
                    if (f.Length >= 5)
                        result[f[0]] = new Identified
                        {
                            Best = f[1].Length > 0 ? f[1] : null, BestWhole = Whole(f[2]),
                            Own = f[3].Length > 0 ? f[3] : null, OwnWhole = Whole(f[4]),
                        };
                    progress?.Invoke(++done);
                    if (cancelled != null && cancelled()) { try { p.Kill(); } catch { } why = "cancelled"; return null; }
                }
                p.WaitForExit(5000);
                return result;
            }
            catch (Exception ex) { why = "the tool could not be run (" + ex.Message + ")"; return null; }
        }

        /// <summary>"found/needed" said whole: every file there, and at least one needed.</summary>
        private static bool Whole(string count)
        {
            var at = count.IndexOf('/');
            return at > 0 && int.TryParse(count.Substring(0, at), out var found) && int.TryParse(count.Substring(at + 1), out var needed)
                   && needed > 0 && found == needed;
        }

        /// <summary>Where a GD-ROM set's image is, as Flycast looks for it: &lt;folder&gt;\&lt;set&gt;\&lt;image&gt;.chd,
        /// else the parent's folder (gdcartridge.cpp). Null when neither is there.</summary>
        public static string GdRomImage(string zipPath, ArcadeSet set)
        {
            if (set?.GdRom == null) return null;
            try
            {
                var dir = Path.GetDirectoryName(zipPath) ?? "";
                foreach (var owner in new[] { set.Name, set.Parent })
                {
                    if (owner == null) continue;
                    var chd = Path.Combine(dir, owner, set.GdRom + ".chd");
                    if (File.Exists(chd)) return chd;
                }
            }
            catch { }
            return null;
        }

        // ── learned from Flycast's log ───────────────────────────────────────

        /// <summary>What a launch that learns the id from the log needs to know once Flycast has quit.</summary>
        internal sealed class Learning
        {
            public string LogPath, RomPath;
            public long From;          // where the log stood - a log of the user's own is read from there
            public bool Ours;          // asked for by this plugin: deleted once read
        }

        /// <summary>Before a launch whose id is unknown: the log asked for (unless the user keeps one) and where
        /// it will be read from. Null when there is nothing to learn from.</summary>
        public static Learning BeforeLaunch(FlycastLayout layout, string romPath, out string configArgument)
        {
            configArgument = null;
            if (string.IsNullOrEmpty(layout?.InstallDir)) return null;
            // Flycast writes "flycast.log" in its working folder, which a launch from LaunchBox is its own.
            var log = Path.Combine(layout.InstallDir, LogName);
            bool users = FlycastIni.AsBool(FlycastIni.Read(layout.ConfigFile, "log", "LogToFile").TryGetValue("LogToFile", out var v) ? v : null, false);
            try
            {
                if (!users && File.Exists(log)) File.Delete(log);   // one this plugin asked for before, left by a session that did not end
            }
            catch (Exception ex) { Log.Warn("game id: could not clear " + LogName, ex); }
            long from = 0;
            try { from = users && File.Exists(log) ? new FileInfo(log).Length : 0; } catch { }
            if (!users) configArgument = "log:LogToFile=yes";
            return new Learning { LogPath = log, RomPath = romPath, From = from, Ours = !users };
        }

        /// <summary>Once Flycast has quit: the id out of what the session wrote to the log, kept for the rom.</summary>
        public static string AfterExit(FlycastLayout layout, Learning learning)
        {
            if (learning == null) return null;
            string id = null;
            try
            {
                if (File.Exists(learning.LogPath))
                {
                    using var s = new FileStream(learning.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (learning.From > 0 && learning.From <= s.Length) s.Seek(learning.From, SeekOrigin.Begin);
                    using var r = new StreamReader(s, Encoding.Latin1);
                    id = FromLog(r.ReadToEnd());
                }
                else Log.Info("game id: no " + LogName + " after the session (" + learning.LogPath + ")");
            }
            catch (Exception ex) { Log.Warn("game id: could not read " + LogName, ex); }
            finally
            {
                try { if (learning.Ours && File.Exists(learning.LogPath)) File.Delete(learning.LogPath); }
                catch (Exception ex) { Log.Warn("game id: could not delete " + LogName, ex); }
            }
            if (id != null) Keep(layout, learning.RomPath, id, "learned from Flycast's log");
            else Log.Info("game id of " + Path.GetFileName(learning.RomPath) + ": not in Flycast's log");
            return id;
        }

        /// <summary>The last "Game ID is [<ID>]" of a log (emulator.cpp, loadGameSpecificSettings); null when
        /// there is none, or an empty one.</summary>
        internal static string FromLog(string text)
        {
            const string Marker = "Game ID is [";
            string id = null;
            foreach (var line in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                int at = line.IndexOf(Marker, StringComparison.Ordinal);
                int end = line.LastIndexOf(']');
                if (at < 0 || end < at + Marker.Length) continue;
                var found = line.Substring(at + Marker.Length, end - at - Marker.Length);
                if (found.Trim().Length > 0 && !found.Any(c => c < 0x20 || c > 0x7E)) id = found;
            }
            return id;
        }
    }
}
