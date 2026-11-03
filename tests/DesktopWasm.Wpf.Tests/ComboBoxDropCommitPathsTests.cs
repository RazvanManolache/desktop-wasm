using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Escape-cancel commit-path pins (job-49 follow-up): closing the drop
/// via F4 or Alt+Up commits the live selection — only Escape reverts to
/// the pre-open stash.
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxDropCommitPathsTests
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

    private static void Key(WasmInputRouter router, string key, bool alt = false) =>
        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), key, true, false, false, alt));

    [Fact]
    public void F4_close_commits_live_selection()
    {
        var (router, combo) = Harness();
        combo.SelectedIndex = 0;

        Key(router, "F4");
        Key(router, "ArrowDown");
        Key(router, "F4");

        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(1, combo.SelectedIndex);
    }

    [Fact]
    public void Alt_up_close_commits_live_selection()
    {
        var (router, combo) = Harness();
        combo.SelectedIndex = 0;

        Key(router, "F4");
        Key(router, "ArrowDown");
        Key(router, "ArrowUp", alt: true);

        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(1, combo.SelectedIndex);
    }
}
