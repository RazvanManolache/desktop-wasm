using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-66 UIElement 13-row cluster: the converted AvalonDock closure
/// references add/remove_IsVisibleChanged, get_IsVisible, CaptureMouse,
/// ReleaseMouseCapture, get_IsMouseCaptured, get_RenderSize, InputHitTest,
/// IsHitTestVisibleProperty, OnLostMouseCapture, OnMouseRightButtonDown/Up
/// and PreviewMouseRightButtonUpEvent (each pinned to its converted user
/// below), and the engine had none of them. Every shape below is
/// live-verified against the 9.0.21 WindowsDesktop runtime:
/// IsVisible is a non-virtual get-only computed from Visibility plus
/// ancestors with a DependencyPropertyChangedEventHandler IsVisibleChanged
/// whose args carry the public read-only IsVisibleProperty;
/// CaptureMouse/ReleaseMouseCapture/IsMouseCaptured are virtual with
/// capture gated on rooted elements; get_RenderSize is non-virtual on
/// UIElement; InputHitTest(Point) returns the topmost IInputElement or
/// null; IsHitTestVisibleProperty is bool defaulting true;
/// OnLostMouseCapture is a protected virtual raising LostMouseCapture; the
/// right-button family is Direct-routed with MouseButtonEventHandler.
/// Staged oracles load the live staged AvalonDock bins (sha256-verified,
/// status gate bypassed in-probe only, manifests untouched).
/// Deliberately not declared: UIElement.GotMouseCaptureEvent/
/// LostMouseCaptureEvent (real WPF owns them on System.Windows.Input.Mouse,
/// and the engine's mouse events stay CLR-only like the left-button pair),
/// which no converted ref needs.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice66UiElementTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private readonly WasmInputRouter _router = new();
    private readonly WpfInputManager _manager;

    public Slice66UiElementTests()
    {
        WpfAnimationClock.ResetForTests();
        WasmDispatcher.Current = new WasmDispatcher(new WasmDispatcherPump());
        _manager = new WpfInputManager(_router);
    }

    public void Dispose() => WpfAnimationClock.ResetForTests();

    private class ProbeBox : FrameworkElement
    {
        public ProbeBox(double x, double y, double w, double h) => Frame = new WpfRect(x, y, w, h);

        public WpfRect Frame { get; }

        public override bool Focusable => true;
        protected override WpfSize MeasureCore(WpfSize available) => new(Frame.Width, Frame.Height);
        protected override void ArrangeCore(WpfRect finalRect) { }
        protected override void OnRender(DrawingContext dc) { }

        public void Layout()
        {
            Measure(new WpfSize(Frame.Width, Frame.Height));
            Arrange(Frame);
        }
    }

    private sealed class RightButtonProbe : ProbeBox
    {
        public RightButtonProbe(double x, double y, double w, double h)
            : base(x, y, w, h) { }

        public int DownCalls;
        public int UpCalls;

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e) => DownCalls++;
        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e) => UpCalls++;
    }

    private void Pointer(double x, double y, WasmPointerButton button, bool isDown, WasmPointerKind kind, int id = 1) =>
        _router.DispatchPointer(new WasmPointerArgs(new WasmWindowHandle(1), x, y, button, isDown) { Kind = kind, PointerId = id });

    private void RightPress(double x, double y) => Pointer(x, y, WasmPointerButton.Right, true, WasmPointerKind.Down);
    private void RightRelease(double x, double y) => Pointer(x, y, WasmPointerButton.Right, false, WasmPointerKind.Up);

    [Fact]
    public void Staged_avalondock_hashes_verify()
    {
        Assert.Empty(StagedAvalonDockUiElementLoader.VerifyHashes());
    }

    [Fact]
    public void Cluster_shapes_resolve_by_arity()
    {
        var ui = typeof(UIElement);
        const BindingFlags instance = BindingFlags.Public | BindingFlags.Instance;
        Assert.NotNull(ui.GetMethod("add_IsVisibleChanged", instance));
        Assert.NotNull(ui.GetMethod("remove_IsVisibleChanged", instance));
        Assert.NotNull(ui.GetMethod("get_IsVisible", instance));
        Assert.NotNull(ui.GetMethod("CaptureMouse", instance));
        Assert.NotNull(ui.GetMethod("ReleaseMouseCapture", instance));
        Assert.NotNull(ui.GetMethod("get_IsMouseCaptured", instance));
        Assert.NotNull(ui.GetMethod("get_RenderSize", instance));
        Assert.NotNull(ui.GetMethod("InputHitTest", instance));
        Assert.NotNull(ui.GetField("IsHitTestVisibleProperty", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy));
        Assert.NotNull(ui.GetField("IsVisibleProperty", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy));
        Assert.NotNull(ui.GetMethod("OnLostMouseCapture", BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.NotNull(ui.GetMethod("OnMouseRightButtonDown", BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.NotNull(ui.GetMethod("OnMouseRightButtonUp", BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.NotNull(ui.GetField("PreviewMouseRightButtonUpEvent", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy));
    }

    [Fact]
    public void Cluster_dp_and_routed_identities_match_wpf()
    {
        Assert.Same(typeof(UIElement), UIElement.IsVisibleProperty.OwnerType);
        Assert.Equal(typeof(bool), UIElement.IsVisibleProperty.PropertyType);
        Assert.Equal(false, UIElement.IsVisibleProperty.DefaultValue);
        Assert.Same(typeof(UIElement), UIElement.IsHitTestVisibleProperty.OwnerType);
        Assert.Equal(typeof(bool), UIElement.IsHitTestVisibleProperty.PropertyType);
        Assert.Equal(true, UIElement.IsHitTestVisibleProperty.DefaultValue);
        Assert.Same(UIElement.IsHitTestVisibleProperty, DependencyProperty.FromName(typeof(Border), "IsHitTestVisible"));
        Assert.Same(UIElement.IsVisibleProperty, DependencyProperty.FromName(typeof(Border), "IsVisible"));
        Assert.Equal("PreviewMouseRightButtonUp", UIElement.PreviewMouseRightButtonUpEvent.Name);
        Assert.Equal(RoutingStrategy.Direct, UIElement.PreviewMouseRightButtonUpEvent.RoutingStrategy);
        Assert.Same(typeof(MouseButtonEventHandler), UIElement.PreviewMouseRightButtonUpEvent.HandlerType);
        Assert.Same(typeof(UIElement), UIElement.PreviewMouseRightButtonUpEvent.OwnerType);
    }

    [Fact]
    public void Converted_cluster_users_load_without_missing_members()
    {
        var assembly = StagedAvalonDockUiElementLoader.LoadAssembly("AvalonDock");
        var users = new[]
        {
            "AvalonDock.Controls.LayoutAnchorableFloatingWindowControl",
            "AvalonDock.Controls.LayoutDocumentTabItem",
            "AvalonDock.DockingManager",
            "AvalonDock.Controls.ToggleDockDragOverlay",
            "AvalonDock.Controls.AnchorablePaneTitle",
            "AvalonDock.Controls.LayoutAnchorablePaneControl",
            "AvalonDock.Controls.LayoutAnchorControl",
            "AvalonDock.Controls.DropDownControlArea",
        };
        foreach (var name in users)
        {
            var type = assembly.GetType(name, throwOnError: true)!;
            try
            {
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            }
            catch (Exception ex)
            {
                var chain = new List<string>();
                for (var e = ex; e is not null; e = e.InnerException)
                    chain.Add(e.GetType().Name + ": " + e.Message.Split('\n')[0]);
                var joined = string.Join(" ||| ", chain);
                Assert.DoesNotContain("IsVisibleChanged", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("CaptureMouse", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("MouseCaptured", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("RenderSize", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("InputHitTest", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("IsHitTestVisible", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("MouseCapture", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("MouseRightButton", joined, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Converted_dockingmanager_still_constructs()
    {
        var assembly = StagedAvalonDockUiElementLoader.LoadAssembly("AvalonDock");
        var manager = assembly.GetType("AvalonDock.DockingManager", throwOnError: true)!;
        var instance = Activator.CreateInstance(manager)!;
        Assert.IsAssignableFrom<FrameworkElement>(instance);
    }

    [Fact]
    public void IsVisible_defaults_true_and_flips_on_visibility()
    {
        var box = new ProbeBox(0, 0, 10, 10);
        box.Layout();
        Assert.True(box.IsVisible);

        var seen = new List<string>();
        box.IsVisibleChanged += (_, e) => seen.Add($"{e.Property.Name}:{e.OldValue}->{e.NewValue}");
        box.Visibility = Visibility.Collapsed;
        Assert.False(box.IsVisible);
        Assert.Equal(["IsVisible:True->False"], seen);
        box.Visibility = Visibility.Visible;
        Assert.True(box.IsVisible);
        Assert.Equal(["IsVisible:True->False", "IsVisible:False->True"], seen);
    }

    [Fact]
    public void IsVisible_args_carry_the_read_only_dp()
    {
        var box = new ProbeBox(0, 0, 10, 10);
        box.Layout();
        DependencyPropertyChangedEventArgs args = default;
        var calls = 0;
        DependencyPropertyChangedEventHandler handler = (_, e) => { calls++; args = e; };
        box.IsVisibleChanged += handler;
        box.Visibility = Visibility.Hidden;
        Assert.Equal(1, calls);
        Assert.Same(UIElement.IsVisibleProperty, args.Property);
        Assert.Equal(true, args.OldValue);
        Assert.Equal(false, args.NewValue);
        box.IsVisibleChanged -= handler;
        box.Visibility = Visibility.Visible;
        Assert.Equal(1, calls);
        Assert.True(box.IsVisible);
    }

    [Fact]
    public void IsVisible_cascades_through_parent_and_detach()
    {
        var parent = new StackPanel();
        var child = new ProbeBox(0, 0, 10, 10);
        parent.AddChild(child);
        Assert.True(child.IsVisible);

        var flips = 0;
        child.IsVisibleChanged += (_, _) => flips++;
        parent.Visibility = Visibility.Collapsed;
        Assert.False(child.IsVisible);
        Assert.Equal(1, flips);

        parent.RemoveChild(child);
        Assert.True(child.IsVisible);
        Assert.Equal(2, flips);
    }

    [Fact]
    public void Window_hide_flips_effective_visibility()
    {
        var window = new Window();
        Assert.True(window.IsVisible);
        Assert.True(((UIElement)window).IsVisible);

        var flips = 0;
        window.IsVisibleChanged += (_, _) => flips++;
        window.Hide();
        Assert.False(window.IsVisible);
        Assert.False(((UIElement)window).IsVisible);
        Assert.Equal(1, flips);

        window.Show();
        Assert.True(window.IsVisible);
        Assert.True(((UIElement)window).IsVisible);
        Assert.Equal(2, flips);
    }

    [Fact]
    public void IsHitTestVisible_round_trips_through_clr_and_dp()
    {
        var box = new ProbeBox(0, 0, 10, 10);
        Assert.True(box.IsHitTestVisible);
        Assert.Equal(true, box.GetValue(UIElement.IsHitTestVisibleProperty));
        box.IsHitTestVisible = false;
        Assert.False(box.IsHitTestVisible);
        Assert.Equal(false, box.GetValue(UIElement.IsHitTestVisibleProperty));
        box.SetValue(UIElement.IsHitTestVisibleProperty, true);
        Assert.True(box.IsHitTestVisible);
    }

    [Fact]
    public void IsHitTestVisible_xaml_attribute_lands()
    {
        var window = Assert.IsType<Window>(XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\">" +
            "<Border IsHitTestVisible=\"False\" /></Window>").Root);
        var border = Assert.IsType<Border>(window.Content);
        Assert.False(border.IsHitTestVisible);
        Assert.Equal(false, border.GetValue(UIElement.IsHitTestVisibleProperty));
    }

    [Fact]
    public void RenderSize_is_uielement_and_tracks_arrange()
    {
        Assert.NotNull(typeof(UIElement).GetMethod(
            "get_RenderSize", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        var box = new ProbeBox(5, 7, 100, 50);
        box.Layout();
        Assert.Equal(new Size(100, 50), box.RenderSize);
        Assert.Equal(new Size(100, 50), ((UIElement)box).RenderSize);
    }

    [Fact]
    public void CaptureMouse_round_trips_with_got_and_lost()
    {
        var first = new ProbeBox(0, 0, 100, 100);
        first.Layout();
        var second = new ProbeBox(100, 0, 100, 100);
        second.Layout();
        _manager.AddRoot(first);
        _manager.AddRoot(second);

        var seen = new List<string>();
        first.GotMouseCapture += (_, _) => seen.Add("first-got");
        first.LostMouseCapture += (_, _) => seen.Add("first-lost");
        second.GotMouseCapture += (_, _) => seen.Add("second-got");
        second.LostMouseCapture += (_, _) => seen.Add("second-lost");

        Assert.False(first.IsMouseCaptured);
        Assert.True(first.CaptureMouse());
        Assert.True(first.IsMouseCaptured);
        Assert.Same(first, Mouse.Captured);
        Assert.Equal(["first-got"], seen);

        Assert.True(second.CaptureMouse());
        Assert.False(first.IsMouseCaptured);
        Assert.True(second.IsMouseCaptured);
        Assert.Equal(["first-got", "first-lost", "second-got"], seen);

        second.ReleaseMouseCapture();
        Assert.Null(Mouse.Captured);
        Assert.False(second.IsMouseCaptured);
        Assert.Equal(["first-got", "first-lost", "second-got", "second-lost"], seen);
    }

    [Fact]
    public void CaptureMouse_is_silent_when_unchanged_or_unrooted()
    {
        var box = new ProbeBox(0, 0, 100, 100);
        box.Layout();
        _manager.AddRoot(box);

        var calls = 0;
        box.GotMouseCapture += (_, _) => calls++;
        box.LostMouseCapture += (_, _) => calls++;
        Assert.True(box.CaptureMouse());
        Assert.Equal(1, calls);
        Assert.True(box.CaptureMouse());
        Assert.Equal(1, calls);

        var stray = new ProbeBox(0, 0, 10, 10);
        stray.Layout();
        Assert.False(stray.CaptureMouse());
        Assert.False(stray.IsMouseCaptured);

        stray.ReleaseMouseCapture();
        Assert.Equal(1, calls);
        Assert.Same(box, Mouse.Captured);
    }

    [Fact]
    public void Right_button_press_dispatches_clr_and_routed()
    {
        var box = new ProbeBox(10, 10, 100, 50);
        box.Layout();
        _manager.AddRoot(box);

        var seen = new List<string>();
        box.PreviewMouseRightButtonDown += (_, _) => seen.Add("preview-down");
        box.MouseRightButtonDown += (_, _) => seen.Add("down");
        box.PreviewMouseRightButtonUp += (_, _) => seen.Add("preview-up");
        box.MouseRightButtonUp += (_, _) => seen.Add("up");
        box.MouseRightButtonDown += (_, _) => { };
        box.PreviewMouseLeftButtonDown += (_, _) => seen.Add("LEFT-DOWN");
        box.MouseLeftButtonDown += (_, _) => seen.Add("LEFT");

        var routed = new List<string>();
        box.AddHandler(UIElement.PreviewMouseRightButtonUpEvent, new MouseButtonEventHandler((_, _) => routed.Add("routed-preview-up")));
        box.AddHandler(UIElement.MouseRightButtonUpEvent, new MouseButtonEventHandler((_, _) => routed.Add("routed-up")));

        RightPress(30, 30);
        Assert.Equal(["preview-down", "down"], seen);
        Assert.Empty(routed);

        RightRelease(30, 30);
        Assert.Equal(["preview-down", "down", "preview-up", "up"], seen);
        Assert.Equal(["routed-preview-up", "routed-up"], routed);
    }

    [Fact]
    public void Converted_style_right_button_overrides_bind()
    {
        var box = new RightButtonProbe(10, 10, 100, 50);
        box.Layout();
        _manager.AddRoot(box);

        RightPress(30, 30);
        RightRelease(30, 30);
        Assert.Equal(1, box.DownCalls);
        Assert.Equal(1, box.UpCalls);
    }

    [Fact]
    public void InputHitTest_returns_topmost_and_null_on_miss()
    {
        var canvas = new Canvas();
        var back = new ProbeBox(0, 0, 200, 200);
        var front = new ProbeBox(0, 0, 200, 200);
        canvas.AddChild(back);
        canvas.AddChild(front);
        canvas.Measure(new WpfSize(200, 200));
        canvas.Arrange(new WpfRect(0, 0, 200, 200));
        back.Layout();
        front.Layout();

        Assert.Same(front, canvas.InputHitTest(new Point(50, 50)));
        Assert.Null(canvas.InputHitTest(new Point(500, 500)));

        front.Visibility = Visibility.Collapsed;
        Assert.Same(back, canvas.InputHitTest(new Point(50, 50)));

        front.Visibility = Visibility.Visible;
        front.IsHitTestVisible = false;
        Assert.Same(back, canvas.InputHitTest(new Point(50, 50)));
    }

    private static class StagedAvalonDockUiElementLoader
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
            while (directory is not null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "src",
                "DesktopWasm.BrowserHost", "wwwroot", "apps", "avalondock", "app-manifest.json")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            var apps = System.IO.Path.Combine(directory!.FullName, "src",
                "DesktopWasm.BrowserHost", "wwwroot", "apps", "avalondock");
            using var document = JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
            var entries = new List<(string, string, byte[], string)>();
            foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
            {
                var file = assembly.GetProperty("file").GetString()!;
                var expected = assembly.GetProperty("sha256").GetString()!;
                var bytes = System.IO.File.ReadAllBytes(System.IO.Path.Combine(apps, file));
                entries.Add((assembly.GetProperty("name").GetString()!, file, bytes, expected));
            }
            return entries;
        }
    }
}
