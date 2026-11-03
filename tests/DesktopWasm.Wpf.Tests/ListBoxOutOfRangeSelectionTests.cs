namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Selector parity: an out-of-range SelectedIndex (transient during
/// collection churn, like the ComboBox tolerates) must read back a null
/// selection, never throw.
/// </summary>
[ResetAmbientState]
public sealed class ListBoxOutOfRangeSelectionTests
{
    [Fact]
    public void Out_of_range_index_reads_null_and_recovers()
    {
        var box = new ListBox();
        box.AddChild(new ListBoxItem { Content = "R0" });
        box.AddChild(new ListBoxItem { Content = "R1" });

        box.SelectedIndex = 99;

        Assert.Null(box.SelectedItem);

        box.SelectedIndex = 1;
        Assert.Equal("R1", box.SelectedItem?.Content);
    }
}
