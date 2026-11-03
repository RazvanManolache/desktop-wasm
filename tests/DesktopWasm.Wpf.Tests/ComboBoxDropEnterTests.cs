using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Native ComboBox parity: with the drop open, arrows move through the rows
/// and Enter commits the selection and closes (Escape already closes; rows
/// already commit on click).
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxDropEnterTests
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
    public void Enter_commits_open_drop_and_closes()
    {
        var (router, combo) = Harness();

        Key(router, " ");
        Assert.True(combo.IsDropDownOpen);
        Key(router, "ArrowDown");
        Key(router, "ArrowDown");
        Assert.Equal(1, combo.SelectedIndex);

        Key(router, "Enter");

        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(1, combo.SelectedIndex);
    }

    [Fact]
    public void Enter_on_closed_box_changes_nothing()
    {
        var (router, combo) = Harness();

        Key(router, "Enter");

        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(-1, combo.SelectedIndex);
    }
}
