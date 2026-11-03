using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Native ComboBox parity: the mouse wheel over an open drop scrolls the
/// drop rows (like the viewer idiom: pixels map through the 24px row
/// stride), instead of doing nothing.
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxDropWheelTests
{
    private static readonly WasmWindowHandle Handle = new(1);

    private static (WasmInputRouter Router, ComboBox Box) Harness(int count = 20)
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var combo = new ComboBox { Width = 400 };
        for (var i = 0; i < count; i++) combo.AddItem(new ComboBoxItem { Content = $"C{i}" });
        root.Content = combo;
        manager.AddRoot(root);
        root.Measure(new WpfSize(800, 600));
        root.Arrange(new WpfRect(0, 0, 800, 600));
        return (router, combo);
    }

    [Fact]
    public void Wheel_over_open_drop_scrolls_rows()
    {
        var (router, combo) = Harness();
        combo.IsDropDownOpen = true;

        var dropTop = combo.RenderRect.Y + 30;
        router.DispatchWheel(new WasmWheelArgs(Handle, combo.RenderRect.X + 20, dropTop + 40, 120));

        // 120px / 24px stride = 5 rows: hovering the top slot must hit C5.
        router.DispatchPointer(new WasmPointerArgs(Handle, combo.RenderRect.X + 20, dropTop + 12, WasmPointerButton.Left, false)
            { Kind = WasmPointerKind.Move });

        var items = combo.Items.Cast<object>().ToList();
        Assert.True(((ComboBoxItem)items[5]).IsHighlighted);
        Assert.False(((ComboBoxItem)items[0]).IsHighlighted);
    }

    [Fact]
    public void Wheel_up_scrolls_drop_back()
    {
        var (router, combo) = Harness();
        combo.IsDropDownOpen = true;
        var dropTop = combo.RenderRect.Y + 30;
        var x = combo.RenderRect.X + 20;
        router.DispatchWheel(new WasmWheelArgs(Handle, x, dropTop + 40, 120));
        router.DispatchWheel(new WasmWheelArgs(Handle, x, dropTop + 40, -120));

        router.DispatchPointer(new WasmPointerArgs(Handle, x, dropTop + 12, WasmPointerButton.Left, false)
            { Kind = WasmPointerKind.Move });

        var items = combo.Items.Cast<object>().ToList();
        Assert.True(((ComboBoxItem)items[0]).IsHighlighted);
        Assert.True(combo.IsDropDownOpen);
    }

    [Fact]
    public void Wheel_over_closed_box_reaches_page_scroller()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var view = new ScrollViewer { Width = 400, Height = 100 };
        var stack = new StackPanel();
        var combo = new ComboBox { Width = 380 };
        combo.AddItem(new ComboBoxItem { Content = "Only" });
        stack.AddChild(combo);
        for (var i = 0; i < 10; i++) stack.AddChild(new Button { Content = $"B{i}" });
        view.Content = stack;
        root.Content = view;
        manager.AddRoot(root);
        root.Measure(new WpfSize(800, 600));
        root.Arrange(new WpfRect(0, 0, 800, 600));

        var r = combo.RenderRect;
        router.DispatchWheel(new WasmWheelArgs(Handle, r.X + r.Width / 2, r.Y + r.Height / 2, 120));

        Assert.False(combo.IsDropDownOpen);
        Assert.True(view.VerticalOffset > 0);
    }
}
