namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Native ComboBox parity: opening the drop raises DropDownOpened and
/// closing raises DropDownClosed — exactly once per transition, silent
/// on no-op sets.
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxDropOpenCloseEventsTests
{
    [Fact]
    public void Drop_transitions_raise_opened_and_closed_once()
    {
        var combo = new ComboBox();
        combo.AddItem(new ComboBoxItem { Content = "Blue" });
        var opened = 0;
        var closed = 0;
        combo.DropDownOpened += (_, _) => opened++;
        combo.DropDownClosed += (_, _) => closed++;

        combo.IsDropDownOpen = true;
        Assert.Equal(1, opened);
        Assert.Equal(0, closed);

        combo.IsDropDownOpen = true;
        Assert.Equal(1, opened);

        combo.IsDropDownOpen = false;
        Assert.Equal(1, opened);
        Assert.Equal(1, closed);
    }
}
