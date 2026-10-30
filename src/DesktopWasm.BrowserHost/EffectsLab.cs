using DesktopWasm.Core;
using DesktopWasm.Wpf;

namespace DesktopWasm.BrowserHost;

// Effects/hosts lab (engine-native ScreenToGif effects/host cluster):
// a shadowed box with toggle/blur controls, a masked PasswordBox with
// live masked readouts, a WebBrowser with a journal, and a ContainerVisual
// replay check. Pure engine surface; the browser proof asserts readout
// text after real pointer/keyboard input.
public sealed class EffectsLab
{
    public static EffectsLab? Current { get; private set; }

    public Window Window { get; } = new()
    {
        Title = "Effects",
    };

    public Border ShadowBox { get; } = new()
    {
        Width = 200,
        Height = 60,
        Margin = new Thickness(8, 8, 8, 4),
    };

    public DropShadowEffect Shadow { get; } = new()
    {
        Color = Color.FromRgb(0x2A, 0x2A, 0x2A),
        Direction = 0,
        ShadowDepth = 5,
        Opacity = 0.6,
        BlurRadius = 4,
        RenderingBias = RenderingBias.Quality,
    };

    public TextBlock ShadowReadout { get; } = new()
    {
        Text = "Shadow: on",
        Margin = new Thickness(8, 4, 8, 4),
    };

    public TextBlock BlurReadout { get; } = new()
    {
        Text = "Blur: 4",
        Margin = new Thickness(8, 4, 8, 4),
    };

