using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// ComboBox ItemsSource parity: generated string rows carry selection text
/// and answer the full keyboard flow (arrows, Enter-commit, type-ahead)
/// like direct rows.
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxItemsSourceTests
{
    private static (WasmInputRouter Router, ComboBox Box) Harness()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new StackPanel();
        var combo = new ComboBox();
        combo.ItemsSource = new List<string> { "Alpha", "Beta", "Gamma" };
        root.AddChild(combo);
        manager.AddRoot(root);
        root.Measure(new WpfSize(400, 400));
        root.Arrange(new WpfRect(0, 0, 400, 400));
        Assert.True(manager.TryFocus(combo));
        return (router, combo);
    }

    private static void Key(WasmInputRouter router, string key) =>
        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), key, true, false, false, false));

    [Fact]
    public void Generated_rows_select_type_and_commit()
    {
        var (router, combo) = Harness();
        Assert.Equal(3, combo.Items.Count);

        combo.SelectedIndex = 1;
        Assert.Equal("Beta", combo.Text);

        combo.IsDropDownOpen = true;
        Key(router, "ArrowDown");
        Assert.Equal(2, combo.SelectedIndex);
        Key(router, "Enter");
        Assert.False(combo.IsDropDownOpen);
        Assert.Equal("Gamma", combo.Text);

        combo.SelectedIndex = -1;
        Key(router, "a");
        Assert.Equal(0, combo.SelectedIndex);
    }
}
