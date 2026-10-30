using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Gallery TileGallery/UserDashboard slice: responsive layouts subscribe to
/// FrameworkElement.SizeChanged and reflow when an arrange pass actually
/// changes the size. Same-size re-arranges stay silent; args carry the exact
/// previous/new sizes plus per-axis change flags.
/// </summary>
[ResetAmbientState]
public sealed class SizeChangedTests
{
    private static Window Harness(out Border tile, double width = 200, double height = 100)
    {
        var window = new Window { Title = "SizeLab" };
        tile = new Border { Width = width, Height = height };
        window.Content = tile;
        return window;
    }

    private static void Layout(Window window, double w, double h)
    {
        window.Measure(new WpfSize(w, h));
        window.Arrange(new WpfRect(0, 0, w, h));
    }

    [Fact]
    public void First_arrange_fires_with_zero_previous_size()
    {
        var window = Harness(out var tile);
        SizeChangedEventArgs? seen = null;
        object? sender = null;
        tile.SizeChanged += (s, e) => { sender = s; seen = e; };

        Layout(window, 800, 600);

        Assert.NotNull(seen);
        Assert.Same(tile, sender);
        Assert.Equal(new Size(0, 0), seen.PreviousSize);
        Assert.Equal(new Size(200, 100), seen.NewSize);
        Assert.True(seen.WidthChanged);
        Assert.True(seen.HeightChanged);
        Assert.Same(FrameworkElement.SizeChangedEvent, seen.RoutedEvent);
        Assert.Same(tile, seen.Source);
        Assert.Equal(new Size(200, 100), tile.RenderSize);
        Assert.Equal(200, tile.ActualWidth);
        Assert.Equal(100, tile.ActualHeight);
    }

    [Fact]
    public void Same_size_rearrange_stays_silent()
    {
        var window = Harness(out var tile);
        Layout(window, 800, 600);
        var fires = 0;
        tile.SizeChanged += (_, _) => fires++;

        Layout(window, 800, 600);

        Assert.Equal(0, fires);
    }

    [Fact]
    public void Arrange_size_change_carries_previous_and_new_sizes()
    {
        var window = Harness(out var tile);
        Layout(window, 800, 600);
        var seen = new List<SizeChangedEventArgs>();
        tile.SizeChanged += (_, e) => seen.Add(e);

        // Border keeps its explicit 200x100 while the window grows: the
        // tile must not fire; the window itself reports the delta.
        var windowSeen = new List<SizeChangedEventArgs>();
        window.SizeChanged += (_, e) => windowSeen.Add(e);
        Layout(window, 1024, 768);

        Assert.Empty(seen);
        var only = Assert.Single(windowSeen);
        Assert.Equal(new Size(800, 600), only.PreviousSize);
        Assert.Equal(new Size(1024, 768), only.NewSize);
        Assert.True(only.WidthChanged);
        Assert.True(only.HeightChanged);
    }

    [Fact]
    public void Width_only_change_sets_only_width_flag()
    {
        var tile = new Border { Width = 200, Height = 100 };
        tile.Measure(new WpfSize(800, 600));
        tile.Arrange(new WpfRect(0, 0, 200, 100));
        SizeChangedEventArgs? seen = null;
        tile.SizeChanged += (_, e) => seen = e;

        tile.Width = 320;
        tile.Measure(new WpfSize(800, 600));
        tile.Arrange(new WpfRect(0, 0, 320, 100));

        Assert.NotNull(seen);
        Assert.Equal(new Size(200, 100), seen.PreviousSize);
        Assert.Equal(new Size(320, 100), seen.NewSize);
        Assert.True(seen.WidthChanged);
        Assert.False(seen.HeightChanged);
    }

    [Fact]
    public void Nested_child_fires_during_parent_arrange()
    {
        var window = Harness(out var tile);
        var childFires = 0;
        var parentFires = 0;
        tile.SizeChanged += (_, _) => childFires++;
        window.SizeChanged += (_, _) => parentFires++;
        Layout(window, 800, 600);

        Assert.Equal(1, childFires);
        Assert.Equal(1, parentFires);
    }

    [Fact]
    public void Xaml_size_changed_attribute_records_codebehind_wire()
    {
        const string xaml = """
            <Window Title="SizeXaml">
              <Border Width="200" Height="100" SizeChanged="OnTileSize"/>
            </Window>
            """;
        var loaded = XamlLoader.Load(xaml);

        var wire = Assert.Single(loaded.EventWires);
        Assert.Equal("SizeChanged", wire.EventName);
        Assert.Equal("OnTileSize", wire.HandlerName);
        Assert.IsType<Border>(wire.Element);
    }

    [Fact]
    public void Size_change_keeps_layout_flags_consistent()
    {
        var window = Harness(out var tile);
        Layout(window, 800, 600);
        window.ClearInvalidation();

        Layout(window, 1024, 768);

        Assert.False(window.IsArrangeDirty);
        Assert.Equal(1024, window.ActualWidth);
        Assert.Equal(768, window.ActualHeight);
    }
}
