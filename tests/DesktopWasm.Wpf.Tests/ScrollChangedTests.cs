namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// ScreenToGif scroll slice: ZoomBox.OnScrollViewerScrollChanged reads
/// e.ExtentHeightChange/e.ExtentWidthChange/e.ExtentHeight/e.ExtentWidth,
/// ImageViewer.ScrollContainer_OnScrollChanged reads the same plus offsets,
/// and Util.ScrollSynchronizer gates on e.VerticalChange/e.HorizontalChange.
/// The engine ScrollViewer must fire ScrollChanged with those exact args
/// from the real scroll path (offset/extent/viewport transitions).
/// </summary>
[ResetAmbientState]
public sealed class ScrollChangedTests
{
    private static ScrollViewer Harness(out StackPanel rows, int count = 10)
    {
        var view = new ScrollViewer { Width = 400, Height = 100 };
        rows = new StackPanel();
        for (var i = 0; i < count; i++) rows.AddChild(new Button { Content = $"B{i}" });
        view.Content = rows;
        view.Measure(new WpfSize(400, 100));
        view.Arrange(new WpfRect(0, 0, 400, 100));
        return view;
    }

    [Fact]
    public void Wheel_scroll_fires_with_offset_change_and_extent_viewport()
    {
        var view = Harness(out _);
        ScrollChangedEventArgs? seen = null;
        object? sender = null;
        view.ScrollChanged += (s, e) => { sender = s; seen = e; };

        view.VerticalOffset = 60;

        Assert.Same(view, sender);
        Assert.NotNull(seen);
        Assert.Same(ScrollViewer.ScrollChangedEvent, seen.RoutedEvent);
        Assert.Same(view, seen.Source);
        Assert.Equal(60, seen.VerticalOffset);
        Assert.Equal(60, seen.VerticalChange);
        Assert.Equal(0, seen.HorizontalChange);
        Assert.Equal(0, seen.HorizontalOffset);
        Assert.Equal(320, seen.ExtentHeight);
        Assert.Equal(0, seen.ExtentHeightChange);
        Assert.Equal(98, seen.ViewportHeight);
        Assert.Equal(0, seen.ViewportHeightChange);
    }

    [Fact]
    public void Setting_same_offset_stays_silent()
    {
        var view = Harness(out _);
        view.VerticalOffset = 60;
        var fires = 0;
        view.ScrollChanged += (_, _) => fires++;

        view.VerticalOffset = 60;

        Assert.Equal(0, fires);
    }

    [Fact]
    public void Extent_growth_fires_with_extent_change_only()
    {
        // ZoomBox guard shape: ignore unless an extent axis changed, then
        // recenter from ExtentWidth/ExtentHeight.
        var view = Harness(out var rows);
        view.VerticalOffset = 40;
        var seen = new List<ScrollChangedEventArgs>();
        view.ScrollChanged += (_, e) =>
        {
            if (Math.Abs(e.ExtentHeightChange) < 0.01 && Math.Abs(e.ExtentWidthChange) < 0.01) return;
            seen.Add(e);
        };

        for (var i = 0; i < 5; i++) rows.AddChild(new Button { Content = "N" });
        view.Measure(new WpfSize(400, 100));
        view.Arrange(new WpfRect(0, 0, 400, 100));

        var only = Assert.Single(seen);
        Assert.Equal(160, only.ExtentHeightChange);
        Assert.Equal(480, only.ExtentHeight);
        Assert.Equal(0, only.VerticalChange);
        Assert.Equal(40, only.VerticalOffset);
    }

    [Fact]
    public void Viewport_resize_fires_with_viewport_change()
    {
        // Explicit Height wins over the arrange slot (WPF parity), so a
        // viewport resize is modeled by changing Height like a real window
        // resize would relay through the parent.
        var view = Harness(out _);
        var seen = new List<ScrollChangedEventArgs>();
        view.ScrollChanged += (_, e) => seen.Add(e);

        view.Height = 60;
        view.Measure(new WpfSize(400, 100));
        view.Arrange(new WpfRect(0, 0, 400, 60));

        var only = Assert.Single(seen);
        Assert.Equal(-40, only.ViewportHeightChange);
        Assert.Equal(58, only.ViewportHeight);
        Assert.Equal(0, only.VerticalChange);
    }

    [Fact]
    public void ScrollSynchronizer_pattern_syncs_follower_and_ignores_zero_delta()
    {
        // Util.ScrollSynchronizer.ScrollViewer_ScrollChanged shape.
        var leader = Harness(out _);
        var follower = Harness(out _);
        leader.ScrollChanged += (_, e) =>
        {
            if (e.VerticalChange == 0 && e.HorizontalChange == 0) return;
            follower.ScrollToVerticalOffset(leader.VerticalOffset);
        };
        var followerFires = 0;
        follower.ScrollChanged += (_, _) => followerFires++;

        leader.VerticalOffset = 100;

        Assert.Equal(100, follower.VerticalOffset);
        Assert.Equal(1, followerFires);
    }

