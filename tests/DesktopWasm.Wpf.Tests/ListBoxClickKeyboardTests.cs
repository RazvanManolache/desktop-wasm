using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Native ListBox parity: pressing a row focuses into the list, so arrow
/// keys move selection right after a pointer click (WPF keeps keyboard
/// navigation live once the press lands focus inside the list).
/// </summary>
[ResetAmbientState]
public sealed class ListBoxClickKeyboardTests
{
    private static void LeftDown(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(new WasmWindowHandle(1), x, y, WasmPointerButton.Left, true));

    private static void Key(WasmInputRouter router, string key) =>
        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), key, true, false, false, false));

    [Fact]
    public void Clicking_a_row_then_ArrowDown_moves_selection()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var box = new ListBox();
        for (var i = 0; i < 5; i++) box.AddChild(new ListBoxItem { Content = $"R{i}" });
        root.Content = box;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(800, 600));
        mil.Render(root, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));

        var row = box.Items[0].ArrangedRect;
        LeftDown(router, row.X + row.Width / 2, row.Y + row.Height / 2);
        Assert.Equal(0, box.SelectedIndex);

        Key(router, "ArrowDown");

        Assert.Equal(1, box.SelectedIndex);
    }
}
