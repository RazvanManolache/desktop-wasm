using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Wpf;

namespace DesktopWasm.Wpf.Tests
{

/// <summary>Slice-96 HandyControl view-content fallback: engine SimplePanel
/// (upstream-exact fill layout), TransitioningContentControl (ContentControl
/// parity with an explicit-storyboard transition) and the retained-only
/// ScrollViewer.IsInertiaEnabled attached knob, plus x:Name on
/// RowDefinition/ColumnDefinition (WPF parity: x:Name registers, plain Name
/// keeps failing). Member evidence via Mono.Cecil on the staged converted
/// HandyControl binary (SimplePanel : engine Panel with no extra members;
/// TransitioningContentControl : engine ContentControl with
/// TransitionMode/TransitionStoryboard DPs; ScrollViewer :
/// engine ScrollViewer with instance + static IsInertiaEnabled accessors over
/// a RegisterAttached bool defaulting to FalseBox; TransitionMode
/// Right2Left=0 through Fade=8 plus Custom=9) and the upstream HandyControl
/// sources (SimplePanel measures the max child desired size and arranges
/// every child into the full rect; TransitioningContentControl swaps content
/// and restarts its storyboard on mode/content change while the built-in mode
/// storyboards resolve through HandyControl app resources). Advance pinned
/// here: 7 of the 9 staged Border views move past their slice-95 gaps to
/// their next precise blockers (the 2 StaticResource views are unchanged);
/// the staged package keeps its 2 absent-binary gaps, so no re-package.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice96HandyFallbackTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice96HandyFallbackTests()
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
    public void Transition_mode_values_match_upstream()
    {
        Assert.Equal(0, (int)TransitionMode.Right2Left);
        Assert.Equal(1, (int)TransitionMode.Left2Right);
        Assert.Equal(2, (int)TransitionMode.Bottom2Top);
        Assert.Equal(3, (int)TransitionMode.Top2Bottom);
        Assert.Equal(4, (int)TransitionMode.Right2LeftWithFade);
        Assert.Equal(5, (int)TransitionMode.Left2RightWithFade);
        Assert.Equal(6, (int)TransitionMode.Bottom2TopWithFade);
        Assert.Equal(7, (int)TransitionMode.Top2BottomWithFade);
        Assert.Equal(8, (int)TransitionMode.Fade);
        Assert.Equal(9, (int)TransitionMode.Custom);
    }

    [Fact]
    public void Simple_panel_measures_max_child_desired()
    {
        var panel = new SimplePanel();
        panel.AddChild(new Border { Width = 100, Height = 50 });
        panel.AddChild(new Border { Width = 60, Height = 80 });
        panel.Measure(new WpfSize(500, 500));
        Assert.Equal(100, panel.DesiredSize.Width);
        Assert.Equal(80, panel.DesiredSize.Height);
    }

    [Fact]
    public void Simple_panel_gives_every_child_the_full_rect()
    {
        // Auto-size children stretch into the slot the panel hands them, so
        // every child renders the full rect (explicit sizes would win over
        // Stretch instead, which the measure test pins separately).
        var panel = new SimplePanel();
        var first = new TextBlock { Text = "A" };
        var second = new TextBlock { Text = "B" };
        panel.AddChild(first);
        panel.AddChild(second);
        panel.Measure(new WpfSize(500, 500));
        panel.Arrange(new WpfRect(0, 0, 400, 300));
        Assert.Equal(new WpfRect(0, 0, 400, 300), first.RenderRect);
        Assert.Equal(new WpfRect(0, 0, 400, 300), second.RenderRect);
    }

    [Fact]
    public void Simple_panel_empty_measures_zero()
    {
        var panel = new SimplePanel();
        panel.Measure(new WpfSize(500, 500));
        Assert.Equal(0, panel.DesiredSize.Width);
        Assert.Equal(0, panel.DesiredSize.Height);
    }

    [Fact]
    public void Transitioning_content_control_defaults_match_upstream()
    {
        var host = new TransitioningContentControl();
        Assert.Equal(TransitionMode.Right2Left, host.TransitionMode);
        Assert.Null(host.TransitionStoryboard);
        Assert.Equal("TransitionMode", TransitioningContentControl.TransitionModeProperty.Name);
        Assert.Same(typeof(TransitioningContentControl), TransitioningContentControl.TransitionModeProperty.OwnerType);
        Assert.Equal("TransitionStoryboard", TransitioningContentControl.TransitionStoryboardProperty.Name);
    }

    [Fact]
    public void Transitioning_content_control_hosts_content_full_bleed()
    {
        var host = new TransitioningContentControl();
        var child = new TextBlock { Text = "Hi" };
        host.Content = child;
        Assert.Same(child, host.Content);
        Assert.Same(host, child.Parent);
        host.Measure(new WpfSize(500, 500));
        Assert.Equal(child.DesiredSize, host.DesiredSize);
        host.Arrange(new WpfRect(0, 0, 400, 300));
        Assert.Equal(new WpfRect(0, 0, 400, 300), child.RenderRect);
    }

