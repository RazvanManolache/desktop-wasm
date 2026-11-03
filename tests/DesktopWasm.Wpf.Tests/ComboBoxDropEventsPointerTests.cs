using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Drop-event centrality completion (jobs 64/65): pointer-driven closes —
/// header toggle-click and outside-press light-dismiss — fire the same
/// DropDownClosed event through the central setter.
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxDropEventsPointerTests
{
    private static readonly WasmWindowHandle Handle = new(1);

    private static (WasmInputRouter Router, ComboBox Box) Harness(UIElement? sibling = null)
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var stack = new StackPanel();
        var combo = new ComboBox { Width = 400 };
        combo.AddItem(new ComboBoxItem { Content = "Blue" });
        combo.AddItem(new ComboBoxItem { Content = "Green" });
        stack.AddChild(combo);
        if (sibling is not null) stack.AddChild(sibling);
        root.Content = stack;
        manager.AddRoot(root);
        root.Measure(new WpfSize(800, 600));
        root.Arrange(new WpfRect(0, 0, 800, 600));
        Assert.True(manager.TryFocus(combo));
        return (router, combo);
    }

    private static void Press(WasmInputRouter router, double x, double y)
    {
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true));
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, false));
    }

    [Fact]
    public void Header_click_close_fires_closed()
    {
        var (router, combo) = Harness();
        var closed = 0;
        combo.DropDownClosed += (_, _) => closed++;
        combo.IsDropDownOpen = true;

        Press(router, combo.RenderRect.X + 20, combo.RenderRect.Y + 15);

        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Light_dismiss_close_fires_closed()
    {
        var sibling = new Button { Content = "Other", Width = 400 };
        var (router, combo) = Harness(sibling);
        var closed = 0;
        combo.DropDownClosed += (_, _) => closed++;
        combo.IsDropDownOpen = true;

        var r = sibling.RenderRect;
        Press(router, r.X + 20, r.Y + r.Height / 2);

        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(1, closed);
    }
}
