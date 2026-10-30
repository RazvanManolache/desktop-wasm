using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Core;
using DesktopWasm.Graphics;
using SkiaSharp;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-43: ListBox is a Control like its sibling ListBoxItem, so the
/// Background/BorderBrush/BorderThickness/Template style setters resolve
/// through the inherited Control dependency properties (OxyPlot
/// ExampleBrowser MainWindow.xaml line 75 sets BorderThickness="0,0,0,1"
/// on the list itself). A null Background keeps the default white fill
/// and a null BorderBrush the default gray frame; an explicitly set
/// BorderThickness wins per edge while the default zero value keeps the
/// legacy 1px frame, so default-measure/layout/paint stay pixel-identical.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class ListBoxChromeTests : IDisposable
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
            DependencyProperty.FromName(typeof(ListBox), "Background"));
        Assert.Same(Control.BorderBrushProperty,
            DependencyProperty.FromName(typeof(ListBox), "BorderBrush"));
        Assert.Same(Control.BorderThicknessProperty,
            DependencyProperty.FromName(typeof(ListBox), "BorderThickness"));
        Assert.Same(Control.TemplateProperty,
            DependencyProperty.FromName(typeof(ListBox), "Template"));
        Assert.Equal(typeof(Brush), DependencyProperty.FromName(typeof(ListBox), "Background")!.PropertyType);
        Assert.Equal(typeof(Brush), DependencyProperty.FromName(typeof(ListBox), "BorderBrush")!.PropertyType);
        Assert.Equal(typeof(Thickness), DependencyProperty.FromName(typeof(ListBox), "BorderThickness")!.PropertyType);
    }

    [Fact]
    public void Chrome_defaults_match_the_legacy_host()
    {
        var list = new ListBox();
        Assert.Null(list.Background);
        Assert.Null(list.BorderBrush);
        Assert.Equal(new Thickness(0), list.BorderThickness);

        // Default measure/layout stay legacy: rows sit inside the 1px frame.
        list.AddChild(new ListBoxItem { Content = "a" });
        list.AddChild(new ListBoxItem { Content = "b" });
        list.Measure(new WpfSize(400, 1000));
        var rowH = list.Items[0].DesiredSize.Height;
        Assert.Equal(2 * rowH, list.DesiredSize.Height);

        var window = new Window { Content = list };
        window.Measure(new WpfSize(400, 1000));
        window.Arrange(new WpfRect(0, 0, 400, 1000));
        Assert.Equal(1, list.Items[0].ArrangedRect.Y);
        Assert.Equal(398, list.Items[0].ArrangedRect.Width);
    }

    [Fact]
    public void Chrome_setters_land_and_border_grows_desired_size()
    {
        var list = new ListBox();
        list.AddChild(new ListBoxItem { Content = "a" });
        list.Measure(new WpfSize(400, 1000));
        var plain = list.DesiredSize;

        list.Background = new SolidColorBrush(Colors.Red);
        list.BorderBrush = new SolidColorBrush(Colors.Blue);
        list.BorderThickness = new Thickness(2);
        Assert.Equal(new SolidColorBrush(Colors.Red).ToRgba(),
            ((SolidColorBrush)list.Background!).ToRgba());
        Assert.Equal(new Thickness(2), list.BorderThickness);

        list.Measure(new WpfSize(400, 1000));
        Assert.Equal(plain.Width + 4, list.DesiredSize.Width);
        Assert.Equal(plain.Height + 4, list.DesiredSize.Height);
    }

    [Fact]
    public void Xaml_attributes_land_on_the_list()
    {
        var window = Assert.IsType<Window>(XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\">" +
            "<ListBox Background=\"Red\" BorderBrush=\"Blue\" BorderThickness=\"0,0,0,1\" Padding=\"4\">" +
            "<ListBoxItem>a</ListBoxItem></ListBox>" +
            "</Window>").Root);
        var list = Assert.IsType<ListBox>(window.Content);
        Assert.Equal(new SolidColorBrush(Colors.Red).ToRgba(),
            ((SolidColorBrush)list.Background!).ToRgba());
        Assert.Equal(new SolidColorBrush(Colors.Blue).ToRgba(),
            ((SolidColorBrush)list.BorderBrush!).ToRgba());
        Assert.Equal(new Thickness(0, 0, 0, 1), list.BorderThickness);
        Assert.Equal(new Thickness(4), list.Padding);
    }

    [Fact]
    public void Implicit_style_with_chrome_setters_applies()
    {
        var window = Assert.IsType<Window>(XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\">" +
            "<Window.Resources><Style TargetType=\"ListBox\">" +
            "<Setter Property=\"Background\" Value=\"Red\"/>" +
            "<Setter Property=\"BorderBrush\" Value=\"Blue\"/>" +
            "<Setter Property=\"BorderThickness\" Value=\"3\"/>" +
            "</Style></Window.Resources>" +
            "<ListBox><ListBoxItem Content=\"a\"/></ListBox>" +
            "</Window>").Root);
        var list = Assert.IsType<ListBox>(window.Content);
        Assert.Equal(new SolidColorBrush(Colors.Red).ToRgba(),
            ((SolidColorBrush)list.Background!).ToRgba());
        Assert.Equal(new Thickness(3), list.BorderThickness);
    }

    [Fact]
    public void Non_uniform_border_insets_rows_per_edge()
    {
        // OxyPlot MainWindow.xaml line 75: bottom-only border.
        var window = new Window();
        var list = new ListBox { Width = 400, BorderThickness = new Thickness(0, 0, 0, 1) };
        list.AddChild(new ListBoxItem { Content = "a" });
        window.Content = list;
        window.Measure(new WpfSize(400, 1000));
        window.Arrange(new WpfRect(0, 0, 400, 1000));
        Assert.Equal(0, list.Items[0].ArrangedRect.Y);
        Assert.Equal(400, list.Items[0].ArrangedRect.Width);
    }

    [Fact]
    public void Bottom_only_frame_paints_in_skia_pixels()
    {
        Application.Current = new BootProbe.ProbeApp();
        var window = new Window();
        var list = new ListBox { Width = 400, BorderThickness = new Thickness(0, 0, 0, 1) };
        list.AddChild(new ListBoxItem { Content = "a" });
        window.Content = list;
        using var surface = new DesktopWasm.Graphics.SkiaSurface(400, 600);
        var mil = new WasmMilCore(surface);
        var commands = mil.Render(window, new WpfSize(400, 600), new WpfRect(0, 0, 400, 600));
        mil.CommitChannel(commands);

        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        var bottom = bitmap.GetPixel(200, 599);
        Assert.True(bottom.Green < 150 && bottom.Red < 150 && bottom.Blue < 150,
            $"list bottom edge {bottom} is not the default gray frame");
        var left = bitmap.GetPixel(0, 300);
        Assert.True(left.Red > 200 && left.Green > 200 && left.Blue > 200,
            $"list left edge {left} is not the default white fill (zero edges paint no ring)");
    }

    [Fact]
    public void Set_background_fills_the_list_in_skia_pixels()
    {
        Application.Current = new BootProbe.ProbeApp();
        var window = new Window();
        var list = new ListBox
        {
            Width = 400,
            Background = new SolidColorBrush(Colors.Red),
            BorderBrush = new SolidColorBrush(Colors.Blue),
            BorderThickness = new Thickness(2),
        };
        list.AddChild(new ListBoxItem { Content = "a" });
        window.Content = list;
        using var surface = new DesktopWasm.Graphics.SkiaSurface(400, 600);
        var mil = new WasmMilCore(surface);
        var commands = mil.Render(window, new WpfSize(400, 600), new WpfRect(0, 0, 400, 600));
        mil.CommitChannel(commands);

        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        var edge = bitmap.GetPixel(0, 300);
        Assert.True(edge.Blue > 200 && edge.Red < 80 && edge.Green < 80,
            $"list left edge {edge} is not the blue frame");
        var fill = bitmap.GetPixel(200, 590);
        Assert.True(fill.Red > 200 && fill.Green < 80 && fill.Blue < 80,
            $"list interior {fill} is not the red fill");
    }

    [Fact]
    public void Staged_MainWindow_xaml_inflates_past_list_chrome()
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
                XamlLoader.Load(reader.ReadToEnd());
            }
            catch (XamlLoadException ex)
            {
                Assert.DoesNotContain("'BorderThickness' is not valid on <ListBox>", ex.Message, StringComparison.Ordinal);
                Assert.True(ex.Line > 75, $"staged MainWindow.xaml must advance past line 75, failed at line {ex.Line}: {ex.Message}");
            }
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
        }
    }

    [Fact]
    public void Staged_boot_advances_past_list_chrome()
    {
        Application.Current = null;
        var entry = LoadStagedOxyPlotEntry();
        try
        {
            WpfAppBoot.Boot(entry);
        }
        catch (Exception ex)
        {
            var messages = new List<string>();
            for (var current = ex; current is not null; current = current.InnerException)
                messages.Add(current.GetType().FullName + ": " + current.Message.Split('\n')[0]);
            Assert.DoesNotContain(messages, m => m.Contains("'BorderThickness' is not valid on <ListBox>", StringComparison.Ordinal));
            var xamlFailure = messages.FirstOrDefault(m => m.Contains(nameof(XamlLoadException), StringComparison.Ordinal));
            if (xamlFailure is not null)
                Assert.DoesNotContain("line 75", xamlFailure, StringComparison.Ordinal);
        }
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