    [Fact]
    public void Mode_only_change_animates_nothing_without_storyboard()
    {
        // Built-in mode storyboards resolve through HandyControl app resources
        // (behind the SkinDefault stop), so a mode change alone swaps nothing
        // and runs nothing: the boundary pin.
        var host = new TransitioningContentControl();
        var child = new TextBlock { Text = "Hi" };
        host.Content = child;
        host.TransitionMode = TransitionMode.Fade;
        Assert.Equal(TransitionMode.Fade, host.TransitionMode);
        Assert.Same(child, host.Content);
        host.Measure(new WpfSize(500, 500));
        host.Arrange(new WpfRect(0, 0, 400, 300));
        Assert.Equal(new WpfRect(0, 0, 400, 300), child.RenderRect);
    }

    private sealed class Dial : FrameworkElement
    {
        public double Angle { get; set; }
    }

    [Fact]
    public void Explicit_transition_storyboard_runs_on_content_change()
    {
        var host = new TransitioningContentControl();
        var dial = new Dial();
        var animation = new DoubleAnimation(0.0, 100.0, new Duration(TimeSpan.FromSeconds(10)));
        var board = new Storyboard();
        board.Children.Add(animation);
        Storyboard.SetTarget(animation, dial);
        Storyboard.SetTargetProperty((object)animation, "Angle");
        host.TransitionStoryboard = board;
        host.Content = dial;
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(50.0, dial.Angle, precision: 9);
    }

    [Fact]
    public void Is_inertia_enabled_defaults_false_and_round_trips()
    {
        var border = new Border();
        Assert.False(ScrollViewer.GetIsInertiaEnabled(border));
        ScrollViewer.SetIsInertiaEnabled(border, true);
        Assert.True(ScrollViewer.GetIsInertiaEnabled(border));
        Assert.True((bool)border.GetValue(ScrollViewer.IsInertiaEnabledProperty)!);
        ScrollViewer.SetIsInertiaEnabled(border, false);
        Assert.False(ScrollViewer.GetIsInertiaEnabled(border));
    }

    [Fact]
    public void Column_definition_x_name_registers_plain_name_fails()
    {
        const string xaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Grid.ColumnDefinitions><ColumnDefinition x:Name=\"Left\" Width=\"240\"/></Grid.ColumnDefinitions>" +
            "<Grid.RowDefinitions><RowDefinition x:Name=\"Top\" Height=\"Auto\"/></Grid.RowDefinitions>" +
            "</Grid>";
        var loaded = XamlLoader.Load(xaml);
        var grid = Assert.IsType<Grid>(loaded.Root);
        var left = Assert.IsType<ColumnDefinition>(loaded.Names["Left"]);
        Assert.Same(grid.ColumnDefinitions[0], left);
        Assert.Equal(240, left.Width.Value);
        var top = Assert.IsType<RowDefinition>(loaded.Names["Top"]);
        Assert.Same(grid.RowDefinitions[0], top);
    }

