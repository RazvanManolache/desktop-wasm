using DesktopWasm.Core;
using DesktopWasm.Wpf;

namespace DesktopWasm.BrowserHost;

// Primitives lab (engine-native ScreenToGif primitives slice): a Thumb grip
// reports started/delta/completed through the real pointer path (the
// CroppingAdorner/ElementAdorner drag shape), a RepeatButton holds to repeat
// Clicks (the UpDown spinner shape), and a toggle opens a Bottom-anchored
// Popup with light dismiss (the SplitButton shape). Readouts mirror every
// state into the noter channel so the browser proof asserts post-input text,
// not just repaint hashes.
public sealed class PrimitivesLab
{
    public static PrimitivesLab? Current { get; private set; }

    public Window Window { get; } = new() { Title = "Primitives lab" };

    public Thumb Grip { get; } = new() { Width = 120, Height = 28, Margin = new Thickness(8, 4, 8, 4) };

    public TextBlock DragReadout { get; } = new() { Text = "Drag: idle", Margin = new Thickness(8, 4, 8, 4) };

    public RepeatButton Repeater { get; } = new()
    {
        Content = "Hold to repeat",
        Width = 200,
        Delay = 300,
        Interval = 100,
        Margin = new Thickness(8, 4, 8, 4),
    };

    public TextBlock ClickReadout { get; } = new() { Text = "Clicks: 0", Margin = new Thickness(8, 4, 8, 4) };

    public Button PopupToggle { get; } = new() { Content = "Details…", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public Popup Details { get; } = new() { Placement = PlacementMode.Bottom, StaysOpen = false };

    public TextBlock PopupReadout { get; } = new() { Text = "Popup: closed", Margin = new Thickness(8, 4, 8, 4) };

    /// <summary>Automation channel mirroring each readout (the browser file replays it for state assertions).</summary>
    public Action<string, string>? Noter { get; set; }

    /// <summary>Latest status per key (the browser file replays these after attaching its noter).</summary>
    public IReadOnlyDictionary<string, string> Notes => _notes;

    private readonly Dictionary<string, string> _notes = new();
    private WasmWpfHost? _host;
    private double _totalX;
    private double _totalY;
    private int _clicks;

    public static Window Build()
    {
        var lab = new PrimitivesLab();
        Current = lab;
        lab.Compose();
        return lab.Window;
    }

    public void Compose()
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.AddChild(new TextBlock
        {
            Text = "Drag the grip, hold the repeater, toggle the popup.",
            Margin = new Thickness(8, 8, 8, 4),
        });

        Grip.DragStarted += (_, e) =>
        {
            _totalX = 0;
            _totalY = 0;
            SetStatus("drag", DragReadout, $"Drag: started @ ({e.HorizontalOffset:0}, {e.VerticalOffset:0})");
        };
        Grip.DragDelta += (_, e) =>
        {
            _totalX += e.HorizontalChange;
            _totalY += e.VerticalChange;
            SetStatus("drag", DragReadout, $"Drag: Δ ({_totalX:0}, {_totalY:0})");
        };
        Grip.DragCompleted += (_, e) => SetStatus("drag", DragReadout,
            e.Canceled ? "Drag: canceled" : $"Drag: done ({e.HorizontalChange:0}, {e.VerticalChange:0})");
        panel.AddChild(Grip);
        panel.AddChild(DragReadout);

        Repeater.Click += (_, _) => SetStatus("clicks", ClickReadout, $"Clicks: {++_clicks}");
        panel.AddChild(Repeater);
        panel.AddChild(ClickReadout);

        Details.PlacementTarget = PopupToggle;
        var body = new Border
        {
            Width = 220,
            Height = 64,
            BackgroundRgba = 0xFFFFFFFFu,
            BorderRgba = 0x707070FFu,
            Thickness = 1,
        };
        body.AddChild(new TextBlock { Text = "Popup body: Bottom-anchored." });
        Details.Child = body;
        Details.Opened += (_, _) => SetStatus("popup", PopupReadout, "Popup: open");
        Details.Closed += (_, _) => SetStatus("popup", PopupReadout, "Popup: closed");
        PopupToggle.Click += (_, _) => Details.IsOpen = !Details.IsOpen;
        panel.AddChild(PopupToggle);
        panel.AddChild(Details);
        panel.AddChild(PopupReadout);

        Window.Content = panel;
        SetStatus("drag", DragReadout, DragReadout.Text);
        SetStatus("clicks", ClickReadout, ClickReadout.Text);
        SetStatus("popup", PopupReadout, PopupReadout.Text);
    }

    public void Wire(WasmWpfHost host)
    {
        _host = host;
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

    internal void SetStatus(string key, TextBlock readout, string text)
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
        var rects = new Dictionary<string, string>
        {
            ["grip"] = Rect(Grip),
            ["repeater"] = Rect(Repeater),
            ["toggle"] = Rect(PopupToggle),
            ["popupBody"] = Rect(Details.Child),
        };
        var json = "{" + string.Join(",", rects.Select(kv => $"\"{kv.Key}\":\"{kv.Value}\"")) + "}";
        _notes["layout"] = json;
        Note("layout", json);
    }

    private static string Rect(UIElement? element)
    {
        if (element is null) return "0,0,0,0";
        var r = element.RenderRect;
        return $"{r.X:0},{r.Y:0},{r.Width:0},{r.Height:0}";
    }
}
