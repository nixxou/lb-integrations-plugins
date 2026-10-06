// A per-game options window opened on SEVERAL games - the pack's rule (Mehdi, 06/10, first in the ares window):
//
//   THE TOP      a list: "All selected games (N)", the default, then each group of games with identical options
//                ("Zelda <and 2 others> - its own: Shader, Scaling") to start from that group's values.
//   ALL          a setting the games agree on shows as it is; one they do not shows as VARIOUS - "Various (n of N)",
//                n the games with a value of their own - with the purple bar (OptionLevel.Various). Left so, each game
//                keeps its own; changed, it goes to all.
//   OK           asked first (several games), then each game gets only what the user changed (LbipGameEdit.Merge):
//                a "various" left as it is changes nothing.
//   ONE GAME'S   facts and tabs about one game only (its file, its saves, its compatibility...) are hidden when several
//                games are selected - the window says nothing it cannot say of all of them.
//
// Short texts (Mehdi: "pas trop verbeux, faire court").
//
// A window keeps, per game, its STORED values as key -> value (a key absent = not set, on its default); it shows the
// same keys, Various for one the games do not agree on, null for one on its default. This class does the bookkeeping;
// the window fills its own editors.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LbIntegrations.Lbip
{
    internal sealed class LbipSelection
    {
        /// <summary>A shown value: the selected games do not agree on this key.</summary>
        public const string Various = "\u0001various";

        public sealed class Member
        {
            public string Title;
            public IDictionary<string, string> Stored;   // key -> value; a key absent = not set
            public object Tag;                           // the window's own (its game, its entry)
            internal string Key;
        }

        private readonly StringComparer _keys;
        public readonly List<Member> Members;
        public readonly List<List<Member>> Groups;
        public bool Several => Members.Count > 1;

        public LbipSelection(IEnumerable<Member> members, StringComparer keys = null)
        {
            _keys = keys ?? StringComparer.Ordinal;
            Members = members.ToList();
            foreach (var m in Members)
            {
                m.Stored = m.Stored ?? new Dictionary<string, string>(_keys);
                m.Key = string.Join("\n", m.Stored.Where(kv => kv.Value != null).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));
            }
            Groups = Members.GroupBy(m => m.Key).Select(g => g.ToList()).ToList();
        }

        private static string Get(IDictionary<string, string> d, string key) => d != null && d.TryGetValue(key, out var v) ? v : null;

        /// <summary>"All selected games": each key as they all have it, or Various.</summary>
        public Dictionary<string, string> All(IEnumerable<string> keys)
        {
            var shown = new Dictionary<string, string>(_keys);
            foreach (var key in keys)
            {
                var values = Members.Select(m => Get(m.Stored, key)).Distinct().ToList();
                shown[key] = values.Count == 1 ? values[0] : Various;
            }
            return shown;
        }

        /// <summary>A group: its first game's values.</summary>
        public Dictionary<string, string> From(Member m, IEnumerable<string> keys)
        {
            var shown = new Dictionary<string, string>(_keys);
            foreach (var key in keys) shown[key] = Get(m.Stored, key);
            return shown;
        }

        /// <summary>How many of the selected games have a value of their own for a key.</summary>
        public int OwnCount(string key) => Members.Count(m => Get(m.Stored, key) != null);

        /// <summary>What an editor shows for a key the games do not agree on - short.</summary>
        public string VariousText(string key) => "Various (" + OwnCount(key) + " of " + Members.Count + ")";

        /// <summary>The top of the window over several games: a title line and the list (All selected games, then the
        /// groups). <paramref name="picked"/> is told the values to show: at once for All, then at each choice. Null for
        /// one game - the window shows its title as it always did.</summary>
        public Panel Top(IEnumerable<string> keys, Func<Member, string> describe, Action<Dictionary<string, string>> picked)
        {
            if (!Several) return null;
            var keyList = keys.ToList();
            var top = new Panel { Dock = DockStyle.Top, Height = 58, Padding = new Padding(12, 8, 12, 0) };
            top.Controls.Add(new Label
            {
                Dock = DockStyle.Top, Height = 22, UseMnemonic = false,
                Text = Members.Count + " games" + (Groups.Count > 1 ? " - their options differ" : ""),
            });
            var source = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
            source.Items.Add("All selected games (" + Members.Count + ")");
            if (Groups.Count > 1)
                foreach (var g in Groups)
                    source.Items.Add(g[0].Title + (g.Count > 1 ? " <and " + (g.Count - 1) + " other" + (g.Count > 2 ? "s" : "") + ">" : "")
                                     + (describe != null ? "   -   " + describe(g[0]) : ""));
            source.SelectedIndexChanged += (_, _) => picked(source.SelectedIndex <= 0 ? All(keyList) : From(Groups[source.SelectedIndex - 1][0], keyList));
            top.Controls.Add(source);
            source.BringToFront();
            Source = source;
            return top;
        }

        /// <summary>The list of the top, once built - select its first item when the window's editors exist.</summary>
        public ComboBox Source { get; private set; }

        /// <summary>OK over several games: asked first. True to go on.</summary>
        public bool Confirm(IWin32Window owner, string caption)
            => !Several || MessageBox.Show(owner, "Your changes go to all " + Members.Count + " selected games; what each has of its own and you did not change stays.",
                                           caption, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK;

        /// <summary>A game's stored values with the user's changes on them: what was not changed - and a Various left as it
        /// is - stays as the game has it (LbipGameEdit.Merge).</summary>
        public Dictionary<string, string> MergeFor(Member m, IDictionary<string, string> atOpen, IDictionary<string, string> now)
        {
            var changes = new Dictionary<string, string>(_keys);
            foreach (var kv in now ?? new Dictionary<string, string>())
                if (kv.Value != Various) changes[kv.Key] = kv.Value;
            var stored = new Dictionary<string, string>(_keys);
            foreach (var kv in m.Stored) if (kv.Value != null) stored[kv.Key] = kv.Value;
            return LbipGameEdit.Merge(stored, atOpen, changes, _keys);
        }

        /// <summary>Did the user change anything at all.</summary>
        public static bool Changed(IDictionary<string, string> atOpen, IDictionary<string, string> now) => LbipGameEdit.Changed(atOpen, now);

        /// <summary>The bar colour of a shown value: Various purple, set blue, else the emulator's grey.</summary>
        public static Color BarOf(string shown) => OptionMarks.ColorOf(shown == Various ? OptionLevel.Various : shown != null ? OptionLevel.Here : OptionLevel.Emulator);
    }
}