    [Fact]
    public void Column_definition_plain_name_keeps_failing_like_wpf()
    {
        // ColumnDefinition has no Name property in WPF either: only x:Name
        // registers (namescope + field); plain Name stays loud.
        const string xaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<Grid.ColumnDefinitions><ColumnDefinition Name=\"Left\" Width=\"240\"/></Grid.ColumnDefinitions>" +
            "</Grid>";
        var error = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(xaml));
        Assert.Contains("Name is not valid on <ColumnDefinition> (only Width/MinWidth/MaxWidth/x:Name)", error.Message);
    }

    [Fact]
    public void Column_grid_fixture_hydrates_all_slice96_knobs()
    {
        Application.Current = new BootProbe.ProbeApp();
        var host = new BootProbe.ColumnGridHost();
        Assert.Equal(240, host.Left!.Width.Value);
        Assert.Equal(200, host.Left.MinWidth);
        Assert.Equal(400, host.Left.MaxWidth);
        Assert.NotNull(host.Top);
        Assert.Equal(TransitionMode.Fade, host.Host!.TransitionMode);
        var panel = Assert.IsType<SimplePanel>(host.Host.Parent);
        Assert.True(ScrollViewer.GetIsInertiaEnabled(panel));
    }

    public static IEnumerable<object[]> StagedAdvanceRows() => new[]
    {
        // Each row: the view, its new precise gap (the slice-95 gap is gone),
        // and the slice-95 fragment that must be gone.
        new object[] { "HandyControlDemo.UserControl.TextDialog", "StaticResource 'TextBlockLargeBold' was not found", "'{ex:Lang}' is not supported" },
        new object[] { "HandyControlDemo.UserControl.TextDialogWithTimer", "StaticResource 'TextBlockLargeBold' was not found", "'{ex:Lang}' is not supported" },
        new object[] { "HandyControlDemo.UserControl.AppNotification", "StaticResource 'LogoGeometry' was not found", "Unknown element <SimplePanel>" },
        new object[] { "HandyControlDemo.UserControl.MainWindowContent", "StaticResource 'ButtonIconCircular' was not found", "Name is not valid on <ColumnDefinition>" },
        new object[] { "HandyControlDemo.UserControl.LeftMainContent", "StaticResource 'TabControlInLine' was not found", "<Interaction.Triggers> does not belong to <TabControl>" },
        new object[] { "HandyControlDemo.UserControl.MainContent", "StaticResource 'EffectShadow4' was not found", "Unknown element <SimplePanel>" },
        new object[] { "HandyControlDemo.UserControl.UnderConstruction", "StaticResource 'TextBlockLargeBold' was not found", "Unknown element <TransitioningContentControl>" },
    };

    [Theory]
    [MemberData(nameof(StagedAdvanceRows))]
    public void Staged_views_advance_past_slice95_gaps(string typeName, string expectedFragment, string goneFragment)
    {
        var entry = LoadStagedHandyControlEntry();
        var type = entry.GetType(typeName, throwOnError: true)!;
        var error = Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(type));
        var load = Assert.IsType<XamlLoadException>(error.InnerException);
        Assert.Contains(expectedFragment, load.Message);
        Assert.DoesNotContain(goneFragment, load.Message);
        Assert.DoesNotContain("is not supported (Window/Page/Application/UserControl/Border only)", load.Message);
        for (var inner = error.InnerException; inner is not null; inner = inner.InnerException)
            Assert.IsNotType<MissingFieldException>(inner);
    }

    [Fact]
    public void Staged_lib_shapes_match_the_engine_fallback()
    {
        // The converted lib already ships these controls (they are the
        // evidence the engine fallback mirrors); the views fail only because
        // the loader never resolved converted-lib elements.
        LoadStagedHandyControlEntry();
        var lib = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.Ordinal));
        var panel = lib.GetType("HandyControl.Controls.SimplePanel", throwOnError: true)!;
        Assert.True(typeof(Panel).IsAssignableFrom(panel));
        var tcc = lib.GetType("HandyControl.Controls.TransitioningContentControl", throwOnError: true)!;
        Assert.True(typeof(ContentControl).IsAssignableFrom(tcc));
        var scroll = lib.GetType("HandyControl.Controls.ScrollViewer", throwOnError: true)!;
        Assert.NotNull(scroll.GetField("IsInertiaEnabledProperty",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy));
        var mode = lib.GetType("HandyControl.Data.TransitionMode", throwOnError: true)!;
        Assert.True(mode.IsEnum);
        Assert.Equal(0, (int)Enum.Parse(mode, "Right2Left"));
        Assert.Equal(8, (int)Enum.Parse(mode, "Fade"));
        Assert.Equal(9, (int)Enum.Parse(mode, "Custom"));
    }

    [Fact]
    public void Staged_manifest_keeps_two_absent_binary_gaps()
    {
        // No re-package this slice (Convert map untouched): 6 staged images
        // sha256-verified, exactly the 2 declared absent-binary gaps left.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(
            directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol", "app-manifest.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var apps = System.IO.Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol");
        using var document = JsonDocument.Parse(
            File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
        var images = new List<string>();
        foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
        {
            var file = assembly.GetProperty("file").GetString()!;
            var expected = assembly.GetProperty("sha256").GetString()!;
            var bytes = File.ReadAllBytes(System.IO.Path.Combine(apps, file));
            var actual = Convert.ToHexString(SHA256.HashData(bytes));
            Assert.True(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                $"hash mismatch in staged '{file}': {actual} != {expected}");
            images.Add(file);
        }
        Assert.Equal(6, images.Count);
        var gaps = document.RootElement.GetProperty("gaps").EnumerateArray().ToList();
        Assert.Equal(2, gaps.Count);
        foreach (var gap in gaps)
        {
            Assert.Equal("missing-dependency", gap.GetProperty("kind").GetString());
            Assert.Contains("absent from upstream build output", gap.GetProperty("detail").GetString());
        }
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
    /// <summary>Stand-in Border exercising the slice-96 XAML knobs: named
    /// ColumnDefinition/RowDefinition, SimplePanel, the IsInertiaEnabled
    /// attached knob and TransitionMode parsing.</summary>
    public sealed class ColumnGridHost : Border
    {
        internal ColumnDefinition? Left;
        internal RowDefinition? Top;
        internal TransitioningContentControl? Host;

        public ColumnGridHost() => InitializeComponent();

        public void InitializeComponent() =>
            Application.LoadComponent(this, new Uri("Boot/ColumnGrid.xaml", UriKind.Relative));
    }
}
