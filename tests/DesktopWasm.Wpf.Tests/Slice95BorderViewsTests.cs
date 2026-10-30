using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Wpf;

namespace DesktopWasm.Wpf.Tests
{

/// <summary>Slice-95 LoadComponent scope for Border-based demo views: the
/// engine hydrates Border-rooted components (Window/Page/Application/
/// UserControl/Border), so the 9 converted HandyControlDemo views (which
/// type-load since the slice-94 Border rebase) now reach their precise
/// content gaps instead of the scope rejection. Verified live against the
/// staged handycontrol bins with System.Reflection.Metadata (10 converted
/// types with base DesktopWasm.Wpf.Border) plus Cecil call-shape evidence
/// (TextDialogWithTimer..ctor ldsfld RangeBase.ValueProperty for
/// BeginAnimation; Circle/WaveProgressBar..cctor OverrideMetadata
/// MaximumProperty to 100 and AddOwner IsIndeterminateProperty).
/// Advance pinned here: Border hydration end-to-end on an engine fixture,
/// the converted lib progress bars type-initializing with the overridden
/// Maximum default, and per-view InitializeComponent errors proving the
/// scope rejection is gone; the content gaps stay the next blockers.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice95BorderViewsTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice95BorderViewsTests()
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
    public void Border_hydrate_carries_footprint_child_and_chrome()
    {
        Application.Current = new BootProbe.ProbeApp();
        var border = new BootProbe.SizedBorder();
        Assert.Equal(400, border.Width);
        Assert.Equal(247, border.Height);
        Assert.Equal(new CornerRadius(10), border.CornerRadius);
        Assert.NotNull(border.Background);
        var panel = Assert.IsType<StackPanel>(border.Child);
        Assert.Equal(2, panel.LogicalChildren.Count);
        Assert.NotNull(border.Caption);
        Assert.Equal("Hi", border.Caption!.Text);
        Assert.NotNull(border.Fire);
        Assert.Equal("Go", border.Fire!.Content);
        border.Fire.RaiseClick();
        Assert.Equal(1, border.Fires);
    }

    [Fact]
    public void Border_hydrate_rejects_root_mismatch()
    {
        Application.Current = new BootProbe.ProbeApp();
        var border = new BootProbe.SizedBorderNoInit();
        var error = Assert.Throws<XamlLoadException>(() =>
            Application.LoadComponent(border, new Uri("Boot/BootWindow.xaml", UriKind.Relative)));
        Assert.Contains("not a Border", error.Message);
    }

    [Fact]
    public void Border_hydrate_rejects_class_mismatch()
    {
        Application.Current = new BootProbe.ProbeApp();
        var border = new Border();
        var error = Assert.Throws<XamlLoadException>(() =>
            Application.LoadComponent(border, new Uri("Boot/SizedBorder.xaml", UriKind.Relative)));
        Assert.Contains("BootProbe.SizedBorder", error.Message);
    }

    [Fact]
    public void Load_component_still_rejects_unsupported_components()
    {
        // Decorator has no Hydrate branch: the scope error now names Border
        // alongside the original four component kinds.
        Application.Current = new BootProbe.ProbeApp();
        var decorator = new Decorator();
        var error = Assert.Throws<XamlLoadException>(() =>
            Application.LoadComponent(decorator, new Uri("Boot/SizedBorder.xaml", UriKind.Relative)));
        Assert.Contains("is not supported (Window/Page/Application/UserControl/Border only)", error.Message);
    }

