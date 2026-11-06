// Scoped MeasureCharacterRanges: HTML elements (BaseElement::MeasureString,
// SelectionManager::CalculateRegion) set measurable ranges then read per-range
// regions. Pins 2-range shape + charsFitted through the public core with the
// headless RasterOverride seam (no browser, no text milestone).
extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfBrowserText = WfDraw::System.Drawing.BrowserText;
using WfCharacterRange = WfDraw::System.Drawing.CharacterRange;
using WfColor = WfDraw::System.Drawing.Color;
using WfFont = WfDraw::System.Drawing.Font;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfRectangleF = WfDraw::System.Drawing.RectangleF;
using WfStringFormat = WfDraw::System.Drawing.StringFormat;

namespace WfWasm.Drawing.Tests;

public sealed class MeasureCharacterRangesTests
{
    private static WfBrowserText.Raster RasterFor(string text)
    {
        int w = Math.Max(1, 10 * text.Length);
        byte[] rgba = new byte[w * 8 * 4];
        for (int i = 3; i < rgba.Length; i += 4)
            rgba[i] = 255;
        return new WfBrowserText.Raster(w, 8, rgba);
    }

    [Fact]
    public void MeasureCharacterRanges_TwoRanges_ReturnsOrderedRegions()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(200, 60);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat();
            format.SetMeasurableCharacterRanges(new[]
            {
                new WfCharacterRange(0, 5),
                new WfCharacterRange(5, 5),
            });
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "HelloWorld", font, new WfRectangleF(10, 4, 180, 40), format, out int fitted);
            Assert.Equal(2, regions.Length);
            WfRectangleF b0 = regions[0].GetBounds(g);
            WfRectangleF b1 = regions[1].GetBounds(g);
            Assert.Equal(10f, b0.X);
            Assert.True(b1.X > b0.X);
            Assert.True(b0.Width > 0f);
            Assert.True(b1.Width > 0f);
            Assert.Equal(10, fitted);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void MeasureCharacterRanges_NoRanges_ReturnsEmpty()
    {
        using var bmp = new WfBitmap(200, 60);
        using var g = WfGraphics.FromImage(bmp);
        using var font = new WfFont("Segoe UI", 12f);
        WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
            "HelloWorld", font, new WfRectangleF(10, 4, 180, 40), null, out int fitted);
        Assert.Empty(regions);
        Assert.Equal(0, fitted);
    }

    [Fact]
    public void RangesRespectVerticalAlignmentAndLayoutClip()
    {
        using var bmp = new WfBitmap(200, 80);
        using var g = WfGraphics.FromImage(bmp);
        using var font = new WfFont("Segoe UI", 12f);
        using var format = new WfStringFormat { LineAlignment = WfDraw::System.Drawing.StringAlignment.Far };
        format.SetMeasurableCharacterRanges([new WfCharacterRange(0, 2)]);
        var rect = new WfRectangleF(10, 4, 180, 60);
        var bounds = g.MeasureCharacterRanges("AB", font, rect, format)[0].GetBounds(g);
        Assert.True(bounds.Y > rect.Y);
        Assert.Equal(rect.Bottom, bounds.Bottom);
        var clipped = g.MeasureCharacterRanges("AB", font, new WfRectangleF(10, 4, 180, 2), format)[0].GetBounds(g);
        Assert.Equal(2f, clipped.Height);
    }

    [Fact]
    public void RangeWidthRetainsPrefixShapingContext()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) =>
        {
            int width = text == "AV" ? 15 : 10;
            var rgba = new byte[width * 8 * 4];
            for (int i = 3; i < rgba.Length; i += 4) rgba[i] = 255;
            return new WfBrowserText.Raster(width, 8, rgba);
        };
        try
        {
            using var bmp = new WfBitmap(200, 60);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat();
            format.SetMeasurableCharacterRanges([new WfCharacterRange(1, 1)]);
            var bounds = g.MeasureCharacterRanges("AV", font, new WfRectangleF(0, 0, 100, 40), format)[0].GetBounds(g);
            Assert.Equal(10f, bounds.X);
            Assert.Equal(5f, bounds.Width);
        }
        finally { WfBrowserText.RasterOverride = null; }
    }

    [Fact]
    public void NullFontReportsArgumentErrorInsteadOfLosingTheRangeCount()
    {
        using var bmp = new WfBitmap(20, 20);
        using var g = WfGraphics.FromImage(bmp);
        using var format = new WfStringFormat();
        format.SetMeasurableCharacterRanges([new WfCharacterRange(0, 1)]);
        Assert.Throws<ArgumentNullException>(() => g.MeasureCharacterRanges("A", null!, new WfRectangleF(0, 0, 20, 20), format));
    }
}
