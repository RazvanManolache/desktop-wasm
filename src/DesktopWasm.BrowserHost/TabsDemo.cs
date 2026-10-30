using DesktopWasm.Core;
using DesktopWasm.Wpf;

namespace DesktopWasm.BrowserHost;

// Tabs lab (engine-native ScreenToGif tab/progress/tooltip slice): a
// three-page TabControl switches header selection with content swapping
// (the HideableTabControl/AwareTabItem ribbon shape), a bump button drives
// a ProgressBar value with a live readout (the encoder Percentage shape),
// and a hovered button opens its ToolTipService hint (the toolbar tooltip
// shape). Readouts mirror every state into the noter channel so the
// browser proof asserts post-input text, not just repaint hashes.
public sealed class TabsLab
{
    public static TabsLab? Current { get; private set; }

    public Window Window { get; } = new() { Title = "Tabs lab" };

    public TabControl Tabs { get; } = new() { TabStripPlacement = Dock.Top };

    public TextBlock TabReadout { get; } = new() { Text = "Tab: 0/Home", Margin = new Thickness(8, 4, 8, 4) };

    public ProgressBar Progress { get; } = new() { Minimum = 0, Maximum = 100, Width = 300, Height = 20, Margin = new Thickness(8, 4, 8, 4) };

    public Button Bump { get; } = new() { Content = "Bump +25", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public TextBlock ProgressReadout { get; } = new() { Text = "Progress: 0", Margin = new Thickness(8, 4, 8, 4) };

    public Button TipButton { get; } = new() { Content = "Hover me", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public TextBlock TipReadout { get; } = new() { Text = "Tip: hidden", Margin = new Thickness(8, 4, 8, 4) };

    /// <summary>Automation channel mirroring each readout (the browser file replays it for state assertions).</summary>
    public Action<string, string>? Noter { get; set; }

    /// <summary>Latest status per key (the browser file replays these after attaching its noter).</summary>
    public IReadOnlyDictionary<string, string> Notes => _notes;

    private readonly Dictionary<string, string> _notes = new();
    private WasmWpfHost? _host;

    public static Window Build()
    {
        var lab = new TabsLab();
        Current = lab;
        lab.Compose();
        return lab.Window;
    }

    public void Compose()
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.AddChild(new TextBlock
        {
            Text = "Pick a tab, bump the progress, hover for the tip.",
            Margin = new Thickness(8, 8, 8, 4),
        });

        Tabs.Width = 400;
        Tabs.Height = 160;
        Tabs.AddChild(new TabItem { Header = "Home", Content = new TextBlock { Text = "Home page body" } });
        Tabs.AddChild(new TabItem { Header = "Profile", Content = new TextBlock { Text = "Profile page body" } });
        Tabs.AddChild(new TabItem { Header = "Settings", Content = new TextBlock { Text = "Settings page body" } });
        Tabs.SelectionChanged += (_, _) => SetStatus("tab", TabReadout,
            $"Tab: {Tabs.SelectedIndex}/{HeaderText(Tabs.SelectedItem)}");
        panel.AddChild(Tabs);
        panel.AddChild(TabReadout);

        Progress.ValueChanged += (_, e) => SetStatus("progress", ProgressReadout, $"Progress: {e.NewValue:0}");
        Bump.Click += (_, _) => Progress.Value = Math.Min(Progress.Maximum, Progress.Value + 25);
        panel.AddChild(Progress);
        panel.AddChild(Bump);
        panel.AddChild(ProgressReadout);

        ToolTipService.SetToolTip(TipButton, "Tabs tip: hover works.");
        TipButton.MouseEnter += (_, _) => SetStatus("tip", TipReadout, "Tip: shown");
        TipButton.MouseLeave += (_, _) => SetStatus("tip", TipReadout, "Tip: hidden");
        panel.AddChild(TipButton);
        panel.AddChild(TipReadout);

        Window.Content = panel;
        SetStatus("tab", TabReadout, TabReadout.Text);
        SetStatus("progress", ProgressReadout, ProgressReadout.Text);
        SetStatus("tip", TipReadout, TipReadout.Text);
    }

    private static string HeaderText(object? item) =>
        item is TabItem tab ? tab.Header?.ToString() ?? "?" : item?.ToString() ?? "?";

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
            ["tab2"] = Rect(Tabs.HeaderRect(1)),
            ["bump"] = Rect(Bump),
            ["tip"] = Rect(TipButton),
            ["progress"] = Rect(Progress),
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

    private static string Rect(WpfRect r) => $"{r.X:0},{r.Y:0},{r.Width:0},{r.Height:0}";
}
