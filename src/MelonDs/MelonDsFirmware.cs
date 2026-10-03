// The firmware dumps melonDS boots on - kept from being written to, and the DS one's owner settled once (Mehdi, 03/10).
//
// MELONDS WRITES THE FIRMWARE FILE IT BOOTS ON. When a game writes the firmware (the Wi-Fi settings, Nintendo WFC), it
// flushes the WHOLE in-memory firmware to DS.FirmwarePath - DSi.FirmwarePath in DSi mode (Platform::WriteFirmware,
// EmuInstance::getEffectiveFirmwareSavePath) - its owner as the override left it included. Those paths are the user's
// own dumps in RetroArch\system (MelonDsBios.DirName). So melonDS is pointed at a COPY of each, in
// <install>\lbip-firmware\, as a NAND is copied into dsi\: the dump is read once and never opened for writing again.
// lbip-firmware.tsv says which copy came from which dump (path, sha256): a dump replaced by another is copied again.
//
// THE DS DUMP'S OWNER, settled at the first launch on it. A DS dump carries the name, language, birthday and colour of the
// console it came from (two copies at [0x20]*8 and +0x100, CRC16 of 0x00-0x6F at 0x72, the newer by the counter at 0x70 -
// GBATEK "DS Firmware User Settings", checked on two dumps).
//   blank (no valid copy, no name, no birthday - G:\DS_lite-W-20060205_2133.bin is one)
//                         the pack's identity, without asking
//   the identity's own    the identity (it is the same person)
//   somebody else's       asked once: the identity, or the dump's owner
// "The identity" is WRITTEN INTO OUR COPY, again at every launch when it changed (WriteOwner) - so it holds with no override
// at all (Mehdi, 03/10: the override is for DSiWare only, below). The answer is kept in lbip-firmware.tsv beside its dump's
// hash and never asked again for that dump; the melonDS tab changes it.
//
// MELONDS'S FIRMWARE OVERRIDE IS THE PLUGIN'S, set for each launch (MelonDsGameSettings.Apply), not the user's to tick:
//   a DS game, a DSi cartridge   off - their owner is written into what they read: the firmware copy above, dsi\work.bin
//                                (MelonDsPlugin.PointCartridgeAtScratch)
//   a DSiWare                    the melonDS tab's "show your console in DSiWare" (lbip-console.ini, on by default): the
//                                one way the identity reaches a console never rewritten - its saves keep its own settings
//                                files (DsiWorkspace.MarkForced)
//   a game with firmware values of its own (its Options window)   on, as always

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LbIntegrations.Dsi;
using LbIntegrations.Identity;

namespace LbIntegrations.MelonDs
{
    internal static class MelonDsFirmware
    {
        private const string CopyDir = "lbip-firmware", IndexName = "lbip-firmware.tsv";

        internal sealed class Owner
        {
            public bool Blank;
            public string Name = "";
            public int Language, Month, Day, Colour;
            public bool MacBlank;
            public string Describe() => Blank ? "nobody (blank)"
                : "\"" + Name + "\" (" + Lang(Language) + ", " + Month + "/" + Day + ", " + MelonDsGameSettings.Colours[Math.Min(15, Math.Max(0, Colour))].ToLowerInvariant() + ")";
        }

        private static string Lang(int l) => l >= 0 && l < MelonDsGameSettings.Languages.Length ? MelonDsGameSettings.Languages[l] : "language " + l;

        // ── the copies ───────────────────────────────────────────────────────

