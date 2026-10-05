// The Nixx window in the dark (Mehdi, 05/10: "je veux un look dark mode") - laid over whatever a page is made of, by the
// window that hosts it: the plugins build their pages as before and know nothing of it.
//
// HOW: every control of a page walked once, and every one added later (a page that redraws a part of itself) as it comes.
// Windows' own colours are replaced - the window's, a control's, its text, the grey of a note - and a colour a page chose on
// purpose is KEPT: the bars that say whether an option is set here (blue) or not (grey), a state's green, a picture's black.
// A few of the purposeful ones are made readable on the dark: dark red, dark gold, dark blue links.
// Native parts dark too: the title bar (DWM), scroll bars, combo lists and list views ("DarkMode_Explorer" theme).
// Group boxes drawn again as cards: a rounded border, their title in LiteBox's frame colour.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LbIntegrations.Ui
{
    internal static class NixxTheme
    {
        // LITEBOX'S PALETTE (Mehdi, 05/10: "faudra que visuellement ça puisse s'intégrer plus tard dans mon LiteBox") - its
        // defaults, LbApiHost\Host\UiKit\LiteBoxTheme.cs, and the few colours its own controls use beside them.
        public static readonly Color Back = Color.FromArgb(30, 30, 30);             // Bg
        public static readonly Color Side = Color.FromArgb(32, 33, 40);             // PanelC: the page bar
        public static readonly Color Card = Color.FromArgb(42, 43, 52);             // Center: a group
        public static readonly Color Field = Color.FromArgb(45, 45, 48);            // Panel2: a text box, a list
        public static readonly Color ButtonBack = Color.FromArgb(60, 60, 75);       // CancelBtn
        public static readonly Color ButtonHot = Color.FromArgb(58, 59, 70);
        public static readonly Color Border = Color.FromArgb(62, 63, 74);           // SearchBar.FieldBorder
        public static readonly Color Text = Color.FromArgb(222, 222, 222);          // Fg
        public static readonly Color Dim = Color.FromArgb(150, 150, 152);           // SubFg
        public static readonly Color Accent = Color.FromArgb(0, 122, 204);          // Accent
        public static readonly Color Caption = Color.FromArgb(96, 156, 224);        // LiteBoxFrame.Accent: a group's title
        public static readonly Color Link = Color.FromArgb(96, 156, 224);
        public static readonly Color Ok = Color.FromArgb(50, 110, 65);              // Ok
        public static readonly Color Error = Color.FromArgb(225, 95, 95);           // Danger
        public static readonly Color Warn = Color.FromArgb(225, 175, 95);           // the Plugins section's warning
        private static readonly HashSet<Control> Seen = new HashSet<Control>();

        /// <summary>The control and everything in it - and in it later - dark.</summary>
        public static void Apply(Control root)
        {
            if (root == null) return;
            Walk(root);
        }

        private static void Walk(Control c)
        {
            One(c);
            foreach (Control child in c.Controls) Walk(child);
        }

        private static bool IsWindowsColour(Color c)
            => c.IsEmpty || c == Color.Transparent || (c.IsSystemColor && c != SystemColors.Highlight && c != SystemColors.HotTrack)
               || c == Color.White || c == Color.WhiteSmoke || c == SystemColors.Control || c == SystemColors.Window;

        /// <summary>A text colour on the dark: Windows' own made light, its grey dimmed, the dark purposeful ones made readable.</summary>
        private static Color TextOf(Color c)
        {
            if (c.IsEmpty || c == SystemColors.ControlText || c == SystemColors.WindowText || c == Color.Black || c == SystemColors.ActiveCaptionText) return Text;
            if (c == SystemColors.GrayText || c == Color.Gray || c == Color.DimGray || c == Color.DarkGray || c == SystemColors.ControlDark) return Dim;
            if (c == Color.Firebrick || c == Color.Red || c == Color.DarkRed || c == Color.Crimson || c == Color.Maroon) return Error;
            if (c == Color.DarkGoldenrod || c == Color.Goldenrod || c == Color.DarkOrange || c == Color.Olive) return Warn;
            if (c == Color.Blue || c == Color.Navy || c == Color.DarkBlue || c == SystemColors.HotTrack) return Link;
            if (c == Color.DarkGreen || c == Color.Green) return Color.FromArgb(0x5F, 0xC8, 0x6E);
            return c;
        }

        /// <summary>A control the window drew itself (its page bar, its title): left as it is.</summary>
        public const string Own = "nixx-dark-own";

        private static void One(Control c)
        {
            if (Equals(c.Tag, Own)) return;
            if (!Seen.Add(c)) return;
            // An & is a letter here, never a keyboard shortcut ("RamDisk & VHDX").
            if (c is Label lab) lab.UseMnemonic = false;
            else if (c is ButtonBase bb && !(c is Button)) bb.UseMnemonic = false;
            c.Disposed += (_, _) => Seen.Remove(c);
            c.ControlAdded += (_, e) => { if (e.Control != null) Walk(e.Control); };

            // Text: as the page set it, made readable - and again whenever it sets it.
            bool busy = false;
            void Fore()
            {
                if (busy) return;
                busy = true;
                try { var t = TextOf(c.ForeColor); if (t != c.ForeColor) c.ForeColor = t; }
                finally { busy = false; }
            }
            Fore();
            c.ForeColorChanged += (_, _) => Fore();

            switch (c)
            {
                case Form f:
                    f.BackColor = Back;
                    if (f.IsHandleCreated) DarkTitle(f); else f.HandleCreated += (_, _) => DarkTitle(f);
                    break;
                case TextBoxBase t:
                    t.BackColor = t is TextBox tb && tb.ReadOnly && tb.BorderStyle == BorderStyle.None ? Card : Field;
                    if (t.BackColor == Field && t is TextBox box && box.BorderStyle == BorderStyle.Fixed3D) box.BorderStyle = BorderStyle.FixedSingle;
                    NativeDark(t);
                    break;
                case ComboBox b:
                    b.FlatStyle = FlatStyle.Flat;
                    b.BackColor = Field;
                    NativeDark(b, "DarkMode_CFD");
                    WheelScrollsPage(b, () => b.DroppedDown);
                    break;
                case ListBox l: l.BackColor = Field; l.BorderStyle = BorderStyle.FixedSingle; NativeDark(l); break;
                case ListView v: v.BackColor = Field; NativeDark(v); HeaderDark(v); break;
                case TreeView tv: tv.BackColor = Field; tv.LineColor = Border; NativeDark(tv); break;
                case NumericUpDown n: n.BackColor = Field; WheelScrollsPage(n, () => false); break;
                case Button bt:
                    bt.FlatStyle = FlatStyle.Flat;
                    bt.UseVisualStyleBackColor = false;
                    bt.BackColor = ButtonBack;
                    bt.FlatAppearance.BorderColor = Border;
                    bt.FlatAppearance.MouseOverBackColor = ButtonHot;
                    bt.FlatAppearance.MouseDownBackColor = Border;
                    bt.EnabledChanged += (_, _) => bt.ForeColor = bt.Enabled ? Text : Dim;
                    break;
                case CheckBox cb:
                    if (cb.Appearance == Appearance.Button) goto default;
                    cb.FlatStyle = FlatStyle.Standard;          // Windows' 13 px box, the text where it puts it - the box painted over
                    cb.UseVisualStyleBackColor = false;
                    Transparent(cb);
                    cb.Paint += (_, e) => PaintCheck(cb, e.Graphics);
                    cb.CheckStateChanged += (_, _) => cb.Invalidate();
                    cb.EnabledChanged += (_, _) => cb.Invalidate();
                    break;
                case RadioButton rb:
                    rb.FlatStyle = FlatStyle.Standard;
                    rb.UseVisualStyleBackColor = false;
                    Transparent(rb);
                    rb.Paint += (_, e) => PaintRadio(rb, e.Graphics);
                    rb.CheckedChanged += (_, _) => rb.Invalidate();
                    rb.EnabledChanged += (_, _) => rb.Invalidate();
                    break;
                case LinkLabel ll:
                    ll.LinkColor = Link; ll.ActiveLinkColor = Color.White; ll.VisitedLinkColor = Link; ll.DisabledLinkColor = Dim;
                    Transparent(ll);
                    break;
                case GroupBox g:
                    g.BackColor = Card;
                    g.Paint += PaintGroup;
                    break;
                case TabControl tc: TabsDark(tc); break;
                case TabPage tp: tp.UseVisualStyleBackColor = false; tp.BackColor = Back; break;
                case PictureBox _: break;                                       // its picture, its own back
                case ScrollableControl s:
                    // A panel: the parent's colour, unless it is one of the page's own (a priority bar).
                    if (IsWindowsColour(s.BackColor)) Transparent(s);
                    NativeDark(s);
                    break;
                default:
                    if (IsWindowsColour(c.BackColor)) Transparent(c);
                    break;
            }

            // A background a window sets AGAIN later, to one of Windows' (an editor shown read-only as SystemColors.Control):
            // made dark again, as the first time - never a page's own colour (a priority bar's blue or grey).
            bool backBusy = false;
            c.BackColorChanged += (_, _) =>
            {
                if (backBusy || !IsWindowsColour(c.BackColor)) return;
                backBusy = true;
                try { if (DarkBackOf(c) is Color dark) c.BackColor = dark; else Transparent(c); }
                catch { }
                finally { backBusy = false; }
            };
        }

        /// <summary>The dark background a control of this kind takes - null for one that takes its parent's.</summary>
        private static Color? DarkBackOf(Control c)
        {
            switch (c)
            {
                case Form _: return Back;
                case TextBoxBase t: return t is TextBox tb && tb.ReadOnly && tb.BorderStyle == BorderStyle.None ? Card : Field;
                case ComboBox _: case ListBox _: case ListView _: case TreeView _: case NumericUpDown _: return Field;
                case Button _: return ButtonBack;
                case GroupBox _: return Card;
                case TabPage _: return Back;
                default: return null;
            }
        }

        /// <summary>The parent's colour: a control's own reset to its ambient one, or the parent's taken when that cannot be.</summary>
        private static void Transparent(Control c)
        {
            try { c.ResetBackColor(); }
            catch { }
            if (c.Parent != null && IsWindowsColour(c.BackColor) && c.Parent.BackColor != c.BackColor && !(c.Parent is Form)) c.BackColor = c.Parent.BackColor;
            c.ParentChanged += (_, _) => { if (IsWindowsColour(c.BackColor)) try { c.ResetBackColor(); } catch { } };
        }

        // ── check boxes and round buttons, readable (Mehdi, 05/10: "blanc sur gris c'est pas lisible") ──
        // LiteBox's own (LbApiHost\Host\UiKit\ThemedCheckBox.cs): the glyph painted over Windows' - on, the accent with a white
        // tick; off, a hollow outline. One change for this window's three-state boxes, where the middle is NOT "mixed" but
        // "not set here" (the grey bar): neutral, a grey outline round a small grey core - never the accent of a choice.

        private static Rectangle GlyphBox(ButtonBase b, ContentAlignment align)
        {
            int size = Math.Max(12, (int)Math.Round(13 * b.DeviceDpi / 96.0));
            bool right = align == ContentAlignment.MiddleRight || align == ContentAlignment.TopRight || align == ContentAlignment.BottomRight;
            bool centre = align == ContentAlignment.MiddleCenter || align == ContentAlignment.TopCenter || align == ContentAlignment.BottomCenter;
            int x = right ? b.Width - size - 1 : centre ? (b.Width - size) / 2 : 0;
            int y = align == ContentAlignment.TopLeft || align == ContentAlignment.TopRight ? 1
                  : align == ContentAlignment.BottomLeft || align == ContentAlignment.BottomRight ? b.Height - size - 1
                  : (b.Height - size) / 2;
            return new Rectangle(x, y, size, size);
        }

        private static void PaintCheck(CheckBox cb, Graphics g)
        {
            var box = GlyphBox(cb, cb.CheckAlign);
            int size = box.Width;
            bool on = cb.CheckState == CheckState.Checked;
            var fill = on && cb.Enabled ? Accent : Field;
            var edge = !cb.Enabled ? Color.FromArgb(90, Dim) : on ? Accent : Color.FromArgb(200, Dim);
            // Under it, the parent's colour: Windows' glyph gone whole.
            using (var under = new SolidBrush(cb.BackColor)) g.FillRectangle(under, Rectangle.Inflate(box, 1, 1));
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(fill)) g.FillRectangle(b, box);
            using (var p = new Pen(edge)) g.DrawRectangle(p, box);
            if (on)
            {
                using var tick = new Pen(cb.Enabled ? Color.White : Dim, Math.Max(1.6f, size / 7f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                g.DrawLines(tick, new[]
                {
                    new PointF(box.Left + size * 0.24f, box.Top + size * 0.52f),
                    new PointF(box.Left + size * 0.44f, box.Top + size * 0.72f),
                    new PointF(box.Left + size * 0.78f, box.Top + size * 0.28f),
                });
            }
            else if (cb.CheckState == CheckState.Indeterminate)
            {
                using var b = new SolidBrush(cb.Enabled ? Dim : Color.FromArgb(90, Dim));
                g.FillRectangle(b, Rectangle.Inflate(box, -(int)(size * 0.3), -(int)(size * 0.3)));
            }
            g.SmoothingMode = old;
        }

        private static void PaintRadio(RadioButton rb, Graphics g)
        {
            var box = GlyphBox(rb, rb.CheckAlign);
            using (var under = new SolidBrush(rb.BackColor)) g.FillRectangle(under, Rectangle.Inflate(box, 1, 1));
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var edge = !rb.Enabled ? Color.FromArgb(90, Dim) : rb.Checked ? Accent : Color.FromArgb(200, Dim);
            using (var b = new SolidBrush(Field)) g.FillEllipse(b, box);
            using (var p = new Pen(edge, rb.Checked ? 1.6f : 1f)) g.DrawEllipse(p, box);
            if (rb.Checked)
            {
                var dot = Rectangle.Inflate(box, -(int)(box.Width * 0.28), -(int)(box.Width * 0.28));
                using var b = new SolidBrush(rb.Enabled ? Accent : Dim);
                g.FillEllipse(b, dot);
            }
            g.SmoothingMode = old;
        }

        // ── the wheel scrolls the page, never a value (Mehdi, 05/10) ───────────
        // Over a closed list or a number box, the wheel changed its value while the page was being scrolled. Now it is taken
        // from them and given to the page they are in; a list opened still scrolls its own entries.

        private static void WheelScrollsPage(Control c, Func<bool> ownWheel)
        {
            c.MouseWheel += (_, e) =>
            {
                if (ownWheel()) return;
                if (e is HandledMouseEventArgs h) h.Handled = true;
                for (var p = c.Parent; p != null; p = p.Parent)
                {
                    if (p is ScrollableControl s && s.AutoScroll && s.VerticalScroll.Visible)
                    {
                        // As the page's own wheel moves it: three lines a notch.
                        int step = SystemInformation.MouseWheelScrollLines * c.Font.Height * e.Delta / 120;
                        int y = Math.Max(0, Math.Min(s.VerticalScroll.Maximum - s.VerticalScroll.LargeChange + 1, -s.AutoScrollPosition.Y - step));
                        s.AutoScrollPosition = new Point(-s.AutoScrollPosition.X, y);
                        return;
                    }
                }
            };
        }

        // ── a group as a card ─────────────────────────────────────────────────

        private static void PaintGroup(object sender, PaintEventArgs e)
        {
            var g = (GroupBox)sender;
            var gr = e.Graphics;
            gr.SmoothingMode = SmoothingMode.AntiAlias;
            var outside = g.Parent?.BackColor ?? Back;
            gr.Clear(outside);
            var font = new Font(g.Font, FontStyle.Bold);
            var size = TextRenderer.MeasureText(gr, g.Text ?? "", font);
            int top = string.IsNullOrEmpty(g.Text) ? 2 : size.Height / 2;
            var rect = new Rectangle(0, top, g.Width - 1, g.Height - top - 1);
            using (var path = Rounded(rect, 6))
            {
                using (var fill = new SolidBrush(Card)) gr.FillPath(fill, path);
                using (var pen = new Pen(Border)) gr.DrawPath(pen, path);
            }
            if (!string.IsNullOrEmpty(g.Text))
            {
                var at = new Rectangle(10, 0, size.Width + 6, size.Height);
                using (var b = new SolidBrush(outside)) gr.FillRectangle(b, at.X - 2, 0, at.Width, top + 1);
                using (var b = new SolidBrush(Card)) gr.FillRectangle(b, at.X - 2, top + 1, at.Width, size.Height - top);
                TextRenderer.DrawText(gr, g.Text, font, at, g.Enabled ? Caption : Dim, TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            }
            font.Dispose();
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ── tabs inside a page ────────────────────────────────────────────────

        private static void TabsDark(TabControl tc)
        {
            tc.DrawMode = TabDrawMode.OwnerDrawFixed;
            tc.DrawItem += (_, e) =>
            {
                bool on = e.Index == tc.SelectedIndex;
                using (var b = new SolidBrush(on ? Card : Back)) e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, tc.TabPages[e.Index].Text, tc.Font, e.Bounds, on ? Text : Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
        }

        // ── list headers ──────────────────────────────────────────────────────

        private static void HeaderDark(ListView v)
        {
            if (v.View != View.Details || v.OwnerDraw) return;
            v.OwnerDraw = true;
            v.DrawColumnHeader += (_, e) =>
            {
                using (var b = new SolidBrush(ButtonBack)) e.Graphics.FillRectangle(b, e.Bounds);
                using (var p = new Pen(Border)) e.Graphics.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom);
                var r = e.Bounds; r.Inflate(-4, 0);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, v.Font, r, Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            v.DrawItem += (_, e) => e.DrawDefault = true;
            v.DrawSubItem += (_, e) => e.DrawDefault = true;
        }

        // ── native parts ──────────────────────────────────────────────────────

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void NativeDark(Control c, string theme = "DarkMode_Explorer")
        {
            void Set() { try { SetWindowTheme(c.Handle, theme, null); } catch { } }
            if (c.IsHandleCreated) Set(); else c.HandleCreated += (_, _) => Set();
        }

        /// <summary>The title bar dark (Windows 10 2004 and later: attribute 20; before: 19).</summary>
        public static void DarkTitle(Form f)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, sizeof(int)) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, sizeof(int));
            }
            catch { }
        }
    }
}
