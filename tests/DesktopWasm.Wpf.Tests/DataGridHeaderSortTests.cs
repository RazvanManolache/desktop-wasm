using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>D6 sort red-first repro (WPF-D6-2314): a header press through
/// the real pointer path must sort the rendered view by that column.
/// Gallery shape (auto-generated columns, ItemsSource-bound). Served
/// w20 shows 0.0px/order-unchanged on header clicks; this test decides
/// whether the managed data path or the served routing is at fault.
/// Outcome (WPF-D6-2314): GREEN in-process - the press reaches
/// TrySortAt, SortDescriptions apply, the view re-sorts with identity
/// preserved. The served fault is therefore upstream (header presses
/// never reach the grid handler served-side); this test locks the
/// data path while routing is diagnosed separately.</summary>
[ResetAmbientState]
public sealed class DataGridHeaderSortTests
{
    private sealed class Product
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
    }

    private static (WasmInputRouter Router, DataGrid Grid, List<Product> Products)
        Harness()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var grid = new DataGrid { Width = 400 };
        var products = new List<Product>
        {
            new() { ProductId = 0, ProductName = "Red Soup" },
            new() { ProductId = 1, ProductName = "Blueberry Marmalade" },
        };
        grid.ItemsSource = products;
        root.Content = grid;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(400, 600));
        mil.Render(root, new WpfSize(400, 600), new WpfRect(0, 0, 400, 600));
        return (router, grid, products);
    }

    private static readonly WasmWindowHandle Handle = new(1);

    private static void Down(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true));

    private static double ColumnCenterX(DataGrid grid, string header)
    {
        var index = grid.Columns.ToList().FindIndex(c => Equals(c.Header, header));
        Assert.True(index >= 0);
        var widths = grid.ColumnWidths;
        var cx = grid.RenderRect.X + 1;
        for (var i = 0; i < index; i++) cx += widths[i];
        return cx + widths[index] / 2;
    }

    [Fact]
    public void Header_press_sorts_view_and_keeps_selected_identity()
    {
        var (router, grid, products) = Harness();
        Assert.True(grid.Columns.Count > 0);

        // Select Blueberry (index 1) first: after an ascending Name sort
        // it must stay selected while sliding to index 0.
        var r1 = grid.RowRects[1];
        Down(router, grid.RenderRect.X + 60, r1.Y + r1.Height / 2);
        Assert.Same(products[1], grid.SelectedItem);

        // Press the ProductName header through the real pointer path.
        var headerY = grid.RowRects[0].Y - 2;
        Assert.True(headerY > grid.RenderRect.Y + 1);
        Down(router, ColumnCenterX(grid, "ProductName"), headerY);

        // The request must reach the data path...
        var descriptions = grid.Items.SortDescriptions;
        Assert.Single(descriptions);
        Assert.Equal("ProductName", descriptions[0].PropertyName);
        Assert.Equal(ListSortDirection.Ascending, descriptions[0].Direction);

        // ...and the rendered view must follow with identity preserved.
        Assert.Same(products[1], grid.SelectedItem);
        Assert.Equal(0, grid.SelectedIndex);
    }
}
