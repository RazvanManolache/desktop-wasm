using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// ComboBox parity with J14 (ListBox click-then-arrows): mouse-toggling
/// the header leaves keyboard focus on the box, so arrows keep navigating
/// after the pointer is done.
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxClickThenArrowsTests
{
    private static (WasmInputRouter Router, ComboBox Box) Harness()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var combo = new ComboBox { Width = 400 };
        combo.AddItem(new ComboBoxItem { Content = "Blue" });
        combo.AddItem(new ComboBoxItem { Content = "Green" });
        combo.AddItem(new ComboBoxItem { Content = "Red" });
        root.Content = combo;
        manager.AddRoot(root);
        root.Measure(new WpfSize(800, 600));
        root.Arrange(new WpfRect(0, 0, 800, 600));
        return (router, combo);
    }

    [Fact]
    public void Header_clicks_then_arrows_keep_navigating()
    {
        var (router, combo) = Harness();
        var handle = new WasmWindowHandle(1);
        var x = combo.RenderRect.X + 20;
        var y = combo.RenderRect.Y + 15;

        // Open then shut via header clicks (toggle), pointer done.
        router.DispatchPointer(new WasmPointerArgs(handle, x, y, WasmPointerButton.Left, true));
        router.DispatchPointer(new WasmPointerArgs(handle, x, y, WasmPointerButton.Left, false));
        Assert.True(combo.IsDropDownOpen);
        router.DispatchPointer(new WasmPointerArgs(handle, x, y, WasmPointerButton.Left, true));
        router.DispatchPointer(new WasmPointerArgs(handle, x, y, WasmPointerButton.Left, false));
        Assert.False(combo.IsDropDownOpen);

        router.DispatchKeyboard(new WasmKeyArgs(handle, "ArrowDown", true, false, false, false));

        Assert.Equal(0, combo.SelectedIndex);
    }
}
