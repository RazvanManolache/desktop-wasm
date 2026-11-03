using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Native ListBox parity: PageUp/PageDown move selection by a page (WPF
/// pages the selection, not just the viewport, when the list has focus).
/// </summary>
[ResetAmbientState]
public sealed class ListBoxPageKeysTests
{
    private static (WasmInputRouter Router, ScrollViewer View, ListBox Box) Harness(int count = 20)
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var view = new ScrollViewer { Width = 400, Height = 100 };
        var box = new ListBox();
        for (var i = 0; i < count; i++) box.AddChild(new ListBoxItem { Content = $"R{i}" });
        view.Content = box;
        root.Content = view;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(800, 600));
        mil.Render(root, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));
        return (router, view, box);
    }

    [Fact]
    public void PageDown_moves_selection_down_a_page()
    {
        var (router, view, box) = Harness();
        var r0 = box.Items[0].RenderRect;
        router.DispatchPointer(new WasmPointerArgs(new WasmWindowHandle(1),
            r0.X + r0.Width / 2, r0.Y + r0.Height / 2, WasmPointerButton.Left, true));
        Assert.Equal(0, box.SelectedIndex);

        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), "PageDown", true, false, false, false));

        Assert.True(box.SelectedIndex > 0,
            $"PageDown must page selection (index={box.SelectedIndex})");
        Assert.True(view.VerticalOffset > 0,
            $"paged selection must scroll (offset={view.VerticalOffset})");

        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), "PageUp", true, false, false, false));

        Assert.Equal(0, box.SelectedIndex);
        Assert.Equal(0, view.VerticalOffset);
    }
}
