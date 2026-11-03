// RVW-5795 Job1 defect-mechanism pin: unset-theme Empty fore color.
// Read-only vendor IL (verification/rvw5795-runtime/style-probe-output.txt
// R1–R7): SfNumericTextBox.get_Style() is a bare field read; the style
// store registers WatermarkForeColor with NO default; unset resolves via
// StyleInfoStore.GetValue to Color.Empty (ARGB 0x00000000), NOT
// Color.Transparent (0x00FFFFFF). The demo applies its theme at runtime
// (Program.Main refs Office2019Theme; sfComboBox1_SelectedIndexChanged
// refs SetVisualStyle) — if theme application fails served-side, the
// store stays unset.
// Frozen fork TextRenderer.DrawTextInternal early-returns only on
// Color.Transparent, so Empty reaches the rasterizer (rasterizer invoked)
// but effective alpha ea = (a * inkA)/255 = 0 (DisplayGraphics.cs:2539)
// blends nothing visible: no cue, no error, no exception. Transparent
// never reaches the rasterizer at all. This test pins that contrast in
// the fork primitive; the served ARGB dump (host grant pending) decides
// which one the app resolves.
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

namespace WfWasm.WinForms.Tests;

public sealed class WatermarkEmptyForeColorTests
{
    private static WfBrowserText.Raster Tick2x8()
    {
        byte[] rgba = new byte[2 * 8 * 4];
        for (int i = 3; i < rgba.Length; i += 4)
            rgba[i] = 255;
        return new WfBrowserText.Raster(2, 8, rgba);
    }

    private static int NonWhiteCount(WfBitmap bmp, WfRectangle region)
    {
        int white = WfColor.White.ToArgb();
        int n = 0;
        for (int y = region.Y; y < region.Y + region.Height; y++)
            for (int x = region.X; x < region.X + region.Width; x++)
                if (bmp.GetPixel(x, y).ToArgb() != white)
                    n++;
        return n;
    }

    [Fact]
    public void EmptyForeColor_Paints_Opaque_Black_Cue_ColorRef_Truncation()
    {
        // Unset-theme resolution is Color.Empty (ARGB 0x00000000), NOT
        // Transparent. The bridge carries foreColor as a GDI COLORREF, which
        // drops the high byte: Empty becomes RGB(0,0,0) = OPAQUE BLACK.
        // Diagnostic moral for the served gate: an Empty-resolved cue would
        // photograph BLACK, not absent. Absent cue therefore means
        // Transparent resolution, missed dispatch, swallowed throw, or an
        // unregistered paint target — never Empty.
        // Vendor-identical cue and flags (Left|VerticalCenter).
        int rasterCalls = 0;
        WfBrowserText.RasterOverride = (text, _, _, _) => { rasterCalls++; return Tick2x8(); };
        try
        {
            using var bmp = new WfBitmap(220, 40);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            using var font = new WfFont("Microsoft Sans Serif", 8.25f);
            var client = new WfRectangle(0, 0, 200, 30);
            ForkTextRenderer.DrawText(g, "Enter your age", font, client, WfColor.Empty, (ForkFlags)4);
            Assert.True(rasterCalls > 0, "Empty (unlike Transparent) must still reach the rasterizer");
            Assert.True(NonWhiteCount(bmp, client) > 0, "Empty cue must ink (as black)");
            int black = WfColor.Black.ToArgb();
            for (int y = client.Y; y < client.Y + client.Height; y++)
                for (int x = client.X; x < client.X + client.Width; x++)
                {
                    int p = bmp.GetPixel(x, y).ToArgb();
                    Assert.True(p == WfColor.White.ToArgb() || p == black,
                        $"every inked pixel is opaque black, found {p:X8}");
                }
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void BrushColor_Flows_Through_ColorRef_Red_Inks_Red()
    {
        // Positive control: foreColor reaches pixels through the GDI
        // COLORREF (high byte dropped). Red (0xFFFF0000) inks red, so a
        // gray WatermarkForeColor would photograph gray — the bridge
        // honors cue colors whenever OnPaint dispatches with a target.
        int rasterCalls = 0;
        WfBrowserText.RasterOverride = (text, _, _, _) => { rasterCalls++; return Tick2x8(); };
        try
        {
            using var bmp = new WfBitmap(220, 40);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            using var font = new WfFont("Microsoft Sans Serif", 8.25f);
            var client = new WfRectangle(0, 0, 200, 30);
            ForkTextRenderer.DrawText(g, "Enter your age", font, client, WfColor.Red, (ForkFlags)4);
            Assert.True(rasterCalls > 0);
            int red = WfColor.Red.ToArgb();
            int black = WfColor.Black.ToArgb();
            int nRed = 0, nBlack = 0;
            for (int y = client.Y; y < client.Y + client.Height; y++)
                for (int x = client.X; x < client.X + client.Width; x++)
                {
                    int p = bmp.GetPixel(x, y).ToArgb();
                    if (p == red) nRed++;
                    else if (p == black) nBlack++;
                }
            Assert.True(nRed + nBlack > 0, "something must ink");
            Assert.True(nRed > 0, $"brush color must reach pixels (red={nRed} black={nBlack} calls={rasterCalls})");
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void TransparentForeColor_Never_Reaches_Rasterizer()
    {
        int rasterCalls = 0;
        WfBrowserText.RasterOverride = (text, _, _, _) => { rasterCalls++; return Tick2x8(); };
        try
        {
            using var bmp = new WfBitmap(220, 40);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            using var font = new WfFont("Microsoft Sans Serif", 8.25f);
            var client = new WfRectangle(0, 0, 200, 30);
            ForkTextRenderer.DrawText(g, "Enter your age", font, client, WfColor.Transparent, (ForkFlags)4);
            Assert.Equal(0, rasterCalls);
            Assert.Equal(0, NonWhiteCount(bmp, client));
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
}
