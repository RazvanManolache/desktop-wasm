using DesktopWasm.Wpf;
using Microsoft.JSInterop;

namespace DesktopWasm.BrowserHost;

// Browser attach for the announcements & preferences lab (engine-native
// Gallery automation slice). Announcement text crosses into a DOM aria-live
// region so assistive tech voices what the engine readout paints; the
// engine readout stays the source of truth when JS is unavailable.
public static class A11yEventsBrowser
{
    public static async Task TryWireAsync(string slug, WasmWpfHost host, IJSRuntime js)
    {
        if (!string.Equals(slug, "a11y-events", StringComparison.OrdinalIgnoreCase)) return;
        if (A11yEventsLab.Current is not { } lab) return;
        lab.AnnounceNote = (text, activityId) =>
        {
            try { _ = js.InvokeVoidAsync("wpfA11y.announce", text, activityId); }
            catch { }
        };
        _ = host;
        await Task.CompletedTask;
    }
}