    public Button ToggleButton { get; } = new() { Content = "Toggle shadow", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public Button BlurButton { get; } = new() { Content = "Blur +2", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public PasswordBox SecretBox { get; } = new() { Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public TextBlock TypedReadout { get; } = new()
    {
        Text = "Typed: 0 chars",
        Margin = new Thickness(8, 4, 8, 4),
    };

    public TextBlock MaskedReadout { get; } = new()
    {
        Text = "Masked: ",
        Margin = new Thickness(8, 4, 8, 4),
    };

    public WebBrowser Docs { get; } = new() { Width = 300, Height = 60, Margin = new Thickness(8, 4, 8, 4) };

    public TextBlock PageReadout { get; } = new()
    {
        Text = "Page: -",
        Margin = new Thickness(8, 4, 8, 4),
    };

    public TextBlock BackReadout { get; } = new()
    {
        Text = "Back: no",
        Margin = new Thickness(8, 4, 8, 4),
    };

    public Button GoButton { get; } = new() { Content = "Go docs", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public Button BackButton { get; } = new() { Content = "Back", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public TextBlock ContainerReadout { get; } = new()
    {
        Text = "Container ops: -",
        Margin = new Thickness(8, 4, 8, 4),
    };

    public ImageBrush TileSample { get; } = new()
    {
        Stretch = Stretch.Uniform,
        TileMode = TileMode.None,
    };

    public TextBlock TileReadout { get; } = new()
    {
        Text = "Tile: Center/Center",
        Margin = new Thickness(8, 4, 8, 4),
    };

    public Button AlignXButton { get; } = new() { Content = "AlignX next", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public Button AlignYButton { get; } = new() { Content = "AlignY next", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public TextBlock PowerReadout { get; } = new()
    {
        Text = "Power: -",
        Margin = new Thickness(8, 4, 8, 4),
    };

    public Button PowerButton { get; } = new() { Content = "Simulate suspend", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public MediaPlayer PreviewPlayer { get; } = new() { Volume = 0, ScrubbingEnabled = true };

    public TextBlock MediaReadout { get; } = new()
    {
        Text = "Media: -",
        Margin = new Thickness(8, 4, 8, 4),
    };

    public Button OpenButton { get; } = new() { Content = "Open clip", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public Button PlayButton { get; } = new() { Content = "Play", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public Button PauseButton { get; } = new() { Content = "Pause", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    /// <summary>Automation channel mirroring each readout (the browser file replays it for state assertions).</summary>
    public Action<string, string>? Noter { get; set; }

    /// <summary>Latest status per key (the browser file replays these after attaching its noter).</summary>
    public IReadOnlyDictionary<string, string> Notes => _notes;

    private readonly Dictionary<string, string> _notes = new();
    private WasmWpfHost? _host;

    private static int _powerSeen;

    static EffectsLab()
    {
        // One process-wide subscription (no per-session leak): the power
        // button drives the static event and every live lab mirrors the
        // broadcast count into its own readout.
        Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) =>
        {
            _powerSeen++;
            Current?.SetStatus("power", Current.PowerReadout, $"Power: {e.Mode} x{_powerSeen}");
        };
    }

    public static Window Build()
    {
        var lab = new EffectsLab();
        Current = lab;
        lab.Compose();
        return lab.Window;
    }

    public void Compose()
    {
        ShadowBox.Effect = Shadow;

        var panel = new StackPanel { Margin = new Thickness(8) };
        var side = new StackPanel { Margin = new Thickness(8) };
        var root = new StackPanel { Orientation = Orientation.Horizontal };
        root.AddChild(panel);
        root.AddChild(side);
        panel.AddChild(ShadowBox);
        panel.AddChild(ShadowReadout);
        panel.AddChild(BlurReadout);

        ToggleButton.Click += (_, _) =>
        {
            ShadowBox.Effect = ShadowBox.Effect is null ? Shadow : null;
            SetStatus("shadow", ShadowReadout,
                ShadowBox.Effect is null ? "Shadow: off" : "Shadow: on");
        };
        BlurButton.Click += (_, _) =>
        {
            Shadow.BlurRadius = (Shadow.BlurRadius + 2) % 10;
            SetStatus("blur", BlurReadout, $"Blur: {Shadow.BlurRadius:0}");
        };
        panel.AddChild(ToggleButton);
        panel.AddChild(BlurButton);

        SecretBox.PasswordChanged += (_, _) =>
        {
            SetStatus("typed", TypedReadout, $"Typed: {SecretBox.Password.Length} chars");
            SetStatus("masked", MaskedReadout,
                $"Masked: {new string(SecretBox.PasswordChar, SecretBox.Password.Length)}");
        };
        panel.AddChild(SecretBox);
        panel.AddChild(TypedReadout);
        panel.AddChild(MaskedReadout);

        Docs.Navigated += (_, e) =>
        {
            SetStatus("page", PageReadout, $"Page: {e.Uri}");
            SetStatus("back", BackReadout, Docs.CanGoBack ? "Back: yes" : "Back: no");
        };
        GoButton.Click += (_, _) => Docs.Navigate(new Uri("https://example.com/"));
        BackButton.Click += (_, _) => Docs.GoBack();
        panel.AddChild(Docs);
        panel.AddChild(PageReadout);
        panel.AddChild(BackReadout);
        panel.AddChild(GoButton);
        panel.AddChild(BackButton);

        AlignXButton.Click += (_, _) =>
        {
            TileSample.AlignmentX = TileSample.AlignmentX switch
            {
                AlignmentX.Left => AlignmentX.Center,
                AlignmentX.Center => AlignmentX.Right,
                _ => AlignmentX.Left,
            };
            SetStatus("tile", TileReadout, $"Tile: {TileSample.AlignmentX}/{TileSample.AlignmentY}");
        };
        AlignYButton.Click += (_, _) =>
        {
            TileSample.AlignmentY = TileSample.AlignmentY switch
            {
                AlignmentY.Top => AlignmentY.Center,
                AlignmentY.Center => AlignmentY.Bottom,
                _ => AlignmentY.Top,
            };
            SetStatus("tile", TileReadout, $"Tile: {TileSample.AlignmentX}/{TileSample.AlignmentY}");
        };
        side.AddChild(TileReadout);
        side.AddChild(AlignXButton);
        side.AddChild(AlignYButton);

        PowerButton.Click += (_, _) => Microsoft.Win32.SystemEvents.RaisePowerModeChanged(Microsoft.Win32.PowerModes.Suspend);
        side.AddChild(PowerReadout);
        side.AddChild(PowerButton);

        PreviewPlayer.MediaOpened += (_, _) => SetStatus("media", MediaReadout, $"Media: opened {ClipName()}");
        PreviewPlayer.MediaFailed += (_, e) => SetStatus("media", MediaReadout, $"Media: failed {e.ErrorException.GetType().Name}");
        OpenButton.Click += (_, _) => PreviewPlayer.Open(new Uri("file:///clip.mp4"));
        PlayButton.Click += (_, _) =>
        {
            PreviewPlayer.Play();
            if (PreviewPlayer.Source is not null)
                SetStatus("media", MediaReadout, $"Media: playing {ClipName()}");
        };
        PauseButton.Click += (_, _) =>
        {
            PreviewPlayer.Pause();
            if (PreviewPlayer.Source is not null)
                SetStatus("media", MediaReadout, $"Media: paused {ClipName()}");
        };
        side.AddChild(MediaReadout);
        side.AddChild(OpenButton);
        side.AddChild(PlayButton);
        side.AddChild(PauseButton);

        panel.AddChild(ContainerReadout);
        SetStatus("tile", TileReadout, TileReadout.Text);
        SetStatus("power", PowerReadout, PowerReadout.Text);
        SetStatus("media", MediaReadout, MediaReadout.Text);
        SetStatus("container", ContainerReadout, $"Container ops: {MeasureContainerInk()}");
        SetStatus("shadow", ShadowReadout, "Shadow: on");
        SetStatus("blur", BlurReadout, $"Blur: {Shadow.BlurRadius:0}");
        SetStatus("typed", TypedReadout, TypedReadout.Text);
        SetStatus("masked", MaskedReadout, MaskedReadout.Text);
        SetStatus("page", PageReadout, PageReadout.Text);
        SetStatus("back", BackReadout, BackReadout.Text);
        Window.Content = root;
    }

    private string ClipName() => PreviewPlayer.Source?.Segments.LastOrDefault()?.Trim('/') ?? "-";

    private static int MeasureContainerInk()
    {
        var container = new ContainerVisual();
        var drawing = new DrawingVisual();
        drawing.RenderOpen().DrawRectangle(0, 0, 40, 20, 0xFF2A2AFF);
        container.Children.Add(drawing);
        var dc = new DrawingContext();
        container.Render(dc);
        return dc.Commands.Count;
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
            ["toggle"] = Rect(ToggleButton),
            ["blur"] = Rect(BlurButton),
            ["secret"] = Rect(SecretBox),
            ["go"] = Rect(GoButton),
            ["back"] = Rect(BackButton),
            ["alignx"] = Rect(AlignXButton),
            ["aligny"] = Rect(AlignYButton),
            ["power"] = Rect(PowerButton),
            ["open"] = Rect(OpenButton),
            ["play"] = Rect(PlayButton),
            ["pause"] = Rect(PauseButton),
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
