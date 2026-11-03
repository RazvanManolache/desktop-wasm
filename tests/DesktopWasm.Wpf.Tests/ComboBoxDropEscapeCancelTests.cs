using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Native ComboBox parity: Escape cancels the open drop (the live
/// selection reverts to whatever was selected when the drop opened);
/// every other close (Enter, arrows then Enter) commits the live
/// selection.
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxDropEscapeCancelTests
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
    public void Escape_reverts_to_preopen_selection()
    {
        var (router, combo) = Harness();
        combo.SelectedIndex = 0;

        Key(router, "F4");
        Assert.True(combo.IsDropDownOpen);
        Key(router, "ArrowDown");
        Assert.Equal(1, combo.SelectedIndex);
        Key(router, "Escape");

        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(0, combo.SelectedIndex);
    }

    [Fact]
    public void Enter_still_commits_live_selection()
    {
        var (router, combo) = Harness();
        combo.SelectedIndex = 0;

        Key(router, "F4");
        Key(router, "ArrowDown");
        Key(router, "Enter");

        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(1, combo.SelectedIndex);
    }
}
