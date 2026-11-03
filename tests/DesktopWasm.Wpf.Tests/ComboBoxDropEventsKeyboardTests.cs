using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Drop-event centrality (job-64 follow-up): keyboard-driven transitions
/// (F4 open, Escape close) fire the same DropDownOpened/Closed events as
/// property sets — every path funnels through the central setter.
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxDropEventsKeyboardTests
{
    [Fact]
    public void Keyboard_transitions_fire_opened_and_closed()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new StackPanel();
        var combo = new ComboBox();
        combo.AddItem(new ComboBoxItem { Content = "Blue" });
        combo.AddItem(new ComboBoxItem { Content = "Green" });
        root.AddChild(combo);
        manager.AddRoot(root);
        Assert.True(manager.TryFocus(combo));
        var opened = 0;
        var closed = 0;
        combo.DropDownOpened += (_, _) => opened++;
        combo.DropDownClosed += (_, _) => closed++;

        var handle = new WasmWindowHandle(1);
        router.DispatchKeyboard(new WasmKeyArgs(handle, "F4", IsDown: true, Ctrl: false, Shift: false, Alt: false));
        Assert.Equal(1, opened);

        router.DispatchKeyboard(new WasmKeyArgs(handle, "Escape", IsDown: true, Ctrl: false, Shift: false, Alt: false));
        Assert.Equal(1, closed);
    }
}
