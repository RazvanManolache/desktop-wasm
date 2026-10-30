using DesktopWasm.Core;
using DesktopWasm.Wpf;

namespace DesktopWasm.BrowserHost;

// Animation lab (engine-native ScreenToGif HideableTabControl slice): Show
// runs the original show shapes (DoubleAnimation height/opacity with
// PowerEase 8, ObjectAnimationUsingKeyFrames visibility, ThicknessAnimation
// margin) and Hide runs the mirrored hide shapes. A dispatcher poller
// mirrors Height/Visibility/Margin into readouts and the noter channel, so
// the browser proof asserts interpolated post-click state, not repaint
// hashes alone. Pure engine surface; the proof drives the real pointer
// path through the shared input surface.
public sealed class AnimationLab
{
    public static AnimationLab? Current { get; private set; }

    public Window Window { get; } = new() { Title = "Animation lab" };

    public Border Stage { get; } = new()
    {
        Width = 400,
        Height = 100,
        Opacity = 1.0,
        BackgroundRgba = 0x156E61FF,
        CornerRadius = new CornerRadius(10),
        Margin = new Thickness(8, 4, 8, 4),
    };

    public Border Ribbon { get; } = new()
    {
        Width = 400,
        Height = 24,
        BackgroundRgba = 0xEDF4ECFF,
        CornerRadius = new CornerRadius(6),
        Margin = new Thickness(5, 5, 0, -1),
    };

    public Button ShowButton { get; } = new() { Content = "Show panel", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public Button HideButton { get; } = new() { Content = "Hide panel", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public TextBlock HeightReadout { get; } = new() { Text = "Height: 100", Margin = new Thickness(8, 4, 8, 0) };

    public TextBlock VisibilityReadout { get; } = new() { Text = "Visibility: Visible", Margin = new Thickness(8, 0, 8, 0) };

    public TextBlock MarginReadout { get; } = new() { Text = "Margin: 5,5,0,-1", Margin = new Thickness(8, 0, 8, 4) };

    /// <summary>Automation channel mirroring each readout (the browser file replays it for state assertions).</summary>
    public Action<string, string>? Noter { get; set; }

    /// <summary>Latest status per key (the browser file replays these after attaching its noter).</summary>
    public IReadOnlyDictionary<string, string> Notes => _notes;

    private readonly Dictionary<string, string> _notes = new();
    private readonly DispatcherTimer _poller = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private WasmWpfHost? _host;

    private static readonly Duration Second = TimeSpan.FromSeconds(1);

    public static Window Build()
    {
        var lab = new AnimationLab();
        Current = lab;
        lab.Compose();
        return lab.Window;
    }

    public void Compose()
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.AddChild(new TextBlock
        {
            Text = "Show and hide the panel: height, opacity, visibility and margin clocks.",
            Margin = new Thickness(8, 8, 8, 4),
        });
        ShowButton.Click += (_, _) => Show();
        HideButton.Click += (_, _) => Hide();
        panel.AddChild(ShowButton);
        panel.AddChild(HideButton);
        panel.AddChild(HeightReadout);
        panel.AddChild(VisibilityReadout);
        panel.AddChild(MarginReadout);
        Stage.AddChild(new TextBlock
        {
            Text = "Animated content",
            ForegroundRgba = 0xFFFFFFFF,
            Margin = new Thickness(22, 25, 10, 0),
        });
        panel.AddChild(Stage);
        panel.AddChild(Ribbon);
        Window.Content = panel;
        _poller.Tick += (_, _) => RefreshReadouts();
        _poller.Start();
        RefreshReadouts();
    }

    /// <summary>Original show shapes: grow + fade in, reveal the button mid-flight, slide the ribbon in.</summary>
    public void Show()
    {
        var height = new DoubleAnimation(Stage.Height, 100, Second)
        {
            EasingFunction = new PowerEase { Power = 8 },
        };
        Stage.BeginAnimation(FrameworkElement.HeightProperty, height);

        var opacity = new DoubleAnimation(Stage.Opacity, 1, Second)
        {
            EasingFunction = new PowerEase { Power = 8 },
        };
        Stage.BeginAnimation(UIElement.OpacityProperty, opacity);

        var visibility = new ObjectAnimationUsingKeyFrames();
        visibility.KeyFrames.Add(new DiscreteObjectKeyFrame(Visibility.Visible, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5))));
        HideButton.BeginAnimation(UIElement.VisibilityProperty, visibility);

        var margin = new ThicknessAnimation(Ribbon.Margin, new Thickness(5, 5, 0, -1), Second)
        {
            EasingFunction = new PowerEase { Power = 8 },
        };
        Ribbon.BeginAnimation(FrameworkElement.MarginProperty, margin);
    }

    /// <summary>Original hide shapes: shrink + fade out, hide the button at once, slide the ribbon out.</summary>
    public void Hide()
    {
        var height = new DoubleAnimation(Stage.Height, 0, Second)
        {
            EasingFunction = new PowerEase { Power = 8 },
        };
        Stage.BeginAnimation(FrameworkElement.HeightProperty, height);

        var opacity = new DoubleAnimation(Stage.Opacity, 0, Second)
        {
            EasingFunction = new PowerEase { Power = 8 },
        };
        Stage.BeginAnimation(UIElement.OpacityProperty, opacity);

        var visibility = new ObjectAnimationUsingKeyFrames();
        visibility.KeyFrames.Add(new DiscreteObjectKeyFrame(Visibility.Collapsed, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0))));
        HideButton.BeginAnimation(UIElement.VisibilityProperty, visibility);

        var margin = new ThicknessAnimation(Ribbon.Margin, new Thickness(5, 5, 0, 5), Second)
        {
            EasingFunction = new PowerEase { Power = 8 },
        };
        Ribbon.BeginAnimation(FrameworkElement.MarginProperty, margin);
    }

    internal void RefreshReadouts()
    {
        SetStatus("height", HeightReadout, $"Height: {Stage.Height:0}");
        SetStatus("visibility", VisibilityReadout, $"Visibility: {HideButton.Visibility}");
        SetStatus("margin", MarginReadout,
            $"Margin: {Ribbon.Margin.Left:0},{Ribbon.Margin.Top:0},{Ribbon.Margin.Right:0},{Ribbon.Margin.Bottom:0}");
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
        if (readout.Text == text && _notes.TryGetValue(key, out var prior) && prior == text) return;
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
            ["show"] = Rect(ShowButton),
            ["hide"] = Rect(HideButton),
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
