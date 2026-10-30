using System.Text;
using DesktopWasm.Core;
using DesktopWasm.Wpf;
using Microsoft.Win32;

namespace DesktopWasm.BrowserHost;

// Device-file bridge (implemented by the browser host over real pickers and
// the device clipboard; headless tests and the simulated lab path use fakes
// or the engine presenter instead). A null pick list is the real cancelled
// state, never an empty fabrication.
public interface IDeviceFileBridge
{
    Task<IReadOnlyList<(string Name, byte[] Bytes)>?> PickFilesAsync(bool multiselect, string? accept, CancellationToken ct);

    Task SaveFileAsync(string suggestedName, byte[] bytes, CancellationToken ct);
}

/// <summary>
/// Files &amp; clipboard lab (engine-native Gallery
/// FileAndFolderDialogsPage/ClipboardPage slice): simulated picks run the
/// real <see cref="OpenFileDialog"/> presenter path over declared session
/// mounts, device buttons bridge to browser pickers, and clipboard
/// copy/paste round-trips through <see cref="DesktopWasm.Wpf.Clipboard"/>
/// with denial and cancellation painted as states. Pure engine surface so
/// the headless suite links it directly; the browser-only bridge attaches
/// through <see cref="DeviceBridge"/> and <see cref="Noter"/>.
/// </summary>
public sealed class FilesClipboardLab
{
    public static FilesClipboardLab? Current { get; private set; }

    public Window Window { get; } = new() { Title = "Files & clipboard lab" };

    public TextBox ClipInput { get; } = new() { Text = "Quarterly figures", Width = 560 };

    public TextBlock FileReadout { get; } = new() { Text = "No file picked yet" };

    public TextBlock ClipReadout { get; } = new() { Text = "Clipboard idle" };

    public Button SimOpenButton { get; } = new() { Content = "Simulated open", Width = 170, Height = 38 };

    public Button DeviceOpenButton { get; } = new() { Content = "Device open", Width = 170, Height = 38 };

    public Button DeviceSaveButton { get; } = new() { Content = "Device save", Width = 170, Height = 38 };

    public Button CancelPickButton { get; } = new() { Content = "Cancel next", Width = 170, Height = 38 };

    public Button CopyButton { get; } = new() { Content = "Copy", Width = 150, Height = 38 };

    public Button PasteButton { get; } = new() { Content = "Paste", Width = 150, Height = 38 };

    public Button DenyButton { get; } = new() { Content = "Deny next sync", Width = 170, Height = 38 };

    /// <summary>Browser bridge for device pickers (null headless: device buttons report unavailable).</summary>
    public IDeviceFileBridge? DeviceBridge { get; set; }

    /// <summary>Automation channel mirroring each painted status (the browser file forwards it to JS).</summary>
    public Action<string, string>? Noter { get; set; }

    /// <summary>Latest status per key (the browser file replays these after attaching its noter).</summary>
    public IReadOnlyDictionary<string, string> Notes => _notes;

    private readonly Dictionary<string, string> _notes = new();
    private WasmWpfHost? _host;
    private bool _cancelNext;
    private bool _denyNext;

    public static Window Build()
    {
        var lab = new FilesClipboardLab();
        Current = lab;
        lab.Compose();
        return lab.Window;
    }

    public void Wire(WasmWpfHost host)
    {
        _host = host;
        var mounts = WasmVirtualFileSystem.Current;
        mounts.Mount("lab-uploads");
        mounts.WriteText("lab-uploads/sample.txt", "Quarterly report draft.");
        mounts.WriteText("lab-uploads/notes.txt", "Lab notes.");
        SimOpenButton.Click += async (_, _) => await SimOpenAsync();
        DeviceOpenButton.Click += async (_, _) => await DeviceOpenAsync();
        DeviceSaveButton.Click += async (_, _) => await DeviceSaveAsync();
        CancelPickButton.Click += (_, _) => ArmCancel();
        CopyButton.Click += async (_, _) => await CopyAsync();
        PasteButton.Click += async (_, _) => await PasteAsync();
        DenyButton.Click += (_, _) => ArmDenial();
        WasmMilCore.RenderHeadless(Window, new WpfSize(800, 600));
        PublishLayoutNotes();
    }

    /// <summary>Forwards every status so far (browser attach replays after Wire ran headless).</summary>
    public void PublishNotes()
    {
        foreach (var (key, value) in _notes)
            Note(key, value);
        PublishLayoutNotes();
    }

    public void ArmCancel()
    {
        _cancelNext = true;
        SetFileStatus("Cancel armed: the next simulated pick reports cancelled.");
    }

