using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Pointer-to-edit gesture contract (served regate pin): a press
/// establishes the current cell and a double-press on it begins editing.
/// Single press stays select-only.</summary>
[ResetAmbientState]
public sealed class DataGridPressEditTests
{
    private sealed class Person
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    private static (WasmInputRouter Router, WpfInputManager Manager, DataGrid Grid, List<Person> People)
        Harness(bool autoColumns)
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var grid = new DataGrid { Width = 400 };
        if (!autoColumns)
        {
            grid.AddColumn(new DataGridTextColumn { Header = "Name", BindingPath = "Name", Width = GridLength.Pixel(120) });
            grid.AddColumn(new DataGridTextColumn { Header = "Age", BindingPath = "Age", Width = GridLength.Star() });
        }
        var people = new List<Person>
        {
            new() { Name = "Ada", Age = 36 },
            new() { Name = "Grace", Age = 85 },
        };
        grid.ItemsSource = people;
        root.Content = grid;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(400, 600));
        mil.Render(root, new WpfSize(400, 600), new WpfRect(0, 0, 400, 600));
        return (router, manager, grid, people);
    }

    private static readonly WasmWindowHandle Handle = new(1);

    private static void Down(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true));

    private static void Key(WasmInputRouter router, string key) =>
        router.DispatchKeyboard(new WasmKeyArgs(Handle, key, true, false, false, false));

    private static DataGridCell RealizedCell(DataGrid grid, object item, DataGridColumn column)
    {
        var row = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromItem(item));
        var cell = row.CellsPresenter.ContainerForColumn(column);
        Assert.NotNull(cell);
        return cell;
    }

    [Fact]
    public void Press_sets_current_cell_to_hit_row_and_column()
    {
        var (router, _, grid, people) = Harness(autoColumns: false);
        Assert.False(grid.CurrentCell.IsValid);
        var r1 = grid.RowRects[1];

        Down(router, 60, r1.Y + r1.Height / 2); // Name column (pixel 120)
        Assert.Equal(1, grid.SelectedIndex);
        Assert.Same(people[1], grid.CurrentCell.Item);
        Assert.Same(grid.Columns[0], grid.CurrentCell.Column);

        Down(router, 200, r1.Y + r1.Height / 2); // Age column (star)
        Assert.Same(people[1], grid.CurrentCell.Item);
        Assert.Same(grid.Columns[1], grid.CurrentCell.Column);
        // Single press selects but never edits.
        Assert.IsNotType<TextBox>(RealizedCell(grid, people[1], grid.Columns[1]).Content);
    }

    [Fact]
    public void Arrow_keys_move_current_cell_with_selection()
    {
        var (router, manager, grid, people) = Harness(autoColumns: false);
        Assert.True(manager.TryFocus(grid));
        Assert.False(grid.CurrentCell.IsValid);

        Key(router, "ArrowDown");
        Assert.Equal(0, grid.SelectedIndex);
        Assert.Same(people[0], grid.CurrentCell.Item);
        Assert.Same(grid.Columns[0], grid.CurrentCell.Column);

        Key(router, "ArrowDown");
        Assert.Equal(1, grid.SelectedIndex);
        Assert.Same(people[1], grid.CurrentCell.Item);

        Key(router, "ArrowUp");
        Assert.Equal(0, grid.SelectedIndex);
        Assert.Same(people[0], grid.CurrentCell.Item);

        Key(router, "End");
        Assert.Same(people[1], grid.CurrentCell.Item);

        Key(router, "Home");
        Assert.Same(people[0], grid.CurrentCell.Item);
    }

    [Fact]
    public void Arrow_navigation_preserves_current_column()
    {
        var (router, manager, grid, people) = Harness(autoColumns: false);
        var r0 = grid.RowRects[0];
        Down(router, 200, r0.Y + r0.Height / 2); // Age column (star)
        Assert.Same(grid.Columns[1], grid.CurrentCell.Column);

        Assert.True(manager.TryFocus(grid));
        Key(router, "ArrowDown");
        Assert.Equal(1, grid.SelectedIndex);
        Assert.Same(people[1], grid.CurrentCell.Item);
        Assert.Same(grid.Columns[1], grid.CurrentCell.Column);
    }

    [Fact]
    public void Press_on_row_padding_falls_back_to_first_column()
    {
        // Narrow pixel columns leave row padding past the last column: the
        // press still selects the row, so the current cell must land on the
        // row's first column instead of staying unset.
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var grid = new DataGrid { Width = 400 };
        grid.AddColumn(new DataGridTextColumn { Header = "Name", BindingPath = "Name", Width = GridLength.Pixel(120) });
        grid.AddColumn(new DataGridTextColumn { Header = "Age", BindingPath = "Age", Width = GridLength.Pixel(100) });
        var people = new List<Person>
        {
            new() { Name = "Ada", Age = 36 },
            new() { Name = "Grace", Age = 85 },
        };
        grid.ItemsSource = people;
        root.Content = grid;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(400, 600));
        mil.Render(root, new WpfSize(400, 600), new WpfRect(0, 0, 400, 600));

        var r0 = grid.RowRects[0];
        Down(router, 300, r0.Y + r0.Height / 2); // past column 220px end, inside the row
        Assert.Equal(0, grid.SelectedIndex);
        Assert.Same(people[0], grid.CurrentCell.Item);
        Assert.Same(grid.Columns[0], grid.CurrentCell.Column);
    }

    [Fact]
    public void Keyboard_only_arrows_then_F2_opens_editor()
    {
        var (router, manager, grid, people) = Harness(autoColumns: false);
        Assert.True(manager.TryFocus(grid));

        Key(router, "ArrowDown");
        Key(router, "F2");
        var editor = Assert.IsType<TextBox>(RealizedCell(grid, people[0], grid.Columns[0]).Content);
        Assert.Equal("Ada", editor.Text);
    }

    [Fact]
    public void Press_below_rows_leaves_current_cell()
    {
        var (router, _, grid, people) = Harness(autoColumns: false);
        var r0 = grid.RowRects[0];
        Down(router, 60, r0.Y + r0.Height / 2);
        var current = grid.CurrentCell;
        Assert.True(current.IsValid);

        Down(router, 60, r0.Y + r0.Height + 200); // below the rows
        Assert.Equal(current, grid.CurrentCell);
    }

    [Fact]
    public void Double_press_opens_editor_on_current_cell()
    {
        var (router, _, grid, people) = Harness(autoColumns: false);
        var r0 = grid.RowRects[0];

        Down(router, 60, r0.Y + r0.Height / 2);
        Down(router, 60, r0.Y + r0.Height / 2); // double-press, same cell
        var editor = Assert.IsType<TextBox>(RealizedCell(grid, people[0], grid.Columns[0]).Content);
        Assert.Equal("Ada", editor.Text);
    }

    [Fact]
    public void Press_then_F2_type_Enter_Escape_end_to_end_on_auto_columns()
    {
        // Gallery-page shape: ItemsSource-bound with minted columns.
        var (router, manager, grid, people) = Harness(autoColumns: true);
        Assert.True(grid.Columns.Count > 0);
        var nameColumn = grid.Columns[0];
        var r0 = grid.RowRects[0];
        var widths = grid.ColumnWidths;
        var cx = grid.RenderRect.X + 1 + widths[0] / 2;

        Down(router, cx, r0.Y + r0.Height / 2);
        Assert.Same(people[0], grid.CurrentCell.Item);
        Assert.Same(nameColumn, grid.CurrentCell.Column);

        Assert.True(manager.TryFocus(grid));
        Key(router, "F2");
        var editor = Assert.IsType<TextBox>(RealizedCell(grid, people[0], nameColumn).Content);
        editor.Text = "Edited";
        Key(router, "Enter");
        Assert.Equal("Edited", people[0].Name);

        var r1 = grid.RowRects[1];
        Down(router, cx, r1.Y + r1.Height / 2);
        Key(router, "F2");
        Assert.IsType<TextBox>(RealizedCell(grid, people[1], nameColumn).Content).Text = "scratch";
        Key(router, "Escape");
        Assert.Equal("Grace", people[1].Name);
    }
}
