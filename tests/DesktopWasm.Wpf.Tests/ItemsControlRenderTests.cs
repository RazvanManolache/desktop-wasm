using DesktopWasm.Graphics;
using DesktopWasm.Wpf;
using Xunit;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Plain ItemsControl paints its generated containers chromeless.
/// Without an OnRender override the control measured and arranged item
/// containers that never painted (gallery sample lists stayed blank).</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class ItemsControlRenderTests
{
    [Fact]
    public void ItemsControl_renders_item_containers()
    {
        var control = new ItemsControl();
        control.AddItem(new TextBlock { Text = "Button" });
        control.AddItem(new TextBlock { Text = "Calendar" });
        var commands = WasmMilCore.RenderHeadless(control, new WpfSize(400, 600));
        var texts = commands.OfType<DrawCommand.DrawText>().Select(t => t.Text).ToList();
        Assert.Contains("Button", texts);
        Assert.Contains("Calendar", texts);
    }

    [Fact]
    public void ItemsControl_renders_items_host_panel_children()
    {
        var control = new ItemsControl
        {
            ItemsPanel = new ItemsPanelTemplate("<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />"),
        };
        control.AddItem(new TextBlock { Text = "Slider" });
        var commands = WasmMilCore.RenderHeadless(control, new WpfSize(400, 600));
        Assert.Contains(commands.OfType<DrawCommand.DrawText>(), t => t.Text == "Slider");
    }
}
