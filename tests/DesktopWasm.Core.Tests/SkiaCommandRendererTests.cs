using DesktopWasm.Graphics;
using SkiaSharp;

namespace DesktopWasm.Core.Tests;

public sealed class SkiaCommandRendererTests
{
    [Fact]
    public void Shear_uses_the_same_matrix_convention_as_Canvas2D()
    {
        using var surface = new SkiaSurface(60, 60);
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Replay([
            // x' = x + y, y' = y. Off-diagonal terms must not be transposed.
            new DrawCommand.PushTransform(1, 0, 1, 1, 0, 0),
            new DrawCommand.FillRect(4, 20, 6, 6, 0xFF0000FF),
            new DrawCommand.PopTransform(),
        ]);
        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        Assert.Equal(SKColors.Red, bitmap.GetPixel(28, 22));
        Assert.Equal(0, bitmap.GetPixel(6, 28).Alpha);
    }

    [Fact]
    public void Gradient_and_geometry_replay_actual_pixels()
    {
        using var surface = new SkiaSurface(80, 80);
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Replay([
            new DrawCommand.FillGradient(0, 0, 40, 80, 0xFF0000FF, 0x0000FFFF),
            new DrawCommand.DrawGeometry("M 45 10 L 75 10 L 60 60 Z", 0x00FF00FF, null, 0),
        ]);
        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        Assert.True(bitmap.GetPixel(20, 5).Red > 200);
        Assert.True(bitmap.GetPixel(20, 75).Blue > 200);
        Assert.Equal(SKColors.Lime, bitmap.GetPixel(60, 20));
        Assert.Equal(0, bitmap.GetPixel(78, 70).Alpha);
    }

    [Fact]
    public void Bitmap_patches_replace_alpha_and_layers_survive_between_frames()
    {
        using var renderer = new SkiaCommandRenderer();
        using var surface = SKSurface.Create(new SKImageInfo(20, 20));
        renderer.Replay(surface.Canvas, [new DrawCommand.DefBitmap("image", [255, 0, 0, 255, 0, 0, 255, 255], 2, 1)]);
        renderer.Replay(surface.Canvas, [new DrawCommand.PatchBitmap("image", [0, 255, 0, 255], 1, 0, 1, 1), new DrawCommand.BlitBitmap("image", 0, 0, 2, 1)]);
        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        Assert.Equal(SKColors.Red, bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.Lime, bitmap.GetPixel(1, 0));
        renderer.Replay(surface.Canvas, [new DrawCommand.RetainLayers([])]);
        Assert.Throws<InvalidOperationException>(() => renderer.Replay(surface.Canvas, [new DrawCommand.BlitBitmap("image", 0, 0, 2, 1)]));
    }

    [Fact]
    public void Image_and_text_render_through_the_same_canvas()
    {
        using var surface = new SkiaSurface(160, 60);
        surface.Canvas.Clear(SKColors.White);
        surface.Replay([
            new DrawCommand.DrawImage([255, 0, 0, 255], 1, 1, 0, 0, 5, 5),
            new DrawCommand.DrawText("Real application", 10, 25, "20px sans-serif", 0x000000FF, 0, 0, -1),
        ]);
        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        Assert.Equal(SKColors.Red, bitmap.GetPixel(2, 2));
        var ink = 0;
        for (var y = 8; y < 35; y++) for (var x = 10; x < 155; x++) if (bitmap.GetPixel(x, y).Red < 100) ink++;
        Assert.True(ink > 30);
    }

    [Fact]
    public void Failed_replay_restores_caller_canvas_state_and_resources_are_disposable()
    {
        using var renderer = new SkiaCommandRenderer();
        using var surface = SKSurface.Create(new SKImageInfo(50, 50));
        var count = surface.Canvas.SaveCount;
        Assert.Throws<InvalidOperationException>(() => renderer.Replay(surface.Canvas, [new DrawCommand.PushClip(0, 0, 4, 4)]));
        Assert.Equal(count, surface.Canvas.SaveCount);
        Assert.Throws<InvalidOperationException>(() => renderer.Replay(surface.Canvas, [new DrawCommand.PopClip()]));
        Assert.Equal(count, surface.Canvas.SaveCount);
        renderer.Dispose(); renderer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => renderer.Replay(surface.Canvas, []));
    }

    [Fact]
    public void Invalid_bitmap_bytes_and_outside_patches_fail_precisely()
    {
        using var renderer = new SkiaCommandRenderer();
        using var surface = SKSurface.Create(new SKImageInfo(10, 10));
        Assert.Throws<ArgumentException>(() => renderer.Replay(surface.Canvas, [new DrawCommand.DefBitmap("bad", [1], 1, 1)]));
        renderer.Replay(surface.Canvas, [new DrawCommand.DefBitmap("ok", [0, 0, 0, 255], 1, 1)]);
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Replay(surface.Canvas, [new DrawCommand.PatchBitmap("ok", [0, 0, 0, 255], 1, 0, 1, 1)]));
    }
}
