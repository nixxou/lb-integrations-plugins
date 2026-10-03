// "Apply to my emulators" of the Nixx window's "Your console" tab (src\Menus\IdentityPanel.cs), for Xenia Canary: its
// signed-in profile renamed after the identity's nickname (the same XUID - its saves and achievements stay its own), and the
// identity's language and clock in xconfig.settings (XeniaConsole.WithIdentity). Found by the relay by its name,
// LbIntegrations.Xenia.IdentityTarget, like the tab's Settings.

using System.Collections.Generic;
using System.Linq;
using LbIntegrations.Identity;

namespace LbIntegrations.Xenia
{
    internal static class IdentityTarget
    {
        private sealed class State
        {
            public string Exe;
            public XeniaLayout Layout;
            public XeniaProfileInfo Profile;
            public XeniaConsoleValues Now, Next;
            public string Tag;
        }

        private static State Look(PackIdentity id)
        {
            var exe = XeniaLibrary.Executables().FirstOrDefault(e => XeniaPaths.ForkOf(e) == XeniaFork.Canary);
            if (exe == null) return null;
            var s = new State { Exe = exe, Layout = XeniaPaths.Resolve(exe) };
            var profiles = XeniaProfile.Find(s.Layout.ContentRoot);
            var signed = XeniaProfile.SignedIn(s.Layout.ConfigFile);
            s.Profile = profiles.FirstOrDefault(p => p.Xuid == signed) ?? profiles.FirstOrDefault();
            s.Now = XeniaConsole.Read(s.Layout.StorageRoot);
            s.Next = XeniaConsole.WithIdentity(s.Now, id);
            s.Tag = id.Nickname.Length > 0 ? XeniaProfile.GamertagFrom(id.Nickname) : s.Profile?.Gamertag ?? XeniaProfile.GamertagFrom();
            return s;
        }

        private static string Say(string tag, XeniaConsoleValues v)
            => "profile \"" + (tag ?? "none") + "\", " + XeniaConsole.LanguageName(v.Language) + ", " + (v.Hour24 ? "24-hour" : "12-hour");

        public static string[][] Plan()
        {
            var id = PackIdentity.Load();
            var s = id == null ? null : Look(id);
            if (s == null) return new string[0][];
            return new[]
            {
                PackIdentity.Row("xenia", "Xenia Canary - profile and console", Say(s.Profile?.Gamertag, s.Now), Say(s.Tag, s.Next),
                                 XeniaConsolePanel.IsRunning(s.Exe) ? "Xenia is running - close it first" : ""),
            };
        }

        public static string Apply(string key)
        {
            try
            {
                var id = PackIdentity.Load();
                var s = id == null ? null : Look(id);
                if (s == null) return "no Xenia Canary";
                if (XeniaConsolePanel.IsRunning(s.Exe)) return "Xenia is running - close it first";
                if (s.Profile == null)
                {
                    var xuid = XeniaProfile.Create(s.Layout.ContentRoot, s.Tag);
                    XeniaProfile.SignInAtStart(s.Layout.ConfigFile, xuid);
                }
                else if (s.Tag != s.Profile.Gamertag) XeniaProfile.Rename(s.Layout.ContentRoot, s.Profile.Xuid, s.Tag);
                XeniaConsole.Write(s.Layout.StorageRoot, s.Next);
                Log.Info("console identity applied: " + Say(s.Tag, s.Next));
                return null;
            }
            catch (System.Exception ex) { Log.Warn("console identity: could not apply", ex); return ex.Message; }
        }
    }
}
