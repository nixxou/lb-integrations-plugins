// Xenia Canary's profiles: what its "No Profiles Found" window asks for at every start until there is one, and
// without which a game cannot save.
//
// A PROFILE IS ONE FILE, measured (01/10, canary 74c4e4a): <content root>\<XUID>\FFFE07D1\00010000\<XUID>\Account,
// 0x194 bytes. FindProfiles lists the folders named by sixteen hex digits that hold that path; LoadAccount decrypts
// the file. Xenia writes the package's header (FFFE07D1\Headers\00010000\<XUID>.header) itself when it mounts the
// profile, and its own Create Profile button writes nothing more than this file (ProfileManager::CreateAccount: the
// gamertag and zeros). So a profile written here IS what that button writes - byte for byte, its confounder being
// the fixed 0xFD of EncryptAccountFile - and a fabricated one loaded and signed in at the first try.
//
// THE FILE (ProfileManager::EncryptAccountFile): 16 bytes of HMAC-SHA1(key 0x19, plain) | RC4(HMAC-SHA1(key 0x19,
// those 16 bytes)[0..16], plain), where plain = 8 bytes of confounder + X_XAMACCOUNTINFO (0x17C bytes, big-endian;
// the gamertag at 0x08, sixteen UTF-16 characters). Key 0x19 is the retail XeKey Xenia carries in crypto_utils.cc.
//
// SIGNED IN AT START: the cvar logged_profile_slot_0_xuid ([Profiles] of the TOML) names the profile Xenia signs into
// slot 0. Its own button sets it for the first profile (CreateProfileUI: autologin when it is the only one).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LbIntegrations.Xenia
{
    internal sealed class XeniaProfileInfo
    {
        public string Xuid;            // sixteen hex digits, upper case: the folder's name
        public string Gamertag;
        public string AccountPath;
        public override string ToString() => Gamertag + " (" + Xuid + ")";
    }

    internal static class XeniaProfile
    {
        private static readonly byte[] Key19 = { 0xE1, 0xBC, 0x15, 0x9C, 0x73, 0xB1, 0xEA, 0xE9, 0xAB, 0x31, 0x70, 0xF3, 0xAD, 0x47, 0xEB, 0xF3 };

        public const int AccountInfoSize = 0x17C;
        public const int FileSize = AccountInfoSize + 0x18;
        private const int GamertagAt = 0x08, GamertagChars = 16;
        private const string Dashboard = "FFFE07D1", ProfileType = "00010000";

        public const string SlotKey = "logged_profile_slot_0_xuid";

        // ── the file ─────────────────────────────────────────────────────────

        public static byte[] Encrypt(byte[] accountInfo)
        {
            if (accountInfo.Length != AccountInfoSize) throw new ArgumentException("an account is " + AccountInfoSize + " bytes");
            var plain = new byte[8 + AccountInfoSize];
            for (int i = 0; i < 8; i++) plain[i] = 0xFD;
            Array.Copy(accountInfo, 0, plain, 8, AccountInfoSize);
            var hash = Hmac(plain).Take(16).ToArray();
            var rc4Key = Hmac(hash).Take(16).ToArray();
            var output = new byte[FileSize];
            Array.Copy(hash, output, 16);
            Array.Copy(Rc4(rc4Key, plain), 0, output, 16, plain.Length);
            return output;
        }

        /// <summary>The account inside a file, or null when its hash does not check (a devkit's, or not an account).</summary>
        public static byte[] Decrypt(byte[] file)
        {
            if (file == null || file.Length < FileSize) return null;
            var hash = file.Take(16).ToArray();
            var rc4Key = Hmac(hash).Take(16).ToArray();
            var plain = Rc4(rc4Key, file.Skip(16).Take(8 + AccountInfoSize).ToArray());
            if (!Hmac(plain).Take(16).SequenceEqual(hash)) return null;
            return plain.Skip(8).ToArray();
        }

        public static string GamertagOf(byte[] accountInfo)
            => Encoding.BigEndianUnicode.GetString(accountInfo, GamertagAt, GamertagChars * 2).TrimEnd('\0');

        /// <summary>A new account: the gamertag and zeros, as ProfileManager::CreateAccount makes it.</summary>
        public static byte[] NewAccount(string gamertag)
        {
            var a = new byte[AccountInfoSize];
            SetGamertag(a, gamertag);
            return a;
        }

        private static void SetGamertag(byte[] a, string gamertag)
        {
            Array.Clear(a, GamertagAt, GamertagChars * 2);
            // copy_and_swap_truncating: at most fifteen characters and the terminator.
            var tag = Encoding.BigEndianUnicode.GetBytes(gamertag.Length > GamertagChars - 1 ? gamertag.Substring(0, GamertagChars - 1) : gamertag);
            Array.Copy(tag, 0, a, GamertagAt, tag.Length);
        }

        private static byte[] Hmac(byte[] data)
        {
            using var h = new HMACSHA1(Key19);
            return h.ComputeHash(data);
        }

        private static byte[] Rc4(byte[] key, byte[] data)
        {
            var s = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
            for (int i = 0, j = 0; i < 256; i++)
            {
                j = (j + s[i] + key[i % key.Length]) & 255;
                (s[i], s[j]) = (s[j], s[i]);
            }
            var output = new byte[data.Length];
            for (int n = 0, i = 0, j = 0; n < data.Length; n++)
            {
                i = (i + 1) & 255;
                j = (j + s[i]) & 255;
                (s[i], s[j]) = (s[j], s[i]);
                output[n] = (byte)(data[n] ^ s[(s[i] + s[j]) & 255]);
            }
            return output;
        }

        // ── the gamertag ─────────────────────────────────────────────────────

        /// <summary>ProfileManager::IsGamertagValid: 1 to 15 characters, a letter first, then letters and digits,
        /// single spaces between words.</summary>
        public static bool IsValidGamertag(string tag)
            => !string.IsNullOrEmpty(tag) && tag.Length <= 15 && Regex.IsMatch(tag, "^[A-Za-z][A-Za-z0-9]*( [A-Za-z0-9]+)*$");

        /// <summary>A valid gamertag made of <paramref name="name"/> - this Windows account's name by default - or "Player".</summary>
        public static string GamertagFrom(string name = null)
        {
            try { name ??= Environment.UserName; } catch { }
            // The pack's one conversion (src\Shared.Identity): the "Your console" tab shows the very gamertag made here.
            return LbIntegrations.Identity.PackIdentity.Gamertag(name);
        }

        // ── on disk ──────────────────────────────────────────────────────────

        public static string AccountPath(string contentRoot, string xuid)
            => Path.Combine(contentRoot, xuid, Dashboard, ProfileType, xuid, "Account");

        /// <summary>The profiles Xenia would find (FindProfiles), with their gamertags.</summary>
        public static List<XeniaProfileInfo> Find(string contentRoot)
        {
            var found = new List<XeniaProfileInfo>();
            try
            {
                if (string.IsNullOrWhiteSpace(contentRoot) || !Directory.Exists(contentRoot)) return found;
                foreach (var dir in Directory.GetDirectories(contentRoot))
                {
                    var xuid = Path.GetFileName(dir);
                    if (!Regex.IsMatch(xuid, "^[0-9A-F]{16}$") || xuid == new string('0', 16)) continue;
                    var path = AccountPath(contentRoot, xuid);
                    if (!File.Exists(path)) continue;
                    string tag = null;
                    try { var a = Decrypt(File.ReadAllBytes(path)); if (a != null) tag = GamertagOf(a); } catch { }
                    found.Add(new XeniaProfileInfo { Xuid = xuid, Gamertag = tag ?? "?", AccountPath = path });
                }
            }
            catch (Exception ex) { Log.Warn("could not list the profiles", ex); }
            return found.OrderBy(p => p.Xuid, StringComparer.Ordinal).ToList();
        }

        /// <summary>GenerateXuid: 0xE03 in the top twelve bits, a random number under 2^31 below.</summary>
        public static string NewXuid(string contentRoot)
        {
            var random = new Random();
            for (int tries = 0; ; tries++)
            {
                ulong xuid = (0xE03UL << 52) + (ulong)random.Next(int.MaxValue);
                var text = xuid.ToString("X16");
                if (tries > 50 || !Directory.Exists(Path.Combine(contentRoot, text))) return text;
            }
        }

        /// <summary>A profile written for <paramref name="gamertag"/>; its xuid.</summary>
        public static string Create(string contentRoot, string gamertag)
        {
            if (!IsValidGamertag(gamertag)) throw new ArgumentException("\"" + gamertag + "\" is not a gamertag Xenia accepts");
            var xuid = NewXuid(contentRoot);
            var path = AccountPath(contentRoot, xuid);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, Encrypt(NewAccount(gamertag)));
            Log.Info("profile created: " + gamertag + " (" + xuid + ") -> " + path);
            return xuid;
        }

        /// <summary>The gamertag of an existing profile changed - every other byte of its account kept.</summary>
        public static void Rename(string contentRoot, string xuid, string gamertag)
        {
            if (!IsValidGamertag(gamertag)) throw new ArgumentException("\"" + gamertag + "\" is not a gamertag Xenia accepts");
            var path = AccountPath(contentRoot, xuid);
            var account = Decrypt(File.ReadAllBytes(path)) ?? throw new InvalidDataException(path + " does not decrypt as a retail account");
            var before = GamertagOf(account);
            SetGamertag(account, gamertag);
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, Encrypt(account));
            File.Move(tmp, path, overwrite: true);
            Log.Info("profile " + xuid + " renamed: " + before + " -> " + gamertag);
        }

        /// <summary>The profile Xenia signs into at start, from its TOML - null when none.</summary>
        public static string SignedIn(string configFile)
        {
            var v = XeniaToml.Read(configFile, SlotKey);
            return string.IsNullOrWhiteSpace(v) ? null : v.Trim().ToUpperInvariant();
        }

        public static void SignInAtStart(string configFile, string xuid)
            => XeniaToml.Write(configFile, "Profiles", SlotKey, "\"" + xuid + "\"");
    }
}
