using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Native ComboBox parity: typing jumps selection to the next item with a
/// matching prefix, in the closed box and inside the open drop (which stays
/// open while typing).
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxDropTypeAheadTests
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

    private static void Type(WasmInputRouter router, string text)
    {
        foreach (var ch in text)
            router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), ch.ToString(), true, false, false, false));
    }

    [Fact]
    public void Typing_jumps_closed_box_selection()
    {
        var (router, combo) = Harness();

        Type(router, "g");

        Assert.Equal(1, combo.SelectedIndex);
        Assert.False(combo.IsDropDownOpen);
    }

    [Fact]
    public void Typing_in_open_drop_moves_and_stays_open()
    {
        var (router, combo) = Harness();
        combo.IsDropDownOpen = true;

        Type(router, "r");

        Assert.Equal(2, combo.SelectedIndex);
        Assert.True(combo.IsDropDownOpen);
    }
}
