// "Apply to my emulators" of the Nixx window's "Your console" tab, for xemu: the console itself made yours - eeprom.bin with
// the seed's serial number, MAC and keys (Eeprom\XemuEeprom.ApplyIdentity, what an install does). Its language needs nothing:
// the Language option's default reads the identity at every launch. Found by the relay by its name, LbIntegrations.Xemu.IdentityTarget.

using System;
using System.IO;
using LbIntegrations.Identity;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Xemu
{
    internal static class IdentityTarget
    {
        /// <summary>The xemu of this plugin LaunchBox has, else the one of Emulators\Nixx-Xemu.</summary>
        private static string Exe()
        {
            try
            {
                foreach (var e in PluginHelper.DataManager?.GetAllEmulators() ?? new IEmulator[0])
                {
                    var p = XemuPlugin.ResolveFullPath(e?.ApplicationPath);
                    if (XemuPaths.IsOurs(p)) return p;
                }
            }
            catch { }
            var root = PackIdentity.LaunchBoxRoot();
            var guess = root == null ? null : XemuPaths.FindExe(Path.Combine(root, "Emulators", "Nixx-Xemu"));
            return XemuPaths.IsOurs(guess) ? guess : null;
        }

        public static string[][] Plan()
        {
            var id = PackIdentity.Load();
            var exe = id == null ? null : Exe();
            if (exe == null) return new string[0][];
            string now = "no console yet";
            try
            {
                var f = XemuPaths.Eeprom(exe);
                if (File.Exists(f)) now = Eeprom.XemuEeprom.Open(File.ReadAllBytes(f)) is Eeprom.XemuEeprom.Opened e ? "serial " + e.Serial : "an EEPROM no Xbox key opens";
            }
            catch { }
            var x = id.Xbox();
            var problem = !id.HasSeed ? "no seed yet - write one first"
                        : XemuPaths.Running(exe) ? "xemu is running - close it first"
                        : !XemuOptions.Effective(null).ContainsKey("console.hddkey") ? "its Console identity option is xemu's own" : "";
            return new[] { PackIdentity.Row("xemu", "xemu - the Xbox console (serial number, MAC, keys)", now, x == null ? "the pack's HDD key" : "serial " + x.Serial, problem) };
        }

        public static string Apply(string key)
        {
            try
            {
                var exe = Exe();
                if (exe == null) return "no xemu";
                if (PackIdentity.Load()?.HasSeed != true) return "no seed yet - write one first";
                if (XemuPaths.Running(exe)) return "xemu is running - close it first";
                var done = Eeprom.XemuEeprom.ApplyIdentity(XemuPaths.Eeprom(exe));
                Log.Info("console identity applied: " + (done ?? "already your console"));
                return null;
            }
            catch (Exception ex) { Log.Warn("console identity: could not apply", ex); return ex.Message; }
        }
    }
}
