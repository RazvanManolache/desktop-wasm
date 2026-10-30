using DesktopWasm.Graphics;
using DesktopWasm.Wpf;
using Xunit;

namespace DesktopWasm.Wpf.Tests;

/// <summary>ContentPresenter hosts visual content live (gallery HeaderTile
/// image slot); text content keeps the label path.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class ContentPresenterTests
{
    [Fact]
    public void ContentPresenter_hosts_visual_content()
    {
        var inner = new TextBlock { Text = "Tile image" };
        var presenter = new ContentPresenter { Content = inner };
        presenter.Measure(new WpfSize(200, 100));
        presenter.Arrange(new WpfRect(0, 0, 200, 100));
        Assert.Equal(new WpfRect(0, 0, 200, 100), inner.RenderRect);
        var dc = new DrawingContext();
        presenter.Render(dc);
        var texts = dc.Commands.OfType<DrawCommand.DrawText>().ToList();
        Assert.Contains(texts, t => t.Text == "Tile image");
        Assert.All(texts, t => Assert.DoesNotContain("DesktopWasm.Wpf", t.Text));
    }
}
