// What a launch is doing before its emulator starts, for the host to show (Mehdi, 14/10): LiteBox draws it on its
// loading screen as a yellow pre-bar - one fill per step, the step's text under it.
//
// THE CONTRACT is LiteBox's (LbApiHost Host\Gameplay\LaunchStatus.cs), by NAME in the AppDomain - this folder is
// compiled into each plugin, so no type can be shared - holding a delegate of BCL types only:
//     "LbIntegrations.LaunchStatus.v1" -> Action<string, double>
//     text: the step ("Extracting game.iso…"); null = the step already shown; a different text = a new step.
//     fraction: 0..1, NaN = unknown.
// LaunchBox sets nothing under that name: every call here is then nothing. Never throws, never blocks.

using System;

namespace LbIntegrations.Lbip
{
    internal static class LbipLaunchStatus
    {
        private const string DataName = "LbIntegrations.LaunchStatus.v1";

        /// <summary>The step being done before the emulator starts, and its progress (0..1; NaN unknown).</summary>
        public static void Report(string text, double fraction = double.NaN)
        {
            try { (AppDomain.CurrentDomain.GetData(DataName) as Action<string, double>)?.Invoke(text, fraction); }
            catch { }
        }

        /// <summary>The progress of the step already shown.</summary>
        public static void Progress(double fraction) => Report(null, fraction);
    }
}