    public static IEnumerable<object[]> StagedBorderViews() => new[]
    {
        // Each row: converted view type, the precise content gap its
        // InitializeComponent now reaches (scope rejection gone).
        new object[] { "HandyControlDemo.UserControl.GeometryItem", "StaticResource 'Path4GeometryItem' was not found" },
        new object[] { "HandyControlDemo.UserControl.InteractiveDialog", "StaticResource 'TextBlockLargeBold' was not found" },
        new object[] { "HandyControlDemo.UserControl.TextDialog", "StaticResource 'TextBlockLargeBold' was not found" },
        new object[] { "HandyControlDemo.UserControl.TextDialogWithTimer", "StaticResource 'TextBlockLargeBold' was not found" },
        new object[] { "HandyControlDemo.UserControl.AppNotification", "StaticResource 'LogoGeometry' was not found" },
        new object[] { "HandyControlDemo.UserControl.MainWindowContent", "StaticResource 'ButtonIconCircular' was not found" },
        new object[] { "HandyControlDemo.UserControl.LeftMainContent", "StaticResource 'TabControlInLine' was not found" },
        new object[] { "HandyControlDemo.UserControl.MainContent", "StaticResource 'EffectShadow4' was not found" },
        new object[] { "HandyControlDemo.UserControl.UnderConstruction", "StaticResource 'TextBlockLargeBold' was not found" },
    };

    [Theory]
    [MemberData(nameof(StagedBorderViews))]
    public void Staged_border_views_reach_content_gaps_not_scope(string typeName, string expectedFragment)
    {
        var entry = LoadStagedHandyControlEntry();
        var type = entry.GetType(typeName, throwOnError: true)!;
        Assert.True(typeof(Border).IsAssignableFrom(type), typeName);
        var error = Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(type));
        var load = Assert.IsType<XamlLoadException>(error.InnerException);
        // The slice-94 scope rejection is gone: InitializeComponent runs and
        // the loader reports the view's own content gap.
        Assert.Contains(expectedFragment, load.Message);
        Assert.DoesNotContain("is not supported (Window/Page/Application/UserControl/Border only)", load.Message);
        for (var inner = error.InnerException; inner is not null; inner = inner.InnerException)
            Assert.IsNotType<MissingFieldException>(inner);
    }

    [Theory]
    [InlineData("HandyControl.Controls.CircleProgressBar")]
    [InlineData("HandyControl.Controls.WaveProgressBar")]
    public void Staged_lib_progress_bars_type_init_with_overridden_maximum(string typeName)
    {
        // The converted lib static ctors OverrideMetadata MaximumProperty to
        // 100 and AddOwner IsIndeterminateProperty: both DP fields land on
        // the engine this slice, so type-init succeeds and the override
        // default flows through the engine store.
        LoadStagedHandyControlEntry();
        var lib = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.Ordinal));
        var type = lib.GetType(typeName, throwOnError: true)!;
        RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        var control = Activator.CreateInstance(type, nonPublic: true)!;
        Assert.Equal(100.0, (double)type.GetProperty("Maximum")!.GetValue(control)!);
    }

    [Fact]
    public void Staged_circle_progress_bar_add_owners_indeterminate()
    {
        // CircleProgressBar..cctor AddOwners ProgressBar.IsIndeterminateProperty:
        // the owned field lands and reads the engine default through the store.
        LoadStagedHandyControlEntry();
        var lib = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.Ordinal));
        var type = lib.GetType("HandyControl.Controls.CircleProgressBar", throwOnError: true)!;
        RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        var owned = (DependencyProperty)type.GetField(
            "IsIndeterminateProperty",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy)!.GetValue(null)!;
        var control = (DependencyObject)Activator.CreateInstance(type, nonPublic: true)!;
        Assert.False((bool)control.GetValue(owned)!);
        Assert.Same(ProgressBar.IsIndeterminateProperty, owned);
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
}

namespace BootProbe
{
    /// <summary>Stand-in Border with root sizing/chrome, a named field and a Click handler.</summary>
    public sealed class SizedBorder : Border
    {
        internal TextBlock? Caption;
        internal Button? Fire;
        public int Fires;

        public SizedBorder() => InitializeComponent();

        public void InitializeComponent() =>
            Application.LoadComponent(this, new Uri("Boot/SizedBorder.xaml", UriKind.Relative));

        private void OnFire(object sender, RoutedEventArgs e) => Fires++;
    }

    /// <summary>Stand-in Border that skips InitializeComponent (mismatch probes).</summary>
    public sealed class SizedBorderNoInit : Border
    {
    }
}
