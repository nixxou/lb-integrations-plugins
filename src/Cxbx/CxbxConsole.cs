// Cxbx-Reloaded's console set as YOUR console for good, not only for a game (Mehdi, 05/10: "a l'install, pour faire bien les
// choses, on peut essayer d'ecrire aussi les donnees") - at install and update, and from the Nixx window's "Apply to my
// emulators":
//   keys.bin     a real Xbox's keys (Shared.Xbox\XboxKeys): its EEPROM key, then its certificate key - the 32 bytes
//                Cxbx-Reloaded reads at start (src/common/FilePaths.cpp, LoadXboxKeys). One the user put there (a dump of his
//                own console) is never replaced: the plugin's is the one lbip-keys.txt, beside it, notes.
//   EEPROM.bin   the seed's serial number, MAC, online key and HDD key, signed with keys.bin's EEPROM key (CxbxEeprom).
// A game's session still sets them over (CxbxOptions.Apply: its save's keys) and puts them back after.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LbIntegrations.Xbox;

namespace LbIntegrations.Cxbx
{
    internal static class CxbxConsole
    {
        private const string KeysBin = "keys.bin", Mark = "lbip-keys.txt";

        private static byte[] PackKeys() => XboxKeys.RetailEeprom.Concat(XboxKeys.Retail).ToArray();

        /// <summary>No keys.bin, or the one this plugin wrote (lbip-keys.txt notes its bytes).</summary>
        public static bool KeysAreOurs(string data)
        {
            try
            {
                var f = Path.Combine(data, KeysBin);
                if (!File.Exists(f)) return true;
                var m = Path.Combine(data, Mark);
                return File.Exists(m) && File.ReadAllText(m).Trim() == Convert.ToHexString(File.ReadAllBytes(f));
            }
            catch { return false; }
        }

        /// <summary>The certificate key a save from before the keys were noted was made with: a keys.bin of the user's,
        /// else zero - Cxbx-Reloaded's without one.</summary>
        public static byte[] LegacyCertificateKey(string data)
            => data != null && !KeysAreOurs(data) ? CxbxOptions.CertificateKey(data) : XboxKeys.Zero;

        /// <summary>keys.bin and EEPROM.bin as your console. Never while Cxbx-Reloaded runs. What it did.</summary>
        public static List<string> SetUp(string exe)
        {
            var said = new List<string>();
            var data = CxbxPaths.DataDir(exe);
            if (data == null) { said.Add("its data folder is not known"); return said; }
            if (CxbxPaths.LoaderRunning()) { said.Add("Cxbx-Reloaded is running - close it first"); return said; }
            // A session left behind first: its keys.bin and EEPROM.bin are not the console's.
            CxbxOptions.Restore(exe, "before setting up the console");
            Directory.CreateDirectory(data);

            var keys = Path.Combine(data, KeysBin);
            if (KeysAreOurs(data))
            {
                var want = PackKeys();
                if (!File.Exists(keys) || !File.ReadAllBytes(keys).SequenceEqual(want))
                {
                    File.WriteAllBytes(keys, want);
                    said.Add("keys.bin: a real Xbox's keys");
                }
                File.WriteAllText(Path.Combine(data, Mark), Convert.ToHexString(want));
            }
            else said.Add("keys.bin: yours, kept");

            bool own = !CxbxOptions.Effective(null).ContainsKey("console.hddkey");
            var eeprom = Path.Combine(data, "EEPROM.bin");
            if (!File.Exists(eeprom)) { CxbxEeprom.MatchRegion(exe, null, createOnly: true); if (File.Exists(eeprom)) said.Add("EEPROM.bin made as your console"); }
            else if (own) said.Add("EEPROM.bin: Cxbx-Reloaded's own identity kept (the Console identity option)");
            else if (CxbxEeprom.ApplyIdentity(eeprom, data) is string done) said.Add(done);
            Log.Info("console: " + (said.Count == 0 ? "already your console" : string.Join("; ", said)));
            return said;
        }
    }
}
