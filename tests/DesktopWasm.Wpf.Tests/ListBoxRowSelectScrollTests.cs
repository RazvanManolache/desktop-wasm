namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Selection scroll parity across the container path: setting IsSelected
/// directly on a row scrolls it into view like any other selection change.
/// </summary>
[ResetAmbientState]
public sealed class ListBoxRowSelectScrollTests
{
    [Fact]
    public void Setting_row_IsSelected_scrolls_it_into_view()
    {
        var view = new ScrollViewer { Width = 400, Height = 100 };
        var box = new ListBox();
        for (var i = 0; i < 20; i++) box.AddChild(new ListBoxItem { Content = $"R{i}" });
        view.Content = box;
        view.Measure(new WpfSize(400, 100));
        view.Arrange(new WpfRect(0, 0, 400, 100));
        Assert.Equal(0, view.VerticalOffset);

        box.Items[19].IsSelected = true;

        Assert.Equal(19, box.SelectedIndex);
        Assert.True(view.VerticalOffset > 0,
            $"row IsSelected must scroll (offset={view.VerticalOffset})");
    }
}
