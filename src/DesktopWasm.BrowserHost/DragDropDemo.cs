using DesktopWasm.Core;
using DesktopWasm.Wpf;

namespace DesktopWasm.BrowserHost;

/// <summary>
/// Drag-drop &amp; interop lab (engine-native ScreenToGif
/// drag-drop/data-object + HwndSource slice): a real <see cref="UIElement.AllowDrop"/>
/// target runs the editor's enter/drop decisions over <see cref="DataObject"/>
/// payloads, a paste box runs the numeric-box <see cref="DataObject.PastingEvent"/>
/// veto, and a session <see cref="HwndSource"/> reports the DPI values the
/// capture helpers read. Simulated buttons drive the exact code the browser
/// HTML5 file-drop bridge calls (<see cref="AcceptFiles"/>), so denial and
/// empty drops repaint as states instead of silent no-ops.
/// </summary>
public sealed class DragDropLab
{
    public static DragDropLab? Current { get; private set; }

    public Window Window { get; } = new() { Title = "Drag-drop & interop lab" };

    public Border DropZone { get; } = new()
    {
        AllowDrop = true,
        Thickness = 2,
        BackgroundRgba = 0xE4EFE8FF,
        Margin = new Thickness(0, 4, 0, 4),
    };

    public TextBlock EffectReadout { get; } = new() { Text = "Hover the zone or drop files" };

    public TextBlock FilesReadout { get; } = new() { Text = "No files dropped yet" };

    public TextBox PasteBox { get; } = new() { Text = "42", Width = 560 };

    public TextBlock PasteReadout { get; } = new() { Text = "Paste idle (digits only)" };

    public TextBlock DpiReadout { get; } = new() { Text = "DPI idle" };

    public Button SimDropButton { get; } = new() { Content = "Drop media files", Width = 170, Height = 38 };

    public Button SimHoverButton { get; } = new() { Content = "Hover text", Width = 170, Height = 38 };

    public Button PasteGoodButton { get; } = new() { Content = "Paste 42", Width = 150, Height = 38 };

    public Button PasteBadButton { get; } = new() { Content = "Paste 12a", Width = 150, Height = 38 };

    /// <summary>Automation channel mirroring each painted status (the browser file forwards it to JS).</summary>
    public Action<string, string>? Noter { get; set; }

    /// <summary>Latest status per key (the browser file replays these after attaching its noter).</summary>
    public IReadOnlyDictionary<string, string> Notes => _notes;

    private readonly Dictionary<string, string> _notes = new();
    private WasmWpfHost? _host;
    private DragDropEffects _lastEffect = DragDropEffects.None;

    public static Window Build()
    {
        var lab = new DragDropLab();
        Current = lab;
        lab.Compose();
        return lab.Window;
    }

    public void Wire(WasmWpfHost host)
    {
        _host = host;
        DropZone.DragEnter += OnZoneDragEnter;
        DropZone.Drop += OnZoneDrop;
        PasteBox.AddHandler(DataObject.PastingEvent, new DataObjectPastingEventHandler(OnBoxPasting));
        SimDropButton.Click += (_, _) => AcceptFiles(
            [("clip.gif", new byte[] { 1, 2, 3 }), ("frame.png", new byte[] { 4, 5 })]);
        SimHoverButton.Click += (_, _) => Hover(new DataObject(DataFormats.Text, "dragged text"));
        PasteGoodButton.Click += (_, _) => TryPaste("42");
        PasteBadButton.Click += (_, _) => TryPaste("12a");
        WasmVirtualFileSystem.Current.Mount("lab-drops");
        ReportDpi();
        WasmMilCore.RenderHeadless(Window, new WpfSize(800, 600));
        PublishLayoutNotes();
    }

    /// <summary>
    /// Browser HTML5 file-drop entry: stages real dropped bytes into the
    /// session mount, then runs the same enter/drop decisions as the
    /// simulated buttons (no separate imitation path).
    /// </summary>
    public void BrowserDrop(IReadOnlyList<(string Name, byte[] Bytes)> files)
    {
        if (files.Count == 0)
        {
            SetFilesStatus("Drop ignored: the browser delivered no files.");
            return;
        }
        AcceptFiles(files);
    }

    /// <summary>Forwards every status so far (browser attach replays after Wire ran headless).</summary>
    public void PublishNotes()
    {
        foreach (var (key, value) in _notes)
            Note(key, value);
        PublishLayoutNotes();
    }

    internal void AcceptFiles(IReadOnlyList<(string Name, byte[] Bytes)> files)
    {
        var mounts = WasmVirtualFileSystem.Current;
        mounts.Mount("lab-drops");
        var paths = new List<string>();
        foreach (var (name, bytes) in files)
        {
            var leaf = Sanitize(name);
            mounts.WriteFile("lab-drops/" + leaf, bytes);
            paths.Add("lab-drops/" + leaf);
        }
        var data = new DataObject(DataFormats.FileDrop, paths.ToArray());
        DropZone.RaiseDragEvent(
            new DragEventArgs(data, DragDropKeyStates.None, DragDropEffects.Copy),
            DragDrop.PreviewDragEnterEvent, DragDrop.DragEnterEvent);
        DropZone.RaiseDragEvent(
            new DragEventArgs(data, DragDropKeyStates.None, _lastEffect),
            DragDrop.PreviewDropEvent, DragDrop.DropEvent);
    }

    internal void Hover(IDataObject data)
    {
        DropZone.RaiseDragEvent(
            new DragEventArgs(data, DragDropKeyStates.None, DragDropEffects.Copy),
            DragDrop.PreviewDragEnterEvent, DragDrop.DragEnterEvent);
    }

