using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Gallery GridPage slice (slice-34): the first Layout example is a 3x3 grid
/// with ShowGridLines="True", which the loader used to reject outright
/// ("keep False"), so the real staged GridPage.xaml could not inflate.
/// The engine now carries Grid.ShowGridLines (default false) and paints the
/// WPF dotted row/column-boundary adornment as 1px black DrawLine dashes
/// (2px on / 2px off), which every renderer replays with no protocol change.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class GridShowGridLinesTests
{
    private static System.Reflection.Assembly LoadConvertedGallery()
    {
        var dir = FindGalleryDir();
        var manifest = System.IO.Path.Combine(dir, "app-manifest.json");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifest));
        string? file = null;
        foreach (var asm in doc.RootElement.GetProperty("assemblies").EnumerateArray())
        {
            if (asm.GetProperty("name").GetString() == "WPFGallery")
                file = asm.GetProperty("file").GetString();
        }
        Assert.NotNull(file);
        var bytes = File.ReadAllBytes(System.IO.Path.Combine(dir, file!));
        return System.Reflection.Assembly.Load(bytes);
    }

    private static string FindGalleryDir()
    {
        var dir = AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
        {
            var candidate = System.IO.Path.Combine(d.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "wpf-gallery");
            if (File.Exists(System.IO.Path.Combine(candidate, "app-manifest.json"))) return candidate;
        }
        throw new InvalidOperationException("staged wpf-gallery package not found");
    }

    private static T? FindVisual<T>(FrameworkElement? root) where T : FrameworkElement
    {
        if (root is null) return null;
        if (root is T match) return match;
        foreach (var child in root.LogicalChildren)
        {
            var found = FindVisual<T>(child as FrameworkElement);
            if (found is not null) return found;
        }
        return null;
    }

    private static List<T> FindVisuals<T>(FrameworkElement? root) where T : FrameworkElement
    {
        var found = new List<T>();
        Collect(root, found);
        return found;
    }

    private static void Collect<T>(FrameworkElement? root, List<T> found) where T : FrameworkElement
    {
        if (root is null) return;
        if (root is T match) found.Add(match);
        foreach (var child in root.LogicalChildren) Collect(child as FrameworkElement, found);
    }

    private static FrameworkElement InflateRealGridPage()
    {
        var asm = LoadConvertedGallery();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("Layout/GridPage.xaml", StringComparison.Ordinal));
        Assert.NotNull(name);
        using var stream = asm.GetManifestResourceStream(name!);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var previous = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = asm;
        try
        {
            return (Assert.IsType<Page>(XamlLoader.Load(reader.ReadToEnd()).Root) as FrameworkElement)!;
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
        }
    }

    [Fact]
    public void Real_GridPage_xaml_inflates_with_show_grid_lines()
    {
        var root = InflateRealGridPage();
        var guided = FindVisuals<Grid>(root).FirstOrDefault(g => g.ShowGridLines);
        Assert.NotNull(guided);
        Assert.Equal(3, guided!.RowDefinitions.Count);
        Assert.Equal(3, guided.ColumnDefinitions.Count);
        var cells = FindVisuals<TextBlock>(guided);
        Assert.Equal(9, cells.Count);
        Assert.Contains(cells, t => t.Text == "Cell 1");
        Assert.Contains(cells, t => t.Text == "Cell 9");
    }

    [Fact]
    public void Real_GridPage_other_examples_still_inflate()
    {
        var root = InflateRealGridPage();
        // Custom sizing + spanning grid and the shorthand-syntax grid.
        var spanning = FindVisuals<TextBlock>(root)
            .FirstOrDefault(t => t.Text == "Row 1, Spans all columns");
        Assert.NotNull(spanning);
        var shorthand = FindVisuals<TextBlock>(root)
            .FirstOrDefault(t => t.Text == "Header (100px)");
        Assert.NotNull(shorthand);
        var grids = FindVisuals<Grid>(root);
        Assert.True(grids.Count >= 4);
        // Exactly one example opts into the debug guides.
        Assert.Single(grids, g => g.ShowGridLines);
    }

    [Fact]
    public void ShowGridLines_defaults_false_and_parses_both_ways()
    {
        Assert.False(new Grid().ShowGridLines);
        var on = (Grid)XamlLoader.Load(
            """<Grid ShowGridLines="True" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" />""").Root;
        Assert.True(on.ShowGridLines);
        var off = (Grid)XamlLoader.Load(
            """<Grid ShowGridLines="False" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" />""").Root;
        Assert.False(off.ShowGridLines);
    }

    [Fact]
    public void ShowGridLines_paints_dotted_lines_on_every_boundary()
    {
        var grid = new Grid { ShowGridLines = true };
        for (var i = 0; i < 3; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star() });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star() });
        }
        var window = new Window { Content = grid };
        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(300, 300));
        var lines = commands.OfType<DrawCommand.DrawLine>().ToList();
        // 4 vertical + 4 horizontal boundaries on a 300px grid with
        // 2px dashes every 4px: 75 dashes per boundary, 600 total.
        Assert.Equal(600, lines.Count);
        foreach (var expected in new[] { 0f, 100f, 200f, 300f })
        {
            Assert.Contains(lines, l => l.X1 == expected && l.X2 == expected && l.Y1 == 0 && l.Width == 1);
            Assert.Contains(lines, l => l.Y1 == expected && l.Y2 == expected && l.X1 == 0 && l.Width == 1);
        }
        // Dotted, not solid: every dash is short with gaps between dashes.
        Assert.All(lines, l => Assert.True(
            Math.Abs(l.X2 - l.X1) <= 2 && Math.Abs(l.Y2 - l.Y1) <= 2,
            $"expected a 2px dash, got ({l.X1},{l.Y1})-({l.X2},{l.Y2})"));
    }

    [Fact]
    public void ShowGridLines_off_paints_no_guides()
    {
        var grid = new Grid();
        for (var i = 0; i < 3; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star() });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star() });
        }
        var window = new Window { Content = grid };
        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(300, 300));
        Assert.Empty(commands.OfType<DrawCommand.DrawLine>());
    }

    [Fact]
    public void ShowGridLines_covers_uniform_rows_cols_grids()
    {
        var grid = new Grid { Rows = 2, Cols = 2, ShowGridLines = true };
        grid.AddChild(new Button { Content = "a" });
        grid.AddChild(new Button { Content = "b" });
        var window = new Window { Content = grid };
        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(200, 200));
        var lines = commands.OfType<DrawCommand.DrawLine>().ToList();
        // 3 vertical + 3 horizontal boundaries on a 200px grid: 50 dashes each.
        Assert.Equal(300, lines.Count);
        foreach (var expected in new[] { 0f, 100f, 200f })
        {
            Assert.Contains(lines, l => l.X1 == expected && l.X2 == expected);
            Assert.Contains(lines, l => l.Y1 == expected && l.Y2 == expected);
        }
    }
}
