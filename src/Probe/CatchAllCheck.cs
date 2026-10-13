// --catchall: the Nixx-Saves plugin (lot 6, docs\saves-lot6.md) without LaunchBox - Probe <NixxSaves.dll> --catchall.
//
// LaunchBox's plugin registry is played by LbPluginRegistry.ForTests (the plugin's own copy), the library by a
// StubDataManager, a plugin with saves by FakeSnes (registered as one of the pack's in LbSaveOwners).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Probe
{
    internal static class CatchAllCheck
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private const string Snes = "Super Nintendo Entertainment System";
        private static int _bad;

        private static void Check(string what, bool ok, string detail = null)
        {
            if (!ok) _bad++;
            Console.WriteLine("  [" + (ok ? "ok" : "FAIL") + "] " + what + (detail != null ? " - " + detail : ""));
        }

        /// <summary>A plugin WITH saves: takes the emulators whose exe is MySnes.exe, lists one save per game, notes what it is asked.</summary>
        private sealed class FakeSnes : EmulatorPlugin
        {
            public readonly List<string> Calls = new List<string>();
            public string Save;
            public override string EmulatorName => "Fake SNES";
            public override IEnumerable<IEmulator> GetApplicableEmulators(IEnumerable<IEmulator> emulators)
                => (emulators ?? Enumerable.Empty<IEmulator>()).Where(e => string.Equals(Path.GetFileName(e?.ApplicationPath ?? ""), "MySnes.exe", StringComparison.OrdinalIgnoreCase)).ToList();
            public override string GetCurrentVersion(string applicationPath) => "";
            public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions() => Array.Empty<EmulatorControllerVersion>();
            public override EmulatorInstallResponse InstallEmulator(InstallEmulatorArgs args) => null;
            public override EmulatorSupportResponse IsPlatformSupported(string platform) => new EmulatorSupportResponse(true, false);
            public override bool SupportsSaveManagement() => true;
            public override GetSavesResponse GetSaves(GetSavesArgs args)
            {
                Calls.Add("GetSaves");
                var rows = new List<GameSaveBase>();
                foreach (var g in args?.Games ?? Array.Empty<IGame>())
                    if (Save != null && File.Exists(Save))
                        rows.Add(new GameSaveGame { GameId = g.Id, FileLocation = Save, OriginalFileName = Path.GetFileName(Save), SaveGroupId = "{\"emu\":\"fakesnes\",\"kind\":\"save\",\"by\":\"name\",\"id\":\"zelda\"}", SaveGroupName = "Fake SNES save" });
                return new GetSavesResponse(rows);
            }
            public override AddSaveResponse AddSaveFile(AddSaveArgs args) { Calls.Add("AddSaveFile"); return new AddSaveResponse("restored by Fake SNES"); }
            public override PluginResponse RemoveSave(GameSaveBase save) { Calls.Add("RemoveSave"); return new PluginResponse(true, null); }
        }

        /// <summary>A plugin that throws when asked about an emulator titled "Throw".</summary>
        private sealed class Thrower : EmulatorPlugin
        {
            public override string EmulatorName => "Thrower";
            public override IEnumerable<IEmulator> GetApplicableEmulators(IEnumerable<IEmulator> emulators)
            {
                if ((emulators ?? Enumerable.Empty<IEmulator>()).Any(e => e?.Title == "Throw")) throw new InvalidOperationException("boom");
                return Array.Empty<IEmulator>();
            }
            public override string GetCurrentVersion(string applicationPath) => "";
            public override IEnumerable<EmulatorControllerVersion> GetInstallableVersions() => Array.Empty<EmulatorControllerVersion>();
            public override EmulatorInstallResponse InstallEmulator(InstallEmulatorArgs args) => null;
            public override EmulatorSupportResponse IsPlatformSupported(string platform) => null;
        }

        private static StubEmulator Emu(string title, string exe, params string[] platforms)
        {
            var e = new StubEmulator { Title = title, ApplicationPath = exe };
            foreach (var p in platforms) e.AddNewEmulatorPlatform().Platform = p;
            return e;
        }

        public static bool Run(Assembly asm)
        {
            _bad = 0;
            Console.WriteLine("-- Nixx-Saves: the emulators nobody drives, their games' saves of other emulators --");
            var scratch = Path.Combine(Path.GetTempPath(), "lbip-catchall-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(scratch);
            var wasDm = PluginHelper.DataManager;
            Type T(string n) => asm.GetType(n, true);
            var claims = T("LbIntegrations.SavesCatchAll.CatchAllClaims");
            var registry = T("LbIntegrations.Compat.LbPluginRegistry");
            var compat = T("LbIntegrations.Compat.Compat");
            var router = T("LbIntegrations.Saves.SaveRouter");
            var forTests = registry.GetField("ForTests", Any);
            void Switches(bool catchAll = true, bool desk = true) => compat.GetMethod("UseForTests", Any).Invoke(null, new object[] { new Dictionary<string, bool> { ["catchAll"] = catchAll, ["desk"] = desk } });
            void Reset(bool ready) { claims.GetMethod("ResetForTests", Any).Invoke(null, new object[] { ready }); router.GetMethod("ForgetStock", Any).Invoke(null, null); }
            try
            {
                LbIntegrations.Catalog.LbCatalog.HostWillAsk = false;
                Switches();
                var plugin = (EmulatorPlugin)Activator.CreateInstance(T("LbIntegrations.SavesCatchAll.NixxSavesPlugin"));
                var fake = new FakeSnes();
                LbIntegrations.Catalog.LbSaveOwners.Register("fakesnes", fake);
                var thrower = new Thrower();
                List<EmulatorPlugin> others = new List<EmulatorPlugin> { fake, thrower, plugin };
                Func<IReadOnlyList<EmulatorPlugin>> reg = () => others;
                forTests.SetValue(null, reg);

                var hand = Emu("My emulator", @"C:\Emus\FakeEmulateur.exe", Snes);
                var snes = Emu("My SNES", @"C:\Emus\MySnes.exe", Snes);
                var retro = Emu("RetroArch, its plugin off", @"C:\Emus\retroarch.exe", Snes);
                var boom = Emu("Throw", @"C:\Emus\Other.exe", Snes);
                var noExe = Emu("No exe", "", Snes);
                var arcade = Emu("My arcade", @"C:\Emus\Arcade.exe", "Arcade");
                PluginHelper.DataManager = new StubDataManager(hand, snes, retro, boom, noExe, arcade);
                IEnumerable<IEmulator> Taken(params IEmulator[] e) => plugin.GetApplicableEmulators(e).ToList();

                Reset(ready: false);
                Check("before LaunchBox loads it: takes nothing", !Taken(hand).Any());
                // Loaded, LaunchBox's start not complete: only once the registry stopped growing (review 13/10).
                claims.GetMethod("InitializedOnlyForTests", Any).Invoke(null, null);
                Check("loaded, the registry just seen: takes nothing yet (a plugin loaded later must be asked)", !Taken(hand).Any());
                others.Add(new FakeSnes { });   // one more plugin appears: the clock starts again
                System.Threading.Thread.Sleep(1500);
                Check("... a plugin added since: still waiting", !Taken(hand).Any());
                System.Threading.Thread.Sleep(3500);
                Check("... the registry unchanged for 3 s: taken", Taken(hand).SequenceEqual(new IEmulator[] { hand }));
                // LaunchBox itself asking (its GetBestEmulatorPlugin on the stack): no waiting (measured 13/10).
                claims.GetMethod("InitializedOnlyForTests", Any).Invoke(null, null);
                claims.GetField("AskedByLaunchBoxForTests", Any).SetValue(null, true);
                Check("loaded, LaunchBox itself asking: taken at once", Taken(hand).SequenceEqual(new IEmulator[] { hand }));
                claims.GetField("AskedByLaunchBoxForTests", Any).SetValue(null, null);
                others.RemoveAt(others.Count - 1);
                Reset(ready: true);
                Check("an emulator nobody drives: taken", Taken(hand).SequenceEqual(new IEmulator[] { hand }));
                Check("an emulator another plugin drives: left", !Taken(snes).Any());
                Check("an exe LaunchBox or the pack has a plugin for (RetroArch, its plugin turned off): left", !Taken(retro).Any());
                Check("a plugin that throws when asked: left (it may take it)", !Taken(boom).Any());
                Check("an emulator without an exe: left", !Taken(noExe).Any());
                Check("several offered at once: only the free ones", Taken(hand, snes, arcade).SequenceEqual(new IEmulator[] { hand, arcade }));

                // LaunchBox's registry unreadable: nothing taken on a guess.
                Reset(ready: true); forTests.SetValue(null, (Func<IReadOnlyList<EmulatorPlugin>>)(() => null));
                Check("LaunchBox's registry unreadable: takes nothing", !Taken(hand).Any());
                forTests.SetValue(null, reg);
                // Under LiteBox, or its switch off, or the desk off.
                Reset(ready: true); LbIntegrations.Catalog.LbCatalog.HostWillAsk = true;
                Check("under LiteBox: takes nothing", !Taken(hand).Any());
                LbIntegrations.Catalog.LbCatalog.HostWillAsk = false;
                Reset(ready: true); Switches(catchAll: false);
                Check("its switch off: takes nothing, says it has no saves", !Taken(hand).Any() && !plugin.SupportsSaveManagement());
                Reset(ready: true); Switches(desk: false);
                Check("the desk off: takes nothing", !Taken(hand).Any());
                Reset(ready: true); Switches();

                // D6: saves only when a plugin with saves covers a platform of one of its emulators.
                Check("SNES covered by Fake SNES: it supports saves", plugin.SupportsSaveManagement());
                PluginHelper.DataManager = new StubDataManager(arcade, snes);
                Reset(ready: true);
                Check("only an arcade emulator taken, nobody with saves on Arcade: no saves", !plugin.SupportsSaveManagement());
                PluginHelper.DataManager = new StubDataManager(hand, snes, retro, boom, noExe, arcade);
                Reset(ready: true);

                // The listing: Fake SNES's save of the game, through the desk; a call on it handed to Fake SNES.
                fake.Save = Path.Combine(scratch, "Zelda.srm"); File.WriteAllText(fake.Save, "fake's");
                var game = StubGame.With(StubGame.Create(Guid.NewGuid().ToString(), "Zelda", @"C:\Roms\Zelda.sfc", hand.Id), "Platform", Snes);
                var listed = plugin.GetSaves(new GetSavesArgs { Emulator = hand, Games = new[] { game }, AdditionalApplications = Array.Empty<IAdditionalApplication>() });
                var rows = listed?.FoundSaves?.ToList() ?? new List<GameSaveBase>();
                Check("GetSaves a success, Fake SNES's save listed with its own id", listed?.WasSuccess == true && rows.Count == 1 && rows[0].SaveGroupId.StartsWith("{\"emu\":\"fakesnes\"", StringComparison.Ordinal),
                      rows.Count + " row(s)" + (listed?.Message == null ? "" : ", " + listed.Message));
                if (rows.Count == 1)
                {
                    var r = plugin.AddSaveFile(new AddSaveArgs { SaveToAdd = rows[0] });
                    Check("its restore handed to Fake SNES", fake.Calls.Contains("AddSaveFile") && r?.Message == "restored by Fake SNES", r?.Message);
                }
                fake.Save = null;
                var none = plugin.GetSaves(new GetSavesArgs { Emulator = hand, Games = new[] { game }, AdditionalApplications = Array.Empty<IAdditionalApplication>() });
                Check("no save anywhere: a success, empty", none?.WasSuccess == true && !(none.FoundSaves?.Any() ?? false));

                // A row nobody claims (a card LaunchBox kept from before): never deleted without the user, never restored.
                var orphan = Path.Combine(scratch, "Orphan.sav"); File.WriteAllText(orphan, "keep me");
                var row = new GameSaveGame { GameId = game.Id, FileLocation = orphan, OriginalFileName = "Orphan.sav", SaveGroupId = Guid.NewGuid().ToString() };
                var instead = T("LbIntegrations.Saves.SlotRestore").GetField("Instead", Any);
                int asked = 0;
                instead.SetValue(null, (Func<string, string, bool>)((title, text) => { asked++; return false; }));
                var removed = plugin.RemoveSave(row);
                Check("RemoveSave on a save nobody claims: the user asked, No keeps the file", asked == 1 && removed?.WasSuccess == false && File.Exists(orphan), removed?.Message);
                instead.SetValue(null, (Func<string, string, bool>)((title, text) => { asked++; return true; }));
                removed = plugin.RemoveSave(row);
                Check("... Yes: deleted as LaunchBox's SDK would", asked == 2 && removed?.WasSuccess == true && !File.Exists(orphan), removed?.Message);
                instead.SetValue(null, null);
                File.WriteAllText(orphan, "keep me");
                var restored = plugin.AddSaveFile(new AddSaveArgs { SaveToAdd = row });
                Check("AddSaveFile on a save nobody claims: refused", restored?.WasSuccess == false, restored?.Message);

                // Supported only on the platforms of the emulators it took (review 13/10).
                Reset(ready: true); Taken(hand);
                Check("IsPlatformSupported: the platform of an emulator taken, never recommended", plugin.IsPlatformSupported(Snes) is { Supported: true, Recommended: false });
                Check("... not any other platform", plugin.IsPlatformSupported("Nintendo 64")?.Supported == false);
                Check("the pack's other names left (x.emu, reicast, nogba)", !Taken(Emu("x", @"C:\E\x.emu.exe", Snes), Emu("r", @"C:\E\reicast.exe", Snes), Emu("n", @"C:\E\nogba.exe", Snes)).Any());
                // Nothing else in LaunchBox's screens, the launch untouched.
                var launch = plugin.PrepareEmulatorForLaunch(new PrepareForLaunchArgs(hand, game, "-f", null, null));
                Check("the launch untouched (success, no new line)", launch?.WasSuccess == true && launch.NewCommandLine == null);
                Check("nothing to install or update", !(plugin.GetInstallableVersions()?.Any() ?? false) && !plugin.IsUpdateAvailable(hand, out var v) && v == null
                      && !plugin.IsUpdateAvailable((string)null, out _) && plugin.InstallEmulator(null)?.WasSuccess != true);
                Check("no BIOS, no RetroAchievements", !plugin.GetBiosFilesForPlatform(Snes).Any() && plugin.SupportsRetroAchievements(hand.ApplicationPath)?.IsSupported != true);
                Check("its name", plugin.EmulatorName == "Nixx-Saves");
            }
            catch (Exception ex) { Check("ran", false, (ex.InnerException ?? ex).ToString()); }
            finally
            {
                PluginHelper.DataManager = wasDm;
                try { forTests.SetValue(null, null); } catch { }
                try { Directory.Delete(scratch, true); } catch { }
            }
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }
    }
}
