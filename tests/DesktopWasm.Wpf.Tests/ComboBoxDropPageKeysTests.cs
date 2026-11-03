using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Native ComboBox parity: PageUp/PageDown move selection by a page, in the
/// closed box and inside the open drop (which stays open).
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxDropPageKeysTests
{
    private static (WasmInputRouter Router, ComboBox Box) Harness(int count = 20)
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new StackPanel();
        var combo = new ComboBox();
        for (var i = 0; i < count; i++) combo.AddItem(new ComboBoxItem { Content = $"C{i}" });
        root.AddChild(combo);
        manager.AddRoot(root);
        Assert.True(manager.TryFocus(combo));
        return (router, combo);
    }

    private static void Key(WasmInputRouter router, string key) =>
        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), key, true, false, false, false));

    [Fact]
    public void Closed_box_pages_down_and_back_up()
    {
        var (router, combo) = Harness();
        combo.SelectedIndex = 0;

        Key(router, "PageDown");

        Assert.True(combo.SelectedIndex > 0,
            $"PageDown must page selection (index={combo.SelectedIndex})");

        Key(router, "PageUp");

        Assert.Equal(0, combo.SelectedIndex);
    }

    [Fact]
    public void Open_drop_pages_and_stays_open()
    {
        var (router, combo) = Harness();
        combo.SelectedIndex = 0;
        combo.IsDropDownOpen = true;

        Key(router, "PageDown");

        Assert.True(combo.SelectedIndex > 0,
            $"PageDown must page selection (index={combo.SelectedIndex})");
        Assert.True(combo.IsDropDownOpen);
    }
}
