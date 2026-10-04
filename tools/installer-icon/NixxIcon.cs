// The installer's icon, drawn (Mehdi, 04/10): in LaunchBox's family - an isometric cube of coloured pieces - but its own:
// two pieces out of the cube, one lifted out of its slot, one thrown to the side, each tied back to its hole by an
// electric arc. Drawn, not painted, so every size of the .ico is sharp; the small sizes (32 and under) bring the pieces
// closer, and 16 keeps one piece and no arc, so the cube still reads.
//
//     dotnet run --project tools\installer-icon -- src\Installer
// writes src\Installer\nixx.ico (16 to 256, PNG inside).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

public static class NixxIcon
{
    // Isometric projection: x to the lower right, y to the lower left, z up.
    static float L, Cx, Cy;
    static readonly float C30 = (float)Math.Cos(Math.PI / 6), S30 = 0.5f;
    static PointF P(float x, float y, float z) => new PointF(Cx + (x - y) * L * C30, Cy + (x + y) * L * S30 - z * L);

    static readonly Color Purple = Color.FromArgb(150, 52, 214), Cyan = Color.FromArgb(18, 176, 240), Orange = Color.FromArgb(255, 84, 20),
                          Yellow = Color.FromArgb(236, 238, 24), Green = Color.FromArgb(40, 214, 110), Ink = Color.FromArgb(18, 16, 28);

    sealed class Cubie { public float X, Y, Z, S = 1f; public Color Top, Right, Left; }

    static Color Shade(Color c, float f) => Color.FromArgb(c.A, Math.Min(255, (int)(c.R * f)), Math.Min(255, (int)(c.G * f)), Math.Min(255, (int)(c.B * f)));

    static void Face(Graphics g, PointF[] pts, Color c, float outline)
    {
        using (var b = new SolidBrush(c)) g.FillPolygon(b, pts);
        using (var hi = new LinearGradientBrush(pts[0], pts[2], Color.FromArgb(70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255))) g.FillPolygon(hi, pts);
        using (var p = new Pen(Ink, outline) { LineJoin = LineJoin.Round }) g.DrawPolygon(p, pts);
    }

