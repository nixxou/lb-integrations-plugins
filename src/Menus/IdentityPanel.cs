// "Your console": the pack's one identity (src\Shared.Identity\PackIdentity.cs), edited - the Nixx window's tab of that
// name and the installer's window of the same. Under each setting, in grey, the plugins that use it (Mehdi, 03/10:
// "precise dans une ligne en dessous a quel greffons ca s'applique"); under the nickname, in red, what a DS will keep
// of a longer one; under the language, the consoles that do not have it.

#nullable disable

using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LbIntegrations.Identity;

namespace LbIntegrations.Menus
{
    internal sealed class IdentityPanel : FlowLayoutPanel
    {
        public const string Title = "Your console";

        private readonly TextBox _nickname, _seed;
        private readonly Label _nickRed, _nickXbox, _languageNot, _seedRed, _seedValues;
        private bool _drawnOnce;

        /// <summary>A seed is written: the installer's OK and the tab's saving wait for it (Mehdi, 05/10).</summary>
        public bool Complete => new PackIdentity { Seed = _seed.Text }.HasSeed;
        public event Action CompleteChanged;
        private readonly ComboBox _language, _date, _clock, _confirm, _month, _colour;
        private readonly NumericUpDown _day;

        private static readonly string[] Months =
            { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };

        /// <param name="applyToEmulators">The Nixx window's "Apply to my emulators..." (IdentityApply) - null in the installer,
        /// where no plugin is loaded to say what it would change.</param>
        public IdentityPanel(PackIdentity start = null, Action<IdentityPanel> applyToEmulators = null)
        {
            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(4);
            var p = start ?? PackIdentity.Load() ?? PackIdentity.FromWindows();
            bool saved = start != null || PackIdentity.Exists();

            Controls.Add(Grey((saved ? "" : "Not set yet: filled in from this Windows. ")
                + "Each plugin reads this when it sets up its emulator for the first time; an emulator already set up keeps "
                + "its own settings; a DSi console is set up with it when made from a blank NAND. Not used by SUPER ZSNES "
                + "(it has no such settings).", 620, new Padding(0, 0, 0, 10)));

            var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            // ── seed (Mehdi, 05/10): every console's own values come from it ──
            _seed = new TextBox { Width = 380, MaxLength = 200, Text = p.Seed ?? "" };
            var draw = new Button { Text = "New", AutoSize = true, Margin = new Padding(6, 1, 3, 1) };
            draw.Click += (_, _) => DrawSeed();
            var seedLine = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            seedLine.Controls.Add(_seed);
            seedLine.Controls.Add(draw);
            _seedRed = new Label { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(460, 0), Margin = new Padding(3, 2, 3, 0) };
            _seedValues = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Font = new Font("Consolas", 8.5f), Margin = new Padding(3, 4, 3, 0) };
            Row(grid, "Seed", seedLine, _seedRed, _seedValues,
                Applies("Cxbx-Reloaded and xemu at every launch: the serial number, MAC address, HDD key and online key above, the same "
                      + "on both - and later the other consoles' own values. A sentence easy to remember: the same seed gives the same "
                      + "console back after a reinstall or on another computer. Another seed is another console: an Insignia "
                      + "registration is lost."));

            // ── nickname ──
            _nickname = new TextBox { Width = 220, MaxLength = PackIdentity.PspNicknameMax, Text = p.Nickname };
            _nickRed = new Label { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(460, 0), Margin = new Padding(3, 2, 3, 0) };
            _nickXbox = Grey("", 460);
            Row(grid, "Nickname", _nickname, _nickRed, _nickXbox,
                Applies("Xenia (the gamertag of the profile it creates), melonDS and no$gba (the DS and DSi name, 10 characters), "
                      + "PPSSPP (the PSP's nickname, 32 characters)."));

            // ── language ──
            _language = Combo(PackIdentity.Languages.Select(l => l.Name), 220);
            _language.SelectedIndex = Math.Max(0, Array.FindIndex(PackIdentity.Languages, l => l.Culture.Equals(p.Language, StringComparison.OrdinalIgnoreCase)));
            _languageNot = new Label { AutoSize = true, ForeColor = Color.DarkGoldenrod, MaximumSize = new Size(460, 0), Margin = new Padding(3, 2, 3, 0) };
            Row(grid, "Language", _language, _languageNot,
                Applies("Xenia, Vita3K, PPSSPP, melonDS, no$gba (its DSi consoles), Flycast - and Cxbx-Reloaded and xemu at every launch (their Language "
                      + "option, when left on its default)."));

            // ── date ──
            _date = Combo(new[] { "2026/10/03 - year, month, day", "03/10/2026 - day, month, year", "10/03/2026 - month, day, year" }, 220);
            _date.SelectedIndex = p.DateOrder == "dmy" ? 1 : p.DateOrder == "mdy" ? 2 : 0;
            Row(grid, "Date", _date, Applies("Vita3K, PPSSPP."));

