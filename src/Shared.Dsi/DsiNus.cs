// Fetching a DSiWare title's metadata from Nintendo's update server, the way melonDS does.
//
// A DSiWare .nds needs a .tmd to be installed into a NAND, and most dumps are the .nds alone. This
// plugin can build one from the ROM - everything melonDS READS out of a TMD is in the header - but a
// built one is not a SIGNED one, and the DSi menu that launches an installed title is real console
// software with real checks. Measured on a live install: a title imported with a fabricated TMD
// booted when direct-booted as a cartridge, and failed from the menu.
//
// So the first choice is the real thing. melonDS's own Manage DSi titles dialog offers exactly this,
// at exactly this address (TitleManagerDialog.cpp:485):
//
//     http://nus.cdn.t.shop.nintendowifi.net/ccs/download/<category><id>/tmd
//
// and the service still answers - measured, 200 with 2312 bytes, whose first 520 are byte for byte
// the TMD melonDS wrote when the user did it by hand. The DSi Shop closed years ago; the content
// distribution server did not.
//
// WHAT IS SENT: a title id, in a URL. Nothing about the machine, nothing about the user, no ROM
// content. Same request the emulator makes from its own dialog.
//
// THIS IS THE THIRD CHOICE, not the first. The plugin carries an archive of about 1700 titles - see
// DsiTmd - so the server is asked only for something that archive does not have. The answer is
// kept, so it is asked at most once per title per machine.

using System;
using System.IO;
using System.Net.Http;
using LbIntegrations.Dsi;

namespace LbIntegrations.Dsi
{
    internal static class DsiNus
    {
        /// <summary>melonDS's own URL, kept identical on purpose: if this ever has to be explained to
        /// somebody, "the same request the emulator makes" is the whole explanation.</summary>
        private const string UrlFormat = "http://nus.cdn.t.shop.nintendowifi.net/ccs/download/{0}/tmd";

        /// <summary>A TMD is 520 bytes; the server sends more - certificates follow - and melonDS
        /// reads the first 520. Anything shorter is not a TMD.</summary>
        private const int TmdBytes = 520;

        /// <summary>In the launch path, so bounded. A title that cannot be fetched in ten seconds
        /// falls back to a built TMD rather than holding up a game.</summary>
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        /// <summary>Fetch a title's metadata and keep it at <paramref name="into"/> - which is the
        /// bundled library, so the next machine needs no network. Returns the path, or null with the
        /// reason. Never throws.</summary>
        public static string TmdFor(string titleId, string into, out string why)
        {
            why = null;
            try
            {
                if (string.IsNullOrWhiteSpace(into)) { why = "no folder to keep it in"; return null; }
                var cached = into;

                var bytes = Download(string.Format(UrlFormat, titleId), out why);
                if (bytes == null) return null;

                if (bytes.Length < TmdBytes)
                {
                    why = "NUS answered with " + bytes.Length + " bytes, too short to be metadata";
                    return null;
                }
                if (!Describes(bytes, titleId))
                {
                    why = "NUS answered with metadata for another title";
                    return null;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(cached));
                Atomic.WriteBytes(cached, bytes);
                DsiLog.Info("fetched the metadata of title " + titleId
                         + " from Nintendo's update server, and kept it in the library");
                return cached;
            }
            catch (Exception ex) { why = ex.GetType().Name + ": " + ex.Message; return null; }
        }

        /// <summary>The title id a TMD claims, at offset 0x18C, big-endian - category then id. Checked
        /// because a CDN that answers 200 with a redirect page would otherwise be written to disk as
        /// metadata and refused much later, with a worse message.</summary>
        private static bool Describes(byte[] tmd, string titleId)
        {
            try
            {
                var actual = "";
                for (int i = 0x18C; i < 0x18C + 8; i++) actual += tmd[i].ToString("x2");
                return string.Equals(actual, titleId, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static byte[] Download(string url, out string why)
        {
            why = null;
            try
            {
                using var client = new HttpClient { Timeout = Timeout };
                using var response = client.GetAsync(url).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    why = "NUS answered " + (int)response.StatusCode + " " + response.ReasonPhrase;
                    return null;
                }
                return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                why = "could not reach Nintendo's update server - " + ex.GetType().Name;
                return null;
            }
        }
    }
}
