using System.Collections.ObjectModel;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// ListBox SelectedValue over generated rows (job-59 follow-up): with
/// ItemsSource + DisplayMemberPath, the value path must resolve off the
/// backing data item — not the generated container (whose own Name is
/// empty) — like native selectors read data items.
/// </summary>
[ResetAmbientState]
public sealed class ListBoxSelectedValueSourceTests
{
    private sealed record Person(string Name, int Age);

    private static ListBox Harness()
    {
        var box = new ListBox();
        box.ItemsSource = new ObservableCollection<Person>(
        [
            new("Ada", 36),
            new("Grace", 85),
        ]);
        box.DisplayMemberPath = "Name";
        box.SelectedValuePath = "Name";
        return box;
    }

    [Fact]
    public void SelectedValue_selects_generated_row_by_data_path()
    {
        var box = Harness();

        box.SelectedValue = "Grace";

        Assert.Equal(1, box.SelectedIndex);
    }

    [Fact]
    public void SelectedValue_reads_data_path_off_generated_row()
    {
        var box = Harness();
        box.SelectedIndex = 0;

        Assert.Equal("Ada", box.SelectedValue);
    }
}
