extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfBrush = WfDraw::System.Drawing.Brush;
using WfColor = WfDraw::System.Drawing.Color;
using WfFont = WfDraw::System.Drawing.Font;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfPen = WfDraw::System.Drawing.Pen;
using WfRectangleF = WfDraw::System.Drawing.RectangleF;
using WfBrowserText = WfDraw::System.Drawing.BrowserText;
using WfSolidBrush = WfDraw::System.Drawing.SolidBrush;

namespace WfWasm.Drawing.Tests;

// Desktop parity: new Pen(Color).Brush is a live SolidBrush of that color,
// never null (BCL always materializes it). Syncfusion SfSmithChart
// DrawChartTitle paints via new Pen(ForeColor).Brush; a null Brush threw
// ArgumentNullException("brush") out of DrawString and blanked the whole
// chart surface while the legend (separate path) painted.
public sealed class PenBrushTests
{
    [Fact]
    public void ColorPen_Brush_IsSolidBrushOfPenColor()
    {
        using var pen = new WfPen(WfColor.Red);
        var brush = Assert.IsType<WfSolidBrush>(pen.Brush);
        Assert.Equal(WfColor.Red.ToArgb(), brush.Color.ToArgb());
        Assert.Equal(WfColor.Red.ToArgb(), pen.Color.ToArgb());
    }

    [Fact]
    public void ColorPen_WithWidth_Brush_IsSolidBrushOfPenColor()
    {
        using var pen = new WfPen(WfColor.Blue, 2f);
        var brush = Assert.IsType<WfSolidBrush>(pen.Brush);
        Assert.Equal(WfColor.Blue.ToArgb(), brush.Color.ToArgb());
    }

    [Fact]
    public void BrushPen_KeepsSuppliedBrush()
    {
        var supplied = new WfSolidBrush(WfColor.Green);
        using var pen = new WfPen(supplied);
        Assert.Same(supplied, pen.Brush);
    }

    [Fact]
    public void ColorSetter_RefreshesBrush()
    {
        using var pen = new WfPen(WfColor.Red);
        pen.Color = WfColor.Blue;
        var brush = Assert.IsType<WfSolidBrush>(pen.Brush);
        Assert.Equal(WfColor.Blue.ToArgb(), brush.Color.ToArgb());
    }

    [Fact]
    public void DrawString_Accepts_ColorPen_Brush_WithoutThrow()
    {
        // DrawChartTitle shape: new Pen(ForeColor).Brush straight into
        // DrawString. Pre-fix this threw ArgumentNullException("brush").
        WfBrowserText.RasterOverride = (text, font, dpi, maxW) =>
            new WfBrowserText.Raster(20, 10, WhiteOpaque(20, 10));
        try
        {
            using var bmp = new WfBitmap(120, 120);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Arial", 12f);
            using var pen = new WfPen(WfColor.Black);
            WfBrush brush = pen.Brush!;
            g.DrawString("T", font, brush, new WfRectangleF(0, 0, 200, 50));
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    private static byte[] WhiteOpaque(int w, int h)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4)
        {
            px[i] = 255;
            px[i + 1] = 255;
            px[i + 2] = 255;
            px[i + 3] = 255;
        }
        return px;
    }
}
