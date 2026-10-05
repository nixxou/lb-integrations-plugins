// The keys an Xbox signs a game's saves with, and how a session of either emulator is given the ones a save was made with
// (Mehdi, 04/10: "garder la clé hdd et certificat dans le fichier de save").
//
// TWO KEYS COUNT. A game signs its saves with a key the kernel derives from the XBE's certificate and the console's
// CERTIFICATE KEY (XboxSignatureKey = HMAC-SHA1(certificate key, cert.SignatureKey)) - and, for a save tied to the console,
// its HDD KEY too. Measured 04/10 on Batman: Rise of Sin Tzu: a save made under Cxbx-Reloaded (certificate key zero - it has
// none without a keys.bin, src/common/FilePaths.cpp LoadXboxKeys) is "damaged" under xemu (the real key, its BIOS's) and loads
// once xemu runs with the key at zero; the HDD key changed, it still loads.
//
// WHERE THE CERTIFICATE KEY IS in a flash BIOS (tommojphillips/XboxBiosTool, inc/bldr.h and src/Bios.cpp - the layout only,
// the code is this file's):
//   the 2BL        0x6000 bytes ending 0x200 before the end of each 256 KB image (a 1 MB file holds it four times)
//   encrypted      RC4, the key the 16 bytes of the MCPX boot ROM at 0x1A5 (MCPX 1.0) or 0x19C (1.1)
//   decrypted      BOOT_PARAMS in its last 0x24 bytes, signature 0x7854794A ("JyTx") at +8 - the proof the key was right;
//                  u32 at 0 & 0xFFFF = the entry, u32 at entry - 8 & 0xFFFF = the keys: EEPROM key [16], certificate key
//                  [16], kernel key [16]
// An MCPX 1.0 checks only that signature, not a hash of the 2BL - which is what lets modified BIOSes boot; measured 04/10:
// xemu boots a copy with the key changed, and Batman's Cxbx save loads.
//
// The user's BIOS is never written: a session gets a copy (SessionFlash).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Xbox
{
    internal static class XboxKeys
    {
        public const int KeySize = 16;
        public static byte[] Zero => new byte[KeySize];

        private static byte[] CertKey2 = Convert.FromHexString("A73E19C45B82F06D2E91D8470CB563FA").Zip(Convert.FromHexString("FB392A6A5F83078594E84BBAC19A7C1A"), (a, b) => (byte)(a ^ b)).ToArray();

        public static byte[] Retail => (byte[])CertKey2.Clone();

        private static byte[] EKey = Convert.FromHexString("A73E19C45B82F06D2E91D8470CB563FA").Zip(Convert.FromHexString("8D05B4E8EA16BFFE845C153906778DA0"), (a, b) => (byte)(a ^ b)).ToArray();

        public static byte[] RetailEeprom => (byte[])EKey.Clone();

        public static string Hex(byte[] b) => b == null ? null : Convert.ToHexString(b);

        public static byte[] FromHex(string s)
        {
            try { var b = Convert.FromHexString((s ?? "").Trim()); return b.Length == KeySize ? b : null; }
            catch { return null; }
        }

        public static bool Same(byte[] a, byte[] b) => a != null && b != null && a.SequenceEqual(b);

        // ── the flash BIOS's boot loader ─────────────────────────────────────

        private const int Image = 0x40000, Bldr = 0x6000, Mcpx = 0x200;
        private const uint BootSignature = 0x7854794A;
        private static readonly int[] McpxKeyAt = { 0x1A5, 0x19C };

        /// <summary>RC4, in place - the 2BL's cipher.</summary>
        private static void Rc4(byte[] key, byte[] data)
        {
            var s = new byte[256];
            for (int i = 0; i < 256; i++) s[i] = (byte)i;
            for (int i = 0, j = 0; i < 256; i++) { j = (j + s[i] + key[i % key.Length]) & 255; (s[i], s[j]) = (s[j], s[i]); }
            for (int n = 0, i = 0, j = 0; n < data.Length; n++)
            {
                i = (i + 1) & 255; j = (j + s[i]) & 255; (s[i], s[j]) = (s[j], s[i]);
                data[n] ^= s[(s[i] + s[j]) & 255];
            }
        }

        private static byte[] Decrypt(byte[] flash, int at, byte[] key)
        {
            var d = new byte[Bldr];
            Array.Copy(flash, at, d, 0, Bldr);
            Rc4(key, d);
            return d;
        }

        /// <summary>Where the keys are in a decrypted 2BL, or -1.</summary>
        private static int KeysAt(byte[] d)
        {
            if (BitConverter.ToUInt32(d, Bldr - 0x24 + 8) != BootSignature) return -1;
            int entry = (int)(BitConverter.ToUInt32(d, 0) & 0xFFFF);
            if (entry < 8 || entry > Bldr) return -1;
            int keys = (int)(BitConverter.ToUInt32(d, entry - 8) & 0xFFFF);
            return keys + 3 * KeySize <= Bldr ? keys : -1;
        }

        /// <summary>Every 2BL of <paramref name="flash"/> the MCPX ROM's key opens: its offset and that key.</summary>
        private static List<(int At, byte[] Key)> Find(byte[] mcpx, byte[] flash)
        {
            var found = new List<(int, byte[])>();
            if (mcpx == null || mcpx.Length < 0x1B5 || flash == null || flash.Length < Image) return found;
            for (int end = flash.Length; end >= Image; end -= Image)
            {
                int at = end - Mcpx - Bldr;
                foreach (var k in McpxKeyAt)
                {
                    var key = new byte[KeySize]; Array.Copy(mcpx, k, key, 0, KeySize);
                    if (KeysAt(Decrypt(flash, at, key)) >= 0) { found.Add((at, key)); break; }
                }
            }
            return found;
        }

        /// <summary>The certificate key of a flash BIOS - null when its boot loader does not open with the MCPX ROM's key.</summary>
        public static byte[] CertificateKeyOf(string mcpxPath, string flashPath)
        {
            try
            {
                var mcpx = File.ReadAllBytes(mcpxPath);
                var flash = File.ReadAllBytes(flashPath);
                var blocks = Find(mcpx, flash);
                if (blocks.Count == 0) return null;
                var d = Decrypt(flash, blocks[^1].At, blocks[^1].Key);
                return d.Skip(KeysAt(d) + KeySize).Take(KeySize).ToArray();
            }
            catch { return null; }
        }

        /// <summary>The flash BIOS of a session: <paramref name="flash"/> itself when <paramref name="cert"/> is null or the
        /// BIOS's own; else a copy at <paramref name="sessionPath"/> with that certificate key in every boot loader. Null when
        /// the copy cannot be made: the launch then uses the BIOS as it is, and says so.</summary>
        public static string SessionFlash(string mcpxPath, string flash, string sessionPath, byte[] cert, List<string> said)
        {
            if (cert == null) return flash;
            try
            {
                var mcpx = File.ReadAllBytes(mcpxPath);
                var bios = File.ReadAllBytes(flash);
                var blocks = Find(mcpx, bios);
                if (blocks.Count == 0) { said.Add("certificate key NOT set (" + Path.GetFileName(flash) + "'s boot loader does not open with the MCPX ROM's key)"); return null; }
                var own = Decrypt(bios, blocks[^1].At, blocks[^1].Key);
                if (Same(own.Skip(KeysAt(own) + KeySize).Take(KeySize).ToArray(), cert)) { said.Add("certificate key the BIOS's own"); return flash; }
                foreach (var (at, key) in blocks)
                {
                    var d = Decrypt(bios, at, key);
                    cert.CopyTo(d, KeysAt(d) + KeySize);              // the certificate key, after the EEPROM key
                    Rc4(key, d);
                    Array.Copy(d, 0, bios, at, Bldr);
                }
                // Read back: every boot loader still opens, holding that key.
                var again = Find(mcpx, bios);
                if (again.Count != blocks.Count || again.Any(b => { var d = Decrypt(bios, b.At, b.Key); return !Same(d.Skip(KeysAt(d) + KeySize).Take(KeySize).ToArray(), cert); }))
                { said.Add("certificate key NOT set (the copy did not read back)"); return null; }
                var part = sessionPath + ".part";
                File.WriteAllBytes(part, bios);
                File.Move(part, sessionPath, overwrite: true);
                said.Add("certificate key " + (Same(cert, Zero) ? "zero, Cxbx-Reloaded's" : "the save's") + " (" + Path.GetFileName(sessionPath) + ")");
                return sessionPath;
            }
            catch (Exception ex) { said.Add("certificate key NOT set (" + ex.Message + ")"); return null; }
        }
    }
}
