using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Wpf;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-63 FrameworkElement lifecycle/binding cluster: the largest coherent
/// remainder of the slice-61/62 engine-resolved member audit over the live
/// AvalonDock closure (FrameworkElement Style/ToolTip/ContextMenu/
/// FlowDirection/LayoutTransform slots, init tracking, logical children,
/// load state, template entry, resource lookup). Every engine shape below is
/// live-verified against the 9.0.21 WindowsDesktop ref pack (PresentationFramework
/// for the element surface, PresentationCore for UIPropertyMetadata); every
/// staged oracle loads the live staged AvalonDock bins (sha256-verified,
/// status gate bypassed in-probe only, manifests untouched).
/// </summary>
[ResetAmbientState]
public sealed class FrameworkLifecycleTests
{
    private sealed class ProbeElement : FrameworkElement
    {
        public int InitializedCalls;
        public void Add(object child) => AddLogicalChild(child);
        public void Remove(object child) => RemoveLogicalChild(child);
        protected override void OnInitialized(EventArgs e)
        {
            InitializedCalls++;
            base.OnInitialized(e);
        }
    }

    [Fact]
    public void Metadata_change_coerce_overloads_wire()
    {
        var changed = 0;
        var metadata = new FrameworkPropertyMetadata(
            "dflt",
            (_, _) => changed++,
            (_, v) => (string)v! == "bad" ? "good" : v);
        var dp = DependencyProperty.Register("Slice63Note", typeof(string), typeof(ProbeElement), metadata);
        var element = new ProbeElement();
        Assert.Equal("dflt", element.GetValue(dp));
        element.SetValue(dp, "bad");
        Assert.Equal("good", element.GetValue(dp));
        Assert.Equal(1, changed);

        var uiChanged = 0;
        var uiMetadata = new UIPropertyMetadata(
            1,
            (_, _) => uiChanged++,
            (_, v) => (int)v! < 0 ? 0 : v);
        var uiDp = DependencyProperty.Register("Slice63Level", typeof(int), typeof(ProbeElement), uiMetadata);
        element.SetValue(uiDp, -5);
        Assert.Equal(0, element.GetValue(uiDp));
        Assert.Equal(1, uiChanged);
    }

    [Fact]
    public void FrameworkPropertyMetadata_callback_only_overload_wires()
    {
        var changed = 0;
        var metadata = new FrameworkPropertyMetadata(
            (_, _) => changed++,
            (_, v) => v);
        var dp = DependencyProperty.Register("Slice63Flag", typeof(string), typeof(ProbeElement), metadata);
        var element = new ProbeElement();
        element.SetValue(dp, "x");
        Assert.Equal("x", element.GetValue(dp));
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Style_slot_applies_through_dp_and_clr()
    {
        var button = new Button();
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 77.0));
        button.Style = style;
        Assert.Same(style, button.Style);
        Assert.Same(style, button.GetValue(FrameworkElement.StyleProperty));
        Assert.Equal(77.0, button.Width);

