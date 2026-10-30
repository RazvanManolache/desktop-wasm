using System.ComponentModel;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Wpf;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-41: the XAML loader instantiates the non-element
/// &lt;Window.DataContext&gt; type (OxyPlot ExampleBrowser
/// MainWindowViewModel shape) from the converted assembly so boot advances
/// past MainWindow.xaml line 10 and bindings flow. The converter keeps the
/// view-model verbatim (base System.Object), so no map change is covered
/// here; resolution is by converted-assembly manifest lookup at load/boot.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class DataContextViewModelTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private readonly Application? _previous = Application.Current;

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    internal sealed class DataContextProbeViewModel : INotifyPropertyChanged
    {
        private string _name = "Ada";

        public string Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public int Count { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    internal sealed class NoDefaultCtorViewModel
    {
        public NoDefaultCtorViewModel(string name) => Name = name;

        public string Name { get; }
    }

    [Fact]
    public void Window_DataContext_instantiates_converted_view_model()
    {
        Application.Current = new BootProbe.ProbeApp();
        XamlLoader.CustomElementSource = typeof(DataContextProbeViewModel).Assembly;
        var root = (Window)XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\" xmlns:local=\"clr-namespace:DesktopWasm.Wpf.Tests\">" +
            "<Window.DataContext><local:DataContextProbeViewModel /></Window.DataContext>" +
            "<Grid /></Window>").Root;
        var vm = Assert.IsType<DataContextProbeViewModel>(root.DataContext);
        Assert.Same(vm, root.DataContext);
        Assert.Equal("Ada", vm.Name);
        Assert.Same(vm, ((Grid)root.Content!).DataContext);
    }

    [Fact]
    public void Window_DataContext_attributes_land_on_view_model()
    {
        Application.Current = new BootProbe.ProbeApp();
        XamlLoader.CustomElementSource = typeof(DataContextProbeViewModel).Assembly;
        var root = (Window)XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\" xmlns:local=\"clr-namespace:DesktopWasm.Wpf.Tests\">" +
            "<Window.DataContext><local:DataContextProbeViewModel Name=\"Grace\" Count=\"3\" /></Window.DataContext>" +
            "<Grid /></Window>").Root;
        var vm = Assert.IsType<DataContextProbeViewModel>(root.DataContext);
        Assert.Equal("Grace", vm.Name);
        Assert.Equal(3, vm.Count);
    }

    [Fact]
    public void Window_DataContext_binding_flows_from_view_model()
    {
        Application.Current = new BootProbe.ProbeApp();
        XamlLoader.CustomElementSource = typeof(DataContextProbeViewModel).Assembly;
        var root = (Window)XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\" xmlns:local=\"clr-namespace:DesktopWasm.Wpf.Tests\">" +
            "<Window.DataContext><local:DataContextProbeViewModel /></Window.DataContext>" +
            "<StackPanel><TextBlock Text=\"{Binding Name}\" /></StackPanel></Window>").Root;
        var panel = Assert.IsType<StackPanel>(root.Content);
        var text = Assert.IsType<TextBlock>(panel.LogicalChildren[0]);
        var vm = Assert.IsType<DataContextProbeViewModel>(root.DataContext);
        Assert.Equal("Ada", text.Text);
        vm.Name = "Grace";
        Assert.Equal("Grace", text.Text);
        Assert.Same(vm, panel.DataContext);
    }

    [Fact]
    public void Window_DataContext_unknown_type_stays_precise()
    {
        Application.Current = new BootProbe.ProbeApp();
        XamlLoader.CustomElementSource = typeof(DataContextProbeViewModel).Assembly;
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\" xmlns:local=\"clr-namespace:DesktopWasm.Wpf.Tests\">" +
            "<Window.DataContext><local:NoSuchViewModel /></Window.DataContext>" +
            "<Grid /></Window>"));
        Assert.Contains("Unknown element <NoSuchViewModel>", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_DataContext_type_without_default_ctor_stays_unresolved()
    {
        Application.Current = new BootProbe.ProbeApp();
        XamlLoader.CustomElementSource = typeof(DataContextProbeViewModel).Assembly;
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\" xmlns:local=\"clr-namespace:DesktopWasm.Wpf.Tests\">" +
            "<Window.DataContext><local:NoDefaultCtorViewModel /></Window.DataContext>" +
            "<Grid /></Window>"));
        Assert.Contains("Unknown element <NoDefaultCtorViewModel>", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_DataContext_rejects_ambiguous_short_names()
    {
        Application.Current = new BootProbe.ProbeApp();
        XamlLoader.CustomElementSource = typeof(DataContextProbeViewModel).Assembly;
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\" xmlns:local=\"clr-namespace:DesktopWasm.Wpf.Tests\">" +
            "<Window.DataContext><local:DupeViewModel /></Window.DataContext>" +
            "<Grid /></Window>"));
        Assert.Contains("matches 2 converted types", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadComponent_hydration_copies_DataContext_and_bindings_flow()
    {
        Application.Current = new BootProbe.ProbeApp();
        var window = new BootProbe.DataContextMain();
        var vm = Assert.IsType<BootProbe.ProbeViewModel>(window.DataContext);
        Assert.Same(vm, window.DataContext);
        var grid = Assert.IsType<Grid>(window.Content);
        var text = Assert.IsType<TextBlock>(Assert.Single(grid.LogicalChildren));
        Assert.Equal("Ada", text.Text);
        vm.Name = "Grace";
        Assert.Equal("Grace", text.Text);
    }

    [Fact]
    public void LoadComponent_hydration_preserves_codebehind_DataContext_when_xaml_declares_none()
    {
        Application.Current = new BootProbe.ProbeApp();
        var window = new BootProbe.PreSetDataContextMain();
        Assert.Same(window, window.DataContext);
        Assert.Equal("Boot", window.Title);
    }

    [Fact]
    public void Staged_MainWindow_xaml_inflates_past_DataContext_line()
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
            try
            {
                var root = XamlLoader.Load(reader.ReadToEnd());
                var window = Assert.IsType<Window>(root.Root);
                Assert.Equal("ExampleBrowser.MainWindowViewModel",
                    window.DataContext?.GetType().FullName);
            }
            catch (XamlLoadException ex)
            {
                Assert.DoesNotContain("MainWindowViewModel", ex.Message, StringComparison.Ordinal);
                Assert.True(ex.Line > 10, $"staged MainWindow.xaml must advance past line 10, failed at line {ex.Line}: {ex.Message}");
            }
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
        }
    }

    [Fact]
    public void Staged_boot_advances_past_DataContext_line()
    {
        Application.Current = null;
        var entry = LoadStagedOxyPlotEntry();
        try
        {
            var window = WpfAppBoot.Boot(entry);
            Assert.Equal("ExampleBrowser.MainWindowViewModel",
                window.DataContext?.GetType().FullName);
        }
        catch (Exception ex)
        {
            var messages = new List<string>();
            for (var current = ex; current is not null; current = current.InnerException)
                messages.Add(current.GetType().FullName + ": " + current.Message.Split('\n')[0]);
            Assert.DoesNotContain(messages, m => m.Contains("Unknown element <MainWindowViewModel>", StringComparison.Ordinal));
            var xamlFailure = messages.FirstOrDefault(m => m.Contains(nameof(XamlLoadException), StringComparison.Ordinal));
            if (xamlFailure is not null)
                Assert.DoesNotMatch(@"\(line 10,", xamlFailure);
        }
    }

    private static readonly object StagedGate = new();
    private static Assembly? _stagedEntry;
    private static Dictionary<string, byte[]>? _stagedImages;

    private static Assembly LoadStagedOxyPlotEntry()
    {
        lock (StagedGate)
        {
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
            string? entryFile = null;
            var entryName = document.RootElement.GetProperty("entryAssembly").GetString();
            foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
            {
                var file = assembly.GetProperty("file").GetString()!;
                var expected = assembly.GetProperty("sha256").GetString()!;
                var bytes = File.ReadAllBytes(System.IO.Path.Combine(apps, file));
                var actual = Convert.ToHexString(SHA256.HashData(bytes));
                Assert.True(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                    $"hash mismatch in staged '{file}': {actual} != {expected}");
                images[assembly.GetProperty("name").GetString()!] = bytes;
                if (string.Equals(assembly.GetProperty("name").GetString(), entryName, StringComparison.Ordinal))
                    entryFile = file;
            }
            Assert.NotNull(entryFile);
            _stagedImages = images;
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                var requested = new AssemblyName(args.Name).Name;
                var hit = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, requested, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) return hit;
                if (requested is not null && images.TryGetValue(requested, out var image))
                    return Assembly.Load(image);
                return null;
            };
            foreach (var image in images.Values)
                Assembly.Load(image);
            _stagedEntry = AppDomain.CurrentDomain.GetAssemblies()
                .First(a => string.Equals(a.GetName().Name, entryName, StringComparison.OrdinalIgnoreCase));
            return _stagedEntry;
        }
    }
}
