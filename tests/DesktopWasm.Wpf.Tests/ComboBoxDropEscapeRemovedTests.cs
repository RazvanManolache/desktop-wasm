using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Escape-cancel removal edge (job-49 follow-up): when the pre-open
/// selection is removed while the drop is open, Escape still closes and
/// lands at nothing (-1) instead of resurrecting a dangling index.
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxDropEscapeRemovedTests
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
    public void Escape_after_preopen_item_removed_lands_at_nothing()
    {
        var (router, combo) = Harness();
        combo.SelectedIndex = 1;
        combo.IsDropDownOpen = true;

        combo.Items.RemoveAt(1);
        Assert.Equal(2, combo.Items.Count);

        Key(router, "Escape");

        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(-1, combo.SelectedIndex);
    }
}
