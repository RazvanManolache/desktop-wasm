using DesktopWasm.Core;
using DesktopWasm.Wpf;

namespace DesktopWasm.BrowserHost;

// Shell/windowing lab (engine-native ScreenToGif shell slice): the window
// sizes to content like NewRecorder (SizeToContent readout), the taskbar slot
// carries progress plus Record/Pause/Stop thumbnail buttons sharing commands
// with the visible buttons (the recorder NewRecorder shape), and a
// GridSplitter redistributes the two content columns through the real
// pointer path (the VideoSource shape). Pure engine surface; the browser
// proof asserts post-input state strings, not just repaint hashes.
public sealed class ShellLab
{
    public static ShellLab? Current { get; private set; }

    public Window Window { get; } = new()
    {
        Title = "Shell",
        SizeToContent = SizeToContent.WidthAndHeight,
    };

    public Grid SplitGrid { get; } = new() { Height = 200 };

    public GridSplitter Splitter { get; } = new() { ResizeBehavior = GridResizeBehavior.PreviousAndNext };

    public TextBlock SizeReadout { get; } = new()
    {
        Text = "Size: WidthAndHeight",
        Margin = new Thickness(8, 8, 8, 4),
    };

    public TextBlock ProgressReadout { get; } = new()
    {
        Text = "Progress: Normal 45%",
        Margin = new Thickness(8, 4, 8, 4),
    };

    public TextBlock ActionReadout { get; } = new()
    {
        Text = "Action: idle",
        Margin = new Thickness(8, 4, 8, 4),
    };

    public TextBlock SplitReadout { get; } = new()
    {
        Text = "Split: 200|4|*",
        Margin = new Thickness(8, 4, 8, 4),
    };

