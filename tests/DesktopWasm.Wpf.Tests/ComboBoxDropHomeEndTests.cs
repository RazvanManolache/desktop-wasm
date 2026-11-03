using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Native ComboBox parity: Home/End jump to the first/last item, in the
/// closed box and inside the open drop (which stays open).
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxDropHomeEndTests
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
    public void Closed_box_Home_End_jump()
    {
        var (router, combo) = Harness();

        Key(router, "End");
        Assert.Equal(2, combo.SelectedIndex);

        Key(router, "Home");
        Assert.Equal(0, combo.SelectedIndex);
    }

    [Fact]
    public void Open_drop_Home_End_jump_and_stay_open()
    {
        var (router, combo) = Harness();
        combo.IsDropDownOpen = true;

        Key(router, "End");
        Assert.Equal(2, combo.SelectedIndex);
        Assert.True(combo.IsDropDownOpen);

        Key(router, "Home");
        Assert.Equal(0, combo.SelectedIndex);
        Assert.True(combo.IsDropDownOpen);
    }
}
