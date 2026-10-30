using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Wpf;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-62 code-built trigger chrome: the converted AvalonDock
/// ToggleDockingManager.CreateShowHiddenButtonTemplate factory references nine
/// engine-resolved members that are missing (Trigger.set_Property(DP),
/// Border.BackgroundProperty, FrameworkElement H/VAlignment DP fields,
/// Path.DataProperty, Shape.Fill/StretchProperty, UIElement
/// IsMouseOver/SnapsToDevicePixelsProperty), so the whole method fails to JIT.
/// The engine now declares the WPF-parity surface (DP fields sharing the
/// WPF-identical instances, store-backed CLR wrappers, DP-accepting trigger
/// setter) and the converted factory builds.
/// </summary>
[ResetAmbientState]
public sealed class CodeBuiltTriggerChromeTests
{
    [Fact]
    public void Staged_avalondock_hashes_verify()
    {
        Assert.Empty(StagedAvalonDockRuntimeLoader.VerifyHashes());
    }

    [Fact]
    public void Trigger_dp_setter_records_the_dp_name()
    {
        var trigger = new Trigger();
        trigger.set_Property(Control.BackgroundProperty);
        Assert.Equal("Background", trigger.Property);
        trigger.set_Property(UIElement.IsMouseOverProperty);
        Assert.Equal("IsMouseOver", trigger.Property);
    }

    [Fact]
    public void Trigger_dp_setter_qualifies_attached_names()
    {
        var trigger = new Trigger();
        trigger.set_Property(ScrollViewer.VerticalScrollBarVisibilityProperty);
        Assert.Equal("ScrollViewer.VerticalScrollBarVisibility", trigger.Property);
    }

    [Fact]
    public void Border_background_shares_the_control_instance()
    {
        Assert.Same(Control.BackgroundProperty, Border.BackgroundProperty);
        Assert.Same(Control.BackgroundProperty, DependencyProperty.FromName(typeof(Border), "Background"));
    }

    [Fact]
    public void Alignment_dps_default_to_stretch_and_back_the_clr()
    {
        var element = new Button();
        Assert.Equal(HorizontalAlignment.Stretch, element.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Stretch, element.VerticalAlignment);
        element.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        Assert.Equal(HorizontalAlignment.Center, element.HorizontalAlignment);
        element.VerticalAlignment = VerticalAlignment.Bottom;
        Assert.Equal(VerticalAlignment.Bottom, element.GetValue(FrameworkElement.VerticalAlignmentProperty));
    }

    [Fact]
    public void Shape_fill_and_stretch_back_the_clr()
    {
        var path = new Path();
        Assert.Null(path.Fill);
        Assert.Equal(Stretch.None, path.Stretch);
        var brush = new SolidColorBrush(Color.FromArgb(255, 10, 20, 30));
        path.SetValue(Shape.FillProperty, brush);
        Assert.Same(brush, path.Fill);
        path.SetValue(Shape.StretchProperty, Stretch.Uniform);
        Assert.Equal(Stretch.Uniform, path.Stretch);
    }

    [Fact]
    public void Path_data_backs_the_clr_and_remeasures()
    {
        var path = new Path();
        Assert.Null(path.Data);
        var geometry = Geometry.Parse("M 0 0 L 10 0 L 5 6 Z");
        path.SetValue(Path.DataProperty, geometry);
        Assert.Same(geometry, path.Data);
    }

    [Fact]
    public void Snaps_dp_backs_the_clr_with_wpf_default()
    {
        var border = new Border();
        Assert.False(border.SnapsToDevicePixels);
        border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        Assert.True(border.SnapsToDevicePixels);
    }

    [Fact]
    public void Dp_condition_trigger_applies_through_evaluation()
    {
        // A fresh button is never hovered, so an IsMouseOver=false condition
        // holds and the DP-addressed setter lands through evaluation.
        var button = new Button();
        var brush = new SolidColorBrush(Color.FromArgb(255, 1, 2, 3));
        var style = new Style(typeof(Button));
        var trigger = new Trigger { Value = false };
        trigger.set_Property(UIElement.IsMouseOverProperty);
        trigger.Setters.Add(new Setter(Control.BackgroundProperty, brush));
        style.Triggers.Add(trigger);
        style.ApplyTo(button);
        Assert.Same(brush, button.Background);
    }

