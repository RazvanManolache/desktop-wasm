using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Scrolled-drop click parity: after the drop scrolls (J25), pressing a
/// visible row selects its absolute item, not the visible slot.
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxDropScrollClickTests
{
    private static (WasmInputRouter Router, WpfInputManager Manager, ComboBox Box) Harness(int count = 20)
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var combo = new ComboBox { Width = 400 };
        for (var i = 0; i < count; i++) combo.AddItem(new ComboBoxItem { Content = $"C{i}" });
        root.Content = combo;
        manager.AddRoot(root);
        root.Measure(new WpfSize(800, 600));
        root.Arrange(new WpfRect(0, 0, 800, 600));
        return (router, manager, combo);
    }

    [Fact]
    public void Clicking_visible_row_of_scrolled_drop_selects_absolute_item()
    {
        var (router, _, combo) = Harness();
        combo.IsDropDownOpen = true;
        combo.SelectedIndex = 12; // scrolls first-visible to 5
        var handle = new WasmWindowHandle(1);

        // Visible slot 2 == absolute item 7.
        var dropTop = combo.RenderRect.Y + 30;
        var x = combo.RenderRect.X + 20;
        var y = dropTop + 2 * 24 + 12;
        router.DispatchPointer(new WasmPointerArgs(handle, x, y, WasmPointerButton.Left, true));
        router.DispatchPointer(new WasmPointerArgs(handle, x, y, WasmPointerButton.Left, false));

        Assert.Equal(7, combo.SelectedIndex);
        Assert.False(combo.IsDropDownOpen);
    }

    [Fact]
    public void Hovering_visible_row_of_scrolled_drop_highlights_absolute_item()
    {
        var (router, _, combo) = Harness();
        combo.IsDropDownOpen = true;
        combo.SelectedIndex = 12; // scrolls first-visible to 5
        var handle = new WasmWindowHandle(1);

        // Visible slot 1 == absolute item 6.
        var dropTop = combo.RenderRect.Y + 30;
        router.DispatchPointer(new WasmPointerArgs(handle, combo.RenderRect.X + 20, dropTop + 1 * 24 + 12, WasmPointerButton.Left, false)
            { Kind = WasmPointerKind.Move });

        var items = combo.Items.Cast<object>().ToList();
        Assert.True(((ComboBoxItem)items[6]).IsHighlighted);
        Assert.False(((ComboBoxItem)items[5]).IsHighlighted);
        Assert.False(((ComboBoxItem)items[1]).IsHighlighted);
    }

    [Fact]
    public void Typing_in_long_open_drop_scrolls_selection_into_view()
    {
        var (router, manager, combo) = Harness();
        Assert.True(manager.TryFocus(combo));
        combo.IsDropDownOpen = true;
        var handle = new WasmWindowHandle(1);

        foreach (var ch in "c12")
            router.DispatchKeyboard(new WasmKeyArgs(handle, ch.ToString(), true, false, false, false));
        Assert.Equal(12, combo.SelectedIndex);

        // Selection 12 scrolled first-visible to 5: hovering the top slot
        // must highlight C5, proving type-ahead feeds the drop scroll.
        var dropTop = combo.RenderRect.Y + 30;
        router.DispatchPointer(new WasmPointerArgs(handle, combo.RenderRect.X + 20, dropTop + 12, WasmPointerButton.Left, false)
            { Kind = WasmPointerKind.Move });

        var items = combo.Items.Cast<object>().ToList();
        Assert.True(((ComboBoxItem)items[5]).IsHighlighted);
        Assert.True(combo.IsDropDownOpen);
    }
}
