using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Shortcut reservation: Ctrl/Alt+letter chords stay for shortcuts and
/// never feed type-ahead — the selection must not move under either
/// control (the J17/J19 guards promise this; nothing pinned it).
/// </summary>
[ResetAmbientState]
public sealed class SelectorModifierChordTests
{
    private static void Chord(WasmInputRouter router, string key, bool ctrl, bool alt) =>
        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), key, IsDown: true, Ctrl: ctrl, Shift: false, Alt: alt));

    [Fact]
    public void ListBox_ctrl_and_alt_chords_leave_selection()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new StackPanel();
        var box = new ListBox();
        box.AddChild(new ListBoxItem { Content = "Green" });
        box.AddChild(new ListBoxItem { Content = "Blue" });
        root.AddChild(box);
        manager.AddRoot(root);
        Assert.True(manager.TryFocus(box));

        Chord(router, "g", ctrl: true, alt: false);
        Chord(router, "g", ctrl: false, alt: true);

        Assert.Equal(-1, box.SelectedIndex);
    }

    [Fact]
    public void ComboBox_ctrl_and_alt_chords_leave_selection()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new StackPanel();
        var combo = new ComboBox();
        combo.AddItem(new ComboBoxItem { Content = "Green" });
        combo.AddItem(new ComboBoxItem { Content = "Blue" });
        root.AddChild(combo);
        manager.AddRoot(root);
        Assert.True(manager.TryFocus(combo));

        Chord(router, "g", ctrl: true, alt: false);
        Chord(router, "g", ctrl: false, alt: true);

        Assert.Equal(-1, combo.SelectedIndex);
    }
}
