using System.Collections.ObjectModel;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// ComboBox SelectedValue over ItemsSource rows (job-60 mirror): the
/// Selector side sources raw data items, so the value path should
/// already resolve off data with no container gap.
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxSelectedValueSourceTests
{
    private sealed record Person(string Name, int Age);

    private static ComboBox Harness()
    {
        var combo = new ComboBox();
        combo.ItemsSource = new ObservableCollection<Person>(
        [
            new("Ada", 36),
            new("Grace", 85),
        ]);
        combo.DisplayMemberPath = "Name";
        combo.SelectedValuePath = "Name";
        return combo;
    }

    [Fact]
    public void SelectedValue_selects_source_row_by_data_path()
    {
        var combo = Harness();

        combo.SelectedValue = "Grace";

        Assert.Equal(1, combo.SelectedIndex);
    }

    [Fact]
    public void SelectedValue_reads_data_path_off_source_row()
    {
        var combo = Harness();
        combo.SelectedIndex = 0;

        Assert.Equal("Ada", combo.SelectedValue);
    }
}
