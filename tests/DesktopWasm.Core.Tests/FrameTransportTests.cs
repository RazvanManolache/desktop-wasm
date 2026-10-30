using DesktopWasm.Graphics;

namespace DesktopWasm.Core.Tests;

public sealed class FrameTransportTests
{
    private sealed class RecordingSink : IFrameSink
    {
        public readonly List<string> Calls = new();
        public string PaintedJson = string.Empty;

        public Task UploadDefAsync(string layerId, byte[] rgba, int width, int height, CancellationToken ct = default)
        {
            Calls.Add($"def:{layerId}:{width}x{height}:{rgba.Length}");
            return Task.CompletedTask;
        }

        public Task UploadPatchAsync(string layerId, byte[] rgba, int x, int y, int width, int height, CancellationToken ct = default)
        {
            Calls.Add($"patch:{layerId}@{x},{y}:{width}x{height}");
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
    public async Task Publishes_uploads_before_paint_and_passes_result_through()
    {
        var sink = new RecordingSink();
        DrawCommand[] uploads =
        [
            new DrawCommand.DefBitmap("l1", new byte[16], 2, 2),
            new DrawCommand.PatchBitmap("l1", new byte[4], 0, 0, 1, 1),
            new DrawCommand.FillRect(0, 0, 1, 1, 0xFFFFFFFF), // not a bitmap: ignored
        ];
        var json = DrawCommandJson.Serialize([
            new DrawCommand.BlitBitmap("l1", 0, 0, 2, 2),
        ]);

        var result = await FramePublisher.PublishAsync(sink, "c1", json, uploads);

        Assert.Equal("painted", result);
        Assert.Equal(["def:l1:2x2:16", "patch:l1@0,0:1x1", "paint:c1"], sink.Calls);
        Assert.Same(json, sink.PaintedJson);
    }

    [Fact]
    public async Task Cancelled_publish_paints_nothing()
    {
        var sink = new RecordingSink();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            FramePublisher.PublishAsync(sink, "c1", "[]", [], cts.Token));
        Assert.Empty(sink.Calls);
    }
}