    [Fact]
    public void Converted_show_hide_button_template_builds()
    {
        var assembly = StagedAvalonDockRuntimeLoader.LoadAssembly("AvalonDock");
        var manager = assembly.GetType("AvalonDock.ToggleDockingManager", throwOnError: true)!;
        var factory = manager.GetMethod("CreateShowHiddenButtonTemplate",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(factory);
        var template = Assert.IsType<ControlTemplate>(factory!.Invoke(null, null));
        Assert.Equal(typeof(Button), template.TargetType);
        var trigger = Assert.Single(template.Triggers.OfType<Trigger>());
        Assert.Equal("IsMouseOver", trigger.Property);
        Assert.Equal(true, trigger.Value);
        var setter = Assert.Single(trigger.Setters.OfType<Setter>());
        Assert.Same(Border.BackgroundProperty, setter.Property);
        Assert.Equal("Bd", setter.TargetName);
        // The factory installs through the base VisualTree; the template
        // eagerly inflates the live Border root from it.
        var rootFactory = Assert.IsType<FrameworkElementFactory>(((FrameworkTemplate)template).VisualTree);
        Assert.Equal("DesktopWasm.Wpf.Border", rootFactory.Type!.FullName);
        Assert.Equal("Bd", rootFactory.Name);
        var root = Assert.IsType<Border>(template.VisualTree);
        Assert.Same(Brushes.Transparent, root.Background);
    }

    [Fact]
    public void Converted_template_factory_values_land_on_create_instance()
    {
        var assembly = StagedAvalonDockRuntimeLoader.LoadAssembly("AvalonDock");
        var manager = assembly.GetType("AvalonDock.ToggleDockingManager", throwOnError: true)!;
        var factory = manager.GetMethod("CreateShowHiddenButtonTemplate",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        var template = (ControlTemplate)factory.Invoke(null, null)!;
        var rootFactory = Assert.IsType<FrameworkElementFactory>(((FrameworkTemplate)template).VisualTree);
        var border = Assert.IsType<Border>(rootFactory.CreateInstance());
        Assert.Same(Brushes.Transparent, border.Background);
        Assert.Equal(new CornerRadius(4), border.CornerRadius);
        Assert.True(border.SnapsToDevicePixels);
        Assert.Equal(HorizontalAlignment.Stretch, border.HorizontalAlignment);
        var path = Assert.IsType<Path>(Assert.Single(border.LogicalChildren));
        Assert.NotNull(path.Data);
        Assert.Equal(Stretch.Uniform, path.Stretch);
        Assert.Equal(12.0, path.Width);
        Assert.Equal(12.0, path.Height);
        Assert.Equal(HorizontalAlignment.Center, path.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Center, path.VerticalAlignment);
        Assert.True(BindingOperations.IsDataBound(path, Shape.FillProperty));
    }

    private static class StagedAvalonDockRuntimeLoader
    {
        private static readonly object Gate = new();
        private static readonly Dictionary<string, Assembly> Loaded = new(StringComparer.OrdinalIgnoreCase);

        public static List<string> VerifyHashes()
        {
            var failures = new List<string>();
            foreach (var (_, file, bytes, expected) in ReadStaged())
            {
                var actual = Convert.ToHexString(SHA256.HashData(bytes));
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    failures.Add($"hash mismatch in staged '{file}'");
            }
            return failures;
        }

        public static Assembly LoadAssembly(string name)
        {
            lock (Gate)
            {
                if (Loaded.TryGetValue(name, out var hit)) return hit;
                var images = ReadStaged().ToDictionary(e => e.name, e => e.bytes, StringComparer.OrdinalIgnoreCase);
                Assert.True(images.ContainsKey(name), $"staged image missing: {name}");
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
                {
                    var assembly = Assembly.Load(image);
                    Loaded[assembly.GetName().Name!] = assembly;
                }
                return Loaded[name];
            }
        }

        private static List<(string name, string file, byte[] bytes, string expected)> ReadStaged()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "src",
                "DesktopWasm.BrowserHost", "wwwroot", "apps", "avalondock", "app-manifest.json")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            var apps = System.IO.Path.Combine(directory!.FullName, "src",
                "DesktopWasm.BrowserHost", "wwwroot", "apps", "avalondock");
            using var document = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
            var entries = new List<(string, string, byte[], string)>();
            foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
            {
                var file = assembly.GetProperty("file").GetString()!;
                var expected = assembly.GetProperty("sha256").GetString()!;
                var bytes = File.ReadAllBytes(System.IO.Path.Combine(apps, file));
                entries.Add((assembly.GetProperty("name").GetString()!, file, bytes, expected));
            }
            return entries;
        }
    }
}
