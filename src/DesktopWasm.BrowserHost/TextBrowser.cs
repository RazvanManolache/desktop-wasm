using Microsoft.JSInterop;

namespace DesktopWasm.BrowserHost;

// Browser attach for the text lab (engine-native ScreenToGif
// typeface/decoration/DPI slice). Called once per demo session from
// Home.razor: installs the status channel so readout text and logical rects
// are readable from JS for post-input state assertions.
public static class TextBrowser
{
    public static async Task TryWireAsync(
        string slug, IJSRuntime js, DotNetObjectReference<Pages.Home>? self)
    {
        if (!string.Equals(slug, "text", StringComparison.OrdinalIgnoreCase)) return;
        _ = self;
        if (TextLab.Current is { } lab)
        {
            lab.Noter = (key, value) =>
            {
                try { _ = js.InvokeVoidAsync("wpfText.note", key, value); }
                catch { }
            };
        }
        try
        {
            await js.InvokeVoidAsync("wpfText.attach").ConfigureAwait(false);
            TextLab.Current?.PublishNotes();
        }
        catch
        {
        }
    }
}