    public Button BumpButton { get; } = new() { Content = "Bump +10%", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public Button FailButton { get; } = new() { Content = "Fail", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public Button ResetButton { get; } = new() { Content = "Reset", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public Button RecordButton { get; } = new() { Content = "Record", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public Button StopButton { get; } = new() { Content = "Stop", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public TextBlock ScreenReadout { get; } = new()
    {
        Text = "Screen: -",
        Margin = new Thickness(8, 4, 8, 4),
    };

    public TextBlock PlaceReadout { get; } = new()
    {
        Text = "Place: -",
        Margin = new Thickness(8, 4, 8, 4),
    };

    public TextBlock TickReadout { get; } = new()
    {
        Text = "Ticks: 0",
        Margin = new Thickness(8, 4, 8, 4),
    };

    public Button CenterButton { get; } = new() { Content = "Center dialog", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public Button ClampButton { get; } = new() { Content = "Clamp large", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public Button StartTimerButton { get; } = new() { Content = "Start timer", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public Button StopTimerButton { get; } = new() { Content = "Stop timer", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    /// <summary>Capture/highlight-style interval timer (slice-30 Forms.Timer over the dispatcher queue).</summary>
    internal System.Windows.Forms.Timer BeatTimer { get; } = new() { Interval = 100 };

    private int _ticks;

    /// <summary>Automation channel mirroring each readout (the browser file replays it for state assertions).</summary>
    public Action<string, string>? Noter { get; set; }

    /// <summary>Latest status per key (the browser file replays these after attaching its noter).</summary>
    public IReadOnlyDictionary<string, string> Notes => _notes;

    private readonly Dictionary<string, string> _notes = new();
    private WasmWpfHost? _host;

    public static Window Build()
    {
        var lab = new ShellLab();
        Current = lab;
        lab.Compose();
        return lab.Window;
    }

    public void Compose()
    {
        var taskbar = Window.TaskbarItemInfo;
        taskbar.Description = "Encoding…";
        taskbar.ProgressState = TaskbarItemProgressState.Normal;
        taskbar.ProgressValue = 0.45;
        taskbar.Overlay = new DrawingImage();

        var recordCommand = new RelayCommand(_ => SetStatus("action", ActionReadout, "Action: recording"));
        var pauseCommand = new RelayCommand(_ => SetStatus("action", ActionReadout, "Action: paused"));
        var stopCommand = new RelayCommand(_ => SetStatus("action", ActionReadout, "Action: stopped"));
        taskbar.ThumbButtonInfos.Add(new ThumbButtonInfo
        {
            Description = "Record",
            DismissWhenClicked = true,
            Command = recordCommand,
        });
        taskbar.ThumbButtonInfos.Add(new ThumbButtonInfo
        {
            Description = "Pause",
            DismissWhenClicked = true,
            Command = pauseCommand,
        });
        taskbar.ThumbButtonInfos.Add(new ThumbButtonInfo
        {
            Description = "Stop",
            DismissWhenClicked = true,
            Command = stopCommand,
        });

        RecordButton.Command = recordCommand;
        StopButton.Command = stopCommand;

        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.AddChild(SizeReadout);
        panel.AddChild(ProgressReadout);
        panel.AddChild(ActionReadout);
        panel.AddChild(SplitReadout);
        panel.AddChild(ScreenReadout);
        panel.AddChild(PlaceReadout);
        panel.AddChild(TickReadout);

        SplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Pixel(200) });
        SplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Pixel(4) });
        SplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star() });
        var preview = new Label { Content = "Preview panel", Background = new SolidColorBrush(Colors.LightGray) };
        preview.SetAttached("Grid.Column", 0);
        Splitter.SetAttached("Grid.Column", 1);
        var frames = new Label { Content = "Frames panel", Background = new SolidColorBrush(Colors.White) };
        frames.SetAttached("Grid.Column", 2);
        SplitGrid.AddChild(preview);
        SplitGrid.AddChild(Splitter);
        SplitGrid.AddChild(frames);
        Splitter.DragDelta += (_, _) => RefreshSplitNotes();
        Splitter.DragCompleted += (_, _) => RefreshSplitNotes();
        panel.AddChild(SplitGrid);

        BumpButton.Click += (_, _) =>
        {
            taskbar.ProgressValue = Math.Min(1, taskbar.ProgressValue + 0.1);
            RefreshProgressNotes();
        };
        FailButton.Click += (_, _) =>
        {
            taskbar.ProgressState = TaskbarItemProgressState.Error;
            RefreshProgressNotes();
        };
        ResetButton.Click += (_, _) =>
        {
            taskbar.ProgressState = TaskbarItemProgressState.Normal;
            taskbar.ProgressValue = 0.45;
            RefreshProgressNotes();
        };
        // Two button columns keep the lab inside the 800x600 demo scene:
        // a single column would push the lower buttons below the fold
        // where pointer input cannot reach them.
        var left = new StackPanel();
        left.AddChild(BumpButton);
        left.AddChild(FailButton);
        left.AddChild(ResetButton);
        left.AddChild(RecordButton);
        left.AddChild(StopButton);
        var right = new StackPanel();
        right.AddChild(CenterButton);
        right.AddChild(ClampButton);
        right.AddChild(StartTimerButton);
        right.AddChild(StopTimerButton);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.AddChild(left);
        buttons.AddChild(right);
        panel.AddChild(buttons);

        CenterButton.Click += (_, _) => PlaceCenter();
        ClampButton.Click += (_, _) => PlaceClamp();
        StartTimerButton.Click += (_, _) => StartBeats();
        StopTimerButton.Click += (_, _) => StopBeats();
        BeatTimer.Tick += (_, _) =>
        {
            _ticks++;
            SetStatus("ticks", TickReadout, $"Ticks: {_ticks}");
        };

        RefreshProgressNotes();
        RefreshSplitNotes();
        SetStatus("size", SizeReadout, $"Size: {Window.SizeToContent}");
        SetStatus("action", ActionReadout, ActionReadout.Text);
        var screen = System.Windows.Forms.Screen.PrimaryScreen;
        SetStatus("screen", ScreenReadout,
            $"Screen: {screen.Bounds.Width}x{screen.Bounds.Height} work={screen.WorkingArea.Width}x{screen.WorkingArea.Height}");
        SetStatus("place", PlaceReadout, PlaceReadout.Text);
        SetStatus("ticks", TickReadout, TickReadout.Text);
        Window.Content = panel;
    }

    /// <summary>Centers a NewRecorder-sized dialog (480x360) in the session working area.</summary>
    internal void PlaceCenter() => Place(480, 360);

    /// <summary>Places an oversized dialog (1600x1200): clamps to the working area origin.</summary>
    internal void PlaceClamp() => Place(1600, 1200);

    private void Place(double width, double height)
    {
        var rect = WindowPlacement.PlaceDialog(width, height, WindowStartupLocation.CenterScreen);
        SetStatus("place", PlaceReadout, $"Place: {rect.X},{rect.Y} {rect.Width}x{rect.Height}");
    }

    /// <summary>Starts the interval timer (ticks arrive on the dispatcher queue).</summary>
    internal void StartBeats() => BeatTimer.Start();

    /// <summary>Stops the interval timer (pending ticks stay silent).</summary>
    internal void StopBeats() => BeatTimer.Stop();

    private void RefreshProgressNotes()
    {
        var taskbar = Window.TaskbarItemInfo;
        SetStatus("progress", ProgressReadout,
            $"Progress: {taskbar.ProgressState} {taskbar.ProgressValue * 100:0}%");
    }

    internal void RefreshSplitNotes()
    {
        static string Def(ColumnDefinition def) =>
            def.Width.Unit == GridUnitType.Pixel ? $"{def.Width.Value:0}" : "*";
        var defs = SplitGrid.ColumnDefinitions;
        SetStatus("split", SplitReadout,
            defs.Count == 3 ? $"Split: {Def(defs[0])}|{Def(defs[1])}|{Def(defs[2])}" : "Split: -");
    }

    public void Wire(WasmWpfHost host)
    {
        _host = host;
        WasmMilCore.RenderHeadless(Window, new WpfSize(800, 600));
        RefreshSplitNotes();
        PublishLayoutNotes();
    }

    /// <summary>Forwards every status so far (browser attach replays after Wire ran headless).</summary>
    public void PublishNotes()
    {
        foreach (var (key, value) in _notes)
            Note(key, value);
        RefreshSplitNotes();
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
            ["splitter"] = Rect(Splitter),
            ["bump"] = Rect(BumpButton),
            ["record"] = Rect(RecordButton),
            ["stop"] = Rect(StopButton),
            ["center"] = Rect(CenterButton),
            ["clamp"] = Rect(ClampButton),
            ["startTimer"] = Rect(StartTimerButton),
            ["stopTimer"] = Rect(StopTimerButton),
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
