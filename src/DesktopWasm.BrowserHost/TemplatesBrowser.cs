using Microsoft.JSInterop;

namespace DesktopWasm.BrowserHost;

// Browser attach for the templates lab (ControlTemplate/DataTemplate
// inflation slice). Called once per demo session from Home.razor: installs
// the status channel so readout text and logical rects are readable from JS
// for post-input state assertions.
public static class TemplatesBrowser
{
    public static async Task TryWireAsync(
        string slug, IJSRuntime js, DotNetObjectReference<Pages.Home>? self)
    {
        if (!string.Equals(slug, "templates", StringComparison.OrdinalIgnoreCase)) return;
        _ = self;
        if (TemplatesLab.Current is { } lab)
        {
            lab.Noter = (key, value) =>
            {
                try { _ = js.InvokeVoidAsync("wpfTemplates.note", key, value); }
                catch { }
            };
        }
        try
        {
            await js.InvokeVoidAsync("wpfTemplates.attach").ConfigureAwait(false);
            TemplatesLab.Current?.PublishNotes();
        }
        catch
        {
        }
    }
}
