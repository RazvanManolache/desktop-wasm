using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Native ComboBox parity: F4 toggles the drop-down (closed opens it,
/// open closes it) without touching the selection.
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxDropF4ToggleTests
{
    private static (WasmInputRouter Router, ComboBox Box) Harness()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new StackPanel();
        var combo = new ComboBox();
        combo.AddItem(new ComboBoxItem { Content = "Blue" });
        combo.AddItem(new ComboBoxItem { Content = "Green" });
        combo.AddItem(new ComboBoxItem { Content = "Red" });
        root.AddChild(combo);
        manager.AddRoot(root);
        Assert.True(manager.TryFocus(combo));
        return (router, combo);
    }

    private static void Key(WasmInputRouter router, string key) =>
        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), key, true, false, false, false));

    [Fact]
    public void F4_toggles_drop_without_moving_selection()
    {
        var (router, combo) = Harness();
        combo.SelectedIndex = 1;

        Key(router, "F4");
        Assert.True(combo.IsDropDownOpen);
        Assert.Equal(1, combo.SelectedIndex);

        Key(router, "F4");
        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(1, combo.SelectedIndex);
    }
}
