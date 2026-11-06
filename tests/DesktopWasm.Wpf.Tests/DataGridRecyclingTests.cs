namespace DesktopWasm.Wpf.Tests;

/// <summary>Recycled-container correctness after edit/selection (RVW-1096 Job2).
/// Row recycling itself lives in <c>Items.cs</c> (pool + <c>ParkRealizedContainers</c>,
/// covered by <c>ContainerRecyclingTests</c>); the grid renders all rows and rebuilds
/// realized rows on every <c>Items</c> mutation. These tests prove the rebuild never
/// leaves a stale <c>Item</c>/selection/committed edit behind, and that the virtualized
/// path keeps edited bound values and selection fresh across viewport recycling.</summary>
[ResetAmbientState]
public sealed class DataGridRecyclingTests
{
    private sealed class Product
    {
        public string ProductName { get; set; } = string.Empty;
        public double UnitPrice { get; set; }
    }

    private static (DataGrid grid, DataGridTextColumn name, DataGridTextColumn price, System.Collections.ObjectModel.ObservableCollection<Product> rows) Harness(int count)
    {
        var grid = new DataGrid();
        var name = new DataGridTextColumn { Header = "ProductName", BindingPath = "ProductName", Width = GridLength.Pixel(160) };
        var price = new DataGridTextColumn { Header = "UnitPrice", BindingPath = "UnitPrice", Width = GridLength.Pixel(80) };
        grid.AddColumn(name);
        grid.AddColumn(price);
        var rows = new System.Collections.ObjectModel.ObservableCollection<Product>();
        for (var i = 0; i < count; i++)
            rows.Add(new Product { ProductName = $"Item {i}", UnitPrice = i });
        grid.ItemsSource = rows;
        grid.Measure(new WpfSize(400, 600));
        grid.Arrange(new WpfRect(0, 0, 400, 600));
        return (grid, name, price, rows);
    }

    private static void AssertViewConsistent(DataGrid grid, DataGridTextColumn name, DataGridTextColumn price)
    {
        for (var i = 0; i < grid.View.Count; i++)
        {
            var item = grid.View[i];
            var row = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(i));
            Assert.Same(item, row.Item);
            Assert.Equal(i, row.GetIndex());
            var product = Assert.IsType<Product>(item);
            Assert.Equal(product.ProductName, grid.CellText(item, name));
            Assert.Equal(product.UnitPrice.ToString(), grid.CellText(item, price));
        }
    }

    [Fact]
    public void Mutations_rebuild_rows_without_stale_items_or_lost_selection()
    {
        var (grid, name, price, rows) = Harness(8);
        grid.SelectedIndex = 4;
        var selected = grid.SelectedItem;
        AssertViewConsistent(grid, name, price);

        // Insert above: selection tracks the same item, every row stays current.
        rows.Insert(0, new Product { ProductName = "New Top", UnitPrice = -1 });
        Assert.Same(selected, grid.SelectedItem);
        Assert.Equal(5, grid.SelectedIndex);
        AssertViewConsistent(grid, name, price);

        // Remove above: index shifts, identity holds.
        rows.RemoveAt(0);
        Assert.Same(selected, grid.SelectedItem);
        Assert.Equal(4, grid.SelectedIndex);
        AssertViewConsistent(grid, name, price);

        // Replace + move: containers follow the items, text tracks the model.
        rows[2] = new Product { ProductName = "Replaced", UnitPrice = 222 };
        rows.Move(2, 6);
        AssertViewConsistent(grid, name, price);
        Assert.Equal("Replaced", grid.CellText(grid.View[6], name));

        // Reset drops the selection and rebuilds clean (documented v1 behavior).
        grid.SelectedIndex = 1;
        rows.Clear();
        Assert.Equal(-1, grid.SelectedIndex);
        Assert.Null(grid.SelectedItem);
        Assert.Empty(grid.View);
        rows.Add(new Product { ProductName = "After Reset", UnitPrice = 1 });
        AssertViewConsistent(grid, name, price);
    }

    [Fact]
    public void Committed_edit_survives_sort_and_late_mutations()
    {
        var (grid, name, price, rows) = Harness(6);
        var target = rows[2];

        grid.CurrentCell = new DataGridCellInfo(target, name);
        Assert.True(grid.BeginEdit());
        Assert.IsType<TextBox>(RealizedCell(grid, target, name).Content).Text = "Survivor";
        Assert.True(grid.CommitEdit());

        grid.Items.SortDescriptions.Add(new SortDescription("UnitPrice", ListSortDirection.Descending));
        Assert.Equal("Survivor", grid.CellText(target, name));
        var at = grid.ViewIndexOf(target);
        Assert.True(at >= 0);
        Assert.Equal("Survivor", grid.CellText(grid.View[at], name));

        // A late source add rebuilds rows; the committed value is intact.
        rows.Add(new Product { ProductName = "Late", UnitPrice = 1000 });
        Assert.Equal("Survivor", grid.CellText(target, name));
        AssertViewConsistent(grid, name, price);
        grid.Items.SortDescriptions.Clear();
    }

    private sealed class NotifyProduct : System.ComponentModel.INotifyPropertyChanged
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

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    [Fact]
    public void Virtualized_scroll_keeps_edited_values_and_selection_fresh()
    {
        const string ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";
        var panel = new VirtualizingStackPanel { VirtualizationMode = VirtualizationMode.Recycling };
        var holders = Enumerable.Range(0, 5000).Select(i => new NotifyProduct { ProductName = $"Cell {i:D4}" }).ToList();
        var list = new ListBox
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            ItemsSource = holders,
            ItemTemplate = new DataTemplate($"<TextBlock {ns} Text=\"{{Binding ProductName}}\" />"),
            Height = 200,
            Width = 300,
        };
        list.SelectedIndex = 10;
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

        // A committed-style model edit lands while realized, then scrolls out.
        holders[10].ProductName = "EDITED-VALUE";
        Layout();
        panel.SetVerticalOffset(panel.GetTotalExtent(3000));
        Layout();
        panel.SetVerticalOffset(panel.GetTotalExtent(10));
        Layout();

        // Back in view: each recycled container serves its current item (item
        // identity rides the templated visual's DataContext) with the
        // selection flag on exactly the selected row — no stale DataContext.
        var (start, end) = (panel.RealizedRange.StartIndex, panel.RealizedRange.EndIndex);
        Assert.InRange(10, start, end - 1);
        for (var i = start; i < end; i++)
        {
            var container = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(i));
            Assert.True(container.HasTemplatedContent);
            var visual = Assert.IsType<TextBlock>(container.TemplatedContent);
            Assert.Same(holders[i], visual.DataContext);
            Assert.Equal(holders[i].ProductName, visual.Text);
            Assert.Equal(i == 10, container.IsSelected);
        }
        var edited = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(10));
        Assert.Equal("EDITED-VALUE", Assert.IsType<TextBlock>(edited.TemplatedContent).Text);
        Assert.True(edited.IsSelected);
        Assert.Equal(10, list.SelectedIndex);
        Assert.Equal(steady, list.ItemContainerGenerator.ContainersCreatedCount);
    }

    private static DataGridCell RealizedCell(DataGrid grid, object item, DataGridColumn column)
    {
        var row = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromItem(item));
        var cell = row.CellsPresenter.ContainerForColumn(column);
        Assert.NotNull(cell);
        return cell;
    }
}
