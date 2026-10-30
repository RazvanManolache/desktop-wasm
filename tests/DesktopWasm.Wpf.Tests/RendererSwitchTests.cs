using System.ComponentModel;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Core;
using DesktopWasm.Wpf;

namespace DesktopWasm.Wpf.Tests;

// Slice-52 regression: the renderer-switch interaction is real behavior.
// The converted ExampleBrowser binds its renderer ComboBox TwoWay to
// VM.Renderer (Canvas/Canvas_XAML/SkiaSharp); picking Canvas_XAML shows the
// converted XamlPlotView with real XamlRenderContext series ink (the engine
// LineSegment(Point, bool) and PointCollection(int/IEnumerable) ctors the
// converted IL binds), SkiaSharp lands on the honest-empty engine shim (no
// staged OxyPlot.SkiaSharp.Wpf image), and switching back restores Canvas.
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class RendererSwitchTests : IDisposable
{
    private readonly Application? _previous = Application.Current;
    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    [Fact]
    public void LineSegment_point_stroked_ctor_sets_point_and_stroke()
    {
        var segment = new LineSegment(new Point(3, 4), false);
        Assert.Equal(new Point(3, 4), segment.Point);
        Assert.False(segment.IsStroked);
        Assert.True(new LineSegment(new Point(0, 0), true).IsStroked);
    }

    [Fact]
    public void PointCollection_capacity_and_copy_ctors_match_wpf()
    {
        var sized = new PointCollection(16);
        Assert.Empty(sized);
        var copied = new PointCollection(new[] { new Point(1, 2), new Point(3, 4) });
        Assert.Equal(2, copied.Count);
        Assert.Equal(new Point(3, 4), copied[1]);
    }

    [Fact]
    public void Selector_selected_item_pushes_two_way_binding_back_to_source()
    {
        var vm = new ChoiceStub { Choice = "a" };
        var combo = new ComboBox { DataContext = vm };
        combo.ItemsSource = new object[] { "a", "b", "c" };
        combo.SetBinding("SelectedItem", new Binding("Choice"));
        Assert.Equal(0, combo.SelectedIndex);

        combo.SelectedIndex = 2;
        Assert.Equal("c", vm.Choice);

        vm.Choice = "b";
        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal("b", combo.SelectedItem);
    }

    [Fact]
    public void Selector_reseats_selection_when_items_rebuild()
    {
        var vm = new ChoiceStub { Choice = "b" };
        var combo = new ComboBox { DataContext = vm };
        combo.SetBinding("SelectedItem", new Binding("Choice"));
        Assert.Equal(-1, combo.SelectedIndex);
        combo.ItemsSource = new object[] { "a", "b", "c" };
        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal("b", combo.SelectedItem);
    }

    [Fact]
    public void Staged_renderer_combo_switches_to_xaml_with_real_series_ink()
    {
        var window = BootWithFirstExample();
        var combo = Descendants(window).OfType<ComboBox>().Single();
        Assert.Equal(3, combo.Items.Count);
        Assert.Equal(0, combo.SelectedIndex);

        // Programmatic selection must push VM.Renderer through the TwoWay
        // SelectedItem binding (stays Canvas without the push).
        combo.SelectedIndex = 1;
        var vm = window.DataContext!;
        Assert.Equal("Canvas_XAML", RendererName(vm));
        Assert.Equal(1, combo.SelectedIndex);

        window.Measure(new WpfSize(800, 600));
        window.Arrange(new WpfRect(0, 0, 800, 600));

        var xamlHost = (FrameworkElement)FindByFullName(window, "ExampleBrowser.XamlPlotView")!;
        var xamlModel = xamlHost.GetType().GetProperty("Model")?.GetValue(xamlHost);
        Assert.NotNull(xamlModel);
        Assert.Same(CanvasXamlModel(vm), xamlModel);
        Assert.Equal(Visibility.Visible, xamlHost.Visibility);
        Assert.Null(LastPlotException(xamlModel!));

        var (paths, labels) = InkOf(xamlHost);
        Assert.NotEmpty(paths);
        Assert.DoesNotContain(labels, l => l.Contains("OxyPlot exception", StringComparison.Ordinal));
        Assert.Contains("Normal distribution", labels);
        Assert.Contains("Probability density function", labels);

        Assert.Equal(Visibility.Collapsed,
            ((FrameworkElement)FindByFullName(window, "OxyPlot.Wpf.PlotView")!).Visibility);
        var shim = Descendants(window).OfType<DesktopWasm.Wpf.PlotView>().Single();
        Assert.Equal(Visibility.Collapsed, shim.Visibility);
        Assert.Null(shim.Model);
    }

    [Fact]
    public void Staged_keyboard_path_switches_renderer_through_focused_combo()
    {
        // Mirrors the browser proof exactly (800x600 scene, Tab + arrows):
        // the renderer panel now docks at the scene's bottom-right corner
        // (slice-54 arrange/alignment fix), but the proof keeps driving
        // the real keyboard path, so this oracle covers that same path.
        WasmDispatcher.Current = new WasmDispatcher(new WasmDispatcherPump());
        var entry = StagedOxyPlot.Load();
        Application.Current = null;
        var window = WpfAppBoot.Boot(entry);
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        manager.AddRoot(window);
        window.Measure(new WpfSize(800, 600));
        window.Arrange(new WpfRect(0, 0, 800, 600));
        var list = Descendants(window).OfType<ListBox>().Single();
        Descendants(window).OfType<Expander>().First().IsExpanded = true;
        list.SelectedIndex = 0;
        var vm = window.DataContext!;
        var handle = new WasmWindowHandle(1);
        void Press(string key) =>
            router.DispatchKeyboard(new WasmKeyArgs(handle, key, true, false, false, false));
        Assert.True(manager.TryFocus(list));
        ComboBox? combo = null;
        for (var i = 0; i < 10 && combo is null; i++)
        {
            Press("Tab");
            combo = Keyboard.FocusedElement as ComboBox;
        }
        Assert.NotNull(combo);
        Assert.Equal(3, combo!.Items.Count);

        Press("ArrowDown");
        Assert.Equal("Canvas_XAML", RendererName(vm));
        window.Measure(new WpfSize(800, 600));
        window.Arrange(new WpfRect(0, 0, 800, 600));
        var xamlHost = (FrameworkElement)FindByFullName(window, "ExampleBrowser.XamlPlotView")!;
        Assert.Equal(Visibility.Visible, xamlHost.Visibility);
        Assert.Same(CanvasXamlModel(vm), xamlHost.GetType().GetProperty("Model")?.GetValue(xamlHost));
        Assert.Null(LastPlotException(CanvasXamlModel(vm)!));
        var (paths, labels) = InkOf(xamlHost);
        Assert.NotEmpty(paths);
        Assert.DoesNotContain(labels, l => l.Contains("OxyPlot exception", StringComparison.Ordinal));
        Assert.Contains("Normal distribution", labels);
        Assert.Contains("Probability density function", labels);

        Press("ArrowDown");
        Assert.Equal("SkiaSharp", RendererName(vm));
        var shim = Descendants(window).OfType<DesktopWasm.Wpf.PlotView>().Single();
        Assert.Equal(Visibility.Visible, shim.Visibility);
        Assert.Same(SkiaModel(vm), shim.Model);

        Press("ArrowUp");
        Press("ArrowUp");
        Assert.Equal("Canvas", RendererName(vm));
        var oxyHost = (FrameworkElement)FindByFullName(window, "OxyPlot.Wpf.PlotView")!;
        Assert.Equal(Visibility.Visible, oxyHost.Visibility);
        Assert.Same(CanvasModel(vm), oxyHost.GetType().GetProperty("Model")?.GetValue(oxyHost));
    }

    [Fact]
    public void Staged_skia_renderer_is_honestly_empty_and_canvas_restores()
    {
        var window = BootWithFirstExample();
        var combo = Descendants(window).OfType<ComboBox>().Single();
        var vm = window.DataContext!;

        combo.SelectedIndex = 2;
        Assert.Equal("SkiaSharp", RendererName(vm));
        window.Measure(new WpfSize(800, 600));
        window.Arrange(new WpfRect(0, 0, 800, 600));

        // No staged OxyPlot.SkiaSharp.Wpf image: the engine shim binds the
        // live SkiaModel and stays Visible but paints nothing (no Canvas
        // surface, unlike the converted hosts).
        var shim = Descendants(window).OfType<DesktopWasm.Wpf.PlotView>().Single();
        Assert.Equal(Visibility.Visible, shim.Visibility);
        Assert.Same(SkiaModel(vm), shim.Model);
        Assert.Null(shim.GetType().GetProperty("Canvas",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
        Assert.Equal(Visibility.Collapsed,
            ((FrameworkElement)FindByFullName(window, "OxyPlot.Wpf.PlotView")!).Visibility);
        Assert.Equal(Visibility.Collapsed,
            ((FrameworkElement)FindByFullName(window, "ExampleBrowser.XamlPlotView")!).Visibility);

        // Switching back restores the live Canvas host with the same model.
        combo.SelectedIndex = 0;
        Assert.Equal("Canvas", RendererName(vm));
        window.Measure(new WpfSize(800, 600));
        window.Arrange(new WpfRect(0, 0, 800, 600));
        var oxyHost = (FrameworkElement)FindByFullName(window, "OxyPlot.Wpf.PlotView")!;
        Assert.Equal(Visibility.Visible, oxyHost.Visibility);
        Assert.Same(CanvasModel(vm), oxyHost.GetType().GetProperty("Model")?.GetValue(oxyHost));
        Assert.Null(LastPlotException(CanvasModel(vm)!));
    }

    private static Window BootWithFirstExample()
    {
        // The converted PlotViewBase renders synchronously only on the
        // dispatcher-owner thread (BeginInvoke posts otherwise and nothing
        // pumps in this harness), so each staged boot takes a thread-owned
        // pump like A11yEventsTests/ChromePackTests do.
        WasmDispatcher.Current = new WasmDispatcher(new WasmDispatcherPump());
        var entry = StagedOxyPlot.Load();
        Application.Current = null;
        var window = WpfAppBoot.Boot(entry);
        window.Measure(new WpfSize(800, 600));
        window.Arrange(new WpfRect(0, 0, 800, 600));
        var list = Descendants(window).OfType<ListBox>().Single();
        Descendants(window).OfType<Expander>().First().IsExpanded = true;
        list.SelectedIndex = 0;
        Assert.Equal("CanvasModel", CanvasModelName(window));
        window.Measure(new WpfSize(800, 600));
        window.Arrange(new WpfRect(0, 0, 800, 600));
        return window;
    }

    private static string CanvasModelName(Window window)
    {
        var vm = window.DataContext!;
        return vm.GetType().GetProperty("CanvasModel")?.GetValue(vm) is null ? "<none>" : "CanvasModel";
    }

    private static string RendererName(object vm) =>
        vm.GetType().GetProperty("Renderer")?.GetValue(vm)?.ToString() ?? "<none>";

    private static object? CanvasModel(object vm) =>
        vm.GetType().GetProperty("CanvasModel")?.GetValue(vm);

    private static object? CanvasXamlModel(object vm) =>
        vm.GetType().GetProperty("CanvasXamlModel")?.GetValue(vm);

    private static object? SkiaModel(object vm) =>
        vm.GetType().GetProperty("SkiaModel")?.GetValue(vm);

    private static Exception? LastPlotException(object model) =>
        model.GetType().GetField("lastPlotException", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(model) as Exception;

    private static (List<DesktopWasm.Wpf.Path> Paths, List<string> Labels) InkOf(object plotView)
    {
        var canvas = plotView.GetType().GetProperty("Canvas",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy)
            ?.GetValue(plotView);
        Assert.NotNull(canvas);
        var children = ((System.Collections.IEnumerable)canvas!.GetType()
            .GetProperty("Children")!.GetValue(canvas)!).Cast<object>().ToList();
        return (children.OfType<DesktopWasm.Wpf.Path>().ToList(),
            children.OfType<TextBlock>().Select(t => t.Text ?? "")
                .Where(t => t.Length > 0).ToList());
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

    private sealed class ChoiceStub : INotifyPropertyChanged
    {
        private string? _choice;
        public string? Choice
        {
            get => _choice;
            set
            {
                if (_choice == value) return;
                _choice = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Choice)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
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
