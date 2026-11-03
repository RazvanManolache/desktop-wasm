using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Commit-path matrix completion (jobs 49/53): closing the drop with a
/// header toggle-click commits the live selection like every other
/// non-Escape close.
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxDropHeaderCommitTests
{
    [Fact]
    public void Header_toggle_close_commits_live_selection()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var combo = new ComboBox { Width = 400 };
        combo.AddItem(new ComboBoxItem { Content = "Blue" });
        combo.AddItem(new ComboBoxItem { Content = "Green" });
        combo.AddItem(new ComboBoxItem { Content = "Red" });
        root.Content = combo;
        manager.AddRoot(root);
        root.Measure(new WpfSize(800, 600));
        root.Arrange(new WpfRect(0, 0, 800, 600));
        Assert.True(manager.TryFocus(combo));
        combo.SelectedIndex = 0;
        combo.IsDropDownOpen = true;

        var handle = new WasmWindowHandle(1);
        router.DispatchKeyboard(new WasmKeyArgs(handle, "ArrowDown", true, false, false, false));
        Assert.Equal(1, combo.SelectedIndex);

        var x = combo.RenderRect.X + 20;
        var y = combo.RenderRect.Y + 15;
        router.DispatchPointer(new WasmPointerArgs(handle, x, y, WasmPointerButton.Left, true));
        router.DispatchPointer(new WasmPointerArgs(handle, x, y, WasmPointerButton.Left, false));

        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(1, combo.SelectedIndex);
    }
}
