using Microsoft.JSInterop;

namespace DesktopWasm.BrowserHost;

// Browser attach for the primitives lab (engine-native ScreenToGif
// Thumb/RepeatButton/Popup slice). Called once per demo session from
// Home.razor: installs the status channel so readout text and logical rects
// are readable from JS for post-input state assertions.
public static class PrimitivesBrowser
{
    public static async Task TryWireAsync(
        string slug, IJSRuntime js, DotNetObjectReference<Pages.Home>? self)
    {
        if (!string.Equals(slug, "primitives", StringComparison.OrdinalIgnoreCase)) return;
        _ = self;
        if (PrimitivesLab.Current is { } lab)
        {
            lab.Noter = (key, value) =>
            {
                try { _ = js.InvokeVoidAsync("wpfPrimitives.note", key, value); }
                catch { }
            };
        }
        try
        {
            await js.InvokeVoidAsync("wpfPrimitives.attach").ConfigureAwait(false);
            PrimitivesLab.Current?.PublishNotes();
        }
        catch
        {
        }
    }
}
