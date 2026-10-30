using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

[ResetAmbientState]
public sealed class InteropHostsTests
{
    private sealed class BarChart : IImmediatePaintable
    {
        public WpfRect PaintedBounds;
        public int PaintCalls;

        public WpfSize Measure(WpfSize available) => new(
            Math.Min(available.Width, 300), Math.Min(available.Height, 120));

        public void Paint(DrawingContext dc, WpfRect bounds)
        {
            PaintCalls++;
            PaintedBounds = bounds;
            dc.DrawRectangle((float)bounds.X, (float)bounds.Y, 10, 10, 0xFF0000FF);
        }
    }

    [Fact]
    public void WindowsFormsHost_delegates_measure_and_clips_paint()
    {
        var chart = new BarChart();
        var host = new WindowsFormsHost { Child = chart };
        host.Measure(new WpfSize(800, 600));
        Assert.Equal(new Size(300, 120), host.DesiredSize);

        host.Arrange(new WpfRect(10, 20, 800, 600));
        var dc = new DrawingContext();
        host.Render(dc);

        Assert.Equal(1, chart.PaintCalls);
        Assert.Equal(new WpfRect(10, 20, 800, 600), chart.PaintedBounds);

        // Clip surrounds the immediate content; commands stay in one shared list.
        Assert.IsType<DrawCommand.PushClip>(dc.Commands[0]);
        Assert.IsType<DrawCommand.FillRect>(dc.Commands[1]);
        Assert.IsType<DrawCommand.PopClip>(dc.Commands[2]);
        var clip = Assert.IsType<DrawCommand.PushClip>(dc.Commands[0]);
        Assert.Equal(new DrawCommand.PushClip(10, 20, 800, 600), clip);
    }

    [Fact]
    public void WindowsFormsHost_empty_is_silent()
    {
        var host = new WindowsFormsHost();
        host.Measure(new WpfSize(800, 600));
        Assert.Equal(new Size(0, 0), host.DesiredSize);
        var dc = new DrawingContext();
        host.Render(dc);
        Assert.Empty(dc.Commands);
    }

    [Fact]
    public void ElementHost_renders_content_to_shared_commands()
    {
        var window = new Window();
        window.Content = new Button { Content = "Hi" };
        var host = new ElementHost { Content = window, Size = new WpfSize(800, 600) };

        var commands = host.Render();
        Assert.Contains(commands, c => c is DrawCommand.DrawText t && t.Text == "Hi");

        var json = host.ExportFrameJson();
        Assert.Contains("Hi", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ElementHost_requires_content()
    {
        Assert.Throws<InvalidOperationException>(() => new ElementHost().Render());
    }
}
