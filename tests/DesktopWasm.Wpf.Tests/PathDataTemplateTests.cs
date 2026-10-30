using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-58: Path.Data mini-language in template/style context (PC Space Map
/// App.xaml ComboBox chevron Data="M 0 0 L 10 0 L 5 6 Z"). The element path
/// parsed it via XamlLoader.ParseGeometry, but template attributes
/// (ApplicationXaml.SetTemplateValue), style setters (Setter.ResolveValue)
/// and trigger/template coercion (Templating) all funnel through
/// StyleValueConverter.Convert, which had no Geometry branch and failed with
/// "'...' cannot convert to Geometry (no string conversion)". The converter
/// now runs the same real Geometry.Parse pipeline as the element path.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class PathDataTemplateTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string Chevron = "M 0 0 L 10 0 L 5 6 Z";

    private readonly Application? _previous = Application.Current;

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    private static Application SeedApp()
    {
        var app = new Application();
        Application.Current = app;
        return app;
    }

    [Fact]
    public void Converter_parses_chevron_mini_language()
    {
        var geometry = Assert.IsType<PathGeometry>(StyleValueConverter.Convert(Chevron, typeof(Geometry)));
        var figure = Assert.Single(geometry.Figures);
        Assert.True(figure.IsClosed);
        Assert.Equal(2, figure.Segments.Count);
        Assert.Equal(0.0, geometry.Bounds.X);
        Assert.Equal(0.0, geometry.Bounds.Y);
        Assert.Equal(10.0, geometry.Bounds.Width);
        Assert.Equal(6.0, geometry.Bounds.Height);
    }

    [Fact]
    public void Converter_accepts_pathgeometry_target()
    {
        var geometry = StyleValueConverter.Convert(Chevron, typeof(PathGeometry));
        var parsed = Assert.IsType<PathGeometry>(geometry);
        Assert.Single(parsed.Figures);
    }

    [Fact]
    public void Converter_rejects_garbage_data_loud()
    {
        Assert.Throws<FormatException>(() => StyleValueConverter.Convert("not a path", typeof(Geometry)));
    }

    [Fact]
    public void Converter_rejects_undeliverable_geometry_target()
    {
        // Geometry.Parse yields a PathGeometry; a StreamGeometry-typed target
        // cannot take it, so the converter fails loud instead of handing an
        // unsettable value to reflection.
        Assert.Throws<InvalidOperationException>(() => StyleValueConverter.Convert(Chevron, typeof(StreamGeometry)));
    }

    [Fact]
    public void Style_setter_lands_chevron_data_on_path()
    {
        SeedApp();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"Chevron\" TargetType=\"{x:Type Path}\">" +
            "<Setter Property=\"Data\" Value=\"" + Chevron + "\"/>" +
            "<Setter Property=\"Fill\" Value=\"Black\"/>" +
            "</Style></ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["Chevron"]);
        var path = new Path();
        style.ApplyTo(path);
        Assert.NotNull(path.Data);
        var geometry = Assert.IsType<PathGeometry>(path.Data);
        Assert.Equal(10.0, geometry.Bounds.Width);
        Assert.Equal(6.0, geometry.Bounds.Height);
        Assert.NotNull(path.Fill);
        Assert.Equal(new SolidColorBrush(Colors.Black).ToRgba(), ((SolidColorBrush)path.Fill).ToRgba());
    }

    [Fact]
    public void Template_attribute_lands_chevron_data_with_ink()
    {
        SeedApp();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"ChevronHost\" TargetType=\"{x:Type Button}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type Button}\">" +
            "<Canvas><Path x:Name=\"Chevron\" Data=\"" + Chevron + "\" Fill=\"Black\"/></Canvas>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style></ResourceDictionary>");
        var template = Assert.IsType<ControlTemplate>(
            Assert.IsType<Setter>(Assert.IsType<Style>(dict["ChevronHost"]).Setters[0]).Value);
        var root = Assert.IsType<Canvas>(template.LoadContent());
        var path = Assert.IsType<Path>(Assert.Single(((FrameworkElement)root).LogicalChildren));
        Assert.NotNull(path.Data);
        var geometry = Assert.IsType<PathGeometry>(path.Data);
        Assert.Equal(10.0, geometry.Bounds.Width);
        Assert.Equal(6.0, geometry.Bounds.Height);
        var commands = WasmMilCore.RenderHeadless(root, new WpfSize(38, 20));
        var draw = Assert.IsType<DrawCommand.DrawGeometry>(Assert.Single(commands.OfType<DrawCommand.DrawGeometry>()));
        Assert.Equal(new SolidColorBrush(Colors.Black).ToRgba(), draw.FillRgba);
        Assert.NotEqual(string.Empty, draw.PathData);
    }

    [Fact]
    public void Element_and_template_paths_agree_on_chevron()
    {
        SeedApp();
        var element = Assert.IsType<Path>(XamlLoader.Load(
            "<Path xmlns=\"" + PresentationNs + "\" Data=\"" + Chevron + "\"/>").Root);
        Assert.NotNull(element.Data);
        var elementBounds = Assert.IsType<PathGeometry>(element.Data).Bounds;
        var converted = Assert.IsType<PathGeometry>(StyleValueConverter.Convert(Chevron, typeof(Geometry)));
        Assert.Equal(elementBounds, converted.Bounds);
    }

    [Fact]
    public void Staged_app_advances_past_path_data()
    {
        // Slice-59 advance: the template Focusable/ClickMode/Popup blockers
        // are gone, so App.InitializeComponent runs clean (chevron geometry
        // still converts, AppBackground lands).
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
            Assert.True(Assert.IsAssignableFrom<Application>(app).Resources.Contains("AppBackground"));
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
            Application.Current = null;
        }
    }

    [Fact]
    public void Staged_boot_advances_past_path_data()
    {
        // Slice-59 advance: staged boot returns the live MainWindow
        // (code-driven OnStartup runs; the Kestrel rows stay a declared
        // conversion boundary and never block boot).
        Application.Current = null;
        var entry = LoadStagedPcSpaceMapEntry();
        try
        {
            var window = WpfAppBoot.Boot(entry);
            Assert.Equal("PcSpaceMap.MainWindow", window.GetType().FullName);
            Assert.Equal("PC Space Map", window.Title);
        }
        finally
        {
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
