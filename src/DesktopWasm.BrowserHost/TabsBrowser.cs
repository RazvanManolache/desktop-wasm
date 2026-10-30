using Microsoft.JSInterop;

namespace DesktopWasm.BrowserHost;

// Browser attach for the tabs lab (engine-native ScreenToGif
// TabControl/ProgressBar/ToolTip slice). Called once per demo session from
// Home.razor: installs the status channel so readout text and logical rects
// are readable from JS for post-input state assertions.
public static class TabsBrowser
{
    public static async Task TryWireAsync(
        string slug, IJSRuntime js, DotNetObjectReference<Pages.Home>? self)
    {
        if (!string.Equals(slug, "tabs", StringComparison.OrdinalIgnoreCase)) return;
        _ = self;
        if (TabsLab.Current is { } lab)
        {
            lab.Noter = (key, value) =>
            {
                try { _ = js.InvokeVoidAsync("wpfTabs.note", key, value); }
                catch { }
            };
        }
        try
        {
            await js.InvokeVoidAsync("wpfTabs.attach").ConfigureAwait(false);
            TabsLab.Current?.PublishNotes();
        }
        catch
        {
        }
    }
}
