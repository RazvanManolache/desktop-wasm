using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

[ResetAmbientState]
public sealed class DirtyRegionTests
{
    private sealed class CountingPaintable : IImmediatePaintable
    {
        public int PaintCalls;
        public WpfSize Measure(WpfSize available) => new(100, 100);

        public void Paint(DrawingContext dc, WpfRect bounds)
        {
            PaintCalls++;
            dc.DrawRectangle((float)bounds.X, (float)bounds.Y, 10, 10, 0xFF0000FF);
        }
    }

    private sealed class CountingSink : IFrameSink
    {
        public int Paints;
        public Task UploadDefAsync(string layerId, byte[] rgba, int width, int height, CancellationToken ct = default) => Task.CompletedTask;
        public Task UploadPatchAsync(string layerId, byte[] rgba, int x, int y, int width, int height, CancellationToken ct = default) => Task.CompletedTask;

        public Task<string> PaintJsonAsync(string canvasId, string json, CancellationToken ct = default)
        {
            Paints++;
            return Task.FromResult($"painted-{Paints}");
        }
    }

    private static (WasmWpfHost Host, WasmInputRouter Router, CountingPaintable Paintable, CountingSink Sink) Harness()
    {
        var surface = new SkiaSurface(800, 600);
        var router = new WasmInputRouter();
        var host = new WasmWpfHost(new WasmDispatcherPump(), surface, router);
        var paintable = new CountingPaintable();
        var window = new Window { Content = new WindowsFormsHost { Child = paintable } };
        host.AddWindow(window, new WpfSize(800, 600));
        return (host, router, paintable, new CountingSink());
    }

    [Fact]
    public void Clean_tick_returns_cached_frame_without_rerender()
    {
        var (host, _, paintable, _) = Harness();

        Assert.True(host.IsDirty);
        var first = host.Tick(new FrameContext(1, 16.6));
        Assert.False(host.IsDirty);
        Assert.Equal(1, paintable.PaintCalls);

        var second = host.Tick(new FrameContext(2, 33.2));
        Assert.Same(first, second);
        Assert.Equal(1, paintable.PaintCalls);
    }

    [Fact]
    public void Input_and_invalidate_rerender()
    {
        var (host, router, paintable, _) = Harness();
        var first = host.Tick(new FrameContext(1, 16.6));

        router.DispatchPointer(new WasmPointerArgs(new WasmWindowHandle(1), 5, 5, WasmPointerButton.Left, true));
        Assert.True(host.IsDirty);
        var second = host.Tick(new FrameContext(2, 33.2));
        Assert.NotSame(first, second);
        Assert.Equal(2, paintable.PaintCalls);

        host.Invalidate();
        Assert.True(host.IsDirty);
        host.Tick(new FrameContext(3, 49.8));
        Assert.Equal(3, paintable.PaintCalls);
    }

    [Fact]
    public async Task Clean_publish_skips_sink_interop()
    {
        var (host, _, _, sink) = Harness();
        var ctx = new FrameContext(1, 16.6);

        var first = await host.PublishAsync("c", sink, ctx);
        Assert.Equal(1, sink.Paints);

        var second = await host.PublishAsync("c", sink, ctx);
        Assert.Equal(first, second);
        Assert.Equal(1, sink.Paints); // no second interop call

        host.Invalidate();
        await host.PublishAsync("c", sink, ctx);
        Assert.Equal(2, sink.Paints);
    }
}
