using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-79 DataGrid x7 tests: VirtualizingPanel/ScrollUnit attached
/// knobs, MultiSelector select-all semantics, DataGrid Sorting/LoadingRow
/// events with the engine default sort, header-press routing, and the
/// DataGrid SelectedItems/SelectAll/UnselectAll/CommitEdit surface that
/// converted HandyControl code binds (member-evidenced via Mono.Cecil on the
/// upstream net10.0-windows HandyControl.dll).</summary>
[ResetAmbientState]
public sealed class DataGridSortingTests
{
    private sealed class Person
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    private sealed class TestMultiSelector : MultiSelector
    {
    }

    private static DataGrid SortingHarness()
    {
        var grid = new DataGrid { Width = 400 };
        grid.AddColumn(new DataGridTextColumn { Header = "Name", BindingPath = "Name", Width = GridLength.Pixel(120) });
        grid.AddColumn(new DataGridTextColumn { Header = "Age", BindingPath = "Age", Width = GridLength.Star() });
        grid.Items.Add(new Person { Name = "Ada", Age = 36 });
        grid.Items.Add(new Person { Name = "Grace", Age = 85 });
        grid.Items.Add(new Person { Name = "Katherine", Age = 101 });
        return grid;
    }

    private static (WasmInputRouter Router, WpfInputManager Manager, DataGrid Grid) PressHarness()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var grid = SortingHarness();
        root.Content = grid;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(400, 600));
        mil.Render(root, new WpfSize(400, 600), new WpfRect(0, 0, 400, 600));
        return (router, manager, grid);
    }

    private static readonly WasmWindowHandle Handle = new(1);

    private static void Down(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true));

    [Fact]
    public void ScrollUnit_values_match_live_wpf()
    {
        // Live-verified against the .NET 9 WindowsDesktop runtime
        // (PresentationFramework.dll via Mono.Cecil): Pixel = 0, Item = 1.
        Assert.Equal(0, (int)ScrollUnit.Pixel);
        Assert.Equal(1, (int)ScrollUnit.Item);
    }

    [Fact]
    public void VirtualizingPanel_knobs_default_and_round_trip()
    {
        var grid = SortingHarness();
        Assert.Equal(ScrollUnit.Item, VirtualizingPanel.GetScrollUnit(grid));
        Assert.False(VirtualizingPanel.GetIsVirtualizingWhenGrouping(grid));

        VirtualizingPanel.SetScrollUnit(grid, ScrollUnit.Pixel);
        Assert.Equal(ScrollUnit.Pixel, VirtualizingPanel.GetScrollUnit(grid));

        VirtualizingPanel.SetIsVirtualizingWhenGrouping(grid, true);
        Assert.True(VirtualizingPanel.GetIsVirtualizingWhenGrouping(grid));

        Assert.NotNull(VirtualizingPanel.ScrollUnitProperty);
        Assert.NotNull(VirtualizingPanel.IsVirtualizingWhenGroupingProperty);
        Assert.Throws<ArgumentNullException>(() => VirtualizingPanel.GetScrollUnit(null!));
        Assert.Throws<ArgumentNullException>(() => VirtualizingPanel.SetScrollUnit(null!, ScrollUnit.Item));
    }

    [Fact]
    public void MultiSelector_select_all_and_clear()
    {
        var selector = new TestMultiSelector();
        selector.AddItem("a");
        selector.AddItem("b");
        selector.AddItem("c");

        selector.SelectAll();
        Assert.Equal(0, selector.SelectedIndex);
        Assert.Equal(3, selector.SelectedItems.Count);

        selector.UnselectAll();
        Assert.Equal(-1, selector.SelectedIndex);
        Assert.Empty(selector.SelectedItems);

        selector.SelectedIndex = 1;
        Assert.Single(selector.SelectedItems);
        Assert.Equal("b", selector.SelectedItems[0]);

        selector.SelectAll();
        selector.UnselectAll();
        Assert.Equal(-1, selector.SelectedIndex);
    }

    [Fact]
    public void MultiSelector_external_set_edit_reseats_index()
    {
        var selector = new TestMultiSelector();
        selector.AddItem("a");
        selector.AddItem("b");
        var set = (System.Collections.ObjectModel.ObservableCollection<object?>)selector.SelectedItems;
        selector.SelectedIndex = 0;
        set.Add("b");
        Assert.Equal(0, selector.SelectedIndex); // first listed item still wins
        set.RemoveAt(0);
        Assert.Equal(1, selector.SelectedIndex); // re-seats on the new first item
        set.Clear();
        Assert.Equal(-1, selector.SelectedIndex);
    }

    [Fact]
    public void Row_and_column_event_args_carry_payloads()
    {
        var grid = SortingHarness();
        var row = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(0));
        var rowArgs = new DataGridRowEventArgs(row);
        Assert.Same(row, rowArgs.Row);
        Assert.Throws<ArgumentNullException>(() => new DataGridRowEventArgs(null!));

        var column = grid.Columns[0];
        var columnArgs = new DataGridColumnEventArgs(column);
        Assert.Same(column, columnArgs.Column);
        Assert.Throws<ArgumentNullException>(() => new DataGridColumnEventArgs(null!));

        var sortArgs = new DataGridSortingEventArgs(column);
        Assert.Same(column, sortArgs.Column);
        Assert.False(sortArgs.Handled);
        sortArgs.Handled = true;
        Assert.True(sortArgs.Handled);
    }

    [Fact]
    public void Sorting_handler_shape_binds_like_converted_code()
    {
        DataGridSortingEventArgs? seen = null;
        object? seenSender = null;
        DataGridSortingEventHandler handler = (sender, e) => { seenSender = sender; seen = e; };
        var grid = SortingHarness();
        var column = grid.Columns[1];
        grid.Sorting += handler;
        grid.TrySortAt(0, -100); // outside the header band: no raise
        Assert.Null(seen);
        grid.Sorting -= handler;
    }

    [Fact]
    public void Sorting_event_carries_column_and_handled_skips_default_sort()
    {
        var grid = SortingHarness();
        DataGridSortingEventArgs? seen = null;
        object? seenSender = null;
        grid.Sorting += (sender, e) => { seenSender = sender; seen = e; e.Handled = true; };

        grid.TrySortAt(-1000, -1000); // nowhere near the header: silent
        Assert.Null(seen);

        // Drive the raise directly: the handler marks it handled, so the
        // view order and the sort descriptions stay untouched.
        var raise = typeof(DataGrid).GetMethod("OnSorting",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(raise);
        raise.Invoke(grid, [new DataGridSortingEventArgs(grid.Columns[1])]);
        Assert.Same(grid, seenSender);
        Assert.Same(grid.Columns[1], seen!.Column);
        Assert.True(seen.Handled);
        Assert.Empty(grid.Items.SortDescriptions);
        Assert.Equal("Ada", ((Person?)grid.Items[0])?.Name);
    }

    [Fact]
    public void Unhandled_sort_runs_the_engine_default_sort()
    {
        var grid = SortingHarness();
        var raise = typeof(DataGrid).GetMethod("OnSorting",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(raise);

        raise.Invoke(grid, [new DataGridSortingEventArgs(grid.Columns[1])]);
        Assert.Equal(ListSortDirection.Ascending, grid.Columns[1].SortDirection);
        Assert.Single(grid.Items.SortDescriptions);
        Assert.Equal(("Age", ListSortDirection.Ascending),
            (grid.Items.SortDescriptions[0].PropertyName, grid.Items.SortDescriptions[0].Direction));

        raise.Invoke(grid, [new DataGridSortingEventArgs(grid.Columns[1])]);
        Assert.Equal(ListSortDirection.Descending, grid.Columns[1].SortDirection);
        Assert.Equal("Katherine", ((Person?)grid.View[0])?.Name);

        // Sorting another column clears the previous glyph.
        raise.Invoke(grid, [new DataGridSortingEventArgs(grid.Columns[0])]);
        Assert.Null(grid.Columns[1].SortDirection);
        Assert.Equal(ListSortDirection.Ascending, grid.Columns[0].SortDirection);
        Assert.Equal("Ada", ((Person?)grid.View[0])?.Name);
    }

    [Fact]
    public void Header_press_sorts_and_row_press_still_selects()
    {
        var (router, _, grid) = PressHarness();
        var headerY = grid.RowRects[0].Y / 2;

        Down(router, 200, headerY); // Age column header
        Assert.Equal(ListSortDirection.Ascending, grid.Columns[1].SortDirection);
        Assert.Single(grid.Items.SortDescriptions);

        Down(router, 200, headerY); // toggle to descending
        Assert.Equal(ListSortDirection.Descending, grid.Columns[1].SortDirection);
        Assert.Equal("Katherine", ((Person?)grid.View[0])?.Name);

        var r0 = grid.RowRects[0];
        Down(router, 200, r0.Y + r0.Height / 2); // row press still selects
        Assert.Equal(0, grid.SelectedIndex);
    }

    [Fact]
    public void Header_press_gated_off_when_sorting_disabled()
    {
        var (router, _, grid) = PressHarness();
        grid.CanUserSortColumns = false;
        var fires = 0;
        grid.Sorting += (_, _) => fires++;
        Down(router, 200, grid.RowRects[0].Y / 2);
        Assert.Equal(0, fires);
        Assert.Empty(grid.Items.SortDescriptions);
    }

    [Fact]
    public void LoadingRow_fires_when_a_container_is_prepared()
    {
        var grid = SortingHarness();
        var seen = new List<DataGridRow>();
        void Handler(object? sender, DataGridRowEventArgs e) => seen.Add(e.Row);
        grid.LoadingRow += Handler;

        var row = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Single(seen);
        Assert.Same(row, seen[0]);
        Assert.Same(row.Item, grid.Items[0]);

        grid.LoadingRow -= Handler;
        Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(1));
        Assert.Single(seen);
    }

    [Fact]
    public void SelectAll_UnselectAll_and_CommitEdit_behave()
    {
        var grid = SortingHarness();
        Assert.True(grid.CommitEdit());

        grid.SelectAll();
        Assert.Equal(0, grid.SelectedIndex);
        Assert.Equal(3, grid.SelectedItems.Count);
        Assert.Same(grid.Items[0], grid.SelectedItems[0]);

        grid.UnselectAll();
        Assert.Equal(-1, grid.SelectedIndex);
        Assert.Empty(grid.SelectedItems);

        grid.SelectedIndex = 2;
        Assert.Single(grid.SelectedItems);
        Assert.Same(grid.Items[2], grid.SelectedItems[0]);

        grid.SelectAll();
        Assert.Equal(3, grid.SelectedItems.Count);
        grid.Items.RemoveAt(0); // structural change re-mirrors the single selection
        Assert.Single(grid.SelectedItems);
    }

    [Fact]
    public void SelectedItems_external_edit_reseats_selection()
    {
        var grid = SortingHarness();
        grid.SelectedIndex = 0;
        var set = (System.Collections.ObjectModel.ObservableCollection<object?>)grid.SelectedItems;
        set.Add(grid.Items[2]);
        Assert.Equal(0, grid.SelectedIndex); // first listed item still wins
        set.RemoveAt(0);
        Assert.Equal(2, grid.SelectedIndex); // re-seats on the new first item
        set.Clear();
        Assert.Equal(-1, grid.SelectedIndex);
    }
}
