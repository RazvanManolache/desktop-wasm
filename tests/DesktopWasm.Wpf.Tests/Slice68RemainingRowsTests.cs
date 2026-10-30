using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-68 remaining-rows cluster: the 32 distinct-unresolved engine-scope
/// member rows the live staged-bin audit still reports after slice-67
/// (4889 engine-scope refs, 674 distinct over the 10 staged AvalonDock bins,
/// sha256-verified, status gate bypassed in-probe only, manifests untouched).
/// Every shape below is live-verified against the 9.0.21
/// PresentationFramework/PresentationCore/WindowsBase contracts (plus the
/// .NET Framework 4.8 reference for the PropertyPath overloads the net48
/// upstream binds): ItemsControl.ItemsSourceProperty (ContextMenuEx.OnOpened),
/// MenuItem.IconProperty/get_Icon/set_Icon (MenuItemEx.cctor/UpdateIcon,
/// ToggleDockingManager Melbourne menu), GridLength..ctor(double)/IsAbsolute/
/// IsAuto/IsStar (DefaultLayoutEngine, LayoutGridControl, LayoutDtoMapper),
/// Panel.BackgroundProperty/get_InternalChildren (LayoutAutoHideWindowControl,
/// LayoutGridControl), ListBoxItem.set_IsSelected (NavigatorWindow),
/// TextOptions.TextHintingModeProperty/TextRenderingModeProperty
/// (LayoutFloatingWindowControl.GetInheritableProperties),
/// ToggleButton.OnClick (DropDownButton.OnClick base call),
/// Visual.PointToScreen (TransformExtensions.PointToScreenDPI),
/// PathGeometry..ctor(IEnumerable) (AnchorablePaneDropTarget),
/// VisualBrush..ctor(Visual) (ToggleDockingManager),
/// MatrixTransform..ctor(6 doubles) (WindowChromeWorker),
/// PropertyPath..ctor(object)/..ctor(string,object[]) (ToggleDockingManager,
/// WindowChrome), Dispatcher.Invoke(Func,Priority)/add_ShutdownStarted
/// (LayoutFloatingWindowControl, SystemParameters2),
/// EventManager.RegisterClassHandler (DropDownControlArea..cctor, targeting
/// PreviewMouseRightButtonUp), PresentationSource.FromDependencyObject
/// (LayoutFloatingWindowControl.OnLoaded), RelativeSource.Self
/// (ToggleDockingManager), AutomationProperties.SetAutomationId
/// (ToggleDockingManager), plus the strict-signature siblings the same scan
/// pins: Keyboard.Focus(IInputElement), Matrix.Scale/Transform(Point),
/// Point.op_Explicit (Size/Vector), ResourceDictionary.Source(Uri),
/// UIElement.Arrange(Rect). Real-WPF parity throughout, never fabricated.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice68RemainingRowsTests : IDisposable
{
    public Slice68RemainingRowsTests()
    {
        WpfAnimationClock.ResetForTests();
        WasmDispatcher.Current = new WasmDispatcher(new WasmDispatcherPump());
    }

    public void Dispose() => WpfAnimationClock.ResetForTests();

    [Fact]
    public void Staged_avalondock_hashes_verify()
    {
        Assert.Empty(StagedAvalonDockRemainderLoader.VerifyHashes());
    }

    [Fact]
    public void Cluster_shapes_resolve_by_signature()
    {
        const BindingFlags instance = BindingFlags.Public | BindingFlags.Instance;
        const BindingFlags nonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        const BindingFlags statics = BindingFlags.Public | BindingFlags.Static;

        Assert.NotNull(typeof(ItemsControl).GetField("ItemsSourceProperty", statics | BindingFlags.FlattenHierarchy));
        Assert.NotNull(typeof(ItemsControl).GetMethod("get_ItemsSource", instance));
        Assert.NotNull(typeof(ItemsControl).GetMethod("set_ItemsSource", instance));

        Assert.NotNull(typeof(MenuItem).GetField("IconProperty", statics | BindingFlags.FlattenHierarchy));
        Assert.NotNull(typeof(MenuItem).GetMethod("get_Icon", instance));
        Assert.NotNull(typeof(MenuItem).GetMethod("set_Icon", instance));

        var gridLength = typeof(GridLength);
        Assert.NotNull(gridLength.GetConstructor([typeof(double)]));
        Assert.NotNull(gridLength.GetMethod("get_Value", instance));
        Assert.NotNull(gridLength.GetMethod("get_GridUnitType", instance));
        Assert.NotNull(gridLength.GetMethod("get_IsAbsolute", instance));
        Assert.NotNull(gridLength.GetMethod("get_IsAuto", instance));
        Assert.NotNull(gridLength.GetMethod("get_IsStar", instance));

        Assert.NotNull(typeof(Panel).GetField("BackgroundProperty", statics | BindingFlags.FlattenHierarchy));
        Assert.NotNull(typeof(Panel).GetMethod("get_InternalChildren", nonPublicInstance));

        Assert.NotNull(typeof(ListBoxItem).GetMethod("get_IsSelected", instance));
        Assert.NotNull(typeof(ListBoxItem).GetMethod("set_IsSelected", instance));

        Assert.NotNull(typeof(TextOptions).GetField("TextHintingModeProperty", statics));
        Assert.NotNull(typeof(TextOptions).GetField("TextRenderingModeProperty", statics));
        Assert.NotNull(typeof(TextOptions).GetMethod("GetTextHintingMode", statics));
        Assert.NotNull(typeof(TextOptions).GetMethod("SetTextHintingMode", statics));
        Assert.NotNull(typeof(TextOptions).GetMethod("GetTextRenderingMode", statics));
        Assert.NotNull(typeof(TextOptions).GetMethod("SetTextRenderingMode", statics));

        Assert.NotNull(typeof(ToggleButton).GetMethod("OnClick", nonPublicInstance, Type.EmptyTypes));
        Assert.NotNull(typeof(ButtonBase).GetMethod("OnClick", nonPublicInstance, Type.EmptyTypes));

        var pointToScreen = typeof(Visual).GetMethod("PointToScreen", instance);
        Assert.NotNull(pointToScreen);
        Assert.Equal(typeof(Point), pointToScreen.ReturnType);
        Assert.Equal(typeof(Point), pointToScreen.GetParameters()[0].ParameterType);

        Assert.NotNull(typeof(PathGeometry).GetConstructor([typeof(IEnumerable<PathFigure>)]));

        var brushCtor = typeof(VisualBrush).GetConstructor([typeof(Visual)]);
        Assert.NotNull(brushCtor);

        Assert.NotNull(typeof(MatrixTransform).GetConstructor(
            [typeof(double), typeof(double), typeof(double), typeof(double), typeof(double), typeof(double)]));
        Assert.NotNull(typeof(Matrix).GetMethod("Scale", instance));
        var transformPoint = typeof(Matrix).GetMethod("Transform", instance, [typeof(Point)]);
        Assert.NotNull(transformPoint);
        Assert.Equal(typeof(Point), transformPoint.ReturnType);

        Assert.NotNull(typeof(PropertyPath).GetConstructor([typeof(object)]));
        Assert.NotNull(typeof(PropertyPath).GetConstructor([typeof(string), typeof(object[])]));
        Assert.NotNull(typeof(RelativeSource).GetProperty("Self", statics));

        var dispatcher = typeof(Dispatcher);
        Assert.NotNull(dispatcher.GetMethod("Invoke", instance, [typeof(Action), typeof(DispatcherPriority)]));
        var invokeGeneric = dispatcher.GetMethods(instance).FirstOrDefault(m =>
            m.Name == "Invoke" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2 &&
            m.GetParameters()[0].ParameterType.IsGenericType &&
            m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(Func<>) &&
            m.GetParameters()[1].ParameterType == typeof(DispatcherPriority) &&
            m.GetGenericArguments().Length == 1);
        Assert.NotNull(invokeGeneric);
        Assert.NotNull(dispatcher.GetMethod("add_ShutdownStarted", instance));
        Assert.NotNull(dispatcher.GetMethod("remove_ShutdownStarted", instance));

        var registerClass = typeof(EventManager).GetMethods(statics)
            .Where(m => m.Name == "RegisterClassHandler").ToList();
        Assert.Contains(registerClass, m => m.GetParameters().Length == 3);
        Assert.Contains(registerClass, m => m.GetParameters().Length == 4);

        Assert.NotNull(typeof(PresentationSource).GetMethod("FromDependencyObject", statics));

        Assert.NotNull(typeof(AutomationProperties).GetMethod("GetAutomationId", statics));
        Assert.NotNull(typeof(AutomationProperties).GetMethod("SetAutomationId", statics));

        var focusByElement = typeof(Keyboard).GetMethod("Focus", statics, [typeof(IInputElement)]);
        Assert.NotNull(focusByElement);
        Assert.Equal(typeof(IInputElement), focusByElement.ReturnType);

        var sourceProp = typeof(ResourceDictionary).GetProperty("Source", instance);
        Assert.NotNull(sourceProp);
        Assert.Equal(typeof(Uri), sourceProp.PropertyType);

        Assert.NotNull(typeof(UIElement).GetMethod("Arrange", instance, [typeof(Rect)]));

        var explicits = typeof(Point).GetMethods(statics)
            .Where(m => m.Name == "op_Explicit" && m.GetParameters().Length == 1).ToList();
        Assert.Contains(explicits, m => m.ReturnType == typeof(Size));
        Assert.Contains(explicits, m => m.ReturnType == typeof(Vector));
    }

    [Fact]
    public void ItemsSource_dp_token_and_store_roundtrip()
    {
        Assert.Same(typeof(ItemsControl), ItemsControl.ItemsSourceProperty.OwnerType);
        Assert.Equal("ItemsSource", ItemsControl.ItemsSourceProperty.Name);
        var control = new ItemsControl();
        var source = new List<string> { "a", "b" };
        control.ItemsSource = source;
        Assert.Equal(2, control.Items.Count);
        Assert.Same(source, control.GetValue(ItemsControl.ItemsSourceProperty));
        var empty = new ItemsControl();
        Assert.Null(empty.GetValue(ItemsControl.ItemsSourceProperty));
    }

    [Fact]
    public void MenuItem_icon_roundtrips_through_clr_and_store()
    {
        Assert.Same(typeof(MenuItem), MenuItem.IconProperty.OwnerType);
        Assert.Equal(typeof(object), MenuItem.IconProperty.PropertyType);
        var item = new MenuItem { Header = "View" };
        Assert.Null(item.Icon);
        var icon = new object();
        item.Icon = icon;
        Assert.Same(icon, item.Icon);
        Assert.Same(icon, item.GetValue(MenuItem.IconProperty));
        item.SetValue(MenuItem.IconProperty, "glyph");
        Assert.Equal("glyph", item.Icon);
    }

    [Fact]
    public void GridLength_parity_members()
    {
        var pixels = new GridLength(120);
        Assert.Equal(120, pixels.Value);
        Assert.Equal(GridUnitType.Pixel, pixels.Unit);
        Assert.Equal(GridUnitType.Pixel, pixels.GridUnitType);
        Assert.True(pixels.IsAbsolute);
        Assert.False(pixels.IsAuto);
        Assert.False(pixels.IsStar);

        Assert.True(GridLength.Auto.IsAuto);
        var star = GridLength.Star(2);
        Assert.True(star.IsStar);
        Assert.Equal(2, star.Value);
        Assert.False(GridLength.Pixel(3).IsStar);
    }

    [Fact]
    public void Panel_background_shares_control_token_and_paints_from_store()
    {
        Assert.Same(Control.BackgroundProperty, Panel.BackgroundProperty);
        // Canvas inherits the Panel CLR (Grid/StackPanel/DockPanel keep
        // their own retained brushes, as before).
        var canvas = new Canvas();
        Assert.Null(canvas.Background);
        var brush = new SolidColorBrush(Color.FromRgb(1, 2, 3));
        canvas.Background = brush;
        Assert.Same(brush, canvas.GetValue(Panel.BackgroundProperty));
        var other = new SolidColorBrush(Color.FromRgb(4, 5, 6));
        canvas.SetValue(Panel.BackgroundProperty, other);
        Assert.Same(other, canvas.Background);
    }

    private sealed class InternalChildrenProbe : StackPanel
    {
        public UIElementCollection Exposed => InternalChildren;
    }

    [Fact]
    public void Panel_internal_children_is_the_live_collection()
    {
        var panel = new InternalChildrenProbe();
        Assert.Same(panel.Children, panel.Exposed);
        var child = new Button();
        panel.AddChild(child);
        Assert.Contains(child, panel.Exposed);
    }

    [Fact]
    public void ListBoxItem_selection_flows_through_the_parent()
    {
        var list = new ListBox();
        var first = new ListBoxItem();
        var second = new ListBoxItem();
        list.AddChild(first);
        list.AddChild(second);
        Assert.False(first.IsSelected);

        first.IsSelected = true;
        Assert.True(first.IsSelected);
        Assert.Same(first, list.SelectedItem);

        second.IsSelected = true;
        Assert.True(second.IsSelected);
        Assert.False(first.IsSelected);

        first.IsSelected = false;
        Assert.Same(second, list.SelectedItem);
        second.IsSelected = false;
        Assert.Null(list.SelectedItem);

        var orphan = new ListBoxItem();
        orphan.IsSelected = true;
        Assert.False(orphan.IsSelected);
    }

    [Fact]
    public void TextOptions_hinting_and_rendering_roundtrip()
    {
        Assert.Same(typeof(TextOptions), TextOptions.TextHintingModeProperty.OwnerType);
        Assert.Equal(typeof(TextHintingMode), TextOptions.TextHintingModeProperty.PropertyType);
        Assert.Same(typeof(TextOptions), TextOptions.TextRenderingModeProperty.OwnerType);
        Assert.Equal(typeof(TextRenderingMode), TextOptions.TextRenderingModeProperty.PropertyType);
        var target = new Button();
        Assert.Equal(TextHintingMode.Auto, TextOptions.GetTextHintingMode(target));
        Assert.Equal(TextRenderingMode.Auto, TextOptions.GetTextRenderingMode(target));
        TextOptions.SetTextHintingMode(target, TextHintingMode.Fixed);
        TextOptions.SetTextRenderingMode(target, TextRenderingMode.ClearType);
        Assert.Equal(TextHintingMode.Fixed, TextOptions.GetTextHintingMode(target));
        Assert.Equal(TextRenderingMode.ClearType, TextOptions.GetTextRenderingMode(target));
        Assert.Equal(TextHintingMode.Fixed, target.GetValue(TextOptions.TextHintingModeProperty));
    }

    private sealed class OnClickProbe : ToggleButton
    {
        public int Calls;
        public void FireClick() => OnClick();
        protected override void OnClick()
        {
            Calls++;
            base.OnClick();
        }
    }

    [Fact]
    public void ToggleButton_press_routes_through_virtual_onclick()
    {
        var toggle = new OnClickProbe();
        var clicks = 0;
        toggle.Click += (_, _) => clicks++;
        Assert.False(toggle.IsChecked ?? true);

        toggle.FireClick();
        Assert.Equal(1, toggle.Calls);
        Assert.True(toggle.IsChecked == true);
        Assert.Equal(1, clicks);

        toggle.FireClick();
        Assert.Equal(2, toggle.Calls);
        Assert.True(toggle.IsChecked == false);
        Assert.Equal(2, clicks);
    }

    private sealed class ClassHandlerProbe : Button
    {
    }

    [Fact]
    public void Class_handlers_run_ahead_of_instance_handlers()
    {
        var button = new ClassHandlerProbe();
        var order = new List<string>();
        EventManager.RegisterClassHandler(typeof(ClassHandlerProbe), ButtonBase.ClickEvent,
            new RoutedEventHandler((_, _) => order.Add("class")));
        EventManager.RegisterClassHandler(typeof(ClassHandlerProbe), ButtonBase.ClickEvent,
            new RoutedEventHandler((_, _) => order.Add("class-too")), handledEventsToo: true);
        button.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => order.Add("instance")));

        button.RaiseEvent(new RoutedEventArgs { RoutedEvent = ButtonBase.ClickEvent, Source = button });
        Assert.Equal(["class", "class-too", "instance"], order);

        var blocked = new List<string>();
        EventManager.RegisterClassHandler(typeof(ClassHandlerProbe), MenuItem.ClickEvent,
            new RoutedEventHandler((_, _) => blocked.Add("class")));
        var menu = new ClassHandlerProbe();
        menu.AddHandler(MenuItem.ClickEvent, new RoutedEventHandler((_, e) =>
        {
            blocked.Add("instance");
            e.Handled = true;
        }));
        menu.RaiseEvent(new RoutedEventArgs { RoutedEvent = MenuItem.ClickEvent, Source = menu });
        Assert.Equal(["class", "instance"], blocked);
    }

    [Fact]
    public void PointToScreen_applies_offsets_and_device_scale()
    {
        var previous = HwndTarget.DeviceScale;
        try
        {
            var canvas = new Canvas();
            var child = new Border { Width = 20, Height = 10 };
            Canvas.SetLeft(child, 10);
            Canvas.SetTop(child, 15);
            canvas.AddChild(child);
            var window = new Window { Content = canvas };
            WasmMilCore.RenderHeadless(window, new WpfSize(300, 100));
            Assert.Equal(10, child.RenderRect.X, 3);
            Assert.Equal(15, child.RenderRect.Y, 3);

            var logical = child.PointToScreen(new Point(0, 0));
            Assert.Equal(child.RenderRect.X, logical.X, 3);
            Assert.Equal(child.RenderRect.Y, logical.Y, 3);

            HwndTarget.DeviceScale = 2.0;
            var device = child.PointToScreen(new Point(1, 1));
            Assert.Equal((child.RenderRect.X + 1) * 2, device.X, 3);
            Assert.Equal((child.RenderRect.Y + 1) * 2, device.Y, 3);
        }
        finally
        {
            HwndTarget.DeviceScale = previous;
        }
    }

    [Fact]
    public void Geometry_and_brush_ctors_retain_values()
    {
        var start = new Point(1, 2);
        var geometry = new PathGeometry(new[] { new PathFigure { StartPoint = start } });
        var figure = Assert.Single(geometry.Figures);
        Assert.Equal(start, figure.StartPoint);

        var visual = new Button();
        var brush = new VisualBrush(visual);
        Assert.Same(visual, brush.Visual);

        var matrixTransform = new MatrixTransform(1, 2, 3, 4, 5, 6);
        Assert.Equal(new Matrix(1, 2, 3, 4, 5, 6), matrixTransform.Matrix);
        Assert.Equal(new Matrix(1, 2, 3, 4, 5, 6), matrixTransform.Value);
    }

    [Fact]
    public void Matrix_scale_and_point_transform_match_wpf_math()
    {
        var matrix = Matrix.Identity;
        matrix.Scale(2, 3);
        Assert.Equal(2, matrix.M11);
        Assert.Equal(3, matrix.M22);
        Assert.Equal(0, matrix.OffsetX);
        var scaled = new Matrix(1, 0, 0, 1, 5, 7);
        scaled.Scale(2, 3);
        Assert.Equal(10, scaled.OffsetX);
        Assert.Equal(21, scaled.OffsetY);

        var transform = new Matrix(2, 0, 0, 2, 10, 20).Transform(new Point(1, 1));
        Assert.Equal(new Point(12, 22), transform);
    }

    [Fact]
    public void PropertyPath_object_and_indexed_ctors()
    {
        var fromObject = new PropertyPath((object)"Name");
        Assert.Equal("(0)", fromObject.Path);
        Assert.Equal(["Name"], fromObject.PathParameters);

        var fromDp = new PropertyPath(ContentControl.ContentProperty);
        Assert.Equal("Content", fromDp.Path);
        Assert.Equal([ContentControl.ContentProperty], fromDp.PathParameters);

        var indexed = new PropertyPath("Items[0]", "extra");
        Assert.Equal("Items[0]", indexed.Path);
        Assert.Equal(["extra"], indexed.PathParameters);

        var plain = new PropertyPath("Name");
        Assert.Equal("Name", plain.Path);
        Assert.Empty(plain.PathParameters);
    }

    [Fact]
    public void RelativeSource_self_is_fresh_and_self_looking()
    {
        Assert.Equal(RelativeSourceMode.Self, RelativeSource.Self.Mode);
        Assert.NotSame(RelativeSource.Self, RelativeSource.Self);
    }

    [Fact]
    public void Dispatcher_priority_invoke_and_shutdown_subscribe()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var ran = 0;
        dispatcher.Invoke(() => ran++, DispatcherPriority.Background);
        Assert.Equal(1, ran);
        var value = dispatcher.Invoke(() => 41 + 1, DispatcherPriority.Normal);
        Assert.Equal(42, value);

        var fired = 0;
        void OnShutdown(object? sender, EventArgs args) => fired++;
        dispatcher.ShutdownStarted += OnShutdown;
        dispatcher.Invoke(() => { });
        Assert.Equal(0, fired);
        dispatcher.ShutdownStarted -= OnShutdown;
    }

    [Fact]
    public void PresentationSource_from_dependency_object_is_null()
    {
        Assert.Null(PresentationSource.FromDependencyObject(new Button()));
        Assert.Null(PresentationSource.FromDependencyObject(null));
    }

    [Fact]
    public void AutomationId_roundtrips_like_name()
    {
        var button = new Button();
        Assert.Equal(string.Empty, AutomationProperties.GetAutomationId(button));
        AutomationProperties.SetAutomationId(button, "toggle-dock");
        Assert.Equal("toggle-dock", AutomationProperties.GetAutomationId(button));
    }

    private sealed class FocusStub : IInputElement
    {
        public bool Focus() => false;
        public bool Focusable => false;
        public bool IsEnabled => true;
        public bool IsKeyboardFocused => false;
        public bool IsKeyboardFocusWithin => false;
        public bool IsMouseOver => false;
        public event KeyboardFocusChangedEventHandler? GotKeyboardFocus;
        public event KeyboardFocusChangedEventHandler? LostKeyboardFocus;
        public event RoutedEventHandler? GotFocus;
        public event RoutedEventHandler? LostFocus;
    }

    [Fact]
    public void Keyboard_focus_input_element_routes_like_element()
    {
        IInputElement stub = new FocusStub();
        Assert.Null(Keyboard.Focus(stub));

        var button = new Button();
        Assert.Equal(Keyboard.Focus(button), Keyboard.Focus((IInputElement)button));
        Assert.Null(Keyboard.Focus((IInputElement?)null));
    }

    [Fact]
    public void ResourceDictionary_source_is_a_uri()
    {
        var dictionary = new ResourceDictionary();
        Assert.Null(dictionary.Source);
        var uri = new Uri("pack://application:,,,/Themes/Dark.xaml", UriKind.Absolute);
        dictionary.Source = uri;
        Assert.Same(uri, dictionary.Source);
    }

    [Fact]
    public void Arrange_rect_matches_wpfrect_path()
    {
        var viaRect = new Border { Width = 50, Height = 40 };
        viaRect.Measure(new WpfSize(200, 200));
        viaRect.Arrange(new Rect(5, 6, 50, 40));
        var viaWpf = new Border { Width = 50, Height = 40 };
        viaWpf.Measure(new WpfSize(200, 200));
        viaWpf.Arrange(new WpfRect(5, 6, 50, 40));
        Assert.Equal(viaWpf.RenderRect.X, viaRect.RenderRect.X, 6);
        Assert.Equal(viaWpf.RenderRect.Y, viaRect.RenderRect.Y, 6);
        Assert.Equal(5, viaRect.RenderRect.X, 6);
        Assert.Equal(6, viaRect.RenderRect.Y, 6);
    }

    [Fact]
    public void Point_explicit_narrowing_matches_wpf()
    {
        Assert.Equal(new Size(3, 4), (Size)new Point(3, 4));
        Assert.Equal(new Vector(3, 4), (Vector)new Point(3, 4));
    }

    [Fact]
    public void Converted_drop_down_override_binds_the_new_virtual()
    {
        var assembly = StagedAvalonDockRemainderLoader.LoadAssembly("AvalonDock");
        var dropDown = assembly.GetType("AvalonDock.Controls.DropDownButton", throwOnError: true)!;
        Assert.NotNull(dropDown.GetMethod("OnClick",
            BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes));
        var floating = assembly.GetType("AvalonDock.Controls.LayoutFloatingWindowControl", throwOnError: true)!;
        RuntimeHelpers.RunClassConstructor(floating.TypeHandle);
        var menuItemEx = assembly.GetType("AvalonDock.Controls.MenuItemEx", throwOnError: true)!;
        RuntimeHelpers.RunClassConstructor(menuItemEx.TypeHandle);
        var area = assembly.GetType("AvalonDock.Controls.DropDownControlArea", throwOnError: true)!;
        RuntimeHelpers.RunClassConstructor(area.TypeHandle);
    }

    [Fact]
    public void Converted_cluster_users_load_without_cluster_members()
    {
        Application.Current = null;
        var previous = HwndSource.DefaultAcquireHwndFocusInMenuMode;
        try
        {
            var assembly = StagedAvalonDockRemainderLoader.LoadAssembly("AvalonDock");
            var users = new[]
            {
                "AvalonDock.DockingManager",
                "AvalonDock.ToggleDockingManager",
                "AvalonDock.DefaultLayoutEngine",
                "AvalonDock.Serialization.LayoutDtoMapper",
                "AvalonDock.Controls.ContextMenuEx",
                "AvalonDock.Controls.MenuItemEx",
                "AvalonDock.Controls.LayoutAutoHideWindowControl",
                "AvalonDock.Controls.LayoutFloatingWindowControl",
                "AvalonDock.Controls.LayoutGridControl`1",
                "AvalonDock.Controls.LayoutDocumentFloatingWindowControl",
                "AvalonDock.Controls.NavigatorWindow",
                "AvalonDock.Controls.DropDownButton",
                "AvalonDock.Controls.DropDownControlArea",
                "AvalonDock.Controls.AnchorablePaneDropTarget",
                "AvalonDock.Controls.AnchorablePaneTabPanel",
                "AvalonDock.Controls.FocusElementManager",
                "AvalonDock.Controls.TransformExtensions",
                "Microsoft.Windows.Shell.WindowChrome",
                "Microsoft.Windows.Shell.WindowChromeWorker",
                "Microsoft.Windows.Shell.SystemParameters2",
                "Standard.DpiHelper",
                "Standard.Utility",
            };
            var cluster = new[]
            {
                "ItemsSourceProperty", "IconProperty", "get_Icon", "set_Icon",
                "BackgroundProperty", "get_InternalChildren", "set_IsSelected",
                "TextHintingModeProperty", "TextRenderingModeProperty",
                "ToggleButton.OnClick", "PointToScreen", "PathGeometry",
                "VisualBrush", "MatrixTransform", "FromDependencyObject",
                "PropertyPath", "get_Self", "set_Source", "add_ShutdownStarted",
                "remove_ShutdownStarted", "RegisterClassHandler", "GridLength",
                "get_IsAbsolute", "get_IsAuto", "get_IsStar", "SetAutomationId",
                "Keyboard.Focus", "Matrix.Scale", "Matrix.Transform",
                "op_Explicit", "UIElement.Arrange", "Dispatcher.Invoke",
            };
            foreach (var name in users)
            {
                var type = assembly.GetType(name, throwOnError: true)!;
                try
                {
                    RuntimeHelpers.RunClassConstructor(type.TypeHandle);
                }
                catch (Exception ex)
                {
                    var chain = new List<string>();
                    for (var e = ex; e is not null; e = e.InnerException)
                        chain.Add(e.GetType().Name + ": " + e.Message.Split('\n')[0]);
                    var joined = string.Join(" ||| ", chain);
                    foreach (var member in cluster)
                        Assert.DoesNotContain(member, joined, StringComparison.Ordinal);
                }
            }
        }
        finally
        {
            HwndSource.DefaultAcquireHwndFocusInMenuMode = previous;
            Application.Current = null;
        }
    }

    private static class StagedAvalonDockRemainderLoader
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
