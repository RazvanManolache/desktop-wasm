using DesktopWasm.Graphics;
using SkiaSharp;

namespace DesktopWasm.Core.Tests;

public sealed class SurfaceReplayTests
{
    [Fact]
    public void Replay_fillrect_paints_exact_pixel()
    {
        using var surface = new SkiaSurface(100, 100);
        surface.Replay([new DrawCommand.FillRect(5, 5, 10, 10, 0xFF0000FF)]);

        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        // Local 0xRRGGBBAA convention: opaque red.
        Assert.Equal(new SKColor(0xFF, 0x00, 0x00, 0xFF), bitmap.GetPixel(7, 7));
        Assert.Equal(0, bitmap.GetPixel(90, 90).Alpha); // untouched: fully transparent
    }

    [Fact]
    public void Replay_clip_confines_paint_and_pops()
    {
        using var surface = new SkiaSurface(100, 100);
        surface.Replay([
            new DrawCommand.PushClip(0, 0, 10, 10),
            new DrawCommand.FillRect(0, 0, 100, 100, 0xFF0000FF),
            new DrawCommand.PopClip(),
        ]);

        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        Assert.Equal(new SKColor(0xFF, 0x00, 0x00, 0xFF), bitmap.GetPixel(5, 5));
        Assert.Equal(0, bitmap.GetPixel(50, 50).Alpha); // clipped away: fully transparent
    }

    [Fact]
    public void Replay_text_is_shaped_and_harmless()
    {
        using var surface = new SkiaSurface(200, 60);
        surface.Replay([
            new DrawCommand.FillRect(0, 0, 200, 60, 0xFFFFFFFF),
            new DrawCommand.DrawText("Hi", 10, 30, "14px sans-serif", 0x000000FF, 0, 0, -1),
            new DrawCommand.DrawText("bad-font", 10, 50, "not-a-size", 0x000000FF, 1, 2, -1),
        ]);

        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        Assert.Equal(200, bitmap.Width);
        Assert.Equal(60, bitmap.Height);
    }
}