    internal void TryPaste(string text)
    {
        var args = new DataObjectPastingEventArgs(
            new DataObject(typeof(string), text), isDragDrop: false, DataFormats.Text)
        {
            RoutedEvent = DataObject.PastingEvent,
        };
        PasteBox.RaiseEvent(args);
        if (args.CommandCancelled)
        {
            SetPasteStatus($"Paste vetoed: '{text}' is not digits-only.");
            return;
        }
        PasteBox.Text = text;
        SetPasteStatus($"Pasted '{text}'.");
    }

    private void OnZoneDragEnter(object sender, DragEventArgs e)
    {
        // The editor's Control_DragEnter decision: files copy in, anything else stays out.
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        _lastEffect = e.Effects;
        SetEffectStatus(e.Effects == DragDropEffects.Copy
            ? "Hover: files accepted (Copy)."
            : "Hover: no files (None).");
    }

    private void OnZoneDrop(object sender, DragEventArgs e)
    {
        // The editor's Control_Drop read: a FileDrop string array or nothing happens.
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] fileNames || fileNames.Length == 0)
        {
            SetFilesStatus("Drop ignored: no FileDrop payload.");
            return;
        }
        var media = new[] { ".gif", ".png", ".jpg", ".jpeg", ".bmp", ".mp4", ".avi", ".wmv", ".stg", ".zip" };
        var names = fileNames.Select(f => f.Split('/').Last()).ToList();
        if (!names.Any(name => media.Contains(System.IO.Path.GetExtension(name).ToLowerInvariant())))
        {
            SetFilesStatus($"Drop rejected: no media or project files in {names.Count} file(s).");
            return;
        }
        SetFilesStatus($"Dropped {names.Count} file(s): {string.Join(", ", names)}.");
    }

    private void OnBoxPasting(object sender, DataObjectPastingEventArgs e)
    {
        // The numeric-box OnPasting shape: digits pass, anything else cancels.
        if (e.DataObject.GetDataPresent(typeof(string)))
        {
            var text = e.DataObject.GetData(typeof(string)) as string;
            if (!string.IsNullOrEmpty(text) && text.All(char.IsDigit)) return;
        }
        e.CancelCommand();
    }

    private void ReportDpi()
    {
        using var source = new HwndSource(new HwndSourceParameters("lab"));
        var scale = source.CompositionTarget.TransformToDevice.M11;
        HwndSourceHook hook = (IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled) => IntPtr.Zero;
        source.AddHook(hook);
        SetDpiStatus($"Session DPI: {96d * scale:0} dpi at scale {scale:0.0} (1 hook retained, no Win32 pump).");
        source.RemoveHook(hook);
    }

    internal void SetEffectStatus(string text) => SetStatus("effect", EffectReadout, text);

    internal void SetFilesStatus(string text) => SetStatus("files", FilesReadout, text);

    internal void SetPasteStatus(string text) => SetStatus("paste", PasteReadout, text);

    internal void SetDpiStatus(string text) => SetStatus("dpi", DpiReadout, text);

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
            ["simDrop"] = Center(SimDropButton),
            ["simHover"] = Center(SimHoverButton),
            ["pasteGood"] = Center(PasteGoodButton),
            ["pasteBad"] = Center(PasteBadButton),
            ["zone"] = Center(DropZone),
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

    private void Compose()
    {
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.AddChild(new TextBlock { Text = "Drag-drop & interop lab", FontCss = "28px sans-serif", ForegroundRgba = 0x214C40FF, Margin = new Thickness(0, 0, 0, 8) });
        panel.AddChild(new TextBlock { Text = "The zone runs the editor's enter/drop decisions over real data objects; paste runs the numeric-box veto; DPI reads the session source.", FontCss = "13px sans-serif", ForegroundRgba = 0x7B8D82FF, Margin = new Thickness(0, 0, 0, 12) });
        var zoneLabel = new TextBlock { Text = "Drop media files here", FontCss = "16px sans-serif", Margin = new Thickness(12) };
        DropZone.AddChild(zoneLabel);
        DropZone.Width = 560;
        DropZone.Height = 120;
        panel.AddChild(DropZone);
        EffectReadout.Margin = new Thickness(0, 8, 0, 0);
        panel.AddChild(EffectReadout);
        FilesReadout.Margin = new Thickness(0, 4, 0, 4);
        panel.AddChild(FilesReadout);
        var drag = new StackPanel { Orientation = Orientation.Horizontal };
        drag.AddChild(SimDropButton);
        drag.AddChild(SimHoverButton);
        panel.AddChild(drag);
        panel.AddChild(new TextBlock { Text = "Paste veto", FontCss = "18px sans-serif", Margin = new Thickness(0, 12, 0, 4) });
        PasteBox.Margin = new Thickness(0, 0, 0, 8);
        panel.AddChild(PasteBox);
        var paste = new StackPanel { Orientation = Orientation.Horizontal };
        paste.AddChild(PasteGoodButton);
        paste.AddChild(PasteBadButton);
        panel.AddChild(paste);
        PasteReadout.Margin = new Thickness(0, 8, 0, 4);
        panel.AddChild(PasteReadout);
        panel.AddChild(new TextBlock { Text = "Session source", FontCss = "18px sans-serif", Margin = new Thickness(0, 12, 0, 4) });
        panel.AddChild(DpiReadout);
        Window.Content = panel;
    }
}
