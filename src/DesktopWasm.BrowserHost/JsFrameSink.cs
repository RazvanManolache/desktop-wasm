using DesktopWasm.Graphics;
using Microsoft.JSInterop;

namespace DesktopWasm.BrowserHost;

/// <summary>IFrameSink over Blazor IJSRuntime (wfwasm.texDef/texPatch/paint).</summary>
public sealed class JsFrameSink(IJSRuntime js) : IFrameSink
{
    private readonly IJSRuntime _js = js;

    public Task UploadDefAsync(string layerId, byte[] rgba, int width, int height, CancellationToken ct = default) =>
        _js.InvokeVoidAsync("wfwasm.texDef", ct, [layerId, Convert.ToBase64String(rgba), width, height]).AsTask();

    public Task UploadPatchAsync(string layerId, byte[] rgba, int x, int y, int width, int height, CancellationToken ct = default) =>
        _js.InvokeVoidAsync("wfwasm.texPatch", ct, [layerId, Convert.ToBase64String(rgba), x, y, width, height]).AsTask();

    public Task<string> PaintJsonAsync(string canvasId, string json, CancellationToken ct = default) =>
        _js.InvokeAsync<string>("wfwasm.paint", ct, [canvasId, json, 0]).AsTask();
}
