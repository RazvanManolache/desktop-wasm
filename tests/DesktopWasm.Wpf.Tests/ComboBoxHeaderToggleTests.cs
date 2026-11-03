using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// ComboBox header toggle parity: pressing the header while the drop is
/// open closes it without touching the selection (the open path is covered
/// by the press-toggle test; this locks the shut half).
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxHeaderToggleTests
{
    [Fact]
    public void Header_press_closes_open_drop_keeping_selection()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new StackPanel();
        var combo = new ComboBox();
        combo.AddItem(new ComboBoxItem { Content = "Blue" });
        combo.AddItem(new ComboBoxItem { Content = "Green" });
        root.AddChild(combo);
        manager.AddRoot(root);
        root.Measure(new WpfSize(400, 400));
        root.Arrange(new WpfRect(0, 0, 400, 400));
        var handle = new WasmWindowHandle(1);

        combo.IsDropDownOpen = true;
        var hr = combo.RenderRect;
        router.DispatchPointer(new WasmPointerArgs(handle, hr.X + 20, hr.Y + 15, WasmPointerButton.Left, true));
        router.DispatchPointer(new WasmPointerArgs(handle, hr.X + 20, hr.Y + 15, WasmPointerButton.Left, false));

        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(-1, combo.SelectedIndex);
    }
}
