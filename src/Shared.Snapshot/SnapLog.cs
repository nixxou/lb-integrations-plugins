// How this folder reaches its host's log without naming it.
//
// Same shape as LbipLog, DsiLog and RamDiskLog, for the same reason: these sources are compiled INTO
// whatever uses them, and each of those has its own static Log, in its own namespace, writing its own
// file under its own prefix. There is no name this folder could use.
//
// NOTHING WIRED IS A VALID STATE. The probe drives a walk and a delta without constructing a plugin,
// so every call is a null-conditional invoke inside a catch.

#nullable disable

using System;

namespace LbIntegrations.Snapshot
{
    internal static class SnapLog
    {
        private static Action<string> _info;
        private static Action<string, Exception> _warn;

        public static void Use(Action<string> info, Action<string, Exception> warn)
        {
            _info = info;
            _warn = warn;
        }

        public static void Info(string message)
        {
            try { _info?.Invoke("snapshot: " + message); } catch { }
        }

        public static void Warn(string message, Exception ex = null)
        {
            try { _warn?.Invoke("snapshot: " + message, ex); } catch { }
        }
    }
}
