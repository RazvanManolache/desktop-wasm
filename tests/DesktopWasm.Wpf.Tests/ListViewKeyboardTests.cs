using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Native ListView parity: arrow/Home/End keys move selection when keyboard
/// focus sits on the list or a realized row (press focuses the row itself,
/// so arrows must navigate right after a pointer click, like ListBox).
/// Pre-fix: ListView.OnRoutedEvent handles only pointer presses, so all
/// keys fall through and selection never moves by keyboard.
/// </summary>
[ResetAmbientState]
public sealed class ListViewKeyboardTests
{
    private static (WasmInputRouter Router, ListView View) Harness()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var list = new ListView { Height = 200, SelectionMode = SelectionMode.Single };
        list.ItemsSource = new List<string> { "R0", "R1", "R2", "R3", "R4" };
        root.Content = list;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(800, 600));
        mil.Render(root, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));
        return (router, list);
    }

    private static readonly WasmWindowHandle Handle = new(1);

    private static void Down(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true));

    private static void Key(WasmInputRouter router, string key) =>
        router.DispatchKeyboard(new WasmKeyArgs(Handle, key, true, false, false, false));

    private static void PressRow(ListView list, WasmInputRouter router, int index)
    {
        var row = Assert.IsAssignableFrom<UIElement>(list.ItemContainerGenerator.ContainerFromIndex(index));
        var r = row.RenderRect;
        Down(router, r.X + r.Width / 2, r.Y + r.Height / 2);
    }

    [Fact]
    public void Clicking_a_row_then_ArrowDown_moves_selection()
    {
        var (router, list) = Harness();
        PressRow(list, router, 0);
        Assert.Equal(0, list.SelectedIndex);
        Key(router, "ArrowDown");
        Assert.Equal(1, list.SelectedIndex);
    }

    [Fact]
    public void ArrowDown_from_empty_starts_at_first_row()
    {
        var (router, list) = Harness();
        Assert.True(list.Focus());
        Key(router, "ArrowDown");
        Assert.Equal(0, list.SelectedIndex);
    }

    [Fact]
    public void ArrowUp_from_empty_selects_last_row()
    {
        var (router, list) = Harness();
        Assert.True(list.Focus());
        Key(router, "ArrowUp");
        Assert.Equal(4, list.SelectedIndex);
    }

    [Fact]
    public void Home_and_End_jump_to_first_and_last_rows()
    {
        var (router, list) = Harness();
        Assert.True(list.Focus());
        Key(router, "End");
        Assert.Equal(4, list.SelectedIndex);
        Key(router, "Home");
        Assert.Equal(0, list.SelectedIndex);
    }

    [Fact]
    public void Arrows_clamp_at_the_ends()
    {
        var (router, list) = Harness();
        Assert.True(list.Focus());
        Key(router, "Home");
        Key(router, "ArrowUp");
        Assert.Equal(0, list.SelectedIndex);
        Key(router, "End");
        Key(router, "ArrowDown");
        Assert.Equal(4, list.SelectedIndex);
    }
}