    static void Draw(Graphics g, Cubie c, float gap, float outline)
    {
        float s = c.S, a = gap * s, x0 = c.X + a, y0 = c.Y + a, z0 = c.Z + a, x1 = c.X + s - a, y1 = c.Y + s - a, z1 = c.Z + s - a;
        Face(g, new[] { P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1) }, c.Top, outline);                 // top
        Face(g, new[] { P(x1, y0, z1), P(x1, y1, z1), P(x1, y1, z0), P(x1, y0, z0) }, Shade(c.Right, 0.86f), outline);  // +x, right
        Face(g, new[] { P(x0, y1, z1), P(x1, y1, z1), P(x1, y1, z0), P(x0, y1, z0) }, Shade(c.Left, 0.72f), outline);   // +y, left
    }

    // A jagged bolt from a to b, the same every time (seeded).
    static PointF[] Bolt(PointF a, PointF b, int seed, int steps, float amp)
    {
        var r = new Random(seed);
        var pts = new List<PointF> { a };
        float dx = b.X - a.X, dy = b.Y - a.Y, len = (float)Math.Sqrt(dx * dx + dy * dy), nx = -dy / len, ny = dx / len;
        for (int i = 1; i < steps; i++)
        {
            float t = i / (float)steps, o = ((float)r.NextDouble() * 2 - 1) * amp * (float)Math.Sin(Math.PI * t);
            pts.Add(new PointF(a.X + dx * t + nx * o, a.Y + dy * t + ny * o));
        }
        pts.Add(b);
        return pts.ToArray();
    }

    static void Arc(Graphics g, PointF a, PointF b, int seed, float size)
    {
        var bolt = Bolt(a, b, seed, size <= 40 ? 5 : 8, size * 0.05f);
        foreach (var (w, col) in new[] { (size * 0.085f, Color.FromArgb(55, 90, 200, 255)), (size * 0.045f, Color.FromArgb(150, 70, 190, 255)), (size * 0.02f, Color.FromArgb(255, 225, 250, 255)) })
            using (var p = new Pen(col, w) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLines(p, bolt);
        // A spark at each end.
        foreach (var e in new[] { a, b })
            using (var br = new SolidBrush(Color.FromArgb(230, 230, 250, 255))) g.FillEllipse(br, e.X - size * 0.018f, e.Y - size * 0.018f, size * 0.036f, size * 0.036f);
    }

    public static float[] FlyTop = { 1.5f, 1.5f, 4.15f, 0.8f }, FlyLow = { -0.45f, 2.75f, -0.55f, 0.72f };
    public static float Margin = 0.06f;

    /// <summary>L, Cx, Cy such that the cube and the two flying pieces fill the square, centred, with a margin.</summary>
    static void Fit(int size)
    {
        L = 1; Cx = 0; Cy = 0;
        var pts = new List<PointF>();
        void Box(float x, float y, float z, float s) { foreach (var dx in new[] { 0f, s }) foreach (var dy in new[] { 0f, s }) foreach (var dz in new[] { 0f, s }) pts.Add(P(x + dx, y + dy, z + dz)); }
        Box(0, 0, 0, 2); Box(FlyTop[0], FlyTop[1], FlyTop[2], FlyTop[3]); if (FlyLow[3] > 0) Box(FlyLow[0], FlyLow[1], FlyLow[2], FlyLow[3]);
        float minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        float avail = size * (1 - 2 * Margin), scale = avail / Math.Max(maxX - minX, maxY - minY);
        L = scale;
        Cx = size / 2f - (minX + maxX) / 2f * scale;
        Cy = size / 2f - (minY + maxY) / 2f * scale;
    }

    public static Bitmap Render(int size, bool arcs = true)
    {
        // The pieces closer for the small sizes - the cube must still read at 32 px; at 16 the lower one is left out.
        bool tiny = size <= 20, small = size <= 40;
        FlyTop = small ? new[] { 1.5f, 1.5f, tiny ? 2.85f : 3.15f, 0.8f } : new[] { 1.5f, 1.5f, 4.15f, 0.8f };
        FlyLow = tiny ? new[] { 0.6f, 0.6f, 0f, 0f } : small ? new[] { -0.2f, 2.5f, -0.35f, 0.72f } : new[] { -0.45f, 2.75f, -0.55f, 0.72f };
        Margin = tiny ? 0.02f : small ? 0.03f : 0.06f;
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            float outline = size <= 20 ? 1f : Math.Max(1.2f, size * 0.022f), gap = size <= 20 ? 0.02f : 0.05f;

            Fit(size);
            // The cube: 2 x 2 x 2, two of its pieces out of it.
            var all = new List<Cubie>();
            Color[] top = { Purple, Yellow, Orange, Cyan }, right = { Purple, Yellow, Cyan, Orange }, left = { Cyan, Orange, Green, Yellow };
            int n = 0;
            for (int z = 0; z < 2; z++) for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++)
            {
                if ((x == 1 && y == 1 && z == 1) || (!tiny && x == 0 && y == 1 && z == 0)) continue;     // the two that fly
                all.Add(new Cubie { X = x, Y = y, Z = z, Top = top[n % 4], Right = right[(n + 1) % 4], Left = left[(n + 2) % 4] });
                n++;
            }
            foreach (var c in all.OrderBy(c => c.X + c.Y).ThenBy(c => c.Z)) Draw(g, c, gap, outline);

            // The two flying pieces, a little smaller, away from where they belong - each tied back by its arc.
            var flyTop = new Cubie { X = FlyTop[0], Y = FlyTop[1], Z = FlyTop[2], S = FlyTop[3], Top = Green, Right = Orange, Left = Purple };
            var flyLow = new Cubie { X = FlyLow[0], Y = FlyLow[1], Z = FlyLow[2], S = FlyLow[3], Top = Yellow, Right = Cyan, Left = Orange };
            if (arcs)
            {
                // From the hole each piece left - its far inner corner - to the piece.
                Arc(g, P(1.5f, 1.5f, 1.15f), P(flyTop.X + flyTop.S * 0.55f, flyTop.Y + flyTop.S * 0.55f, flyTop.Z), 7, size);
                if (!tiny) Arc(g, P(0.35f, 1.6f, 0.65f), P(flyLow.X + flyLow.S * 0.9f, flyLow.Y + flyLow.S * 0.1f, flyLow.Z + flyLow.S * 0.85f), 11, size);
            }
            if (!tiny) Draw(g, flyLow, gap, outline);
            Draw(g, flyTop, gap, outline);
        }
        return bmp;
    }

    /// <summary>An .ico of PNG images, one per size.</summary>
    public static void WriteIco(string path, int[] sizes)
    {
        var pngs = sizes.Select(s => { using (var b = Render(s, s > 20)) using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); } }).ToList();
        using (var w = new BinaryWriter(File.Create(path)))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(pngs[i].Length); w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (var p in pngs) w.Write(p);
        }
    }
}
