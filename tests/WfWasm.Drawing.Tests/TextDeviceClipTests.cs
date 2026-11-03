extern alias WfDraw;
using Bitmap = WfDraw::System.Drawing.Bitmap;
using Graphics = WfDraw::System.Drawing.Graphics;
using WfFont = WfDraw::System.Drawing.Font;
using SolidBrush = WfDraw::System.Drawing.SolidBrush;
using BrowserText = WfDraw::System.Drawing.BrowserText;

public sealed class TextDeviceClipTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Glyph_pixels_preserve_integer_fractional_and_hole_clips(bool fractional, bool hole)
    {
        byte[] rgba = new byte[12 * 10 * 4];
        for (int i = 0; i < 120; i++) rgba[i * 4 + 3] = (byte)(i % 3 == 0 ? 0 : i % 3 == 1 ? 127 : 255);
        BrowserText.RasterOverride = (_, _, _, _) => new BrowserText.Raster(12, 10, rgba);
        try
        {
            using var bitmap = new Bitmap(16, 14);
            using var g = Graphics.FromImage(bitmap);
            g.Clear(Color.White);
            float f = fractional ? 0.5f : 0f;
            var clip = new RectangleF(4 + f, 3 + f, 6, 7);
            g.SetClip(clip);
            if (hole) g.ExcludeClip(new Rectangle(6, 5, 2, 3));
            // Device clip precedes translation; the glyph begins off-surface.
            g.TranslateTransform(-2, 1);
            using var font = new WfFont("Arial", 10f);
            using var brush = new SolidBrush(Color.Black);
            int version = bitmap.PaintVersion;
            g.DrawString("clip", font, brush, new PointF(0, 0));
            bool any = false;
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                {
                    int rx = x + 2, ry = y - 1;
                    bool inRaster = rx >= 0 && rx < 12 && ry >= 0 && ry < 10;
                    bool inClip = clip.Contains(x, y) && !(hole && x >= 6 && x < 8 && y >= 5 && y < 8);
                    int alpha = inRaster && inClip ? rgba[(ry * 12 + rx) * 4 + 3] : 0;
                    int channel = 255 - alpha;
                    Assert.Equal(Color.FromArgb(255, channel, channel, channel).ToArgb(), bitmap.GetPixel(x, y).ToArgb());
                    any |= alpha != 0;
                }
            Assert.Equal(version + (any ? 1 : 0), bitmap.PaintVersion);
        }
        finally { BrowserText.RasterOverride = null; }
    }
}
