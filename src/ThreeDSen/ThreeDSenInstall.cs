// "Install" for 3dSen: nothing is downloaded. 3dSen PC is a commercial game (Geod Studio, sold on Steam), so the plugin
// ASKS (Mehdi's install flow): use the Steam install, have Steam install it, or point at the folder of a copy the user has.
// The emulator entry is then made on that folder as it is - nothing copied, no stamp (the only thing the plugin ever puts
// there is Unpacked\, for a game's archive 3dSen cannot play itself - ThreeDSenLaunch.RomFor).
//
// THE QUESTION is asked from the Download button of LaunchBox's Add Emulator window (IsUpdateAvailable answers true for
// the empty path, ThreeDSenPlugin), on a thread of its own (STA, as LbipNotice), above every window, and
// waited for: InstallEmulator returns what was chosen. What it shows comes from ThreeDSenSteam.Read: Steam there or not,
// someone logged in or not, 3dSen installed in a library or not.
//
// THE VERSION is not the exe's: 3dSen.exe carries Unity's (2019.4.41.7021524, docs\3dsen.md section 1). It is the C#
// constant Nes3D.Info.Version of 3dSen_Data\Managed\Assembly-CSharp.dll ("1.1.2" in the build of 05/11/2025) - a literal
// in the metadata's Constant table, read with System.Reflection.Metadata, the assembly never loaded nor run.
//
// A FOLDER is accepted when it holds 3dSen.exe and that version can be read - or when its one sub-folder does (a zip
// extracted as is gives "3dsenpc-win\"). The VR edition (3dSenVR.exe, Steam app 954280) is another product, not driven
// by this plugin yet: said so.

