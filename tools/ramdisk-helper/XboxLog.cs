// What Xdvdfs.cs (linked from src\Cxbx) logs through: nothing here - the helper has no log of its own, its
// verdict is ramdisk.result.

namespace LbIntegrations.Cxbx
{
    internal static class Log
    {
        public static void Info(string message) { }
        public static void Verbose(string message) { }
        public static void Warn(string message, System.Exception ex = null) { }
    }
}