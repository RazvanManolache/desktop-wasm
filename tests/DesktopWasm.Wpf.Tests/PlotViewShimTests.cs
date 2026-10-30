using System.ComponentModel;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-46: honest engine placeholders for the OxyPlot plot views
/// (OxyPlot.Wpf PlotView, OxyPlot.SkiaSharp.Wpf PlotView, and the
/// entry-local ExampleBrowser.XamlPlotView, which derives the missing
/// OxyPlot.Wpf.PlotView base). The view assemblies are conversion-blocked
/// (missing System.IO.Packaging/ReachFramework/System.Printing/
/// ConfigurationManager plus unmapped SerializerWriter/TextOptions/
/// ClearTypeHint/manipulation types), so no hash-verified converted image
/// exists to stage; the shim keeps layout participation, DataContext
/// inheritance and Model/Controller/Visibility bindings against the real
/// staged view-model IL while painting nothing (never fabricating plot
/// pixels). Staged boot now completes through MainWindow.xaml line 133.
/// Slice-47: the legacy ListBox materializes the live 754-example
/// CollectionView into rows (71 groups) and row selection flows back into
/// the VM, populating the bound plot models.
/// Slice-49: the staged converted views instantiate (oxyWpf:PlotView resolves
/// OxyPlot.Wpf.PlotView, local:XamlPlotView resolves
/// ExampleBrowser.XamlPlotView; oxySkia keeps the engine shim with no staged
/// image). Engine UIElement.OnRender is protected virtual like real WPF
/// (plus the int CommandBindingCollection.Add, LayoutUpdated, 3-arg Register
/// and DefaultStyleKey contracts the converted constructors need). The engine
/// shim still paints nothing; the converted hosts paint their own chrome
/// while series ink awaits the converted default template (PART_Grid).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class PlotViewShimTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    private readonly Application? _previous = Application.Current;

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    private sealed class PlotStubViewModel : INotifyPropertyChanged
    {
        private object? _model = new();
        private object? _controller = new();

        public object? SkiaModel
        {
            get => _model;
            set { _model = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SkiaModel))); }
        }

        public object? PlotController
        {
            get => _controller;
            set { _controller = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlotController))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class NonNullToCollapsedConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is null ? Visibility.Visible : Visibility.Collapsed;

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    [Fact]
    public void Model_and_controller_default_to_null_with_dp_identity()
    {
        Assert.Same(PlotView.ModelProperty, DependencyProperty.FromName(typeof(PlotView), "Model"));
        Assert.Same(PlotView.ControllerProperty, DependencyProperty.FromName(typeof(PlotView), "Controller"));
        Assert.Equal(typeof(object), PlotView.ModelProperty.PropertyType);
        Assert.Equal(typeof(object), PlotView.ControllerProperty.PropertyType);
        var view = new PlotView();
        Assert.Null(view.Model);
        Assert.Null(view.Controller);
        var model = new object();
        var controller = new object();
        view.Model = model;
        view.Controller = controller;
        Assert.Same(model, view.Model);
        Assert.Same(controller, view.Controller);
        Assert.Same(model, view.GetValue(PlotView.ModelProperty));
        Assert.Same(controller, view.GetValue(PlotView.ControllerProperty));
    }

    [Fact]
    public void XamlPlotView_mirrors_upstream_subclass_shape()
    {
        Assert.True(typeof(PlotView).IsAssignableFrom(typeof(XamlPlotView)));
        var view = new XamlPlotView();
        Assert.Null(view.Model);
        var model = new object();
        view.Model = model;
        Assert.Same(model, ((PlotView)view).Model);
    }

    [Fact]
    public void Template_less_plotview_measures_zero_and_paints_nothing()
    {
        var view = new PlotView { Model = new object(), Controller = new object() };
        var window = new Window { Content = view };
        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(800, 600));
        Assert.Equal(0, view.DesiredSize.Width);
        Assert.Equal(0, view.DesiredSize.Height);
        // Window chrome (background fill, composition records) may exist;
        // the placeholder itself must emit no ink of any kind.
        Assert.DoesNotContain(commands, c => c is DrawCommand.DrawImage or DrawCommand.DrawText
            or DrawCommand.DrawGeometry or DrawCommand.DrawEllipse or DrawCommand.DrawLine
            or DrawCommand.FillGradient or DrawCommand.FillRoundedRect
            or DrawCommand.DefBitmap or DrawCommand.BlitBitmap or DrawCommand.PatchBitmap);
    }

    [Fact]
    public void Plot_xaml_block_parses_with_bindings_and_converter()
    {
        var app = new Application();
        app.Resources.Add("PlotVisibility", new NonNullToCollapsedConverter());
        Application.Current = app;
        XamlLoader.CustomElementSource = typeof(PlotStubViewModel).Assembly;
        var root = (Window)XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\" xmlns:x=\"{XamlNs}\" xmlns:local=\"clr-namespace:DesktopWasm.Wpf.Tests\">" +
            "<Window.DataContext><local:PlotStubViewModel /></Window.DataContext>" +
            "<Grid>" +
            "<PlotView Model=\"{Binding SkiaModel}\" Controller=\"{Binding PlotController}\" Visibility=\"{Binding SkiaModel, Converter={StaticResource PlotVisibility}}\" />" +
            "<XamlPlotView Model=\"{Binding SkiaModel}\" Controller=\"{Binding PlotController}\" />" +
            "</Grid></Window>").Root;
        var grid = Assert.IsType<Grid>(root.Content);
        Assert.Equal(2, grid.LogicalChildren.Count);
        var first = Assert.IsType<PlotView>(grid.LogicalChildren[0]);
        var second = Assert.IsType<XamlPlotView>(grid.LogicalChildren[1]);
        var vm = Assert.IsType<PlotStubViewModel>(root.DataContext);
        Assert.Same(vm.SkiaModel, first.Model);
        Assert.Same(vm.PlotController, first.Controller);
        Assert.Same(vm.SkiaModel, second.Model);
        // Non-null through the converter lands Collapsed (default is
        // Visible), proving the StaticResource converter evaluated.
        Assert.Equal(Visibility.Collapsed, first.Visibility);
        Assert.Same(vm, first.DataContext);
    }

    [Fact]
    public void Model_binding_tracks_source_changes_live()
    {
        Application.Current = new BootProbe.ProbeApp();
        XamlLoader.CustomElementSource = typeof(PlotStubViewModel).Assembly;
        var root = (Window)XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\" xmlns:x=\"{XamlNs}\" xmlns:local=\"clr-namespace:DesktopWasm.Wpf.Tests\">" +
            "<Window.DataContext><local:PlotStubViewModel /></Window.DataContext>" +
            "<Grid><PlotView Model=\"{Binding SkiaModel}\" /></Grid></Window>").Root;
        var grid = Assert.IsType<Grid>(root.Content);
        var view = Assert.IsType<PlotView>(grid.LogicalChildren[0]);
        var vm = Assert.IsType<PlotStubViewModel>(root.DataContext);
        Assert.Same(vm.SkiaModel, view.Model);
        var replacement = new object();
        vm.SkiaModel = replacement;
        Assert.Same(replacement, view.Model);
    }

    [Fact]
    public void Staged_MainWindow_xaml_prefers_converted_plot_hosts()
    {
        var entry = LoadStagedOxyPlotEntry();
        var name = entry.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("MainWindow.xaml", StringComparison.Ordinal));
        Assert.NotNull(name);
        using var stream = entry.GetManifestResourceStream(name!);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var previous = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = entry;
        try
        {
            // Slice-49: the oxyWpf:/local: prefixes resolve to the staged
            // converted assemblies; only oxySkia (no staged
            // OxyPlot.SkiaSharp.Wpf image) keeps the engine shim.
            var root = (Window)XamlLoader.Load(reader.ReadToEnd()).Root;
            var hosts = new List<FrameworkElement>();
            void Collect(object? node)
            {
                if (node is FrameworkElement fe)
                {
                    if (fe.GetType().FullName is "DesktopWasm.Wpf.PlotView"
                        or "OxyPlot.Wpf.PlotView" or "ExampleBrowser.XamlPlotView")
                        hosts.Add(fe);
                    foreach (var child in fe.LogicalChildren) Collect(child);
                }
            }
            Collect(root);
            Assert.Equal(3, hosts.Count);
            Assert.Single(hosts, h => h.GetType() == typeof(PlotView));
            Assert.Single(hosts, h => h.GetType().FullName == "OxyPlot.Wpf.PlotView");
            Assert.Single(hosts, h => h.GetType().FullName == "ExampleBrowser.XamlPlotView");
            foreach (var host in hosts)
            {
                Assert.Same(root.DataContext, host.DataContext);
                // Each host binds its own Model DP (engine DP on the shim,
                // converted OxyPlot.Wpf.PlotViewBase DP on the converted pair).
                Assert.NotNull(ModelBinding(host));
            }
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
        }
    }

    private static BindingExpression? ModelBinding(FrameworkElement host)
    {
        if (host.GetType() == typeof(PlotView) || host.GetType() == typeof(XamlPlotView))
            return ((PlotView)host).GetBindingExpression(PlotView.ModelProperty);
        var dp = (DependencyProperty)host.GetType()
            .GetField("ModelProperty", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)!
            .GetValue(null)!;
        return host.GetBindingExpression(dp);
    }

    [Fact]
    public void Staged_boot_materializes_example_rows_and_selection_populates_models()
    {
        Application.Current = null;
        var entry = LoadStagedOxyPlotEntry();
        // The whole 134-line MainWindow hydrates into the converted
        // component (an ExampleBrowser.MainWindow, not the base).
        var window = WpfAppBoot.Boot(entry);
        Assert.Equal("ExampleBrowser.MainWindow", window.GetType().FullName);
        Assert.Equal("ExampleBrowser.MainWindowViewModel", window.DataContext?.GetType().FullName);
        var plots = new List<FrameworkElement>();
        var boxes = new List<ComboBox>();
        var lists = new List<ListBox>();
        void Collect(object? node)
        {
            if (node is FrameworkElement fe && fe.GetType().FullName is "DesktopWasm.Wpf.PlotView"
                or "OxyPlot.Wpf.PlotView" or "ExampleBrowser.XamlPlotView") plots.Add(fe);
            if (node is ComboBox box) boxes.Add(box);
            if (node is ListBox list) lists.Add(list);
            if (node is FrameworkElement fer)
                foreach (var child in fer.LogicalChildren) Collect(child);
        }
        Collect(window);
        Assert.Equal(3, plots.Count);
        // Slice-49: oxyWpf + local resolve to the staged converted views;
        // oxySkia (no staged OxyPlot.SkiaSharp.Wpf image) keeps the shim.
        Assert.Single(plots, p => p.GetType() == typeof(PlotView));
        Assert.Single(plots, p => p.GetType().FullName == "OxyPlot.Wpf.PlotView");
        Assert.Single(plots, p => p.GetType().FullName == "ExampleBrowser.XamlPlotView");
        // Bindings evaluate end-to-end against the real converted VM:
        // the renderer box lands the VM default with all three options.
        var renderer = Assert.Single(boxes);
        Assert.NotNull(renderer.SelectedItem);
        Assert.Equal(3, renderer.ItemsSource is System.Collections.IEnumerable e ? e.Cast<object>().Count() : -1);
        // Slice-47: the 754-example CollectionView lands as the legacy
        // ListBox ItemsSource AND generates one row per example across the
        // 71 category groups (pre-fix this stayed 0: a StaticResource throw
        // inside generation was swallowed by the ItemsSource binding).
        var examples = Assert.Single(lists);
        var source = Assert.IsAssignableFrom<System.Collections.IEnumerable>(examples.ItemsSource);
        Assert.Equal(754, source.Cast<object>().Count());
        Assert.Equal(754, examples.Items.Count);
        Assert.Equal(71, examples.LogicalChildren.OfType<GroupItem>().Count());
        Assert.Equal("Normal distribution", examples.Items[0].Content);
        foreach (var plot in plots)
            Assert.NotNull(ModelBinding(plot));
        // Example selection flows back through the SelectedItem binding
        // into the real VM and populates the bound Canvas plot model.
        var vm = window.DataContext!;
        examples.SelectedIndex = 0;
        var selected = vm.GetType().GetProperty("SelectedExample")?.GetValue(vm);
        Assert.NotNull(selected);
        var canvas = vm.GetType().GetProperty("CanvasModel")?.GetValue(vm);
        Assert.NotNull(canvas);
        // Slice-49: the converted oxyWpf host carries the live model (same
        // PlotModel instance the VM exposes), proving the converted
        // Model DP binding evaluates end to end.
        var canvasPlot = Assert.Single(plots, p =>
            p.GetType().FullName == "OxyPlot.Wpf.PlotView" &&
            ReferenceEquals(p.GetType().GetProperty("Model")?.GetValue(p), canvas));
        Assert.Equal(Visibility.Visible, canvasPlot.Visibility);
    }

    [Fact]
    public void OnRender_contract_matches_wpf_visibility()
    {
        // Real WPF declares UIElement.OnRender protected virtual; the engine
        // must not widen it (converted protected overrides fail to load with
        // TypeLoadException "cannot reduce access" on a public base).
        var onRender = typeof(UIElement).GetMethod("OnRender",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(DrawingContext) }, null);
        Assert.NotNull(onRender);
        Assert.True(onRender.IsFamily);
        Assert.False(onRender.IsPublic);
        Assert.True(onRender.IsVirtual);
        Assert.False(onRender.IsAbstract);
        // Engine overrides keep the protected contract; the compositor and
        // tests paint through the internal Render entry.
        foreach (var type in new[] { typeof(Button), typeof(TextBlock), typeof(Grid), typeof(PlotView) })
        {
            var resolved = type.GetMethod("OnRender",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(DrawingContext) }, null);
            Assert.NotNull(resolved);
            Assert.True(resolved.IsFamily);
            Assert.False(resolved.IsPublic);
        }
        var render = typeof(UIElement).GetMethod("Render",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(DrawingContext) }, null);
        Assert.NotNull(render);
    }

    [Fact]
    public void CommandBindingCollection_Add_returns_index_like_wpf()
    {
        // Real WPF CommandBindingCollection.Add returns int32 (verified
        // against the upstream net8.0-windows OxyPlot.Wpf binary, whose
        // PlotView constructor binds the int-returning shape): a void-only
        // engine raises MissingMethodException at instantiation.
        var bindings = new CommandBindingCollection();
        var first = new CommandBinding(new RoutedCommand());
        var second = new CommandBinding(new RoutedCommand());
        Assert.Equal(0, bindings.Add(first));
        Assert.Equal(1, bindings.Add(second));
        Assert.Equal(2, bindings.Count);
        Assert.Same(first, bindings[0]);
    }

    [Fact]
    public void LayoutUpdated_raises_at_end_of_arrange()
    {
        // Converted OxyPlot.Wpf PlotViewBase subscribes in its constructor,
        // so the engine must own the event (matches UIElement.LayoutUpdated).
        var grid = new Grid();
        var calls = 0;
        object? sender = null;
        grid.LayoutUpdated += (s, e) =>
        {
            calls++;
            sender = s;
        };
        var window = new Window { Content = grid };
        WasmMilCore.RenderHeadless(window, new WpfSize(800, 600));
        Assert.True(calls >= 1);
        Assert.Same(grid, sender);
    }

    [Fact]
    public void DependencyProperty_three_arg_register_matches_wpf()
    {
        // Real WPF declares the 3-arg overload alongside the default-valued
        // shape; converted static initializers bind the exact 3-arg MethodDef
        // (verified in the staged OxyPlot.Wpf.Shared image).
        var three = DependencyProperty.Register("Slice49Probe", typeof(string), typeof(PlotViewShimTests));
        var four = DependencyProperty.Register("Slice49Probe", typeof(string), typeof(PlotViewShimTests), new PropertyMetadata("x"));
        Assert.Same(three, four);
        var host = new TextBlock();
        Assert.Null(host.GetValue(DependencyProperty.Register("Slice49ProbeDefault", typeof(string), typeof(PlotViewShimTests))));
    }

    [Fact]
    public void DefaultStyleKey_contract_exists_and_round_trips()
    {
        // Converted OxyPlot.Wpf PlotViewBase overrides
        // DefaultStyleKeyProperty metadata in its static constructor.
        Assert.Same(FrameworkElement.DefaultStyleKeyProperty,
            DependencyProperty.FromName(typeof(FrameworkElement), "DefaultStyleKey"));
        var button = new Button();
        Assert.Null(button.DefaultStyleKey);
        var key = new object();
        button.DefaultStyleKey = key;
        Assert.Same(key, button.DefaultStyleKey);
    }

    [Fact]
    public void Converted_plot_views_instantiate_from_staged_images()
    {
        // Positive blocker assertions: the pre-fix TypeLoadException
        // (protected override against a public abstract base) and the
        // MissingMethod/MissingField chain in the converted constructors are
        // gone, so the staged converted views instantiate.
        var entry = LoadStagedOxyPlotEntry();
        var oxyWpf = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "OxyPlot.Wpf", StringComparison.OrdinalIgnoreCase));
        var plotView = oxyWpf.GetType("OxyPlot.Wpf.PlotView");
        Assert.NotNull(plotView);
        Assert.True(typeof(UIElement).IsAssignableFrom(plotView));
        var instance = Activator.CreateInstance(plotView, nonPublic: true);
        Assert.NotNull(instance);
        Assert.Equal("OxyPlot.Wpf.PlotView", instance.GetType().FullName);
        // The converted override keeps the protected contract.
        var onRender = plotView.GetMethod("OnRender",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(DrawingContext) }, null);
        Assert.NotNull(onRender);
        Assert.True(onRender.IsFamily);
        var xamlPlotView = entry.GetType("ExampleBrowser.XamlPlotView");
        Assert.NotNull(xamlPlotView);
        Assert.True(plotView.IsAssignableFrom(xamlPlotView));
        var xamlInstance = Activator.CreateInstance(xamlPlotView, nonPublic: true);
        Assert.NotNull(xamlInstance);
    }

    [Fact]
    public void Engine_native_plotview_keeps_shim_after_converted_load()
    {
        // Loader precedence is namespace-aware: presentation-namespace XAML
        // keeps the engine shim even when the converted assemblies are
        // already staged in the AppDomain (order-safe with the oracle above).
        LoadStagedOxyPlotEntry();
        Application.Current = new BootProbe.ProbeApp();
        XamlLoader.CustomElementSource = typeof(PlotStubViewModel).Assembly;
        try
        {
            var root = (Window)XamlLoader.Load(
                $"<Window xmlns=\"{PresentationNs}\" xmlns:x=\"{XamlNs}\">" +
                "<Grid><PlotView /><XamlPlotView /></Grid></Window>").Root;
            var grid = Assert.IsType<Grid>(root.Content);
            Assert.Equal(2, grid.LogicalChildren.Count);
            Assert.Equal(typeof(PlotView), grid.LogicalChildren[0].GetType());
            Assert.Equal(typeof(XamlPlotView), grid.LogicalChildren[1].GetType());
        }
        finally
        {
            XamlLoader.CustomElementSource = null;
        }
    }

    [Fact]
    public void Converted_canvas_host_binds_layouts_and_paints_chrome()
    {
        // End to end on the converted oxyWpf host: selection populates the
        // converted Model, the host takes grid space, becomes visible, and
        // the next frame carries its chrome. Series ink still awaits the
        // converted default template (PART_Grid), so this asserts host
        // behavior only and never fabricated plot pixels.
        Application.Current = null;
        var entry = LoadStagedOxyPlotEntry();
        var window = WpfAppBoot.Boot(entry);
        var pre = WasmMilCore.RenderHeadless(window, new WpfSize(1280, 720));
        var hosts = new List<FrameworkElement>();
        var lists = new List<ListBox>();
        void Collect(object? node)
        {
            if (node is FrameworkElement fe && fe.GetType().FullName is "DesktopWasm.Wpf.PlotView"
                or "OxyPlot.Wpf.PlotView" or "ExampleBrowser.XamlPlotView") hosts.Add(fe);
            if (node is ListBox list) lists.Add(list);
            if (node is FrameworkElement fer)
                foreach (var child in fer.LogicalChildren) Collect(child);
        }
        Collect(window);
        var canvasHost = Assert.Single(hosts, h => h.GetType().FullName == "OxyPlot.Wpf.PlotView");
        lists.Single().SelectedIndex = 0;
        Assert.Equal(Visibility.Visible, canvasHost.Visibility);
        Assert.True(canvasHost.ActualWidth > 0);
        Assert.True(canvasHost.ActualHeight > 0);
        var post = WasmMilCore.RenderHeadless(window, new WpfSize(1280, 720));
        Assert.True(post.Count > pre.Count);
    }

    private static readonly object StagedGate = new();
    private static Assembly? _stagedEntry;

    private static Assembly LoadStagedOxyPlotEntry()
    {
        lock (StagedGate)
        {
            var hit = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "ExampleBrowser.WPF", StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                VerifyStagedHashes();
                return hit;
            }
            if (_stagedEntry is not null) return _stagedEntry;
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(System.IO.Path.Combine(
                directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "oxyplot", "app-manifest.json")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            var apps = System.IO.Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "oxyplot");
            using var document = JsonDocument.Parse(
                File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
            var images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
            {
                var file = assembly.GetProperty("file").GetString()!;
                var expected = assembly.GetProperty("sha256").GetString()!;
                var bytes = File.ReadAllBytes(System.IO.Path.Combine(apps, file));
                var actual = Convert.ToHexString(SHA256.HashData(bytes));
                Assert.True(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                    $"hash mismatch in staged '{file}': {actual} != {expected}");
                images[assembly.GetProperty("name").GetString()!] = bytes;
            }
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
            foreach (var image in images.Values)
                Assembly.Load(image);
            _stagedEntry = AppDomain.CurrentDomain.GetAssemblies()
                .First(a => string.Equals(a.GetName().Name, "ExampleBrowser.WPF", StringComparison.OrdinalIgnoreCase));
            return _stagedEntry;
        }
    }

    [Fact]
    public void Staged_manifest_reports_converted_with_zero_gaps()
    {
        var entry = LoadStagedOxyPlotEntry();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(
            directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "oxyplot", "app-manifest.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        using var document = JsonDocument.Parse(
            File.ReadAllText(System.IO.Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "oxyplot", "app-manifest.json")));
        var root = document.RootElement;
        // Slice-48: the 8 unmapped view types plus the 4 missing document/
        // print/settings dependencies now map onto engine counterparts, so the
        // OxyPlot.Wpf/OxyPlot.Wpf.Shared view assemblies stage and the
        // package converts with zero gaps.
        Assert.Equal("converted", root.GetProperty("status").GetString());
        Assert.Empty(root.GetProperty("gaps").EnumerateArray());
        var names = root.GetProperty("assemblies").EnumerateArray()
            .Select(a => a.GetProperty("name").GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(5, names.Count);
        Assert.Contains("ExampleBrowser.WPF", names);
        Assert.Contains("OxyPlot.Wpf", names);
        Assert.Contains("OxyPlot.Wpf.Shared", names);
        Assert.NotNull(entry);
    }

    [Fact]
    public void Staged_boot_collapses_null_model_hosts_until_selection()
    {
        Application.Current = null;
        var entry = LoadStagedOxyPlotEntry();
        var window = WpfAppBoot.Boot(entry);
        var plots = new List<FrameworkElement>();
        var lists = new List<ListBox>();
        void Collect(object? node)
        {
            if (node is FrameworkElement fe && fe.GetType().FullName is "DesktopWasm.Wpf.PlotView"
                or "OxyPlot.Wpf.PlotView" or "ExampleBrowser.XamlPlotView") plots.Add(fe);
            if (node is ListBox list) lists.Add(list);
            if (node is FrameworkElement fer)
                foreach (var child in fer.LogicalChildren) Collect(child);
        }
        Collect(window);
        Assert.Equal(3, plots.Count);
        // Slice-48 null-converter parity, end to end through the real
        // converted NotNullVisibilityConverter: all three plot models start
        // null, the converter runs on the null source (no TargetNullValue),
        // and every empty host collapses instead of overlapping. Slice-49:
        // the converted pair collapses through the same converter on the
        // converted Model DP.
        foreach (var plot in plots)
        {
            Assert.Null(plot.GetType().GetProperty("Model")?.GetValue(plot));
            Assert.Equal(Visibility.Collapsed, plot.Visibility);
        }
        var examples = Assert.Single(lists);
        examples.SelectedIndex = 0;
        var vm = window.DataContext!;
        var canvas = vm.GetType().GetProperty("CanvasModel")?.GetValue(vm);
        Assert.NotNull(canvas);
        var canvasPlot = Assert.Single(plots, p =>
            p.GetType().FullName == "OxyPlot.Wpf.PlotView" &&
            ReferenceEquals(p.GetType().GetProperty("Model")?.GetValue(p), canvas));
        Assert.Equal(Visibility.Visible, canvasPlot.Visibility);
        Assert.Equal(2, plots.Count(p => p.Visibility == Visibility.Collapsed));
    }

    private static void VerifyStagedHashes()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(
            directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "oxyplot", "app-manifest.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var apps = System.IO.Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "oxyplot");
        using var document = JsonDocument.Parse(
            File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
        foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
        {
            var file = assembly.GetProperty("file").GetString()!;
            var expected = assembly.GetProperty("sha256").GetString()!;
            var actual = Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(System.IO.Path.Combine(apps, file))));
            Assert.True(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                $"hash mismatch in staged '{file}': {actual} != {expected}");
        }
    }
}
