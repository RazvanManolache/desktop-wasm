using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Native ComboBox parity: arrows clamp at the ends (Down on the last item
/// stays, Up on the first stays) instead of wrapping around.
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxArrowClampTests
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
    public void Closed_box_arrows_clamp_at_ends()
    {
        var (router, combo) = Harness();
        combo.SelectedIndex = 2;

        Key(router, "ArrowDown");

        Assert.Equal(2, combo.SelectedIndex);

        combo.SelectedIndex = 0;
        Key(router, "ArrowUp");

        Assert.Equal(0, combo.SelectedIndex);
    }

    [Fact]
    public void Open_drop_arrows_clamp_at_ends()
    {
        var (router, combo) = Harness();
        combo.IsDropDownOpen = true;
        combo.SelectedIndex = 2;

        Key(router, "ArrowDown");

        Assert.Equal(2, combo.SelectedIndex);
        Assert.True(combo.IsDropDownOpen);

        combo.SelectedIndex = 0;
        Key(router, "ArrowUp");

        Assert.Equal(0, combo.SelectedIndex);
        Assert.True(combo.IsDropDownOpen);
    }
}
