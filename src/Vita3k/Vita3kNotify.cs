// A notification in the host's own notification centre - LaunchBox's, or LiteBox's.
//
// THERE IS NO SDK CALL FOR IT, so the application assembly is reached by name, the way LaunchBox plugins
// do it: the assembly called "LaunchBox", its Unbroken.LaunchBox.Windows.Desktop.Notifications.
// NotificationCenter, SendInfoNotification(string, int). Measured on LaunchBox 14 (28/09): the type and its
// Send* methods keep their names under the obfuscation, and a method called by reflection runs its real
// body. UNDER LITEBOX THE SAME CALL WORKS: LiteBox registers, before any plugin loads, an assembly of that
// very name carrying that API and forwarding it to its own centre (LbApiHost\Host\Notifications\
// LaunchBoxShim.cs). So there is one path and no host test here.
//
// SendInputNotification(string, IEnumerable<KeyValuePair<string, Action>>, int?) is the one with buttons -
// a question - on both hosts; nothing asks one yet.
//
// FAIL-SOFT: no such assembly (Big Box, a host without the shim), a changed signature - a log line, and
// the message is in vita3k.log anyway. LaunchBox's centre is WPF: the call goes through its dispatcher.

using System;
using System.Linq;
using System.Reflection;

namespace LbIntegrations.Vita3k
{
    internal static class Vita3kNotify
    {
        private const string CenterType = "Unbroken.LaunchBox.Windows.Desktop.Notifications.NotificationCenter";

        /// <summary>An information notification. <paramref name="seconds"/> is how long it stays at least.</summary>
        public static void Info(string message, int seconds = 0)
        {
            Log.Info("notification: " + message);
            try
            {
                var center = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "LaunchBox", StringComparison.OrdinalIgnoreCase))
                    ?.GetType(CenterType);
                var send = center?.GetMethod("SendInfoNotification", BindingFlags.Public | BindingFlags.Static, null,
                                             new[] { typeof(string), typeof(int) }, null);
                if (send == null) { Log.Info("notification: no notification centre in this host - the log only"); return; }

                Action call = () =>
                {
                    try { send.Invoke(null, new object[] { message, seconds }); }
                    catch (Exception ex) { Log.Warn("notification: the host refused it", ex); }
                };
                var app = System.Windows.Application.Current;
                if (app != null && !app.Dispatcher.CheckAccess()) app.Dispatcher.BeginInvoke(call);
                else call();
            }
            catch (Exception ex) { Log.Warn("notification: could not send it", ex); }
        }
    }
}
