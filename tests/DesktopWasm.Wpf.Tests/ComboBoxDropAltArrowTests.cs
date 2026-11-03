using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Native ComboBox parity: Alt+ArrowDown opens the drop and Alt+ArrowUp
/// closes it; the Alt chord never moves the selection the way bare
/// arrows do.
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxDropAltArrowTests
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

    private static void AltKey(WasmInputRouter router, string key) =>
        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), key, true, false, false, true));

    [Fact]
    public void Alt_arrows_toggle_drop_without_moving_selection()
    {
        var (router, combo) = Harness();
        combo.SelectedIndex = 1;

        AltKey(router, "ArrowDown");
        Assert.True(combo.IsDropDownOpen);
        Assert.Equal(1, combo.SelectedIndex);

        AltKey(router, "ArrowUp");
        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(1, combo.SelectedIndex);
    }
}
