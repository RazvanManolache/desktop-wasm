namespace DesktopWasm.Graphics;

/// <summary>
/// Browser-side frame consumer. Implementations forward to the JS bridge:
/// uploads via TexDef/TexPatch ([JSImport] globals), then one
/// wfwasm.paint(canvasId, json) call. Mirrors the WinForms
/// CanvasBridge.PaintAsync split (binary out-of-band, JSON in-band).
/// </summary>
public interface IFrameSink
{
    Task UploadDefAsync(string layerId, byte[] rgba, int width, int height, CancellationToken ct = default);
    Task UploadPatchAsync(string layerId, byte[] rgba, int x, int y, int width, int height, CancellationToken ct = default);
    Task<string> PaintJsonAsync(string canvasId, string json, CancellationToken ct = default);
}

/// <summary>
/// Publishes a split frame in bridge order: every binary upload first
/// (defs before their patches, in record order), then exactly one paint call.
/// Non-bitmap records in <paramref name="uploads"/> are ignored.
/// </summary>
public static class FramePublisher
{
    public static async Task<string> PublishAsync(
        IFrameSink sink,
        string canvasId,
        string json,
        IReadOnlyList<DrawCommand> uploads,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(canvasId);
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(uploads);

        foreach (var upload in uploads)
        {
            ct.ThrowIfCancellationRequested();
            switch (upload)
            {
                case DrawCommand.DefBitmap d:
                    await sink.UploadDefAsync(d.LayerId, d.Rgba, d.Width, d.Height, ct).ConfigureAwait(false);
                    break;
                case DrawCommand.PatchBitmap p:
                    await sink.UploadPatchAsync(p.LayerId, p.Rgba, p.X, p.Y, p.Width, p.Height, ct).ConfigureAwait(false);
                    break;
            }
        }

        ct.ThrowIfCancellationRequested();
        return await sink.PaintJsonAsync(canvasId, json, ct).ConfigureAwait(false);
    }
}
