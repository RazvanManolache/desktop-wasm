using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-94 sealed-engine-Border boundary: the engine Border was
/// sealed over FrameworkElement while live WPF declares it public non-sealed
/// over Decorator, so every converted Border child (HandyControl RunningBorder
/// plus nine HandyControlDemo UserControls) failed type-load at runtime.
/// Verified live against the 10.0.13 PresentationFramework pack plus a live
/// net10.0-windows runtime probe (Border IsSealed=False,
/// BaseType System.Windows.Controls.Decorator; Decorator IsSealed=False,
/// BaseType FrameworkElement) and against the staged handycontrol bins with
/// System.Reflection.Metadata (10 converted types with base
/// DesktopWasm.Wpf.Border). Fix: Border is now a public Decorator with a
/// list-backed Child override, so AddChild/Child/list paths stay in sync and
/// converted subclasses load and host content.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice94BorderTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice94BorderTests()
    {
        WpfAnimationClock.ResetForTests();
    }

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
        WpfAnimationClock.ResetForTests();
    }

    [Fact]
    public void Border_matches_live_decorator_lineage()
    {
        // Live WPF: public class Border : Decorator (non-sealed).
        Assert.False(typeof(Border).IsSealed);
        Assert.True(typeof(Decorator).IsAssignableFrom(typeof(Border)));
        Assert.Equal(typeof(Decorator), typeof(Border).BaseType);
        Assert.Equal(typeof(FrameworkElement), typeof(Decorator).BaseType);
        Assert.True(typeof(FrameworkElement).IsAssignableFrom(typeof(Border)));
        Assert.True(typeof(UIElement).IsAssignableFrom(typeof(Border)));
        // Engine-owned DP surface stays on Border.
        Assert.Same(Control.BackgroundProperty, Border.BackgroundProperty);
        Assert.NotNull(Border.CornerRadiusProperty);
        Assert.NotNull(Border.BorderBrushProperty);
    }

    [Fact]
    public void Border_child_syncs_with_child_list()
    {
        var border = new Border();
        Assert.Null(border.Child);
        Assert.Empty(border.LogicalChildren);

        var first = new TextBlock { Text = "one" };
        border.AddChild(first);
        Assert.Same(first, border.Child);
        Assert.Single(border.LogicalChildren);

        // The Decorator Child setter replaces the single host child.
        var second = new TextBlock { Text = "two" };
        border.Child = second;
        Assert.Same(second, border.Child);
        Assert.Same(second, Assert.Single(border.LogicalChildren));
        Assert.Null(first.Parent);

        border.Child = null;
        Assert.Null(border.Child);
        Assert.Empty(border.LogicalChildren);
    }

    [Fact]
    public void Border_decorator_layout_stays_pixel_identical()
    {
        // Legacy pin: 300-wide border hosting a child keeps desired width.
        var border = new Border { Width = 300 };
        border.AddChild(new TextBlock { Text = "Hi" });
        border.Measure(new WpfSize(800, 600));
        Assert.Equal(300, border.DesiredSize.Width);

        // Child fills the padded/bordered slot like before the rebase.
        var inner = new Border { Padding = new Thickness(4), Thickness = 2 };
        var leaf = new TextBlock { Text = "Hi" };
        inner.AddChild(leaf);
        inner.Measure(new WpfSize(800, 600));
        inner.Arrange(new WpfRect(0, 0, inner.DesiredSize.Width, inner.DesiredSize.Height));
        Assert.Same(leaf, inner.Child);
        Assert.Equal(6, leaf.ArrangedRect.X, precision: 5);
        Assert.Equal(6, leaf.ArrangedRect.Y, precision: 5);
    }

    [Fact]
    public void Staged_converted_border_children_type_load()
    {
        var entry = LoadStagedHandyControlEntry();
        var lib = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.Ordinal));
        // Pre-fix each of these threw TypeLoadException (sealed base).
        var names = new[]
        {
            "HandyControl.Controls.RunningBorder",
            "HandyControlDemo.UserControl.GeometryItem",
            "HandyControlDemo.UserControl.InteractiveDialog",
            "HandyControlDemo.UserControl.TextDialog",
            "HandyControlDemo.UserControl.TextDialogWithTimer",
            "HandyControlDemo.UserControl.AppNotification",
            "HandyControlDemo.UserControl.MainWindowContent",
            "HandyControlDemo.UserControl.LeftMainContent",
            "HandyControlDemo.UserControl.MainContent",
            "HandyControlDemo.UserControl.UnderConstruction",
        };
        foreach (var name in names)
        {
            var assembly = name.StartsWith("HandyControlDemo.", StringComparison.Ordinal) ? entry : lib;
            var type = assembly.GetType(name, throwOnError: true)!;
            Assert.True(typeof(Border).IsAssignableFrom(type), name);
            Assert.True(typeof(Decorator).IsAssignableFrom(type), name);
        }
    }

    [Fact]
    public void Staged_border_user_control_ctor_hits_load_component_boundary()
    {
        // The 9 converted demo views derive Border (upstream views are
        // Border-based, not UserControl): they type-load now, and since
        // slice-95 their ctors' InitializeComponent hydrates through the
        // Border Hydrate branch, reaching the views' own content gaps -
        // MainContent cleared the slice-96 SimplePanel fallback and now
        // reaches its first attached StaticResource (BorderRootEffect's
        // EffectShadow4, applied at that child's attach before the Watermark
        // row is ever parsed - see Slice96HandyFallbackTests for all 9
        // per-view pins).
        var entry = LoadStagedHandyControlEntry();
        var type = entry.GetType("HandyControlDemo.UserControl.MainContent", throwOnError: true)!;
        var error = Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(type));
        var load = Assert.IsType<XamlLoadException>(error.InnerException);
        Assert.Contains("StaticResource 'EffectShadow4' was not found", load.Message);
        Assert.DoesNotContain("Unknown element <SimplePanel>", load.Message);
    }

    [Fact]
    public void Staged_running_border_hosts_engine_child()
    {
        LoadStagedHandyControlEntry();
        var lib = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.Ordinal));
        var type = lib.GetType("HandyControl.Controls.RunningBorder", throwOnError: true)!;
        var control = (Border)Activator.CreateInstance(type)!;
        var leaf = new TextBlock { Text = "held" };
        control.Child = leaf;
        Assert.Same(leaf, control.Child);
        Assert.Same(leaf, Assert.Single(control.LogicalChildren));
        control.Measure(new WpfSize(800, 600));
        control.Arrange(new WpfRect(0, 0, control.DesiredSize.Width, control.DesiredSize.Height));
        Assert.Equal(control, leaf.Parent);
    }

    [Fact]
    public void Staged_manifest_keeps_declared_boundaries_only()
    {
        // No re-package this slice (Convert map untouched): the 6 staged
        // images still sha256-match and the only gaps are the 2 declared
        // absent binaries with no browser path.
        LoadStagedHandyControlEntry();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(
            directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol", "app-manifest.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        using var document = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(
            directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol", "app-manifest.json")));
        Assert.Equal("conversion-failed", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(6, document.RootElement.GetProperty("assemblies").GetArrayLength());
        var gaps = document.RootElement.GetProperty("gaps").EnumerateArray().ToArray();
        Assert.Equal(2, gaps.Length);
        foreach (var gap in gaps)
        {
            Assert.Equal("missing-dependency", gap.GetProperty("kind").GetString());
            var detail = gap.GetProperty("detail").GetString()!;
            Assert.True(detail.Contains("System.Security.Permissions") || detail.Contains("System.Windows.Extensions"), detail);
        }
        Assert.DoesNotContain("Border", document.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    private static readonly object StagedGate = new();
    private static Assembly? _stagedEntry;

    private static Assembly LoadStagedHandyControlEntry()
    {
        lock (StagedGate)
        {
            var hit = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "HandyControlDemo", StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                VerifyStagedHashes();
                return hit;
            }
            if (_stagedEntry is not null) return _stagedEntry;
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(System.IO.Path.Combine(
                directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol", "app-manifest.json")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            var apps = System.IO.Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol");
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
                .First(a => string.Equals(a.GetName().Name, "HandyControlDemo", StringComparison.OrdinalIgnoreCase));
            return _stagedEntry;
        }
    }

    private static void VerifyStagedHashes()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(
            directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol", "app-manifest.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var apps = System.IO.Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol");
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
