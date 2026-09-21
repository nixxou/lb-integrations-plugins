// Console logging, prefixed the way the hosts do it ([loader], [emuplugin], ...) so a plugin line
// is recognisable in a mixed log. LaunchBox's own plugins call Root.Logging, which lives in the
// obfuscated core; this repo deliberately references nothing but the public SDK, so the console is
// all we have — and it is enough, because LiteBox runs with a console and LaunchBox captures stdout.

using System;

namespace LbIntegrations.Xenia
{
    internal static class Log
    {
        private const string Prefix = "[xenia] ";

        public static void Info(string message)
        {
            try { Console.WriteLine(Prefix + message); } catch { }
        }

        /// <summary>An exception we swallowed. Prints the type and message, never the stack: these
        /// lines land in a user's log, and every call site here is already a non-fatal path.</summary>
        public static void Warn(string message, Exception ex = null)
        {
            try
            {
                Console.WriteLine(ex == null
                    ? Prefix + message
                    : Prefix + message + " — " + ex.GetType().Name + ": " + ex.Message);
            }
            catch { }
        }
    }
}
