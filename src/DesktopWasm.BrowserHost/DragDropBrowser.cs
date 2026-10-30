using Microsoft.JSInterop;

namespace DesktopWasm.BrowserHost;

// Browser attach for the drag-drop lab (engine-native ScreenToGif
// drag-drop/data-object + HwndSource slice). Called once per demo session
// from Home.razor: installs the HTML5 file-drop listener on the shared
// input surface. Dropped bytes arrive through Home.OnBrowserFilesDropped
// into DragDropLab.BrowserDrop, which runs the same AcceptFiles core as the
// simulated buttons, so the browser path exercises the real routed events.
public static class DragDropBrowser
{
    public static async Task TryWireAsync(
        string slug, IJSRuntime js, DotNetObjectReference<Pages.Home>? self)
    {
        if (!string.Equals(slug, "drag-drop", StringComparison.OrdinalIgnoreCase)) return;
        if (self is null) return;
        if (DragDropLab.Current is { } lab)
        {
            lab.Noter = (key, value) =>
            {
                try { _ = js.InvokeVoidAsync("wpfDragDrop.note", key, value); }
                catch { }
            };
        }
        try
        {
            await js.InvokeVoidAsync("wpfDragDrop.attach", "inputSurface", self).ConfigureAwait(false);
            DragDropLab.Current?.PublishNotes();
        }
        catch
        {
        }
    }
}
