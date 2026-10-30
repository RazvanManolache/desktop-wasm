using System.Text.Json;
using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Session-memory regression tests: the gallery OOM. Every dirty frame used
/// to re-encode each card image to base64 inside the frame JSON (managed
/// strings, ~1 MB per publish for 38 staged cards), growing the fixed WASM
/// heap to its 2 GB cap on extended scroll sessions while the JS heap stayed
/// flat. Frames now upload identical pixel buffers once and repaint by
/// layer reference; republish transients stay small and bounded.
/// </summary>
[ResetAmbientState]
public sealed class ImageUploadCacheTests
{
    private sealed class ImageStrip : FrameworkElement
    {
        public byte[] Pixels = [255, 0, 0, 255];
        public int PixelWidth = 1;
        public int PixelHeight = 1;

        protected override WpfSize MeasureCore(WpfSize available) => new(PixelWidth, PixelHeight);

        protected override void OnRender(DrawingContext dc) =>
            dc.DrawImage(Pixels, PixelWidth, PixelHeight, 0, 0, PixelWidth, PixelHeight);
    }

    private sealed class RecordingSink : IFrameSink
    {
        public int Defs;
        public long DefBytes;
        public int Paints;
        public long JsonChars;
        public string LastJson = string.Empty;
        public bool FailNextPaint;

        public Task UploadDefAsync(string layerId, byte[] rgba, int width, int height, CancellationToken ct = default)
        {
            Defs++;
            DefBytes += rgba.Length;
            return Task.CompletedTask;
        }

        public Task UploadPatchAsync(string layerId, byte[] rgba, int x, int y, int width, int height, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<string> PaintJsonAsync(string canvasId, string json, CancellationToken ct = default)
        {
            if (FailNextPaint)
            {
                FailNextPaint = false;
                throw new InvalidOperationException("synthetic paint failure");
            }
            Paints++;
            JsonChars += json.Length;
            LastJson = json;
            return Task.FromResult("painted");
        }
    }

    private static (WasmWpfHost Host, RecordingSink Sink) ImageHarness(params ImageStrip[] strips)
    {
        var host = new WasmWpfHost(new WasmDispatcherPump(), new WasmInputRouter());
        var panel = new StackPanel();
        foreach (var strip in strips) panel.AddChild(strip);
        host.AddWindow(new Window { Content = panel }, new WpfSize(800, 600));
        return (host, new RecordingSink());
    }

