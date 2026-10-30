using DesktopWasm.Wpf;

namespace DesktopWasm.BrowserHost;

// Text lab (engine-native ScreenToGif text/font/DPI slice): a decorated
// caption cycles Underline/Strikethrough/None through the real
// TextDecoration paint path (the StorageSettings strikethrough shape), a
// FormattedText outline renders as retained geometry (the TextPath shape),
// and the session DPI scale reads out live with a host-sync zoom (the
// recorder/editor DpiChanged shape). Readouts mirror every state into the
// noter channel so the browser proof asserts post-input text and pixels,
// not just repaint hashes.
public sealed class TextLab
{
    public static TextLab? Current { get; private set; }

    public Window Window { get; } = new() { Title = "Text lab" };

    public TextBlock Caption { get; } = new()
    {
        Text = "EDITOR TITLE",
        FontCss = "20px sans-serif",
        Margin = new Thickness(8, 8, 8, 4),
    };

    public Button Cycle { get; } = new() { Content = "Cycle decoration", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public TextBlock DecorationReadout { get; } = new() { Text = "Decoration: Underline", Margin = new Thickness(8, 4, 8, 4) };

    public DesktopWasm.Wpf.Path Outline { get; } = new() { Fill = Brushes.Black, Margin = new Thickness(8, 4, 8, 4) };

    public TextBlock OutlineReadout { get; } = new() { Text = "Outline: pending", Margin = new Thickness(8, 4, 8, 4) };

    public TextBlock DpiReadout { get; } = new() { Text = "Dpi: 1", Margin = new Thickness(8, 4, 8, 4) };

    public Button Zoom { get; } = new() { Content = "Zoom x2", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    private int _decorationIndex;

    /// <summary>Automation channel mirroring each readout (the browser file replays it for state assertions).</summary>
    public Action<string, string>? Noter { get; set; }

    /// <summary>Latest status per key (the browser file replays these after attaching its noter).</summary>
    public IReadOnlyDictionary<string, string> Notes => _notes;

    private readonly Dictionary<string, string> _notes = new();
    private WasmWpfHost? _host;

    public static Window Build()
    {
        var lab = new TextLab();
        Current = lab;
        lab.Compose();
        return lab.Window;
    }

    public void Compose()
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.AddChild(new TextBlock
        {
            Text = "Typeface shaping, decorations and session DPI.",
            Margin = new Thickness(8, 8, 8, 4),
        });

        ApplyDecoration();
        Cycle.Click += (_, _) =>
        {
            _decorationIndex = (_decorationIndex + 1) % 3;
            ApplyDecoration();
        };
        panel.AddChild(Caption);
        panel.AddChild(Cycle);
        panel.AddChild(DecorationReadout);

        BuildOutline();
        panel.AddChild(Outline);
        panel.AddChild(OutlineReadout);

        RefreshDpi();
        Zoom.Click += (_, _) =>
        {
            var current = VisualTreeHelper.GetDpi(Window);
            var next = current.DpiScaleX >= 1.5 ? new DpiScale(1.0, 1.0) : new DpiScale(2.0, 2.0);
            VisualTreeHelper.SetRootDpi(next);
            RefreshDpi();
        };
        panel.AddChild(DpiReadout);
        panel.AddChild(Zoom);

        Window.Content = panel;
    }

    private void ApplyDecoration()
    {
        var name = _decorationIndex switch { 0 => "Underline", 1 => "Strikethrough", _ => "None" };
        Caption.TextDecorations = _decorationIndex switch
        {
            0 => TextDecorations.Parse("Underline"),
            1 => new TextDecorationCollection
            {
                new TextDecoration(TextDecorationLocation.Strikethrough, new Pen(Brushes.DarkSlateGray, 1), 0,
                    TextDecorationUnit.FontRecommended, TextDecorationUnit.FontRecommended),
            },
            _ => new TextDecorationCollection(),
        };
        SetStatus("decoration", DecorationReadout, $"Decoration: {name}");
    }

    private void BuildOutline()
    {
        // TextPath shape: resolve the session typeface at session DPI, build
        // the glyph outline geometry, normalize through Bounds.
        var face = new Typeface(new FontFamily("sans-serif"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var dpi = VisualTreeHelper.GetDpi(Window).PixelsPerDip;
        var formatted = new FormattedText("Gif", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, face, 48, Brushes.Black, dpi);
        var geometry = formatted.BuildGeometry(new Point(0, 48));
        geometry.Transform = new TranslateTransform(-geometry.Bounds.X, -geometry.Bounds.Y);
        Outline.Data = geometry;
        Outline.Width = Math.Ceiling(geometry.Bounds.Width);
        Outline.Height = Math.Ceiling(geometry.Bounds.Height);
        SetStatus("outline", OutlineReadout,
            $"Outline: {geometry.Bounds.Width:0.0}x{geometry.Bounds.Height:0.0} from {formatted.Width:0.0}px text");
    }

    private void RefreshDpi()
    {
        var dpi = VisualTreeHelper.GetDpi(Window);
        SetStatus("dpi", DpiReadout, $"Dpi: {dpi.PixelsPerDip:0.#} ({dpi.PixelsPerInchX:0} ppi)");
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
            ["caption"] = Rect(Caption),
            ["cycle"] = Rect(Cycle),
            ["outline"] = Rect(Outline),
            ["zoom"] = Rect(Zoom),
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
