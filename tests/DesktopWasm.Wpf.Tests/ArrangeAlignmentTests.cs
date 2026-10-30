using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Core;
using DesktopWasm.Wpf;

namespace DesktopWasm.Wpf.Tests;

// Slice-54 regression: non-Stretch alignment without an explicit size was
// not honored in arrange. FrameworkElement.ClampArrange returned the whole
// slot for every Auto dimension, so the converted OxyPlot ExampleBrowser
// renderer panel (StackPanel, HorizontalAlignment Right +
// VerticalAlignment Bottom, no explicit size, 354x30 desired) arranged over
// the entire right cell (853x720) and overlapped the plot instead of
// docking at the cell's bottom-right; the 1280x720 window additionally
// overflowed the 800x600 host scene on the right/bottom. The fix sizes
// Auto + non-Stretch dimensions to content (DesiredSize clamped into the
// slot) with the alignment offset docking them, restores the WPF Stretch
// default for VerticalAlignment, and confines top-level windows to the
// host-provided slot.
[ResetAmbientState]
public sealed class ArrangeAlignmentTests
{
    private const double SceneW = 800;
    private const double SceneH = 600;

    private static Border AutoBox(double w = 120, double h = 40)
    {
        var outer = new Border();
        outer.AddChild(new Border { Width = w, Height = h });
        return outer;
    }

    private static WpfRect Arrange(Border box, double x, double y, double w, double h)
    {
        box.Measure(new WpfSize(w, h));
        box.Arrange(new WpfRect(x, y, w, h));
        return box.RenderRect;
    }

    [Fact]
    public void Alignment_defaults_match_wpf_stretch()
    {
        var text = new TextBlock { Text = "Hi" };
        Assert.Equal(HorizontalAlignment.Stretch, text.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Stretch, text.VerticalAlignment);
    }

    [Fact]
    public void Stretch_default_fills_slot()
    {
        var box = AutoBox();
        var rect = Arrange(box, 0, 0, 400, 300);
        Assert.Equal(new WpfRect(0, 0, 400, 300), rect);
    }

    [Fact]
    public void Bottom_right_auto_docks_to_corner()
    {
        var box = AutoBox();
        box.HorizontalAlignment = HorizontalAlignment.Right;
        box.VerticalAlignment = VerticalAlignment.Bottom;
        var rect = Arrange(box, 0, 0, 400, 300);
        Assert.Equal(new WpfRect(280, 260, 120, 40), rect);
    }

    [Fact]
    public void Center_auto_centers_on_content_size()
    {
        var box = AutoBox();
        box.HorizontalAlignment = HorizontalAlignment.Center;
        box.VerticalAlignment = VerticalAlignment.Center;
        var rect = Arrange(box, 0, 0, 400, 300);
        Assert.Equal(new WpfRect(140, 130, 120, 40), rect);
    }

    [Fact]
    public void Top_left_auto_anchors_at_origin_with_content_size()
    {
        var box = AutoBox();
        box.HorizontalAlignment = HorizontalAlignment.Left;
        box.VerticalAlignment = VerticalAlignment.Top;
        var rect = Arrange(box, 0, 0, 400, 300);
        Assert.Equal(new WpfRect(0, 0, 120, 40), rect);
    }

    [Fact]
    public void Explicit_size_keeps_legacy_offsets()
    {
        var centered = new Border { Width = 120, Height = 40, HorizontalAlignment = HorizontalAlignment.Center };
        Assert.Equal(new WpfRect(140, 0, 120, 40), Arrange(centered, 0, 0, 400, 300));

        // Legacy quirk pin: explicit size + Stretch centers when it fits.
        var stretched = new Border { Width = 120, Height = 40 };
        Assert.Equal(new WpfRect(140, 0, 120, 40), Arrange(stretched, 0, 0, 400, 300));
    }

    [Fact]
    public void Definitionless_grid_overlapping_panel_docks_corner()
    {
        // Mirrors the OxyPlot right cell: a definition-less single-cell
        // grid overlaps same-cell children; the fill child keeps the cell
        // while the Right/Bottom panel docks to its corner.
        var grid = new Grid();
        var fill = AutoBox(400, 300);
        var panel = AutoBox();
        panel.HorizontalAlignment = HorizontalAlignment.Right;
        panel.VerticalAlignment = VerticalAlignment.Bottom;
        grid.AddChild(fill);
        grid.AddChild(panel);
        grid.Measure(new WpfSize(400, 300));
        grid.Arrange(new WpfRect(0, 0, 400, 300));
        Assert.Equal(new WpfRect(0, 0, 400, 300), fill.RenderRect);
        Assert.Equal(new WpfRect(280, 260, 120, 40), panel.RenderRect);
    }

