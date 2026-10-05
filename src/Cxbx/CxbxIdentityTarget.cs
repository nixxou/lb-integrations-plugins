// "Apply to my emulators" of the Nixx window's "Your console" tab, for Cxbx-Reloaded: the console itself made yours - a real
// Xbox's keys.bin and EEPROM.bin with the seed's serial number, MAC and keys (CxbxConsole.SetUp, what an install does). Its
// language needs nothing: its Language option's default reads the identity at every launch (CxbxEeprom.WindowsLanguage).
// Found by the relay by its name, LbIntegrations.Cxbx.IdentityTarget.

using System;
using System.IO;
using System.Linq;
using LbIntegrations.Identity;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Cxbx
{
    internal static class IdentityTarget
    {
        /// <summary>The Cxbx-Reloaded of this plugin LaunchBox has, else the one of Emulators\Nixx-Cxbx.</summary>
        private static string Exe()
        {
            try
            {
                foreach (var e in PluginHelper.DataManager?.GetAllEmulators() ?? new IEmulator[0])
                {
                    var p = CxbxPlugin.ResolveFullPathForUi(e?.ApplicationPath);
                    if (p != null && CxbxPaths.IsCxbx(p) && File.Exists(p)) return p;
                }
            }
            catch { }
            var root = PackIdentity.LaunchBoxRoot();
            return root == null ? null : CxbxPaths.FindLoader(Path.Combine(root, "Emulators", "Nixx-Cxbx"));
        }

        public static string[][] Plan()
        {
            var id = PackIdentity.Load();
            var exe = id == null ? null : Exe();
            if (exe == null) return new string[0][];
            var data = CxbxPaths.DataDir(exe);
            var eeprom = data == null ? null : Path.Combine(data, "EEPROM.bin");
            string now = "no console yet";
            try
            {
                if (eeprom != null && File.Exists(eeprom))
                {
                    var b = File.ReadAllBytes(eeprom);
                    if (b.Length == 256) now = "serial " + System.Text.Encoding.ASCII.GetString(b, 0x34, 12);
                }
            }
            catch { }
            now += data != null && !CxbxConsole.KeysAreOurs(data) ? ", your keys.bin" : data != null && File.Exists(Path.Combine(data, "keys.bin")) ? ", a real Xbox's keys" : ", no keys.bin";
            var x = id.Xbox();
            var next = x == null ? "the pack's HDD key" : "serial " + x.Serial + ", a real Xbox's keys";
            var problem = !id.HasSeed ? "no seed yet - write one first"
                        : CxbxPaths.LoaderRunning() ? "Cxbx-Reloaded is running - close it first"
                        : !CxbxOptions.Effective(null).ContainsKey("console.hddkey") ? "its Console identity option is Cxbx-Reloaded's own" : "";
            return new[] { PackIdentity.Row("cxbx", "Cxbx-Reloaded - the Xbox console (serial number, MAC, keys)", now, next, problem) };
        }

        public static string Apply(string key)
        {
            try
            {
                var exe = Exe();
                if (exe == null) return "no Cxbx-Reloaded";
                if (PackIdentity.Load()?.HasSeed != true) return "no seed yet - write one first";
                var said = CxbxConsole.SetUp(exe);
                var refused = said.FirstOrDefault(s => s.Contains("close it first") || s.Contains("not known"));
                return refused;
            }
            catch (Exception ex) { Log.Warn("console identity: could not apply", ex); return ex.Message; }
        }
    }
}
