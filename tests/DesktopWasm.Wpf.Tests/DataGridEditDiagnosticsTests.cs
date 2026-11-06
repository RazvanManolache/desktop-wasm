using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Edit-entry diagnostics contract (EDITENTRY H1): a flag-gated,
/// hook-readable snapshot reporting current-cell validity, editor state,
/// press-branch reach count and the last press/begin-edit outcome.
/// No behavior change while the flag is off.</summary>
[ResetAmbientState]
public sealed class DataGridEditDiagnosticsTests
{
    private sealed class Person
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    private static (WasmInputRouter Router, WpfInputManager Manager, DataGrid Grid, List<Person> People) Harness()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var grid = new DataGrid { Width = 400 };
        grid.AddColumn(new DataGridTextColumn { Header = "Name", BindingPath = "Name", Width = GridLength.Pixel(120) });
        grid.AddColumn(new DataGridTextColumn { Header = "Age", BindingPath = "Age", Width = GridLength.Star() });
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

    [Fact]
    public void Diagnostics_are_off_by_default()
    {
        Assert.False(DataGrid.EditDiagnosticsEnabled);
    }

    [Fact]
    public void Flag_off_records_nothing()
    {
        var (router, _, grid, _) = Harness();
        var r0 = grid.RowRects[0];
        Down(router, 60, r0.Y + r0.Height / 2);
        Assert.Equal(0, grid.EditPressCount);
        Assert.Contains("\"pressCount\":0", grid.GetEditDiagnostics());
    }

    [Fact]
    public void Press_records_branch_count_and_current_cell_state()
    {
        var (router, _, grid, people) = Harness();
        DataGrid.EditDiagnosticsEnabled = true;
        try
        {
            Assert.Contains("\"pressCount\":0", grid.GetEditDiagnostics());
            var r0 = grid.RowRects[0];
            Down(router, 60, r0.Y + r0.Height / 2);
            Assert.Equal(1, grid.EditPressCount);
            Assert.Same(people[0], grid.CurrentCell.Item);
            var snapshot = grid.GetEditDiagnostics();
            Assert.Contains("\"currentCellValid\":true", snapshot);
            Assert.Contains("\"editorOpen\":false", snapshot);
            Assert.Contains("\"pressCount\":1", snapshot);
            Assert.Contains("\"lastPress\":\"CellSet\"", snapshot);
        }
        finally
        {
            DataGrid.EditDiagnosticsEnabled = false;
        }
    }

    [Fact]
    public void Double_press_records_begin_edit_outcome_and_editor_open()
    {
        var (router, _, grid, people) = Harness();
        DataGrid.EditDiagnosticsEnabled = true;
        try
        {
            var r0 = grid.RowRects[0];
            Down(router, 60, r0.Y + r0.Height / 2);
            Down(router, 60, r0.Y + r0.Height / 2);
            var snapshot = grid.GetEditDiagnostics();
            Assert.Contains("\"pressCount\":2", snapshot);
            Assert.Contains("\"editorOpen\":true", snapshot);
            Assert.Contains("\"lastBeginEdit\":true", snapshot);
            Assert.Equal("Ada", people[0].Name);
        }
        finally
        {
            DataGrid.EditDiagnosticsEnabled = false;
        }
    }

    [Fact]
    public void Failed_begin_edit_is_recorded()
    {
        var (router, manager, grid, _) = Harness();
        DataGrid.EditDiagnosticsEnabled = true;
        try
        {
            // F2 with no current cell: BeginEdit is attempted and fails.
            Assert.True(manager.TryFocus(grid));
            Key(router, "F2");
            Assert.Contains("\"lastBeginEdit\":false", grid.GetEditDiagnostics());
            Assert.Contains("\"editorOpen\":false", grid.GetEditDiagnostics());
        }
        finally
        {
            DataGrid.EditDiagnosticsEnabled = false;
        }
    }
}
