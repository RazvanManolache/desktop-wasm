namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// ComboBox removal convention (matches ListBox/DataGrid): the selected
/// item survives removals elsewhere, a removed selection slides into its
/// raw slot, and nothing to slide into clears — never a dangling index.
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxRemoveSelectionTests
{
    private static ComboBox Harness()
    {
        var combo = new ComboBox();
        combo.AddItem(new ComboBoxItem { Content = "A" });
        combo.AddItem(new ComboBoxItem { Content = "B" });
        combo.AddItem(new ComboBoxItem { Content = "C" });
        return combo;
    }

    private static string? Text(object? item) => (item as ComboBoxItem)?.Content;

    [Fact]
    public void Removing_selected_middle_slides_into_its_slot()
    {
        var combo = Harness();
        combo.SelectedIndex = 1;

        combo.Items.RemoveAt(1);

        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal("C", Text(combo.SelectedItem));
    }

    [Fact]
    public void Removing_selected_last_clears()
    {
        var combo = Harness();
        combo.SelectedIndex = 2;

        combo.Items.RemoveAt(2);

        Assert.Equal(-1, combo.SelectedIndex);
        Assert.Null(combo.SelectedItem);
    }

    [Fact]
    public void Removing_row_above_tracks_the_item()
    {
        var combo = Harness();
        combo.SelectedIndex = 2;
        var fires = 0;
        combo.SelectionChanged += (_, _) => fires++;

        combo.Items.RemoveAt(0);

        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal("C", Text(combo.SelectedItem));
        Assert.Equal(1, fires);
    }
}
