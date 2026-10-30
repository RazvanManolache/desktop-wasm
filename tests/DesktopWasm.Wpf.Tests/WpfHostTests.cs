using DesktopWasm.Core;
using DesktopWasm.Graphics;
using SkiaSharp;

namespace DesktopWasm.Wpf.Tests;

[ResetAmbientState]
public sealed class WpfHostTests
{
    private const string DemoXaml = """
        <Window Title="Demo">
          <StackPanel>
            <Button Content="OK"/>
            <TextBlock Text="Hello"/>
          </StackPanel>
        </Window>
        """;

    [Fact]
    public void Host_tick_renders_window_to_paint_frame()
    {
        using var surface = new SkiaSurface(800, 600);
        var pump = new WasmDispatcherPump();
        var router = new WasmInputRouter();
        var host = new WasmWpfHost(pump, surface, router);

        var window = XamlLoader.LoadComponent<Window>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(DemoXaml)));
        host.AddWindow(window, new WpfSize(800, 600));

        var frame = host.Tick(new FrameContext(1, 16.6));

        // Same payload shape wfwasm.paint consumes: JSON ops + out-of-band uploads.
        Assert.Contains("OK", frame.Json, StringComparison.Ordinal);
        Assert.Contains("Hello", frame.Json, StringComparison.Ordinal);
        Assert.Empty(frame.Uploads);

        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        Assert.Equal(SKColors.White, bitmap.GetPixel(700, 500)); // uncovered window background
    }

    private sealed class RecordingSink : IFrameSink
    {
        public readonly List<string> Calls = new();
        public string PaintedJson = string.Empty;

        public Task UploadDefAsync(string layerId, byte[] rgba, int width, int height, CancellationToken ct = default)
        {
            Calls.Add($"def:{layerId}");
            return Task.CompletedTask;
        }

        public Task UploadPatchAsync(string layerId, byte[] rgba, int x, int y, int width, int height, CancellationToken ct = default)
        {
            Calls.Add($"patch:{layerId}");
            return Task.CompletedTask;
        }

        public Task<string> PaintJsonAsync(string canvasId, string json, CancellationToken ct = default)
        {
            Calls.Add($"paint:{canvasId}");
            PaintedJson = json;
            return Task.FromResult("painted");
        }
    }

    [Fact]
    public async Task Host_publish_drives_shared_canvas_host_path()
    {
        using var surface = new SkiaSurface(800, 600);
        var host = new WasmWpfHost(new WasmDispatcherPump(), surface, new WasmInputRouter());
        var window = XamlLoader.LoadComponent<Window>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(DemoXaml)));
        host.AddWindow(window, new WpfSize(800, 600));
        var sink = new RecordingSink();

        var result = await host.PublishAsync("main", sink, new FrameContext(1, 16.6));

        Assert.Equal("painted", result);
        Assert.Equal(["paint:main"], sink.Calls); // WPF emits no bitmap uploads
        Assert.Contains("OK", sink.PaintedJson, StringComparison.Ordinal);
        Assert.Contains("Hello", sink.PaintedJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Host_without_surface_renders_and_publishes_json_only()
    {
        // Browser configuration: no Skia surface, no native calls.
        var router = new WasmInputRouter();
        var host = new WasmWpfHost(new WasmDispatcherPump(), router);
        var window = XamlLoader.LoadComponent<Window>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(DemoXaml)));
        host.AddWindow(window, new WpfSize(800, 600));
        var sink = new RecordingSink();

        var frame = host.Tick(new FrameContext(1, 16.6));
        Assert.Contains("OK", frame.Json, StringComparison.Ordinal);
        Assert.Empty(frame.Uploads);

        var result = await host.PublishAsync("main", sink, new FrameContext(2, 33.2));
        Assert.Equal("painted", result);
        Assert.Equal(["paint:main"], sink.Calls);
        Assert.Same(frame, host.Tick(new FrameContext(3, 49.8))); // clean: cached
    }

    [Fact]
    public void FormattedText_ellipsizes_and_draws()
    {
        const string Long = "This is a very long line of text";
        FormattedText Build(double maxWidth) => new(Long, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("sans-serif"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            14, Brushes.Black)
        { MaxTextWidth = maxWidth };
        var full = Build(1000);
        Assert.Equal(Long, full.DisplayText);
        Assert.True(full.Width > 0 && full.Height > 0);

        var narrow = Build(30);
        Assert.True(narrow.Width <= 30, $"ellipsized width {narrow.Width} exceeds 30");
        Assert.True(narrow.DisplayText.Length < Long.Length);

        var dc = new DrawingContext();
        narrow.DrawInto(dc, 0, 0, 0x000000FF);
        var text = Assert.Single(dc.Commands);
        var run = Assert.IsType<DrawCommand.DrawText>(text);
        Assert.Equal(narrow.DisplayText, run.Text);
    }
}