using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LbIntegrations.ThreeDSen
{
    internal static class ThreeDSenInstall
    {
        internal enum Pick { Cancel, UseSteam, InstallWithSteam, Folder }

        /// <summary>For the probe: the answer instead of the window (what Steam says -> the choice and, for Folder, the folder).</summary>
        internal static Func<ThreeDSenSteam.Status, (Pick, string)> Instead;

        // ── the version ──────────────────────────────────────────────────────

        /// <summary>Assembly-CSharp.dll of the install of <paramref name="exe"/>.</summary>
        public static string AssemblyOf(string exe)
            => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), "3dSen_Data", "Managed", "Assembly-CSharp.dll");

        /// <summary>3dSen's own version ("1.1.2"), or null when the install is not a 3dSen PC we can read.</summary>
        public static string VersionOf(string exe)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return null;
                var dll = AssemblyOf(exe);
                return File.Exists(dll) ? VersionConstant(dll) : null;
            }
            catch (Exception ex) { Log.Warn("the version of " + exe + " could not be read", ex); return null; }
        }

        /// <summary>The string constant Nes3D.Info.Version of the assembly, from its metadata.</summary>
        internal static string VersionConstant(string dll)
        {
            using var stream = File.OpenRead(dll);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return null;
            var md = pe.GetMetadataReader();
            foreach (var th in md.TypeDefinitions)
            {
                var type = md.GetTypeDefinition(th);
                if (md.GetString(type.Name) != "Info" || md.GetString(type.Namespace) != "Nes3D") continue;
                foreach (var fh in type.GetFields())
                {
                    var field = md.GetFieldDefinition(fh);
                    if (md.GetString(field.Name) != "Version") continue;
                    var ch = field.GetDefaultValue();
                    if (ch.IsNil) return null;
                    var constant = md.GetConstant(ch);
                    if (constant.TypeCode != ConstantTypeCode.String) return null;
                    var blob = md.GetBlobBytes(constant.Value);
                    return Encoding.Unicode.GetString(blob);
                }
            }
            return null;
        }

        // ── a folder the user points at ──────────────────────────────────────

        /// <summary>3dSen.exe in <paramref name="folder"/> or its one sub-folder, with its version readable; null with
        /// <paramref name="problem"/> said otherwise.</summary>
        public static string ExeIn(string folder, out string problem)
        {
            problem = null;
            try
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) { problem = "the folder does not exist."; return null; }
                string Exe(string dir) => Path.Combine(dir, ThreeDSenLaunch.ExecutableName);
                var exe = Exe(folder);
                if (!File.Exists(exe))
                {
                    var subs = Directory.GetDirectories(folder).Where(d => File.Exists(Exe(d))).ToList();
                    if (subs.Count == 1) exe = Exe(subs[0]);
                }
                if (!File.Exists(exe))
                {
                    problem = File.Exists(Path.Combine(folder, "3dSenVR.exe"))
                        ? "this is 3dSen VR (3dSenVR.exe), which this plugin does not drive yet - choose the folder of 3dSen PC."
                        : "there is no " + ThreeDSenLaunch.ExecutableName + " in " + folder + ".";
                    return null;
                }
                if (VersionOf(exe) == null)
                {
                    problem = exe + " is there, but its version could not be read (3dSen_Data\\Managed\\Assembly-CSharp.dll) - is it 3dSen PC?";
                    return null;
                }
                return exe;
            }
            catch (Exception ex) { problem = ex.Message; return null; }
        }

        // ── the question ─────────────────────────────────────────────────────

        /// <summary>What the user chose, and the folder for <see cref="Pick.Folder"/>. Cancel when no window could be shown.</summary>
        public static (Pick Pick, string Folder) Ask(ThreeDSenSteam.Status steam)
        {
            if (Instead != null) return Instead(steam);
            var answer = (Pick.Cancel, (string)null);
            try
            {
                // No owner: the host's window belongs to another thread, possibly the one waiting here - owning it would tie
                // the two threads' input together. Above everything instead, as LbipNotice.
                var thread = new Thread(() =>
                {
                    try { answer = Window(steam); }
                    catch (Exception ex) { Log.Warn("the install question could not be shown", ex); }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Name = "3dSen install question";
                thread.Start();
                thread.Join();
            }
            catch (Exception ex) { Log.Warn("the install question could not be shown", ex); }
            return answer;
        }

        /// <summary>The lines saying what Steam has - also the Nixx page's.</summary>
        internal static string[] Describe(ThreeDSenSteam.Status s)
        {
            if (s.SteamRoot == null) return new[] { "Steam is not installed on this computer." };
            var lines = new System.Collections.Generic.List<string>
            {
                "Steam: " + s.SteamRoot + (s.LoggedIn ? " - running, a user logged in." : " - not running, or nobody logged in."),
            };
            if (s.Exe != null) lines.Add("3dSen is installed by Steam: " + s.Exe + " (version " + (VersionOf(s.Exe) ?? "unknown") + ").");
            else if (s.InstallDir != null) lines.Add("3dSen is in Steam's library at " + s.InstallDir + " but not fully installed yet (Steam's state " + s.StateFlags + ").");
            else lines.Add("3dSen is not installed in any Steam library of this computer.");
            return lines.ToArray();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (Pick, string) Window(ThreeDSenSteam.Status steam)
        {
            var result = (Pick.Cancel, (string)null);
            using var form = new Form
            {
                Text = "Nixx-3DSen - where is 3dSen?",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = true, TopMost = true,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(14),
                Font = SystemFonts.MessageBoxFont,
            };
            var layout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill };
            Label Line(string text, bool bold = false) => new Label
            {
                Text = text, AutoSize = true, MaximumSize = new Size(600, 0), UseMnemonic = false, Margin = new Padding(0, 2, 0, 6),
                Font = bold ? new Font(form.Font, FontStyle.Bold) : form.Font,
            };
            // Mehdi 19/10: 3dSen 1.1.2 ships 3dSen_Data\Plugins\x86\fceumm_core.dll (libretro's FCEUmm, GPL v2 - its strings say
            // so) with no licence, no source and no offer of it anywhere in its folder (checked in the lab's copy).
            var gpl = Line("Read before buying: 3dSen includes fceumm_core.dll (the FCEUmm emulator core, licensed under the GNU GPL v2) "
                           + "but does not distribute its source code, nor offer it - a clear breach of the GPL. "
                           + "This is not a call to piracy, just a notice that the author is a piece of shit.", true);
            gpl.ForeColor = Color.Firebrick;
            gpl.Margin = new Padding(0, 2, 0, 12);
            layout.Controls.Add(gpl);
            layout.Controls.Add(Line("3dSen PC is a game sold on Steam: this plugin does not download it. Which 3dSen should LaunchBox use?", true));
            foreach (var l in Describe(steam)) layout.Controls.Add(Line(l));
            var error = Line("");
            error.ForeColor = Color.Firebrick;

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 10, 0, 0) };
            Button Add(string text, bool enabled, Action click)
            {
                var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(110, 30), Enabled = enabled, Margin = new Padding(0, 0, 8, 0) };
                b.Click += (_, _) => click();
                buttons.Controls.Add(b);
                return b;
            }
            var useSteam = Add("Use the Steam install", steam.Exe != null, () => { result = (Pick.UseSteam, null); form.Close(); });
            if (steam.SteamRoot != null && steam.Exe == null)
                Add("Install it with Steam", true, () => { result = (Pick.InstallWithSteam, null); form.Close(); });
            Add("Choose the folder...", true, () =>
            {
                using var dialog = new FolderBrowserDialog { Description = "The folder that holds 3dSen.exe", UseDescriptionForTitle = true, ShowNewFolderButton = false };
                if (dialog.ShowDialog(form) != DialogResult.OK) return;
                if (ExeIn(dialog.SelectedPath, out var problem) == null) { error.Text = "Not this folder: " + problem; return; }
                result = (Pick.Folder, dialog.SelectedPath);
                form.Close();
            });
            var cancel = Add("Cancel", true, () => { result = (Pick.Cancel, null); form.Close(); });
            form.CancelButton = cancel;
            if (useSteam.Enabled) form.AcceptButton = useSteam;

            layout.Controls.Add(error);
            layout.Controls.Add(buttons);
            form.Controls.Add(layout);
            form.Shown += (_, _) => form.Activate();
            form.ShowDialog();
            return result;
        }
    }
}
