using DesktopWasm.Wpf;
using Xunit;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Resolved resources assign directly when the target property
/// accepts the runtime type (gallery GitHubIconGeometry, page brushes).</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class ResourceApplyTests
{
    private const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    [Fact]
    public void StaticGeometryResourceAssignsDirectlyToPathData()
    {
        Application.Current = new Application();
        var result = XamlLoader.Load(
            $"<Grid {Ns}><Grid.Resources><Geometry x:Key=\"G\">M0,0 L10,10</Geometry></Grid.Resources>" +
            "<Path Data=\"{StaticResource G}\" /></Grid>");
        var grid = Assert.IsType<Grid>(result.Root);
        var path = Assert.IsType<Path>(grid.LogicalChildren[0]);
        Assert.IsType<PathGeometry>(path.Data);
    }

    [Fact]
    public void PageAcceptsBackgroundAndForegroundLiterals()
    {
        Application.Current = new Application();
        var result = XamlLoader.Load($"<Page {Ns} Background=\"Red\" Foreground=\"Blue\" />");
        var page = Assert.IsType<Page>(result.Root);
        Assert.IsType<SolidColorBrush>(page.Background);
        Assert.IsType<SolidColorBrush>(page.Foreground);
    }
}
