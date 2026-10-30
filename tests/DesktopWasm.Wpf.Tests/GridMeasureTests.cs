using DesktopWasm.Wpf;
using Xunit;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Grid never propagates infinity: definition-less grids size to
/// content under infinite constraints and star tracks size to content when
/// the available size is unbounded (gallery Dashboard scroll content).</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class GridMeasureTests
{
    [Fact]
    public void Definitionless_grid_sizes_to_content_under_infinity()
    {
        var grid = new Grid();
        grid.AddChild(new TextBlock { Text = "Hi" });
        grid.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        Assert.True(double.IsFinite(grid.DesiredSize.Width), $"width {grid.DesiredSize.Width}");
        Assert.True(double.IsFinite(grid.DesiredSize.Height), $"height {grid.DesiredSize.Height}");
    }

    [Fact]
    public void Unplaced_children_default_to_track_zero_in_definition_grids()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Pixel(56) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star() });
        var first = new TextBlock { Text = "A" };
        var second = new TextBlock { Text = "B" };
        second.SetAttached("Grid.Row", 1);
        grid.AddChild(first);
        grid.AddChild(second);
        grid.Measure(new WpfSize(200, 300));
        grid.Arrange(new WpfRect(0, 0, 200, 300));
        Assert.Equal(0, first.RenderRect.Y);
        Assert.Equal(56, second.RenderRect.Y);
    }

    [Fact]
    public void Implicit_tracks_measure_content_but_fill_at_arrange()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star() });
        var label = new TextBlock { Text = "Hi" };
        grid.AddChild(label);
        grid.Measure(new WpfSize(800, 600));
        Assert.True(double.IsFinite(grid.DesiredSize.Height), $"height {grid.DesiredSize.Height}");
        Assert.True(grid.DesiredSize.Height < 100, $"implicit row inflated to {grid.DesiredSize.Height}");
        grid.Arrange(new WpfRect(0, 0, 800, 600));
        Assert.Equal(600, label.RenderRect.Height);
    }

    [Fact]
    public void Star_tracks_size_to_content_under_infinite_available()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star() });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star() });
        grid.AddChild(new TextBlock { Text = "Hi" });
        grid.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        Assert.True(double.IsFinite(grid.DesiredSize.Width), $"width {grid.DesiredSize.Width}");
        Assert.True(double.IsFinite(grid.DesiredSize.Height), $"height {grid.DesiredSize.Height}");
    }
}
