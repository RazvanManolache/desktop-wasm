namespace DesktopWasm.Wpf.Tests;

/// <summary>DataGrid auto-generated columns (genuine-WPF parity): the converted
/// gallery DataGridPage binds ItemsSource with no explicit columns and relies
/// on auto-generation. Bool props degrade to text cells (the engine has no
/// checkbox column type); indexers are never columns.</summary>
[ResetAmbientState]
public sealed class DataGridAutoGenerateColumnsTests
{
    private sealed class Product
    {
        public string Name { get; set; } = string.Empty;
        public int Stock { get; set; }
        public bool Discontinued { get; set; }
        public string Sku { get; } = "static-sku";
        public string this[int index] => "indexed";
    }

    private sealed class Other
    {
        public string Title { get; set; } = string.Empty;
    }

    [Fact]
    public void AutoGenerateColumns_defaults_true()
    {
        Assert.True(new DataGrid().AutoGenerateColumns);
    }

    [Fact]
    public void ItemsSource_mints_text_columns_per_property()
    {
        var grid = new DataGrid();
        grid.ItemsSource = new[]
        {
            new Product { Name = "Hammer", Stock = 12, Discontinued = true },
        };
        Assert.Equal(4, grid.Columns.Count);
        Assert.Equal(["Name", "Stock", "Discontinued", "Sku"],
            grid.Columns.Select(c => c.Header));
        Assert.Equal(["Name", "Stock", "Discontinued", "Sku"],
            grid.Columns.Select(c => c.BindingPath));
        Assert.Equal(1, grid.Items.Count);
        Assert.Equal("Hammer", grid.Columns[0].CellTextFor(grid.Items[0]));
        Assert.Equal("12", grid.Columns[1].CellTextFor(grid.Items[0]));
        Assert.Equal("True", grid.Columns[2].CellTextFor(grid.Items[0]));
    }

    [Fact]
    public void Explicit_columns_suppress_generation()
    {
        var grid = new DataGrid();
        grid.AddColumn(new DataGridTextColumn { Header = "Name", BindingPath = "Name", Width = GridLength.Pixel(120) });
        grid.ItemsSource = new[] { new Product { Name = "Hammer", Stock = 12 } };
        Assert.Single(grid.Columns);
        Assert.Equal("Name", grid.Columns[0].Header);
    }

    [Fact]
    public void AutoGenerateColumns_false_suppresses_generation()
    {
        var grid = new DataGrid { AutoGenerateColumns = false };
        grid.ItemsSource = new[] { new Product { Name = "Hammer", Stock = 12 } };
        Assert.Empty(grid.Columns);
        Assert.Single(grid.Items);
    }

    [Fact]
    public void New_source_type_regenerates_auto_columns()
    {
        var grid = new DataGrid();
        grid.ItemsSource = new[] { new Product { Name = "Hammer" } };
        Assert.Equal(4, grid.Columns.Count);
        grid.ItemsSource = new[] { new Other { Title = "Saw" } };
        Assert.Single(grid.Columns);
        Assert.Equal("Title", grid.Columns[0].Header);
        Assert.Equal("Saw", grid.Columns[0].CellTextFor(grid.Items[0]));
    }

    [Fact]
    public void Null_source_clears_auto_columns_but_keeps_explicit()
    {
        var grid = new DataGrid();
        grid.ItemsSource = new[] { new Product { Name = "Hammer" } };
        Assert.Equal(4, grid.Columns.Count);
        grid.ItemsSource = null;
        Assert.Empty(grid.Columns);
    }
}