        /// <summary>Point <paramref name="table"/>.FirmwarePath at our copy of the dump it names - made now when there is
        /// none, or when the dump changed since. Returns the copy, or null when nothing is configured (or on failure: the
        /// path is left as it was).</summary>
        public static string Protect(MelonDsLayout layout, string table)
        {
            try
            {
                var keys = MelonDsToml.Read(layout.ConfigFile, table, "FirmwarePath");
                if (!keys.TryGetValue("FirmwarePath", out var set) || string.IsNullOrWhiteSpace(set)) return null;
                var path = Absolute(layout, set);
                var dir = Path.Combine(layout.InstallDir, CopyDir);
                var index = ReadIndex(layout);
                if (IsInside(path, dir))
                {
                    // Already ours: still the copy of the dump it was made from, unless that dump changed.
                    var row = index.FirstOrDefault(r => string.Equals(r.Copy, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
                    if (row == null || !File.Exists(row.Source) || Sha256(row.Source) == row.SourceSha) return File.Exists(path) ? path : null;
                    path = row.Source;          // the dump was replaced: copied again below
                }
                if (!File.Exists(path)) return null;
                var sha = Sha256(path);
                var copy = Path.Combine(dir, Path.GetFileName(path));
                var known = index.FirstOrDefault(r => string.Equals(r.Copy, Path.GetFileName(copy), StringComparison.OrdinalIgnoreCase));
                if (known == null || known.SourceSha != sha || !File.Exists(copy))
                {
                    Directory.CreateDirectory(dir);
                    File.Copy(path, copy + ".tmp", overwrite: true);
                    File.Copy(copy + ".tmp", copy, overwrite: true);
                    File.Delete(copy + ".tmp");
                    index.RemoveAll(r => string.Equals(r.Copy, Path.GetFileName(copy), StringComparison.OrdinalIgnoreCase));
                    index.Add(new Row { Copy = Path.GetFileName(copy), Source = path, SourceSha = sha, Answer = "" });
                    WriteIndex(layout, index);
                    Log.Info("firmware: " + path + " copied to " + copy + " - melonDS writes the firmware it boots on, never the dump");
                }
                var error = MelonDsToml.Write(layout.ConfigFile, table, new Dictionary<string, string>(StringComparer.Ordinal)
                    { ["FirmwarePath"] = MelonDsToml.Text(copy) }, force: true);
                if (error != null) { Log.Warn("firmware: the copy could not be selected - " + error); return null; }
                return copy;
            }
            catch (Exception ex) { Log.Warn("firmware: could not protect the dump", ex); return null; }
        }

        // ── the DS dump's owner ──────────────────────────────────────────────

        /// <summary>The DS firmware copy melonDS boots on, its owner settled - see the header. Called on a DS launch with the
        /// external BIOS on.</summary>
        public static void Settle(MelonDsLayout layout)
        {
            try
            {
                var copy = Protect(layout, MelonDsPaths.DsTable);
                if (copy == null) return;
                var index = ReadIndex(layout);
                var row = index.FirstOrDefault(r => string.Equals(r.Copy, Path.GetFileName(copy), StringComparison.OrdinalIgnoreCase));
                if (row == null) return;

                var owner = Read(File.ReadAllBytes(copy));
                var id = Wanted(layout);
                if (row.Answer.Length > 0)
                {
                    // Settled before: melonDS's owner followed into the copy when it has changed since.
                    if (row.Answer != "dump" && !Same(owner, id)) WriteIdentity(layout, copy, id);
                    return;
                }
                string answer;
                if (owner.Blank) answer = "identity";
                else if (Same(owner, id)) answer = "same";
                else
                {
                    if (!DsiDialog.Available) { Log.Info("firmware: " + Path.GetFileName(row.Source) + " belongs to " + owner.Describe() + "; windows are off, nothing asked"); return; }
                    var mine = id.Describe();
                    var pick = DsiDialog.Ask("melonDS - whose DS is it?",
                        "Your DS firmware dump, " + Path.GetFileName(row.Source) + ", belongs to " + owner.Describe() + "." + Environment.NewLine
                        + Environment.NewLine
                        + "DS games show the console's owner: the name, the language a game starts in, the birthday." + Environment.NewLine
                        + "Use your console (\"Your console\" in the Nixx window) instead: " + mine + "?" + Environment.NewLine
                        + Environment.NewLine
                        + "Asked once for this dump. You can change it later in the melonDS tab of the Nixx window." + Environment.NewLine
                        + "Your dump itself is never written either way: your console goes into melonDS's copy of it.",
                        new[] { "Use mine", "Keep the dump's" });
                    answer = pick == 0 ? "identity" : "dump";
                }

                if (answer != "dump" && !Same(owner, id)) WriteIdentity(layout, copy, id);
                row.Answer = answer;
                WriteIndex(layout, index);
                Log.Info("firmware: " + Path.GetFileName(row.Source) + " belonged to " + owner.Describe() + " - "
                         + (answer == "dump" ? "its owner is kept" : "your console is its owner in melonDS's copy"));
            }
            catch (Exception ex) { Log.Warn("firmware: could not settle the DS firmware's owner", ex); }
        }

        /// <summary>The owner melonDS shows on its own firmware and in DSiWare - its Firmware settings, filled from "Your
        /// console" - which the copy of a dump follows too, so that all three say the same.</summary>
        public static Owner Wanted(MelonDsLayout layout)
        {
            var v = MelonDsGameSettings.Current(layout.ConfigFile);
            int I(string k, int d) => v.TryGetValue(k, out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : d;
            return new Owner
            {
                Blank = false, Name = v.TryGetValue("FwUsername", out var u) ? u : "melonDS", Language = I("FwLanguage", 1),
                Month = I("FwBirthdayMonth", 1), Day = I("FwBirthdayDay", 1), Colour = I("FwColour", 0),
            };
        }

        /// <summary>melonDS's owner as an identity - for a DSi cartridge's scratch image (DsiUserSettings.SetUpScratch): its
        /// name, birthday and colour, its language as a culture; the rest (date, country) from the pack's identity.</summary>
        public static PackIdentity WantedIdentity(MelonDsLayout layout)
        {
            var w = Wanted(layout);
            var id = PackIdentity.Load() ?? PackIdentity.FromWindows();
            string[] cultures = { "ja-JP", "en-US", "fr-FR", "de-DE", "it-IT", "es-ES" };
            id.Nickname = w.Name;
            // The identity's own culture when it is that language (en-GB, pt-BR...), else the language's.
            if (w.Language >= 0 && w.Language < cultures.Length && id.DsLanguage() != w.Language) id.Language = cultures[w.Language];
            id.BirthMonth = w.Month;
            id.BirthDay = w.Day;
            id.Colour = w.Colour;
            return id;
        }

        /// <summary>melonDS's owner as the owner of our copy of the dump. False, said in the log, when it could not be written.</summary>
        private static bool WriteIdentity(MelonDsLayout layout, string copy, Owner id)
        {
            if (copy == null || !File.Exists(copy) || !IsInside(copy, Path.Combine(layout.InstallDir, CopyDir))) return false;
            var why = WriteOwner(copy, id);
            if (why != null) { Log.Warn("firmware: your console could not be written into " + Path.GetFileName(copy) + " - " + why); return false; }
            Log.Info("firmware: your console written into the copy " + Path.GetFileName(copy) + " - " + Read(File.ReadAllBytes(copy)).Describe());
            return true;
        }

        // ── the two choices of the melonDS tab ───────────────────────────────

        private const string ChoicesName = "lbip-console.ini";

        /// <summary>Show the identity in DSiWare titles, over each DSi console's own owner - melonDS's override for a DSiWare
        /// session. On unless the melonDS tab turned it off.</summary>
        public static bool DsiWareOverride(MelonDsLayout layout)
        {
            try
            {
                var p = Path.Combine(layout.InstallDir, ChoicesName);
                if (!File.Exists(p)) return true;
                var line = File.ReadAllLines(p).FirstOrDefault(l => l.StartsWith("dsiware_override=", StringComparison.OrdinalIgnoreCase));
                return line == null || !line.Substring(line.IndexOf('=') + 1).Trim().Equals("false", StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }

        public static void SetDsiWareOverride(MelonDsLayout layout, bool on)
            => MelonDsToml.WriteAtomicBytes(Path.Combine(layout.InstallDir, ChoicesName),
                   Encoding.UTF8.GetBytes("# Nixx-melonDS: show your console (\"Your console\") in DSiWare titles, over each DSi console's own owner\r\n"
                                          + "dsiware_override=" + (on ? "true" : "false") + "\r\n"));

        /// <summary>The DS dump's owner in melonDS: true the identity, false the dump's - the tab's choice, as the first
        /// launch's question would have answered it. The copy follows at once.</summary>
        public static string SetDumpOwner(MelonDsLayout layout, bool identity)
        {
            if (DsiNand.EmulatorRunning()) return "melonDS is running - close it first";
            var copy = Protect(layout, MelonDsPaths.DsTable);
            if (copy == null) return "melonDS boots on no DS firmware dump";
            var index = ReadIndex(layout);
            var row = index.FirstOrDefault(r => string.Equals(r.Copy, Path.GetFileName(copy), StringComparison.OrdinalIgnoreCase));
            if (row == null) return "the copy of the dump is not known";
            if (identity) WriteIdentity(layout, copy, Wanted(layout));
            else if (File.Exists(row.Source))
            {
                // The dump's owner back: the copy made again from it.
                File.Copy(row.Source, copy, overwrite: true);
                row.SourceSha = Sha256(row.Source);
            }
            row.Answer = identity ? "identity" : "dump";
            WriteIndex(layout, index);
            return null;
        }

        /// <summary>The DS firmware dump melonDS boots on - external BIOS on - with the owner of our copy and whether that owner
        /// is the identity (the dump's answer). Null when melonDS boots on its own firmware.</summary>
        public static (string Dump, Owner Owner, bool Identity, Owner DumpOwner)? Active(MelonDsLayout layout)
        {
            try
            {
                var emu = MelonDsToml.Read(layout.ConfigFile, MelonDsPaths.EmuTable, MelonDsPaths.KeyExternalBios);
                if (!(emu.TryGetValue(MelonDsPaths.KeyExternalBios, out var on) && on.Trim() == "true")) return null;
                var ds = MelonDsToml.Read(layout.ConfigFile, MelonDsPaths.DsTable, "FirmwarePath");
                if (!ds.TryGetValue("FirmwarePath", out var set) || string.IsNullOrWhiteSpace(set)) return null;
                var path = Absolute(layout, set);
                if (!File.Exists(path)) return null;
                var row = ReadIndex(layout).FirstOrDefault(r => string.Equals(r.Copy, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)
                                                             && IsInside(path, Path.Combine(layout.InstallDir, CopyDir)));
                var source = row != null && File.Exists(row.Source) ? row.Source : path;
                return (Path.GetFileName(row?.Source ?? path), Read(File.ReadAllBytes(path)), row != null && row.Answer != "dump", Read(File.ReadAllBytes(source)));
            }
            catch { return null; }
        }

        /// <summary>"Apply to my emulators": the identity as the DS dump's owner, written into our copy (the dump never), the
        /// answer kept as the first launch's would be. Null, or why not.</summary>
        public static string UseIdentity(MelonDsLayout layout) => SetDumpOwner(layout, identity: true);

        /// <summary>The identity as the owner of a firmware COPY of ours: name, colour, birthday, language in both user-settings
        /// copies, each with its CRC16 (GBATEK "DS Firmware User Settings": 0x02 colour, 0x03 month, 0x04 day, 0x06 name in
        /// UTF-16 and 0x1A its length, 0x64 bits 0-2 the language, 0x70 counter, 0x72 CRC of 0x00-0x6F). The newer copy keeps
        /// its counter, the older one gets the next and becomes the newer: both are the identity. The extended block
        /// (0x74-0xFF, its own CRC) is not touched. Null, or why not.</summary>
        internal static string WriteOwner(string copy, Owner id)
        {
            var fw = File.ReadAllBytes(copy);
            if (fw.Length < 0x200) return "too small to be a firmware";
            int at = BitConverter.ToUInt16(fw, 0x20) * 8;
            if (at <= 0 || at + 0x200 > fw.Length) at = fw.Length - 0x200;
            int a = at, b = at + 0x100;
            bool va = CrcOk(fw, a), vb = CrcOk(fw, b);
            int newer = va && vb ? (((BitConverter.ToUInt16(fw, b + 0x70) - BitConverter.ToUInt16(fw, a + 0x70)) & 0x7F) is int d && d > 0 && d < 0x40 ? b : a)
                      : vb ? b : a;
            int older = newer == a ? b : a;
            // A blank block (no valid copy at all) is made out of the first one's bytes, version 5 as melonDS's own.
            if (!va && !vb) { fw[newer] = 5; BitConverter.GetBytes((ushort)0).CopyTo(fw, newer + 0x70); }
            int counter = BitConverter.ToUInt16(fw, newer + 0x70);
            var name = (id.Name ?? "").Length > 10 ? id.Name.Substring(0, 10) : id.Name ?? "";
            foreach (var slot in new[] { newer, older })
            {
                if (slot == older) Buffer.BlockCopy(fw, newer, fw, older, 0x100);      // the whole block, extended part and its CRC too
                fw[slot + 0x02] = (byte)Math.Min(15, Math.Max(0, id.Colour));
                fw[slot + 0x03] = (byte)Math.Min(12, Math.Max(1, id.Month));
                fw[slot + 0x04] = (byte)Math.Min(31, Math.Max(1, id.Day));
                if (name.Length > 0)
                {
                    Array.Clear(fw, slot + 0x06, 20);
                    var utf16 = Encoding.Unicode.GetBytes(name);
                    Buffer.BlockCopy(utf16, 0, fw, slot + 0x06, Math.Min(20, utf16.Length));
                    BitConverter.GetBytes((ushort)Math.Min(10, name.Length)).CopyTo(fw, slot + 0x1A);
                }
                fw[slot + 0x64] = (byte)((fw[slot + 0x64] & ~0x07) | (id.Language & 0x07));
                BitConverter.GetBytes((ushort)(slot == newer ? counter : (counter + 1) & 0x7F)).CopyTo(fw, slot + 0x70);
                BitConverter.GetBytes((ushort)Crc16(fw, slot, 0x70, 0xFFFF)).CopyTo(fw, slot + 0x72);
            }
            var tmp = copy + ".tmp";
            File.WriteAllBytes(tmp, fw);
            File.Copy(tmp, copy, overwrite: true);
            File.Delete(tmp);
            return null;
        }

        /// <summary>The identity in the words of Owner.Describe - for "Apply to my emulators".</summary>
        public static string DescribeIdentity(PackIdentity id) => OwnerOf(id).Describe();

        public static Owner OwnerOf(PackIdentity id)
            => new Owner { Blank = false, Name = id.DsNickname(), Language = id.DsLanguage(), Month = id.BirthMonth, Day = id.BirthDay, Colour = id.Colour };

        private static bool Same(Owner o, Owner w)
            => !o.Blank && o.Name == w.Name && o.Language == w.Language && o.Month == w.Month && o.Day == w.Day && o.Colour == w.Colour;

        /// <summary>A DS firmware's owner: the newer of its two user-settings copies whose CRC holds.</summary>
        public static Owner Read(byte[] fw)
        {
            var o = new Owner { Blank = true };
            try
            {
                if (fw == null || fw.Length < 0x200) return o;
                int at = BitConverter.ToUInt16(fw, 0x20) * 8;
                if (at <= 0 || at + 0x200 > fw.Length) at = fw.Length - 0x200;
                int a = at, b = at + 0x100;
                bool va = CrcOk(fw, a), vb = CrcOk(fw, b);
                int use;
                if (va && vb) use = ((BitConverter.ToUInt16(fw, b + 0x70) - BitConverter.ToUInt16(fw, a + 0x70)) & 0x7F) is int d && d > 0 && d < 0x40 ? b : a;
                else if (va) use = a;
                else if (vb) use = b;
                else return o;
                int len = Math.Min(10, (int)fw[use + 0x1A]);
                o.Name = Encoding.Unicode.GetString(fw, use + 6, len * 2);
                o.Colour = fw[use + 2] & 0x0F;
                o.Month = fw[use + 3];
                o.Day = fw[use + 4];
                o.Language = fw[use + 0x64] & 0x07;
                o.Blank = len == 0 || o.Month == 0 || o.Day == 0;
                o.MacBlank = fw.Length > 0x3B && fw.Skip(0x36).Take(6).All(x => x == 0xFF) || fw.Skip(0x36).Take(6).All(x => x == 0);
            }
            catch { }
            return o;
        }

        private static bool CrcOk(byte[] fw, int at) => Crc16(fw, at, 0x70, 0xFFFF) == BitConverter.ToUInt16(fw, at + 0x72);

        /// <summary>The firmware's CRC16 (GBATEK "DS Firmware Header", the BIOS's GetCRC16).</summary>
        private static int Crc16(byte[] d, int start, int len, int crc)
        {
            int[] table = { 0xC0C1, 0xC181, 0xC301, 0xC601, 0xCC01, 0xD801, 0xF001, 0xA001 };
            for (int i = 0; i < len; i++)
            {
                crc ^= d[start + i];
                for (int j = 0; j < 8; j++)
                {
                    bool carry = (crc & 1) != 0;
                    crc >>= 1;
                    if (carry) crc ^= table[j] << (7 - j);
                }
            }
            return crc & 0xFFFF;
        }

        private static string NewMac()
        {
            var r = new byte[3];
            RandomNumberGenerator.Fill(r);
            return "00:09:BF:" + r[0].ToString("X2") + ":" + r[1].ToString("X2") + ":" + r[2].ToString("X2");
        }

        // ── lbip-firmware.tsv ────────────────────────────────────────────────

        private sealed class Row { public string Copy, Source, SourceSha, Answer; }

        private static string IndexPath(MelonDsLayout layout) => Path.Combine(layout.InstallDir, IndexName);

        private static List<Row> ReadIndex(MelonDsLayout layout)
        {
            var rows = new List<Row>();
            try
            {
                var p = IndexPath(layout);
                if (!File.Exists(p)) return rows;
                foreach (var line in File.ReadAllLines(p))
                {
                    var c = line.Split('\t');
                    if (c.Length >= 3) rows.Add(new Row { Copy = c[0], Source = c[1], SourceSha = c[2], Answer = c.Length > 3 ? c[3] : "" });
                }
            }
            catch (Exception ex) { Log.Warn("firmware: could not read " + IndexName, ex); }
            return rows;
        }

        private static void WriteIndex(MelonDsLayout layout, List<Row> rows)
            => MelonDsToml.WriteAtomicBytes(IndexPath(layout), Encoding.UTF8.GetBytes(string.Join("\r\n", rows.Select(r => string.Join("\t", r.Copy, r.Source, r.SourceSha, r.Answer))) + "\r\n"));

        private static string Sha256(string path)
        {
            using var s = File.OpenRead(path);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(s)).ToLowerInvariant();
        }

        private static string Absolute(MelonDsLayout layout, string path)
        {
            try { return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(layout.ConfigDir, path)); } catch { return path; }
        }

        private static bool IsInside(string path, string dir)
        {
            try { return Path.GetFullPath(path).StartsWith(Path.GetFullPath(dir).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase); } catch { return false; }
        }
    }
}
