namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// ListBox removal keeps selection on its item (Selector semantics):
/// removing above shifts the index down, removing the selected row hands
/// selection to the row sliding into its place.
/// </summary>
[ResetAmbientState]
public sealed class ListBoxRemoveSelectionTests
{
    private static ListBox Harness(out List<ListBoxItem> rows, int count = 5)
    {
        var box = new ListBox();
        rows = new List<ListBoxItem>();
        for (var i = 0; i < count; i++)
        {
            var row = new ListBoxItem { Content = $"R{i}" };
            rows.Add(row);
            box.AddChild(row);
        }
        return box;
    }

    [Fact]
    public void Removing_selected_row_selects_the_row_sliding_in()
    {
        var box = Harness(out var rows);
        box.SelectedIndex = 2;
        var fires = 0;
        box.SelectionChanged += () => fires++;

        Assert.True(box.RemoveChild(rows[2]));

        Assert.Equal(2, box.SelectedIndex);
        Assert.Same(rows[3], box.SelectedItem);
        Assert.Equal(1, fires);
    }

    [Fact]
    public void Removing_row_above_shifts_selection_down()
    {
        var box = Harness(out var rows);
        box.SelectedIndex = 2;
        var fires = 0;
        box.SelectionChanged += () => fires++;

        Assert.True(box.RemoveChild(rows[0]));

        Assert.Equal(1, box.SelectedIndex);
        Assert.Same(rows[2], box.SelectedItem);
        Assert.Equal(1, fires);
    }

    [Fact]
    public void Removing_row_below_leaves_selection_silent()
    {
        var box = Harness(out var rows);
        box.SelectedIndex = 1;
        var fires = 0;
        box.SelectionChanged += () => fires++;

        Assert.True(box.RemoveChild(rows[3]));

        Assert.Equal(1, box.SelectedIndex);
        Assert.Same(rows[1], box.SelectedItem);
        Assert.Equal(0, fires);
    }
}
