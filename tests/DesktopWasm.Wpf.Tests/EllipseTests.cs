using DesktopWasm.Graphics;
using SkiaSharp;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Gallery TileGallery/UserDashboard avatar slice: Shapes.Ellipse renders
/// fill/stroke through the retained DrawEllipse op (JSON op 6 for Canvas2D,
/// Skia oval on the native renderer) and hit-tests the true ellipse, not
/// its bounding box. Pixel assertions run against both transports below.
/// </summary>
[ResetAmbientState]
public sealed class EllipseTests
{
    private const string EllipseXaml = """
        <Window Title="AvatarLab">
          <Ellipse Width="100" Height="60" Fill="Red" Stroke="Black" StrokeThickness="6"/>
        </Window>
        """;

    private static (Window Window, Ellipse Avatar) Harness()
    {
        var window = (Window)XamlLoader.Load(EllipseXaml).Root;
        var avatar = Assert.IsType<Ellipse>(window.Content);
        return (window, avatar);
    }

    [Fact]
    public void Ellipse_xaml_loads_fill_stroke_and_layout_slot()
    {
        var (_, avatar) = Harness();

        Assert.NotNull(avatar.Fill);
        Assert.NotNull(avatar.Stroke);
        Assert.Equal(6, avatar.StrokeThickness);
        Assert.Equal(100, avatar.Width);
        Assert.Equal(60, avatar.Height);

        var commands = WasmMilCore.RenderHeadless((Window)XamlLoader.Load(EllipseXaml).Root, new WpfSize(800, 600));
        var draw = Assert.IsType<DrawCommand.DrawEllipse>(Assert.Single(commands, c => c is DrawCommand.DrawEllipse));
        // Explicit 100x60 centered in the 800-wide window, top-aligned.
        Assert.Equal(350, draw.X, precision: 3);
        Assert.Equal(0, draw.Y, precision: 3);
        Assert.Equal(100, draw.W, precision: 3);
        Assert.Equal(60, draw.H, precision: 3);
        Assert.Equal(6, draw.LineWidth, precision: 3);
        Assert.NotNull(draw.FillRgba);
        Assert.NotEqual(0u, draw.OutlineRgba);
    }

    [Fact]
    public void Ellipse_skia_pixels_show_fill_center_and_stroke_edge()
    {
        var (window, _) = Harness();
        using var surface = new SkiaSurface(800, 600);
        var mil = new WasmMilCore(surface);
        var commands = mil.Render(window, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));
        mil.CommitChannel(commands);

        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        var center = bitmap.GetPixel(400, 30);
        Assert.True(center.Red > 200 && center.Green < 80 && center.Blue < 80, $"center {center} is not the red fill");
        var edge = bitmap.GetPixel(351, 30);
        Assert.True(edge.Red < 80 && edge.Green < 80 && edge.Blue < 80, $"edge {edge} is not the black stroke ring");
        var corner = bitmap.GetPixel(351, 1);
        Assert.True(corner.Red > 200 && corner.Green > 200 && corner.Blue > 200, $"corner {corner} is not background (ellipse overpaints its box)");
    }

    [Fact]
    public void Ellipse_canvas2d_json_carries_op6_with_fill_and_outline()
    {
        var (window, _) = Harness();
        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(800, 600));
        var json = WasmMilCore.ExportFrameJson(commands);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var ellipseOps = doc.RootElement.EnumerateArray()
            .Where(op => op[0].GetInt32() == 6)
            .ToList();
        var op = Assert.Single(ellipseOps);
        Assert.Equal(350, op[1].GetDouble(), precision: 3);
        Assert.Equal(0, op[2].GetDouble(), precision: 3);
        Assert.Equal(100, op[3].GetDouble(), precision: 3);
        Assert.Equal(60, op[4].GetDouble(), precision: 3);
        Assert.Equal(6, op[5].GetDouble(), precision: 3);
        Assert.Equal("#000000ff", op[6].GetString());
        Assert.Equal("#ff0000ff", op[7].GetString());
    }

    [Fact]
    public void Ellipse_hit_tests_true_shape_not_bounding_box()
    {
        var (window, avatar) = Harness();
        WasmMilCore.RenderHeadless(window, new WpfSize(800, 600));

        Assert.True(avatar.HitTest(new WpfPoint(400, 30))); // center
        Assert.True(avatar.HitTest(new WpfPoint(353, 30))); // stroke ring
        Assert.False(avatar.HitTest(new WpfPoint(351, 1))); // box corner outside the oval
        Assert.False(avatar.HitTest(new WpfPoint(100, 100))); // outside entirely
    }

    [Fact]
    public void Ellipse_without_stroke_paints_fill_only_op()
    {
        var avatar = new Ellipse { Width = 40, Height = 40, Fill = Brushes.Blue };
        var window = new Window { Content = avatar };
        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(800, 600));

        var draw = Assert.IsType<DrawCommand.DrawEllipse>(Assert.Single(commands, c => c is DrawCommand.DrawEllipse));
        Assert.Equal(0, draw.LineWidth, precision: 3);
        Assert.NotNull(draw.FillRgba);
    }

    [Fact]
    public void Ellipse_zero_size_draws_nothing()
    {
        var avatar = new Ellipse { Width = 0, Height = 0, Fill = Brushes.Red };
        var window = new Window { Content = avatar };
        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(800, 600));

        Assert.Empty(commands.OfType<DrawCommand.DrawEllipse>());
    }

    [Fact]
    public void Size_ellipse_demo_renders_avatars_and_readout_follows_resize()
    {
        var window = DesktopWasm.BrowserHost.DemoCatalog.Find("size-ellipse").Build();
        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(800, 600));
        var json = WasmMilCore.ExportFrameJson(commands);

        // Three avatar ellipses paint; the readout shows the initial tile size.
        Assert.Equal(3, commands.OfType<DrawCommand.DrawEllipse>().Count());
        Assert.Contains("Tiles: 220x120", json, StringComparison.Ordinal);

        var tiles = Descendants(window).OfType<Border>().First(b => b.Width == 220);
        tiles.Width = 340;
        tiles.Height = 160;
        var resized = WasmMilCore.RenderHeadless(window, new WpfSize(800, 600));
        Assert.Contains("Tiles: 340x160", WasmMilCore.ExportFrameJson(resized), StringComparison.Ordinal);
    }

    private static IEnumerable<UIElement> Descendants(UIElement root)
    {
        yield return root;
        if (root is FrameworkElement fe)
            foreach (var child in fe.LogicalChildren)
                foreach (var inner in Descendants(child))
                    yield return inner;
    }
}
