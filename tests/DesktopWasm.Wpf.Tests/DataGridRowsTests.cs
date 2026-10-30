namespace DesktopWasm.Wpf.Tests;

/// <summary>DataGrid row/cell/column containers, sorting and logical-tree
/// helpers (ScreenToGif DataGridHelper/KeyStrokes/VisualHelper contract:
/// ContainerFromIndex/Item generation, cell lookup, CurrentCell,
/// SortDescriptions, LogicalTreeHelper.GetParent).</summary>
[ResetAmbientState]
public sealed class DataGridRowsTests
{
    private sealed class Person
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    private static (DataGrid grid, DataGridTextColumn name, DataGridTextColumn age, Person ada, Person grace) Harness()
    {
        var grid = new DataGrid();
        var name = new DataGridTextColumn { Header = "Name", BindingPath = "Name", Width = GridLength.Pixel(120) };
        var age = new DataGridTextColumn { Header = "Age", BindingPath = "Age", Width = GridLength.Star() };
        grid.AddColumn(name);
        grid.AddColumn(age);
        var ada = new Person { Name = "Ada", Age = 36 };
        var grace = new Person { Name = "Grace", Age = 85 };
        grid.Items.Add(ada);
        grid.Items.Add(grace);
        grid.Measure(new WpfSize(400, 600));
        grid.Arrange(new WpfRect(0, 0, 400, 600));
        return (grid, name, age, ada, grace);
    }

    [Fact]
    public void Column_hierarchy_matches_real_wpf()
    {
        var column = new DataGridTextColumn();
        Assert.IsAssignableFrom<DataGridBoundColumn>(column);
        Assert.IsAssignableFrom<DataGridColumn>(column);
        Assert.IsAssignableFrom<DependencyObject>(column);
        var binding = new Binding("Name");
        column.Binding = binding;
        Assert.Same(binding, column.Binding);
        // A real Binding path wins over the engine BindingPath extension.
        Assert.Equal("Name", column.EffectivePath);
        column.Binding = null;
        column.BindingPath = "Age";
        Assert.Equal("Age", column.EffectivePath);
    }

    [Fact]
    public void Columns_carry_display_index_and_sort_state()
    {
        var (grid, name, age, _, _) = Harness();
        Assert.Equal(0, name.DisplayIndex);
        Assert.Equal(1, age.DisplayIndex);
        Assert.Null(name.SortDirection);
        name.SortDirection = ListSortDirection.Ascending;
        name.SortMemberPath = "Name";
        Assert.Equal(ListSortDirection.Ascending, name.SortDirection);
        Assert.True(name.SortDirection.HasValue);
        Assert.Equal((int)ListSortDirection.Descending, 1);
        Assert.Equal((int)ListSortDirection.Ascending, 0);
        Assert.True(grid.RemoveColumn(name));
        Assert.Equal(0, age.DisplayIndex);
        Assert.Equal(-1, name.DisplayIndex);
    }

    [Fact]
    public void Row_containers_round_trip_through_the_generator()
    {
        var (grid, _, _, ada, grace) = Harness();
        var row0 = grid.ItemContainerGenerator.ContainerFromIndex(0) as DataGridRow;
        var row1 = grid.ItemContainerGenerator.ContainerFromIndex(1) as DataGridRow;
        Assert.NotNull(row0);
        Assert.NotNull(row1);
        Assert.Same(ada, row0.Item);
        Assert.Same(grace, row1.Item);
        Assert.Equal(0, row0.GetIndex());
        Assert.Equal(1, row1.GetIndex());
        Assert.Same(row1, grid.ItemContainerGenerator.ContainerFromItem(grace));
        Assert.Null(grid.ItemContainerGenerator.ContainerFromIndex(2));
        // The shared generator mints containers on demand (documented
        // ItemsControl behavior); off-grid items realize with no view index.
        var alien = Assert.IsType<DataGridRow>(
            grid.ItemContainerGenerator.ContainerFromItem(new Person { Name = "Zed", Age = 3 }));
        Assert.Equal(-1, alien.GetIndex());
        Assert.Same(row0, grid.ContainerForItem(ada));
        Assert.Equal(1, grid.ViewIndexOf(grace));
        Assert.Equal(-1, grid.ViewIndexOf(new Person()));
    }

