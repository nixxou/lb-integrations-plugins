// The game list of LaunchBox's Import ROM Files wizard, put right for Vita games - after the scan, while
// the list is on show, so the user sees the result and can still change it before "Finish".
//
// The folders of the list are scanned (Vita3kScan: every Vita file there read once by its param.sfo, without unpacking,
// and kept in the scan's cache), under a progress window, then each line is judged by what its file holds:
//   - a GAME (CATEGORY gd) stays, titled by its param.sfo's TITLE - "LittleBigPlanet PlayStation Vita"
//     rather than the file's name with its tags - cleaned of ™ ® © and their kind;
//   - an UPDATE (gp) or a DLC (ac) goes: it is not a game, a launch installs it with its game - found there in the
//     scan's cache, by its title id;
//   - a GAME AS A .pkg WHOSE LICENCE IS NOWHERE goes: it would be imported, then refused at every
//     launch (Vita3kLicences - beside it, or in the emulator's zrif folder). An update needs none (it
//     runs under its game's); a DLC's is not looked for here - it leaves the list anyway.
//   - anything else - unreadable, not Vita content - goes, and the log says why.
//
// ONLY IN THE VITA CASE (Vita3kLbImport swapped the platform: the wizard was on its way to its own
// Vita import), and when the setting is on (Vita3kSettings.CleanImportList, the Vita3K tab).
//
// The list is Caliburn's BindableCollection, the one the grid shows: a record removed from it is gone
// from the grid and from the import. A record's Title has a setter (the grid edits it); a renamed
// record is taken out and put back at its place, so the grid redraws it - the record itself says
// nothing when it changes.
//
// TWO HALVES, AND ONLY ONE IS LAUNCHBOX'S. Reached only from Vita3kLbImport, so only inside LaunchBox.
//   - LAUNCHBOX'S: the list itself - Games, ApplicationPath, the Title setter, the out-and-back to
//     redraw a line, NotifyOfPropertyChange("GameCount"), the progress window over the wizard. All
//     by reflection on a host we do not own.
//   - THE HOST'S BUSINESS, NOBODY'S IN PARTICULAR: what each FILE is - Read (the scan), then the verdict in
//     Run (a game, and its title by CleanTitle; an update or a DLC; a .pkg game without a licence; not Vita content).
// FOR LITEBOX'S FUTURE IMPORT (Mehdi, 28/09: it will call the chosen emulator's plugin on the final
// listing - the contract to come is noted at the end of src\Catalog\LbCatalog.cs), the second half
// comes out of Run into a method over PATHS that returns a verdict per file - keep as <title>, or
// drop with a reason. Run then becomes LaunchBox's adapter: the list
// in, the verdicts applied to its records. Not done yet: nothing calls it but this file.

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

            int renamed = 0, extras = 0, invalid = 0, unlicensed = 0;
            // Three boxes of the Vita3K tab, each on its own (Mehdi, 03/10): the filter, the name, the region (Vita3kImportFinished).
            bool clean = Vita3kSettings.CleanImportList, titles = Vita3kSettings.ImportTitle;
            if (Vita3kLicences.InstallDir == null) Vita3kLicences.InstallDir = Installs().FirstOrDefault();
            var games = findings.Where(f => f.Content != null && f.Content.IsGame).ToList();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var f = findings.FirstOrDefault(x => ReferenceEquals(x.Record, list[i]));
                if (f == null) continue;
                var name = System.IO.Path.GetFileName(f.Path ?? "");
                if (f.Content == null)
                {
                    if (clean) { list.RemoveAt(i); invalid++; }
                    Log.Info("[import]   " + (clean ? "removed " : "kept, though ") + name + " - " + (f.Error ?? "not readable"));
                    continue;
                }
                if (f.Content.IsPatch || f.Content.IsAddon)
                {
                    if (!clean) { Log.Info("[import]   kept, though " + name + " is an update or a DLC (filtering turned off)"); continue; }
                    list.RemoveAt(i); extras++;
                    var with = games.Where(g => string.Equals(g.Content.TitleId, f.Content.TitleId, StringComparison.OrdinalIgnoreCase)).ToList();
                    Log.Info("[import]   removed " + name + " - " + (f.Content.IsPatch ? "update " + f.Content.AppVer : "DLC " + (f.Content.Title ?? f.Content.ContentId))
                             + " of " + f.Content.TitleId + ", found by its game at launch" + (with.Count == 0 ? " (its game is not in this list)" : ""));
                    continue;
                }
                if (!f.Content.IsGame)
                {
                    if (clean) { list.RemoveAt(i); invalid++; }
                    Log.Info("[import]   " + (clean ? "removed " : "kept, though ") + name + " - category " + (f.Content.Category ?? "?") + " is not a game");
                    continue;
                }
                if (clean && Vita3kContent.IsPkg(f.Path) && Vita3kLicences.Find(f.Path, f.Content.ContentId, out _, out var noLicence) == null)
                {
                    list.RemoveAt(i); unlicensed++;
                    Log.Info("[import]   removed " + name + " - " + noLicence);
                    continue;
                }
                // Its region and version, set once it is in the library - see Vita3kImportFinished.
                Vita3kImportFinished.Prepare(f.Path, f.Content.ContentId, f.Content.TitleId);
                // The pack's rule (LbipImportTitle): the original kept when LaunchBox's database knows it, else the param.sfo's
                // TITLE (its full one, then its short one) when the database knows that, else the original - unless it is no
                // name at all (a serial, a store's code): then the param.sfo's.
                var current = GetProperty(f.Record, "Title") as string;
                var title = LbIntegrations.Lbip.LbipImportTitle.Choose("Sony Playstation Vita", current, System.IO.Path.GetFileNameWithoutExtension(f.Path ?? ""),
                                                                       out var why, CleanTitle(f.Content.FullTitle), CleanTitle(f.Content.Title));
                Log.Info("[import]   " + name + ": " + why);
                if (titles && !string.IsNullOrWhiteSpace(title) && !string.Equals(title, current, StringComparison.Ordinal)
                    && SetProperty(f.Record, "Title", title))
                {
                    // Out and back at its place: the grid redraws a line only when the list says it changed.
                    list.RemoveAt(i);
                    list.Insert(i, f.Record);
                    renamed++;
                }
            }
            Notify(gameList, "GameCount");

            Log.Info("[import] the list put right: " + games.Count + " game(s), " + renamed + " renamed (from their param.sfo), "
                     + extras + " update(s)/DLC removed (found by their game at launch), " + invalid + " file(s) that are not Vita games removed, "
                     + unlicensed + " .pkg game(s) without a licence removed");
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
            var bar = new ProgressBar { Location = new Point(14, 40), Size = new Size(432, 18), Maximum = 1000 };
            var cancel = new Button { Text = "Cancel", Location = new Point(356, 70), Width = 90 };
            form.Controls.AddRange(new Control[] { label, bar, cancel });
            bool cancelled = false;
            cancel.Click += (_, _) => { cancelled = true; cancel.Enabled = false; };

            form.Shown += (_, _) =>
            {
                var thread = new Thread(() =>
                {
                    // Every folder of the list scanned (Vita3kScan): each file read once by its contents and kept in the scan's
                    // cache - which is where a launch then finds the updates and DLC this list held, wherever they were.
                    var folders = findings.Where(f => !string.IsNullOrWhiteSpace(f.Path)).Select(f => System.IO.Path.GetDirectoryName(f.Path))
                                          .Where(d => d != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    for (int i = 0; i < folders.Count && !cancelled; i++)
                    {
                        int n = i;
                        Vita3kScan.Scan(folders[i], (step, frac) =>
                        {
                            try { form.BeginInvoke(new Action(() => { label.Text = "(" + (n + 1) + "/" + folders.Count + ") " + step; bar.Value = Math.Min(bar.Maximum, (int)((n + (frac ?? 0)) * bar.Maximum / folders.Count)); })); } catch { }
                        }, () => cancelled);
                    }
                    var known = Vita3kScan.CachedAll();
                    foreach (var f in findings)
                    {
                        if (string.IsNullOrWhiteSpace(f.Path) || !File.Exists(f.Path)) { f.Error = "the file is not there"; continue; }
                        if (!Vita3kContent.Installable(f.Path)) { f.Error = "not an archive a Vita game comes in"; continue; }
                        var of = known.Where(e => string.Equals(e.Archive, f.Path, StringComparison.OrdinalIgnoreCase)).ToList();
                        if (of.Count == 0) { f.Error = "not read"; continue; }
                        if (of.All(e => e.Invalid)) { f.Error = of[0].Problem; continue; }
                        f.Content = Vita3kContent.Primary(of.Where(e => !e.Invalid).Select(e => e.ToContent()).ToList(), f.Path);
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
