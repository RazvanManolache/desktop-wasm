using WfWasm.CanvasHost;

namespace WfWasm.CanvasHost.Tests;

// Pixel-proof scroll upload decision matrix (USERFIX RTE scroll, review
// safe option): the fresh full raster's overlap must equal the retained
// raster translated by the pending shift. Any doubt uploads the full
// bitmap as today. Edits, selection and caret pixels always fail the
// compare and ride the full bitmap: correctness never depends on tokens,
// generations or dirty provenance.
public sealed class ScrollShiftOverlapTests
{
    private const int W = 8;
    private const int H = 6;

    // Surface where pixel (x,y) holds bytes (x, y, 0, 255).
    private static byte[] Surface(int w = W, int h = H)
    {
        byte[] px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                px[o] = (byte)x;
                px[o + 1] = (byte)y;
                px[o + 3] = 255;
            }
        return px;
    }

    // Retained surface = fresh content scrolled down by s (content moved
    // up): old[y] = fresh[y - s] for scrolled rows.
    private static byte[] ScrolledDown(byte[] fresh, int w = W, int h = H, int s = 2)
    {
        byte[] old = new byte[w * h * 4];
        Buffer.BlockCopy(fresh, 0, old, s * w * 4, (h - s) * w * 4);
        return old;
    }

    [Fact]
    public void PureScrollDown_OverlapEqual()
    {
        byte[] fresh = Surface();
        Assert.True(WinFormsAppHost.ScrollOverlapEqual(fresh, ScrolledDown(fresh), W, H, 0, -2));
    }

    [Fact]
    public void PureScrollUp_OverlapEqual()
    {
        byte[] fresh = Surface();
        byte[] old = new byte[W * H * 4];
        Buffer.BlockCopy(fresh, 2 * W * 4, old, 0, (H - 2) * W * 4);
        Assert.True(WinFormsAppHost.ScrollOverlapEqual(fresh, old, W, H, 0, 2));
    }

    [Fact]
    public void PureScrollRight_OverlapEqual()
    {
        byte[] fresh = Surface();
        byte[] old = new byte[W * H * 4];
        for (int r = 0; r < H; r++)
            Buffer.BlockCopy(fresh, (r * W + 2) * 4, old, r * W * 4, (W - 2) * 4);
        Assert.True(WinFormsAppHost.ScrollOverlapEqual(fresh, old, W, H, 2, 0));
    }

    [Fact]
    public void SingleByteEdit_FailsProof()
    {
        // Simultaneous edit: one changed pixel in the overlap fails safe.
        byte[] fresh = Surface();
        byte[] old = ScrolledDown(fresh);
        old[(3 * W + 3) * 4] ^= 0xFF;
        Assert.False(WinFormsAppHost.ScrollOverlapEqual(fresh, old, W, H, 0, -2));
    }

    [Fact]
    public void SelectionPixelChange_FailsProof()
    {
        // Selection highlight changes overlap pixels without touching any
        // document token: pixel proof still catches it.
        byte[] fresh = Surface();
        byte[] old = ScrolledDown(fresh);
        for (int x = 1; x < 5; x++)
            old[(2 * W + x) * 4] = 0x80;
        Assert.False(WinFormsAppHost.ScrollOverlapEqual(fresh, old, W, H, 0, -2));
    }

    [Fact]
    public void ZeroShift_Rejected()
    {
        byte[] fresh = Surface();
        Assert.False(WinFormsAppHost.ScrollOverlapEqual(fresh, (byte[])fresh.Clone(), W, H, 0, 0));
    }

    [Fact]
    public void DiagonalShift_Rejected()
    {
        byte[] fresh = Surface();
        Assert.False(WinFormsAppHost.ScrollOverlapEqual(fresh, ScrolledDown(fresh), W, H, 1, -2));
    }

    [Fact]
    public void OversizeShift_Rejected()
    {
        byte[] fresh = Surface();
        Assert.False(WinFormsAppHost.ScrollOverlapEqual(fresh, ScrolledDown(fresh), W, H, 0, -H));
    }

    [Fact]
    public void SizeMismatch_Rejected()
    {
        byte[] fresh = Surface();
        Assert.False(WinFormsAppHost.ScrollOverlapEqual(fresh, new byte[W * H * 4 - 4], W, H, 0, -2));
    }

    [Fact]
    public void ExposedStrip_MatchesNarrowConvention()
    {
        Assert.Equal(new System.Drawing.Rectangle(0, H - 2, W, 2),
            WinFormsAppHost.ScrollExposedStrip(W, H, 0, -2));
        Assert.Equal(new System.Drawing.Rectangle(0, 0, W, 2),
            WinFormsAppHost.ScrollExposedStrip(W, H, 0, 2));
        Assert.Equal(new System.Drawing.Rectangle(0, 0, 2, H),
            WinFormsAppHost.ScrollExposedStrip(W, H, 2, 0));
        Assert.Equal(new System.Drawing.Rectangle(W - 3, 0, 3, H),
            WinFormsAppHost.ScrollExposedStrip(W, H, -3, 0));
    }

    [Fact]
    public void SliceRgba_CutsExactStrip()
    {
        byte[] fresh = Surface();
        byte[] strip = WinFormsAppHost.SliceRgba(fresh, W, new System.Drawing.Rectangle(0, H - 2, W, 2));
        Assert.Equal(2 * W * 4, strip.Length);
        byte[] expect = new byte[2 * W * 4];
        Buffer.BlockCopy(fresh, (H - 2) * W * 4, expect, 0, expect.Length);
        Assert.Equal(expect, strip);
    }
}
