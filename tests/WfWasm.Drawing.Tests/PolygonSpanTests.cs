extern alias WfDraw;

using Bitmap = WfDraw::System.Drawing.Bitmap;
using Graphics = WfDraw::System.Drawing.Graphics;
using SolidBrush = WfDraw::System.Drawing.SolidBrush;
using TextureBrush = WfDraw::System.Drawing.TextureBrush;

public sealed class PolygonSpanTests
{
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(127, false, false)]
    [InlineData(255, false, false)]
    [InlineData(0, true, false)]
    [InlineData(127, true, false)]
    [InlineData(255, true, false)]
    [InlineData(127, false, true)]
    [InlineData(127, true, true)]
    public void Concave_polygon_preserves_blending_clip_holes_and_write_version(int alpha, bool copy, bool hole)
    {
        using var bitmap = new Bitmap(18, 14);
        using var g = Graphics.FromImage(bitmap);
        var background = Color.FromArgb(173, 31, 79, 117);
        var ink = Color.FromArgb(alpha, 211, 41, 97);
        g.Clear(background);
        g.SetClip(new Rectangle(2, 3, 8, 7));
        if (hole) g.ExcludeClip(new Rectangle(4, 4, 3, 3));
        g.CompositingMode = copy ? CompositingMode.SourceCopy : CompositingMode.SourceOver;
        int versionBefore = bitmap.PaintVersion;
        using var brush = new SolidBrush(ink);
        g.FillPolygon(brush, new PointF[] { new(-4, 2), new(11, 2), new(11, 6), new(5, 6), new(5, 12), new(-4, 12) });
        int writes = 0;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                bool painted = x >= 2 && x < 10 && y >= 3 && y < 10 && (y < 6 || x < 5)
                    && !(hole && x >= 4 && x < 7 && y >= 4 && y < 7);
                int expected = !painted ? background.ToArgb() : copy ? ink.ToArgb() : Over(background, ink);
                Assert.Equal(expected, bitmap.GetPixel(x, y).ToArgb());
                if (painted) writes++;
            }
        Assert.Equal(versionBefore + writes, bitmap.PaintVersion);
    }

    [Fact]
    public void Translated_polygon_and_texture_keep_device_sampling_and_clipping()
    {
        using var tile = new Bitmap(2, 2);
        tile.SetPixel(0, 0, Color.Red); tile.SetPixel(1, 0, Color.Blue);
        tile.SetPixel(0, 1, Color.Green); tile.SetPixel(1, 1, Color.White);
        using var bitmap = new Bitmap(12, 10);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Black);
        g.SetClip(new Rectangle(2, 2, 6, 5));
        g.TranslateTransform(3, 1);
        using var brush = new TextureBrush(tile);
        g.FillPolygon(brush, [new PointF(-2, 0), new(7, 0), new(7, 8), new(-2, 8)]);
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                var expected = x >= 2 && x < 8 && y >= 2 && y < 7 ? tile.GetPixel(x % 2, y % 2) : Color.Black;
                Assert.Equal(expected.ToArgb(), bitmap.GetPixel(x, y).ToArgb());
            }
    }

    private static int Over(Color background, Color ink)
    {
        int inv = 255 - ink.A;
        return Color.FromArgb(ink.A + background.A * inv / 255,
            (ink.R * ink.A + background.R * inv) / 255,
            (ink.G * ink.A + background.G * inv) / 255,
            (ink.B * ink.A + background.B * inv) / 255).ToArgb();
    }
}
