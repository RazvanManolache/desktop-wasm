using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>DataGrid cell editing on the original-gallery workflow (RVW-1096 Job1).
/// The WPFGallery DataGrid page binds <c>ItemsSource="{Binding
/// ViewModel.ProductsCollection, Mode=TwoWay}"</c> over 50 <c>Product</c> rows
/// (ProductId/ProductCode/ProductName/QuantityPerUnit/UnitPrice/UnitsInStock);
/// the engine renders declared columns only (auto-columns stay a boundary),
/// so the harness binds the same model shape through declared text columns and
/// proves selection, keyboard focus/navigation and edit-&gt;commit/cancel-&gt;model.
/// Failing-first: no edit lifecycle exists yet (only the <c>CommitEdit</c> stub).</summary>
[ResetAmbientState]
public sealed class DataGridEditingTests
{
    private sealed class Product
    {
        public int ProductId { get; set; }

        public int ProductCode { get; set; }

        public string ProductName { get; set; } = string.Empty;

        public string QuantityPerUnit { get; set; } = string.Empty;

        public double UnitPrice { get; set; }

        public int UnitsInStock { get; set; }
    }

    private static readonly string[] Adjectives = ["Red", "Blueberry"];
    private static readonly string[] Names = ["Marmalade", "Dumplings", "Soup"];

    private static List<Product> GalleryProducts(int count)
    {
        var rows = new List<Product>(count);
        for (var i = 0; i < count; i++)
        {
            rows.Add(new Product
            {
                ProductId = i,
                ProductCode = i,
                ProductName = $"{Adjectives[i % Adjectives.Length]} {Names[i % Names.Length]}",
                QuantityPerUnit = "12",
                UnitPrice = Math.Round(i * 1.25 + 0.5, 3),
                UnitsInStock = (i * 7) % 100,
            });
        }
        return rows;
    }

    private sealed class Harness
    {
        public DataGrid Grid { get; } = new();
        public DataGridTextColumn NameColumn { get; } = new() { Header = "ProductName", BindingPath = "ProductName", Width = GridLength.Pixel(160) };
        public DataGridTextColumn PriceColumn { get; } = new() { Header = "UnitPrice", BindingPath = "UnitPrice", Width = GridLength.Pixel(80) };
        public DataGridTextColumn StockColumn { get; } = new() { Header = "UnitsInStock", BindingPath = "UnitsInStock", Width = GridLength.Pixel(80) };
        public List<Product> Products { get; } = GalleryProducts(50);

        public Harness()
        {
            Grid.AddColumn(NameColumn);
            Grid.AddColumn(PriceColumn);
            Grid.AddColumn(StockColumn);
            Grid.ItemsSource = Products;
            Grid.Measure(new WpfSize(400, 600));
            Grid.Arrange(new WpfRect(0, 0, 400, 600));
        }
    }

