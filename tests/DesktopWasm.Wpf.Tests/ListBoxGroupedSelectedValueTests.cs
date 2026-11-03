using System.Collections.ObjectModel;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Grouped-list parity for SelectedValue (job-60 fix): GroupItem-nested
/// generated rows must resolve the value path off their backing data
/// items through the same row→data map.
/// </summary>
[ResetAmbientState]
public sealed class ListBoxGroupedSelectedValueTests
{
    private sealed record Item(string Category, string Name);

    private static ListBox Harness()
    {
        var box = new ListBox();
        var data = new ObservableCollection<Item>();
        for (var i = 0; i < 10; i++) data.Add(new Item("A", $"A{i}"));
        for (var i = 0; i < 10; i++) data.Add(new Item("B", $"B{i}"));
        box.DisplayMemberPath = "Name";
        box.SelectedValuePath = "Name";
        var source = new ListCollectionView(data);
        source.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
        box.ItemsSource = source;
        box.GroupStyle.Add(new GroupStyle());
        return box;
    }

    [Fact]
    public void SelectedValue_selects_grouped_row_by_data_path()
    {
        var box = Harness();

        box.SelectedValue = "B3";

        Assert.Equal(13, box.SelectedIndex);
    }

    [Fact]
    public void SelectedValue_reads_data_path_off_grouped_row()
    {
        var box = Harness();
        box.SelectedIndex = 13;

        Assert.Equal("B3", box.SelectedValue);
    }
}