            // ── clock ──
            _clock = Combo(new[] { "24-hour - 15:30", "12-hour - 3:30 PM" }, 220);
            _clock.SelectedIndex = p.Clock24 ? 0 : 1;
            Row(grid, "Time", _clock, Applies("Xenia, Vita3K, PPSSPP."));

            // ── confirm ──
            _confirm = Combo(new[] { "✕ Cross confirms - Europe, the Americas", "○ Circle confirms - Japan" }, 260);
            _confirm.SelectedIndex = p.Confirm == "circle" ? 1 : 0;
            Row(grid, "Confirm button", _confirm, Applies("Vita3K, PPSSPP. The Xbox, DS and Dreamcast have no such setting."));

            // ── birthday ──
            _month = Combo(Months, 120);
            _month.SelectedIndex = Math.Min(11, Math.Max(0, p.BirthMonth - 1));
            _day = new NumericUpDown { Minimum = 1, Maximum = 31, Width = 50, Value = Math.Min(31, Math.Max(1, p.BirthDay)), Margin = new Padding(6, 3, 3, 3) };
            var birthday = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            birthday.Controls.Add(_month);
            birthday.Controls.Add(_day);
            Row(grid, "Birthday", birthday, Applies("melonDS (DS games, and the DSi consoles made from a blank NAND), no$gba (the same DSi consoles)."));

            // ── colour ──
            _colour = Combo(PackIdentity.DsColours.Select(c => c.Name), 180);
            _colour.DrawMode = DrawMode.OwnerDrawFixed;
            _colour.DrawItem += (_, e) =>
            {
                e.DrawBackground();
                if (e.Index >= 0)
                {
                    using var swatch = new SolidBrush(Color.FromArgb(255, Color.FromArgb(PackIdentity.DsColours[e.Index].Rgb)));
                    e.Graphics.FillRectangle(swatch, e.Bounds.Left + 3, e.Bounds.Top + 2, 14, e.Bounds.Height - 4);
                    TextRenderer.DrawText(e.Graphics, PackIdentity.DsColours[e.Index].Name, e.Font, new Point(e.Bounds.Left + 22, e.Bounds.Top + 1), e.ForeColor);
                }
            };
            _colour.SelectedIndex = Math.Min(15, Math.Max(0, p.Colour));
            Row(grid, "Favourite colour", _colour, Applies("melonDS (the same)."));

