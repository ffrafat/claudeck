using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ClauDeck;

/// <summary>
/// Account avatars: Claude's white sunburst, taken from the Claude installed on this
/// PC, on a tile of the account's own colour. Nothing of Claude's artwork ships with
/// ClauDeck. Icon files are named for their colour because Explorer caches icons by path.
/// </summary>
static class Icons
{
    public sealed record Swatch(string Name, Color Color);

    // Every colour here keeps the white sunburst clearly readable: mid-to-deep tones
    // only, nothing pale enough for white to wash out on. The first is Claude's own clay.
    public static readonly Swatch[] Palette =
    {
        new("Clay", Color.FromArgb(0xD9, 0x77, 0x57)),
        new("Ocean", Color.FromArgb(0x25, 0x63, 0xEB)),
        new("Indigo", Color.FromArgb(0x5B, 0x52, 0xD6)),
        new("Plum", Color.FromArgb(0x8E, 0x44, 0xAD)),
        new("Rose", Color.FromArgb(0xC2, 0x41, 0x6B)),
        new("Crimson", Color.FromArgb(0xC0, 0x39, 0x2B)),
        new("Amber", Color.FromArgb(0xC2, 0x7C, 0x0E)),
        new("Forest", Color.FromArgb(0x2E, 0x7D, 0x4F)),
        new("Teal", Color.FromArgb(0x0F, 0x8B, 0x8D)),
        new("Cyan", Color.FromArgb(0x0E, 0x74, 0x90)),
        new("Slate", Color.FromArgb(0x47, 0x55, 0x69)),
        new("Charcoal", Color.FromArgb(0x2B, 0x2A, 0x27)),
    };

    static Bitmap? baseBitmap;
    static Color? baseTile;
    static readonly Dictionary<int, Bitmap> recoloured = new();

    public static int ColorIndex(Account a) => ((a.Color % Palette.Length) + Palette.Length) % Palette.Length;
    public static Color ColorOf(Account a) => Palette[ColorIndex(a)].Color;

    /// <summary>The sunburst on the account's colour, at one size.</summary>
    public static Bitmap Avatar(int size, int colorIndex)
    {
        var full = Recoloured(colorIndex);
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawImage(full, 0, 0, size, size);
        return bmp;
    }

    public static string IconFileFor(Account a)
    {
        Directory.CreateDirectory(Paths.IconDir);
        var index = ColorIndex(a);
        var file = Path.Combine(Paths.IconDir, $"{a.Id}-c{index}.ico");
        if (!File.Exists(file))
        {
            foreach (var stale in Directory.GetFiles(Paths.IconDir, $"{a.Id}-*.ico")) File.Delete(stale);
            WriteIco(file, s => Avatar(s, index));
        }
        return file;
    }

    /// <summary>A PNG of the account's avatar at one size, for the window's own use.</summary>
    public static string PngFileFor(Account a, int size) => SwatchPng(ColorIndex(a), size);

    public static string SwatchPng(int colorIndex, int size)
    {
        Directory.CreateDirectory(Paths.IconDir);
        var file = Path.Combine(Paths.IconDir, $"avatar-c{colorIndex}-{size}.png");
        if (!File.Exists(file)) { using var bmp = Avatar(size, colorIndex); bmp.Save(file, ImageFormat.Png); }
        return file;
    }

    /// <summary>
    /// Claude's icon at 256 px with its tile repainted. Each pixel is a blend of the
    /// tile colour and the sunburst's white, so it is re-blended between the new colour
    /// and white in the same proportion, which keeps the sunburst's smooth edges.
    /// </summary>
    static Bitmap Recoloured(int index)
    {
        if (recoloured.TryGetValue(index, out var cached)) return cached;
        var source = Base();
        var bmp = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.DrawImage(source, 0, 0, source.Width, source.Height);
        if (baseTile is not Color tile) return recoloured[index] = bmp;   // no Claude icon: leave as is

        var target = Palette[index].Color;
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var px = new byte[data.Stride * data.Height];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        var span = (255 - tile.R) + (255 - tile.G) + (255 - tile.B);
        for (var i = 0; i < px.Length; i += 4)
        {
            if (px[i + 3] == 0) continue;
            // BGRA. How far this pixel has moved from the tile colour towards white.
            var t = Math.Clamp(((px[i + 2] - tile.R) + (px[i + 1] - tile.G) + (px[i] - tile.B)) / (double)span, 0, 1);
            px[i + 2] = (byte)(target.R + (255 - target.R) * t);
            px[i + 1] = (byte)(target.G + (255 - target.G) * t);
            px[i] = (byte)(target.B + (255 - target.B) * t);
        }
        Marshal.Copy(px, 0, data.Scan0, px.Length);
        bmp.UnlockBits(data);
        return recoloured[index] = bmp;
    }

