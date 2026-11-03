using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Native parity: pressing a disabled row never selects it — neither in
/// the ListBox nor in the ComboBox drop (the drop stays open, the
/// selection stays put).
/// </summary>
[ResetAmbientState]
public sealed class SelectorDisabledRowPressTests
{
    private static readonly WasmWindowHandle Handle = new(1);

    [Fact]
    public void ListBox_press_on_disabled_row_keeps_selection()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var box = new ListBox();
        box.AddChild(new ListBoxItem { Content = "R0" });
        var disabled = new ListBoxItem { Content = "R1", IsEnabled = false };
        box.AddChild(disabled);
        box.AddChild(new ListBoxItem { Content = "R2" });
        root.Content = box;
        manager.AddRoot(root);
        root.Measure(new WpfSize(800, 600));
        root.Arrange(new WpfRect(0, 0, 800, 600));

        var row = disabled.RenderRect;
        router.DispatchPointer(new WasmPointerArgs(Handle, row.X + 5, row.Y + row.Height / 2, WasmPointerButton.Left, true));
        router.DispatchPointer(new WasmPointerArgs(Handle, row.X + 5, row.Y + row.Height / 2, WasmPointerButton.Left, false));

        Assert.Equal(-1, box.SelectedIndex);
    }

    [Fact]
    public void Drop_press_on_disabled_row_keeps_selection_and_drop()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var combo = new ComboBox { Width = 400 };
        combo.AddItem(new ComboBoxItem { Content = "Blue" });
        var disabled = new ComboBoxItem { Content = "Green", IsEnabled = false };
        combo.AddItem(disabled);
        root.Content = combo;
        manager.AddRoot(root);
        root.Measure(new WpfSize(800, 600));
        root.Arrange(new WpfRect(0, 0, 800, 600));
        combo.IsDropDownOpen = true;

        var dropTop = combo.RenderRect.Y + 30;
        var x = combo.RenderRect.X + 20;
        var y = dropTop + 1 * 24 + 12;
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true));
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, false));

        Assert.Equal(-1, combo.SelectedIndex);
        Assert.True(combo.IsDropDownOpen);
    }
}
