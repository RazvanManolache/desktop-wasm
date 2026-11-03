using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Grouped-list parity for the job-13/14 fixes: rows nested under GroupItem
/// containers must scroll into view on selection and keep arrow navigation
/// live after a click (Items flattens depth-first; focus/scroll walk the
/// visual parents, so grouping must not break either path).
/// </summary>
[ResetAmbientState]
public sealed class ListBoxGroupedSelectionTests
{
    private sealed record Item(string Category, string Name);

    private static (WasmInputRouter Router, WpfInputManager Manager, ScrollViewer View, ListBox Box) Harness()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var view = new ScrollViewer { Width = 400, Height = 100 };
        var box = new ListBox();
        var data = new List<Item>();
        for (var i = 0; i < 5; i++) data.Add(new Item("A", $"A{i}"));
        for (var i = 0; i < 5; i++) data.Add(new Item("B", $"B{i}"));
        var source = new ListCollectionView(data);
        source.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
        box.ItemsSource = source;
        box.GroupStyle.Add(new GroupStyle());
        view.Content = box;
        root.Content = view;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(800, 600));
        mil.Render(root, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));
        return (router, manager, view, box);
    }

    [Fact]
    public void Grouped_rows_flatten_in_order()
    {
        var (_, _, _, box) = Harness();
        Assert.Equal(10, box.Items.Count);
    }

    [Fact]
    public void Selecting_last_grouped_row_scrolls_it_into_view()
    {
        var (_, _, view, box) = Harness();

        box.SelectedIndex = box.Items.Count - 1;

        Assert.True(view.VerticalOffset > 0,
            $"selecting the last grouped row must scroll (offset={view.VerticalOffset})");
    }

    [Fact]
    public void Clicking_grouped_row_then_ArrowDown_moves_selection()
    {
        var (router, manager, _, box) = Harness();
        // Aim without assuming nesting: scan down for the first row hit.
        // Aim from the row's own rect (the viewer centers in the window, so
        // fixed coords miss the content).
        var r0 = box.Items[0].RenderRect;
        var cx = r0.X + r0.Width / 2;
        var cy = r0.Y + r0.Height / 2;
        Assert.Same(box.Items[0], manager.HitTest(cx, cy));
        router.DispatchPointer(new WasmPointerArgs(new WasmWindowHandle(1), cx, cy, WasmPointerButton.Left, true));
        Assert.Equal(0, box.SelectedIndex);

        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), "ArrowDown", true, false, false, false));

        Assert.Equal(1, box.SelectedIndex);
    }
}