    /// <summary>
    /// ClauDeck's own mark: three ivory cards fanned in parallel on a clay tile. Drawn
    /// here rather than shipped as artwork, so every size is rendered sharp.
    /// </summary>
    public static Bitmap Logo(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        // Drawn on the 128-unit grid of branding/logo-concepts.html, concept A.
        g.ScaleTransform(size / 128f, size / 128f);
        using (var tile = Rounded(new RectangleF(6, 6, 116, 116), 28))
        using (var clay = new SolidBrush(Color.FromArgb(0xD9, 0x77, 0x57)))
            g.FillPath(clay, tile);
        g.TranslateTransform(64, 66);
        g.RotateTransform(-14);
        g.TranslateTransform(-64, -66);
        var alphas = new[] { 115, 184, 255 };
        for (var i = 0; i < 3; i++)
        {
            using var card = Rounded(new RectangleF(31 + 25 * i, 34, 16, 62), 8);
            using var ivory = new SolidBrush(Color.FromArgb(alphas[i], 0xFA, 0xF9, 0xF5));
            g.FillPath(ivory, card);
        }
        return bmp;
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

    public static void WriteAppIcon(string path) => WriteIco(path, Logo);

    public static string AppPng(int size)
    {
        Directory.CreateDirectory(Paths.IconDir);
        var file = Path.Combine(Paths.IconDir, $"claudeck-{size}.png");
        if (!File.Exists(file)) { using var bmp = Logo(size); bmp.Save(file, ImageFormat.Png); }
        return file;
    }

    static void WriteIco(string path, Func<int, Bitmap> draw)
    {
        var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 256 };
        var pngs = sizes.Select(s =>
        {
            using var bmp = draw(s);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }).ToList();
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            var dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
            w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
            w.Write((ushort)1); w.Write((ushort)32);
            w.Write((uint)pngs[i].Length); w.Write((uint)offset);
            offset += pngs[i].Length;
        }
        foreach (var png in pngs) w.Write(png);
    }

    static Bitmap Base()
    {
        if (baseBitmap != null) return baseBitmap;
        var exe = Store.Load().ClaudeExe;
        if (exe != null && File.Exists(exe))
        {
            var handles = new IntPtr[1];
            var ids = new uint[1];
            if (PrivateExtractIcons(exe, 0, 256, 256, handles, ids, 1, 0) >= 1 && handles[0] != IntPtr.Zero)
            {
                using var icon = Icon.FromHandle(handles[0]);
                baseBitmap = icon.ToBitmap();
                DestroyIcon(handles[0]);
                baseTile = TileColour(baseBitmap);
                return baseBitmap;
            }
        }
        // No Claude to borrow from: a plain tile with a white dot, recoloured like the rest.
        baseBitmap = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(baseBitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var tilePath = Rounded(new RectangleF(12, 12, 232, 232), 56);
            using var clay = new SolidBrush(Palette[0].Color);
            g.FillPath(clay, tilePath);
            g.FillEllipse(Brushes.White, 88, 88, 80, 80);
        }
        baseTile = Palette[0].Color;
        return baseBitmap;
    }

    /// <summary>The icon's tile colour: the commonest fully opaque colour that is not near white.</summary>
    static Color? TileColour(Bitmap bmp)
    {
        var counts = new Dictionary<int, int>();
        for (var y = 0; y < bmp.Height; y += 2)
            for (var x = 0; x < bmp.Width; x += 2)
            {
                var c = bmp.GetPixel(x, y);
                if (c.A < 250 || c.R + c.G + c.B > 690) continue;
                var key = (c.R & 0xF8) << 16 | (c.G & 0xF8) << 8 | (c.B & 0xF8);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        if (counts.Count == 0) return null;
        var best = counts.MaxBy(kv => kv.Value).Key;
        // Average the exact pixels in that bucket, so the blend maths uses the true colour.
        long r = 0, g = 0, b = 0, n = 0;
        for (var y = 0; y < bmp.Height; y += 2)
            for (var x = 0; x < bmp.Width; x += 2)
            {
                var c = bmp.GetPixel(x, y);
                if (c.A < 250 || ((c.R & 0xF8) << 16 | (c.G & 0xF8) << 8 | (c.B & 0xF8)) != best) continue;
                r += c.R; g += c.G; b += c.B; n++;
            }
        return Color.FromArgb((int)(r / n), (int)(g / n), (int)(b / n));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, uint[] ids, uint count, uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
}
