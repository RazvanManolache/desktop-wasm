using System.ComponentModel;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-77 HandyControl entry boot oracle: the converted entry
/// stages with zero gaps, so the engine-native boot runs the real
/// HandyControlDemo.App.Main. The generated version-qualified self-reference
/// (/HandyControlDemo;V3.6.0.0;component/app.xaml) now resolves by short name
/// like real WPF (PackUri qualifier strip), the VS-generated d1P1:Ignorable
/// root attribute is skipped by compatibility-namespace URI, and boot hydrates
/// App.xaml up to the first lib skin merge
/// (pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml),
/// which stays the precise next blocker: the HandyControl lib is unstaged
/// (conversion-failed, absent binary) so its theme XAML cannot resolve, and
/// the hc:EnumDataProvider entries behind it need the same absent lib. The splash
/// path is proven without App.xaml: the real Resources/Img/Cover.png
/// resolves out of the staged entry's .g.resources and decodes to
/// 1320x680, and the exact entry call shape (new SplashScreen(asm, path)
/// + Show(true)) lands visible with that image. Member evidence via
/// Mono.Cecil on the upstream net10.0-windows HandyControlDemo.dll
/// (App.OpenSplashScreen .ctor(string)/Show(bool),
/// ChatBoxViewModel.ReadMessage SoundPlayer..ctor(string)/PlaySync, plus
/// App.g.cs InitializeComponent call shapes).</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class HandyControlEntryBootTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public HandyControlEntryBootTests()
    {
        WpfAnimationClock.ResetForTests();
        SplashScreen.ResetForTests();
    }

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
        WpfAnimationClock.ResetForTests();
        SplashScreen.ResetForTests();
    }

    [Fact]
    public void Staged_entry_splash_resource_decodes_to_cover()
    {
        var entry = LoadStagedHandyControlEntry();
        var bytes = WpfAppBoot.FindPackBytes("Resources/Img/Cover.png", "HandyControlDemo");
        Assert.NotNull(bytes);
        var image = new BitmapImage();
        image.StreamSource = new MemoryStream(bytes!);
        image.EndInit();
        Assert.Equal(1320, image.PixelWidth);
        Assert.Equal(680, image.PixelHeight);
    }

    [Fact]
    public void Staged_entry_splash_call_shape_shows_cover()
    {
        var entry = LoadStagedHandyControlEntry();
        var splash = new SplashScreen(entry, "Resources/Img/Cover.png");
        splash.Show(true);
        try
        {
            Assert.True(splash.IsVisible);
            Assert.True(splash.AutoClose);
            Assert.NotNull(splash.Image);
            Assert.Equal(1320, splash.Image!.PixelWidth);
            Assert.Equal(680, splash.Image.PixelHeight);
        }
        finally
        {
            splash.Close(TimeSpan.Zero);
        }
        Assert.False(splash.IsVisible);
    }

    [Fact]
    public void Staged_lib_interactivity_collections_enumerate_through_engine_nested_enumerator()
    {
        // Slice-83: the HandyControl lib itself converts and stages, and its
        // Interactivity foreach machinery binds at JIT time: each converted
        // Behavior/Trigger collection GetEnumerator returns the engine nested
        // FreezableCollection<T>/Enumerator instantiation (empty MoveNext is
        // false, Current throws before the start, like WPF).
        LoadStagedHandyControlEntry();
        var lib = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.Ordinal));
        foreach (var name in new[]
        {
            "HandyControl.Interactivity.BehaviorCollection",
            "HandyControl.Interactivity.TriggerCollection",
            "HandyControl.Interactivity.TriggerActionCollection",
        })
        {
            var type = lib.GetType(name, throwOnError: true)!;
            var collection = Activator.CreateInstance(type, nonPublic: true)!;
            var getEnumerator = type.GetMethod("GetEnumerator", Type.EmptyTypes)!;
            Assert.True(getEnumerator.ReturnType.IsNested);
            Assert.StartsWith("DesktopWasm.Wpf.FreezableCollection`1+Enumerator[", getEnumerator.ReturnType.FullName, StringComparison.Ordinal);
            var enumerator = getEnumerator.Invoke(collection, null)!;
            var moveNext = getEnumerator.ReturnType.GetMethod("MoveNext", Type.EmptyTypes)!;
            Assert.False((bool)moveNext.Invoke(enumerator, null)!);
            var getCurrent = getEnumerator.ReturnType.GetMethod("get_Current", Type.EmptyTypes)!;
            var error = Assert.Throws<TargetInvocationException>(() => getCurrent.Invoke(enumerator, null));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }

    [Fact]
    public void Staged_lib_controls_carry_the_converted_feedback_attach_surface()
    {
        // Slice-85: the staged HandyControl lib is byte-identical, and every
        // converted UIElement now exposes the GiveFeedback/QueryContinueDrag
        // attach points the converted AvalonEdit SelectionMouseHandler
        // pattern needs. A converted control attaches through the exact
        // converted add_ shapes, fires through the engine raisers, and goes
        // silent after remove_ (the AvalonEdit attach/detach loop).
        LoadStagedHandyControlEntry();
        var lib = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.Ordinal));
        // Slice-94: the engine Border is a public Decorator like live WPF, so
        // the converted Border child RunningBorder loads; the oracle pins the
        // fixed boundary through it (see Slice94BorderTests for the full
        // 10-type type-load plus list-backed Child coverage).
        var type = lib.GetType("HandyControl.Controls.RunningBorder", throwOnError: true)!;
        Assert.True(typeof(UIElement).IsAssignableFrom(type));
        var control = (UIElement)Activator.CreateInstance(type)!;
        int feedbacks = 0;
        int queries = 0;
        var addFeedback = typeof(UIElement).GetMethod("add_GiveFeedback", [typeof(GiveFeedbackEventHandler)])!;
        var removeFeedback = typeof(UIElement).GetMethod("remove_GiveFeedback", [typeof(GiveFeedbackEventHandler)])!;
        var addQuery = typeof(UIElement).GetMethod("add_QueryContinueDrag", [typeof(QueryContinueDragEventHandler)])!;
        var removeQuery = typeof(UIElement).GetMethod("remove_QueryContinueDrag", [typeof(QueryContinueDragEventHandler)])!;
        GiveFeedbackEventHandler onFeedback = (sender, e) =>
        {
            feedbacks++;
            Assert.Same(control, sender);
            e.UseDefaultCursors = false;
        };
        QueryContinueDragEventHandler onQuery = (sender, e) =>
        {
            queries++;
            Assert.Same(control, sender);
            e.Action = e.EscapePressed ? DragAction.Cancel : DragAction.Continue;
        };
        addFeedback.Invoke(control, [onFeedback]);
        addQuery.Invoke(control, [onQuery]);
        var feedbackArgs = (GiveFeedbackEventArgs)typeof(GiveFeedbackEventArgs)
            .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
                [typeof(DragDropEffects), typeof(bool)])!
            .Invoke([DragDropEffects.Copy, true]);
        typeof(UIElement).GetMethod("OnGiveFeedback", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(control, [feedbackArgs]);
        var queryArgs = (QueryContinueDragEventArgs)typeof(QueryContinueDragEventArgs)
            .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
                [typeof(bool), typeof(DragDropKeyStates)])!
            .Invoke([true, DragDropKeyStates.None]);
        typeof(UIElement).GetMethod("OnQueryContinueDrag", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(control, [queryArgs]);
        Assert.Equal(1, feedbacks);
        Assert.False(feedbackArgs.UseDefaultCursors);
        Assert.Equal(1, queries);
        Assert.Equal(DragAction.Cancel, queryArgs.Action);
        removeFeedback.Invoke(control, [onFeedback]);
        removeQuery.Invoke(control, [onQuery]);
        typeof(UIElement).GetMethod("OnGiveFeedback", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(control, [feedbackArgs]);
        typeof(UIElement).GetMethod("OnQueryContinueDrag", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(control, [queryArgs]);
        Assert.Equal(1, feedbacks);
        Assert.Equal(1, queries);
    }

    [Fact]
    public void Staged_lib_observable_routes_through_engine_property_changed_manager()
    {
        // Slice-91: the converted GalaSoft.MvvmLight.ObservableObject carries
        // the exact 3-arg AddListener/RemoveListener call shape the converted
        // AvalonEdit TextEditor/LineNumberMargin document hooks need
        // (INotifyPropertyChanged source, IWeakEventListener, property name).
        // Raising through the converted RaisePropertyChanged delivers weakly
        // (property-filtered, removed stops); the handler path works too.
        LoadStagedHandyControlEntry();
        var lib = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "GalaSoft.MvvmLight", StringComparison.Ordinal));
        var type = lib.GetType("GalaSoft.MvvmLight.ObservableObject", throwOnError: true)!;
        Assert.True(typeof(INotifyPropertyChanged).IsAssignableFrom(type));
        var source = (INotifyPropertyChanged)Activator.CreateInstance(type)!;
        var raise = type.GetMethod("RaisePropertyChanged", [typeof(string)])!;
        var listener = new OracleListener();
        PropertyChangedEventManager.AddListener(source, listener, "P");
        raise.Invoke(source, ["P"]);
        Assert.Equal(1, listener.Count);
        Assert.Same(source, listener.LastSender);
        Assert.Equal(typeof(PropertyChangedEventManager), listener.LastManager);
        raise.Invoke(source, ["Q"]);
        Assert.Equal(1, listener.Count);
        int handled = 0;
        EventHandler<PropertyChangedEventArgs> onChanged = (_, _) => handled++;
        PropertyChangedEventManager.AddHandler(source, onChanged, "P");
        raise.Invoke(source, ["P"]);
        Assert.Equal(2, listener.Count);
        Assert.Equal(1, handled);
        PropertyChangedEventManager.RemoveListener(source, listener, "P");
        PropertyChangedEventManager.RemoveHandler(source, onChanged, "P");
        raise.Invoke(source, ["P"]);
        Assert.Equal(2, listener.Count);
        Assert.Equal(1, handled);
    }

    private sealed class OracleListener : IWeakEventListener
    {
        public int Count;
        public object? LastSender;
        public Type? LastManager;
        public bool ReceiveWeakEvent(Type managerType, object? sender, EventArgs e)
        {
            Count++;
            LastSender = sender;
            LastManager = managerType;
            return true;
        }
    }

    [Fact]
    public void Staged_entry_boot_advances_past_lib_skin_to_lib_theme()
    {
        var entry = LoadStagedHandyControlEntry();
        var error = Assert.Throws<InvalidOperationException>(() => WpfAppBoot.Boot(entry));
        Assert.Contains("startup failed in entry point", error.Message);
        // Slice-122 re-pin: past the self-reference (no version-qualified
        // self-ref anywhere in the chain) and past the line-17 lib skin
        // merge, which now resolves through compiled BAML; the inner failure
        // is the App.xaml hydration reaching the line-23 lib theme merge,
        // whose key/index + scalar run + PathBaseStyle style entry +
        // x:Static-keyed Double + converted Behavior entries + implicit
        // BlendEffectBox style + 'BorderCircular' entry + implicit
        // Col/ContentControl/FlipClock/FloatingBlock/GridSplitter styles
        // resolve too - the stop is the implicit Notification style entry
        // past the implicit SimpleItemsControl/Sprite styles, the nine transition
        // Storyboards (keyidx 113-121) and the implicit TransitioningContentControl
        // style through the keyed RepeatButton styles (keyidx 123-126) through the keyed 'ToggleBlockBaseStyle' style (keyidx 127) through the keyed 'GroupBoxBaseTopTemplate' template (keyidx 128) through the keyed 'GroupBoxBaseLeftTemplate' template (keyidx 129) through the keyed 'GroupBoxTabTopTemplate'/'GroupBoxTabLeftTemplate' templates (keyidx 130-131) through the four keyed ButtonGroup ItemsPanelTemplates (keyidx 132-135) and the converted 'ButtonGroupItemStyleSelector' entry (keyidx 136) plus the keyed 'ProgressBarIndicatorAnimatedFill' brush (keyidx 137) through the keyed 'CircleProgressBarTemplate' template (keyidx 138) and the four keyed Uniform/Stack ItemsPanelTemplates (keyidx 139-142) and the two VirtualizingStackPanel ItemsPanelTemplates (keyidx 143-144), the two WrapPanel plus DockPanel ItemsPanelTemplates (keyidx 145-147), the 'ResizeGripperForeground' brush (keyidx 148) and the 'SineEaseOut' easing (keyidx 149) through the 'BadgeDotTemplate' entry at BAML+0x1913D and the 'ColorPickerSliderRepeatButtonBaseStyle' Style (keyidx 151) through the 'ColorPickerSliderThumbTemplate' entry at BAML+0x19279 to the 'ColorPickerRainbowBrush' entry at BAML+0x192B2 (first GradientStop at BAML+0x192B9 has no Offset).
        Assert.DoesNotContain("HandyControlDemo;V3.6.0.0", error.Message + error.InnerException?.Message);
        var inner = Assert.IsType<XamlLoadException>(error.InnerException);
        Assert.DoesNotContain("SkinDefault", inner.Message);
        Assert.DoesNotContain("PathBaseStyle", inner.Message);
        Assert.DoesNotContain("BAML+0x166FF", inner.Message);
        Assert.DoesNotContain("BAML+0x167B8", inner.Message);
        Assert.DoesNotContain("x:Static", inner.Message);
        Assert.DoesNotContain("VerticalScrollBarButtonHeightKey", inner.Message);
        Assert.DoesNotContain("BAML+0x167C8", inner.Message);
        Assert.DoesNotContain("BehaviorXY200", inner.Message);
        Assert.DoesNotContain("FluidMoveBehavior", inner.Message);
        Assert.DoesNotContain("BAML+0x168D2", inner.Message);
        Assert.DoesNotContain("BlendEffectBox", inner.Message);
        Assert.DoesNotContain("BAML+0x16926", inner.Message);
        Assert.DoesNotContain("BorderCircular", inner.Message);
        Assert.Contains("HandyControl;component/Themes/Theme.xaml", inner.Message);
        Assert.Contains("line 23", inner.Message);
        Assert.DoesNotContain("BAML+0x16954", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.Col", inner.Message);
        Assert.DoesNotContain("well-known member id -132", inner.Message);
        Assert.DoesNotContain("BAML+0x16C96", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.Notification", inner.Message);
        Assert.DoesNotContain("setter property owner Window", inner.Message);
        Assert.DoesNotContain("BAML+0x16E7C", inner.Message);
        Assert.DoesNotContain("RectangleCircular", inner.Message);
        Assert.DoesNotContain("SnapRect4ScreenshotStyle", inner.Message);
        Assert.DoesNotContain("TargetArea4ScreenshotStyle", inner.Message);
        Assert.DoesNotContain("holds a Trigger list", inner.Message);
        Assert.DoesNotContain("BAML+0x1760B", inner.Message);
        Assert.DoesNotContain("BAML+0x17C06", inner.Message);
        Assert.DoesNotContain("Right2LeftTransition", inner.Message);
        Assert.DoesNotContain("SliderRepeatButtonHorizontalStyle", inner.Message);
        Assert.DoesNotContain("SliderRepeatButtonVerticalStyle", inner.Message);
        Assert.DoesNotContain("CompareSliderRepeatButtonTransparent", inner.Message);
        Assert.DoesNotContain("ScrollBarBaseRepeatButton", inner.Message);
        Assert.DoesNotContain("BAML+0x17F4C", inner.Message);
        Assert.DoesNotContain("ToggleBlockBaseStyle", inner.Message);
        Assert.DoesNotContain("well-known member id -38", inner.Message);
        Assert.DoesNotContain("BAML+0x1835D", inner.Message);
        Assert.DoesNotContain("GroupBoxBaseTopTemplate", inner.Message);
        Assert.DoesNotContain("BAML+0x1859F", inner.Message);
        Assert.DoesNotContain("GroupBoxBaseLeftTemplate", inner.Message);
        Assert.DoesNotContain("holds ControlTemplate", inner.Message);
        Assert.DoesNotContain("BAML+0x1888C", inner.Message);
        Assert.DoesNotContain("GroupBoxTabTopTemplate", inner.Message);
        Assert.DoesNotContain("GroupBoxTabLeftTemplate", inner.Message);
        Assert.DoesNotContain("template Binding holds record 0x38", inner.Message);
        Assert.DoesNotContain("BAML+0x18D3F", inner.Message);
        Assert.DoesNotContain("ButtonGroupHorizontalUniformItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("holds well-known type id -330", inner.Message);
        Assert.DoesNotContain("BAML+0x18DBD", inner.Message);
        Assert.DoesNotContain("ProgressBarIndicatorAnimatedFill", inner.Message);
        Assert.DoesNotContain("holds well-known type id -358", inner.Message);
        Assert.DoesNotContain("BAML+0x18E5D", inner.Message);
        Assert.DoesNotContain("BAML+0x19029", inner.Message);
        Assert.DoesNotContain("CircleProgressBarTemplate", inner.Message);
        Assert.DoesNotContain("VirtualizingStackHorizontalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("WrapHorizontalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("WrapVerticalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("DockItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("ResizeGripperForeground", inner.Message);
        Assert.DoesNotContain("SineEaseOut", inner.Message);
        Assert.DoesNotContain("BAML+0x19150", inner.Message);
        Assert.DoesNotContain("BadgeDotTemplate", inner.Message);
        Assert.DoesNotContain("element ContentPresenter on the template root panel", inner.Message);
        Assert.DoesNotContain("root panels take ContentPresenter, Border, converted Shape and TextBlock children", inner.Message);
        Assert.DoesNotContain("BAML+0x1927D", inner.Message);
        Assert.DoesNotContain("ColorPickerSliderThumbTemplate", inner.Message);
        Assert.DoesNotContain("BAML+0x192B9", inner.Message);
        Assert.DoesNotContain("ColorPickerRainbowBrush", inner.Message);
        Assert.DoesNotContain("has no Offset", inner.Message);
        Assert.DoesNotContain("BAML+0x19383", inner.Message);
        Assert.DoesNotContain("ColorPickerOpacityBrush", inner.Message);
        Assert.DoesNotContain("well-known type id -180", inner.Message);
        Assert.DoesNotContain("BAML+0x19497", inner.Message);
        Assert.DoesNotContain("CoverFlowBaseStyle", inner.Message);
        Assert.DoesNotContain("well-known type id -729", inner.Message);
        Assert.DoesNotContain("BAML+0x195FD", inner.Message);
        Assert.DoesNotContain("GridViewColumnHeaderGripper", inner.Message);
        Assert.DoesNotContain("well-known type id -66", inner.Message);
        Assert.DoesNotContain("Canvas.Right", inner.Message);
        Assert.DoesNotContain("BAML+0x196C6", inner.Message);
        Assert.DoesNotContain("'Padding' is not a dependency property of Border", inner.Message);
        Assert.DoesNotContain("SortByCategoryDrawingBrush", inner.Message);
        Assert.DoesNotContain("BAML+0x196F6", inner.Message);
        Assert.DoesNotContain("SortByNameDrawingBrush", inner.Message);
        Assert.DoesNotContain("TabItemMenu", inner.Message);
        Assert.DoesNotContain("BAML+0x19D99", inner.Message);
        Assert.DoesNotContain("BAML+0x19DCE", inner.Message);
        Assert.DoesNotContain("well-known type id -105", inner.Message);
        Assert.DoesNotContain("Lang.Close", inner.Message);
        Assert.DoesNotContain("throws reading the static property", inner.Message);
        Assert.DoesNotContain("XmlnsDefinitionAttribute", inner.Message);
        Assert.DoesNotContain("ToolBarTrayBaseStyle", inner.Message);
        Assert.DoesNotContain("BAML+0x19E4F", inner.Message);
        Assert.DoesNotContain("well-known type id -672", inner.Message);
        Assert.DoesNotContain("TreeArrow", inner.Message);
        Assert.DoesNotContain("BAML+0x19E7B", inner.Message);
        Assert.DoesNotContain("well-known type id -446", inner.Message);
        Assert.DoesNotContain("RowHeaderGripperStyle", inner.Message);
        Assert.DoesNotContain("BAML+0x1A034", inner.Message);
        Assert.DoesNotContain("'Cursor' is not a dependency property of FrameworkElement", inner.Message);
        Assert.DoesNotContain("DataGridComboBoxColumnStyle", inner.Message);
        Assert.DoesNotContain("DataGridTextColumnStyle", inner.Message);        
        Assert.DoesNotContain("CarouselItemBaseStyle", inner.Message);
        Assert.DoesNotContain("BAML+0x1A113", inner.Message);
        Assert.DoesNotContain("BAML+0x1A186", inner.Message);        
        Assert.DoesNotContain("BAML+0x1A21B", inner.Message);
        Assert.DoesNotContain("well-known type id -92", inner.Message);
        Assert.DoesNotContain("targets TextBlock", inner.Message);        
        Assert.DoesNotContain("template property id -21", inner.Message);
        Assert.DoesNotContain("GrowlWindow", inner.Message);
        Assert.DoesNotContain("BAML+0x1A2CA", inner.Message);
        Assert.DoesNotContain("well-known member id -51", inner.Message);
        Assert.DoesNotContain("ListBoxItemCustom", inner.Message);
        Assert.DoesNotContain("BAML+0x1A368", inner.Message);
        Assert.DoesNotContain("well-known type id -374", inner.Message);
        Assert.DoesNotContain("BAML+0x1A92D", inner.Message);
        Assert.DoesNotContain("well-known member id -81", inner.Message);
        Assert.DoesNotContain("RibbonTab", inner.Message);
        Assert.DoesNotContain("BAML+0x1A990", inner.Message);
        Assert.DoesNotContain("template root panel property 'Visibility' with converter -195", inner.Message);
        Assert.DoesNotContain("BAML+0x1A9DD", inner.Message);
        Assert.DoesNotContain("element well-known type id -550 on the template root panel", inner.Message);
        Assert.DoesNotContain("BAML+0x1AE91", inner.Message);
        Assert.DoesNotContain("'EffectShadow1'", inner.Message);
        Assert.DoesNotContain("record 0x34 (converted-element values", inner.Message);
        Assert.DoesNotContain("BAML+0x1B001", inner.Message);
        Assert.DoesNotContain("BAML+0x1B22E", inner.Message);
        Assert.DoesNotContain("'SearchPathStyle'", inner.Message);
        Assert.DoesNotContain("'RightPathStyle'", inner.Message);
        Assert.DoesNotContain("record 0x38 in a Style", inner.Message);
        Assert.DoesNotContain("'RenderTransformOrigin' is not a dependency property of UIElement", inner.Message);
        Assert.DoesNotContain("BAML+0x1B260", inner.Message);
        Assert.DoesNotContain("well-known member id -134", inner.Message);
        Assert.Contains("'BorderRegion'", inner.Message);
        Assert.Contains("BAML+0x1B2F9", inner.Message);
        Assert.Contains("setter property owner Border in style entry 'BorderRegion'", inner.Message);
        Assert.DoesNotContain("BAML+0x1AA35", inner.Message);
        Assert.DoesNotContain("'PrimaryBrush'", inner.Message);
        Assert.DoesNotContain("record 0x34 in a LinearGradientBrush", inner.Message);
        Assert.DoesNotContain("BAML+0x1AA1C", inner.Message);
        Assert.DoesNotContain("LightPrimaryBrush", inner.Message);
        Assert.DoesNotContain("well-known type id -578", inner.Message);
        Assert.DoesNotContain("setter 'Template'", inner.Message);
        Assert.DoesNotContain("well-known type id -658", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.SimplePanel", inner.Message);
        Assert.DoesNotContain("VirtualizingStackPanel", inner.Message);
        Assert.DoesNotContain("templates take a ContentPresenter", inner.Message);
        Assert.DoesNotContain("items panels take a UniformGrid/StackPanel/VirtualizingStackPanel/WrapPanel/DockPanel visual", inner.Message);

        Assert.DoesNotContain("well-known type id -608", inner.Message);
        Assert.DoesNotContain("well-known type id -522", inner.Message);
        Assert.DoesNotContain("BAML+0x1744C", inner.Message);
        Assert.DoesNotContain("element Border on the template", inner.Message);
        Assert.DoesNotContain("SimpleItemsControl", inner.Message);
        Assert.DoesNotContain("BAML+0x17483", inner.Message);
        Assert.DoesNotContain("Sprite", inner.Message);
        Assert.DoesNotContain("IsDragElement", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.SimpleItemsControl", inner.Message);
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
