using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Grouped-list parity for the job-44 wheel fix: with GroupItem
/// containers in the visual parents, the wheel over grouped rows still
/// scrolls one full row per small tick (Items flattens depth-first, so
/// the row stride must survive grouping).
/// </summary>
[ResetAmbientState]
public sealed class ListBoxGroupedWheelTests
{
    private sealed record Item(string Category, string Name);

    private static (WasmInputRouter Router, ScrollViewer View, ListBox Box) Harness()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var view = new ScrollViewer { Width = 400, Height = 100 };
        var box = new ListBox();
        var data = new List<Item>();
        for (var i = 0; i < 10; i++) data.Add(new Item("A", $"A{i}"));
        for (var i = 0; i < 10; i++) data.Add(new Item("B", $"B{i}"));
        var source = new ListCollectionView(data);
        source.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
        box.ItemsSource = source;
        box.GroupStyle.Add(new GroupStyle());
        view.Content = box;
        root.Content = view;
        manager.AddRoot(root);
        root.Measure(new WpfSize(800, 600));
        root.Arrange(new WpfRect(0, 0, 800, 600));
        return (router, view, box);
    }

    [Fact]
    public void Small_wheel_tick_over_grouped_rows_scrolls_one_row()
    {
        var (router, view, box) = Harness();
        Assert.Equal(0, view.VerticalOffset);
        var rowH = box.Items[0].RenderRect.Height;
        Assert.True(rowH > 5, $"rows must dwarf the probe tick (rowH={rowH})");

        var r = box.Items[2].RenderRect;
        router.DispatchWheel(new WasmWheelArgs(new WasmWindowHandle(1), r.X + 10, r.Y + r.Height / 2, 5));

        Assert.Equal(rowH, view.VerticalOffset);
    }
}
