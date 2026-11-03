namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// ListBox change notification: a programmatic selection change fires
/// SelectionChanged exactly once; a no-op set stays silent.
/// </summary>
[ResetAmbientState]
public sealed class ListBoxSelectionChangedTests
{
    [Fact]
    public void Effective_change_fires_once_and_noop_stays_silent()
    {
        var box = new ListBox();
        box.AddChild(new ListBoxItem { Content = "R0" });
        box.AddChild(new ListBoxItem { Content = "R1" });
        var fires = 0;
        box.SelectionChanged += () => fires++;

        box.SelectedIndex = 1;
        Assert.Equal(1, fires);

        box.SelectedIndex = 1;
        Assert.Equal(1, fires);

        box.SelectedIndex = -1;
        Assert.Equal(2, fires);
    }
}
