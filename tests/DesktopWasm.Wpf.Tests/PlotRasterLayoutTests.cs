using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Core;
using DesktopWasm.Wpf;

namespace DesktopWasm.Wpf.Tests;

// Slice-53 regression: the converted OxyPlot ExampleBrowser plot tab hosts
// its three PlotViews (oxySkia, oxyWpf, local XamlPlotView) in a single
// definition-less <Grid> (no RowDefinitions/ColumnDefinitions). Real WPF
// overlaps same-cell children, but the engine's definition-less arrange path
// placed children by index (row = i / cols), so the second and third hosts
// arranged a full cell below the first (y = 692 / 1384 at the staged size)
// and every proof screenshot was a byte-identical list-only bitmap.
// The fix clamps definition-less arrange cells (WPF parity), keeping legacy
// by-index placement for in-range uniform tables.
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class PlotRasterLayoutTests : IDisposable
{
    private readonly Application? _previous = Application.Current;
    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    [Fact]
    public void Definitionless_single_cell_grid_overlaps_children()
    {
        var grid = new Grid();
        var first = new TextBlock { Text = "A" };
        var second = new TextBlock { Text = "B" };
        var third = new TextBlock { Text = "C" };
        grid.AddChild(first);
        grid.AddChild(second);
        grid.AddChild(third);
        grid.Measure(new WpfSize(800, 600));
        grid.Arrange(new WpfRect(0, 0, 800, 600));
        Assert.Equal(first.RenderRect.X, second.RenderRect.X);
        Assert.Equal(first.RenderRect.Y, second.RenderRect.Y);
        Assert.Equal(first.RenderRect.Width, second.RenderRect.Width);
        Assert.Equal(first.RenderRect.Height, second.RenderRect.Height);
        Assert.Equal(first.RenderRect.Y, third.RenderRect.Y);
        Assert.Equal(0, first.RenderRect.Y);
        Assert.Equal(800, first.RenderRect.Width);
        Assert.Equal(600, first.RenderRect.Height);
    }

    [Fact]
    public void Definitionless_grid_clamps_explicit_row_into_the_single_cell()
    {
        var grid = new Grid();
        var child = new TextBlock { Text = "A" };
        child.SetAttached("Grid.Row", 5);
        child.SetAttached("Grid.Column", 3);
        grid.AddChild(child);
        grid.Measure(new WpfSize(800, 600));
        grid.Arrange(new WpfRect(0, 0, 800, 600));
        Assert.Equal(0, child.RenderRect.Y);
        Assert.Equal(0, child.RenderRect.X);
        Assert.Equal(800, child.RenderRect.Width);
        Assert.Equal(600, child.RenderRect.Height);
    }

    [Fact]
    public void Legacy_uniform_table_keeps_by_index_placement()
    {
        var grid = new Grid { Rows = 2, Cols = 2 };
        var cells = new TextBlock[4];
        for (var i = 0; i < 4; i++)
        {
            cells[i] = new TextBlock { Text = "C" + i };
            grid.AddChild(cells[i]);
        }
        grid.Measure(new WpfSize(200, 200));
        grid.Arrange(new WpfRect(0, 0, 200, 200));
        Assert.Equal(0, cells[0].RenderRect.X);
        Assert.Equal(0, cells[0].RenderRect.Y);
        Assert.Equal(100, cells[1].RenderRect.X);
        Assert.Equal(0, cells[1].RenderRect.Y);
        Assert.Equal(0, cells[2].RenderRect.X);
        Assert.Equal(100, cells[2].RenderRect.Y);
        Assert.Equal(100, cells[3].RenderRect.X);
        Assert.Equal(100, cells[3].RenderRect.Y);
    }

    [Fact]
    public void Staged_plot_hosts_overlap_inside_the_scene_with_real_series_ink()
    {
        WasmDispatcher.Current = new WasmDispatcher(new WasmDispatcherPump());
        var entry = StagedOxyPlot.Load();
        Application.Current = null;
        var window = WpfAppBoot.Boot(entry);
        window.Measure(new WpfSize(800, 600));
        window.Arrange(new WpfRect(0, 0, 800, 600));
        var list = Descendants(window).OfType<ListBox>().Single();
        Descendants(window).OfType<Expander>().First().IsExpanded = true;
        list.SelectedIndex = 0;
        window.Measure(new WpfSize(800, 600));
        window.Arrange(new WpfRect(0, 0, 800, 600));

        var oxyHost = (FrameworkElement)FindByFullName(window, "OxyPlot.Wpf.PlotView")!;
        var xamlHost = (FrameworkElement)FindByFullName(window, "ExampleBrowser.XamlPlotView")!;
        var shim = Descendants(window).OfType<DesktopWasm.Wpf.PlotView>().Single();
        Assert.NotNull(oxyHost);
        Assert.NotNull(xamlHost);

        // Same cell: every host shares one rect (the pre-fix layout stacked
        // the second and third hosts at y = 692 / 1384, below the scene).
        Assert.Equal(oxyHost.RenderRect.X, xamlHost.RenderRect.X);
        Assert.Equal(oxyHost.RenderRect.Y, xamlHost.RenderRect.Y);
        Assert.Equal(oxyHost.RenderRect.Width, xamlHost.RenderRect.Width);
        Assert.Equal(oxyHost.RenderRect.Height, xamlHost.RenderRect.Height);
        Assert.Equal(oxyHost.RenderRect.Y, shim.RenderRect.Y);
        Assert.Equal(oxyHost.RenderRect.Height, shim.RenderRect.Height);

        // The visible Canvas host intersects the 800x600 scene with a
        // non-trivial visible slice (slice-54 confines the 1280x720
        // original window to the scene; the pre-slice-53 layout instead
        // stacked the live hosts fully below it at y = 692 / 1384).
        Assert.Equal(Visibility.Visible, oxyHost.Visibility);
        var plot = oxyHost.RenderRect;
        Assert.True(plot.Y >= 0, $"plot y {plot.Y}");
        Assert.True(plot.X < 800 && plot.Y < 600, $"plot origin {plot.X},{plot.Y}");
        var visibleWidth = Math.Min(plot.X + plot.Width, 800) - Math.Max(plot.X, 0);
        var visibleHeight = Math.Min(plot.Y + plot.Height, 600) - Math.Max(plot.Y, 0);
        Assert.True(visibleWidth > 100, $"visible plot width {visibleWidth}");
        Assert.True(visibleHeight > 100, $"visible plot height {visibleHeight}");

        // Real series ink (not fabricated pixels): the converted
        // XamlRenderContext children carry paths and the example title.
        var canvas = oxyHost.GetType().GetProperty("Canvas",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy)
            ?.GetValue(oxyHost);
        Assert.NotNull(canvas);
        var children = ((System.Collections.IEnumerable)canvas!.GetType()
            .GetProperty("Children")!.GetValue(canvas)!).Cast<object>().ToList();
        Assert.NotEmpty(children.OfType<DesktopWasm.Wpf.Path>());
        Assert.Contains(children.OfType<TextBlock>().Select(t => t.Text ?? ""),
            t => t.Contains("Normal distribution", StringComparison.Ordinal));
    }

    private static List<object> Descendants(object? node)
    {
        var found = new List<object>();
        void Collect(object? current)
        {
            if (current is null) return;
            found.Add(current);
            if (current is FrameworkElement fe)
                foreach (var child in fe.LogicalChildren) Collect(child);
        }
        Collect(node);
        return found;
    }

    private static object? FindByFullName(object? node, string fullName)
    {
        if (node?.GetType().FullName == fullName) return node;
        if (node is FrameworkElement fe)
            foreach (var child in fe.LogicalChildren)
            {
                var hit = FindByFullName(child, fullName);
                if (hit is not null) return hit;
            }
        return null;
    }

    private static class StagedOxyPlot
    {
        private static Assembly? _entry;
        private static readonly object Gate = new();

        public static Assembly Load()
        {
            lock (Gate)
            {
                var hit = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "ExampleBrowser.WPF",
                        StringComparison.OrdinalIgnoreCase));
                if (hit is not null) return hit;
                if (_entry is not null) return _entry;
                var images = ReadStaged().ToDictionary(e => e.name, e => e.bytes,
                    StringComparer.OrdinalIgnoreCase);
                AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
                {
                    var requested = new AssemblyName(args.Name).Name;
                    var loaded = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => string.Equals(a.GetName().Name, requested,
                            StringComparison.OrdinalIgnoreCase));
                    if (loaded is not null) return loaded;
                    if (requested is not null && images.TryGetValue(requested, out var image))
                        return Assembly.Load(image);
                    return null;
                };
                foreach (var image in images.Values) Assembly.Load(image);
                _entry = AppDomain.CurrentDomain.GetAssemblies().First(a =>
                    string.Equals(a.GetName().Name, "ExampleBrowser.WPF",
                        StringComparison.OrdinalIgnoreCase));
                return _entry;
            }
        }

        private static List<(string name, string file, byte[] bytes, string expected)> ReadStaged()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(System.IO.Path.Combine(
                directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot",
                "apps", "oxyplot", "app-manifest.json")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            var apps = System.IO.Path.Combine(directory!.FullName, "src",
                "DesktopWasm.BrowserHost", "wwwroot", "apps", "oxyplot");
            using var document = JsonDocument.Parse(File.ReadAllText(
                System.IO.Path.Combine(apps, "app-manifest.json")));
            var entries = new List<(string, string, byte[], string)>();
            foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
            {
                var file = assembly.GetProperty("file").GetString()!;
                var expected = assembly.GetProperty("sha256").GetString()!;
                var bytes = File.ReadAllBytes(System.IO.Path.Combine(apps, file));
                Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(bytes)), ignoreCase: true);
                entries.Add((assembly.GetProperty("name").GetString()!, file, bytes, expected));
            }
            return entries;
        }
    }
}
