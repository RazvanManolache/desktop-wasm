namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Selection assignment edges: container-side sets route to the owner,
/// and foreign items clear instead of dangling.
/// </summary>
[ResetAmbientState]
public sealed class ItemAssignEdgeTests
{
    [Fact]
    public void ComboBoxItem_IsSelected_routes_to_owner()
    {
        var combo = new ComboBox();
        var a = new ComboBoxItem { Content = "A" };
        var b = new ComboBoxItem { Content = "B" };
        combo.AddItem(a);
        combo.AddItem(b);

        b.IsSelected = true;

        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal("B", combo.Text);
    }

    [Fact]
    public void ListBox_SelectedItem_foreign_item_clears()
    {
        var box = new ListBox();
        box.AddChild(new ListBoxItem { Content = "R0" });
        box.AddChild(new ListBoxItem { Content = "R1" });
        box.SelectedIndex = 0;

        box.SelectedItem = new ListBoxItem { Content = "Stranger" };

        Assert.Equal(-1, box.SelectedIndex);
        Assert.Null(box.SelectedItem);
    }

    [Fact]
    public void ComboBox_SelectedItem_data_item_selects_by_identity()
    {
        var combo = new ComboBox();
        var x = new ComboBoxItem { Content = "X" };
        var y = new ComboBoxItem { Content = "Y" };
        combo.AddItem(x);
        combo.AddItem(y);

        combo.SelectedItem = y;

        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal("Y", combo.Text);
    }
}
