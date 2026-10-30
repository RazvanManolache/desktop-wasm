namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Gallery MainWindow slice: the root Frame declares
/// Navigated="RootContentFrame_Navigated" and the handler refreshes
/// back-navigation state after every in-session page swap. Frame must
/// raise Navigated with NavigationEventArgs carrying the new content,
/// scoped to the navigating Frame instance (no journal involved).
/// </summary>
[ResetAmbientState]
public sealed class NavigationEventTests
{
    [Fact]
    public void Navigate_raises_navigated_with_content_args()
    {
        var frame = new Frame();
        var page = new Page { Title = "Dashboard" };
        object? sender = null;
        NavigationEventArgs? args = null;
        frame.Navigated += (s, e) => { sender = s; args = e; };

        Assert.True(frame.Navigate(page));

        Assert.Same(frame, sender);
        Assert.NotNull(args);
        Assert.Same(page, args.Content);
        Assert.Null(args.Uri);
        Assert.Null(args.ExtraData);
    }

    [Fact]
    public void Navigated_fires_before_content_rendered()
    {
        var frame = new Frame();
        var order = new List<string>();
        frame.Navigated += (_, _) => order.Add("navigated");
        frame.ContentRendered += (_, _) => order.Add("rendered");

        frame.Navigate(new Page());

        Assert.Equal(["navigated", "rendered"], order);
    }

    [Fact]
    public void Navigated_is_scoped_to_the_navigating_frame()
    {
        var left = new Frame();
        var right = new Frame();
        var leftHits = 0;
        var rightHits = 0;
        NavigationEventArgs? rightArgs = null;
        left.Navigated += (_, _) => leftHits++;
        right.Navigated += (_, e) => { rightHits++; rightArgs = e; };

        var page = new Page { Title = "Colors" };
        left.Navigate(new Page { Title = "Other" });
        Assert.Equal(1, leftHits);
        Assert.Equal(0, rightHits);

        right.Navigate(page);
        Assert.Equal(1, leftHits);
        Assert.Equal(1, rightHits);
        Assert.Same(page, rightArgs!.Content);
    }

    [Fact]
    public void Removed_handler_stays_silent()
    {
        var frame = new Frame();
        var hits = 0;
        void OnNavigated(object sender, NavigationEventArgs e) => hits++;
        frame.Navigated += OnNavigated;
        frame.Navigated -= OnNavigated;

        frame.Navigate(new Page());

        Assert.Equal(0, hits);
    }

    [Fact]
    public void Failed_navigate_raises_neither_navigated_nor_rendered()
    {
        var frame = new Frame();
        var navigated = 0;
        var rendered = 0;
        frame.Navigated += (_, _) => navigated++;
        frame.ContentRendered += (_, _) => rendered++;

        Assert.Throws<InvalidOperationException>(() => frame.Navigate("nope"));

        Assert.Equal(0, navigated);
        Assert.Equal(0, rendered);
        Assert.Null(frame.Content);
    }

    [Fact]
    public void Navigation_event_args_carry_uri_and_extra_data()
    {
        var page = new Page { Title = "FromUri" };
        var uri = new Uri("Page1.xaml", UriKind.Relative);
        var args = new NavigationEventArgs(page, uri, extraData: "state");

        Assert.Same(page, args.Content);
        Assert.Equal(uri, args.Uri);
        Assert.Equal("state", args.ExtraData);
    }

    [Fact]
    public void Gallery_navigated_handler_shape_binds_to_event()
    {
        // MainWindow.RootContentFrame_Navigated(object, NavigationEventArgs).
        var method = typeof(GalleryHandlerProbe).GetMethod(nameof(GalleryHandlerProbe.RootContentFrame_Navigated))!;
        var handler = (NavigatedEventHandler)Delegate.CreateDelegate(typeof(NavigatedEventHandler), new GalleryHandlerProbe(), method);

        var frame = new Frame();
        frame.Navigated += handler;
        frame.Navigate(new Page { Title = "Dashboard" });

        Assert.Equal(1, ((GalleryHandlerProbe)handler.Target!).Calls);
    }

    [Fact]
    public void Xaml_navigated_attribute_records_codebehind_wire()
    {
        const string xaml = """
            <Window Title="NavXaml" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Frame Navigated="OnFrameNavigated" NavigationUIVisibility="Hidden" />
            </Window>
            """;
        var loaded = XamlLoader.Load(xaml);

        var wire = Assert.Single(loaded.EventWires);
        Assert.Equal("Navigated", wire.EventName);
        Assert.Equal("OnFrameNavigated", wire.HandlerName);
        Assert.IsType<Frame>(wire.Element);
    }

    private sealed class GalleryHandlerProbe
    {
        public int Calls;
        public void RootContentFrame_Navigated(object sender, NavigationEventArgs e) => Calls++;
    }
}
