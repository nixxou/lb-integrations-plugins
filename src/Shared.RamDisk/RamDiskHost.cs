// The one thing this folder cannot work out for itself: where LaunchBox is.
//
// Everything else here derives from that - the helper's folder, its cfg, the scheduled task's name.
// And every caller finds it differently, which is exactly why it is a delegate rather than a
// computation:
//
//   a plugin      walks up from its own assembly looking for Core\ and Data\
//   the installer already holds it, resolved from the LaunchBox.exe the user pointed at
//   the probe     is handed it on the command line
//
// Same shape as DsiHost: a field the caller sets, and an accessor that tolerates it being unset.

#nullable disable

using System;

namespace LbIntegrations.RamDisk
{
    internal static class RamDiskHost
    {
        /// <summary>Returns the LaunchBox root - the folder holding Core\ and Data\. Set once, by
        /// whoever is using this folder.</summary>
        public static Func<string> LaunchBoxRoot;

        /// <summary>The root, or null. Never throws: a caller that never set the delegate gets the
        /// same "not available" every probe here already handles.</summary>
        public static string Root()
        {
            try
            {
                var root = LaunchBoxRoot?.Invoke();
                return string.IsNullOrWhiteSpace(root) ? null : root;
            }
            catch (Exception ex) { RamDiskLog.Warn("could not work out the LaunchBox root", ex); return null; }
        }

        /// <summary>Convenience for a caller that already has the path: sets the delegate to return
        /// it.</summary>
        public static void UseRoot(string root) => LaunchBoxRoot = () => root;
    }
}
