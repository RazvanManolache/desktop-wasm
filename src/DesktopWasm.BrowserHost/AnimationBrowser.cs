using Microsoft.JSInterop;

namespace DesktopWasm.BrowserHost;

// Browser attach for the animation lab (engine-native ScreenToGif
// HideableTabControl slice). Called once per demo session from
// Home.razor: installs the status channel so readout text and logical
// rects are readable from JS for post-input state assertions.
public static class AnimationBrowser
{
    public static async Task TryWireAsync(
        string slug, IJSRuntime js, DotNetObjectReference<Pages.Home>? self)
    {
        if (!string.Equals(slug, "animation", StringComparison.OrdinalIgnoreCase)) return;
        _ = self;
        if (AnimationLab.Current is { } lab)
        {
            lab.Noter = (key, value) =>
            {
                try { _ = js.InvokeVoidAsync("wpfAnimation.note", key, value); }
                catch { }
            };
        }
        try
        {
            await js.InvokeVoidAsync("wpfAnimation.attach").ConfigureAwait(false);
            AnimationLab.Current?.PublishNotes();
        }
        catch
        {
        }
    }
}
