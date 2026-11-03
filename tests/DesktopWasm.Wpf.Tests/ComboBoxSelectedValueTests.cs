using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Selector value contract on ComboBox: SelectedValuePath + SelectedValue
/// round-trips to the matching index (XAML/binding-driven selection by
/// value, not just by row).
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxSelectedValueTests
{
    [Fact]
    public void SelectedValue_selects_by_path_and_reads_back()
    {
        var combo = new ComboBox();
        combo.AddItem(new ComboBoxItem { Content = "Blue" });
        combo.AddItem(new ComboBoxItem { Content = "Green" });
        combo.AddItem(new ComboBoxItem { Content = "Red" });
        combo.SelectedValuePath = "Content";

        combo.SelectedValue = "Green";

        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal("Green", combo.SelectedValue);
    }
}
