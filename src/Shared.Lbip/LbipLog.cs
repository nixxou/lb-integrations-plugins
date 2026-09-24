// How the shared row injection talks, without knowing which plugin it is inside.
//
// THIS FOLDER IS COMPILED INTO FIVE PLUGINS, and each one has its own Log: a different prefix
// ("[flycast]" / "[melonds]" / "[nogba]" / "[ppsspp]" / "[xenia]"), a different file beside it, its
// own kill-switch markers. The injection cannot name any of them - a `using` picks one namespace,
// and there is only one copy of each source file. So the plugin hands its logger over at start-up,
// once, and the injection calls through here.
//
// Same shape as DsiLog, for the same reason and with one member more: Tracing, because the SQL
// trace asks the question on every query and a delegate call per row would be silly.
//
// NOTHING IS WIRED IS A VALID STATE. The probe reaches into the injection directly, without going
// through a plugin's constructor, so every call here is null-safe and silence is the answer when
// nobody is listening.

using System;

namespace LbIntegrations.Lbip
{
    internal static class LbipLog
    {
        private static Action<string> _info;
        private static Action<string, Exception> _warn;
        private static Func<string, bool> _disabled;
        private static Func<bool> _tracing;

        /// <summary>Called once by each plugin, from its constructor. Passing the host's own Log
        /// methods keeps every line of the injection in the file the rest of that plugin writes
        /// to.</summary>
        public static void Use(Action<string> info, Action<string, Exception> warn,
                               Func<string, bool> disabled, Func<bool> tracing)
        {
            _info = info;
            _warn = warn;
            _disabled = disabled;
            _tracing = tracing;
        }

        public static void Info(string message)
        {
            try { _info?.Invoke(message); } catch { }
        }

        public static void Warn(string message, Exception ex = null)
        {
            try { _warn?.Invoke(message, ex); } catch { }
        }

        /// <summary>Is a kill switch set? UNSET MEANS ENABLED, which is the answer that keeps the
        /// injection working when nobody wired anything up - a switch nobody can read is not a
        /// switch that is on.</summary>
        public static bool Disabled(string marker)
        {
            try { return _disabled != null && _disabled(marker); } catch { return false; }
        }

        /// <summary>Is the SQL trace on? OFF is the answer nobody has to pay for.</summary>
        public static bool Tracing
        {
            get { try { return _tracing != null && _tracing(); } catch { return false; } }
        }
    }
}
