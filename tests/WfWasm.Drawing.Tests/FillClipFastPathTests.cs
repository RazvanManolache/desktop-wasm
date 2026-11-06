extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;
using WfCombineMode = WfDraw::System.Drawing.Drawing2D.CombineMode;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfRectangleF = WfDraw::System.Drawing.RectangleF;
using WfRegion = WfDraw::System.Drawing.Region;
using WfSolidBrush = WfDraw::System.Drawing.SolidBrush;
using WfCounters = WfDraw::System.Drawing.TextRasterCounters;

// Rectangular clipped solid-fill fast path: a single integer device rect
// constrains the memset to the clip intersection — the exact set the
// per-pixel loop writes (same Contains test, same half-open bounds).
// Multi-rect unions and fractional rects keep the slow path; empty
// clips/intersections write and bump nothing.
public sealed class FillClipFastPathTests
{
    private static int[] Pixels(WfBitmap bmp)
    {
        var arr = new int[bmp.Width * bmp.Height];
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
                arr[y * bmp.Width + x] = bmp.GetPixel(x, y).ToArgb();
        return arr;
    }

    [Fact]
    public void Single_Rect_Clip_Fill_Matches_Intersection()
    {
        using var clipped = new WfBitmap(20, 20);
        using (var g = WfGraphics.FromImage(clipped))
        {
            g.Clear(WfColor.White);
            g.SetClip(new WfRectangleF(4, 4, 10, 10), WfCombineMode.Intersect);
            g.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(0, 0, 20, 20));
        }
        using var reference = new WfBitmap(20, 20);
        using (var g = WfGraphics.FromImage(reference))
        {
            g.Clear(WfColor.White);
            g.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(4, 4, 10, 10));
        }
        Assert.Equal(Pixels(reference), Pixels(clipped));
        Assert.Equal(reference.PaintVersion, clipped.PaintVersion);
        // Spot checks: inside red, outside white.
        Assert.Equal(WfColor.Red.ToArgb(), clipped.GetPixel(4, 4).ToArgb());
        Assert.Equal(WfColor.Red.ToArgb(), clipped.GetPixel(13, 13).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), clipped.GetPixel(3, 3).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), clipped.GetPixel(14, 14).ToArgb());
    }

    [Fact]
    public void Empty_Clip_Fill_Is_NoOp()
    {
        using var bmp = new WfBitmap(20, 20);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        int before = bmp.PaintVersion;
        g.ExcludeClip(new WfRectangleF(0, 0, 20, 20));
        g.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(0, 0, 20, 20));
        Assert.Equal(before, bmp.PaintVersion);
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(10, 10).ToArgb());
    }

    [Fact]
    public void Empty_Intersection_Returns_Without_Bump()
    {
        using var bmp = new WfBitmap(20, 20);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        int before = bmp.PaintVersion;
        g.SetClip(new WfRectangleF(0, 0, 5, 5), WfCombineMode.Intersect);
        g.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(10, 10, 5, 5));
        Assert.Equal(before, bmp.PaintVersion);
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(12, 12).ToArgb());
    }

    [Fact]
    public void Multi_Rect_Clip_Keeps_Slow_Path_Pixels()
    {
        using var bmp = new WfBitmap(20, 20);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        int before = bmp.PaintVersion;
        var union = new WfRegion(new WfRectangleF(0, 0, 6, 20));
        union.Union(new WfRectangleF(14, 0, 6, 20));
        g.Clip = union;
        g.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(0, 0, 20, 20));
        Assert.Equal(WfColor.Red.ToArgb(), bmp.GetPixel(0, 10).ToArgb());
        Assert.Equal(WfColor.Red.ToArgb(), bmp.GetPixel(19, 10).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(10, 10).ToArgb());
        // 6*20 + 6*20 written pixels bump the version by the same count
        // the per-pixel loop writes.
        Assert.Equal(before + 240, bmp.PaintVersion);
    }

    [Fact]
    public void Fractional_Rect_Clip_Pixel_Contract()
    {
        using var bmp = new WfBitmap(10, 10);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        g.SetClip(new WfRectangleF(2.5f, 2.5f, 5f, 5f), WfCombineMode.Intersect);
        g.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(0, 0, 10, 10));
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(2, 2).ToArgb());
        Assert.Equal(WfColor.Red.ToArgb(), bmp.GetPixel(3, 3).ToArgb());
        Assert.Equal(WfColor.Red.ToArgb(), bmp.GetPixel(7, 7).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(8, 8).ToArgb());
    }

    [Fact]
    public void Infinite_Clip_Behaves_As_Null()
    {
        using var clipped = new WfBitmap(20, 20);
        using (var g = WfGraphics.FromImage(clipped))
        {
            g.Clear(WfColor.White);
            g.Clip = new WfRegion();
            g.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(2, 2, 8, 8));
        }
        using var reference = new WfBitmap(20, 20);
        using (var g = WfGraphics.FromImage(reference))
        {
            g.Clear(WfColor.White);
            g.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(2, 2, 8, 8));
        }
        Assert.Equal(Pixels(reference), Pixels(clipped));
        Assert.Equal(reference.PaintVersion, clipped.PaintVersion);
    }

    [Fact]
    public void Translucent_Fill_Blends_Exact_Math()
    {
        // Semi-transparent SourceOver flat fill takes the BlendOverInt row
        // loop: ((255*R*129 + 255*126) / 255) per channel over white.
        using var bmp = new WfBitmap(20, 20);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        int before = bmp.PaintVersion;
        g.FillRectangle(new WfSolidBrush(WfColor.FromArgb(129, 255, 0, 0)),
            new WfRectangleF(0, 0, 20, 20));
        int r = (255 * 129 + 255 * 126) / 255;
        int gb = (0 * 129 + 255 * 126) / 255;
        int a = 129 + (255 * 126) / 255;
        int expected = (a << 24) | (r << 16) | (gb << 8) | gb;
        Assert.Equal(expected, bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(expected, bmp.GetPixel(19, 19).ToArgb());
        Assert.Equal(before + 400, bmp.PaintVersion);
    }

    [Fact]
    public void Translucent_Fill_Single_Rect_Clip_Matches_Intersection()
    {
        using var clipped = new WfBitmap(20, 20);
        using (var g = WfGraphics.FromImage(clipped))
        {
            g.Clear(WfColor.White);
            g.SetClip(new WfRectangleF(4, 4, 10, 10), WfCombineMode.Intersect);
            g.FillRectangle(new WfSolidBrush(WfColor.FromArgb(129, 255, 0, 0)),
                new WfRectangleF(0, 0, 20, 20));
        }
        using var reference = new WfBitmap(20, 20);
        using (var g = WfGraphics.FromImage(reference))
        {
            g.Clear(WfColor.White);
            g.FillRectangle(new WfSolidBrush(WfColor.FromArgb(129, 255, 0, 0)),
                new WfRectangleF(4, 4, 10, 10));
        }
        Assert.Equal(Pixels(reference), Pixels(clipped));
        Assert.Equal(reference.PaintVersion, clipped.PaintVersion);
        Assert.Equal(WfColor.White.ToArgb(), clipped.GetPixel(3, 3).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), clipped.GetPixel(14, 14).ToArgb());
    }

    [Fact]
    public void Translucent_Fill_Multi_Rect_Clip_Keeps_Slow_Path_Pixels()
    {
        using var bmp = new WfBitmap(20, 20);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        int before = bmp.PaintVersion;
        var union = new WfRegion(new WfRectangleF(0, 0, 6, 20));
        union.Union(new WfRectangleF(14, 0, 6, 20));
        g.Clip = union;
        g.FillRectangle(new WfSolidBrush(WfColor.FromArgb(129, 255, 0, 0)),
            new WfRectangleF(0, 0, 20, 20));
        int r = (255 * 129 + 255 * 126) / 255;
        int gb = (0 * 129 + 255 * 126) / 255;
        int a = 129 + (255 * 126) / 255;
        int expected = (a << 24) | (r << 16) | (gb << 8) | gb;
        Assert.Equal(expected, bmp.GetPixel(0, 10).ToArgb());
        Assert.Equal(expected, bmp.GetPixel(19, 10).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(10, 10).ToArgb());
        Assert.Equal(before + 240, bmp.PaintVersion);
    }

    [Fact]
    public void Translucent_SourceCopy_Still_Writes_Raw_Argb()
    {
        // SourceCopy is replace-with-self: alpha is written, not blended.
        using var bmp = new WfBitmap(10, 10);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        g.CompositingMode = WfDraw::System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        int before = bmp.PaintVersion;
        g.FillRectangle(new WfSolidBrush(WfColor.FromArgb(129, 255, 0, 0)),
            new WfRectangleF(0, 0, 10, 10));
        Assert.Equal(WfColor.FromArgb(129, 255, 0, 0).ToArgb(),
            bmp.GetPixel(5, 5).ToArgb());
        Assert.Equal(before + 100, bmp.PaintVersion);
    }

    [Fact]
    public void Skipped_Fills_Skip_Attribution()
    {
        // Attribution runs after the no-op skips: transparent and
        // clear-color fills (even >50000px) never capture a stack.
        bool saved = WfCounters.PerfEnabled;
        WfCounters.TakeBigFills(out long _, out long _, out string? _, out int _);
        try
        {
            WfCounters.PerfEnabled = true;
            using var bmp = new WfBitmap(300, 300);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            g.FillRectangle(new WfSolidBrush(WfColor.FromArgb(0, 255, 0, 0)), new WfRectangleF(0, 0, 300, 300));
            g.FillRectangle(new WfSolidBrush(WfColor.White), new WfRectangleF(0, 0, 300, 300));
            WfCounters.TakeBigFills(out long skippedN, out long _, out string? _, out int _);
            Assert.Equal(0, skippedN);
            g.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(0, 0, 300, 300));
            WfCounters.TakeBigFills(out long realN, out long _, out string? _, out int _);
            Assert.Equal(1, realN);
        }
        finally
        {
            WfCounters.PerfEnabled = saved;
            WfCounters.TakeBigFills(out long _, out long _, out string? _, out int _);
        }
    }

    [Fact]
    public void Multi_Rect_Disjoint_Bulk_Matches_Slow_Path()
    {
        // Two-disjoint-rect union takes the per-rect bulk row loops: exact
        // pixels and version of the per-pixel loop (6*20 + 6*20 writes).
        using var bulk = new WfBitmap(20, 20);
        using (var g = WfGraphics.FromImage(bulk))
        {
            g.Clear(WfColor.White);
            var union = new WfRegion(new WfRectangleF(0, 0, 6, 20));
            union.Union(new WfRectangleF(14, 0, 6, 20));
            g.Clip = union;
            g.FillRectangle(new WfSolidBrush(WfColor.FromArgb(129, 255, 0, 0)),
                new WfRectangleF(0, 0, 20, 20));
        }
        using var reference = new WfBitmap(20, 20);
        int expectedVersion;
        using (var g = WfGraphics.FromImage(reference))
        {
            g.Clear(WfColor.White);
            int before = reference.PaintVersion;
            g.Clip = new WfRegion(new WfRectangleF(0, 0, 6, 20));
            g.FillRectangle(new WfSolidBrush(WfColor.FromArgb(129, 255, 0, 0)),
                new WfRectangleF(0, 0, 20, 20));
            g.Clip = new WfRegion(new WfRectangleF(14, 0, 6, 20));
            g.FillRectangle(new WfSolidBrush(WfColor.FromArgb(129, 255, 0, 0)),
                new WfRectangleF(0, 0, 20, 20));
            expectedVersion = reference.PaintVersion;
            Assert.Equal(before + 240, expectedVersion);
        }
        Assert.Equal(Pixels(reference), Pixels(bulk));
        Assert.Equal(expectedVersion, bulk.PaintVersion);
        Assert.Equal(WfColor.White.ToArgb(), bulk.GetPixel(10, 10).ToArgb());
    }

    [Fact]
    public void Multi_Rect_Opaque_Bulk_Matches_Single_Rect_Fills()
    {
        using var bulk = new WfBitmap(20, 20);
        using (var g = WfGraphics.FromImage(bulk))
        {
            g.Clear(WfColor.White);
            var union = new WfRegion(new WfRectangleF(0, 0, 6, 20));
            union.Union(new WfRectangleF(14, 0, 6, 20));
            g.Clip = union;
            g.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(0, 0, 20, 20));
        }
        using var reference = new WfBitmap(20, 20);
        using (var g = WfGraphics.FromImage(reference))
        {
            g.Clear(WfColor.White);
            g.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(0, 0, 6, 20));
            int afterFirst = reference.PaintVersion;
            g.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(14, 0, 6, 20));
            Assert.Equal(afterFirst + 120, reference.PaintVersion);
        }
        Assert.Equal(Pixels(reference), Pixels(bulk));
        Assert.Equal(reference.PaintVersion, bulk.PaintVersion);
    }

    [Fact]
    public void Multi_Rect_Overlapping_Keeps_Exact_Slow_Path()
    {
        // Overlapping rects refuse the bulk path (a second blend would be
        // wrong for translucent fills): the per-pixel loop writes the union
        // once per pixel.
        using var bmp = new WfBitmap(20, 20);
        using (var g = WfGraphics.FromImage(bmp))
        {
            g.Clear(WfColor.White);
            var union = new WfRegion(new WfRectangleF(0, 0, 12, 20));
            union.Union(new WfRectangleF(8, 0, 12, 20));
            g.Clip = union;
            g.FillRectangle(new WfSolidBrush(WfColor.FromArgb(129, 255, 0, 0)),
                new WfRectangleF(0, 0, 20, 20));
        }
        using var reference = new WfBitmap(20, 20);
        using (var g = WfGraphics.FromImage(reference))
        {
            g.Clear(WfColor.White);
            g.FillRectangle(new WfSolidBrush(WfColor.FromArgb(129, 255, 0, 0)),
                new WfRectangleF(0, 0, 12, 20));
            g.FillRectangle(new WfSolidBrush(WfColor.FromArgb(129, 255, 0, 0)),
                new WfRectangleF(8, 0, 12, 20));
        }
        // Reference double-blends the 4x20 overlap; the clipped fill blends
        // every union pixel exactly once.
        Assert.NotEqual(
            reference.GetPixel(10, 10).ToArgb(),
            bmp.GetPixel(10, 10).ToArgb());
        int r = (255 * 129 + 255 * 126) / 255;
        int gb = (0 * 129 + 255 * 126) / 255;
        int a = 129 + (255 * 126) / 255;
        Assert.Equal((a << 24) | (r << 16) | (gb << 8) | gb, bmp.GetPixel(10, 10).ToArgb());
    }
}
