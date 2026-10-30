using System.Reflection;
using System.Xml;
using DesktopWasm.Wpf;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-73 material-design remainder: the date-picker pair, context-menu
/// pair, adorner placeholder, cache hint, color converter, focus manager,
/// size-change carrier, static-resource extension, generic weak-event
/// manager, annotation store, navigation service and the two collection
/// enumerators. Every member is pinned against the upstream
/// net8.0-windows MaterialDesignThemes.Wpf.dll, MaterialDesignColors.dll
/// and Microsoft.Xaml.Behaviors.dll member users plus the 9.0.21 runtime
/// (see Slice73Remainder.cs). Ambient-state tests run serialized: the
/// class joins the ApplicationState collection like
/// DynamicResourceExtensionTests.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice73RemainderTests
{
    [Fact]
    public void DatePickerFormat_values_match_wpf()
    {
        Assert.Equal(0, (int)DatePickerFormat.Long);
        Assert.Equal(1, (int)DatePickerFormat.Short);
    }

    [Fact]
    public void DatePicker_hosts_a_date_picker_text_box()
    {
        var picker = new DatePicker();
        Assert.IsType<DatePickerTextBox>(picker.TextField);
        var box = picker.DateTextBox;
        Assert.Null(box.Watermark);
        box.Watermark = "Select a date";
        Assert.Equal("Select a date", box.Watermark);
        Assert.Equal(typeof(object), DatePickerTextBox.WatermarkProperty.PropertyType);
        Assert.Same(typeof(DatePickerTextBox), DatePickerTextBox.WatermarkProperty.OwnerType);
    }

    [Fact]
    public void SelectedDateFormat_switches_short_and_long_render()
    {
        var picker = new DatePicker();
        Assert.Equal(DatePickerFormat.Short, picker.SelectedDateFormat);
        var day = new DateTime(2026, 10, 26);
        picker.SelectedDate = day;
        Assert.Equal(day.ToString("d", System.Globalization.CultureInfo.CurrentCulture), picker.Text);
        picker.SelectedDateFormat = DatePickerFormat.Long;
        Assert.Equal(day.ToString("D", System.Globalization.CultureInfo.CurrentCulture), picker.Text);
        picker.SelectedDateFormat = DatePickerFormat.Short;
        Assert.Equal(day.ToString("d", System.Globalization.CultureInfo.CurrentCulture), picker.Text);
    }

    [Fact]
    public void ContextMenuEventArgs_shape_matches_wpf()
    {
        Assert.True(typeof(ContextMenuEventArgs).IsSealed);
        Assert.Same(typeof(RoutedEventArgs), typeof(ContextMenuEventArgs).BaseType);
        Assert.NotNull(typeof(ContextMenuEventHandler).GetMethod("Invoke", [typeof(object), typeof(ContextMenuEventArgs)]));
        var target = new Button();
        var args = (ContextMenuEventArgs)Activator.CreateInstance(
            typeof(ContextMenuEventArgs),
            BindingFlags.NonPublic | BindingFlags.Instance,
            null, [target, true, 12.0, 34.0], null)!;
        Assert.Equal(12, args.CursorLeft);
        Assert.Equal(34, args.CursorTop);
        Assert.True(args.Opening);
        Assert.Same(target, args.Source);
        Assert.Null(args.TargetElement);
        args.TargetElement = target;
        Assert.Same(target, args.TargetElement);
    }

    [Fact]
    public void ContextMenu_opening_closing_order_and_veto()
    {
        var order = new List<string>();
        var target = new Button { Content = "Row" };
        target.ContextMenuOpening += (_, e) =>
        {
            order.Add("opening");
            Assert.Same(target, e.TargetElement);
        };
        target.ContextMenuClosing += (_, _) => order.Add("closing");
        var menu = new ContextMenu { PlacementTarget = target };
        menu.Opened += (_, _) => order.Add("opened");
        menu.Closed += (_, _) => order.Add("closed");

        menu.IsOpen = true;
        Assert.True(menu.IsOpen);
        menu.IsOpen = false;
        Assert.False(menu.IsOpen);
        Assert.Equal(["opening", "opened", "closed", "closing"], order);
    }

    [Fact]
    public void ContextMenu_handled_opening_aborts_the_open()
    {
        var target = new Button();
        var menu = new ContextMenu { PlacementTarget = target };
        var opened = 0;
        menu.Opened += (_, _) => opened++;
        target.ContextMenuOpening += (_, e) => e.Handled = true;
        menu.IsOpen = true;
        Assert.False(menu.IsOpen);
        Assert.Equal(0, opened);
    }

    [Fact]
    public void ContextMenu_without_target_opens_quietly()
    {
        var menu = new ContextMenu();
        var opened = 0;
        menu.Opened += (_, _) => opened++;
        menu.IsOpen = true;
        menu.IsOpen = false;
        Assert.Equal(1, opened);
    }

    [Fact]
    public void AdornedElementPlaceholder_defaults_and_follows()
    {
        var placeholder = new AdornedElementPlaceholder();
        Assert.Null(placeholder.AdornedElement);
        placeholder.Measure(new WpfSize(100, 100));
        Assert.Equal(new Size(0, 0), placeholder.DesiredSize);

        var adorned = new Button { Content = "Field", Width = 120, Height = 30 };
        adorned.Measure(new WpfSize(500, 500));
        placeholder.SetAdornedElement(adorned);
        placeholder.Measure(new WpfSize(500, 500));
        Assert.Equal(adorned.DesiredSize, placeholder.DesiredSize);
        placeholder.Arrange(new WpfRect(0, 0, 200, 40));
    }

    [Fact]
    public void CachingHint_values_match_wpf()
    {
        Assert.Equal(0, (int)CachingHint.Unspecified);
        Assert.Equal(1, (int)CachingHint.Cache);
    }

    [Fact]
    public void ColorConverter_hex_named_and_garbage()
    {
        var converter = new ColorConverter();
        Assert.True(converter.CanConvertFrom(null, typeof(string)));
        Assert.False(converter.CanConvertFrom(null, typeof(int)));
        Assert.True(converter.CanConvertTo(null, typeof(string)));
        Assert.False(converter.CanConvertTo(null, typeof(int)));

        Assert.Equal(new Color(0xFF, 0x15, 0x6E, 0x61), converter.ConvertFrom(null, null, "#156E61"));
        Assert.Equal(new Color(0x80, 0x15, 0x6E, 0x61), converter.ConvertFrom(null, null, "#80156E61"));
        Assert.Equal(Colors.Red, converter.ConvertFrom(null, null, "Red"));
        Assert.Equal(Colors.Red, converter.ConvertFrom(null, null, "rEd"));
        Assert.Throws<FormatException>(() => converter.ConvertFrom(null, null, "not a color"));
        Assert.Throws<NotSupportedException>(() => converter.ConvertFrom(null, null, 42));
    }

    [Fact]
    public void ColorConverter_ConvertFromString_and_ConvertTo()
    {
        Assert.Equal(new Color(0xFF, 0x15, 0x6E, 0x61), ColorConverter.ConvertFromString("#156E61"));
        Assert.Equal(Colors.Transparent, ColorConverter.ConvertFromString("Transparent"));
        var converter = new ColorConverter();
        Assert.Equal("#FF156E61", converter.ConvertTo(null, null, new Color(0xFF, 0x15, 0x6E, 0x61), typeof(string)));
        Assert.Throws<NotSupportedException>(() => converter.ConvertTo(null, null, new Color(1, 2, 3, 4), typeof(int)));
    }

    [Fact]
    public void Localizability_shapes_match_wpf()
    {
        Assert.Equal(4, (int)LocalizationCategory.Button);
        Assert.Equal(17, (int)LocalizationCategory.NeverLocalize);
        Assert.Equal(0, (int)Modifiability.Unmodifiable);
        Assert.Equal(2, (int)Readability.Inherit);
        Assert.True(typeof(LocalizabilityAttribute).IsSealed);
        var attribute = new LocalizabilityAttribute(LocalizationCategory.Text);
        Assert.Equal(LocalizationCategory.Text, attribute.Category);
        Assert.Equal(Modifiability.Modifiable, attribute.Modifiability);
        Assert.Equal(Readability.Readable, attribute.Readability);
        attribute.Modifiability = Modifiability.Unmodifiable;
        Assert.Equal(Modifiability.Unmodifiable, attribute.Modifiability);
    }

    [Fact]
    public void FocusManager_dp_defaults_and_scope()
    {
        var scope = new StackPanel();
        var child = new Button();
        scope.AddChild(child);
        Assert.False(FocusManager.GetIsFocusScope(scope));
        Assert.Null(FocusManager.GetFocusedElement(scope));
        Assert.Null(FocusManager.GetFocusScope(child));

        FocusManager.SetIsFocusScope(scope, true);
        Assert.True(FocusManager.GetIsFocusScope(scope));
        Assert.True((bool)scope.GetValue(FocusManager.IsFocusScopeProperty)!);
        Assert.Same(scope, FocusManager.GetFocusScope(child));
        Assert.Same(scope, FocusManager.GetFocusScope(scope));

        var other = new Button();
        Assert.Null(FocusManager.GetFocusedElement(other));
        FocusManager.SetFocusedElement(other, null);
        Assert.Null(FocusManager.GetFocusedElement(other));
    }

    [Fact]
    public void FocusManager_focused_element_flows_from_the_manager()
    {
        var router = new DesktopWasm.Core.WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window { Width = 400, Height = 300 };
        var panel = new StackPanel();
        var first = new Button { Content = "First", Width = 100, Height = 30 };
        panel.AddChild(first);
        root.Content = panel;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(400, 300));
        mil.Render(root, new WpfSize(400, 300), new WpfRect(0, 0, 400, 300));
        Assert.True(manager.TryFocus(first));
        Assert.Same(first, manager.Focused);
        Assert.Same(first, Keyboard.FocusedElement);
        Assert.Same(panel, first.Parent);

        Assert.Same(first, FocusManager.GetFocusedElement(panel));
        Assert.Null(FocusManager.GetFocusedElement(new Button()));

        var second = new Button { Content = "Second", Width = 100, Height = 30 };
        panel.AddChild(second);
        mil.Render(root, new WpfSize(400, 300), new WpfRect(0, 0, 400, 300));
        FocusManager.SetFocusedElement(panel, second);
        Assert.Same(second, manager.Focused);
        Assert.Same(second, FocusManager.GetFocusedElement(panel));
        Assert.Same(second, panel.GetValue(FocusManager.FocusedElementProperty));

        FocusManager.SetFocusedElement(panel, null);
        Assert.Same(second, FocusManager.GetFocusedElement(panel));
        Assert.True(manager.TryFocus(first));
        Assert.Same(first, FocusManager.GetFocusedElement(panel));
    }

    [Fact]
    public void FocusManager_got_focus_handlers_wire()
    {
        var router = new DesktopWasm.Core.WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window { Width = 400, Height = 300 };
        var first = new Button { Content = "First", Width = 100, Height = 30 };
        var panel = new StackPanel();
        panel.AddChild(first);
        root.Content = panel;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(400, 300));
        mil.Render(root, new WpfSize(400, 300), new WpfRect(0, 0, 400, 300));

        var arrivals = 0;
        RoutedEventHandler arrived = (_, _) => arrivals++;
        FocusManager.AddGotFocusHandler(first, arrived);
        FocusManager.AddLostFocusHandler(first, (_, _) => { });
        Assert.True(Keyboard.Focus(first) is not null);
        Assert.Equal(1, arrivals);
        FocusManager.RemoveGotFocusHandler(first, arrived);
        var other = new Button { Content = "Other", Width = 100, Height = 30 };
        panel.AddChild(other);
        mil.Render(root, new WpfSize(400, 300), new WpfRect(0, 0, 400, 300));
        Assert.True(manager.TryFocus(other));
        Assert.True(manager.TryFocus(first));
        Assert.Equal(1, arrivals);
        Assert.Throws<ArgumentException>(() => FocusManager.AddGotFocusHandler(new DependencyObject(), arrived));
    }

    [Fact]
    public void SizeChangedInfo_carries_element_sizes()
    {
        var element = new Button { Width = 120, Height = 30 };
        element.Measure(new WpfSize(500, 500));
        element.Arrange(new WpfRect(0, 0, 120, 30));
        var info = new SizeChangedInfo(element, new Size(100, 20), widthChanged: true, heightChanged: true);
        Assert.Same(element, info.Element);
        Assert.Equal(new Size(100, 20), info.PreviousSize);
        Assert.Equal(element.RenderSize, info.NewSize);
        Assert.True(info.WidthChanged);
        Assert.True(info.HeightChanged);
        info.Update(false, false);
        Assert.False(info.WidthChanged);
        Assert.False(info.HeightChanged);
        Assert.Throws<ArgumentNullException>(() => new SizeChangedInfo(null!, new Size(0, 0), false, false));
    }

    [Fact]
    public void StaticResourceExtension_shape_and_provide()
    {
        var previous = Application.Current;
        try
        {
            var app = new Application();
            app.Resources.Add("Accent", "red");
            var plain = new StaticResourceExtension();
            Assert.Null(plain.ResourceKey);
            Assert.Throws<InvalidOperationException>(() => plain.ProvideValue(null!));
            var keyed = new StaticResourceExtension("Accent");
            Assert.Equal("Accent", keyed.ResourceKey);
            Assert.Equal("red", keyed.ProvideValue(null!));
            keyed.ResourceKey = "Missing";
            Assert.Throws<InvalidOperationException>(() => keyed.ProvideValue(null!));

            var swatch = new Button();
            var provider = new StaticTargetProvider(new StaticTarget(swatch, "Tag"));
            Assert.Equal("red", new StaticResourceExtension("Accent").ProvideValue(provider));
            swatch.Resources.Add("Local", "blue");
            Assert.Equal("blue", new StaticResourceExtension("Local").ProvideValue(provider));
        }
        finally
        {
            Application.Current = previous;
        }
    }

    private sealed class StaticTarget(object targetObject, object targetProperty) : IProvideValueTarget
    {
        public object TargetObject => targetObject;
        public object TargetProperty => targetProperty;
    }

    private sealed class StaticTargetProvider(IProvideValueTarget target) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IProvideValueTarget) ? target : null;
    }

    [Fact]
    public void XamlLoader_parses_new_elements_and_scope_flag()
    {
        var box = (DatePickerTextBox)((Window)XamlLoader.Load(
            """<Window><DatePickerTextBox Text="hi"/></Window>""").Root is Window window
            ? window.Content : throw new InvalidOperationException("No window content."));
        Assert.Equal("hi", box.Text);

        var placeholder = (AdornedElementPlaceholder)((Window)XamlLoader.Load(
            """<Window><AdornedElementPlaceholder/></Window>""").Root is Window holder
            ? holder.Content : throw new InvalidOperationException("No window content."));
        Assert.Null(placeholder.AdornedElement);

        var scoped = (Button)((Window)XamlLoader.Load(
            """<Window><Button FocusManager.IsFocusScope="True"/></Window>""").Root is Window scopedWindow
            ? scopedWindow.Content : throw new InvalidOperationException("No window content."));
        Assert.True(FocusManager.GetIsFocusScope(scoped));
        Assert.True((bool)scoped.GetValue(FocusManager.IsFocusScopeProperty)!);
    }

    private sealed class ProbeSource
    {
        public event EventHandler<ProbeArgs>? Pinged;

        public void Ping() => Pinged?.Invoke(this, new ProbeArgs());
    }

    private sealed class ProbeArgs : EventArgs
    {
    }

    private sealed class ProbeArgs2 : EventArgs
    {
    }

    [Fact]
    public void WeakEventManager_shape_matches_wpf()
    {
        var managerType = typeof(WeakEventManager<ProbeSource, ProbeArgs>);
        Assert.Same(typeof(WeakEventManager), managerType.BaseType);
        Assert.False(managerType.IsSealed);
        Assert.Equal(GenericParameterAttributes.None,
            typeof(WeakEventManager<,>).GetGenericArguments()[0].GenericParameterAttributes);
        Assert.Equal(GenericParameterAttributes.None,
            typeof(WeakEventManager<,>).GetGenericArguments()[1].GenericParameterAttributes);
        Assert.NotNull(managerType.GetMethod("AddHandler",
            [typeof(ProbeSource), typeof(string), typeof(EventHandler<ProbeArgs>)]));
        Assert.NotNull(managerType.GetMethod("RemoveHandler",
            [typeof(ProbeSource), typeof(string), typeof(EventHandler<ProbeArgs>)]));
        Assert.NotNull(managerType.GetMethod("CurrentManager",
            BindingFlags.NonPublic | BindingFlags.Static, [typeof(string)]));
        Assert.NotNull(managerType.GetMethod("StartListening",
            BindingFlags.NonPublic | BindingFlags.Instance, [typeof(object)]));
        Assert.NotNull(managerType.GetMethod("StopListening",
            BindingFlags.NonPublic | BindingFlags.Instance, [typeof(object)]));
        Assert.NotNull(managerType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, [typeof(string)]));
    }

    [Fact]
    public void WeakEventManager_delivers_and_drops_collectors()
    {
        var source = new ProbeSource();
        var seen = new List<ProbeArgs>();
        void Live(object? sender, ProbeArgs args) => seen.Add(args);
        WeakEventManager<ProbeSource, ProbeArgs>.AddHandler(source, "Pinged", Live);
        source.Ping();
        Assert.Single(seen);

        var weakSeen = 0;
        var tracker = TrackCollector(source, () => weakSeen++);
        source.Ping();
        Assert.Equal(1, weakSeen);
        for (var i = 0; i < 3 && tracker.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.False(tracker.IsAlive);
        source.Ping();
        Assert.Equal(1, weakSeen);
        Assert.Equal(3, seen.Count);

        WeakEventManager<ProbeSource, ProbeArgs>.RemoveHandler(source, "Pinged", Live);
        source.Ping();
        Assert.Equal(3, seen.Count);

        // The manager registry is keyed by closed type (first event name wins,
        // like WPF), so a missing event fails on a fresh closed type whose
        // manager is constructed for that name.
        var badSource = new ProbeSource();
        Assert.Throws<InvalidOperationException>(() =>
            WeakEventManager<ProbeSource, ProbeArgs2>.AddHandler(badSource, "NoSuchEvent", (_, _) => { }));
    }

    private sealed class Collector(Action onPing)
    {
        public void OnPing(object? sender, ProbeArgs args) => onPing();
    }

    private static WeakReference TrackCollector(ProbeSource source, Action onPing)
    {
        var collector = new Collector(onPing);
        WeakEventManager<ProbeSource, ProbeArgs>.AddHandler(source, "Pinged", collector.OnPing);
        return new WeakReference(collector, trackResurrection: false);
    }

    [Fact]
    public void Annotation_defaults_and_events()
    {
        var before = DateTime.UtcNow;
        var annotation = new Annotation();
        Assert.Null(annotation.AnnotationType);
        Assert.NotEqual(Guid.Empty, annotation.Id);
        Assert.True(annotation.CreationTime >= before);
        Assert.Empty(annotation.Anchors);

        var changed = new List<(AnnotationAction Action, string Name)>();
        annotation.AnchorChanged += (_, e) =>
        {
            Assert.Same(annotation, e.Annotation);
            changed.Add((e.Action, e.Resource.Name));
        };
        var resource = new AnnotationResource("anchor-1");
        Assert.NotEqual(Guid.Empty, resource.Id);
        var stamped = annotation.LastModificationTime;
        annotation.Anchors.Add(resource);
        Assert.Single(changed);
        Assert.Equal((AnnotationAction.Added, "anchor-1"), changed[0]);
        Assert.True(annotation.LastModificationTime >= stamped);
        annotation.Anchors.Remove(resource);
        Assert.Equal(2, changed.Count);
        Assert.Equal((AnnotationAction.Removed, "anchor-1"), changed[1]);

        var authors = new List<(AnnotationAction Action, object Author)>();
        annotation.AuthorChanged += (_, e) => authors.Add((e.Action, e.Author));
        annotation.Authors.Add("Ada");
        Assert.Single(authors);
        Assert.Equal((AnnotationAction.Added, (object)"Ada"), authors[0]);

        var cargos = 0;
        annotation.CargoChanged += (_, e) =>
        {
            Assert.Equal(AnnotationAction.Added, e.Action);
            cargos++;
        };
        annotation.Cargos.Add(new AnnotationResource());
        Assert.Equal(1, cargos);

        var named = new Annotation(new XmlQualifiedName("note", "urn:demo"), Guid.NewGuid(), before, before);
        Assert.Equal("note", named.AnnotationType!.Name);
        Assert.Equal(2, (int)AnnotationAction.Modified);
    }

    [Fact]
    public void NavigationService_journals_content()
    {
        var root = new Window { Width = 400, Height = 300 };
        var frame = new Frame();
        root.Content = frame;
        var service = frame.NavigationService;
        Assert.Same(service, frame.NavigationService);
        Assert.Null(service.Content);
        Assert.False(service.CanGoBack);
        Assert.False(service.CanGoForward);

        var first = new TextBlock { Text = "First" };
        var second = new TextBlock { Text = "Second" };
        var navigated = 0;
        frame.Navigated += (_, _) => navigated++;
        Assert.True(service.Navigate(first));
        Assert.Same(first, service.Content);
        Assert.Same(first, frame.Content);
        Assert.Equal(1, navigated);
        Assert.True(service.Navigate(second));
        Assert.True(service.CanGoBack);
        Assert.False(service.CanGoForward);

        service.GoBack();
        Assert.Same(first, service.Content);
        Assert.Same(first, frame.Content);
        Assert.True(service.CanGoForward);

        var third = new TextBlock { Text = "Third" };
        Assert.True(service.Navigate(third));
        Assert.False(service.CanGoForward);
        Assert.Throws<InvalidOperationException>(() => service.GoForward());
        service.GoBack();
        Assert.Same(first, service.Content);
        Assert.Throws<InvalidOperationException>(() => service.GoBack());

        Assert.True(service.Navigate("plain data"));
        Assert.Equal("plain data", service.Content);
        Assert.Same(first, frame.Content);
        Assert.Throws<InvalidOperationException>(() => service.Navigate(new Uri("pack://application:,,,/Page.xaml")));
    }

    [Fact]
    public void NavigationService_resolves_through_the_host_frame()
    {
        var root = new Window { Width = 400, Height = 300 };
        var frame = new Frame();
        var panel = new StackPanel();
        panel.AddChild(frame);
        root.Content = panel;
        var inner = new TextBlock { Text = "Inner" };
        frame.Navigate(inner);
        Assert.Same(frame.NavigationService, NavigationService.GetNavigationService(inner));
        Assert.Null(NavigationService.GetNavigationService(new Button()));
    }

    [Fact]
    public void TimelineCollection_nested_enumerator_matches_wpf()
    {
        var nested = typeof(TimelineCollection).GetNestedType("Enumerator");
        Assert.NotNull(nested);
        Assert.True(nested!.IsValueType);
        var timelines = new TimelineCollection
        {
            new DoubleAnimation(),
            new DoubleAnimation(),
        };
        var seen = new List<Timeline>();
        foreach (var timeline in timelines)
            seen.Add(timeline);
        Assert.Equal(2, seen.Count);

        using var enumerator = timelines.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        Assert.Same(timelines[0], enumerator.Current);
        Assert.True(enumerator.MoveNext());
        enumerator.Reset();
        Assert.True(enumerator.MoveNext());
        Assert.Same(timelines[0], enumerator.Current);
        Assert.Equal(timelines[0], ((System.Collections.IEnumerator)enumerator).Current);
    }

    [Fact]
    public void TransformCollection_nested_enumerator_matches_wpf()
    {
        var nested = typeof(TransformCollection).GetNestedType("Enumerator");
        Assert.NotNull(nested);
        Assert.True(nested!.IsValueType);
        var transforms = new TransformCollection
        {
            new TranslateTransform(1, 2),
            new ScaleTransform(3, 4),
        };
        var seen = 0;
        foreach (var _ in transforms)
            seen++;
        Assert.Equal(2, seen);

        using var enumerator = transforms.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        Assert.Same(transforms[0], enumerator.Current);
        Assert.False(new TransformCollection().GetEnumerator().MoveNext());
    }
}
