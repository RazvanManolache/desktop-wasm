using Microsoft.JSInterop;

namespace DesktopWasm.BrowserHost;

// Browser attach for the effects/hosts lab (engine-native ScreenToGif
// effects/host cluster). Called once per demo session from Home.razor:
// installs the status channel so the shadow, blur, masked-typing, page
// and back readouts plus logical rects are readable from JS for
// post-input state assertions.
public static class EffectsBrowser
{
    public static async Task TryWireAsync(
        string slug, IJSRuntime js, DotNetObjectReference<Pages.Home>? self)
    {
        if (!string.Equals(slug, "effects", StringComparison.OrdinalIgnoreCase)) return;
        _ = self;
        if (EffectsLab.Current is { } lab)
        {
            lab.Noter = (key, value) =>
            {
                try { _ = js.InvokeVoidAsync("wpfEffects.note", key, value); }
                catch { }
            };
        }
        try
        {
            await js.InvokeVoidAsync("wpfEffects.attach").ConfigureAwait(false);
            EffectsLab.Current?.PublishNotes();
        }
        catch
        {
        }
    }
}
