// RVW-5923 Job1 causal test: the Syncfusion SfNumericTextBox watermark.
// IL-verified on the frozen staging bytes (verification/rvw5923-wpf/VendorProbe):
//   SfNumericTextBoxDemo.GettingStarted::InitializeComponent calls
//   SfNumericTextBox.set_WatermarkText("Enter your age") on its WatermarkTextBox;
//   Syncfusion.WinForms.Input.SfNumericTextBox::OnPaint, when Text is empty,
//   calls TextRenderer.DrawText(Graphics, WatermarkText, Font, ClientRectangle,
//   Style.WatermarkForeColor, flags) with flags 4/5/6 (Left/Center/Right +
//   VerticalCenter; TextFormatFlags mirrors DRAW_TEXT_FORMAT DT_CENTER=1,
//   DT_RIGHT=2, DT_VCENTER=4).
// This test drives the fork's TextRenderer with those vendor-identical
// arguments headless. Green means the fork primitive serves the vendor
// contract, so the browser defect is upstream of it (OnPaint dispatch, HDC
// paint-target registration during host paint, or a Transparent fore color,
// which DrawTextInternal drops silently). Red means the TextRenderer->GDI
// bridge itself drops the cue.
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

public sealed class WatermarkCausalTests
{
    // 2x8 fully-opaque tick; the paint path tints it by the brush, so each
    // laid word paints a 2px tick exactly at its origin (same technique as
    // WasmGdiTextDrawTextTests).
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
    public void WatermarkStyle_DrawText_Inks_Cue_Inside_Client_Rect()
    {
        WfBrowserText.RasterOverride = (_, _, _, _) => Tick2x8();
        try
        {
            using var bmp = new WfBitmap(220, 40);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            using var font = new WfFont("Microsoft Sans Serif", 8.25f);
            // Vendor-identical: gray cue, client rect, Left|VerticalCenter.
            var gray = WfColor.FromArgb(255, 128, 128, 128);
            var client = new WfRectangle(0, 0, 200, 30);
            ForkTextRenderer.DrawText(g, "Enter your age", font, client, gray, (ForkFlags)4);
            int inked = NonWhiteCount(bmp, client);
            Assert.True(inked > 0, "watermark cue must ink pixels inside the client rect");
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void WatermarkStyle_DrawText_Transparent_Stays_Silent()
    {
        // Pins the upstream silent-drop contract: a Transparent fore color
        // paints nothing and throws nothing. If the served app's resolved
        // WatermarkForeColor were Transparent, the cue would vanish exactly
        // like the browser defect, with no error anywhere.
        WfBrowserText.RasterOverride = (_, _, _, _) => Tick2x8();
        try
        {
            using var bmp = new WfBitmap(220, 40);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            using var font = new WfFont("Microsoft Sans Serif", 8.25f);
            var client = new WfRectangle(0, 0, 200, 30);
            ForkTextRenderer.DrawText(g, "Enter your age", font, client, WfColor.Transparent, (ForkFlags)4);
            Assert.Equal(0, NonWhiteCount(bmp, client));
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
}
