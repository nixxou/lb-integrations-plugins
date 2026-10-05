// The keys a save was made with, carried BY THE SAVE (Mehdi, 04/10: "garder la clé hdd et certificat dans le fichier de
// save"): the HDD key and the certificate key of the session that wrote it (XboxKeys), in an entry at the root of the
// .cxbxsave's zip (Mehdi, 05/10) -
//     lbip-xbox-keys.txt     lbip-xbox-keys/1 hdd=<32 hex> cert=<32 hex>
// - which both plugins leave out when they lay the save into a console (EntryName). The zip stays deterministic: entries
// sorted, 1980, stored. A save of 04/10 held them in the zip's comment instead: still read, moved to the entry when written.
//
// AT LAUNCH, both plugins give the session the keys of the game's save (ForLaunch): its HDD key in the session's EEPROM, its
// certificate key in a keys.bin (Cxbx-Reloaded) or a copy of the flash BIOS (xemu). A save from before the keys were noted
// is Cxbx-Reloaded's (every save of the pack was, then): Cxbx-Reloaded's certificate key without keys.bin, zero; its HDD
// key not imposed. No save: your console's.
// AFTER A SESSION, the capture writes into the save the keys the session ran with - noted in the console's stamp at the
// launch (XboxSaveSync.NoteSessionKeys), so they survive the host killed mid-game.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers.Zip;

namespace LbIntegrations.Xbox
{
    internal sealed class SaveKeys
    {
        public byte[] Hdd;          // null: not imposed
        public byte[] Cert;
        public string Origin;       // for the log
    }

    internal static class XboxSaveKeys
    {
        private const string Tag = "lbip-xbox-keys/1";
        /// <summary>The entry at the zip's root that holds the keys - never laid into a console.</summary>
        public const string EntryName = "lbip-xbox-keys.txt";
        private static readonly DateTime Stamp1980 = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static bool IsKeysEntry(string key) => string.Equals((key ?? "").Replace('\\', '/').TrimStart('/'), EntryName, StringComparison.OrdinalIgnoreCase);

        public static string Format(byte[] hdd, byte[] cert) => Tag + " hdd=" + XboxKeys.Hex(hdd) + " cert=" + XboxKeys.Hex(cert);

        public static SaveKeys Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith(Tag, StringComparison.Ordinal)) return null;
            byte[] hdd = null, cert = null;
            foreach (var part in text.Trim().Substring(Tag.Length).Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.StartsWith("hdd=", StringComparison.Ordinal)) hdd = XboxKeys.FromHex(part.Substring(4));
                else if (part.StartsWith("cert=", StringComparison.Ordinal)) cert = XboxKeys.FromHex(part.Substring(5));
            }
            return hdd != null && cert != null ? new SaveKeys { Hdd = hdd, Cert = cert } : null;
        }

        // ── in the zip ───────────────────────────────────────────────────────

        /// <summary>The end-of-central-directory record of a zip: its offset and its comment's length; -1 when there is none.</summary>
        private static int Eocd(byte[] b, out int commentLength)
        {
            commentLength = 0;
            for (int at = b.Length - 22; at >= 0 && at >= b.Length - 22 - 0xFFFF; at--)
            {
                if (b[at] != 0x50 || b[at + 1] != 0x4B || b[at + 2] != 0x05 || b[at + 3] != 0x06) continue;
                int len = b[at + 20] | (b[at + 21] << 8);
                if (at + 22 + len == b.Length) { commentLength = len; return at; }
            }
            return -1;
        }

        /// <summary>The zip's comment - where a save of 04/10 kept its keys.</summary>
        public static string Comment(string pack)
        {
            try
            {
                var b = File.ReadAllBytes(pack);
                int at = Eocd(b, out var len);
                return at < 0 ? null : Encoding.ASCII.GetString(b, at + 22, len);
            }
            catch { return null; }
        }

        public static SaveKeys Read(string pack)
        {
            if (pack == null || !File.Exists(pack)) return null;
            try
            {
                using var a = ZipArchive.Open(pack);
                var e = a.Entries.FirstOrDefault(x => !x.IsDirectory && IsKeysEntry(x.Key));
                if (e != null)
                {
                    using var s = e.OpenEntryStream();
                    using var r = new StreamReader(s, Encoding.ASCII);
                    if (Parse(r.ReadToEnd()) is SaveKeys k) return k;
                }
            }
            catch { }
            return Parse(Comment(pack));
        }

        /// <summary>The keys written into <paramref name="pack"/> - its entry added or replaced, the comment of 04/10 dropped,
        /// every other entry kept as it is - through a .part file. Unchanged when they are there already.</summary>
        public static void Write(string pack, byte[] hdd, byte[] cert)
        {
            var text = Format(hdd, cert);
            var files = new List<(string Name, byte[] Data)>();
            bool same = false;
            using (var a = ZipArchive.Open(pack))
                foreach (var e in a.Entries)
                {
                    if (e.IsDirectory || string.IsNullOrEmpty(e.Key)) continue;
                    using var s = e.OpenEntryStream();
                    using var m = new MemoryStream();
                    s.CopyTo(m);
                    if (IsKeysEntry(e.Key)) { same = Encoding.ASCII.GetString(m.ToArray()) == text; continue; }
                    files.Add((e.Key, m.ToArray()));
                }
            if (same && string.IsNullOrEmpty(Comment(pack))) return;
            files.Add((EntryName, Encoding.ASCII.GetBytes(text)));
            var part = pack + ".part";
            try
            {
                using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new ZipWriter(output, new ZipWriterOptions(CompressionType.None)))
                    foreach (var (name, data) in files.OrderBy(f => f.Name, StringComparer.Ordinal))
                    {
                        using var source = new MemoryStream(data, writable: false);
                        writer.Write(name, source, new ZipWriterEntryOptions { CompressionType = CompressionType.None, ModificationDateTime = Stamp1980, EnableZip64 = false });
                    }
                File.Move(part, pack, overwrite: true);
            }
            finally { try { if (File.Exists(part)) File.Delete(part); } catch { } }
        }

        /// <summary>The keys a launch is given: the save's - or, for a save from before the keys were noted, Cxbx-Reloaded's
        /// certificate key <paramref name="legacyCert"/>, its HDD key not imposed. Null with no save: your console's.</summary>
        public static SaveKeys ForLaunch(string pack, byte[] legacyCert)
        {
            if (pack == null || !File.Exists(pack)) return null;
            var k = Read(pack);
            if (k != null) { k.Origin = "the save's"; return k; }
            return new SaveKeys { Hdd = null, Cert = legacyCert, Origin = "a save from before the keys were noted: Cxbx-Reloaded's" };
        }
    }
}
