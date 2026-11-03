using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Native parity: hovering a disabled drop row raises no hover highlight
/// (disabled rows give no pointer feedback, like they take no presses).
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxDropDisabledHoverTests
{
    [Fact]
    public void Hovering_disabled_row_flags_nothing()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var combo = new ComboBox { Width = 400 };
        combo.AddItem(new ComboBoxItem { Content = "Blue" });
        var disabled = new ComboBoxItem { Content = "Green", IsEnabled = false };
        combo.AddItem(disabled);
        combo.AddItem(new ComboBoxItem { Content = "Red" });
        root.Content = combo;
        manager.AddRoot(root);
        root.Measure(new WpfSize(800, 600));
        root.Arrange(new WpfRect(0, 0, 800, 600));
        combo.IsDropDownOpen = true;

        var handle = new WasmWindowHandle(1);
        var dropTop = combo.RenderRect.Y + 30;
        router.DispatchPointer(new WasmPointerArgs(handle, combo.RenderRect.X + 20, dropTop + 1 * 24 + 12, WasmPointerButton.Left, false)
            { Kind = WasmPointerKind.Move });

        var items = combo.Items.Cast<object>().Cast<ComboBoxItem>().ToList();
        Assert.All(items, row => Assert.False(row.IsHighlighted));
    }
}