    private static byte[] Pixels(byte r, byte g, byte b, int width = 4, int height = 4)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
            pixels[i + 3] = 255;
        }
        return pixels;
    }

    private static List<int> OpCodes(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(op => op[0].GetInt32()).ToList();
    }

    [Fact]
    public async Task Identical_pixels_upload_once_then_repaint_by_reference()
    {
        var shared = Pixels(10, 20, 30);
        var first = new ImageStrip { Pixels = shared, PixelWidth = 4, PixelHeight = 4 };
        var second = new ImageStrip { Pixels = shared, PixelWidth = 4, PixelHeight = 4 };
        var (host, sink) = ImageHarness(first, second);

        await host.PublishAsync("c", sink, new FrameContext(1, 0));
        Assert.Equal(1, sink.Defs);
        Assert.Equal(shared.Length, sink.DefBytes);
        var firstOps = OpCodes(sink.LastJson);
        Assert.DoesNotContain(7, firstOps);
        Assert.Equal(2, firstOps.Count(op => op == 9));
        Assert.Single(firstOps, 10);

        host.Invalidate();
        await host.PublishAsync("c", sink, new FrameContext(2, 16.6));
        Assert.Equal(1, sink.Defs);
        Assert.Equal(shared.Length, sink.DefBytes);
        var secondOps = OpCodes(sink.LastJson);
        Assert.DoesNotContain(7, secondOps);
        Assert.Equal(2, secondOps.Count(op => op == 9));
    }

    [Fact]
    public async Task Mutated_pixels_reupload_on_next_publish()
    {
        var buffer = Pixels(10, 20, 30);
        var strip = new ImageStrip { Pixels = buffer, PixelWidth = 4, PixelHeight = 4 };
        var (host, sink) = ImageHarness(strip);

        await host.PublishAsync("c", sink, new FrameContext(1, 0));
        Assert.Equal(1, sink.Defs);

        buffer[0] = 200;
        host.Invalidate();
        await host.PublishAsync("c", sink, new FrameContext(2, 16.6));
        Assert.Equal(2, sink.Defs);
        Assert.Equal(2 * buffer.Length, sink.DefBytes);
    }

    [Fact]
    public async Task Republish_transients_stay_small_and_stable()
    {
        var strips = Enumerable.Range(0, 12)
            .Select(i => new ImageStrip { Pixels = Pixels((byte)i, 9, 9, 16, 16), PixelWidth = 16, PixelHeight = 16 })
            .ToArray();
        var (host, sink) = ImageHarness(strips);

        await host.PublishAsync("c", sink, new FrameContext(1, 0));
        var firstJson = sink.LastJson.Length;
        var firstUploads = sink.DefBytes;
        Assert.True(firstUploads > 0);

        for (var i = 2; i <= 12; i++)
        {
            host.Invalidate();
            await host.PublishAsync("c", sink, new FrameContext(i, i * 16.6));
        }

        Assert.Equal(firstUploads, sink.DefBytes);
        Assert.True(sink.LastJson.Length <= 4096, $"republish JSON {sink.LastJson.Length} chars exceeds the reference-only budget");
        Assert.True(sink.LastJson.Length <= firstJson);
        var telemetry = host.GetTelemetry();
        Assert.Equal(12, telemetry.Paints);
        Assert.Equal(12, telemetry.GeneratedRevision);
        Assert.Equal(firstUploads, telemetry.TotalUploadBytes);
        Assert.Equal(strips.Length, telemetry.ImageCount);
    }

    [Fact]
    public void Cache_bounds_distinct_images_and_evicts_lru()
    {
        var cache = new ImageUploadCache();
        string firstId = string.Empty;
        for (var i = 0; i < ImageUploadCache.MaxImages + 50; i++)
        {
            var (id, _) = cache.GetOrAdd(Pixels((byte)(i % 251), (byte)(i / 251), 7, 2, 2), 2, 2);
            if (i == 0) firstId = id;
            cache.EndFrame([id], i + 1);
        }
        Assert.True(cache.Count <= ImageUploadCache.MaxImages);
        Assert.True(cache.Bytes <= ImageUploadCache.MaxBytes);

        var (revived, isNew) = cache.GetOrAdd(Pixels(0, 0, 7, 2, 2), 2, 2);
        Assert.Equal(firstId, revived);
        Assert.True(isNew);
    }

    [Fact]
    public async Task Skipped_frame_replays_on_a_fresh_renderer_without_missing_layers()
    {
        // GPU replay coalescing: the renderer may only ever see the second of
        // two frames. Every frame carries defs for its whole live set, so a
        // fresh renderer builds all layers from the latest frame alone.
        var strip = new ImageStrip { Pixels = Pixels(200, 30, 40, 8, 8), PixelWidth = 8, PixelHeight = 8 };
        var (host, sink) = ImageHarness(strip);

        await host.PublishAsync("c", sink, new FrameContext(1, 0));
        host.Invalidate();
        await host.PublishAsync("c", sink, new FrameContext(2, 16.6));

        using var surface = new SkiaSurface(800, 600);
        using var renderer = new SkiaCommandRenderer();
        var exception = Record.Exception(() => renderer.Replay(surface.Canvas, host.CurrentCommands));
        Assert.Null(exception);
    }

    [Fact]
    public void Replayed_blits_paint_source_pixels()
    {
        var strip = new ImageStrip { Pixels = Pixels(200, 30, 40, 8, 8), PixelWidth = 8, PixelHeight = 8 };
        using var surface = new SkiaSurface(800, 600);
        var host = new WasmWpfHost(new WasmDispatcherPump(), surface, new WasmInputRouter());
        var panel = new StackPanel();
        panel.AddChild(strip);
        host.AddWindow(new Window { Content = panel }, new WpfSize(800, 600));
        host.Tick(new FrameContext(1, 0));

        using var bitmap = SkiaSharp.SKBitmap.FromImage(surface.Snapshot());
        var painted = false;
        for (var y = 0; y < 100 && !painted; y++)
            for (var x = 0; x < 100 && !painted; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red == 200 && pixel.Green == 30 && pixel.Blue == 40) painted = true;
            }
        Assert.True(painted, "Blitted layer pixels never reached the shared surface.");
    }

    [Fact]
    public async Task Canvas_switch_reuploads_live_layers()
    {
        var strip = new ImageStrip { Pixels = Pixels(10, 20, 30, 4, 4), PixelWidth = 4, PixelHeight = 4 };
        var (host, sink) = ImageHarness(strip);

        await host.PublishAsync("c1", sink, new FrameContext(1, 0));
        Assert.Equal(1, sink.Defs);

        await host.PublishAsync("c2", sink, new FrameContext(2, 16.6));
        Assert.Equal(2, sink.Paints);
        Assert.Equal(2, sink.Defs);
    }

    [Fact]
    public async Task Sink_change_reuploads_live_layers()
    {
        var strip = new ImageStrip { Pixels = Pixels(10, 20, 30, 4, 4), PixelWidth = 4, PixelHeight = 4 };
        var (host, sink) = ImageHarness(strip);

        await host.PublishAsync("c", sink, new FrameContext(1, 0));
        Assert.Equal(1, sink.Defs);

        host.NoteSinkChanged();
        await host.PublishAsync("c", sink, new FrameContext(2, 16.6));
        Assert.Equal(2, sink.Paints);
        Assert.Equal(2, sink.Defs);
    }

    [Fact]
    public async Task Failed_publish_reuploads_layers_on_next_frame()
    {
        var strip = new ImageStrip { Pixels = Pixels(10, 20, 30, 4, 4), PixelWidth = 4, PixelHeight = 4 };
        var (host, sink) = ImageHarness(strip);

        await host.PublishAsync("c", sink, new FrameContext(1, 0));
        Assert.Equal(1, sink.Defs);

        // The failed frame introduces a new layer: its def was uploaded before
        // the paint threw, and the retry re-sends it (presented tracking was
        // reset by the failure) instead of assuming it arrived.
        strip.Pixels = Pixels(11, 21, 31, 4, 4);
        host.Invalidate();
        sink.FailNextPaint = true;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.PublishAsync("c", sink, new FrameContext(2, 16.6)));
        Assert.Equal(2, sink.Defs);

        await host.PublishAsync("c", sink, new FrameContext(3, 33.2));
        Assert.Equal(2, sink.Paints);
        Assert.Equal(3, sink.Defs);
    }

    [Fact]
    public void Telemetry_reports_counters_and_heap_watermark()
    {
        var strip = new ImageStrip();
        var (host, _) = ImageHarness(strip);
        var before = host.GetTelemetry();
        Assert.Equal(0, before.Paints);
        Assert.Equal(0, before.SkippedPublishes);

        host.Tick(new FrameContext(1, 0));
        var afterTick = host.GetTelemetry();
        Assert.Equal(1, afterTick.GeneratedRevision);
        Assert.True(afterTick.ManagedHeapBytes > 0);
        Assert.True(afterTick.ManagedHeapHighWatermark >= afterTick.ManagedHeapBytes);
        Assert.True(before.ManagedHeapHighWatermark <= afterTick.ManagedHeapHighWatermark);
    }
}
