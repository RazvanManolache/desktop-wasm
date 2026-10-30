using Microsoft.JSInterop;

namespace DesktopWasm.BrowserHost;

// Browser attach for the grid-rows lab (engine-native ScreenToGif
// DataGrid row/cell/sort slice). Called once per demo session from
// Home.razor: installs the status channel so selection, first-row,
// cell-text, row-index and sort readouts are readable from JS for
// post-input state assertions.
public static class GridRowsBrowser
{
    public static async Task TryWireAsync(
        string slug, IJSRuntime js, DotNetObjectReference<Pages.Home>? self)
    {
        if (!string.Equals(slug, "datagrid", StringComparison.OrdinalIgnoreCase)) return;
        _ = self;
        if (GridRowsLab.Current is { } lab)
        {
            lab.Noter = (key, value) =>
            {
                try { _ = js.InvokeVoidAsync("wpfGridRows.note", key, value); }
                catch { }
            };
        }
        try
        {
            await js.InvokeVoidAsync("wpfGridRows.attach").ConfigureAwait(false);
            GridRowsLab.Current?.PublishNotes();
        }
        catch
        {
        }
    }
}
