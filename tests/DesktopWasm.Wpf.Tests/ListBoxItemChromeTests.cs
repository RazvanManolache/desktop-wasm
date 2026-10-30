using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Core;
using DesktopWasm.Graphics;
using SkiaSharp;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-42: ListBoxItem is a Control like its sibling item containers, so
/// the Background/BorderBrush/BorderThickness style setters (OxyPlot
/// ExampleBrowser MainWindow.xaml ListboxItemStyle, lines 38-66) resolve and
/// the trigger conditions naming IsSelected/IsMouseOver parse. Rows paint the
/// set fill plus an inner border ring while a null Background keeps the
/// parent selection highlight visible.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class ListBoxItemChromeTests : IDisposable
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
            DependencyProperty.FromName(typeof(ListBoxItem), "Background"));
        Assert.Same(Control.BorderBrushProperty,
            DependencyProperty.FromName(typeof(ListBoxItem), "BorderBrush"));
        Assert.Same(Control.BorderThicknessProperty,
            DependencyProperty.FromName(typeof(ListBoxItem), "BorderThickness"));
        Assert.Same(Control.TemplateProperty,
            DependencyProperty.FromName(typeof(ListBoxItem), "Template"));
        Assert.Equal(typeof(Brush), DependencyProperty.FromName(typeof(ListBoxItem), "Background")!.PropertyType);
        Assert.Equal(typeof(Brush), DependencyProperty.FromName(typeof(ListBoxItem), "BorderBrush")!.PropertyType);
        Assert.Equal(typeof(Thickness), DependencyProperty.FromName(typeof(ListBoxItem), "BorderThickness")!.PropertyType);
    }

    [Fact]
    public void Chrome_defaults_match_an_unset_row()
    {
        var row = new ListBoxItem { Content = "a" };
        Assert.Null(row.Background);
        Assert.Null(row.BorderBrush);
        Assert.Equal(new Thickness(0), row.BorderThickness);
    }

    [Fact]
    public void Chrome_setters_land_and_border_grows_desired_size()
    {
        var row = new ListBoxItem { Content = "a" };
        row.Measure(new WpfSize(200, 100));
        var plain = row.DesiredSize;

        row.Background = new SolidColorBrush(Colors.Red);
        row.BorderBrush = new SolidColorBrush(Colors.Blue);
        row.BorderThickness = new Thickness(2);
        Assert.Equal(new SolidColorBrush(Colors.Red).ToRgba(),
            ((SolidColorBrush)row.Background!).ToRgba());
        Assert.Equal(new Thickness(2), row.BorderThickness);

        row.Measure(new WpfSize(200, 100));
        Assert.Equal(plain.Width + 4, row.DesiredSize.Width);
        Assert.Equal(plain.Height + 4, row.DesiredSize.Height);
    }

    [Fact]
    public void Inherited_foreground_still_drives_row_ink()
    {
        var row = new ListBoxItem { Content = "a" };
        row.Foreground = new SolidColorBrush(Colors.Blue);
        Assert.Equal(new SolidColorBrush(Colors.Blue).ToRgba(), row.ForegroundRgba);
    }

    [Fact]
    public void Xaml_attributes_land_on_rows()
    {
        var window = Assert.IsType<Window>(XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\">" +
            "<ListBox><ListBoxItem Background=\"Red\" BorderBrush=\"Blue\" BorderThickness=\"2\">a</ListBoxItem>" +
            "</ListBox></Window>").Root);
        var row = Assert.IsType<ListBoxItem>(Assert.IsType<ListBox>(window.Content).Items[0]);
        Assert.Equal(new SolidColorBrush(Colors.Red).ToRgba(),
            ((SolidColorBrush)row.Background!).ToRgba());
        Assert.Equal(new SolidColorBrush(Colors.Blue).ToRgba(),
            ((SolidColorBrush)row.BorderBrush!).ToRgba());
        Assert.Equal(new Thickness(2), row.BorderThickness);
    }

    [Fact]
    public void Implicit_style_with_selected_trigger_parses_and_applies()
    {
        var window = Assert.IsType<Window>(XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\">" +
            "<Window.Resources><Style TargetType=\"ListBoxItem\">" +
            "<Setter Property=\"Background\" Value=\"Red\"/>" +
            "<Setter Property=\"BorderBrush\" Value=\"Blue\"/>" +
            "<Setter Property=\"BorderThickness\" Value=\"2\"/>" +
            "<Style.Triggers>" +
            "<Trigger Property=\"IsSelected\" Value=\"True\">" +
            "<Setter Property=\"Background\" Value=\"Green\"/>" +
            "</Trigger>" +
            "<MultiTrigger>" +
            "<MultiTrigger.Conditions>" +
            "<Condition Property=\"IsMouseOver\" Value=\"True\"/>" +
            "<Condition Property=\"IsSelected\" Value=\"False\"/>" +
            "</MultiTrigger.Conditions>" +
            "<Setter Property=\"Background\" Value=\"Yellow\"/>" +
            "</MultiTrigger>" +
            "</Style.Triggers>" +
            "</Style></Window.Resources>" +
            "<ListBox><ListBoxItem Content=\"a\"/><ListBoxItem Content=\"b\"/></ListBox>" +
            "</Window>").Root);
        var list = Assert.IsType<ListBox>(window.Content);
        Assert.Equal(2, list.Items.Count);
        foreach (var row in list.Items)
        {
            Assert.Equal(new SolidColorBrush(Colors.Red).ToRgba(),
                ((SolidColorBrush)row.Background!).ToRgba());
            Assert.Equal(new Thickness(2), row.BorderThickness);
        }
    }

    [Fact]
    public void Row_fill_and_ring_paint_in_skia_pixels()
    {
        Application.Current = new BootProbe.ProbeApp();
        var window = new Window();
        var list = new ListBox { Width = 400 };
        var filled = new ListBoxItem { Content = "filled", Background = new SolidColorBrush(Colors.Red) };
        var ringed = new ListBoxItem
        {
            Content = "ringed",
            BorderBrush = new SolidColorBrush(Colors.Blue),
            BorderThickness = new Thickness(2),
        };
        list.AddChild(filled);
        list.AddChild(ringed);
        window.Content = list;
        using var surface = new DesktopWasm.Graphics.SkiaSurface(400, 600);
        var mil = new WasmMilCore(surface);
        var commands = mil.Render(window, new WpfSize(400, 600), new WpfRect(0, 0, 400, 600));
        mil.CommitChannel(commands);

        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        var rowH = filled.ArrangedRect.Height;
        var fillCenter = bitmap.GetPixel(200, (int)(filled.ArrangedRect.Y + rowH / 2));
        Assert.True(fillCenter.Red > 200 && fillCenter.Green < 80 && fillCenter.Blue < 80,
            $"filled row center {fillCenter} is not the red fill");
        var ringEdge = bitmap.GetPixel((int)(ringed.ArrangedRect.X + 1),
            (int)(ringed.ArrangedRect.Y + ringed.ArrangedRect.Height / 2));
        Assert.True(ringEdge.Blue > 200 && ringEdge.Red < 80 && ringEdge.Green < 80,
            $"ringed row edge {ringEdge} is not the blue ring");
        var ringCenter = bitmap.GetPixel(200,
            (int)(ringed.ArrangedRect.Y + ringed.ArrangedRect.Height / 2));
        Assert.True(ringCenter.Red > 200 && ringCenter.Green > 200 && ringCenter.Blue > 200,
            $"ringed row center {ringCenter} is not the list background (transparent fill must show through)");
    }

    [Fact]
    public void Selected_row_without_background_keeps_parent_highlight()
    {
        Application.Current = new BootProbe.ProbeApp();
        var window = new Window();
        var list = new ListBox { Width = 400 };
        list.AddChild(new ListBoxItem { Content = "a" });
        list.AddChild(new ListBoxItem { Content = "b" });
        window.Content = list;
        using var surface = new DesktopWasm.Graphics.SkiaSurface(400, 600);
        var mil = new WasmMilCore(surface);
        list.SelectedIndex = 0;
        var commands = mil.Render(window, new WpfSize(400, 600), new WpfRect(0, 0, 400, 600));
        mil.CommitChannel(commands);

        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        var row = list.Items[0];
        Assert.True(row.IsSelected);
        var highlight = bitmap.GetPixel(200, (int)(row.ArrangedRect.Y + row.ArrangedRect.Height / 2));
        Assert.True(highlight.Blue > 150 && highlight.Red < 150,
            $"selected row center {highlight} is not the parent highlight (transparent rows must not cover it)");
    }

    [Fact]
    public void Staged_MainWindow_xaml_inflates_past_style_block()
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
                Assert.DoesNotContain("not a settable property of <ListBoxItem>", ex.Message, StringComparison.Ordinal);
                Assert.DoesNotContain("is not a property of <ListBoxItem>", ex.Message, StringComparison.Ordinal);
                Assert.True(ex.Line > 66, $"staged MainWindow.xaml must advance past line 66, failed at line {ex.Line}: {ex.Message}");
            }
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
        }
    }

    [Fact]
    public void Staged_boot_advances_past_style_block()
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
            Assert.DoesNotContain(messages, m => m.Contains("not a settable property of <ListBoxItem>", StringComparison.Ordinal));
            Assert.DoesNotContain(messages, m => m.Contains("is not a property of <ListBoxItem>", StringComparison.Ordinal));
            var xamlFailure = messages.FirstOrDefault(m => m.Contains(nameof(XamlLoadException), StringComparison.Ordinal));
            if (xamlFailure is not null)
                Assert.DoesNotContain("line 66", xamlFailure, StringComparison.Ordinal);
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
