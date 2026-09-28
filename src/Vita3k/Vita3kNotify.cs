// A notification in the host's own notification centre - LaunchBox's, or LiteBox's.
//
// THERE IS NO SDK CALL FOR IT, so the application assembly is reached by name, the way LaunchBox plugins
// do it: the assembly called "LaunchBox", its Unbroken.LaunchBox.Windows.Desktop.Notifications.
// NotificationCenter. Measured on LaunchBox 14 (28/09): the type and its Send* methods keep their names
// under the obfuscation, and a method called by reflection runs its real body. UNDER LITEBOX THE SAME
// CALL WORKS: LiteBox registers, before any plugin loads, an assembly of that very name carrying that
// API and forwarding it to its own centre (LbApiHost\Host\Notifications\LaunchBoxShim.cs). So there is
// one path and no host test here.
//
//   SendInfoNotification(string, int)                                               - Info
//   SendInputNotification(string, IEnumerable<KeyValuePair<string, Action>>, int?)  - Ask, with buttons
// Both signatures read from LaunchBox 14's own metadata (28/09), and the same in the shim.
//
// FAIL-SOFT: no such assembly (Big Box, a host without the shim), a changed signature - a log line, and
// the message is in vita3k.log anyway. LaunchBox's centre is WPF: the call goes through its dispatcher.

using System;
using System.Collections.Generic;
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
            var send = Center()?.GetMethod("SendInfoNotification", BindingFlags.Public | BindingFlags.Static, null,
                                           new[] { typeof(string), typeof(int) }, null);
            if (send == null) { Log.Info("notification: no notification centre in this host - the log only"); return; }
            OnHostThread(() => send.Invoke(null, new object[] { message, seconds }));
        }

        /// <summary>A notification with buttons - a question. Each button runs its action on the host's
        /// thread when clicked. False when this host has no such notification (the question is only in the
        /// log then, and nothing is run).</summary>
        public static bool Ask(string message, int seconds, params (string Label, Action OnClick)[] buttons)
        {
            Log.Info("notification: " + message + " [" + string.Join(" / ", buttons.Select(b => b.Label)) + "]");
            var send = Center()?.GetMethod("SendInputNotification", BindingFlags.Public | BindingFlags.Static, null,
                                           new[] { typeof(string), typeof(IEnumerable<KeyValuePair<string, Action>>), typeof(int?) }, null);
            if (send == null) { Log.Info("notification: no question notification in this host - the log only"); return false; }
            var actions = buttons.Select(b => new KeyValuePair<string, Action>(b.Label, () =>
            {
                Log.Info("notification: \"" + b.Label + "\" clicked");
                try { b.OnClick?.Invoke(); }
                catch (Exception ex) { Log.Warn("notification: the \"" + b.Label + "\" action failed", ex); }
            })).ToList();
            OnHostThread(() => send.Invoke(null, new object[] { message, actions, (int?)seconds }));
            return true;
        }

        private static Type Center()
        {
            try
            {
                return AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "LaunchBox", StringComparison.OrdinalIgnoreCase))
                    ?.GetType(CenterType);
            }
            catch (Exception ex) { Log.Warn("notification: could not look for the notification centre", ex); return null; }
        }

        private static void OnHostThread(Action call)
        {
            Action safe = () =>
            {
                try { call(); }
                catch (Exception ex) { Log.Warn("notification: the host refused it", ex); }
            };
            try
            {
                var app = System.Windows.Application.Current;
                if (app != null && !app.Dispatcher.CheckAccess()) app.Dispatcher.BeginInvoke(safe);
                else safe();
            }
            catch (Exception ex) { Log.Warn("notification: could not send it", ex); }
        }
    }
}
