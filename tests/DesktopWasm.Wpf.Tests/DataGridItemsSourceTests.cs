using System.Collections.ObjectModel;

namespace DesktopWasm.Wpf.Tests;

/// <summary>DataGrid ItemsSource: the converted gallery DataGrid page binds
/// ItemsSource="{Binding ...}". The engine DataGrid exposes a public
/// ItemsSource CLR property and the markup transfer works (pinned below,
/// including the served TwoWay + dotted-path shape). A served
/// "'DataGrid' has no bindable property 'ItemsSource'" failure therefore
/// points at the served hydration ambient, not this transfer.
/// The source collection mints plain data rows (the grid's v1 row model),
/// tracks live INCC edits, and guards direct-row mixing like WPF.</summary>
[ResetAmbientState]
public sealed class DataGridItemsSourceTests
{
    private sealed class Person
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    private sealed class Store
    {
        public ObservableCollection<Person> People { get; } = new()
        {
            new Person { Name = "Ada", Age = 36 },
            new Person { Name = "Grace", Age = 85 },
        };
    }

    private static DataGrid Grid()
    {
        var grid = new DataGrid();
        grid.AddColumn(new DataGridTextColumn { Header = "Name", BindingPath = "Name", Width = GridLength.Pixel(120) });
        grid.AddColumn(new DataGridTextColumn { Header = "Age", BindingPath = "Age", Width = GridLength.Star() });
        return grid;
    }

    [Fact]
    public void ItemsSource_bulk_rows_render_and_select()
    {
        var grid = Grid();
        var ada = new Person { Name = "Ada", Age = 36 };
        var grace = new Person { Name = "Grace", Age = 85 };
        grid.ItemsSource = new[] { ada, grace };
        Assert.Equal(2, grid.Items.Count);
        Assert.Same(ada, grid.Items[0]);

        grid.SelectedIndex = 1;
        Assert.Same(grace, grid.SelectedItem);
        Assert.Equal("Grace", ((Person?)grid.SelectedItem)?.Name);
    }

    [Fact]
    public void ItemsSource_live_collection_add_remove_reset()
    {
        var grid = Grid();
        var source = new ObservableCollection<Person>
        {
            new Person { Name = "Ada", Age = 36 },
        };
        grid.ItemsSource = source;
        Assert.Single(grid.Items);

        source.Add(new Person { Name = "Grace", Age = 85 });
        Assert.Equal(2, grid.Items.Count);

        source.RemoveAt(0);
        Assert.Single(grid.Items);
        Assert.Equal("Grace", ((Person?)grid.Items[0])?.Name);

        grid.SelectedIndex = 0;
        source.Clear();
        Assert.Empty(grid.Items);
        Assert.Equal(-1, grid.SelectedIndex);
        Assert.Null(grid.SelectedItem);
    }

    [Fact]
    public void ItemsSource_guards_mirror_wpf()
    {
        var direct = Grid();
        direct.Items.Add(new Person { Name = "Ada", Age = 36 });
        var ex = Assert.Throws<InvalidOperationException>(() => direct.ItemsSource = new[] { new Person() });
        Assert.Contains("Items collection must be empty", ex.Message);

        var sourced = Grid();
        sourced.ItemsSource = new ObservableCollection<Person> { new Person { Name = "Ada", Age = 36 } };
        var edit = Assert.Throws<InvalidOperationException>(() => sourced.Items.Add(new Person()));
        Assert.Contains("while ItemsSource is in use", edit.Message);

        sourced.ItemsSource = null; // clearing drops the generated rows
        Assert.Empty(sourced.Items);
        sourced.Items.Add(new Person { Name = "Hopper", Age = 41 });
        Assert.Equal("Hopper", ((Person?)sourced.Items[0])?.Name);
    }

    [Fact]
    public void Xaml_literal_ItemsSource_needs_binding()
    {
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            """<Window><DataGrid ItemsSource="People"/></Window>"""));
        Assert.Contains("needs a {Binding}", ex.Message);
    }

    [Fact]
    public void Xaml_attribute_binding_ItemsSource_loads_and_updates()
    {
        var root = (Window)XamlLoader.Load("""
            <Window><DataGrid ItemsSource="{Binding People}"><DataGrid.Columns><DataGridTextColumn Header="Name" BindingPath="Name" Width="120"/></DataGrid.Columns></DataGrid></Window>
            """).Root;
        var grid = Assert.IsType<DataGrid>(root.Content);
        var store = new Store();
        root.DataContext = store;
        Assert.Equal(2, grid.Items.Count);
        store.People.Add(new Person { Name = "Hopper", Age = 41 });
        Assert.Equal(3, grid.Items.Count);
    }

    [Fact]
    public void Xaml_attribute_binding_ItemsSource_twoway_dotted_path_loads()
    {
        // Served gallery pattern (DataGridPage.xaml line 59):
        // ItemsSource="{Binding ViewModel.ProductsCollection, Mode=TwoWay}".
        // Pins the engine markup contract for the TwoWay + dotted-path shape
        // (leaf name is irrelevant to the machinery) so a served hydration
        // failure points at ambient, not this call.
        var root = (Window)XamlLoader.Load("""
            <Window><DataGrid ItemsSource="{Binding ViewModel.People, Mode=TwoWay}"><DataGrid.Columns><DataGridTextColumn Header="Name" BindingPath="Name" Width="120"/></DataGrid.Columns></DataGrid></Window>
            """).Root;
        var grid = Assert.IsType<DataGrid>(root.Content);
        root.DataContext = new ViewModelStore();
        Assert.Equal(2, grid.Items.Count);
    }

    private sealed class ViewModelStore
    {
        public Store ViewModel { get; } = new();
    }
}
