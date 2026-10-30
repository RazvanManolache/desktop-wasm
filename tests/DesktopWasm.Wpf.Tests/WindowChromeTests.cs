using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Core;
using DesktopWasm.Graphics;
using SkiaSharp;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Stand-in for generated App codebehind (implicit Window style host).
/// </summary>
public sealed class WindowChromeApp : Application
{
    public void InitializeComponent() =>
        LoadComponent(this, new Uri("Boot/WindowChromeApp.xaml", UriKind.Relative));
}

/// <summary>
/// Stand-in for generated Window codebehind (chrome attribute host).
/// </summary>
public sealed class WindowChromeMain : Window
{
    internal Grid? ChromeSurface;

    public WindowChromeMain() => InitializeComponent();

    public void InitializeComponent() =>
        Application.LoadComponent(this, new Uri("Boot/WindowChromeWindow.xaml", UriKind.Relative));
}

/// <summary>
/// Slice-57: Window is a ContentControl like real WPF, so the Control chrome
/// (PC Space Map App.xaml's implicit Window style: Background/Foreground/
/// FontFamily setters, plus MainWindow.xaml's Background/Foreground
/// attributes) resolves through the inherited Control dependency properties
/// with WPF-identical sharing (DependencyProperty.FromName resolves to
/// Control's instances). The set fill paints in place of the legacy white;
/// a null Background keeps the legacy white/glass pixel-identical.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class WindowChromeTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private readonly Application? _previous = Application.Current;

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    [Fact]
    public void Chrome_dependency_properties_resolve_through_Control()
    {
        Assert.Same(Control.BackgroundProperty,
            DependencyProperty.FromName(typeof(Window), "Background"));
        Assert.Same(Control.ForegroundProperty,
            DependencyProperty.FromName(typeof(Window), "Foreground"));
        Assert.Same(Control.BorderBrushProperty,
            DependencyProperty.FromName(typeof(Window), "BorderBrush"));
        Assert.Same(Control.BorderThicknessProperty,
            DependencyProperty.FromName(typeof(Window), "BorderThickness"));
        Assert.Same(Control.TemplateProperty,
            DependencyProperty.FromName(typeof(Window), "Template"));
        Assert.Same(Control.FontFamilyProperty,
            DependencyProperty.FromName(typeof(Window), "FontFamily"));
        Assert.Same(Control.FontSizeProperty,
            DependencyProperty.FromName(typeof(Window), "FontSize"));
        Assert.Equal(typeof(Brush), DependencyProperty.FromName(typeof(Window), "Background")!.PropertyType);
        Assert.Equal(typeof(Thickness), DependencyProperty.FromName(typeof(Window), "BorderThickness")!.PropertyType);
    }

    [Fact]
    public void Window_is_a_content_control()
    {
        Assert.IsAssignableFrom<ContentControl>(new Window());
        Assert.IsAssignableFrom<Control>(new Window());
        Assert.IsAssignableFrom<FrameworkElement>(new Window());
    }

    [Fact]
    public void Chrome_defaults_match_an_unset_window()
    {
        var window = new Window();
        Assert.Null(window.Background);
        Assert.Null(window.Foreground);
        Assert.Null(window.BorderBrush);
        Assert.Equal(new Thickness(0), window.BorderThickness);
        Assert.Null(window.Content);
    }

    [Fact]
    public void Content_hosts_visuals_and_keeps_legacy_layout()
    {
        var window = new Window();
        var panel = new StackPanel();
        window.Content = panel;
        Assert.Same(panel, window.Content);
        Assert.Same(window, panel.Parent);
        window.Measure(new WpfSize(800, 600));
        window.Arrange(new WpfRect(0, 0, 800, 600));
        Assert.Equal(new WpfRect(0, 0, 800, 600), ((FrameworkElement)window.Content).RenderRect);

        var empty = new Window();
        empty.Measure(new WpfSize(800, 600));
        Assert.Equal(800.0, empty.DesiredSize.Width);
        Assert.Equal(600.0, empty.DesiredSize.Height);
    }

    [Fact]
    public void Xaml_attributes_land_on_window()
    {
        var window = Assert.IsType<Window>(XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\" Title=\"T\" Background=\"Red\" " +
            "Foreground=\"Blue\" BorderThickness=\"2\" FontSize=\"20\">" +
            "<Grid/></Window>").Root);
        Assert.Equal(new SolidColorBrush(Colors.Red).ToRgba(),
            ((SolidColorBrush)window.Background!).ToRgba());
        Assert.Equal(new SolidColorBrush(Colors.Blue).ToRgba(),
            ((SolidColorBrush)window.Foreground!).ToRgba());
        Assert.Equal(new Thickness(2), window.BorderThickness);
        Assert.Equal(20.0, window.FontSize);
        Assert.IsType<Grid>(window.Content);
    }

    [Fact]
    public void Implicit_window_style_parses_and_applies()
    {
        // The style parses inline (like App.xaml) and applies to the window
        // on demand: the loader applies implicit styles eagerly at creation,
        // so a root never sees its own not-yet-parsed Resources (pre-existing
        // behavior for every type); application resources drive the real flow.
        var window = Assert.IsType<Window>(XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Window.Resources>" +
            "<SolidColorBrush x:Key=\"ChromeBackground\" Color=\"#09111F\"/>" +
            "<Style TargetType=\"Window\">" +
            "<Setter Property=\"Background\" Value=\"{StaticResource ChromeBackground}\"/>" +
            "<Setter Property=\"Foreground\" Value=\"Red\"/>" +
            "<Setter Property=\"FontFamily\" Value=\"Segoe UI\"/>" +
            "</Style>" +
            "</Window.Resources>" +
            "<Grid/></Window>").Root);
        var style = Assert.IsType<Style>(window.Resources[typeof(Window)]);
        style.ApplyTo(window);
        Assert.Equal(new SolidColorBrush(Color.FromRgb(0x09, 0x11, 0x1F)).ToRgba(),
            ((SolidColorBrush)window.Background!).ToRgba());
        Assert.Equal(new SolidColorBrush(Colors.Red).ToRgba(),
            ((SolidColorBrush)window.Foreground!).ToRgba());
        Assert.Equal(new FontFamily("Segoe UI"), window.FontFamily);
    }

    [Fact]
    public void Hydrate_copies_chrome_to_the_component()
    {
        Application.Current = new WindowChromeApp();
        ((WindowChromeApp)Application.Current).InitializeComponent();
        var window = new WindowChromeMain();
        Assert.Equal(new SolidColorBrush(Color.FromRgb(0x09, 0x11, 0x1F)).ToRgba(),
            ((SolidColorBrush)window.Background!).ToRgba());
        Assert.Equal(new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)).ToRgba(),
            ((SolidColorBrush)window.Foreground!).ToRgba());
        Assert.Equal(new FontFamily("Segoe UI"), window.FontFamily);
        Assert.IsType<Grid>(window.Content);
    }

    [Fact]
    public void Window_fill_and_ring_paint_in_skia_pixels()
    {
        Application.Current = new BootProbe.ProbeApp();
        var filled = new Window
        {
            Width = 400,
            Height = 300,
            Background = new SolidColorBrush(Colors.Red),
            Content = new Grid(),
        };
        using var surface = new DesktopWasm.Graphics.SkiaSurface(400, 300);
        var mil = new WasmMilCore(surface);
        mil.CommitChannel(mil.Render(filled, new WpfSize(400, 300), new WpfRect(0, 0, 400, 300)));

        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        var center = bitmap.GetPixel(200, 150);
        Assert.True(center.Red > 200 && center.Green < 80 && center.Blue < 80,
            $"filled window center {center} is not the red fill");

        var ringed = new Window
        {
            Width = 400,
            Height = 300,
            BorderBrush = new SolidColorBrush(Colors.Blue),
            BorderThickness = new Thickness(3),
            Content = new Grid(),
        };
        using var ringSurface = new DesktopWasm.Graphics.SkiaSurface(400, 300);
        var ringMil = new WasmMilCore(ringSurface);
        ringMil.CommitChannel(ringMil.Render(ringed, new WpfSize(400, 300), new WpfRect(0, 0, 400, 300)));
        using var ringBitmap = SKBitmap.FromImage(ringSurface.Snapshot());
        var edge = ringBitmap.GetPixel(1, 150);
        Assert.True(edge.Blue > 200 && edge.Red < 80 && edge.Green < 80,
            $"ringed window edge {edge} is not the blue ring");
        var legacy = ringBitmap.GetPixel(200, 150);
        Assert.True(legacy.Red > 200 && legacy.Green > 200 && legacy.Blue > 200,
            $"ringed window center {legacy} is not the legacy white fill");
    }

    [Fact]
    public void Unset_window_paints_legacy_white()
    {
        Application.Current = new BootProbe.ProbeApp();
        var window = new Window { Width = 400, Height = 300, Content = new Grid() };
        using var surface = new DesktopWasm.Graphics.SkiaSurface(400, 300);
        var mil = new WasmMilCore(surface);
        mil.CommitChannel(mil.Render(window, new WpfSize(400, 300), new WpfRect(0, 0, 400, 300)));

        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        var center = bitmap.GetPixel(200, 150);
        Assert.True(center.Red > 200 && center.Green > 200 && center.Blue > 200,
            $"unset window center {center} is not the legacy white fill");
    }

    [Fact]
    public void Staged_pcspacemap_app_parses_past_the_window_style()
    {
        // Slice-59 advance: the template Focusable/ClickMode/Popup and
        // trigger-coercion blockers are gone, so App.xaml parses fully and
        // the implicit Window style lands.
        Application.Current = null;
        var entry = LoadStagedPcSpaceMapEntry();
        var previous = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = entry;
        try
        {
            var appType = entry.GetType("PcSpaceMap.App");
            Assert.NotNull(appType);
            var app = Activator.CreateInstance(appType!);
            Assert.NotNull(app);
            appType!.GetMethod("InitializeComponent",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(app, null);
            var application = Assert.IsAssignableFrom<Application>(app);
            Assert.IsType<Style>(application.Resources.TryGetValue(typeof(Window), out var style) ? style : null);
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
            Application.Current = null;
        }
    }

    [Fact]
    public void Staged_main_window_hydrates_past_window_chrome()
    {
        // Slice-59 advance: MainWindow hydrates with its chrome, named
        // fields, ComboBox parity members, and Treemap event wires.
        Application.Current = null;
        var entry = LoadStagedPcSpaceMapEntry();
        var previous = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = entry;
        try
        {
            var appType = entry.GetType("PcSpaceMap.App");
            Assert.NotNull(appType);
            var app = Activator.CreateInstance(appType!);
            Assert.NotNull(app);
            appType!.GetMethod("InitializeComponent",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(app, null);
            var mainType = entry.GetType("PcSpaceMap.MainWindow");
            Assert.NotNull(mainType);
            var window = Activator.CreateInstance(mainType!) as Window;
            Assert.NotNull(window);
            Assert.Equal("PC Space Map", window!.Title);
            Assert.IsType<Grid>(window.Content);
            Assert.Equal(1520, window.Width);
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
            Application.Current = null;
        }
    }

    private static List<string> Chain(Exception ex)
    {
        var messages = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
            messages.Add(current.GetType().FullName + ": " + current.Message.Split('\n')[0]);
        return messages;
    }

    [Fact]
    public void Staged_boot_advances_past_window_chrome()
    {
        // Slice-59 advance: staged boot returns the live MainWindow with
        // code-driven OnStartup (Closing/Show/ShowActivated wired, first
        // shown window becomes MainWindow).
        Application.Current = null;
        var entry = LoadStagedPcSpaceMapEntry();
        try
        {
            var window = WpfAppBoot.Boot(entry);
            Assert.Equal("PcSpaceMap.MainWindow", window.GetType().FullName);
            Assert.True(window.IsVisible);
            Assert.Same(window, Application.Current!.MainWindow);
        }
        finally
        {
            Application.Current = null;
        }
    }

    private static readonly object StagedGate = new();
    private static Assembly? _stagedEntry;

    private static Assembly LoadStagedPcSpaceMapEntry()
    {
        lock (StagedGate)
        {
            var hit = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "PC Space Map", StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                VerifyStagedHashes();
                return hit;
            }
            if (_stagedEntry is not null) return _stagedEntry;
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(System.IO.Path.Combine(
                directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot",
                "apps", "pc-space-map", "app-manifest.json")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            var apps = System.IO.Path.Combine(directory!.FullName, "src",
                "DesktopWasm.BrowserHost", "wwwroot", "apps", "pc-space-map");
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
                .First(a => string.Equals(a.GetName().Name, "PC Space Map", StringComparison.OrdinalIgnoreCase));
            return _stagedEntry;
        }
    }

    private static void VerifyStagedHashes()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(
            directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot",
            "apps", "pc-space-map", "app-manifest.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var apps = System.IO.Path.Combine(directory!.FullName, "src",
            "DesktopWasm.BrowserHost", "wwwroot", "apps", "pc-space-map");
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
