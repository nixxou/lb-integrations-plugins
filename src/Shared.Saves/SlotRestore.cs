// A restore's two decisions, shared by every plugin (lot 3, docs\saves-lot3.md §6 and §8, measured 08-09/10):
//
// THE SLOT A STATE GOES TO. LaunchBox hands the slot picked in its list (SaveToAdd.Slot), and that list is the GAME
// emulator's plugin's (GetPotentialSaveSlots takes no argument): a state of another emulator gets the front desk's
// numbers. So: the pick when the owner has that slot; the backup's own slot when nothing usable was picked (none, or
// -1 "Auto" - none of ours has an Auto slot); a slot the owner does not have is REFUSED, never guessed. The row sent
// back carries the slot written and its id recomputed for it - measured coherent (a new tile for that slot, the
// backup's own tile untouched). Written under the backup's slot with the picked one returned, the file, the tile and
// the id disagreed (ares, SUPER ZSNES, RASnes9x, Flycast, melonDS before this).
//
// THE WARNING. LaunchBox's precautionary copy, before a restore, is of the RESTORED card's own live file - not of the
// file the emulator is about to overwrite. The two differ when a state goes into another slot (§9.2, reproduced with
// stock RetroArch) and when a card without a live file ("In Vault" - a manual import) is restored over another card's
// live save (§8, test A): that file is replaced with no copy kept. Its question (ShouldOverwriteFunc) answers yes
// without asking. So, only in that case - the target exists, it is another card's, and its content differs - the user
// is asked Yes / No. Mehdi, 08-09/10:
// - LaunchBox only. Under LiteBox (LbCatalog.HostWillAsk) LiteBox restores right itself (it backs up whoever holds the
//   destination first, SaveAction_SetActive), and a RomM restore has nobody in front of it. In BigBox no dialog: the
//   case is refused with a message.
// - Lifted once LaunchBox is fixed: FixedIn, LaunchBox's first fixed version, checked with the probe first (a fix
//   cannot be detected). Null = not fixed.
// - Asked on the caller's thread: AddSaveFile runs on LaunchBox's UI thread (seen in the stack under
//   RestoreSaveCommand.Execute).

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace LbIntegrations.Saves
{
    internal static class SlotRestore
    {
        /// <summary>LaunchBox's first version whose precautionary copy is of the overwritten file; null = not fixed.</summary>
        internal static Version? FixedIn => LbIntegrations.Compat.FixedIn.OverwriteBackup;

        /// <summary>For the probe: answers instead of a window (title, text → yes).</summary>
        internal static Func<string, string, bool>? Instead;

        /// <summary>For the probe: the host, instead of the running process's.</summary>
        internal static string? HostOverride;

        /// <summary>The slot a state restore writes: the pick when <paramref name="valid"/> has it, the backup's own slot
        /// when nothing usable was picked (null, -1), refused otherwise. <paramref name="who"/> names the emulator in the
        /// message ("ares numbers its states 1 to 9.").</summary>
        public static (int Slot, string? Refused) Choose(int? picked, int? own, IReadOnlyCollection<int> valid, string who)
        {
            valid ??= Array.Empty<int>();
            if (picked is int p && p >= 0)
                return valid.Contains(p) ? (p, null) : (0, who + " has no slot " + p + ": " + Range(valid, who) + " Pick one of those.");
            if (own is int o && valid.Contains(o)) return (o, null);
            return (0, "No slot was picked and the backup's own is not known - pick one of " + who + "'s: " + Range(valid, who));
        }

        private static string Range(IReadOnlyCollection<int> valid, string who)
        {
            var s = valid.OrderBy(v => v).ToList();
            if (s.Count == 0) return who + " has no numbered slots.";
            bool run = s.Count > 1 && s[s.Count - 1] - s[0] == s.Count - 1;
            return who + " numbers its states " + (run ? s[0] + " to " + s[s.Count - 1] : string.Join(", ", s)) + ".";
        }

        /// <summary><paramref name="id"/> (our JSON) with its slot set to <paramref name="slot"/>; the id itself when it is
        /// not our JSON (nothing to recompute).</summary>
        public static string? WithSlot(string? id, int slot)
        {
            if (!SaveId.TryRead(id, out var d) || d == null) return id;
            return SaveId.Write(new SaveIdData { Emu = d.Emu, Kind = d.Kind, Sys = d.Sys, By = d.By, Id = d.Id, Slot = slot, Native = d.Native, Dir = d.Dir });
        }

        /// <summary>The slot inside <paramref name="id"/> (our JSON), if any.</summary>
        public static int? SlotOf(string? id) => SaveId.TryRead(id, out var d) ? d?.Slot : null;

        /// <summary>True to go on writing <paramref name="source"/> over <paramref name="target"/>. Asks only when the
        /// target exists, holds another card's save (<paramref name="otherCard"/>) and differs from the source, under
        /// LaunchBox (not fixed). <paramref name="what"/> names the target for the user ("Slot 3", "The current save").
        /// <paramref name="refused"/> is the message when the answer is no or the host cannot ask.</summary>
        public static bool MayOverwrite(string target, string source, bool otherCard, string what, Action<string>? log, out string? refused)
        {
            refused = null;
            try
            {
                if (!LbIntegrations.Compat.Compat.On("overwriteWarning")) return true;
                if (!otherCard || !File.Exists(target) || SameContent(target, source)) return true;
                var host = Host();
                if (host == "litebox") return true;                   // LiteBox backs up the destination itself
                if (FixedIn != null && LaunchBoxVersion() is Version v && v >= FixedIn) return true;
                const string Why = "Because of a LaunchBox bug, it will not be backed up before being replaced: it would be lost.";
                if (host == "bigbox")
                {
                    refused = what + " already holds another save. " + Why + " Restore it from LaunchBox, or back it up first.";
                    log?.Invoke("restore refused in BigBox: " + target + " would be overwritten with no copy");
                    return false;
                }
                var text = what + " already holds another save.\n" + Why + "\n\nReplace it anyway?";
                bool yes = Ask("Restore", text);
                log?.Invoke("restore over another card's save (" + target + "): " + (yes ? "replaced, the user said yes" : "cancelled by the user"));
                if (!yes) refused = "Restore cancelled: " + what.ToLowerInvariant() + " was left as it was.";
                return yes;
            }
            catch (Exception ex) { log?.Invoke("overwrite check failed, going on: " + ex.Message); return true; }
        }

        /// <summary>Lot 5: true to let LaunchBox's SDK body delete <paramref name="file"/> - the file of a row on an
        /// integration without saves that nobody claims (Mehdi 12/10: a Yes / No, No by default). Only LaunchBox asks;
        /// BigBox, LiteBox and a failed question keep the file. <paramref name="refused"/> is the message otherwise.</summary>
        public static bool MayDeleteUnclaimed(string? file, Action<string>? log, out string? refused)
        {
            refused = "This save belongs to no emulator known here - nothing was deleted.";
            try
            {
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) { refused = null; return true; }   // nothing to lose
                if (Host() != "launchbox") { log?.Invoke("deletion of an unclaimed save refused (" + Host() + "): " + file); return false; }
                var text = "Nobody claims this save:\n" + file + "\n\nNo emulator known here owns it, so its file would be deleted for good.\n\nDelete the file anyway?";
                bool yes = Ask("Delete save", text);
                log?.Invoke("deletion of an unclaimed save (" + file + "): " + (yes ? "deleted, the user said yes" : "cancelled by the user"));
                if (yes) refused = null; else refused = "Deletion cancelled: the save file was left as it was.";
                return yes;
            }
            catch (Exception ex) { log?.Invoke("deletion of an unclaimed save not asked, file kept: " + ex.Message); return false; }
        }

        private static bool SameContent(string a, string b)
        {
            var fa = new FileInfo(a); var fb = new FileInfo(b);
            if (!fb.Exists || fa.Length != fb.Length) return false;
            using var sa = fa.OpenRead(); using var sb = fb.OpenRead();
            var x = new byte[81920]; var y = new byte[81920];
            int n;
            while ((n = sa.Read(x, 0, x.Length)) > 0)
            {
                int m = 0;
                while (m < n) { int k = sb.Read(y, m, n - m); if (k <= 0) return false; m += k; }
                for (int i = 0; i < n; i++) if (x[i] != y[i]) return false;
            }
            return true;
        }

        private static string Host()
        {
            if (HostOverride != null) return HostOverride;
            try { if (LbIntegrations.Catalog.LbCatalog.HostWillAsk) return "litebox"; } catch { }
            try { if (string.Equals(Process.GetCurrentProcess().ProcessName, "BigBox", StringComparison.OrdinalIgnoreCase)) return "bigbox"; } catch { }
            return "launchbox";
        }

        private static Version? LaunchBoxVersion()
        {
            try
            {
                var dll = Path.Combine(AppContext.BaseDirectory, "LaunchBox.dll");
                var fv = FileVersionInfo.GetVersionInfo(dll);
                return new Version(fv.FileMajorPart, fv.FileMinorPart, fv.FileBuildPart, fv.FilePrivatePart);
            }
            catch { return null; }
        }

        private static bool Ask(string title, string text) => Instead != null ? Instead(title, text) : Box(title, text);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool Box(string title, string text)
        {
            // LaunchBox's window is WPF: the owner is the window in front (Game Saves), so the box is modal to it.
            System.Windows.Forms.IWin32Window? owner = System.Windows.Forms.Form.ActiveForm;
            if (owner == null) { var h = GetForegroundWindow(); if (h != IntPtr.Zero) owner = new Hwnd(h); }
            var r = System.Windows.Forms.MessageBox.Show(owner, text, title, System.Windows.Forms.MessageBoxButtons.YesNo,
                                                         System.Windows.Forms.MessageBoxIcon.Warning, System.Windows.Forms.MessageBoxDefaultButton.Button2);
            return r == System.Windows.Forms.DialogResult.Yes;
        }

        private sealed class Hwnd : System.Windows.Forms.IWin32Window { public Hwnd(IntPtr h) { Handle = h; } public IntPtr Handle { get; } }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }
}
