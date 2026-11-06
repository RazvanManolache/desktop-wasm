using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Clipboard device-bridge regression (PUSH-1106-wpf fallback path):
/// permission denial and cancellation surface as real states, never silent
/// no-ops, and a failed pull never invents session content.
/// </summary>
[ResetAmbientState]
public sealed class ClipboardBridgeTests
{
    private sealed class DenyingClipboard : IWasmClipboard
    {
        public Task<string?> GetTextAsync(CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task SetTextAsync(string text, CancellationToken ct = default) =>
            Task.FromException(new UnauthorizedAccessException("denied"));
    }

    private sealed class RecordingClipboard : IWasmClipboard
    {
        public int Sets;
        public int Gets;

        public Task<string?> GetTextAsync(CancellationToken ct = default)
        {
            Gets++;
            return Task.FromResult<string?>("device");
        }

        public Task SetTextAsync(string text, CancellationToken ct = default)
        {
            Sets++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Flush_denial_throws_and_keeps_session_text()
    {
        var prior = Clipboard.Provider;
        Clipboard.Provider = new DenyingClipboard();
        try
        {
            Clipboard.Clear();
            Clipboard.SetText("hello");
            var error = await Assert.ThrowsAsync<ClipboardException>(() => Clipboard.FlushAsync());
            Assert.Contains("denied", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("hello", Clipboard.GetText());
        }
        finally
        {
            Clipboard.Provider = prior;
            Clipboard.Clear();
        }
    }

    [Fact]
    public async Task Refresh_null_read_throws_and_keeps_session_text()
    {
        var prior = Clipboard.Provider;
        Clipboard.Provider = new DenyingClipboard();
        try
        {
            Clipboard.Clear();
            Clipboard.SetText("kept");
            var error = await Assert.ThrowsAsync<ClipboardException>(() => Clipboard.RefreshAsync());
            Assert.Contains("no text", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("kept", Clipboard.GetText());
        }
        finally
        {
            Clipboard.Provider = prior;
            Clipboard.Clear();
        }
    }

    [Fact]
    public async Task Flush_honors_cancellation_before_touching_device()
    {
        var prior = Clipboard.Provider;
        var device = new RecordingClipboard();
        Clipboard.Provider = device;
        try
        {
            Clipboard.Clear();
            Clipboard.SetText("hello");
            using var denied = new CancellationTokenSource();
            denied.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => Clipboard.FlushAsync(denied.Token));
            Assert.Equal(0, device.Sets);
        }
        finally
        {
            Clipboard.Provider = prior;
            Clipboard.Clear();
        }
    }

    [Fact]
    public async Task Flush_then_refresh_round_trips_through_device()
    {
        var prior = Clipboard.Provider;
        Clipboard.Provider = new InMemoryClipboard();
        try
        {
            Clipboard.Clear();
            Clipboard.SetText("abc");
            await Clipboard.FlushAsync();
            Clipboard.Clear();
            Assert.Equal(string.Empty, Clipboard.GetText());
            await Clipboard.RefreshAsync();
            Assert.Equal("abc", Clipboard.GetText());
        }
        finally
        {
            Clipboard.Provider = prior;
            Clipboard.Clear();
        }
    }
}
