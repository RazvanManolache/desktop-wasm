using System.Collections.Specialized;
using DesktopWasm.BrowserHost;
using DesktopWasm.Core;
using Microsoft.Win32;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Gallery FileAndFolderDialogsPage + ClipboardPage slice: real
/// browser-mediated file dialogs over declared virtual mounts and a real
/// clipboard where cancellation and denial are states, never silent skips.
/// Statics (Provider, Current, Presenter) are saved/restored per test and the
/// class joins the serialized ApplicationState collection.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class FileDialogClipboardTests
{
    private static WasmVirtualFileSystem FreshMounts(params (string Path, string Text)[] files)
    {
        var previous = WasmVirtualFileSystem.Current;
        var fs = new WasmVirtualFileSystem();
        fs.Mount("lab-uploads");
        foreach (var (path, text) in files)
            fs.WriteFile(path, System.Text.Encoding.UTF8.GetBytes(text));
        WasmVirtualFileSystem.Current = fs;
        return previous;
    }

    private static IWasmClipboard FreshClipboard()
    {
        var previous = Clipboard.Provider;
        Clipboard.Provider = new InMemoryClipboard();
        Clipboard.Clear();
        return previous;
    }

    private static Func<CommonDialog, bool?> PickFile(OpenFileDialog dialog, string path) => _ =>
    {
        dialog.FileName = path;
        dialog.FileNames = [path];
        return true;
    };

    [Fact]
    public void Clipboard_text_roundtrip_and_clear()
    {
        var previous = FreshClipboard();
        try
        {
            Assert.False(Clipboard.ContainsText());
            Assert.Equal(string.Empty, Clipboard.GetText());
            Clipboard.SetText("hello lab");
            Assert.True(Clipboard.ContainsText());
            Assert.True(Clipboard.ContainsText(TextDataFormat.Text));
            Assert.True(Clipboard.ContainsText(TextDataFormat.UnicodeText));
            Assert.False(Clipboard.ContainsText(TextDataFormat.Rtf));
            Assert.Equal("hello lab", Clipboard.GetText());
            Assert.Equal("hello lab", Clipboard.GetText(TextDataFormat.UnicodeText));
            Clipboard.Clear();
            Assert.False(Clipboard.ContainsText());
            Assert.Equal(string.Empty, Clipboard.GetText());
        }
        finally { Clipboard.Provider = previous; }
    }

    [Fact]
    public void Clipboard_rich_formats_are_precise_errors_not_downgrades()
    {
        var previous = FreshClipboard();
        try
        {
            Assert.Throws<NotSupportedException>(() => Clipboard.SetText("x", TextDataFormat.Rtf));
            Assert.Throws<NotSupportedException>(() => Clipboard.SetText("x", TextDataFormat.Html));
            Assert.Throws<NotSupportedException>(() => Clipboard.GetText(TextDataFormat.Xaml));
            Assert.False(Clipboard.ContainsText());
        }
        finally { Clipboard.Provider = previous; }
    }

    [Fact]
    public void Clipboard_image_roundtrip()
    {
        var previous = FreshClipboard();
        try
        {
            Assert.False(Clipboard.ContainsImage());
            Assert.Null(Clipboard.GetImage());
            var pixels = new byte[] { 255, 0, 0, 255 };
            var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
            Clipboard.SetImage(image);
            Assert.True(Clipboard.ContainsImage());
            Assert.Same(image, Clipboard.GetImage());
            Clipboard.Clear();
            Assert.False(Clipboard.ContainsImage());
        }
        finally { Clipboard.Provider = previous; }
    }

    [Fact]
    public void Clipboard_filedrop_roundtrip()
    {
        var previous = FreshClipboard();
        try
        {
            Assert.False(Clipboard.ContainsFileDropList());
            Assert.Empty(Clipboard.GetFileDropList());
            var list = new StringCollection { "lab-uploads/report.txt" };
            Clipboard.SetFileDropList(list);
            Assert.True(Clipboard.ContainsFileDropList());
            Assert.Equal(["lab-uploads/report.txt"], Clipboard.GetFileDropList().Cast<string>());
            list.Add("lab-uploads/other.txt");
            Assert.Equal(["lab-uploads/report.txt"], Clipboard.GetFileDropList().Cast<string>());
            Clipboard.Clear();
            Assert.False(Clipboard.ContainsFileDropList());
        }
        finally { Clipboard.Provider = previous; }
    }

    [Fact]
    public void Clipboard_contains_audio_is_honestly_false()
    {
        var previous = FreshClipboard();
        try
        {
            Clipboard.SetText("not audio");
            Assert.False(Clipboard.ContainsAudio());
        }
        finally { Clipboard.Provider = previous; }
    }

    [Fact]
    public async Task Clipboard_flush_pushes_session_text_to_the_device()
    {
        var previous = FreshClipboard();
        try
        {
            var device = new ScriptedClipboard("device-text");
            Clipboard.Provider = device;
            Clipboard.SetText("staged");
            await Clipboard.FlushAsync();
            Assert.Equal("staged", device.Written);
            Assert.Equal("staged", Clipboard.GetText());
        }
        finally { Clipboard.Provider = previous; }
    }

    [Fact]
    public async Task Clipboard_refresh_pulls_device_text_into_the_session()
    {
        var previous = FreshClipboard();
        try
        {
            Clipboard.Provider = new ScriptedClipboard("device-text");
            Assert.Equal(string.Empty, Clipboard.GetText());
            await Clipboard.RefreshAsync();
            Assert.Equal("device-text", Clipboard.GetText());
        }
        finally { Clipboard.Provider = previous; }
    }

    [Fact]
    public async Task Clipboard_denial_is_a_real_error_not_silent()
    {
        var previous = FreshClipboard();
        try
        {
            Clipboard.Provider = new DenyingClipboard();
            Clipboard.SetText("staged");
            var flush = await Assert.ThrowsAsync<ClipboardException>(() => Clipboard.FlushAsync());
            Assert.Contains("denied", flush.Message, StringComparison.OrdinalIgnoreCase);
            var refresh = await Assert.ThrowsAsync<ClipboardException>(() => Clipboard.RefreshAsync());
            Assert.Contains("denied", refresh.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("staged", Clipboard.GetText());
        }
        finally { Clipboard.Provider = previous; }
    }

    [Fact]
    public async Task Clipboard_cancellation_is_a_real_state()
    {
        var previous = FreshClipboard();
        try
        {
            Clipboard.Provider = new ScriptedClipboard("device-text");
            using var denied = new CancellationTokenSource();
            denied.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => Clipboard.FlushAsync(denied.Token));
            await Assert.ThrowsAsync<OperationCanceledException>(() => Clipboard.RefreshAsync(denied.Token));
        }
        finally { Clipboard.Provider = previous; }
    }

    [Fact]
    public void Dialog_pick_returns_file_and_stream()
    {
        var previous = FreshMounts(("lab-uploads/report.txt", "quarterly"));
        try
        {
            var fired = 0;
            var dialog = new OpenFileDialog { Filter = "Text files (*.txt)|*.txt" };
            dialog.FileOk += (_, e) => { fired++; Assert.False(e.Cancel); };
            dialog.DialogPresenter = PickFile(dialog, "lab-uploads/report.txt");
            Assert.Equal(true, dialog.ShowDialog());
            Assert.Equal("lab-uploads/report.txt", dialog.FileName);
            Assert.Equal(["lab-uploads/report.txt"], dialog.FileNames);
            Assert.Equal("report.txt", dialog.SafeFileName);
            Assert.Equal(["report.txt"], dialog.SafeFileNames);
            Assert.Equal(1, fired);
            using var stream = dialog.OpenFile();
            Assert.Equal("quarterly", new StreamReader(stream).ReadToEnd());
        }
        finally { WasmVirtualFileSystem.Current = previous; }
    }

    [Fact]
    public void Dialog_cancel_is_a_real_state_not_a_skip()
    {
        var previous = FreshMounts();
        try
        {
            var dialog = new OpenFileDialog();
            dialog.DialogPresenter = _ => false;
            Assert.Equal(false, dialog.ShowDialog());
            Assert.Equal(string.Empty, dialog.FileName);
            Assert.Empty(dialog.FileNames);

            var dismissed = new OpenFileDialog();
            dismissed.DialogPresenter = _ => null;
            Assert.Null(dismissed.ShowDialog());

            var cancelled = new OpenFileDialog();
            cancelled.DialogPresenter = _ => throw new OperationCanceledException("picker dismissed");
            Assert.Equal(false, cancelled.ShowDialog());
        }
        finally { WasmVirtualFileSystem.Current = previous; }
    }

    [Fact]
    public void Dialog_without_presenter_is_a_precise_error()
    {
        var dialog = new OpenFileDialog();
        var error = Assert.Throws<InvalidOperationException>(() => dialog.ShowDialog());
        Assert.Contains("no presenter", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dialog_fileok_veto_reports_cancelled()
    {
        var previous = FreshMounts(("lab-uploads/report.txt", "quarterly"));
        try
        {
            var dialog = new OpenFileDialog();
            dialog.DialogPresenter = PickFile(dialog, "lab-uploads/report.txt");
            dialog.FileOk += (_, e) => e.Cancel = true;
            Assert.Equal(false, dialog.ShowDialog());
            Assert.Equal("lab-uploads/report.txt", dialog.FileName);
        }
        finally { WasmVirtualFileSystem.Current = previous; }
    }

    [Fact]
    public void Dialog_filter_parses_and_matches()
    {
        var dialog = new OpenFileDialog { Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*" };
        var entries = dialog.GetFilterEntries();
        Assert.Equal(2, entries.Count);
        Assert.Equal("Text files (*.txt)", entries[0].Label);
        dialog.FilterIndex = 1;
        Assert.True(dialog.MatchesFilter("lab-uploads/report.txt"));
        Assert.False(dialog.MatchesFilter("lab-uploads/photo.png"));
        dialog.FilterIndex = 2;
        Assert.True(dialog.MatchesFilter("lab-uploads/photo.png"));
    }

    [Fact]
    public void Dialog_addextension_and_name_rules()
    {
        var previous = FreshMounts();
        try
        {
            WasmVirtualFileSystem.Current.WriteText("lab-uploads/notes.txt", "n");
            var dialog = new OpenFileDialog { AddExtension = true, DefaultExt = "txt" };
            dialog.DialogPresenter = PickFile(dialog, "lab-uploads/notes");
            Assert.Equal(true, dialog.ShowDialog());
            Assert.Equal("lab-uploads/notes.txt", dialog.FileName);

            var bad = new OpenFileDialog { ValidateNames = true };
            bad.DialogPresenter = PickFile(bad, "lab-uploads/in*valid.txt");
            Assert.Throws<InvalidOperationException>(() => bad.ShowDialog());

            var link = new OpenFileDialog();
            link.DialogPresenter = PickFile(link, "lab-uploads/notes.lnk");
            Assert.Throws<InvalidOperationException>(() => link.ShowDialog());
        }
        finally { WasmVirtualFileSystem.Current = previous; }
    }

    [Fact]
    public void Dialog_unstaged_pick_is_a_loud_error()
    {
        var previous = FreshMounts();
        try
        {
            var dialog = new OpenFileDialog();
            dialog.DialogPresenter = PickFile(dialog, "lab-uploads/missing.txt");
            Assert.Throws<FileNotFoundException>(() => dialog.ShowDialog());

            var outside = new OpenFileDialog();
            outside.DialogPresenter = PickFile(outside, "ghost-mount/missing.txt");
            Assert.Throws<InvalidOperationException>(() => outside.ShowDialog());
        }
        finally { WasmVirtualFileSystem.Current = previous; }
    }

    [Fact]
    public void Save_dialog_writes_through_mounts()
    {
        var previous = FreshMounts(("lab-uploads/report.txt", "old"));
        try
        {
            var dialog = new SaveFileDialog { DefaultExt = "txt" };
            dialog.DialogPresenter = _ =>
            {
                dialog.FileName = "lab-uploads/export";
                return true;
            };
            Assert.Equal(true, dialog.ShowDialog());
            Assert.Equal("lab-uploads/export.txt", dialog.FileName);
            using (var stream = dialog.OpenFile())
            using (var writer = new StreamWriter(stream))
                writer.Write("fresh");
            Assert.Equal("fresh", System.Text.Encoding.UTF8.GetString(WasmVirtualFileSystem.Current.ReadFile("lab-uploads/export.txt")));

            var overwrite = new SaveFileDialog();
            overwrite.DialogPresenter = _ =>
            {
                overwrite.FileName = "lab-uploads/report.txt";
                return true;
            };
            Assert.Equal(true, overwrite.ShowDialog());
            using (var stream = overwrite.OpenFile())
            using (var writer = new StreamWriter(stream))
                writer.Write("new");
            Assert.Equal("new", System.Text.Encoding.UTF8.GetString(WasmVirtualFileSystem.Current.ReadFile("lab-uploads/report.txt")));
        }
        finally { WasmVirtualFileSystem.Current = previous; }
    }

    [Fact]
    public void Folder_dialog_resolves_declared_mounts()
    {
        var previous = FreshMounts(("lab-uploads/report.txt", "quarterly"));
        try
        {
            var dialog = new OpenFolderDialog();
            dialog.DialogPresenter = _ =>
            {
                dialog.FolderName = "lab-uploads";
                return true;
            };
            Assert.Equal(true, dialog.ShowDialog());
            Assert.Equal("lab-uploads", dialog.FolderName);

            var ghost = new OpenFolderDialog();
            ghost.DialogPresenter = _ =>
            {
                ghost.FolderName = "ghost-mount/sub";
                return true;
            };
            Assert.Throws<InvalidOperationException>(() => ghost.ShowDialog());
        }
        finally { WasmVirtualFileSystem.Current = previous; }
    }

    [Fact]
    public void Mounts_are_session_isolated_and_absolute_paths_rejected()
    {
        var previous = WasmVirtualFileSystem.Current;
        try
        {
            var first = new WasmVirtualFileSystem();
            first.Mount("lab-uploads");
            first.WriteText("lab-uploads/a.txt", "a");
            var second = new WasmVirtualFileSystem();
            second.Mount("lab-uploads");
            Assert.False(second.Exists("lab-uploads/a.txt"));

            WasmVirtualFileSystem.Current = first;
            Assert.Throws<InvalidOperationException>(() => first.ReadFile("/etc/passwd"));
            Assert.Throws<InvalidOperationException>(() => first.ReadFile("C:/temp/x.txt"));
            Assert.Throws<InvalidOperationException>(() => first.ReadFile("lab-uploads/../escape.txt"));
            Assert.Throws<ArgumentNullException>(() => first.WriteFile("lab-uploads/a.txt", null!));
        }
        finally { WasmVirtualFileSystem.Current = previous; }
    }

    [Fact]
    public void Dialog_reset_restores_defaults()
    {
        var open = new OpenFileDialog
        {
            Title = "Pick",
            Filter = "All|*.*",
            FilterIndex = 2,
            FileName = "lab-uploads/a.txt",
            Multiselect = true,
            CheckFileExists = false,
        };
        open.Reset();
        Assert.Equal(string.Empty, open.Title);
        Assert.Equal(string.Empty, open.Filter);
        Assert.Equal(1, open.FilterIndex);
        Assert.Equal(string.Empty, open.FileName);
        Assert.Empty(open.FileNames);
        Assert.False(open.Multiselect);
        Assert.True(open.CheckFileExists);

        var save = new SaveFileDialog();
        Assert.False(save.CheckFileExists);
        save.CheckFileExists = true;
        save.Reset();
        Assert.False(save.CheckFileExists);
    }

    [Fact]
    public void Dialog_owner_overload_matches_and_commonitem_title_resolves()
    {
        var previous = FreshMounts(("lab-uploads/report.txt", "quarterly"));
        try
        {
            var owner = new Window();
            var dialog = new OpenFileDialog();
            dialog.DialogPresenter = PickFile(dialog, "lab-uploads/report.txt");
            Assert.Equal(true, dialog.ShowDialog(owner));

            CommonItemDialog folder = new OpenFolderDialog { Title = "Where?" };
            Assert.Equal("Where?", folder.Title);
        }
        finally { WasmVirtualFileSystem.Current = previous; }
    }

    [Fact]
    public async Task Files_clipboard_lab_drives_real_dialog_and_clipboard_paths()
    {
        var previousFs = WasmVirtualFileSystem.Current;
        var previousProvider = Clipboard.Provider;
        WasmVirtualFileSystem.Current = new WasmVirtualFileSystem();
        Clipboard.Provider = new InMemoryClipboard();
        Clipboard.Clear();
        try
        {
            var demo = DemoCatalog.Find("files-clipboard");
            var window = demo.Build();
            var lab = FilesClipboardLab.Current;
            Assert.NotNull(lab);
            var noted = new Dictionary<string, string>();
            lab.Noter = (key, value) => noted[key] = value;
            var host = new WasmWpfHost(new WasmDispatcherPump(), new WasmInputRouter());
            host.AddWindow(window, new WpfSize(800, 600));
            demo.OnHostReady?.Invoke(host);
            host.Tick(new FrameContext(1, 0));

            await lab.SimOpenAsync();
            Assert.Equal("Picked: sample.txt (23 chars)", lab.FileReadout.Text);
            Assert.Equal(lab.FileReadout.Text, noted["file"]);

            lab.ArmCancel();
            await lab.SimOpenAsync();
            Assert.Equal("Pick cancelled", lab.FileReadout.Text);

            lab.ClipInput.Text = "hello device";
            await lab.CopyAsync();
            Assert.StartsWith("Copied 12 chars", lab.ClipReadout.Text);
            lab.ClipInput.Text = string.Empty;
            await lab.PasteAsync();
            Assert.Equal("hello device", lab.ClipInput.Text);
            Assert.StartsWith("Pasted 12 chars", lab.ClipReadout.Text);
            Assert.Equal(lab.ClipReadout.Text, noted["clipboard"]);

            lab.ArmDenial();
            await lab.CopyAsync();
            Assert.StartsWith("Clipboard denied:", lab.ClipReadout.Text);

            lab.ArmDenial();
            await lab.PasteAsync();
            Assert.StartsWith("Clipboard denied:", lab.ClipReadout.Text);

            var bridge = new FakeDeviceBridge([( "device.txt", "device bytes" )]);
            lab.DeviceBridge = bridge;
            await lab.DeviceOpenAsync();
            Assert.Equal("Picked: device.txt (12 bytes)", lab.FileReadout.Text);
            Assert.Equal("device bytes", System.Text.Encoding.UTF8.GetString(WasmVirtualFileSystem.Current.ReadFile("lab-uploads/device.txt")));

            lab.ClipInput.Text = "save me";
            await lab.DeviceSaveAsync();
            Assert.Equal("Saved: lab-note.txt (7 bytes)", lab.FileReadout.Text);
            Assert.Equal(("lab-note.txt", "save me"), bridge.Saved);

            bridge.PickResult = null;
            await lab.DeviceOpenAsync();
            Assert.Equal("Pick cancelled", lab.FileReadout.Text);

            Assert.True(lab.Notes.ContainsKey("layout"));
            Assert.Contains("simOpen", lab.Notes["layout"], StringComparison.Ordinal);

            var frame = host.Tick(new FrameContext(2, 16.6));
            Assert.Contains("Pick cancelled", frame.Json, StringComparison.Ordinal);
        }
        finally
        {
            WasmVirtualFileSystem.Current = previousFs;
            Clipboard.Provider = previousProvider;
        }
    }

    private sealed class FakeDeviceBridge(IReadOnlyList<(string Name, string Text)>? files) : IDeviceFileBridge
    {
        public IReadOnlyList<(string Name, string Text)>? PickResult { get; set; } = files;

        public (string Name, string Text)? Saved { get; private set; }

        public Task<IReadOnlyList<(string Name, byte[] Bytes)>?> PickFilesAsync(bool multiselect, string? accept, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<(string Name, byte[] Bytes)>?>(PickResult?.Select(f => (f.Name, System.Text.Encoding.UTF8.GetBytes(f.Text))).ToList());

        public Task SaveFileAsync(string suggestedName, byte[] bytes, CancellationToken ct)
        {
            Saved = (suggestedName, System.Text.Encoding.UTF8.GetString(bytes));
            return Task.CompletedTask;
        }
    }

    private sealed class ScriptedClipboard(string text) : IWasmClipboard
    {
        public string? Written { get; private set; }

        public Task<string?> GetTextAsync(CancellationToken ct = default) => Task.FromResult<string?>(text);

        public Task SetTextAsync(string value, CancellationToken ct = default)
        {
            Written = value;
            return Task.CompletedTask;
        }
    }

    private sealed class DenyingClipboard : IWasmClipboard
    {
        public Task<string?> GetTextAsync(CancellationToken ct = default) =>
            Task.FromException<string?>(new UnauthorizedAccessException("clipboard read denied by policy"));

        public Task SetTextAsync(string text, CancellationToken ct = default) =>
            Task.FromException(new UnauthorizedAccessException("clipboard write denied by policy"));
    }
}
