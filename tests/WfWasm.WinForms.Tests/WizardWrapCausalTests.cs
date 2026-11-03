// RVW-5923 Job2 focused regression: the Syncfusion wizard welcome paragraph.
// IL-verified on the frozen staging bytes (verification/rvw5923-wpf/VendorProbe):
//   WizardControlTest.Form1::InitializeComponent sets Form1.label6
//   (System.Windows.Forms.Label, Size 316x56) to the 124-char single-line
//   paragraph "The Wizard control allows developers to make wizard
//   applications quickly offering easy control over pages and wizard buttons".
//   No AutoSize/MaximumSize assignment exists anywhere in InitializeComponent.
// Upstream desktop contract (frozen runtime sources, read-only):
//   ControlPaint.CreateTextFormatFlags always sets WordBreak|TextBoxControl for
//   label paint; Label.CreateTextFormatFlags strips WordBreak only when the
//   text fits on one line. A 124-char paragraph in a 316px box must wrap.
// This test pins the fork primitive the wrapped welcome page needs:
// TextRenderer.DrawText with WordBreak|TextBoxControl in a 316px-wide rect
// must ink at least two text rows. If the served welcome page stays
// single-line while this is green, the defect is in the fork Label flag/box
// path (flag audit via the runtime text census), not in the paint primitive.
extern alias Fork;
extern alias WasmDraw;

using WfBitmap = WasmDraw::System.Drawing.Bitmap;
using WfColor = WasmDraw::System.Drawing.Color;
using WfFont = WasmDraw::System.Drawing.Font;
using WfGraphics = WasmDraw::System.Drawing.Graphics;
using WfRectangle = WasmDraw::System.Drawing.Rectangle;
using WfBrowserText = WasmDraw::System.Drawing.BrowserText;
using ForkFlags = Fork::System.Windows.Forms.TextFormatFlags;
using ForkTextRenderer = Fork::System.Windows.Forms.TextRenderer;
using WfSize = WasmDraw::System.Drawing.Size;

namespace WfWasm.WinForms.Tests;

public sealed class WizardWrapCausalTests
{
    private const string Welcome =
        "The Wizard control allows developers to make wizard applications " +
        "quickly offering easy control over pages and wizard buttons";

    // Wrap-emulating raster stub (same seam as the tick technique in
    // WasmGdiTextDrawTextTests, but wrap-aware): lays words into rows of
    // maxW at a fixed 7px advance and returns one 8px-tall opaque band per
    // row, so wrapped paint inks multiple row bands while single-row paint
    // inks only the first. A fixed per-word tick cannot distinguish them:
    // the whole-paragraph DrawString call rasters once, so it would paint
    // a single tick either way.
    private static WfBrowserText.Raster WrapStub(string text, int maxW)
    {
        const int advance = 7;
        const int rowH = 8;
        maxW = Math.Max(1, maxW);
        int rows = 1;
        int lineW = 0;
        foreach (string word in text.Split(' '))
        {
            int w = Math.Max(1, word.Length * advance);
            int need = lineW == 0 ? w : lineW + advance + w;
            if (need > maxW && lineW > 0)
            {
                rows++;
                lineW = w;
            }
            else
            {
                lineW = need;
            }
        }
        byte[] rgba = new byte[maxW * rows * rowH * 4];
        for (int i = 3; i < rgba.Length; i += 4)
            rgba[i] = 255;
        return new WfBrowserText.Raster(maxW, rows * rowH, rgba);
    }

    private static bool AnyInkInBand(WfBitmap bmp, int y0, int y1, int width)
    {
        int white = WfColor.White.ToArgb();
        for (int y = y0; y < y1; y++)
            for (int x = 0; x < width; x++)
                if (bmp.GetPixel(x, y).ToArgb() != white)
                    return true;
        return false;
    }

    [Fact]
    public void WelcomeParagraph_WordBreak_Inks_At_Least_Two_Rows()
    {
        WfBrowserText.RasterOverride = (text, _, _, maxW) => WrapStub(text, maxW);
        try
        {
            using var bmp = new WfBitmap(340, 80);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            using var font = new WfFont("Microsoft Sans Serif", 8.25f);
            // label6's box: 316 wide. WordBreak|TextBoxControl, no VCenter.
            var box = new WfRectangle(0, 0, 316, 56);
            ForkTextRenderer.DrawText(
                g, Welcome, font, box, WfColor.Black,
                (ForkFlags)0x10 | (ForkFlags)0x2000);
            Assert.True(AnyInkInBand(bmp, 0, 12, 316), "first row must ink");
            Assert.True(AnyInkInBand(bmp, 12, 56, 316), "a second row must ink inside the 56px box");
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void ServedPath_Label_OnPaint_Wraps_In_Served_Box()
    {
        // WC1145 reconcile: served label6 provably paints via fork
        // Label.OnPaint (DrawString branch: UseCompatibleTextRendering
        // defaults true and no flip exists in served Main/Init/host,
        // verified on served bytes) — NOT via the GDI TextRenderer path
        // pinned above. Pin the served path itself, in the SERVED live box
        // (344x45, runtime layout) as well as the frozen IL box (316x56):
        // with a wrapping rasterizer both must ink at least two row bands.
        // A single band here means the DrawString branch regressed
        // (NoWrap format, unbounded maxW, or raster bypass) — exactly the
        // fork-side shape that would explain a served single-row page.
        WfBrowserText.RasterOverride = (text, _, _, maxW) => WrapStub(text, maxW);
        try
        {
            AssertRowBands(316, 56);
            AssertRowBands(344, 45);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    private static void AssertRowBands(int w, int h)
    {
        using var bmp = new WfBitmap(400, 100);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        using var font = new WfFont("Microsoft Sans Serif", 8.25f);
        using var label = new System.Windows.Forms.Label
        {
            AutoSize = false,
            Text = Welcome,
            Font = font,
            Size = new WfSize(w, h),
        };
        var args = new System.Windows.Forms.PaintEventArgs(g, new WfRectangle(0, 0, w, h));
        typeof(System.Windows.Forms.Control).GetMethod("OnPaint",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(label, new object[] { args });
        Assert.True(AnyInkInBand(bmp, 0, 12, w), $"box {w}x{h}: first row must ink");
        Assert.True(AnyInkInBand(bmp, 12, h, w), $"box {w}x{h}: a second row must ink (served DrawString path wraps)");
    }
}
