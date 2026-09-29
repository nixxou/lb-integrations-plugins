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
            if (string.IsNullOrEmpty(layout?.InstallDir)) { why = "no Flycast install"; return null; }
            var beside = Path.Combine(layout.InstallDir, ToolName);
            try
            {
                var dir = Path.GetDirectoryName(typeof(FlycastGameIdentity).Assembly.Location);
                var carried = string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "native", ToolName);
                if (carried != null && File.Exists(carried) && !SameBytes(carried, beside))
                {
                    File.Copy(carried, beside, overwrite: true);
                    Log.Info("game id: " + ToolName + " put beside Flycast (" + beside + ")");
                }
            }
            catch (Exception ex) { Log.Warn("game id: could not put " + ToolName + " beside Flycast", ex); }
            if (File.Exists(beside)) return beside;
            why = ToolName + " is not there (neither beside Flycast nor in the plugin's native folder)";
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
