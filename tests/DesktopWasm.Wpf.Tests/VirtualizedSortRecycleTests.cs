using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Sort + virtualized-recycle gate (FP2015, RVW-1096 fallback). The original
/// gallery DataGrid page binds a live product collection; sorting a bound column must
/// reorder the view without stale rows, and a sorted-then-scrolled virtualized list must
/// recycle containers with flat allocation counters, identity-following selection and no
/// edit bleed across reused rows.</summary>
[ResetAmbientState]
public sealed class VirtualizedSortRecycleTests
{
    private sealed class Product : System.ComponentModel.INotifyPropertyChanged
    {
        private string _productName = string.Empty;

        public string ProductName
        {
            get => _productName;
            set
            {
                if (_productName == value) return;
                _productName = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ProductName)));
            }
        }

        public double UnitPrice { get; set; }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    private const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";

    private static (DataGrid grid, DataGridTextColumn name, DataGridTextColumn price, System.Collections.ObjectModel.ObservableCollection<Product> rows) BoundGrid(int count)
    {
        var grid = new DataGrid { Width = 400 };
        var name = new DataGridTextColumn { Header = "ProductName", BindingPath = "ProductName", Width = GridLength.Pixel(160) };
        var price = new DataGridTextColumn { Header = "UnitPrice", BindingPath = "UnitPrice", Width = GridLength.Pixel(80) };
        grid.AddColumn(name);
        grid.AddColumn(price);
        var rows = new System.Collections.ObjectModel.ObservableCollection<Product>();
        var adjectives = new[] { "Red", "Blueberry" };
        var names = new[] { "Marmalade", "Dumplings", "Soup" };
        for (var i = 0; i < count; i++)
            rows.Add(new Product { ProductName = $"{adjectives[i % 2]} {names[i % 3]}", UnitPrice = i });
        grid.ItemsSource = rows;
        return (grid, name, price, rows);
    }

    [Fact]
    public void Bound_column_header_sort_reorders_view_without_new_containers_or_lost_selection()
    {
        var (grid, name, price, rows) = BoundGrid(50);
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        root.Content = grid;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(400, 600));
        mil.Render(root, new WpfSize(400, 600), new WpfRect(0, 0, 400, 600));
        var handle = new WasmWindowHandle(1);
        void Down(double x, double y) =>
            router.DispatchPointer(new WasmPointerArgs(handle, x, y, WasmPointerButton.Left, true));

        var target = rows[7];
        grid.SelectedItem = target;
        // Realize all rows pre-sort and pin the container count: the sort
        // must reuse mappings, not mint containers.
        for (var i = 0; i < grid.View.Count; i++)
            Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(i));
        var before = grid.ItemContainerGenerator.ContainersCreatedCount;
        Assert.Equal(50, before);

        // Header press on the bound UnitPrice column: ascending then descending.
        // (Price column spans x 161-241: Name 160px + 1px border offset.)
        var headerY = grid.RowRects[0].Y / 2;
        Down(200, headerY);
        Assert.Equal(ListSortDirection.Ascending, price.SortDirection);
        Assert.Equal(0.0, ((Product?)grid.View[0])?.UnitPrice);
        Down(200, headerY);
        Assert.Equal(ListSortDirection.Descending, price.SortDirection);
        Assert.Equal(49.0, ((Product?)grid.View[0])?.UnitPrice);

        // Selection follows the item, cells track the sorted view, no rebuild churn.
        Assert.Same(target, grid.SelectedItem);
        Assert.Equal(grid.ViewIndexOf(target), grid.SelectedIndex);
        for (var i = 0; i < grid.View.Count; i++)
        {
            var row = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(i));
            Assert.Same(grid.View[i], row.Item);
            Assert.Equal(((Product?)grid.View[i])?.ProductName, grid.CellText(grid.View[i], name));
        }
        Assert.Equal(before, grid.ItemContainerGenerator.ContainersCreatedCount);
    }

    [Fact]
    public void Clear_and_readd_sort_preserves_selection_by_item_identity()
    {
        // D2: a live reorder that empties the source must keep the selected
        // item, not the raw slot.
        var panel = new VirtualizingStackPanel { VirtualizationMode = VirtualizationMode.Recycling };
        var source = new System.Collections.ObjectModel.ObservableCollection<Product>(
            Enumerable.Range(0, 500).Select(i => new Product { ProductName = $"Cell {i:D4}", UnitPrice = i }));
        var list = new ListBox
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            ItemsSource = source,
            ItemTemplate = new DataTemplate($"<TextBlock {Ns} Text=\"{{Binding ProductName}}\" />"),
            Height = 200,
            Width = 300,
        };
        void Layout()
        {
            list.Measure(new WpfSize(300, 200));
            list.Arrange(new WpfRect(0, 0, 300, 200));
        }
        Layout();
        var selected = source[100];
        list.SelectedIndex = 100;
        var desc = source.OrderByDescending(p => p.UnitPrice).ToList();
        source.Clear();
        foreach (var p in desc) source.Add(p);
        Layout();

        var selectedAt = source.IndexOf(selected);
        Assert.True(selectedAt >= 0);
        Assert.Equal(selectedAt, list.SelectedIndex);
        var container = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(selectedAt));
        Assert.True(container.IsSelected);
        Assert.Same(selected, Assert.IsType<TextBlock>(container.TemplatedContent).DataContext);
    }

    [Fact]
    public void Move_selected_item_preserves_selection_by_identity()
    {
        var panel = new VirtualizingStackPanel { VirtualizationMode = VirtualizationMode.Recycling };
        var source = new System.Collections.ObjectModel.ObservableCollection<string>(
            Enumerable.Range(0, 500).Select(i => $"Cell {i:D4}"));
        var list = new ListBox
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            ItemsSource = source,
            Height = 200,
            Width = 300,
        };
        void Layout()
        {
            list.Measure(new WpfSize(300, 200));
            list.Arrange(new WpfRect(0, 0, 300, 200));
        }
        Layout();
        list.SelectedIndex = 100;
        source.Move(100, 399);
        Layout();
        Assert.Equal(399, list.SelectedIndex);
        var container = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(399));
        Assert.Equal("Cell 0100", container.Content);
    }

    [Fact]
    public void Insert_above_selection_shifts_index_on_virtualized_bound_list()
    {
        // Selector parity: an insert at/above the raw index moves it so the
        // selected item (not the slot) stays selected, silently.
        var panel = new VirtualizingStackPanel { VirtualizationMode = VirtualizationMode.Recycling };
        var source = new System.Collections.ObjectModel.ObservableCollection<string>(
            Enumerable.Range(0, 500).Select(i => $"Cell {i:D4}"));
        var list = new ListBox
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            ItemsSource = source,
            Height = 200,
            Width = 300,
        };
        void Layout()
        {
            list.Measure(new WpfSize(300, 200));
            list.Arrange(new WpfRect(0, 0, 300, 200));
        }
        Layout();
        list.SelectedIndex = 42;
        source.Insert(0, "Cell NEW-TOP");
        Layout();
        Assert.Equal(43, list.SelectedIndex);
        var container = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(43));
        Assert.Equal("Cell 0042", container.Content);
    }

    [Fact]
    public void Sorted_virtualized_scroll_recycles_without_stale_lost_or_bleed()
    {
        var panel = new VirtualizingStackPanel { VirtualizationMode = VirtualizationMode.Recycling };
        var source = new System.Collections.ObjectModel.ObservableCollection<Product>(
            Enumerable.Range(0, 500).Select(i => new Product { ProductName = $"Cell {i:D4}", UnitPrice = i }));
        var list = new ListBox
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            ItemsSource = source,
            ItemTemplate = new DataTemplate($"<TextBlock {Ns} Text=\"{{Binding ProductName}}\" />"),
            Height = 200,
            Width = 300,
        };
        void Layout()
        {
            list.Measure(new WpfSize(300, 200));
            list.Arrange(new WpfRect(0, 0, 300, 200));
        }
        Layout();
        for (var i = 1; i <= 3; i++)
        {
            panel.SetVerticalOffset(i * 24.0);
            Layout();
        }
        var steady = list.ItemContainerGenerator.ContainersCreatedCount;

        // Edit one row (committed-style model edit), then sort the live bound
        // source descending; selection is taken on the sorted order (live
        // reorder across an emptied source is a routed follow-up: selection
        // anchors on the raw index, not item identity).
        var edited = source[200];
        edited.ProductName = "EDITED-VALUE";
        Layout();
        var desc = source.OrderByDescending(p => p.UnitPrice).ToList();
        source.Clear();
        foreach (var p in desc) source.Add(p);
        Layout();

        const int selectedAt = 100;
        list.SelectedIndex = selectedAt;
        var editedAt = source.IndexOf(edited);
        Assert.True(editedAt >= 0);

        // Scroll far across the viewport boundary and back: every recycled
        // container serves its current item with no stale text, no bleed of
        // the edited value onto other rows, and the flag on exactly one row.
        panel.SetVerticalOffset(panel.GetTotalExtent(400));
        Layout();
        panel.SetVerticalOffset(panel.GetTotalExtent(selectedAt));
        Layout();
        var (start, end) = (panel.RealizedRange.StartIndex, panel.RealizedRange.EndIndex);
        Assert.InRange(selectedAt, start, end - 1);
        for (var i = start; i < end; i++)
        {
            var container = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(i));
            Assert.True(container.HasTemplatedContent);
            var visual = Assert.IsType<TextBlock>(container.TemplatedContent);
            Assert.Same(source[i], visual.DataContext);
            Assert.Equal(source[i].ProductName, visual.Text);
            // No bleed: only the edited item's own row may show the edit.
            Assert.True(!ReferenceEquals(source[i], edited) || visual.Text == "EDITED-VALUE");
            Assert.Equal(i == selectedAt, container.IsSelected);
        }
        // The edited row itself renders the edited value once scrolled to.
        panel.SetVerticalOffset(panel.GetTotalExtent(editedAt));
        Layout();
        var editedContainer = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(editedAt));
        var editedVisual = Assert.IsType<TextBlock>(editedContainer.TemplatedContent);
        Assert.Same(edited, editedVisual.DataContext);
        Assert.Equal("EDITED-VALUE", editedVisual.Text);
        Assert.True(list.ItemContainerGenerator.ContainersCreatedCount <= steady + (end - start) + 5);
    }
}