        var replacement = new Style(typeof(Button));
        replacement.Setters.Add(new Setter(FrameworkElement.HeightProperty, 33.0));
        button.SetValue(FrameworkElement.StyleProperty, replacement);
        Assert.Same(replacement, button.Style);
        Assert.Equal(33.0, button.Height);
        Assert.Same(FrameworkElement.StyleProperty,
            DependencyProperty.FromName(typeof(Button), "Style"));
    }

    [Fact]
    public void Tooltip_slot_round_trips_through_the_store()
    {
        var button = new Button();
        Assert.Null(button.ToolTip);
        button.ToolTip = "tip";
        Assert.Equal("tip", button.GetValue(FrameworkElement.ToolTipProperty));
        button.SetValue(FrameworkElement.ToolTipProperty, "dp-tip");
        Assert.Equal("dp-tip", button.ToolTip);
        Assert.Same(FrameworkElement.ToolTipProperty,
            DependencyProperty.FromName(typeof(Button), "ToolTip"));
    }

    [Fact]
    public void Contextmenu_slot_round_trips_and_textbox_inherits_it()
    {
        var button = new Button();
        var menu = new ContextMenu();
        Assert.Null(button.ContextMenu);
        button.ContextMenu = menu;
        Assert.Same(menu, button.ContextMenu);
        Assert.Same(menu, button.GetValue(FrameworkElement.ContextMenuProperty));

        var box = new TextBox();
        box.ContextMenu = menu;
        Assert.Same(menu, box.ContextMenu);
        Assert.Same(menu, box.GetValue(FrameworkElement.ContextMenuProperty));
    }

    [Fact]
    public void Flowdirection_inherits_with_local_override()
    {
        var parent = new StackPanel();
        var child = new Button();
        parent.AddChild(child);
        Assert.Equal(FlowDirection.LeftToRight, FrameworkElement.GetFlowDirection(child));
        FrameworkElement.SetFlowDirection(parent, FlowDirection.RightToLeft);
        Assert.Equal(FlowDirection.RightToLeft, FrameworkElement.GetFlowDirection(child));
        child.FlowDirection = FlowDirection.LeftToRight;
        Assert.Equal(FlowDirection.LeftToRight, FrameworkElement.GetFlowDirection(child));
        Assert.Equal(FlowDirection.RightToLeft, parent.FlowDirection);
        Assert.Throws<ArgumentNullException>(() => FrameworkElement.GetFlowDirection(null!));
        Assert.Throws<ArgumentNullException>(() => FrameworkElement.SetFlowDirection(null!, FlowDirection.RightToLeft));
    }

    [Fact]
    public void Layouttransform_defaults_to_identity_and_round_trips()
    {
        var button = new Button();
        Assert.Same(Transform.Identity, button.LayoutTransform);
        var transform = new MatrixTransform(Matrix.Identity);
        button.LayoutTransform = transform;
        Assert.Same(transform, button.LayoutTransform);
        Assert.Same(transform, button.GetValue(FrameworkElement.LayoutTransformProperty));
        button.ClearValue(FrameworkElement.LayoutTransformProperty);
        Assert.Same(Transform.Identity, button.LayoutTransform);
    }

    [Fact]
    public void Init_tracking_fires_once_through_the_override()
    {
        var element = new ProbeElement();
        Assert.False(element.IsInitialized);
        var fired = 0;
        element.Initialized += (_, _) => fired++;
        element.BeginInit();
        element.BeginInit();
        element.EndInit();
        Assert.False(element.IsInitialized);
        element.EndInit();
        Assert.True(element.IsInitialized);
        Assert.Equal(1, element.InitializedCalls);
        Assert.Equal(1, fired);
        element.EndInit();
        Assert.Equal(1, element.InitializedCalls);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Logical_children_parent_and_retain()
    {
        var host = new ProbeElement();
        var child = new Button();
        host.Add(child);
        Assert.Same(host, child.Parent);
        Assert.Contains(child, host.LogicalChildren);
        host.Remove(child);
        Assert.Null(child.Parent);
        Assert.DoesNotContain(child, host.LogicalChildren);

        var model = new object();
        host.Add(model);
        host.Remove(model);
        Assert.Null(Record.Exception(() => host.Add(null!)));
        Assert.Null(Record.Exception(() => host.Remove(null!)));
    }

    [Fact]
    public void Nonvisual_logical_children_are_retained()
    {
        var host = new ProbeElement();
        var model = new object();
        host.Add(model);
        var retained = (List<object?>)typeof(FrameworkElement)
            .GetField("_nonVisualLogicalChildren", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(host)!;
        Assert.Contains(model, retained);
        host.Remove(model);
        Assert.DoesNotContain(model, retained);
    }

    [Fact]
    public void Unloaded_only_fires_for_loaded_subtrees()
    {
        var host = new StackPanel();
        var child = new Button();
        host.AddChild(child);
        var fired = 0;
        child.Unloaded += (_, _) => fired++;
        Assert.False(child.IsLoaded);
        Assert.True(host.RemoveChild(child));
        Assert.Equal(0, fired);
        Assert.False(child.IsLoaded);
    }

    [Fact]
    public void Applytemplate_base_returns_false_and_control_overrides()
    {
        Assert.False(new ProbeElement().ApplyTemplate());
        Assert.False(new Button().ApplyTemplate());
        var feApply = typeof(FrameworkElement).GetMethod("ApplyTemplate", Type.EmptyTypes)!;
        Assert.Equal(typeof(bool), feApply.ReturnType);
        var controlApply = typeof(Control).GetMethod("ApplyTemplate", Type.EmptyTypes)!;
        Assert.Same(feApply, controlApply.GetBaseDefinition());
    }

    [Fact]
    public void Tryfindresource_single_arg_returns_null_when_absent()
    {
        var button = new Button();
        Assert.Null(button.TryFindResource("Slice63Missing"));
        button.Resources["Slice63Key"] = "Slice63Value";
        Assert.Equal("Slice63Value", button.TryFindResource("Slice63Key"));
        var child = new Button();
        button.AddChild(child);
        Assert.Equal("Slice63Value", child.TryFindResource("Slice63Key"));
        Assert.Throws<ArgumentNullException>(() => button.TryFindResource(null!));
    }

    [Fact]
    public void Cluster_member_shapes_match_the_converted_callsites()
    {
        var fe = typeof(FrameworkElement);
        foreach (var name in new[]
        {
            "StyleProperty", "ToolTipProperty", "ContextMenuProperty",
            "FlowDirectionProperty", "LayoutTransformProperty",
        })
            Assert.IsType<DependencyProperty>(fe.GetField(name,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)!
                .GetValue(null));
        var getFlow = fe.GetMethod("GetFlowDirection",
            BindingFlags.Public | BindingFlags.Static)!;
        Assert.Equal(typeof(FlowDirection), getFlow.ReturnType);
        Assert.Equal(typeof(DependencyObject), fe.GetMethod("GetFlowDirection",
            BindingFlags.Public | BindingFlags.Static)!.GetParameters()[0].ParameterType);
        var add = fe.GetMethod("AddLogicalChild",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.True(add.IsFamilyOrAssembly);
        Assert.Equal(typeof(void), add.ReturnType);
        var remove = fe.GetMethod("RemoveLogicalChild",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.True(remove.IsFamilyOrAssembly);
        var init = fe.GetMethod("OnInitialized",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.True(init.IsFamily);
        Assert.Equal(typeof(EventArgs), init.GetParameters()[0].ParameterType);
        Assert.NotNull(fe.GetMethod("BeginInit"));
        Assert.NotNull(fe.GetMethod("EndInit"));
        Assert.NotNull(fe.GetProperty("IsInitialized"));
        Assert.NotNull(fe.GetProperty("IsLoaded"));
        Assert.NotNull(fe.GetEvent("Initialized"));
        Assert.NotNull(fe.GetEvent("Unloaded"));
        var single = fe.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == "TryFindResource" && m.GetParameters().Length == 1);
        Assert.Equal(typeof(object), single.ReturnType);
        Assert.Equal(typeof(ContextMenu), fe.GetProperty("ContextMenu")!.PropertyType);
        Assert.Equal(typeof(Transform), fe.GetProperty("LayoutTransform")!.PropertyType);
        Assert.NotNull(typeof(UIPropertyMetadata).GetConstructor(
            new[] { typeof(object), typeof(PropertyChangedCallback), typeof(CoerceValueCallback) }));
        Assert.NotNull(typeof(FrameworkPropertyMetadata).GetConstructor(
            new[] { typeof(object), typeof(PropertyChangedCallback), typeof(CoerceValueCallback) }));
        Assert.NotNull(typeof(FrameworkPropertyMetadata).GetConstructor(
            new[] { typeof(PropertyChangedCallback), typeof(CoerceValueCallback) }));
    }

    [Fact]
    public void Staged_avalondock_hashes_verify()
    {
        Assert.Empty(StagedAvalonDockRuntimeLoader.VerifyHashes());
    }

    [Fact]
    public void Converted_layoutitem_cctor_runs()
    {
        var assembly = StagedAvalonDockRuntimeLoader.LoadAssembly("AvalonDock");
        var item = assembly.GetType("AvalonDock.Controls.LayoutItem", throwOnError: true)!;
        RuntimeHelpers.RunClassConstructor(item.TypeHandle);
        Assert.NotNull(item.GetField("TitleProperty",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)!.GetValue(null));
    }

    [Fact]
    public void Converted_dropdown_constructs_and_initializes()
    {
        var assembly = StagedAvalonDockRuntimeLoader.LoadAssembly("AvalonDock");
        var dropType = assembly.GetType("AvalonDock.Controls.DropDownButton", throwOnError: true)!;
        var drop = (FrameworkElement)Activator.CreateInstance(dropType)!;
        Assert.False(drop.IsInitialized);
        var fired = 0;
        drop.Initialized += (_, _) => fired++;
        drop.BeginInit();
        drop.EndInit();
        Assert.True(drop.IsInitialized);
        Assert.Equal(1, fired);
        drop.EndInit();
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Converted_dropdown_logical_children_parent()
    {
        var assembly = StagedAvalonDockRuntimeLoader.LoadAssembly("AvalonDock");
        var dropType = assembly.GetType("AvalonDock.Controls.DropDownButton", throwOnError: true)!;
        var drop = (FrameworkElement)Activator.CreateInstance(dropType)!;
        var add = typeof(FrameworkElement).GetMethod("AddLogicalChild",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var remove = typeof(FrameworkElement).GetMethod("RemoveLogicalChild",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var child = new Button();
        add.Invoke(drop, new object[] { child });
        Assert.Same(drop, child.Parent);
        Assert.Contains(child, drop.LogicalChildren);
        remove.Invoke(drop, new object[] { child });
        Assert.Null(child.Parent);

        var model = new object();
        add.Invoke(drop, new object[] { model });
        var retained = (List<object?>)typeof(FrameworkElement)
            .GetField("_nonVisualLogicalChildren", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(drop)!;
        Assert.Contains(model, retained);
        remove.Invoke(drop, new object[] { model });
        Assert.DoesNotContain(model, retained);
    }

    [Fact]
    public void Converted_dropdown_template_style_menu_transform_resource_paths()
    {
        var assembly = StagedAvalonDockRuntimeLoader.LoadAssembly("AvalonDock");
        var dropType = assembly.GetType("AvalonDock.Controls.DropDownButton", throwOnError: true)!;
        var drop = (FrameworkElement)Activator.CreateInstance(dropType)!;
        Assert.False(drop.ApplyTemplate());

        var menu = new ContextMenu();
        drop.ContextMenu = menu;
        Assert.Same(menu, drop.GetValue(FrameworkElement.ContextMenuProperty));

        var transform = new MatrixTransform(Matrix.Identity);
        drop.LayoutTransform = transform;
        Assert.Same(transform, drop.LayoutTransform);

        var style = new Style(typeof(Control));
        style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 41.0));
        drop.SetValue(FrameworkElement.StyleProperty, style);
        Assert.Same(style, drop.Style);
        Assert.Equal(41.0, drop.Width);

        Assert.Null(drop.TryFindResource("Slice63Missing"));
        drop.Resources["Slice63Key"] = "Slice63Value";
        Assert.Equal("Slice63Value", drop.TryFindResource("Slice63Key"));

        Assert.Equal(FlowDirection.LeftToRight, FrameworkElement.GetFlowDirection(drop));
        drop.SetValue(FrameworkElement.FlowDirectionProperty, FlowDirection.RightToLeft);
        Assert.Equal(FlowDirection.RightToLeft, FrameworkElement.GetFlowDirection(drop));
    }

    [Fact]
    public void Converted_transform_advances_past_flowdirection()
    {
        // Slice-68 re-pin: Visual.PointToScreen now binds, so the converted
        // DPI probe runs clean on an unrooted button (identity at the default
        // session scale) instead of throwing MissingMethodException.
        var assembly = StagedAvalonDockRuntimeLoader.LoadAssembly("AvalonDock");
        var transforms = assembly.GetType("AvalonDock.Controls.TransformExtensions", throwOnError: true)!;
        var probe = transforms.GetMethod("PointToScreenDPIWithoutFlowDirection",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var pointType = typeof(DesktopWasm.Wpf.Point);
        var point = Activator.CreateInstance(pointType, 10.0, 20.0)!;
        var result = probe.Invoke(null, new[] { new Button(), point });
        Assert.Equal(new DesktopWasm.Wpf.Point(10.0, 20.0), result);
    }

    [Fact]
    public void Converted_oninitialized_override_binds_the_new_base()
    {
        var assembly = StagedAvalonDockRuntimeLoader.LoadAssembly("AvalonDock");
        var open = assembly.GetType("AvalonDock.Controls.LayoutGridControl`1", throwOnError: true)!;
        var panel = assembly.GetType("AvalonDock.Layout.LayoutPanel", throwOnError: true)!;
        var closed = open.MakeGenericType(panel);
        var over = closed.GetMethod("OnInitialized",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        var engineBase = typeof(FrameworkElement).GetMethod("OnInitialized",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.Same(engineBase, over.GetBaseDefinition());
    }

    [Fact]
    public void Converted_dockingmanager_builds_past_the_hwndsource_trio()
    {
        // Slice-65 declares the HwndSource trio plus the Window chrome/events
        // cluster: the converted DockingManager .cctor and instance .ctor both
        // run (the trio MissingMethod is gone, not merely moved).
        var assembly = StagedAvalonDockRuntimeLoader.LoadAssembly("AvalonDock");
        var manager = assembly.GetType("AvalonDock.DockingManager", throwOnError: true)!;
        var instance = Activator.CreateInstance(manager)!;
        Assert.IsAssignableFrom<FrameworkElement>(instance);
    }

/// <summary>
/// Slice-63 boot oracle: the staged PC Space Map entry boots through the
/// new lifecycle surface (loaded tree marks <see cref="FrameworkElement.IsLoaded"/>)
/// and detach raises <see cref="FrameworkElement.Unloaded"/>. Serialized with
/// the other Application-state tests; restores global state on dispose.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class FrameworkLifecycleBootTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public FrameworkLifecycleBootTests() => WpfAnimationClock.ResetForTests();

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
        WpfAnimationClock.ResetForTests();
    }

    [Fact]
    public void Staged_pcspacemap_boot_loads_and_unloads()
    {
        Application.Current = null;
        var entry = LoadStagedPcSpaceMapEntry();
        var window = WpfAppBoot.Boot(entry);
        Assert.True(window.IsLoaded);
        var grid = Assert.IsType<Grid>(window.Content);
        var child = grid.LogicalChildren.OfType<FrameworkElement>().First();
        Assert.True(child.IsLoaded);
        var unloadFired = 0;
        child.Unloaded += (_, _) => unloadFired++;
        Assert.True(grid.RemoveChild(child));
        Assert.Equal(1, unloadFired);
        Assert.False(child.IsLoaded);
        Assert.True(window.IsLoaded);
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
