// A short sentence on a page, the whole of what it says on hover (Mehdi, 05/10: "raccourcir à des phrases simples et afficher
// plus d'info en hover"). The page shows the sentence; its tooltip - and the tooltip of the controls it explains - the full
// text, in lines of 80 characters (a tooltip never wraps by itself), shown long enough to be read.
//
//     var help = LbipHint.Note("Escape twice to save and quit.", "First Escape shows ... as it ships.", 450);
//     LbipHint.Attach(checkBox, "First Escape shows ...");
//
// Nothing of the old text is lost: what a page said before goes into the tooltip, word for word.

using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace LbIntegrations.Lbip
{
    internal static class LbipHint
    {
        private static ToolTip _tips;

        /// <summary>One tooltip for the whole page - read slowly: 30 s on screen, after 300 ms.</summary>
        public static ToolTip Tips => _tips ??= new ToolTip { AutoPopDelay = 30000, InitialDelay = 300, ReshowDelay = 100 };

        /// <summary>A grey note: <paramref name="shortText"/> shown, <paramref name="full"/> on hover (none when it is null or the same).</summary>
        public static Label Note(string shortText, string full = null, int width = 450, Padding? margin = null)
        {
            var label = new Label
            {
                Text = shortText, AutoSize = true, MaximumSize = new Size(width, 0), ForeColor = SystemColors.GrayText,
                Margin = margin ?? new Padding(0, 0, 0, 6), UseMnemonic = false,
            };
            Attach(label, full);
            return label;
        }

        /// <summary>The full text on hover over <paramref name="c"/> - and over each of <paramref name="more"/>.</summary>
        public static void Attach(Control c, string full, params Control[] more)
        {
            if (c == null || string.IsNullOrWhiteSpace(full)) return;
            var text = Wrap(full.Trim(), 80);
            Tips.SetToolTip(c, text);
            foreach (var m in more) if (m != null) Tips.SetToolTip(m, text);
        }

        /// <summary>Lines of at most <paramref name="width"/> characters, by words; the text's own line breaks kept.</summary>
        public static string Wrap(string text, int width = 80)
        {
            var lines = new List<string>();
            foreach (var paragraph in text.Replace("\r", "").Split('\n'))
            {
                var line = new StringBuilder();
                foreach (var word in paragraph.Split(' '))
                {
                    if (line.Length > 0 && line.Length + 1 + word.Length > width) { lines.Add(line.ToString()); line.Clear(); }
                    if (line.Length > 0) line.Append(' ');
                    line.Append(word);
                }
                lines.Add(line.ToString());
            }
            return string.Join("\n", lines);
        }
    }
}
