using Microsoft.JSInterop;

namespace DesktopWasm.BrowserHost;

// Browser attach for the shell/windowing lab (engine-native ScreenToGif
// shell slice). Called once per demo session from Home.razor: installs the
// status channel so the size, progress, action and split readouts plus
// logical rects are readable from JS for post-input state assertions.
public static class ShellBrowser
{
    public static async Task TryWireAsync(
        string slug, IJSRuntime js, DotNetObjectReference<Pages.Home>? self)
    {
        if (!string.Equals(slug, "shell", StringComparison.OrdinalIgnoreCase)) return;
        _ = self;
        if (ShellLab.Current is { } lab)
        {
            lab.Noter = (key, value) =>
            {
                try { _ = js.InvokeVoidAsync("wpfShell.note", key, value); }
                catch { }
            };
        }
        try
        {
            await js.InvokeVoidAsync("wpfShell.attach").ConfigureAwait(false);
            ShellLab.Current?.PublishNotes();
        }
        catch
        {
        }
    }
}
