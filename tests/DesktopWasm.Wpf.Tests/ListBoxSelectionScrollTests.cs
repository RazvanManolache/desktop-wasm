namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Native ListBox parity: selecting a row scrolls it into view (WPF routes
/// ListBoxItem selection through BringIntoView so keyboard/pointer selection
/// in a scrolled list stays visible). The engine BringIntoViewCore already
/// walks to the parent ScrollViewer; selection must call it.
/// </summary>
[ResetAmbientState]
public sealed class ListBoxSelectionScrollTests
{
    private static ScrollViewer Harness(out ListBox box, int count = 20)
    {
        var view = new ScrollViewer { Width = 400, Height = 100 };
        box = new ListBox();
        for (var i = 0; i < count; i++) box.AddChild(new ListBoxItem { Content = $"R{i}" });
        view.Content = box;
        view.Measure(new WpfSize(400, 100));
        view.Arrange(new WpfRect(0, 0, 400, 100));
        return view;
    }

    [Fact]
    public void Selecting_last_row_scrolls_it_into_view()
    {
        var view = Harness(out var box);
        Assert.Equal(0, view.VerticalOffset);

        box.SelectedIndex = 19;

        Assert.True(view.VerticalOffset > 0,
            $"selecting the last row must scroll (offset={view.VerticalOffset})");
    }

    [Fact]
    public void Selecting_first_row_after_scrolling_scrolls_back_to_top()
    {
        var view = Harness(out var box);
        box.SelectedIndex = 19;
        Assert.True(view.VerticalOffset > 0);

        box.SelectedIndex = 0;

        Assert.Equal(0, view.VerticalOffset);
    }
}
