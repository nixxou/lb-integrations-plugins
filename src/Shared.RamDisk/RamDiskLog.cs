// How this folder reaches its host's log without naming it.
//
// The same shape as LbipLog and DsiLog, for the same reason: these sources are compiled INTO
// whatever uses them, and each of those has its own static Log in its own namespace, writing its
// own file under its own prefix. There is no name this folder could use.
//
// NOTHING WIRED IS A VALID STATE, and here more than anywhere else. The probe drives a mount
// without constructing a plugin, and the INSTALLER compiles these sources into a Windows Forms
// executable that has no plugin log at all - it shows what it finds in a window instead. So every
// call is a null-conditional invoke inside a catch, and a library that is talking to nobody works
// exactly as well as one that is.

#nullable disable

using System;

namespace LbIntegrations.RamDisk
{
    internal static class RamDiskLog
    {
        private static Action<string> _info;
        private static Action<string, Exception> _warn;

        /// <summary>Point this folder at a host's logger. Called from a plugin's constructor, or not
        /// at all.</summary>
        public static void Use(Action<string> info, Action<string, Exception> warn)
        {
            _info = info;
            _warn = warn;
        }

        public static void Info(string message)
        {
            try { _info?.Invoke("ramdisk: " + message); } catch { }
        }

        public static void Warn(string message, Exception ex = null)
        {
            try { _warn?.Invoke("ramdisk: " + message, ex); } catch { }
        }
    }
}
