using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Regression tests for review findings P1-2 (queued updates rendered but
/// never published) and P1-3 (a failed publish suppresses recovery).
/// Each test reproduces the recorded probe behaviour from
/// docs/review-evidence-2026-10-22/ and asserts the repaired behaviour.
/// </summary>
[ResetAmbientState]
public sealed class PublishRevisionTests
{
    private sealed class ProbeSink : IFrameSink
    {
        public int Attempts;
        public int Paints;
        public string Json = string.Empty;
        public string LastCanvasId = string.Empty;
        public bool FailNextPaint;

        public Task UploadDefAsync(string layerId, byte[] rgba, int width, int height, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task UploadPatchAsync(string layerId, byte[] rgba, int x, int y, int width, int height, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<string> PaintJsonAsync(string canvasId, string json, CancellationToken ct = default)
        {
            Attempts++;
            if (FailNextPaint)
            {
                FailNextPaint = false;
                throw new InvalidOperationException("synthetic failure");
            }

            Paints++;
            Json = json;
            LastCanvasId = canvasId;
            return Task.FromResult($"painted-{Paints}");
        }
    }

    private sealed class GatedSink : IFrameSink
    {
        public int Paints;
        public readonly TaskCompletionSource PaintEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<string> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task UploadDefAsync(string layerId, byte[] rgba, int width, int height, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task UploadPatchAsync(string layerId, byte[] rgba, int x, int y, int width, int height, CancellationToken ct = default) =>
            Task.CompletedTask;

        public async Task<string> PaintJsonAsync(string canvasId, string json, CancellationToken ct = default)
        {
            Paints++;
            PaintEntered.TrySetResult();
            return await Release.Task.ConfigureAwait(false);
        }
    }

    private static (WasmWpfHost Host, WasmDispatcherPump Pump, TextBlock Label, ProbeSink Sink) Harness(string text = "initial")
    {
        var pump = new WasmDispatcherPump();
        var host = new WasmWpfHost(pump, new WasmInputRouter());
        var label = new TextBlock { Text = text };
        host.AddWindow(new Window { Content = label }, new WpfSize(100, 100));
        return (host, pump, label, new ProbeSink());
    }

    [Fact]
    public async Task QueuedUpdate_PublishesAfterPump_ExactlyOnce()
    {
        // Probe: internal JSON had the new label but the sink kept one paint
        // with the old visible frame.
        var (host, pump, label, sink) = Harness("initial");

        await host.PublishAsync("c", sink, new FrameContext(1, 0));
        Assert.Equal(1, sink.Paints);
        Assert.Contains("initial", sink.Json, StringComparison.Ordinal);

        pump.Post(() => { label.Text = "queued"; host.Invalidate(); });
        await host.PublishAsync("c", sink, new FrameContext(2, 0));

        Assert.Equal(2, sink.Paints);
        Assert.Contains("queued", sink.Json, StringComparison.Ordinal);
        Assert.DoesNotContain("initial", sink.Json, StringComparison.Ordinal);

        // Clean re-publish must not present again: visible exactly once.
        var cached = await host.PublishAsync("c", sink, new FrameContext(3, 0));
        Assert.Equal(2, sink.Paints);
        Assert.Equal("painted-2", cached);
    }

    [Fact]
    public async Task FailedPublish_RetriesLatestFrame_InsteadOfStaleSuccess()
    {
        // Probe: one success, one throwing publish, then only two attempts
        // total and the third call reported the old success.
        var (host, _, label, sink) = Harness("v1");

        var first = await host.PublishAsync("c", sink, new FrameContext(1, 0));
        Assert.Equal("painted-1", first);

        label.Text = "v2";
        host.Invalidate();
        sink.FailNextPaint = true;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.PublishAsync("c", sink, new FrameContext(2, 0)));
        Assert.Equal(2, sink.Attempts);
        Assert.Equal(1, sink.Paints);

        var retry = await host.PublishAsync("c", sink, new FrameContext(3, 0));
        Assert.Equal(3, sink.Attempts);
        Assert.Equal(2, sink.Paints);
        Assert.NotEqual(first, retry);
        Assert.Contains("v2", sink.Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelledPublish_RetainsPendingWork_ForRetry()
    {
        var (host, _, label, sink) = Harness("v1");

        await host.PublishAsync("c", sink, new FrameContext(1, 0));
        Assert.Equal(1, sink.Paints);

        label.Text = "v2";
        host.Invalidate();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => host.PublishAsync("c", sink, new FrameContext(2, 0), cts.Token));
        Assert.Equal(1, sink.Paints);

        var retry = await host.PublishAsync("c", sink, new FrameContext(3, 0));
        Assert.Equal(2, sink.Paints);
        Assert.Equal("painted-2", retry);
        Assert.Contains("v2", sink.Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CanvasSwitch_RepublishesCompleteFrame_WithoutInvalidation()
    {
        var (host, _, _, sink) = Harness("hello");

        await host.PublishAsync("c1", sink, new FrameContext(1, 0));
        Assert.Equal(1, sink.Paints);

        await host.PublishAsync("c2", sink, new FrameContext(2, 0));
        Assert.Equal(2, sink.Paints);
        Assert.Equal("c2", sink.LastCanvasId);
        Assert.Contains("hello", sink.Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddWindow_AfterCleanFrame_InvalidatesAndPresents()
    {
        var (host, _, _, sink) = Harness("one");

        await host.PublishAsync("c", sink, new FrameContext(1, 0));
        await host.PublishAsync("c", sink, new FrameContext(2, 0));
        Assert.Equal(1, sink.Paints);

        host.AddWindow(new Window { Content = new TextBlock { Text = "two" } }, new WpfSize(100, 100));
        await host.PublishAsync("c", sink, new FrameContext(3, 0));

        Assert.Equal(2, sink.Paints);
        Assert.Contains("two", sink.Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OverlappingPublishes_Serialize_AndPresentLatestOnce()
    {
        var pump = new WasmDispatcherPump();
        var host = new WasmWpfHost(pump, new WasmInputRouter());
        host.AddWindow(new Window { Content = new TextBlock { Text = "solo" } }, new WpfSize(100, 100));
        var sink = new GatedSink();

        var first = host.PublishAsync("c", sink, new FrameContext(1, 0));
        await sink.PaintEntered.Task;
        var second = host.PublishAsync("c", sink, new FrameContext(2, 0));
        sink.Release.TrySetResult("painted-gated");

        Assert.Equal("painted-gated", await first);
        Assert.Equal("painted-gated", await second);
        Assert.Equal(1, sink.Paints);
    }
}
