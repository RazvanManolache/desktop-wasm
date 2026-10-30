using DesktopWasm.Graphics;
using DesktopWasm.Wpf;
using Xunit;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Buttons host visual content live (gallery HeaderTile tiles);
/// text content keeps the label path.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class ButtonContentTests
{
    [Fact]
    public void Button_renders_visual_content_instead_of_typename()
    {
        var inner = new TextBlock { Text = "Getting started" };
        var button = new Button { Content = inner };
        button.Measure(new WpfSize(300, 100));
        button.Arrange(new WpfRect(0, 0, 300, 100));
        Assert.Equal(new WpfRect(1, 1, 298, 98), inner.RenderRect);
        var dc = new DrawingContext();
        button.Render(dc);
        var texts = dc.Commands.OfType<DrawCommand.DrawText>().ToList();
        Assert.NotEmpty(texts);
        Assert.All(texts, t => Assert.DoesNotContain("DesktopWasm.Wpf", t.Text));
        Assert.Contains(texts, t => t.Text == "Getting started");
    }

    [Fact]
    public void Button_renders_text_content_as_label()
    {
        var button = new Button { Content = "Go" };
        button.Measure(new WpfSize(300, 100));
        button.Arrange(new WpfRect(0, 0, 300, 100));
        var dc = new DrawingContext();
        button.Render(dc);
        var texts = dc.Commands.OfType<DrawCommand.DrawText>().ToList();
        Assert.Contains(texts, t => t.Text == "Go");
    }
}
