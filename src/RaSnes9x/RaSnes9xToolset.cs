// RetroAchievements' toolset (RA_Integration.dll) put beside RASnes9x by the plugin, EXACTLY as the emulator would
// (Mehdi, 06/10: "strictement identique, user agent compris") - so its first start asks nothing.
//
// WHAT THE EMULATOR DOES, read in RAInterface's RA_Interface.cpp at the commit RASnes9x 1.2 pins (7cb0c95e,
// RA_AttemptLoadNewIntegration / DoBlockingHttpCall / FetchIntegrationFromWeb):
//
//   1. WinHTTP, session agent "RetroAchievements Client Bootstrap", default proxy;
//      POST http://retroachievements.org/dorequest.php, body "r=latestintegration",
//      header "Content-Type: application/x-www-form-urlencoded"; up to 4 tries on a network error.
//      The answer (06/10): {"Success":true,"MinimumVersion":"1.3","LatestVersion":"1.4.2",
//      "LatestVersionUrl":".../bin/RA_Integration.dll","LatestVersionUrlX64":".../bin/RA_Integration-x64.dll"}
//   2. When the toolset installed is older than LatestVersion (and the user says yes): a GET of LatestVersionUrlX64 the
//      same way (same agent, same header), written to RA_Integration.download, then RA_Integration.old deleted, the
//      present RA_Integration.dll renamed .old, the download renamed RA_Integration.dll, the .old deleted.
//
// THE FILE IS RA_Integration.dll even for the x64 build (GetIntegrationPath): beside the executable, which is where
// it is loaded from. The emulator writes its download in its CURRENT folder; LaunchBox starts it in its own folder,
// so this writes it there too.
//
// THE VERSION INSTALLED: the emulator asks the DLL itself (_RA_IntegrationVersion) - loading it here would load it into
// LaunchBox, so its file version is read instead, compared the same way (ParseVersion: up to four numbers).
//
// NEVER FAILS A LAUNCH: no network, a refusal, a running emulator - nothing is done, the emulator asks as it would. And
// never HOLDS one: short timeouts (5 s to resolve and connect, 10 s to send and to receive - WinHTTP's defaults are
// minutes), and once a day at most while the toolset is there (the emulator checks again at its own start anyway). A
// download is installed only whole: its length is the one announced and it starts "MZ".

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace LbIntegrations.RaSnes9x
{
    internal static class RaSnes9xToolset
    {
        public const string FileName = "RA_Integration.dll";
        private const string Agent = "RetroAchievements Client Bootstrap";
        private const string Host = "http://retroachievements.org";

        /// <summary>The installed toolset's version ("1.4.2.0"), or null when there is none.</summary>
        public static string Installed(string exe)
        {
            try
            {
                var dll = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), FileName);
                if (!File.Exists(dll)) return null;
                var v = FileVersionInfo.GetVersionInfo(dll);
                return !string.IsNullOrWhiteSpace(v.ProductVersion) ? v.ProductVersion.Trim() : v.FileVersion?.Trim();
            }
            catch { return null; }
        }

        /// <summary>The toolset brought up to RetroAchievements' latest when it is older or missing. Null when nothing had to
        /// be done or it was done; else why not.</summary>
        /// <param name="always">At install: whatever the last check. At a launch: once a day while the toolset is there.</param>
        public static string Ensure(string exe, bool always = true)
        {
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(exe));
                var stamp = Path.Combine(Path.GetDirectoryName(RaSnes9xSettings.SettingsPath), "toolset-checked.txt");
                if (!always && Installed(exe) != null && File.Exists(stamp) && DateTime.UtcNow - File.GetLastWriteTimeUtc(stamp) < TimeSpan.FromDays(1))
                    return null;
                if (RaSnes9xLaunch.Running(exe)) return "RASnes9x is running - its toolset is loaded";

                if (!Call(Host, "dorequest.php", "r=latestintegration", out var status, out var body))
                    return "retroachievements.org could not be reached (status " + status + ")";
                try { Directory.CreateDirectory(Path.GetDirectoryName(stamp)); File.WriteAllText(stamp, DateTime.UtcNow.ToString("o")); } catch { }
                var json = Encoding.UTF8.GetString(body);
                var latest = Field(json, "LatestVersion");
                var url = Field(json, "LatestVersionUrlX64");
                if (ParseVersion(latest) == 0 || string.IsNullOrEmpty(url))
                    return "retroachievements.org did not say which toolset is the latest (" + (Field(json, "Error") ?? json) + ")";

                var installed = Installed(exe);
                if (ParseVersion(installed) >= ParseVersion(latest))
                {
                    Log.Verbose("toolset " + installed + " is the latest (" + latest + ")");
                    return null;
                }

                // GET of the URL as the emulator splits it: host up to the first '/' after the protocol, the rest the page.
                int slash = url.IndexOf('/', Math.Min(8, url.Length));
                if (slash < 0) return "the toolset's address is not one (" + url + ")";
                var download = Path.Combine(dir, "RA_Integration.download");
                if (!Call(url.Substring(0, slash), url.Substring(slash + 1), null, out status, out var dll) || status != 200)
                    return "the toolset could not be downloaded (status " + status + ")";
                if (dll.Length < 2 || dll[0] != (byte)'M' || dll[1] != (byte)'Z')
                    return "what was downloaded is not a DLL (" + dll.Length + " bytes) - the toolset is left as it was";
                File.WriteAllBytes(download, dll);

                var target = Path.Combine(dir, FileName);
                var old = Path.Combine(dir, "RA_Integration.old");
                try { File.Delete(old); } catch { }
                if (File.Exists(target)) File.Move(target, old);
                File.Move(download, target);
                try { File.Delete(old); } catch { }
                Log.Info("toolset " + (installed ?? "(none)") + " -> " + (Installed(exe) ?? "?") + " (latest " + latest + "), " + dll.Length + " bytes from " + url);
                return null;
            }
            catch (Exception ex) { Log.Warn("the toolset could not be brought up to date", ex); return ex.Message; }
        }

        /// <summary>RAInterface's ParseVersion: up to four dot-separated numbers, 16 bits each.</summary>
        internal static ulong ParseVersion(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return 0;
            ulong result = 0;
            var parts = v.Trim().Split('.');
            for (int i = 0; i < 4; i++)
            {
                ulong n = 0;
                if (i < parts.Length) ulong.TryParse(Regex.Match(parts[i], @"^\d+").Value, out n);
                result = (result << 16) | Math.Min(n, 0xFFFF);
            }
            return result;
        }

        private static string Field(string json, string name)
        {
            var m = Regex.Match(json ?? "", "\"" + Regex.Escape(name) + "\"\\s*:\\s*\"(?<v>(?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? m.Groups["v"].Value.Replace("\\/", "/") : null;
        }

        // ── WinHTTP, as DoBlockingHttpCall ───────────────────────────────────

        /// <summary>DoBlockingHttpCallWithRetry: up to 4 tries while the failure is a network error.</summary>
        private static bool Call(string hostUrl, string page, string post, out uint status, out byte[] body)
        {
            int tries = 4;
            do
            {
                if (Once(hostUrl, page, post, out status, out body)) return true;
                if (!IsNetworkError(status)) return false;
            } while (--tries > 0);
            return false;
        }

        private static bool IsNetworkError(uint code)
            => code == 12002 || code == 12007 || code == 12017 || code == 12019 || code == 12028 || code == 12029
            || code == 12030 || code == 12031 || code == 12032 || code == 12152 || code == 12163;

        private static bool Once(string hostUrl, string page, string post, out uint status, out byte[] body)
        {
            status = 0; body = Array.Empty<byte>();
            ushort port = 80;   // INTERNET_DEFAULT_HTTP_PORT
            var host = hostUrl;
            if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) host = host.Substring(7);
            else if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) { host = host.Substring(8); port = 443; }
            int colon = host.IndexOf(':');
            if (colon >= 0) { ushort.TryParse(host.Substring(colon + 1), out port); host = host.Substring(0, colon); }

            IntPtr session = IntPtr.Zero, connect = IntPtr.Zero, request = IntPtr.Zero;
            try
            {
                session = WinHttpOpen(Agent, 0 /* WINHTTP_ACCESS_TYPE_DEFAULT_PROXY */, null, null, 0);
                if (session == IntPtr.Zero) { status = (uint)Marshal.GetLastWin32Error(); return false; }
                WinHttpSetTimeouts(session, 5000, 5000, 10000, post != null ? 10000 : 30000);
                connect = WinHttpConnect(session, host, port, 0);
                if (connect == IntPtr.Zero) { status = (uint)Marshal.GetLastWin32Error(); return false; }
                request = WinHttpOpenRequest(connect, post != null ? "POST" : "GET", page, null, null, IntPtr.Zero,
                                             port == 443 ? 0x00800000u /* WINHTTP_FLAG_SECURE */ : 0);
                if (request == IntPtr.Zero) { status = (uint)Marshal.GetLastWin32Error(); return false; }

                const string headers = "Content-Type: application/x-www-form-urlencoded";
                bool sent;
                if (post != null)
                {
                    var data = Encoding.ASCII.GetBytes(post);
                    var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
                    try { sent = WinHttpSendRequest(request, headers, 0, pin.AddrOfPinnedObject(), (uint)data.Length, (uint)data.Length, IntPtr.Zero); }
                    finally { pin.Free(); }
                }
                else sent = WinHttpSendRequest(request, headers, 0, IntPtr.Zero, 0, 0, IntPtr.Zero);
                if (!sent || !WinHttpReceiveResponse(request, IntPtr.Zero)) { status = (uint)Marshal.GetLastWin32Error(); return false; }

                uint code = 0, size = 4, index = 0;
                WinHttpQueryHeaders(request, 19 | 0x20000000 /* WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER */, null, ref code, ref size, ref index);
                status = code;
                uint announced = 0; size = 4; index = 0;
                bool hasLength = WinHttpQueryHeaders(request, 5 | 0x20000000 /* WINHTTP_QUERY_CONTENT_LENGTH | WINHTTP_QUERY_FLAG_NUMBER */, null, ref announced, ref size, ref index);

                using var ms = new MemoryStream();
                var buffer = new byte[16384];
                while (true)
                {
                    if (!WinHttpQueryDataAvailable(request, out uint available)) { status = (uint)Marshal.GetLastWin32Error(); return false; }
                    if (available == 0) break;
                    while (available > 0)
                    {
                        uint want = Math.Min(available, (uint)buffer.Length);
                        if (!WinHttpReadData(request, buffer, want, out uint got)) { if (status == 200) status = (uint)Marshal.GetLastWin32Error(); return false; }
                        ms.Write(buffer, 0, (int)got);
                        available -= got;
                        if (got == 0) break;
                    }
                }
                body = ms.ToArray();
                if (hasLength && body.Length != announced) { status = 998; return false; }   // cut short: RAInterface's own "998"
                return true;
            }
            finally
            {
                if (request != IntPtr.Zero) WinHttpCloseHandle(request);
                if (connect != IntPtr.Zero) WinHttpCloseHandle(connect);
                if (session != IntPtr.Zero) WinHttpCloseHandle(session);
            }
        }

        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr WinHttpOpen(string agent, uint accessType, string proxy, string proxyBypass, uint flags);

        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr WinHttpConnect(IntPtr session, string server, ushort port, uint reserved);

        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr WinHttpOpenRequest(IntPtr connect, string verb, string objectName, string version, string referrer, IntPtr acceptTypes, uint flags);

        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WinHttpSendRequest(IntPtr request, string headers, uint headersLength, IntPtr optional, uint optionalLength, uint totalLength, IntPtr context);

        [DllImport("winhttp.dll", SetLastError = true)]
        private static extern bool WinHttpReceiveResponse(IntPtr request, IntPtr reserved);

        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WinHttpQueryHeaders(IntPtr request, uint infoLevel, string name, ref uint buffer, ref uint bufferLength, ref uint index);

        [DllImport("winhttp.dll", SetLastError = true)]
        private static extern bool WinHttpQueryDataAvailable(IntPtr request, out uint available);

        [DllImport("winhttp.dll", SetLastError = true)]
        private static extern bool WinHttpReadData(IntPtr request, byte[] buffer, uint toRead, out uint read);

        [DllImport("winhttp.dll", SetLastError = true)]
        private static extern bool WinHttpCloseHandle(IntPtr handle);

        [DllImport("winhttp.dll", SetLastError = true)]
        private static extern bool WinHttpSetTimeouts(IntPtr handle, int resolve, int connect, int send, int receive);
    }
}
