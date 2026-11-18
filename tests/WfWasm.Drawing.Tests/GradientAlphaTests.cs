extern alias WfDraw;

using System;
using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;
using WfColorBlend = WfDraw::System.Drawing.Drawing2D.ColorBlend;
using WfGradientBrush = WfDraw::System.Drawing.Drawing2D.LinearGradientBrush;
using WfGradientMode = WfDraw::System.Drawing.Drawing2D.LinearGradientMode;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfRectangleF = WfDraw::System.Drawing.RectangleF;
using Xunit;

public sealed class GradientAlphaTests
{
    private static WfBitmap Filled(WfColor background, Action<WfGradientBrush> configure)
    {
        var bmp = new WfBitmap(8, 8);
        using (var g = WfGraphics.FromImage(bmp))
        {
            g.Clear(background);
            using var brush = new WfGradientBrush(new WfRectangleF(0, 0, 8, 8),
                WfColor.FromArgb(0, 0, 0), WfColor.FromArgb(0, 0, 0), WfGradientMode.Horizontal);
            configure(brush);
            g.FillRectangle(brush, new WfRectangleF(0, 0, 8, 8));
        }

        return bmp;
    }

    [Fact]
    public void TransparentBlack_Gradient_Leaves_Background_Unchanged()
    {
        // G2: ARGB(0,0,0,0) stops are the standard "no color" sentinel in
        // theme fills; forcing alpha=255 turned them into opaque black
        // (titlebar glyph strip in the photo app).
        using var bmp = Filled(WfColor.FromArgb(255, 200, 30, 30), brush =>
        {
            brush.LinearColors = [WfColor.FromArgb(0, 0, 0, 0), WfColor.FromArgb(0, 0, 0, 0)];
        });
        Assert.Equal(WfColor.FromArgb(255, 200, 30, 30).ToArgb(), bmp.GetPixel(4, 4).ToArgb());
    }

    [Fact]
    public void HalfTransparent_Gradient_Blends_With_Averaged_Alpha()
    {
        // Stops transparent-red -> opaque-red: flat resolve is
        // A=128,R=255 (127.5 rounds up); over white: (255,255,127,127).
        using var bmp = Filled(WfColor.FromArgb(255, 255, 255, 255), brush =>
        {
            brush.LinearColors = [WfColor.FromArgb(0, 255, 0, 0), WfColor.FromArgb(255, 255, 0, 0)];
        });
        Assert.Equal(WfColor.FromArgb(255, 255, 127, 127).ToArgb(), bmp.GetPixel(4, 4).ToArgb());
    }

    // Allocation census (gauge-type repaint path): a repeated two-stop
    // gradient fill must not rebuild the uniform stop-position array
    // per call — positions are a pure function of the stop count.
    [Fact]
    public void TwoStop_GradientFill_StaysAllocationLean()
    {
        using var bmp = new WfBitmap(8, 8);
        using var g = WfGraphics.FromImage(bmp);
        using var brush = new WfGradientBrush(new WfRectangleF(0, 0, 8, 8),
            WfColor.FromArgb(255, 255, 0, 0), WfColor.FromArgb(255, 0, 0, 255), WfGradientMode.Horizontal);
        var rect = new WfRectangleF(0, 0, 8, 8);
        g.FillRectangle(brush, rect);
        GC.Collect(2, GCCollectionMode.Forced, true);
        long before = GC.GetAllocatedBytesForCurrentThread();
        const int n = 500;
        for (int i = 0; i < n; i++)
            g.FillRectangle(brush, rect);
        long perCall = (GC.GetAllocatedBytesForCurrentThread() - before) / n;
        Assert.True(perCall <= 80, $"two-stop gradient fill allocated {perCall} B/call");
    }

    [Fact]
    public void ThreeStop_InterpolationColors_Are_Honored()
    {
        // red@0 -> green@0.5 -> blue@1 trapezoid average is
        // (A=255, R=64, G=128, B=64); the old code averaged only the
        // two LinearColors and forced alpha opaque.
        using var bmp = Filled(WfColor.FromArgb(255, 255, 255, 255), brush =>
        {
            brush.InterpolationColors = new WfColorBlend
            {
                Colors = [WfColor.FromArgb(255, 255, 0, 0), WfColor.FromArgb(255, 0, 255, 0), WfColor.FromArgb(255, 0, 0, 255)],
                Positions = [0f, 0.5f, 1f],
            };
        });
        Assert.Equal(WfColor.FromArgb(255, 64, 128, 64).ToArgb(), bmp.GetPixel(4, 4).ToArgb());
    }
}
