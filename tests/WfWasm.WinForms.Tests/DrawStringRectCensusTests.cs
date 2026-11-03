// WC1204: DrawString rect census pin. The fork Label paints via
// Graphics.DrawString directly (bypassing the Shim and GDI censuses),
// so label6's paint-time rect+flags were unobservable (WC1141). The
// NoteDrawStringRect hunk (Graphics.cs) logs preview + layoutRectangle +
// NoWrap bit once per text shape. These tests drive the exact 124-char
// welcome paragraph through the frozen box (316x56) and the served box
// (344x45) and assert the emission. Rect origins (0,0) are disjoint by
// construction from the TextRenderer funnel path, which insets to
// (3,0,309x56) — and the GDI wrap path, which paints (0,0,316,56) —
// so these tests drive X=7 origins: identical WxH boxes, shapes no
// sibling test can consume, deterministic under parallel runners.
extern alias WasmDraw;

using WfBitmap = WasmDraw::System.Drawing.Bitmap;
using WfBrush = WasmDraw::System.Drawing.Brush;
using WfColor = WasmDraw::System.Drawing.Color;
using WfFont = WasmDraw::System.Drawing.Font;
using WfGraphics = WasmDraw::System.Drawing.Graphics;
using WfRectF = WasmDraw::System.Drawing.RectangleF;
using WfSolidBrush = WasmDraw::System.Drawing.SolidBrush;
using WfStringFormat = WasmDraw::System.Drawing.StringFormat;
using WfStringFormatFlags = WasmDraw::System.Drawing.StringFormatFlags;
using WfBrowserText = WasmDraw::System.Drawing.BrowserText;

public sealed class DrawStringRectCensusTests
{
    private const string Welcome =
        "The Wizard control allows developers to make wizard applications " +
        "quickly offering easy control over pages and wizard buttons";

    private static WfBrowserText.Raster? StubRaster(string text, WfFont font, float size, int maxW)
    {
        int w = Math.Max(1, Math.Min(maxW, 2048));
        byte[] rgba = new byte[w * 8 * 4];
        for (int i = 3; i < rgba.Length; i += 4) rgba[i] = 255;
        return new WfBrowserText.Raster(w, 8, rgba);
    }

    private static string DrawAndCapture(WfGraphics g, WfFont font, WfBrush brush, WfRectF rect, WfStringFormat? format)
    {
        var sb = new System.Text.StringBuilder();
        var old = System.Console.Out;
        System.Console.SetOut(new System.IO.StringWriter(sb));
        try
        {
            g.DrawString(Welcome, font, brush, rect, format);
        }
        finally
        {
            System.Console.SetOut(old);
        }

        return sb.ToString();
    }

    [Fact]
    public void WelcomeParagraph_BothBoxes_Emit_Rect_With_NoWrap_Bit()
    {
        // Grant boxes, wrapping format: both shapes must emit with
        // nowrap=False. X=7 origins keep the shapes unique to these
        // direct calls (TextRenderer funnel uses X=3, GDI wrap uses X=0).
        WfBrowserText.RasterOverride = StubRaster;
        try
        {
            using var bmp = new WfBitmap(420, 100);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Microsoft Sans Serif", 8.25f);
            using var brush = new WfSolidBrush(WfColor.Black);

            string frozen = DrawAndCapture(g, font, brush, new WfRectF(7, 0, 316, 56), new WfStringFormat());
            Assert.Contains("[DS-RECT]", frozen);
            Assert.Contains("The Wizard control allow", frozen);
            Assert.Contains("316x56", frozen);
            Assert.Contains("nowrap=False", frozen);

            string served = DrawAndCapture(g, font, brush, new WfRectF(7, 0, 344, 45), new WfStringFormat());
            Assert.Contains("[DS-RECT]", served);
            Assert.Contains("344x45", served);
            Assert.Contains("nowrap=False", served);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void NoWrap_Format_Emits_NoWrap_Bit()
    {
        // Same served box, NoWrap format: theWrap bit must read True.
        // This is the served single-row shape the re-gate is looking for.
        WfBrowserText.RasterOverride = StubRaster;
        try
        {
            using var bmp = new WfBitmap(420, 100);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Microsoft Sans Serif", 8.25f);
            using var brush = new WfSolidBrush(WfColor.Black);

            string log = DrawAndCapture(g, font, brush, new WfRectF(7, 0, 344, 45),
                new WfStringFormat(WfStringFormatFlags.NoWrap));
            Assert.Contains("[DS-RECT]", log);
            Assert.Contains("344x45", log);
            Assert.Contains("nowrap=True", log);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
}
