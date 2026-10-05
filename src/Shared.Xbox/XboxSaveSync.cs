// The active save and the console, kept in step (Mehdi, 05/10) - the same rule for Cxbx-Reloaded and xemu.
//
// THE ACTIVE SAVE is <emulator>\lbip-saves\<title id>.cxbxsave. THE CONSOLE is what the game reads: Cxbx-Reloaded's folder
// E:\UDATA\<title id>, xemu's the same folder inside the game's disk. THE STAMP, kept WITH THE CONSOLE and not with the saves
// (Mehdi: a stamp copied with a save would lie) - xemu hdd\games\<title id>.stamp, Cxbx-Reloaded <data>\lbip-stamps\<title
// id>.stamp - two lines:
//     content=<the CONTENT HASH of the save the two last agreed on>
//     keys=lbip-xbox-keys/1 hdd=... cert=...      the keys the console's save is written with (Mehdi, 05/10)
// The keys are noted at each launch, before the game runs (NoteSessionKeys), and a capture writes them into the save it
// makes - so they survive the host killed mid-game, and a conflict's copy carries the keys of what it holds.
//
// THE CONTENT HASH: SHA-256 of the save's files - sorted by their path ('/' separated, ordinal), each its path, its length
// and its bytes - lbip-xbox-keys.txt left out. Neither the zip's dates, compression nor comment count, only the files.
// Every comparison here is of whole contents, byte for byte, through it.
//
// AT A LAUNCH, and when LaunchBox removes the active save (Remove):
//   console = save                         nothing (the stamp written if it was not)
//   the save changed since the stamp       laid into the console (removed from it, the file gone), the stamp made again
//   the console changed since the stamp    captured into the save (a session cut short, a save copied into the console)
//   both changed                           the console's version kept in lbip-conflicts\, then the save laid in
//   no stamp yet                           a save there wins over a console without any; with no save, the console is
//                                          captured - NEVER "no stamp" read as "the save was deleted"
// A RESTORE (LaunchBox writing the active save) always lays the save in: the console's version kept in lbip-conflicts\
// first when it changed since the last agreement - none when it is that agreement (it is in LaunchBox's backups).
// AT A SESSION'S END, AND WHEN LAUNCHBOX LISTS SAVES (its backups go through the list): only what is safe - the console
// captured when the save is unchanged since the stamp (or there is neither save nor stamp); never laid in, never a conflict
// settled: the next launch does that.
// A SAVE IS WRITTEN AT ONCE: its files and its keys into one .part file, moved into place; the stamp after. Anything failing
// leaves the save and the stamp as they were.
// The callers never run it while the emulator runs: the game may be writing.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers.Zip;

namespace LbIntegrations.Xbox
{
    internal enum XboxSyncMode { Launch, Restore, Listing, SessionEnd }

    /// <summary>One game's save on one emulator: where its files are, and how its console is read and written.</summary>
    internal sealed class XboxSaveSide
    {
        public string TitleId;
        public string Pack;                                       // lbip-saves\<title id>.cxbxsave
        public string StampPath;                                  // with the console
        public string ConflictDir;                                // the console's version kept there in a conflict
        public Func<List<(string Name, byte[] Data)>> ReadConsole;   // its save's files - empty when it has none
        public Action LayIn;                                      // the active save into the console, its folder replaced
        public Func<bool> RemoveFromConsole;
        public Func<SaveKeys> NaturalKeys;                        // the keys a launch would use now, when none are known
        public Action<string> Log;
    }

    internal static class XboxSaveSync
    {
        private static readonly DateTime Stamp1980 = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly object Gate = new object();

        // ── contents ─────────────────────────────────────────────────────────

        public static string ContentHash(IEnumerable<(string Name, byte[] Data)> files)
        {
            using var sha = SHA256.Create();
            using var buffer = new MemoryStream();
            foreach (var (name, data) in files.Where(f => !XboxSaveKeys.IsKeysEntry(f.Name)).OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                var n = Encoding.UTF8.GetBytes(name.Replace('\\', '/'));
                buffer.Write(BitConverter.GetBytes(n.Length));
                buffer.Write(n);
                buffer.Write(BitConverter.GetBytes((long)data.Length));
                buffer.Write(data);
            }
            buffer.Position = 0;
            return Convert.ToHexString(sha.ComputeHash(buffer));
        }

        /// <summary>The save's files, its keys left out - null when there is no file.</summary>
        public static List<(string Name, byte[] Data)> FilesOf(string pack)
        {
            if (pack == null || !File.Exists(pack)) return null;
            var files = new List<(string, byte[])>();
            using var a = ZipArchive.Open(pack);
            foreach (var e in a.Entries)
            {
                if (e.IsDirectory || string.IsNullOrEmpty(e.Key) || XboxSaveKeys.IsKeysEntry(e.Key)) continue;
                using var s = e.OpenEntryStream();
                using var m = new MemoryStream();
                s.CopyTo(m);
                files.Add((e.Key.Replace('\\', '/').TrimStart('/'), m.ToArray()));
            }
            return files;
        }

