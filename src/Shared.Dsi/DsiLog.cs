// How the shared DSi engine talks, without knowing which plugin it is inside.
//
// THIS FOLDER IS COMPILED INTO TWO PLUGINS, and each one has its own Log: a different prefix
// ("[melonds]" / "[nogba]"), a different file, its own kill-switch markers beside it. The engine
// cannot name either of them - a `using` picks one namespace, and there is only one copy of each
// source file.
//
// So the plugin hands its logger over at start-up, once, and the engine calls through here. Four
// delegates and a marker test is the whole contract; nothing is invented and nothing is lost,
// because these forward to exactly the methods the melonDS side was calling before this folder
// existed.
//
// NOTHING IS WIRED IS A VALID STATE. The probe reaches into the engine directly, without going
// through a plugin's constructor, and a null delegate must not take a test down - so every call
// here is null-safe and silence is the answer when nobody is listening.

using System;

namespace LbIntegrations.Dsi
{
    internal static class DsiLog
    {
        private static Action<string> _info, _verbose;
        private static Action<string, Exception> _warn;
        private static Func<string, bool> _disabled;

        /// <summary>Called once by each plugin, from its constructor. Passing the host's own Log
        /// methods keeps every line of the engine in the file the rest of that plugin writes to.</summary>
        public static void Use(Action<string> info, Action<string, Exception> warn,
                               Action<string> verbose, Func<string, bool> disabled)
        {
            _info = info;
            _warn = warn;
            _verbose = verbose;
            _disabled = disabled;
        }

        public static void Info(string message)
        {
            try { _info?.Invoke(message); } catch { }
        }

        public static void Verbose(string message)
        {
            try { _verbose?.Invoke(message); } catch { }
        }

        public static void Warn(string message, Exception ex = null)
        {
            try { _warn?.Invoke(message, ex); } catch { }
        }

        /// <summary>Is a kill switch set? UNSET MEANS ENABLED, which is the answer that keeps the
        /// engine working when nobody wired anything up - a switch nobody can read is not a switch
        /// that is on.</summary>
        public static bool Disabled(string marker)
        {
            try { return _disabled != null && _disabled(marker); } catch { return false; }
        }
    }
}