    [Fact]
    public void Row_hosts_cells_presenter_and_details_presenter()
    {
        var (grid, _, _, ada, _) = Harness();
        var row = grid.ItemContainerGenerator.ContainerFromIndex(0) as DataGridRow;
        Assert.NotNull(row);
        Assert.Same(grid, row.OwnerGrid);
        Assert.Equal(2, VisualTreeHelper.GetChildrenCount(row));
        var presenter = Assert.IsType<DataGridCellsPresenter>(VisualTreeHelper.GetChild(row, 0));
        Assert.IsType<DataGridDetailsPresenter>(VisualTreeHelper.GetChild(row, 1));
        Assert.Same(row, VisualTreeHelper.GetParent(presenter));
        Assert.Same(row, LogicalTreeHelper.GetParent(presenter));
        Assert.Same(ada, presenter.RowItem);
        Assert.Same(grid, presenter.OwnerGrid);
    }

    [Fact]
    public void Cells_resolve_per_column_with_text_content()
    {
        var (grid, name, age, ada, _) = Harness();
        var row = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(0));
        var cell0 = row.CellsPresenter.ItemContainerGenerator.ContainerFromIndex(0) as DataGridCell;
        var cell1 = row.CellsPresenter.ItemContainerGenerator.ContainerFromIndex(1) as DataGridCell;
        Assert.NotNull(cell0);
        Assert.NotNull(cell1);
        Assert.Same(name, cell0.Column);
        Assert.Same(age, cell1.Column);
        var content = Assert.IsType<TextBlock>(name.GetCellContent(ada));
        Assert.Equal("Ada", content.Text);
        Assert.Same(cell0, content.Parent);
        var ageContent = Assert.IsType<TextBlock>(age.GetCellContent(ada));
        Assert.Equal("36", ageContent.Text);
        // Unknown columns and missing owners answer null, like an
        // unrealized virtualized cell.
        Assert.Null(new DataGridTextColumn { BindingPath = "Name" }.GetCellContent(ada));
        Assert.Null(name.GetCellContent(new Person { Name = "Nobody", Age = 1 }));
        Assert.Null(row.CellsPresenter.ItemContainerGenerator.ContainerFromIndex(7));
    }

    [Fact]
    public void Selection_syncs_realized_rows()
    {
        var (grid, _, _, ada, grace) = Harness();
        var row0 = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(0));
        var row1 = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(1));
        Assert.False(row0.IsSelected);
        grid.SelectedIndex = 1;
        Assert.True(row1.IsSelected);
        Assert.False(row0.IsSelected);
        Assert.Same(grace, grid.SelectedItem);
        grid.SelectedItem = ada;
        Assert.True(row0.IsSelected);
        Assert.False(row1.IsSelected);
        row0.IsSelected = false;
        Assert.Null(grid.SelectedItem);
        Assert.Equal(-1, grid.SelectedIndex);
    }

    [Fact]
    public void Current_cell_and_scroll_into_view_realize_containers()
    {
        var (grid, name, _, ada, _) = Harness();
        Assert.False(grid.CurrentCell.IsValid);
        Assert.Equal(DataGridCellInfo.Unset, grid.CurrentCell);
        grid.ScrollIntoView(ada, name);
        var row = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromItem(ada));
        Assert.NotNull(row.CellsPresenter.ItemContainerGenerator.ContainerFromItem(name));
        grid.CurrentCell = new DataGridCellInfo(ada, name);
        Assert.True(grid.CurrentCell.IsValid);
        Assert.Same(ada, grid.CurrentCell.Item);
        Assert.Same(name, grid.CurrentCell.Column);
        Assert.Same(name, grid.CurrentColumn);
        var other = new DataGridCellInfo(ada, name);
        Assert.Equal(grid.CurrentCell, other);
        Assert.True(grid.CurrentCell == other);
        Assert.False(grid.CurrentCell == DataGridCellInfo.Unset);
    }

    [Fact]
    public void Sort_descriptions_reorder_the_view_not_the_source()
    {
        var (grid, name, age, ada, grace) = Harness();
        grid.SelectedItem = grace;
        grid.Items.SortDescriptions.Add(new SortDescription("Age", ListSortDirection.Descending));
        Assert.Single(grid.Items.SortDescriptions);
        // View flips (Grace 85 first); source order is untouched.
        Assert.Same(ada, grid.Items[0]);
        var first = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Same(grace, first.Item);
        Assert.Equal(0, first.GetIndex());
        Assert.Equal(0, grid.SelectedIndex);
        Assert.Same(grace, grid.SelectedItem);
        Assert.True(first.IsSelected);
        // Rendered text follows the sorted view.
        Assert.Equal("Grace", grid.CellText(grid.View[0], name));
        Assert.Equal("85", grid.CellText(grid.View[0], age));
        grid.Items.SortDescriptions.Clear();
        var restored = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Same(ada, restored.Item);
        Assert.Equal(1, grid.SelectedIndex);
    }

    [Fact]
    public void Sort_matches_screen_to_gif_helper_flow()
    {
        var (grid, _, _, ada, grace) = Harness();
        // DataGridHelper.Sort: skip when a column already sorts, else mark
        // the matching bound column and describe the sort.
        grid.Items.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
        foreach (var column in grid.Columns)
        {
            if (column.SortDirection.HasValue) continue;
            if (column is DataGridTextColumn dataColumn)
            {
                dataColumn.Binding ??= new Binding(dataColumn.BindingPath);
                column.SortMemberPath = dataColumn.Binding.Path.Path;
                column.SortDirection = ListSortDirection.Ascending;
            }
        }
        var first = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Same(ada, first.Item);
        var second = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(1));
        Assert.Same(grace, second.Item);
        grid.Items.SortDescriptions.Clear();
    }

    [Fact]
    public void Sort_and_group_description_values_match_real_wpf()
    {
        var ascending = new SortDescription("Name", ListSortDirection.Ascending);
        Assert.Equal("Name", ascending.PropertyName);
        Assert.Equal(ListSortDirection.Ascending, ascending.Direction);
        Assert.Equal(ascending, new SortDescription("Name", ListSortDirection.Ascending));
        Assert.NotEqual(ascending, new SortDescription("Name", ListSortDirection.Descending));
        Assert.NotEqual(ascending, SortDescription.Empty);
        var groups = new DataGridItemCollection();
        groups.GroupDescriptions.Add(new PropertyGroupDescription("Mode"));
        Assert.Single(groups.GroupDescriptions);
        Assert.Equal("b", groups.GroupDescriptions[0].GroupNameFromItem(
            new GroupProbe { Mode = "b" }, 0, System.Globalization.CultureInfo.InvariantCulture));
    }

    private sealed class GroupProbe
    {
        public string Mode { get; set; } = string.Empty;
    }

    [Fact]
    public void Logical_tree_helper_walks_parents_and_names()
    {
        var (grid, _, _, ada, _) = Harness();
        var row = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(0));
        var cell = Assert.IsType<DataGridCell>(row.CellsPresenter.ItemContainerGenerator.ContainerFromIndex(0));
        var content = cell.EnsureContent(ada);
        Assert.NotNull(content);
        Assert.Same(row.CellsPresenter, LogicalTreeHelper.GetParent(cell));
        Assert.Same(row, LogicalTreeHelper.GetParent(row.CellsPresenter));
        Assert.Null(LogicalTreeHelper.GetParent(row));
        Assert.Contains(LogicalTreeHelper.GetChildren(row), c => ReferenceEquals(c, row.CellsPresenter));
        Assert.Null(LogicalTreeHelper.FindLogicalNode(row, "missing"));
        // Cell content is reached through the cell (Content is not a
        // logical child in the engine tree); containers carry names.
        cell.Name = "firstCell";
        Assert.Same(cell, LogicalTreeHelper.FindLogicalNode(row, "firstCell"));
        // Realized rows stay out of the grid's own visual tree (it paints
        // itself), so the grid root cannot see them by name.
        Assert.Null(LogicalTreeHelper.FindLogicalNode(grid, "firstCell"));
    }

    [Fact]
    public void Realized_rows_rebuild_after_source_changes()
    {
        var (grid, _, _, ada, _) = Harness();
        var before = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Same(ada, before.Item);
        grid.Items.RemoveAt(0);
        var after = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.NotSame(before, after);
        Assert.Equal(0, after.GetIndex());
    }
}
