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

    // Native oracle (Windows GDI+, oracle-mcr.ps1): "AB\nCD" reports line 2
    // at Y+lineH with the same left origin; a range spanning the break
    // unions both line fragments into one region.
    [Fact]
    public void Multiline_TwoLines_StackAtLinePitch()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(200, 120);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat();
            format.SetMeasurableCharacterRanges(new[]
            {
                new WfCharacterRange(0, 2),
                new WfCharacterRange(3, 2),
            });
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "AB\nCD", font, new WfRectangleF(10, 4, 180, 80), format, out int fitted);
            Assert.Equal(2, regions.Length);
            WfRectangleF b0 = regions[0].GetBounds(g);
            WfRectangleF b1 = regions[1].GetBounds(g);
            Assert.Equal(10f, b0.X);
            Assert.Equal(10f, b1.X);
            Assert.True(b1.Y > b0.Y);
            Assert.True(Math.Abs((b1.Y - b0.Y) - b0.Height) < 0.01);
            Assert.Equal(5, fitted);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void Multiline_RangeSpanningBreak_UnionsBothLines()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(200, 120);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat();
            format.SetMeasurableCharacterRanges([new WfCharacterRange(0, 5)]);
            var rect = new WfRectangleF(10, 4, 180, 80);
            WfRectangleF span = g.MeasureCharacterRanges("AB\nCD", font, rect, format)[0].GetBounds(g);
            using var single = new WfStringFormat();
            single.SetMeasurableCharacterRanges([new WfCharacterRange(0, 2)]);
            WfRectangleF one = g.MeasureCharacterRanges("AB", font, rect, single)[0].GetBounds(g);
            Assert.Equal(rect.X, span.X);
            Assert.Equal(one.Y, span.Y);
            Assert.True(Math.Abs(span.Height - 2 * one.Height) < 0.01);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void Multiline_CrlfMatchesLf()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(200, 120);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var lf = new WfStringFormat();
            lf.SetMeasurableCharacterRanges(new[]
            {
                new WfCharacterRange(0, 2),
                new WfCharacterRange(3, 2),
            });
            using var crlf = new WfStringFormat();
            crlf.SetMeasurableCharacterRanges(new[]
            {
                new WfCharacterRange(0, 2),
                new WfCharacterRange(4, 2),
            });
            var rect = new WfRectangleF(10, 4, 180, 80);
            WfDraw::System.Drawing.Region[] a = g.MeasureCharacterRanges("AB\nCD", font, rect, lf);
            WfDraw::System.Drawing.Region[] b = g.MeasureCharacterRanges("AB\r\nCD", font, rect, crlf);
            Assert.Equal(a[0].GetBounds(g), b[0].GetBounds(g));
            Assert.Equal(a[1].GetBounds(g), b[1].GetBounds(g));
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    // Native oracle centers each line on its own width (center-multiline case:
    // distinct X per line), not the block on its widest line.
    [Fact]
    public void Multiline_CenteredLines_AlignPerLine()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(400, 120);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat { Alignment = WfDraw::System.Drawing.StringAlignment.Center };
            format.SetMeasurableCharacterRanges(new[]
            {
                new WfCharacterRange(0, 1),
                new WfCharacterRange(2, 2),
            });
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "A\nCD", font, new WfRectangleF(10, 4, 180, 80), format);
            WfRectangleF b0 = regions[0].GetBounds(g);
            WfRectangleF b1 = regions[1].GetBounds(g);
            Assert.True(b0.X > b1.X);
            Assert.Equal(10f + (180f - 10f) / 2f, b0.X);
            Assert.Equal(10f + (180f - 20f) / 2f, b1.X);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void Fitted_StopsWhenNoLineBoxIsFullyVisible()
    {
        using var bmp = new WfBitmap(200, 120);
        using var g = WfGraphics.FromImage(bmp);
        using var font = new WfFont("Segoe UI", 12f);
        using var format = new WfStringFormat();
        format.SetMeasurableCharacterRanges([new WfCharacterRange(0, 5)]);
        // Single line taller than the rect: no line box is fully visible, but the
        // fragment still reports clipped (native oracle fitted-overflow case).
        WfDraw::System.Drawing.Region[] single = g.MeasureCharacterRanges(
            "Hello", font, new WfRectangleF(10, 4, 180, 2), format, out int singleFitted);
        Assert.Equal(0, singleFitted);
        Assert.Equal(2f, single[0].GetBounds(g).Height);
        // Two lines, room for exactly one: the visible line plus its consumed
        // break count as fitted.
        WfDraw::System.Drawing.Region[] two = g.MeasureCharacterRanges(
            "AB\nCD", font, new WfRectangleF(10, 4, 180, 30), format, out int twoFitted);
        Assert.Equal(3, twoFitted);
    }

    // Allocation regression (RTE repaint path): per-call managed bytes for a
    // 2-range call must stay under the bound. Original baselines were 856
    // (single) / 864 (multiline); the stack-span work cut those to 576/488,
    // and the wrap-row line-width measures cost one extra measure per line
    // (608/680 now). The override below mirrors production's raster cache
    // (same instance per text) so the count reflects product code, not the
    // seam. GC.GetAllocatedBytesForCurrentThread is exact per thread; one
    // warm-up call excludes JIT.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, WfBrowserText.Raster> s_cachedRasters = new();

    private static long BytesPerCall(System.Action act, int n)
    {
        act();
        GC.Collect(2, GCCollectionMode.Forced, true);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < n; i++)
            act();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / n;
    }

    [Fact]
    public void RangeCalls_StayAllocationLean()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) =>
            s_cachedRasters.GetOrAdd(text, RasterFor);
        try
        {
            using var bmp = new WfBitmap(200, 120);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat();
            format.SetMeasurableCharacterRanges(new[]
            {
                new WfCharacterRange(0, 5),
                new WfCharacterRange(5, 5),
            });
            var rect = new WfRectangleF(10, 4, 180, 80);
            long single = BytesPerCall(() =>
                g.MeasureCharacterRanges("HelloWorld", font, rect, format, out _), 500);
            long multi = BytesPerCall(() =>
                g.MeasureCharacterRanges("AB\nCD", font, rect, format, out _), 500);
            Assert.True(single <= 680, $"single-line MCR allocated {single} B/call");
            Assert.True(multi <= 760, $"multiline MCR allocated {multi} B/call");
            // Tabbed spans build one tab-free copy via string.Create
            // instead of Substring+Replace; tabbed text stays inside the
            // shared multi bound (diet verified: was ~790 B/call before).
            long tabbed = BytesPerCall(() =>
                g.MeasureCharacterRanges("AA\tBB\tCC", font, rect, format, out _), 500);
            Assert.True(tabbed <= 760, $"tabbed MCR allocated {tabbed} B/call");
            using var flagFormat = new WfStringFormat { FormatFlags = WfDraw::System.Drawing.StringFormatFlags.MeasureTrailingSpaces };
            flagFormat.SetMeasurableCharacterRanges(new[]
            {
                new WfCharacterRange(0, 7),
            });
            long trailing = BytesPerCall(() =>
                g.MeasureCharacterRanges("AA BB  ", font, rect, flagFormat, out _), 500);
            Assert.True(trailing <= 760, $"trailing-space MCR allocated {trailing} B/call");
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    // Native oracle (oracle-wrap.ps1): 'AA BB CC DD EE FF' in a 60px rect
    // wraps into 3 rows; a range spanning rows unions one fragment per row.
    // 10px/char seam reproduces the native break indices (0-5, 6-11, 12-16).
    [Fact]
    public void Wrap_SplitsIntoStackedRows()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(200, 220);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat();
            var rect = new WfRectangleF(10, 4, 60, 190);
            format.SetMeasurableCharacterRanges(new[]
            {
                new WfCharacterRange(0, 5),
                new WfCharacterRange(6, 5),
                new WfCharacterRange(12, 5),
            });
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "AA BB CC DD EE FF", font, rect, format, out int fitted);
            Assert.Equal(3, regions.Length);
            WfRectangleF b0 = regions[0].GetBounds(g);
            WfRectangleF b1 = regions[1].GetBounds(g);
            WfRectangleF b2 = regions[2].GetBounds(g);
            Assert.Equal(10f, b0.X);
            Assert.Equal(10f, b1.X);
            Assert.Equal(10f, b2.X);
            Assert.True(b1.Y > b0.Y);
            Assert.True(Math.Abs((b1.Y - b0.Y) - b0.Height) < 0.01);
            Assert.True(Math.Abs((b2.Y - b1.Y) - b1.Height) < 0.01);
            Assert.Equal(17, fitted);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void Wrap_SpanningRangeUnionsOneFragmentPerRow()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(200, 220);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat();
            format.SetMeasurableCharacterRanges([new WfCharacterRange(0, 8)]);
            var rect = new WfRectangleF(10, 4, 60, 190);
            WfRectangleF span = g.MeasureCharacterRanges("AA BB CC DD EE FF", font, rect, format)[0].GetBounds(g);
            using var first = new WfStringFormat();
            first.SetMeasurableCharacterRanges([new WfCharacterRange(0, 5)]);
            WfRectangleF one = g.MeasureCharacterRanges("AA BB CC DD EE FF", font, rect, first)[0].GetBounds(g);
            Assert.Equal(rect.X, span.X);
            Assert.Equal(one.Y, span.Y);
            Assert.True(Math.Abs(span.Height - 2 * one.Height) < 0.01);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    // Native oracle (oracle-wrap10.ps1 + oracle-wrap11.ps1): '%' breaks
    // after itself like hyphen/space (4 single-token rows for
    // 'AAA%BBB%CCC%DDD' in 60px); ':' and '/' do NOT follow the
    // break-after model ('AAA:BBB:CCC:DDD' and 'AAA/BBB/CCC/DDD' both
    // give 3 rows, matching plain character wrap). The 10px/char seam
    // needs the 4-token texts to discriminate (2-token rows fit the
    // seam but not Segoe, so short texts cannot pin this).
    [Fact]
    public void Wrap_PercentBreaksAfterSeparator()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(200, 220);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat();
            format.SetMeasurableCharacterRanges([new WfCharacterRange(0, 15)]);
            var rect = new WfRectangleF(10, 4, 60, 190);
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "AAA%BBB%CCC%DDD", font, rect, format, out int fitted);
            Assert.Equal(15, fitted);
            Assert.Single(regions);
            Assert.Equal(4, regions[0].GetRegionScans(new WfDraw::System.Drawing.Drawing2D.Matrix()).Length);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
    [Fact]
    public void Wrap_ColonAndSlashDoNotFollowSeparatorBreaks()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(200, 220);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            var rect = new WfRectangleF(10, 4, 60, 190);
            foreach (string text in new[] { "AAA:BBB:CCC:DDD", "AAA/BBB/CCC/DDD" })
            {
                using var format = new WfStringFormat();
                format.SetMeasurableCharacterRanges([new WfCharacterRange(0, 15)]);
                WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                    text, font, rect, format, out int fitted);
                Assert.Equal(15, fitted);
                Assert.Single(regions);
                Assert.Equal(3, regions[0].GetRegionScans(new WfDraw::System.Drawing.Drawing2D.Matrix()).Length);
            }
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
    // Native oracle (oracle-wrap13.ps1): a mid-row tab advances exactly
    // 0 in MCR region geometry at every prefix length (1, 2, 3, 5, 8
    // chars: post-tab X == prefix right edge), and a tab-only range is
    // empty. No tab stops exist to model; the engine must give tabs zero
    // width in region geometry (the test seam measures every char 10px,
    // so an unhandled tab shows up as a 10px shift).
    [Fact]
    public void Wrap_MidRowTabMeasuresZero()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(300, 220);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat();
            format.SetMeasurableCharacterRanges(new[]
            {
                new WfCharacterRange(0, 2),
                new WfCharacterRange(3, 1),
            });
            var rect = new WfRectangleF(10, 4, 200, 190);
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "AA\tB", font, rect, format, out int fitted);
            Assert.Equal(4, fitted);
            WfRectangleF pre = regions[0].GetBounds(g);
            WfRectangleF post = regions[1].GetBounds(g);
            Assert.Equal(10f, pre.X);
            Assert.Equal(20f, pre.Width);
            Assert.Equal(pre.Right, post.X);
            Assert.Equal(10f, post.Width);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
    [Fact]
    public void Wrap_TabOnlyRangeIsEmpty()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(300, 220);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat();
            format.SetMeasurableCharacterRanges([new WfCharacterRange(2, 1)]);
            var rect = new WfRectangleF(10, 4, 200, 190);
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "AA\tBB", font, rect, format, out int fitted);
            Assert.Equal(5, fitted);
            Assert.Single(regions);
            Assert.True(regions[0].IsEmpty(g));
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
    // Native oracle (oracle-wrap14.ps1 + wrap15 scaling sweep 8/12/16/24):
    // WITHOUT the flag trailing spaces measure zero (full range == bare
    // text); WITH MeasureTrailingSpaces each trailing space adds ~one
    // space advance (exact at 8/16/24, within 1px at hinted size 12).
    // Mid-range geometry is unaffected. The test seam measures every char
    // 10px, so one trailing space must add exactly 10px with the flag.
    [Fact]
    public void Wrap_TrailingSpacesCountWithFlag()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(300, 220);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat { FormatFlags = WfDraw::System.Drawing.StringFormatFlags.MeasureTrailingSpaces };
            format.SetMeasurableCharacterRanges([new WfCharacterRange(0, 6)]);
            var rect = new WfRectangleF(10, 4, 200, 190);
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "AA BB ", font, rect, format, out int fitted);
            Assert.Equal(6, fitted);
            Assert.Single(regions);
            WfRectangleF b = regions[0].GetBounds(g);
            Assert.Equal(10f, b.X);
            Assert.Equal(60f, b.Width);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
    [Fact]
    public void Wrap_TrailingSpacesDroppedByDefault()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(300, 220);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat();
            format.SetMeasurableCharacterRanges([new WfCharacterRange(0, 6)]);
            var rect = new WfRectangleF(10, 4, 200, 190);
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "AA BB ", font, rect, format, out int fitted);
            Assert.Equal(6, fitted);
            Assert.Single(regions);
            WfRectangleF b = regions[0].GetBounds(g);
            Assert.Equal(10f, b.X);
            Assert.Equal(50f, b.Width);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
    [Fact]
    public void Wrap_TrailingSpacesScalePerSpaceWithFlag()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(300, 220);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat { FormatFlags = WfDraw::System.Drawing.StringFormatFlags.MeasureTrailingSpaces };
            format.SetMeasurableCharacterRanges([new WfCharacterRange(0, 7)]);
            var rect = new WfRectangleF(10, 4, 200, 190);
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "AA BB  ", font, rect, format, out int fitted);
            Assert.Equal(7, fitted);
            Assert.Single(regions);
            WfRectangleF b = regions[0].GetBounds(g);
            Assert.Equal(10f, b.X);
            Assert.Equal(70f, b.Width);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
    // Native oracle (oracle-wrap20.ps1): all-whitespace text WITH
    // MeasureTrailingSpaces measures every space ('   ' -> 18px at
    // 16pt, '  ' -> 19px at 24pt), while the no-flag case stays empty.
    // The 10px/char seam needs exactly 3 advances for '   '.
    [Fact]
    public void Wrap_AllWhitespaceCountsSpacesWithFlag()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(300, 220);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat { FormatFlags = WfDraw::System.Drawing.StringFormatFlags.MeasureTrailingSpaces };
            format.SetMeasurableCharacterRanges([new WfCharacterRange(0, 3)]);
            var rect = new WfRectangleF(10, 4, 200, 190);
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "   ", font, rect, format, out int fitted);
            Assert.Equal(3, fitted);
            Assert.Single(regions);
            WfRectangleF b = regions[0].GetBounds(g);
            Assert.Equal(10f, b.X);
            Assert.Equal(30f, b.Width);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
    // Native oracle (oracle-wrap20.ps1 sliver re-probe): a range covering
    // only trailing spaces measures with the flag ('AB  ' (2,2) ->
    // 11px at 16pt, x at end of AB ink); without the flag it stays
    // empty. The seam needs 2 advances at x = 10 + ink('AB').
    [Fact]
    public void Wrap_TrailingOnlySliverCountsWithFlag()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(300, 220);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat { FormatFlags = WfDraw::System.Drawing.StringFormatFlags.MeasureTrailingSpaces };
            format.SetMeasurableCharacterRanges([new WfCharacterRange(2, 2)]);
            var rect = new WfRectangleF(10, 4, 200, 190);
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "AB  ", font, rect, format, out int fitted);
            Assert.Equal(4, fitted);
            Assert.Single(regions);
            WfRectangleF b = regions[0].GetBounds(g);
            Assert.Equal(30f, b.X);
            Assert.Equal(20f, b.Width);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
    // Native oracle (oracle-wrap20.ps1 wrap x flag): wrapped row
    // fragments keep their row-trailing space with the flag ('AA BB CC'
    // rows 28->34 and 24->30 at 16pt; last row without trailing space
    // unchanged). The seam lays out 'AA BB' in a 35px rect as
    // 'AA '/'BB', so row 0 must keep one 10px advance with the flag
    // and drop it without.
    [Fact]
    public void Wrap_WrappedRowsKeepRowSpaceWithFlag()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(300, 220);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var flagged = new WfStringFormat { FormatFlags = WfDraw::System.Drawing.StringFormatFlags.MeasureTrailingSpaces };
            flagged.SetMeasurableCharacterRanges([new WfCharacterRange(0, 5)]);
            using var plain = new WfStringFormat();
            plain.SetMeasurableCharacterRanges([new WfCharacterRange(0, 5)]);
            var rect = new WfRectangleF(10, 4, 35, 190);
            var m = new WfDraw::System.Drawing.Drawing2D.Matrix();
            WfRectangleF[] kept =
                g.MeasureCharacterRanges("AA BB", font, rect, flagged, out _)[0].GetRegionScans(m);
            WfRectangleF[] dropped =
                g.MeasureCharacterRanges("AA BB", font, rect, plain, out _)[0].GetRegionScans(m);
            Assert.Equal(2, kept.Length);
            Assert.Equal(2, dropped.Length);
            Assert.Equal(10f, kept[0].X);
            Assert.Equal(30f, kept[0].Width);
            Assert.Equal(20f, dropped[0].Width);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
    // Native oracle (oracle-wrap20.ps1): a leading tab measures 1px
    // narrower than the bare text ('\tAB' 26 vs 'AB' 27 at 16pt) —
    // glyph positioning at a tab boundary, below the tight-core seam
    // resolution. The engine deliberately models tabs as zero-width
    // (documented deviation); this pins that the lead tab adds nothing.
    [Fact]
    public void Wrap_LeadingTabMatchesBareText()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(300, 220);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat();
            format.SetMeasurableCharacterRanges([new WfCharacterRange(0, 3)]);
            var rect = new WfRectangleF(10, 4, 200, 190);
            WfRectangleF led = g.MeasureCharacterRanges(
                "\tAB", font, rect, format, out _)[0].GetBounds(g);
            WfRectangleF bare = g.MeasureCharacterRanges(
                "AB", font, rect, format, out _)[0].GetBounds(g);
            Assert.Equal(bare.Width, led.Width);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
    // Native oracles wrap5/wrap16/wrap17/wrap18: the wrap5 "empty region"
    // anomaly and the wrap16/17 throws are the long-documented
    // out-of-range rule (range length past text end throws "Parameter is
    // not valid" — 'AABB\tCCDD' is 9 chars, those ranges asked for 10+),
    // not a tab quirk: exact-range tab text lays out 2 rows with the tab
    // row-terminating at zero width, and a tab-only range is empty. The
    // engine deliberately stays fail-soft on the over-long range
    // (documented out-of-range-clamp deviation) and reproduces the exact-
    // range layout.
    [Fact]
    public void Wrap_TabOnlyBreakStaysFailSoft()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(300, 220);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat();
            format.SetMeasurableCharacterRanges([new WfCharacterRange(0, 10)]);
            var rect = new WfRectangleF(10, 4, 60, 190);
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "AABB\tCCDD", font, rect, format, out int fitted);
            Assert.Equal(9, fitted);
            Assert.Single(regions);
            Assert.Equal(2, regions[0].GetRegionScans(new WfDraw::System.Drawing.Drawing2D.Matrix()).Length);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
    // Native oracle wrap16: mixed-class text 'AA BB-CC%DD\tEE FF'
    // lays out 4 rows in 60px. The 10px/char seam reproduces the same
    // per-class rules, so the engine must agree row-for-row.
    [Fact]
    public void Wrap_MixedBreakClassesMatchNativeRows()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(300, 220);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat();
            format.SetMeasurableCharacterRanges([new WfCharacterRange(0, 17)]);
            var rect = new WfRectangleF(10, 4, 60, 190);
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "AA BB-CC%DD\tEE FF", font, rect, format, out int fitted);
            Assert.Equal(17, fitted);
            Assert.Single(regions);
            Assert.Equal(4, regions[0].GetRegionScans(new WfDraw::System.Drawing.Drawing2D.Matrix()).Length);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
    [Fact]
    public void Wrap_ShortRectDropsLaterWrapRows()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(200, 120);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat();
            format.SetMeasurableCharacterRanges(new[]
            {
                new WfCharacterRange(0, 17),
                new WfCharacterRange(6, 2),
            });
            var rect = new WfRectangleF(10, 4, 60, 25);
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "AA BB CC DD EE FF", font, rect, format);
            WfRectangleF b0 = regions[0].GetBounds(g);
            Assert.Equal(10f, b0.X);
            Assert.Equal(4f, b0.Y);
            Assert.Equal(50f, b0.Width);
            Assert.True(regions[1].IsEmpty(g));
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void Wrap_NoWrapReportsFirstRowOnly()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(200, 120);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat { FormatFlags = WfDraw::System.Drawing.StringFormatFlags.NoWrap };
            format.SetMeasurableCharacterRanges(new[]
            {
                new WfCharacterRange(0, 5),
                new WfCharacterRange(6, 2),
            });
            var rect = new WfRectangleF(10, 4, 60, 190);
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "AA BB CC DD EE FF", font, rect, format);
            Assert.False(regions[0].IsEmpty(g));
            Assert.Equal(10f, regions[0].GetBounds(g).X);
            Assert.True(regions[1].IsEmpty(g));
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void Wrap_NoWrapKeepsExplicitRows()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(200, 120);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat { FormatFlags = WfDraw::System.Drawing.StringFormatFlags.NoWrap };
            format.SetMeasurableCharacterRanges(new[]
            {
                new WfCharacterRange(0, 2),
                new WfCharacterRange(3, 2),
            });
            var rect = new WfRectangleF(10, 4, 180, 190);
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "AB\nCD", font, rect, format);
            Assert.Equal(10f, regions[0].GetBounds(g).X);
            Assert.True(regions[1].GetBounds(g).Y > regions[0].GetBounds(g).Y);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void Wrap_TrailingSpaceFragmentIsEmpty()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(200, 120);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat();
            format.SetMeasurableCharacterRanges([new WfCharacterRange(5, 1)]);
            var rect = new WfRectangleF(10, 4, 60, 190);
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "AA BB CC", font, rect, format);
            Assert.True(regions[0].IsEmpty(g));
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    // Centered wrap rows carry distinct X per row width (native oracle
    // wrap-center-narrow: 19/17/22). 10px/char seam rows here are 50/40/50.
    [Fact]
    public void Wrap_CenteredRowsAlignPerRow()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(200, 220);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat { Alignment = WfDraw::System.Drawing.StringAlignment.Center };
            format.SetMeasurableCharacterRanges(new[]
            {
                new WfCharacterRange(0, 5),
                new WfCharacterRange(6, 4),
            });
            var rect = new WfRectangleF(10, 4, 60, 190);
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "AA BB CC D EE FF", font, rect, format);
            WfRectangleF b0 = regions[0].GetBounds(g);
            WfRectangleF b1 = regions[1].GetBounds(g);
            Assert.Equal(15f, b0.X);
            Assert.Equal(20f, b1.X);
            Assert.True(b1.X > b0.X);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void Wrap_FarRowsUseRightMargin()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(400, 120);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat { Alignment = WfDraw::System.Drawing.StringAlignment.Far };
            format.SetMeasurableCharacterRanges(new[]
            {
                new WfCharacterRange(0, 2),
                new WfCharacterRange(3, 2),
            });
            var rect = new WfRectangleF(10, 4, 180, 190);
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "AB\nCD", font, rect, format);
            Assert.Equal(168f, regions[0].GetBounds(g).X);
            Assert.Equal(168f, regions[1].GetBounds(g).X);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void Wrap_FittedCountsVisibleRowsOnly()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(200, 120);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat();
            format.SetMeasurableCharacterRanges([new WfCharacterRange(0, 17)]);
            var rect = new WfRectangleF(10, 4, 60, 30);
            g.MeasureCharacterRanges("AA BB CC DD EE FF", font, rect, format, out int fitted);
            Assert.Equal(6, fitted);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    // Native oracle (oracle-wrap4/5/6): GDI+ breaks wrap rows after hyphens
    // (ink stays on the row) and after tabs (row-trailing tabs measure
    // zero, like trailing spaces). 10px/char seam reproduces the native
    // break indices here; tab-stop widths remain platform native behavior.
    [Fact]
    public void Wrap_BreaksAfterHyphen()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(200, 220);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat();
            format.SetMeasurableCharacterRanges(new[]
            {
                new WfCharacterRange(3, 3),
                new WfCharacterRange(0, 2),
            });
            var rect = new WfRectangleF(10, 4, 60, 190);
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "AA-BBCC", font, rect, format, out int fitted);
            WfRectangleF b1 = regions[0].GetBounds(g);
            WfRectangleF b0 = regions[1].GetBounds(g);
            Assert.Equal(10f, b1.X);
            Assert.True(b1.Y > b0.Y);
            Assert.True(Math.Abs((b1.Y - b0.Y) - b0.Height) < 0.01);
            Assert.Equal(7, fitted);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void Wrap_BreaksAfterTab()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(200, 220);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat();
            format.SetMeasurableCharacterRanges(new[]
            {
                new WfCharacterRange(5, 2),
                new WfCharacterRange(4, 1),
            });
            var rect = new WfRectangleF(10, 4, 60, 190);
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "AABB\tCCDD", font, rect, format, out int fitted);
            WfRectangleF b1 = regions[0].GetBounds(g);
            Assert.Equal(10f, b1.X);
            Assert.True(b1.Y > rect.Y);
            Assert.True(Math.Abs(b1.Y - (rect.Y + b1.Height)) < 0.01);
            Assert.True(regions[1].IsEmpty(g));
            Assert.Equal(9, fitted);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    // NoWrap+LineLimit composes to NoWrap alone (oracle-wrap4): row-0 only.
    [Fact]
    public void Wrap_NoWrapLineLimitMatchesNoWrap()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(200, 120);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat
            {
                FormatFlags = WfDraw::System.Drawing.StringFormatFlags.NoWrap
                    | WfDraw::System.Drawing.StringFormatFlags.LineLimit
            };
            format.SetMeasurableCharacterRanges(new[]
            {
                new WfCharacterRange(0, 5),
                new WfCharacterRange(6, 2),
            });
            var rect = new WfRectangleF(10, 4, 60, 190);
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "AA BB CC DD EE FF", font, rect, format);
            Assert.False(regions[0].IsEmpty(g));
            Assert.Equal(10f, regions[0].GetBounds(g).X);
            Assert.True(regions[1].IsEmpty(g));
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    // Native oracle (lone-cr): a lone \r is not a line break — "AB\rCD"
    // lays out as one row. Only \n and \r\n start new rows.
    [Fact]
    public void Wrap_LoneCrDoesNotBreak()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(200, 120);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat();
            format.SetMeasurableCharacterRanges(new[]
            {
                new WfCharacterRange(0, 2),
                new WfCharacterRange(3, 2),
            });
            var rect = new WfRectangleF(10, 4, 180, 190);
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "AB\rCD", font, rect, format, out int fitted);
            WfRectangleF b0 = regions[0].GetBounds(g);
            WfRectangleF b1 = regions[1].GetBounds(g);
            Assert.Equal(b0.Y, b1.Y);
            Assert.True(b1.X > b0.X);
            Assert.Equal(5, fitted);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    // Native oracle (valign-far-short): an overflowing Far block stays
    // bottom-aligned (top rows spill above and clip), not top-anchored.
    [Fact]
    public void Wrap_FarOverflowBottomAligns()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(200, 120);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat { LineAlignment = WfDraw::System.Drawing.StringAlignment.Far };
            format.SetMeasurableCharacterRanges(new[]
            {
                new WfCharacterRange(0, 2),
                new WfCharacterRange(3, 2),
            });
            var rect = new WfRectangleF(10, 4, 180, 30);
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "AB\nCD", font, rect, format);
            WfRectangleF b0 = regions[0].GetBounds(g);
            WfRectangleF b1 = regions[1].GetBounds(g);
            Assert.Equal(rect.Y, b0.Y);
            Assert.True(b0.Height < b1.Height);
            Assert.True(b1.Y > b0.Y);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    // Native oracle (noclip-short): NoClip skips bounds clipping but the
    // laid-out gate still drops later wrap rows that do not fit.
    [Fact]
    public void Wrap_NoClipShortKeepsLaidOutRowsUnclipped()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => RasterFor(text);
        try
        {
            using var bmp = new WfBitmap(200, 120);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 12f);
            using var format = new WfStringFormat { FormatFlags = WfDraw::System.Drawing.StringFormatFlags.NoClip };
            format.SetMeasurableCharacterRanges(new[]
            {
                new WfCharacterRange(0, 17),
                new WfCharacterRange(0, 5),
            });
            var rect = new WfRectangleF(10, 4, 60, 25);
            WfDraw::System.Drawing.Region[] regions = g.MeasureCharacterRanges(
                "AA BB CC DD EE FF", font, rect, format, out int fitted);
            WfRectangleF span = regions[0].GetBounds(g);
            WfRectangleF one = regions[1].GetBounds(g);
            Assert.Equal(one.Y, span.Y);
            Assert.True(Math.Abs(span.Height - one.Height) < 0.01);
            Assert.Equal(6, fitted);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
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
