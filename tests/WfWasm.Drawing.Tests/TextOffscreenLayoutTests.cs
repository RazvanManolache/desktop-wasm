extern alias WfDraw;
using Bitmap = WfDraw::System.Drawing.Bitmap;
using Graphics = WfDraw::System.Drawing.Graphics;
using BrowserText = WfDraw::System.Drawing.BrowserText;
using WfFont = WfDraw::System.Drawing.Font;
using Brush = WfDraw::System.Drawing.SolidBrush;
using Format = WfDraw::System.Drawing.StringFormat;

public sealed class TextOffscreenLayoutTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Offscreen_clipped_layout_skips_rasterization_without_writes(bool deviceClip)
    {
        int calls = 0;
        BrowserText.RasterOverride = (_, _, _, _) => { calls++; return Raster(); };
        try
        {
            using var bitmap = new Bitmap(40, 20);
            using var g = Graphics.FromImage(bitmap);
            if (deviceClip) g.SetClip(new Rectangle(0, 0, 10, 20));
            using var font = new WfFont("Arial", 10);
            using var brush = new Brush(Color.Black);
            int version = bitmap.PaintVersion;
            g.DrawString("offscreen", font, brush, new RectangleF(deviceClip ? 20 : 100, 0, 10, 10));
            Assert.Equal(0, calls);
            Assert.Equal(version, bitmap.PaintVersion);
        }
        finally { BrowserText.RasterOverride = null; }
    }

    [Fact]
    public void NoClip_aligned_glyphs_can_extend_from_an_offscreen_layout_into_the_target()
    {
        int calls = 0;
        BrowserText.RasterOverride = (_, _, _, _) => { calls++; return Raster(); };
        try
        {
            using var bitmap = new Bitmap(40, 20);
            using var g = Graphics.FromImage(bitmap);
            using var font = new WfFont("Arial", 10);
            using var brush = new Brush(Color.Red);
            using var format = new Format(StringFormatFlags.NoClip) { Alignment = StringAlignment.Far };
            g.DrawString("visible", font, brush, new RectangleF(100, 0, 2, 8), format);
            Assert.True(calls > 0);
            Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        }
        finally { BrowserText.RasterOverride = null; }
    }

    private static BrowserText.Raster Raster()
    {
        var bytes = new byte[200 * 8 * 4];
        for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
        return new BrowserText.Raster(200, 8, bytes);
    }
}
