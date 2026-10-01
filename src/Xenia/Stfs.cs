// STFS containers: CON / LIVE / PIRS.
//
// This is the format of every Games-on-Demand title, every XBLA game, every DLC pack and every title
// update - and of the `.header` sidecars Xenia writes beside a save. All of them carry the same
// XContentHeader, so one reader answers for all of them, and the title id is a single big-endian
// integer at a fixed offset. It is the cheapest identification of the lot: one seek.
//
//     0x000  char[4]  "CON " | "LIVE" | "PIRS"
//     0x344  u32 BE   content_type      (00000001 saved game, 00000002 DLC, 000B0000 title update...)
//     0x360  u32 BE   execution_info.title_id
//
// Worth knowing: sigil, which is what Argosy uses to identify games, has no STFS reader at all. A
// user's Games-on-Demand and XBLA titles therefore get no id on the Android side and no save sync.
// They do here.

using System;
using System.IO;

namespace LbIntegrations.Xenia
{
    /// <summary>What an STFS header says about itself.</summary>
    internal sealed class StfsInfo
    {
        public uint TitleId;
        public uint ContentType;
        public string Magic;
        /// <summary>execution_info: the media id of the disc it belongs to, the title version it brings (a title update's
        /// new version), the one it applies over, and the disc of a multi-disc set.</summary>
        public uint MediaId, Version, BaseVersion;
        public byte DiscNumber, DiscCount;
        /// <summary>0 STFS, 1 SVOD (a Games on Demand package, its data in a <name>.data folder beside it).</summary>
        public uint VolumeType;
        /// <summary>The package's own name (English, the first language) and its game's, empty when it has none.</summary>
        public string DisplayName = "", TitleName = "";
    }

    internal static class Stfs
    {
        private const int TitleIdOffset = 0x360;
        private const int ContentTypeOffset = 0x344;
        // xcontent.h's XContentMetadata, from 0x344: execution_info at 0x354 (media id, version, base version, title id,
        // platform, executable type, disc number, disc count), volume_type at 0x3A9, display names at 0x411 (nine
        // languages of 0x100 bytes, UTF-16BE), title name at 0x1691 (0x80 bytes).
        private const int MediaIdOffset = 0x354, VersionOffset = 0x358, BaseVersionOffset = 0x35C, DiscNumberOffset = 0x366,
                          DiscCountOffset = 0x367, VolumeTypeOffset = 0x3A9, DisplayNameOffset = 0x411, TitleNameOffset = 0x1691;
        /// <summary>Everything we read lives inside the header: up to the end of the title name.</summary>
        private const int HeaderProbe = 0x1711;

        public static bool IsStfs(byte[] head)
        {
            if (head == null || head.Length < 4) return false;
            var magic = System.Text.Encoding.ASCII.GetString(head, 0, 4);
            return magic == "CON " || magic == "LIVE" || magic == "PIRS";
        }

        /// <summary>Reads the header of an STFS package, or null when the file is not one.</summary>
        public static StfsInfo Read(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                return Parse(Xex.ReadAt(fs, 0, HeaderProbe));
            }
            catch (Exception ex) { Log.Warn("could not read the STFS header of " + path, ex); return null; }
        }

        /// <summary>The first <see cref="HeaderSize"/> bytes of a package - from a file, or from an archive's entry
        /// decompressed that far and no further - read; null when they are not an STFS header.</summary>
        public static StfsInfo Parse(byte[] head)
        {
            try
            {
                if (!IsStfs(head)) return null;
                return new StfsInfo
                {
                    Magic = System.Text.Encoding.ASCII.GetString(head, 0, 4),
                    ContentType = Xex.BeUInt32(head, ContentTypeOffset),
                    TitleId = Xex.BeUInt32(head, TitleIdOffset),
                    MediaId = head.Length >= 0x358 ? Xex.BeUInt32(head, MediaIdOffset) : 0,
                    Version = head.Length >= 0x35C ? Xex.BeUInt32(head, VersionOffset) : 0,
                    BaseVersion = head.Length >= 0x360 ? Xex.BeUInt32(head, BaseVersionOffset) : 0,
                    DiscNumber = head.Length > DiscNumberOffset ? head[DiscNumberOffset] : (byte)0,
                    DiscCount = head.Length > DiscCountOffset ? head[DiscCountOffset] : (byte)0,
                    VolumeType = head.Length >= VolumeTypeOffset + 4 ? Xex.BeUInt32(head, VolumeTypeOffset) : 0,
                    DisplayName = Utf16(head, DisplayNameOffset, 0x100),
                    TitleName = Utf16(head, TitleNameOffset, 0x80),
                };
            }
            catch { return null; }
        }

        /// <summary>How many bytes of a package <see cref="Parse"/> wants.</summary>
        public const int HeaderSize = HeaderProbe;

        public static uint? TitleIdOfFile(string path) => Read(path)?.TitleId;

        private static string Utf16(byte[] head, int at, int bytes)
        {
            if (head.Length < at + bytes) return "";
            var s = System.Text.Encoding.BigEndianUnicode.GetString(head, at, bytes);
            int end = s.IndexOf('\0');
            return (end >= 0 ? s.Substring(0, end) : s).Trim();
        }
    }
}
