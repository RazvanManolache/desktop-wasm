using System.Text.Json;
using DesktopWasm.Core;
using DesktopWasm.Wpf;
using Microsoft.JSInterop;

namespace DesktopWasm.BrowserHost;

// Browser attach for the files & clipboard lab (engine-native Gallery
// FileAndFolderDialogsPage/ClipboardPage slice). Called once per demo
// session from Home.razor: installs the navigator.clipboard device bridge,
// the file-picker bridge and the JS status mirror. Sync engine APIs stay
// session-local; only the lab's explicit Flush/Refresh and device buttons
// cross into browser mediation, so denial and cancellation surface instead
// of being skipped.
public static class FileDialogDemoBrowser
{
    public static async Task TryWireAsync(string slug, WasmWpfHost host, IJSRuntime js)
    {
        if (!string.Equals(slug, "files-clipboard", StringComparison.OrdinalIgnoreCase)) return;
        if (FilesClipboardLab.Current is not { } lab) return;
        Clipboard.Provider = new BrowserClipboard(js);
        lab.DeviceBridge = new JsDeviceBridge(js);
        lab.Noter = (key, value) =>
        {
            try { _ = js.InvokeVoidAsync("wpfFileDialogs.note", key, value); }
            catch { }
        };
        lab.PublishNotes();
        _ = host;
        await Task.CompletedTask;
    }

    private sealed class BrowserClipboard(IJSRuntime js) : IWasmClipboard
    {
        public async Task<string?> GetTextAsync(CancellationToken ct = default)
        {
            try
            {
                return await js.InvokeAsync<string>("wpfFileDialogs.readClipboardText", ct).ConfigureAwait(false);
            }
            catch
            {
                return null; // denied/unavailable: RefreshAsync turns this into a loud ClipboardException
            }
        }

        public async Task SetTextAsync(string text, CancellationToken ct = default)
        {
            // Denial must stay loud: rejections propagate into FlushAsync's ClipboardException.
            await js.InvokeVoidAsync("wpfFileDialogs.writeClipboardText", ct, text).ConfigureAwait(false);
        }
    }

    private sealed class JsDeviceBridge(IJSRuntime js) : IDeviceFileBridge
    {
        public async Task<IReadOnlyList<(string Name, byte[] Bytes)>?> PickFilesAsync(bool multiselect, string? accept, CancellationToken ct)
        {
            using var document = await js.InvokeAsync<JsonDocument>(
                "wpfFileDialogs.pickFiles", ct, new { multiple = multiselect, accept = accept ?? string.Empty }).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.TryGetProperty("cancelled", out var cancelled) && cancelled.GetBoolean()) return null;
            var files = new List<(string, byte[])>();
            if (root.TryGetProperty("files", out var list))
            {
                foreach (var file in list.EnumerateArray())
                {
                    var name = file.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "device-file" : "device-file";
                    var encoded = file.TryGetProperty("bytesBase64", out var bytesElement) ? bytesElement.GetString() ?? string.Empty : string.Empty;
                    files.Add((name, Convert.FromBase64String(encoded)));
                }
            }
            return files.Count == 0 ? null : files;
        }

        public async Task SaveFileAsync(string suggestedName, byte[] bytes, CancellationToken ct)
        {
            await js.InvokeVoidAsync("wpfFileDialogs.saveFile", ct,
                new { suggestedName, bytesBase64 = Convert.ToBase64String(bytes) }).ConfigureAwait(false);
        }
    }
}
