// RVW-5795 Job1: newline-free DT_WORDBREAK paint/measure height contract.
//
// The wizard welcome paragraph (124 chars, no newlines, WordBreak, 316px box)
// paints wrapped across 2+ rows but TryDrawText reported font.Height, while
// DT_CALCRECT for the same input reports the real multi-row height. Desktop
// DrawTextEx returns the drawn height, so wrapped paint must return what
// measurement reports. Single-line paint (watermark/clip flags, no WordBreak)
// must keep returning font.Height.
extern alias Fork;
extern alias WasmDraw;

using WfBitmap = WasmDraw::System.Drawing.Bitmap;
using WfColor = WasmDraw::System.Drawing.Color;
using WfFont = WasmDraw::System.Drawing.Font;
using WfGraphics = WasmDraw::System.Drawing.Graphics;
using WfBrowserText = WasmDraw::System.Drawing.BrowserText;
using ForkHdc = Fork::Windows.Win32.Graphics.Gdi.HDC;
using ForkFill = Fork::Windows.Win32.PInvoke.WasmFillBridge;
using ForkGdiText = Fork::System.Windows.Forms.WasmGdiText;

namespace WfWasm.WinForms.Tests;

public sealed class WizardWrapHeightTests
{
    private const string Welcome =
        "The Wizard control allows developers to make wizard applications " +
        "quickly offering easy control over pages and wizard buttons";

    private const int DT_WORDBREAK = 0x10;
    private const int DT_EDITCONTROL = 0x2000;
    private const int DT_CALCRECT = 0x400;
    private const int DT_CENTER = 0x2;
    private const int DT_VCENTER = 0x4;

    // Wrap-emulating raster stub (same technique as WizardWrapCausalTests):
    // lays words into rows of maxW at a fixed 7px advance and returns one
    // 8px-tall opaque band per row.
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
    public void WelcomeParagraph_WordBreak_PaintHeight_Equals_CalcHeight()
    {
        WfBrowserText.RasterOverride = (text, _, _, maxW) => WrapStub(text, maxW);
        try
        {
            using var bmp = new WfBitmap(340, 80);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            using var font = new WfFont("Microsoft Sans Serif", 8.25f);
            var hdc = (ForkHdc)(IntPtr)0x5795B1;
            using var scope = ForkFill.TrackTarget(hdc, g);
            ForkGdiText.SelectObject((IntPtr)0x5795B1, font.ToHfont());

            int paintHeight = ForkGdiText.DrawText(
                (IntPtr)0x5795B1, Welcome, -1, 0, 0, 316, 56,
                DT_WORDBREAK | DT_EDITCONTROL, out _, out _);
            bool row1 = AnyInkInBand(bmp, 0, 12, 316);
            bool row2 = AnyInkInBand(bmp, 12, 56, 316);

            g.Clear(WfColor.White);
            int calcHeight = ForkGdiText.DrawText(
                (IntPtr)0x5795B1, Welcome, -1, 0, 0, 316, 56,
                DT_WORDBREAK | DT_EDITCONTROL | DT_CALCRECT,
                out _, out _);

            Assert.True(row1 && row2, "wrapped paint must ink two rows");
            Assert.True(calcHeight > font.Height,
                $"CALCRECT must report multi-row height, got {calcHeight} vs font {font.Height}");
            Assert.Equal(calcHeight, paintHeight);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Theory]
    [InlineData(0)] // DT_LEFT/TOP: fast word path
    [InlineData(DT_CENTER | DT_VCENTER)] // watermark centering: DrawString fallback
    public void SingleLine_PaintHeight_Stays_FontHeight(int flags)
    {
        WfBrowserText.RasterOverride = (text, _, _, maxW) => WrapStub(text, maxW);
        try
        {
            using var bmp = new WfBitmap(340, 80);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            using var font = new WfFont("Microsoft Sans Serif", 8.25f);
            var hdc = (ForkHdc)(IntPtr)0x5795B2;
            using var scope = ForkFill.TrackTarget(hdc, g);
            ForkGdiText.SelectObject((IntPtr)0x5795B2, font.ToHfont());

            int paintHeight = ForkGdiText.DrawText(
                (IntPtr)0x5795B2, "Hi", -1, 10, 4, 150, 22, flags, out _, out _);

            Assert.Equal(font.Height, paintHeight);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
}
