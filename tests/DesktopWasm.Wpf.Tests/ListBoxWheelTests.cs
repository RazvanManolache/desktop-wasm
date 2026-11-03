using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Native ListBox parity: the wheel over the list scrolls whole rows
/// through its ancestor scroller (a small tick still moves a full row),
/// instead of the viewer's raw-pixel creep.
/// </summary>
[ResetAmbientState]
public sealed class ListBoxWheelTests
{
    private static readonly WasmWindowHandle Handle = new(1);

    private static (WasmInputRouter Router, ScrollViewer View, ListBox Box) Harness(int count = 20)
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var view = new ScrollViewer { Width = 400, Height = 100 };
        var box = new ListBox();
        for (var i = 0; i < count; i++) box.AddChild(new ListBoxItem { Content = $"R{i}" });
        view.Content = box;
        root.Content = view;
        manager.AddRoot(root);
        root.Measure(new WpfSize(800, 600));
        root.Arrange(new WpfRect(0, 0, 800, 600));
        return (router, view, box);
    }

    [Fact]
    public void Small_wheel_tick_scrolls_one_full_row()
    {
        var (router, view, box) = Harness();
        Assert.Equal(0, view.VerticalOffset);
        var rowH = box.Items[0].RenderRect.Height;
        Assert.True(rowH > 5, $"rows must dwarf the probe tick (rowH={rowH})");

        router.DispatchWheel(new WasmWheelArgs(Handle, box.RenderRect.X + 20, box.RenderRect.Y + 12, 5));

        Assert.Equal(rowH, view.VerticalOffset);
    }

    [Fact]
    public void Wheel_up_scrolls_rows_back()
    {
        var (router, view, box) = Harness();
        var x = box.RenderRect.X + 20;
        var y = box.RenderRect.Y + 12;
        router.DispatchWheel(new WasmWheelArgs(Handle, x, y, 120));
        var down = view.VerticalOffset;
        Assert.True(down > 0);

        router.DispatchWheel(new WasmWheelArgs(Handle, x, y, -120));

        Assert.True(view.VerticalOffset < down);
    }
}
