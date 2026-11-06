using DesktopWasm.Core;
using DesktopWasm.Graphics;
using SkiaSharp;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Editor value-flow (served sh2 split: entry green, value red).
/// Opening the editor must move keyboard focus into it (like WPF moves
/// focus to the editing element), so routed character keys land in the
/// editor, the caret/editor paints, and Enter commits typed text.
/// Pre-fix: focus stays on the grid/cell, typing is lost, commit persists
/// nothing — the exact served state-vs-pixels split.</summary>
[ResetAmbientState]
public sealed class DataGridEditorValueFlowTests
{
    private sealed class Person
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    private static (WasmInputRouter Router, WpfInputManager Manager, DataGrid Grid, List<Person> People, Window Root)
        Harness()
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
        return (router, manager, grid, people, root);
    }

    private static readonly WasmWindowHandle Handle = new(1);

    private static void Down(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true));

    private static void Key(WasmInputRouter router, string key) =>
        router.DispatchKeyboard(new WasmKeyArgs(Handle, key, true, false, false, false));

    private static TextBox OpenEditor(WasmInputRouter router, WpfInputManager manager, DataGrid grid, List<Person> people)
    {
        var r0 = grid.RowRects[0];
        Down(router, 60, r0.Y + r0.Height / 2);
        // Served precondition (H1 green): the grid holds focus and F2 opens
        // the editor; the defect under test is everything after that.
        Assert.True(manager.TryFocus(grid));
        Key(router, "F2");
        var row = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromItem(people[0]));
        var cell = row.CellsPresenter.ContainerForColumn(grid.Columns[0]);
        Assert.NotNull(cell);
        return Assert.IsType<TextBox>(cell.Content);
    }

    [Fact]
    public void BeginEdit_moves_keyboard_focus_to_editor()
    {
        var (router, manager, grid, people, _) = Harness();
        var editor = OpenEditor(router, manager, grid, people);
        Assert.Same(editor, manager.Focused);
    }

    [Fact]
    public void Typed_keys_land_in_editor_and_Enter_commits_them()
    {
        var (router, manager, grid, people, _) = Harness();
        var editor = OpenEditor(router, manager, grid, people);
        Key(router, "W");
        Key(router, "9");
        Assert.StartsWith("W9", editor.Text);
        Key(router, "Enter");
        Assert.StartsWith("W9", people[0].Name);
    }

    [Fact]
    public void Arrow_navigation_still_moves_selection_after_Enter_commit()
    {
        // No-regression pin for the focus move: after commit, arrow keys
        // must still drive grid selection (served H1 shows arrows alive
        // post-Enter; focus must not dangle on the swapped-out editor).
        var (router, manager, grid, people, _) = Harness();
        var editor = OpenEditor(router, manager, grid, people);
        editor.Text = "W9Ada";
        Key(router, "Enter");
        Assert.Equal("W9Ada", people[0].Name);
        Key(router, "ArrowDown");
        Assert.Equal(1, grid.SelectedIndex);
        Assert.Same(people[1], grid.CurrentCell.Item);
    }

    [Fact]
    public void Open_editor_repaints_cell_region()
    {
        var (router, manager, grid, people, root) = Harness();
        using var before = new SkiaSurface(400, 600);
        var mil = new WasmMilCore(before);
        mil.CommitChannel(mil.Render(root, new WpfSize(400, 600), new WpfRect(0, 0, 400, 600)));
        using var beforeBits = SKBitmap.FromImage(before.Snapshot());

        OpenEditor(router, manager, grid, people);
        using var after = new SkiaSurface(400, 600);
        var mil2 = new WasmMilCore(after);
        mil2.CommitChannel(mil2.Render(root, new WpfSize(400, 600), new WpfRect(0, 0, 400, 600)));
        using var afterBits = SKBitmap.FromImage(after.Snapshot());

        Assert.Equal(beforeBits.Width, afterBits.Width);
        Assert.Equal(beforeBits.Height, afterBits.Height);
        var same = true;
        for (var y = 0; y < beforeBits.Height && same; y++)
            for (var x = 0; x < beforeBits.Width && same; x++)
                same = beforeBits.GetPixel(x, y) == afterBits.GetPixel(x, y);
        Assert.False(same, "opening the editor must repaint (editor chrome/text/caret differ from display)");
    }
}
