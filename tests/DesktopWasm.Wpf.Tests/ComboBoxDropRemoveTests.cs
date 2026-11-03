namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Open-drop removal interplay (J25 scroll state meets J34 tracking): the
/// drop stays open on the tracked/slid selection instead of stranding it.
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxDropRemoveTests
{
    private static ComboBox Harness(int count = 20)
    {
        var combo = new ComboBox { Width = 400 };
        for (var i = 0; i < count; i++) combo.AddItem(new ComboBoxItem { Content = $"C{i}" });
        return combo;
    }

    private static string? Text(object? item) => (item as ComboBoxItem)?.Content;

    [Fact]
    public void Removing_above_tracks_with_drop_open()
    {
        var combo = Harness();
        combo.IsDropDownOpen = true;
        combo.SelectedIndex = 12;

        combo.Items.RemoveAt(0);

        Assert.Equal(11, combo.SelectedIndex);
        Assert.Equal("C12", Text(combo.SelectedItem));
        Assert.True(combo.IsDropDownOpen);
    }

    [Fact]
    public void Removing_selected_slides_with_drop_open()
    {
        var combo = Harness();
        combo.IsDropDownOpen = true;
        combo.SelectedIndex = 12;

        combo.Items.RemoveAt(12);

        Assert.Equal(12, combo.SelectedIndex);
        Assert.Equal("C13", Text(combo.SelectedItem));
        Assert.True(combo.IsDropDownOpen);
    }
}
