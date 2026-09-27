// The game list of LaunchBox's Import ROM Files wizard, put right for Vita games - after the scan, while
// the list is on show, so the user sees the result and can still change it before "Finish".
//
// Every file of the list is read (its param.sfo, from the archive, without unpacking it), with a
// progress window:
//   - a GAME (CATEGORY gd) stays, titled by its param.sfo's TITLE - "LittleBigPlanet PlayStation Vita"
//     rather than the file's name with its tags - cleaned of ™ ® © and their kind;
//   - an UPDATE (gp) or a DLC (ac) goes: it is not a game, a launch installs it with its game. It is
//     recorded in the emulator's index (Vita3kExtrasIndex), with the game archives of the same title
//     id found in the same list - the next launch looks there too;
//   - anything else - unreadable, not Vita content - goes, and the log says why.
//
// ONLY IN THE VITA CASE (Vita3kLbImport swapped the platform: the wizard was on its way to its own
// Vita import), and when the setting is on (Vita3kSettings.CleanImportList, the Vita3K tab).
//
// The list is Caliburn's BindableCollection, the one the grid shows: a record removed from it is gone
// from the grid and from the import. A record's Title has a setter (the grid edits it); a renamed
// record is taken out and put back at its place, so the grid redraws it - the record itself says
// nothing when it changes.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LbIntegrations.Vita3k
{
    internal static class Vita3kImportCleanup
    {
        /// <summary>A title fit for the library: the trademark signs and their kind gone, line breaks
        /// (some TITLEs have two lines) made spaces, spaces collapsed.
        ///
        /// A SIGN BECOMES A SPACE, not nothing: Sony writes "LittleBigPlanet™ PlayStation®Vita", and
        /// taken out it would glue "PlayStationVita". A space left before punctuation goes ("Game™:").</summary>
        internal static string CleanTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return title;
            var text = new StringBuilder(title.Length);
            foreach (var c in title)
            {
                bool sign = c == '™' || c == '®' || c == '©' || c == '℠' || c == '℗'
                            || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.OtherSymbol;   // their kind
                text.Append(sign || char.IsControl(c) ? ' ' : c);
            }
            var words = string.Join(" ", text.ToString().Split(new[] { ' ', '\u00A0' }, StringSplitOptions.RemoveEmptyEntries));
            return System.Text.RegularExpressions.Regex.Replace(words, @" +([:;,.!?)\]])", "$1");
        }

        /// <summary>What one file of the list turned out to be.</summary>
        private sealed class Finding
        {
            public object Record;
            public string Path;
            public VitaContent Content;
            public string Error;
        }

        /// <summary>Read every file of <paramref name="gameList"/>'s list, then put the list right. On
        /// the UI thread; the reading runs on another one, under a progress window.</summary>
        public static void Run(IWin32Window owner, object gameList)
        {
            var list = GetProperty(gameList, "Games") as IList;
            if (list == null || list.Count == 0) return;
            var findings = list.Cast<object>().Select(r => new Finding { Record = r, Path = GetProperty(r, "ApplicationPath") as string }).ToList();

            if (!Read(owner, findings)) { Log.Info("[import] the list was left as LaunchBox made it - reading cancelled"); return; }

            int renamed = 0, extras = 0, invalid = 0;
            var games = findings.Where(f => f.Content != null && f.Content.IsGame).ToList();
            var found = new List<IndexedExtra>();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var f = findings.FirstOrDefault(x => ReferenceEquals(x.Record, list[i]));
                if (f == null) continue;
                var name = System.IO.Path.GetFileName(f.Path ?? "");
                if (f.Content == null)
                {
                    list.RemoveAt(i); invalid++;
                    Log.Info("[import]   removed " + name + " - " + (f.Error ?? "not readable"));
                    continue;
                }
                if (f.Content.IsPatch || f.Content.IsAddon)
                {
                    list.RemoveAt(i); extras++;
                    var with = games.Where(g => string.Equals(g.Content.TitleId, f.Content.TitleId, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (with.Count == 0) found.Add(Vita3kExtrasIndex.From("", f.Path, f.Content));
                    foreach (var g in with) found.Add(Vita3kExtrasIndex.From(g.Path, f.Path, f.Content));
                    Log.Info("[import]   removed " + name + " - " + (f.Content.IsPatch ? "update " + f.Content.AppVer : "DLC " + (f.Content.Title ?? f.Content.ContentId))
                             + " of " + f.Content.TitleId + ", recorded for its game" + (with.Count == 0 ? " (none in this list)" : ""));
                    continue;
                }
                if (!f.Content.IsGame)
                {
                    list.RemoveAt(i); invalid++;
                    Log.Info("[import]   removed " + name + " - category " + (f.Content.Category ?? "?") + " is not a game");
                    continue;
                }
                var title = CleanTitle(f.Content.FullTitle ?? f.Content.Title);
                if (!string.IsNullOrWhiteSpace(title) && !string.Equals(title, GetProperty(f.Record, "Title") as string, StringComparison.Ordinal)
                    && SetProperty(f.Record, "Title", title))
                {
                    // Out and back at its place: the grid redraws a line only when the list says it changed.
                    list.RemoveAt(i);
                    list.Insert(i, f.Record);
                    renamed++;
                }
            }
            Notify(gameList, "GameCount");

            if (games.Count > 0 || found.Count > 0)
                foreach (var install in Installs())
                    Vita3kExtrasIndex.Record(install, games.Select(g => g.Path), found);

            Log.Info("[import] the list put right: " + games.Count + " game(s), " + renamed + " renamed from their param.sfo, "
                     + extras + " update(s)/DLC recorded for their game and removed, " + invalid + " file(s) that are not Vita games removed");
        }

        /// <summary>Every file read, off the UI thread, under a window saying which. False when cancelled.</summary>
        private static bool Read(IWin32Window owner, List<Finding> findings)
        {
            using var form = new Form
            {
                Text = "Nixx-Vita3K - reading the games", StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ControlBox = false,
                ShowInTaskbar = false, ClientSize = new Size(460, 110), Font = new Font("Segoe UI", 9f),
            };
            var label = new Label { Location = new Point(14, 14), Size = new Size(432, 20), AutoEllipsis = true, Text = "Reading the list..." };
            var bar = new ProgressBar { Location = new Point(14, 40), Size = new Size(432, 18), Maximum = Math.Max(1, findings.Count) };
            var cancel = new Button { Text = "Cancel", Location = new Point(356, 70), Width = 90 };
            form.Controls.AddRange(new Control[] { label, bar, cancel });
            bool cancelled = false;
            cancel.Click += (_, _) => { cancelled = true; cancel.Enabled = false; };

            form.Shown += (_, _) =>
            {
                var thread = new Thread(() =>
                {
                    for (int i = 0; i < findings.Count && !cancelled; i++)
                    {
                        var f = findings[i];
                        int n = i;
                        try { form.BeginInvoke(new Action(() => { label.Text = "Reading " + (n + 1) + "/" + findings.Count + ": " + System.IO.Path.GetFileName(f.Path); bar.Value = n; })); } catch { }
                        if (string.IsNullOrWhiteSpace(f.Path) || !File.Exists(f.Path)) { f.Error = "the file is not there"; continue; }
                        if (!Vita3kContent.Installable(f.Path)) { f.Error = "not an archive a Vita game comes in"; continue; }
                        try { f.Content = Vita3kContent.Describe(f.Path, out f.Error); }
                        catch (Exception ex) { f.Error = ex.Message; }
                    }
                    try { form.BeginInvoke(new Action(() => form.Close())); } catch { }
                })
                { IsBackground = true, Name = "Vita3K import reading" };
                thread.Start();
            };
            form.ShowDialog(owner);
            return !cancelled;
        }

        /// <summary>The emulator folders the index goes into: every Vita3K this plugin knows.</summary>
        private static IEnumerable<string> Installs()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var exe in Vita3kPlugin.KnownExecutables())
            {
                string dir = null;
                try { dir = Vita3kPaths.Resolve(exe)?.InstallDir; } catch { }
                if (!string.IsNullOrEmpty(dir) && seen.Add(dir)) yield return dir;
            }
        }

        private static object GetProperty(object o, string name)
        {
            try { return o?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(o); }
            catch { return null; }
        }

        private static bool SetProperty(object o, string name, object value)
        {
            try
            {
                var p = o?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (p == null || !p.CanWrite) return false;
                p.SetValue(o, value);
                return true;
            }
            catch (Exception ex) { Log.Warn("[import] could not set " + name, ex); return false; }
        }

        /// <summary>Caliburn's NotifyOfPropertyChange, so a count derived from the list is shown again.</summary>
        private static void Notify(object vm, string property)
        {
            try { vm?.GetType().GetMethod("NotifyOfPropertyChange", new[] { typeof(string) })?.Invoke(vm, new object[] { property }); }
            catch { }
        }
    }
}
