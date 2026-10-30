using Microsoft.JSInterop;

namespace DesktopWasm.BrowserHost;

// Browser attach for the hit-test lab (DynamicResource + hit-test slice).
// Called once per demo session from Home.razor: installs the status channel
// so readout text and logical rects are readable from JS for post-input
// state assertions.
public static class HitTestBrowser
{
    public static async Task TryWireAsync(
        string slug, IJSRuntime js, DotNetObjectReference<Pages.Home>? self)
    {
        if (!string.Equals(slug, "hittest", StringComparison.OrdinalIgnoreCase)) return;
        _ = self;
        if (HitTestLab.Current is { } lab)
        {
            lab.Noter = (key, value) =>
            {
                try { _ = js.InvokeVoidAsync("wpfHitTest.note", key, value); }
                catch { }
            };
        }
        try
        {
            await js.InvokeVoidAsync("wpfHitTest.attach").ConfigureAwait(false);
            HitTestLab.Current?.PublishNotes();
        }
        catch
        {
        }
    }
}
