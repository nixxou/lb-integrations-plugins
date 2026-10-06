// A launch as LaunchBox makes it, for the RASnes9x, bsnes and ares plugins - offline, on a COPY of an install:
//   --e2e-launch <plugin dll> <emulator exe>
// The plugin is constructed, handed a stub emulator (GetApplicableEmulators: its hotkey scripts, ares's platform lines),
// then PrepareEmulatorForLaunch with a stub game and the line LaunchBox would build (the platform row's line, then the
// quoted game). The launch gate is off and a refusal's message is caught, not shown. Nothing is started.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Probe
{
    internal static class SnesFamilyE2E
    {
        private static int _bad;

        private static void Check(string what, bool ok, string detail = null)
        {
            if (!ok) _bad++;
            Console.WriteLine("  [" + (ok ? "ok" : "FAIL") + "] " + what + (detail != null ? " - " + detail : ""));
        }

        public static bool Run(Assembly asm, string exe)
        {
            _bad = 0;
            var exeName = Path.GetFileName(exe);
            Console.WriteLine("-- a launch through " + asm.GetName().Name + " (" + exeName + ") --");
            var copy = Path.Combine(Path.GetTempPath(), "lbip-e2e-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(copy);
            var said = new List<string>();
            var gate = asm.GetType("LbIntegrations.Lbip.LbipLaunchGate", true);
            var notice = asm.GetType("LbIntegrations.Lbip.LbipNotice", true);
            gate.GetField("Off", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, true);
            notice.GetField("Instead", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, new Action<string, string>((t, x) => said.Add(x)));
            try
            {
                // The install's files that matter, copied: its settings, nothing else (the exe is a stand-in).
                foreach (var f in new[] { "settings.bml", "snes9x.conf" })
                    if (File.Exists(Path.Combine(Path.GetDirectoryName(exe), f))) File.Copy(Path.Combine(Path.GetDirectoryName(exe), f), Path.Combine(copy, f));
                var fake = Path.Combine(copy, exeName);
                File.WriteAllText(fake, "");
                var roms = Path.Combine(copy, "My Games");
                Directory.CreateDirectory(roms);

                var pluginType = asm.GetTypes().First(t => typeof(EmulatorPlugin).IsAssignableFrom(t) && !t.IsAbstract);
                var plugin = (EmulatorPlugin)Activator.CreateInstance(pluginType);
                var emu = new StubEmulator { Title = "under test", ApplicationPath = fake };
                bool isAres = exeName.Equals("ares.exe", StringComparison.OrdinalIgnoreCase);
                if (isAres)
                {
                    // Two rows as LaunchBox would have them after an Add Emulator from the catalogue: empty lines.
                    var cd = emu.AddNewEmulatorPlatform(); cd.Platform = "Sega CD";
                    var gen = emu.AddNewEmulatorPlatform(); gen.Platform = "Sega Genesis"; gen.CommandLine = "--system \"Mega Drive\" --setting Video/Shader=None";
                }
                var claimed = plugin.GetApplicableEmulators(new IEmulator[] { emu, new StubEmulator { Title = "other", ApplicationPath = @"C:\x\other.exe" } }).ToList();
                Check("claimed: ours only", claimed.Count == 1 && claimed[0] == emu);
                Check("hotkey scripts filled", !string.IsNullOrEmpty(emu.ExitAutoHotkeyScript) && !string.IsNullOrEmpty(emu.SaveStateAutoHotkeyScript));
                if (isAres)
                {
                    var rows = emu.GetAllEmulatorPlatforms();
                    Check("an empty platform line gets its --system", rows[0].CommandLine == "--system \"Mega CD\"", rows[0].CommandLine);
                    Check("a written platform line is kept", rows[1].CommandLine == "--system \"Mega Drive\" --setting Video/Shader=None", rows[1].CommandLine);
                }

                PrepareForLaunchResponse Launch(string platform, string rom, string rowLine)
                {
                    var game = StubGame.With(StubGame.Create("e2e-" + Path.GetFileNameWithoutExtension(rom), Path.GetFileNameWithoutExtension(rom), rom), "Platform", platform);
                    return plugin.PrepareEmulatorForLaunch(new PrepareForLaunchArgs(emu, game, ((rowLine ?? "") + " \"" + rom + "\"").Trim(), null, null));
                }

                if (isAres)
                {
                    var md = Path.Combine(roms, "Sonic The Hedgehog.md"); File.WriteAllBytes(md, new byte[1024]);
                    var r = Launch("Sega Genesis", md, "--system \"Mega Drive\" --setting Video/Shader=None");
                    Check("a Genesis game launches", r.WasSuccess, r.NewCommandLine);
                    Check("--system once, the user's switch kept, the game last", r.NewCommandLine != null && r.NewCommandLine.Split(new[] { "--system" }, StringSplitOptions.None).Length == 2
                          && r.NewCommandLine.Contains("--setting Video/Shader=None") && r.NewCommandLine.EndsWith("\"" + md + "\""), r.NewCommandLine);
                    var x32 = Path.Combine(roms, "Chaotix.32x"); File.WriteAllBytes(x32, new byte[1024]);
                    r = Launch("Sega Genesis", x32, null);
                    Check("a .32x with no line: Mega 32X", r.NewCommandLine?.Contains("--system \"Mega 32X\"") == true, r.NewCommandLine);
                    var cue = Path.Combine(roms, "Sonic CD.cue"); File.WriteAllText(cue, "FILE \"x.bin\" BINARY");
                    said.Clear();
                    r = Launch("Sega CD", cue, "--system \"Mega CD\"");
                    Check("a Sega CD game without its BIOS is refused, said", (!r.WasSuccess && said.Count == 1 && said[0].Contains("bios_CD_U.bin")) || r.WasSuccess,
                          r.WasSuccess ? "(a BIOS was found: launched) " + r.NewCommandLine : said.FirstOrDefault()?.Split('\n')[0]);
                    var unknown = Launch("Commodore 64", Path.Combine(roms, "x.d64"), null);
                    Check("a platform it does not map is refused", !unknown.WasSuccess);
                }
                else
                {
                    var sfc = Path.Combine(roms, "Super Metroid (USA).sfc"); File.WriteAllBytes(sfc, new byte[1024]);
                    var r = Launch("Super Nintendo Entertainment System", sfc, null);
                    Check("a game launches", r.WasSuccess, r.NewCommandLine ?? "(line unchanged)");
                    if (exeName.Equals("bsnes.exe", StringComparison.OrdinalIgnoreCase))
                        Check("bsnes: --fullscreen in front, the game kept", r.NewCommandLine == "--fullscreen \"" + sfc + "\"", r.NewCommandLine);
                    Check("the emulator's settings were written", File.Exists(Path.Combine(copy, exeName.StartsWith("bsnes") ? "settings.bml" : "snes9x.conf")));
                }
            }
            catch (Exception ex) { Check("ran", false, (ex.InnerException ?? ex).ToString()); }
            finally
            {
                notice.GetField("Instead", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
                try { Directory.Delete(copy, true); } catch { }
            }
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }
    }
}
