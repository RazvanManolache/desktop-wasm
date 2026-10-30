using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

[ResetAmbientState]
public sealed class LayoutTests
{
    [Fact]
    public void StackPanel_stacks_children_vertically()
    {
        var panel = new StackPanel();
        var ok = new Button { Content = "OK" };
        var label = new TextBlock { Text = "Hello" };
        panel.AddChild(ok);
        panel.AddChild(label);

        panel.Measure(new WpfSize(800, 600));
        panel.Arrange(new WpfRect(0, 0, 800, 600));

        Assert.Equal(120, panel.DesiredSize.Width); // max child width (button)
        Assert.Equal(52, panel.DesiredSize.Height); // 32 button + 20 text
        Assert.Equal(new WpfRect(0, 0, 800, 32), ok.ArrangedRect);
        Assert.Equal(new WpfRect(0, 32, 800, 20), label.ArrangedRect);
    }

    [Fact]
    public void DependencyProperty_roundtrips_and_notifies()
    {
        var el = new Button();
        var seen = new List<string>();
        el.PropertyChanged += (dp, _, _) => seen.Add(dp.Name);
        el.Width = 200;
        Assert.Equal(200, el.Width);
        Assert.Contains("Width", seen);
    }
}

[ResetAmbientState]
public sealed class MilCoreTests
{
    [Fact]
    public void Window_renders_commands_and_replays_to_skia()
    {
        using var surface = new SkiaSurface(800, 600);
        var mil = new WasmMilCore(surface);
        var window = new Window { Title = "Smoke" };
        var panel = new StackPanel();
        panel.AddChild(new Button { Content = "OK" });
        panel.AddChild(new TextBlock { Text = "Hello" });
        window.Content = panel;

        var commands = mil.Render(window, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));
        Assert.NotEmpty(commands);
        mil.CommitChannel(commands); // must not throw

        var json = WasmMilCore.ExportFrameJson(commands);
        Assert.Contains("OK", json, StringComparison.Ordinal);
        Assert.Contains("Hello", json, StringComparison.Ordinal);

        var snapshot = surface.Snapshot();
        Assert.Equal(800, snapshot.Width);
        Assert.Equal(600, snapshot.Height);
    }
}