    private static DataGridCell RealizedCell(DataGrid grid, object item, DataGridColumn column)
    {
        var row = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromItem(item));
        var cell = row.CellsPresenter.ContainerForColumn(column);
        Assert.NotNull(cell);
        return cell;
    }

    [Fact]
    public void Gallery_workflow_select_navigate_edit_commit_cancel()
    {
        // Original-app shape: 50 gallery Products via ItemsSource, declared
        // columns over the real property paths (auto-columns are a boundary).
        var h = new Harness();
        Assert.Equal(50, h.Grid.Items.Count);

        // Selection: programmatic select lands on the source object.
        h.Grid.SelectedIndex = 3;
        Assert.Same(h.Products[3], h.Grid.SelectedItem);

        // Keyboard focus/navigation: arrows move the selection on the same rows.
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var grid2 = new DataGrid { Width = 400 };
        var name2 = new DataGridTextColumn { Header = "ProductName", BindingPath = "ProductName", Width = GridLength.Pixel(160) };
        grid2.AddColumn(name2);
        foreach (var p in h.Products) grid2.Items.Add(p);
        root.Content = grid2;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(400, 600));
        mil.Render(root, new WpfSize(400, 600), new WpfRect(0, 0, 400, 600));
        var handle = new WasmWindowHandle(1);
        Assert.True(manager.TryFocus(grid2));
        router.DispatchKeyboard(new WasmKeyArgs(handle, "ArrowDown", true, false, false, false));
        router.DispatchKeyboard(new WasmKeyArgs(handle, "ArrowDown", true, false, false, false));
        Assert.Equal(1, grid2.SelectedIndex);
        Assert.Same(h.Products[1], grid2.SelectedItem);

        // Edit -> commit -> model: rename row 3 through the cell editor.
        var target = h.Products[3];
        var before = target.ProductName;
        h.Grid.CurrentCell = new DataGridCellInfo(target, h.NameColumn);
        Assert.True(h.Grid.BeginEdit());
        var editor = Assert.IsType<TextBox>(RealizedCell(h.Grid, target, h.NameColumn).Content);
        Assert.Equal(before, editor.Text);
        editor.Text = "Edited Marmalade";
        Assert.True(h.Grid.CommitEdit());
        Assert.Equal("Edited Marmalade", target.ProductName);
        Assert.Equal("Edited Marmalade", h.Grid.CellText(target, h.NameColumn));

        // Edit -> cancel -> model untouched.
        var stockBefore = target.UnitsInStock;
        h.Grid.CurrentCell = new DataGridCellInfo(target, h.StockColumn);
        Assert.True(h.Grid.BeginEdit());
        var stockEditor = Assert.IsType<TextBox>(RealizedCell(h.Grid, target, h.StockColumn).Content);
        stockEditor.Text = "9999";
        Assert.True(h.Grid.CancelEdit());
        Assert.Equal(stockBefore, target.UnitsInStock);
        Assert.Equal(stockBefore.ToString(), h.Grid.CellText(target, h.StockColumn));

        // A committed edit survives a view sort; selection tracks the item.
        h.Grid.SelectedItem = target;
        h.Grid.Items.SortDescriptions.Add(new SortDescription("UnitPrice", ListSortDirection.Descending));
        Assert.Same(target, h.Grid.SelectedItem);
        var at = h.Grid.ViewIndexOf(target);
        Assert.True(at >= 0);
        Assert.Equal("Edited Marmalade", h.Grid.CellText(h.Grid.View[at], h.NameColumn));
        h.Grid.Items.SortDescriptions.Clear();
    }

    [Fact]
    public void BeginEdit_needs_current_cell_and_honors_read_only()
    {
        var h = new Harness();
        Assert.False(h.Grid.BeginEdit());
        h.Grid.CurrentCell = new DataGridCellInfo(h.Products[0], h.NameColumn);
        Assert.True(h.Grid.BeginEdit());
        Assert.True(h.Grid.CancelEdit());

        h.Grid.IsReadOnly = true;
        Assert.False(h.Grid.BeginEdit());
        h.Grid.IsReadOnly = false;
        Assert.True(h.Grid.BeginEdit());
        Assert.True(h.Grid.CancelEdit());
    }

    [Fact]
    public void CommitEdit_without_active_edit_reports_success()
    {
        var h = new Harness();
        Assert.True(h.Grid.CommitEdit());
        Assert.True(h.Grid.CancelEdit());
    }

    [Fact]
    public void CommitEdit_converts_text_into_numeric_model_values()
    {
        var h = new Harness();
        var target = h.Products[5];

        h.Grid.CurrentCell = new DataGridCellInfo(target, h.StockColumn);
        Assert.True(h.Grid.BeginEdit());
        Assert.IsType<TextBox>(RealizedCell(h.Grid, target, h.StockColumn).Content).Text = "42";
        Assert.True(h.Grid.CommitEdit());
        Assert.Equal(42, target.UnitsInStock);

        h.Grid.CurrentCell = new DataGridCellInfo(target, h.PriceColumn);
        Assert.True(h.Grid.BeginEdit());
        Assert.IsType<TextBox>(RealizedCell(h.Grid, target, h.PriceColumn).Content).Text = "19.99";
        Assert.True(h.Grid.CommitEdit());
        Assert.Equal(19.99, target.UnitPrice);
    }

    [Fact]
    public void CommitEdit_rejects_unconvertible_text_and_stays_editing()
    {
        var h = new Harness();
        var target = h.Products[5];
        var before = target.UnitsInStock;

        h.Grid.CurrentCell = new DataGridCellInfo(target, h.StockColumn);
        Assert.True(h.Grid.BeginEdit());
        var editor = Assert.IsType<TextBox>(RealizedCell(h.Grid, target, h.StockColumn).Content);
        editor.Text = "not-a-number";
        Assert.False(h.Grid.CommitEdit());
        Assert.Equal(before, target.UnitsInStock);

        // Still editing: the bad text is intact and cancel restores the display.
        Assert.Same(editor, RealizedCell(h.Grid, target, h.StockColumn).Content);
        Assert.True(h.Grid.CancelEdit());
        Assert.Equal(before.ToString(), h.Grid.CellText(target, h.StockColumn));
    }

    [Fact]
    public void BeginningEdit_cancel_blocks_the_editor()
    {
        var h = new Harness();
        var target = h.Products[1];
        h.Grid.BeginningEdit += (_, e) => e.Cancel = true;
        h.Grid.CurrentCell = new DataGridCellInfo(target, h.NameColumn);
        Assert.False(h.Grid.BeginEdit());
        Assert.False(RealizedCell(h.Grid, target, h.NameColumn).IsEditing);
    }

    [Fact]
    public void CellEditEnding_cancel_aborts_the_push()
    {
        var h = new Harness();
        var target = h.Products[2];
        var before = target.ProductName;
        h.Grid.CellEditEnding += (_, e) =>
        {
            if (e.EditAction == DataGridEditAction.Commit) e.Cancel = true;
        };
        h.Grid.CurrentCell = new DataGridCellInfo(target, h.NameColumn);
        Assert.True(h.Grid.BeginEdit());
        Assert.IsType<TextBox>(RealizedCell(h.Grid, target, h.NameColumn).Content).Text = "Blocked";
        Assert.False(h.Grid.CommitEdit());
        Assert.Equal(before, target.ProductName);
        Assert.True(h.Grid.CancelEdit());
    }

    [Fact]
    public void Selection_change_commits_a_pending_edit()
    {
        var h = new Harness();
        var target = h.Products[4];
        h.Grid.CurrentCell = new DataGridCellInfo(target, h.NameColumn);
        Assert.True(h.Grid.BeginEdit());
        Assert.IsType<TextBox>(RealizedCell(h.Grid, target, h.NameColumn).Content).Text = "Committed By Nav";

        h.Grid.SelectedIndex = 6;
        Assert.Equal(6, h.Grid.SelectedIndex);
        Assert.Equal("Committed By Nav", target.ProductName);
    }

    [Fact]
    public void Keyboard_F2_Enter_Escape_drive_the_edit_lifecycle()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var grid = new DataGrid { Width = 400 };
        var name = new DataGridTextColumn { Header = "ProductName", BindingPath = "ProductName", Width = GridLength.Pixel(160) };
        var stock = new DataGridTextColumn { Header = "UnitsInStock", BindingPath = "UnitsInStock", Width = GridLength.Pixel(80) };
        grid.AddColumn(name);
        grid.AddColumn(stock);
        var products = GalleryProducts(5);
        foreach (var p in products) grid.Items.Add(p);
        root.Content = grid;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(400, 600));
        mil.Render(root, new WpfSize(400, 600), new WpfRect(0, 0, 400, 600));
        var handle = new WasmWindowHandle(1);
        void Key(string key) => router.DispatchKeyboard(new WasmKeyArgs(handle, key, true, false, false, false));

        Assert.True(manager.TryFocus(grid));
        Key("ArrowDown");
        Assert.Equal(0, grid.SelectedIndex);
        grid.CurrentCell = new DataGridCellInfo(products[0], name);

        // F2 opens the editor; Enter commits to the model.
        Key("F2");
        var editor = Assert.IsType<TextBox>(RealizedCell(grid, products[0], name).Content);
        Assert.Equal(products[0].ProductName, editor.Text);
        editor.Text = "Keyboard Edit";
        Key("Enter");
        Assert.Equal("Keyboard Edit", products[0].ProductName);

        // F2 then Escape discards.
        grid.CurrentCell = new DataGridCellInfo(products[0], stock);
        var stockBefore = products[0].UnitsInStock;
        Key("F2");
        Assert.IsType<TextBox>(RealizedCell(grid, products[0], stock).Content).Text = "12345";
        Key("Escape");
        Assert.Equal(stockBefore, products[0].UnitsInStock);
    }

    [Fact]
    public void RowEditEnding_fires_after_a_cell_commit()
    {
        var h = new Harness();
        var target = h.Products[7];
        var seen = new List<DataGridEditAction>();
        h.Grid.RowEditEnding += (_, e) => seen.Add(e.EditAction);
        h.Grid.CurrentCell = new DataGridCellInfo(target, h.NameColumn);
        Assert.True(h.Grid.BeginEdit());
        Assert.IsType<TextBox>(RealizedCell(h.Grid, target, h.NameColumn).Content).Text = "Row Ending";
        Assert.True(h.Grid.CommitEdit());
        Assert.Equal([DataGridEditAction.Commit], seen);
        Assert.Equal("Row Ending", target.ProductName);
    }
}
