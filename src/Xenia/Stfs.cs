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
    }

    internal static class Stfs
    {
        private const int TitleIdOffset = 0x360;
        private const int ContentTypeOffset = 0x344;
        /// <summary>Everything we read lives inside the first header page.</summary>
        private const int HeaderProbe = 0x400;

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
                var head = Xex.ReadAt(fs, 0, HeaderProbe);
                if (!IsStfs(head)) return null;
                return new StfsInfo
                {
                    Magic = System.Text.Encoding.ASCII.GetString(head, 0, 4),
                    ContentType = Xex.BeUInt32(head, ContentTypeOffset),
                    TitleId = Xex.BeUInt32(head, TitleIdOffset),
                };
            }
            catch (Exception ex) { Log.Warn("could not read the STFS header of " + path, ex); return null; }
        }

        public static uint? TitleIdOfFile(string path) => Read(path)?.TitleId;
    }
}