    [Fact]
    public void Oversized_window_confines_to_host_slot()
    {
        var window = new Window { Width = 1280, Height = 720, Content = AutoBox() };
        WasmMilCore.RenderHeadless(window, new WpfSize(SceneW, SceneH));
        Assert.Equal(new WpfRect(0, 0, SceneW, SceneH), window.RenderRect);
        Assert.Equal(new WpfRect(0, 0, SceneW, SceneH), ((FrameworkElement)window.Content!).RenderRect);
    }

    [Fact]
    public void Explicit_window_inside_slot_keeps_its_size()
    {
        // Legacy explicit-size offset pin: Stretch centers a fitting
        // window horizontally (the same quirk plain elements keep).
        var window = new Window { Width = 525, Height = 350, Content = AutoBox() };
        WasmMilCore.RenderHeadless(window, new WpfSize(SceneW, SceneH));
        Assert.Equal(new WpfRect(137.5, 0, 525, 350), window.RenderRect);
    }

    [Fact]
    public void Auto_window_without_explicit_size_fills_slot()
    {
        var window = new Window { Content = AutoBox() };
        WasmMilCore.RenderHeadless(window, new WpfSize(SceneW, SceneH));
        Assert.Equal(new WpfRect(0, 0, SceneW, SceneH), window.RenderRect);
    }
}

[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class StagedArrangeAlignmentTests : IDisposable
{
    private readonly Application? _previous = Application.Current;
    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    [Fact]
    public void Staged_window_confines_to_scene_and_plot_lands_fully_in_scene()
    {
        var window = BootSelected();
        Assert.Equal(new WpfRect(0, 0, 800, 600), window.RenderRect);

        var host = (FrameworkElement)FindByFullName(window, "OxyPlot.Wpf.PlotView")!;
        Assert.Equal(Visibility.Visible, host.Visibility);
        var plot = host.RenderRect;
        Assert.True(plot.X >= 0 && plot.Y >= 0, $"plot origin {plot.X},{plot.Y}");
        Assert.True(plot.X + plot.Width <= 800, $"plot right {plot.X + plot.Width}");
        Assert.True(plot.Y + plot.Height <= 600, $"plot bottom {plot.Y + plot.Height}");
        Assert.True(plot.Width > 100 && plot.Height > 100, $"plot size {plot.Width}x{plot.Height}");

        // Real series ink behind the browser proof's plot-region gate.
        var canvas = host.GetType().GetProperty("Canvas",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy)
            ?.GetValue(host);
        Assert.NotNull(canvas);
        var children = ((System.Collections.IEnumerable)canvas!.GetType()
            .GetProperty("Children")!.GetValue(canvas)!).Cast<object>().ToList();
        Assert.NotEmpty(children.OfType<Path>());
        Assert.Contains(children.OfType<TextBlock>().Select(t => t.Text ?? ""),
            t => t.Contains("Normal distribution", StringComparison.Ordinal));
    }

    [Fact]
    public void Staged_renderer_panel_docks_bottom_right_in_scene()
    {
        var window = BootSelected();
        var combo = Descendants(window).OfType<ComboBox>().Single();
        var panel = (FrameworkElement)combo.Parent!;
        Assert.IsType<StackPanel>(panel);

        // Content-sized and anchored to the scene's bottom-right corner
        // (pre-fix it arranged over the whole right cell and overlapped
        // the plot; the ComboBox sat below the fold at y = 720).
        Assert.Equal(panel.DesiredSize.Width, panel.RenderRect.Width);
        Assert.Equal(panel.DesiredSize.Height, panel.RenderRect.Height);
        Assert.Equal(800, panel.RenderRect.X + panel.RenderRect.Width);
        Assert.Equal(600, panel.RenderRect.Y + panel.RenderRect.Height);

        // The browser proof's rendererBox STATE: clickable inside the scene.
        var box = combo.RenderRect;
        Assert.True(box.Width > 0 && box.Height > 0);
        Assert.True(box.X >= 0 && box.Y >= 0, $"combo origin {box.X},{box.Y}");
        Assert.True(box.X + box.Width <= 800, $"combo right {box.X + box.Width}");
        Assert.True(box.Y + box.Height <= 600, $"combo bottom {box.Y + box.Height}");
    }

    private static Window BootSelected()
    {
        // Converted PlotViewBase renders synchronously only on the
        // dispatcher-owner thread, like the other staged oracles.
        WasmDispatcher.Current = new WasmDispatcher(new WasmDispatcherPump());
        var window = WpfAppBoot.Boot(StagedOxyPlot.Load());
        window.Measure(new WpfSize(800, 600));
        window.Arrange(new WpfRect(0, 0, 800, 600));
        var list = Descendants(window).OfType<ListBox>().Single();
        Descendants(window).OfType<Expander>().First().IsExpanded = true;
        list.SelectedIndex = 0;
        window.Measure(new WpfSize(800, 600));
        window.Arrange(new WpfRect(0, 0, 800, 600));
        return window;
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