    public void ArmDenial()
    {
        _denyNext = true;
        SetClipStatus("Denial armed: the next device sync fails loudly.");
    }

    public async Task SimOpenAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open a lab file",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FilterIndex = 1,
        };
        dialog.DialogPresenter = _ =>
        {
            if (_cancelNext)
            {
                _cancelNext = false;
                return false;
            }
            dialog.FileName = "lab-uploads/sample.txt";
            dialog.FileNames = ["lab-uploads/sample.txt"];
            return true;
        };
        bool? result;
        try
        {
            result = dialog.ShowDialog();
        }
        catch (FileNotFoundException error)
        {
            SetFileStatus($"Pick missing: {error.FileName ?? "unknown file"}");
            return;
        }
        catch (InvalidOperationException error)
        {
            SetFileStatus($"Pick rejected: {FirstLine(error.Message)}");
            return;
        }
        if (result == true)
        {
            using var stream = dialog.OpenFile();
            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync().ConfigureAwait(false);
            SetFileStatus($"Picked: {dialog.SafeFileName} ({text.Length} chars)");
        }
        else if (result == false)
        {
            SetFileStatus("Pick cancelled");
        }
        else
        {
            SetFileStatus("Pick dismissed");
        }
    }

    public async Task DeviceOpenAsync(CancellationToken ct = default)
    {
        if (DeviceBridge is null)
        {
            SetFileStatus("Device picker unavailable: no browser bridge is attached.");
            return;
        }
        IReadOnlyList<(string Name, byte[] Bytes)>? picked;
        try
        {
            picked = await DeviceBridge.PickFilesAsync(false, ".txt", ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            SetFileStatus("Pick cancelled");
            return;
        }
        if (picked is null)
        {
            SetFileStatus("Pick cancelled");
            return;
        }
        var mounts = WasmVirtualFileSystem.Current;
        mounts.Mount("lab-uploads");
        foreach (var (name, bytes) in picked)
            mounts.WriteFile("lab-uploads/" + Sanitize(name), bytes);
        var first = picked[0];
        var dialog = new OpenFileDialog();
        dialog.DialogPresenter = _ =>
        {
            dialog.FileName = "lab-uploads/" + Sanitize(first.Name);
            dialog.FileNames = ["lab-uploads/" + Sanitize(first.Name)];
            return true;
        };
        if (dialog.ShowDialog() != true)
        {
            SetFileStatus("Pick cancelled");
            return;
        }
        using var stream = dialog.OpenFile();
        SetFileStatus($"Picked: {dialog.SafeFileName} ({stream.Length} bytes)");
    }

    public async Task DeviceSaveAsync(CancellationToken ct = default)
    {
        var bytes = Encoding.UTF8.GetBytes(ClipInput.Text);
        if (DeviceBridge is null)
        {
            SetFileStatus("Device save unavailable: no browser bridge is attached.");
            return;
        }
        try
        {
            await DeviceBridge.SaveFileAsync("lab-note.txt", bytes, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            SetFileStatus("Save cancelled");
            return;
        }
        SetFileStatus($"Saved: lab-note.txt ({bytes.Length} bytes)");
    }

    public async Task CopyAsync(CancellationToken ct = default)
    {
        Clipboard.SetText(ClipInput.Text);
        IWasmClipboard? swapped = null;
        if (_denyNext)
        {
            _denyNext = false;
            swapped = Clipboard.Provider;
            Clipboard.Provider = new DenyingClipboard();
        }
        try
        {
            await Clipboard.FlushAsync(ct).ConfigureAwait(false);
            SetClipStatus($"Copied {ClipInput.Text.Length} chars to the device clipboard");
        }
        catch (ClipboardException error)
        {
            SetClipStatus($"Clipboard denied: {FirstLine(error.Message)}");
        }
        catch (OperationCanceledException)
        {
            SetClipStatus("Clipboard cancelled");
        }
        finally
        {
            if (swapped is not null) Clipboard.Provider = swapped;
        }
    }

    public async Task PasteAsync(CancellationToken ct = default)
    {
        IWasmClipboard? swapped = null;
        if (_denyNext)
        {
            _denyNext = false;
            swapped = Clipboard.Provider;
            Clipboard.Provider = new DenyingClipboard();
        }
        try
        {
            await Clipboard.RefreshAsync(ct).ConfigureAwait(false);
            var text = Clipboard.GetText();
            ClipInput.Text = text;
            SetClipStatus($"Pasted {text.Length} chars from the device clipboard");
        }
        catch (ClipboardException error)
        {
            SetClipStatus($"Clipboard denied: {FirstLine(error.Message)}");
        }
        catch (OperationCanceledException)
        {
            SetClipStatus("Clipboard cancelled");
        }
        finally
        {
            if (swapped is not null) Clipboard.Provider = swapped;
        }
    }

    internal void SetFileStatus(string text) => SetStatus("file", FileReadout, text);

    internal void SetClipStatus(string text) => SetStatus("clipboard", ClipReadout, text);

    private void SetStatus(string key, TextBlock readout, string text)
    {
        readout.Text = text;
        _notes[key] = text;
        Note(key, text);
        _host?.Invalidate();
    }

    private void Note(string key, string value)
    {
        try { Noter?.Invoke(key, value); }
        catch { }
    }

    private void PublishLayoutNotes()
    {
        var centers = new Dictionary<string, string>
        {
            ["simOpen"] = Center(SimOpenButton),
            ["deviceOpen"] = Center(DeviceOpenButton),
            ["deviceSave"] = Center(DeviceSaveButton),
            ["cancelPick"] = Center(CancelPickButton),
            ["copy"] = Center(CopyButton),
            ["paste"] = Center(PasteButton),
            ["deny"] = Center(DenyButton),
        };
        var json = "{" + string.Join(",", centers.Select(kv => $"\"{kv.Key}\":\"{kv.Value}\"")) + "}";
        _notes["layout"] = json;
        Note("layout", json);
    }

    private static string Center(UIElement element)
    {
        var r = element.ArrangedRect;
        return $"{r.X + r.Width / 2:0},{r.Y + r.Height / 2:0}";
    }

    private static string Sanitize(string name)
    {
        var leaf = (name ?? string.Empty).Replace('\\', '/');
        var slash = leaf.LastIndexOf('/');
        leaf = slash >= 0 ? leaf[(slash + 1)..] : leaf;
        return string.IsNullOrEmpty(leaf) ? "device-file" : leaf;
    }

    private static string FirstLine(string message)
    {
        var line = (message ?? string.Empty).Split('\n', 2)[0].Trim();
        return line.Length > 120 ? line[..120] : line;
    }

    private void Compose()
    {
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.AddChild(new TextBlock { Text = "Files & clipboard lab", FontCss = "28px sans-serif", ForegroundRgba = 0x214C40FF, Margin = new Thickness(0, 0, 0, 8) });
        panel.AddChild(new TextBlock { Text = "Simulated picks run the real dialog path over session mounts; device buttons use browser pickers. Denial and cancel repaint this page.", FontCss = "13px sans-serif", ForegroundRgba = 0x7B8D82FF, Margin = new Thickness(0, 0, 0, 12) });
        panel.AddChild(new TextBlock { Text = "Device files", FontCss = "18px sans-serif", Margin = new Thickness(0, 8, 0, 4) });
        var files = new StackPanel { Orientation = Orientation.Horizontal };
        files.AddChild(SimOpenButton);
        files.AddChild(DeviceOpenButton);
        files.AddChild(DeviceSaveButton);
        files.AddChild(CancelPickButton);
        panel.AddChild(files);
        FileReadout.Margin = new Thickness(0, 8, 0, 4);
        panel.AddChild(FileReadout);
        panel.AddChild(new TextBlock { Text = "Clipboard", FontCss = "18px sans-serif", Margin = new Thickness(0, 12, 0, 4) });
        ClipInput.Margin = new Thickness(0, 0, 0, 8);
        panel.AddChild(ClipInput);
        var clip = new StackPanel { Orientation = Orientation.Horizontal };
        clip.AddChild(CopyButton);
        clip.AddChild(PasteButton);
        clip.AddChild(DenyButton);
        panel.AddChild(clip);
        ClipReadout.Margin = new Thickness(0, 8, 0, 4);
        panel.AddChild(ClipReadout);
        panel.AddChild(new TextBlock { Text = "Mounts: lab-uploads/ (session-local). Staged bytes never leave the session except through an explicit device save.", FontCss = "12px sans-serif", ForegroundRgba = 0x7B8D82FF, Margin = new Thickness(0, 12, 0, 0) });
        Window.Content = panel;
    }

    private sealed class DenyingClipboard : IWasmClipboard
    {
        public Task<string?> GetTextAsync(CancellationToken ct = default) =>
            Task.FromException<string?>(new UnauthorizedAccessException("clipboard read denied by policy"));

        public Task SetTextAsync(string text, CancellationToken ct = default) =>
            Task.FromException(new UnauthorizedAccessException("clipboard write denied by policy"));
    }
}
