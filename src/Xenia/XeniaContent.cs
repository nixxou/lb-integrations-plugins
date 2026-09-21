// The Xbox 360 save unit, and why it stops where it does.
//
// Xenia lays content out as, in canary:
//
//     content\<XUID:016X>\<TITLEID:08X>\<TYPE:08X>\<package name>\
//     content\<XUID:016X>\<TITLEID:08X>\Headers\<TYPE:08X>\<package name>.header
//
// and in master, which predates profiles, without the XUID level:
//
//     content\<TITLEID:08X>\<TYPE:08X>\<package name>\
//
// A real install can hold BOTH at once, because canary migrates in place rather than all at once.
// The discriminator at the top of content\ is the name length: 16 hex digits is an XUID, 8 is a
// legacy title id.
//
// THE UNIT IS THE CONTENT-TYPE FOLDER 00000001, not the title folder. Under the same title id sit
// 00000002 (downloadable content) and 000B0000 (title updates); syncing those as though they were
// progress would push gigabytes of add-ons around. This is also exactly where Argosy stops, and
// matching it is what lets the two ends agree on a hash.
//
// The XUID above the unit belongs to whoever is signed in. It is ENUMERATED, never constructed:
// inventing one produces a directory the emulator never reads. The machine XUID, all zeroes, holds
// no saved games and is skipped.
//
// The `.header` beside the unit carries the save's display name, thumbnail and license mask. It is
// NOT part of the unit - Argosy does not send it, and including it would move the hash - so it
// travels in LiteBox's reserved `.litebox-plugin` folder inside the vault copy, which SaveVault
// excludes from every hash and from the zip served to clients.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Xenia
{
    internal sealed class XeniaSaveUnit
    {
        /// <summary>8 uppercase hex digits.</summary>
        public string TitleId;
        /// <summary>16 uppercase hex digits, or null on a master-style tree that has no profile level.</summary>
        public string Xuid;
        /// <summary>The content-type folder itself: ...\&lt;TITLEID&gt;\00000001</summary>
        public string UnitPath;
        /// <summary>...\&lt;TITLEID&gt;\Headers\00000001, or null when absent.</summary>
        public string HeadersPath;
        public long SizeBytes;
        public DateTime LastWriteUtc;
        /// <summary>The package folders inside the unit - one per save the game created.</summary>
        public List<string> Packages = new List<string>();
    }

    internal static class XeniaContent
    {
        public const string SavedGameType = "00000001";
        public const string HeadersDirName = "Headers";
        public const string MachineXuid = "0000000000000000";
        private const int XuidLength = 16;
        private const int TitleIdLength = 8;

        /// <summary>Every saved-game unit under a content root, across both tree shapes.</summary>
        public static List<XeniaSaveUnit> Enumerate(string contentRoot)
        {
            var units = new List<XeniaSaveUnit>();
            if (string.IsNullOrWhiteSpace(contentRoot) || !SafeDir(contentRoot)) return units;

            foreach (var top in SafeDirs(contentRoot))
            {
                var name = Path.GetFileName(top);
                if (IsHex(name, XuidLength))
                {
                    // Canary: a profile. The machine XUID holds no saved games.
                    if (string.Equals(name, MachineXuid, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var titleDir in SafeDirs(top))
                        AddIfSavedGame(units, titleDir, name.ToUpperInvariant());
                }
                else if (IsHex(name, TitleIdLength))
                {
                    // Master, or a canary install that has not been migrated yet.
                    AddIfSavedGame(units, top, null);
                }
            }
            return units;
        }

        /// <summary>The unit for one title id, or null. When several profiles hold the same title, the
        /// one whose files were written last wins - the same rule Argosy applies, so the two ends pick
        /// the same save.</summary>
        public static XeniaSaveUnit ForTitleId(string contentRoot, string titleId)
        {
            if (string.IsNullOrWhiteSpace(titleId)) return null;
            return Enumerate(contentRoot)
                .Where(u => string.Equals(u.TitleId, titleId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(u => u.LastWriteUtc)
                .FirstOrDefault();
        }

        private static void AddIfSavedGame(List<XeniaSaveUnit> into, string titleDir, string xuid)
        {
            var titleId = Path.GetFileName(titleDir);
            if (!IsHex(titleId, TitleIdLength)) return;

            var unitPath = Path.Combine(titleDir, SavedGameType);
            if (!SafeDir(unitPath)) return;                     // the title is here, but with no saves

            var unit = new XeniaSaveUnit
            {
                TitleId = titleId.ToUpperInvariant(),
                Xuid = xuid,
                UnitPath = unitPath,
                Packages = SafeDirs(unitPath).ToList(),
            };

            var headers = Path.Combine(titleDir, HeadersDirName, SavedGameType);
            if (SafeDir(headers)) unit.HeadersPath = headers;

            foreach (var file in SafeFiles(unitPath))
            {
                try
                {
                    var fi = new FileInfo(file);
                    unit.SizeBytes += fi.Length;
                    if (fi.LastWriteTimeUtc > unit.LastWriteUtc) unit.LastWriteUtc = fi.LastWriteTimeUtc;
                }
                catch { }
            }
            into.Add(unit);
        }

        /// <summary>Copy the unit into <paramref name="destination"/> under the name Argosy roots its
        /// archive at - the literal "00000001" - with the headers tucked into LiteBox's reserved
        /// metadata folder so they travel with the backup without entering its fingerprint.</summary>
        public static bool CopyInto(XeniaSaveUnit unit, string destination, string metaDirName, out string error)
        {
            error = null;
            try
            {
                if (unit == null || !SafeDir(unit.UnitPath))
                { error = "This Xenia save is no longer on disk."; return false; }
                if (string.IsNullOrWhiteSpace(destination))
                { error = "No destination folder was given."; return false; }

                Directory.CreateDirectory(destination);
                CopyDirectory(unit.UnitPath, Path.Combine(destination, SavedGameType));

                if (unit.HeadersPath != null && metaDirName != null)
                    CopyDirectory(unit.HeadersPath,
                                  Path.Combine(destination, metaDirName, HeadersDirName, SavedGameType));
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("could not copy the save unit", ex);
                error = ex.Message;
                return false;
            }
        }

        public static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
            foreach (var dir in Directory.EnumerateDirectories(source))
                CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }

        public static bool IsHex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            foreach (var c in value)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            return true;
        }

        private static bool SafeDir(string p)
        {
            try { return Directory.Exists(p); } catch { return false; }
        }

        private static IEnumerable<string> SafeDirs(string p)
        {
            try { return Directory.EnumerateDirectories(p).ToList(); } catch { return Array.Empty<string>(); }
        }

        private static IEnumerable<string> SafeFiles(string p)
        {
            try { return Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories); }
            catch { return Array.Empty<string>(); }
        }
    }
}
