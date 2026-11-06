extern alias WfDraw;

using WfDraw::System.Drawing;
using ShimFlags = WfWasm.Shim.TextFormatFlags;

// Wizard text wrapping: exact flag trace at the DrawString boundary.
// The fork maps WordBreak-without-SingleLine to a wrapping StringFormat,
// which must reach the rasterizer with maxW == layout width (wrap
// requested); SingleLine must request the unbounded 2048 row. The
// RasterOverride seam observes maxW directly (text, font, dpi, maxW),
// so this pins the flags-to-width contract with no browser, no fonts.
// FAIL while the boundary drops wrap (maxW 2048 on a WordBreak call).
public sealed class WrapLayoutProbeTests
{
    private const string Title = "Welcome to the Wizard demo application";

    [Fact]
    public void WordBreak_NarrowRect_RequestsLayoutWidth()
    {
        int seenMaxW = -1;
        BrowserText.RasterOverride = (text, font, dpi, maxW) =>
        {
            seenMaxW = maxW;
            return new BrowserText.Raster(600, 28, new byte[600 * 28 * 4]);
        };
        try
        {
            using var bitmap = new Bitmap(500, 120);
            using var graphics = Graphics.FromImage(bitmap);
            using var font = new Font("Verdana", 24f, FontStyle.Bold);
            var bounds = new Rectangle(0, 0, 376, 52);
            WfWasm.Shim.TextRenderer.DrawText(graphics, Title, font, bounds, Color.Black,
                ShimFlags.WordBreak | ShimFlags.TextBoxControl | ShimFlags.NoPrefix);
        }
        finally
        {
            BrowserText.RasterOverride = null;
        }
        Assert.Equal(376, seenMaxW);
    }

    [Fact]
    public void SingleLine_NarrowRect_RequestsUnboundedRow()
    {
        int seenMaxW = -1;
        BrowserText.RasterOverride = (text, font, dpi, maxW) =>
        {
            seenMaxW = maxW;
            return new BrowserText.Raster(600, 28, new byte[600 * 28 * 4]);
        };
        try
        {
            using var bitmap = new Bitmap(500, 120);
            using var graphics = Graphics.FromImage(bitmap);
            using var font = new Font("Verdana", 24f, FontStyle.Bold);
            var bounds = new Rectangle(0, 0, 376, 52);
            WfWasm.Shim.TextRenderer.DrawText(graphics, Title, font, bounds, Color.Black,
                ShimFlags.SingleLine | ShimFlags.NoPrefix);
        }
        finally
        {
            BrowserText.RasterOverride = null;
        }
        Assert.Equal(2048, seenMaxW);
    }
}
