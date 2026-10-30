using DesktopWasm.Core;
using DesktopWasm.Wpf;

namespace DesktopWasm.BrowserHost;

// Hit-test lab (engine-native DynamicResource + hit-test slice): three
// bordered rectangles exercise the real retained tree, a theme button swaps
// a live resource, clicks pick through VisualTreeHelper.HitTest, and drags
// rubber-band through a GeometryHitTestParameters callback walk (the
// DrawingCanvas.GetSelectedShapes shape). Readouts mirror every state into
// the noter channel so the browser proof asserts post-input state, not just
// repaint hashes.
public sealed class HitTestLab
{
    public static HitTestLab? Current { get; private set; }

    public Window Window { get; } = new() { Title = "Hit-test lab" };

    public Canvas Play { get; } = new() { Width = 800, Height = 380 };

    public Rectangle Alpha { get; } = new() { Name = "Alpha", Width = 140, Height = 100 };

    public Rectangle Beta { get; } = new() { Name = "Beta", Width = 140, Height = 100 };

    public Rectangle Gamma { get; } = new() { Name = "Gamma", Width = 140, Height = 100 };

    public TextBlock PickReadout { get; } = new() { Text = "Pick: none", Margin = new Thickness(8, 4, 8, 4) };

    public TextBlock SelectReadout { get; } = new() { Text = "Selected: none", Margin = new Thickness(8, 4, 8, 4) };

    public TextBlock ThemeReadout { get; } = new() { Text = "Theme: light", Margin = new Thickness(8, 4, 8, 4) };

    public Button SwapButton { get; } = new() { Content = "Swap theme", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public Action<string, string>? Noter { get; set; }

    public IReadOnlyDictionary<string, string> Notes => _notes;

    private readonly Dictionary<string, string> _notes = new();
    private readonly List<Border> _frames = new();
    private WasmWpfHost? _host;
    private Point? _bandStart;
    private Point _bandEnd;
    private bool _dark;

    public static Window Build()
    {
        var lab = new HitTestLab();
        Current = lab;
        lab.Compose();
        return lab.Window;
    }

    public void Compose()
    {
        EnsureResources();
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.AddChild(new TextBlock
        {
            Text = "Click a shape to pick it, drag empty space to rubber-band, swap the live theme.",
            Margin = new Thickness(8, 8, 8, 4),
        });

        AddFramed(Alpha, 60, 80, Colors.LightBlue);
        AddFramed(Beta, 260, 80, Colors.LightGreen);
        AddFramed(Gamma, 460, 80, Colors.LightYellow);
        panel.AddChild(Play);

        panel.AddChild(PickReadout);
        panel.AddChild(SelectReadout);
        panel.AddChild(ThemeReadout);
        SwapButton.Click += (_, _) => SwapTheme();
        panel.AddChild(SwapButton);

        Window.Content = panel;
        Play.PreviewMouseLeftButtonDown += (_, e) => PressAt(e.GetPosition(Play));
        Play.PreviewMouseMove += (_, e) => DragAt(e.GetPosition(Play));
        Play.PreviewMouseLeftButtonUp += (_, e) => ReleaseAt(e.GetPosition(Play));

        SetStatus("pick", PickReadout, PickReadout.Text);
        SetStatus("selected", SelectReadout, SelectReadout.Text);
        SetStatus("theme", ThemeReadout, ThemeReadout.Text);
    }

    public void Wire(WasmWpfHost host)
    {
        _host = host;
        WasmMilCore.RenderHeadless(Window, new WpfSize(800, 600));
        PublishLayoutNotes();
    }

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
            ["alpha"] = Rect(Alpha),
            ["beta"] = Rect(Beta),
            ["gamma"] = Rect(Gamma),
            ["swap"] = Rect(SwapButton),
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

    private void AddFramed(Rectangle shape, double left, double top, Color fill)
    {
        var frame = new Border
        {
            Width = shape.Width + 8,
            Height = shape.Height + 8,
            Thickness = 4,
        };
        shape.Fill = new SolidColorBrush(fill);
        frame.AddChild(shape);
        Canvas.SetLeft(frame, left);
        Canvas.SetTop(frame, top);
        Play.AddChild(frame);
        _frames.Add(frame);
        BindAccent(frame);
    }

    private void BindAccent(Border frame)
    {
        var extension = new DynamicResourceExtension("AccentBrush");
        extension.ProvideValue(new TargetProvider(new ValueTarget(frame, Border.BorderBrushProperty)));
    }

    private void EnsureResources()
    {
        var app = Application.Current ?? new Application();
        if (!app.Resources.Contains("AccentBrush"))
            app.Resources.Add("AccentBrush", new SolidColorBrush(Colors.DarkRed));
    }

    /// <summary>Click handling (also the engine-native pick entry).</summary>
    public void PressAt(Point point)
    {
        var pick = VisualTreeHelper.HitTest(Play, point);
        if (pick?.VisualHit is Rectangle rectangle && !string.IsNullOrEmpty(rectangle.Name))
        {
            _bandStart = null;
            SetStatus("pick", PickReadout, $"Pick: {rectangle.Name}");
            return;
        }
        _bandStart = point;
        _bandEnd = point;
    }

    /// <summary>Drag handling (also the engine-native band entry).</summary>
    public void DragAt(Point point)
    {
        if (_bandStart is null) return;
        _bandEnd = point;
    }

    /// <summary>Release handling (also the engine-native select entry).</summary>
    public void ReleaseAt(Point point)
    {
        if (_bandStart is null) return;
        var start = _bandStart.Value;
        _bandStart = null;
        var rect = new Rect(
            Math.Min(start.X, point.X), Math.Min(start.Y, point.Y),
            Math.Abs(point.X - start.X), Math.Abs(point.Y - start.Y));
        if (rect.Width < 4 && rect.Height < 4)
        {
            SetStatus("pick", PickReadout, "Pick: none");
            return;
        }
        var names = new List<string>();
        VisualTreeHelper.HitTest(Play, null,
            result =>
            {
                if (result.VisualHit is Rectangle shape && !string.IsNullOrEmpty(shape.Name))
                    names.Add(shape.Name);
                return HitTestResultBehavior.Continue;
            },
            new GeometryHitTestParameters(new RectangleGeometry(rect)));
        foreach (var shape in new[] { Alpha, Beta, Gamma })
        {
            var selected = names.Contains(shape.Name);
            shape.Stroke = selected ? new SolidColorBrush(Colors.Gold) : null;
            shape.StrokeThickness = selected ? 4 : 1;
        }
        names.Sort(StringComparer.Ordinal);
        SetStatus("selected", SelectReadout,
            names.Count == 0 ? "Selected: none" : $"Selected: {string.Join("+", names)}");
        _host?.Invalidate();
    }

    /// <summary>Theme swap (also the engine-native resource entry).</summary>
    public void SwapTheme()
    {
        _dark = !_dark;
        var app = Application.Current ?? new Application();
        app.Resources["AccentBrush"] = new SolidColorBrush(_dark ? Colors.Navy : Colors.DarkRed);
        SetStatus("theme", ThemeReadout, _dark ? "Theme: dark" : "Theme: light");
    }

    private sealed class ValueTarget(object targetObject, object targetProperty) : IProvideValueTarget
    {
        public object TargetObject => targetObject;
        public object TargetProperty => targetProperty;
    }

    private sealed class TargetProvider(IProvideValueTarget target) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IProvideValueTarget) ? target : null;
    }
}