    [Fact]
    public void ScrollToVerticalOffset_matches_setter_and_clamps()
    {
        var view = Harness(out _);
        var seen = new List<ScrollChangedEventArgs>();
        view.ScrollChanged += (_, e) => seen.Add(e);

        view.ScrollToVerticalOffset(500);
        Assert.Equal(222, view.VerticalOffset);
        var first = Assert.Single(seen);
        Assert.Equal(222, first.VerticalOffset);
        Assert.Equal(222, first.VerticalChange);

        view.ScrollToTop();
        Assert.Equal(0, view.VerticalOffset);
        view.ScrollToBottom();
        Assert.Equal(222, view.VerticalOffset);
        Assert.Equal(3, seen.Count);
    }

    [Fact]
    public void Removed_handler_and_static_remove_stay_silent()
    {
        var view = Harness(out _);
        var fires = 0;
        void OnScroll(object sender, ScrollChangedEventArgs e) => fires++;
        view.ScrollChanged += OnScroll;
        view.ScrollChanged -= OnScroll;

        ScrollChangedEventHandler staticHandler = (_, _) => fires++;
        ScrollViewer.AddScrollChangedHandler(view, staticHandler);
        ScrollViewer.RemoveScrollChangedHandler(view, staticHandler);

        view.VerticalOffset = 60;

        Assert.Equal(0, fires);
    }

    [Fact]
    public void Static_add_handler_receives_routed_event_on_scroll()
    {
        var view = Harness(out _);
        ScrollChangedEventArgs? seen = null;
        ScrollChangedEventHandler handler = (_, e) => seen = e;
        ScrollViewer.AddScrollChangedHandler(view, handler);

        view.LineDown();

        Assert.NotNull(seen);
        Assert.Equal(16, seen.VerticalChange);
        Assert.Equal(16, seen.VerticalOffset);
    }

    [Fact]
    public void Original_handler_delegate_shape_binds_and_fires()
    {
        // ZoomBox.OnScrollViewerScrollChanged(object, ScrollChangedEventArgs):
        // converted code news up a ScrollChangedEventHandler over the method.
        var view = Harness(out var rows);
        var probe = new ZoomBoxScrollProbe();
        var method = typeof(ZoomBoxScrollProbe).GetMethod(nameof(ZoomBoxScrollProbe.OnScrollViewerScrollChanged))!;
        var handler = (ScrollChangedEventHandler)Delegate.CreateDelegate(typeof(ScrollChangedEventHandler), probe, method);
        view.ScrollChanged += handler;

        for (var i = 0; i < 5; i++) rows.AddChild(new Button { Content = "N" });
        view.Measure(new WpfSize(400, 100));
        view.Arrange(new WpfRect(0, 0, 400, 100));

        Assert.Equal(1, probe.Calls);
        Assert.Equal(480, probe.LastExtentHeight);
    }

    [Fact]
    public void Bubbles_to_ancestor_subscribed_via_add_handler()
    {
        var window = new Window();
        var view = Harness(out _);
        window.Content = view;
        var ancestorHits = 0;
        window.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => ancestorHits++));

        view.VerticalOffset = 60;

        Assert.Equal(1, ancestorHits);
    }

    [Fact]
    public void Xaml_scroll_changed_attribute_records_codebehind_wire()
    {
        const string xaml = """
            <Window Title="ScrollXaml" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <ScrollViewer ScrollChanged="OnScrollChanged">
                <StackPanel>
                  <TextBlock Text="Hi"/>
                </StackPanel>
              </ScrollViewer>
            </Window>
            """;
        var loaded = XamlLoader.Load(xaml);

        var wire = Assert.Single(loaded.EventWires);
        Assert.Equal("ScrollChanged", wire.EventName);
        Assert.Equal("OnScrollChanged", wire.HandlerName);
        Assert.IsType<ScrollViewer>(wire.Element);
    }

    private sealed class ZoomBoxScrollProbe
    {
        public int Calls;
        public double LastExtentHeight;

        // ScreenToGif Controls/ZoomBox.cs OnScrollViewerScrollChanged shape.
        public void OnScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (Math.Abs(e.ExtentHeightChange) < 0.01 && Math.Abs(e.ExtentWidthChange) < 0.01)
                return;
            Calls++;
            LastExtentHeight = e.ExtentHeight;
        }
    }
}
