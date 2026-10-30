using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Graphics;
using DesktopWasm.Wpf;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-65 HwndSource trio + Window chrome/events: the converted AvalonDock
/// closure failed to JIT the DockingManager static constructor on
/// MissingMethodException for
/// HwndSource.set_DefaultAcquireHwndFocusInMenuMode, and the floating-window,
/// overlay, drop-target and WindowChromeWorker code reads Left/Top/Owner/
/// Icon/AllowsTransparency/RestoreBounds plus Activate/ShowDialog and the
/// OnClosed/OnClosing/OnStateChanged/SourceInitialized entries. Every shape
/// below is live-verified against the .NET 9 WindowsDesktop runtime
/// (DefaultAcquireHwndFocusInMenuMode defaults true; HwndSource.SizeToContent
/// is a plain CLR property with no backing field; FromHwnd is static and
/// returns HwndSource; HwndTarget.BackgroundColor is a read/write Color;
/// Window.Left/Top share the Canvas instances with NaN defaults; Icon is an
/// ImageSource DP defaulting null; Owner/RestoreBounds are plain CLR with no
/// backing fields; Activate returns bool; ShowDialog returns bool?;
/// OnClosed/OnClosing/OnStateChanged/OnSourceInitialized are protected
/// virtuals; SourceInitialized is an EventHandler). Staged oracles load the
/// live staged AvalonDock bins (sha256-verified, status gate bypassed
/// in-probe only, manifests untouched). Out of scope on purpose: the
/// remaining UIElement audit rows (IsVisible/RenderSize/mouse-capture/
/// right-button/InputHitTest/IsHitTestVisible) and the DrawingContext,
/// ContextMenu, ContentPresenter, GridLength, TabControl, Grid, MenuItem and
/// singleton clusters.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice65HwndWindowTests
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private sealed class ClosingProbe : Window
    {
        public int ClosingCalls;
        public int ClosedCalls;
        public int StateCalls;
        public void RaiseSource() => OnSourceInitialized(EventArgs.Empty);
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            ClosingCalls++;
            base.OnClosing(e);
        }
        protected override void OnClosed(EventArgs e)
        {
            ClosedCalls++;
            base.OnClosed(e);
        }
        protected override void OnStateChanged(EventArgs e)
        {
            StateCalls++;
            base.OnStateChanged(e);
        }
    }

    [Fact]
    public void Staged_avalondock_hashes_verify()
    {
        Assert.Empty(StagedAvalonDockHwndLoader.VerifyHashes());
    }

    [Fact]
    public void Hwndsource_static_default_is_true_like_wpf()
    {
        // The compiled-in default is pinned through a fresh collectible load:
        // converted DockingManager .cctors in sibling staged oracles flip the
        // ambient static to false, so reading the ambient value would race.
        var alc = new System.Runtime.Loader.AssemblyLoadContext("slice65-default-probe", isCollectible: true);
        try
        {
            using var stream = System.IO.File.OpenRead(typeof(HwndSource).Assembly.Location);
            var fresh = alc.LoadFromStream(stream);
            var hwnd = fresh.GetType("DesktopWasm.Wpf.HwndSource", throwOnError: true)!;
            Assert.True((bool)hwnd.GetProperty("DefaultAcquireHwndFocusInMenuMode")!.GetValue(null)!);
        }
        finally
        {
            alc.Unload();
        }

        var previous = HwndSource.DefaultAcquireHwndFocusInMenuMode;
        try
        {
            HwndSource.DefaultAcquireHwndFocusInMenuMode = false;
            Assert.False(HwndSource.DefaultAcquireHwndFocusInMenuMode);
        }
        finally
        {
            HwndSource.DefaultAcquireHwndFocusInMenuMode = previous;
        }
    }

    [Fact]
    public void Hwndsource_sizetocontent_is_plain_clr_defaulting_to_manual()
    {
        Assert.Null(typeof(HwndSource).GetField("SizeToContentProperty",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy));
        using var source = new HwndSource(new HwndSourceParameters("slice65", 800, 600));
        Assert.Equal(SizeToContent.Manual, source.SizeToContent);
        source.SizeToContent = SizeToContent.WidthAndHeight;
        Assert.Equal(SizeToContent.WidthAndHeight, source.SizeToContent);
    }

    [Fact]
    public void Fromhwnd_reports_null_for_any_handle()
    {
        Assert.Null(HwndSource.FromHwnd(IntPtr.Zero));
        Assert.Null(HwndSource.FromHwnd(new IntPtr(12345)));
    }

    [Fact]
    public void Hwndtarget_backgroundcolor_is_retained()
    {
        using var source = new HwndSource(new HwndSourceParameters());
        Assert.Equal(Colors.Transparent, source.CompositionTarget.BackgroundColor);
        var tint = Color.FromRgb(0x11, 0x22, 0x33);
        source.CompositionTarget.BackgroundColor = tint;
        Assert.Equal(tint, source.CompositionTarget.BackgroundColor);
    }

    [Fact]
    public void Canvas_offsets_are_attached_dps_defaulting_to_nan()
    {
        Assert.Same(Canvas.LeftProperty, DependencyProperty.FromName(typeof(Canvas), "Left"));
        Assert.Same(Canvas.TopProperty, DependencyProperty.FromName(typeof(Canvas), "Top"));
        Assert.Equal(typeof(double), Canvas.LeftProperty.PropertyType);
        var child = new Border();
        Assert.True(double.IsNaN(Canvas.GetLeft(child)));
        Assert.True(double.IsNaN(Canvas.GetTop(child)));
        Assert.True(double.IsNaN((double)child.GetValue(Canvas.LeftProperty)!));
    }

    [Fact]
    public void Canvas_getset_round_trips_through_store_and_bag()
    {
        var child = new Border();
        Canvas.SetLeft(child, 12);
        Assert.Equal(12.0, Canvas.GetLeft(child));
        Assert.Equal(12.0, child.GetValue(Canvas.LeftProperty));
        child.SetValue(Canvas.TopProperty, 34.0);
        Assert.Equal(34.0, Canvas.GetTop(child));
        Assert.Equal(34.0, child.GetValue(Canvas.TopProperty));
    }

    [Fact]
    public void Canvas_arrange_honors_store_offsets()
    {
        var canvas = new Canvas { Width = 400, Height = 300 };
        var child = new Border { Width = 50, Height = 20 };
        child.SetValue(Canvas.LeftProperty, 30.0);
        child.SetValue(Canvas.TopProperty, 40.0);
        canvas.AddChild(child);
        canvas.Measure(new WpfSize(400, 300));
        canvas.Arrange(new WpfRect(0, 0, 400, 300));
        Assert.Equal(30, child.RenderRect.X, 3);
        Assert.Equal(40, child.RenderRect.Y, 3);
    }

    [Fact]
    public void Window_edges_share_the_canvas_instances()
    {
        Assert.Same(Canvas.LeftProperty, Window.LeftProperty);
        Assert.Same(Canvas.TopProperty, Window.TopProperty);
        Assert.Same(Window.LeftProperty, DependencyProperty.FromName(typeof(Window), "Left"));
        Assert.Same(Window.TopProperty, DependencyProperty.FromName(typeof(Window), "Top"));
    }

    [Fact]
    public void Window_edges_default_nan_and_round_trip()
    {
        var window = new Window();
        Assert.True(double.IsNaN(window.Left));
        Assert.True(double.IsNaN(window.Top));
        window.Left = 120;
        window.Top = 80;
        Assert.Equal(120.0, window.Left);
        Assert.Equal(80.0, window.Top);
        Assert.Equal(120.0, window.GetValue(Window.LeftProperty));
        window.ClearValue(Window.LeftProperty);
        Assert.True(double.IsNaN(window.Left));
    }

    [Fact]
    public void Window_allows_transparency_defaults_false()
    {
        Assert.Equal(typeof(bool), Window.AllowsTransparencyProperty.PropertyType);
        Assert.Equal(false, Window.AllowsTransparencyProperty.DefaultValue);
        Assert.Same(Window.AllowsTransparencyProperty, DependencyProperty.FromName(typeof(Window), "AllowsTransparency"));
        var window = new Window();
        Assert.False(window.AllowsTransparency);
        window.AllowsTransparency = true;
        Assert.True(window.AllowsTransparency);
        Assert.Equal(true, window.GetValue(Window.AllowsTransparencyProperty));
    }

    [Fact]
    public void Window_show_toggles_keep_wpf_defaults()
    {
        Assert.Equal(true, Window.ShowActivatedProperty.DefaultValue);
        Assert.Equal(true, Window.ShowInTaskbarProperty.DefaultValue);
        Assert.Equal(WindowStyle.SingleBorderWindow, Window.WindowStyleProperty.DefaultValue);
        Assert.Same(Window.ShowActivatedProperty, DependencyProperty.FromName(typeof(Window), "ShowActivated"));
        Assert.Same(Window.ShowInTaskbarProperty, DependencyProperty.FromName(typeof(Window), "ShowInTaskbar"));
        Assert.Same(Window.WindowStyleProperty, DependencyProperty.FromName(typeof(Window), "WindowStyle"));
        var window = new Window();
        Assert.True(window.ShowActivated);
        Assert.True(window.ShowInTaskbar);
        Assert.Equal(WindowStyle.SingleBorderWindow, window.WindowStyle);
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.WindowStyle = WindowStyle.ToolWindow;
        Assert.Equal(false, window.GetValue(Window.ShowActivatedProperty));
        Assert.Equal(false, window.GetValue(Window.ShowInTaskbarProperty));
        Assert.Equal(WindowStyle.ToolWindow, window.GetValue(Window.WindowStyleProperty));
        window.ClearValue(Window.WindowStyleProperty);
        Assert.Equal(WindowStyle.SingleBorderWindow, window.WindowStyle);
    }

    [Fact]
    public void Window_icon_is_an_imagesource_dp_leaving_source_seam_alone()
    {
        Assert.Null(typeof(Window).GetField("IconSourceProperty",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy));
        Assert.Equal(typeof(ImageSource), Window.IconProperty.PropertyType);
        Assert.Null(Window.IconProperty.DefaultValue);
        var window = new Window { IconSource = "app.ico" };
        Assert.Null(window.Icon);
        var icon = new BitmapImage(new Uri("app.ico", UriKind.RelativeOrAbsolute));
        window.Icon = icon;
        Assert.Same(icon, window.Icon);
        Assert.Equal("app.ico", window.IconSource);
    }

    [Fact]
    public void Window_owner_is_plain_clr_and_restorebounds_is_empty()
    {
        Assert.Null(typeof(Window).GetField("OwnerProperty",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy));
        Assert.Null(typeof(Window).GetField("RestoreBoundsProperty",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy));
        var owner = new Window();
        var window = new Window { Owner = owner };
        Assert.Same(owner, window.Owner);
        Assert.Equal(Rect.Empty, window.RestoreBounds);
        Assert.Equal(new Window().RestoreBounds, window.RestoreBounds);
    }

    [Fact]
    public void Window_activate_reports_hidden_false_and_marks_visible_active()
    {
        var window = new Window { Width = 100, Height = 100 };
        var activations = 0;
        window.Activated += (_, _) => activations++;
        Assert.True(window.IsVisible);
        Assert.True(window.Activate());
        Assert.True(window.IsActive);
        Assert.Equal(0, activations);

        window.Hide();
        Assert.False(window.IsVisible);
        Assert.False(window.Activate());
        Assert.False(window.IsVisible);
    }

    [Fact]
    public void Window_showdialog_shows_and_reports_the_dialog_result()
    {
        var window = new Window { Width = 100, Height = 100 };
        window.Hide();
        Assert.Null(window.ShowDialog());
        Assert.True(window.IsVisible);

        window.DialogResult = true;
        Assert.True(window.ShowDialog());
    }

    [Fact]
    public void Window_closing_cancel_blocks_close_and_routes_through_onclosing()
    {
        var window = new ClosingProbe { Width = 100, Height = 100 };
        window.Closing += (_, e) => e.Cancel = true;
        window.Close();
        Assert.Equal(1, window.ClosingCalls);
        Assert.Equal(0, window.ClosedCalls);
        Assert.True(window.IsVisible);
    }

    [Fact]
    public void Window_close_routes_through_onclosed_and_hides()
    {
        var window = new ClosingProbe { Width = 100, Height = 100 };
        var closed = 0;
        window.Closed += (_, _) => closed++;
        window.Close();
        Assert.Equal(1, window.ClosingCalls);
        Assert.Equal(1, window.ClosedCalls);
        Assert.Equal(1, closed);
        Assert.False(window.IsVisible);
    }

    [Fact]
    public void Window_state_route_goes_through_onstatechanged()
    {
        var window = new ClosingProbe { Width = 100, Height = 100 };
        var state = 0;
        window.StateChanged += (_, _) => state++;
        window.WindowState = WindowState.Maximized;
        Assert.Equal(1, window.StateCalls);
        Assert.Equal(1, state);
        window.WindowState = WindowState.Maximized;
        Assert.Equal(1, window.StateCalls);
    }

    [Fact]
    public void Window_sourceinitialized_is_retained_but_never_auto_raised()
    {
        var window = new ClosingProbe { Width = 100, Height = 100 };
        var calls = 0;
        window.SourceInitialized += (_, _) => calls++;
        window.Show();
        window.Close();
        Assert.Equal(0, calls);
        window.RaiseSource();
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Xaml_window_chrome_knobs_land_in_clr_and_dp()
    {
        var window = Assert.IsType<Window>(XamlLoader.Load(
            "<Window xmlns=\"" + PresentationNs + "\" Title=\"T\" Left=\"120\" Top=\"80\" " +
            "AllowsTransparency=\"True\" ShowActivated=\"False\" ShowInTaskbar=\"False\" " +
            "WindowStyle=\"ToolWindow\" Icon=\"app.ico\">" +
            "<Grid />" +
            "</Window>").Root);
        Assert.Equal(120.0, window.Left);
        Assert.Equal(80.0, window.Top);
        Assert.Equal(120.0, window.GetValue(Window.LeftProperty));
        Assert.True(window.AllowsTransparency);
        Assert.False(window.ShowActivated);
        Assert.False(window.ShowInTaskbar);
        Assert.Equal(WindowStyle.ToolWindow, window.WindowStyle);
        Assert.Equal("app.ico", window.IconSource);
        Assert.IsType<BitmapImage>(window.Icon);
    }

    [Fact]
    public void Xaml_canvas_offsets_land_in_store_and_bag()
    {
        var window = Assert.IsType<Window>(XamlLoader.Load(
            "<Window xmlns=\"" + PresentationNs + "\">" +
            "<Canvas><Border Canvas.Left=\"12\" Canvas.Top=\"34\" /></Canvas>" +
            "</Window>").Root);
        var canvas = Assert.IsType<Canvas>(window.Content);
        var child = Assert.IsType<Border>(canvas.Children[0]);
        Assert.Equal(12.0, Canvas.GetLeft(child));
        Assert.Equal(34.0, Canvas.GetTop(child));
        Assert.Equal(12.0, child.GetValue(Canvas.LeftProperty));
        Assert.Equal(34.0, child.GetValue(Canvas.TopProperty));
    }

    [Fact]
    public void Style_setter_applies_window_chrome_through_the_dp()
    {
        var window = new Window();
        var style = new Style(typeof(Window));
        style.Setters.Add(new Setter(Window.LeftProperty, 12.0));
        style.Setters.Add(new Setter(Window.AllowsTransparencyProperty, true));
        style.ApplyTo(window);
        Assert.Equal(12, window.Left);
        Assert.True(window.AllowsTransparency);
    }

    [Fact]
    public void Trigger_dp_setter_records_window_edges()
    {
        // Attached DPs record qualified (slice-62 convention); the shared
        // Canvas instance keeps the qualification.
        var trigger = new Trigger();
        trigger.set_Property(Window.LeftProperty);
        Assert.Equal("Canvas.Left", trigger.Property);
        trigger.set_Property(Window.IconProperty);
        Assert.Equal("Icon", trigger.Property);
    }

    [Fact]
    public void Converted_dockingmanager_static_ctor_runs_and_flips_the_flag()
    {
        Application.Current = null;
        var assembly = StagedAvalonDockHwndLoader.LoadAssembly("AvalonDock");
        var manager = assembly.GetType("AvalonDock.DockingManager", throwOnError: true)!;
        var previous = HwndSource.DefaultAcquireHwndFocusInMenuMode;
        try
        {
            RuntimeHelpers.RunClassConstructor(manager.TypeHandle);
            Assert.False(HwndSource.DefaultAcquireHwndFocusInMenuMode);
        }
        finally
        {
            HwndSource.DefaultAcquireHwndFocusInMenuMode = previous;
            Application.Current = null;
        }
    }

    [Fact]
    public void Converted_dockingmanager_instance_constructs_past_the_trio()
    {
        // Slice-64 pinned MissingMethodException for
        // set_DefaultAcquireHwndFocusInMenuMode from this exact call; with
        // the trio plus the focus cluster declared, the converted manager
        // constructs (instance activation is the advance, not a boundary).
        Application.Current = null;
        try
        {
            var assembly = StagedAvalonDockHwndLoader.LoadAssembly("AvalonDock");
            var manager = assembly.GetType("AvalonDock.DockingManager", throwOnError: true)!;
            var instance = Activator.CreateInstance(manager)!;
            Assert.IsAssignableFrom<FrameworkElement>(instance);
        }
        finally
        {
            Application.Current = null;
        }
    }

    [Fact]
    public void Converted_floating_content_hosts_bind_the_hwndsource_trio()
    {
        Application.Current = null;
        try
        {
            var assembly = StagedAvalonDockHwndLoader.LoadAssembly("AvalonDock");
            var host = assembly.GetType(
                "AvalonDock.Controls.LayoutFloatingWindowControl+FloatingWindowContentHost",
                throwOnError: true)!;
            Assert.NotNull(host.GetMethod("OnSizeToContentChanged",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
            var setter = typeof(HwndSource).GetMethod("set_SizeToContent",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(setter);
            Assert.Equal(typeof(SizeToContent), setter.GetParameters()[0].ParameterType);

            var chrome = assembly.GetType("Microsoft.Windows.Shell.WindowChromeWorker", throwOnError: true)!;
            Assert.NotNull(chrome.GetMethod("_SetWindow",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static));
            Assert.NotNull(typeof(HwndSource).GetMethod("FromHwnd",
                BindingFlags.Public | BindingFlags.Static));
        }
        finally
        {
            Application.Current = null;
        }
    }

    private static class StagedAvalonDockHwndLoader
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