        /// <summary>A save's zip from its files - and its keys, when given, as lbip-xbox-keys.txt - sorted ordinal, every entry
        /// 1980-01-01, stored, no zip64; written whole to a .part file, then moved into place.</summary>
        public static void WriteZip(IEnumerable<(string Name, byte[] Data)> files, string target, SaveKeys keys = null)
        {
            var all = files.Where(f => !XboxSaveKeys.IsKeysEntry(f.Name)).ToList();
            if (keys?.Hdd != null && keys.Cert != null) all.Add((XboxSaveKeys.EntryName, Encoding.ASCII.GetBytes(XboxSaveKeys.Format(keys.Hdd, keys.Cert))));
            var part = target + ".part";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target)));
                using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new ZipWriter(output, new ZipWriterOptions(CompressionType.None)))
                    foreach (var (name, data) in all.OrderBy(f => f.Name, StringComparer.Ordinal))
                    {
                        using var source = new MemoryStream(data, writable: false);
                        writer.Write(name, source, new ZipWriterEntryOptions { CompressionType = CompressionType.None, ModificationDateTime = Stamp1980, EnableZip64 = false });
                    }
                File.Move(part, target, overwrite: true);
            }
            finally { try { if (File.Exists(part)) File.Delete(part); } catch { } }
        }

        // ── the stamp ────────────────────────────────────────────────────────

        private sealed class Stamp
        {
            public string Content;       // null: no agreement known
            public SaveKeys Keys;        // null: not known
        }

        /// <summary>The stamp - null when there is none. A stamp of the first days, one hash alone, has no keys.</summary>
        private static Stamp ReadStamp(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var st = new Stamp();
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("content=", StringComparison.Ordinal)) st.Content = line.Substring(8);
                    else if (line.StartsWith("keys=", StringComparison.Ordinal)) st.Keys = XboxSaveKeys.Parse(line.Substring(5));
                    else if (line.Length == 64 && st.Content == null) st.Content = line;
                }
                return st;
            }
            catch { return null; }
        }

        private static void WriteStamp(string path, string content, SaveKeys keys)
        {
            var text = "content=" + content + "\r\n" + (keys?.Hdd != null && keys.Cert != null ? "keys=" + XboxSaveKeys.Format(keys.Hdd, keys.Cert) + "\r\n" : "");
            try { if (File.Exists(path) && File.ReadAllText(path) == text) return; } catch { }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var part = path + ".part";
            File.WriteAllText(part, text);
            File.Move(part, path, overwrite: true);
        }

        private static void DeleteStamp(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

        /// <summary>AT A LAUNCH, before the game runs: the keys this session writes the console with, into its stamp.</summary>
        public static void NoteSessionKeys(XboxSaveSide s, byte[] hdd, byte[] cert)
        {
            if (s == null || hdd == null || cert == null) return;
            lock (Gate)
            {
                var st = ReadStamp(s.StampPath);
                var files = st?.Content == null ? s.ReadConsole() ?? new List<(string, byte[])>() : null;
                WriteStamp(s.StampPath, st?.Content ?? ContentHash(files), new SaveKeys { Hdd = hdd, Cert = cert });
            }
        }

        /// <summary>The notes of 04/10, beside the save (<title id>.stamp, .synced): their agreement read once - the file
        /// as it is, when the note says so - then gone.</summary>
        private static string FromOldNotes(XboxSaveSide s, string saveHash)
        {
            var oldStamp = Path.ChangeExtension(s.Pack, ".stamp");
            var oldSynced = Path.ChangeExtension(s.Pack, ".synced");
            bool had = File.Exists(oldStamp) || File.Exists(oldSynced);
            if (!had) return null;
            string agreed = null;
            try
            {
                if (saveHash != null)
                {
                    if (File.Exists(oldSynced))
                    {
                        var note = File.ReadAllText(oldSynced).Trim();
                        using var sha = SHA1.Create();
                        using var f = File.OpenRead(s.Pack);
                        var h = Convert.ToHexString(sha.ComputeHash(f));
                        if (note == h || note == h + "|" + Path.GetFullPath(s.Pack).ToLowerInvariant()) agreed = saveHash;
                    }
                    else agreed = saveHash;                  // a stamp alone: a file the plugin packed itself
                }
            }
            catch { }
            try { if (File.Exists(oldStamp)) File.Delete(oldStamp); } catch { }
            try { if (File.Exists(oldSynced)) File.Delete(oldSynced); } catch { }
            return agreed;
        }

        // ── the rule ─────────────────────────────────────────────────────────

        /// <summary>The save and the console put in step for <paramref name="mode"/> (the file's head). What it did, or null.</summary>
        public static string Sync(XboxSaveSide s, XboxSyncMode mode)
        {
            lock (Gate)
            {
                var consoleFiles = s.ReadConsole() ?? new List<(string, byte[])>();
                var saveFiles = FilesOf(s.Pack);
                string c = ContentHash(consoleFiles), p = saveFiles == null ? null : ContentHash(saveFiles), empty = ContentHash(new List<(string, byte[])>());
                var st = ReadStamp(s.StampPath);
                if (st == null && FromOldNotes(s, p) is string old) { st = new Stamp { Content = old }; WriteStamp(s.StampPath, old, null); }
                var stamp = st?.Content;
                bool consoleHas = consoleFiles.Count > 0;

                // In step.
                if (p == null && !consoleHas) { DeleteStamp(s.StampPath); return null; }
                if (p != null && c == p) { WriteStamp(s.StampPath, p, st?.Keys ?? XboxSaveKeys.Read(s.Pack)); return null; }

                // A restore: the save LaunchBox wrote is put in, whatever the stamp says.
                if (mode == XboxSyncMode.Restore && p != null)
                    return !consoleHas || c == stamp ? Lay(s, p, "restored: laid into the console")
                                                     : Conflict(s, consoleFiles, st?.Keys, p, "restored over a console changed since the last agreement");

                bool launch = mode == XboxSyncMode.Launch || mode == XboxSyncMode.Restore;
                if (stamp == null)
                {
                    if (p == null) return Capture(s, consoleFiles, c, st?.Keys, "no save yet: the console's captured");
                    if (!consoleHas) return launch ? Lay(s, p, "the save laid into the console") : null;
                    return launch ? Conflict(s, consoleFiles, st?.Keys, p, "the save and the console differ, never agreed") : null;
                }
                if (p == stamp || (p == null && stamp == empty))
                    return Capture(s, consoleFiles, c, st.Keys, "the console changed since the last agreement: captured");
                if (c == stamp)
                {
                    if (!launch) return null;
                    return p == null ? Take(s, "the save removed: taken out of the console") : Lay(s, p, "the save changed: laid into the console");
                }
                return launch ? Conflict(s, consoleFiles, st.Keys, p, "both changed since the last agreement") : null;
            }
        }

        /// <summary>The console made the save, whatever the stamp says - a console put back from a backup of its own.</summary>
        public static string CaptureNow(XboxSaveSide s)
        {
            lock (Gate)
            {
                var files = s.ReadConsole() ?? new List<(string, byte[])>();
                return Capture(s, files, ContentHash(files), ReadStamp(s.StampPath)?.Keys, "captured");
            }
        }

        /// <summary>The keys of what the console holds: its stamp's, else those the save had, else those a launch would use.</summary>
        private static SaveKeys KeysOfConsole(XboxSaveSide s, SaveKeys stamped)
            => stamped ?? XboxSaveKeys.Read(s.Pack) ?? s.NaturalKeys?.Invoke();

        private static string Capture(XboxSaveSide s, List<(string Name, byte[] Data)> files, string hash, SaveKeys stamped, string what)
        {
            if (files.Count == 0)
            {
                // The game removed its save itself.
                if (File.Exists(s.Pack)) File.Delete(s.Pack);
                DeleteStamp(s.StampPath);
                return "the console holds no save any more: its file removed";
            }
            var keys = KeysOfConsole(s, stamped);
            WriteZip(files, s.Pack, keys);
            WriteStamp(s.StampPath, hash, keys);
            return what + " -> " + s.Pack;
        }

        private static string Lay(XboxSaveSide s, string hash, string what)
        {
            s.LayIn();
            // The console now holds the save, made with its keys; the launch notes the session's after (NoteSessionKeys).
            WriteStamp(s.StampPath, hash, XboxSaveKeys.Read(s.Pack));
            return what;
        }

        private static string Take(XboxSaveSide s, string what)
        {
            s.RemoveFromConsole();
            DeleteStamp(s.StampPath);
            return what;
        }

        /// <summary>Both changed: the console's version kept apart - with the keys it was written with - then the save, what the
        /// user put there, wins.</summary>
        private static string Conflict(XboxSaveSide s, List<(string Name, byte[] Data)> consoleFiles, SaveKeys stamped, string saveHash, string what)
        {
            string kept = null;
            if (consoleFiles.Count > 0)
            {
                var name = s.TitleId + "-console-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                kept = Path.Combine(s.ConflictDir, name + ".cxbxsave");
                for (int i = 2; File.Exists(kept); i++) kept = Path.Combine(s.ConflictDir, name + "-" + i + ".cxbxsave");
                // The keys of what the console holds - not of the save about to replace it.
                WriteZip(consoleFiles, kept, stamped ?? s.NaturalKeys?.Invoke());
            }
            var done = saveHash == null ? Take(s, "the save removed: taken out of the console") : Lay(s, saveHash, "the save laid into the console");
            return what + (kept != null ? " - the console's version kept: " + kept : "") + " - " + done;
        }
    }
}
