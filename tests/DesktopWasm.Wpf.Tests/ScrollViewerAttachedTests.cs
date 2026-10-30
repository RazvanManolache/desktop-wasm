using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-45: ScrollViewer attached style setters. The staged OxyPlot
/// ExampleBrowser MainWindow.xaml declares a CodeTextBox style with
/// &lt;Setter Property="ScrollViewer.VerticalScrollBarVisibility" Value="Auto"&gt;
/// (file line 108); boot used to stop with XamlLoadException
/// "Unknown attached property 'ScrollViewer.VerticalScrollBarVisibility'"
/// (loader line 109, position 29). The engine now registers the real
/// attached dependency properties with WPF parity (DP registration with
/// Inherits metadata, static Get/Set, instance wrappers, value inheritance
/// to scrollable descendants, attribute + style-setter + template paths).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class ScrollViewerAttachedTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private readonly Application? _previous = Application.Current;

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    [Fact]
    public void ScrollBarVisibility_numerics_match_wpf()
    {
        Assert.Equal(0, (int)ScrollBarVisibility.Disabled);
        Assert.Equal(1, (int)ScrollBarVisibility.Auto);
        Assert.Equal(2, (int)ScrollBarVisibility.Hidden);
        Assert.Equal(3, (int)ScrollBarVisibility.Visible);
    }

    [Fact]
    public void Visibility_defaults_match_wpf()
    {
        var box = new TextBox();
        Assert.Equal(ScrollBarVisibility.Disabled, ScrollViewer.GetHorizontalScrollBarVisibility(box));
        Assert.Equal(ScrollBarVisibility.Visible, ScrollViewer.GetVerticalScrollBarVisibility(box));
        Assert.False(ScrollViewer.GetCanContentScroll(box));
        Assert.False(ScrollViewer.GetIsDeferredScrollingEnabled(box));

        var view = new ScrollViewer();
        Assert.Equal(ScrollBarVisibility.Disabled, view.HorizontalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Visible, view.VerticalScrollBarVisibility);
        Assert.False(view.CanContentScroll);
        Assert.False(view.IsDeferredScrollingEnabled);
    }

    [Fact]
    public void Attached_dps_register_with_inherits_metadata()
    {
        var horizontal = DependencyProperty.FromName(typeof(ScrollViewer), "HorizontalScrollBarVisibility");
        var vertical = DependencyProperty.FromName(typeof(ScrollViewer), "VerticalScrollBarVisibility");
        var canScroll = DependencyProperty.FromName(typeof(ScrollViewer), "CanContentScroll");
        var deferred = DependencyProperty.FromName(typeof(ScrollViewer), "IsDeferredScrollingEnabled");
        Assert.NotNull(horizontal);
        Assert.NotNull(vertical);
        Assert.NotNull(canScroll);
        Assert.NotNull(deferred);
        Assert.Same(ScrollViewer.HorizontalScrollBarVisibilityProperty, horizontal);
        Assert.Same(ScrollViewer.VerticalScrollBarVisibilityProperty, vertical);
        Assert.Same(ScrollViewer.CanContentScrollProperty, canScroll);
        Assert.Same(ScrollViewer.IsDeferredScrollingEnabledProperty, deferred);
        Assert.Equal(typeof(ScrollBarVisibility), horizontal!.PropertyType);
        Assert.Equal(typeof(ScrollBarVisibility), vertical!.PropertyType);
        Assert.Equal(typeof(bool), canScroll!.PropertyType);
        Assert.Equal(typeof(bool), deferred!.PropertyType);
        foreach (var dp in new[] { horizontal, vertical, canScroll, deferred })
            Assert.True(((FrameworkPropertyMetadata)dp!.DefaultMetadata).Inherits, $"{dp.Name} must inherit to scrollable descendants like WPF.");
        Assert.Equal(ScrollBarVisibility.Disabled, horizontal.DefaultValue);
        Assert.Equal(ScrollBarVisibility.Visible, vertical.DefaultValue);
        Assert.Equal(false, canScroll.DefaultValue);
        Assert.Equal(false, deferred.DefaultValue);
    }

    [Fact]
    public void Set_routes_through_dp_and_reads_back()
    {
        var box = new TextBox();
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        Assert.Equal(ScrollBarVisibility.Auto, ScrollViewer.GetVerticalScrollBarVisibility(box));
        Assert.Equal(ScrollBarVisibility.Auto, box.GetValue(ScrollViewer.VerticalScrollBarVisibilityProperty));

        var view = new ScrollViewer();
        view.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        Assert.Equal(ScrollBarVisibility.Auto, ScrollViewer.GetHorizontalScrollBarVisibility(view));
        view.CanContentScroll = true;
        Assert.True(ScrollViewer.GetCanContentScroll(view));
        view.IsDeferredScrollingEnabled = true;
        Assert.True(ScrollViewer.GetIsDeferredScrollingEnabled(view));
    }

    [Fact]
    public void Value_inherits_to_scrollable_descendants()
    {
        var root = new StackPanel();
        var box = new TextBox();
        root.AddChild(box);

        ScrollViewer.SetVerticalScrollBarVisibility(root, ScrollBarVisibility.Auto);
        Assert.Equal(ScrollBarVisibility.Auto, ScrollViewer.GetVerticalScrollBarVisibility(box));

        // A local value wins over the inherited one.
        box.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        Assert.Equal(ScrollBarVisibility.Hidden, ScrollViewer.GetVerticalScrollBarVisibility(box));

        // Clearing the local value falls back to the ancestor.
        box.ClearValue(ScrollViewer.VerticalScrollBarVisibilityProperty);
        Assert.Equal(ScrollBarVisibility.Auto, ScrollViewer.GetVerticalScrollBarVisibility(box));

        // No value anywhere in the chain yields the WPF default.
        var lone = new TextBox();
        Assert.Equal(ScrollBarVisibility.Visible, ScrollViewer.GetVerticalScrollBarVisibility(lone));
    }

    [Fact]
    public void Horizontal_gate_follows_effective_value()
    {
        var view = new ScrollViewer();
        Assert.Equal(ScrollBarVisibility.Disabled, view.HorizontalScrollBarVisibility);
        var root = new StackPanel();
        root.AddChild(view);
        ScrollViewer.SetHorizontalScrollBarVisibility(root, ScrollBarVisibility.Auto);
        Assert.Equal(ScrollBarVisibility.Auto, view.HorizontalScrollBarVisibility);
    }

    [Fact]
    public void Xaml_attribute_attached_visibility_lands()
    {
        var root = (StackPanel)XamlLoader.Load(
            $"<StackPanel xmlns=\"{PresentationNs}\">" +
            "<TextBox ScrollViewer.VerticalScrollBarVisibility=\"Auto\" " +
            "ScrollViewer.HorizontalScrollBarVisibility=\"Hidden\" " +
            "ScrollViewer.CanContentScroll=\"True\" />" +
            "</StackPanel>").Root;
        var box = Assert.IsType<TextBox>(Assert.Single(root.LogicalChildren));
        Assert.Equal(ScrollBarVisibility.Auto, ScrollViewer.GetVerticalScrollBarVisibility(box));
        Assert.Equal(ScrollBarVisibility.Hidden, ScrollViewer.GetHorizontalScrollBarVisibility(box));
        Assert.True(ScrollViewer.GetCanContentScroll(box));
    }

    [Fact]
    public void Xaml_attribute_rejects_bad_visibility_value()
    {
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            $"<StackPanel xmlns=\"{PresentationNs}\">" +
            "<TextBox ScrollViewer.VerticalScrollBarVisibility=\"Sideways\" />" +
            "</StackPanel>"));
        Assert.Contains("ScrollBarVisibility", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CodeTextBox_style_shape_applies_to_textbox()
    {
        // The exact staged OxyPlot CodeTextBox setter block (attached
        // ScrollViewer setter beside plain CLR setters, explicit key,
        // applied through {StaticResource}).
        var window = Assert.IsType<Window>(XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Window.Resources>" +
            "<Style TargetType=\"TextBox\" x:Key=\"CodeTextBox\">" +
            "<Setter Property=\"AcceptsReturn\" Value=\"True\" />" +
            "<Setter Property=\"FontFamily\" Value=\"Consolas\" />" +
            "<Setter Property=\"BorderThickness\" Value=\"0\" />" +
            "<Setter Property=\"ScrollViewer.VerticalScrollBarVisibility\" Value=\"Auto\" />" +
            "</Style>" +
            "</Window.Resources>" +
            "<TextBox Style=\"{StaticResource CodeTextBox}\" />" +
            "</Window>").Root);
        var box = Assert.IsType<TextBox>(window.Content);
        Assert.True(box.AcceptsReturn);
        Assert.Equal(ScrollBarVisibility.Auto, ScrollViewer.GetVerticalScrollBarVisibility(box));
        Assert.Equal(ScrollBarVisibility.Auto, box.GetValue(ScrollViewer.VerticalScrollBarVisibilityProperty));
    }

    [Fact]
    public void Implicit_scrollviewer_style_setter_applies()
    {
        var window = Assert.IsType<Window>(XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\">" +
            "<Window.Resources>" +
            "<Style TargetType=\"ScrollViewer\">" +
            "<Setter Property=\"ScrollViewer.HorizontalScrollBarVisibility\" Value=\"Auto\" />" +
            "</Style>" +
            "</Window.Resources>" +
            "<ScrollViewer />" +
            "</Window>").Root);
        var view = Assert.IsType<ScrollViewer>(window.Content);
        Assert.Equal(ScrollBarVisibility.Auto, view.HorizontalScrollBarVisibility);
    }

    [Fact]
    public void Template_attached_visibility_parses()
    {
        var window = Assert.IsType<Window>(XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Window.Resources>" +
            "<Style TargetType=\"{x:Type Button}\">" +
            "<Setter Property=\"Template\">" +
            "<Setter.Value>" +
            "<ControlTemplate>" +
            "<StackPanel ScrollViewer.VerticalScrollBarVisibility=\"Auto\" />" +
            "</ControlTemplate>" +
            "</Setter.Value>" +
            "</Setter>" +
            "</Style>" +
            "</Window.Resources>" +
            "<Button Content=\"Hi\" />" +
            "</Window>").Root);
        var button = Assert.IsType<Button>(window.Content);
        button.ApplyTemplate();
        var panel = Assert.IsType<StackPanel>(button.TemplateRoot);
        Assert.Equal(ScrollBarVisibility.Auto, ScrollViewer.GetVerticalScrollBarVisibility(panel));
    }

    [Fact]
    public void Staged_MainWindow_xaml_advances_past_scrollviewer_setter()
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
            // Slice-45 pinned Unknown element <PlotView> at line 116 here;
            // slice-46 clears lines 116-118, so the whole file loads.
            var root = Assert.IsType<Window>(XamlLoader.Load(reader.ReadToEnd()).Root);
            // (slice-46: no ScrollViewer/PlotView failure can remain when the file loads).
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
        }
    }

    [Fact]
    public void Staged_boot_advances_past_scrollviewer()
    {
        Application.Current = null;
        var entry = LoadStagedOxyPlotEntry();
        // Slice-45 pinned the line-116 PlotView failure here; slice-46
        // boots through line 133 (see PlotViewShimTests for live binding
        // pins and the empty-row next blocker).
        var window = WpfAppBoot.Boot(entry);
        Assert.Equal("ExampleBrowser.MainWindow", window.GetType().FullName);
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




