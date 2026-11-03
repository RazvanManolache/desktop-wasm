namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// ComboBox change notification (ListBox half pinned in job 63): the
/// typed SelectionChanged fires exactly once per effective change and
/// stays silent on a no-op set.
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxSelectionChangedTests
{
    [Fact]
    public void Effective_change_fires_once_and_noop_stays_silent()
    {
        var combo = new ComboBox();
        combo.AddItem(new ComboBoxItem { Content = "Blue" });
        combo.AddItem(new ComboBoxItem { Content = "Green" });
        var fires = 0;
        combo.SelectionChanged += (_, _) => fires++;

        combo.SelectedIndex = 1;
        Assert.Equal(1, fires);

        combo.SelectedIndex = 1;
        Assert.Equal(1, fires);

        combo.SelectedIndex = -1;
        Assert.Equal(2, fires);
    }
}
