using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>End-to-end proof that the original Geometry sample (converted
/// with zero gaps) boots through app-level resource dictionaries and runs
/// its radio-select workflow: SampleSelected state, the Page.Triggers
/// zoom-out storyboard, Frame navigation, ContentRendered zoom-in and the
/// visibility handshake. The Convert boot/press oracles cover routed
/// pointer input at the arranged centers; these tests pin the state chain.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class ConvertedGeometryInteractionTests
{
    private sealed record BootedApp(Window Window, Application? Previous);

    private static BootedApp Boot()
    {
        var previous = Application.Current;
        var directory = FindAppDirectory("geometry");
        var manifest = System.Text.Json.JsonSerializer.Deserialize<ApplicationPackage>(
            File.ReadAllText(System.IO.Path.Combine(directory, "app-manifest.json")),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var entry = Assert.Single(manifest.Assemblies, a => a.Name == manifest.EntryAssembly);
        var path = System.IO.Path.Combine(directory, entry.File);
        using var loader = ApplicationPackageLoader.CreateAsync(manifest, "geometry",
            file => File.ReadAllBytesAsync(System.IO.Path.Combine(directory, file))).GetAwaiter().GetResult();
        var bytes = File.ReadAllBytes(path);
        // The booted app stays current for the assertions (real WPF keeps it
        // current for the process lifetime); callers restore afterwards.
        return new BootedApp(WpfAppBoot.Boot(bytes), previous);
    }

    private static string FindAppDirectory(string slug)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "DesktopWasm.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return System.IO.Path.Combine(directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", slug);
    }

    private static List<UIElement> Descendants(UIElement root)
    {
        var all = new List<UIElement> { root };
        if (root is FrameworkElement host)
            foreach (var child in host.LogicalChildren)
                all.AddRange(Descendants(child));
        return all;
    }

    [Fact]
    public void Geometry_boots_into_navigation_window_with_app_resources()
    {
        var booted = Boot();
        try
        {
            var window = booted.Window;
            var navigation = Assert.IsType<NavigationWindow>(window);
            Assert.Equal("SampleViewer", navigation.Content?.GetType().Name);

            var app = Application.Current;
            Assert.NotNull(app);
            Assert.True(app.Resources.TryGetValue("MyGlassBrushResource", out var glassRaw));
            var glass = Assert.IsType<LinearGradientBrush>(glassRaw);
            // Real WPF collection semantics: the nested GradientStops items
            // add into the {StaticResource} collection (3 shared + 3 nested).
            Assert.Equal(6, glass.GradientStops.Count);
            Assert.Equal(new Point(0, 0), glass.StartPoint);
            Assert.True(app.Resources.TryGetValue("MyBlueGridBrushResource", out var gridRaw));
            var grid = Assert.IsType<DrawingBrush>(gridRaw);
            Assert.Equal(TileMode.Tile, grid.TileMode);
            Assert.Equal(new Rect(0, 0, 10, 10), grid.Viewport);
            var drawings = Assert.IsType<DrawingGroup>(grid.Drawing).Children;
            Assert.Equal(3, drawings.Count);

            Assert.True(app.Resources.TryGetValue(typeof(RadioButton), out var styleRaw));
            var template = Assert.IsType<ControlTemplate>(Assert.IsType<Setter>(Assert.IsType<Style>(styleRaw).Setters[1]).Value);
            Assert.IsType<Grid>(template.VisualTree);
            Assert.True(template.TemplateNames.ContainsKey("glassCube"));
            Assert.NotEmpty(template.TemplateBindings);
            Assert.Equal(5, template.Triggers.Count);

            var labels = Descendants(window).OfType<ToggleButton>().Select(b => b.Content?.ToString()).ToList();
            Assert.Contains("Geometry Usage", labels);
            Assert.Contains("Shape Geometries", labels);
            Assert.Contains("PathGeometry", labels);
            Assert.Contains("Combining Geometries Example", labels);
            Assert.Contains("Geometry Attribute Syntax Example", labels);
        }
        finally
        {
            Application.Current = booted.Previous;
        }
    }

    [Fact]
    public void Geometry_radio_select_runs_original_storyboard_chain()
    {
        var booted = Boot();
        try
        {
            var window = booted.Window;
            var host = new WasmWpfHost(new WasmDispatcherPump(), new WasmInputRouter());
            host.AddWindow(window, new WpfSize(800, 600));
            host.Tick(new FrameContext(1, 16.6));

            var example1 = Assert.IsType<RadioButton>(window.FindName("Example1RadioButton"));
            Assert.True(example1.IsChecked == true);

            var pathRadio = Assert.Single(Descendants(window).OfType<RadioButton>(), b => b.Content?.ToString() == "PathGeometry");
            pathRadio.IsChecked = true;

            var border = Assert.IsType<Border>(window.FindName("scrollViewerBorder"));
            Assert.Equal(Visibility.Hidden, border.Visibility);
            var viewport = Assert.IsType<Viewport3D>(window.FindName("myViewport3D"));
            Assert.Equal(100.0, viewport.Width);

            WpfAnimationClock.Advance(TimeSpan.FromSeconds(4));
            var frame = Assert.IsType<Frame>(window.FindName("mainFrame"));
            Assert.Equal("PathGeometryExample", frame.Content?.GetType().Name);

            WpfAnimationClock.Advance(TimeSpan.FromSeconds(3));
            Assert.Equal(Visibility.Visible, border.Visibility);

            // 3D names live in the SampleViewer namescope (outside the logical tree).
            var viewer = Assert.IsType<NavigationWindow>(window).Content;
            Assert.True(WpfAppBoot.NameScopes.TryGetValue(viewer, out var viewerNames));
            var geometry = Assert.IsType<MeshGeometry3D>(viewerNames["myGeometry"]);
            Assert.NotEmpty(geometry.Positions);
            Assert.All(geometry.Positions, p =>
                Assert.True(double.IsFinite(p.X + p.Y + p.Z), $"Position {p} is not finite."));

            var rendered = host.Tick(new FrameContext(2, 33.2));
            Assert.Contains("[16,", rendered.Json);
        }
        finally
        {
            Application.Current = booted.Previous;
        }
    }
}
