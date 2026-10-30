using DesktopWasm.Graphics;
using SkiaSharp;

namespace DesktopWasm.Core.Tests;

public sealed class TransformReplayTests
{
    private static SKColor At(SKBitmap bitmap, int x, int y) => bitmap.GetPixel(x, y);

    [Fact]
    public void Replay_translation_moves_paint()
    {
        using var surface = new SkiaSurface(100, 100);
        surface.Replay([
            new DrawCommand.PushTransform(1, 0, 0, 1, 20, 10),
            new DrawCommand.FillRect(0, 0, 10, 10, 0xFF0000FF),
            new DrawCommand.PopTransform(),
        ]);

        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        // Opaque red shifted by (20, 10).
        Assert.Equal(new SKColor(0xFF, 0x00, 0x00, 0xFF), At(bitmap, 25, 15));
        Assert.Equal(0, At(bitmap, 5, 5).Alpha); // origin stays untouched
    }

    [Fact]
    public void Replay_scale_still_applies()
    {
        using var surface = new SkiaSurface(100, 100);
        surface.Replay([
            new DrawCommand.PushTransform(2, 0, 0, 2, 0, 0),
            new DrawCommand.FillRect(0, 0, 10, 10, 0xFF0000FF),
            new DrawCommand.PopTransform(),
        ]);

        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        Assert.Equal(new SKColor(0xFF, 0x00, 0x00, 0xFF), At(bitmap, 15, 15));
        Assert.Equal(0, At(bitmap, 25, 5).Alpha); // outside the doubled rect
    }

    [Fact]
    public void Replay_pop_restores_untransformed_state()
    {
        using var surface = new SkiaSurface(100, 100);
        surface.Replay([
            new DrawCommand.PushTransform(1, 0, 0, 1, 50, 0),
            new DrawCommand.FillRect(0, 0, 10, 10, 0xFF0000FF),
            new DrawCommand.PopTransform(),
            new DrawCommand.FillRect(0, 0, 10, 10, 0x0000FFFF),
        ]);

        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        Assert.Equal(new SKColor(0xFF, 0x00, 0x00, 0xFF), At(bitmap, 55, 5));
        Assert.Equal(new SKColor(0x00, 0x00, 0xFF, 0xFF), At(bitmap, 5, 5));
    }
}
