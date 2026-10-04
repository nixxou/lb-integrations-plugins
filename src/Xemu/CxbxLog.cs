// The Log the Cxbx plugin's shared readers write to (Xbe.cs, Xdvdfs.cs, GitHubReleases.cs, Archives.cs - linked into this
// project, see Xemu.csproj): their lines land in this plugin's log, xemu.log. tools\ramdisk-helper answers the same name
// with a silent one (XboxLog.cs).

namespace LbIntegrations.Cxbx
{
    internal static class Log
    {
        public static void Info(string message) => LbIntegrations.Xemu.Log.Info(message);
        public static void Verbose(string message) => LbIntegrations.Xemu.Log.Verbose(message);
        public static void Warn(string message, System.Exception ex = null) => LbIntegrations.Xemu.Log.Warn(message, ex);
    }
}
