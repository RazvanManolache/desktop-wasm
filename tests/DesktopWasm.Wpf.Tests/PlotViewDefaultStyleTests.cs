using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Graphics;
using DesktopWasm.Wpf;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-50 regression: the converted OxyPlot default style/template is real
/// behavior. The staged OxyPlot.Wpf.Shared Themes/Generic.xaml implicit style
/// resolves by DefaultStyleKey (with base-type walk), OnApplyTemplate finds
/// PART_Grid, and PlotViewBase.Render() proceeds past the early return so the
/// converted model draws real series ink (StreamGeometry paths, axis labels)
/// into the presenter canvas. A model-less view keeps its template but stays
/// honestly empty.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class PlotViewDefaultStyleTests : IDisposable
{
    private readonly Application? _previous = Application.Current;
    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    [Fact]
    public void Staged_oxyplot_manifest_hashes_verify()
    {
        var failures = StagedOxyPlotLoader.VerifyHashes();
        Assert.Empty(failures);
    }

    [Fact]
    public void PlotView_applies_converted_default_style_and_finds_part_grid()
    {
        var plotView = BootAndFindPlotView("OxyPlot.Wpf.PlotView");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var grid = FindField(plotView, "grid");
        var presenter = FindField(plotView, "plotPresenter");
        var ctx = FindField(plotView, "renderContext");
        Assert.NotNull(grid);
        Assert.Equal("Grid", grid!.GetType().Name);
        Assert.NotNull(presenter);
        Assert.NotNull(ctx);
        Assert.NotNull(((Control)plotView).Template);
        Assert.Equal("OxyPlot.Wpf.PlotViewBase", plotView.GetType().GetProperty("DefaultStyleKey", flags)?.GetValue(plotView)?.ToString());
        var templateRoot = typeof(Control).GetProperty("TemplateRoot", flags)?.GetValue(plotView);
        Assert.NotNull(templateRoot);
    }

    [Fact]
    public void PlotView_renders_real_series_ink_into_presenter_canvas()
    {
        var entry = StagedOxyPlotLoader.Load();
        Application.Current = null;
        var window = WpfAppBoot.Boot(entry);
        SelectFirst(window);
        var plotView = (FrameworkElement)FindByFullName(window, "OxyPlot.Wpf.PlotView")!;
        Assert.NotNull(plotView);
        window.Measure(new WpfSize(1280, 720));
        window.Arrange(new WpfRect(0, 0, 1280, 720));
        var frame = WasmMilCore.RenderHeadless(window, new WpfSize(1280, 720));
        Assert.NotEmpty(frame);

        var model = plotView.GetType().GetProperty("Model")?.GetValue(plotView);
        Assert.NotNull(model);
        var lastEx = model!.GetType().GetField("lastPlotException", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(model) as Exception;
        Assert.Null(lastEx);

        var canvas = CanvasOf(plotView);
        Assert.NotNull(canvas);
        var children = ((System.Collections.IEnumerable)canvas!.GetType().GetProperty("Children")!.GetValue(canvas)!).Cast<object>().ToList();
        var paths = children.OfType<DesktopWasm.Wpf.Path>().ToList();
        var labels = children.OfType<TextBlock>().Where(t => !string.IsNullOrEmpty(t.Text)).ToList();
        Assert.NotEmpty(paths);
        Assert.All(paths, p => Assert.NotNull(p.Data));
        Assert.NotEmpty(labels);
    }

    [Fact]
    public void Model_less_plot_view_keeps_template_but_stays_honestly_empty()
    {
        var plotView = BootAndFindPlotView("ExampleBrowser.XamlPlotView");
        Assert.NotNull(((Control)plotView).Template);
        Assert.NotNull(FindField(plotView, "grid"));
        var model = plotView.GetType().GetProperty("Model")?.GetValue(plotView);
        Assert.Null(model);
        Assert.Equal(0, ChildCount(CanvasOf(plotView)));
    }

    private static FrameworkElement BootAndFindPlotView(string fullName)
    {
        var entry = StagedOxyPlotLoader.Load();
        Application.Current = null;
        var window = WpfAppBoot.Boot(entry);
        SelectFirst(window);
        var found = FindByFullName(window, fullName);
        Assert.NotNull(found);
        return (FrameworkElement)found!;
    }

    private static void SelectFirst(Window window)
    {
        var lists = new List<ListBox>();
        void Collect(object? node)
        {
            if (node is ListBox lb) lists.Add(lb);
            if (node is FrameworkElement fe)
                foreach (var child in fe.LogicalChildren) Collect(child);
        }
        Collect(window);
        lists.Single().SelectedIndex = 0;
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

    private static object? FindField(object target, string name)
    {
        for (var bt = target.GetType(); bt is not null; bt = bt.BaseType)
        {
            var value = bt.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)?.GetValue(target);
            if (value is not null) return value;
        }
        return null;
    }

    private static object? CanvasOf(object plotView) =>
        plotView.GetType().GetProperty("Canvas", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy)?.GetValue(plotView);

    private static int ChildCount(object? canvas) =>
        canvas is null ? -1 : ((System.Collections.IEnumerable)canvas.GetType().GetProperty("Children")!.GetValue(canvas)!).Cast<object>().Count();

    private static class StagedOxyPlotLoader
    {
        private static Assembly? _entry;
        private static readonly object Gate = new();

        public static List<string> VerifyHashes()
        {
            var failures = new List<string>();
            foreach (var (_, file, bytes, expected) in ReadStaged())
            {
                var actual = Convert.ToHexString(SHA256.HashData(bytes));
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    failures.Add($"hash mismatch in staged '{file}'");
            }
            return failures;
        }

        public static Assembly Load()
        {
            lock (Gate)
            {
                var hit = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "ExampleBrowser.WPF", StringComparison.OrdinalIgnoreCase));
                if (hit is not null) return hit;
                if (_entry is not null) return _entry;
                var images = ReadStaged().ToDictionary(e => e.name, e => e.bytes, StringComparer.OrdinalIgnoreCase);
                AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
                {
                    var requested = new AssemblyName(args.Name).Name;
                    var loaded = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => string.Equals(a.GetName().Name, requested, StringComparison.OrdinalIgnoreCase));
                    if (loaded is not null) return loaded;
                    if (requested is not null && images.TryGetValue(requested, out var image))
                        return Assembly.Load(image);
                    return null;
                };
                foreach (var image in images.Values) Assembly.Load(image);
                _entry = AppDomain.CurrentDomain.GetAssemblies().First(a => string.Equals(a.GetName().Name, "ExampleBrowser.WPF", StringComparison.OrdinalIgnoreCase));
                return _entry;
            }
        }

        private static List<(string name, string file, byte[] bytes, string expected)> ReadStaged()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "oxyplot", "app-manifest.json")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            var apps = System.IO.Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "oxyplot");
            using var document = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
            var entries = new List<(string, string, byte[], string)>();
            foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
            {
                var file = assembly.GetProperty("file").GetString()!;
                var expected = assembly.GetProperty("sha256").GetString()!;
                var bytes = File.ReadAllBytes(System.IO.Path.Combine(apps, file));
                entries.Add((assembly.GetProperty("name").GetString()!, file, bytes, expected));
            }
            return entries;
        }
    }
}