            Controls.Add(grid);

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 10, 0, 0) };
            var windows = new Button { Text = "Take this Windows' values", AutoSize = true };
            windows.Click += (_, _) => Fill(PackIdentity.FromWindows(), keepNickname: _nickname.Text.Trim().Length > 0);
            buttons.Controls.Add(windows);
            if (applyToEmulators != null)
            {
                // Saved first: each plugin reads identity.ini to say what it would change.
                var apply = new Button { Text = "Save and apply to my emulators...", AutoSize = true, Margin = new Padding(12, 3, 3, 3) };
                apply.Click += (_, _) =>
                {
                    var problem = Save();
                    if (problem != null) { MessageBox.Show(this, problem, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    applyToEmulators(this);
                };
                buttons.Controls.Add(apply);
                Controls.Add(buttons);
                Controls.Add(Grey("Applies your console to the emulators already set up - each one listed with what it holds now and "
                                + "what it would hold, for you to tick. The DSi consoles already made are never rewritten.", 620, new Padding(0, 4, 0, 0)));
            }
            else Controls.Add(buttons);

            _nickname.TextChanged += (_, _) => FollowNickname();
            _language.SelectedIndexChanged += (_, _) => FollowLanguage();
            _seed.TextChanged += (_, _) => { FollowSeed(); CompleteChanged?.Invoke(); };
            FollowNickname();
            FollowLanguage();
            FollowSeed();
        }

        // ── the seed ──

        private static string[] _words;

        /// <summary>The words a new seed is drawn from: Moby-Dick's (SeedWords.txt, embedded).</summary>
        private static string[] Words()
        {
            if (_words != null) return _words;
            try
            {
                using var s = typeof(IdentityPanel).Assembly.GetManifestResourceStream("LbIntegrations.SeedWords.txt");
                using var r = new System.IO.StreamReader(s);
                _words = r.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(w => w.Trim()).Where(w => w.Length > 0 && w[0] != '#').ToArray();
            }
            catch { _words = Array.Empty<string>(); }
            return _words;
        }

        /// <summary>Six words drawn at random. The first time only, a word that one of your own would be better (Mehdi, 05/10).</summary>
        private void DrawSeed()
        {
            if (!_drawnOnce)
            {
                _drawnOnce = true;
                MessageBox.Show(this, "A seed of your own is better: a sentence easy to remember. The same seed gives the same console back "
                                    + "after a reinstall or on another computer - a drawn one has to be written down somewhere to be found again.",
                                Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            var words = Words();
            if (words.Length == 0) return;
            _seed.Text = string.Join(" ", Enumerable.Range(0, 6).Select(_ => words[System.Security.Cryptography.RandomNumberGenerator.GetInt32(words.Length)]));
        }

        private void FollowSeed()
        {
            var x = new PackIdentity { Seed = _seed.Text }.Xbox();
            _seedRed.Text = x == null ? "Write a seed, or draw one with New: the consoles' serial numbers and keys are made from it." : "";
            _seedRed.Visible = x == null;
            _seedValues.Text = x == null ? "" :
                "Xbox    serial " + x.Serial + "    MAC " + x.MacText + "\n"
              + "        HDD key " + Convert.ToHexString(x.HddKey) + "\n"
              + "        online key " + Convert.ToHexString(x.OnlineKey);
            _seedValues.Visible = x != null;
        }

        // ── building blocks ──

        private static Label Grey(string text, int width, Padding? margin = null) => new Label
        {
            Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(width, 0),
            Margin = margin ?? new Padding(3, 2, 3, 0),
        };

        private static Label Applies(string text) => Grey("Applies to: " + text, 460, new Padding(3, 1, 3, 10));

        private static ComboBox Combo(System.Collections.Generic.IEnumerable<string> items, int width)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width };
            c.Items.AddRange(items.Cast<object>().ToArray());
            return c;
        }

        private static void Row(TableLayoutPanel grid, string label, Control input, params Control[] under)
        {
            int row = grid.RowCount++;
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, 0, row);
            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
            stack.Controls.Add(input);
            foreach (var u in under) stack.Controls.Add(u);
            grid.Controls.Add(stack, 1, row);
        }

        // ── following the choices ──

        private void FollowNickname()
        {
            var n = _nickname.Text.Trim();
            _nickRed.Text = n.Length > PackIdentity.DsNicknameMax
                ? "Longer than " + PackIdentity.DsNicknameMax + " characters: the DS (melonDS) will keep only \"" + n.Substring(0, PackIdentity.DsNicknameMax) + "\"."
                : "";
            _nickRed.Visible = _nickRed.Text.Length > 0;
            _nickXbox.Text = n.Length == 0
                ? "Empty: each emulator does as before (Xenia names its profile after this Windows account)."
                : "Xbox 360 gamertag (letters and digits only, 15 at most): \"" + PackIdentity.Gamertag(n) + "\"";
        }

        private void FollowLanguage()
        {
            var code = new PackIdentity { Language = PackIdentity.Languages[Math.Max(0, _language.SelectedIndex)].Culture }.LanguageCode;
            var missing = PackIdentity.ConsoleLanguages.Where(c => !c.Has.Contains(code)).Select(c => c.Console).ToList();
            _languageNot.Text = missing.Count == 0 ? "" : "Not on " + string.Join(", ", missing) + ": English there.";
            _languageNot.Visible = missing.Count > 0;
        }

        private void Fill(PackIdentity p, bool keepNickname)
        {
            if (!keepNickname) _nickname.Text = p.Nickname;
            _language.SelectedIndex = Math.Max(0, Array.FindIndex(PackIdentity.Languages, l => l.Culture.Equals(p.Language, StringComparison.OrdinalIgnoreCase)));
            _date.SelectedIndex = p.DateOrder == "dmy" ? 1 : p.DateOrder == "mdy" ? 2 : 0;
            _clock.SelectedIndex = p.Clock24 ? 0 : 1;
            _confirm.SelectedIndex = p.Confirm == "circle" ? 1 : 0;
        }

        /// <summary>What the panel says now.</summary>
        public PackIdentity Value => new PackIdentity
        {
            Nickname = _nickname.Text.Trim(),
            Language = PackIdentity.Languages[Math.Max(0, _language.SelectedIndex)].Culture,
            DateOrder = _date.SelectedIndex == 1 ? "dmy" : _date.SelectedIndex == 2 ? "mdy" : "ymd",
            Clock24 = _clock.SelectedIndex == 0,
            Confirm = _confirm.SelectedIndex == 1 ? "circle" : "cross",
            BirthMonth = _month.SelectedIndex + 1,
            BirthDay = (int)_day.Value,
            Colour = Math.Max(0, _colour.SelectedIndex),
            Seed = _seed.Text.Trim(),
        };

        /// <summary>Null when saved, or why not.</summary>
        public string Save()
        {
            if (!Complete) return "Write a seed first - or draw one with New: the consoles' serial numbers and keys are made from it.";
            try { Value.Save(); return null; }
            catch (Exception ex) { return "Your console could not be saved: " + ex.Message; }
        }
    }
}
