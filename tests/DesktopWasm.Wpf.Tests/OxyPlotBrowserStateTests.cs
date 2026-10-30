using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Graphics;
using DesktopWasm.Wpf;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-51 regression: the STATE contract the OxyPlot browser proof asserts
/// (benchmarks/oxyplot_proof.py) holds engine-side at the 800x600 browser
/// scene size. The staged ExampleBrowser boots with 754 rows across 71
/// collapsed groups; expanding the first group gives the first row ('Normal
/// distribution') a clickable rect; selecting it sets SelectedExample,
/// populates CanvasModel, and the converted oxyWpf PlotView carries the same
/// PlotModel instance with real series ink (paths + tick labels) in its
/// presenter canvas while the model-less XamlPlotView stays honestly empty.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class OxyPlotBrowserStateTests : IDisposable
{
    private readonly Application? _previous = Application.Current;
    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    [Fact]
    public void Staged_boot_has_754_rows_across_71_collapsed_groups()
    {
        Application.Current = null;
        var window = WpfAppBoot.Boot(StagedOxyPlot.Load());
        var list = Descendants(window).OfType<ListBox>().Single();
        Assert.Equal(754, list.Items.Count);
        Assert.Equal(71, list.LogicalChildren.OfType<GroupItem>().Count());
        Assert.Equal("Normal distribution", list.Items[0].Content);
        var firstGroup = list.LogicalChildren.OfType<GroupItem>().First();
        var expander = Descendants(firstGroup).OfType<Expander>().Single();
        Assert.False(expander.IsExpanded);
        Assert.Equal(new WpfRect(0, 0, 0, 0), list.Items[0].RenderRect);
    }

    [Fact]
    public void Expanding_first_group_selects_row_zero_into_canvas_model()
    {
        Application.Current = null;
        var window = WpfAppBoot.Boot(StagedOxyPlot.Load());
        window.Measure(new WpfSize(800, 600));
        window.Arrange(new WpfRect(0, 0, 800, 600));
        var list = Descendants(window).OfType<ListBox>().Single();
        var firstGroup = list.LogicalChildren.OfType<GroupItem>().First();
        var expander = Descendants(firstGroup).OfType<Expander>().Single();
        expander.IsExpanded = true;
        window.Measure(new WpfSize(800, 600));
        window.Arrange(new WpfRect(0, 0, 800, 600));

        var row = list.Items[0].RenderRect;
        Assert.True(row.Width > 0 && row.Height > 0);

        list.SelectedIndex = 0;
        var vm = window.DataContext!;
        var selected = vm.GetType().GetProperty("SelectedExample")?.GetValue(vm);
        Assert.NotNull(selected);
        Assert.Equal("Normal distribution", selected!.GetType().GetProperty("Title")?.GetValue(selected));
        var canvas = vm.GetType().GetProperty("CanvasModel")?.GetValue(vm);
        Assert.NotNull(canvas);

        var host = Descendants(window).OfType<FrameworkElement>()
            .Single(f => f.GetType().FullName == "OxyPlot.Wpf.PlotView");
        Assert.Same(canvas, host.GetType().GetProperty("Model")?.GetValue(host));
        Assert.Equal(Visibility.Visible, host.Visibility);
    }

    [Fact]
    public void Selected_plot_carries_series_ink_and_xaml_host_stays_empty()
    {
        Application.Current = null;
        var window = WpfAppBoot.Boot(StagedOxyPlot.Load());
        var list = Descendants(window).OfType<ListBox>().Single();
        var firstGroup = list.LogicalChildren.OfType<GroupItem>().First();
        Descendants(firstGroup).OfType<Expander>().Single().IsExpanded = true;
        list.SelectedIndex = 0;
        window.Measure(new WpfSize(800, 600));
        window.Arrange(new WpfRect(0, 0, 800, 600));

        var host = Descendants(window).OfType<FrameworkElement>()
            .Single(f => f.GetType().FullName == "OxyPlot.Wpf.PlotView");
        var canvas = CanvasOf(host);
        Assert.NotNull(canvas);
        var children = ((System.Collections.IEnumerable)canvas!.GetType()
            .GetProperty("Children")!.GetValue(canvas)!).Cast<object>().ToList();
        var paths = children.OfType<DesktopWasm.Wpf.Path>().ToList();
        var labels = children.OfType<TextBlock>()
            .Where(t => !string.IsNullOrEmpty(t.Text)).Select(t => t.Text!).ToList();
        Assert.NotEmpty(paths);
        Assert.All(paths, p => Assert.NotNull(p.Data));
        Assert.Contains("Normal distribution", labels);

        var frame = WasmMilCore.RenderHeadless(window, new WpfSize(800, 600));
        Assert.NotEmpty(frame);
        Assert.Contains(frame, c => c is DrawCommand.DrawText);

        var xamlHost = Descendants(window).OfType<FrameworkElement>()
            .Single(f => f.GetType().FullName == "ExampleBrowser.XamlPlotView");
        Assert.Null(xamlHost.GetType().GetProperty("Model")?.GetValue(xamlHost));
        Assert.Equal(0, ChildCount(CanvasOf(xamlHost)));
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

    private static object? CanvasOf(object plotView) =>
        plotView.GetType().GetProperty("Canvas",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy)
            ?.GetValue(plotView);

    private static int ChildCount(object? canvas) =>
        canvas is null ? -1 : ((System.Collections.IEnumerable)canvas.GetType()
            .GetProperty("Children")!.GetValue(canvas)!).Cast<object>().Count();

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
