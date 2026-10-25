using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace ClauDeck;

/// <summary>
/// Artwork for the installer, drawn rather than shipped: the tall panel on the welcome
/// and finish pages, and the small mark in the corner of every other page, each at the
/// sizes Inno Setup picks between for 100% to 250% display scaling.
/// </summary>
static class Branding
{
    static readonly Color ClayLight = Color.FromArgb(0xE0, 0x86, 0x66);
    static readonly Color Clay = Color.FromArgb(0xD9, 0x77, 0x57);
    static readonly Color ClayDeep = Color.FromArgb(0xB8, 0x5C, 0x3E);
    static readonly Color Ivory = Color.FromArgb(0xFA, 0xF9, 0xF5);

    public static void Write(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var (w, h) in new[] { (164, 314), (246, 459), (328, 628), (410, 797) })
            Save(Panel(w, h), Path.Combine(dir, $"wizard-{w}.bmp"));
        foreach (var (w, h) in new[] { (55, 55), (83, 80), (110, 106), (138, 140) })
            Save(Corner(w, h), Path.Combine(dir, $"small-{w}.bmp"));
    }

    // The welcome-page panel: clay, the mark, and the name.
    static Bitmap Panel(int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        using (var fill = new LinearGradientBrush(new Rectangle(0, 0, w, h), ClayLight, ClayDeep, 70f))
            g.FillRectangle(fill, 0, 0, w, h);

        var s = w / 164f;
        // The three cards of the logo, without its tile, standing on the panel itself.
        var cards = 64 * s;
        var top = h * 0.24f;
        var state = g.Save();
        g.TranslateTransform(w / 2f, top + cards / 2);
        g.RotateTransform(-14);
        var alphas = new[] { 115, 184, 255 };
        for (var i = 0; i < 3; i++)
        {
            var cw = 14 * s;
            var ch = 54 * s;
            var x = (i - 1) * 22 * s - cw / 2;
            using var brush = new SolidBrush(Color.FromArgb(alphas[i], Ivory));
            using var path = Rounded(new RectangleF(x, -ch / 2, cw, ch), cw / 2);
            g.FillPath(brush, path);
        }
        g.Restore(state);

        using var centre = new StringFormat { Alignment = StringAlignment.Center };
        using var name = new Font("Segoe UI Semibold", 21 * s, FontStyle.Bold, GraphicsUnit.Pixel);
        using var sub = new Font("Segoe UI", 12 * s, GraphicsUnit.Pixel);
        using var small = new Font("Segoe UI", 9.5f * s, GraphicsUnit.Pixel);
        using var ivory = new SolidBrush(Ivory);
        using var soft = new SolidBrush(Color.FromArgb(220, Ivory));
        using var faint = new SolidBrush(Color.FromArgb(185, Ivory));
        var y = top + cards + 26 * s;
        g.DrawString("ClauDeck", name, ivory, new RectangleF(0, y, w, 30 * s), centre);
        g.DrawString("for Windows", sub, soft, new RectangleF(0, y + 30 * s, w, 20 * s), centre);
        g.DrawString("Multiple Claude Desktops,\nin parallel", small, faint, new RectangleF(10 * s, h - 54 * s, w - 20 * s, 40 * s), centre);
        return bmp;
    }

    // The mark in the corner of the inner pages, on the white those pages use.
    static Bitmap Corner(int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var size = Math.Min(w, h);
        using var logo = Icons.Logo(size);
        g.DrawImage(logo, (w - size) / 2, (h - size) / 2, size, size);
        return bmp;
    }

    static void Save(Bitmap bmp, string path)
    {
        using (bmp) bmp.Save(path, ImageFormat.Bmp);
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
