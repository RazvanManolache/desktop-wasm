namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Selector value contract on ListBox (ComboBox pins its half in
/// ComboBoxSelectedValueTests): SelectedValuePath + SelectedValue
/// round-trip through the shared Selector machinery.
/// </summary>
[ResetAmbientState]
public sealed class ListBoxSelectedValueTests
{
    [Fact]
    public void SelectedValue_selects_by_path_and_reads_back()
    {
        var box = new ListBox();
        box.AddChild(new ListBoxItem { Content = "R0" });
        box.AddChild(new ListBoxItem { Content = "R1" });
        box.SelectedValuePath = "Content";

        box.SelectedValue = "R1";

        Assert.Equal(1, box.SelectedIndex);
        Assert.Equal("R1", box.SelectedValue);
    }

    [Fact]
    public void SelectedValue_null_clears_and_miss_lands_at_nothing()
    {
        var box = new ListBox();
        box.AddChild(new ListBoxItem { Content = "R0" });
        box.AddChild(new ListBoxItem { Content = "R1" });
        box.SelectedValuePath = "Content";
        box.SelectedIndex = 0;

        box.SelectedValue = "Nobody";
        Assert.Equal(-1, box.SelectedIndex);
        Assert.Null(box.SelectedValue);

        box.SelectedIndex = 1;
        box.SelectedValue = null;
        Assert.Equal(-1, box.SelectedIndex);
    }
}
